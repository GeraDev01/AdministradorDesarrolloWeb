using System.Text;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Seguridad;
using AdminWeb.Shared.Dtos.Despliegues;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Lo que la pantalla de despliegues LEE: el catálogo, los servidores, los perfiles y el historial.
///
/// <para>Consulta pura y toda con <c>AsNoTracking</c>. En el escritorio esto era obligatorio por un
/// motivo que aquí ya no aplica —el contexto era Singleton y devolvía instancias viejas de antes de
/// desplegar, así que el historial mentía—; aquí el contexto es por petición, pero sigue siendo lo
/// correcto: nada de esto se va a modificar y rastrearlo solo cuesta.</para>
///
/// <para><b>Ninguna de estas respuestas lleva una contraseña.</b> Es la regla que ordena el archivo
/// entero: <see cref="ServidorDto"/> dice si el servidor TIENE contraseña y si hay que recapturarla,
/// nunca cuál es.</para>
/// </summary>
public class DespliegueQueryService(
    AppDbContext db,
    ICurrentUser quien,
    SettingsService configuracion,
    DespliegueCatalogoService catalogo,
    EjecutorDeDespliegues ejecutor)
{
    /// <summary>Cuántos despliegues trae el historial. Doscientos es lo que traía el escritorio.</summary>
    private const int MaxHistorial = 200;

    // ── Al abrir la pantalla ─────────────────────────────────────────────────────

    /// <summary>Las reglas y los permisos con los que se pinta la pantalla.</summary>
    public async Task<OpcionesDeDesplieguesDto> OpcionesAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(quien);

        // Mismo criterio que el motor: activo salvo que diga expresamente «false». Enseñarlo aquí
        // permite que el checklist avise en voz alta cuando se va a desplegar sin red de seguridad.
        var respaldo = await configuracion.ObtenerAsync(SettingsService.Claves.DeployBackupEnabled, ct);
        bool respaldoActivo = !string.Equals(respaldo, "false", StringComparison.OrdinalIgnoreCase);

        var carpeta = await configuracion.ObtenerAsync(SettingsService.Claves.DefaultDeployFolder, ct);

        return new OpcionesDeDesplieguesDto(
            [.. DeploymentChecklist.Puntos.Select(p => new PuntoDeChecklistDto(p.Clave, p.Texto, p.Ayuda))],
            DeploymentChecklist.MinimoNota,
            respaldoActivo,
            !string.IsNullOrWhiteSpace(carpeta),
            PuedeAdministrarCatalogo: quien.IsAdmin,
            PuedeCrearServidores: DeploymentTargetService.PuedeCrear(quien),
            PuedeEditarServidores: DeploymentTargetService.PuedeEditar(quien));
    }

    // ── Catálogo ─────────────────────────────────────────────────────────────────

    public async Task<List<SistemaDto>> SistemasAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(quien);

        return await db.AppSystems.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new SistemaDto(
                s.Id, s.Name, s.Description, s.DefaultBlobFolder, s.IsActive, s.Releases.Count))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Las versiones de un sistema (o de todos). El <b>changelog viaja siempre</b>, también para
    /// Operaciones: es la información que necesita quien despliega para saber qué va a subir, y esa
    /// fue una decisión explícita del escritorio.
    /// </summary>
    public async Task<List<VersionDto>> VersionesAsync(int? sistemaId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(quien);

        var versiones = await db.AppReleases.AsNoTracking()
            .Include(r => r.AppSystem)
            .Where(r => sistemaId == null || r.AppSystemId == sistemaId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        var ids = versiones.Select(v => v.Id).ToList();
        var desplegadas = await db.DeploymentJobs.AsNoTracking()
            .Where(j => ids.Contains(j.AppReleaseId)).Select(j => j.AppReleaseId).Distinct().ToListAsync(ct);
        var programadas = await db.ScheduledDeployments.AsNoTracking()
            .Where(s => ids.Contains(s.AppReleaseId)).Select(s => s.AppReleaseId).Distinct().ToListAsync(ct);
        var bloqueadas = desplegadas.Concat(programadas).ToHashSet();

        return [.. versiones.Select(r => new VersionDto(
            r.Id, r.AppSystemId, r.AppSystem.Name, r.Version, r.Changelog, r.TargetFolder,
            r.ZipSizeBytes, r.ZipChecksum, r.CreatedAt,
            EtiquetaBloqueada: bloqueadas.Contains(r.Id),
            // Se comprueba de verdad contra el disco del SERVIDOR, que es quien va a desplegar. Es
            // barato y evita descubrir a mitad del despliegue que el paquete ya no está.
            PaqueteDisponible: !string.IsNullOrWhiteSpace(r.ZipLocalPath) && File.Exists(r.ZipLocalPath)))];
    }

    public Task<List<PaqueteDisponibleDto>> PaquetesAsync(CancellationToken ct = default) =>
        catalogo.PaquetesDisponiblesAsync(ct);

    // ── Servidores ───────────────────────────────────────────────────────────────

    public async Task<List<ServidorDto>> ServidoresAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(quien);

        var servidores = await db.DeploymentTargets.AsNoTracking()
            .Include(t => t.LastRelease).ThenInclude(r => r!.AppSystem)
            .OrderBy(t => t.Nombre)
            .ToListAsync(ct);

        // Los nombres, en UNA consulta: con doscientos servidores, resolver el usuario fila por fila
        // son doscientos viajes a una base remota. Misma decisión que el historial del escritorio.
        var usuarios = await NombresDeAsync(
            servidores.Where(t => t.LastDeployedById != null).Select(t => t.LastDeployedById!.Value), ct);

        return [.. servidores.Select(t => new ServidorDto(
            t.Id, t.Nombre, t.Host, t.Puerto, t.Usuario, t.RutaRemota, t.URL, t.IsActive,
            // Ni el valor ni su longitud: solo si hay algo y si el servidor puede leerlo. Una
            // contraseña heredada del cifrado de Windows existe pero es ilegible aquí, y decirlo es
            // la diferencia entre «hay que recapturarla» y un «530» incomprensible al desplegar.
            TieneContrasena: !string.IsNullOrEmpty(t.Contrasena) && ProtectorPortable.EsPortable(t.Contrasena),
            t.LastDeployedAt,
            t.LastRelease != null ? $"{t.LastRelease.AppSystem.Name} {t.LastRelease.Version}" : null,
            t.LastDeployedById is int id && usuarios.TryGetValue(id, out var nombre) ? nombre : null,
            t.LastDeploymentJobId))];
    }

    // ── Perfiles ─────────────────────────────────────────────────────────────────

    public async Task<List<PerfilDto>> PerfilesAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        // Los IsAdHoc quedan fuera: son la foto congelada del destino de un despliegue concreto, no
        // perfiles que nadie vaya a reutilizar.
        var perfiles = await db.DeploymentProfiles.AsNoTracking()
            .Where(p => !p.IsAdHoc)
            .Include(p => p.ProfileTargets).ThenInclude(pt => pt.Target)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var conHistorial = await db.DeploymentJobs.AsNoTracking()
            .Select(j => j.DeploymentProfileId).Distinct().ToListAsync(ct);

        return [.. perfiles.Select(p =>
        {
            var ordenados = p.ProfileTargets.OrderBy(pt => pt.Order).ToList();
            return new PerfilDto(
                p.Id, p.Name, p.Description, p.AllowedForOperaciones,
                [.. ordenados.Select(pt => pt.TargetId)],
                string.Join(", ", ordenados.Select(pt => pt.Target.Nombre)),
                conHistorial.Contains(p.Id));
        })];
    }

    // ── En curso ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Los despliegues que el servidor tiene entre manos. Es lo que permite <b>retomar la vista</b>
    /// al volver a abrir la pantalla: en el escritorio, cerrar la aplicación mataba el despliegue y
    /// no había nada que retomar.
    /// </summary>
    public IReadOnlyList<TrabajoDeDespliegueDto> EnCurso()
    {
        AuthorizationGuard.RequireAdminOrOperaciones(quien);
        return ejecutor.EnCurso(quien.UserId);
    }

    public TrabajoDeDespliegueDto? Trabajo(int jobId)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(quien);
        return ejecutor.Trabajo(jobId, quien.UserId);
    }

    // ── Historial ────────────────────────────────────────────────────────────────

    public async Task<List<DespliegueDelHistorialDto>> HistorialAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(quien);

        var trabajos = await db.DeploymentJobs.AsNoTracking()
            .Include(j => j.AppRelease).ThenInclude(r => r.AppSystem)
            .Include(j => j.Profile)
            .OrderByDescending(j => j.CreatedAt)
            .Take(MaxHistorial)
            .ToListAsync(ct);

        var usuarios = await NombresDeAsync(
            trabajos.Where(j => j.StartedById != null).Select(j => j.StartedById!.Value), ct);

        return [.. trabajos.Select(j => ACabecera(j, usuarios))];
    }

    /// <summary>El expediente de un despliegue: sus datos, el checklist confirmado y el log completo.</summary>
    public async Task<ExpedienteDeDespliegueDto?> ExpedienteAsync(int jobId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(quien);

        var trabajo = await db.DeploymentJobs.AsNoTracking()
            .Include(j => j.AppRelease).ThenInclude(r => r.AppSystem)
            .Include(j => j.Profile)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (trabajo == null) return null;

        var usuarios = await NombresDeAsync(
            trabajo.StartedById is int id ? [id] : [], ct);

        // La SEÑAL DE VIDA se queda fuera, y no es un filtro cosmético. Esa fila no es un renglón de
        // bitácora: es la reserva que un despliegue en curso mantiene sobre sus servidores, guardada
        // en esta tabla porque no había otra donde no estorbara (ver SenalDeVidaDelDespliegue). Mide
        // «alguien lo está corriendo», no «pasó esto», y este método alimenta también la EVIDENCIA
        // descargable — el .txt que se entrega cuando alguien pregunta qué pasó, y que se abre en un
        // equipo del que no sabemos nada. Un apunte interno ahí dentro no explica nada y hay que
        // explicarlo.
        //
        // Se filtra por el texto EXACTO de la marca a propósito: cuando la reconciliación cierra un
        // despliegue interrumpido REESCRIBE esa misma fila con lo que pasó de verdad, y entonces deja
        // de coincidir con la marca y sí sale — que es justo lo que tiene que salir.
        var renglones = await db.DeploymentLogEntries.AsNoTracking()
            .Where(l => l.JobId == jobId && l.Message != SenalDeVidaDelDespliegue.Marca)
            .OrderBy(l => l.Timestamp)
            .Select(l => new RenglonDeBitacoraDto(l.Timestamp, l.TargetName, l.Message, l.Level))
            .ToListAsync(ct);

        return new ExpedienteDeDespliegueDto(ACabecera(trabajo, usuarios), trabajo.Notes, renglones);
    }

    /// <summary>
    /// La evidencia de UN despliegue en texto plano, tal como se entrega cuando alguien pregunta qué
    /// pasó: legible sin la aplicación y sin hoja de cálculo. Es el mismo documento del escritorio.
    /// </summary>
    public async Task<(string nombre, string contenido)?> EvidenciaAsync(int jobId, CancellationToken ct = default)
    {
        var expediente = await ExpedienteAsync(jobId, ct);
        if (expediente == null) return null;

        var c = expediente.Cabecera;
        var sb = new StringBuilder();
        sb.AppendLine("EVIDENCIA DE DESPLIEGUE");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Despliegue #  : {c.JobId}");
        sb.AppendLine($"Sistema       : {c.Sistema}");
        sb.AppendLine($"Versión       : {c.Version}");
        sb.AppendLine($"Destino       : {c.Destino}");
        sb.AppendLine($"Lanzado por   : {c.QuienLoLanzo}");
        sb.AppendLine($"Inicio        : {c.InicioUtc?.ToLocalTime():dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"Fin           : {c.FinUtc?.ToLocalTime():dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"Resultado     : {c.EstadoTexto}  —  {c.Ok} correcto(s), {c.Fallidos} fallido(s) de {c.Total}");
        sb.AppendLine();
        // No se disimula: un despliegue anterior al checklist no tiene evidencia, y decirlo vale más
        // que un espacio en blanco que parece un descuido.
        sb.AppendLine(string.IsNullOrWhiteSpace(expediente.Checklist)
            ? "(Este despliegue es anterior al checklist previo: no hay confirmación registrada.)"
            : expediente.Checklist);
        sb.AppendLine();
        sb.AppendLine("LOG COMPLETO");
        sb.AppendLine(new string('-', 60));
        foreach (var r in expediente.Bitacora)
            sb.AppendLine($"[{r.Utc.ToLocalTime():dd/MM HH:mm:ss}] " +
                          $"{(r.Nivel == DeployLogLevel.Info ? "" : r.Nivel.ToString().ToUpperInvariant() + " ")}" +
                          $"{(r.Servidor != null ? $"[{r.Servidor}] " : "")}{r.Mensaje}");

        var nombre = $"Despliegue_{c.JobId}_{Archivable(c.Version)}_{c.InicioUtc?.ToLocalTime():yyyyMMdd}.txt";
        return (nombre, sb.ToString());
    }

    // ── Apoyos ───────────────────────────────────────────────────────────────────

    private static DespliegueDelHistorialDto ACabecera(
        Domain.Entities.DeploymentJob j, IReadOnlyDictionary<int, string> usuarios) =>
        new(j.Id,
            j.AppRelease.AppSystem.Name,
            j.AppRelease.Version,
            j.Profile.Name,
            j.Status,
            TextosDeDespliegue.Estado(j.Status),
            j.TargetsOk, j.TargetsFailed, j.TargetsTotal,
            j.StartedAt, j.CompletedAt,
            j.StartedAt != null && j.CompletedAt != null
                ? Math.Round((j.CompletedAt.Value - j.StartedAt.Value).TotalSeconds, 1)
                : null,
            j.StartedById is int id && usuarios.TryGetValue(id, out var nombre) ? nombre : "—",
            !string.IsNullOrWhiteSpace(j.Notes));

    private async Task<Dictionary<int, string>> NombresDeAsync(IEnumerable<int> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return (await db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.Username })
                .ToListAsync(ct))
            .ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName);
    }

    private static string Archivable(string texto) =>
        new([.. texto.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_')]);
}
