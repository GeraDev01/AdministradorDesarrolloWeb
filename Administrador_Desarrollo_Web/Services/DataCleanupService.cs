using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Lo que necesita una pasada de limpieza: un contexto propio (nunca el compartido de la UI) y de
/// quién es la sesión, para las áreas que tienen que protegerse a sí mismas.
/// </summary>
public sealed record ContextoLimpieza(AppDbContext Db, int? UsuarioActualId, CancellationToken Ct);

/// <summary>
/// Un apartado de la aplicación visto desde la limpieza: cuántos registros tiene y cómo se borran
/// sin dejar la base inconsistente.
/// </summary>
/// <param name="Clave">Identificador estable: es lo que queda escrito en la bitácora.</param>
/// <param name="Grupo">Para agrupar la lista igual que el menú.</param>
/// <param name="Nombre">El apartado, con el nombre que tiene en el menú.</param>
/// <param name="Arrastra">
/// Qué más se lleva por delante. Es la mitad importante de la ficha: nadie espera que borrar
/// «Requerimientos» borre también las horas trabajadas, y enterarse después no sirve de nada.
/// </param>
/// <param name="Advertencia">Si el área merece que se piense dos veces, por qué.</param>
public sealed record AreaLimpieza(
    string Clave,
    string Grupo,
    string Nombre,
    string Arrastra,
    Func<ContextoLimpieza, Task<int>> ContarAsync,
    Func<ContextoLimpieza, Task<int>> BorrarAsync,
    string? Advertencia = null);

/// <summary>Cómo le fue a un área. <paramref name="Filas"/> incluye lo arrastrado, no solo lo principal.</summary>
public sealed record ResultadoLimpieza(string Clave, string Nombre, int Habia, int Filas, string? Error)
{
    public bool Ok => Error == null;
}

/// <summary>
/// Borrado centralizado de datos por apartado. Nace de una necesidad concreta: al preparar la
/// aplicación se cargan registros de prueba en cada pantalla, y limpiarlos después significaba ir
/// pantalla por pantalla borrando de a uno — cuando se podía, porque varias listas no tienen botón
/// de borrar, y siempre en el orden equivocado (la fila se niega a irse porque otra tabla la
/// referencia, sin decir cuál).
///
/// TRES DECISIONES QUE SOSTIENEN ESTO:
///
/// 1. <b>Cada área se hace cargo de sus dependencias.</b> Borrar «Requerimientos» borra primero las
///    asignaciones, adjuntos, sesiones y SLA que cuelgan de ellos, y desliga —no borra— lo que
///    sobrevive por sí solo, como los puntos ganados. Así el orden en que se marquen las áreas no
///    cambia el resultado.
///
/// 2. <b>Contexto propio y transacción por área.</b> El <see cref="AppDbContext"/> inyectado es
///    singleton y lo comparte toda la UI; borrar por debajo lo dejaría con entidades rastreadas que
///    ya no existen. Se abre uno nuevo por área, se envuelve en una transacción —una FK imprevista
///    deja el área intacta, no a medio borrar— y al final se limpia el rastreo del compartido.
///
/// 3. <b>Nada se borra en silencio.</b> Cada área deja su renglón en la bitácora, con cuántos
///    registros había y cuántas filas se fueron.
///
/// LO QUE NO ESTÁ AQUÍ, a propósito: la configuración y los secretos (<c>AppSettings</c>). No son
/// registros que se acumulen probando, y borrarlos deja a la aplicación sin conexión ni credenciales
/// —un estado del que no se sale desde la propia aplicación—.
/// </summary>
public class DataCleanupService
{
    /// <summary>Lo que hay que escribir para confirmar. Verbo, no «sí»: se teclea mirando.</summary>
    public const string FraseConfirmacion = "BORRAR";

    /// <summary>El área de la bitácora, que recibe un trato especial al ordenar la pasada.</summary>
    public const string ClaveBitacora = "bitacora";

    private readonly AppDbContext _db;
    private readonly DbContextOptions<AppDbContext> _dbOptions;
    private readonly AuditService _audit;
    private readonly CurrentUserContext _currentUser;

    public DataCleanupService(AppDbContext db, DbContextOptions<AppDbContext> dbOptions,
        AuditService audit, CurrentUserContext currentUser)
    {
        _db = db; _dbOptions = dbOptions; _audit = audit; _currentUser = currentUser;
    }

