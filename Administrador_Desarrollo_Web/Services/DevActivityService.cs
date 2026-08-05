using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Actividades libres del desarrollador: trabajo real fuera de sus requerimientos asignados.
/// Mismo criterio que <see cref="VacationRequestService"/>: las reglas y la guarda de pertenencia
/// viven aquí, no en la pantalla.
/// </summary>
public class DevActivityService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly AuditService _audit;
    private readonly WorkSessionService _work;

    public DevActivityService(AppDbContext db, ICurrentUser currentUser, AuditService audit, WorkSessionService work)
    {
        _db = db; _currentUser = currentUser; _audit = audit; _work = work;
    }

    public List<DevActivity> DeDesarrollador(int developerId, bool incluirCerradas = true)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, developerId);
        var q = _db.DevActivities.Where(a => a.DeveloperId == developerId);
        if (!incluirCerradas) q = q.Where(a => a.Status == DevActivityStatus.Abierta);
        return q.OrderByDescending(a => a.Status == DevActivityStatus.Abierta)
                .ThenByDescending(a => a.CreatedAt)
                .AsNoTracking()
                .ToList();
    }

    /// <summary>
    /// Todas las actividades del equipo, para la vista del administrador. Requiere rol Admin:
    /// aquí sí se ve el trabajo de todos, a diferencia de <see cref="DeDesarrollador"/>.
    /// </summary>
    public List<DevActivity> TodasParaAdministrador(int? developerId = null, DevActivityStatus? estado = null)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var q = _db.DevActivities.Include(a => a.Developer).AsQueryable();
        if (developerId is int dev) q = q.Where(a => a.DeveloperId == dev);
        if (estado is DevActivityStatus e) q = q.Where(a => a.Status == e);

        return q.OrderByDescending(a => a.Status == DevActivityStatus.Abierta)
                .ThenByDescending(a => a.CreatedAt)
                .AsNoTracking()
                .ToList();
    }

    /// <summary>Sesiones de cronómetro de una actividad, para ver cómo se acumuló el tiempo.</summary>
    public List<WorkSession> SesionesDe(int activityId)
    {
        var a = _db.DevActivities.AsNoTracking().FirstOrDefault(x => x.Id == activityId);
        if (a == null) return [];
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, a.DeveloperId);

        return _db.WorkSessions
            .Where(w => w.ActivityId == activityId)
            .OrderBy(w => w.StartedAt)
            .AsNoTracking()
            .ToList();
    }

    public (bool ok, string mensaje, DevActivity? actividad) Crear(int developerId, string titulo, string? descripcion)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, developerId);

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
        _db.DevActivities.Add(a);
        _db.SaveChanges();
        _audit.Record(AuditAction.Create, "DevActivity", a.Id.ToString(), $"Actividad libre: {a.Title}");
        return (true, "Actividad creada.", a);
    }

    public (bool ok, string mensaje) Renombrar(int activityId, string titulo, string? descripcion)
    {
        var (a, error) = ObtenerPropia(activityId);
        if (a == null) return (false, error!);
        if (a.Status == DevActivityStatus.Cerrada) return (false, "La actividad está cerrada. Reábrela para editarla.");

        titulo = (titulo ?? "").Trim();
        if (titulo.Length == 0) return (false, "Escribe un título para la actividad.");
        if (titulo.Length > 200) return (false, "El título no puede pasar de 200 caracteres.");

        a.Title = titulo;
        a.Description = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "DevActivity", a.Id.ToString(), $"Actividad renombrada: {a.Title}");
        return (true, "Actividad actualizada.");
    }

    /// <summary>Cierra la actividad. Si tenía el cronómetro corriendo, lo detiene consolidando el tiempo.</summary>
    public (bool ok, string mensaje) Cerrar(int activityId)
    {
        var (a, error) = ObtenerPropia(activityId);
        if (a == null) return (false, error!);
        if (a.Status == DevActivityStatus.Cerrada) return (false, "La actividad ya estaba cerrada.");

        // Cerrar sin detener dejaría una sesión abierta acumulando tiempo de forma invisible.
        _work.Stop(a.DeveloperId, WorkTarget.Actividad(a.Id));

        a.Status = DevActivityStatus.Cerrada;
        a.ClosedAt = DateTime.UtcNow;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "DevActivity", a.Id.ToString(),
            $"Actividad cerrada: {a.Title} (total {WorkSessionService.Format(_work.GetTotalSecondsByActivity(a.Id))})");
        return (true, "Actividad cerrada.");
    }

    public (bool ok, string mensaje) Reabrir(int activityId)
    {
        var (a, error) = ObtenerPropia(activityId);
        if (a == null) return (false, error!);
        if (a.Status == DevActivityStatus.Abierta) return (false, "La actividad ya estaba abierta.");

        a.Status = DevActivityStatus.Abierta;
        a.ClosedAt = null;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "DevActivity", a.Id.ToString(), $"Actividad reabierta: {a.Title}");
        return (true, "Actividad reabierta.");
    }

    /// <summary>
    /// Elimina la actividad. Se rehúsa si ya tiene tiempo registrado: ese tiempo es evidencia de
    /// trabajo hecho y borrarlo en silencio falsearía el total del desarrollador. En ese caso se
    /// cierra, no se borra.
    /// </summary>
    public (bool ok, string mensaje) Eliminar(int activityId)
    {
        var (a, error) = ObtenerPropia(activityId);
        if (a == null) return (false, error!);

        int segundos = _work.GetTotalSecondsByActivity(a.Id);
        if (segundos > 0)
            return (false,
                $"La actividad tiene {WorkSessionService.Format(segundos)} de tiempo registrado y no se puede eliminar. " +
                "Ciérrala si ya terminaste.");

        var titulo = a.Title;
        _db.DevActivities.Remove(a);
        _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "DevActivity", activityId.ToString(), $"Actividad eliminada (sin tiempo): {titulo}");
        return (true, "Actividad eliminada.");
    }

    private (DevActivity? a, string? error) ObtenerPropia(int activityId)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        var a = _db.DevActivities.FirstOrDefault(x => x.Id == activityId);
        if (a == null) return (null, "La actividad ya no existe. Actualiza la lista.");
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, a.DeveloperId);
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
    /// «ejecutar lo que subió otro».
    /// </summary>
    private static readonly HashSet<string> ExtensionesProhibidas = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".com", ".scr", ".msi", ".bat", ".cmd", ".ps1", ".psm1",
        ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".jar", ".lnk", ".reg", ".cpl"
    };

    /// <summary>Evidencia de una actividad, SIN los bytes: es lo que necesita una lista.</summary>
    public List<DevActivityAttachment> EvidenciasDe(int activityId)
    {
        var a = _db.DevActivities.AsNoTracking().FirstOrDefault(x => x.Id == activityId);
        if (a == null) return [];
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, a.DeveloperId);

        // La proyección anónima es la que garantiza que el BLOB NO entre en el SELECT; el mapeo a
        // la entidad se hace ya en memoria (un árbol de expresión tampoco admite «Bytes = []»).
        return _db.DevActivityAttachments
            .Where(x => x.ActivityId == activityId)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id, x.ActivityId, x.FileName, x.ContentType,
                x.SizeBytes, x.Description, x.UploadedByUserId, x.CreatedAtUtc
            })
            .ToList()
            .Select(x => new DevActivityAttachment
            {
                Id = x.Id, ActivityId = x.ActivityId, FileName = x.FileName, ContentType = x.ContentType,
                SizeBytes = x.SizeBytes, Description = x.Description,
                UploadedByUserId = x.UploadedByUserId, CreatedAtUtc = x.CreatedAtUtc,
                Bytes = []   // el contenido solo viaja al abrirlo (ver BytesDeEvidencia)
            })
            .ToList();
    }

    /// <summary>Cuántas evidencias tiene cada actividad, sin traer contenido. Para las rejillas.</summary>
    public Dictionary<int, int> ConteoEvidencias(IEnumerable<int> activityIds)
    {
        var ids = activityIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return _db.DevActivityAttachments
            .Where(x => ids.Contains(x.ActivityId))
            .GroupBy(x => x.ActivityId)
            .Select(g => new { g.Key, Cuantas = g.Count() })
            .AsNoTracking()
            .ToDictionary(x => x.Key, x => x.Cuantas);
    }

    /// <summary>El contenido de una evidencia, para abrirla o guardarla.</summary>
    public (byte[] bytes, string nombre, string tipo) BytesDeEvidencia(int attachmentId)
    {
        var adj = _db.DevActivityAttachments.AsNoTracking().FirstOrDefault(x => x.Id == attachmentId);
        if (adj == null) return ([], "", "");

        var a = _db.DevActivities.AsNoTracking().FirstOrDefault(x => x.Id == adj.ActivityId);
        if (a == null) return ([], "", "");
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, a.DeveloperId);

        return (adj.Bytes, NombreSeguro(adj.FileName), adj.ContentType);
    }

    /// <summary>
    /// Adjunta una captura o un documento que justifica la actividad. Solo su dueño (o un
    /// administrador) y solo mientras la actividad siga abierta: cerrada es evidencia consolidada,
    /// mismo criterio que <see cref="Renombrar"/>.
    /// </summary>
    public (bool ok, string mensaje, DevActivityAttachment? evidencia) AgregarEvidencia(
        int activityId, string nombreArchivo, byte[] bytes, string? descripcion = null)
    {
        var (a, error) = ObtenerPropia(activityId);
        if (a == null) return (false, error!, null);
        if (a.Status == DevActivityStatus.Cerrada)
            return (false, "La actividad está cerrada. Reábrela para agregarle evidencia.", null);

        if (bytes == null || bytes.Length == 0) return (false, "El archivo está vacío.", null);
        if (bytes.Length > MaxEvidenciaBytes)
            return (false, $"El archivo supera {MaxEvidenciaBytes / (1024 * 1024)} MB.", null);

        int yaHay = _db.DevActivityAttachments.Count(x => x.ActivityId == activityId);
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
            UploadedByUserId = _currentUser.UserId ?? 0,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.DevActivityAttachments.Add(evidencia);
        _db.SaveChanges();

        _audit.Record(AuditAction.Create, "DevActivityAttachment", evidencia.Id.ToString(),
            $"Evidencia adjuntada a la actividad «{a.Title}»: {nombre} ({bytes.Length / 1024} KB)");
        return (true, "Evidencia adjuntada.", evidencia);
    }

    public (bool ok, string mensaje) EliminarEvidencia(int attachmentId)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);

        var adj = _db.DevActivityAttachments.FirstOrDefault(x => x.Id == attachmentId);
        if (adj == null) return (false, "Esa evidencia ya no existe. Actualiza la lista.");

        var (a, error) = ObtenerPropia(adj.ActivityId);
        if (a == null) return (false, error!);
        if (a.Status == DevActivityStatus.Cerrada && !_currentUser.IsAdmin)
            return (false, "La actividad está cerrada. Reábrela si necesitas cambiar su evidencia.");

        var nombre = adj.FileName;
        _db.DevActivityAttachments.Remove(adj);
        _db.SaveChanges();

        _audit.Record(AuditAction.Delete, "DevActivityAttachment", attachmentId.ToString(),
            $"Evidencia eliminada de la actividad «{a.Title}»: {nombre}");
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

    /// <summary>El nombre lo eligió quien subió el archivo: se limpia antes de tocar el disco.</summary>
    public static string NombreSeguro(string? nombre)
    {
        var n = string.IsNullOrWhiteSpace(nombre) ? "evidencia" : Path.GetFileName(nombre!.Trim());
        foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        if (n.Length > 200) n = n[^200..];
        return string.IsNullOrWhiteSpace(n) ? "evidencia" : n;
    }
}
