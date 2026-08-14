using System.Security.Cryptography;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Despliegues;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// El catálogo del que se despliega: los sistemas, sus versiones y los perfiles de destino. Es la
/// escritura que en el escritorio vivía repartida entre las pestañas «Sistemas y Versiones» y
/// «Perfiles» del control de despliegues.
///
/// <para><b>Cómo entra una versión, y qué cambia.</b> En el escritorio se creaba de dos maneras:
/// comprimiendo una carpeta de la máquina de quien la creaba, o tomando un artefacto ya subido a
/// Azure Blob Storage.</para>
///
/// <para>La primera <b>no se puede traducir</b> y no se traduce: en el navegador no hay carpeta local
/// que el servidor pueda leer, y una caja de texto donde alguien teclea una ruta del servidor es un
/// agujero, no una función. La segunda <b>sí está</b>: se elige un paquete de la carpeta de versiones
/// del almacén. Y se añade una tercera, la misma idea sin depender de Blob: el .zip se deja en la
/// <b>carpeta de despliegue del servidor</b> (<c>DefaultDeployFolder</c>) —por CI, por FTP, como
/// sea— y desde aquí se registra.</para>
///
/// <para>En los dos casos el paquete no se copia ni se mueve: se apunta a él, se mide y se le calcula
/// el SHA-256.</para>
/// </summary>
public class DespliegueCatalogoService(
    AppDbContext db,
    ICurrentUser quien,
    AuditService bitacora,
    SettingsService configuracion)
{
    /// <summary>
    /// Subcarpeta de la carpeta de despliegue donde el motor deja los respaldos previos. No son
    /// paquetes desplegables —son copias de lo que HABÍA en un servidor— y por eso el listado de
    /// paquetes solo mira el nivel de arriba.
    /// </summary>
    public const string CarpetaDeRespaldos = "respaldos";

    // ── Sistemas ─────────────────────────────────────────────────────────────────

    public async Task<(bool ok, string mensaje)> CrearSistemaAsync(
        GuardarSistemaRequest datos, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var nombre = (datos.Nombre ?? "").Trim();
        if (nombre.Length == 0) return (false, "El nombre del sistema es obligatorio.");
        if (await db.AppSystems.AnyAsync(s => s.Name == nombre, ct))
            return (false, $"Ya existe un sistema llamado «{nombre}».");

        var sistema = new AppSystem
        {
            Name = nombre,
            Description = Limpiar(datos.Descripcion),
            DefaultBlobFolder = Limpiar(datos.CarpetaPorOmision),
            TeamId = datos.EquipoId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.AppSystems.Add(sistema);
        await db.SaveChangesAsync(ct);

        await bitacora.RecordAsync(AuditAction.Create, "AppSystem", sistema.Id.ToString(), sistema.Name, ct);
        return (true, $"Sistema «{sistema.Name}» dado de alta.");
    }

    public async Task<(bool ok, string mensaje)> EditarSistemaAsync(
        int sistemaId, GuardarSistemaRequest datos, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var sistema = await db.AppSystems.FirstOrDefaultAsync(s => s.Id == sistemaId, ct);
        if (sistema == null) return (false, "Ese sistema ya no existe. Actualiza la lista.");

        var nombre = (datos.Nombre ?? "").Trim();
        if (nombre.Length == 0) return (false, "El nombre del sistema es obligatorio.");
        if (await db.AppSystems.AnyAsync(s => s.Name == nombre && s.Id != sistemaId, ct))
            return (false, $"Ya existe otro sistema llamado «{nombre}».");

        var previo = new { sistema.Name, sistema.Description, sistema.DefaultBlobFolder, sistema.TeamId };
        sistema.Name = nombre;
        sistema.Description = Limpiar(datos.Descripcion);
        sistema.DefaultBlobFolder = Limpiar(datos.CarpetaPorOmision);
        sistema.TeamId = datos.EquipoId;
        await db.SaveChangesAsync(ct);

        await bitacora.RecordDetailedAsync(AuditAction.Update, "AppSystem", sistema.Id.ToString(),
            $"Sistema editado: {sistema.Name}", AuditOutcome.Exito, previo,
            new { sistema.Name, sistema.Description, sistema.DefaultBlobFolder, sistema.TeamId }, ct: ct);

        return (true, "Sistema actualizado.");
    }

    /// <summary>
    /// Da de baja o reactiva un sistema.
    ///
    /// <para><b>Sustituye al borrado del escritorio, y es una corrección.</b> Allí el botón
    /// «Eliminar» borraba el sistema con todas sus versiones; y como el borrado de una versión
    /// arrastra en cascada sus despliegues, ese clic también se llevaba por delante el historial de
    /// todo lo que se hubiera desplegado desde ese sistema — justo lo que el resto del módulo se
    /// esfuerza en conservar. Un sistema dado de baja deja de ofrecerse para desplegar y su historial
    /// queda intacto.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> AlternarSistemaAsync(int sistemaId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var sistema = await db.AppSystems.FirstOrDefaultAsync(s => s.Id == sistemaId, ct);
        if (sistema == null) return (false, "Ese sistema ya no existe. Actualiza la lista.");

        sistema.IsActive = !sistema.IsActive;
        await db.SaveChangesAsync(ct);

        await bitacora.RecordAsync(AuditAction.Update, "AppSystem", sistema.Id.ToString(),
            sistema.IsActive ? $"Sistema reactivado: {sistema.Name}" : $"Sistema dado de baja: {sistema.Name}", ct);

        return (true, sistema.IsActive
            ? "Sistema reactivado."
            : "Sistema dado de baja. Deja de ofrecerse para desplegar y su historial se conserva.");
    }

    // ── Versiones ────────────────────────────────────────────────────────────────

    /// <summary>Los .zip que están en la carpeta de despliegue y todavía no son una versión registrada.</summary>
    public async Task<List<PaqueteDisponibleDto>> PaquetesDisponiblesAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var carpeta = await CarpetaDeDespliegueAsync(ct);
        if (carpeta == null || !Directory.Exists(carpeta)) return [];

        var yaRegistrados = await db.AppReleases.AsNoTracking()
            .Where(r => r.ZipLocalPath != null)
            .Select(r => r.ZipLocalPath!)
            .ToListAsync(ct);
        var nombresRegistrados = yaRegistrados
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Solo el nivel de arriba: la subcarpeta de respaldos guarda copias de lo que HABÍA en los
        // servidores, que no es algo que nadie quiera volver a desplegar por error.
        return [.. Directory.EnumerateFiles(carpeta, "*.zip", SearchOption.TopDirectoryOnly)
            .Select(ruta => new FileInfo(ruta))
            .Where(f => !nombresRegistrados.Contains(f.Name))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new PaqueteDisponibleDto(f.Name, f.Length, f.LastWriteTimeUtc))];
    }

    /// <summary>Registra como versión un paquete que ya está en la carpeta de despliegue del servidor.</summary>
    public async Task<(bool ok, string mensaje)> CrearVersionAsync(
        CrearVersionRequest datos, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var sistema = await db.AppSystems.FirstOrDefaultAsync(s => s.Id == datos.SistemaId, ct);
        if (sistema == null) return (false, "Ese sistema ya no existe. Actualiza la lista.");

        var etiqueta = (datos.Version ?? "").Trim();
        if (etiqueta.Length == 0) return (false, "La etiqueta de la versión es obligatoria.");

        // Dos versiones del MISMO sistema no deben compartir etiqueta: se volverían indistinguibles
        // al desplegar y en el historial.
        if (await db.AppReleases.AnyAsync(r => r.AppSystemId == sistema.Id && r.Version == etiqueta, ct))
            return (false, $"Ya existe una versión «{etiqueta}» en {sistema.Name}. Usa otra etiqueta.");

        var carpeta = await CarpetaDeDespliegueAsync(ct);
        if (carpeta == null)
            return (false, "Falta configurar la carpeta de despliegue del servidor (clave DefaultDeployFolder). " +
                           "Es donde el servidor busca los paquetes.");

        // El nombre se compara contra lo que HAY, no se concatena a la ruta: así ningún «..\..\» ni
        // ninguna ruta absoluta puede señalar un archivo fuera de la carpeta de despliegue.
        var ruta = Directory.Exists(carpeta)
            ? Directory.EnumerateFiles(carpeta, "*.zip", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(r => string.Equals(Path.GetFileName(r), datos.Paquete, StringComparison.OrdinalIgnoreCase))
            : null;
        if (ruta == null)
            return (false, $"El servidor no encuentra «{datos.Paquete}» en su carpeta de despliegue. " +
                           "Vuelve a dejar el archivo ahí y actualiza la lista.");

        var informacion = new FileInfo(ruta);
        var checksum = await Task.Run(() => Sha256De(ruta), ct);

        var version = new AppRelease
        {
            AppSystemId = sistema.Id,
            Version = etiqueta,
            Changelog = Limpiar(datos.Changelog),
            ZipLocalPath = ruta,
            ZipChecksum = checksum,
            ZipSizeBytes = informacion.Length,
            TargetFolder = sistema.DefaultBlobFolder,
            CreatedById = quien.UserId,
            CreatedAt = DateTime.UtcNow
        };
        db.AppReleases.Add(version);
        await db.SaveChangesAsync(ct);

        await bitacora.RecordDetailedAsync(AuditAction.Create, "AppRelease", version.Id.ToString(),
            $"{sistema.Name} v{etiqueta} registrada desde {informacion.Name}", AuditOutcome.Exito,
            newValues: new { version.Version, version.ZipChecksum, version.ZipSizeBytes }, ct: ct);

        return (true, $"Versión «{etiqueta}» registrada ({informacion.Length / 1024d / 1024d:0.0} MB, " +
                      $"SHA-256 {checksum[..16]}…).");
    }

    /// <summary>
    /// Registra como versión un paquete que ya vive en el ALMACÉN (Azure Blob), que es de donde los
    /// saca la compilación automática.
    ///
    /// <para>Es la vía que el escritorio tenía y que aquí faltaba: el servicio de Blob se portó en
    /// paralelo y nadie unió las dos piezas, así que un artefacto correctamente subido no había forma
    /// de registrarlo. El motor de despliegue ya sabe bajarlo cuando toca desplegar.</para>
    ///
    /// <para>El paquete no se copia a ningún sitio: se apunta a él y se le calcula el SHA-256
    /// bajándolo una vez. Ese cálculo es lo que después permite afirmar que se desplegó exactamente
    /// lo que se registró.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> CrearVersionDesdeAlmacenAsync(
        AlmacenamientoService almacenamiento, CrearVersionDesdeAlmacenRequest datos,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var sistema = await db.AppSystems.FirstOrDefaultAsync(s => s.Id == datos.SistemaId, ct);
        if (sistema == null) return (false, "Ese sistema ya no existe. Actualiza la lista.");

        var etiqueta = (datos.Version ?? "").Trim();
        if (etiqueta.Length == 0) return (false, "La etiqueta de la versión es obligatoria.");

        if (await db.AppReleases.AnyAsync(r => r.AppSystemId == sistema.Id && r.Version == etiqueta, ct))
            return (false, $"Ya existe una versión «{etiqueta}» en {sistema.Name}. Usa otra etiqueta.");

        if (string.IsNullOrWhiteSpace(datos.Blob))
            return (false, "Elige el paquete en el almacén.");

        if (!await almacenamiento.EstaConfiguradoAsync(ct))
            return (false, "Azure Blob Storage no está configurado en este servidor.");

        // El nombre del blob se comprueba contra lo que HAY, no se concatena: el listado ya normaliza
        // y valida la ruta, así que ningún «../» puede señalar fuera de la carpeta de versiones.
        long tamano;
        string checksum;
        try
        {
            var (contenido, _) = await almacenamiento.AbrirParaDescargaAsync(datos.Blob, ct);
            await using (contenido)
                (tamano, checksum) = await MedirYFirmarAsync(contenido, ct);
        }
        catch (Exception ex)
        {
            return (false, $"No se pudo leer «{datos.Blob}» del almacén: {ex.Message}");
        }

        var version = new AppRelease
        {
            AppSystemId = sistema.Id,
            Version = etiqueta,
            Changelog = Limpiar(datos.Changelog),
            ZipBlobUrl = datos.Blob,
            ZipChecksum = checksum,
            ZipSizeBytes = tamano,
            TargetFolder = sistema.DefaultBlobFolder,
            CreatedById = quien.UserId,
            CreatedAt = DateTime.UtcNow
        };
        db.AppReleases.Add(version);
        await db.SaveChangesAsync(ct);

        await bitacora.RecordDetailedAsync(AuditAction.Create, "AppRelease", version.Id.ToString(),
            $"{sistema.Name} v{etiqueta} registrada desde el almacén ({datos.Blob})", AuditOutcome.Exito,
            newValues: new { version.Version, version.ZipChecksum, version.ZipSizeBytes }, ct: ct);

        return (true, $"Versión «{etiqueta}» registrada desde el almacén " +
                      $"({tamano / 1024d / 1024d:0.0} MB, SHA-256 {checksum[..16]}…).");
    }

    /// <summary>
    /// Mide y firma el paquete leyéndolo de corrido, sin volcarlo a disco: un artefacto de varios
    /// cientos de megas no tiene por qué aterrizar en el servidor solo para calcularle el hash.
    /// </summary>
    private static async Task<(long tamano, string sha256)> MedirYFirmarAsync(
        Stream contenido, CancellationToken ct)
    {
        using var algoritmo = System.Security.Cryptography.SHA256.Create();
        var bufer = new byte[81920];
        long total = 0;
        int leidos;

        while ((leidos = await contenido.ReadAsync(bufer, ct)) > 0)
        {
            algoritmo.TransformBlock(bufer, 0, leidos, null, 0);
            total += leidos;
        }
        algoritmo.TransformFinalBlock([], 0, 0);

        return (total, Convert.ToHexString(algoritmo.Hash!).ToLowerInvariant());
    }

    public async Task<(bool ok, string mensaje)> EditarVersionAsync(
        int versionId, EditarVersionRequest datos, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var version = await db.AppReleases.FirstOrDefaultAsync(r => r.Id == versionId, ct);
        if (version == null) return (false, "Esa versión ya no existe. Actualiza la lista.");

        // La ETIQUETA de una versión que ya se desplegó o está programada es su identificador en el
        // historial y en las citas programadas: renombrarla reescribiría lo que dicen esos registros.
        // Decisión del escritorio que se conserva.
        bool bloqueada = await EtiquetaBloqueadaAsync(versionId, ct);

        var etiqueta = (datos.Version ?? "").Trim();
        if (!bloqueada)
        {
            if (etiqueta.Length == 0) return (false, "La etiqueta de la versión es obligatoria.");
            if (await db.AppReleases.AnyAsync(
                    r => r.AppSystemId == version.AppSystemId && r.Id != versionId && r.Version == etiqueta, ct))
                return (false, $"Ya existe otra versión «{etiqueta}» en este sistema. Usa otra etiqueta.");
        }

        var previo = new { version.Version, version.TargetFolder, version.Changelog };
        if (!bloqueada) version.Version = etiqueta;
        version.TargetFolder = Limpiar(datos.CarpetaDestino);
        version.Changelog = Limpiar(datos.Changelog);
        await db.SaveChangesAsync(ct);

        await bitacora.RecordDetailedAsync(AuditAction.Update, "AppRelease", version.Id.ToString(),
            $"Versión editada: {version.Version}", AuditOutcome.Exito, previo,
            new { version.Version, version.TargetFolder, version.Changelog }, ct: ct);

        return (true, bloqueada
            ? "Versión actualizada. La etiqueta no se tocó: ya se desplegó y es su nombre en el historial."
            : "Versión actualizada.");
    }

    /// <summary>
    /// Quita una versión del catálogo. <b>No borra el .zip del servidor</b>: el archivo pudo dejarlo
    /// ahí un proceso de compilación y no le corresponde a esta pantalla decidir sobre él.
    /// </summary>
    public async Task<(bool ok, string mensaje)> EliminarVersionAsync(int versionId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var version = await db.AppReleases.FirstOrDefaultAsync(r => r.Id == versionId, ct);
        if (version == null) return (false, "Esa versión ya no existe.");

        // Borrarla arrastraría en cascada sus despliegues, y con ellos la evidencia de lo que se
        // publicó y quién lo publicó. El historial pesa más que la limpieza del catálogo.
        if (await EtiquetaBloqueadaAsync(versionId, ct))
            return (false, "No se puede eliminar: esta versión ya se desplegó o está programada, y " +
                           "borrarla se llevaría su historial. Da de baja el sistema si ya no se usa.");

        db.AppReleases.Remove(version);
        await db.SaveChangesAsync(ct);

        await bitacora.RecordAsync(AuditAction.Delete, "AppRelease", versionId.ToString(), version.Version, ct);
        return (true, "Versión eliminada del catálogo. El archivo .zip sigue en la carpeta del servidor.");
    }

    // ── Perfiles ─────────────────────────────────────────────────────────────────

    public async Task<(bool ok, string mensaje)> GuardarPerfilAsync(
        int? perfilId, GuardarPerfilRequest datos, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var nombre = (datos.Nombre ?? "").Trim();
        if (nombre.Length == 0) return (false, "El nombre del perfil es obligatorio.");
        if (datos.ServidorIds is not { Count: > 0 })
            return (false, "Un perfil sin servidores no sirve para desplegar: elige al menos uno.");

        if (await db.DeploymentProfiles.AnyAsync(p => p.Name == nombre && p.Id != (perfilId ?? 0), ct))
            return (false, $"Ya existe un perfil llamado «{nombre}».");

        DeploymentProfile perfil;
        if (perfilId is int id)
        {
            perfil = await db.DeploymentProfiles.FirstOrDefaultAsync(p => p.Id == id, ct)
                     ?? new DeploymentProfile();
            if (perfil.Id == 0) return (false, "Ese perfil ya no existe. Actualiza la lista.");
            // Un perfil interno congelado documenta a dónde fue un despliegue pasado: editarlo
            // reescribiría el historial.
            if (perfil.IsAdHoc) return (false, "Ese destino pertenece a un despliegue ya realizado y no se edita.");
        }
        else
        {
            perfil = new DeploymentProfile { CreatedAt = DateTime.UtcNow };
            db.DeploymentProfiles.Add(perfil);
        }

        perfil.Name = nombre;
        perfil.Description = Limpiar(datos.Descripcion);
        perfil.AllowedForOperaciones = datos.PermitidoParaOperaciones;
        await db.SaveChangesAsync(ct);

        var viejos = await db.DeploymentProfileTargets.Where(pt => pt.ProfileId == perfil.Id).ToListAsync(ct);
        db.DeploymentProfileTargets.RemoveRange(viejos);
        int orden = 0;
        foreach (var servidorId in datos.ServidorIds)
            db.DeploymentProfileTargets.Add(new DeploymentProfileTarget
            {
                ProfileId = perfil.Id,
                TargetId = servidorId,
                Order = orden++
            });
        await db.SaveChangesAsync(ct);

        await bitacora.RecordAsync(perfilId is null ? AuditAction.Create : AuditAction.Update,
            "DeploymentProfile", perfil.Id.ToString(),
            $"{perfil.Name} ({datos.ServidorIds.Count} servidor(es), " +
            $"operaciones: {(perfil.AllowedForOperaciones ? "sí" : "no")})", ct);

        return (true, perfilId is null ? "Perfil creado." : "Perfil actualizado.");
    }

    public async Task<(bool ok, string mensaje)> EliminarPerfilAsync(int perfilId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var perfil = await db.DeploymentProfiles.FirstOrDefaultAsync(p => p.Id == perfilId, ct);
        if (perfil == null) return (false, "Ese perfil ya no existe.");

        // Regla del escritorio: un perfil con historial no se borra, o los despliegues pasados
        // quedarían apuntando a un destino inexistente.
        if (await db.DeploymentJobs.AnyAsync(j => j.DeploymentProfileId == perfilId, ct))
            return (false, "No se puede eliminar: el perfil tiene despliegues en el historial.");

        db.DeploymentProfileTargets.RemoveRange(
            await db.DeploymentProfileTargets.Where(pt => pt.ProfileId == perfilId).ToListAsync(ct));
        db.DeploymentProfiles.Remove(perfil);
        await db.SaveChangesAsync(ct);

        await bitacora.RecordAsync(AuditAction.Delete, "DeploymentProfile", perfilId.ToString(), perfil.Name, ct);
        return (true, "Perfil eliminado.");
    }

    // ── Apoyos ───────────────────────────────────────────────────────────────────

    /// <summary>La carpeta del servidor donde viven los paquetes, o null si no está configurada.</summary>
    public async Task<string?> CarpetaDeDespliegueAsync(CancellationToken ct = default)
    {
        var valor = await configuracion.ObtenerAsync(SettingsService.Claves.DefaultDeployFolder, ct);
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }

    /// <summary>La versión ya se desplegó o está programada, así que su etiqueta es su identificador.</summary>
    public async Task<bool> EtiquetaBloqueadaAsync(int versionId, CancellationToken ct = default) =>
        await db.DeploymentJobs.AnyAsync(j => j.AppReleaseId == versionId, ct)
        || await db.ScheduledDeployments.AnyAsync(s => s.AppReleaseId == versionId, ct);

    private static string Sha256De(string ruta)
    {
        using var sha = SHA256.Create();
        using var flujo = File.OpenRead(ruta);
        return Convert.ToHexString(sha.ComputeHash(flujo)).ToLowerInvariant();
    }

    private static string? Limpiar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
