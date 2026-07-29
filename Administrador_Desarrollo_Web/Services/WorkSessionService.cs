using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Objetivo de un cronómetro: un requerimiento asignado o una actividad libre. Se modela como un
/// tipo propio y no como dos parámetros sueltos para que sea imposible llamar a los métodos con
/// ambos o con ninguno sin que salte la validación.
/// </summary>
/// <summary>Tiempo dedicado a un item en un día concreto (fila del reporte por día).</summary>
public record DayTotal(DateTime Date, int? RequirementId, int? ActivityId, string Target, int Seconds);

public readonly record struct WorkTarget(int? RequirementId, int? ActivityId)
{
    public static WorkTarget Requerimiento(int id) => new(id, null);
    public static WorkTarget Actividad(int id) => new(null, id);

    public bool EsValido => RequirementId.HasValue ^ ActivityId.HasValue;

    public override string ToString() =>
        RequirementId is int r ? $"req #{r}" : ActivityId is int a ? $"actividad #{a}" : "(sin objetivo)";
}

/// <summary>
/// Cronómetro por item: gestiona las sesiones de trabajo (WorkSession) de un
/// desarrollador sobre un requerimiento. Regla: un desarrollador tiene a lo sumo
/// UNA sesión ACTIVA a la vez; iniciar otro item pausa el/los anteriores, que
/// quedan Pausada (siguen abiertos) hasta que se Detienen.
/// </summary>
public class WorkSessionService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly ICurrentUser _currentUser;

    public WorkSessionService(AppDbContext db, AuditService audit, ICurrentUser currentUser)
    {
        _db = db; _audit = audit; _currentUser = currentUser;
    }

    /// <summary>Sesión abierta (Activa/Pausada) del dev sobre ese objetivo, la más reciente; null si no hay.</summary>
    public WorkSession? GetOpenSession(int devId, WorkTarget target) =>
        _db.WorkSessions
            .Where(w => w.DeveloperId == devId && w.Status != WorkSessionStatus.Detenida
                     && w.RequirementId == target.RequirementId && w.ActivityId == target.ActivityId)
            .OrderByDescending(w => w.Id)
            .FirstOrDefault();

    /// <summary>Sobrecarga por requerimiento (compatibilidad con las pantallas existentes).</summary>
    public WorkSession? GetOpenSession(int devId, int reqId) => GetOpenSession(devId, WorkTarget.Requerimiento(reqId));

    /// <summary>Sesión abierta del dev sobre CUALQUIER objetivo; útil para avisar qué se va a pausar.</summary>
    public WorkSession? GetAnyOpenSession(int devId) =>
        _db.WorkSessions
            .Include(w => w.Requirement).Include(w => w.Activity)
            .Where(w => w.DeveloperId == devId && w.Status == WorkSessionStatus.Activa)
            .OrderByDescending(w => w.Id)
            .FirstOrDefault();

    /// <summary>
    /// Inicia (o reanuda si estaba pausada) el cronómetro del dev sobre el objetivo indicado.
    /// Auto-pausa cualquier otra sesión abierta del mismo dev, sea de un requerimiento o de una
    /// actividad libre: la regla de "un solo cronómetro a la vez" vale para ambos tipos, si no el
    /// tiempo se contaría doble.
    /// Si <paramref name="moveToInProgress"/> y el objetivo es un requerimiento que aún está antes
    /// de "En desarrollo", lo mueve a EnDesarrollo de forma ATÓMICA (un solo SaveChanges).
    /// </summary>
    public WorkSession StartOrResume(int devId, WorkTarget target, bool moveToInProgress = true)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, devId);
        if (!target.EsValido)
            throw new ArgumentException("La sesión debe apuntar a un requerimiento o a una actividad, no a ambos.", nameof(target));

        var now = DateTime.UtcNow;

        foreach (var other in _db.WorkSessions
                     .Where(w => w.DeveloperId == devId && w.Status != WorkSessionStatus.Detenida
                              && !(w.RequirementId == target.RequirementId && w.ActivityId == target.ActivityId))
                     .ToList())
            PauseInternal(other, now);

        var s = GetOpenSession(devId, target);
        if (s == null)
        {
            s = new WorkSession
            {
                DeveloperId = devId,
                RequirementId = target.RequirementId,
                ActivityId = target.ActivityId,
                StartedAt = now, LastResumedAt = now, CreatedAt = now,
                Status = WorkSessionStatus.Activa, AccumulatedSeconds = 0
            };
            _db.WorkSessions.Add(s);
        }
        else if (s.Status == WorkSessionStatus.Pausada)
        {
            s.LastResumedAt = now;
            s.Status = WorkSessionStatus.Activa;
        }
        // Si ya estaba Activa, no hay cambio.

        // Transición de estado atómica con el arranque de la sesión (mismo SaveChanges).
        bool moved = false;
        if (moveToInProgress && target.RequirementId is int reqId)
        {
            var req = _db.Requirements.Find(reqId);
            if (req != null && (req.Status == RequirementStatus.PorEstimar || req.Status == RequirementStatus.Estimado))
            {
                req.Status = RequirementStatus.EnDesarrollo;
                req.StatusChangedAt = now;
                moved = true;
            }
        }

        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "WorkSession", s.Id.ToString(), $"Iniciar/Reanudar {target}");
        if (moved) _audit.Record(AuditAction.Update, "Requirement", target.RequirementId!.Value.ToString(),
            $"#{target.RequirementId} → En desarrollo (inicio de cronómetro)");
        return s;
    }

    /// <summary>Sobrecarga por requerimiento (compatibilidad con las pantallas existentes).</summary>
    public WorkSession StartOrResume(int devId, int reqId, bool moveToInProgress = true) =>
        StartOrResume(devId, WorkTarget.Requerimiento(reqId), moveToInProgress);

    /// <summary>
    /// Pausa TODAS las sesiones Activa (de todos los devs) consolidando el tramo real.
    /// Se llama al cerrar la app (cierre limpio) para no perder ni inflar tiempo.
    /// </summary>
    public void PauseAllActive()
    {
        var now = DateTime.UtcNow;
        var actives = _db.WorkSessions.Where(w => w.Status == WorkSessionStatus.Activa).ToList();
        if (actives.Count == 0) return;
        foreach (var s in actives) PauseInternal(s, now);
        _db.SaveChanges();
    }

    /// <summary>
    /// Reconcilia sesiones huérfanas al arrancar: si quedó alguna Activa (cierre sucio/crash),
    /// se pasa a Pausada DESCARTANDO el tramo colgante (no se sabe cuándo se cerró la app),
    /// evitando contar noches/fines de semana. Devuelve cuántas se reconciliaron.
    /// </summary>
    public int ReconcileOrphans()
    {
        var actives = _db.WorkSessions.Where(w => w.Status == WorkSessionStatus.Activa).ToList();
        if (actives.Count == 0) return 0;
        foreach (var s in actives)
        {
            s.LastResumedAt = null;                 // descarta el tramo no confiable
            s.Status = WorkSessionStatus.Pausada;
        }
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "WorkSession", "", $"{actives.Count} sesión(es) huérfana(s) reconciliada(s) al arranque");
        return actives.Count;
    }

    public void Pause(int devId, WorkTarget target)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, devId);
        var s = GetOpenSession(devId, target);
        if (s == null || s.Status != WorkSessionStatus.Activa) return;
        PauseInternal(s, DateTime.UtcNow);
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "WorkSession", s.Id.ToString(), $"Pausar {target}");
    }

    public void Pause(int devId, int reqId) => Pause(devId, WorkTarget.Requerimiento(reqId));

    public void Stop(int devId, WorkTarget target)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, devId);
        var s = GetOpenSession(devId, target);
        if (s == null) return;
        var now = DateTime.UtcNow;
        ConsolidarTramo(s, now);
        s.EndedAt = now;
        s.Status = WorkSessionStatus.Detenida;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "WorkSession", s.Id.ToString(), $"Detener {target} (total {s.AccumulatedSeconds}s)");
    }

    public void Stop(int devId, int reqId) => Stop(devId, WorkTarget.Requerimiento(reqId));

    /// <summary>
    /// Consolida el tramo en curso: lo suma a AccumulatedSeconds Y lo registra como
    /// <see cref="WorkInterval"/> con su fecha, para el reporte por día. Deja la sesión sin tramo
    /// abierto (LastResumedAt = null); el estado (Pausada/Detenida) lo pone quien llama.
    /// </summary>
    private void ConsolidarTramo(WorkSession s, DateTime now)
    {
        if (s.Status == WorkSessionStatus.Activa && s.LastResumedAt is DateTime r)
        {
            int secs = Math.Max(0, (int)(now - r).TotalSeconds);
            if (secs > 0)
            {
                s.AccumulatedSeconds += secs;
                _db.WorkIntervals.Add(new WorkInterval
                {
                    DeveloperId = s.DeveloperId,
                    RequirementId = s.RequirementId,
                    ActivityId = s.ActivityId,
                    StartUtc = r,
                    EndUtc = now,
                    Seconds = secs,
                    LocalDate = r.ToLocalTime().Date   // se atribuye al día local en que INICIÓ el tramo
                });
            }
        }
        s.LastResumedAt = null;
    }

    private void PauseInternal(WorkSession s, DateTime now)
    {
        ConsolidarTramo(s, now);
        s.Status = WorkSessionStatus.Pausada;
    }

    /// <summary>Segundos totales dedicados a un item (todas las sesiones, incluyendo el tramo en curso).</summary>
    public int GetTotalSecondsByRequirement(int reqId)
    {
        var now = DateTime.UtcNow;
        return _db.WorkSessions.Where(w => w.RequirementId == reqId).AsNoTracking().AsEnumerable().Sum(w => w.LiveSeconds(now));
    }

    /// <summary>Segundos totales dedicados por un desarrollador (todos sus items).</summary>
    public int GetTotalSecondsByDeveloper(int devId)
    {
        var now = DateTime.UtcNow;
        return _db.WorkSessions.Where(w => w.DeveloperId == devId).AsNoTracking().AsEnumerable().Sum(w => w.LiveSeconds(now));
    }

    /// <summary>Segundos totales de un desarrollador sobre un objetivo concreto.</summary>
    public int GetTotalSeconds(int devId, WorkTarget target)
    {
        var now = DateTime.UtcNow;
        return _db.WorkSessions
            .Where(w => w.DeveloperId == devId && w.RequirementId == target.RequirementId && w.ActivityId == target.ActivityId)
            .AsNoTracking().AsEnumerable().Sum(w => w.LiveSeconds(now));
    }

    public int GetTotalSeconds(int devId, int reqId) => GetTotalSeconds(devId, WorkTarget.Requerimiento(reqId));

    /// <summary>Segundos totales dedicados a una actividad libre (todas sus sesiones).</summary>
    public int GetTotalSecondsByActivity(int activityId)
    {
        var now = DateTime.UtcNow;
        return _db.WorkSessions.Where(w => w.ActivityId == activityId).AsNoTracking().AsEnumerable().Sum(w => w.LiveSeconds(now));
    }

    /// <summary>
    /// Tiempo por DÍA y por item de un desarrollador, en un rango de fechas locales (inclusive).
    /// Se calcula de los tramos registrados (<see cref="WorkInterval"/>): es exacto por día aunque
    /// una sesión abarque varios. Solo cubre lo trabajado a partir de que existe esta bitácora.
    /// </summary>
    public List<DayTotal> ResumenPorDia(int devId, DateTime desdeLocal, DateTime hastaLocal)
    {
        var d0 = desdeLocal.Date;
        var d1 = hastaLocal.Date;

        var grupos = _db.WorkIntervals
            .Where(w => w.DeveloperId == devId && w.LocalDate >= d0 && w.LocalDate <= d1)
            .GroupBy(w => new { w.LocalDate, w.RequirementId, w.ActivityId })
            .Select(g => new { g.Key.LocalDate, g.Key.RequirementId, g.Key.ActivityId, Seconds = g.Sum(x => x.Seconds) })
            .ToList();

        var reqIds = grupos.Where(x => x.RequirementId != null).Select(x => x.RequirementId!.Value).Distinct().ToList();
        var actIds = grupos.Where(x => x.ActivityId != null).Select(x => x.ActivityId!.Value).Distinct().ToList();
        var reqTitles = _db.Requirements.Where(r => reqIds.Contains(r.Id)).Select(r => new { r.Id, r.Title }).ToDictionary(r => r.Id, r => r.Title);
        var actTitles = _db.DevActivities.Where(a => actIds.Contains(a.Id)).Select(a => new { a.Id, a.Title }).ToDictionary(a => a.Id, a => a.Title);

        return grupos
            .Select(x => new DayTotal(
                x.LocalDate,
                x.RequirementId, x.ActivityId,
                x.RequirementId is int rid ? (reqTitles.GetValueOrDefault(rid) ?? $"Requerimiento #{rid}")
                : x.ActivityId is int aid ? (actTitles.GetValueOrDefault(aid) ?? $"Actividad #{aid}")
                : "—",
                x.Seconds))
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Seconds)
            .ToList();
    }

    /// <summary>Formatea segundos como "Nh MMm SSs" (o "MMm SSs" si &lt; 1h).</summary>
    public static string Format(int seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}h {t.Minutes:00}m {t.Seconds:00}s"
            : $"{t.Minutes:00}m {t.Seconds:00}s";
    }
}
