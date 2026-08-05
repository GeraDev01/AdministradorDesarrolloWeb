using System.IO.Compression;
using System.Security.Cryptography;
using FluentFTP;
using FluentFTP.Helpers;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Security;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Avance en vivo de un despliegue: porcentaje global, servidor actual y qué está haciendo
/// (respaldando, subiendo tal archivo, etc.), para la barra y el renglón de estado de la pantalla.</summary>
public readonly record struct DeployStatus(int Percent, string Server, string Detail);

public class DeploymentService
{
    private readonly AppDbContext _db;
    private readonly BlobStorageService _blob;
    private readonly AuditService _audit;
    private readonly CurrentUserContext _currentUser;
    private readonly RemoteBackupService _remoteBackup;
    /// <summary>Para abrir un contexto propio por despliegue; el inyectado es singleton y lo comparte la UI.</summary>
    private readonly DbContextOptions<AppDbContext> _dbOptions;

    private static readonly string ReleasesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AdministradorDesarrolloWeb", "releases");

    public DeploymentService(AppDbContext db, BlobStorageService blob,
        AuditService audit, CurrentUserContext currentUser,
        RemoteBackupService remoteBackup, DbContextOptions<AppDbContext> dbOptions)
    {
        _db = db; _blob = blob; _audit = audit; _currentUser = currentUser;
        _remoteBackup = remoteBackup; _dbOptions = dbOptions;
        Directory.CreateDirectory(ReleasesPath);
    }

    // ── Release creation ─────────────────────────────────────────
    public async Task<AppRelease> CreateReleaseAsync(
        int appSystemId, string version, string? changelog, string sourceFolder, string? carpetaDestino,
        IProgress<string> progress, CancellationToken ct = default)
    {
        var system = await _db.AppSystems.FindAsync(new object[] { appSystemId }, ct)
            ?? throw new ArgumentException("Sistema no encontrado.");

        if (!Directory.Exists(sourceFolder))
            throw new DirectoryNotFoundException($"La carpeta no existe: {sourceFolder}");

        progress.Report($"📦  Comprimiendo carpeta: {sourceFolder}");
        var safeName = string.Join("_", system.Name.Split(Path.GetInvalidFileNameChars()));
        var safeVer  = string.Join("_", version.Split(Path.GetInvalidFileNameChars()));
        var zipName  = $"{safeName}_{safeVer}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
        var zipPath  = Path.Combine(ReleasesPath, zipName);

        await Task.Run(() => ZipFile.CreateFromDirectory(sourceFolder, zipPath, CompressionLevel.Optimal, false), ct);

        var sizeBytes = new FileInfo(zipPath).Length;
        progress.Report($"  ✓  ZIP generado: {zipName} ({sizeBytes / 1024.0 / 1024.0:F1} MB)");

        progress.Report("🔒  Calculando checksum SHA-256...");
        var checksum = await Task.Run(() => ComputeSha256(zipPath), ct);
        progress.Report($"  ✓  SHA-256: {checksum[..16]}...");

        string? blobUrl = null;
        if (_blob.IsConfigured)
        {
            // Metadatos en el propio blob: así el artefacto sigue siendo identificable desde el
            // portal de Azure aunque se consulte fuera de la aplicación.
            var meta = new Dictionary<string, string>
            {
                ["sistema"] = system.Name,
                ["version"] = version,
                ["checksum_sha256"] = checksum,
                ["creado_utc"] = DateTime.UtcNow.ToString("O"),
                ["creado_por"] = _currentUser.Username ?? "sistema"
            };
            if (!string.IsNullOrWhiteSpace(carpetaDestino)) meta["destino"] = carpetaDestino.Trim();

            var ruta = $"{_blob.RutaVersiones(carpetaDestino)}/{zipName}";
            try { blobUrl = await _blob.UploadFileAsync(zipPath, ruta, progress, ct, meta); }
            catch (Exception ex) { progress.Report($"  ⚠  Azure Blob: {ex.Message} (se guardó solo localmente)"); }
        }
        else
        {
            progress.Report("  ℹ  Azure Blob no configurado — versión solo local.");
        }

        var release = new AppRelease
        {
            AppSystemId  = appSystemId,
            Version      = version,
            Changelog    = changelog,
            ZipLocalPath = zipPath,
            ZipBlobUrl   = blobUrl,
            ZipChecksum  = checksum,
            ZipSizeBytes = sizeBytes,
            TargetFolder = string.IsNullOrWhiteSpace(carpetaDestino) ? null : carpetaDestino.Trim(),
            CreatedById  = _currentUser.User?.Id,
            CreatedAt    = DateTime.UtcNow
        };
        _db.AppReleases.Add(release);
        await _db.SaveChangesAsync(ct);
        progress.Report($"✅  Versión '{version}' creada (ID {release.Id}).");
        _audit.Record(AuditAction.Create, "AppRelease", release.Id.ToString(), $"{system.Name} v{version}");
        return release;
    }

