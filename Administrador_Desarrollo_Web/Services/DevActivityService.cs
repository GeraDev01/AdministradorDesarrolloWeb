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
}
