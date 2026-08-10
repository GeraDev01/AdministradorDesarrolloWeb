using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>Tiempo dedicado a un item en un día concreto (fila del reporte por día).</summary>
public record DayTotal(DateTime Date, int? RequirementId, int? ActivityId, string Target, int Seconds);

/// <summary>
/// Objetivo de un cronómetro: un requerimiento asignado o una actividad libre. Se modela como un
/// tipo propio y no como dos parámetros sueltos para que sea imposible llamar a los métodos con
/// ambos o con ninguno sin que salte la validación.
/// </summary>
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
///
/// LO QUE CAMBIA EN LA WEB: en el escritorio el tiempo se consolidaba al cerrar la ventana, que era
/// un acto deliberado y avisado. Aquí cerrar la pestaña es lo NORMAL y no avisa de nada, así que la
/// sesión late (<see cref="RegistrarLatidoAsync"/>) y las que dejan de latir se consolidan hasta su
/// último latido (<see cref="ConsolidarSesionesSinLatidoAsync"/>) — la misma política que
/// <see cref="PresenceService"/> ya aplica a las jornadas caídas, y por el mismo motivo: cerrar
/// «con la hora de ahora» le regalaría a alguien las horas que su pestaña pasó cerrada.
/// </summary>
public class WorkSessionService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
{
    /// <summary>
    /// Cada cuánto late el cronómetro desde el navegador. Es pública porque quien pinta la pantalla
    /// programa el temporizador con ella, y tenerla escrita a mano allí garantizaba que un día
    /// dejaran de coincidir.
    /// </summary>
    public static readonly TimeSpan IntervaloLatido = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Sin latido en este tiempo, la sesión se da por caída y se consolida. Es varias veces el
    /// intervalo a propósito, igual que en la presencia: un equipo que suspende un momento, una red
    /// que parpadea o una pestaña en segundo plano que el navegador ralentiza no deben cortarle el
    /// cronómetro a nadie.
    /// </summary>
    public static readonly TimeSpan ToleranciaSinLatido = TimeSpan.FromMinutes(10);