    /// <summary>
    /// Crea una versión tomando el paquete de un archivo YA EXISTENTE en Blob Storage, en vez de
    /// comprimir una carpeta local. El artefacto no se re-sube (ya está en el blob): se descarga una
    /// copia local para poder desplegar de inmediato y se registra su URL, checksum y tamaño. La
    /// subcarpeta de destino se deduce de la ruta del propio blob.
    /// </summary>
    public async Task<AppRelease> CreateReleaseFromBlobAsync(
        int appSystemId, string version, string? changelog, string blobName,
        IProgress<string> progress, CancellationToken ct = default)
    {
        var system = await _db.AppSystems.FindAsync(new object[] { appSystemId }, ct)
            ?? throw new ArgumentException("Sistema no encontrado.");
        if (!_blob.IsConfigured)
            throw new InvalidOperationException("Azure Blob Storage no está configurado.");
        if (string.IsNullOrWhiteSpace(blobName))
            throw new ArgumentException("No se indicó el archivo del blob.");

        // No se descarga: el paquete se queda en el blob y se LEE EN STREAMING al desplegar (estilo
        // Blobup). Solo se registran la referencia y el tamaño (de las propiedades del blob).
        progress.Report($"🔗  Registrando el paquete del blob: {blobName}");
        var sizeBytes = await _blob.TamanoBlobAsync(blobName, ct);
        progress.Report($"  ✓  {sizeBytes / 1024.0 / 1024.0:F1} MB — se leerá en streaming al desplegar.");

        // Subcarpeta destino = la carpeta del propio blob dentro de «releases».
        var carpetaDestino = SubcarpetaDe(blobName);

        var release = new AppRelease
        {
            AppSystemId  = appSystemId,
            Version      = version,
            Changelog    = changelog,
            ZipLocalPath = null,   // vive solo en el blob; el despliegue lo lee por streaming
            ZipBlobUrl   = _blob.UriDeBlob(blobName).ToString(),
            ZipChecksum  = null,   // la integridad se valida por CRC de cada entrada del ZIP al leerlo
            ZipSizeBytes = sizeBytes,
            TargetFolder = carpetaDestino,
            CreatedById  = _currentUser.User?.Id,
            CreatedAt    = DateTime.UtcNow
        };
        _db.AppReleases.Add(release);
        await _db.SaveChangesAsync(ct);
        progress.Report($"✅  Versión '{version}' creada desde el blob (ID {release.Id}).");
        _audit.Record(AuditAction.Create, "AppRelease", release.Id.ToString(), $"{system.Name} v{version} (desde Blob: {blobName})");
        return release;
    }

