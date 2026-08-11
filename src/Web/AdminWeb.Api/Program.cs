using AdminWeb.Api.Arranque;
using AdminWeb.Api.Auth;
using AdminWeb.Api.Endpoints;
using AdminWeb.Api.Filtros;
using AdminWeb.Api.Jobs;
using AdminWeb.Api.TiempoReal;
using AdminWeb.Shared.TiempoReal;
using AdminWeb.Application.Services;
using AdminWeb.Domain.Avisos;
using AdminWeb.Domain.Documentos;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Avisos;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Documentos;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared;
using AdminWeb.Shared.Enums;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Lo PRIMERO: si algo del arranque falla, tiene que quedar escrito en el archivo del día y no solo
// en una salida estándar que quizá nadie estaba mirando.
RegistroEnArchivo.Configurar(builder);

// ── Datos ───────────────────────────────────────────────────────────────────────
//
// SCOPED, uno por petición. Es el cambio de fondo respecto al escritorio, donde el AppDbContext era
// Singleton y obligaba a desconfiar de todo lo que estuviera rastreado. La fábrica es para los
// trabajos de fondo, que no tienen petición de la que colgarse.
var cadena = builder.Configuration.GetConnectionString("Default")
             ?? throw new InvalidOperationException(
                 "Falta la cadena de conexión 'Default'. En desarrollo va en user-secrets o appsettings.Development.json; " +
                 "en Azure, como ajuste del App Service llamado ConnectionStrings__Default.");

// El proveedor es configurable porque SQLite hace falta en dos sitios reales: para levantar la
// aplicación en un equipo sin SQL Server, y para las pruebas de humo, que no deben tocar ninguna
// base de verdad. En staging y en producción es SQL Server y punto.
bool usarSqlite = string.Equals(
    builder.Configuration["AdminWeb:ProveedorDeBase"], "Sqlite", StringComparison.OrdinalIgnoreCase);

void Configurar(DbContextOptionsBuilder o)
{
    if (usarSqlite) o.UseSqlite(cadena);
    else o.UseSqlServer(cadena);
}

builder.Services.AddDbContext<AppDbContext>(Configurar);
builder.Services.AddDbContextFactory<AppDbContext>(Configurar, ServiceLifetime.Scoped);

// ── Identidad de la petición ────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, ClaimsCurrentUser>();
builder.Services.AddScoped<IRequestOrigin, HttpRequestOrigin>();

// ── Secretos por usuario ────────────────────────────────────────────────────────
//
// Sustituye al archivo cifrado con DPAPI que cada quien tenía en su máquina. Dónde viven las llaves
// —Blob en Azure, carpeta en local— y con qué se cifran ellas mismas —un certificado, porque esta
// suscripción no tiene Key Vault— lo decide Llavero a partir de la configuración; ahí está explicado
// por qué NO pueden quedarse en el sistema de archivos efímero del contenedor, y por qué una huella
// configurada cuyo certificado no aparece tumba el arranque a propósito.
Llavero.Configurar(builder);
builder.Services.AddScoped<IProtectorDeSecretos, ProtectorDeSecretos>();

// ── Servicios de negocio ────────────────────────────────────────────────────────
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<PruebasDeConexionService>();

// Los documentos en PDF. Singleton porque no guarda estado ni toca la base: recibe los datos ya
// resueltos y los maqueta. Es el ÚNICO sitio que sabe qué librería de PDF se usa — cambiarla es
// escribir otra implementación de la interfaz, no tocar a quien pide un documento.
builder.Services.AddSingleton<IGeneradorDeDocumentos, GeneradorDeDocumentosQuestPdf>();

// La MISMA solicitud de vacaciones, pero en Word y sobre una plantilla que el área puede editar sin
// recompilar. Singleton por lo mismo: no guarda estado y no toca la base. Rellenar un .docx no
// necesita LibreOffice — eso solo hacía falta para convertirlo a PDF, y de eso se encarga QuestPDF.
builder.Services.AddSingleton<IPlantillaDeVacacionesEnWord, PlantillaDeVacacionesOpenXml>();

