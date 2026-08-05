using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Data;

public static class DatabaseMigrator
{
    public static void EnsureUpToDate(AppDbContext db)
    {
        // SQL Server: EF genera el esquema completo con tipos T-SQL correctos.
        // EnsureCreated NO altera BDs ya existentes, así que aplicamos parches idempotentes.
        if (!db.Database.IsSqlite())
        {
            db.Database.EnsureCreated();
            PatchSqlServer(db);
            return;
        }

        // SQLite: crear desde cero si no existe, luego parches idempotentes por fase.
        db.Database.EnsureCreated();

        // Anti-fuerza-bruta en Users (BDs ya existentes).
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Users"" ADD COLUMN ""FailedLoginCount"" INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Users"" ADD COLUMN ""LockoutUntil"" TEXT"); } catch { }

        // Fase 2: agrega tablas nuevas a BDs que ya existían.
        // Es seguro correr contra BDs nuevas porque IF NOT EXISTS lo evita.
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Minutes"" (
                ""Id""          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Type""        INTEGER NOT NULL DEFAULT 0,
                ""Date""        TEXT    NOT NULL,
                ""Title""       TEXT    NOT NULL,
                ""Content""     TEXT,
                ""CreatedById"" INTEGER,
                ""CreatedAt""   TEXT    NOT NULL
            );
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""MinuteActionItems"" (
                ""Id""                      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""MinuteId""                INTEGER NOT NULL,
                ""Description""             TEXT    NOT NULL,
                ""ResponsibleDeveloperId""  INTEGER,
                ""DueDate""                 TEXT,
                ""IsCompleted""             INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT ""FK_MAI_Minutes""
                    FOREIGN KEY (""MinuteId"") REFERENCES ""Minutes""(""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_MAI_Developers""
                    FOREIGN KEY (""ResponsibleDeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE SET NULL
            );
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""VacationRequests"" (
                ""Id""             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""DeveloperId""    INTEGER NOT NULL,
                ""StartDate""      TEXT    NOT NULL,
                ""EndDate""        TEXT    NOT NULL,
                ""Status""         INTEGER NOT NULL DEFAULT 0,
                ""Comment""        TEXT,
                ""ReviewComment""  TEXT,
                ""ReviewedById""   INTEGER,
                ""ReviewedAt""     TEXT,
                ""CreatedAt""      TEXT    NOT NULL,
                CONSTRAINT ""FK_VacReq_Developers""
                    FOREIGN KEY (""DeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE CASCADE
            );
        ");
        // Adjunto de respaldo en solicitudes de vacaciones (BDs ya existentes).
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""VacationRequests"" ADD COLUMN ""AttachmentBytes"" BLOB"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""VacationRequests"" ADD COLUMN ""AttachmentFileName"" TEXT"); } catch { }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Notes"" (
                ""Id""             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Title""          TEXT    NOT NULL,
                ""Content""        TEXT,
                ""DeveloperId""    INTEGER,
                ""ReminderDate""   TEXT,
                ""IsCompleted""    INTEGER NOT NULL DEFAULT 0,
                ""Priority""       INTEGER NOT NULL DEFAULT 1,
                ""CreatedAt""      TEXT    NOT NULL,
                CONSTRAINT ""FK_Notes_Developers""
                    FOREIGN KEY (""DeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE SET NULL
            );
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""ScoringCriteria"" (
                ""Id""             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Name""           TEXT    NOT NULL,
                ""Description""    TEXT,
                ""DefaultPoints""  INTEGER NOT NULL DEFAULT 0,
                ""IsActive""       INTEGER NOT NULL DEFAULT 1,
                ""Scope""          INTEGER NOT NULL DEFAULT 0,
                ""CreatedAt""      TEXT    NOT NULL
            );
        ");
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ScoringCriteria"" ADD COLUMN ""Scope"" INTEGER NOT NULL DEFAULT 0"); } catch { }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""PointEntries"" (
                ""Id""               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""DeveloperId""      INTEGER NOT NULL,
                ""CriterionId""      INTEGER NOT NULL,
                ""Points""           INTEGER NOT NULL,
                ""Year""             INTEGER NOT NULL,
                ""Month""            INTEGER NOT NULL,
                ""Comment""          TEXT,
                ""AssignedByUserId"" INTEGER,
                ""RequirementId""    INTEGER,
                ""Date""             TEXT    NOT NULL,
                ""Screenshot""           BLOB,
                ""ScreenshotFileName""   TEXT,
                ""ApprovalStatus""         INTEGER NOT NULL DEFAULT 1,
                ""SubmittedByDeveloperId"" INTEGER,
                ""ReviewedByUserId""       INTEGER,
                ""ReviewedAt""             TEXT,
                ""ReviewComment""          TEXT,
                CONSTRAINT ""FK_PE_Developers""
                    FOREIGN KEY (""DeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_PE_ScoringCriteria""
                    FOREIGN KEY (""CriterionId"") REFERENCES ""ScoringCriteria""(""Id"") ON DELETE RESTRICT,
                CONSTRAINT ""FK_PE_Requirements""
                    FOREIGN KEY (""RequirementId"") REFERENCES ""Requirements""(""Id"") ON DELETE SET NULL
            );
        ");

        // Parches idempotentes: captura de pantalla en PointEntries (BDs ya existentes)
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PointEntries"" ADD COLUMN ""Screenshot"" BLOB"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PointEntries"" ADD COLUMN ""ScreenshotFileName"" TEXT"); } catch { }
        // Parches idempotentes: flujo de aprobación (autocalificación del desarrollador).
        // DEFAULT 1 = Aprobado, para no alterar el histórico ya asignado por el jefe.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PointEntries"" ADD COLUMN ""ApprovalStatus"" INTEGER NOT NULL DEFAULT 1"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PointEntries"" ADD COLUMN ""SubmittedByDeveloperId"" INTEGER"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PointEntries"" ADD COLUMN ""ReviewedByUserId"" INTEGER"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PointEntries"" ADD COLUMN ""ReviewedAt"" TEXT"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PointEntries"" ADD COLUMN ""ReviewComment"" TEXT"); } catch { }

        // ── Sesiones de trabajo (cronómetro por item) ───────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""WorkSessions"" (
                ""Id""                 INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""RequirementId""      INTEGER NOT NULL,
                ""DeveloperId""        INTEGER NOT NULL,
                ""StartedAt""          TEXT    NOT NULL,
                ""EndedAt""            TEXT,
                ""AccumulatedSeconds"" INTEGER NOT NULL DEFAULT 0,
                ""LastResumedAt""      TEXT,
                ""Status""             INTEGER NOT NULL DEFAULT 0,
                ""Note""               TEXT,
                ""CreatedAt""          TEXT    NOT NULL,
                CONSTRAINT ""FK_WS_Req"" FOREIGN KEY (""RequirementId"") REFERENCES ""Requirements""(""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_WS_Dev"" FOREIGN KEY (""DeveloperId"")   REFERENCES ""Developers""(""Id"")   ON DELETE RESTRICT
            );
        ");

        // ── Nuevos campos en Developer ────────────────────────────
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" ADD COLUMN ""Phone"" TEXT"); } catch { /* ya existe */ }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" ADD COLUMN ""HireDate"" TEXT"); } catch { /* ya existe */ }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" ADD COLUMN ""Address"" TEXT"); } catch { /* ya existe */ }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" ADD COLUMN ""EquipmentSerial"" TEXT"); } catch { /* ya existe */ }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" ADD COLUMN ""VacationDaysLeft"" INTEGER NOT NULL DEFAULT 15"); } catch { /* ya existe */ }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""LeaveRequests"" (
                ""Id""           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""DeveloperId""  INTEGER NOT NULL,
                ""Type""         INTEGER NOT NULL DEFAULT 0,
                ""Date""         TEXT    NOT NULL,
                ""DaysCount""    INTEGER NOT NULL DEFAULT 1,
                ""Reason""       TEXT,
                ""ApprovedBy""   TEXT,
                ""Notes""        TEXT,
                ""CreatedAt""    TEXT    NOT NULL,
                CONSTRAINT ""FK_LR_Dev"" FOREIGN KEY (""DeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE CASCADE
            );
        ");

        // ── Infraestructura Azure ─────────────────────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""AzureResources"" (
                ""Id""                   INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Name""                 TEXT    NOT NULL,
                ""ResourceType""         INTEGER NOT NULL DEFAULT 0,
                ""Status""               INTEGER NOT NULL DEFAULT 0,
                ""Environment""          INTEGER NOT NULL DEFAULT 0,
                ""ResourceGroup""        TEXT,
                ""SubscriptionName""     TEXT,
                ""Region""               TEXT,
                ""Url""                  TEXT,
                ""Notes""                TEXT,
                ""MonthlyCostEstimate""  REAL,
                ""CreatedAt""            TEXT    NOT NULL,
                ""UpdatedAt""            TEXT
            );
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""SoftwareItems"" (
                ""Id""              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Name""            TEXT    NOT NULL,
                ""Category""        INTEGER NOT NULL DEFAULT 0,
                ""LicenseType""     INTEGER NOT NULL DEFAULT 0,
                ""Status""          INTEGER NOT NULL DEFAULT 0,
                ""Version""         TEXT,
                ""Publisher""       TEXT,
                ""LicenseKey""      TEXT,
                ""LicenseExpiry""   TEXT,
                ""InstalledOn""     TEXT,
                ""Url""             TEXT,
                ""Notes""           TEXT,
                ""CreatedAt""       TEXT    NOT NULL,
                ""UpdatedAt""       TEXT
            );
        ");

        // ── Fase 3: Despliegues + Versionamiento ──────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""AppSystems"" (
                ""Id""                  INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Name""                TEXT    NOT NULL,
                ""Description""         TEXT,
                ""DefaultSourceFolder"" TEXT,
                ""IsActive""            INTEGER NOT NULL DEFAULT 1,
                ""CreatedAt""           TEXT    NOT NULL
            );
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""AppReleases"" (
                ""Id""           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""AppSystemId""  INTEGER NOT NULL,
                ""Version""      TEXT    NOT NULL,
                ""Changelog""    TEXT,
                ""ZipLocalPath"" TEXT,
                ""ZipBlobUrl""   TEXT,
                ""ZipChecksum""  TEXT,
                ""ZipSizeBytes"" INTEGER NOT NULL DEFAULT 0,
                ""CreatedById""  INTEGER,
                ""CreatedAt""    TEXT    NOT NULL,
                CONSTRAINT ""FK_Rel_Sys"" FOREIGN KEY (""AppSystemId"") REFERENCES ""AppSystems""(""Id"") ON DELETE CASCADE
            );
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DeploymentTargets"" (
                ""Id""             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Nombre""         TEXT    NOT NULL,
                ""Host""           TEXT    NOT NULL,
                ""Puerto""         INTEGER NOT NULL DEFAULT 21,
                ""Usuario""        TEXT    NOT NULL,
                ""Contrasena""     TEXT    NOT NULL,
                ""RutaRemota""     TEXT    NOT NULL,
                ""URL""            TEXT,
                ""LastDeployedAt"" TEXT,
                ""LastReleaseId""  INTEGER,
                ""IsActive""       INTEGER NOT NULL DEFAULT 1,
                CONSTRAINT ""FK_Tgt_Rel"" FOREIGN KEY (""LastReleaseId"") REFERENCES ""AppReleases""(""Id"") ON DELETE SET NULL
            );
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DeploymentProfiles"" (
                ""Id""                    INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Name""                  TEXT    NOT NULL,
                ""Description""           TEXT,
                ""AllowedForOperaciones"" INTEGER NOT NULL DEFAULT 0,
                ""CreatedAt""             TEXT    NOT NULL
            );
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DeploymentProfileTargets"" (
                ""Id""        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""ProfileId"" INTEGER NOT NULL,
                ""TargetId""  INTEGER NOT NULL,
                ""Order""     INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT ""FK_PT_Profile"" FOREIGN KEY (""ProfileId"") REFERENCES ""DeploymentProfiles""(""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_PT_Target""  FOREIGN KEY (""TargetId"")  REFERENCES ""DeploymentTargets""(""Id"") ON DELETE CASCADE
            );
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DeploymentJobs"" (
                ""Id""                  INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""AppReleaseId""        INTEGER NOT NULL,
                ""DeploymentProfileId"" INTEGER NOT NULL,
                ""Status""              INTEGER NOT NULL DEFAULT 0,
                ""StartedAt""           TEXT,
                ""CompletedAt""         TEXT,
                ""StartedById""         INTEGER,
                ""Notes""               TEXT,
                ""TargetsTotal""        INTEGER NOT NULL DEFAULT 0,
                ""TargetsOk""           INTEGER NOT NULL DEFAULT 0,
                ""TargetsFailed""       INTEGER NOT NULL DEFAULT 0,
                ""CreatedAt""           TEXT    NOT NULL,
                CONSTRAINT ""FK_Job_Rel""     FOREIGN KEY (""AppReleaseId"")        REFERENCES ""AppReleases""(""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_Job_Profile"" FOREIGN KEY (""DeploymentProfileId"") REFERENCES ""DeploymentProfiles""(""Id"") ON DELETE RESTRICT
            );
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DeploymentLogEntries"" (
                ""Id""         INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""JobId""      INTEGER NOT NULL,
                ""TargetId""   INTEGER,
                ""TargetName"" TEXT,
                ""Timestamp""  TEXT    NOT NULL,
                ""Message""    TEXT    NOT NULL,
                ""Level""      INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT ""FK_Log_Job"" FOREIGN KEY (""JobId"") REFERENCES ""DeploymentJobs""(""Id"") ON DELETE CASCADE
            );
        ");

        // ── Integración de tickets ─────────────────────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DevOpsTickets"" (
                ""Id""                  INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""ExternalId""          INTEGER NOT NULL UNIQUE,
                ""Title""               TEXT    NOT NULL,
                ""WorkItemType""        TEXT    NOT NULL DEFAULT '',
                ""State""               TEXT    NOT NULL DEFAULT '',
                ""Priority""            TEXT    NOT NULL DEFAULT '',
                ""AssignedTo""          TEXT    NOT NULL DEFAULT '',
                ""AreaPath""            TEXT    NOT NULL DEFAULT '',
                ""IterationPath""       TEXT    NOT NULL DEFAULT '',
                ""Tags""                TEXT    NOT NULL DEFAULT '',
                ""Description""         TEXT,
                ""StoryPoints""         REAL,
                ""CreatedAtExternal""   TEXT,
                ""UpdatedAtExternal""   TEXT,
                ""SyncedAt""            TEXT    NOT NULL,
                ""Url""                 TEXT    NOT NULL DEFAULT ''
            );
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""FreshDeskTickets"" (
                ""Id""                  INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""ExternalId""          INTEGER NOT NULL UNIQUE,
                ""Subject""             TEXT    NOT NULL,
                ""Status""              INTEGER NOT NULL DEFAULT 2,
                ""Priority""            INTEGER NOT NULL DEFAULT 1,
                ""Type""                TEXT,
                ""Source""              INTEGER NOT NULL DEFAULT 1,
                ""ResponderId""         INTEGER,
                ""AgentName""           TEXT    NOT NULL DEFAULT '',
                ""GroupName""           TEXT    NOT NULL DEFAULT '',
                ""RequesterName""       TEXT    NOT NULL DEFAULT '',
                ""RequesterEmail""      TEXT    NOT NULL DEFAULT '',
                ""Tags""                TEXT    NOT NULL DEFAULT '',
                ""Description""         TEXT,
                ""CreatedAtExternal""   TEXT,
                ""UpdatedAtExternal""   TEXT,
                ""SyncedAt""            TEXT    NOT NULL,
                ""Url""                 TEXT    NOT NULL DEFAULT ''
            );
        ");
        // Columna agregada después: el agente asignado, para detectar «me asignaron este ticket».
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""FreshDeskTickets"" ADD COLUMN ""ResponderId"" INTEGER"); } catch { /* ya existe */ }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""TicketLinks"" (
                ""Id""                  INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""DevOpsTicketId""      INTEGER NOT NULL,
                ""FreshDeskTicketId""   INTEGER NOT NULL,
                ""Notes""               TEXT,
                ""LinkedAt""            TEXT    NOT NULL,
                ""LinkedByUser""        TEXT,
                CONSTRAINT ""FK_TL_DevOps""    FOREIGN KEY (""DevOpsTicketId"")    REFERENCES ""DevOpsTickets""(""Id"")    ON DELETE CASCADE,
                CONSTRAINT ""FK_TL_FreshDesk"" FOREIGN KEY (""FreshDeskTicketId"") REFERENCES ""FreshDeskTickets""(""Id"") ON DELETE CASCADE,
                UNIQUE (""DevOpsTicketId"", ""FreshDeskTicketId"")
            );
        ");

        // Parches idempotentes sobre tablas ya existentes
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""DevOpsTickets"" ADD COLUMN ""CommentCount"" INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""DevOpsTickets"" ADD COLUMN ""AssignedToUniqueName"" TEXT"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""AppSystems"" ADD COLUMN ""DefaultBlobFolder"" TEXT"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""DeploymentProfiles"" ADD COLUMN ""IsAdHoc"" INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""DeploymentTargets"" ADD COLUMN ""LastDeployedById"" INTEGER"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""DeploymentTargets"" ADD COLUMN ""LastDeploymentJobId"" INTEGER"); } catch { }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""WatchedTickets"" (
                ""Id""              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""DevOpsTicketId""  INTEGER NOT NULL,
                ""WatchedByUser""   TEXT    NOT NULL DEFAULT '',
                ""WatchedSince""    TEXT    NOT NULL,
                CONSTRAINT ""FK_WT_DevOps"" FOREIGN KEY (""DevOpsTicketId"") REFERENCES ""DevOpsTickets""(""Id"") ON DELETE CASCADE,
                UNIQUE (""DevOpsTicketId"", ""WatchedByUser"")
            );
        ");

        // ── Equipos ─────────────────────────────────────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Teams"" (
                ""Id""              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Name""            TEXT    NOT NULL,
                ""Description""     TEXT,
                ""ColorHex""        TEXT,
                ""LeadDeveloperId"" INTEGER,
                ""CreatedAt""       TEXT    NOT NULL,
                CONSTRAINT ""FK_Team_Lead"" FOREIGN KEY (""LeadDeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE SET NULL
            );
        ");
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" ADD COLUMN ""TeamId"" INTEGER"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" ADD COLUMN ""TeamRole"" INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""AppSystems"" ADD COLUMN ""TeamId"" INTEGER"); } catch { }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Projects"" (
                ""Id""          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Name""        TEXT    NOT NULL,
                ""Client""      TEXT,
                ""Description"" TEXT,
                ""Status""      INTEGER NOT NULL DEFAULT 0,
                ""TeamId""      INTEGER,
                ""CreatedAt""   TEXT    NOT NULL,
                CONSTRAINT ""FK_Project_Team"" FOREIGN KEY (""TeamId"") REFERENCES ""Teams""(""Id"") ON DELETE SET NULL
            );
        ");

        // ── Contactos ───────────────────────────────────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Contacts"" (
                ""Id""         INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Name""       TEXT    NOT NULL,
                ""JobTitle""   TEXT,
                ""Company""    TEXT,
                ""Email""      TEXT,
                ""TeamsLink""  TEXT,
                ""Phone""      TEXT,
                ""Notes""      TEXT,
                ""CreatedAt""  TEXT    NOT NULL
            );
        ");

        // ── Reglas de auto-asignación de DevOps ─────────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DevOpsAssignmentRules"" (
                ""Id""           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Match""        INTEGER NOT NULL DEFAULT 0,
                ""MatchValue""   TEXT    NOT NULL DEFAULT '',
                ""DeveloperId""  INTEGER NOT NULL,
                ""Order""        INTEGER NOT NULL DEFAULT 0,
                ""IsActive""     INTEGER NOT NULL DEFAULT 1,
                ""CreatedAt""    TEXT    NOT NULL,
                CONSTRAINT ""FK_DOAR_Dev"" FOREIGN KEY (""DeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE CASCADE
            );
        ");

        // Requerimiento: momento del último cambio de estado
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Requirements"" ADD COLUMN ""StatusChangedAt"" TEXT"); } catch { }
        // Segundos ya reportados al ticket de DevOps (para reportar solo el delta y no duplicar).
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Requirements"" ADD COLUMN ""DevOpsReportedSeconds"" INTEGER NOT NULL DEFAULT 0"); } catch { }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DevOpsSavedFilters"" (
                ""Id""                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Name""              TEXT    NOT NULL,
                ""GlobalSearch""      TEXT,
                ""TitleContains""     TEXT,
                ""ColumnFiltersJson"" TEXT,
                ""CreatedAt""         TEXT    NOT NULL
            );
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""TeamRotations"" (
                ""Id""               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""DeveloperId""      INTEGER NOT NULL,
                ""DeveloperName""    TEXT    NOT NULL DEFAULT '',
                ""FromTeamId""       INTEGER,
                ""FromTeamName""     TEXT    NOT NULL DEFAULT 'Sin equipo',
                ""ToTeamId""         INTEGER,
                ""ToTeamName""       TEXT    NOT NULL DEFAULT 'Sin equipo',
                ""Note""             TEXT,
                ""RotatedByUserId""  INTEGER,
                ""RotatedAt""        TEXT    NOT NULL
            );
        ");

        // ── Puntos a nivel de equipo (independientes del individual) ─
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""TeamPointEntries"" (
                ""Id""                 INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""TeamId""             INTEGER NOT NULL,
                ""CriterionId""        INTEGER NOT NULL,
                ""Points""             INTEGER NOT NULL,
                ""Year""               INTEGER NOT NULL,
                ""Month""              INTEGER NOT NULL,
                ""Comment""            TEXT,
                ""AssignedByUserId""   INTEGER,
                ""Date""               TEXT    NOT NULL,
                ""Screenshot""         BLOB,
                ""ScreenshotFileName"" TEXT,
                CONSTRAINT ""FK_TPE_Team""      FOREIGN KEY (""TeamId"")      REFERENCES ""Teams""(""Id"")           ON DELETE CASCADE,
                CONSTRAINT ""FK_TPE_Criterion"" FOREIGN KEY (""CriterionId"") REFERENCES ""ScoringCriteria""(""Id"") ON DELETE RESTRICT
            );
        ");

        // ── Adjuntos de requerimientos ─────────────────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""RequirementAttachments"" (
                ""Id""                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""RequirementId""     INTEGER NOT NULL,
                ""Kind""              INTEGER NOT NULL DEFAULT 0,
                ""FileName""          TEXT    NOT NULL DEFAULT '',
                ""FileBytes""         BLOB    NOT NULL,
                ""SizeBytes""         INTEGER NOT NULL DEFAULT 0,
                ""UploadedByUserId""  INTEGER,
                ""UploadedAtUtc""     TEXT    NOT NULL,
                CONSTRAINT ""FK_ReqAtt_Req"" FOREIGN KEY (""RequirementId"") REFERENCES ""Requirements""(""Id"") ON DELETE CASCADE
            );
        ");

        // ── Firmas + documentos de vacaciones ──────────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""SignatureProfiles"" (
                ""Id""                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""DisplayName""       TEXT    NOT NULL,
                ""PngBytes""          BLOB    NOT NULL,
                ""WidthPx""           INTEGER NOT NULL DEFAULT 0,
                ""HeightPx""          INTEGER NOT NULL DEFAULT 0,
                ""OwnerDeveloperId""  INTEGER,
                ""IsDefault""         INTEGER NOT NULL DEFAULT 0,
                ""CreatedAtUtc""      TEXT    NOT NULL,
                CONSTRAINT ""FK_Sig_Dev"" FOREIGN KEY (""OwnerDeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE SET NULL
            );
        ");

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""VacationDocuments"" (
                ""Id""                  INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""VacationRequestId""   INTEGER NOT NULL,
                ""Source""              INTEGER NOT NULL DEFAULT 0,
                ""Status""              INTEGER NOT NULL DEFAULT 0,
                ""FileName""            TEXT    NOT NULL DEFAULT '',
                ""DocxBytes""           BLOB,
                ""SignedPdfBytes""      BLOB,
                ""PdfBlobUrl""          TEXT,
                ""PdfChecksum""         TEXT,
                ""SignatureProfileId""  INTEGER,
                ""SignedByUserId""      INTEGER,
                ""SignedAtUtc""         TEXT,
                ""CreatedAtUtc""        TEXT    NOT NULL,
                CONSTRAINT ""FK_VDoc_Req"" FOREIGN KEY (""VacationRequestId"")  REFERENCES ""VacationRequests""(""Id"")  ON DELETE CASCADE,
                CONSTRAINT ""FK_VDoc_Sig"" FOREIGN KEY (""SignatureProfileId"") REFERENCES ""SignatureProfiles""(""Id"") ON DELETE SET NULL
            );
        ");

        // ── Actividades libres del desarrollador ───────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DevActivities"" (
                ""Id""           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""DeveloperId""  INTEGER NOT NULL,
                ""Title""        TEXT    NOT NULL,
                ""Description""  TEXT,
                ""Status""       INTEGER NOT NULL DEFAULT 0,
                ""CreatedAt""    TEXT    NOT NULL,
                ""ClosedAt""     TEXT,
                CONSTRAINT ""FK_Act_Dev"" FOREIGN KEY (""DeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE CASCADE
            );
        ");

        // Carpeta de destino de la versión en Blob Storage (QA, Productivo, un cliente…).
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""AppReleases"" ADD COLUMN ""TargetFolder"" TEXT"); } catch { }
        // ── Compromisos de SLA ─────────────────────────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""SlaCommitments"" (
                ""Id""                     INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""RequirementId""          INTEGER,
                ""ActivityId""             INTEGER,
                ""DeveloperId""            INTEGER NOT NULL,
                ""DevOpsTicketExternalId"" INTEGER,
                ""DevOpsTicketUrl""        TEXT,
                ""DueAtUtc""               TEXT    NOT NULL,
                ""ReminderEveryHours""     INTEGER NOT NULL DEFAULT 24,
                ""NextReminderAtUtc""      TEXT,
                ""LastCommentAtUtc""       TEXT,
                ""CommentCount""           INTEGER NOT NULL DEFAULT 0,
                ""Status""                 INTEGER NOT NULL DEFAULT 0,
                ""BreachNotifiedAtUtc""    TEXT,
                ""Notes""                  TEXT,
                ""CreatedByUserId""        INTEGER,
                ""CreatedAt""              TEXT    NOT NULL,
                CONSTRAINT ""FK_Sla_Req"" FOREIGN KEY (""RequirementId"") REFERENCES ""Requirements""(""Id"")  ON DELETE CASCADE,
                CONSTRAINT ""FK_Sla_Act"" FOREIGN KEY (""ActivityId"")    REFERENCES ""DevActivities""(""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_Sla_Dev"" FOREIGN KEY (""DeveloperId"")   REFERENCES ""Developers""(""Id"")    ON DELETE RESTRICT
            );
        ");

        // ── Bitácora robusta: campos nuevos sobre AuditLog ─────────
        foreach (var col in new[]
                 {
                     @"ALTER TABLE ""AuditLogs"" ADD COLUMN ""OldValues"" TEXT",
                     @"ALTER TABLE ""AuditLogs"" ADD COLUMN ""NewValues"" TEXT",
                     @"ALTER TABLE ""AuditLogs"" ADD COLUMN ""Origin"" TEXT",
                     @"ALTER TABLE ""AuditLogs"" ADD COLUMN ""Outcome"" INTEGER NOT NULL DEFAULT 0",
                     @"ALTER TABLE ""AuditLogs"" ADD COLUMN ""CorrelationId"" TEXT",
                 })
            try { db.Database.ExecuteSqlRaw(col); } catch { /* ya existe */ }

        // ── Despliegues programados ────────────────────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""ScheduledDeployments"" (
                ""Id""                  INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""AppReleaseId""        INTEGER NOT NULL,
                ""DeploymentProfileId"" INTEGER NOT NULL,
                ""ScheduledAtUtc""      TEXT    NOT NULL,
                ""Status""              INTEGER NOT NULL DEFAULT 0,
                ""ClaimedBy""           TEXT,
                ""ClaimedAtUtc""        TEXT,
                ""DeploymentJobId""     INTEGER,
                ""ToleranciaMinutos""   INTEGER NOT NULL DEFAULT 60,
                ""Notes""               TEXT,
                ""ResultMessage""       TEXT,
                ""CreatedByUserId""     INTEGER,
                ""CreatedAt""           TEXT    NOT NULL,
                CONSTRAINT ""FK_Sched_Rel""  FOREIGN KEY (""AppReleaseId"")        REFERENCES ""AppReleases""(""Id"")        ON DELETE CASCADE,
                CONSTRAINT ""FK_Sched_Prof"" FOREIGN KEY (""DeploymentProfileId"") REFERENCES ""DeploymentProfiles""(""Id"") ON DELETE RESTRICT
            );
        ");

        // ── Sugerencias / propuestas de mejora ─────────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Suggestions"" (
                ""Id""              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""DeveloperId""     INTEGER,
                ""CreatedByUserId"" INTEGER NOT NULL,
                ""Title""           TEXT    NOT NULL,
                ""Body""            TEXT    NOT NULL,
                ""Category""        INTEGER NOT NULL DEFAULT 0,
                ""Status""          INTEGER NOT NULL DEFAULT 0,
                ""Anonymous""       INTEGER NOT NULL DEFAULT 0,
                ""AdminResponse""   TEXT,
                ""ReviewedByUserId"" INTEGER,
                ""ReviewedAt""      TEXT,
                ""CreatedAt""       TEXT    NOT NULL,
                CONSTRAINT ""FK_Sug_Dev"" FOREIGN KEY (""DeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE SET NULL
            );
        ");
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""SuggestionVotes"" (
                ""Id""           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""SuggestionId"" INTEGER NOT NULL,
                ""UserId""       INTEGER NOT NULL,
                ""CreatedAt""    TEXT    NOT NULL,
                CONSTRAINT ""FK_SugVote_Sug"" FOREIGN KEY (""SuggestionId"") REFERENCES ""Suggestions""(""Id"") ON DELETE CASCADE
            );
        ");
        try { db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SugVote_Unico"" ON ""SuggestionVotes""(""SuggestionId"",""UserId"")"); } catch { }
        // Visibilidad y votación (BDs ya existentes). Los valores por omisión reproducen el
        // comportamiento anterior —todas públicas y votables—, para no cambiarle el sentido a lo
        // que la gente ya envió.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Suggestions"" ADD COLUMN ""Visibility"" INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Suggestions"" ADD COLUMN ""OpenToVoting"" INTEGER NOT NULL DEFAULT 1"); } catch { }

        // ── Ficha de perfil del desarrollador (admin) ─────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DeveloperProfiles"" (
                ""Id""                 INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""DeveloperId""        INTEGER NOT NULL,
                ""Strengths""          TEXT,
                ""Weaknesses""         TEXT,
                ""TechStack""          TEXT,
                ""Salary""             TEXT,
                ""Currency""           TEXT,
                ""GrowthExpectations"" TEXT,
                ""Notes""              TEXT,
                ""UpdatedAt""          TEXT NOT NULL,
                CONSTRAINT ""FK_DevProfile_Dev"" FOREIGN KEY (""DeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE CASCADE
            );
        ");
        try { db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_DevProfile_Dev"" ON ""DeveloperProfiles""(""DeveloperId"")"); } catch { }

        PatchSqliteWorkSessions(db);

        // Índices declarados en el modelo, para BDs SQLite migradas (en BDs nuevas ya los crea EnsureCreated).
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_PointEntries_ApprovalStatus"" ON ""PointEntries""(""ApprovalStatus"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_WorkSessions_DeveloperId_Status"" ON ""WorkSessions""(""DeveloperId"",""Status"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_WorkSessions_RequirementId"" ON ""WorkSessions""(""RequirementId"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_WorkSessions_ActivityId"" ON ""WorkSessions""(""ActivityId"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_DevActivities_DeveloperId_Status"" ON ""DevActivities""(""DeveloperId"",""Status"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Sla_DeveloperId_Status"" ON ""SlaCommitments""(""DeveloperId"",""Status"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Sla_NextReminder"" ON ""SlaCommitments""(""NextReminderAtUtc"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Sla_DueAt"" ON ""SlaCommitments""(""DueAtUtc"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Audit_Timestamp"" ON ""AuditLogs""(""Timestamp"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Audit_Correlation"" ON ""AuditLogs""(""CorrelationId"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Sched_Status_At"" ON ""ScheduledDeployments""(""Status"",""ScheduledAtUtc"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Sug_CreatedByUser"" ON ""Suggestions""(""CreatedByUserId"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Sug_Status"" ON ""Suggestions""(""Status"")"); } catch { }

        // ── Avisos in-app ──────────────────────────────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Notifications"" (
                ""Id""        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""ForUserId"" INTEGER NOT NULL,
                ""Kind""      INTEGER NOT NULL DEFAULT 0,
                ""Title""     TEXT    NOT NULL,
                ""Message""   TEXT    NOT NULL,
                ""Url""       TEXT,
                ""DedupeKey"" TEXT,
                ""CreatedAt"" TEXT    NOT NULL,
                ""ReadAt""    TEXT
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Notif_User_Read"" ON ""Notifications""(""ForUserId"",""ReadAt"")"); } catch { }
        // Índice único PARCIAL: es el respaldo real del dedupe de avisos. La comprobación en
        // código es un lee-luego-inserta sin transacción, y con dos instancias abiertas el mismo
        // recordatorio se duplicaba. FILTRADO por DedupeKey NOT NULL a propósito: hay avisos
        // legítimos sin clave (los de asignación), y sin el filtro solo cabría UNO por usuario.
        try { db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_Notif_Dedupe"" ON ""Notifications""(""ForUserId"",""DedupeKey"") WHERE ""DedupeKey"" IS NOT NULL"); } catch { }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DevOpsAssignmentsSeen"" (
                ""Id""         INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""UserId""     INTEGER NOT NULL,
                ""ExternalId"" INTEGER NOT NULL,
                ""SeenAt""     TEXT    NOT NULL,
                UNIQUE (""UserId"", ""ExternalId"")
            );");

        // Si la tabla viene de la primera versión (sin AgentId), se recrea: es solo una caché de línea
        // base (no hay datos valiosos que perder) y así queda con AgentId y el índice único correcto.
        try
        {
            bool faltaAgentId = false;
            try { db.Database.ExecuteSqlRaw(@"SELECT ""AgentId"" FROM ""FreshDeskAssignmentsSeen"" LIMIT 0"); }
            catch { faltaAgentId = true; }   // columna (o tabla) inexistente
            if (faltaAgentId) db.Database.ExecuteSqlRaw(@"DROP TABLE IF EXISTS ""FreshDeskAssignmentsSeen""");
        }
        catch { /* si no se pudo comprobar, el CREATE de abajo la deja como esté */ }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""FreshDeskAssignmentsSeen"" (
                ""Id""         INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""UserId""     INTEGER NOT NULL,
                ""AgentId""    INTEGER NOT NULL DEFAULT 0,
                ""ExternalId"" INTEGER NOT NULL,
                ""SeenAt""     TEXT    NOT NULL,
                UNIQUE (""UserId"", ""AgentId"", ""ExternalId"")
            );");

        // ── Evaluaciones e hitos por desarrollador ─────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DeveloperEvaluations"" (
                ""Id""              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""DeveloperId""     INTEGER NOT NULL,
                ""EvaluatorUserId"" INTEGER,
                ""EvaluatorName""   TEXT,
                ""EvaluationDate""  TEXT    NOT NULL,
                ""PeriodLabel""     TEXT,
                ""OverallRating""   INTEGER,
                ""Strengths""       TEXT,
                ""Weaknesses""      TEXT,
                ""Comments""        TEXT,
                ""CreatedAt""       TEXT    NOT NULL,
                CONSTRAINT ""FK_DevEval_Dev"" FOREIGN KEY (""DeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE CASCADE
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_DevEval_Dev"" ON ""DeveloperEvaluations""(""DeveloperId"")"); } catch { }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DeveloperMilestones"" (
                ""Id""              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""DeveloperId""     INTEGER NOT NULL,
                ""Title""           TEXT    NOT NULL,
                ""Description""      TEXT,
                ""Date""            TEXT    NOT NULL,
                ""Kind""            INTEGER NOT NULL DEFAULT 0,
                ""CreatedByUserId"" INTEGER,
                ""CreatedAt""       TEXT    NOT NULL,
                CONSTRAINT ""FK_DevMile_Dev"" FOREIGN KEY (""DeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE CASCADE
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_DevMile_Dev"" ON ""DeveloperMilestones""(""DeveloperId"")"); } catch { }

        // ── Biblioteca de plantillas y scripts ─────────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Templates"" (
                ""Id""              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Kind""            INTEGER NOT NULL DEFAULT 8,
                ""Title""           TEXT    NOT NULL,
                ""Description""     TEXT,
                ""Body""            TEXT    NOT NULL,
                ""Tags""            TEXT,
                ""IsArchived""      INTEGER NOT NULL DEFAULT 0,
                ""FileBytes""       BLOB,
                ""FileName""        TEXT,
                ""UsageCount""      INTEGER NOT NULL DEFAULT 0,
                ""LastUsedAt""      TEXT,
                ""CreatedByUserId"" INTEGER,
                ""CreatedAt""       TEXT    NOT NULL,
                ""UpdatedAt""       TEXT
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Tpl_Kind_Archived"" ON ""Templates""(""Kind"",""IsArchived"")"); } catch { }

        // ── Foro del equipo ────────────────────────────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""ForumPosts"" (
                ""Id""                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""ParentId""          INTEGER,
                ""RootId""            INTEGER NOT NULL DEFAULT 0,
                ""Depth""             INTEGER NOT NULL DEFAULT 0,
                ""AuthorUserId""      INTEGER NOT NULL,
                ""AuthorName""        TEXT    NOT NULL,
                ""AuthorDeveloperId"" INTEGER,
                ""Title""             TEXT,
                ""Body""              TEXT    NOT NULL,
                ""Topic""             INTEGER NOT NULL DEFAULT 0,
                ""Tags""              TEXT,
                ""Pinned""            INTEGER NOT NULL DEFAULT 0,
                ""Locked""            INTEGER NOT NULL DEFAULT 0,
                ""CreatedAtUtc""      TEXT    NOT NULL,
                ""EditedAtUtc""       TEXT,
                ""DeletedAtUtc""      TEXT,
                ""DeletedByUserId""   INTEGER,
                CONSTRAINT ""FK_Forum_Parent"" FOREIGN KEY (""ParentId"") REFERENCES ""ForumPosts""(""Id"")
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Forum_Root_Created"" ON ""ForumPosts""(""RootId"",""CreatedAtUtc"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Forum_Parent"" ON ""ForumPosts""(""ParentId"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Forum_Created"" ON ""ForumPosts""(""CreatedAtUtc"")"); } catch { }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""ForumLikes"" (
                ""Id""           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""PostId""       INTEGER NOT NULL,
                ""UserId""       INTEGER NOT NULL,
                ""CreatedAtUtc"" TEXT    NOT NULL,
                CONSTRAINT ""FK_ForumLike_Post"" FOREIGN KEY (""PostId"") REFERENCES ""ForumPosts""(""Id"") ON DELETE CASCADE
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ForumLike_Unico"" ON ""ForumLikes""(""PostId"",""UserId"")"); } catch { }

        // Imágenes incrustadas en una publicación o comentario. Thumb es la miniatura: es lo que se
        // pinta, y por eso va en su propia columna en vez de recalcularse en cada refresco.
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""ForumAttachments"" (
                ""Id""               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""PostId""           INTEGER NOT NULL,
                ""FileName""         TEXT    NOT NULL,
                ""ContentType""      TEXT    NOT NULL,
                ""Bytes""            BLOB    NOT NULL,
                ""Thumb""            BLOB    NOT NULL,
                ""SizeBytes""        INTEGER NOT NULL DEFAULT 0,
                ""Width""            INTEGER NOT NULL DEFAULT 0,
                ""Height""           INTEGER NOT NULL DEFAULT 0,
                ""Orden""            INTEGER NOT NULL DEFAULT 0,
                ""UploadedByUserId"" INTEGER NOT NULL DEFAULT 0,
                ""CreatedAtUtc""     TEXT    NOT NULL,
                CONSTRAINT ""FK_ForumAtt_Post"" FOREIGN KEY (""PostId"") REFERENCES ""ForumPosts""(""Id"") ON DELETE CASCADE
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_ForumAtt_Post"" ON ""ForumAttachments""(""PostId"",""Orden"")"); } catch { }

        // ── Presencia en vivo y registro de asistencia ─────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""WorkPresences"" (
                ""Id""            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""UserId""        INTEGER NOT NULL,
                ""DeveloperId""   INTEGER,
                ""DisplayName""   TEXT    NOT NULL,
                ""StartedAtUtc""  TEXT    NOT NULL,
                ""EndedAtUtc""    TEXT,
                ""LastSeenUtc""   TEXT    NOT NULL,
                ""State""         INTEGER NOT NULL DEFAULT 0,
                ""StateNote""     TEXT,
                ""EndReason""     INTEGER,
                ""Origin""        TEXT
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Presence_User_Start"" ON ""WorkPresences""(""UserId"",""StartedAtUtc"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Presence_Ended"" ON ""WorkPresences""(""EndedAtUtc"")"); } catch { }

        // ── Tramos trabajados (reporte de tiempo por día) ──────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""WorkIntervals"" (
                ""Id""            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""DeveloperId""   INTEGER NOT NULL,
                ""RequirementId"" INTEGER,
                ""ActivityId""    INTEGER,
                ""StartUtc""      TEXT    NOT NULL,
                ""EndUtc""        TEXT    NOT NULL,
                ""Seconds""       INTEGER NOT NULL,
                ""LocalDate""     TEXT    NOT NULL
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_WI_Dev_Date"" ON ""WorkIntervals""(""DeveloperId"",""LocalDate"")"); } catch { }

        // ── Sprints (seguimiento del avance contra calendario) ─────
        // Sin FK en la columna nueva a propósito: el ALTER es aditivo y el servicio desliga a
        // mano al eliminar un sprint; solo las bases recién creadas llevan la FK (EnsureCreated).
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Sprints"" (
                ""Id""        INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Name""      TEXT NOT NULL,
                ""Goal""      TEXT,
                ""StartDate"" TEXT NOT NULL,
                ""EndDate""   TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Sprint_Start"" ON ""Sprints""(""StartDate"")"); } catch { }
        // Sin catch ciego en el ALTER: Requirements es la tabla central y una columna que no se
        // creó de verdad rompería TODA la app, no solo los sprints. Se comprueba con PRAGMA (el
        // ALTER de SQLite no tiene IF NOT EXISTS) y, si falta, se ejecuta dejando que un fallo
        // real suba al log en vez de tragarse.
        if (!SqliteTieneColumna(db, "Requirements", "SprintId"))
            db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Requirements"" ADD COLUMN ""SprintId"" INTEGER");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Req_Sprint"" ON ""Requirements""(""SprintId"")"); } catch { }
    }

    private static bool SqliteTieneColumna(AppDbContext db, string tabla, string columna)
    {
        var conn = db.Database.GetDbConnection();
        bool abrir = conn.State != System.Data.ConnectionState.Open;
        if (abrir) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info('{tabla}')";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (string.Equals(r.GetString(1), columna, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        finally { if (abrir) conn.Close(); }
    }

    /// <summary>
    /// Las sesiones de trabajo pasaron a admitir como objetivo un requerimiento O una actividad
    /// libre, así que RequirementId dejó de ser obligatorio y apareció ActivityId.
    ///
    /// SQLite no sabe cambiar la nulabilidad de una columna existente: hay que reconstruir la
    /// tabla y copiar los datos. Se hace una sola vez — si RequirementId ya admite NULL, no se
    /// toca nada.
    /// </summary>
    private static void PatchSqliteWorkSessions(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        bool abrir = conn.State != System.Data.ConnectionState.Open;
        if (abrir) conn.Open();
        try
        {
            bool requiereRebuild = false, existe = false;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info('WorkSessions')";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    existe = true;
                    // columnas de PRAGMA table_info: cid, name, type, notnull, dflt_value, pk
                    if (string.Equals(r.GetString(1), "RequirementId", StringComparison.OrdinalIgnoreCase)
                        && r.GetInt32(3) == 1)
                        requiereRebuild = true;
                }
            }
            if (!existe || !requiereRebuild) return;

            // Sin FK activas durante la reconstrucción; PRAGMA no puede ir dentro de una transacción.
            db.Database.ExecuteSqlRaw("PRAGMA foreign_keys=off");
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    CREATE TABLE ""WorkSessions_nuevo"" (
                        ""Id""                 INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                        ""RequirementId""      INTEGER NULL,
                        ""ActivityId""         INTEGER NULL,
                        ""DeveloperId""        INTEGER NOT NULL,
                        ""StartedAt""          TEXT    NOT NULL,
                        ""EndedAt""            TEXT,
                        ""AccumulatedSeconds"" INTEGER NOT NULL DEFAULT 0,
                        ""LastResumedAt""      TEXT,
                        ""Status""             INTEGER NOT NULL DEFAULT 0,
                        ""Note""               TEXT,
                        ""CreatedAt""          TEXT    NOT NULL,
                        CONSTRAINT ""FK_WS_Req"" FOREIGN KEY (""RequirementId"") REFERENCES ""Requirements""(""Id"")  ON DELETE CASCADE,
                        CONSTRAINT ""FK_WS_Act"" FOREIGN KEY (""ActivityId"")    REFERENCES ""DevActivities""(""Id"") ON DELETE CASCADE,
                        CONSTRAINT ""FK_WS_Dev"" FOREIGN KEY (""DeveloperId"")   REFERENCES ""Developers""(""Id"")    ON DELETE RESTRICT
                    );");

                db.Database.ExecuteSqlRaw(@"
                    INSERT INTO ""WorkSessions_nuevo""
                        (""Id"",""RequirementId"",""DeveloperId"",""StartedAt"",""EndedAt"",
                         ""AccumulatedSeconds"",""LastResumedAt"",""Status"",""Note"",""CreatedAt"")
                    SELECT ""Id"",""RequirementId"",""DeveloperId"",""StartedAt"",""EndedAt"",
                           ""AccumulatedSeconds"",""LastResumedAt"",""Status"",""Note"",""CreatedAt""
                    FROM ""WorkSessions"";");

                db.Database.ExecuteSqlRaw(@"DROP TABLE ""WorkSessions"";");
                db.Database.ExecuteSqlRaw(@"ALTER TABLE ""WorkSessions_nuevo"" RENAME TO ""WorkSessions"";");
            }
            finally
            {
                db.Database.ExecuteSqlRaw("PRAGMA foreign_keys=on");
            }
        }
        finally
        {
            if (abrir) conn.Close();
        }
    }

    /// <summary>
    /// Parches idempotentes para BDs SQL Server ya existentes (EnsureCreated no altera
    /// esquemas creados por versiones previas): agrega columnas de aprobación a
    /// PointEntries y crea la tabla WorkSessions con sus índices si faltan.
    /// </summary>
    private static void PatchSqlServer(AppDbContext db)
    {
        void Exec(string sql) { try { db.Database.ExecuteSqlRaw(sql); } catch { } }

        // Crea un índice solo si NO existe ya uno que empiece por la misma columna.
        //
        // No se comprueba por NOMBRE a propósito: si la tabla la creó EnsureCreated, EF nombró el
        // índice a su manera (IX_AuditLogs_CorrelationId) y no como este parche
        // (IX_Audit_Correlation). Comprobando por nombre se creaban DOS índices sobre la misma
        // columna, que ocupan espacio y encarecen cada escritura sin aportar nada.
        void ExecIndex(string tabla, string indice, string primeraColumna, string columnas)
            => Exec($@"
IF OBJECT_ID(N'[{tabla}]', N'U') IS NOT NULL
   AND COL_LENGTH('{tabla}','{primeraColumna}') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1 FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = OBJECT_ID(N'[{tabla}]')
          AND c.name = '{primeraColumna}' AND ic.key_ordinal = 1)
CREATE INDEX [{indice}] ON [{tabla}]({columnas});");

        // Anti-fuerza-bruta en Users.
        Exec("IF COL_LENGTH('Users','FailedLoginCount') IS NULL ALTER TABLE [Users] ADD [FailedLoginCount] int NOT NULL DEFAULT 0;");
        Exec("IF COL_LENGTH('Users','LockoutUntil') IS NULL ALTER TABLE [Users] ADD [LockoutUntil] datetime2 NULL;");

        // Columnas de autocalificación/aprobación en PointEntries (DEFAULT 1 = Aprobado para el histórico).
        Exec("IF COL_LENGTH('PointEntries','ApprovalStatus') IS NULL ALTER TABLE [PointEntries] ADD [ApprovalStatus] int NOT NULL DEFAULT 1;");
        Exec("IF COL_LENGTH('PointEntries','SubmittedByDeveloperId') IS NULL ALTER TABLE [PointEntries] ADD [SubmittedByDeveloperId] int NULL;");
        Exec("IF COL_LENGTH('PointEntries','ReviewedByUserId') IS NULL ALTER TABLE [PointEntries] ADD [ReviewedByUserId] int NULL;");
        Exec("IF COL_LENGTH('PointEntries','ReviewedAt') IS NULL ALTER TABLE [PointEntries] ADD [ReviewedAt] datetime2 NULL;");
        Exec("IF COL_LENGTH('PointEntries','ReviewComment') IS NULL ALTER TABLE [PointEntries] ADD [ReviewComment] nvarchar(max) NULL;");
        ExecIndex("PointEntries", "IX_PointEntries_ApprovalStatus", "ApprovalStatus", "[ApprovalStatus]");

        // Adjunto de respaldo en solicitudes de vacaciones.
        Exec("IF COL_LENGTH('VacationRequests','AttachmentBytes') IS NULL ALTER TABLE [VacationRequests] ADD [AttachmentBytes] varbinary(max) NULL;");
        Exec("IF COL_LENGTH('VacationRequests','AttachmentFileName') IS NULL ALTER TABLE [VacationRequests] ADD [AttachmentFileName] nvarchar(260) NULL;");

        // Tabla WorkSessions (FK a Developers sin cascada para evitar rutas múltiples de cascada).
        Exec(@"
IF OBJECT_ID(N'[WorkSessions]', N'U') IS NULL
CREATE TABLE [WorkSessions] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_WorkSessions] PRIMARY KEY,
    [RequirementId] int NOT NULL,
    [DeveloperId] int NOT NULL,
    [StartedAt] datetime2 NOT NULL,
    [EndedAt] datetime2 NULL,
    [AccumulatedSeconds] int NOT NULL DEFAULT 0,
    [LastResumedAt] datetime2 NULL,
    [Status] int NOT NULL DEFAULT 0,
    [Note] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [FK_WS_Req] FOREIGN KEY ([RequirementId]) REFERENCES [Requirements]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_WS_Dev] FOREIGN KEY ([DeveloperId]) REFERENCES [Developers]([Id])
);");
        ExecIndex("WorkSessions", "IX_WorkSessions_DeveloperId_Status", "DeveloperId", "[DeveloperId],[Status]");
        ExecIndex("WorkSessions", "IX_WorkSessions_RequirementId", "RequirementId", "[RequirementId]");

        // ── Actividades libres del desarrollador ───────────────────
        Exec(@"
IF OBJECT_ID(N'[DevActivities]', N'U') IS NULL
CREATE TABLE [DevActivities] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_DevActivities] PRIMARY KEY,
    [DeveloperId] int NOT NULL,
    [Title] nvarchar(200) NOT NULL,
    [Description] nvarchar(max) NULL,
    [Status] int NOT NULL DEFAULT 0,
    [CreatedAt] datetime2 NOT NULL,
    [ClosedAt] datetime2 NULL,
    CONSTRAINT [FK_Act_Dev] FOREIGN KEY ([DeveloperId]) REFERENCES [Developers]([Id]) ON DELETE CASCADE
);");
        ExecIndex("DevActivities", "IX_DevActivities_DeveloperId_Status", "DeveloperId", "[DeveloperId],[Status]");

        // RequirementId deja de ser obligatorio (una sesión puede cronometrar una actividad libre).
        // SQL Server no permite alterar la columna mientras la referencian un índice y una FK:
        // se quitan, se altera y se vuelven a crear. Idempotente: solo corre si sigue NOT NULL.
        // Las FK se buscan por la COLUMNA que referencian, nunca por nombre: según si la tabla la
        // creó EnsureCreated o este parche, la misma FK se llama FK_WorkSessions_Requirements_
        // RequirementId o FK_WS_Req. Buscar por nombre crearía constraints duplicadas.
        Exec(@"
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'[WorkSessions]') AND name = 'RequirementId' AND is_nullable = 0)
BEGIN
    DECLARE @fk sysname = (
        SELECT TOP 1 fk.name FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
        WHERE fk.parent_object_id = OBJECT_ID(N'[WorkSessions]') AND c.name = 'RequirementId');
    IF @fk IS NOT NULL EXEC('ALTER TABLE [WorkSessions] DROP CONSTRAINT [' + @fk + ']');

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_WorkSessions_RequirementId' AND object_id=OBJECT_ID(N'[WorkSessions]'))
        DROP INDEX [IX_WorkSessions_RequirementId] ON [WorkSessions];

    ALTER TABLE [WorkSessions] ALTER COLUMN [RequirementId] int NULL;

    CREATE INDEX [IX_WorkSessions_RequirementId] ON [WorkSessions]([RequirementId]);
    ALTER TABLE [WorkSessions] ADD CONSTRAINT [FK_WS_Req]
        FOREIGN KEY ([RequirementId]) REFERENCES [Requirements]([Id]) ON DELETE CASCADE;
END");

        Exec("IF COL_LENGTH('WorkSessions','ActivityId') IS NULL ALTER TABLE [WorkSessions] ADD [ActivityId] int NULL;");
        // NO ACTION en esta FK: con cascada habría dos caminos hasta Developers y SQL Server la rechaza.
        Exec(@"
IF COL_LENGTH('WorkSessions','ActivityId') IS NOT NULL
   AND OBJECT_ID(N'[DevActivities]', N'U') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
        WHERE fk.parent_object_id = OBJECT_ID(N'[WorkSessions]') AND c.name = 'ActivityId')
ALTER TABLE [WorkSessions] ADD CONSTRAINT [FK_WS_Act]
    FOREIGN KEY ([ActivityId]) REFERENCES [DevActivities]([Id]);");
        ExecIndex("WorkSessions", "IX_WorkSessions_ActivityId", "ActivityId", "[ActivityId]");

        // Carpeta de destino de la versión en Blob Storage (QA, Productivo, un cliente…).
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""AppReleases"" ADD COLUMN ""TargetFolder"" TEXT"); } catch { }
        // ── Compromisos de SLA ─────────────────────────────────────
        // Sin cascada hacia Developers ni DevActivities: SQL Server rechaza múltiples rutas de
        // cascada que terminan en la misma tabla.
        Exec(@"
IF OBJECT_ID(N'[SlaCommitments]', N'U') IS NULL
CREATE TABLE [SlaCommitments] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SlaCommitments] PRIMARY KEY,
    [RequirementId] int NULL,
    [ActivityId] int NULL,
    [DeveloperId] int NOT NULL,
    [DevOpsTicketExternalId] int NULL,
    [DevOpsTicketUrl] nvarchar(500) NULL,
    [DueAtUtc] datetime2 NOT NULL,
    [ReminderEveryHours] int NOT NULL DEFAULT 24,
    [NextReminderAtUtc] datetime2 NULL,
    [LastCommentAtUtc] datetime2 NULL,
    [CommentCount] int NOT NULL DEFAULT 0,
    [Status] int NOT NULL DEFAULT 0,
    [BreachNotifiedAtUtc] datetime2 NULL,
    [Notes] nvarchar(max) NULL,
    [CreatedByUserId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [FK_Sla_Req] FOREIGN KEY ([RequirementId]) REFERENCES [Requirements]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_Sla_Act] FOREIGN KEY ([ActivityId])    REFERENCES [DevActivities]([Id]),
    CONSTRAINT [FK_Sla_Dev] FOREIGN KEY ([DeveloperId])   REFERENCES [Developers]([Id])
);");
        ExecIndex("SlaCommitments", "IX_Sla_DeveloperId_Status", "DeveloperId", "[DeveloperId],[Status]");
        ExecIndex("SlaCommitments", "IX_Sla_NextReminder", "NextReminderAtUtc", "[NextReminderAtUtc]");
        ExecIndex("SlaCommitments", "IX_Sla_DueAt", "DueAtUtc", "[DueAtUtc]");

        // ── Bitácora robusta ───────────────────────────────────────
        Exec("IF COL_LENGTH('AuditLogs','OldValues') IS NULL ALTER TABLE [AuditLogs] ADD [OldValues] nvarchar(max) NULL;");
        Exec("IF COL_LENGTH('AuditLogs','NewValues') IS NULL ALTER TABLE [AuditLogs] ADD [NewValues] nvarchar(max) NULL;");
        Exec("IF COL_LENGTH('AuditLogs','Origin') IS NULL ALTER TABLE [AuditLogs] ADD [Origin] nvarchar(200) NULL;");
        Exec("IF COL_LENGTH('AuditLogs','Outcome') IS NULL ALTER TABLE [AuditLogs] ADD [Outcome] int NOT NULL DEFAULT 0;");
        Exec("IF COL_LENGTH('AuditLogs','CorrelationId') IS NULL ALTER TABLE [AuditLogs] ADD [CorrelationId] nvarchar(40) NULL;");
        Exec("IF COL_LENGTH('AppReleases','TargetFolder') IS NULL ALTER TABLE [AppReleases] ADD [TargetFolder] nvarchar(200) NULL;");
        Exec("IF COL_LENGTH('Developers','Phone') IS NULL ALTER TABLE [Developers] ADD [Phone] nvarchar(50) NULL;");
        Exec("IF COL_LENGTH('Developers','EquipmentSerial') IS NULL ALTER TABLE [Developers] ADD [EquipmentSerial] nvarchar(100) NULL;");
        Exec("IF COL_LENGTH('Requirements','DevOpsReportedSeconds') IS NULL ALTER TABLE [Requirements] ADD [DevOpsReportedSeconds] int NOT NULL DEFAULT 0;");
        Exec("IF COL_LENGTH('DevOpsTickets','AssignedToUniqueName') IS NULL ALTER TABLE [DevOpsTickets] ADD [AssignedToUniqueName] nvarchar(256) NULL;");
        Exec("IF COL_LENGTH('AppSystems','DefaultBlobFolder') IS NULL ALTER TABLE [AppSystems] ADD [DefaultBlobFolder] nvarchar(200) NULL;");
        Exec("IF COL_LENGTH('DeploymentProfiles','IsAdHoc') IS NULL ALTER TABLE [DeploymentProfiles] ADD [IsAdHoc] bit NOT NULL DEFAULT 0;");
        // Quién dejó la versión que hoy tiene cada servidor, y de qué despliegue salió. Sin FK, igual
        // que DeploymentJob.StartedById: es historia, y dar de baja a un usuario no debe borrarla.
        Exec("IF COL_LENGTH('DeploymentTargets','LastDeployedById') IS NULL ALTER TABLE [DeploymentTargets] ADD [LastDeployedById] int NULL;");
        Exec("IF COL_LENGTH('DeploymentTargets','LastDeploymentJobId') IS NULL ALTER TABLE [DeploymentTargets] ADD [LastDeploymentJobId] int NULL;");
        ExecIndex("AuditLogs", "IX_Audit_Timestamp", "Timestamp", "[Timestamp]");
        ExecIndex("AuditLogs", "IX_Audit_Correlation", "CorrelationId", "[CorrelationId]");

        // ── Despliegues programados ────────────────────────────────
        Exec(@"
IF OBJECT_ID(N'[ScheduledDeployments]', N'U') IS NULL
CREATE TABLE [ScheduledDeployments] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ScheduledDeployments] PRIMARY KEY,
    [AppReleaseId] int NOT NULL,
    [DeploymentProfileId] int NOT NULL,
    [ScheduledAtUtc] datetime2 NOT NULL,
    [Status] int NOT NULL DEFAULT 0,
    [ClaimedBy] nvarchar(200) NULL,
    [ClaimedAtUtc] datetime2 NULL,
    [DeploymentJobId] int NULL,
    [ToleranciaMinutos] int NOT NULL DEFAULT 60,
    [Notes] nvarchar(max) NULL,
    [ResultMessage] nvarchar(max) NULL,
    [CreatedByUserId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [FK_Sched_Rel]  FOREIGN KEY ([AppReleaseId])        REFERENCES [AppReleases]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_Sched_Prof] FOREIGN KEY ([DeploymentProfileId]) REFERENCES [DeploymentProfiles]([Id])
);");
        ExecIndex("ScheduledDeployments", "IX_Sched_Status_At", "Status", "[Status],[ScheduledAtUtc]");

        // ── Avisos in-app ──────────────────────────────────────────
        Exec(@"
IF OBJECT_ID(N'[Notifications]', N'U') IS NULL
CREATE TABLE [Notifications] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Notifications] PRIMARY KEY,
    [ForUserId] int NOT NULL,
    [Kind] int NOT NULL DEFAULT 0,
    [Title] nvarchar(300) NOT NULL,
    [Message] nvarchar(max) NOT NULL,
    [Url] nvarchar(1000) NULL,
    [DedupeKey] nvarchar(200) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ReadAt] datetime2 NULL
);");
        ExecIndex("Notifications", "IX_Notif_User_Read", "ForUserId", "[ForUserId],[ReadAt]");
        // Directo y NO por ExecIndex: ese helper omite el índice si ya existe otro que empiece
        // por la misma columna, y IX_Notif_User_Read ya empieza por ForUserId — nunca se crearía.
        // Filtrado por DedupeKey NOT NULL: en SQL Server NULL es un valor comparable en un índice
        // único, así que sin el filtro solo cabría un aviso sin clave por usuario.
        Exec(@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Notif_Dedupe' AND object_id=OBJECT_ID('Notifications'))
CREATE UNIQUE INDEX [UX_Notif_Dedupe] ON [Notifications]([ForUserId],[DedupeKey]) WHERE [DedupeKey] IS NOT NULL;");

        // ── Sugerencias / propuestas de mejora ─────────────────────
        Exec(@"
IF OBJECT_ID(N'[Suggestions]', N'U') IS NULL
CREATE TABLE [Suggestions] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Suggestions] PRIMARY KEY,
    [DeveloperId] int NULL,
    [CreatedByUserId] int NOT NULL,
    [Title] nvarchar(150) NOT NULL,
    [Body] nvarchar(max) NOT NULL,
    [Category] int NOT NULL DEFAULT 0,
    [Status] int NOT NULL DEFAULT 0,
    [Anonymous] bit NOT NULL DEFAULT 0,
    [AdminResponse] nvarchar(max) NULL,
    [ReviewedByUserId] int NULL,
    [ReviewedAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [FK_Sug_Dev] FOREIGN KEY ([DeveloperId]) REFERENCES [Developers]([Id]) ON DELETE SET NULL
);");
        ExecIndex("Suggestions", "IX_Sug_CreatedByUser", "CreatedByUserId", "[CreatedByUserId]");
        ExecIndex("Suggestions", "IX_Sug_Status", "Status", "[Status]");
        // Visibilidad y votación. Por omisión, lo que ya existía: pública y votable.
        Exec("IF COL_LENGTH('Suggestions','Visibility') IS NULL ALTER TABLE [Suggestions] ADD [Visibility] int NOT NULL DEFAULT 0;");
        Exec("IF COL_LENGTH('Suggestions','OpenToVoting') IS NULL ALTER TABLE [Suggestions] ADD [OpenToVoting] bit NOT NULL DEFAULT 1;");

        // ── Votos de sugerencias ───────────────────────────────────
        Exec(@"
IF OBJECT_ID(N'[SuggestionVotes]', N'U') IS NULL
CREATE TABLE [SuggestionVotes] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SuggestionVotes] PRIMARY KEY,
    [SuggestionId] int NOT NULL,
    [UserId] int NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [FK_SugVote_Sug] FOREIGN KEY ([SuggestionId]) REFERENCES [Suggestions]([Id]) ON DELETE CASCADE
);");
        Exec(@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_SugVote_Unico' AND object_id=OBJECT_ID('SuggestionVotes'))
CREATE UNIQUE INDEX [IX_SugVote_Unico] ON [SuggestionVotes]([SuggestionId],[UserId]);");

        // ── Ficha de perfil del desarrollador (admin) ─────────────
        Exec(@"
IF OBJECT_ID(N'[DeveloperProfiles]', N'U') IS NULL
CREATE TABLE [DeveloperProfiles] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_DeveloperProfiles] PRIMARY KEY,
    [DeveloperId] int NOT NULL,
    [Strengths] nvarchar(max) NULL,
    [Weaknesses] nvarchar(max) NULL,
    [TechStack] nvarchar(max) NULL,
    [Salary] decimal(18,2) NULL,
    [Currency] nvarchar(10) NULL,
    [GrowthExpectations] nvarchar(max) NULL,
    [Notes] nvarchar(max) NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [FK_DevProfile_Dev] FOREIGN KEY ([DeveloperId]) REFERENCES [Developers]([Id]) ON DELETE CASCADE
);");
        Exec(@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_DevProfile_Dev' AND object_id=OBJECT_ID('DeveloperProfiles'))
CREATE UNIQUE INDEX [IX_DevProfile_Dev] ON [DeveloperProfiles]([DeveloperId]);");

        Exec(@"
IF OBJECT_ID(N'[DevOpsAssignmentsSeen]', N'U') IS NULL
CREATE TABLE [DevOpsAssignmentsSeen] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_DevOpsAssignmentsSeen] PRIMARY KEY,
    [UserId] int NOT NULL,
    [ExternalId] int NOT NULL,
    [SeenAt] datetime2 NOT NULL,
    CONSTRAINT [UQ_DevOpsSeen_User_Ext] UNIQUE ([UserId],[ExternalId])
);");

        // ── Freshdesk: tickets, vínculos y línea base de «asignado a mí» ──
        // Normalmente FreshDeskTickets/TicketLinks las crea EnsureCreated, pero en una BD vieja (creada
        // antes de que existiera el modelo de Freshdesk) EnsureCreated ya no corre; se crean aquí si faltan.
        Exec(@"
IF OBJECT_ID(N'[FreshDeskTickets]', N'U') IS NULL
CREATE TABLE [FreshDeskTickets] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_FreshDeskTickets] PRIMARY KEY,
    [ExternalId] bigint NOT NULL,
    [Subject] nvarchar(max) NOT NULL,
    [Status] int NOT NULL,
    [Priority] int NOT NULL,
    [Type] nvarchar(max) NULL,
    [Source] int NOT NULL,
    [ResponderId] bigint NULL,
    [AgentName] nvarchar(max) NOT NULL,
    [GroupName] nvarchar(max) NOT NULL,
    [RequesterName] nvarchar(max) NOT NULL,
    [RequesterEmail] nvarchar(max) NOT NULL,
    [Tags] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [CreatedAtExternal] datetime2 NULL,
    [UpdatedAtExternal] datetime2 NULL,
    [SyncedAt] datetime2 NOT NULL,
    [Url] nvarchar(max) NOT NULL
);");
        Exec(@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_FreshDeskTickets_ExternalId' AND object_id=OBJECT_ID('FreshDeskTickets'))
CREATE UNIQUE INDEX [IX_FreshDeskTickets_ExternalId] ON [FreshDeskTickets]([ExternalId]);");
        Exec(@"
IF OBJECT_ID(N'[TicketLinks]', N'U') IS NULL
CREATE TABLE [TicketLinks] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_TicketLinks] PRIMARY KEY,
    [DevOpsTicketId] int NOT NULL,
    [FreshDeskTicketId] int NOT NULL,
    [Notes] nvarchar(max) NULL,
    [LinkedAt] datetime2 NOT NULL,
    [LinkedByUser] nvarchar(max) NULL,
    CONSTRAINT [FK_TL_DevOps] FOREIGN KEY ([DevOpsTicketId]) REFERENCES [DevOpsTickets]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_TL_FreshDesk] FOREIGN KEY ([FreshDeskTicketId]) REFERENCES [FreshDeskTickets]([Id]) ON DELETE CASCADE
);");
        Exec(@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_TicketLinks_DevOps_FreshDesk' AND object_id=OBJECT_ID('TicketLinks'))
CREATE UNIQUE INDEX [IX_TicketLinks_DevOps_FreshDesk] ON [TicketLinks]([DevOpsTicketId],[FreshDeskTicketId]);");

        // Y si la tabla ya existía pero de una versión anterior, se agrega la columna del agente asignado.
        Exec("IF COL_LENGTH('FreshDeskTickets','ResponderId') IS NULL ALTER TABLE [FreshDeskTickets] ADD [ResponderId] bigint NULL;");
        // Si la tabla viene de la primera versión (sin AgentId), se recrea: es solo caché de línea base.
        Exec("IF OBJECT_ID('FreshDeskAssignmentsSeen','U') IS NOT NULL AND COL_LENGTH('FreshDeskAssignmentsSeen','AgentId') IS NULL DROP TABLE [FreshDeskAssignmentsSeen];");
        Exec(@"
IF OBJECT_ID(N'[FreshDeskAssignmentsSeen]', N'U') IS NULL
CREATE TABLE [FreshDeskAssignmentsSeen] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_FreshDeskAssignmentsSeen] PRIMARY KEY,
    [UserId] int NOT NULL,
    [AgentId] bigint NOT NULL CONSTRAINT [DF_FreshDeskSeen_AgentId] DEFAULT 0,
    [ExternalId] bigint NOT NULL,
    [SeenAt] datetime2 NOT NULL,
    CONSTRAINT [UQ_FreshDeskSeen_User_Agent_Ext] UNIQUE ([UserId],[AgentId],[ExternalId])
);");

        // ── Evaluaciones e hitos por desarrollador ─────────────────
        Exec(@"
IF OBJECT_ID(N'[DeveloperEvaluations]', N'U') IS NULL
CREATE TABLE [DeveloperEvaluations] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_DeveloperEvaluations] PRIMARY KEY,
    [DeveloperId] int NOT NULL,
    [EvaluatorUserId] int NULL,
    [EvaluatorName] nvarchar(200) NULL,
    [EvaluationDate] datetime2 NOT NULL,
    [PeriodLabel] nvarchar(100) NULL,
    [OverallRating] int NULL,
    [Strengths] nvarchar(max) NULL,
    [Weaknesses] nvarchar(max) NULL,
    [Comments] nvarchar(max) NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [FK_DevEval_Dev] FOREIGN KEY ([DeveloperId]) REFERENCES [Developers]([Id]) ON DELETE CASCADE
);");
        ExecIndex("DeveloperEvaluations", "IX_DevEval_Dev", "DeveloperId", "[DeveloperId]");

        Exec(@"
IF OBJECT_ID(N'[DeveloperMilestones]', N'U') IS NULL
CREATE TABLE [DeveloperMilestones] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_DeveloperMilestones] PRIMARY KEY,
    [DeveloperId] int NOT NULL,
    [Title] nvarchar(300) NOT NULL,
    [Description] nvarchar(max) NULL,
    [Date] datetime2 NOT NULL,
    [Kind] int NOT NULL DEFAULT 0,
    [CreatedByUserId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [FK_DevMile_Dev] FOREIGN KEY ([DeveloperId]) REFERENCES [Developers]([Id]) ON DELETE CASCADE
);");
        ExecIndex("DeveloperMilestones", "IX_DevMile_Dev", "DeveloperId", "[DeveloperId]");

        // ── Biblioteca de plantillas y scripts ─────────────────────
        Exec(@"
IF OBJECT_ID(N'[Templates]', N'U') IS NULL
CREATE TABLE [Templates] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Templates] PRIMARY KEY,
    [Kind] int NOT NULL DEFAULT 8,
    [Title] nvarchar(200) NOT NULL,
    [Description] nvarchar(max) NULL,
    [Body] nvarchar(max) NOT NULL,
    [Tags] nvarchar(300) NULL,
    [IsArchived] bit NOT NULL DEFAULT 0,
    [FileBytes] varbinary(max) NULL,
    [FileName] nvarchar(260) NULL,
    [UsageCount] int NOT NULL DEFAULT 0,
    [LastUsedAt] datetime2 NULL,
    [CreatedByUserId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL
);");
        ExecIndex("Templates", "IX_Tpl_Kind_Archived", "Kind", "[Kind],[IsArchived]");

        // ── Foro del equipo ────────────────────────────────────────
        // NO ACTION en la autorreferencia: SQL Server rechaza una cascada de una tabla sobre sí
        // misma. Da igual, aquí nada se borra de verdad (se marca DeletedAtUtc).
        Exec(@"
IF OBJECT_ID(N'[ForumPosts]', N'U') IS NULL
CREATE TABLE [ForumPosts] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ForumPosts] PRIMARY KEY,
    [ParentId] int NULL,
    [RootId] int NOT NULL DEFAULT 0,
    [Depth] int NOT NULL DEFAULT 0,
    [AuthorUserId] int NOT NULL,
    [AuthorName] nvarchar(200) NOT NULL,
    [AuthorDeveloperId] int NULL,
    [Title] nvarchar(200) NULL,
    [Body] nvarchar(max) NOT NULL,
    [Topic] int NOT NULL DEFAULT 0,
    [Tags] nvarchar(300) NULL,
    [Pinned] bit NOT NULL DEFAULT 0,
    [Locked] bit NOT NULL DEFAULT 0,
    [CreatedAtUtc] datetime2 NOT NULL,
    [EditedAtUtc] datetime2 NULL,
    [DeletedAtUtc] datetime2 NULL,
    [DeletedByUserId] int NULL,
    CONSTRAINT [FK_Forum_Parent] FOREIGN KEY ([ParentId]) REFERENCES [ForumPosts]([Id])
);");
        ExecIndex("ForumPosts", "IX_Forum_Root_Created", "RootId", "[RootId],[CreatedAtUtc]");
        ExecIndex("ForumPosts", "IX_Forum_Parent", "ParentId", "[ParentId]");
        ExecIndex("ForumPosts", "IX_Forum_Created", "CreatedAtUtc", "[CreatedAtUtc]");

        Exec(@"
IF OBJECT_ID(N'[ForumLikes]', N'U') IS NULL
CREATE TABLE [ForumLikes] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ForumLikes] PRIMARY KEY,
    [PostId] int NOT NULL,
    [UserId] int NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [FK_ForumLike_Post] FOREIGN KEY ([PostId]) REFERENCES [ForumPosts]([Id]) ON DELETE CASCADE
);");
        Exec(@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_ForumLike_Unico' AND object_id=OBJECT_ID('ForumLikes'))
CREATE UNIQUE INDEX [IX_ForumLike_Unico] ON [ForumLikes]([PostId],[UserId]);");

        // Imágenes incrustadas. Thumb es la miniatura: es lo que se pinta, y por eso va en su propia
        // columna en vez de recalcularse en cada refresco del muro.
        Exec(@"
IF OBJECT_ID(N'[ForumAttachments]', N'U') IS NULL
CREATE TABLE [ForumAttachments] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ForumAttachments] PRIMARY KEY,
    [PostId] int NOT NULL,
    [FileName] nvarchar(260) NOT NULL,
    [ContentType] nvarchar(100) NOT NULL,
    [Bytes] varbinary(max) NOT NULL,
    [Thumb] varbinary(max) NOT NULL,
    [SizeBytes] bigint NOT NULL DEFAULT 0,
    [Width] int NOT NULL DEFAULT 0,
    [Height] int NOT NULL DEFAULT 0,
    [Orden] int NOT NULL DEFAULT 0,
    [UploadedByUserId] int NOT NULL DEFAULT 0,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [FK_ForumAtt_Post] FOREIGN KEY ([PostId]) REFERENCES [ForumPosts]([Id]) ON DELETE CASCADE
);");
        ExecIndex("ForumAttachments", "IX_ForumAtt_Post", "PostId", "[PostId],[Orden]");

        // ── Presencia en vivo y registro de asistencia ─────────────
        Exec(@"
IF OBJECT_ID(N'[WorkPresences]', N'U') IS NULL
CREATE TABLE [WorkPresences] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_WorkPresences] PRIMARY KEY,
    [UserId] int NOT NULL,
    [DeveloperId] int NULL,
    [DisplayName] nvarchar(200) NOT NULL,
    [StartedAtUtc] datetime2 NOT NULL,
    [EndedAtUtc] datetime2 NULL,
    [LastSeenUtc] datetime2 NOT NULL,
    [State] int NOT NULL DEFAULT 0,
    [StateNote] nvarchar(200) NULL,
    [EndReason] int NULL,
    [Origin] nvarchar(200) NULL
);");
        ExecIndex("WorkPresences", "IX_Presence_User_Start", "UserId", "[UserId],[StartedAtUtc]");
        ExecIndex("WorkPresences", "IX_Presence_Ended", "EndedAtUtc", "[EndedAtUtc]");

        // ── Tramos trabajados (reporte de tiempo por día) ──────────
        Exec(@"
IF OBJECT_ID(N'[WorkIntervals]', N'U') IS NULL
CREATE TABLE [WorkIntervals] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_WorkIntervals] PRIMARY KEY,
    [DeveloperId] int NOT NULL,
    [RequirementId] int NULL,
    [ActivityId] int NULL,
    [StartUtc] datetime2 NOT NULL,
    [EndUtc] datetime2 NOT NULL,
    [Seconds] int NOT NULL,
    [LocalDate] datetime2 NOT NULL
);");
        ExecIndex("WorkIntervals", "IX_WI_Dev_Date", "DeveloperId", "[DeveloperId],[LocalDate]");

        // ── Sprints (seguimiento del avance contra calendario) ─────
        Exec(@"
IF OBJECT_ID(N'[Sprints]', N'U') IS NULL
CREATE TABLE [Sprints] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Sprints] PRIMARY KEY,
    [Name] nvarchar(100) NOT NULL,
    [Goal] nvarchar(1000) NULL,
    [StartDate] datetime2 NOT NULL,
    [EndDate] datetime2 NOT NULL,
    [CreatedAt] datetime2 NOT NULL
);");
        ExecIndex("Sprints", "IX_Sprint_Start", "StartDate", "[StartDate]");
        // Sin FK a propósito (ver la rama SQLite): el servicio desliga a mano al eliminar.
        // Directo, sin el catch de Exec: el IF COL_LENGTH ya lo hace idempotente, y un fallo REAL
        // en la columna de la tabla central debe subir al log, no tragarse — sin ella rompe toda
        // la app, no solo los sprints.
        db.Database.ExecuteSqlRaw("IF COL_LENGTH('Requirements','SprintId') IS NULL ALTER TABLE [Requirements] ADD [SprintId] int NULL;");
        // ExecIndex comprueba por primera columna, no por nombre: en una base recién creada
        // EnsureCreated ya dejó IX_Requirements_SprintId y crear otro con distinto nombre sería
        // un duplicado silencioso.
        ExecIndex("Requirements", "IX_Req_Sprint", "SprintId", "[SprintId]");
    }
}