    /// <summary>Subcarpeta (bajo «releases») a la que pertenece un blob, o null si está en la raíz.</summary>
    private string? SubcarpetaDe(string blobName)
    {
        var prefijo = _blob.PrefijoVersiones + "/";
        if (!blobName.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)) return null;
        var resto = blobName[prefijo.Length..];
        int barra = resto.LastIndexOf('/');
        return barra > 0 ? resto[..barra] : null;
    }

    // ── Selección directa de servidores (estilo Blobup) ──────────

    /// <summary>Nombre fijo del perfil interno que respalda la «selección directa» de servidores.</summary>
    public const string PerfilSeleccionDirecta = "⚡ Selección directa";

    /// <summary>
    /// Prepara el despliegue a una selección DIRECTA de servidores (sin perfil guardado, como Blobup):
    /// reutiliza un único perfil interno marcado <see cref="DeploymentProfile.IsAdHoc"/> y reescribe sus
    /// servidores con los elegidos (solo los activos). Devuelve el Id del perfil, listo para
    /// <see cref="DeployAsync"/>. Está separado del envío para poder probarlo sin red.
    /// </summary>
    public int PrepararSeleccionDirecta(IReadOnlyList<int> targetIds)
    {
        var activos = _db.DeploymentTargets.Where(t => t.IsActive && targetIds.Contains(t.Id)).Select(t => t.Id).ToList();
        if (activos.Count == 0)
            throw new InvalidOperationException("No hay servidores activos seleccionados.");

        // El perfil REUTILIZABLE se identifica por su nombre fijo (no por IsAdHoc a secas): ahora también
        // existen perfiles «congelados» IsAdHoc de despliegues programados, y a ésos NUNCA hay que tocarlos.
        var perfil = _db.DeploymentProfiles.FirstOrDefault(p => p.IsAdHoc && p.Name == PerfilSeleccionDirecta);
        if (perfil == null)
        {
            perfil = new DeploymentProfile
            {
                Name = PerfilSeleccionDirecta,
                Description = "Servidores elegidos directamente para un despliegue (no es un perfil guardado).",
                AllowedForOperaciones = false,   // da igual: IsAdHoc lo oculta de todos los selectores
                IsAdHoc = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.DeploymentProfiles.Add(perfil);
            _db.SaveChanges();
        }

        var viejos = _db.DeploymentProfileTargets.Where(pt => pt.ProfileId == perfil.Id).ToList();
        _db.DeploymentProfileTargets.RemoveRange(viejos);
        int orden = 0;
        foreach (var tid in activos)
            _db.DeploymentProfileTargets.Add(new DeploymentProfileTarget { ProfileId = perfil.Id, TargetId = tid, Order = orden++ });
        _db.SaveChanges();

        return perfil.Id;
    }

    /// <summary>
    /// Crea un perfil interno CONGELADO (oculto, IsAdHoc) con los servidores dados, para un despliegue
    /// PROGRAMADO. A diferencia de la «selección directa» reutilizable, este NO se reescribe: la cita se
    /// disparará exactamente a los servidores elegidos aunque después alguien haga otra selección directa
    /// u otro agendado. Devuelve el Id del perfil. Solo Admin u Operaciones.
    /// </summary>
    public int CrearPerfilCongelado(IReadOnlyList<int> targetIds, string etiqueta)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(_currentUser);
        var activos = _db.DeploymentTargets.Where(t => t.IsActive && targetIds.Contains(t.Id)).Select(t => t.Id).ToList();
        if (activos.Count == 0)
            throw new InvalidOperationException("No hay servidores activos seleccionados.");

        var perfil = new DeploymentProfile
        {
            Name = string.IsNullOrWhiteSpace(etiqueta) ? "⏱ Despliegue programado" : etiqueta,
            Description = "Servidores congelados para un despliegue programado (no es un perfil guardado).",
            AllowedForOperaciones = false,   // IsAdHoc lo oculta de todos los selectores
            IsAdHoc = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.DeploymentProfiles.Add(perfil);
        _db.SaveChanges();

        int orden = 0;
        foreach (var tid in activos)
            _db.DeploymentProfileTargets.Add(new DeploymentProfileTarget { ProfileId = perfil.Id, TargetId = tid, Order = orden++ });
        _db.SaveChanges();

        return perfil.Id;
    }

    /// <summary>
    /// ¿Puede este usuario desplegar ESTE perfil? Admin siempre; Operaciones solo los perfiles
    /// habilitados. Los perfiles internos IsAdHoc (la «selección directa» reutilizable y los snapshots
    /// congelados de despliegues programados) están EXENTOS: son internos y solo se alcanzan por rutas ya
    /// autorizadas (DeployToServersAsync / ProgramarServidores, que exigen RequireAdminOrOperaciones), así
    /// que Operaciones sí puede desplegarlos aunque tengan AllowedForOperaciones=false.
    /// </summary>
    internal static bool PuedeDesplegarPerfil(ICurrentUser user, DeploymentProfile profile)
        => user.IsAdmin || profile.IsAdHoc || profile.AllowedForOperaciones;

    /// <summary>
    /// Despliega una versión a una selección DIRECTA de servidores. Admin y Operaciones pueden usarla
    /// (Operaciones también elige servidores, no solo perfiles). Internamente arma la selección y reutiliza
    /// <see cref="DeployAsync"/>, así que hereda streaming, respaldo previo, reintentos e historial.
    /// </summary>
    public async Task<DeploymentJob> DeployToServersAsync(
        int releaseId, IReadOnlyList<int> targetIds,
        IProgress<string> progress, CancellationToken ct = default,
        IReadOnlySet<int>? respaldarTargets = null, IProgress<DeployStatus>? onStatus = null,
        string? evidenciaChecklist = null)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(_currentUser);
        var profileId = PrepararSeleccionDirecta(targetIds);
        return await DeployAsync(releaseId, profileId, progress, ct, respaldarTargets, onStatus, evidenciaChecklist);
    }

    // ── Deployment ───────────────────────────────────────────────

    /// <summary>
    /// Ejecuta un despliegue.
    ///
    /// Cambios de fondo respecto a la versión anterior, todos por defectos reales:
    ///  · <b>Guarda de autorización en el servicio</b>: antes la restricción de Operaciones vivía
    ///    solo en el combo de la pantalla, así que cualquier otra ruta desplegaba lo que quisiera.
    ///  · <b>Contexto de BD propio</b>: el AppDbContext es singleton y compartido con la UI. Un
    ///    despliegue largo —y más aún uno programado en segundo plano— chocaba con cualquier otra
    ///    pantalla ("A second operation started on this context") y además su SaveChanges arrastraba
    ///    entidades sucias ajenas.
    ///  · <b>Bitácora persistida paso a paso</b>: antes todo el log se guardaba hasta el
    ///    SaveChanges final, de modo que una cancelación lo perdía entero y dejaba el job
    ///    eternamente "EnCurso".
    ///  · <b>Respaldo previo</b> de la carpeta remota a Blob Storage, para poder revertir. Es
    ///    OPCIONAL y POR SERVIDOR (<paramref name="respaldarTargets"/>): null = respaldar todos;
    ///    en otro caso, solo los servidores cuyo Id esté en el conjunto (vacío = ninguno).
    ///  · <b>Avance</b> (<paramref name="onStatus"/>): porcentaje global 0..100 + servidor y detalle
    ///    (qué archivo se sube, si está respaldando…), para la barra y el renglón de estado.
    ///  · <b>Reintentos</b> ante conexiones lentas (ver <see cref="FtpRetryPolicy"/>).
    /// </summary>
    /// <param name="evidenciaChecklist">
    /// El checklist previo confirmado por quien despliega (ver <see cref="DeploymentChecklist"/>).
    /// Se guarda con el job: separada del hecho que documenta, la evidencia se pierde.
    /// </param>
    public async Task<DeploymentJob> DeployAsync(
        int releaseId, int profileId,
        IProgress<string> progress, CancellationToken ct = default,
        IReadOnlySet<int>? respaldarTargets = null, IProgress<DeployStatus>? onStatus = null,
        string? evidenciaChecklist = null)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(_currentUser);
        var correlacion = AuditService.NuevaCorrelacion();

        // Contexto propio: aislado de la UI y utilizable desde un hilo de segundo plano.
        await using var db = new AppDbContext(_dbOptions);
        var audit = new AuditService(db, _currentUser);

        var release = await db.AppReleases
            .Include(r => r.AppSystem)
            .FirstOrDefaultAsync(r => r.Id == releaseId, ct)
            ?? throw new ArgumentException("Versión no encontrada.");

        var profile = await db.DeploymentProfiles
            .Include(p => p.ProfileTargets).ThenInclude(pt => pt.Target)
            .FirstOrDefaultAsync(p => p.Id == profileId, ct)
            ?? throw new ArgumentException("Perfil no encontrado.");

        // La regla se revalida contra el perfil REAL, no contra lo que la pantalla haya mostrado.
        if (!PuedeDesplegarPerfil(_currentUser, profile))
        {
            audit.RecordDenied(AuditAction.Deploy, "DeploymentProfile", profile.Id.ToString(),
                $"Intento de desplegar el perfil «{profile.Name}», no habilitado para Operaciones.", correlacion);
            throw new AuthorizationException(
                $"El perfil «{profile.Name}» no está habilitado para el rol Operaciones.");
        }

        // Un servidor desactivado ya no debe recibir despliegues; antes se le desplegaba igual.
        var targets = profile.ProfileTargets.OrderBy(pt => pt.Order)
            .Select(pt => pt.Target).Where(t => t.IsActive).ToList();
        if (targets.Count == 0)
            throw new InvalidOperationException("El perfil no tiene servidores activos asignados.");

        // El paquete puede estar en disco (equipo que lo creó) o solo en Blob Storage (versión creada
        // desde el blob, o despliegue desde otra máquina). Se resuelve el ORIGEN aquí; el contenido se
        // LEE EN STREAMING más abajo, sin bajarlo a disco ni extraerlo a una carpeta temporal.
        var localZip = release.ZipLocalPath;
        var blobName = _blob.IsConfigured ? _blob.BlobNameFromUrl(release.ZipBlobUrl) : null;
        bool hayLocal = !string.IsNullOrEmpty(localZip) && File.Exists(localZip);
        if (!hayLocal && string.IsNullOrEmpty(blobName))
            throw new FileNotFoundException("El paquete de la versión no está disponible ni en disco ni en Blob Storage.");
        var origenPaquete = hayLocal ? Path.GetFileName(localZip)! : $"Blob (streaming): {blobName}";

        var job = new DeploymentJob
        {
            AppReleaseId        = releaseId,
            DeploymentProfileId = profileId,
            Status              = JobStatus.EnCurso,
            StartedAt           = DateTime.UtcNow,
            StartedById         = _currentUser.User?.Id,
            TargetsTotal        = targets.Count,
            Notes               = evidenciaChecklist,
            CreatedAt           = DateTime.UtcNow
        };
        db.DeploymentJobs.Add(job);
        await db.SaveChangesAsync(ct);

        audit.RecordDetailed(AuditAction.Deploy, "DeploymentJob", job.Id.ToString(),
            $"Inicio: {release.AppSystem.Name} v{release.Version} → perfil «{profile.Name}» ({targets.Count} servidor(es))",
            AuditOutcome.Exito, correlationId: correlacion);

        int conRespaldo = respaldarTargets == null ? targets.Count : targets.Count(t => respaldarTargets.Contains(t.Id));
        var respaldoResumen = conRespaldo == targets.Count ? "todos"
                            : conRespaldo == 0 ? "ninguno"
                            : $"{conRespaldo} de {targets.Count}";

        var relojTotal = System.Diagnostics.Stopwatch.StartNew();
        progress.Report($"\n🚀  Iniciando despliegue de {release.AppSystem.Name} v{release.Version}");
        progress.Report($"    Perfil: {profile.Name} — {targets.Count} servidor(es)");
        progress.Report($"    Paquete: {origenPaquete}   ·   Bitácora: {correlacion}");
        progress.Report($"    Respaldo previo: {respaldoResumen}");
        progress.Report($"    🕐 Hora de inicio: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        progress.Report(new string('─', 60));

        await LogAsync(db, job, null,
            $"Despliegue iniciado por {_currentUser.Username ?? "sistema"} (correlación {correlacion}). Respaldo previo: {respaldoResumen}.",
            DeployLogLevel.Info, ct);

        int ok = 0, failed = 0, procesados = 0;
        bool cancelado = false;
        onStatus?.Report(new DeployStatus(0, "", "preparando…"));

        try
        {
            // Lectura EN STREAMING del paquete (estilo Blobup): si hay copia local se lee del disco;
            // si no, se abre el blob con lecturas por rango (OpenRead) SIN descargarlo entero. Las
            // entradas del ZIP quedan en memoria y se publican por FTP a cada servidor; no se extrae a
            // ninguna carpeta temporal. La integridad de cada archivo la valida el CRC del propio ZIP.
            progress.Report(hayLocal ? "📂  Leyendo el paquete (local)..." : "📡  Leyendo el paquete desde Blob Storage (streaming)...");
            List<ReleaseEntry> entradas;
            Stream zipStream = hayLocal ? File.OpenRead(localZip!) : await _blob.OpenReadBlobAsync(blobName!, ct);
            await using (zipStream)
                entradas = await LeerEntradasAsync(zipStream, ct);
            progress.Report($"  ✓  {entradas.Count} archivos listos para publicar.");
            await LogAsync(db, job, null, $"Paquete leído en streaming: {entradas.Count} archivos desde {(hayLocal ? "disco" : "Blob Storage")}.", DeployLogLevel.Info, ct);

            int idx = 0;
            foreach (var target in targets)
            {
                if (ct.IsCancellationRequested) { cancelado = true; break; }
                progress.Report($"\n🌐  [{target.Nombre}]  ·  🕐 {DateTime.Now:HH:mm:ss}");
                var reloj = System.Diagnostics.Stopwatch.StartNew();

                bool respaldar = respaldarTargets == null || respaldarTargets.Contains(target.Id);
                try
                {
                    await DeployToTargetAsync(db, job, target, entradas, respaldar, idx, targets.Count, onStatus, progress, ct);
                    reloj.Stop();

                    // Lo que responde «¿qué tiene este servidor, quién se lo puso y cuándo?» sin
                    // reconstruirlo leyendo la bitácora entera. Se guarda por servidor y en cuanto
                    // termina: si el despliegue falla en el siguiente, lo ya publicado queda igual
                    // de registrado.
                    target.LastDeployedAt      = DateTime.UtcNow;
                    target.LastReleaseId       = releaseId;
                    target.LastDeployedById    = _currentUser.User?.Id;
                    target.LastDeploymentJobId = job.Id;
                    await db.SaveChangesAsync(ct);

                    progress.Report($"  ✅  {target.Nombre} completado en {reloj.Elapsed.TotalSeconds:0.0}s  ·  🕐 {DateTime.Now:HH:mm:ss}");
                    await LogAsync(db, job, target, $"Despliegue completado en {reloj.Elapsed.TotalSeconds:0.0}s (fin {DateTime.Now:HH:mm:ss}).", DeployLogLevel.Exito, ct);
                    ok++;
                }
                catch (OperationCanceledException)
                {
                    // La cancelación NO se propaga: se cierra el job ordenadamente para no perder
                    // la bitácora ni dejarlo colgado en "EnCurso".
                    cancelado = true;
                    await LogAsync(db, job, target, "Cancelado por el usuario durante este servidor.", DeployLogLevel.Advertencia, CancellationToken.None);
                    break;
                }
                catch (Exception ex)
                {
                    reloj.Stop();
                    progress.Report($"  ❌  [{target.Nombre}] Error tras {reloj.Elapsed.TotalSeconds:0.0}s: {ex.Message}");
                    await LogAsync(db, job, target, $"Error tras {reloj.Elapsed.TotalSeconds:0.0}s: {ex.Message}", DeployLogLevel.Error, CancellationToken.None);
                    failed++;
                }

                idx++;
                procesados++;
                onStatus?.Report(new DeployStatus((int)Math.Round((double)procesados / targets.Count * 100), target.Nombre, $"servidor {procesados} de {targets.Count} listo"));
            }
        }
        catch (OperationCanceledException) { cancelado = true; }

        int noIntentados = targets.Count - ok - failed;
        job.Status = cancelado ? JobStatus.Cancelado
                   : failed == 0 ? JobStatus.Completado
                   : ok == 0     ? JobStatus.Fallido
                   : JobStatus.Parcial;   // antes un despliegue a medias se reportaba como Completado
        job.CompletedAt   = DateTime.UtcNow;
        job.TargetsOk     = ok;
        job.TargetsFailed = failed;
        // CancellationToken.None: el cierre del job debe guardarse aunque se haya cancelado.
        await db.SaveChangesAsync(CancellationToken.None);

        var resumen = $"{release.AppSystem.Name} v{release.Version} → {profile.Name}: {ok} OK, {failed} fallidos"
                    + (noIntentados > 0 ? $", {noIntentados} sin intentar" : "");
        await LogAsync(db, job, null, $"Fin ({job.Status}). {resumen}",
            job.Status == JobStatus.Completado ? DeployLogLevel.Exito : DeployLogLevel.Advertencia, CancellationToken.None);

        relojTotal.Stop();
        progress.Report(new string('─', 60));
        progress.Report($"\n{(job.Status == JobStatus.Completado ? "✅" : "⚠")}  Despliegue {job.Status} — OK: {ok}  Fallidos: {failed}"
                        + (noIntentados > 0 ? $"  Sin intentar: {noIntentados}" : ""));
        progress.Report($"    🕐 Hora de fin: {DateTime.Now:dd/MM/yyyy HH:mm:ss}  ·  Duración total: {FormatoDuracion(relojTotal.Elapsed)}");
        onStatus?.Report(new DeployStatus(100, "", $"{job.Status} en {FormatoDuracion(relojTotal.Elapsed)}"));

        audit.RecordDetailed(AuditAction.Deploy, "DeploymentJob", job.Id.ToString(), resumen,
            job.Status is JobStatus.Completado ? AuditOutcome.Exito : AuditOutcome.Fallo,
            newValues: new { job.Status, job.TargetsOk, job.TargetsFailed, NoIntentados = noIntentados },
            correlationId: correlacion);

        return job;
    }

    /// <summary>Un archivo del paquete ya leído en memoria (nombre relativo + contenido).</summary>
    private sealed record ReleaseEntry(string RelativePath, byte[] Content);

    /// <summary>
    /// Lee las entradas de un ZIP a memoria (como Blobup). El stream puede ser un archivo local o un
    /// blob abierto por rango (OpenRead): ZipArchive en modo lectura solo exige que admita Seek. Al
    /// leer cada entrada completa se valida su CRC, así que la integridad va incluida.
    /// </summary>
    private static async Task<List<ReleaseEntry>> LeerEntradasAsync(Stream zipStream, CancellationToken ct)
    {
        var lista = new List<ReleaseEntry>();
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var e in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            // Entradas de carpeta: nombre vacío o el nombre completo termina en «/».
            if (string.IsNullOrEmpty(e.Name) || e.FullName.EndsWith('/')) continue;

            await using var es = e.Open();
            using var ms = new MemoryStream();
            await es.CopyToAsync(ms, ct);
            lista.Add(new ReleaseEntry(e.FullName.Replace('\\', '/'), ms.ToArray()));
        }
        return lista;
    }

    /// <summary>
    /// Publica el paquete (ya en memoria) a un servidor, con respaldo previo y reintentos. Cada
    /// archivo se sube por FTP desde un MemoryStream (streaming), sin tocar disco. El respaldo va
    /// ANTES de tocar nada: si no se puede respaldar, no se despliega.
    /// </summary>
    private async Task DeployToTargetAsync(AppDbContext db, DeploymentJob job, DeploymentTarget target,
        IReadOnlyList<ReleaseEntry> entradas, bool hacerRespaldo, int serverIndex, int totalServers,
        IProgress<DeployStatus>? onStatus, IProgress<string> progress, CancellationToken ct)
    {
        var (host, explicitTls) = FtpRetryPolicy.ParseHost(target.Host);

        // Sin fallback al valor crudo. Antes, cuando no se podía descifrar, se mandaba el texto
        // CIFRADO como contraseña: el servidor contestaba «530 User cannot log in» y el problema
        // parecía de las credenciales del FTP y no de esta aplicación. Hoy solo puede pasar con una
        // contraseña heredada que se capturó en otra PC (cifrado DPAPI, atado a esa cuenta de
        // Windows), y lo que hay que hacer es recapturarla una vez — ya queda legible para todos.
        if (!SharedSecretProtector.TryUnprotect(target.Contrasena, out var password))
            throw new InvalidOperationException(
                $"No se pudo descifrar la contraseña del servidor «{target.Nombre}» en este equipo. " +
                "Se guardó desde otra PC con el cifrado anterior. Un líder debe volver a " +
                "capturarla en Despliegues → Servidores (una sola vez: a partir de ahí la usan todos).");

        // Avance global = (servidores ya terminados + fracción de archivos del actual) / total.
        void Estado(string detalle, int done)
        {
            double frac = entradas.Count <= 0 ? 0 : (double)done / entradas.Count;
            int pct = (int)Math.Round((serverIndex + frac) / Math.Max(1, totalServers) * 100);
            onStatus?.Report(new DeployStatus(pct, target.Nombre, detalle));
        }

        // 1. Respaldo de la carpeta remota completa a Blob Storage (opcional, por servidor). Si está
        //    apagado para este servidor, se publica directo, sin copia previa.
        if (hacerRespaldo)
        {
            Estado("respaldando la carpeta remota…", 0);
            await _remoteBackup.RespaldarAsync(target, host, explicitTls, password, job.Id, progress, ct);
        }
        else
            await LogAsync(db, job, target, "Sin respaldo previo para este servidor (elección del usuario).", DeployLogLevel.Advertencia, ct);

        // 2. Publicación con reintentos a nivel sitio: ante cualquier fallo se descarta la conexión.
        var subidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Estado("conectando…", 0);

        await FtpRetryPolicy.ConReintentosAsync(async intento =>
        {
            if (intento > 0)
            {
                progress.Report($"  ↻  Reintento {intento + 1}/{FtpRetryPolicy.MaxSiteAttempts} sobre {host} ({subidos.Count}/{entradas.Count} ya subidos)");
                Estado($"reintentando (intento {intento + 1})…", subidos.Count);
            }

            await using var client = new AsyncFtpClient(host, target.Usuario, password, target.Puerto);
            FtpRetryPolicy.Configure(client.Config, explicitTls);
            await client.AutoConnect(ct);
            progress.Report($"  ✓  Conectado a {host}:{target.Puerto}");

            foreach (var entrada in entradas)
            {
                ct.ThrowIfCancellationRequested();
                if (subidos.Contains(entrada.RelativePath)) continue;   // ya fue en una pasada anterior

                var remoto = target.RutaRemota.TrimEnd('/') + "/" + entrada.RelativePath;
                // Detalle en vivo: qué archivo se está subiendo y cuántos van.
                Estado($"subiendo {entrada.RelativePath}  ({subidos.Count + 1}/{entradas.Count})", subidos.Count);

                // Reintento barato por archivo, sin tirar la sesión.
                await FtpRetryPolicy.ConReintentosAsync(async _ =>
                {
                    if (!client.IsConnected) await client.AutoConnect(ct);
                    using var ms = new MemoryStream(entrada.Content, writable: false);
                    var status = await client.UploadStream(ms, remoto, FtpRemoteExists.Overwrite, createRemoteDir: true, progress: null, token: ct);
                    if (status == FtpStatus.Failed)
                        throw new IOException($"El servidor rechazó {entrada.RelativePath}");
                    return status;
                }, FtpRetryPolicy.MaxFileAttempts, progress, entrada.RelativePath, ct);

                subidos.Add(entrada.RelativePath);
                Estado($"subidos {subidos.Count}/{entradas.Count}", subidos.Count);
            }

            await client.Disconnect(ct);
            return true;
        }, FtpRetryPolicy.MaxSiteAttempts, progress, $"servidor {target.Nombre}", ct);

        progress.Report($"  ✓  {subidos.Count} archivos publicados → {target.RutaRemota}");
        await LogAsync(db, job, target, $"{subidos.Count} archivo(s) publicados a {target.RutaRemota}.", DeployLogLevel.Info, ct);
    }

    private static string FormatoDuracion(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s"
        : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds}s"
        : $"{t.TotalSeconds:0.0}s";

    /// <summary>
    /// Escribe una entrada de bitácora y la PERSISTE de inmediato. Acumularlas hasta el final
    /// hacía que una cancelación o una caída se llevara el registro completo.
    /// </summary>
    private static async Task LogAsync(AppDbContext db, DeploymentJob job, DeploymentTarget? target,
        string msg, DeployLogLevel level, CancellationToken ct)
    {
        db.DeploymentLogEntries.Add(new DeploymentLogEntry
        {
            JobId      = job.Id,
            TargetId   = target?.Id,
            TargetName = target?.Nombre,
            Message    = msg,
            Level      = level,
            Timestamp  = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLower();
    }

    // ── JSON import of targets ────────────────────────────────────
    public record JsonTarget(string Nombre, string Host, int Puerto, string Usuario,
        string Contrasena, string RutaRemota, string? URL);

    public (int added, int updated) ImportTargetsFromJson(string json)
    {
        var list = System.Text.Json.JsonSerializer.Deserialize<List<JsonTarget>>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("JSON inválido.");

        int added = 0, updated = 0;
        foreach (var jt in list)
        {
            var existing = _db.DeploymentTargets.FirstOrDefault(t => t.Nombre == jt.Nombre);
            if (existing == null)
            {
                _db.DeploymentTargets.Add(new DeploymentTarget
                {
                    Nombre       = jt.Nombre,
                    Host         = jt.Host,
                    Puerto       = jt.Puerto,
                    Usuario      = jt.Usuario,
                    Contrasena   = SharedSecretProtector.Protect(jt.Contrasena),
                    RutaRemota   = jt.RutaRemota,
                    URL          = jt.URL,
                    IsActive     = true
                });
                added++;
            }
            else
            {
                existing.Host       = jt.Host;
                existing.Puerto     = jt.Puerto;
                existing.Usuario    = jt.Usuario;
                existing.Contrasena = SharedSecretProtector.Protect(jt.Contrasena);
                existing.RutaRemota = jt.RutaRemota;
                existing.URL        = jt.URL;
                updated++;
            }
        }
        _db.SaveChanges();
        return (added, updated);
    }
}
