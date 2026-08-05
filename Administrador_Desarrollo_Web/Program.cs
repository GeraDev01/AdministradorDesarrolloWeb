using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Forms.Controls;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Velopack;

namespace Administrador_Desarrollo_Web;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // PRIMERA línea de todo, antes del mutex de instancia única y de cualquier otra cosa.
        // Velopack reinvoca este mismo ejecutable con argumentos especiales al instalar, actualizar
        // y desinstalar; esas invocaciones hacen su trabajo y terminan el proceso aquí mismo. Si el
        // mutex corriera antes, el instalador se vería como «ya hay una instancia» y no haría nada.
        //
        // Un fallo aquí NO puede impedir que la aplicación abra: se anota y se sigue sin
        // actualización automática. Los hooks terminan el proceso DENTRO de Run(), así que este
        // catch solo se alcanza en un arranque normal.
        Exception? fallaVelopack = null;
        try
        {
            VelopackApp.Build()
                // APAGADO a propósito. Encendido —su valor por omisión— abrir el .exe con un
                // paquete ya descargado dispara la aplicación y el Exit DENTRO de Run(), o sea
                // ANTES del mutex: la ventana que la persona pidió nunca aparece, y el updater
                // intenta reemplazar la carpeta mientras la instancia de la bandeja sigue viva con
                // sus DLL abiertas desde ahí. Aplicar es decisión de UpdateService, en el cierre.
                .SetAutoApplyOnStartup(false)
                .Run();
        }
        catch (Exception ex) { fallaVelopack = ex; }

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        // Una sola aplicación por sesión de Windows. Se comprueba ANTES del log y de la base: una
        // segunda ejecución no debe abrir el archivo de registro, ni conectar, ni sembrar nada.
        using var instancia = SingleInstance.Adquirir();
        if (!instancia.EsPrimera)
        {
            // Sin mensaje de «ya está abierta»: la respuesta a abrir la aplicación es que aparezca
            // la ventana. Un aviso sobraría y, con la ventana escondida en la bandeja, sería lo
            // único que se vería.
            instancia.PedirQueSeMuestre();
            return;
        }

        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdministradorDesarrolloWeb");
        Directory.CreateDirectory(appDataPath);
        var logsPath = Path.Combine(appDataPath, "logs");

        var loggerFactory = LoggingSetup.Configure(logsPath);
        var logger = loggerFactory.CreateLogger("Program");

        // El fallo de Velopack, si lo hubo, se registra ahora: en su momento no había log todavía
        // (a propósito — una segunda instancia no debe abrir el archivo de registro).
        if (fallaVelopack != null)
            logger.LogWarning(fallaVelopack, "Velopack no pudo inicializarse; se sigue sin actualización automática.");

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
        // configurado en este equipo o lo incrustado al publicar. Si no hay ninguna, la aplicación
        // NO abre una base local: lo dice en el login y no deja entrar.
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

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        if (eleccion.UsaSqlServer)
            // Normaliza también las cadenas guardadas antes (formato abreviado o sin Encrypt).
            services.AddDbContext<AppDbContext>(
                opts => opts.UseSqlServer(SqlConnectionStringHelper.NormalizeOrOriginal(eleccion.SqlServerConnection)),
                ServiceLifetime.Singleton);
        else
            // Sin conexión al servidor del equipo no se registra ninguna base: si algún camino
            // llegara aquí por accidente, se cae con un mensaje explícito en vez de crear una base
            // local vacía a espaldas del usuario.
            services.AddSingleton<AppDbContext>(_ => throw new InvalidOperationException(
                "No hay conexión con la base de datos del equipo. Esta aplicación no trabaja con una base local."));
        services.AddSingleton(dbCfg);
        services.AddSingleton(eleccion);
        // El estado de la conexión es de la aplicación, no de la ventana de inicio de sesión (que se
        // vuelve a construir en cada cierre de sesión).
        services.AddSingleton<DbConnectionState>();

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
        services.AddSingleton<ServerStatusService>();
        services.AddSingleton<DeploymentScheduleService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<DataMigrationService>();
        services.AddSingleton<SharedSecretMigrationService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<AzureDevOpsService>();
        services.AddSingleton<FreshDeskService>();
        services.AddSingleton<EmailService>();
        services.AddSingleton<WorkSessionService>();
        services.AddSingleton<PerformanceScoringService>();
        services.AddSingleton<VacationRequestService>();
        services.AddSingleton<LeaveRequestService>();
        services.AddSingleton<SuggestionService>();
        services.AddSingleton<DeveloperProfileService>();
        services.AddSingleton<AnnouncementService>();
        services.AddSingleton<SearchService>();
        services.AddSingleton<AutoBackupService>();
        services.AddSingleton<DigestService>();
        services.AddSingleton<DevActivityService>();
        services.AddSingleton<SlaService>();
        services.AddSingleton<SlaNotificationService>();
        services.AddSingleton<TemplateService>();
        services.AddSingleton<PresenceService>();
        services.AddSingleton<ForumService>();
        services.AddSingleton<SprintService>();
        services.AddSingleton<CommitmentAlertService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<DataCleanupService>();

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
        services.AddTransient<SprintControl>();
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
        services.AddTransient<MyPresenceControl>();
        services.AddTransient<MyEvaluationsControl>();
        services.AddTransient<NotificationsControl>();
        services.AddTransient<MyActivitiesControl>();
        services.AddTransient<ActivitiesAdminControl>();
        services.AddTransient<SlaAdminControl>();
        services.AddTransient<MySlaControl>();
        services.AddTransient<MyDevPerformanceControl>();
        services.AddTransient<MyVacationsControl>();
        services.AddTransient<MyLeavesControl>();
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
        services.AddTransient<TemplatesControl>();
        services.AddTransient<PresenceControl>();
        services.AddTransient<ForumControl>();
        services.AddTransient<DataCleanupControl>();

        var provider = services.BuildServiceProvider();

        // El esquema y el seed solo tienen sentido si la base del equipo respondió. El resultado es
        // lo que pinta el indicador ● del login: verde si se abrió la base del equipo, rojo con el
        // motivo si no. Antes un fallo aquí cerraba la aplicación tras un MessageBox y no quedaba
        // rastro en pantalla de contra qué base se había intentado trabajar.
        DbConnectionStatus estado;
        string? adminPwd = null;
        if (eleccion.UsaSqlServer)
            (estado, adminPwd) = PrepararBaseDeDatos(provider, eleccion, logger);
        else
            estado = DbConnectionStatus.SinConexion();

        if (!estado.Conectado)
            logger.LogError("Sin base de datos del equipo: {titulo} — {detalle}", estado.Titulo, estado.Detalle);
        if (adminPwd != null) MostrarPasswordAdminInicial(adminPwd);

        // Reintentar solo aporta cuando hay una cadena que probar (típicamente falta la VPN). Si el
        // ejecutable no trae conexión y el equipo no tiene ninguna, no hay nada que reintentar hasta
        // que el administrador reparta un ejecutable publicado con ella.
        Func<Task<DbConnectionStatus>>? reintentar = null;
        if (eleccion.UsaSqlServer)
            reintentar = async () =>
            {
                var (nuevo, pwd) = await Task.Run(() => PrepararBaseDeDatos(provider, eleccion, logger));
                if (pwd != null) MostrarPasswordAdminInicial(pwd);   // el await vuelve al hilo de UI
                return nuevo;
            };

        // Se deja en el estado compartido ANTES de crear la ventana: así lo lee tanto esta pantalla
        // de inicio de sesión como la que se construya al cerrar sesión.
        provider.GetRequiredService<DbConnectionState>().Configurar(estado, reintentar);

        var login = provider.GetRequiredService<LoginForm>();

        // Volver a abrir el ejecutable trae al frente ESTA aplicación en vez de arrancar otra.
        // La señal llega en un hilo de fondo, así que se salta al de la interfaz: `login` sirve de
        // puente porque es la ventana de Application.Run y sigue viva (escondida) toda la ejecución,
        // incluso mientras se trabaja en la principal o después de cerrar sesión.
        instancia.OtraInstanciaLlamo += () =>
        {
            try { login.BeginInvoke(WindowActivation.TraerAlFrente); }
            catch { /* aún sin handle o cerrando: no hay ventana a la que saltar */ }
        };

        logger.LogInformation("Iniciando aplicación...");
        Application.Run(login);
        logger.LogInformation("Aplicación cerrada.");
        Serilog.Log.CloseAndFlush();
    }

    /// <summary>
    /// Abre la base del equipo, pone el esquema al día y siembra lo indispensable. Devuelve el estado
    /// para el indicador del login y, solo cuando la base estaba recién creada, la contraseña temporal
    /// del admin sembrado.
    ///
    /// No lanza: un fallo se convierte en el ● rojo del login en vez de cerrar la aplicación.
    /// </summary>
    private static (DbConnectionStatus estado, string? adminPwd) PrepararBaseDeDatos(
        IServiceProvider provider, DbConnectionChoice eleccion, ILogger logger)
    {
        AppDbContext db;
        try
        {
            db = provider.GetRequiredService<AppDbContext>();

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
                eleccion.Descripcion, SqlConnectionStringHelper.Mask(eleccion.SqlServerConnection));
        }
        catch (Exception ex)
        {
            // El detalle técnico (servidor, base, número de error de SQL) se queda aquí, en el log:
            // la pantalla de login solo dice que no se pudo conectar y el motivo en una línea.
            logger.LogCritical(ex, "Error al inicializar la BD");
            return (DbConnectionStatus.Fallo(ex), null);
        }

        string? seededAdminPwd = null;
        try
        {
            seededAdminPwd = provider.GetRequiredService<AuthService>().SeedAdmin();
            // El renombrado va ANTES del sembrado: al revés, el sembrado insertaría la versión
            // nueva de cada criterio y la base quedaría con el mismo criterio dos veces.
            int renombrados = ScoringCriteriaSeed.MigrarNomenclatura(db);
            if (renombrados > 0) logger.LogInformation("{n} criterio(s) renombrados al catálogo nuevo.", renombrados);
            ScoringCriteriaSeed.Sembrar(db);
            // Catálogo inicial de plantillas: una sola vez, para que la pantalla no se estrene vacía.
            TemplateSeed.Sembrar(db);
            // Reconciliar cronómetros huérfanos de un cierre sucio/crash anterior
            // (evita contar el tiempo con la app cerrada).
            provider.GetRequiredService<WorkSessionService>().ReconcileOrphans();
            logger.LogInformation("Seed completado.");
        }
        catch (Exception ex) { logger.LogError(ex, "Error en seed"); }

        // Secretos compartidos: pasa al cifrado portable lo que este equipo alcance a descifrar, para
        // que las contraseñas FTP y la conexión del Blob dejen de estar atadas a la PC que las
        // capturó. Va fuera del try anterior y no lanza: con un login restringido falla y es normal.
        provider.GetRequiredService<SharedSecretMigrationService>().EjecutarSeguro();

        return (DbConnectionStatus.Ok(), seededAdminPwd);
    }

    /// <summary>Base recién creada: la contraseña temporal del admin se muestra una sola vez.</summary>
    private static void MostrarPasswordAdminInicial(string password) =>
        MessageBox.Show(
            $"Se creó el usuario líder inicial.\n\nUsuario:  admin\nContraseña temporal:  {password}\n\nGuárdala en un lugar seguro. Deberás cambiarla al iniciar sesión.",
            "Primer arranque", MessageBoxButtons.OK, MessageBoxIcon.Information);

}
