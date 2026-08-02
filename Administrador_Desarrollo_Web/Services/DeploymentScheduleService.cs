using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Agenda de despliegues y su ejecución a la hora indicada.
///
/// Quién ejecuta: la aplicación abierta (también la que quedó en la bandeja). Para que dos
/// instancias abiertas no lancen el mismo despliegue por duplicado, la ejecución se TOMA primero
/// con una actualización condicional en la base: solo quien logra pasar el registro de
/// <see cref="ScheduledDeploymentStatus.Programado"/> a EnEjecucion lo ejecuta.
/// </summary>
public class DeploymentScheduleService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly AuditService _audit;
    private readonly DeploymentService _deploy;
    private readonly DbContextOptions<AppDbContext> _dbOptions;

    public DeploymentScheduleService(AppDbContext db, ICurrentUser currentUser, AuditService audit,
        DeploymentService deploy, DbContextOptions<AppDbContext> dbOptions)
    {
        _db = db; _currentUser = currentUser; _audit = audit; _deploy = deploy; _dbOptions = dbOptions;
    }

    private static string EsteEquipo => $"{Environment.MachineName}\\{Environment.UserName}";

    // ── Agenda ──────────────────────────────────────────────────────────────────

    public (bool ok, string mensaje, ScheduledDeployment? cita) Programar(
        int releaseId, int profileId, DateTime cuandoLocal, int toleranciaMinutos, string? notas)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(_currentUser);

        if (cuandoLocal <= DateTime.Now.AddMinutes(1))
            return (false, "La hora programada debe ser al menos un minuto en el futuro.", null);
        if (toleranciaMinutos is < 5 or > 1440)
            return (false, "La tolerancia debe estar entre 5 minutos y 24 horas.", null);

        var profile = _db.DeploymentProfiles.Find(profileId);
        if (profile == null) return (false, "El perfil no existe.", null);

        // Misma regla que al desplegar en vivo: Operaciones solo perfiles habilitados.
        if (!_currentUser.IsAdmin && !profile.AllowedForOperaciones)
        {
            _audit.RecordDenied(AuditAction.Deploy, "DeploymentProfile", profileId.ToString(),
                $"Intento de programar el perfil «{profile.Name}», no habilitado para Operaciones.");
            throw new AuthorizationException($"El perfil «{profile.Name}» no está habilitado para Operaciones.");
        }

        if (!_db.AppReleases.Any(r => r.Id == releaseId))
            return (false, "La versión no existe.", null);

        var cita = new ScheduledDeployment
        {
            AppReleaseId = releaseId,
            DeploymentProfileId = profileId,
            ScheduledAtUtc = cuandoLocal.ToUniversalTime(),
            ToleranciaMinutos = toleranciaMinutos,
            Status = ScheduledDeploymentStatus.Programado,
            Notes = string.IsNullOrWhiteSpace(notas) ? null : notas.Trim(),
            CreatedByUserId = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow
        };
        _db.ScheduledDeployments.Add(cita);
        _db.SaveChanges();

        _audit.RecordDetailed(AuditAction.Deploy, "ScheduledDeployment", cita.Id.ToString(),
            $"Despliegue programado para {cuandoLocal:dd/MM/yyyy HH:mm} (perfil «{profile.Name}»)",
            AuditOutcome.Exito, newValues: new { cita.AppReleaseId, cita.DeploymentProfileId, cita.ScheduledAtUtc });

        return (true, $"Despliegue programado para {cuandoLocal:dd/MM/yyyy HH:mm}.", cita);
    }

    /// <summary>
    /// Agenda un despliegue a una selección DIRECTA de servidores (no un perfil). Congela los servidores
    /// elegidos en un perfil interno oculto para que la cita se dispare exactamente a ésos. Admin u
    /// Operaciones (Operaciones también elige servidores, igual que en el despliegue en vivo).
    /// </summary>
    public (bool ok, string mensaje, ScheduledDeployment? cita) ProgramarServidores(
        int releaseId, IReadOnlyList<int> targetIds, DateTime cuandoLocal, int toleranciaMinutos, string? notas)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(_currentUser);

        if (cuandoLocal <= DateTime.Now.AddMinutes(1))
            return (false, "La hora programada debe ser al menos un minuto en el futuro.", null);
        if (toleranciaMinutos is < 5 or > 1440)
            return (false, "La tolerancia debe estar entre 5 minutos y 24 horas.", null);
        if (targetIds.Count == 0)
            return (false, "Selecciona al menos un servidor.", null);
        if (!_db.AppReleases.Any(r => r.Id == releaseId))
            return (false, "La versión no existe.", null);

        // Etiqueta con los nombres de los servidores, para reconocerla en la lista de programados.
        var nombres = _db.DeploymentTargets.Where(t => targetIds.Contains(t.Id)).Select(t => t.Nombre).ToList();
        if (nombres.Count == 0) return (false, "Los servidores seleccionados ya no existen.", null);
        var etiqueta = "⏱ " + string.Join(", ", nombres.Take(3)) + (nombres.Count > 3 ? $" (+{nombres.Count - 3})" : "");
        if (etiqueta.Length > 190) etiqueta = etiqueta[..190];

        int profileId;
        try { profileId = _deploy.CrearPerfilCongelado(targetIds, etiqueta); }
        catch (InvalidOperationException ex) { return (false, ex.Message, null); }

        var cita = new ScheduledDeployment
        {
            AppReleaseId = releaseId,
            DeploymentProfileId = profileId,
            ScheduledAtUtc = cuandoLocal.ToUniversalTime(),
            ToleranciaMinutos = toleranciaMinutos,
            Status = ScheduledDeploymentStatus.Programado,
            Notes = string.IsNullOrWhiteSpace(notas) ? null : notas.Trim(),
            CreatedByUserId = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow
        };
        _db.ScheduledDeployments.Add(cita);
        _db.SaveChanges();

        _audit.RecordDetailed(AuditAction.Deploy, "ScheduledDeployment", cita.Id.ToString(),
            $"Despliegue programado para {cuandoLocal:dd/MM/yyyy HH:mm} ({nombres.Count} servidor(es) directos)",
            AuditOutcome.Exito, newValues: new { cita.AppReleaseId, servidores = targetIds.Count, cita.ScheduledAtUtc });

        return (true, $"Despliegue programado para {cuandoLocal:dd/MM/yyyy HH:mm}.", cita);
    }

    public (bool ok, string mensaje) Cancelar(int citaId)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(_currentUser);
        var cita = _db.ScheduledDeployments.Find(citaId);
        if (cita == null) return (false, "La programación ya no existe.");
        if (cita.Status != ScheduledDeploymentStatus.Programado)
            return (false, $"No se puede cancelar: está en estado {cita.Status}.");

        cita.Status = ScheduledDeploymentStatus.Cancelado;
        _db.SaveChanges();
        _audit.RecordDetailed(AuditAction.Deploy, "ScheduledDeployment", citaId.ToString(),
            "Programación cancelada", AuditOutcome.Exito);
        return (true, "Programación cancelada.");
    }

    public List<ScheduledDeployment> Listar(bool soloPendientes = false)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(_currentUser);
        var q = _db.ScheduledDeployments
            .Include(s => s.AppRelease).ThenInclude(r => r.AppSystem)
            .Include(s => s.Profile)
            .AsQueryable();
        if (soloPendientes) q = q.Where(s => s.Status == ScheduledDeploymentStatus.Programado);
        return q.OrderBy(s => s.ScheduledAtUtc).AsNoTracking().ToList();
    }

    // ── Ejecución ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Marca como perdidas las citas cuya tolerancia ya expiró. Se hace ANTES de tomar ninguna:
    /// una cita de anoche no debe dispararse al abrir la aplicación por la mañana.
    /// </summary>
    public List<ScheduledDeployment> MarcarPerdidas(DateTime? nowUtc = null)
    {
        var ahora = nowUtc ?? DateTime.UtcNow;
        var perdidas = _db.ScheduledDeployments
            .Where(s => s.Status == ScheduledDeploymentStatus.Programado)
            .ToList()
            .Where(s => s.SePerdio(ahora))
            .ToList();

        foreach (var c in perdidas)
        {
            c.Status = ScheduledDeploymentStatus.Perdido;
            c.ResultMessage = $"No se ejecutó: no había ninguna aplicación abierta dentro de los {c.ToleranciaMinutos} minutos de tolerancia.";
        }
        if (perdidas.Count > 0)
        {
            _db.SaveChanges();
            foreach (var c in perdidas)
                _audit.RecordDetailed(AuditAction.Deploy, "ScheduledDeployment", c.Id.ToString(),
                    c.ResultMessage!, AuditOutcome.Fallo);
        }
        return perdidas;
    }

    /// <summary>
    /// Intenta TOMAR una cita cuya hora ya llegó. Devuelve null si no hay ninguna o si otra
    /// instancia se adelantó. La toma es condicional en la base para evitar ejecuciones dobles.
    /// </summary>
    public ScheduledDeployment? TomarSiguiente(DateTime? nowUtc = null)
    {
        var ahora = nowUtc ?? DateTime.UtcNow;

        var candidata = _db.ScheduledDeployments
            .Where(s => s.Status == ScheduledDeploymentStatus.Programado && s.ScheduledAtUtc <= ahora)
            .OrderBy(s => s.ScheduledAtUtc)
            .FirstOrDefault();
        if (candidata == null || candidata.SePerdio(ahora)) return null;

        // UPDATE ... WHERE Status = Programado: si otra instancia ya lo tomó, afecta 0 filas.
        int filas = _db.Database.ExecuteSql(
            $@"UPDATE ScheduledDeployments
                  SET Status = {(int)ScheduledDeploymentStatus.EnEjecucion},
                      ClaimedBy = {EsteEquipo},
                      ClaimedAtUtc = {ahora}
                WHERE Id = {candidata.Id}
                  AND Status = {(int)ScheduledDeploymentStatus.Programado}");

        if (filas == 0) return null;   // otro se adelantó

        _db.Entry(candidata).Reload();
        return candidata;
    }

    /// <summary>
    /// Ejecuta una cita ya tomada. Usa contexto propio: corre desde un temporizador, no desde la UI.
    /// </summary>
    public async Task<(bool ok, string mensaje)> EjecutarAsync(
        int citaId, IProgress<string> progress, CancellationToken ct = default)
    {
        await using var db = new AppDbContext(_dbOptions);
        var cita = await db.ScheduledDeployments.FindAsync([citaId], ct);
        if (cita == null) return (false, "La programación ya no existe.");

        try
        {
            // Un despliegue programado no tiene checklist humano: nadie estaba frente a la
            // pantalla. Aun así deja evidencia AUTOMÁTICA — quién lo agendó, para cuándo y con qué
            // notas — o el expediente lo confundiría con un despliegue anterior a la función y le
            // atribuiría la decisión a quien solo tenía la aplicación abierta a esa hora.
            var quienAgendo = cita.CreatedByUserId is int uid
                ? await db.Users.AsNoTracking().Where(u => u.Id == uid)
                      .Select(u => string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName)
                      .FirstOrDefaultAsync(ct) ?? $"usuario #{uid}"
                : "(desconocido)";
            var lineas = new List<string>
            {
                "DESPLIEGUE PROGRAMADO (sin checklist: se ejecutó solo, sin nadie delante)",
                $"Agendado por : {quienAgendo}",
                $"Agendado el  : {cita.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}",
                $"Programado a : {cita.ScheduledAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}",
                $"Ejecutado el : {DateTime.Now:dd/MM/yyyy HH:mm}"
            };
            if (!string.IsNullOrWhiteSpace(cita.Notes))
                lineas.Add($"{Environment.NewLine}Nota de la programación: {cita.Notes.Trim()}");
            var evidencia = string.Join(Environment.NewLine, lineas);

            var job = await _deploy.DeployAsync(cita.AppReleaseId, cita.DeploymentProfileId, progress, ct,
                evidenciaChecklist: evidencia);

            cita.DeploymentJobId = job.Id;
            cita.Status = job.Status == JobStatus.Completado
                ? ScheduledDeploymentStatus.Completado
                : ScheduledDeploymentStatus.Fallido;
            cita.ResultMessage = $"Job #{job.Id}: {job.Status} — {job.TargetsOk} OK, {job.TargetsFailed} fallidos.";
            await db.SaveChangesAsync(CancellationToken.None);

            return (cita.Status == ScheduledDeploymentStatus.Completado, cita.ResultMessage);
        }
        catch (Exception ex)
        {
            cita.Status = ScheduledDeploymentStatus.Fallido;
            cita.ResultMessage = $"Error: {ex.Message}";
            await db.SaveChangesAsync(CancellationToken.None);
            return (false, cita.ResultMessage);
        }
    }
}