// ── Avisos con la pestaña cerrada ───────────────────────────────────────────────
//
// Sustituyen a los globos de la bandeja del sistema, que era lo único que la web no podía hacer.
// Las llaves VAPID se generan UNA vez y no cambian: regenerarlas invalida de golpe todas las
// suscripciones guardadas y nadie vuelve a recibir un aviso hasta que acepte otra vez. En Azure la
// privada va como ajuste del App Service, no en un vault: no hay ninguno en esta suscripción, así que
// además hay que guardarla aparte porque de ahí no se recupera. Sin llaves configuradas, esto se
// comporta como «no hay push» y la aplicación
// funciona igual — los avisos dentro de la aplicación son los que de verdad importan.
builder.Services.AddSingleton(builder.Configuration.GetSection("AdminWeb:Push").Get<OpcionesDeAvisosPush>()
                              ?? new OpcionesDeAvisosPush());
builder.Services.AddHttpClient<IEnvioDeAvisosPush, EnvioDeAvisosPush>();
builder.Services.AddScoped<AvisosPushService>();
builder.Services.AddScoped<UserSecretsService>();
builder.Services.AddScoped<UserPreferencesService>();

// Servicios portados del escritorio.
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<PerformanceScoringService>();
builder.Services.AddScoped<TemplateService>();
builder.Services.AddScoped<PresenceService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<WorkSessionService>();
builder.Services.AddScoped<DevActivityService>();
builder.Services.AddScoped<PoolActivityService>();
builder.Services.AddScoped<ForumService>();
builder.Services.AddScoped<SuggestionService>();
builder.Services.AddScoped<VacationRequestService>();
builder.Services.AddScoped<LeaveRequestService>();
builder.Services.AddScoped<SprintService>();
builder.Services.AddScoped<SearchService>();
builder.Services.AddScoped<DeveloperProfileService>();
builder.Services.AddScoped<AnnouncementService>();

// Administración (fase 3).
//
// Que un servicio falte aquí NO se nota al compilar: un parámetro de endpoint cuyo tipo no está
// registrado se interpreta como cuerpo de la petición, y la aplicación revienta al ARRANCAR con
// «Body was inferred but the method does not allow inferred body parameters». La prueba de humo es
// lo que lo atrapa, porque levanta la aplicación de verdad.
builder.Services.AddScoped<RequirementService>();
builder.Services.AddScoped<RequirementAttachmentService>();
builder.Services.AddScoped<NotasService>();
builder.Services.AddScoped<DataCleanupService>();
builder.Services.AddScoped<ReportesService>();
builder.Services.AddScoped<MinutasService>();
builder.Services.AddScoped<RevisionDePuntosService>();

// ── Integraciones (fase 4) ──────────────────────────────────────────────────────
//
// Los clientes que hablan con el mundo exterior viven en Infrastructure y se registran por su
// interfaz: es lo que permite probar los servicios que los usan sin salir a la red.
//
// Freshdesk va con AddHttpClient y no con un HttpClient nuevo por petición, que agota los sockets.
// El correo no lo necesita: MailKit habla SMTP e IMAP por socket, no por HTTP.
builder.Services.AddHttpClient<IApiDeFreshdesk, FreshDeskService>();
builder.Services.AddScoped<TicketsDeFreshdeskService>();
builder.Services.AddScoped<VinculosDeTicketsService>();

builder.Services.AddSingleton<IClienteDeCorreo, EmailService>();
builder.Services.AddScoped<DigestService>();
builder.Services.AddScoped<IngestaDeCorreoService>();

// Azure DevOps. El PAT no se configura aquí: es PERSONAL y viaja por petición, firmada con el de
// quien la provocó, para que en DevOps los comentarios queden a su nombre y no a nombre de una
// cuenta compartida. Es lo que arregla que en el escritorio se perdiera al cambiar de máquina.
builder.Services.AddHttpClient<IClienteAzureDevOps, AzureDevOpsService>(cliente =>
{
    cliente.Timeout = TimeSpan.FromSeconds(100);
});
builder.Services.AddScoped<DevOpsService>();
builder.Services.AddScoped<DevOpsQueryService>();

