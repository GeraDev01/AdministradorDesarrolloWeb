using Microsoft.EntityFrameworkCore;
using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Fase 0
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    // Fase 1
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

    // Fase 2
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
    public DbSet<SlaCommitment> SlaCommitments => Set<SlaCommitment>();
    public DbSet<ScheduledDeployment> ScheduledDeployments => Set<ScheduledDeployment>();

    // Firmas + documentos de vacaciones
    public DbSet<SignatureProfile> SignatureProfiles => Set<SignatureProfile>();
    public DbSet<VacationDocument> VacationDocuments => Set<VacationDocument>();

    // Infraestructura Azure
    public DbSet<AzureResource> AzureResources => Set<AzureResource>();
    public DbSet<Software> SoftwareItems => Set<Software>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

    // Fase 3
    public DbSet<AppSystem> AppSystems => Set<AppSystem>();
    public DbSet<AppRelease> AppReleases => Set<AppRelease>();
    public DbSet<DeploymentTarget> DeploymentTargets => Set<DeploymentTarget>();
    public DbSet<DeploymentProfile> DeploymentProfiles => Set<DeploymentProfile>();
    public DbSet<DeploymentProfileTarget> DeploymentProfileTargets => Set<DeploymentProfileTarget>();
    public DbSet<DeploymentJob> DeploymentJobs => Set<DeploymentJob>();
    public DbSet<DeploymentLogEntry> DeploymentLogEntries => Set<DeploymentLogEntry>();

    // Integración tickets
    public DbSet<DevOpsTicket> DevOpsTickets => Set<DevOpsTicket>();
    public DbSet<FreshDeskTicket> FreshDeskTickets => Set<FreshDeskTicket>();
    public DbSet<TicketLink> TicketLinks => Set<TicketLink>();
    public DbSet<WatchedTicket> WatchedTickets => Set<WatchedTicket>();

    // Avisos in-app
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DevOpsAssignmentSeen> DevOpsAssignmentsSeen => Set<DevOpsAssignmentSeen>();
    public DbSet<FreshDeskAssignmentSeen> FreshDeskAssignmentsSeen => Set<FreshDeskAssignmentSeen>();

    // Evaluaciones e hitos por desarrollador (reporte en PDF)
    public DbSet<DeveloperEvaluation> DeveloperEvaluations => Set<DeveloperEvaluation>();
    public DbSet<DeveloperMilestone> DeveloperMilestones => Set<DeveloperMilestone>();

    // Biblioteca de plantillas y scripts (administrador)
    public DbSet<Template> Templates => Set<Template>();

    // Presencia en vivo y registro de asistencia
    public DbSet<WorkPresence> WorkPresences => Set<WorkPresence>();

    // Foro del equipo
    public DbSet<ForumPost> ForumPosts => Set<ForumPost>();
    public DbSet<ForumLike> ForumLikes => Set<ForumLike>();
    public DbSet<ForumAttachment> ForumAttachments => Set<ForumAttachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Role).HasConversion<int>();
        });

        modelBuilder.Entity<AppSetting>(e => e.HasIndex(s => s.Key).IsUnique());

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
            e.HasOne(l => l.Developer).WithMany().HasForeignKey(l => l.DeveloperId).OnDelete(DeleteBehavior.Cascade);
        });

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

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.Property(a => a.Action).HasConversion<int>();
            e.Property(a => a.Outcome).HasConversion<int>();
            // Índices para que la pantalla de Bitácora siga siendo usable cuando haya años de datos.
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => a.CorrelationId);
        });

        modelBuilder.Entity<ScheduledDeployment>(e =>
        {
            e.Property(s => s.Status).HasConversion<int>();
            e.HasOne(s => s.AppRelease).WithMany().HasForeignKey(s => s.AppReleaseId).OnDelete(DeleteBehavior.Cascade);
            // NoAction en el perfil: con cascada habría dos rutas hasta la misma tabla.
            e.HasOne(s => s.Profile).WithMany().HasForeignKey(s => s.DeploymentProfileId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(s => new { s.Status, s.ScheduledAtUtc });
        });

        modelBuilder.Entity<Assignment>(e =>
        {
            e.HasOne(a => a.Requirement).WithMany(r => r.Assignments).HasForeignKey(a => a.RequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Developer).WithMany(d => d.Assignments).HasForeignKey(a => a.DeveloperId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RequirementAttachment>(e =>
        {
            e.Property(a => a.Kind).HasConversion<int>();
            e.HasOne(a => a.Requirement).WithMany().HasForeignKey(a => a.RequirementId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.RequirementId);
        });

        // Equipos: un dev pertenece a un equipo (Members); el líder es otra FK aparte.
        modelBuilder.Entity<Team>(e =>
        {
            e.HasMany(t => t.Members).WithOne(d => d.Team).HasForeignKey(d => d.TeamId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.Lead).WithMany().HasForeignKey(t => t.LeadDeveloperId).IsRequired(false).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<Developer>(e => e.Property(d => d.TeamRole).HasConversion<int>());
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

        modelBuilder.Entity<User>(e =>
            e.HasOne(u => u.Developer).WithMany().HasForeignKey(u => u.DeveloperId).IsRequired(false).OnDelete(DeleteBehavior.SetNull));

        // Minute
        modelBuilder.Entity<Minute>(e => e.Property(m => m.Type).HasConversion<int>());
        modelBuilder.Entity<MinuteActionItem>(e =>
        {
            e.HasOne(i => i.Minute).WithMany(m => m.ActionItems).HasForeignKey(i => i.MinuteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.ResponsibleDeveloper).WithMany().HasForeignKey(i => i.ResponsibleDeveloperId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        // Vacation
        modelBuilder.Entity<VacationRequest>(e =>
        {
            e.Property(v => v.Status).HasConversion<int>();
            e.HasOne(v => v.Developer).WithMany().HasForeignKey(v => v.DeveloperId).OnDelete(DeleteBehavior.Cascade);
        });

        // Sugerencias / propuestas de mejora
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

        // Note
        modelBuilder.Entity<Note>(e =>
        {
            e.Property(n => n.Priority).HasConversion<int>();
            e.HasOne(n => n.Developer).WithMany().HasForeignKey(n => n.DeveloperId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        // Performance
        modelBuilder.Entity<ScoringCriterion>(e => e.Property(c => c.Scope).HasConversion<int>());

        modelBuilder.Entity<PointEntry>(e =>
        {
            e.HasOne(p => p.Developer).WithMany().HasForeignKey(p => p.DeveloperId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Criterion).WithMany().HasForeignKey(p => p.CriterionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Requirement).WithMany().HasForeignKey(p => p.RequirementId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            e.Property(p => p.ApprovalStatus).HasConversion<int>();
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

        // Firmas + documentos de vacaciones
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

        // Fase 3 — Despliegues
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

        // Integración tickets
        modelBuilder.Entity<DevOpsTicket>(e =>
            e.HasIndex(t => t.ExternalId).IsUnique());

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

        modelBuilder.Entity<Notification>(e =>
        {
            e.Property(n => n.Kind).HasConversion<int>();
            e.HasIndex(n => new { n.ForUserId, n.ReadAt });
        });

        modelBuilder.Entity<DevOpsAssignmentSeen>(e =>
            e.HasIndex(s => new { s.UserId, s.ExternalId }).IsUnique());

        modelBuilder.Entity<FreshDeskAssignmentSeen>(e =>
            e.HasIndex(s => new { s.UserId, s.AgentId, s.ExternalId }).IsUnique());

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

        // Bitácora de tramos trabajados (sin FK a propósito: registro histórico de tiempo por día).
        modelBuilder.Entity<WorkInterval>(e => e.HasIndex(w => new { w.DeveloperId, w.LocalDate }));

        // Fase 4: RowVersion para concurrencia optimista (solo SQL Server).
        // En SQLite se ignora: single-user, no requiere control de concurrencia.
        if (Database.IsSqlServer())
        {
            modelBuilder.Entity<Requirement>().Property(r => r.RowVersion).IsRowVersion();
            modelBuilder.Entity<VacationRequest>().Property(v => v.RowVersion).IsRowVersion();
            modelBuilder.Entity<DeploymentTarget>().Property(t => t.RowVersion).IsRowVersion();
            modelBuilder.Entity<DeploymentProfile>().Property(p => p.RowVersion).IsRowVersion();
            modelBuilder.Entity<SignatureProfile>().Property(s => s.RowVersion).IsRowVersion();
            modelBuilder.Entity<VacationDocument>().Property(d => d.RowVersion).IsRowVersion();
        }
        else
        {
            modelBuilder.Entity<Requirement>().Ignore(r => r.RowVersion);
            modelBuilder.Entity<VacationRequest>().Ignore(v => v.RowVersion);
            modelBuilder.Entity<DeploymentTarget>().Ignore(t => t.RowVersion);
            modelBuilder.Entity<DeploymentProfile>().Ignore(p => p.RowVersion);
            modelBuilder.Entity<SignatureProfile>().Ignore(s => s.RowVersion);
            modelBuilder.Entity<VacationDocument>().Ignore(d => d.RowVersion);
        }
    }
}
