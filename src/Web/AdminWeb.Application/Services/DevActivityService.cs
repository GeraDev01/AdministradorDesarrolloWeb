using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Actividades libres del desarrollador: trabajo real fuera de sus requerimientos asignados.
/// Mismo criterio que <see cref="VacationRequestService"/>: las reglas y la guarda de pertenencia
/// viven aquí, no en la pantalla.
/// </summary>
public class DevActivityService(AppDbContext db, ICurrentUser currentUser, AuditService audit, WorkSessionService work)
{
    public async Task<List<DevActivity>> DeDesarrolladorAsync(int developerId, bool incluirCerradas = true,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, developerId);
        var q = db.DevActivities.Where(a => a.DeveloperId == developerId);
        if (!incluirCerradas) q = q.Where(a => a.Status == DevActivityStatus.Abierta);
        return await q.OrderByDescending(a => a.Status == DevActivityStatus.Abierta)
                      .ThenByDescending(a => a.CreatedAt)
                      .AsNoTracking()
                      .ToListAsync(ct);
    }

    /// <summary>
    /// Todas las actividades del equipo, para la vista del administrador. Requiere rol Admin:
    /// aquí sí se ve el trabajo de todos, a diferencia de <see cref="DeDesarrolladorAsync"/>.
    /// </summary>
    public async Task<List<DevActivity>> TodasParaAdministradorAsync(int? developerId = null,
        DevActivityStatus? estado = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var q = db.DevActivities.Include(a => a.Developer).AsQueryable();
        if (developerId is int dev) q = q.Where(a => a.DeveloperId == dev);
        if (estado is DevActivityStatus e) q = q.Where(a => a.Status == e);

        return await q.OrderByDescending(a => a.Status == DevActivityStatus.Abierta)
                      .ThenByDescending(a => a.CreatedAt)
                      .AsNoTracking()
                      .ToListAsync(ct);
    }

    /// <summary>
    /// Las actividades del equipo en una hoja de cálculo, con su tiempo cronometrado.
    ///
    /// <para>Se exporta <b>lo que el filtro está enseñando</b> —persona y estado—, que es la decisión
    /// que ya se tomó en minutas: el botón vive junto a esos desplegables y bajar la tabla entera
    /// sorprende a quien acaba de acotar.</para>
    ///
    /// <para>El tiempo se resuelve en UNA consulta y no actividad por actividad como hace la pantalla:
    /// ahí son veintitantas filas visibles, aquí son todas las del histórico, y un viaje a la base por
    /// fila convertiría el botón en una espera de minutos.</para>
    /// </summary>
    public async Task<byte[]> ExcelDelEquipoAsync(int? developerId = null,
        DevActivityStatus? estado = null, CancellationToken ct = default)
    {
        // La guarda de administrador y el orden los pone TodasParaAdministradorAsync: bajar lo mismo
        // que se ve incluye el orden en que se ve.
        var actividades = await TodasParaAdministradorAsync(developerId, estado, ct);
        var ids = actividades.Select(a => a.Id).ToList();

        var evidencias = await ConteoEvidenciasAsync(ids, ct);
        var segundos = await SegundosPorActividadAsync(ids, ct);

        var filas = actividades.Select(a => new object?[]
        {
            a.Developer?.FullName ?? "—",
            a.Title,
            Etiqueta(a.Status),
            a.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
            a.ClosedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
            WorkSessionService.Format(segundos.GetValueOrDefault(a.Id)),
            // Los segundos van además del texto porque son con lo que se puede sumar y ordenar en la
            // hoja: «1h 05m 00s» ordenado alfabéticamente pone «59m» por encima de «2h».
            segundos.GetValueOrDefault(a.Id),
            evidencias.GetValueOrDefault(a.Id),
            a.Description
        }).ToList();

        return HojaDeCalculo.Escribir(
            ["Desarrollador", "Actividad", "Estado", "Creada", "Cerrada", "Tiempo", "Segundos",
             "Evidencias", "Descripción"],
            filas, "Actividades");
    }

    /// <summary>
    /// Segundos cronometrados de cada actividad, en una sola consulta.
    ///
    /// El tramo en curso cuenta, igual que en <see cref="WorkSessionService.GetTotalSecondsByActivityAsync"/>:
    /// una actividad con el cronómetro corriendo no puede salir con menos tiempo del que lleva.
    /// </summary>
    private async Task<Dictionary<int, int>> SegundosPorActividadAsync(
        IReadOnlyCollection<int> activityIds, CancellationToken ct)
    {
        if (activityIds.Count == 0) return [];

        var ahora = DateTime.UtcNow;
        var sesiones = await db.WorkSessions.AsNoTracking()
            .Where(w => w.ActivityId != null && activityIds.Contains(w.ActivityId.Value))
            .ToListAsync(ct);

        return sesiones
            .GroupBy(w => w.ActivityId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(w => w.LiveSeconds(ahora)));
    }

    /// <summary>
    /// Etiqueta del estado de una actividad, con la PALABRA SOLA.
    ///
    /// <para>Vive aquí y no en la pantalla ni en el endpoint porque la escriben los dos —la rejilla y
    /// la exportación— y dos copias del mismo texto acaban diciendo cosas distintas. El COLOR sí lo
    /// pone la pantalla: un servicio no debe saber del tema visual.</para>
    ///
    /// <para><b>Llevaba delante un círculo (🟢 / ⚪) y por aquí es por donde más urgía quitarlo.</b>
    /// Ese círculo lo dibuja EL SISTEMA OPERATIVO, no nosotros: sale distinto en cada equipo, NO
    /// hereda el color del texto y donde no hay fuente de emoji instalada sale como un CUADRO VACÍO
    /// —comprobado en una captura, no es teoría—. Y esta etiqueta no se queda en nuestra pantalla:
    /// <see cref="ExcelDelEquipoAsync"/> la escribe en la columna «Estado» de un .xlsx que se manda
    /// por correo y se abre en una máquina de la que no sabemos nada. Ahí no hay tema que arreglarlo
    /// ni captura que nos avise: quien recibe el libro ve un cuadro y ya. La palabra sola se lee y se
    /// filtra igual en cualquier Excel.</para>
    ///
    /// <para>Nadie compara esta cadena por igualdad —el filtro de la pantalla y la consulta van por
    /// <see cref="DevActivityStatus"/>, que viaja en el DTO al lado de este texto—, así que quitarle
    /// el círculo no descasa ningún switch. Si alguien necesita decidir por el estado, que use el
    /// enum; comparar la palabra volvería a atar el color a la ortografía.</para>
    /// </summary>
    public static string Etiqueta(DevActivityStatus estado) =>
        estado == DevActivityStatus.Abierta ? "Abierta" : "Cerrada";

    /// <summary>Sesiones de cronómetro de una actividad, para ver cómo se acumuló el tiempo.</summary>
    public async Task<List<WorkSession>> SesionesDeAsync(int activityId, CancellationToken ct = default)
    {
        var a = await db.DevActivities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == activityId, ct);
        if (a == null) return [];
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, a.DeveloperId);

        return await db.WorkSessions
            .Where(w => w.ActivityId == activityId)
            .OrderBy(w => w.StartedAt)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<(bool ok, string mensaje, DevActivity? actividad)> CrearAsync(
        int developerId, string titulo, string? descripcion, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, developerId);

        titulo = (titulo ?? "").Trim();
        if (titulo.Length == 0) return (false, "Escribe un título para la actividad.", null);
        if (titulo.Length > 200) return (false, "El título no puede pasar de 200 caracteres.", null);

        var a = new DevActivity
        {
            DeveloperId = developerId,
            Title = titulo,
            Description = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim(),
            Status = DevActivityStatus.Abierta,
            CreatedAt = DateTime.UtcNow
        };
        db.DevActivities.Add(a);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Create, "DevActivity", a.Id.ToString(), $"Actividad libre: {a.Title}", ct);
        return (true, "Actividad creada.", a);
    }

    public async Task<(bool ok, string mensaje)> RenombrarAsync(int activityId, string titulo, string? descripcion,
        CancellationToken ct = default)
    {
        var (a, error) = await ObtenerPropiaAsync(activityId, ct);
        if (a == null) return (false, error!);
        if (a.Status == DevActivityStatus.Cerrada) return (false, "La actividad está cerrada. Reábrela para editarla.");

        titulo = (titulo ?? "").Trim();
        if (titulo.Length == 0) return (false, "Escribe un título para la actividad.");
        if (titulo.Length > 200) return (false, "El título no puede pasar de 200 caracteres.");

        a.Title = titulo;
        a.Description = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "DevActivity", a.Id.ToString(), $"Actividad renombrada: {a.Title}", ct);
        return (true, "Actividad actualizada.");
    }

    /// <summary>Cierra la actividad. Si tenía el cronómetro corriendo, lo detiene consolidando el tiempo.</summary>
    public async Task<(bool ok, string mensaje)> CerrarAsync(int activityId, CancellationToken ct = default)
    {
        var (a, error) = await ObtenerPropiaAsync(activityId, ct);
        if (a == null) return (false, error!);
        if (a.Status == DevActivityStatus.Cerrada) return (false, "La actividad ya estaba cerrada.");

        // Cerrar sin detener dejaría una sesión abierta acumulando tiempo de forma invisible.
        await work.StopAsync(a.DeveloperId, WorkTarget.Actividad(a.Id), ct);

        a.Status = DevActivityStatus.Cerrada;
        a.ClosedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "DevActivity", a.Id.ToString(),
            $"Actividad cerrada: {a.Title} (total {WorkSessionService.Format(await work.GetTotalSecondsByActivityAsync(a.Id, ct))})", ct);
        return (true, "Actividad cerrada.");
    }

    public async Task<(bool ok, string mensaje)> ReabrirAsync(int activityId, CancellationToken ct = default)
    {
        var (a, error) = await ObtenerPropiaAsync(activityId, ct);
        if (a == null) return (false, error!);
        if (a.Status == DevActivityStatus.Abierta) return (false, "La actividad ya estaba abierta.");

        a.Status = DevActivityStatus.Abierta;
        a.ClosedAt = null;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "DevActivity", a.Id.ToString(), $"Actividad reabierta: {a.Title}", ct);
        return (true, "Actividad reabierta.");
    }

    /// <summary>
    /// Elimina la actividad. Se rehúsa si ya tiene tiempo registrado: ese tiempo es evidencia de
    /// trabajo hecho y borrarlo en silencio falsearía el total del desarrollador. En ese caso se
    /// cierra, no se borra.
    /// </summary>
    public async Task<(bool ok, string mensaje)> EliminarAsync(int activityId, CancellationToken ct = default)
    {
        var (a, error) = await ObtenerPropiaAsync(activityId, ct);
        if (a == null) return (false, error!);

        int segundos = await work.GetTotalSecondsByActivityAsync(a.Id, ct);
        if (segundos > 0)
            return (false,
                $"La actividad tiene {WorkSessionService.Format(segundos)} de tiempo registrado y no se puede eliminar. " +
                "Ciérrala si ya terminaste.");

        var titulo = a.Title;
        db.DevActivities.Remove(a);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Delete, "DevActivity", activityId.ToString(),
            $"Actividad eliminada (sin tiempo): {titulo}", ct);
        return (true, "Actividad eliminada.");
    }

    private async Task<(DevActivity? a, string? error)> ObtenerPropiaAsync(int activityId, CancellationToken ct)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        var a = await db.DevActivities.FirstOrDefaultAsync(x => x.Id == activityId, ct);
        if (a == null) return (null, "La actividad ya no existe. Actualiza la lista.");
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, a.DeveloperId);
        return (a, null);
    }

    // ── Evidencia adjunta ────────────────────────────────────────────────────

    /// <summary>Tope por archivo. Mismo criterio que el resto de adjuntos de la aplicación.</summary>
    public const int MaxEvidenciaBytes = 15 * 1024 * 1024;

    /// <summary>
    /// Más allá de esto deja de ser evidencia y pasa a ser un repositorio. La base es compartida y
    /// se lee por red: veinte archivos de 15 MB colgando de una actividad los paga todo el equipo.
    /// </summary>
    public const int MaxEvidenciasPorActividad = 20;

    /// <summary>
    /// Extensiones que NO se aceptan. La evidencia acaba volcada a un archivo temporal que se abre
    /// con el programa asociado: permitir un ejecutable convertiría «ver la evidencia» en
    /// «ejecutar lo que subió otro». En la web el temporal lo crea la descarga del navegador, en la
    /// máquina de quien la abre: el mismo riesgo, en otra carpeta.
    /// </summary>
    private static readonly HashSet<string> ExtensionesProhibidas = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".com", ".scr", ".msi", ".bat", ".cmd", ".ps1", ".psm1",
        ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".jar", ".lnk", ".reg", ".cpl"
    };

    /// <summary>Evidencia de una actividad, SIN los bytes: es lo que necesita una lista.</summary>
    public async Task<List<DevActivityAttachment>> EvidenciasDeAsync(int activityId, CancellationToken ct = default)
    {
        var a = await db.DevActivities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == activityId, ct);
        if (a == null) return [];
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, a.DeveloperId);

        // La proyección anónima es la que garantiza que el BLOB NO entre en el SELECT; el mapeo a
        // la entidad se hace ya en memoria (un árbol de expresión tampoco admite «Bytes = []»).
        return (await db.DevActivityAttachments
            .Where(x => x.ActivityId == activityId)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id, x.ActivityId, x.FileName, x.ContentType,
                x.SizeBytes, x.Description, x.UploadedByUserId, x.CreatedAtUtc
            })
            .ToListAsync(ct))
            .Select(x => new DevActivityAttachment
            {
                Id = x.Id, ActivityId = x.ActivityId, FileName = x.FileName, ContentType = x.ContentType,
                SizeBytes = x.SizeBytes, Description = x.Description,
                UploadedByUserId = x.UploadedByUserId, CreatedAtUtc = x.CreatedAtUtc,
                Bytes = []   // el contenido solo viaja al abrirlo (ver BytesDeEvidenciaAsync)
            })
            .ToList();
    }

    /// <summary>Cuántas evidencias tiene cada actividad, sin traer contenido. Para las rejillas.</summary>
    public async Task<Dictionary<int, int>> ConteoEvidenciasAsync(IEnumerable<int> activityIds,
        CancellationToken ct = default)
    {
        var ids = activityIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return (await db.DevActivityAttachments
            .Where(x => ids.Contains(x.ActivityId))
            .GroupBy(x => x.ActivityId)
            .Select(g => new { g.Key, Cuantas = g.Count() })
            .AsNoTracking()
            .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Cuantas);
    }

    /// <summary>
    /// El contenido de una evidencia. Devuelve los bytes y con qué nombre y tipo entregarlos; quien
    /// llama decide qué hacer con ellos — en la web, el endpoint los sirve como descarga. Aquí no se
    /// abre ningún archivo ni se toca el disco.
    /// </summary>
    public async Task<(byte[] bytes, string nombre, string tipo)> BytesDeEvidenciaAsync(int attachmentId,
        CancellationToken ct = default)
    {
        var adj = await db.DevActivityAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId, ct);
        if (adj == null) return ([], "", "");

        var a = await db.DevActivities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == adj.ActivityId, ct);
        if (a == null) return ([], "", "");
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, a.DeveloperId);

        return (adj.Bytes, NombreSeguro(adj.FileName), adj.ContentType);
    }

    /// <summary>
    /// Adjunta una captura o un documento que justifica la actividad. Solo su dueño (o un
    /// administrador) y solo mientras la actividad siga abierta: cerrada es evidencia consolidada,
    /// mismo criterio que <see cref="RenombrarAsync"/>.
    /// </summary>
    public async Task<(bool ok, string mensaje, DevActivityAttachment? evidencia)> AgregarEvidenciaAsync(
        int activityId, string nombreArchivo, byte[] bytes, string? descripcion = null, CancellationToken ct = default)
    {
        var (a, error) = await ObtenerPropiaAsync(activityId, ct);
        if (a == null) return (false, error!, null);
        if (a.Status == DevActivityStatus.Cerrada)
            return (false, "La actividad está cerrada. Reábrela para agregarle evidencia.", null);

        if (bytes == null || bytes.Length == 0) return (false, "El archivo está vacío.", null);
        if (bytes.Length > MaxEvidenciaBytes)
            return (false, $"El archivo supera {MaxEvidenciaBytes / (1024 * 1024)} MB.", null);

        int yaHay = await db.DevActivityAttachments.CountAsync(x => x.ActivityId == activityId, ct);
        if (yaHay >= MaxEvidenciasPorActividad)
            return (false, $"Esta actividad ya tiene {MaxEvidenciasPorActividad} evidencias, que es el máximo.", null);

        var nombre = NombreSeguro(nombreArchivo);
        var ext = Path.GetExtension(nombre);
        if (ExtensionesProhibidas.Contains(ext))
            return (false, $"No se admiten archivos «{ext}». Si necesitas compartir algo así, comprímelo o enlázalo.", null);

        var evidencia = new DevActivityAttachment
        {
            ActivityId = activityId,
            FileName = nombre,
            ContentType = TipoPorContenido(bytes, ext),
            Bytes = bytes,
            SizeBytes = bytes.LongLength,
            Description = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim(),
            UploadedByUserId = currentUser.UserId ?? 0,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.DevActivityAttachments.Add(evidencia);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Create, "DevActivityAttachment", evidencia.Id.ToString(),
            $"Evidencia adjuntada a la actividad «{a.Title}»: {nombre} ({bytes.Length / 1024} KB)", ct);
        return (true, "Evidencia adjuntada.", evidencia);
    }

    public async Task<(bool ok, string mensaje)> EliminarEvidenciaAsync(int attachmentId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var adj = await db.DevActivityAttachments.FirstOrDefaultAsync(x => x.Id == attachmentId, ct);
        if (adj == null) return (false, "Esa evidencia ya no existe. Actualiza la lista.");

        var (a, error) = await ObtenerPropiaAsync(adj.ActivityId, ct);
        if (a == null) return (false, error!);
        if (a.Status == DevActivityStatus.Cerrada && !currentUser.IsAdmin)
            return (false, "La actividad está cerrada. Reábrela si necesitas cambiar su evidencia.");

        var nombre = adj.FileName;
        db.DevActivityAttachments.Remove(adj);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Delete, "DevActivityAttachment", attachmentId.ToString(),
            $"Evidencia eliminada de la actividad «{a.Title}»: {nombre}", ct);
        return (true, "Evidencia eliminada.");
    }

    /// <summary>
    /// Tipo real por los BYTES para las imágenes conocidas; para lo demás, por extensión. Se decide
    /// por contenido y no por el nombre porque el nombre lo pone quien sube el archivo.
    /// </summary>
    private static string TipoPorContenido(byte[] b, string ext)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length >= 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46) return "image/gif";
        if (b.Length >= 2 && b[0] == 0x42 && b[1] == 0x4D) return "image/bmp";
        if (b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46) return "application/pdf";

        return ext.ToLowerInvariant() switch
        {
            ".pdf"  => "application/pdf",
            ".doc"  => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls"  => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".txt"  => "text/plain",
            ".csv"  => "text/csv",
            _       => "application/octet-stream"
        };
    }

    /// <summary>
    /// El nombre lo eligió quien subió el archivo: se limpia antes de que viaje en la cabecera
    /// <c>Content-Disposition</c> y acabe en el disco de quien lo descarga. La limpieza es la de
    /// <see cref="ArchivosSubidos.NombreSeguro"/>, común a todo lo que se sube.
    /// </summary>
    public static string NombreSeguro(string? nombre)
    {
        var n = ArchivosSubidos.NombreSeguro(nombre);
        return n.Length == 0 ? "evidencia" : n;
    }
}