// ── Despliegues (fase 5) ────────────────────────────────────────────────────────
//
// El despliegue corre en el SERVIDOR y sobrevive a la petición que lo lanzó: por eso el ejecutor es
// SINGLETON y abre su propio ámbito por trabajo (IServiceScopeFactory). Es la diferencia de fondo
// con el escritorio, donde lo ejecutaba el .exe de quien pulsaba el botón y cerrar la aplicación a
// media subida lo dejaba a medias. Aquí cerrar la pestaña no lo cancela.
builder.Services.AddScoped<IPublicacionDeDespliegue, PublicacionFtp>();
builder.Services.AddSingleton<IAvisosDeDespliegue, AvisosDeDespliegueEnVivo>();
builder.Services.AddSingleton<EjecutorDeDespliegues>();
builder.Services.AddScoped<DeploymentService>();
builder.Services.AddScoped<DeploymentTargetService>();
builder.Services.AddScoped<DespliegueCatalogoService>();
builder.Services.AddScoped<DespliegueQueryService>();

// Almacenamiento y agenda. Los clientes que hablan con Azure y con FTP no guardan estado: arman su
// conexión por llamada a partir de la configuración.
builder.Services.AddSingleton<IClienteDeBlobs, ClienteDeBlobsAzure>();
builder.Services.AddSingleton<IDescargaDeCarpetaRemota, DescargaDeCarpetaRemotaFtp>();
builder.Services.AddScoped<AlmacenamientoService>();
builder.Services.AddScoped<RespaldoPrevioService>();
builder.Services.AddScoped<ProgramadosService>();
builder.Services.AddScoped<EstadoDeServidoresService>();

// La pieza que une la agenda con el despliegue. NO se registra DeploymentService aquí: ese es la
// puerta de la pantalla y exige sesión, checklist marcado y nota — nada de lo cual tiene un
// despliegue que corre de madrugada sin nadie delante. El adaptador usa la misma maquinaria y
// sustituye el checklist por la evidencia que la agenda generó al programar la cita.
builder.Services.AddScoped<IEjecutorDeDespliegues, EjecutorDeProgramados>();

builder.Services.AddScoped<SlaService>();
builder.Services.AddScoped<SlaAlertTracker>();
builder.Services.AddScoped<SlaNotificationService>();
builder.Services.AddScoped<CommitmentAlertService>();
builder.Services.AddScoped<EvaluacionesService>();
builder.Services.AddScoped<SignatureService>();
builder.Services.AddScoped<DocumentoDeVacacionesService>();

// El saldo de vacaciones. Scoped como el resto: lee la ficha, las solicitudes y la configuración de
// caducidad por petición, y no guarda nada entre llamadas — el saldo se calcula cada vez a propósito.
builder.Services.AddScoped<SaldoDeVacacionesService>();

// Consultas propias de la web: agrupan en una sola respuesta lo que una pantalla necesita, para no
// obligar al navegador a encadenar cinco peticiones y armar el resultado por su cuenta.
builder.Services.AddScoped<DashboardQueryService>();
builder.Services.AddScoped<AvisosQueryService>();
builder.Services.AddScoped<DesempenoQueryService>();
builder.Services.AddScoped<SlaCumplimientoQueryService>();
builder.Services.AddScoped<CatalogosQueryService>();
builder.Services.AddScoped<CatalogosService>();
builder.Services.AddScoped<BitacoraQueryService>();
builder.Services.AddScoped<ForoQueryService>();
builder.Services.AddScoped<JornadaQueryService>();
builder.Services.AddScoped<PoolQueryService>();
builder.Services.AddScoped<AutocalificacionQueryService>();
builder.Services.AddScoped<AusenciasService>();

// La única consulta de ausencias que mira lo AJENO: quién más del equipo estará fuera en unas
// fechas. Va aparte de AusenciasService a propósito; el porqué está en su propia clase.
builder.Services.AddScoped<AusenciasDelEquipoService>();
builder.Services.AddScoped<TrabajoQueryService>();
builder.Services.AddScoped<PersonasQueryService>();
builder.Services.AddScoped<MetricasQueryService>();
builder.Services.AddScoped<FichaDeDesarrolladorQueryService>();

