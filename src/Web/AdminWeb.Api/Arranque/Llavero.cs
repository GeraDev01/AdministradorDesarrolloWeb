// Azure.Core 1.55 absorbió DefaultAzureCredential, que Azure.Identity también define: con los dos
// ensamblados en el árbol el compilador se niega a elegir (CS0433). El alias, declarado en el .csproj,
// dice explícitamente de cuál se quiere y deja al resto de la solución usando Azure.Core sin cambios.
extern alias azidentidad;

using azidentidad::Azure.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace AdminWeb.Api.Arranque;

/// <summary>
/// Dónde viven las llaves con las que se cifran los secretos por usuario (hoy, el PAT personal de
/// Azure DevOps).
///
/// <para><b>El problema que resuelve.</b> <c>AddDataProtection()</c> a secas guarda el llavero en el
/// sistema de archivos que encuentre. En un contenedor eso es disco EFÍMERO: cada reinicio inventa
/// llaves nuevas y todo lo cifrado antes deja de poder descifrarse. Y con dos instancias detrás de
/// un balanceador es peor, porque cada una tiene las suyas: lo que cifra una no lo lee la otra, así
/// que el fallo aparece y desaparece según a cuál te toque. El síntoma no es un error claro sino un
/// «tu token no sirve» al hablar con DevOps, que manda a mirar exactamente al sitio equivocado.</para>
///
/// <para><b>Por qué es configuración y no una decisión fija.</b> En Azure el llavero va en Blob y se
/// protege con una llave de Key Vault; en la máquina de alguien, en docker-compose y en las pruebas
/// no hay ni Blob ni Key Vault, y exigirlos dejaría la aplicación sin arrancar en los tres sitios
/// donde más se levanta. Así que se declara por configuración y, sin configurar, se cae a una
/// carpeta local — que en esos tres casos es exactamente lo correcto.</para>
///
/// <para><b>Se avisa en el registro de en qué modo quedó.</b> Un llavero mal configurado se comporta
/// igual que uno bien configurado hasta el primer reinicio, que puede ser semanas después; sin esta
/// línea nadie se entera hasta que alguien pierde su token.</para>
/// </summary>
public static class Llavero
{
    /// <summary>Sección de configuración: <c>AdminWeb:Llavero:Blob</c> y <c>:LlaveDeKeyVault</c>.</summary>
    public const string Seccion = "AdminWeb:Llavero";

    public static void Configurar(WebApplicationBuilder builder)
    {
        var proteccion = builder.Services.AddDataProtection()
            // El nombre fija el «propósito» del cifrado. Tiene que ser el MISMO en todas las
            // instancias o, aun compartiendo llavero, una no leería lo que cifró la otra.
            .SetApplicationName("AdminWeb");

        var blob = builder.Configuration[$"{Seccion}:Blob"];
        var llaveDeKeyVault = builder.Configuration[$"{Seccion}:LlaveDeKeyVault"];
        var carpeta = builder.Configuration[$"{Seccion}:Carpeta"];

        // La identidad administrada del App Service. No lleva credenciales en ningún sitio, que es
        // justo lo que se busca: una cadena de conexión al Blob del llavero sería un secreto que
        // protege a los secretos, y habría que guardarla en alguna parte.
        var identidad = new DefaultAzureCredential();

        if (!string.IsNullOrWhiteSpace(blob))
        {
            proteccion.PersistKeysToAzureBlobStorage(new Uri(blob), identidad);

            if (!string.IsNullOrWhiteSpace(llaveDeKeyVault))
                proteccion.ProtectKeysWithAzureKeyVault(new Uri(llaveDeKeyVault), identidad);

            Anunciar(builder,
                llaveDeKeyVault is { Length: > 0 }
                    ? "Llavero de Data Protection en Blob, protegido con Key Vault."
                    : "Llavero de Data Protection en Blob, SIN protección de Key Vault: las llaves " +
                      "quedan legibles para quien pueda leer ese contenedor. Configura " +
                      $"{Seccion}:LlaveDeKeyVault antes de producción.");
            return;
        }

        // Sin Blob: una carpeta. Se PRUEBA cuál se puede escribir en vez de suponerlo.
        //
        // La imagen del contenedor corre como usuario sin privilegios, así que el directorio de la
        // aplicación es de solo lectura: dar por hecho que se puede crear una carpeta ahí tumbaba el
        // arranque entero con «Access to the path '/aplicacion/llavero' is denied». Y era un fallo
        // silencioso hasta que se levantaba en Linux, porque en Windows esa carpeta sí se escribe.
        var candidatas = string.IsNullOrWhiteSpace(carpeta)
            ? new[] { Path.Combine(builder.Environment.ContentRootPath, "llavero"),
                      Path.Combine(Path.GetTempPath(), "adminweb-llavero") }
            : [carpeta];

        var ruta = candidatas.FirstOrDefault(SePuedeEscribir);

        if (ruta is not null)
        {
            proteccion.PersistKeysToFileSystem(new DirectoryInfo(ruta));
            Anunciar(builder,
                $"Llavero de Data Protection en la carpeta «{ruta}». Vale para desarrollo y para las " +
                $"pruebas; en Azure hay que configurar {Seccion}:Blob o cada reinicio del " +
                "contenedor invalidará todos los secretos por usuario ya cifrados.");
            return;
        }

        // Ni una carpeta escribible. Se sigue con el llavero por omisión —efímero— y se dice bien
        // claro, porque el síntoma llega tarde y disfrazado: los PAT dejan de descifrarse tras el
        // primer reinicio y parece que el token de DevOps caducó. Lo que NO se hace es reventar: el
        // resto de la aplicación funciona perfectamente sin secretos por usuario.
        Anunciar(builder,
            "AVISO: no hay ninguna carpeta escribible para el llavero de Data Protection " +
            $"(se intentó: {string.Join(", ", candidatas)}). Las llaves serán EFÍMERAS y los PAT " +
            $"personales dejarán de poder descifrarse en el próximo reinicio. Configura " +
            $"{Seccion}:Carpeta apuntando a un volumen con permisos, o {Seccion}:Blob en Azure.",
            aviso: true);
    }

    /// <summary>
    /// Si de verdad se puede escribir ahí. Se comprueba CREANDO y borrando un archivo, no mirando
    /// si el directorio existe: en un contenedor una carpeta puede existir y no ser escribible, que
    /// es justo el caso que tumbaba el arranque.
    /// </summary>
    private static bool SePuedeEscribir(string ruta)
    {
        try
        {
            Directory.CreateDirectory(ruta);
            var prueba = Path.Combine(ruta, $".escritura-{Guid.NewGuid():N}");
            File.WriteAllText(prueba, "");
            File.Delete(prueba);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deja dicho en el registro en qué modo quedó el llavero.
    ///
    /// Se escribe con un registrador propio y no con el de la aplicación porque esto ocurre ANTES de
    /// que exista el host: no hay todavía nada de donde sacar un ILogger.
    /// </summary>
    private static void Anunciar(WebApplicationBuilder builder, string mensaje, bool aviso = false)
    {
        using var fabrica = LoggerFactory.Create(b => b.AddConfiguration(
            builder.Configuration.GetSection("Logging")).AddConsole());
        var registro = fabrica.CreateLogger(nameof(Llavero));

        // Un llavero efímero se anuncia como AVISO y no como información: en un registro de arranque
        // con cientos de líneas, una más en gris no la lee nadie.
        if (aviso) registro.LogWarning("{Mensaje}", mensaje);
        else registro.LogInformation("{Mensaje}", mensaje);
    }
}
