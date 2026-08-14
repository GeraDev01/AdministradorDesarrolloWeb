using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Infrastructure.Data;

public static class DatabaseMigrator
{
    /// <summary>
    /// Pone la base al día: crea lo que falte y aplica los parches idempotentes acumulados.
    ///
    /// <para><b>La API es la única dueña del esquema.</b> Este migrador se portó tal cual del
    /// escritorio, pero a partir del corte a la web el ejecutable de escritorio deja de ejecutar
    /// DDL: si dos programas distintos crean columnas sobre la misma base, tarde o temprano una
    /// versión vieja del escritorio revive un parche que la web ya superó, o al revés. Un solo
    /// dueño, y es la API.</para>
    ///
    /// <para><b>Quien invoque este método debe envolverlo en <c>sp_getapplock</c></b> (ámbito de
    /// sesión, con la misma cadena de recurso en todas las instancias). En el escritorio no hacía
    /// falta porque migraba un proceso a la vez; en la web la API arranca en varias instancias —o
    /// se reinicia durante un despliegue escalonado— y dos migraciones simultáneas se pisan: los
    /// <c>IF COL_LENGTH … IS NULL ALTER TABLE</c> no son atómicos, así que dos hilos pueden
    /// comprobar a la vez que la columna falta y el segundo ALTER falla (o peor, corre a medias
    /// sobre una tabla que el otro está reconstruyendo). El bloqueo lo pone el llamador y no este
    /// método porque el candado debe abarcar también el arranque que decide migrar.</para>
    /// </summary>
    /// <summary>
    /// Deja el esquema al día y devuelve las sentencias que FALLARON, con su motivo.
    ///
    /// <para><b>Devuelve algo, y quien llama tiene que mirarlo.</b> Antes no devolvía nada y cada
    /// fallo se tragaba en silencio; el resultado fue un migrador que se saltó 114 sentencias y
    /// anunció «Esquema al día». Una lista vacía es la ÚNICA señal honesta de que todo se aplicó.</para>
    /// </summary>
    public static IReadOnlyList<string> EnsureUpToDate(AppDbContext db)
    {
        // SQL Server: EF genera el esquema completo con tipos T-SQL correctos.
        // EnsureCreated NO altera BDs ya existentes, así que aplicamos parches idempotentes.
        if (!db.Database.IsSqlite())
        {
            db.Database.EnsureCreated();
            var fallidas = PatchSqlServer(db);
            SembrarVentanaDeCaducidadDeVacaciones(db);
            // Después de los parches: necesita las columnas de horas ya creadas para poder rellenarlas.
            ConvertirPlazosDeDiasAHorasUnaVez(db);
            return fallidas;
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
                ""MinutesSpent""           INTEGER,
                ""EvidenceUrl""            TEXT,
                ""ReviewRound""            INTEGER NOT NULL DEFAULT 0,
                ""ReviewHistory""          TEXT,
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
        // Parches idempotentes: evidencia de la actividad (tiempo declarado y enlace al item).
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PointEntries"" ADD COLUMN ""MinutesSpent"" INTEGER"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PointEntries"" ADD COLUMN ""EvidenceUrl"" TEXT"); } catch { }
        // Réplica del desarrollador a un rechazo.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PointEntries"" ADD COLUMN ""ReviewRound"" INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PointEntries"" ADD COLUMN ""ReviewHistory"" TEXT"); } catch { }

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

        // ── Ajuste manual del saldo de vacaciones ─────────────────
        //
        // El saldo NO se guarda: se calcula desde HireDate y las solicitudes cada vez que se
        // pregunta. Lo único que se guarda es esta corrección que escribe el líder, porque es el
        // único dato del saldo que no se puede deducir de nada.
        //
        // DEFAULT 0 y NULLables: el histórico queda como «nunca ajustado», que es la verdad. Un
        // valor distinto de cero por omisión movería de golpe el saldo de las once fichas que ya
        // hay sin que nadie lo hubiera decidido.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" ADD COLUMN ""VacationAdjustmentDays"" INTEGER NOT NULL DEFAULT 0"); } catch { /* ya existe */ }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" ADD COLUMN ""VacationAdjustmentNote"" TEXT"); } catch { /* ya existe */ }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" ADD COLUMN ""VacationAdjustmentBy"" TEXT"); } catch { /* ya existe */ }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" ADD COLUMN ""VacationAdjustmentAtUtc"" TEXT"); } catch { /* ya existe */ }

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
                ""Status""                  INTEGER NOT NULL DEFAULT 1,
                ""RequestedByDeveloperId""  INTEGER,
                ""ReviewedById""            INTEGER,
                ""ReviewedAt""              TEXT,
                ""ReviewComment""           TEXT,
                ""AttachmentBytes""         BLOB,
                ""AttachmentFileName""      TEXT,
                ""HoraInicio""              TEXT,
                ""HoraFin""                 TEXT,
                CONSTRAINT ""FK_LR_Dev"" FOREIGN KEY (""DeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE CASCADE
            );
        ");

        // Flujo de solicitud/aprobación y justificante en permisos (BDs ya existentes).
        // DEFAULT 1 = Aprobada: lo que hay en el histórico lo capturó el administrador al CONCEDER
        // el permiso, no es una solicitud esperando respuesta. Dejarlo en Pendiente le llenaría la
        // bandeja de trámites ya resueltos hace meses.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""LeaveRequests"" ADD COLUMN ""Status"" INTEGER NOT NULL DEFAULT 1"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""LeaveRequests"" ADD COLUMN ""RequestedByDeveloperId"" INTEGER"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""LeaveRequests"" ADD COLUMN ""ReviewedById"" INTEGER"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""LeaveRequests"" ADD COLUMN ""ReviewedAt"" TEXT"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""LeaveRequests"" ADD COLUMN ""ReviewComment"" TEXT"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""LeaveRequests"" ADD COLUMN ""AttachmentBytes"" BLOB"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""LeaveRequests"" ADD COLUMN ""AttachmentFileName"" TEXT"); } catch { }