    /// <summary>Sesión abierta (Activa/Pausada) del dev sobre ese objetivo, la más reciente; null si no hay.</summary>
    public Task<WorkSession?> GetOpenSessionAsync(int devId, WorkTarget target, CancellationToken ct = default) =>
        db.WorkSessions
            .Where(w => w.DeveloperId == devId && w.Status != WorkSessionStatus.Detenida
                     && w.RequirementId == target.RequirementId && w.ActivityId == target.ActivityId)
            .OrderByDescending(w => w.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>Sobrecarga por requerimiento (compatibilidad con las pantallas existentes).</summary>
    public Task<WorkSession?> GetOpenSessionAsync(int devId, int reqId, CancellationToken ct = default) =>
        GetOpenSessionAsync(devId, WorkTarget.Requerimiento(reqId), ct);

    /// <summary>Sesión abierta del dev sobre CUALQUIER objetivo; útil para avisar qué se va a pausar.</summary>
    public Task<WorkSession?> GetAnyOpenSessionAsync(int devId, CancellationToken ct = default) =>
        db.WorkSessions
            .Include(w => w.Requirement).Include(w => w.Activity)
            .Where(w => w.DeveloperId == devId && w.Status == WorkSessionStatus.Activa)
            .OrderByDescending(w => w.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Inicia (o reanuda si estaba pausada) el cronómetro del dev sobre el objetivo indicado.
    /// Auto-pausa cualquier otra sesión abierta del mismo dev, sea de un requerimiento o de una
    /// actividad libre: la regla de "un solo cronómetro a la vez" vale para ambos tipos, si no el
    /// tiempo se contaría doble.
    /// Si <paramref name="moveToInProgress"/> y el objetivo es un requerimiento que aún está antes
    /// de "En desarrollo", lo mueve a EnDesarrollo de forma ATÓMICA (un solo SaveChanges).
    /// </summary>
    public async Task<WorkSession> StartOrResumeAsync(int devId, WorkTarget target,
        bool moveToInProgress = true, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, devId);
        if (!target.EsValido)
            throw new ArgumentException("La sesión debe apuntar a un requerimiento o a una actividad, no a ambos.", nameof(target));

        var now = DateTime.UtcNow;

        foreach (var other in await db.WorkSessions
                     .Where(w => w.DeveloperId == devId && w.Status != WorkSessionStatus.Detenida
                              && !(w.RequirementId == target.RequirementId && w.ActivityId == target.ActivityId))
                     .ToListAsync(ct))
            PauseInternal(other, now);

        var s = await GetOpenSessionAsync(devId, target, ct);
        if (s == null)
        {
            s = new WorkSession
            {
                DeveloperId = devId,
                RequirementId = target.RequirementId,
                ActivityId = target.ActivityId,
                StartedAt = now, LastResumedAt = now, CreatedAt = now,
                Status = WorkSessionStatus.Activa, AccumulatedSeconds = 0,
                LastHeartbeatUtc = now
            };
            db.WorkSessions.Add(s);
        }
        else if (s.Status == WorkSessionStatus.Pausada)
        {
            s.LastResumedAt = now;
            s.Status = WorkSessionStatus.Activa;
            // El latido arranca con la reanudación: sin esto, una sesión reanudada arrastraría el
            // latido de antes de la pausa y el barrido la consolidaría de inmediato.
            s.LastHeartbeatUtc = now;
        }
        // Si ya estaba Activa, no hay cambio.

        // Transición de estado atómica con el arranque de la sesión (mismo SaveChanges).
        bool moved = false;
        if (moveToInProgress && target.RequirementId is int reqId)
        {
            var req = await db.Requirements.FirstOrDefaultAsync(r => r.Id == reqId, ct);
            if (req != null && (req.Status == RequirementStatus.PorEstimar || req.Status == RequirementStatus.Estimado))
            {
                req.Status = RequirementStatus.EnDesarrollo;
                req.StatusChangedAt = now;
                moved = true;
            }
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "WorkSession", s.Id.ToString(), $"Iniciar/Reanudar {target}", ct);
        if (moved) await audit.RecordAsync(AuditAction.Update, "Requirement", target.RequirementId!.Value.ToString(),
            $"#{target.RequirementId} → En desarrollo (inicio de cronómetro)", ct);
        return s;
    }

    /// <summary>Sobrecarga por requerimiento (compatibilidad con las pantallas existentes).</summary>
    public Task<WorkSession> StartOrResumeAsync(int devId, int reqId,
        bool moveToInProgress = true, CancellationToken ct = default) =>
        StartOrResumeAsync(devId, WorkTarget.Requerimiento(reqId), moveToInProgress, ct);

    /// <summary>
    /// El latido del cronómetro: deja constancia de que la pestaña sigue abierta y de que la persona
    /// sigue en ello.
    ///
    /// No hacía falta en el escritorio, donde cerrar la ventana era deliberado y consolidaba el
    /// tiempo ahí mismo. En la web nadie «cierra» nada: se cierra la pestaña, se duerme el equipo o
    /// se cae la red, y sin este dato la sesión seguiría contando toda la noche. El latido es lo que
    /// permite que <see cref="ConsolidarSesionesSinLatidoAsync"/> sepa hasta qué momento hubo
    /// trabajo de verdad.
    ///
    /// NO se anota en la bitácora: un apunte cada dos minutos por persona la volvería ilegible, que
    /// es la forma más segura de que nadie vuelva a mirarla.
    ///
    /// Devuelve false si esa persona no tiene el cronómetro corriendo sobre ese objetivo. El
    /// navegador debe entonces dejar de latir en vez de resucitar una sesión ya detenida —quizá la
    /// detuvo desde otra pestaña, que en la web es lo habitual.
    /// </summary>
    public async Task<bool> RegistrarLatidoAsync(int devId, WorkTarget target, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, devId);
        if (!target.EsValido) return false;

        var s = await db.WorkSessions
            .Where(w => w.DeveloperId == devId && w.Status == WorkSessionStatus.Activa
                     && w.RequirementId == target.RequirementId && w.ActivityId == target.ActivityId)
            .OrderByDescending(w => w.Id)
            .FirstOrDefaultAsync(ct);
        if (s == null) return false;

        s.LastHeartbeatUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Sobrecarga por requerimiento, como el resto del cronómetro.</summary>
    public Task<bool> RegistrarLatidoAsync(int devId, int reqId, CancellationToken ct = default) =>
        RegistrarLatidoAsync(devId, WorkTarget.Requerimiento(reqId), ct);

    /// <summary>
    /// Consolida las sesiones que llevan demasiado tiempo sin latir, sellando su tramo con la hora
    /// del ÚLTIMO LATIDO y dejándolas Pausada (siguen abiertas: la persona puede retomarlas).
    ///
    /// Es la MISMA política que <see cref="PresenceService.CerrarCaidasAsync"/> aplica a las jornadas
    /// caídas, y por el mismo motivo: consolidar «hasta ahora» le regalaría a alguien todas las horas
    /// que su pestaña pasó cerrada. Y a diferencia de <see cref="ReconcileOrphansAsync"/> —que tira
    /// el tramo entero porque no tiene ningún dato mejor— aquí sí sabemos hasta cuándo hubo trabajo,
    /// así que quitárselo sería igual de falso que regalárselo.
    ///
    /// Las sesiones SIN latido registrado (las que vienen del escritorio, o una recién reanudada que
    /// no alcanzó a latir) se consolidan hasta su reanudación: tramo de duración cero. Es la única
    /// hora de la que consta que la persona estaba ahí, y no inventa nada.
    ///
    /// Devuelve cuántas se consolidaron. Lo llama un trabajo en segundo plano; no lleva guarda de
    /// autorización porque no lo dispara ninguna persona, igual que el resto del barrido.
    /// </summary>
    public async Task<int> ConsolidarSesionesSinLatidoAsync(CancellationToken ct = default)
    {
        var corte = DateTime.UtcNow - ToleranciaSinLatido;

        var caidas = await db.WorkSessions
            .Where(w => w.Status == WorkSessionStatus.Activa
                     && (w.LastHeartbeatUtc ?? w.LastResumedAt ?? w.StartedAt) < corte)
            .ToListAsync(ct);
        if (caidas.Count == 0) return 0;

        foreach (var s in caidas)
        {
            var hasta = s.LastHeartbeatUtc ?? s.LastResumedAt ?? s.StartedAt;

            // Nunca hacia atrás: si el latido guardado es anterior a la reanudación, el tramo se
            // queda en cero antes que en negativo. ConsolidarTramo ya lo acota, pero dejarlo escrito
            // aquí evita que un cambio futuro en aquel método reabra el agujero sin darse cuenta.
            if (s.LastResumedAt is DateTime r && hasta < r) hasta = r;

            PauseInternal(s, hasta);
        }
        await db.SaveChangesAsync(ct);

        await audit.RecordSystemAsync(AuditAction.Update,
            $"{caidas.Count} sesión(es) de cronómetro sin latido consolidada(s) hasta su último latido", ct);
        return caidas.Count;
    }

    /// <summary>
    /// Pausa TODAS las sesiones Activa (de todos los devs) consolidando el tramo real.
    /// Se llama al cerrar la app (cierre limpio) para no perder ni inflar tiempo.
    ///
    /// En la web el equivalente de aquel «cierre limpio» es el apagado ordenado del servidor; el
    /// caso corriente —alguien que cierra su pestaña— lo cubre el barrido por latido.
    /// </summary>
    public async Task PauseAllActiveAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var actives = await db.WorkSessions.Where(w => w.Status == WorkSessionStatus.Activa).ToListAsync(ct);
        if (actives.Count == 0) return;
        foreach (var s in actives) PauseInternal(s, now);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Reconcilia sesiones huérfanas al arrancar: si quedó alguna Activa (cierre sucio/crash),
    /// se pasa a Pausada DESCARTANDO el tramo colgante (no se sabe cuándo se cerró la app),
    /// evitando contar noches/fines de semana. Devuelve cuántas se reconciliaron.
    ///
    /// Sigue siendo el último recurso, para sesiones de las que no consta NINGÚN latido; cuando sí
    /// lo hay, <see cref="ConsolidarSesionesSinLatidoAsync"/> conserva el tramo hasta ese momento en
    /// vez de tirarlo.
    /// </summary>
    public async Task<int> ReconcileOrphansAsync(CancellationToken ct = default)
    {
        var actives = await db.WorkSessions.Where(w => w.Status == WorkSessionStatus.Activa).ToListAsync(ct);
        if (actives.Count == 0) return 0;
        foreach (var s in actives)
        {
            s.LastResumedAt = null;                 // descarta el tramo no confiable
            s.Status = WorkSessionStatus.Pausada;
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "WorkSession", "",
            $"{actives.Count} sesión(es) huérfana(s) reconciliada(s) al arranque", ct);
        return actives.Count;
    }

    public async Task PauseAsync(int devId, WorkTarget target, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, devId);
        var s = await GetOpenSessionAsync(devId, target, ct);
        if (s == null || s.Status != WorkSessionStatus.Activa) return;
        PauseInternal(s, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "WorkSession", s.Id.ToString(), $"Pausar {target}", ct);
    }

