using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Objetivo de un SLA: un requerimiento asignado o una actividad libre.</summary>
public readonly record struct SlaTarget(int? RequirementId, int? ActivityId)
{
    public static SlaTarget Requerimiento(int id) => new(id, null);
    public static SlaTarget Actividad(int id) => new(null, id);
    public bool EsValido => RequirementId.HasValue ^ ActivityId.HasValue;
}

/// <summary>
/// Compromisos de atención (SLA) con recordatorios para comentar el ticket de Azure DevOps.
///
/// Reparto de responsabilidades:
///  · solo el administrador asigna, ajusta o cancela un SLA;
///  · el desarrollador solo puede comentar el ticket de los suyos, y eso reprograma el recordatorio;
///  · el vencimiento no lo decide la UI: se calcula contra la fecha límite guardada.
///
/// Las horas se guardan en UTC. La UI convierte a hora local al mostrarlas — mezclarlas es la vía
/// rápida a recordatorios que se disparan con horas de diferencia.
/// </summary>
public class SlaService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly AuditService _audit;
    private readonly AzureDevOpsService _devops;

    public SlaService(AppDbContext db, ICurrentUser currentUser, AuditService audit, AzureDevOpsService devops)
    {
        _db = db; _currentUser = currentUser; _audit = audit; _devops = devops;
    }

    // ── Alta y mantenimiento (administrador) ────────────────────────────────────

    public (bool ok, string mensaje, SlaCommitment? sla) Asignar(
        SlaTarget objetivo, int developerId, DateTime dueAtLocal,
        int reminderEveryHours, int? devOpsTicketId, string? url, string? notas)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        if (!objetivo.EsValido)
            return (false, "El SLA debe apuntar a un requerimiento o a una actividad, no a ambos.", null);
        if (dueAtLocal <= DateTime.Now)
            return (false, "La fecha límite ya pasó. Elige una futura.", null);
        if (reminderEveryHours is < 0 or > 720)
            return (false, "El intervalo de recordatorio debe estar entre 0 y 720 horas (30 días).", null);
        if (!_db.Developers.Any(d => d.Id == developerId))
            return (false, "El desarrollador indicado no existe.", null);

        // Un objetivo con SLA vigente no admite otro: dos fechas límite simultáneas no significan nada.
        bool yaTiene = _db.SlaCommitments.Any(s =>
            s.Status == SlaStatus.Activo &&
            s.RequirementId == objetivo.RequirementId && s.ActivityId == objetivo.ActivityId);
        if (yaTiene)
            return (false, "Ese objetivo ya tiene un SLA activo. Ciérralo o cancélalo antes de asignar otro.", null);

        var dueUtc = dueAtLocal.ToUniversalTime();
        var sla = new SlaCommitment
        {
            RequirementId = objetivo.RequirementId,
            ActivityId = objetivo.ActivityId,
            DeveloperId = developerId,
            DevOpsTicketExternalId = devOpsTicketId,
            DevOpsTicketUrl = string.IsNullOrWhiteSpace(url) ? null : url.Trim(),
            DueAtUtc = dueUtc,
            ReminderEveryHours = reminderEveryHours,
            NextReminderAtUtc = PrimerRecordatorio(DateTime.UtcNow, dueUtc, reminderEveryHours),
            Status = SlaStatus.Activo,
            Notes = string.IsNullOrWhiteSpace(notas) ? null : notas.Trim(),
            CreatedByUserId = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow
        };
        _db.SlaCommitments.Add(sla);
        _db.SaveChanges();

        _audit.Record(AuditAction.Create, "SlaCommitment", sla.Id.ToString(),
            $"SLA asignado (vence {dueAtLocal:dd/MM/yyyy HH:mm}, recordatorio cada {reminderEveryHours}h)");
        return (true, "SLA asignado.", sla);
    }

    /// <summary>
    /// El primer recordatorio nunca se programa después del vencimiento: si el intervalo es más
    /// largo que el plazo, se recuerda al vencer, no nunca. Es público y estático porque el SLA
    /// automático por prioridad (en <see cref="AzureDevOpsService.MaterializarAsignados"/>) reutiliza
    /// exactamente esta misma regla al crear los compromisos.
    /// </summary>
    public static DateTime? PrimerRecordatorio(DateTime nowUtc, DateTime dueUtc, int cadaHoras)
    {
        if (cadaHoras <= 0) return dueUtc;
        var siguiente = nowUtc.AddHours(cadaHoras);
        return siguiente > dueUtc ? dueUtc : siguiente;
    }

    public (bool ok, string mensaje) MarcarCumplido(int slaId, string? nota = null)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        var sla = _db.SlaCommitments.Find(slaId);
        if (sla == null) return (false, "El SLA ya no existe.");
        if (sla.Status is SlaStatus.Cumplido or SlaStatus.Cancelado) return (false, $"El SLA ya está {sla.Status}.");

        sla.Status = SlaStatus.Cumplido;
        sla.NextReminderAtUtc = null;   // deja de molestar
        if (!string.IsNullOrWhiteSpace(nota)) sla.Notes = $"{nota.Trim()}\n{sla.Notes}".Trim();
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "SlaCommitment", slaId.ToString(), "SLA marcado como cumplido");
        return (true, "SLA marcado como cumplido.");
    }

    public (bool ok, string mensaje) Cancelar(int slaId)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        var sla = _db.SlaCommitments.Find(slaId);
        if (sla == null) return (false, "El SLA ya no existe.");
        if (sla.Status == SlaStatus.Cancelado) return (false, "El SLA ya estaba cancelado.");

        sla.Status = SlaStatus.Cancelado;
        sla.NextReminderAtUtc = null;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "SlaCommitment", slaId.ToString(), "SLA cancelado");
        return (true, "SLA cancelado.");
    }

    // ── Consultas ───────────────────────────────────────────────────────────────

    private IQueryable<SlaCommitment> ConDetalle() =>
        _db.SlaCommitments
            .Include(s => s.Requirement)
            .Include(s => s.Activity)
            .Include(s => s.Developer);

    /// <summary>
    /// Cierra los SLA cuyo ticket de DevOps ya terminó, antes de leer. Se hace en la LECTURA a
    /// propósito: es lo que hace que la pantalla se corrija sola al abrirla, sin depender de que
    /// alguien lance una sincronización. Solo mira datos ya sincronizados en local — no toca la red.
    ///
    /// Con <paramref name="developerId"/> se acota a esa persona: refrescar «Mis SLA» no debe
    /// reescribir los compromisos del resto del equipo.
    /// </summary>
    public int ReconciliarConDevOps(int? developerId = null, DateTime? nowUtc = null)
    {
        var cerrados = SlaDevOpsReconciler.Aplicar(_db, developerId, nowUtc ?? DateTime.UtcNow);
        if (cerrados.Count == 0) return 0;

        _audit.Record(AuditAction.Update, "SlaCommitment",
            string.Join(",", cerrados.Select(c => c.SlaId)),
            $"{cerrados.Count} SLA cerrado(s) automáticamente por el estado de su ticket en DevOps: " +
            string.Join("; ", cerrados.Select(c => $"#{c.TicketExternalId} «{c.EstadoTicket}» → {c.Nuevo}")));
        return cerrados.Count;
    }

    /// <summary>SLA vigentes de un desarrollador (los suyos, o cualquiera si es admin).</summary>
    public List<SlaCommitment> DeDesarrollador(int developerId, bool soloActivos = true)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, developerId);
        ReconciliarConDevOps(developerId);
        var q = ConDetalle().Where(s => s.DeveloperId == developerId);
        if (soloActivos) q = q.Where(s => s.Status == SlaStatus.Activo);
        return q.OrderBy(s => s.DueAtUtc).AsNoTracking().ToList();
    }

    public List<SlaCommitment> Todos(SlaStatus? estado = null, int? developerId = null)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        ReconciliarConDevOps(developerId);
        var q = ConDetalle();
        if (estado is SlaStatus e) q = q.Where(s => s.Status == e);
        if (developerId is int d) q = q.Where(s => s.DeveloperId == d);
        return q.OrderBy(s => s.DueAtUtc).AsNoTracking().ToList();
    }

    /// <summary>
    /// Lo que hay que avisarle YA a un desarrollador: recordatorio vencido, o SLA pasado de fecha.
    /// </summary>
    public List<SlaCommitment> PendientesDeAviso(int developerId, DateTime? nowUtc = null)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, developerId);
        var ahora = nowUtc ?? DateTime.UtcNow;
        // Antes de molestar a nadie: un ticket ya cerrado en DevOps no genera recordatorio.
        ReconciliarConDevOps(developerId, ahora);
        return ConDetalle()
            .Where(s => s.DeveloperId == developerId && s.Status == SlaStatus.Activo)
            .AsNoTracking()
            .AsEnumerable()
            .Where(s => s.TocaRecordar(ahora) || s.EstaVencido(ahora))
            .OrderBy(s => s.DueAtUtc)
            .ToList();
    }

    // ── Vencimientos ────────────────────────────────────────────────────────────

    /// <summary>
    /// Marca como Vencido lo que pasó de fecha y devuelve lo que aún no se le ha reportado al
    /// administrador, para que quien llame mande el correo y luego confirme con
    /// <see cref="MarcarIncumplimientoNotificado"/>. Separar ambos pasos evita dar por avisado algo
    /// cuyo correo falló.
    /// </summary>
    public List<SlaCommitment> RevisarVencimientos(DateTime? nowUtc = null)
    {
        var ahora = nowUtc ?? DateTime.UtcNow;

        // Primero se cierra lo que DevOps ya dio por terminado. Sin este paso, un ticket entregado a
        // tiempo acababa marcado como vencido y escalado al jefe como incumplimiento — es el correo
        // más caro que puede mandar la aplicación, porque acusa a alguien que sí cumplió.
        ReconciliarConDevOps(null, ahora);

        var vencidos = _db.SlaCommitments
            .Where(s => s.Status == SlaStatus.Activo && s.DueAtUtc < ahora)
            .ToList();

        foreach (var s in vencidos)
        {
            s.Status = SlaStatus.Vencido;
            s.NextReminderAtUtc = null;
        }
        if (vencidos.Count > 0)
        {
            _db.SaveChanges();
            _audit.Record(AuditAction.Update, "SlaCommitment",
                string.Join(",", vencidos.Select(v => v.Id)), $"{vencidos.Count} SLA vencido(s)");
        }

        var ids = vencidos.Select(v => v.Id).ToList();
        return ConDetalle()
            .Where(s => ids.Contains(s.Id) && s.BreachNotifiedAtUtc == null)
            .AsNoTracking()
            .ToList();
    }

    public void MarcarIncumplimientoNotificado(IEnumerable<int> slaIds)
    {
        var ids = slaIds.ToList();
        if (ids.Count == 0) return;
        foreach (var s in _db.SlaCommitments.Where(s => ids.Contains(s.Id)))
            s.BreachNotifiedAtUtc = DateTime.UtcNow;
        _db.SaveChanges();
    }

    /// <summary>Aplaza el recordatorio sin comentar (posponer). No cambia la fecha límite.</summary>
    public (bool ok, string mensaje) Posponer(int slaId, int horas)
    {
        var sla = _db.SlaCommitments.Find(slaId);
        if (sla == null) return (false, "El SLA ya no existe.");
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, sla.DeveloperId);
        if (sla.Status != SlaStatus.Activo) return (false, $"El SLA está {sla.Status}.");
        if (horas is < 1 or > 72) return (false, "Solo se puede posponer entre 1 y 72 horas.");

        // Nunca más allá del vencimiento: posponer no debe servir para saltarse el SLA.
        var propuesto = DateTime.UtcNow.AddHours(horas);
        sla.NextReminderAtUtc = propuesto > sla.DueAtUtc ? sla.DueAtUtc : propuesto;
        _db.SaveChanges();
        return (true, $"Recordatorio pospuesto {horas}h.");
    }

    // ── Comentar el ticket en DevOps ────────────────────────────────────────────

    /// <summary>
    /// Publica un comentario en el work item de Azure DevOps ligado al SLA y reprograma el
    /// siguiente recordatorio. Si DevOps falla, NO se marca nada como comentado: la constancia
    /// solo cuenta si de verdad quedó en el ticket.
    /// </summary>
    public async Task<(bool ok, string mensaje)> ComentarTicketAsync(int slaId, string texto,
        IReadOnlyList<(byte[] bytes, string fileName)>? evidencias = null, CancellationToken ct = default)
    {
        var sla = _db.SlaCommitments.Include(s => s.Developer).FirstOrDefault(s => s.Id == slaId);
        if (sla == null) return (false, "El SLA ya no existe.");
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, sla.DeveloperId);

        if (sla.DevOpsTicketExternalId is not int ticket)
            return (false, "Este SLA no tiene un ticket de Azure DevOps ligado.");
        texto = (texto ?? "").Trim();
        bool hayEvidencias = evidencias is { Count: > 0 };
        if (texto.Length == 0 && !hayEvidencias) return (false, "Escribe el comentario o adjunta una evidencia.");

        try
        {
            if (hayEvidencias)
                await _devops.PostCommentWithEvidenceAsync(ticket, texto, evidencias!, ct);
            else
                await _devops.PostCommentAsync(ticket, texto, ct);
        }
        catch (Exception ex)
        {
            return (false, $"No se pudo publicar el comentario en DevOps:\n{ex.Message}");
        }

        var ahora = DateTime.UtcNow;
        sla.LastCommentAtUtc = ahora;
        sla.CommentCount++;
        sla.NextReminderAtUtc = sla.Status == SlaStatus.Activo
            ? PrimerRecordatorio(ahora, sla.DueAtUtc, sla.ReminderEveryHours)
            : null;
        _db.SaveChanges();

        _audit.Record(AuditAction.Update, "SlaCommitment", sla.Id.ToString(),
            $"Comentario publicado en el work item #{ticket}");
        return (true, $"Comentario publicado en el ticket #{ticket}.");
    }

    /// <summary>Texto legible del objetivo, para avisos y correos.</summary>
    public static string DescribirObjetivo(SlaCommitment s) =>
        s.Requirement != null ? $"Requerimiento #{s.Requirement.Id} — {s.Requirement.Title}"
        : s.Activity != null  ? $"Actividad — {s.Activity.Title}"
        : "(objetivo desconocido)";
}