// ── Autenticación por cookie ────────────────────────────────────────────────────
//
// Cookie y no JWT: el cliente Blazor lo sirve esta misma API, así que todo es del mismo origen. Con
// HttpOnly el token no es alcanzable desde JavaScript —un XSS no se lleva la sesión— y el sello de
// seguridad permite revocarla de verdad, cosa que un JWT no da sin infraestructura extra.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opciones =>
    {
        opciones.Cookie.Name = "adminweb.sesion";
        opciones.Cookie.HttpOnly = true;
        opciones.Cookie.SameSite = SameSiteMode.Strict;

        // Secure SIEMPRE fuera de desarrollo: sin eso la cookie de sesión podría viajar en claro.
        // En desarrollo se afloja porque ahí la aplicación se levanta en HTTP y una cookie Secure
        // simplemente no se devuelve — el síntoma es que el login «funciona» y la siguiente petición
        // responde 401, que cuesta un buen rato entender.
        opciones.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        opciones.ExpireTimeSpan = TimeSpan.FromHours(8);
        opciones.SlidingExpiration = true;
        opciones.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = ContrasenaObligatoria.ValidarSelloAsync,

            // Sin esto, una petición de la API a una ruta protegida respondería con una redirección
            // a una página de login que no existe; el cliente necesita el código, no un 302 a HTML.
            OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
        };
    });

// ── Autorización: la barrera real ───────────────────────────────────────────────
//
// Estas políticas son el equivalente del PuedeVer del escritorio, pero del lado que cuenta. En el
// cliente el menú se recorta por comodidad; aquí es donde se decide de verdad, porque cualquiera
// puede llamar a la API sin pasar por el navegador. Los servicios conservan además sus
// AuthorizationGuard: dos barreras, como ya estaba dispuesto en el escritorio.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build())
    .AddPolicy("SoloAdmin", p => p.RequireRole(nameof(UserRole.Admin)))
    .AddPolicy("AdminUOperaciones", p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Operaciones)))
    .AddPolicy("AdminUDesarrollador", p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Desarrollador)));

// ── Varios ──────────────────────────────────────────────────────────────────────
// ── Tiempo real ─────────────────────────────────────────────────────────────────
//
// Con una sola instancia el hub en proceso basta y sobra. Si algún día la API escala a más de una,
// hará falta Azure SignalR — es una línea aquí, pero decidirlo entonces es más caro.
builder.Services.AddSignalR();

// Cuántas pestañas tiene abiertas cada quien. SINGLETON porque es estado del proceso —qué sockets
// vivos hay— y es lo que evita que cerrar UNA pestaña dé la jornada por terminada.
builder.Services.AddSingleton<RegistroDeConexiones>();

// Y quién está dentro AHORA MISMO, para el tablero de presencia. NO es una línea de adorno: las
// cuentas de Operaciones ya no abren jornada, así que para ellas no hay fila que consultar y este
// contrato es lo único que las distingue de estar desconectadas. Sin este registro, PresenceService
// se construye igual —el parámetro es opcional para no obligar a las pruebas a doblarlo— y todos
// los operativos salen «Desconectado» para siempre, sin error, sin aviso y sin nada que lo delate
// más que mirar la pantalla. Ver ConexionesEnVivoDelHub.
builder.Services.AddSingleton<IConexionesEnVivo, ConexionesEnVivoDelHub>();

// ── Trabajos de fondo ───────────────────────────────────────────────────────────
//
// Se encienden solo si la configuración lo dice, y por omisión están APAGADOS. No es prudencia
// excesiva: mientras el escritorio siga en producción, sus temporizadores hacen este mismo trabajo,
// y con los dos encendidos a la vez se duplicarían los avisos y, cuando lleguen, los despliegues
// programados. Se encienden en el corte, cuando el escritorio ya se retiró.
if (builder.Configuration.GetValue("AdminWeb:TrabajosDeFondoActivos", false))
{
    builder.Services.AddHostedService<BarridoDePresenciaJob>();
    builder.Services.AddHostedService<ConsolidacionDeCronometrosJob>();

    // Integraciones (fase 4). Aquí está la mejora que justifica moverlas al servidor: en el
    // escritorio, el resumen y la ingesta solo ocurrían si alguien tenía la aplicación abierta.
    builder.Services.AddHostedService<ResumenDiarioJob>();
    builder.Services.AddHostedService<IngestaDeCorreoJob>();

    // El escalamiento de SLA es el caso más claro de la mejora: en el escritorio solo corría si un
    // administrador tenía la aplicación abierta, así que un SLA vencido en fin de semana no se
    // escalaba. Aquí corre siempre.
    builder.Services.AddHostedService<EscalamientoDeSlaJob>();

    // Despliegues programados (fase 5). En el escritorio una cita solo se disparaba si alguien tenía
    // la aplicación abierta a esa hora; si no, se marcaba como perdida y no ocurría nunca.
    builder.Services.AddHostedService<DesplieguesProgramadosJob>();
}

