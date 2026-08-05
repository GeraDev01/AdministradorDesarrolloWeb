using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Permisos: el desarrollador los SOLICITA y el administrador los RESUELVE.
///
/// Antes esta pantalla era la libreta del administrador —él capturaba el permiso ya concedido— y
/// el desarrollador ni la veía. El trámite ocurría por fuera (un mensaje, un pasillo) y lo único
/// que quedaba era el apunte de quien lo anotó. Ahora la solicitud y su respuesta viven aquí, que
/// es lo que permite responder «¿pedí eso?, ¿qué me contestaron?» sin buscar en un chat.
///
/// Las reglas viven en el servicio y no en la UI, igual que en <see cref="VacationRequestService"/>:
/// una pantalla que decide por su cuenta acaba enseñando un botón que el servicio luego rechaza.
/// </summary>
public class LeaveRequestService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly AuditService _audit;

    public LeaveRequestService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
    {
        _db = db; _currentUser = currentUser; _audit = audit;
    }

    /// <summary>Tope del justificante. Mismo criterio que el resto de adjuntos de la aplicación.</summary>
    public const int MaxAdjuntoBytes = 15 * 1024 * 1024;

    public const int MaxDias = 365;

    /// <summary>Se puede cancelar mientras siga viva: pendiente, o aprobada pero ya no se va a tomar.</summary>
    public static bool PuedeCancelar(LeaveStatus estado) =>
        estado is LeaveStatus.Pendiente or LeaveStatus.Aprobada;

    /// <summary>El desarrollador solo corrige lo que aún no ha sido resuelto.</summary>
    public static bool PuedeEditar(LeaveStatus estado) => estado == LeaveStatus.Pendiente;

    /// <summary>
    /// Solo se borra lo que nunca llegó a ser una decisión. Una aprobada o rechazada es historial:
    /// se cancela, no se borra. (El administrador sí puede depurar cualquier fila; ver Eliminar.)
    /// </summary>
    public static bool PuedeEliminarElDesarrollador(LeaveStatus estado) =>
        estado is LeaveStatus.Pendiente or LeaveStatus.Cancelada;

    // ── Lectura ──────────────────────────────────────────────────────────────

    /// <summary>Los permisos de un desarrollador. Suyos o de quien administre.</summary>
    public List<LeaveRequest> DeDesarrollador(int developerId)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, developerId);
        return _db.LeaveRequests
            .Include(l => l.Developer)
            .Where(l => l.DeveloperId == developerId)
            .OrderByDescending(l => l.Date).ThenByDescending(l => l.Id)
            .AsNoTracking()
            .ToList();
    }

    /// <summary>Todos los permisos del equipo, para la pantalla del administrador.</summary>
    public List<LeaveRequest> Todas(int? developerId = null, LeaveStatus? estado = null)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var q = _db.LeaveRequests.Include(l => l.Developer).AsQueryable();
        if (developerId is int dev) q = q.Where(l => l.DeveloperId == dev);
        if (estado is LeaveStatus e) q = q.Where(l => l.Status == e);

        // Las pendientes primero: son las únicas que piden una acción del administrador.
        return q.OrderByDescending(l => l.Status == LeaveStatus.Pendiente)
                .ThenByDescending(l => l.Date).ThenByDescending(l => l.Id)
                .AsNoTracking()
                .ToList();
    }

    /// <summary>Cuántas esperan respuesta. Para el contador del menú.</summary>
    public int PendientesCount() =>
        _currentUser.IsAdmin ? _db.LeaveRequests.Count(l => l.Status == LeaveStatus.Pendiente) : 0;

    /// <summary>El justificante de un permiso. Vacío si no tiene o si no hay derecho a verlo.</summary>
    public (byte[] bytes, string nombre) Adjunto(int requestId)
    {
        var l = _db.LeaveRequests.AsNoTracking().FirstOrDefault(x => x.Id == requestId);
        if (l == null) return ([], "");
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, l.DeveloperId);
        return l.AttachmentBytes is { Length: > 0 }
            ? (l.AttachmentBytes, NombreSeguro(l.AttachmentFileName))
            : ([], "");
    }

    // ── Alta ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// El desarrollador pide un permiso para sí mismo. Nace Pendiente: nadie se autoriza solo.
    /// </summary>
    public (bool ok, string mensaje, LeaveRequest? solicitud) Solicitar(LeaveRequest borrador)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, borrador.DeveloperId);

        var (valido, error) = Validar(borrador, exigirMotivo: true);
        if (!valido) return (false, error, null);

        borrador.Status = LeaveStatus.Pendiente;
        borrador.RequestedByDeveloperId = borrador.DeveloperId;
        borrador.ApprovedBy = null;
        borrador.ReviewedById = null;
        borrador.ReviewedAt = null;
        borrador.ReviewComment = null;
        borrador.CreatedAt = DateTime.UtcNow;

        _db.LeaveRequests.Add(borrador);
        _db.SaveChanges();

        _audit.Record(AuditAction.Create, "LeaveRequest", borrador.Id.ToString(),
            $"Permiso solicitado: {Describir(borrador)}");
        return (true, "Solicitud enviada. Queda pendiente de que el líder la resuelva.", borrador);
    }

    /// <summary>
    /// El administrador captura un permiso ya concedido (el trámite ocurrió fuera de la app). Nace
    /// Aprobada porque el acto de registrarlo ES la aprobación: dejarlo Pendiente le crearía a él
    /// mismo un trámite que ya resolvió.
    /// </summary>
    public (bool ok, string mensaje, LeaveRequest? solicitud) RegistrarPorAdministrador(LeaveRequest borrador)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var (valido, error) = Validar(borrador, exigirMotivo: false);
        if (!valido) return (false, error, null);

        borrador.Status = LeaveStatus.Aprobada;
        borrador.RequestedByDeveloperId = null;
        borrador.ReviewedById = _currentUser.UserId;
        borrador.ReviewedAt = DateTime.UtcNow;
        borrador.CreatedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(borrador.ApprovedBy)) borrador.ApprovedBy = _currentUser.Username;

        _db.LeaveRequests.Add(borrador);
        _db.SaveChanges();

        _audit.Record(AuditAction.Create, "LeaveRequest", borrador.Id.ToString(),
            $"Permiso registrado por el líder: {Describir(borrador)}");
        return (true, "Permiso registrado.", borrador);
    }

    // ── Edición y cancelación (del solicitante) ──────────────────────────────

    public (bool ok, string mensaje) Editar(int requestId, LeaveRequest cambios)
    {
        var (l, error) = ObtenerPropia(requestId);
        if (l == null) return (false, error!);

        if (!PuedeEditar(l.Status))
            return (false, $"No se puede modificar una solicitud «{Etiqueta(l.Status)}». " +
                           "Si necesitas cambiarla, cancélala y crea otra.");

        cambios.DeveloperId = l.DeveloperId;   // nunca cambia de dueño
        var (valido, errorVal) = Validar(cambios, exigirMotivo: l.EsSolicitudDelDesarrollador);
        if (!valido) return (false, errorVal);

        l.Type = cambios.Type;
        l.Date = cambios.Date.Date;
        l.DaysCount = cambios.DaysCount;
        l.Reason = Limpiar(cambios.Reason);
        l.Notes = Limpiar(cambios.Notes);
        l.AttachmentBytes = cambios.AttachmentBytes;
        l.AttachmentFileName = Limpiar(cambios.AttachmentFileName);

        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "LeaveRequest", l.Id.ToString(), $"Permiso actualizado: {Describir(l)}");
        return (true, "Solicitud actualizada.");
    }

    public (bool ok, string mensaje) Cancelar(int requestId, string? motivo = null)
    {
        var (l, error) = ObtenerPropia(requestId);
        if (l == null) return (false, error!);

        if (!PuedeCancelar(l.Status))
            return (false, $"No se puede cancelar una solicitud «{Etiqueta(l.Status)}».");

        l.Status = LeaveStatus.Cancelada;

        // Se anota en ReviewComment y NO en ReviewedById/ReviewedAt: esos campos significan «quién
        // la resolvió» y llenarlos aquí haría pasar una cancelación propia por una decisión del jefe.
        var quien = _currentUser.Username ?? "el solicitante";
        var nota = $"Cancelada por {quien} el {DateTime.Now:dd/MM/yyyy HH:mm}"
                 + (string.IsNullOrWhiteSpace(motivo) ? "." : $": {motivo.Trim()}");
        l.ReviewComment = string.IsNullOrWhiteSpace(l.ReviewComment) ? nota : $"{nota}\n{l.ReviewComment}";

        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "LeaveRequest", l.Id.ToString(), $"Permiso cancelado: {Describir(l)}");
        return (true, "Solicitud cancelada.");
    }

    /// <summary>
    /// Elimina la solicitud. Al desarrollador solo se le permite sobre lo que nunca fue una
    /// decisión; el administrador puede depurar cualquier fila (es quien mantiene el registro).
    /// </summary>
    public (bool ok, string mensaje) Eliminar(int requestId)
    {
        var (l, error) = ObtenerPropia(requestId);
        if (l == null) return (false, error!);

        if (!_currentUser.IsAdmin && !PuedeEliminarElDesarrollador(l.Status))
            return (false,
                $"No se puede eliminar una solicitud «{Etiqueta(l.Status)}»: es parte del historial. " +
                "Si ya no la vas a tomar, cancélala.");

        var descripcion = Describir(l);
        _db.LeaveRequests.Remove(l);
        _db.SaveChanges();

        _audit.Record(AuditAction.Delete, "LeaveRequest", requestId.ToString(), $"Permiso eliminado: {descripcion}");
        return (true, "Solicitud eliminada.");
    }

    // ── Resolución (del administrador) ───────────────────────────────────────

    public (bool ok, string mensaje) Aprobar(int requestId, string? comentario = null) =>
        Resolver(requestId, LeaveStatus.Aprobada, comentario);

    public (bool ok, string mensaje) Rechazar(int requestId, string? motivo)
    {
        // Un rechazo sin motivo deja al solicitante sin nada que hacer con la respuesta.
        if (string.IsNullOrWhiteSpace(motivo))
            return (false, "Escribe el motivo del rechazo: es lo único que el solicitante va a leer.");
        return Resolver(requestId, LeaveStatus.Rechazada, motivo);
    }

    private (bool ok, string mensaje) Resolver(int requestId, LeaveStatus destino, string? comentario)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var l = _db.LeaveRequests.Include(x => x.Developer).FirstOrDefault(x => x.Id == requestId);
        if (l == null) return (false, "La solicitud ya no existe. Actualiza la lista.");

        // El AppDbContext es Singleton: el solicitante pudo cancelarla desde su equipo hace un
        // momento y lo rastreado la seguiría dando por pendiente.
        _db.Entry(l).Reload();

        if (l.Status != LeaveStatus.Pendiente)
            return (false, $"Esa solicitud ya está «{Etiqueta(l.Status)}»; no hay nada que resolver.");

        l.Status = destino;
        l.ReviewedById = _currentUser.UserId;
        l.ReviewedAt = DateTime.UtcNow;
        l.ReviewComment = Limpiar(comentario);
        if (destino == LeaveStatus.Aprobada && string.IsNullOrWhiteSpace(l.ApprovedBy))
            l.ApprovedBy = _currentUser.Username;

        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "LeaveRequest", l.Id.ToString(),
            $"Permiso {Etiqueta(destino).ToLowerInvariant()}: {Describir(l)}");

        return (true, destino == LeaveStatus.Aprobada ? "Permiso aprobado." : "Permiso rechazado.");
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────

    private (LeaveRequest? l, string? error) ObtenerPropia(int requestId)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);

        var l = _db.LeaveRequests.FirstOrDefault(x => x.Id == requestId);
        if (l == null) return (null, "La solicitud ya no existe. Actualiza la lista.");

        _db.Entry(l).Reload();   // pudo resolverse desde otro equipo mientras esta pantalla esperaba
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, l.DeveloperId);
        return (l, null);
    }

    private (bool ok, string error) Validar(LeaveRequest l, bool exigirMotivo)
    {
        if (l.DeveloperId <= 0) return (false, "Falta indicar de quién es el permiso.");
        if (l.DaysCount < 1) return (false, "El permiso tiene que ser de al menos un día.");
        if (l.DaysCount > MaxDias) return (false, $"El permiso no puede pasar de {MaxDias} días.");
        if (l.Date == default) return (false, "Indica la fecha de inicio.");

        if (exigirMotivo && string.IsNullOrWhiteSpace(l.Reason))
            return (false, "Escribe el motivo: es lo que el líder va a leer para decidir.");

        if (l.AttachmentBytes is { Length: > 0 })
        {
            if (l.AttachmentBytes.Length > MaxAdjuntoBytes)
                return (false, $"El justificante supera {MaxAdjuntoBytes / (1024 * 1024)} MB.");
            if (string.IsNullOrWhiteSpace(l.AttachmentFileName))
                l.AttachmentFileName = "justificante";
        }
        else
        {
            // Sin bytes no debe quedar un nombre suelto: la pantalla mostraría un adjunto que no existe.
            l.AttachmentBytes = null;
            l.AttachmentFileName = null;
        }

        l.Reason = Limpiar(l.Reason);
        l.Notes = Limpiar(l.Notes);
        l.Date = l.Date.Date;
        return (true, "");
    }

    private static string? Limpiar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>El nombre lo eligió quien subió el archivo: se limpia antes de tocar el disco.</summary>
    public static string NombreSeguro(string? nombre)
    {
        var n = string.IsNullOrWhiteSpace(nombre) ? "justificante" : nombre!;
        foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        return n;
    }

    private static string Describir(LeaveRequest l) =>
        $"{EtiquetaTipo(l.Type)} {l.Date:dd/MM/yyyy} ({l.DaysCount} día(s))";

    public static string Etiqueta(LeaveStatus s) => s switch
    {
        LeaveStatus.Pendiente => "⏳ Pendiente",
        LeaveStatus.Aprobada  => "✅ Aprobada",
        LeaveStatus.Rechazada => "❌ Rechazada",
        _                     => "🚫 Cancelada"
    };

    // El color de cada estado lo pone la UI (ver LeaveStatusUi): un servicio no debe depender del
    // tema visual, y con esto sigue siendo utilizable desde el futuro portal web.

    public static string EtiquetaTipo(LeaveType t) => t switch
    {
        LeaveType.PermisoPersonal => "🙋 Permiso personal",
        LeaveType.Incapacidad     => "🏥 Incapacidad",
        LeaveType.CitaMedica      => "🩺 Cita médica",
        LeaveType.AsuntoFamiliar  => "👨‍👩‍👧 Asunto familiar",
        LeaveType.Capacitacion    => "📚 Capacitación",
        _                         => "📋 Otro"
    };
}
