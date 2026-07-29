using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Operaciones que un desarrollador puede hacer sobre SUS PROPIAS solicitudes de vacaciones:
/// cancelarlas o eliminarlas. Las reglas viven aquí y no en la UI para que la vista y la
/// operación no puedan discrepar (un botón habilitado que luego el servicio rechaza, o peor,
/// al revés) y para que la guarda de pertenencia se aplique aunque la llamada venga de otro lado.
/// </summary>
public class VacationRequestService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly AuditService _audit;

    public VacationRequestService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
    {
        _db = db; _currentUser = currentUser; _audit = audit;
    }

    /// <summary>
    /// Cancelar tiene sentido mientras la solicitud siga viva: pendiente de revisión, o ya
    /// aprobada pero el desarrollador decide no tomarla. Una rechazada o ya cancelada no.
    /// </summary>
    public static bool PuedeCancelar(VacationStatus estado) =>
        estado is VacationStatus.Pendiente or VacationStatus.Aprobada;

    /// <summary>
    /// Solo se borra lo que nunca llegó a ser una decisión del administrador. Una solicitud
    /// aprobada o rechazada es historial: se cancela, no se borra.
    /// </summary>
    public static bool PuedeEliminar(VacationStatus estado) =>
        estado is VacationStatus.Pendiente or VacationStatus.Cancelada;

    /// <summary>Cuántos documentos generados se perderían al eliminar (se borran en cascada).</summary>
    public int DocumentosAsociados(int requestId) =>
        _db.VacationDocuments.Count(d => d.VacationRequestId == requestId);

    public (bool ok, string mensaje) Cancelar(int requestId, string? motivo = null)
    {
        var (v, error) = ObtenerPropia(requestId);
        if (v == null) return (false, error!);

        if (!PuedeCancelar(v.Status))
            return (false, $"No se puede cancelar una solicitud en estado «{v.Status}».");

        v.Status = VacationStatus.Cancelada;

        // Se anota en ReviewComment y no en ReviewedById/ReviewedAt: esos campos significan
        // "quién la revisó" y llenarlos aquí haría pasar una cancelación propia por una revisión
        // del administrador.
        var quien = _currentUser.Username ?? "desarrollador";
        var nota = $"Cancelada por {quien} el {DateTime.Now:dd/MM/yyyy HH:mm}"
                 + (string.IsNullOrWhiteSpace(motivo) ? "." : $": {motivo.Trim()}");
        v.ReviewComment = string.IsNullOrWhiteSpace(v.ReviewComment) ? nota : $"{nota}\n{v.ReviewComment}";

        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "VacationRequest", v.Id.ToString(),
            $"Cancelada por el desarrollador: {v.StartDate:dd/MM/yyyy} — {v.EndDate:dd/MM/yyyy}");
        return (true, "Solicitud cancelada.");
    }

    public (bool ok, string mensaje) Eliminar(int requestId)
    {
        var (v, error) = ObtenerPropia(requestId);
        if (v == null) return (false, error!);

        if (!PuedeEliminar(v.Status))
            return (false,
                $"No se puede eliminar una solicitud «{v.Status}»: es parte del historial. " +
                "Si ya no la vas a tomar, cancélala.");

        var descripcion = $"{v.StartDate:dd/MM/yyyy} — {v.EndDate:dd/MM/yyyy} ({v.Status})";
        _db.VacationRequests.Remove(v);   // VacationDocuments cae en cascada por configuración del modelo
        _db.SaveChanges();

        _audit.Record(AuditAction.Delete, "VacationRequest", requestId.ToString(),
            $"Eliminada por el desarrollador: {descripcion}");
        return (true, "Solicitud eliminada.");
    }

    /// <summary>Carga la solicitud verificando que la sesión tenga derecho a tocarla.</summary>
    private (VacationRequest? v, string? error) ObtenerPropia(int requestId)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);

        var v = _db.VacationRequests.FirstOrDefault(x => x.Id == requestId);
        if (v == null) return (null, "La solicitud ya no existe. Actualiza la lista.");

        // Lanza AuthorizationException si no es dueño ni administrador.
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, v.DeveloperId);
        return (v, null);
    }
}