builder.Services.AddExceptionHandler<MapeoExcepciones>();
builder.Services.AddProblemDetails();
// Antiforgery. Protege las subidas MULTIPART, que son las únicas que un sitio ajeno podría provocar
// desde el navegador de alguien con sesión abierta: un formulario suyo puede apuntar aquí y el
// navegador mandaría la cookie. Las peticiones con cuerpo JSON no lo necesitan —el tipo de contenido
// obliga al navegador a preguntar antes (preflight) y no hay política CORS que lo permita—, pero eso
// no cubre los formularios, que existen desde antes que esa regla.
builder.Services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("base-de-datos");

var app = builder.Build();

// El esquema se pone al día al arrancar, bajo candado para que dos instancias no migren a la vez.
// La API es la única dueña del esquema: el escritorio deja de ejecutar DDL a partir del corte.
await PreparacionDeLaBase.PrepararAsync(app.Services, app.Logger);

app.UseExceptionHandler();

// Fuera de desarrollo, HTTPS obligatorio: la cookie de sesión va marcada como Secure y sin TLS
// simplemente no viajaría. En desarrollo se deja en paz porque ahí se levanta en HTTP y redirigir a
// un puerto HTTPS que nadie escucha convierte cualquier petición en un error incomprensible.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Los archivos del cliente Blazor los sirve esta misma aplicación.
app.UseBlazorFrameworkFiles();

// NUESTROS archivos estáticos SE REVALIDAN SIEMPRE, y esto no es una optimización al revés: es lo
// único que evita que un despliegue deje a media plantilla con la hoja de estilos vieja.
//
// El problema es que nuestros archivos NO llevan huella en el nombre: css/tema.css se llama igual
// hoy que mañana, y la fuente de iconos también —cambia de contenido cada vez que se añade un
// icono, conservando el nombre—. Sin ninguna cabecera de caché, el navegador aplica su regla
// heurística y puede darse por bueno un archivo guardado sin volver a preguntar. Ya ocurrió: una
// hoja de estilos vieja dejó la pantalla de acceso con la marca convertida en un cuadrado negro de
// 600 píxeles y el formulario desordenado debajo, en una aplicación cuyo servidor estaba sirviendo
// la versión correcta. Diagnosticarlo desde fuera es carísimo, porque el servidor no tiene la culpa
// y todo lo que se mire ahí sale bien.
//
// «no-cache» NO significa «no guardes»: significa «guárdalo, pero pregunta antes de usarlo». La
// respuesta normal es un 304 sin cuerpo, así que el coste es un viaje de ida y vuelta por archivo
// y no la descarga. A cambio, un despliegue se ve en la siguiente recarga, sin pedirle a nadie que
// vacíe la caché ni que sepa qué es Ctrl+F5.
//
// Lo de _framework/ NO pasa por aquí y no hace falta tocarlo: esos nombres SÍ llevan huella y los
// gestiona UseBlazorFrameworkFiles, que ya los marca como inmutables. Ahí cachear para siempre es
// correcto, porque un archivo distinto tiene un nombre distinto.
// LA FUENTE DE ICONOS DE RADZEN NO SE SIRVE NUNCA: se responde la nuestra en su lugar.
//
// Radzen empaqueta «MaterialSymbolsOutlined.woff2», que pesa 3 MB. El tema ya no la usa —los iconos
// salen de nuestro recorte de Material Symbols Sharp, 150 KB— pero el navegador la descargaba
// IGUAL en cada primera carga. Se midió y se persiguió: la variable apunta a la nuestra, ninguna
// regla de Radzen nombra la vieja salvo su propio @font-face, no queda ni un elemento en la página
// cuya familia calculada sea ésa, y redeclarar la familia apuntando a nuestro archivo tampoco lo
// evitó. El iniciador que reporta el navegador es el PARSER de material-base.css, así que la pide
// al leer la hoja y no al pintar nada.
//
// Perseguirlo más cuesta más de lo que vale, y esto lo zanja sin depender de por qué: se reescribe
// la ruta antes de que los archivos estáticos la atiendan, así que quien pida la de Radzen recibe
// la nuestra. Son 3 MB menos en cada primera carga.
//
// Y si algún día un componente de Radzen pide un icono por esa vía, saldrá con el mismo trazo que
// el resto: los nombres de las ligaduras son los mismos en las dos variantes. Antes habrían
// convivido dos juegos de iconos distintos en la misma pantalla sin que nadie lo notara.
app.Use(async (contexto, siguiente) =>
{
    if (contexto.Request.Path.Equals("/_content/Radzen.Blazor/fonts/MaterialSymbolsOutlined.woff2",
                                     StringComparison.OrdinalIgnoreCase))
        contexto.Request.Path = "/fuentes/MaterialSymbolsSharp.woff2";

    await siguiente();
});

