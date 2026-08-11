using AdminWeb.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Infrastructure.Data;

/// <summary>
/// El contexto de datos de la web. Apunta a la MISMA base que el escritorio (mismas tablas, mismas
/// columnas), con una diferencia de fondo en cómo se usa: aquí se registra como <b>Scoped</b>, uno
/// por petición.
///
/// Eso no es un detalle de configuración, es lo que hace innecesarias las defensas que el escritorio
/// tuvo que construir por tener un contexto Singleton compartido por toda la interfaz: los
/// <c>Reload()</c> antes de decidir, los <c>Detach</c> tras un fallo y los <c>AsNoTracking</c>
/// puestos «porque el contexto es Singleton». Al copiar cada servicio, esas muletas se quitan —
/// mantenerlas con scoped no solo sobra: entorpece el patrón unidad-de-trabajo de la petición.
///
/// El MAPEO, en cambio, es el mismo que el del escritorio y tiene que seguir siéndolo: las dos
/// aplicaciones escriben sobre las mismas tablas mientras dure el corte. Cada decisión de borrado
/// (Cascade, SetNull, Restrict, NoAction) está copiada al pie de la letra junto con el comentario
/// que la explica — varias existen para evitar las rutas múltiples de cascada que SQL Server
/// rechaza al crear las restricciones, así que cambiarlas rompe la creación del esquema.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // ── Fase 0 ───────────────────────────────────────────────────────────────
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    // ── Fase 1 ───────────────────────────────────────────────────────────────
    public DbSet<Developer> Developers => Set<Developer>();
    public DbSet<DeveloperProfile> DeveloperProfiles => Set<DeveloperProfile>();
    public DbSet<Requirement> Requirements => Set<Requirement>();
    public DbSet<Sprint> Sprints => Set<Sprint>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<RequirementAttachment> RequirementAttachments => Set<RequirementAttachment>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamRotation> TeamRotations => Set<TeamRotation>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<DevOpsAssignmentRule> DevOpsAssignmentRules => Set<DevOpsAssignmentRule>();
    public DbSet<DevOpsSavedFilter> DevOpsSavedFilters => Set<DevOpsSavedFilter>();

    // ── Fase 2 ───────────────────────────────────────────────────────────────
    public DbSet<Minute> Minutes => Set<Minute>();
    public DbSet<MinuteActionItem> MinuteActionItems => Set<MinuteActionItem>();
    public DbSet<VacationRequest> VacationRequests => Set<VacationRequest>();
    public DbSet<Suggestion> Suggestions => Set<Suggestion>();
    public DbSet<SuggestionVote> SuggestionVotes => Set<SuggestionVote>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<ScoringCriterion> ScoringCriteria => Set<ScoringCriterion>();
    public DbSet<PointEntry> PointEntries => Set<PointEntry>();
    public DbSet<TeamPointEntry> TeamPointEntries => Set<TeamPointEntry>();
    public DbSet<WorkSession> WorkSessions => Set<WorkSession>();
    public DbSet<WorkInterval> WorkIntervals => Set<WorkInterval>();
    public DbSet<DevActivity> DevActivities => Set<DevActivity>();
    public DbSet<DevActivityAttachment> DevActivityAttachments => Set<DevActivityAttachment>();
    public DbSet<SlaCommitment> SlaCommitments => Set<SlaCommitment>();
    public DbSet<ScheduledDeployment> ScheduledDeployments => Set<ScheduledDeployment>();

    // ── Firmas + documentos de vacaciones ────────────────────────────────────
    public DbSet<SignatureProfile> SignatureProfiles => Set<SignatureProfile>();
    public DbSet<VacationDocument> VacationDocuments => Set<VacationDocument>();

    // ── Infraestructura Azure ────────────────────────────────────────────────
    public DbSet<AzureResource> AzureResources => Set<AzureResource>();
    public DbSet<Software> SoftwareItems => Set<Software>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

    // ── Fase 3 ───────────────────────────────────────────────────────────────
    public DbSet<AppSystem> AppSystems => Set<AppSystem>();
    public DbSet<AppRelease> AppReleases => Set<AppRelease>();
    public DbSet<DeploymentTarget> DeploymentTargets => Set<DeploymentTarget>();
    public DbSet<DeploymentProfile> DeploymentProfiles => Set<DeploymentProfile>();
    public DbSet<DeploymentProfileTarget> DeploymentProfileTargets => Set<DeploymentProfileTarget>();
    public DbSet<DeploymentJob> DeploymentJobs => Set<DeploymentJob>();
    public DbSet<DeploymentLogEntry> DeploymentLogEntries => Set<DeploymentLogEntry>();

    // ── Integración tickets ──────────────────────────────────────────────────
    public DbSet<DevOpsTicket> DevOpsTickets => Set<DevOpsTicket>();
    public DbSet<FreshDeskTicket> FreshDeskTickets => Set<FreshDeskTicket>();
    public DbSet<TicketLink> TicketLinks => Set<TicketLink>();
    public DbSet<WatchedTicket> WatchedTickets => Set<WatchedTicket>();

    // ── Avisos in-app ────────────────────────────────────────────────────────
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DevOpsAssignmentSeen> DevOpsAssignmentsSeen => Set<DevOpsAssignmentSeen>();
    public DbSet<FreshDeskAssignmentSeen> FreshDeskAssignmentsSeen => Set<FreshDeskAssignmentSeen>();

    // ── Evaluaciones e hitos por desarrollador (reporte en PDF) ──────────────
    public DbSet<DeveloperEvaluation> DeveloperEvaluations => Set<DeveloperEvaluation>();
    public DbSet<DeveloperMilestone> DeveloperMilestones => Set<DeveloperMilestone>();

    // ── Biblioteca de plantillas y scripts (administrador) ───────────────────
    public DbSet<Template> Templates => Set<Template>();

    // ── Presencia en vivo (telemetría por latido) y asistencia oficial (marcada a mano) ──
    public DbSet<WorkPresence> WorkPresences => Set<WorkPresence>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();

    // ── Pool de actividades valoradas (puntos fijados antes de trabajarlas) ──
    public DbSet<PoolActivity> PoolActivities => Set<PoolActivity>();
    public DbSet<PoolPointsMatrixEntry> PoolPointsMatrix => Set<PoolPointsMatrixEntry>();
    public DbSet<PoolChecklistTemplateItem> PoolChecklistTemplateItems => Set<PoolChecklistTemplateItem>();
    public DbSet<PoolActivityChecklistItem> PoolActivityChecklistItems => Set<PoolActivityChecklistItem>();
    public DbSet<PoolActivityExtraCriterion> PoolActivityExtraCriteria => Set<PoolActivityExtraCriterion>();

    /// <summary>Plantillas de documento que el área puede sustituir sin recompilar.</summary>
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();

    // ── Foro del equipo ──────────────────────────────────────────────────────
    public DbSet<ForumPost> ForumPosts => Set<ForumPost>();
    public DbSet<ForumLike> ForumLikes => Set<ForumLike>();
    public DbSet<ForumAttachment> ForumAttachments => Set<ForumAttachment>();

    // ── Base de conocimiento ─────────────────────────────────────────────────
    // Una sola tabla para todo: una guía larga y un término del glosario son el mismo tipo de fila
    // con etiquetas distintas. Ver KnowledgeArticle.
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();

    // ── Propias de la web ────────────────────────────────────────────────────
    // No existen en el escritorio: sustituyen a cosas que allí vivían en la máquina de cada quien
    // (el token de DevOps cifrado con DPAPI, la configuración de columnas) o que no hacían falta
    // porque la aplicación seguía viva en la bandeja del sistema (los avisos push).
    public DbSet<UserSecret> UserSecrets => Set<UserSecret>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    // Segundo factor. El secreto NO está aquí: vive cifrado en UserSecrets. Estas dos tablas son
    // las piezas que no caben ahí — los ocho códigos de rescate, que se gastan de uno en uno, y los
    // navegadores en los que ya no hace falta volver a pedir el código durante treinta días.
    public DbSet<UserRecoveryCode> UserRecoveryCodes => Set<UserRecoveryCode>();
    public DbSet<UserTrustedDevice> UserTrustedDevices => Set<UserTrustedDevice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── Acceso y bitácora ────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Role).HasConversion<int>();
            e.Property(u => u.Username).IsRequired().HasMaxLength(100);
            e.Property(u => u.FullName).HasMaxLength(200);
            // El sello de sesión es de la web; en el escritorio la columna simplemente no se lee.
            e.Property(u => u.SecurityStamp).HasMaxLength(64);
            e.HasOne(u => u.Developer).WithMany()
                .HasForeignKey(u => u.DeveloperId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AppSetting>(e => e.HasIndex(s => s.Key).IsUnique());

        // Sin FK al usuario a propósito: la bitácora es histórica y debe sobrevivir a que se borre
        // la cuenta de quien hizo la operación. Misma decisión que en el escritorio.
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.Property(a => a.Action).HasConversion<int>();
            e.Property(a => a.Outcome).HasConversion<int>();
            e.Property(a => a.UserName).IsRequired().HasMaxLength(100);
            e.Property(a => a.EntityType).HasMaxLength(100);
            e.Property(a => a.EntityId).HasMaxLength(100);
            e.Property(a => a.Origin).HasMaxLength(200);
            e.Property(a => a.CorrelationId).HasMaxLength(50);
            // Índices para que la pantalla de Bitácora siga siendo usable cuando haya años de datos.
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => a.CorrelationId);
        });

        // ── Infraestructura Azure y software ─────────────────────────────────
        modelBuilder.Entity<AzureResource>(e =>
        {
            e.Property(r => r.ResourceType).HasConversion<int>();
            e.Property(r => r.Status).HasConversion<int>();
            e.Property(r => r.Environment).HasConversion<int>();
        });

        modelBuilder.Entity<Software>(e =>
        {
            e.Property(s => s.Category).HasConversion<int>();
            e.Property(s => s.LicenseType).HasConversion<int>();
            e.Property(s => s.Status).HasConversion<int>();
        });

        modelBuilder.Entity<LeaveRequest>(e =>
        {
            e.Property(l => l.Type).HasConversion<int>();
            e.Property(l => l.Status).HasConversion<int>();
            e.Property(l => l.AttachmentFileName).HasMaxLength(260);
            e.Ignore(l => l.EndDate);
            e.Ignore(l => l.EsSolicitudDelDesarrollador);
            e.HasOne(l => l.Developer).WithMany().HasForeignKey(l => l.DeveloperId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => new { l.DeveloperId, l.Date });   // «mis permisos», por fecha
            e.HasIndex(l => l.Status);                        // la bandeja de pendientes del admin
        });

        // ── Requerimientos, sprints y asignaciones ───────────────────────────
        modelBuilder.Entity<Requirement>(e =>
        {
            e.Property(r => r.Status).HasConversion<int>();
            e.Property(r => r.Priority).HasConversion<int>();
            e.Property(r => r.Source).HasConversion<int>();
            // SetNull: borrar un sprint regresa sus requerimientos al backlog, jamás los borra.
            // En la base real la columna se agrega por ALTER sin FK (el migrador es aditivo), así
            // que el servicio también desliga a mano al eliminar — esto cubre las bases nuevas.
            e.HasOne(r => r.Sprint).WithMany(s => s.Requirements)
             .HasForeignKey(r => r.SprintId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(r => r.SprintId);
        });

        modelBuilder.Entity<Sprint>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(100);
            e.Property(s => s.Goal).HasMaxLength(1000);
            e.HasIndex(s => s.StartDate);
        });

        modelBuilder.Entity<Assignment>(e =>
        {
            e.HasOne(a => a.Requirement).WithMany(r => r.Assignments).HasForeignKey(a => a.RequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Developer).WithMany(d => d.Assignments).HasForeignKey(a => a.DeveloperId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RequirementAttachment>(e =>
        {
            e.Property(a => a.Kind).HasConversion<int>();
            // Mismo largo que la columna del parche del migrador, por lo mismo que en las vacaciones.
            e.Property(a => a.FileName).HasMaxLength(260);
            e.HasOne(a => a.Requirement).WithMany().HasForeignKey(a => a.RequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.RequirementId);
        });

        // ── Equipos ──────────────────────────────────────────────────────────
        // Equipos: un dev pertenece a un equipo (Members); el líder es otra FK aparte.
        modelBuilder.Entity<Team>(e =>
        {
            e.HasMany(t => t.Members).WithOne(d => d.Team).HasForeignKey(d => d.TeamId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.Lead).WithMany().HasForeignKey(t => t.LeadDeveloperId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Developer>(e =>
        {
            e.Property(d => d.TeamRole).HasConversion<int>();
            e.Property(d => d.FullName).IsRequired().HasMaxLength(200);
            e.Property(d => d.Email).HasMaxLength(200);
            e.Property(d => d.Phone).HasMaxLength(50);
            e.Property(d => d.Seniority).HasMaxLength(50);
            e.Property(d => d.EquipmentSerial).HasMaxLength(100);

            // La función dentro del equipo: 200, el mismo tope que valida
            // PersonasQueryService.LargoMaximoDeFuncion y el mismo que crea el parche de
            // DatabaseMigrator. Sin esta línea EF la haría nvarchar(max) en las bases NUEVAS mientras
            // el parche la deja en nvarchar(200) en las que ya existían: la misma columna con dos
            // tipos según cuándo se creó la base, que es justo lo que advierte el comentario de abajo.
            e.Property(d => d.TeamFunction).HasMaxLength(200);

            // Las longitudes van declaradas y no se dejan a la omisión de EF, que en SQL Server
            // sería nvarchar(max). No es cosmética: una columna (max) no cabe como clave de índice
            // —ya mordió una vez con DedupeKey de Notifications— y aquí además tienen que coincidir
            // con lo que el parche de DatabaseMigrator crea en una base que ya existe, o la misma
            // columna acabaría siendo de un tipo en las bases nuevas y de otro en las viejas.
            e.Property(d => d.VacationAdjustmentNote).HasMaxLength(500);
            e.Property(d => d.VacationAdjustmentBy).HasMaxLength(150);
        });

        modelBuilder.Entity<TeamRotation>(e => e.HasIndex(r => r.RotatedAt));

        // Sistemas y proyectos relacionados a equipos (equipo opcional; al borrarlo, se libera).
        modelBuilder.Entity<AppSystem>(e =>
            e.HasOne(a => a.Team).WithMany().HasForeignKey(a => a.TeamId).IsRequired(false).OnDelete(DeleteBehavior.SetNull));
        modelBuilder.Entity<Project>(e =>
        {
            e.Property(pr => pr.Status).HasConversion<int>();
            e.HasOne(pr => pr.Team).WithMany().HasForeignKey(pr => pr.TeamId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DevOpsAssignmentRule>(e =>
        {
            e.Property(r => r.Match).HasConversion<int>();
            e.HasOne(r => r.Developer).WithMany().HasForeignKey(r => r.DeveloperId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Minutas ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Minute>(e => e.Property(m => m.Type).HasConversion<int>());
        modelBuilder.Entity<MinuteActionItem>(e =>
        {
            e.HasOne(i => i.Minute).WithMany(m => m.ActionItems).HasForeignKey(i => i.MinuteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.ResponsibleDeveloper).WithMany().HasForeignKey(i => i.ResponsibleDeveloperId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        // ── Vacaciones ───────────────────────────────────────────────────────
        modelBuilder.Entity<VacationRequest>(e =>
        {
            e.Property(v => v.Status).HasConversion<int>();
            // El mismo largo que la columna que crea el parche del migrador y que el nombre del
            // justificante de un permiso: sin decirlo aquí, una base nueva nacería con nvarchar(max) y
            // el mismo campo tendría dos definiciones según cómo se creara la base.
            e.Property(v => v.AttachmentFileName).HasMaxLength(260);
            e.HasOne(v => v.Developer).WithMany().HasForeignKey(v => v.DeveloperId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Sugerencias / propuestas de mejora ───────────────────────────────
        modelBuilder.Entity<Suggestion>(e =>
        {
            e.Property(s => s.Category).HasConversion<int>();
            e.Property(s => s.Status).HasConversion<int>();
            e.Property(s => s.Visibility).HasConversion<int>();
            e.Ignore(s => s.SePuedeVotar);   // se deriva de Visibility + OpenToVoting
            e.Property(s => s.Title).IsRequired().HasMaxLength(150);
            // Si se borra la ficha del desarrollador, la sugerencia se conserva (queda sin autor).
            e.HasOne(s => s.Developer).WithMany().HasForeignKey(s => s.DeveloperId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(s => s.CreatedByUserId);
            e.HasIndex(s => s.Status);
        });

        modelBuilder.Entity<DeveloperProfile>(e =>
        {
            e.HasOne(p => p.Developer).WithMany().HasForeignKey(p => p.DeveloperId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => p.DeveloperId).IsUnique();   // una ficha por desarrollador
        });

        modelBuilder.Entity<SuggestionVote>(e =>
        {
            e.HasOne(v => v.Suggestion).WithMany().HasForeignKey(v => v.SuggestionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(v => new { v.SuggestionId, v.UserId }).IsUnique();   // un voto por usuario y sugerencia
        });

        // ── Notas ────────────────────────────────────────────────────────────
        modelBuilder.Entity<Note>(e =>
        {
            e.Property(n => n.Priority).HasConversion<int>();
            e.HasOne(n => n.Developer).WithMany().HasForeignKey(n => n.DeveloperId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        // ── Desempeño ────────────────────────────────────────────────────────
        modelBuilder.Entity<ScoringCriterion>(e => e.Property(c => c.Scope).HasConversion<int>());

        modelBuilder.Entity<PointEntry>(e =>
        {
            e.HasOne(p => p.Developer).WithMany().HasForeignKey(p => p.DeveloperId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Criterion).WithMany().HasForeignKey(p => p.CriterionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Requirement).WithMany().HasForeignKey(p => p.RequirementId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.Property(p => p.ApprovalStatus).HasConversion<int>();
            e.Property(p => p.EvidenceUrl).HasMaxLength(500);
            e.Ignore(p => p.AdmiteReplica);
            e.HasIndex(p => p.ApprovalStatus);
        });

        // Actividades libres del desarrollador (trabajo fuera de sus requerimientos asignados)
        modelBuilder.Entity<DevActivity>(e =>
        {
            e.Property(a => a.Status).HasConversion<int>();
            e.Property(a => a.Title).IsRequired().HasMaxLength(200);
            e.HasOne(a => a.Developer).WithMany().HasForeignKey(a => a.DeveloperId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.DeveloperId, a.Status });
        });

        // Evidencia de las actividades libres. Cascada desde la actividad: si la actividad se
        // borra (solo se permite cuando no tiene tiempo registrado), sus archivos se van con ella.
        modelBuilder.Entity<DevActivityAttachment>(e =>
        {
            e.HasOne(a => a.Activity).WithMany().HasForeignKey(a => a.ActivityId).OnDelete(DeleteBehavior.Cascade);
            e.Property(a => a.FileName).IsRequired().HasMaxLength(260);
            e.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
            e.Property(a => a.Description).HasMaxLength(400);
            e.Ignore(a => a.EsImagen);
            e.HasIndex(a => a.ActivityId);
        });

        // Compromisos de SLA. Mismo patrón de objetivo doble que WorkSession.
        modelBuilder.Entity<SlaCommitment>(e =>
        {
            e.Property(s => s.Status).HasConversion<int>();
            e.HasOne(s => s.Requirement).WithMany().HasForeignKey(s => s.RequirementId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
            // NoAction en actividad y desarrollador: con cascada habría varias rutas hasta
            // Developers y SQL Server rechaza la creación de las FK.
            e.HasOne(s => s.Activity).WithMany().HasForeignKey(s => s.ActivityId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(s => s.Developer).WithMany().HasForeignKey(s => s.DeveloperId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(s => new { s.DeveloperId, s.Status });
            e.HasIndex(s => s.NextReminderAtUtc);
            e.HasIndex(s => s.DueAtUtc);
        });

        // Sesiones de trabajo (cronómetro). El objetivo es un requerimiento O una actividad libre:
        // ambas FK son opcionales y el servicio garantiza que siempre venga exactamente una.
        modelBuilder.Entity<WorkSession>(e =>
        {
            e.Property(w => w.Status).HasConversion<int>();
            e.HasOne(w => w.Requirement).WithMany().HasForeignKey(w => w.RequirementId).IsRequired(false).OnDelete(DeleteBehavior.Cascade);
            // NoAction en la actividad: con Cascade habría dos rutas de cascada hasta Developers
            // (Requirement→…→Developer y Activity→Developer) y SQL Server lo rechaza al crear la FK.
            e.HasOne(w => w.Activity).WithMany().HasForeignKey(w => w.ActivityId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
            // Restrict en Developer para evitar múltiples rutas de cascada (Requirement ya es Cascade) en SQL Server.
            e.HasOne(w => w.Developer).WithMany().HasForeignKey(w => w.DeveloperId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(w => new { w.DeveloperId, w.Status });
            e.HasIndex(w => w.RequirementId);
            e.HasIndex(w => w.ActivityId);
        });

        // Puntos a nivel de equipo (independientes del individual)
        modelBuilder.Entity<TeamPointEntry>(e =>
        {
            e.HasOne(p => p.Team).WithMany().HasForeignKey(p => p.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Criterion).WithMany().HasForeignKey(p => p.CriterionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => new { p.TeamId, p.Year, p.Month });
        });

        // ── Firmas + documentos de vacaciones ────────────────────────────────
        modelBuilder.Entity<SignatureProfile>(e =>
        {
            e.HasOne(s => s.OwnerDeveloper).WithMany().HasForeignKey(s => s.OwnerDeveloperId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(s => new { s.OwnerDeveloperId, s.IsDefault });
        });

        modelBuilder.Entity<VacationDocument>(e =>
        {
            e.Property(d => d.Source).HasConversion<int>();
            e.Property(d => d.Status).HasConversion<int>();
            e.HasOne(d => d.VacationRequest).WithMany().HasForeignKey(d => d.VacationRequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.SignatureProfile).WithMany().HasForeignKey(d => d.SignatureProfileId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        // ── Fase 3 — Despliegues ─────────────────────────────────────────────
        modelBuilder.Entity<AppRelease>(e =>
            e.HasOne(r => r.AppSystem).WithMany(a => a.Releases).HasForeignKey(r => r.AppSystemId).OnDelete(DeleteBehavior.Cascade));

        modelBuilder.Entity<DeploymentTarget>(e =>
            e.HasOne(t => t.LastRelease).WithMany().HasForeignKey(t => t.LastReleaseId).IsRequired(false).OnDelete(DeleteBehavior.SetNull));

        modelBuilder.Entity<DeploymentProfileTarget>(e =>
        {
            e.HasOne(pt => pt.Profile).WithMany(p => p.ProfileTargets).HasForeignKey(pt => pt.ProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(pt => pt.Target).WithMany().HasForeignKey(pt => pt.TargetId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeploymentJob>(e =>
        {
            e.Property(j => j.Status).HasConversion<int>();
            e.HasOne(j => j.AppRelease).WithMany(r => r.Jobs).HasForeignKey(j => j.AppReleaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(j => j.Profile).WithMany().HasForeignKey(j => j.DeploymentProfileId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeploymentLogEntry>(e =>
        {
            e.Property(l => l.Level).HasConversion<int>();
            e.HasOne(l => l.Job).WithMany(j => j.LogEntries).HasForeignKey(l => l.JobId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduledDeployment>(e =>
        {
            e.Property(s => s.Status).HasConversion<int>();
            e.HasOne(s => s.AppRelease).WithMany().HasForeignKey(s => s.AppReleaseId).OnDelete(DeleteBehavior.Cascade);
            // NoAction en el perfil: con cascada habría dos rutas hasta la misma tabla.
            e.HasOne(s => s.Profile).WithMany().HasForeignKey(s => s.DeploymentProfileId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(s => new { s.Status, s.ScheduledAtUtc });
        });

        // ── Integración tickets ──────────────────────────────────────────────
        modelBuilder.Entity<DevOpsTicket>(e =>
        {
            e.HasIndex(t => t.ExternalId).IsUnique();
            e.Ignore(t => t.SinPrioridadDefinida);
            e.Ignore(t => t.SinEstimar);
        });

        modelBuilder.Entity<FreshDeskTicket>(e =>
            e.HasIndex(t => t.ExternalId).IsUnique());

        modelBuilder.Entity<TicketLink>(e =>
        {
            e.HasOne(l => l.DevOpsTicket).WithMany(t => t.TicketLinks).HasForeignKey(l => l.DevOpsTicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.FreshDeskTicket).WithMany(t => t.TicketLinks).HasForeignKey(l => l.FreshDeskTicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => new { l.DevOpsTicketId, l.FreshDeskTicketId }).IsUnique();
        });

        modelBuilder.Entity<WatchedTicket>(e =>
        {
            e.HasOne(w => w.DevOpsTicket).WithMany().HasForeignKey(w => w.DevOpsTicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(w => new { w.DevOpsTicketId, w.WatchedByUser }).IsUnique();
        });

        // ── Avisos in-app ────────────────────────────────────────────────────
        modelBuilder.Entity<Notification>(e =>
        {
            e.Property(n => n.Kind).HasConversion<int>();
            e.HasIndex(n => new { n.ForUserId, n.ReadAt });

            // La LONGITUD de DedupeKey no es cosmética: sin ella EF la crea como nvarchar(max), y
            // SQL Server no admite una columna así como clave de un índice. El resultado era que el
            // índice único que impide avisos duplicados (UX_Notif_Dedupe) fallaba al crearse en cada
            // arranque —en silencio, porque el migrador lo intenta dentro de un try vacío— y la
            // deduplicación se quedaba solo en el «comprueba y luego inserta» del servicio, que dos
            // trabajos de fondo a la vez pueden atravesar. 200 es lo que ya declaraba el CREATE TABLE
            // del migrador; las claves reales son del estilo «pool-aceptada-1234» y no se acercan.
            e.Property(n => n.DedupeKey).HasMaxLength(200);
        });

        modelBuilder.Entity<DevOpsAssignmentSeen>(e =>
            e.HasIndex(s => new { s.UserId, s.ExternalId }).IsUnique());

        modelBuilder.Entity<FreshDeskAssignmentSeen>(e =>
            e.HasIndex(s => new { s.UserId, s.AgentId, s.ExternalId }).IsUnique());

        // ── Evaluaciones e hitos ─────────────────────────────────────────────
        modelBuilder.Entity<DeveloperEvaluation>(e =>
        {
            e.HasOne(x => x.Developer).WithMany().HasForeignKey(x => x.DeveloperId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.DeveloperId);
        });
        modelBuilder.Entity<DeveloperMilestone>(e =>
        {
            e.Property(x => x.Kind).HasConversion<int>();
            e.HasOne(x => x.Developer).WithMany().HasForeignKey(x => x.DeveloperId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.DeveloperId);
        });

        // Biblioteca de plantillas y scripts. Sin FK al usuario que la creó: la plantilla es del
        // área, no de la persona, y debe sobrevivir a que su cuenta se elimine.
        modelBuilder.Entity<Template>(e =>
        {
            e.Property(t => t.Kind).HasConversion<int>();
            e.Property(t => t.Title).IsRequired().HasMaxLength(200);
            e.Property(t => t.Tags).HasMaxLength(300);
            e.Property(t => t.FileName).HasMaxLength(260);
            e.HasIndex(t => new { t.Kind, t.IsArchived });
        });

        // Presencia y asistencia. Sin FK al usuario ni a la ficha a propósito: es un registro
        // histórico y debe sobrevivir a que se borre la cuenta, igual que la bitácora.
        modelBuilder.Entity<WorkPresence>(e =>
        {
            e.Property(p => p.State).HasConversion<int>();
            e.Property(p => p.EndReason).HasConversion<int>();
            e.Property(p => p.DisplayName).IsRequired().HasMaxLength(200);
            e.Property(p => p.StateNote).HasMaxLength(200);
            e.Property(p => p.Origin).HasMaxLength(200);
            e.Ignore(p => p.Abierta);
            e.Ignore(p => p.Duracion);
            // Las dos consultas que existen: «quién está ahora» (abiertas) y «la jornada de fulano».
            e.HasIndex(p => new { p.UserId, p.StartedAtUtc });
            e.HasIndex(p => p.EndedAtUtc);
        });

        // Asistencia oficial. Sin FK, por lo mismo que la presencia: es histórico y debe sobrevivir
        // a que se borre la cuenta. Lo que se marcó a mano no deja de haber pasado porque alguien
        // salga del equipo.
        modelBuilder.Entity<AttendanceRecord>(e =>
        {
            e.Property(a => a.CloseKind).HasConversion<int>();
            e.Property(a => a.DisplayName).IsRequired().HasMaxLength(200);
            e.Property(a => a.CheckInOrigin).HasMaxLength(200);
            e.Property(a => a.CheckOutOrigin).HasMaxLength(200);
            e.Property(a => a.CheckInNote).HasMaxLength(300);
            e.Property(a => a.CheckOutNote).HasMaxLength(300);
            e.Property(a => a.CorrectionRequestNote).HasMaxLength(500);
            e.Property(a => a.CorrectedByName).HasMaxLength(200);
            e.Property(a => a.CorrectionReason).HasMaxLength(500);
            e.Ignore(a => a.Abierto);
            e.Ignore(a => a.Duracion);
            // Las dos consultas que existen: «mis marcas» / «las del día X», y «la que sigue abierta».
            e.HasIndex(a => new { a.UserId, a.CheckInUtc });
            e.HasIndex(a => a.CheckOutUtc);
        });

        // Pool de actividades. La FK a Developers es RESTRICT: una actividad ya aceptada es la
        // justificación de unos puntos, y borrar la ficha de quien la hizo no debe borrar esa
        // evidencia en silencio. Los enlaces a PointEntry y a DevActivity van SIN FK (ver el modelo):
        // con ellas habría dos rutas de cascada desde Developers y SQL Server las rechaza.
        modelBuilder.Entity<PoolActivity>(e =>
        {
            e.Property(p => p.WorkType).HasConversion<int>();
            e.Property(p => p.Complexity).HasConversion<int>();
            e.Property(p => p.Status).HasConversion<int>();
            e.Property(p => p.Title).IsRequired().HasMaxLength(200);
            e.Property(p => p.ExternalUrl).HasMaxLength(500);
            e.Property(p => p.ReviewComment).HasMaxLength(1000);
            // Precisión DECLARADA, y con el mismo texto que usa el migrador —decimal(6,2)—. Sin
            // declararla, EF levanta DecimalTypeDefaultWarning en cada arranque y mapea decimal(18,2)
            // por su cuenta: una base creada por EnsureCreated y otra parcheada por el migrador
            // dejarían de ser la misma base. 6,2 da hasta 9999.99 h, muy por encima de los topes
            // validados (2920 h de plazo, 1000 h de esfuerzo), y dos decimales cubren el cuarto de
            // hora, que es la granularidad con la que ya se estima en DevOps.
            //
            // AVISO PARA QUIEN ESCRIBA CONSULTAS: en SQLite EF guarda decimal como TEXT (igual que
            // DeveloperProfiles.Salary). Ordenar o filtrar por estas columnas DENTRO de un LINQ
            // traducido compararía texto — "9.0" saldría mayor que "40.0" — así que toda comparación
            // (horas > 0, rangos, validaciones) va en C# sobre objetos ya materializados. Hoy no hay
            // ninguna consulta que ordene por el plazo; que siga así.
            e.Property(p => p.HorasLimite).HasPrecision(6, 2);
            e.Property(p => p.HorasEstimadas).HasPrecision(6, 2);
            e.Ignore(p => p.EnCurso);
            e.Ignore(p => p.Vencida);
            e.HasOne(p => p.ClaimedBy).WithMany()
                .HasForeignKey(p => p.ClaimedByDeveloperId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => p.Status);                                      // el pool disponible
            e.HasIndex(p => new { p.ClaimedByDeveloperId, p.Status });      // «las mías»
        });

        modelBuilder.Entity<PoolPointsMatrixEntry>(e =>
        {
            e.Property(m => m.WorkType).HasConversion<int>();
            e.Property(m => m.Complexity).HasConversion<int>();
            // Misma precisión y por el mismo motivo que en PoolActivity: el tipo declarado aquí y el
            // que escribe el migrador tienen que ser el mismo texto, decimal(6,2).
            e.Property(m => m.HorasLimite).HasPrecision(6, 2);
            // Único: dos celdas para el mismo par harían que el valor de una actividad dependiera
            // de cuál se leyera primero.
            e.HasIndex(m => new { m.WorkType, m.Complexity }).IsUnique();
        });

        modelBuilder.Entity<PoolChecklistTemplateItem>(e =>
        {
            e.Property(t => t.WorkType).HasConversion<int>();
            e.Property(t => t.Text).IsRequired().HasMaxLength(300);
            e.HasIndex(t => new { t.WorkType, t.IsActive });
        });

        modelBuilder.Entity<PoolActivityChecklistItem>(e =>
        {
            e.Property(c => c.Text).IsRequired().HasMaxLength(300);
            e.Property(c => c.EvidenceUrl).HasMaxLength(500);
            // Cascade: el checklist de una actividad no significa nada sin ella.
            e.HasOne(c => c.Activity).WithMany()
                .HasForeignKey(c => c.PoolActivityId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.PoolActivityId);
        });

        modelBuilder.Entity<DocumentTemplate>(e =>
        {
            e.Property(p => p.Clave).IsRequired().HasMaxLength(80);
            e.Property(p => p.NombreDeArchivo).HasMaxLength(260);
            e.Property(p => p.SubidaPor).HasMaxLength(200);
            // Única: dos filas para la misma plantilla harían que el documento saliera con un
            // formato u otro según cuál se leyera primero.
            e.HasIndex(p => p.Clave).IsUnique();
        });

        modelBuilder.Entity<PoolActivityExtraCriterion>(e =>
        {
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Comment).HasMaxLength(500);
            // Cascade por lo mismo que el checklist: estos criterios describen UNA actividad y no
            // significan nada sin ella. Se navega desde la actividad, así que la colección va
            // declarada en los dos sentidos.
            e.HasOne(x => x.Activity).WithMany(a => a.ExtraCriteria)
                .HasForeignKey(x => x.PoolActivityId).OnDelete(DeleteBehavior.Cascade);
            // El mismo criterio dos veces en una actividad sumaría dos veces sin que nadie lo
            // hubiera decidido. Lo impide la base, no la disciplina de quien escriba el servicio.
            e.HasIndex(x => new { x.PoolActivityId, x.ScoringCriterionId }).IsUnique();
        });

        // Foro. Sin FK al autor a propósito: una publicación es histórica y debe sobrevivir a que
        // se borre la cuenta de quien la escribió, igual que la bitácora.
        modelBuilder.Entity<ForumPost>(e =>
        {
            e.Property(p => p.Topic).HasConversion<int>();
            e.Property(p => p.AuthorName).IsRequired().HasMaxLength(200);
            e.Property(p => p.Title).HasMaxLength(200);
            e.Property(p => p.Tags).HasMaxLength(300);
            e.Ignore(p => p.EsPublicacion);
            e.Ignore(p => p.Eliminado);
            e.Ignore(p => p.TextoVisible);
            // NoAction en la autorreferencia: una cascada sobre sí misma la rechaza SQL Server, y de
            // todos modos aquí nada se borra de verdad (se marca DeletedAtUtc).
            e.HasOne<ForumPost>().WithMany().HasForeignKey(p => p.ParentId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(p => new { p.RootId, p.CreatedAtUtc });   // traer un hilo completo
            e.HasIndex(p => new { p.ParentId });                 // colgar los comentarios de su padre
            e.HasIndex(p => p.CreatedAtUtc);                     // el muro, por fecha
        });

        modelBuilder.Entity<ForumLike>(e =>
        {
            e.HasOne(l => l.Post).WithMany().HasForeignKey(l => l.PostId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => new { l.PostId, l.UserId }).IsUnique();   // un «me gusta» por persona
        });

        // Imágenes incrustadas. El original y la miniatura van en la misma fila, pero SOLO la
        // miniatura se proyecta al pintar: traer los dos en cada refresco del muro sería mover
        // decenas de MB por la red para enseñar recuadros de 200 píxeles.
        modelBuilder.Entity<ForumAttachment>(e =>
        {
            e.HasOne(a => a.Post).WithMany().HasForeignKey(a => a.PostId).OnDelete(DeleteBehavior.Cascade);
            e.Property(a => a.FileName).IsRequired().HasMaxLength(260);
            e.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
            e.HasIndex(a => new { a.PostId, a.Orden });
        });

        // Base de conocimiento. Sin FK al autor por lo mismo que el foro: la documentación es
        // histórica y tiene que sobrevivir a que se borre la cuenta de quien la escribió.
        //
        // PointEntryId tampoco lleva FK, y esto sí es una restricción del motor y no una preferencia:
        // PointEntries ya cae en cascada desde Developers, así que una segunda ruta hasta la misma
        // tabla es de las que SQL Server rechaza al crear las restricciones. Es la misma decisión —y
        // por el mismo motivo— que PoolActivity.PointEntryId.
        modelBuilder.Entity<KnowledgeArticle>(e =>
        {
            e.Property(a => a.Status).HasConversion<int>();
            e.Property(a => a.Title).IsRequired().HasMaxLength(200);
            e.Property(a => a.AuthorName).IsRequired().HasMaxLength(200);
            e.Property(a => a.ReviewerName).HasMaxLength(200);
            e.Property(a => a.Tags).HasMaxLength(300);
            e.Ignore(a => a.YaOtorgoPuntos);
            e.Ignore(a => a.EsPublico);
            e.Ignore(a => a.EnManosDelAutor);

            // El estado va primero en el índice porque TODA consulta empieza por él: la cola es
            // «por revisar», el buscador es «publicado» y la lista propia es «lo mío».
            e.HasIndex(a => new { a.Status, a.UpdatedAtUtc });
            e.HasIndex(a => new { a.AuthorUserId, a.Status });
            e.HasIndex(a => a.PublishedAtUtc);
        });

        // Bitácora de tramos trabajados (sin FK a propósito: registro histórico de tiempo por día).
        modelBuilder.Entity<WorkInterval>(e => e.HasIndex(w => new { w.DeveloperId, w.LocalDate }));

        // Fase 4: RowVersion para concurrencia optimista (solo SQL Server).
        // En SQLite se ignora: single-user, no requiere control de concurrencia.
        //
        // Las seis primeras vienen del escritorio; las seis siguientes son nuevas de la web. Ahí la
        // pelea no es entre dos instancias del .exe sino entre dos navegadores abiertos sobre la
        // misma ficha —el líder revisando lo que el desarrollador está entregando en ese momento—,
        // y sin sello el segundo en guardar pisa al primero sin que nadie se entere.
        if (Database.IsSqlServer())
        {
            modelBuilder.Entity<Requirement>().Property(r => r.RowVersion).IsRowVersion();
            modelBuilder.Entity<VacationRequest>().Property(v => v.RowVersion).IsRowVersion();
            modelBuilder.Entity<DeploymentTarget>().Property(t => t.RowVersion).IsRowVersion();
            modelBuilder.Entity<DeploymentProfile>().Property(p => p.RowVersion).IsRowVersion();
            modelBuilder.Entity<SignatureProfile>().Property(s => s.RowVersion).IsRowVersion();
            modelBuilder.Entity<VacationDocument>().Property(d => d.RowVersion).IsRowVersion();

            modelBuilder.Entity<PoolActivity>().Property(p => p.RowVersion).IsRowVersion();
            modelBuilder.Entity<LeaveRequest>().Property(l => l.RowVersion).IsRowVersion();
            modelBuilder.Entity<Sprint>().Property(s => s.RowVersion).IsRowVersion();
            modelBuilder.Entity<Template>().Property(t => t.RowVersion).IsRowVersion();
            modelBuilder.Entity<PointEntry>().Property(p => p.RowVersion).IsRowVersion();
            modelBuilder.Entity<DevActivity>().Property(a => a.RowVersion).IsRowVersion();

            // El autor corrigiendo su artículo mientras el líder lo resuelve, cada uno en su pestaña.
            modelBuilder.Entity<KnowledgeArticle>().Property(a => a.RowVersion).IsRowVersion();
        }
        else
        {
            modelBuilder.Entity<Requirement>().Ignore(r => r.RowVersion);
            modelBuilder.Entity<VacationRequest>().Ignore(v => v.RowVersion);
            modelBuilder.Entity<DeploymentTarget>().Ignore(t => t.RowVersion);
            modelBuilder.Entity<DeploymentProfile>().Ignore(p => p.RowVersion);
            modelBuilder.Entity<SignatureProfile>().Ignore(s => s.RowVersion);
            modelBuilder.Entity<VacationDocument>().Ignore(d => d.RowVersion);

            modelBuilder.Entity<PoolActivity>().Ignore(p => p.RowVersion);
            modelBuilder.Entity<LeaveRequest>().Ignore(l => l.RowVersion);
            modelBuilder.Entity<Sprint>().Ignore(s => s.RowVersion);
            modelBuilder.Entity<Template>().Ignore(t => t.RowVersion);
            modelBuilder.Entity<PointEntry>().Ignore(p => p.RowVersion);
            modelBuilder.Entity<DevActivity>().Ignore(a => a.RowVersion);

            modelBuilder.Entity<KnowledgeArticle>().Ignore(a => a.RowVersion);
        }

        // ── Propias de la web ────────────────────────────────────────────────
        // Ambas cuelgan del usuario en cascada: un secreto o una preferencia sin dueño no significan
        // nada, a diferencia de la bitácora o la asistencia, que son histórico y sobreviven a la baja.
        modelBuilder.Entity<UserSecret>(e =>
        {
            e.Property(s => s.Proposito).IsRequired().HasMaxLength(100);
            e.Property(s => s.CipherText).IsRequired();
            e.HasOne(s => s.User).WithMany()
                .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            // Un secreto por persona y propósito: dos filas para el mismo par harían que el token
            // usado dependiera de cuál se leyera primero.
            e.HasIndex(s => new { s.UserId, s.Proposito }).IsUnique();
        });

        modelBuilder.Entity<UserPreference>(e =>
        {
            e.Property(p => p.Clave).IsRequired().HasMaxLength(100);
            e.HasOne(p => p.User).WithMany()
                .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.UserId, p.Clave }).IsUnique();
        });

        modelBuilder.Entity<PushSubscription>(e =>
        {
            // La dirección de entrega es larga —los servicios de push usan direcciones de varios
            // cientos de caracteres— y es la clave real: el mismo navegador que vuelve a suscribirse
            // trae la misma, y hay que actualizar en vez de acumular filas muertas.
            e.Property(p => p.Endpoint).IsRequired().HasMaxLength(600);
            e.Property(p => p.P256dh).IsRequired().HasMaxLength(200);
            e.Property(p => p.Auth).IsRequired().HasMaxLength(100);
            e.Property(p => p.Descripcion).HasMaxLength(120);
            e.HasIndex(p => p.Endpoint).IsUnique();
            e.HasIndex(p => p.UserId);

            // Al desactivar una cuenta se van sus suscripciones: seguir mandándole avisos a un
            // navegador de alguien que ya no entra sería filtrarle movimiento del equipo.
            e.HasOne(p => p.User).WithMany()
                .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Segundo factor ───────────────────────────────────────────────────
        modelBuilder.Entity<UserRecoveryCode>(e =>
        {
            // 64 caracteres exactos: SHA-256 en hexadecimal. El largo fijo mantiene el índice
            // pequeño y deja claro de un vistazo que ahí no hay ningún código legible.
            e.Property(c => c.CodigoHash).IsRequired().HasMaxLength(64);

            // Único por (usuario, hash) y con el usuario primero: al entrar se busca por ese par, y
            // así el mismo índice sirve para la consulta y para impedir que un código se dé de alta
            // dos veces en la misma cuenta.
            e.HasIndex(c => new { c.UserId, c.CodigoHash }).IsUnique();

            // En cascada: los códigos de rescate de una cuenta borrada no protegen nada y dejarlos
            // huérfanos sería dejar hashes de llaves de acceso sin dueño.
            e.HasOne(c => c.User).WithMany()
                .HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserTrustedDevice>(e =>
        {
            e.Property(d => d.TokenHash).IsRequired().HasMaxLength(64);
            e.Property(d => d.Descripcion).HasMaxLength(200);

            // Único por el testigo SOLO: es aleatorio de 256 bits, así que identifica al navegador
            // sin ayuda de nadie. Buscar por él y no por (usuario, testigo) evita además que una
            // cookie de una persona pueda probarse contra la cuenta de otra.
            e.HasIndex(d => d.TokenHash).IsUnique();
            e.HasIndex(d => d.UserId);

            e.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}