    // ── Catálogo ─────────────────────────────────────────────────────────────────────────────
    //
    // Fijo y en código, como el checklist de despliegue: un catálogo configurable acabaría con
    // áreas a medio declarar, y aquí una dependencia que falta no es una molestia, es una FK que
    // revienta a mitad del borrado.

    public static readonly AreaLimpieza[] Areas =
    [
        // ── Trabajo ──────────────────────────────────────────────────────────────────────
        new("requerimientos", "Trabajo", "Requerimientos",
            "asignaciones, adjuntos, sesiones de trabajo y compromisos de SLA ligados a ellos",
            c => c.Db.Requirements.CountAsync(c.Ct),
            async c =>
            {
                int n = 0;
                // Los puntos ganados y las horas trabajadas se conservan: son del desarrollador,
                // no del requerimiento. Se desligan para no dejarlos apuntando a un id fantasma.
                await c.Db.PointEntries.Where(p => p.RequirementId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.RequirementId, (int?)null), c.Ct);
                await c.Db.WorkIntervals.Where(w => w.RequirementId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.RequirementId, (int?)null), c.Ct);

                n += await c.Db.WorkSessions.Where(w => w.RequirementId != null).ExecuteDeleteAsync(c.Ct);
                n += await c.Db.SlaCommitments.Where(s => s.RequirementId != null).ExecuteDeleteAsync(c.Ct);
                n += await c.Db.RequirementAttachments.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.Assignments.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.Requirements.ExecuteDeleteAsync(c.Ct);
                return n;
            }),

        new("sprints", "Trabajo", "Sprints",
            "nada más: los requerimientos vuelven al backlog, no se borran",
            c => c.Db.Sprints.CountAsync(c.Ct),
            async c =>
            {
                // Mismo criterio que el modelo (SetNull): borrar un sprint jamás borra el trabajo
                // que contenía.
                await c.Db.Requirements.Where(r => r.SprintId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.SprintId, (int?)null), c.Ct);
                return await c.Db.Sprints.ExecuteDeleteAsync(c.Ct);
            }),

        new("actividades", "Trabajo", "Actividades libres",
            "sus sesiones de trabajo y compromisos de SLA",
            c => c.Db.DevActivities.CountAsync(c.Ct),
            async c =>
            {
                int n = 0;
                await c.Db.WorkIntervals.Where(w => w.ActivityId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.ActivityId, (int?)null), c.Ct);
                n += await c.Db.WorkSessions.Where(w => w.ActivityId != null).ExecuteDeleteAsync(c.Ct);
                n += await c.Db.SlaCommitments.Where(s => s.ActivityId != null).ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DevActivities.ExecuteDeleteAsync(c.Ct);
                return n;
            }),

        new("sla", "Trabajo", "Compromisos de SLA", "nada más",
            c => c.Db.SlaCommitments.CountAsync(c.Ct),
            c => c.Db.SlaCommitments.ExecuteDeleteAsync(c.Ct)),

        new("desempeno", "Trabajo", "Desempeño (puntos)",
            "puntos individuales y de equipo; los criterios de puntuación se conservan",
            async c => await c.Db.PointEntries.CountAsync(c.Ct) + await c.Db.TeamPointEntries.CountAsync(c.Ct),
            async c =>
            {
                // Los criterios NO: son el catálogo con el que se puntúa, se siembran al arrancar
                // y borrarlos dejaría la pantalla de desempeño sin con qué asignar nada.
                int n = await c.Db.PointEntries.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.TeamPointEntries.ExecuteDeleteAsync(c.Ct);
                return n;
            }),

        new("minutas", "Trabajo", "Minutas", "sus acuerdos y compromisos",
            c => c.Db.Minutes.CountAsync(c.Ct),
            async c =>
            {
                int n = await c.Db.MinuteActionItems.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.Minutes.ExecuteDeleteAsync(c.Ct);
                return n;
            }),

        new("sugerencias", "Trabajo", "Sugerencias", "sus votos",
            c => c.Db.Suggestions.CountAsync(c.Ct),
            async c =>
            {
                int n = await c.Db.SuggestionVotes.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.Suggestions.ExecuteDeleteAsync(c.Ct);
                return n;
            }),

        new("notas", "Trabajo", "Notas y recordatorios", "nada más",
            c => c.Db.Notes.CountAsync(c.Ct),
            c => c.Db.Notes.ExecuteDeleteAsync(c.Ct)),

        // ── Equipo ───────────────────────────────────────────────────────────────────────
        new("jornadas", "Equipo", "Jornadas y presencia",
            "asistencia marcada a mano, registro automático, tramos trabajados y sesiones del cronómetro",
            async c => await c.Db.WorkPresences.CountAsync(c.Ct) + await c.Db.WorkIntervals.CountAsync(c.Ct)
                     + await c.Db.AttendanceRecords.CountAsync(c.Ct),
            async c =>
            {
                int n = await c.Db.WorkSessions.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.WorkIntervals.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.WorkPresences.ExecuteDeleteAsync(c.Ct);
                // La asistencia oficial se va con lo demás: conservarla sin la telemetría dejaría un
                // registro que ya no se puede contrastar, y borrar «jornadas» a medias sorprende.
                n += await c.Db.AttendanceRecords.ExecuteDeleteAsync(c.Ct);
                return n;
            }),

        new("vacaciones", "Equipo", "Vacaciones y permisos",
            "los documentos firmados de cada solicitud",
            async c => await c.Db.VacationRequests.CountAsync(c.Ct) + await c.Db.LeaveRequests.CountAsync(c.Ct),
            async c =>
            {
                int n = await c.Db.VacationDocuments.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.VacationRequests.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.LeaveRequests.ExecuteDeleteAsync(c.Ct);
                return n;
            }),

        new("evaluaciones", "Equipo", "Evaluaciones e hitos", "nada más",
            async c => await c.Db.DeveloperEvaluations.CountAsync(c.Ct) + await c.Db.DeveloperMilestones.CountAsync(c.Ct),
            async c =>
            {
                int n = await c.Db.DeveloperEvaluations.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DeveloperMilestones.ExecuteDeleteAsync(c.Ct);
                return n;
            }),

        new("perfiles", "Equipo", "Perfil y desarrollo", "nada más; los desarrolladores se conservan",
            c => c.Db.DeveloperProfiles.CountAsync(c.Ct),
            c => c.Db.DeveloperProfiles.ExecuteDeleteAsync(c.Ct)),

        new("equipos", "Equipo", "Equipos",
            "puntos de equipo y rotaciones; los desarrolladores quedan sin equipo, no se borran",
            c => c.Db.Teams.CountAsync(c.Ct),
            async c =>
            {
                await c.Db.Developers.Where(d => d.TeamId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.TeamId, (int?)null), c.Ct);
                await c.Db.AppSystems.Where(a => a.TeamId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.TeamId, (int?)null), c.Ct);
                await c.Db.Projects.Where(p => p.TeamId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.TeamId, (int?)null), c.Ct);

                int n = await c.Db.TeamPointEntries.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.TeamRotations.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.Teams.ExecuteDeleteAsync(c.Ct);
                return n;
            }),

        new("desarrolladores", "Equipo", "Desarrolladores",
            "TODO lo que cuelga de una persona: asignaciones, actividades, sesiones, SLA, puntos, "
            + "vacaciones, permisos, evaluaciones y su perfil",
            c => c.Db.Developers.CountAsync(c.Ct),
            async c =>
            {
                // Primero se sueltan las referencias de lo que sobrevive a la persona: su cuenta de
                // usuario, el equipo que lideraba, sus notas y sugerencias, los acuerdos de minuta
                // a su nombre y su firma. Nada de eso se borra — deja de apuntarla.
                await c.Db.Users.Where(u => u.DeveloperId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeveloperId, (int?)null), c.Ct);
                await c.Db.Teams.Where(t => t.LeadDeveloperId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.LeadDeveloperId, (int?)null), c.Ct);
                await c.Db.Notes.Where(n => n.DeveloperId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(n => n.DeveloperId, (int?)null), c.Ct);
                await c.Db.Suggestions.Where(s2 => s2.DeveloperId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeveloperId, (int?)null), c.Ct);
                await c.Db.MinuteActionItems.Where(i => i.ResponsibleDeveloperId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.ResponsibleDeveloperId, (int?)null), c.Ct);
                await c.Db.SignatureProfiles.Where(p => p.OwnerDeveloperId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.OwnerDeveloperId, (int?)null), c.Ct);

                // Y después lo que no tiene sentido sin ella, de la hoja a la raíz.
                int n = await c.Db.DevOpsAssignmentRules.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.Assignments.ExecuteDeleteAsync(c.Ct);
                // El pool va antes que Developers y no se puede omitir: su FK es RESTRICT, así que
                // una actividad reclamada impediría borrar la ficha y el área entera fallaría.
                n += await c.Db.PoolActivityChecklistItems.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.PoolActivities.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.PointEntries.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.WorkSessions.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.SlaCommitments.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.WorkIntervals.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DevActivities.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DeveloperEvaluations.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DeveloperMilestones.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DeveloperProfiles.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.VacationDocuments.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.VacationRequests.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.LeaveRequests.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.Developers.ExecuteDeleteAsync(c.Ct);
                return n;
            },
            Advertencia: "Deja el equipo vacío y desvincula las cuentas de usuario. Se lleva también el pool de actividades."),

        new("pool", "Trabajo", "Pool de actividades",
            "sus checklists y la traza a los puntos que generaron",
            c => c.Db.PoolActivities.CountAsync(c.Ct),
            async c =>
            {
                // La matriz y las plantillas NO se borran: son configuración, no datos, y volverlas a
                // capturar a mano sería el trabajo de una tarde. Los PointEntry ya abonados tampoco:
                // esos puntos se ganaron y viven en el ranking por su cuenta.
                int n = await c.Db.PoolActivityChecklistItems.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.PoolActivities.ExecuteDeleteAsync(c.Ct);
                return n;
            },
            Advertencia: "Conserva la matriz de puntos, los checklists y los puntos ya abonados."),

        new("contactos", "Equipo", "Contactos", "nada más",
            c => c.Db.Contacts.CountAsync(c.Ct),
            c => c.Db.Contacts.ExecuteDeleteAsync(c.Ct)),

        // ── Despliegues ──────────────────────────────────────────────────────────────────
        new("despliegues", "Despliegues", "Historial de despliegues",
            "el log completo de cada despliegue y su checklist",
            c => c.Db.DeploymentJobs.CountAsync(c.Ct),
            async c =>
            {
                await SoltarReferenciasAJobs(c);
                int n = await c.Db.DeploymentLogEntries.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DeploymentJobs.ExecuteDeleteAsync(c.Ct);
                return n;
            }),

        new("programados", "Despliegues", "Despliegues programados", "nada más",
            c => c.Db.ScheduledDeployments.CountAsync(c.Ct),
            c => c.Db.ScheduledDeployments.ExecuteDeleteAsync(c.Ct)),

        new("releases", "Despliegues", "Sistemas y versiones",
            "el historial de despliegues y las citas programadas de esas versiones",
            async c => await c.Db.AppSystems.CountAsync(c.Ct) + await c.Db.AppReleases.CountAsync(c.Ct),
            async c =>
            {
                await SoltarReferenciasAJobs(c);
                await c.Db.DeploymentTargets.Where(t => t.LastReleaseId != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastReleaseId, (int?)null), c.Ct);

                int n = await c.Db.ScheduledDeployments.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DeploymentLogEntries.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DeploymentJobs.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.AppReleases.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.AppSystems.ExecuteDeleteAsync(c.Ct);
                return n;
            }),

        new("servidores", "Despliegues", "Servidores y perfiles de despliegue",
            "el historial de despliegues y las citas programadas que los usan",
            async c => await c.Db.DeploymentTargets.CountAsync(c.Ct) + await c.Db.DeploymentProfiles.CountAsync(c.Ct),
            async c =>
            {
                await SoltarReferenciasAJobs(c);
                int n = await c.Db.ScheduledDeployments.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DeploymentLogEntries.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DeploymentJobs.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DeploymentProfileTargets.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DeploymentProfiles.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DeploymentTargets.ExecuteDeleteAsync(c.Ct);
                return n;
            },
            Advertencia: "Se lleva los datos de conexión y las contraseñas de los servidores: hay que capturarlos otra vez."),

        // ── Integraciones ────────────────────────────────────────────────────────────────
        new("devops", "Integraciones", "Tickets de Azure DevOps",
            "los vínculos con Freshdesk y los tickets en seguimiento",
            c => c.Db.DevOpsTickets.CountAsync(c.Ct),
            async c =>
            {
                int n = await c.Db.TicketLinks.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.WatchedTickets.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DevOpsAssignmentsSeen.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.DevOpsTickets.ExecuteDeleteAsync(c.Ct);
                return n;
            }),

        new("freshdesk", "Integraciones", "Tickets de Freshdesk", "los vínculos con Azure DevOps",
            c => c.Db.FreshDeskTickets.CountAsync(c.Ct),
            async c =>
            {
                int n = await c.Db.TicketLinks.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.FreshDeskAssignmentsSeen.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.FreshDeskTickets.ExecuteDeleteAsync(c.Ct);
                return n;
            }),

        new("vinculos", "Integraciones", "Vínculos entre tickets", "nada más; los tickets se conservan",
            c => c.Db.TicketLinks.CountAsync(c.Ct),
            c => c.Db.TicketLinks.ExecuteDeleteAsync(c.Ct)),

        // ── Otros ────────────────────────────────────────────────────────────────────────
        new("foro", "Otros", "Foro del equipo", "publicaciones, comentarios, imágenes y «me gusta»",
            c => c.Db.ForumPosts.CountAsync(c.Ct),
            async c =>
            {
                int n = await c.Db.ForumLikes.ExecuteDeleteAsync(c.Ct);
                n += await c.Db.ForumAttachments.ExecuteDeleteAsync(c.Ct);
                // De la respuesta más anidada hacia la publicación: un comentario apunta a su padre
                // y la comprobación de la FK es inmediata, así que borrar el padre primero falla.
                var profundidades = await c.Db.ForumPosts.Select(p => p.Depth).Distinct()
                    .OrderByDescending(d => d).ToListAsync(c.Ct);
                foreach (var d in profundidades)
                    n += await c.Db.ForumPosts.Where(p => p.Depth == d).ExecuteDeleteAsync(c.Ct);
                return n;
            }),

        new("avisos", "Otros", "Avisos", "nada más",
            c => c.Db.Notifications.CountAsync(c.Ct),
            c => c.Db.Notifications.ExecuteDeleteAsync(c.Ct)),

        new("plantillas", "Otros", "Plantillas y scripts", "nada más",
            c => c.Db.Templates.CountAsync(c.Ct),
            c => c.Db.Templates.ExecuteDeleteAsync(c.Ct)),

        new("azure", "Otros", "Recursos Azure", "nada más",
            c => c.Db.AzureResources.CountAsync(c.Ct),
            c => c.Db.AzureResources.ExecuteDeleteAsync(c.Ct)),

        new("programas", "Otros", "Programas", "nada más",
            c => c.Db.SoftwareItems.CountAsync(c.Ct),
            c => c.Db.SoftwareItems.ExecuteDeleteAsync(c.Ct)),

        // ── Administración ───────────────────────────────────────────────────────────────
        new("usuarios", "Administración", "Usuarios de prueba",
            "nada más; las fichas de desarrollador se conservan",
            c => UsuariosBorrables(c).CountAsync(c.Ct),
            c => UsuariosBorrables(c).ExecuteDeleteAsync(c.Ct),
            Advertencia: "Nunca borra cuentas de líder ni la tuya: quedarse fuera de la aplicación no tiene vuelta atrás."),

        new(ClaveBitacora, "Administración", "Bitácora de auditoría",
            "nada más; esta limpieza sí queda registrada",
            c => c.Db.AuditLogs.CountAsync(c.Ct),
            c => c.Db.AuditLogs.ExecuteDeleteAsync(c.Ct),
            Advertencia: "Borra la evidencia de quién hizo qué en toda la aplicación."),
    ];

    /// <summary>
    /// Las cuentas que la limpieza puede tocar. La sesión actual y los administradores quedan fuera
    /// SIEMPRE: es la única operación de esta pantalla cuyo error no se arregla desde la aplicación,
    /// porque para arreglarlo habría que entrar.
    /// </summary>
    private static IQueryable<User> UsuariosBorrables(ContextoLimpieza c) =>
        c.Db.Users.Where(u => u.Role != UserRole.Admin && u.Id != c.UsuarioActualId);

    /// <summary>
    /// Suelta lo que apunta a un despliegue concreto antes de borrarlo. Son referencias «de último
    /// resultado» (el último despliegue de un servidor, el job que ejecutó una cita programada):
    /// sobreviven al job, así que se desligan en vez de arrastrar la fila entera.
    /// </summary>
    private static async Task SoltarReferenciasAJobs(ContextoLimpieza c)
    {
        await c.Db.DeploymentTargets.Where(t => t.LastDeploymentJobId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastDeploymentJobId, (int?)null), c.Ct);
        await c.Db.ScheduledDeployments.Where(s2 => s2.DeploymentJobId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DeploymentJobId, (int?)null), c.Ct);
    }

    // ── Operaciones ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cuántos registros tiene cada área hoy. Un área que devuelve <c>-1</c> es una que no se pudo
    /// contar (tabla ausente en una base antigua): se muestra como desconocida en vez de tumbar la
    /// pantalla o, peor, aparentar que está vacía.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> ContarAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        await using var db = new AppDbContext(_dbOptions);
        var ctx = new ContextoLimpieza(db, _currentUser.UserId, ct);

        var cuentas = new Dictionary<string, int>();
        foreach (var area in Areas)
        {
            try { cuentas[area.Clave] = await area.ContarAsync(ctx); }
            catch { cuentas[area.Clave] = -1; }
        }
        return cuentas;
    }

    /// <summary>
    /// Borra las áreas indicadas. Un área que falla no detiene a las demás: se registra el motivo y
    /// se sigue — que una FK imprevista en «Equipos» impidiera limpiar «Avisos» no ayudaría a nadie.
    /// </summary>
    public async Task<IReadOnlyList<ResultadoLimpieza>> LimpiarAsync(
        IReadOnlyCollection<string> claves, IProgress<string>? progreso = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var seleccion = Ordenar(claves);
        if (seleccion.Count == 0) return [];

        var correlacion = AuditService.NuevaCorrelacion();
        var resultados = new List<ResultadoLimpieza>();

        foreach (var area in seleccion)
        {
            await using var db = new AppDbContext(_dbOptions);
            var ctx = new ContextoLimpieza(db, _currentUser.UserId, ct);
            int habia = 0;
            try
            {
                habia = await area.ContarAsync(ctx);
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                int filas = await area.BorrarAsync(ctx);
                await tx.CommitAsync(ct);

                resultados.Add(new(area.Clave, area.Nombre, habia, filas, null));
                progreso?.Report($"✓  {area.Nombre}: {habia} registro(s), {filas} fila(s) borradas.");
                _audit.RecordDetailed(AuditAction.Delete, "Limpieza", area.Clave,
                    $"Limpieza de «{area.Nombre}»: había {habia} registro(s), se borraron {filas} fila(s).",
                    AuditOutcome.Exito, null, null, correlacion);
            }
            catch (Exception ex)
            {
                resultados.Add(new(area.Clave, area.Nombre, habia, 0, ex.Message));
                progreso?.Report($"✗  {area.Nombre}: {ex.Message}");
                _audit.RecordDetailed(AuditAction.Delete, "Limpieza", area.Clave,
                    $"Limpieza de «{area.Nombre}» fallida: {ex.Message}",
                    AuditOutcome.Fallo, null, null, correlacion);
            }
        }

        // El contexto compartido por la UI sigue rastreando entidades que acaban de dejar de
        // existir; sin esto, la pantalla siguiente las mostraría como si nada hubiera pasado.
        _db.ChangeTracker.Clear();
        return resultados;
    }

    /// <summary>
    /// El orden de la pasada. Importa por una sola razón: la bitácora va PRIMERO. Borrada al final
    /// se llevaría los renglones que esta misma limpieza acaba de escribir, y la operación más
    /// destructiva de la aplicación sería la única que no deja rastro. Yendo primero, todo lo que se
    /// registre después sobrevive.
    /// </summary>
    internal static List<AreaLimpieza> Ordenar(IReadOnlyCollection<string> claves) =>
        Areas.Where(a => claves.Contains(a.Clave))
             .OrderByDescending(a => a.Clave == ClaveBitacora)
             .ToList();
}
