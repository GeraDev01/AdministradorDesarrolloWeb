using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Sla;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>Objetivo de un SLA: un requerimiento asignado o una actividad libre.</summary>
public readonly record struct SlaObjetivo(int? RequirementId, int? ActivityId)
{
    public static SlaObjetivo Requerimiento(int id) => new(id, null);
    public static SlaObjetivo Actividad(int id) => new(null, id);

    /// <summary>Exactamente uno de los dos, nunca ambos ni ninguno.</summary>
    public bool EsValido => RequirementId.HasValue ^ ActivityId.HasValue;
}

/// <summary>
/// Compromisos de atención (SLA) con recordatorios para comentar el ticket de Azure DevOps.
///
/// <para>Reparto de responsabilidades, igual que en el escritorio:</para>
/// <list type="bullet">
///   <item>solo el líder asigna, cierra o cancela un SLA;</item>
///   <item>el desarrollador solo puede posponer el recordatorio de los suyos;</item>
///   <item>el vencimiento no lo decide la pantalla: se calcula contra la fecha límite guardada.</item>
/// </list>
///
/// <para><b>Las horas se guardan y se comparan en UTC, y aquí eso importa más que en el escritorio.</b>
/// Allí «ahora» era el reloj de quien miraba la pantalla y usar la hora local era inofensivo. Aquí el
/// proceso corre en un servidor que bien puede estar en UTC: comparar contra su hora local haría que
/// un mismo compromiso se considerara vencido o no según dónde esté hospedada la aplicación. Por eso
/// los métodos reciben y devuelven UTC, y la conversión a hora local es cosa de la pantalla.</para>
///
/// <para><b>La constancia solo cuenta si de verdad quedó en el ticket.</b> Es la regla que gobierna
/// <see cref="RegistrarAvanceAsync"/>: el avance se registra COMENTANDO el work item en Azure DevOps
/// y solo si DevOps aceptó el comentario se apunta y se reprograma el recordatorio. «Posponer» sigue
/// existiendo para el «estoy en ello», pero no cuenta como constancia; y el compromiso se cierra solo
/// cuando el ticket se cierra (<see cref="SlaDevOpsReconciler"/>).</para>
/// </summary>
public class SlaService(
    AppDbContext db, ICurrentUser currentUser, AuditService audit, SettingsService configuracion,
    DevOpsService devops)
{
    // ── Alta y mantenimiento (líder) ────────────────────────────────────────────

    /// <summary>
    /// Da de alta un compromiso. Devuelve el motivo del rechazo en vez de lanzar: casi todos los
    /// motivos son datos que una persona acaba de capturar en un formulario.
    /// </summary>
    /// <param name="venceUtc">Fecha y hora límite, ya en UTC.</param>
    /// <param name="recordatorioCadaHoras">0 = avisar solo al vencer.</param>
    public async Task<(bool ok, string mensaje, int? id)> AsignarAsync(
        SlaObjetivo objetivo, int developerId, DateTime venceUtc, int recordatorioCadaHoras,
        int? ticketExternalId, string? url, string? notas, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var ahora = DateTime.UtcNow;
        if (!objetivo.EsValido)
            return (false, "El SLA debe apuntar a un requerimiento o a una actividad, no a ambos.", null);
        if (venceUtc <= ahora)
            return (false, "La fecha límite ya pasó. Elige una futura.", null);
        if (recordatorioCadaHoras is < 0 or > 720)
            return (false, "El intervalo de recordatorio debe estar entre 0 y 720 horas (30 días).", null);
        if (ticketExternalId is int t && t <= 0)
            return (false, "El ticket debe ser el número del work item de Azure DevOps.", null);
        if (!await db.Developers.AnyAsync(d => d.Id == developerId, ct))
            return (false, "El desarrollador indicado no existe.", null);

        if (objetivo.RequirementId is int reqId && !await db.Requirements.AnyAsync(r => r.Id == reqId, ct))
            return (false, "Ese requerimiento ya no existe. Actualiza la lista.", null);
        if (objetivo.ActivityId is int actId && !await db.DevActivities.AnyAsync(a => a.Id == actId, ct))
            return (false, "Esa actividad ya no existe. Actualiza la lista.", null);

        // Un objetivo con SLA vigente no admite otro: dos fechas límite simultáneas no significan nada.
        bool yaTiene = await db.SlaCommitments.AnyAsync(s =>
            s.Status == SlaStatus.Activo &&
            s.RequirementId == objetivo.RequirementId && s.ActivityId == objetivo.ActivityId, ct);
        if (yaTiene)
            return (false, "Ese objetivo ya tiene un SLA activo. Ciérralo o cancélalo antes de asignar otro.", null);

        var (urlOk, urlLimpia) = SanearUrl(url);
        if (!urlOk)
            return (false, "La URL del ticket debe empezar por http:// o https://.", null);

        var sla = new SlaCommitment
        {
            RequirementId = objetivo.RequirementId,
            ActivityId = objetivo.ActivityId,
            DeveloperId = developerId,
            DevOpsTicketExternalId = ticketExternalId,
            DevOpsTicketUrl = urlLimpia,
            DueAtUtc = venceUtc,
            ReminderEveryHours = recordatorioCadaHoras,
            NextReminderAtUtc = PrimerRecordatorio(ahora, venceUtc, recordatorioCadaHoras),
            Status = SlaStatus.Activo,
            Notes = string.IsNullOrWhiteSpace(notas) ? null : notas.Trim(),
            CreatedByUserId = currentUser.UserId,
            CreatedAt = ahora
        };
        db.SlaCommitments.Add(sla);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Create, "SlaCommitment", sla.Id.ToString(),
            $"SLA asignado (vence {venceUtc:dd/MM/yyyy HH:mm} UTC, recordatorio cada {recordatorioCadaHoras}h)", ct);
        return (true, "SLA asignado.", sla.Id);
    }

    /// <summary>
    /// El primer recordatorio nunca se programa después del vencimiento: si el intervalo es más largo
    /// que el plazo, se recuerda al vencer, no nunca. Es público y estático porque el SLA automático
    /// por prioridad reutilizará exactamente esta misma regla al crear los compromisos.
    /// </summary>
    public static DateTime? PrimerRecordatorio(DateTime nowUtc, DateTime dueUtc, int cadaHoras)
    {
        if (cadaHoras <= 0) return dueUtc;
        var siguiente = nowUtc.AddHours(cadaHoras);
        return siguiente > dueUtc ? dueUtc : siguiente;
    }

    /// <summary>Da el compromiso por atendido. Deja de recordar y deja de contar para el escalamiento.</summary>
    public async Task<(bool ok, string mensaje)> MarcarCumplidoAsync(
        int slaId, string? nota = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var sla = await db.SlaCommitments.FirstOrDefaultAsync(s => s.Id == slaId, ct);
        if (sla == null) return (false, "El SLA ya no existe.");
        if (sla.Status is SlaStatus.Cumplido or SlaStatus.Cancelado) return (false, $"El SLA ya está {sla.Status}.");

        sla.Status = SlaStatus.Cumplido;
        sla.NextReminderAtUtc = null;   // deja de molestar
        if (!string.IsNullOrWhiteSpace(nota)) sla.Notes = $"{nota.Trim()}\n{sla.Notes}".Trim();
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "SlaCommitment", slaId.ToString(), "SLA marcado como cumplido", ct);
        return (true, "SLA marcado como cumplido.");
    }

    /// <summary>Deja el compromiso sin efecto. No cuenta como incumplimiento.</summary>
    public async Task<(bool ok, string mensaje)> CancelarAsync(int slaId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var sla = await db.SlaCommitments.FirstOrDefaultAsync(s => s.Id == slaId, ct);
        if (sla == null) return (false, "El SLA ya no existe.");
        if (sla.Status == SlaStatus.Cancelado) return (false, "El SLA ya estaba cancelado.");

        sla.Status = SlaStatus.Cancelado;
        sla.NextReminderAtUtc = null;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "SlaCommitment", slaId.ToString(), "SLA cancelado", ct);
        return (true, "SLA cancelado.");
    }

    /// <summary>Aplaza el recordatorio sin comentar (posponer). No cambia la fecha límite.</summary>
    public async Task<(bool ok, string mensaje)> PosponerAsync(
        int slaId, int horas, CancellationToken ct = default)
    {
        var sla = await db.SlaCommitments.FirstOrDefaultAsync(s => s.Id == slaId, ct);
        if (sla == null) return (false, "El SLA ya no existe.");

        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, sla.DeveloperId);
        if (sla.Status != SlaStatus.Activo) return (false, $"El SLA está {sla.Status}.");
        if (horas is < 1 or > 72) return (false, "Solo se puede posponer entre 1 y 72 horas.");

        // Nunca más allá del vencimiento: posponer no debe servir para saltarse el SLA.
        var propuesto = DateTime.UtcNow.AddHours(horas);
        sla.NextReminderAtUtc = propuesto > sla.DueAtUtc ? sla.DueAtUtc : propuesto;
        await db.SaveChangesAsync(ct);

        return (true, $"Recordatorio pospuesto {horas}h.");
    }

    /// <summary>
    /// Registra el avance del compromiso dejando constancia en su ticket de Azure DevOps.
    ///
    /// <para><b>Primero el ticket, después el compromiso.</b> El comentario se publica y solo si
    /// DevOps lo aceptó se cuenta la constancia y se reprograma el recordatorio. Al revés —apuntar
    /// primero y publicar después— un fallo de la integración dejaría un compromiso que dice estar
    /// atendido y un ticket donde no consta nada: exactamente lo que este botón existe para evitar.
    /// Si falla, el aviso sigue reclamando con el mensaje que devolvió la integración.</para>
    ///
    /// <para>Quién puede comentar el ticket lo decide <see cref="DevOpsService.ComentarAsync"/>, que
    /// además lo firma con el token de quien lo pide. Aquí solo se comprueba que el compromiso sea
    /// suyo: repetir allí la comprobación del ticket invitaría a que un día se arreglara una sola de
    /// las dos.</para>
    /// </summary>
    /// <param name="evidencias">Capturas que acompañan al comentario. Pueden no venir ninguna.</param>
    public async Task<(bool ok, string mensaje)> RegistrarAvanceAsync(
        int slaId, string? texto, IReadOnlyList<(string nombre, byte[] contenido)> evidencias,
        CancellationToken ct = default)
    {
        var sla = await db.SlaCommitments.FirstOrDefaultAsync(s => s.Id == slaId, ct);
        if (sla == null) return (false, "El SLA ya no existe.");

        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, sla.DeveloperId);

        if (sla.DevOpsTicketExternalId is not int ticket)
            return (false, "Este compromiso no tiene ticket de Azure DevOps ligado, así que no hay " +
                           "dónde dejar constancia. Pídele al líder que lo ligue.");

        var (publicado, mensaje) = await devops.ComentarAsync(ticket, texto, evidencias, ct);
        if (!publicado) return (false, mensaje);

        var ahora = DateTime.UtcNow;
        sla.LastCommentAtUtc = ahora;
        sla.CommentCount++;

        // Un compromiso ya cerrado admite el comentario pero no vuelve a recordar nada: dejarle un
        // próximo recordatorio lo devolvería a la lista de urgentes de su responsable.
        sla.NextReminderAtUtc = sla.Status == SlaStatus.Activo
            ? PrimerRecordatorio(ahora, sla.DueAtUtc, sla.ReminderEveryHours)
            : null;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "SlaCommitment", sla.Id.ToString(),
            $"Avance registrado con un comentario en el work item #{ticket}", ct);

        return (true, sla.Status == SlaStatus.Activo
            ? $"{mensaje} El recordatorio del compromiso se reprogramó."
            : mensaje);
    }

    // ── Reconciliación con DevOps ───────────────────────────────────────────────

    /// <summary>
    /// Cierra los SLA cuyo ticket de DevOps ya terminó y devuelve cuántos cerró.
    ///
    /// Se hace en la LECTURA a propósito: es lo que hace que la pantalla se corrija sola al abrirla,
    /// sin depender de que alguien lance una sincronización. Solo mira datos ya sincronizados en
    /// local — no toca la red.
    /// </summary>
    public async Task<int> ReconciliarConDevOpsAsync(
        int? developerId = null, DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var cerrados = await SlaDevOpsReconciler.AplicarAsync(db, developerId, nowUtc ?? DateTime.UtcNow, ct);
        if (cerrados.Count == 0) return 0;

        await audit.RecordAsync(AuditAction.Update, "SlaCommitment",
            string.Join(",", cerrados.Select(c => c.SlaId)),
            $"{cerrados.Count} SLA cerrado(s) automáticamente por el estado de su ticket en DevOps: " +
            string.Join("; ", cerrados.Select(c => $"#{c.TicketExternalId} «{c.EstadoTicket}» → {c.Nuevo}")), ct);
        return cerrados.Count;
    }

    // ── Consultas para pantalla (DTO, nunca entidades) ──────────────────────────

    /// <summary>Todo lo que pinta la pantalla del líder: los compromisos del filtro y sus tarjetas.</summary>
    public async Task<SlaAdminDto> PantallaDelLiderAsync(SlaStatus? estado, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var ahora = DateTime.UtcNow;
        await ReconciliarConDevOpsAsync(null, ahora, ct);

        var todos = await ConDetalle().AsNoTracking().OrderBy(s => s.DueAtUtc).ToListAsync(ct);
        var filtrados = estado is SlaStatus e ? todos.Where(s => s.Status == e).ToList() : todos;

        // Las tarjetas se calculan sobre TODOS y no sobre el filtro: con «Cumplidos» seleccionado,
        // unas tarjetas en cero dirían que no hay nada vencido, que es justo lo contrario de lo que
        // el líder necesita ver.
        var activos = todos.Where(s => s.Status == SlaStatus.Activo).ToList();
        var resumen = new SlaResumenDto(
            Activos: activos.Count,
            VencenEn24h: activos.Count(s => s.DueAtUtc > ahora && s.DueAtUtc <= ahora.AddHours(24)),
            FueraDePlazo: todos.Count(s => s.Status == SlaStatus.Vencido) + activos.Count(s => s.EstaVencido(ahora)));

        var mensaje = filtrados.Count == 0
            ? "No hay compromisos con ese filtro."
            : $"{filtrados.Count} compromiso(s).";

        return new SlaAdminDto(
            filtrados.Select(s => AVista(s, ahora)).ToList(),
            resumen,
            await PoliticasAsync(ct),
            await configuracion.ObtenerAsync(SettingsService.Claves.SlaEscalationEmail, ct) ?? "",
            mensaje);
    }

    // ── Políticas de SLA automático ─────────────────────────────────────────────

    /// <summary>
    /// Las cuatro políticas por prioridad, siempre completas: lo que falte o esté corrupto en la
    /// configuración se rellena con los valores por defecto (todos desactivados).
    /// </summary>
    public async Task<List<PoliticaSlaDto>> PoliticasAsync(CancellationToken ct = default)
    {
        var json = await configuracion.ObtenerAsync(SlaPolicyStore.ClaveDeConfiguracion, ct);
        return SlaPolicyStore.Parse(json)
            .Select(p => new PoliticaSlaDto(
                p.Priority, SlaPolicyStore.NombrePrioridad(p.Priority), p.Enabled, p.Hours, p.ReminderEveryHours))
            .ToList();
    }

    /// <summary>
    /// Guarda las políticas. Lo que llegue se sanea contra los rangos con sentido antes de escribir,
    /// así que la pantalla no puede grabar un plazo de cero horas ni un recordatorio de un año.
    ///
    /// La guarda de líder la pone <see cref="SettingsService.GuardarAsync"/>, que además lo deja en
    /// la bitácora: es configuración compartida y quien la cambie está cambiando los plazos de todo
    /// el equipo.
    /// </summary>
    public async Task<(bool ok, string mensaje)> GuardarPoliticasAsync(
        IReadOnlyList<PoliticaSlaDto> politicas, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var json = SlaPolicyStore.Serialize(
            politicas.Select(p => new SlaPolicy(p.Prioridad, p.Activa, p.Horas, p.RecordatorioCadaHoras)));

        var (ok, _) = await configuracion.GuardarAsync(SlaPolicyStore.ClaveDeConfiguracion, json, ct);

        // El mensaje de SettingsService habla de claves de configuración («SlaAutoPolicies guardada»),
        // que es su vocabulario y no el de esta pantalla. Aquí se dice lo que la persona hizo.
        return ok
            ? (true, "Políticas de SLA automático guardadas.")
            : (false, "No se pudieron guardar las políticas.");
    }

    /// <summary>Los compromisos de quien tiene la sesión, para «Mis SLA».</summary>
    public async Task<MisSlaDto> MisCompromisosAsync(
        bool incluirCerrados = false, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        if (currentUser.DeveloperId is not int developerId)
            return new MisSlaDto(TieneFicha: false, [], 0,
                "Tu cuenta no está vinculada a una ficha de desarrollador, así que no tiene compromisos asignados.");

        var ahora = DateTime.UtcNow;
        await ReconciliarConDevOpsAsync(developerId, ahora, ct);

        var consulta = ConDetalle().Where(s => s.DeveloperId == developerId);
        if (!incluirCerrados) consulta = consulta.Where(s => s.Status == SlaStatus.Activo);

        var filas = await consulta.OrderBy(s => s.DueAtUtc).AsNoTracking().ToListAsync(ct);
        int urgentes = filas.Count(s => s.EstaVencido(ahora) || s.TocaRecordar(ahora));

        var mensaje = filas.Count == 0
            ? "No tienes compromisos pendientes."
            : urgentes == 0
                ? $"{filas.Count} compromiso(s) en plazo. Nada urgente."
                : $"{urgentes} compromiso(s) requieren que comentes el ticket ahora.";

        return new MisSlaDto(true, filas.Select(s => AVista(s, ahora)).ToList(), urgentes, mensaje);
    }

    /// <summary>Lo que el formulario de alta necesita: responsables y objetivos disponibles.</summary>
    public async Task<OpcionesDeSlaDto> OpcionesAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var desarrolladores = await db.Developers.AsNoTracking()
            .Where(d => d.IsActive).OrderBy(d => d.FullName)
            .Select(d => new OpcionDto(d.Id, d.FullName))
            .ToListAsync(ct);

        // Solo lo que sigue vivo: comprometer una fecha sobre algo ya entregado o cancelado no
        // significa nada, y la lista completa haría inmanejable el desplegable.
        var requerimientos = await db.Requirements.AsNoTracking()
            .Where(r => r.Status != RequirementStatus.Entregado && r.Status != RequirementStatus.Cancelado)
            .OrderByDescending(r => r.Id)
            .Select(r => new
            {
                r.Id, r.Title, r.Source, r.ExternalId, r.ExternalUrl,
                Asignado = r.Assignments.Select(a => (int?)a.DeveloperId).FirstOrDefault()
            })
            .ToListAsync(ct);

        var actividades = await db.DevActivities.AsNoTracking()
            .Where(a => a.Status == DevActivityStatus.Abierta)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new { a.Id, a.Title, a.DeveloperId, Dueno = a.Developer.FullName })
            .ToListAsync(ct);

        return new OpcionesDeSlaDto(
            desarrolladores,
            requerimientos.Select(r => new ObjetivoDeSlaDto(
                r.Id, $"#{r.Id}  {r.Title}", r.Asignado,
                // El ticket se propone solo cuando el requerimiento VINO de DevOps: es justo el caso
                // que motiva el recordatorio de comentar, y en los capturados a mano el ExternalId
                // no es un work item.
                r.Source == RequirementSource.AzureDevOps && int.TryParse(r.ExternalId, out var ext) ? ext : null,
                r.Source == RequirementSource.AzureDevOps ? r.ExternalUrl : null)).ToList(),
            actividades.Select(a => new ObjetivoDeSlaDto(
                a.Id, $"#{a.Id}  {a.Title} ({a.Dueno})", a.DeveloperId, null, null)).ToList());
    }

    // ── Vencimientos y avisos (los usa el trabajo de fondo) ─────────────────────

    /// <summary>
    /// Marca como Vencido lo que acaba de pasarse de fecha y devuelve esos mismos, para que quien
    /// llame avise y luego confirme con <see cref="MarcarIncumplimientoNotificadoAsync"/>. Separar
    /// los dos pasos evita dar por avisado algo cuyo aviso falló.
    ///
    /// <para><b>Devuelve solo lo que marcó en ESTA vuelta</b>, igual que en el escritorio: un
    /// compromiso ya marcado como Vencido deja de ser Activo y no vuelve a entrar por aquí. Conviene
    /// tenerlo claro porque tiene una consecuencia — si el aviso no llega a nadie, ese incumplimiento
    /// no se reintenta. Lo que lo hace inofensivo es que el escalamiento siempre tiene destinatario:
    /// cuando no hay ninguno configurado cae en los líderes activos (ver
    /// <c>SlaNotificationService</c>). Ensanchar la consulta a «todo lo vencido sin notificar» sería
    /// la otra salida, y se descartó a conciencia: en la base real hay años de vencidos que el
    /// escritorio nunca marcó como notificados —no tenía el correo configurado y salía sin
    /// marcarlos—, así que la primera vuelta del trabajo de fondo escalaría de golpe cientos de
    /// incumplimientos de hace meses. Un aviso que llega tarde es un aviso; trescientos son ruido que
    /// se archiva sin leer, y de paso entierran el que sí importaba.</para>
    ///
    /// <para><b>Sin guarda de usuario a propósito:</b> lo llama el trabajo de fondo, que no tiene
    /// sesión detrás, y el endpoint que lo expone al líder ya exige su política. Ver la nota de
    /// <see cref="PendientesDeAvisoDelSistemaAsync"/>.</para>
    /// </summary>
    public async Task<List<SlaCommitment>> RevisarVencimientosAsync(
        DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var ahora = nowUtc ?? DateTime.UtcNow;

        // Primero se cierra lo que DevOps ya dio por terminado. Sin este paso, un ticket entregado a
        // tiempo acababa marcado como vencido y escalado al líder como incumplimiento — es el aviso
        // más caro que puede mandar la aplicación, porque acusa a alguien que sí cumplió.
        await ReconciliarConDevOpsAsync(null, ahora, ct);

        var vencidos = await db.SlaCommitments
            .Where(s => s.Status == SlaStatus.Activo && s.DueAtUtc < ahora)
            .ToListAsync(ct);

        foreach (var s in vencidos)
        {
            s.Status = SlaStatus.Vencido;
            s.NextReminderAtUtc = null;
        }
        if (vencidos.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(AuditAction.Update, "SlaCommitment",
                string.Join(",", vencidos.Select(v => v.Id)), $"{vencidos.Count} SLA vencido(s)", ct);
        }

        var ids = vencidos.Select(v => v.Id).ToList();
        return await ConDetalle()
            .Where(s => ids.Contains(s.Id) && s.BreachNotifiedAtUtc == null)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    /// <summary>Deja constancia de que el incumplimiento ya se escaló, para no repetirlo.</summary>
    public async Task MarcarIncumplimientoNotificadoAsync(
        IEnumerable<int> slaIds, CancellationToken ct = default)
    {
        var ids = slaIds.Distinct().ToList();
        if (ids.Count == 0) return;

        var ahora = DateTime.UtcNow;
        foreach (var s in await db.SlaCommitments.Where(s => ids.Contains(s.Id)).ToListAsync(ct))
            s.BreachNotifiedAtUtc = ahora;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Lo que hay que avisarle YA a alguien: recordatorio vencido, o SLA pasado de fecha. Sin
    /// <paramref name="developerId"/> revisa a todo el equipo.
    ///
    /// <para><b>No lleva guarda de propiedad y no es un olvido.</b> Quien lo llama es el trabajo de
    /// fondo: en el servidor no hay sesión que comprobar, y exigir una obligaría a inventarle al job
    /// una identidad de administrador — que es exactamente la puerta trasera que las guardas existen
    /// para evitar. Lo que sí está protegido es todo lo que puede pedir un navegador: las consultas
    /// de pantalla llevan su guarda y sus endpoints su política.</para>
    /// </summary>
    public async Task<List<SlaCommitment>> PendientesDeAvisoDelSistemaAsync(
        int? developerId = null, DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var ahora = nowUtc ?? DateTime.UtcNow;

        // Antes de molestar a nadie: un ticket ya cerrado en DevOps no genera recordatorio.
        await ReconciliarConDevOpsAsync(developerId, ahora, ct);

        var activos = await ConDetalle()
            .Where(s => s.Status == SlaStatus.Activo)
            .Where(s => developerId == null || s.DeveloperId == developerId)
            .AsNoTracking()
            .ToListAsync(ct);

        return activos
            .Where(s => s.TocaRecordar(ahora) || s.EstaVencido(ahora))
            .OrderBy(s => s.DueAtUtc)
            .ToList();
    }

    // ── Apoyos ──────────────────────────────────────────────────────────────────

    private IQueryable<SlaCommitment> ConDetalle() =>
        db.SlaCommitments
            .Include(s => s.Requirement)
            .Include(s => s.Activity)
            .Include(s => s.Developer);

    /// <summary>Texto legible del objetivo, para las pantallas y los avisos.</summary>
    public static string DescribirObjetivo(SlaCommitment s) =>
        s.Requirement != null ? $"Requerimiento #{s.Requirement.Id} — {s.Requirement.Title}"
        : s.Activity != null ? $"Actividad — {s.Activity.Title}"
        : "(objetivo desconocido)";

    /// <summary>Traduce el compromiso a lo que viaja al navegador, con su estado ya resuelto.</summary>
    private static SlaCompromisoDto AVista(SlaCommitment s, DateTime ahora) => new(
        s.Id,
        s.DeveloperId,
        s.Developer?.FullName ?? "—",
        DescribirObjetivo(s),
        s.DevOpsTicketExternalId,
        s.DevOpsTicketUrl,
        s.DueAtUtc,
        s.Status,
        s.EstaVencido(ahora),
        s.TocaRecordar(ahora),
        s.NextReminderAtUtc,
        s.ReminderEveryHours,
        s.CommentCount,
        s.LastCommentAtUtc,
        s.Notes);

    /// <summary>
    /// Acepta la URL del ticket solo si es http o https.
    ///
    /// <b>Es una comprobación nueva de la web, no una manía.</b> En el escritorio esa dirección la
    /// abría el sistema operativo desde la máquina de quien pulsaba; aquí acaba en un enlace del
    /// navegador de OTRA persona —la responsable del SLA—, así que un «javascript:…» escrito en el
    /// formulario se ejecutaría dentro de su sesión. Se valida al guardar y no al pintar, para que
    /// nunca llegue a existir en la base.
    /// </summary>
    private static (bool ok, string? url) SanearUrl(string? url)
    {
        var limpia = (url ?? "").Trim();
        if (limpia.Length == 0) return (true, null);

        return Uri.TryCreate(limpia, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? (true, limpia)
            : (false, null);
    }
}
