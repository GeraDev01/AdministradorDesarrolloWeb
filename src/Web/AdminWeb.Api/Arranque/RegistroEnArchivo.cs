using Serilog;
using Serilog.Events;

namespace AdminWeb.Api.Arranque;

/// <summary>
/// El registro en ARCHIVO, con rotación diaria y retención, ADEMÁS de la salida estándar.
///
/// <para><b>Por qué hace falta.</b> El escritorio guardaba un archivo por día con treinta de
/// retención (<c>LoggingSetup</c>) y la web se quedó solo con la salida estándar. Es defendible
/// mientras la plataforma la recoja y la guarde, pero el día que haya que responder «¿qué pasó
/// anteayer a las seis?» esa respuesta depende de una configuración de hospedaje que puede no estar
/// puesta — y para entonces ya es tarde para ponerla.</para>
///
/// <para><b>Se añade como PROVEEDOR, no como sustituto.</b> La salida estándar sigue intacta: es la
/// que lee <c>docker logs</c> y la que recoge Application Insights. Escribir solo en archivo dejaría
/// ciego justo al sitio donde se mira primero.</para>
///
/// <para><b>Dónde escribe.</b> En App Service Linux, <c>/home</c> es el único almacenamiento
/// persistente y compartido; el resto del sistema de archivos es efímero, así que un log fuera de
/// ahí desaparece con el contenedor. En cualquier otro sitio se cae a una carpeta dentro del
/// directorio de la aplicación. Se puede fijar con <c>AdminWeb:Registro:Carpeta</c>.</para>
///
/// <para><b>El nombre lleva el identificador de la instancia.</b> Con dos instancias detrás de un
/// balanceador, las dos escribirían el mismo archivo del mismo recurso compartido y se pisarían.</para>
/// </summary>
public static class RegistroEnArchivo
{
    public const string Seccion = "AdminWeb:Registro";

    public static void Configurar(WebApplicationBuilder builder)
    {
        // Apagable a propósito: en las pruebas y en algunos contenedores no hay dónde escribir, y un
        // registro que no arranca no puede impedir que arranque la aplicación.
        if (builder.Configuration.GetValue($"{Seccion}:EnArchivo", true) is false) return;

        var carpeta = builder.Configuration[$"{Seccion}:Carpeta"];
        if (string.IsNullOrWhiteSpace(carpeta))
        {
            // Se detecta App Service por su VARIABLE DE ENTORNO, no por si existe /home: esa carpeta
            // existe en casi cualquier imagen de Linux y ahí NO se puede escribir, así que mirarla
            // llevaba a intentar el sitio equivocado en todos los contenedores que no son App Service.
            bool enAppService = !string.IsNullOrEmpty(
                Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));

            carpeta = enAppService
                ? "/home/LogFiles/adminweb"          // lo único persistente en App Service Linux
                : Path.Combine(Path.GetTempPath(), "adminweb-logs");
        }

        try
        {
            Directory.CreateDirectory(carpeta);

            // El identificador de instancia lo pone App Service; en local no existe y basta con el
            // del proceso para que dos ejecuciones a la vez no compartan archivo.
            var instancia = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID")
                            ?? Environment.ProcessId.ToString();

            var registro = new LoggerConfiguration()
                .MinimumLevel.Information()
                // EF Core cuenta CADA consulta en Information. En el arranque eso son cientos de
                // sentencias del migrador, y con ellas dentro el archivo del día deja de servir para
                // encontrar nada. Se sube a Warning solo para él.
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .WriteTo.File(
                    Path.Combine(carpeta, $"adminweb-{instancia}-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    // Tope por archivo con rollOnFileSizeLimit: sin él, un fallo que se repite en
                    // bucle llena el disco compartido de /home y se lleva por delante a la
                    // aplicación entera, no solo al registro.
                    fileSizeLimitBytes: 32 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    outputTemplate:
                        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            builder.Logging.AddSerilog(registro, dispose: true);
        }
        catch (Exception ex)
        {
            // Sin permisos, sin disco o con la ruta mal: se dice y se sigue. Que no se pueda escribir
            // un archivo de registro no es motivo para dejar al equipo sin aplicación.
            using var fabrica = LoggerFactory.Create(b => b.AddConsole());
            fabrica.CreateLogger(nameof(RegistroEnArchivo)).LogWarning(
                ex, "No se pudo abrir el registro en archivo en «{Carpeta}». Sigue solo la salida estándar.",
                carpeta);
        }
    }
}
