using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Forms.Controls;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdministradorDesarrolloWeb");
        Directory.CreateDirectory(appDataPath);
        var dbPath   = Path.Combine(appDataPath, "app.db");
        var logsPath = Path.Combine(appDataPath, "logs");

        var loggerFactory = LoggingSetup.Configure(logsPath);
        var logger = loggerFactory.CreateLogger("Program");

        Application.ThreadException += (_, e) =>
        {
            logger.LogError(e.Exception, "Excepción no manejada en UI thread");
            MessageBox.Show($"Error inesperado:\n{e.Exception.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.LogCritical(e.ExceptionObject as Exception, "Excepción crítica");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "Excepción de Task no observada");
            e.SetObserved();
        };

        // Una sola aplicación para todo el equipo. La conexión sale, en este orden, de: lo
        // configurado en este equipo, lo incrustado al publicar, o la base local SQLite.
        var dbCfg = DbProviderConfig.Load();
        var eleccion = DbConnectionResolver.Resolve(dbCfg, EmbeddedDbConfig.ConnectionString);

        // Un problema al leer la configuración no se traga: el silencio hacía que el usuario
        // siguiera capturando datos en SQLite creyendo estar en SQL Server.
        if (eleccion.Warning is { Length: > 0 } cfgError)
        {
            logger.LogWarning("Configuración de BD con problema: {motivo}", cfgError);
            MessageBox.Show(
                $"{cfgError}\n\nSe usará: {eleccion.Descripcion}.",
                "Configuración de base de datos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        bool usaSqlServer = eleccion.UsaSqlServer;

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddDbContext<AppDbContext>(opts =>
        {
            if (usaSqlServer)
                // Normaliza también las cadenas guardadas antes (formato abreviado o sin Encrypt).
                opts.UseSqlServer(SqlConnectionStringHelper.NormalizeOrOriginal(eleccion.SqlServerConnection));
            else
                opts.UseSqlite($"Data Source={dbPath}");
        }, ServiceLifetime.Singleton);
        services.AddSingleton(dbCfg);
        services.AddSingleton(eleccion);

        services.AddSingleton<CurrentUserContext>();
        // Identidad de sesión como abstracción (misma instancia singleton en el escritorio).
        services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserContext>());
        services.AddSingleton<AuditService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<SignatureService>();
        services.AddSingleton<VacationDocumentService>();
        services.AddSingleton<IDocxToPdfConverter, LibreOfficeConverter>();
        services.AddSingleton<RequirementAttachmentService>();
        services.AddSingleton<TeamRosterService>();
        services.AddSingleton<DeveloperReportService>();
        services.AddSingleton<BlobStorageService>();
        services.AddSingleton<RemoteBackupService>();
        services.AddSingleton<DeploymentService>();
        services.AddSingleton<DeploymentTargetService>();
        services.AddSingleton<DeploymentScheduleService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<DataMigrationService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<AzureDevOpsService>();
        services.AddSingleton<FreshDeskService>();
        services.AddSingleton<EmailService>();
        services.AddSingleton<WorkSessionService>();
        services.AddSingleton<PerformanceScoringService>();
        services.AddSingleton<VacationRequestService>();
        services.AddSingleton<SuggestionService>();
        services.AddSingleton<DeveloperProfileService>();
        services.AddSingleton<AnnouncementService>();
        services.AddSingleton<SearchService>();
        services.AddSingleton<AutoBackupService>();
        services.AddSingleton<DigestService>();
        services.AddSingleton<DevActivityService>();
        services.AddSingleton<SlaService>();
        services.AddSingleton<SlaNotificationService>();

        services.AddTransient<LoginForm>();
        services.AddTransient<MainForm>();
        // Fase 0/1
        services.AddTransient<DashboardControl>();
        services.AddTransient<DevelopersControl>();
        services.AddTransient<TeamsControl>();
        services.AddTransient<ContactsControl>();
        services.AddTransient<MetricsControl>();
        services.AddTransient<ReportsControl>();
        services.AddTransient<EmailControl>();
        services.AddTransient<RequirementsControl>();
        services.AddTransient<UserManagementControl>();
        services.AddTransient<AuditLogControl>();
        services.AddTransient<ConfigurationControl>();
        // Fase 2
        services.AddTransient<MinutesControl>();
        services.AddTransient<VacationsControl>();
        services.AddTransient<SuggestionsControl>();
        services.AddTransient<LeaveRequestsControl>();
        services.AddTransient<PerformanceControl>();
        services.AddTransient<DeveloperReportsControl>();
        // Fase 3
        services.AddTransient<DeploymentControl>();
        services.AddTransient<ScheduledDeploymentsControl>();
        services.AddTransient<AzureResourcesControl>();
        services.AddTransient<SoftwareControl>();
        // Fase 4 — Desarrollador self-service
        services.AddTransient<MyAssignmentsControl>();
        services.AddTransient<MyDevOpsTicketsControl>();
        services.AddTransient<MyEvaluationsControl>();
        services.AddTransient<NotificationsControl>();
        services.AddTransient<MyActivitiesControl>();
        services.AddTransient<ActivitiesAdminControl>();
        services.AddTransient<SlaAdminControl>();
        services.AddTransient<MySlaControl>();
        services.AddTransient<MyDevPerformanceControl>();
        services.AddTransient<MyVacationsControl>();
        services.AddTransient<MySuggestionsControl>();
        // Tickets / integración
        services.AddTransient<AzureDevOpsControl>(); // SettingsService inyectado automáticamente
        services.AddTransient<DevOpsTagsDashboardControl>();
        services.AddTransient<SlaComplianceControl>();
        services.AddTransient<EstimationCapacityControl>();
        services.AddTransient<DeveloperProfilesControl>();
        services.AddTransient<AnnouncementsControl>();
        services.AddTransient<FreshDeskControl>();
        services.AddTransient<TicketLinkControl>();

        var provider = services.BuildServiceProvider();

        try
        {
            var db = provider.GetRequiredService<AppDbContext>();

            try
            {
                DatabaseMigrator.EnsureUpToDate(db);
            }
            catch (Exception exEsquema)
            {
                // Con un login restringido (sin permisos de DDL) esto falla, y es normal: el
                // esquema ya lo preparó el administrador. Solo es fatal si además no hay conexión.
                if (!db.Database.CanConnect())
                    throw new InvalidOperationException(
                        "No se pudo conectar a la base de datos del equipo. Revisa tu red o VPN.", exEsquema);

                logger.LogWarning(exEsquema,
                    "No se pudo verificar/actualizar el esquema (probablemente por permisos). Se continúa.");
            }

            // Antes esto siempre registraba la ruta de SQLite, aunque la app estuviera en SQL Server:
            // imposible saber desde el log contra qué base se estaba trabajando.
            logger.LogInformation("BD inicializada [{origen}]: {destino}",
                eleccion.Descripcion,
                usaSqlServer ? SqlConnectionStringHelper.Mask(eleccion.SqlServerConnection) : dbPath);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Error al inicializar la BD");
            MessageBox.Show($"No se pudo inicializar la base de datos:\n{ex.Message}", "Error crítico", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            var auth = provider.GetRequiredService<AuthService>();
            var seededAdminPwd = auth.SeedAdmin();
            SeedDefaultCriteria(provider.GetRequiredService<AppDbContext>());
            // Reconciliar cronómetros huérfanos de un cierre sucio/crash anterior
            // (evita contar el tiempo con la app cerrada).
            provider.GetRequiredService<WorkSessionService>().ReconcileOrphans();
            logger.LogInformation("Seed completado.");
            // Primer arranque: mostrar una sola vez la contraseña temporal del admin recién creado.
            if (seededAdminPwd != null)
                MessageBox.Show(
                    $"Se creó el usuario administrador inicial.\n\nUsuario:  admin\nContraseña temporal:  {seededAdminPwd}\n\nGuárdala en un lugar seguro. Deberás cambiarla al iniciar sesión.",
                    "Primer arranque", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { logger.LogError(ex, "Error en seed"); }

        logger.LogInformation("Iniciando aplicación...");
        Application.Run(provider.GetRequiredService<LoginForm>());
        logger.LogInformation("Aplicación cerrada.");
        Serilog.Log.CloseAndFlush();
    }

    private static void SeedDefaultCriteria(AppDbContext db)
    {
        var criteria = new (string Name, string Desc, int Pts)[]
        {
            // ── Premios (positivos) ─────────────────────────────────
            ("Entrega a tiempo",            "El requerimiento se entregó en la fecha comprometida.",                    +10),
            ("Entrega anticipada",          "Se entregó antes de la fecha comprometida sin sacrificar calidad.",        +12),
            ("Calidad / sin retrabajo",     "La entrega no requirió correcciones significativas.",                      +8),
            ("Cero bugs en QA",             "El ciclo de pruebas no encontró defectos.",                                +12),
            ("Corrección de bug",           "Se corrigió un defecto reportado en producción o QA.",                     +5),
            ("Corrección de bug crítico",   "Resolvió con rapidez un incidente crítico o de producción.",               +10),
            ("Apoyo a compañeros",          "Colaboración activa en la resolución de problemas del equipo.",            +5),
            ("Mentoría / onboarding",       "Ayudó a integrar o capacitar a un compañero nuevo.",                       +8),
            ("Buen code review",            "Revisiones de código oportunas, cuidadosas y constructivas.",              +5),
            ("Documentación actualizada",   "Se actualizó la documentación técnica del sistema.",                       +5),
            ("Cobertura de pruebas",        "Agregó pruebas automatizadas significativas.",                             +8),
            ("Automatización",              "Automatizó una tarea manual repetitiva (CI/CD, scripts, pruebas).",        +10),
            ("Reducción de deuda técnica",  "Refactorizó código legado mejorando la mantenibilidad.",                   +10),
            ("Cumplimiento del daily",      "Asistencia y participación en la sesión diaria.",                          +3),
            ("Cumplimiento de estándares",  "Siguió las guías de estilo y arquitectura del equipo.",                    +5),
            ("Iniciativa / mejora",         "Propuesta o implementación de una mejora sin solicitarse.",                +15),
            ("Innovación técnica",          "Introdujo una tecnología o práctica que benefició al equipo.",             +15),
            ("Atención al cliente",         "Resolvió una solicitud de cliente con excelente servicio.",                +8),
            ("Disponibilidad en incidente", "Apoyó fuera de horario en una contingencia o guardia.",                    +8),
            ("Capacitación completada",     "Terminó un curso o certificación relevante para su rol.",                  +6),
            // ── Penalizaciones (negativos) ──────────────────────────
            ("Entrega tardía",              "El requerimiento se entregó después de la fecha comprometida.",            -10),
            ("Bug en producción",           "Se detectó un defecto crítico en el ambiente productivo.",                 -15),
            ("Retrabajo por calidad",       "La entrega requirió correcciones importantes.",                            -8),
            ("Reapertura de bug",           "Un bug marcado como resuelto se reabrió.",                                 -8),
            ("Incumplimiento del daily",    "Faltó o no participó en la reunión diaria sin aviso.",                     -5),
            ("Falta de documentación",      "Entregó sin la documentación requerida.",                                  -5),
            ("Incumplimiento de estándares","No siguió las guías de estilo/arquitectura acordadas.",                    -5),
            ("Estimación imprecisa",        "Desviación considerable respecto a lo estimado sin justificación.",        -5),

            // ── Git / Pull Requests / commits ───────────────────────
            ("PR claro y pequeño",           "Abrió un Pull Request pequeño, enfocado y con buena descripción.",         +5),
            ("Historial de commits limpio",  "Commits atómicos, ordenados y con mensajes descriptivos.",                +4),
            ("Desbloqueó revisando un PR",   "Revisó a tiempo el PR de un compañero para desbloquearlo.",               +4),
            ("No subió el PR",               "No creó el Pull Request de sus cambios para revisión.",                   -8),
            ("PR sin descripción",           "Abrió un PR sin descripción ni contexto de los cambios.",                 -4),
            ("PR demasiado grande",          "PR excesivamente grande y difícil de revisar en un solo cambio.",         -4),
            ("Planchó cambios en el merge",  "Sobrescribió cambios de otros al hacer merge (pérdida de código).",      -15),
            ("Conflictos mal resueltos",     "Resolvió conflictos de merge de forma incorrecta rompiendo algo.",       -8),
            ("Rompió la rama principal",     "Dejó la rama principal con el build o el pipeline roto.",                 -12),
            ("Commit directo a protegida",   "Subió cambios directo a una rama protegida evitando la revisión.",       -8),
            ("Mal mensaje de commit",        "Mensajes de commit poco descriptivos o sin seguir la convención.",       -3),
            ("Commits no atómicos",          "Mezcló múltiples cambios no relacionados en un solo commit.",            -3),
            ("Subió secretos al repo",       "Expuso credenciales, llaves o secretos en el control de versiones.",    -20),
            ("Versionó archivos basura",     "Incluyó binarios, dependencias o temporales que no debían versionarse.",-3),
            ("Trabajó sobre rama vieja",     "No actualizó su rama antes de trabajar provocando conflictos evitables.",-3),

            // ── Tickets / evidencias / seguimiento ──────────────────
            ("Evidencias completas",         "Adjuntó evidencias claras (capturas, pasos, resultados) al ticket.",    +5),
            ("Buen seguimiento del ticket",  "Mantuvo el ticket actualizado con comentarios y estado real.",          +4),
            ("Reprodujo y documentó bug",    "Reprodujo y documentó a detalle un defecto para su corrección.",        +5),
            ("Sin evidencias en el ticket",  "Cerró el ticket sin adjuntar evidencias de la solución o pruebas.",     -5),
            ("Sin comentarios en el ticket", "No registró comentarios ni actualizaciones de avance en el ticket.",    -4),
            ("Estado del ticket sin actualizar","Dejó el ticket con un estado que no refleja el avance real.",        -3),
            ("No registró tiempo",           "No registró el tiempo trabajado en el ticket.",                         -2),

            // ── QA / pruebas / sprint ───────────────────────────────
            ("Aprobado por QA a la primera", "La entrega pasó QA a la primera sin observaciones.",                   +10),
            ("Cumplió criterios de aceptación","Cumplió todos los criterios de aceptación en la primera revisión.",  +5),
            ("Sin carry over",               "Cerró todos sus compromisos del sprint sin dejar arrastres.",          +6),
            ("Apoyo a QA",                   "Preparó datos o ambiente de prueba facilitando la validación de QA.",  +4),
            ("Carry over",                   "Dejó tareas comprometidas del sprint sin terminar (carry over).",      -6),
            ("Carry over recurrente",        "Arrastra tareas sin terminar de forma recurrente entre sprints.",      -10),
            ("Rechazo en QA",                "La entrega fue rechazada por QA por no cumplir los criterios.",        -8),
            ("Bug encontrado en QA",         "QA encontró defectos en la entrega.",                                  -5),
            ("Entregó sin probar",           "Entregó a QA sin haber probado sus propios cambios.",                  -6),
            ("Regresión introducida",        "Introdujo una regresión que rompió funcionalidad existente.",          -10),

            // ── Seguridad / comunicación / proceso ──────────────────
            ("Buenas prácticas de seguridad","Aplicó buenas prácticas de seguridad (validación, manejo de secretos).",+6),
            ("Vulnerabilidad introducida",   "Introdujo una vulnerabilidad de seguridad conocida.",                  -10),
            ("Comunicó bloqueo a tiempo",    "Comunicó oportunamente un riesgo o bloqueo permitiendo mitigarlo.",    +4),
            ("No comunicó un bloqueo",       "No avisó a tiempo de un bloqueo que afectó la entrega.",               -5),
            ("No siguió el proceso",         "No siguió el flujo de trabajo o proceso acordado por el equipo.",      -4),
            ("Ausencia sin aviso",           "Se ausentó sin avisar, afectando la coordinación del equipo.",         -6),

            // ── Según nivel: Junior ─────────────────────────────────
            ("Autonomía creciente (Junior)", "Junior que resolvió una tarea con mínima ayuda, mostrando autonomía.", +6),
            ("Aprendizaje rápido (Junior)",  "Junior que asimiló con rapidez una nueva tecnología del proyecto.",    +6),
            ("Buenas preguntas (Junior)",    "Junior que hizo preguntas oportunas evitando retrabajo.",             +3),
            ("Dependencia excesiva (Junior)","Junior que requirió acompañamiento constante en tareas ya explicadas.",-3),
            ("No pidió ayuda a tiempo (Junior)","Se atascó demasiado tiempo sin pedir ayuda, retrasando la tarea.",  -3),

            // ── Según nivel: Mid ────────────────────────────────────
            ("Ownership de módulo (Mid)",    "Mid que se hizo responsable de un módulo de principio a fin.",         +8),
            ("Estimaciones confiables (Mid)","Mid cuyas estimaciones fueron consistentemente acertadas.",           +6),
            ("Resolvió sin escalar (Mid)",   "Mid que resolvió un problema propio de su nivel sin escalarlo.",       +5),
            ("Requiere supervisión (Mid)",   "Mid que aún requiere supervisión en tareas propias de su nivel.",      -4),
            ("Entregó sin criterio (Mid)",   "Mid que entregó sin aplicar el criterio esperado para su nivel.",      -4),

            // ── Según nivel: Senior ─────────────────────────────────
            ("Liderazgo técnico (Senior)",   "Senior que guió decisiones técnicas y destrabó al equipo.",           +12),
            ("Diseño de arquitectura (Senior)","Senior que propuso un diseño o arquitectura sólido y escalable.",    +12),
            ("Mentoría de nivel (Senior)",   "Senior que elevó el nivel del equipo mediante mentoría constante.",    +10),
            ("Gestión de riesgos (Senior)",  "Senior que anticipó y mitigó riesgos técnicos del proyecto.",         +8),
            ("Cuello de botella (Senior)",   "Senior que centralizó el trabajo volviéndose cuello de botella.",      -5),
            ("Falta de liderazgo (Senior)",  "Senior que no asumió el liderazgo técnico esperado para su nivel.",    -6),
            ("Decisión técnica deficiente (Senior)","Senior cuya decisión técnica generó retrabajo significativo.",  -8),
        };

        // Alta idempotente: agrega solo los criterios que aún no existen (por nombre),
        // de modo que también se completen en bases de datos ya sembradas.
        // Criterios propios de EQUIPO (Scope = Equipo): no aplican a individuos.
        var teamCriteria = new (string Name, string Desc, int Pts)[]
        {
            ("Objetivo de sprint cumplido (equipo)",   "El equipo cumplió el objetivo comprometido del sprint.",     +15),
            ("Cero incidentes en producción (equipo)", "El equipo no tuvo incidentes en producción en el período.",  +12),
            ("Calidad del equipo",                     "Bajo índice de retrabajo y bugs a nivel de equipo.",         +10),
            ("Colaboración entre equipos",             "El equipo colaboró efectivamente con otras áreas.",          +8),
            ("Velocidad estable (equipo)",             "El equipo mantuvo una velocidad de entrega estable.",        +8),
            ("Clima y colaboración",                   "Buen ambiente y colaboración dentro del equipo.",            +6),
            ("Documentación del equipo al día",        "El equipo mantuvo su documentación actualizada.",            +5),
            ("Objetivo de sprint incumplido (equipo)", "El equipo no cumplió el objetivo del sprint.",               -12),
            ("Incidente de producción (equipo)",       "El equipo causó un incidente en producción.",                -15),
            ("Carry over del equipo",                  "El equipo arrastró trabajo sin terminar del sprint.",        -8),
        };

        var existing = db.ScoringCriteria.Select(c => c.Name).ToHashSet();
        bool added = false;
        foreach (var (name, desc, pts) in criteria)
        {
            if (existing.Contains(name)) continue;
            db.ScoringCriteria.Add(new ScoringCriterion { Name = name, Description = desc, DefaultPoints = pts, IsActive = true, Scope = CriterionScope.Individual, CreatedAt = DateTime.UtcNow });
            added = true;
        }
        foreach (var (name, desc, pts) in teamCriteria)
        {
            if (existing.Contains(name)) continue;
            db.ScoringCriteria.Add(new ScoringCriterion { Name = name, Description = desc, DefaultPoints = pts, IsActive = true, Scope = CriterionScope.Equipo, CreatedAt = DateTime.UtcNow });
            added = true;
        }
        if (added) db.SaveChanges();
    }
}