var archivosQueSeRevalidan = new StaticFileOptions
{
    OnPrepareResponse = contexto =>
        contexto.Context.Response.Headers.CacheControl = "no-cache, must-revalidate"
};
app.UseStaticFiles(archivosQueSeRevalidan);

app.UseAuthentication();
app.UseAuthorization();
app.UseContrasenaObligatoria();   // después de autenticar: necesita los claims ya leídos

// Sin esta línea, el AddAntiforgery de arriba no valida nada: queda una configuración que PARECE
// protección y no lo es, que es peor que no tenerla. Va después de autenticar porque el testigo se
// ata a la identidad de la sesión.
app.UseAntiforgery();

app.MapAuthEndpoints();
app.MapPreferenciasEndpoints();

// Pantallas de consulta (fase 1).
app.MapDashboardEndpoints();
app.MapAvisosEndpoints();
app.MapDesempenoEndpoints();
app.MapCumplimientoSlaEndpoints();
app.MapCatalogosEndpoints();
app.MapPlantillasEndpoints();
app.MapBitacoraEndpoints();
app.MapForoEndpoints();
app.MapAdjuntosEndpoints();

// Autoservicio del desarrollador (fase 2).
app.MapJornadaEndpoints();
app.MapPoolEndpoints();
app.MapAutocalificacionEndpoints();
app.MapAusenciasEndpoints();
app.MapAusenciasDelEquipoEndpoints();
app.MapSugerenciasEndpoints();

// Administración (fase 3).
app.MapTrabajoEndpoints();
app.MapPersonasEndpoints();
app.MapAdministracionEndpoints();
app.MapNotasEndpoints();
app.MapBusquedaEndpoints();
app.MapReportesEndpoints();
app.MapAusenciasLiderEndpoints();
app.MapEvaluacionesEndpoints();
app.MapMetricasEndpoints();

// Integraciones (fase 4).
app.MapFreshdeskEndpoints();
app.MapCorreoEndpoints();
app.MapSlaEndpoints();
app.MapDevOpsEndpoints();

// Despliegues (fase 5).
app.MapDesplieguesEndpoints();
app.MapProgramadosEndpoints();

app.MapHub<AppHub>(RutasDeTiempoReal.Hub);
app.MapHealthChecks("/api/health").AllowAnonymous();

// La versión que está corriendo, sin sesión: es lo primero que hay que poder preguntar cuando algo
// va mal, y exigir una cookie para saberlo lo haría inútil justo cuando el acceso es lo que falla.
// No revela nada: el número de versión no es un secreto y el SHA apunta a un repositorio privado.
app.MapGet("/api/version", () => Results.Ok(new
{
    version = VersionDeLaAplicacion.Corta,
    commit = VersionDeLaAplicacion.Commit,
    completa = VersionDeLaAplicacion.Completa
}))
.AllowAnonymous()
.WithTags("Estado")
.WithSummary("Qué versión de la aplicación está desplegada");

// Cualquier ruta que no sea de la API la resuelve el enrutador de Blazor: es lo que hace que
// recargar el navegador sobre una pantalla concreta (o compartir su enlace) funcione.
// Con LAS MISMAS opciones, y es el archivo donde más importa: index.html no se sirve por
// UseStaticFiles sino por aquí —no hay UseDefaultFiles—, así que sin pasarlas se quedaría fuera
// precisamente el archivo del que cuelgan los enlaces a todos los demás. Un index.html viejo en
// caché no solo trae estilos viejos: puede no mencionar siquiera una hoja añadida después, y
// entonces no hay recarga normal que la traiga.
app.MapFallbackToFile("index.html", archivosQueSeRevalidan).AllowAnonymous();

app.Run();

/// <summary>Punto de entrada visible para las pruebas de integración (WebApplicationFactory).</summary>
public partial class Program;