    public Task PauseAsync(int devId, int reqId, CancellationToken ct = default) =>
        PauseAsync(devId, WorkTarget.Requerimiento(reqId), ct);

    public async Task StopAsync(int devId, WorkTarget target, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, devId);
        var s = await GetOpenSessionAsync(devId, target, ct);
        if (s == null) return;
        var now = DateTime.UtcNow;
        ConsolidarTramo(s, now);
        s.EndedAt = now;
        s.Status = WorkSessionStatus.Detenida;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "WorkSession", s.Id.ToString(),
            $"Detener {target} (total {s.AccumulatedSeconds}s)", ct);
    }

    public Task StopAsync(int devId, int reqId, CancellationToken ct = default) =>
        StopAsync(devId, WorkTarget.Requerimiento(reqId), ct);

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
                db.WorkIntervals.Add(new WorkInterval
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
    public async Task<int> GetTotalSecondsByRequirementAsync(int reqId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var sesiones = await db.WorkSessions.Where(w => w.RequirementId == reqId).AsNoTracking().ToListAsync(ct);
        return sesiones.Sum(w => w.LiveSeconds(now));
    }

    /// <summary>Segundos totales dedicados por un desarrollador (todos sus items).</summary>
    public async Task<int> GetTotalSecondsByDeveloperAsync(int devId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var sesiones = await db.WorkSessions.Where(w => w.DeveloperId == devId).AsNoTracking().ToListAsync(ct);
        return sesiones.Sum(w => w.LiveSeconds(now));
    }

    /// <summary>Segundos totales de un desarrollador sobre un objetivo concreto.</summary>
    public async Task<int> GetTotalSecondsAsync(int devId, WorkTarget target, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var sesiones = await db.WorkSessions
            .Where(w => w.DeveloperId == devId && w.RequirementId == target.RequirementId && w.ActivityId == target.ActivityId)
            .AsNoTracking().ToListAsync(ct);
        return sesiones.Sum(w => w.LiveSeconds(now));
    }

    public Task<int> GetTotalSecondsAsync(int devId, int reqId, CancellationToken ct = default) =>
        GetTotalSecondsAsync(devId, WorkTarget.Requerimiento(reqId), ct);

    /// <summary>Segundos totales dedicados a una actividad libre (todas sus sesiones).</summary>
    public async Task<int> GetTotalSecondsByActivityAsync(int activityId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var sesiones = await db.WorkSessions.Where(w => w.ActivityId == activityId).AsNoTracking().ToListAsync(ct);
        return sesiones.Sum(w => w.LiveSeconds(now));
    }

    /// <summary>
    /// Tiempo por DÍA y por item de un desarrollador, en un rango de fechas locales (inclusive).
    /// Se calcula de los tramos registrados (<see cref="WorkInterval"/>): es exacto por día aunque
    /// una sesión abarque varios. Solo cubre lo trabajado a partir de que existe esta bitácora.
    /// </summary>
    public async Task<List<DayTotal>> ResumenPorDiaAsync(int devId, DateTime desdeLocal, DateTime hastaLocal,
        CancellationToken ct = default)
    {
        var d0 = desdeLocal.Date;
        var d1 = hastaLocal.Date;

        var grupos = await db.WorkIntervals
            .Where(w => w.DeveloperId == devId && w.LocalDate >= d0 && w.LocalDate <= d1)
            .GroupBy(w => new { w.LocalDate, w.RequirementId, w.ActivityId })
            .Select(g => new { g.Key.LocalDate, g.Key.RequirementId, g.Key.ActivityId, Seconds = g.Sum(x => x.Seconds) })
            .ToListAsync(ct);

        var reqIds = grupos.Where(x => x.RequirementId != null).Select(x => x.RequirementId!.Value).Distinct().ToList();
        var actIds = grupos.Where(x => x.ActivityId != null).Select(x => x.ActivityId!.Value).Distinct().ToList();
        var reqTitles = (await db.Requirements.Where(r => reqIds.Contains(r.Id)).Select(r => new { r.Id, r.Title }).ToListAsync(ct))
            .ToDictionary(r => r.Id, r => r.Title);
        var actTitles = (await db.DevActivities.Where(a => actIds.Contains(a.Id)).Select(a => new { a.Id, a.Title }).ToListAsync(ct))
            .ToDictionary(a => a.Id, a => a.Title);

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