        // Permisos POR HORAS: el tramo del día. Se agregan al lado de DaysCount y no en su lugar —
        // los permisos de días completos siguen existiendo igual, y son todo lo que hay en el
        // histórico.
        //
        // SIN RELLENO, y a propósito: las dos propiedades del modelo son TimeOnly? (anulables), así
        // que las filas que ya están quedan con NULL en las dos y eso significa exactamente lo que
        // son, permisos de día completo. La lección del sello de sesión —una columna nueva en NULL
        // sobre una propiedad NO anulable deja filas que EF no puede materializar— se cumple aquí
        // por el otro lado: si alguna de estas dos dejara de ser anulable, habría que rellenarla en
        // esta misma línea o nadie podría leer un solo permiso.
        //
        // TEXT porque es lo que el proveedor de SQLite usa para TimeOnly ('HH:mm:ss.fffffff').
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""LeaveRequests"" ADD COLUMN ""HoraInicio"" TEXT"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""LeaveRequests"" ADD COLUMN ""HoraFin"" TEXT"); } catch { }

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
        // Prioridad definida por el líder y estimación del desarrollador.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""DevOpsTickets"" ADD COLUMN ""PriorityConfirmedAt"" TEXT"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""DevOpsTickets"" ADD COLUMN ""PriorityConfirmedByUserId"" INTEGER"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""DevOpsTickets"" ADD COLUMN ""EstimatedHours"" REAL"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""DevOpsTickets"" ADD COLUMN ""EstimatedAt"" TEXT"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""DevOpsTickets"" ADD COLUMN ""EstimatedByDeveloperId"" INTEGER"); } catch { }
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
        // Qué hace cada persona DENTRO de su equipo, la frase que el organigrama pinta bajo su nombre.
        // Va aquí, pegada a TeamRole, porque son el mismo dato en dos formas —de qué es y qué hace— y
        // quien añada mañana otra columna de equipo la va a buscar en este renglón.
        //
        // VERSIÓN DE SQLITE: comillas dobles y ADD COLUMN … TEXT. La gemela de T-SQL está en
        // PatchSqlServer con nvarchar(200) y su IF COL_LENGTH; están traducidas, no copiadas.
        //
        // Nullable y sin DEFAULT a propósito: la función es opcional y «no escrita todavía» tiene que
        // poder distinguirse de «escrita y vacía», que es lo que un DEFAULT '' borraría.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" ADD COLUMN ""TeamFunction"" TEXT"); } catch { }

        // De qué equipo cuelga cada equipo: la columna que convierte la lista de equipos en un árbol
        // y permite los subequipos.
        //
        // VERSIÓN DE SQLITE: comillas dobles, ADD COLUMN … INTEGER y el REFERENCES en la misma
        // sentencia, que es la única forma de ponerle clave foránea a una columna añadida (SQLite no
        // sabe agregar una restricción después). La gemela de T-SQL está en PatchSqlServer, con su
        // IF COL_LENGTH y su ALTER TABLE … ADD CONSTRAINT aparte: están traducidas, no copiadas.
        //
        // NO HAY NADA QUE RELLENAR, y conviene dejarlo escrito para que nadie tenga que
        // preguntárselo: cuando una columna nueva cae en NULL sobre una propiedad que el modelo
        // declara NO anulable, EF no puede materializar la fila y revienta CUALQUIER consulta de esa
        // tabla —le pasó al sello de sesión de Users y dejó fuera a todo el mundo—. Aquí la
        // propiedad es «int?»: el NULL es el valor legítimo de «equipo raíz», que es justo lo que
        // son todos los equipos que ya existían. Rellenarlos con algo sería inventarles un padre.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Teams"" ADD COLUMN ""EquipoPadreId"" INTEGER REFERENCES ""Teams""(""Id"")"); } catch { }

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

        // Evidencia adjunta a las actividades libres (capturas, documentos).
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DevActivityAttachments"" (
                ""Id""               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""ActivityId""       INTEGER NOT NULL,
                ""FileName""         TEXT    NOT NULL,
                ""ContentType""      TEXT    NOT NULL DEFAULT 'application/octet-stream',
                ""Bytes""            BLOB    NOT NULL,
                ""SizeBytes""        INTEGER NOT NULL DEFAULT 0,
                ""Description""      TEXT,
                ""UploadedByUserId"" INTEGER NOT NULL DEFAULT 0,
                ""CreatedAtUtc""     TEXT    NOT NULL,
                CONSTRAINT ""FK_ActAdj_Act"" FOREIGN KEY (""ActivityId"") REFERENCES ""DevActivities""(""Id"") ON DELETE CASCADE
            );
        ");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_DevActivityAttachments_ActivityId"" ON ""DevActivityAttachments""(""ActivityId"")"); } catch { }

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

        // Rastro de los avisos de SLA ya dados (ver SlaCommitment.ReminderNotifiedAtUtc).
        //
        // En el escritorio ese rastro era un HashSet en memoria del proceso; en el servidor se
        // perdería en cada despliegue y la siguiente vuelta del trabajo de fondo volvería a avisar
        // de todo lo ya avisado. Se guardan NULLABLES y sin valor por omisión a propósito: null
        // significa «nunca se avisó», que es la verdad sobre el histórico, y así el escritorio —que
        // lee esta misma base hasta el corte y no conoce estas columnas— sigue insertando y leyendo
        // sin enterarse. Solo se añade: nada se renombra ni se quita.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""SlaCommitments"" ADD COLUMN ""ReminderNotifiedAtUtc"" TEXT"); } catch { /* ya existe */ }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""SlaCommitments"" ADD COLUMN ""OverdueNotifiedAtUtc"" TEXT"); } catch { /* ya existe */ }

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

        // ── Asistencia oficial (entrada y salida marcadas a mano) ──
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""AttendanceRecords"" (
                ""Id""                       INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""UserId""                   INTEGER NOT NULL,
                ""DeveloperId""              INTEGER,
                ""DisplayName""              TEXT    NOT NULL,
                ""CheckInUtc""               TEXT    NOT NULL,
                ""CheckOutUtc""              TEXT,
                ""CheckInOrigin""            TEXT,
                ""CheckOutOrigin""           TEXT,
                ""CheckInNote""              TEXT,
                ""CheckOutNote""             TEXT,
                ""CloseKind""                INTEGER,
                ""CorrectionRequestNote""    TEXT,
                ""CorrectionRequestedAtUtc"" TEXT,
                ""CorrectedByUserId""        INTEGER,
                ""CorrectedByName""          TEXT,
                ""CorrectedAtUtc""           TEXT,
                ""CorrectionReason""         TEXT
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Att_User_CheckIn"" ON ""AttendanceRecords""(""UserId"",""CheckInUtc"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Att_Open"" ON ""AttendanceRecords""(""CheckOutUtc"")"); } catch { }

        // ── Pool de actividades valoradas ──────────────────────────
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""PoolActivities"" (
                ""Id""                   INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Title""                TEXT    NOT NULL,
                ""Description""          TEXT,
                ""WorkType""             INTEGER NOT NULL DEFAULT 0,
                ""Complexity""           INTEGER NOT NULL DEFAULT 0,
                ""Points""               INTEGER NOT NULL DEFAULT 0,
                ""Status""               INTEGER NOT NULL DEFAULT 0,
                ""ExternalUrl""          TEXT,
                ""CreatedByUserId""      INTEGER,
                ""CreatedAt""            TEXT    NOT NULL,
                ""ClaimedByDeveloperId"" INTEGER,
                ""ClaimedAt""            TEXT,
                ""ClaimDeadlineAt""      TEXT,
                ""HorasLimite""          TEXT,
                ""HorasEstimadas""       TEXT,
                ""HorasEstimadasEnUtc""  TEXT,
                ""ReturnedCount""        INTEGER NOT NULL DEFAULT 0,
                ""DeliveredAt""          TEXT,
                ""ReviewedByUserId""     INTEGER,
                ""ReviewedAt""           TEXT,
                ""ReviewComment""        TEXT,
                ""ReviewRound""          INTEGER NOT NULL DEFAULT 0,
                ""ReviewHistory""        TEXT,
                ""PointEntryId""         INTEGER,
                ""LinkedDevActivityId""  INTEGER,
                CONSTRAINT ""FK_Pool_Dev"" FOREIGN KEY (""ClaimedByDeveloperId"") REFERENCES ""Developers""(""Id"") ON DELETE RESTRICT
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Pool_Status"" ON ""PoolActivities""(""Status"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Pool_Claimed_Status"" ON ""PoolActivities""(""ClaimedByDeveloperId"",""Status"")"); } catch { }
        // Prioridad por omisión Media (1): las actividades que ya existían no tenían urgencia
        // declarada, y suponerlas críticas o irrelevantes sería inventar información.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolActivities"" ADD COLUMN ""Priority"" INTEGER NOT NULL DEFAULT 1"); } catch { }
        // Nula: significa «usa los días de la matriz», que es lo que se hacía antes de existir.
        // OBSOLETA para la web desde el paso a horas; se conserva porque el ESCRITORIO la lee en
        // producción hasta el corte. Se puede tirar DESPUÉS del corte, junto con la de PoolPointsMatrix.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolActivities"" ADD COLUMN ""DiasLimite"" INTEGER"); } catch { }
        // Plazo y esfuerzo en HORAS. Se AÑADEN al lado de DiasLimite en vez de renombrarla: renombrar
        // una columna que el escritorio lee lo rompe en producción el mismo día del despliegue.
        // TEXT y no REAL: EF Core guarda decimal en SQLite como TEXT (igual que DeveloperProfiles.Salary,
        // más abajo). Una columna REAL leída como decimal acaba dependiendo de qué convertidor toque
        // primero; MonthlyCostEstimate REAL es la incoherencia que ya hay ahí y no el patrón a copiar.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolActivities"" ADD COLUMN ""HorasLimite"" TEXT"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolActivities"" ADD COLUMN ""HorasEstimadas"" TEXT"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolActivities"" ADD COLUMN ""HorasEstimadasEnUtc"" TEXT"); } catch { }
        // Vínculo con Azure DevOps y marca de agua del empuje. Todas NULAS: lo que ya está publicado
        // no está ligado a nada, y suponer un número sería inventarse un ticket.
        //
        // «DevOpsEsfuerzoEnviado» va en TEXT y no en REAL por lo mismo que HorasEstimadas: EF guarda
        // decimal como TEXT en SQLite, y las dos se comparan por igualdad entre sí. Una en REAL y la
        // otra en TEXT harían que nunca se parecieran, y toda actividad ligada se quedaría
        // «pendiente de enviar» para siempre.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolActivities"" ADD COLUMN ""DevOpsWorkItemId"" INTEGER"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolActivities"" ADD COLUMN ""DevOpsEsfuerzoEnviado"" TEXT"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolActivities"" ADD COLUMN ""DevOpsPrioridadEnviada"" INTEGER"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolActivities"" ADD COLUMN ""DevOpsEmpujadoEnUtc"" TEXT"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolActivities"" ADD COLUMN ""DevOpsUltimoError"" TEXT"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Pool_DevOps"" ON ""PoolActivities""(""DevOpsWorkItemId"")"); } catch { }

        // A qué subequipo se publica una actividad, o NULO para toda la casa. Nace en nulo en todo lo
        // que ya existe, que es lo que hace que el pool siga viéndose entero el día del despliegue.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolActivities"" ADD COLUMN ""EquipoId"" INTEGER"); } catch { }
        // El estado va de primera columna porque toda consulta del pool empieza filtrando por él.
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Pool_Equipo"" ON ""PoolActivities""(""Status"",""EquipoId"")"); } catch { }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""PoolPointsMatrix"" (
                ""Id""              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""WorkType""        INTEGER NOT NULL,
                ""Complexity""      INTEGER NOT NULL,
                ""Points""          INTEGER NOT NULL DEFAULT 0,
                ""DiasLimite""      INTEGER NOT NULL DEFAULT 0,
                ""HorasLimite""     TEXT    NOT NULL DEFAULT '0.0',
                ""UpdatedAt""       TEXT    NOT NULL,
                ""UpdatedByUserId"" INTEGER
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_PoolMatrix"" ON ""PoolPointsMatrix""(""WorkType"",""Complexity"")"); } catch { }
        // NOT NULL con un default CONSTANTE: es lo único que admite ALTER TABLE ADD COLUMN en SQLite.
        // '0.0' y no '0' porque ése es el texto exacto que escribe Microsoft.Data.Sqlite al guardar un
        // decimal, y conviene que lo que rellena el migrador y lo que escribe la aplicación tengan la
        // misma forma. DiasLimite se queda al lado, intacta, para el escritorio.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""PoolPointsMatrix"" ADD COLUMN ""HorasLimite"" TEXT NOT NULL DEFAULT '0.0'"); } catch { }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""PoolChecklistTemplateItems"" (
                ""Id""                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""WorkType""          INTEGER NOT NULL,
                ""Text""              TEXT    NOT NULL,
                ""Orden""             INTEGER NOT NULL DEFAULT 0,
                ""RequiereEvidencia"" INTEGER NOT NULL DEFAULT 0,
                ""IsActive""          INTEGER NOT NULL DEFAULT 1,
                ""CreatedAt""         TEXT    NOT NULL
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_PoolTpl_Tipo_Activo"" ON ""PoolChecklistTemplateItems""(""WorkType"",""IsActive"")"); } catch { }

        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""PoolActivityChecklistItems"" (
                ""Id""                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""PoolActivityId""    INTEGER NOT NULL,
                ""Text""              TEXT    NOT NULL,
                ""Orden""             INTEGER NOT NULL DEFAULT 0,
                ""RequiereEvidencia"" INTEGER NOT NULL DEFAULT 0,
                ""IsDone""            INTEGER NOT NULL DEFAULT 0,
                ""DoneAtUtc""         TEXT,
                ""EvidenceUrl""       TEXT,
                CONSTRAINT ""FK_PoolChk_Pool"" FOREIGN KEY (""PoolActivityId"") REFERENCES ""PoolActivities""(""Id"") ON DELETE CASCADE
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_PoolChk_Actividad"" ON ""PoolActivityChecklistItems""(""PoolActivityId"")"); } catch { }

        // Plantillas de documento sustituibles desde la web. Van en la base porque el disco del
        // contenedor es efímero: en disco desaparecerían en el siguiente despliegue.
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DocumentTemplates"" (
                ""Id""              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Clave""           TEXT    NOT NULL,
                ""Contenido""       BLOB    NOT NULL,
                ""NombreDeArchivo"" TEXT,
                ""SubidaPor""       TEXT,
                ""SubidaEnUtc""     TEXT    NOT NULL
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_DocTpl_Clave"" ON ""DocumentTemplates""(""Clave"")"); } catch { }

        // Criterios extra que se evalúan en una actividad concreta. Guardan su propia copia del
        // nombre y de los puntos: quien toma la actividad viendo «+5» tiene que cobrar 5 aunque el
        // catálogo cambie mientras la trabaja.
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""PoolActivityExtraCriteria"" (
                ""Id""                 INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""PoolActivityId""     INTEGER NOT NULL,
                ""ScoringCriterionId"" INTEGER,
                ""Name""               TEXT    NOT NULL,
                ""Points""             INTEGER NOT NULL DEFAULT 0,
                ""IsMet""              INTEGER,
                ""EvaluatedAtUtc""     TEXT,
                ""Comment""            TEXT,
                CONSTRAINT ""FK_PoolExtra_Pool"" FOREIGN KEY (""PoolActivityId"") REFERENCES ""PoolActivities""(""Id"") ON DELETE CASCADE
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_PoolExtra"" ON ""PoolActivityExtraCriteria""(""PoolActivityId"",""ScoringCriterionId"")"); } catch { }

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

        // ══════════════════════════════════════════════════════════════════════════════════
        // Parches NUEVOS de la web (no existen en el escritorio). Van al final para no alterar
        // el orden de los ya probados contra la base real.
        // ══════════════════════════════════════════════════════════════════════════════════

        // Sello de sesión. En el escritorio no hacía falta: la sesión moría con el proceso, así que
        // cambiar la contraseña o dar de baja a alguien surtía efecto al siguiente arranque. En la
        // web la cookie de autenticación puede seguir viva horas después, y sin este sello la
        // cuenta desactivada sigue entrando hasta que expire. Al cambiar la contraseña o desactivar
        // la cuenta se regenera el sello; los tickets emitidos con el sello anterior dejan de valer.
        // El relleno de la línea siguiente es obligatorio, no un adorno: `User.SecurityStamp` es
        // `string` no anulable y una fila con NULL no se puede materializar — la consulta de
        // LoginAsync revienta y no entra nadie. Ver la explicación larga en la rama de SQL Server.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Users"" ADD COLUMN ""SecurityStamp"" TEXT"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"UPDATE ""Users"" SET ""SecurityStamp"" = lower(hex(randomblob(16))) WHERE ""SecurityStamp"" IS NULL"); } catch { }

        // RowVersion (concurrencia optimista) NO se aplica en SQLite: no existe el tipo rowversion
        // y el original ya usa ese patrón — el escritorio siempre corrió su concurrencia contra SQL
        // Server. Ver la rama PatchSqlServer.

        // PAT personal de Azure DevOps cifrado del lado del servidor.
        // En el escritorio el PAT vivía en un archivo por máquina protegido con DPAPI, que ata el
        // secreto al usuario de Windows y a ese equipo. En la web no hay DPAPI ni «esa máquina»:
        // el secreto tiene que viajar con la cuenta, así que se guarda cifrado en la base y solo
        // el servidor lo descifra (el navegador nunca ve el texto claro).
        // El índice único (UserId, Proposito) es lo que hace que «el PAT de fulano para DevOps»
        // sea uno y no una colección que crece con cada guardado.
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""UserSecrets"" (
                ""Id""         INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""UserId""     INTEGER NOT NULL,
                ""Proposito""  TEXT    NOT NULL,
                ""CipherText"" TEXT    NOT NULL,
                ""UpdatedAt""  TEXT    NOT NULL,
                CONSTRAINT ""FK_UserSecret_User"" FOREIGN KEY (""UserId"") REFERENCES ""Users""(""Id"") ON DELETE CASCADE
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_UserSecrets_User_Proposito"" ON ""UserSecrets""(""UserId"",""Proposito"")"); } catch { }

        // Preferencias de interfaz por usuario (sustituye al columnas.json por máquina).
        // El escritorio guardaba el ancho y el orden de las columnas en un archivo local, así que
        // quien cambiaba de equipo perdía su configuración y volvía a acomodar las rejillas. Aquí
        // la preferencia cuelga del usuario y lo sigue a cualquier navegador.
        // El valor va como JSON en una sola columna a propósito: cada pantalla guarda su forma sin
        // que agregar una preferencia nueva obligue a migrar la tabla.
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""UserPreferences"" (
                ""Id""     INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""UserId"" INTEGER NOT NULL,
                ""Clave""  TEXT    NOT NULL,
                ""Json""   TEXT    NOT NULL,
                CONSTRAINT ""FK_UserPref_User"" FOREIGN KEY (""UserId"") REFERENCES ""Users""(""Id"") ON DELETE CASCADE
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_UserPreferences_User_Clave"" ON ""UserPreferences""(""UserId"",""Clave"")"); } catch { }

        // Suscripciones a avisos push (sustituyen a los globos de la bandeja del sistema).
        // En el escritorio la aplicación seguía viva escondida en la bandeja y podía avisar cuando
        // quisiera; una pestaña cerrada no puede. Aquí se guarda a qué navegador entregar el aviso.
        // Una fila por NAVEGADOR y no por persona: quien use el portátil y el teléfono tiene dos.
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""PushSubscriptions"" (
                ""Id""           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""UserId""       INTEGER NOT NULL,
                ""Endpoint""     TEXT    NOT NULL,
                ""P256dh""       TEXT    NOT NULL,
                ""Auth""         TEXT    NOT NULL,
                ""Descripcion""  TEXT    NULL,
                ""CreatedAtUtc"" TEXT    NOT NULL,
                ""LastOkUtc""    TEXT    NULL,
                CONSTRAINT ""FK_PushSub_User"" FOREIGN KEY (""UserId"") REFERENCES ""Users""(""Id"") ON DELETE CASCADE
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_PushSubscriptions_Endpoint"" ON ""PushSubscriptions""(""Endpoint"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_PushSubscriptions_User"" ON ""PushSubscriptions""(""UserId"")"); } catch { }

        // Último latido del cronómetro. En el escritorio, cerrar la ventana era un acto deliberado
        // y la sesión se cerraba con él. En la web cerrar la pestaña —o que se duerma el equipo— es
        // lo normal y no avisa: sin este dato la sesión abierta se quedaría corriendo para siempre
        // o habría que descartar el tramo entero. Con el latido, el tiempo se consolida hasta el
        // último momento en que sabemos que la persona seguía ahí.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""WorkSessions"" ADD COLUMN ""LastHeartbeatUtc"" TEXT"); } catch { }

        // ── Segundo factor: código de la aplicación del teléfono ──────────────────────────────
        //
        // Tres columnas de ESTADO en Users y dos tablas. El SECRETO no aparece por ningún lado de
        // este bloque, y no es un descuido: vive cifrado en UserSecrets, con la protección de datos
        // del servidor y bajo su propio propósito. Una columna en claro aquí sería una llave de
        // acceso legible para cualquiera que abriera una consulta.
        //
        // «Activo» arranca en FALSO para todas las cuentas que ya existen. Es lo que hace que el
        // segundo factor sea obligatorio sin excepciones al desplegar: nadie lo tiene, y a nadie se
        // le deja hacer nada más que activarlo.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Users"" ADD COLUMN ""SegundoFactorActivo"" INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Users"" ADD COLUMN ""SegundoFactorDesdeUtc"" TEXT"); } catch { }
        // La última ventana de treinta segundos aceptada: la ANTIRREPETICIÓN. Sin esta columna, un
        // código visto por encima del hombro sirve durante minuto y medio.
        try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Users"" ADD COLUMN ""SegundoFactorUltimaVentana"" INTEGER"); } catch { }

        // Los ocho códigos de rescate, uno por fila y HASHEADOS.
        // Una fila por código y no los ocho juntos en una columna: cada uno se gasta por separado y
        // hay que poder contar cuántos quedan sin leer, parsear y reescribir el conjunto entero —que
        // es la forma de que dos intentos a la vez se pisen y un código gastado «reviva».
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""UserRecoveryCodes"" (
                ""Id""           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""UserId""       INTEGER NOT NULL,
                ""CodigoHash""   TEXT    NOT NULL,
                ""CreatedAtUtc"" TEXT    NOT NULL,
                ""UsadoEnUtc""   TEXT    NULL,
                CONSTRAINT ""FK_UserRecoveryCode_User"" FOREIGN KEY (""UserId"") REFERENCES ""Users""(""Id"") ON DELETE CASCADE
            );");
        // Único por (usuario, hash), con el usuario primero: el mismo índice sirve para buscar al
        // entrar y para impedir que un código se dé de alta dos veces en la misma cuenta. No hace
        // falta otro índice solo por UserId — este ya lo lleva de primera columna.
        try { db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_UserRecoveryCodes_User_Hash"" ON ""UserRecoveryCodes""(""UserId"",""CodigoHash"")"); } catch { }

        // Los navegadores en los que ya no se vuelve a pedir el código durante treinta días.
        // Se guarda en la base y no solo en una cookie firmada porque hay que poder RETIRAR la
        // confianza: cuando el líder reinicia el segundo factor de alguien que perdió el teléfono,
        // los equipos recordados tienen que dejar de valer en ese mismo momento. Una cookie
        // autosuficiente no se puede alcanzar desde el servidor; una fila se borra.
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""UserTrustedDevices"" (
                ""Id""           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""UserId""       INTEGER NOT NULL,
                ""TokenHash""    TEXT    NOT NULL,
                ""CreatedAtUtc"" TEXT    NOT NULL,
                ""ExpiraEnUtc""  TEXT    NOT NULL,
                ""UltimoUsoUtc"" TEXT    NULL,
                ""Descripcion""  TEXT    NULL,
                CONSTRAINT ""FK_UserTrustedDevice_User"" FOREIGN KEY (""UserId"") REFERENCES ""Users""(""Id"") ON DELETE CASCADE
            );");
        // Único por el testigo solo: son 256 bits aleatorios, identifican al navegador sin ayuda.
        try { db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_UserTrustedDevices_Token"" ON ""UserTrustedDevices""(""TokenHash"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_UserTrustedDevices_User"" ON ""UserTrustedDevices""(""UserId"")"); } catch { }

        // ── Base de conocimiento (SQLite) ────────────────────────────────────────────────────
        //
        // ESTA ES LA VERSIÓN DE SQLITE: comillas dobles, TEXT/INTEGER y CREATE TABLE IF NOT EXISTS.
        // La gemela de SQL Server está en PatchSqlServer y NO es la misma sentencia — está traducida,
        // no copiada. Si hay que tocar una, hay que traducir el cambio a la otra.
        //
        // Una sola tabla para todo: un término del glosario es un artículo corto etiquetado y una
        // guía de despliegue es uno largo. Sin clave foránea al autor (la documentación es histórica
        // y sobrevive a que se borre la cuenta) ni a PointEntries — ahí la razón es del otro motor:
        // PointEntries ya cae en cascada desde Developers y SQL Server rechaza la segunda ruta, así
        // que las dos ramas se quedan igual para que el esquema no dependa de dónde corra.
        //
        // RowVersion no aparece: no existe en SQLite y el modelo la ignora ahí.
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""KnowledgeArticles"" (
                ""Id""                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Title""             TEXT    NOT NULL,
                ""Body""              TEXT    NOT NULL,
                ""Tags""              TEXT,
                ""Status""            INTEGER NOT NULL DEFAULT 0,
                ""AuthorUserId""      INTEGER NOT NULL,
                ""AuthorName""        TEXT    NOT NULL,
                ""AuthorDeveloperId"" INTEGER,
                ""CreatedAtUtc""      TEXT    NOT NULL,
                ""UpdatedAtUtc""      TEXT,
                ""SubmittedAtUtc""    TEXT,
                ""ReviewedByUserId""  INTEGER,
                ""ReviewerName""      TEXT,
                ""ReviewedAtUtc""     TEXT,
                ""ReviewComment""     TEXT,
                ""ReviewRound""       INTEGER NOT NULL DEFAULT 0,
                ""ReviewHistory""     TEXT,
                ""PublishedAtUtc""    TEXT,
                ""PointEntryId""      INTEGER,
                ""PointsAwarded""     INTEGER NOT NULL DEFAULT 0
            );");
        // ── Los días que no se trabajan (SQLite) ─────────────────────────────────────
        //
        // Los del artículo 74 se siembran desde la regla al arrancar; los que ponga la casa se
        // escriben a mano. Índice ÚNICO por fecha: un festivo repetido se descontaría dos veces de
        // las vacaciones de quien lo pida.
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DiasFestivos"" (
                ""Id""      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Fecha""   TEXT    NOT NULL,
                ""Motivo""  TEXT    NOT NULL,
                ""EsDeLey"" INTEGER NOT NULL DEFAULT 1
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Festivo_Fecha"" ON ""DiasFestivos""(""Fecha"")"); } catch { }

        // ── Qué hace, en cada equipo, quien tiene cada rol (SQLite) ──────────────────
        //
        // Sustituye a teclear la misma frase una vez por persona. La clave única (TeamId, Rol) la
        // pone la BASE y no el servicio: dos pestañas guardando a la vez dejarían dos filas para el
        // mismo puesto, y quien leyera se quedaría con la que devolviera el motor.
        //
        // Cae en cascada con el equipo: describe un puesto DENTRO de él y fuera no significa nada.
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DescripcionesDeRolDeEquipo"" (
                ""Id""          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""TeamId""      INTEGER NOT NULL,
                ""Rol""         INTEGER NOT NULL,
                ""Descripcion"" TEXT    NOT NULL,
                CONSTRAINT ""FK_DescRol_Team"" FOREIGN KEY (""TeamId"") REFERENCES ""Teams""(""Id"") ON DELETE CASCADE
            );");
        try { db.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_DescRol_EquipoRol"" ON ""DescripcionesDeRolDeEquipo""(""TeamId"",""Rol"")"); } catch { }

        // El estado va de primera columna porque toda consulta empieza por él: la cola es «por
        // revisar», el buscador es «publicado» y la lista propia es «lo mío».
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Know_Estado"" ON ""KnowledgeArticles""(""Status"",""UpdatedAtUtc"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Know_Autor"" ON ""KnowledgeArticles""(""AuthorUserId"",""Status"")"); } catch { }
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_Know_Publicado"" ON ""KnowledgeArticles""(""PublishedAtUtc"")"); } catch { }

        // Las imágenes incrustadas en los artículos (SQLite). Van en su propia tabla y no en una
        // columna del artículo porque son binarios de megas y el cuerpo se lee entero en cada
        // búsqueda. ESTA ES LA VERSIÓN DE SQLITE; la gemela de SQL Server está traducida en
        // PatchSqlServer, y ahí el BLOB es varbinary(max).
        //
        // Aquí sí hay clave foránea, al revés que el autor del artículo: una imagen sin artículo no
        // significa nada y nadie podría volver a llegar a ella, así que se va con él en cascada.
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""KnowledgeImages"" (
                ""Id""               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""ArticleId""        INTEGER NOT NULL,
                ""FileName""         TEXT    NOT NULL,
                ""ContentType""      TEXT    NOT NULL,
                ""Bytes""            BLOB    NOT NULL,
                ""SizeBytes""        INTEGER NOT NULL DEFAULT 0,
                ""UploadedByUserId"" INTEGER NOT NULL DEFAULT 0,
                ""CreatedAtUtc""     TEXT    NOT NULL,
                CONSTRAINT ""FK_KnowledgeImage_Article"" FOREIGN KEY (""ArticleId"") REFERENCES ""KnowledgeArticles""(""Id"") ON DELETE CASCADE
            );");
        // Por artículo: es la única forma en que se piden —«las imágenes de este artículo», para
        // pintarlas en el editor— además de por su propio identificador, que ya es la clave.
        try { db.Database.ExecuteSqlRaw(@"CREATE INDEX IF NOT EXISTS ""IX_KnowImg_Articulo"" ON ""KnowledgeImages""(""ArticleId"",""Id"")"); } catch { }

        SembrarVentanaDeCaducidadDeVacaciones(db);

        // Al final de la rama, con todas las columnas de horas ya creadas: sin ellas no habría dónde
        // escribir la conversión.
        ConvertirPlazosDeDiasAHorasUnaVez(db);

        // La rama de SQLite no acumula fallos: aquí cada parche va en su propio try/catch porque
        // SQLite no sabe decir «añade la columna solo si no está», así que el fallo por columna
        // repetida es lo NORMAL y contarlo sería contar ruido. En SQL Server es al revés: las
        // sentencias llevan su IF, así que un fallo siempre significa algo.
        return [];
    }

    /// <summary>
    /// Deja escrita la ventana de caducidad de los días de vacaciones no gozados, con su valor por
    /// omisión de 18 meses.
    ///
    /// <para><b>Por qué se siembra la fila en vez de dejar solo la constante del programa.</b> El
    /// número es POLÍTICA DE LA EMPRESA, no una regla del código: quien decide cuánto tiempo se
    /// arrastran los días es el líder, y tiene que poder cambiarlo sin recompilar. La pantalla de
    /// Configuración lista lo que hay en <c>AppSettings</c>, así que una clave que nunca se ha
    /// capturado no aparece por ningún lado y no habría dónde tocarla. Con la fila sembrada sale en
    /// la pantalla desde el primer arranque, con su explicación al lado.</para>
    ///
    /// <para><b>De dónde sale el 18.</b> Es la suma de los seis meses que la ley da al patrón para
    /// conceder las vacaciones ya generadas más el año de prescripción que corre después. Es el
    /// valor POR OMISIÓN y una lectura razonable, <b>no una afirmación de que sea obligatorio</b>
    /// ni una asesoría legal: si el área correspondiente decide otro plazo —o ninguno—, se cambia
    /// aquí y el saldo se recalcula solo, porque no hay ningún número guardado que corregir.</para>
    ///
    /// <para>Idempotente y NO PISA lo capturado: el <c>WHERE NOT EXISTS</c> hace que el arranque
    /// siguiente respete el valor que el líder haya puesto. Sin eso, cada reinicio le devolvería el
    /// 18 y el cambio parecería «borrarse solo».</para>
    /// </summary>
    private static void SembrarVentanaDeCaducidadDeVacaciones(AppDbContext db)
    {
        // La descripción va con comillas angulares y sin apóstrofos a propósito: viaja dentro de un
        // literal de SQL, y un apóstrofo suelto ahí lo parte en dos.
        const string descripcion =
            "Meses que se arrastran los dias de vacaciones no gozados antes de caducar, contados "
            + "desde el cierre del periodo anual de cada persona. Por omision 18: seis meses para "
            + "conceder mas un ano de prescripcion. Es politica de la empresa y se puede cambiar; "
            + "el saldo se recalcula solo porque no se guarda en ninguna parte.";

        try
        {
            // [Key] entre corchetes en T-SQL: KEY es palabra reservada. La forma
            // «INSERT … SELECT … WHERE NOT EXISTS» la entienden los dos motores y resuelve en una
            // sola sentencia el «solo si falta», sin necesidad de transacción.
            if (db.Database.IsSqlite())
                db.Database.ExecuteSqlRaw($@"
                    INSERT INTO ""AppSettings"" (""Key"", ""Value"", ""IsSecret"", ""Description"")
                    SELECT 'vacaciones.caducidad-meses', '18', 0, '{descripcion}'
                    WHERE NOT EXISTS (SELECT 1 FROM ""AppSettings"" WHERE ""Key"" = 'vacaciones.caducidad-meses')");
            else
                db.Database.ExecuteSqlRaw($@"
INSERT INTO [AppSettings] ([Key], [Value], [IsSecret], [Description])
SELECT N'vacaciones.caducidad-meses', N'18', 0, N'{descripcion}'
WHERE NOT EXISTS (SELECT 1 FROM [AppSettings] WHERE [Key] = N'vacaciones.caducidad-meses');");
        }
        catch
        {
            // Sin la fila el saldo sigue saliendo bien: el servicio cae a los 18 meses por omisión.
            // Lo único que se pierde es poder cambiarlo desde la pantalla, y eso no justifica tumbar
            // el arranque de la aplicación entera.
        }
    }

    /// <summary>
    /// Convierte a HORAS —multiplicando por ocho, que es la jornada— los plazos que el pool guardaba
    /// en DÍAS. <b>Una sola vez en la vida de la base.</b>
    ///
    /// <para><b>Por qué la marca va en AppSettings y no en una condición sobre los propios datos.</b>
    /// Ése es el punto entero. Las dos condiciones que uno escribiría primero están mal, y las dos
    /// fallan del mismo modo: deshaciendo en silencio una decisión que alguien acababa de tomar.</para>
    /// <list type="bullet">
    ///   <item><c>WHERE HorasLimite = 0</c> en la matriz: 0 es un valor LEGÍTIMO y significa «sin
    ///         fecha límite». El día que el líder ponga 0 a mano en una celda cuya DiasLimite vieja
    ///         dice 5, el arranque siguiente le resucitaría 40 horas.</item>
    ///   <item><c>WHERE HorasLimite IS NULL</c> en la actividad: NULL también es legítimo y significa
    ///         «usa el de la matriz». El líder vacía el campo a propósito y el arranque siguiente le
    ///         vuelve a meter las horas del DiasLimite congelado que dejó ahí el escritorio.</item>
    /// </list>
    /// <para>La conversión no es un estado deducible de las filas: es un HECHO que ocurrió una vez, y
    /// se registra como tal. Con la marca puesta no vuelve a correr nunca, se toque después lo que se
    /// toque. Mismo patrón que usa <c>TemplateSeed</c> para no resembrar plantillas.</para>
    ///
    /// <para><b>Las columnas de DÍAS no se tocan ni se borran</b>: el escritorio sigue leyéndolas en
    /// producción hasta el corte.</para>
    ///
    /// <para>En una base NUEVA actualiza 0 filas y deja la marca, que es lo correcto: la siembra
    /// escribirá las horas directamente y nadie debe multiplicarlas después por ocho.</para>
    /// </summary>
    private static void ConvertirPlazosDeDiasAHorasUnaVez(AppDbContext db)
    {
        bool esSqlite = db.Database.IsSqlite();

        try
        {
            // La lectura de la marca, los UPDATE y la escritura de la marca van en la MISMA
            // transacción: si el proceso muere entre medias, o se hizo todo o no se hizo nada. Sin
            // eso, un reinicio en el hueco volvería a multiplicar por ocho lo ya convertido.
            using var tx = db.Database.BeginTransaction();

            // Todo por SQL crudo y nada por el ChangeTracker, a propósito: el contexto del arranque
            // es el MISMO que después siembra los catálogos, así que una entidad marcada como
            // «Added» que se quedara colgando aquí tras un fallo la cometería el SaveChanges de la
            // siembra — y la base acabaría marcada como convertida sin haberse convertido.
            int yaEsta = db.Database.SqlQueryRaw<int>(
                esSqlite
                    ? @"SELECT COUNT(*) AS ""Value"" FROM ""AppSettings"" WHERE ""Key"" = 'PoolHorasConvertidas'"
                    : "SELECT COUNT(*) AS [Value] FROM [AppSettings] WHERE [Key] = 'PoolHorasConvertidas';")
                .AsEnumerable().First();

            // Sale sin cometer nada: al salir del «using», la transacción se deshace sola.
            if (yaEsta > 0) return;

            if (esSqlite)
            {
                // printf('%.1f', …) escribe el mismo texto que escribiría la aplicación al guardar un
                // decimal, porque en SQLite EF guarda los decimales como TEXT.
                // min(…, 9999) es un tope de seguridad, no una regla de negocio: la matriz vieja no
                // acotaba los días por arriba, y un valor absurdo (1 250 días o más) no cabe en el
                // decimal(6,2) de SQL Server. Sin el tope, una sola celda disparatada abortaría la
                // conversión ENTERA y dejaría la base a medias en el otro dialecto.
                db.Database.ExecuteSqlRaw(
                    @"UPDATE ""PoolPointsMatrix"" SET ""HorasLimite"" = printf('%.1f', min(""DiasLimite"" * 8, 9999))");
                db.Database.ExecuteSqlRaw(
                    @"UPDATE ""PoolActivities"" SET ""HorasLimite"" = printf('%.1f', min(""DiasLimite"" * 8, 9999)) WHERE ""DiasLimite"" IS NOT NULL");
            }
            else
            {
                db.Database.ExecuteSqlRaw(
                    "UPDATE [PoolPointsMatrix] SET [HorasLimite] = CASE WHEN [DiasLimite] > 1249 THEN 9999.00 ELSE [DiasLimite] * 8.0 END;");
                db.Database.ExecuteSqlRaw(
                    "UPDATE [PoolActivities] SET [HorasLimite] = CASE WHEN [DiasLimite] > 1249 THEN 9999.00 ELSE [DiasLimite] * 8.0 END WHERE [DiasLimite] IS NOT NULL;");
            }

            // [Key] siempre entre corchetes: KEY es palabra reservada en T-SQL. La tabla AppSettings
            // existe seguro en las dos bases —es de la Fase 0 y la crea EnsureCreated— y su índice
            // único sobre Key es una red extra: dos marcas no caben.
            if (esSqlite)
                db.Database.ExecuteSqlRaw(@"
                    INSERT INTO ""AppSettings"" (""Key"", ""Value"", ""IsSecret"", ""Description"")
                    VALUES ('PoolHorasConvertidas', '1', 0,
                            'Los plazos del pool ya se convirtieron de días a horas (× 8). NO BORRAR esta fila: sin ella, el próximo arranque volvería a multiplicar por ocho y dejaría todos los plazos ocho veces más largos.')");
            else
                db.Database.ExecuteSqlRaw(@"
INSERT INTO [AppSettings] ([Key], [Value], [IsSecret], [Description])
VALUES (N'PoolHorasConvertidas', N'1', 0,
        N'Los plazos del pool ya se convirtieron de días a horas (× 8). NO BORRAR esta fila: sin ella, el próximo arranque volvería a multiplicar por ocho y dejaría todos los plazos ocho veces más largos.');");

            tx.Commit();
        }
        catch
        {
            // La base sigue usable con los plazos viejos y sin marca, así que el intento se repite en
            // el arranque siguiente. El arranque no debe caerse por esto: sin la web no hay a dónde
            // volver, y el escritorio —que lee los días— sigue funcionando igual.
        }
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
    /// <summary>
    /// Devuelve las sentencias que FALLARON, con su motivo. Una lista vacía es la única señal
    /// honesta de «esquema al día».
    ///
    /// <para><b>Antes esto no devolvía nada y cada fallo se tragaba en silencio</b>, y esa fue la
    /// causa del peor defecto que ha tenido este migrador: una sentencia con sintaxis de SQLite
    /// copiada a este método hacía que las 114 siguientes no se aplicaran, y el arranque terminaba
    /// anunciando que el esquema estaba al día. No falló: mintió, y durante días.</para>
    ///
    /// <para>Se sigue tragando la excepción de CADA sentencia —una sola que falle no debe impedir
    /// que se apliquen las demás, que es justo lo que se busca en un migrador idempotente— pero
    /// ahora queda constancia y quien llama decide qué hacer con ella.</para>
    /// </summary>
    private static List<string> PatchSqlServer(AppDbContext db)
    {
        var fallidas = new List<string>();

        void Exec(string sql)
        {
            try { db.Database.ExecuteSqlRaw(sql); }
            catch (Exception ex)
            {
                // La sentencia se recorta: algunas son un CREATE TABLE entero y lo que hace falta
                // para localizarla es su principio, no sus cuarenta columnas.
                var recorte = sql.Trim().Replace('\n', ' ').Replace('\r', ' ');
                if (recorte.Length > 160) recorte = recorte[..160] + "…";
                fallidas.Add($"{recorte}  →  {ex.GetBaseException().Message}");
            }
        }

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

        // Lo mismo, pero para índices donde la unicidad ES la regla y no una optimización.
        //
        // Existe por el mismo motivo que ExecIndex y comete el mismo error si se comprueba por
        // nombre: en una base RECIÉN CREADA las tablas las hace EnsureCreated a partir del modelo,
        // y EF ya deja ahí su índice único con su propio nombre (IX_Tabla_Col1_Col2). Un parche que
        // preguntara «¿existe uno llamado UX_...?» diría que no y crearía un SEGUNDO índice único
        // sobre las mismas columnas: no rompe nada, pero duplica el trabajo de cada escritura.
        //
        // Se comprueba por la PRIMERA COLUMNA, igual que ExecIndex, con el mismo compromiso: si
        // algún día alguien crea a mano otro índice que empiece por esa columna, este no se crearía
        // y la unicidad se quedaría sin declarar. Es asumible mientras el modelo de EF sea el que
        // manda, que es el caso.
        void ExecIndiceUnico(string tabla, string indice, string primeraColumna, string columnas)
            => Exec($@"
IF OBJECT_ID(N'[{tabla}]', N'U') IS NOT NULL
   AND COL_LENGTH('{tabla}','{primeraColumna}') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1 FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = OBJECT_ID(N'[{tabla}]')
          AND c.name = '{primeraColumna}' AND ic.key_ordinal = 1)
CREATE UNIQUE INDEX [{indice}] ON [{tabla}]({columnas});");

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

        // Evidencia de la actividad: tiempo declarado y enlace al item de DevOps (PR/ticket).
        Exec("IF COL_LENGTH('PointEntries','MinutesSpent') IS NULL ALTER TABLE [PointEntries] ADD [MinutesSpent] int NULL;");
        Exec("IF COL_LENGTH('PointEntries','EvidenceUrl') IS NULL ALTER TABLE [PointEntries] ADD [EvidenceUrl] nvarchar(500) NULL;");

        // Réplica del desarrollador a un rechazo.
        Exec("IF COL_LENGTH('PointEntries','ReviewRound') IS NULL ALTER TABLE [PointEntries] ADD [ReviewRound] int NOT NULL DEFAULT 0;");
        Exec("IF COL_LENGTH('PointEntries','ReviewHistory') IS NULL ALTER TABLE [PointEntries] ADD [ReviewHistory] nvarchar(max) NULL;");

        // Adjunto de respaldo en solicitudes de vacaciones.
        Exec("IF COL_LENGTH('VacationRequests','AttachmentBytes') IS NULL ALTER TABLE [VacationRequests] ADD [AttachmentBytes] varbinary(max) NULL;");
        Exec("IF COL_LENGTH('VacationRequests','AttachmentFileName') IS NULL ALTER TABLE [VacationRequests] ADD [AttachmentFileName] nvarchar(260) NULL;");

        // Permisos: flujo de solicitud/aprobación y justificante adjunto.
        // DEFAULT 1 = Aprobada, por lo mismo que en la rama SQLite: el histórico son permisos ya
        // concedidos por el administrador, no solicitudes pendientes de responder.
        Exec("IF COL_LENGTH('LeaveRequests','Status') IS NULL ALTER TABLE [LeaveRequests] ADD [Status] int NOT NULL DEFAULT 1;");
        Exec("IF COL_LENGTH('LeaveRequests','RequestedByDeveloperId') IS NULL ALTER TABLE [LeaveRequests] ADD [RequestedByDeveloperId] int NULL;");
        Exec("IF COL_LENGTH('LeaveRequests','ReviewedById') IS NULL ALTER TABLE [LeaveRequests] ADD [ReviewedById] int NULL;");
        Exec("IF COL_LENGTH('LeaveRequests','ReviewedAt') IS NULL ALTER TABLE [LeaveRequests] ADD [ReviewedAt] datetime2 NULL;");
        Exec("IF COL_LENGTH('LeaveRequests','ReviewComment') IS NULL ALTER TABLE [LeaveRequests] ADD [ReviewComment] nvarchar(max) NULL;");
        Exec("IF COL_LENGTH('LeaveRequests','AttachmentBytes') IS NULL ALTER TABLE [LeaveRequests] ADD [AttachmentBytes] varbinary(max) NULL;");
        Exec("IF COL_LENGTH('LeaveRequests','AttachmentFileName') IS NULL ALTER TABLE [LeaveRequests] ADD [AttachmentFileName] nvarchar(260) NULL;");
        ExecIndex("LeaveRequests", "IX_LeaveRequests_Status", "Status", "[Status]");

        // Permisos POR HORAS: el tramo del día, al lado de DaysCount y no en su lugar. Es la gemela
        // de la rama SQLite de aquí arriba, con el tipo que le toca a TimeOnly en este dialecto.
        //
        // NULLables y SIN RELLENO, y eso es una decisión y no un olvido: las dos propiedades del
        // modelo son anulables, así que el NULL de las filas que ya están dice la verdad —permiso de
        // día completo—. Lo que NO se puede hacer nunca es lo del sello de sesión: agregar una
        // columna que en el modelo sea NO anulable y dejarla en NULL, porque entonces EF no puede
        // materializar ni una fila y la pantalla entera deja de abrir.
        //
        // El escritorio, que sigue leyendo esta misma base hasta el corte, no se entera: son
        // columnas nuevas que su modelo no mapea, y al ser anulables tampoco le estorban al insertar.
        Exec("IF COL_LENGTH('LeaveRequests','HoraInicio') IS NULL ALTER TABLE [LeaveRequests] ADD [HoraInicio] time NULL;");
        Exec("IF COL_LENGTH('LeaveRequests','HoraFin') IS NULL ALTER TABLE [LeaveRequests] ADD [HoraFin] time NULL;");

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

        // Evidencia adjunta a las actividades libres.
        Exec(@"
IF OBJECT_ID(N'[DevActivityAttachments]', N'U') IS NULL
   AND OBJECT_ID(N'[DevActivities]', N'U') IS NOT NULL
CREATE TABLE [DevActivityAttachments] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_DevActivityAttachments] PRIMARY KEY,
    [ActivityId] int NOT NULL,
    [FileName] nvarchar(260) NOT NULL,
    [ContentType] nvarchar(100) NOT NULL DEFAULT 'application/octet-stream',
    [Bytes] varbinary(max) NOT NULL,
    [SizeBytes] bigint NOT NULL DEFAULT 0,
    [Description] nvarchar(400) NULL,
    [UploadedByUserId] int NOT NULL DEFAULT 0,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [FK_ActAdj_Act] FOREIGN KEY ([ActivityId]) REFERENCES [DevActivities]([Id]) ON DELETE CASCADE
);");
        ExecIndex("DevActivityAttachments", "IX_DevActivityAttachments_ActivityId", "ActivityId", "[ActivityId]");

        // ── Adjuntos de requerimientos ─────────────────────────────
        //
        // La rama SQLite ya la creaba y esta no: en una base de SQL Server que existía antes de que la
        // entidad se añadiera al modelo, EnsureCreated no altera nada y la tabla nunca llegó a
        // aparecer. Es aditivo y el escritorio no la conoce en SQL Server, así que crearla aquí no le
        // cambia nada a él.
        Exec(@"
IF OBJECT_ID(N'[RequirementAttachments]', N'U') IS NULL
   AND OBJECT_ID(N'[Requirements]', N'U') IS NOT NULL
CREATE TABLE [RequirementAttachments] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_RequirementAttachments] PRIMARY KEY,
    [RequirementId] int NOT NULL,
    [Kind] int NOT NULL DEFAULT 0,
    [FileName] nvarchar(260) NOT NULL DEFAULT '',
    [FileBytes] varbinary(max) NOT NULL,
    [SizeBytes] bigint NOT NULL DEFAULT 0,
    [UploadedByUserId] int NULL,
    [UploadedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [FK_ReqAtt_Req] FOREIGN KEY ([RequirementId]) REFERENCES [Requirements]([Id]) ON DELETE CASCADE
);");
        ExecIndex("RequirementAttachments", "IX_RequirementAttachments_RequirementId", "RequirementId", "[RequirementId]");

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

        // AQUÍ HABÍA UNA SENTENCIA CON SINTAXIS DE SQLITE —comillas dobles, ADD COLUMN … TEXT— metida
        // en el método de SQL Server. Se copió de la rama de SQLite sin traducirla; el parche bueno
        // de TargetFolder ya existe más abajo, con su IF COL_LENGTH.
        //
        // No era un adorno inofensivo: al ejecutarla contra SQL Server, el resto de ESTE MÉTODO
        // dejaba de aplicarse —114 sentencias— y el arranque terminaba anunciando «Esquema al día».
        // Se descubrió porque el segundo factor no se le exigía a nadie: su columna nunca se creó, y
        // ninguna de las anteriores tampoco.
        //
        // Es el peor defecto que puede tener un migrador, porque no falla: MIENTE. El día del corte
        // habría dejado la base de producción sin más de cien parches, informando de que todo fue
        // bien. Si vuelves a copiar un parche de la rama de arriba, TRADÚCELO.

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

        // Rastro de los avisos de SLA ya dados, por lo mismo que en la rama SQLite: en el escritorio
        // vivía en la memoria del proceso y aquí tiene que sobrevivir a los reinicios, o cada
        // despliegue volvería a avisar de todo. NULLABLES: el histórico queda como «nunca avisado» y
        // el escritorio, que no conoce estas columnas, sigue insertando sin tocarlas.
        Exec("IF COL_LENGTH('SlaCommitments','ReminderNotifiedAtUtc') IS NULL ALTER TABLE [SlaCommitments] ADD [ReminderNotifiedAtUtc] datetime2 NULL;");
        Exec("IF COL_LENGTH('SlaCommitments','OverdueNotifiedAtUtc') IS NULL ALTER TABLE [SlaCommitments] ADD [OverdueNotifiedAtUtc] datetime2 NULL;");

        // ── Bitácora robusta ───────────────────────────────────────
        Exec("IF COL_LENGTH('AuditLogs','OldValues') IS NULL ALTER TABLE [AuditLogs] ADD [OldValues] nvarchar(max) NULL;");
        Exec("IF COL_LENGTH('AuditLogs','NewValues') IS NULL ALTER TABLE [AuditLogs] ADD [NewValues] nvarchar(max) NULL;");
        Exec("IF COL_LENGTH('AuditLogs','Origin') IS NULL ALTER TABLE [AuditLogs] ADD [Origin] nvarchar(200) NULL;");
        Exec("IF COL_LENGTH('AuditLogs','Outcome') IS NULL ALTER TABLE [AuditLogs] ADD [Outcome] int NOT NULL DEFAULT 0;");
        Exec("IF COL_LENGTH('AuditLogs','CorrelationId') IS NULL ALTER TABLE [AuditLogs] ADD [CorrelationId] nvarchar(40) NULL;");
        Exec("IF COL_LENGTH('AppReleases','TargetFolder') IS NULL ALTER TABLE [AppReleases] ADD [TargetFolder] nvarchar(200) NULL;");
        Exec("IF COL_LENGTH('Developers','Phone') IS NULL ALTER TABLE [Developers] ADD [Phone] nvarchar(50) NULL;");
        Exec("IF COL_LENGTH('Developers','EquipmentSerial') IS NULL ALTER TABLE [Developers] ADD [EquipmentSerial] nvarchar(100) NULL;");

        // Qué hace cada persona DENTRO de su equipo: la frase que el organigrama pinta bajo su nombre
        // y que va impresa en el PDF que se reparte.
        //
        // ESTA ES LA VERSIÓN DE T-SQL. La gemela de SQLite dice ADD COLUMN "TeamFunction" TEXT y vive
        // al lado de "TeamRole" en la otra rama: es la MISMA columna TRADUCIDA, no la misma sentencia.
        // Pegar aquella aquí abortaría esta y todas las que vienen detrás mientras el arranque anuncia
        // «Esquema al día», que es exactamente cómo se perdieron 114 parches.
        //
        // nvarchar(200) porque es lo que declara el modelo (PersonasQueryService.LargoMaximoDeFuncion):
        // con otra longitud, la columna saldría de un tipo en las bases nuevas —que las crea
        // EnsureCreated desde el modelo— y de otro en las que ya existían. NULL y sin DEFAULT porque
        // «todavía nadie la ha escrito» es un dato distinto de «escrita y vacía».
        Exec("IF COL_LENGTH('Developers','TeamFunction') IS NULL ALTER TABLE [Developers] ADD [TeamFunction] nvarchar(200) NULL;");

        // De qué equipo cuelga cada equipo: la columna que convierte la lista de equipos en un árbol
        // y permite los subequipos.
        //
        // ESTA ES LA VERSIÓN DE T-SQL. La gemela de SQLite dice
        // ADD COLUMN "EquipoPadreId" INTEGER REFERENCES "Teams"("Id") y vive al lado de
        // "TeamFunction" en la otra rama, con la restricción metida en la misma sentencia porque allí
        // no se puede añadir después. Aquí van en dos: es la MISMA columna traducida, no la misma
        // sentencia. Pegar aquella aquí abortaría esta y todas las que vienen detrás mientras el
        // arranque anuncia «Esquema al día», que es exactamente cómo se perdieron 114 parches.
        //
        // NULL Y NADA QUE RELLENAR, escrito aquí para que nadie lo dude al leerlo: una columna nueva
        // que se queda en NULL sobre una propiedad NO anulable del modelo hace que EF no pueda
        // materializar la fila y tumba cualquier consulta de la tabla —así se dejó fuera a todo el
        // mundo con el sello de sesión—. La propiedad es «int?» y NULL significa «equipo raíz», que
        // es lo que son todos los equipos que ya existen. Darles un padre sería inventárselo.
        Exec("IF COL_LENGTH('Teams','EquipoPadreId') IS NULL ALTER TABLE [Teams] ADD [EquipoPadreId] int NULL;");

        // La clave foránea se comprueba por COLUMNA y no por nombre, igual que los índices: en una
        // base recién creada la tabla la hace EnsureCreated desde el modelo y EF ya deja ahí su
        // propia restricción con su nombre (FK_Teams_Teams_EquipoPadreId). Preguntando por
        // «FK_Team_Padre» se crearía una SEGUNDA clave foránea sobre la misma columna.
        //
        // Sin ON DELETE: SQL Server rechaza cascada o SET NULL en una clave que apunta a su propia
        // tabla. Los subequipos de un equipo que se borra los recoloca el servicio.
        Exec(@"
IF COL_LENGTH('Teams','EquipoPadreId') IS NOT NULL
   AND NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys fk
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
        WHERE fk.parent_object_id = OBJECT_ID(N'[Teams]') AND c.name = 'EquipoPadreId')
ALTER TABLE [Teams] ADD CONSTRAINT [FK_Team_Padre]
    FOREIGN KEY ([EquipoPadreId]) REFERENCES [Teams]([Id]);");

        // ── DOS GEMELAS QUE FALTABAN ─────────────────────────────────────────────
        //
        // Las dos existían solo en la rama de SQLite. La base de producción SÍ las tiene —las escribió
        // el escritorio antes de la mudanza, y por eso el organigrama cuenta sistemas y proyectos—, así
        // que esto no arregla nada que esté roto hoy: cierra el agujero para cualquier base nueva o
        // restaurada, donde la mitad del organigrama fallaría al leer una columna que no está.
        //
        // Y ahora pesa más que antes: «TeamId» dejó de ser un dato que solo se leía. Desde que la
        // pantalla de despliegues puede decir qué equipo se encarga de cada sistema, esta columna se
        // ESCRIBE, y sin ella el guardado fallaría con la excepción que Exec se traga en silencio.
        Exec("IF COL_LENGTH('AppSystems','TeamId') IS NULL ALTER TABLE [AppSystems] ADD [TeamId] int NULL;");

        // La tabla de proyectos, traducida de la rama SQLite. ON DELETE SET NULL igual que allá: al
        // borrar un equipo sus proyectos se quedan sin dueño, no se van con él.
        Exec(@"
IF OBJECT_ID(N'[Projects]', N'U') IS NULL
CREATE TABLE [Projects] (
    [Id]          int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Projects] PRIMARY KEY,
    [Name]        nvarchar(200) NOT NULL,
    [Client]      nvarchar(200) NULL,
    [Description] nvarchar(max) NULL,
    [Status]      int NOT NULL DEFAULT 0,
    [TeamId]      int NULL,
    [CreatedAt]   datetime2 NOT NULL,
    CONSTRAINT [FK_Project_Team] FOREIGN KEY ([TeamId]) REFERENCES [Teams]([Id]) ON DELETE SET NULL
);");

        // Ajuste manual del saldo de vacaciones. Mismas cuatro columnas y mismos criterios que en la
        // rama SQLite: el saldo se calcula y no se guarda, así que esto es lo único que un humano
        // escribe. DEFAULT 0 y el resto NULL para que el histórico quede como «nunca ajustado».
        // Las longitudes son las mismas que declara AppDbContext, o la columna saldría de un tipo en
        // las bases nuevas (las crea EnsureCreated con el modelo) y de otro en las que ya existían.
        Exec("IF COL_LENGTH('Developers','VacationAdjustmentDays') IS NULL ALTER TABLE [Developers] ADD [VacationAdjustmentDays] int NOT NULL DEFAULT 0;");
        Exec("IF COL_LENGTH('Developers','VacationAdjustmentNote') IS NULL ALTER TABLE [Developers] ADD [VacationAdjustmentNote] nvarchar(500) NULL;");
        Exec("IF COL_LENGTH('Developers','VacationAdjustmentBy') IS NULL ALTER TABLE [Developers] ADD [VacationAdjustmentBy] nvarchar(150) NULL;");
        Exec("IF COL_LENGTH('Developers','VacationAdjustmentAtUtc') IS NULL ALTER TABLE [Developers] ADD [VacationAdjustmentAtUtc] datetime2 NULL;");
        Exec("IF COL_LENGTH('Requirements','DevOpsReportedSeconds') IS NULL ALTER TABLE [Requirements] ADD [DevOpsReportedSeconds] int NOT NULL DEFAULT 0;");
        Exec("IF COL_LENGTH('DevOpsTickets','AssignedToUniqueName') IS NULL ALTER TABLE [DevOpsTickets] ADD [AssignedToUniqueName] nvarchar(256) NULL;");

        // Prioridad definida por el líder y estimación del desarrollador.
        Exec("IF COL_LENGTH('DevOpsTickets','PriorityConfirmedAt') IS NULL ALTER TABLE [DevOpsTickets] ADD [PriorityConfirmedAt] datetime2 NULL;");
        Exec("IF COL_LENGTH('DevOpsTickets','PriorityConfirmedByUserId') IS NULL ALTER TABLE [DevOpsTickets] ADD [PriorityConfirmedByUserId] int NULL;");
        Exec("IF COL_LENGTH('DevOpsTickets','EstimatedHours') IS NULL ALTER TABLE [DevOpsTickets] ADD [EstimatedHours] float NULL;");
        Exec("IF COL_LENGTH('DevOpsTickets','EstimatedAt') IS NULL ALTER TABLE [DevOpsTickets] ADD [EstimatedAt] datetime2 NULL;");
        Exec("IF COL_LENGTH('DevOpsTickets','EstimatedByDeveloperId') IS NULL ALTER TABLE [DevOpsTickets] ADD [EstimatedByDeveloperId] int NULL;");
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
        // ANTES del índice hay que acotar la columna, y esto arregla un defecto que estuvo callado
        // mucho tiempo: cuando la tabla la crea EnsureCreated —que es lo que pasa siempre, porque el
        // migrador arranca por ahí— EF respeta el modelo, y hasta ahora el modelo no declaraba
        // longitud para DedupeKey, así que salía nvarchar(max). SQL Server NO admite una columna de
        // ese tipo como clave de un índice, de modo que el CREATE de abajo fallaba en cada arranque
        // y se lo tragaba el try vacío de Exec. Resultado: la deduplicación de avisos no tenía
        // ninguna garantía en la base, solo el «comprueba y luego inserta» del servicio.
        //
        // Se convierte solo si hoy es (max) y ningún valor guardado pasa de 200, para no truncar
        // datos por sorpresa. Si algo no cuadra, la conversión no ocurre y el índice sigue sin
        // crearse: exactamente como estaba, sin empeorar nada.
        Exec(@"
IF COL_LENGTH('Notifications','DedupeKey') = -1
   AND NOT EXISTS (SELECT 1 FROM [Notifications] WHERE LEN([DedupeKey]) > 200)
ALTER TABLE [Notifications] ALTER COLUMN [DedupeKey] nvarchar(200) NULL;");

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

        // ── Asistencia oficial (entrada y salida marcadas a mano) ──
        Exec(@"
IF OBJECT_ID(N'[AttendanceRecords]', N'U') IS NULL
CREATE TABLE [AttendanceRecords] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AttendanceRecords] PRIMARY KEY,
    [UserId] int NOT NULL,
    [DeveloperId] int NULL,
    [DisplayName] nvarchar(200) NOT NULL,
    [CheckInUtc] datetime2 NOT NULL,
    [CheckOutUtc] datetime2 NULL,
    [CheckInOrigin] nvarchar(200) NULL,
    [CheckOutOrigin] nvarchar(200) NULL,
    [CheckInNote] nvarchar(300) NULL,
    [CheckOutNote] nvarchar(300) NULL,
    [CloseKind] int NULL,
    [CorrectionRequestNote] nvarchar(500) NULL,
    [CorrectionRequestedAtUtc] datetime2 NULL,
    [CorrectedByUserId] int NULL,
    [CorrectedByName] nvarchar(200) NULL,
    [CorrectedAtUtc] datetime2 NULL,
    [CorrectionReason] nvarchar(500) NULL
);");
        ExecIndex("AttendanceRecords", "IX_Att_User_CheckIn", "UserId", "[UserId],[CheckInUtc]");
        ExecIndex("AttendanceRecords", "IX_Att_Open", "CheckOutUtc", "[CheckOutUtc]");

        // ── Pool de actividades valoradas ──────────────────────────
        // FK a Developers sin cascada (NO ACTION), igual que FK_WS_Dev: una actividad ya aceptada
        // justifica unos puntos y no debe desaparecer porque se borre la ficha de quien la hizo.
        // PointEntryId y LinkedDevActivityId van sin FK a propósito (ver el modelo): con ellas
        // habría dos rutas de cascada desde Developers y SQL Server rechaza crearlas.
        Exec(@"
IF OBJECT_ID(N'[PoolActivities]', N'U') IS NULL
CREATE TABLE [PoolActivities] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PoolActivities] PRIMARY KEY,
    [Title] nvarchar(200) NOT NULL,
    [Description] nvarchar(max) NULL,
    [WorkType] int NOT NULL DEFAULT 0,
    [Complexity] int NOT NULL DEFAULT 0,
    [Points] int NOT NULL DEFAULT 0,
    [Status] int NOT NULL DEFAULT 0,
    [ExternalUrl] nvarchar(500) NULL,
    [CreatedByUserId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ClaimedByDeveloperId] int NULL,
    [ClaimedAt] datetime2 NULL,
    [ClaimDeadlineAt] datetime2 NULL,
    [HorasLimite] decimal(6,2) NULL,
    [HorasEstimadas] decimal(6,2) NULL,
    [HorasEstimadasEnUtc] datetime2 NULL,
    [ReturnedCount] int NOT NULL DEFAULT 0,
    [DeliveredAt] datetime2 NULL,
    [ReviewedByUserId] int NULL,
    [ReviewedAt] datetime2 NULL,
    [ReviewComment] nvarchar(1000) NULL,
    [ReviewRound] int NOT NULL DEFAULT 0,
    [ReviewHistory] nvarchar(max) NULL,
    [PointEntryId] int NULL,
    [LinkedDevActivityId] int NULL,
    CONSTRAINT [FK_Pool_Dev] FOREIGN KEY ([ClaimedByDeveloperId]) REFERENCES [Developers]([Id])
);");
        ExecIndex("PoolActivities", "IX_Pool_Status", "Status", "[Status]");
        ExecIndex("PoolActivities", "IX_Pool_Claimed_Status", "ClaimedByDeveloperId", "[ClaimedByDeveloperId],[Status]");
        // Prioridad por omisión Media (1): lo ya publicado no tenía urgencia declarada, y suponerla
        // crítica o irrelevante sería inventar información sobre trabajo que ya está en el pool.
        Exec("IF COL_LENGTH('PoolActivities','Priority') IS NULL ALTER TABLE [PoolActivities] ADD [Priority] int NOT NULL DEFAULT 1;");
        // Nula = usa los días de la matriz, que es exactamente lo que se hacía antes de existir.
        // OBSOLETA para la web desde el paso a horas; se conserva porque el ESCRITORIO la lee en
        // producción hasta el corte. Se puede tirar DESPUÉS del corte, junto con la de PoolPointsMatrix.
        Exec("IF COL_LENGTH('PoolActivities','DiasLimite') IS NULL ALTER TABLE [PoolActivities] ADD [DiasLimite] int NULL;");
        // Plazo y esfuerzo en HORAS. Se AÑADEN al lado de DiasLimite, nunca en su lugar: renombrarla
        // rompería el escritorio en producción el mismo día. decimal(6,2) es el mismo texto que
        // declara AppDbContext, para que una base creada por EnsureCreated y una parcheada aquí
        // tengan exactamente la misma columna.
        Exec("IF COL_LENGTH('PoolActivities','HorasLimite') IS NULL ALTER TABLE [PoolActivities] ADD [HorasLimite] decimal(6,2) NULL;");
        Exec("IF COL_LENGTH('PoolActivities','HorasEstimadas') IS NULL ALTER TABLE [PoolActivities] ADD [HorasEstimadas] decimal(6,2) NULL;");
        Exec("IF COL_LENGTH('PoolActivities','HorasEstimadasEnUtc') IS NULL ALTER TABLE [PoolActivities] ADD [HorasEstimadasEnUtc] datetime2 NULL;");
        // Vínculo con Azure DevOps y marca de agua del empuje. La gemela de SQLite está en la otra
        // rama y NO es la misma sentencia: allá va ADD COLUMN sin corchetes, el decimal es TEXT y el
        // instante es TEXT. Traducida, no copiada.
        //
        // Todas NULAS: lo publicado antes de existir el vínculo no está ligado a ningún ticket, y un
        // DEFAULT aquí inventaría un work item para
        // cada actividad del pool que ya existe.
        // decimal(6,2) es el MISMO texto que declara AppDbContext para esta columna, y tiene que
        // serlo: se compara por igualdad contra HorasEstimadas, que también es decimal(6,2).
        Exec("IF COL_LENGTH('PoolActivities','DevOpsWorkItemId') IS NULL ALTER TABLE [PoolActivities] ADD [DevOpsWorkItemId] int NULL;");
        Exec("IF COL_LENGTH('PoolActivities','DevOpsEsfuerzoEnviado') IS NULL ALTER TABLE [PoolActivities] ADD [DevOpsEsfuerzoEnviado] decimal(6,2) NULL;");
        Exec("IF COL_LENGTH('PoolActivities','DevOpsPrioridadEnviada') IS NULL ALTER TABLE [PoolActivities] ADD [DevOpsPrioridadEnviada] int NULL;");
        Exec("IF COL_LENGTH('PoolActivities','DevOpsEmpujadoEnUtc') IS NULL ALTER TABLE [PoolActivities] ADD [DevOpsEmpujadoEnUtc] datetime2 NULL;");
        Exec("IF COL_LENGTH('PoolActivities','DevOpsUltimoError') IS NULL ALTER TABLE [PoolActivities] ADD [DevOpsUltimoError] nvarchar(1000) NULL;");
        ExecIndex("PoolActivities", "IX_Pool_DevOps", "DevOpsWorkItemId", "[DevOpsWorkItemId]");

        // La gemela de la de SQLite: a qué subequipo se publica, nulo para toda la casa.
        Exec("IF COL_LENGTH('PoolActivities','EquipoId') IS NULL ALTER TABLE [PoolActivities] ADD [EquipoId] int NULL;");
        ExecIndex("PoolActivities", "IX_Pool_Equipo", "EquipoId", "[Status],[EquipoId]");

        Exec(@"
IF OBJECT_ID(N'[PoolPointsMatrix]', N'U') IS NULL
CREATE TABLE [PoolPointsMatrix] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PoolPointsMatrix] PRIMARY KEY,
    [WorkType] int NOT NULL,
    [Complexity] int NOT NULL,
    [Points] int NOT NULL DEFAULT 0,
    [DiasLimite] int NOT NULL DEFAULT 0,
    [HorasLimite] decimal(6,2) NOT NULL DEFAULT 0,
    [UpdatedAt] datetime2 NOT NULL,
    [UpdatedByUserId] int NULL
);");
        // El plazo de la matriz en HORAS, al lado de los días y sin sustituirlos: el escritorio los
        // sigue leyendo hasta el corte. NOT NULL con default 0, que ya significa «sin fecha límite».
        Exec("IF COL_LENGTH('PoolPointsMatrix','HorasLimite') IS NULL ALTER TABLE [PoolPointsMatrix] ADD [HorasLimite] decimal(6,2) NOT NULL DEFAULT 0;");
        // El índice ÚNICO va con Exec y no con ExecIndex: aquel crea índices normales, y aquí la
        // unicidad es la regla (dos celdas del mismo par harían que el valor de una actividad
        // dependiera de cuál se leyera primero).
        Exec(@"
IF OBJECT_ID(N'[PoolPointsMatrix]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PoolMatrix' AND object_id = OBJECT_ID(N'[PoolPointsMatrix]'))
CREATE UNIQUE INDEX [UX_PoolMatrix] ON [PoolPointsMatrix]([WorkType],[Complexity]);");

        Exec(@"
IF OBJECT_ID(N'[PoolChecklistTemplateItems]', N'U') IS NULL
CREATE TABLE [PoolChecklistTemplateItems] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PoolChecklistTemplateItems] PRIMARY KEY,
    [WorkType] int NOT NULL,
    [Text] nvarchar(300) NOT NULL,
    [Orden] int NOT NULL DEFAULT 0,
    [RequiereEvidencia] bit NOT NULL DEFAULT 0,
    [IsActive] bit NOT NULL DEFAULT 1,
    [CreatedAt] datetime2 NOT NULL
);");
        ExecIndex("PoolChecklistTemplateItems", "IX_PoolTpl_Tipo_Activo", "WorkType", "[WorkType],[IsActive]");

        Exec(@"
IF OBJECT_ID(N'[PoolActivityChecklistItems]', N'U') IS NULL
CREATE TABLE [PoolActivityChecklistItems] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PoolActivityChecklistItems] PRIMARY KEY,
    [PoolActivityId] int NOT NULL,
    [Text] nvarchar(300) NOT NULL,
    [Orden] int NOT NULL DEFAULT 0,
    [RequiereEvidencia] bit NOT NULL DEFAULT 0,
    [IsDone] bit NOT NULL DEFAULT 0,
    [DoneAtUtc] datetime2 NULL,
    [EvidenceUrl] nvarchar(500) NULL,
    CONSTRAINT [FK_PoolChk_Pool] FOREIGN KEY ([PoolActivityId]) REFERENCES [PoolActivities]([Id]) ON DELETE CASCADE
);");
        ExecIndex("PoolActivityChecklistItems", "IX_PoolChk_Actividad", "PoolActivityId", "[PoolActivityId]");

        Exec(@"
IF OBJECT_ID(N'[DocumentTemplates]', N'U') IS NULL
CREATE TABLE [DocumentTemplates] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_DocumentTemplates] PRIMARY KEY,
    [Clave] nvarchar(80) NOT NULL,
    [Contenido] varbinary(max) NOT NULL,
    [NombreDeArchivo] nvarchar(260) NULL,
    [SubidaPor] nvarchar(200) NULL,
    [SubidaEnUtc] datetime2 NOT NULL
);");
        Exec(@"
IF OBJECT_ID(N'[DocumentTemplates]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_DocTpl_Clave'
                   AND object_id = OBJECT_ID(N'[DocumentTemplates]'))
CREATE UNIQUE INDEX [UX_DocTpl_Clave] ON [DocumentTemplates]([Clave]);");

        Exec(@"
IF OBJECT_ID(N'[PoolActivityExtraCriteria]', N'U') IS NULL
CREATE TABLE [PoolActivityExtraCriteria] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PoolActivityExtraCriteria] PRIMARY KEY,
    [PoolActivityId] int NOT NULL,
    [ScoringCriterionId] int NULL,
    [Name] nvarchar(200) NOT NULL,
    [Points] int NOT NULL DEFAULT 0,
    [IsMet] bit NULL,
    [EvaluatedAtUtc] datetime2 NULL,
    [Comment] nvarchar(500) NULL,
    CONSTRAINT [FK_PoolExtra_Pool] FOREIGN KEY ([PoolActivityId]) REFERENCES [PoolActivities]([Id]) ON DELETE CASCADE
);");
        // Único de verdad y no solo un índice: el mismo criterio dos veces en una actividad sumaría
        // dos veces sin que nadie lo hubiera decidido.
        Exec(@"
IF OBJECT_ID(N'[PoolActivityExtraCriteria]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PoolExtra'
                   AND object_id = OBJECT_ID(N'[PoolActivityExtraCriteria]'))
CREATE UNIQUE INDEX [UX_PoolExtra] ON [PoolActivityExtraCriteria]([PoolActivityId],[ScoringCriterionId])
WHERE [ScoringCriterionId] IS NOT NULL;");

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

        // ══════════════════════════════════════════════════════════════════════════════════
        // Parches NUEVOS de la web (no existen en el escritorio). Van al final para no alterar
        // el orden de los ya probados contra la base real.
        // ══════════════════════════════════════════════════════════════════════════════════

        // Sello de sesión. En el escritorio no hacía falta: la sesión moría con el proceso, así que
        // cambiar la contraseña o dar de baja a alguien surtía efecto al siguiente arranque. En la
        // web la cookie de autenticación puede seguir viva horas después, y sin este sello la
        // cuenta desactivada sigue entrando hasta que expire. Al cambiar la contraseña o desactivar
        // la cuenta se regenera el sello; los tickets emitidos con el sello anterior dejan de valer.
        // La columna se agrega anulable —no hay forma de agregarla NOT NULL sin un valor— pero
        // ENSEGUIDA se rellena, y ese relleno no es cosmético: `User.SecurityStamp` es `string` no
        // anulable, así que una fila con NULL no es «una cuenta sin sello», es una fila que EF NO
        // PUEDE MATERIALIZAR. Revienta con SqlNullValueException dentro de la consulta de
        // LoginAsync, o sea que NADIE PUEDE ENTRAR. Ninguna prueba lo veía porque en las bases de
        // prueba los usuarios se crean por el modelo, que ya trae su sello puesto; solo aparece
        // contra una base que ya tenía cuentas, que es exactamente la de producción.
        //
        // Antes había aquí un comentario diciendo que el NULL era deliberado, «porque un sello
        // vacío no debe invalidar nada por sí mismo». El razonamiento sobre el significado del
        // sello era correcto y se respeta: dar a cada cuenta un sello propio y recién hecho no
        // invalida ninguna sesión, porque las sesiones se comparan contra el sello que se emitió
        // con ellas y aquí todavía no hay ninguna emitida.
        Exec("IF COL_LENGTH('Users','SecurityStamp') IS NULL ALTER TABLE [Users] ADD [SecurityStamp] nvarchar(64) NULL;");
        Exec("UPDATE [Users] SET [SecurityStamp] = REPLACE(CONVERT(varchar(36), NEWID()), '-', '') WHERE [SecurityStamp] IS NULL;");

        // Concurrencia optimista donde dos personas editan de verdad lo mismo al mismo tiempo.
        // En el escritorio esto casi no se daba: un contexto Singleton por proceso y una ventana a
        // la vez. En la web, la misma actividad del pool la pueden estar reclamando dos
        // desarrolladores y el mismo permiso lo pueden estar resolviendo dos jefes desde pestañas
        // distintas; sin rowversion gana el último en guardar y el otro cambio se pierde sin aviso.
        // [rowversion] es la columna que SQL Server mantiene solo: se puede agregar NOT NULL sin
        // DEFAULT porque el motor la rellena en cada fila existente y en cada UPDATE posterior.
        // Solo en SQL Server: SQLite no tiene rowversion, y el escritorio siempre corrió su
        // concurrencia contra SQL Server.
        foreach (var tabla in new[]
                 {
                     "PoolActivities",   // reclamar / entregar / revisar una actividad del pool
                     "LeaveRequests",    // aprobar o rechazar un permiso
                     "Sprints",          // mover fechas del sprint
                     "Templates",        // editar una plantilla compartida
                     "PointEntries",     // revisar una autocalificación
                     "DevActivities",    // cerrar una actividad libre
                 })
            Exec($@"
IF OBJECT_ID(N'[{tabla}]', N'U') IS NOT NULL AND COL_LENGTH('{tabla}','RowVersion') IS NULL
ALTER TABLE [{tabla}] ADD [RowVersion] rowversion NOT NULL;");

        // PAT personal de Azure DevOps cifrado del lado del servidor.
        // En el escritorio el PAT vivía en un archivo por máquina protegido con DPAPI, que ata el
        // secreto al usuario de Windows y a ese equipo. En la web no hay DPAPI ni «esa máquina»:
        // el secreto tiene que viajar con la cuenta, así que se guarda cifrado en la base y solo
        // el servidor lo descifra (el navegador nunca ve el texto claro).
        // El índice único (UserId, Proposito) es lo que hace que «el PAT de fulano para DevOps»
        // sea uno y no una colección que crece con cada guardado.
        Exec(@"
IF OBJECT_ID(N'[UserSecrets]', N'U') IS NULL
CREATE TABLE [UserSecrets] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_UserSecrets] PRIMARY KEY,
    [UserId] int NOT NULL,
    [Proposito] nvarchar(100) NOT NULL,
    [CipherText] nvarchar(max) NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    CONSTRAINT [FK_UserSecret_User] FOREIGN KEY ([UserId]) REFERENCES [Users]([Id]) ON DELETE CASCADE
);");
        // Único y con Exec, no con ExecIndex: aquel crea índices normales y aquí la unicidad es la
        // regla — dos filas del mismo par harían que el PAT usado dependiera de cuál se leyera.
        Exec(@"IF OBJECT_ID(N'[UserSecrets]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_UserSecrets_User_Proposito' AND object_id=OBJECT_ID(N'[UserSecrets]'))
CREATE UNIQUE INDEX [UX_UserSecrets_User_Proposito] ON [UserSecrets]([UserId],[Proposito]);");

        // Preferencias de interfaz por usuario (sustituye al columnas.json por máquina).
        // El escritorio guardaba el ancho y el orden de las columnas en un archivo local, así que
        // quien cambiaba de equipo perdía su configuración y volvía a acomodar las rejillas. Aquí
        // la preferencia cuelga del usuario y lo sigue a cualquier navegador.
        // El valor va como JSON en una sola columna a propósito: cada pantalla guarda su forma sin
        // que agregar una preferencia nueva obligue a migrar la tabla.
        Exec(@"
IF OBJECT_ID(N'[UserPreferences]', N'U') IS NULL
CREATE TABLE [UserPreferences] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_UserPreferences] PRIMARY KEY,
    [UserId] int NOT NULL,
    [Clave] nvarchar(100) NOT NULL,
    [Json] nvarchar(max) NOT NULL,
    CONSTRAINT [FK_UserPref_User] FOREIGN KEY ([UserId]) REFERENCES [Users]([Id]) ON DELETE CASCADE
);");
        Exec(@"IF OBJECT_ID(N'[UserPreferences]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_UserPreferences_User_Clave' AND object_id=OBJECT_ID(N'[UserPreferences]'))
CREATE UNIQUE INDEX [UX_UserPreferences_User_Clave] ON [UserPreferences]([UserId],[Clave]);");

        // Suscripciones a avisos push (sustituyen a los globos de la bandeja del sistema).
        // En el escritorio la aplicación seguía viva escondida en la bandeja y podía avisar cuando
        // quisiera; una pestaña cerrada no puede. Aquí se guarda a qué navegador entregar el aviso.
        // Una fila por NAVEGADOR y no por persona: quien use el portátil y el teléfono tiene dos.
        Exec(@"
IF OBJECT_ID(N'[PushSubscriptions]', N'U') IS NULL
CREATE TABLE [PushSubscriptions] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_PushSubscriptions] PRIMARY KEY,
    [UserId] int NOT NULL,
    [Endpoint] nvarchar(600) NOT NULL,
    [P256dh] nvarchar(200) NOT NULL,
    [Auth] nvarchar(100) NOT NULL,
    [Descripcion] nvarchar(120) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [LastOkUtc] datetime2 NULL,
    CONSTRAINT [FK_PushSub_User] FOREIGN KEY ([UserId]) REFERENCES [Users]([Id]) ON DELETE CASCADE
);");
        // Única y con Exec: dos filas con la misma dirección de entrega harían que el mismo
        // navegador recibiera el aviso por duplicado.
        Exec(@"IF OBJECT_ID(N'[PushSubscriptions]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_PushSubscriptions_Endpoint' AND object_id=OBJECT_ID(N'[PushSubscriptions]'))
CREATE UNIQUE INDEX [UX_PushSubscriptions_Endpoint] ON [PushSubscriptions]([Endpoint]);");
        ExecIndex("PushSubscriptions", "IX_PushSubscriptions_User", "UserId", "[UserId]");

        // Último latido del cronómetro. En el escritorio, cerrar la ventana era un acto deliberado
        // y la sesión se cerraba con él. En la web cerrar la pestaña —o que se duerma el equipo— es
        // lo normal y no avisa: sin este dato la sesión abierta se quedaría corriendo para siempre
        // o habría que descartar el tramo entero. Con el latido, el tiempo se consolida hasta el
        // último momento en que sabemos que la persona seguía ahí.
        Exec("IF COL_LENGTH('WorkSessions','LastHeartbeatUtc') IS NULL ALTER TABLE [WorkSessions] ADD [LastHeartbeatUtc] datetime2 NULL;");

        // ── Segundo factor: código de la aplicación del teléfono ──────────────────────────────
        //
        // Tres columnas de ESTADO en Users y dos tablas. El SECRETO no aparece por ningún lado de
        // este bloque, y no es un descuido: vive cifrado en UserSecrets, con la protección de datos
        // del servidor y bajo su propio propósito. Una columna en claro aquí sería una llave de
        // acceso legible para cualquiera que abriera una consulta.
        //
        // «Activo» se agrega NOT NULL con DEFAULT 0, así que todas las cuentas que ya existen
        // quedan SIN segundo factor. Es exactamente lo que se quiere: al desplegar, nadie lo tiene
        // y a nadie se le deja hacer nada más que activarlo — obligatorio sin excepciones.
        Exec("IF COL_LENGTH('Users','SegundoFactorActivo') IS NULL ALTER TABLE [Users] ADD [SegundoFactorActivo] bit NOT NULL CONSTRAINT [DF_Users_SegundoFactorActivo] DEFAULT 0;");
        Exec("IF COL_LENGTH('Users','SegundoFactorDesdeUtc') IS NULL ALTER TABLE [Users] ADD [SegundoFactorDesdeUtc] datetime2 NULL;");
        // bigint y no int: son segundos desde 1970 divididos entre 30. Hoy caben en 32 bits de
        // sobra, pero el tipo de una cuenta de tiempo no debería llevar fecha de caducidad escrita.
        // Es la ANTIRREPETICIÓN: sin esta columna, un código visto por encima del hombro sirve
        // durante minuto y medio.
        Exec("IF COL_LENGTH('Users','SegundoFactorUltimaVentana') IS NULL ALTER TABLE [Users] ADD [SegundoFactorUltimaVentana] bigint NULL;");

        // Los ocho códigos de rescate, uno por fila y HASHEADOS.
        // Una fila por código y no los ocho juntos en una columna: cada uno se gasta por separado y
        // hay que poder contar cuántos quedan sin leer, parsear y reescribir el conjunto entero —que
        // es la forma de que dos intentos a la vez se pisen y un código gastado «reviva».
        Exec(@"
IF OBJECT_ID(N'[UserRecoveryCodes]', N'U') IS NULL
CREATE TABLE [UserRecoveryCodes] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_UserRecoveryCodes] PRIMARY KEY,
    [UserId] int NOT NULL,
    [CodigoHash] nvarchar(64) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UsadoEnUtc] datetime2 NULL,
    CONSTRAINT [FK_UserRecoveryCode_User] FOREIGN KEY ([UserId]) REFERENCES [Users]([Id]) ON DELETE CASCADE
);");
        // Con el usuario de primera columna, el mismo índice sirve para buscar al entrar y para
        // impedir que un código se dé de alta dos veces en la misma cuenta; por eso no se crea otro
        // índice solo por UserId.
        ExecIndiceUnico("UserRecoveryCodes", "UX_UserRecoveryCodes_User_Hash", "UserId", "[UserId],[CodigoHash]");

        // Los navegadores en los que ya no se vuelve a pedir el código durante treinta días.
        // Se guarda en la base y no solo en una cookie firmada porque hay que poder RETIRAR la
        // confianza: cuando el líder reinicia el segundo factor de alguien que perdió el teléfono,
        // los equipos recordados tienen que dejar de valer en ese mismo momento. Una cookie
        // autosuficiente no se puede alcanzar desde el servidor; una fila se borra.
        Exec(@"
IF OBJECT_ID(N'[UserTrustedDevices]', N'U') IS NULL
CREATE TABLE [UserTrustedDevices] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_UserTrustedDevices] PRIMARY KEY,
    [UserId] int NOT NULL,
    [TokenHash] nvarchar(64) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [ExpiraEnUtc] datetime2 NOT NULL,
    [UltimoUsoUtc] datetime2 NULL,
    [Descripcion] nvarchar(200) NULL,
    CONSTRAINT [FK_UserTrustedDevice_User] FOREIGN KEY ([UserId]) REFERENCES [Users]([Id]) ON DELETE CASCADE
);");
        // Único por el testigo solo: son 256 bits aleatorios, identifican al navegador sin ayuda.
        ExecIndiceUnico("UserTrustedDevices", "UX_UserTrustedDevices_Token", "TokenHash", "[TokenHash]");
        // Y uno normal por usuario, que es como se listan y como se borran todos de golpe al
        // reiniciarle el segundo factor a alguien.
        ExecIndex("UserTrustedDevices", "IX_UserTrustedDevices_User", "UserId", "[UserId]");

        // ── Base de conocimiento (SQL Server) ────────────────────────────────────────────────
        //
        // ESTA ES LA VERSIÓN DE T-SQL: corchetes, IF OBJECT_ID … IS NULL, nvarchar/datetime2/rowversion.
        // La gemela de SQLite está en la otra rama y NO es la misma sentencia: está TRADUCIDA. Pegar
        // una en la otra rompe esta sentencia y, con ella, todas las que vengan detrás — que es
        // exactamente cómo se perdieron 114 parches mientras el arranque anunciaba «Esquema al día».
        //
        // Una sola tabla para todo: un término del glosario es un artículo corto etiquetado y una
        // guía de despliegue es uno largo. Sin FK al autor (la documentación es histórica y
        // sobrevive a que se borre la cuenta) y sin FK a PointEntries, que ya cae en cascada desde
        // Developers: una segunda ruta hasta la misma tabla es de las que SQL Server rechaza al
        // crear las restricciones. Es la misma decisión que en PoolActivities.
        Exec(@"
IF OBJECT_ID(N'[KnowledgeArticles]', N'U') IS NULL
CREATE TABLE [KnowledgeArticles] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_KnowledgeArticles] PRIMARY KEY,
    [Title] nvarchar(200) NOT NULL,
    [Body] nvarchar(max) NOT NULL,
    [Tags] nvarchar(300) NULL,
    [Status] int NOT NULL DEFAULT 0,
    [AuthorUserId] int NOT NULL,
    [AuthorName] nvarchar(200) NOT NULL,
    [AuthorDeveloperId] int NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [UpdatedAtUtc] datetime2 NULL,
    [SubmittedAtUtc] datetime2 NULL,
    [ReviewedByUserId] int NULL,
    [ReviewerName] nvarchar(200) NULL,
    [ReviewedAtUtc] datetime2 NULL,
    [ReviewComment] nvarchar(max) NULL,
    [ReviewRound] int NOT NULL DEFAULT 0,
    [ReviewHistory] nvarchar(max) NULL,
    [PublishedAtUtc] datetime2 NULL,
    [PointEntryId] int NULL,
    [PointsAwarded] int NOT NULL DEFAULT 0
);");
        // El sello de concurrencia va APARTE y justo aquí, no en la lista de tablas de más arriba:
        // aquella corre antes de que esta tabla exista, así que el ALTER no habría hecho nada en el
        // primer arranque y la columna habría aparecido —en silencio— hasta el segundo.
        // [rowversion] se puede agregar NOT NULL sin DEFAULT: lo rellena el motor en cada fila.
        Exec(@"
IF OBJECT_ID(N'[KnowledgeArticles]', N'U') IS NOT NULL AND COL_LENGTH('KnowledgeArticles','RowVersion') IS NULL
ALTER TABLE [KnowledgeArticles] ADD [RowVersion] rowversion NOT NULL;");

        // El estado va de primera columna porque toda consulta empieza por él: la cola es «por
        // revisar», el buscador es «publicado» y la lista propia es «lo mío».
        // ── Los días que no se trabajan (SQL Server) ─────────────────────────────────
        // La gemela de la de SQLite. Misma clave única por fecha.
        Exec(@"
IF OBJECT_ID(N'[DiasFestivos]', N'U') IS NULL
CREATE TABLE [DiasFestivos] (
    [Id]      int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_DiasFestivos] PRIMARY KEY,
    [Fecha]   datetime2 NOT NULL,
    [Motivo]  nvarchar(200) NOT NULL,
    [EsDeLey] bit NOT NULL DEFAULT 1
);");

        Exec(@"
IF OBJECT_ID(N'[DiasFestivos]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Festivo_Fecha'
                   AND object_id = OBJECT_ID(N'[DiasFestivos]'))
CREATE UNIQUE INDEX [IX_Festivo_Fecha] ON [DiasFestivos]([Fecha]);");

        // ── Qué hace, en cada equipo, quien tiene cada rol (SQL Server) ──────────────
        // La gemela de la de SQLite. Misma clave única y misma cascada: el esquema no puede depender
        // de dónde corra.
        Exec(@"
IF OBJECT_ID(N'[DescripcionesDeRolDeEquipo]', N'U') IS NULL
CREATE TABLE [DescripcionesDeRolDeEquipo] (
    [Id]          int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_DescripcionesDeRolDeEquipo] PRIMARY KEY,
    [TeamId]      int NOT NULL,
    [Rol]         int NOT NULL,
    [Descripcion] nvarchar(600) NOT NULL,
    CONSTRAINT [FK_DescRol_Team] FOREIGN KEY ([TeamId]) REFERENCES [Teams]([Id]) ON DELETE CASCADE
);");

        // El índice va aparte de la creación y es ÚNICO: ExecIndex no crea únicos, así que se escribe
        // a mano con la misma guarda de existencia que usa aquél.
        Exec(@"
IF OBJECT_ID(N'[DescripcionesDeRolDeEquipo]', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DescRol_EquipoRol'
                   AND object_id = OBJECT_ID(N'[DescripcionesDeRolDeEquipo]'))
CREATE UNIQUE INDEX [IX_DescRol_EquipoRol] ON [DescripcionesDeRolDeEquipo]([TeamId],[Rol]);");

        ExecIndex("KnowledgeArticles", "IX_Know_Estado", "Status", "[Status],[UpdatedAtUtc]");
        ExecIndex("KnowledgeArticles", "IX_Know_Autor", "AuthorUserId", "[AuthorUserId],[Status]");
        ExecIndex("KnowledgeArticles", "IX_Know_Publicado", "PublishedAtUtc", "[PublishedAtUtc]");

        // Las imágenes incrustadas en los artículos (SQL Server). Va DESPUÉS de la tabla de la que
        // cuelga y no en la lista de tablas de más arriba: aquella corre antes de que
        // KnowledgeArticles exista, y una clave foránea hacia una tabla que todavía no está aborta
        // su sentencia — y con ella todas las que vengan detrás.
        //
        // Aquí SÍ hay clave foránea, al revés que el autor: una imagen sin artículo no significa
        // nada y nadie podría volver a llegar a ella, así que se va con él en cascada. Y no hay
        // segunda ruta hasta la misma tabla, que es lo que este motor rechaza.
        //
        // ESTA ES LA VERSIÓN DE T-SQL: la gemela de SQLite está traducida en la otra rama, con
        // BLOB donde aquí va varbinary(max).
        Exec(@"
IF OBJECT_ID(N'[KnowledgeImages]', N'U') IS NULL
CREATE TABLE [KnowledgeImages] (
    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_KnowledgeImages] PRIMARY KEY,
    [ArticleId] int NOT NULL,
    [FileName] nvarchar(260) NOT NULL,
    [ContentType] nvarchar(100) NOT NULL,
    [Bytes] varbinary(max) NOT NULL,
    [SizeBytes] bigint NOT NULL DEFAULT 0,
    [UploadedByUserId] int NOT NULL DEFAULT 0,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [FK_KnowledgeImage_Article] FOREIGN KEY ([ArticleId]) REFERENCES [KnowledgeArticles]([Id]) ON DELETE CASCADE
);");
        // Por artículo: es la única forma en que se piden —«las imágenes de este artículo», para
        // pintarlas en el editor— además de por su propio identificador, que ya es la clave.
        ExecIndex("KnowledgeImages", "IX_KnowImg_Articulo", "ArticleId", "[ArticleId],[Id]");

        return fallidas;
    }
}
