// Azure.Core 1.55 absorbió DefaultAzureCredential, que Azure.Identity también define: con los dos
// ensamblados en el árbol el compilador se niega a elegir (CS0433). El alias, declarado en el .csproj,
// dice explícitamente de cuál se quiere y deja al resto de la solución usando Azure.Core sin cambios.
extern alias azidentidad;

using System.Security.Cryptography.X509Certificates;
using azidentidad::Azure.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace AdminWeb.Api.Arranque;

/// <summary>
/// Dónde viven las llaves con las que se cifran los secretos por usuario (hoy, el PAT personal de
/// Azure DevOps) y con qué se cifran ellas mismas.
///
/// <para><b>El problema que resuelve.</b> <c>AddDataProtection()</c> a secas guarda el llavero en el
/// sistema de archivos que encuentre. En un contenedor eso es disco EFÍMERO: cada reinicio inventa
/// llaves nuevas y todo lo cifrado antes deja de poder descifrarse. Y con dos instancias detrás de
/// un balanceador es peor, porque cada una tiene las suyas: lo que cifra una no lo lee la otra, así
/// que el fallo aparece y desaparece según a cuál te toque. El síntoma no es un error claro sino un
/// «tu token no sirve» al hablar con DevOps, que manda a mirar exactamente al sitio equivocado.</para>
///
/// <para><b>Por qué es configuración y no una decisión fija.</b> En Azure el llavero va en Blob; en la
/// máquina de alguien, en docker-compose y en las pruebas no hay Blob, y exigirlo dejaría la
/// aplicación sin arrancar en los tres sitios donde más se levanta. Así que se declara por
/// configuración y, sin configurar, se cae a una carpeta local — que en esos tres casos es
/// exactamente lo correcto.</para>
///
/// <para><b>Y el llavero, ¿quién lo protege a él?</b> Un contenedor de Blob es legible para cualquiera
/// con permiso de lectura sobre él, así que el llavero puesto ahí en claro no protege nada: quien lo
/// lea descifra los PAT de todo el equipo. Hay dos formas de cifrarlo y aquí se admiten las dos, con
/// una precedencia deliberada:
/// <list type="number">
///   <item><b>Key Vault</b> (<c>:LlaveDeKeyVault</c>) si está configurado. Gana siempre porque la
///   llave privada nunca sale del vault y porque no tiene fecha de caducidad que vigilar.</item>
///   <item><b>Un certificado</b> (<c>:Certificado</c>, por huella) si no hay Key Vault. Es lo que
///   se usa aquí: esta suscripción no tiene Key Vault.</item>
/// </list>
/// Nunca se aplican los dos a la vez. Llamar a <c>ProtectKeysWith…</c> dos veces NO encadena nada:
/// cada llamada pisa el <c>XmlEncryptor</c> de la anterior y manda la última, en silencio y según el
/// orden del código. Por eso se elige uno explícitamente y se avisa si están los dos puestos.</para>
///
/// <para><b>Los certificados caducan, y ahí está el peligro de verdad.</b> Cifrar con certificado solo
/// afecta a las llaves NUEVAS; las que ya estaban cifradas siguen necesitando el certificado con el
/// que se cifraron para poder leerse. Quien rote el certificado y borre el viejo deja el llavero
/// entero ilegible y pierde TODOS los PAT — sin error, y con el mismo síntoma disfrazado de siempre.
/// Por eso la configuración distingue el certificado ACTUAL (el que cifra) de una LISTA de
/// ANTERIORES (que solo descifran): rotar es mover una huella de la primera a la segunda, y no
/// borrar nada hasta que ya no quede nada cifrado con ella. El procedimiento escrito está en
/// GUIA-DE-PUESTA-EN-MARCHA.md.</para>
///
/// <para><b>Se avisa en el registro de en qué modo quedó.</b> Un llavero mal configurado se comporta
/// igual que uno bien configurado hasta el primer reinicio, que puede ser semanas después; sin esta
/// línea nadie se entera hasta que alguien pierde su token.</para>
///
/// <para><b>La excepción a «avisar y seguir».</b> Todo lo demás de este archivo degrada avisando,
/// porque la aplicación entera funciona perfectamente sin secretos por usuario y tumbarla por eso
/// sería peor que el daño. Falta el certificado con el que hay que CIFRAR es el único caso que sí
/// tumba el arranque, y por tres razones que no se dan en los otros:
/// <list type="bullet">
///   <item>No es la ausencia de una configuración, es la <b>regresión de una que ya existía</b>. Que
///   haya una huella puesta demuestra que ahí fuera hay un llavero ya cifrado con ese certificado.
///   Seguir no significa «todavía sin cifrar»: significa escribir llaves nuevas EN CLARO al lado de
///   otras que ya no se pueden abrir. Se pierde lo viejo y se expone lo nuevo, de una vez.</item>
///   <item><b>Mentiría sobre la seguridad.</b> Los otros modos son honestos: una carpeta se anuncia
///   como carpeta. Aquí la configuración dice «cifrado», el despliegue no lo está, y no hay ningún
///   momento posterior en el que eso salga a la luz — que es justo lo que se quería evitar al elegir
///   el certificado.</item>
///   <item><b>Nadie se queda fuera por esto.</b> El pipeline publica a la ranura de ensayo y pide
///   <c>/api/health</c> antes de intercambiar: si no arranca, el intercambio no ocurre y producción
///   sigue en la instancia anterior, que funciona. El coste real es un despliegue que no pasa, con
///   el mensaje delante de quien lo lanzó — la forma más barata que hay de enterarse.</item>
/// </list>
/// Que falte un certificado ANTERIOR no tumba nada, y la diferencia es exactamente ésta: <b>el caso
/// mortal es evitable y éste ya ocurrió</b>. Negarse a arrancar por el certificado que cifra impide un
/// daño que todavía no se ha hecho; negarse por uno anterior no devuelve nada —lo cifrado con él ya
/// es ilegible— y solo añade una caída encima. Además el actual está, así que lo nuevo se cifra y la
/// promesa se sigue cumpliendo. Se avisa bien fuerte, con la huella, y se arranca.</para>
/// </summary>
public static class Llavero
{
    /// <summary>Sección de configuración: <c>AdminWeb:Llavero</c>.</summary>
    public const string Seccion = "AdminWeb:Llavero";

    /// <summary>Con qué se cifra el llavero antes de escribirlo.</summary>
    public enum ModoDeCifrado
    {
        /// <summary>Con nada. Correcto en una carpeta local; en Blob deja las llaves legibles.</summary>
        Ninguno,

        /// <summary>Con una llave de Key Vault. La privada no sale del vault.</summary>
        KeyVault,

        /// <summary>Con un certificado del almacén del usuario actual, identificado por su huella.</summary>
        Certificado
    }

    /// <summary>
    /// Las huellas ya leídas y limpias: la del certificado que CIFRA y las de los que además pueden
    /// DESCIFRAR. Es un tipo aparte, y público, para que se pueda comprobar en pruebas qué sale de
    /// una configuración dada sin tener que instalar certificados en la máquina de nadie.
    /// </summary>
    /// <param name="Actual">Huella del certificado con el que se cifra. <c>null</c> = no hay.</param>
    /// <param name="Anteriores">Huellas que solo descifran. Sin repetidos y sin la actual.</param>
    public sealed record HuellasDelLlavero(string? Actual, IReadOnlyList<string> Anteriores);

    /// <summary>
    /// Lo que se va a hacer, ya resuelto contra el almacén y antes de tocar Data Protection.
    /// </summary>
    /// <param name="Cifrado">Qué protegerá las llaves nuevas.</param>
    /// <param name="ParaCifrar">El certificado que cifra, si el modo es <see cref="ModoDeCifrado.Certificado"/>.</param>
    /// <param name="ParaDescifrar">Todos los certificados que aparecieron y sirven para leer.</param>
    /// <param name="Avisos">Lo que hay que decir en el registro. Vacío cuando todo está en su sitio.</param>
    public sealed record PlanDelLlavero(
        ModoDeCifrado Cifrado,
        X509Certificate2? ParaCifrar,
        IReadOnlyList<X509Certificate2> ParaDescifrar,
        IReadOnlyList<string> Avisos);

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

        // CÓMO se cifra se decide antes que DÓNDE se guarda, y se aplica valga cual valga el destino:
        // proteger las llaves es independiente de en qué disco acaben. Es también lo que permite
        // ensayar la rotación en local, con el llavero en una carpeta, sin montar nada en Azure.
        var plan = Planificar(llaveDeKeyVault, LeerHuellas(builder.Configuration), BuscarPorHuella);

        foreach (var aviso in plan.Avisos) Anunciar(builder, aviso, aviso: true);

        // El certificado ACTUAL entra también en la lista de descifrado, junto con los anteriores.
        // Sin esta llamada Data Protection acabaría encontrándolo igual, resolviendo por su cuenta la
        // huella que viene escrita en el XML de cada llave; se pone explícito para que leer no dependa
        // de una segunda búsqueda implícita en el almacén, que es una pieza más que puede fallar
        // meses después y en otro sitio.
        if (plan.ParaDescifrar.Count > 0)
            proteccion.UnprotectKeysWithAnyCertificate([.. plan.ParaDescifrar]);

        switch (plan.Cifrado)
        {
            case ModoDeCifrado.KeyVault:
                proteccion.ProtectKeysWithAzureKeyVault(new Uri(llaveDeKeyVault!), identidad);
                break;
            case ModoDeCifrado.Certificado:
                proteccion.ProtectKeysWithCertificate(plan.ParaCifrar!);
                break;
        }

        var comoSeProtege = plan.Cifrado switch
        {
            ModoDeCifrado.KeyVault => "protegido con Key Vault",
            ModoDeCifrado.Certificado =>
                $"cifrado con el certificado {plan.ParaCifrar!.Thumbprint} (caduca el " +
                $"{plan.ParaCifrar!.NotAfter:yyyy-MM-dd}); {plan.ParaDescifrar.Count - 1} certificado(s) " +
                "anterior(es) disponibles para descifrar",
            _ => "SIN cifrar"
        };

        if (!string.IsNullOrWhiteSpace(blob))
        {
            proteccion.PersistKeysToAzureBlobStorage(new Uri(blob), identidad);

            var sinCifrar = plan.Cifrado == ModoDeCifrado.Ninguno;
            Anunciar(builder,
                $"Llavero de Data Protection en Blob, {comoSeProtege}." + (sinCifrar
                    ? " Las llaves quedan legibles para quien pueda leer ese contenedor. Configura " +
                      $"{Seccion}:Certificado con la huella del certificado antes de producción."
                    : ""),
                // Un Blob sin cifrar es un aviso, no una nota: el contenedor es exactamente el sitio
                // donde no vale dejarlas en claro, y en un arranque de cientos de líneas una en gris
                // no la lee nadie.
                aviso: sinCifrar);
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
                "contenedor invalidará todos los secretos por usuario ya cifrados." +
                (plan.Cifrado == ModoDeCifrado.Ninguno ? "" : $" El llavero va {comoSeProtege}."));
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
    /// Lee <c>:Certificado</c> y <c>:CertificadosAnteriores</c> y los deja normalizados.
    ///
    /// <para>La lista de anteriores se admite de dos maneras porque las dos aparecen de verdad: como
    /// arreglo (<c>…__CertificadosAnteriores__0</c>, que es lo que sale de un appsettings) y como una
    /// sola cadena separada por comas o punto y coma, que es lo único cómodo de teclear en la
    /// configuración plana del App Service. Si vinieran las dos, manda la cadena: es la que alguien
    /// acaba de escribir a mano.</para>
    /// </summary>
    public static HuellasDelLlavero LeerHuellas(IConfiguration configuracion)
    {
        var actual = Normalizar(configuracion[$"{Seccion}:Certificado"]);

        var anteriores = new List<string>();
        foreach (var cruda in AnterioresCrudas(configuracion))
        {
            var huella = Normalizar(cruda);

            // Se quitan los repetidos y la propia actual. No es cosmética: cada huella de la lista se
            // busca en el almacén y acaba en UnprotectKeysWithAnyCertificate, así que repetirla
            // significa cargar el mismo certificado dos veces y —peor— que el aviso de «no aparece»
            // salga duplicado y parezca que faltan dos certificados distintos.
            if (huella is null || huella == actual || anteriores.Contains(huella, StringComparer.Ordinal))
                continue;

            anteriores.Add(huella);
        }

        return new HuellasDelLlavero(actual, anteriores);
    }

    /// <summary>
    /// Decide qué se va a usar para cifrar y con qué se va a poder descifrar, resolviendo cada huella
    /// contra el almacén.
    ///
    /// <para>La búsqueda entra como parámetro y no se hace aquí dentro por una razón práctica: así
    /// las decisiones —cuál gana, qué se descarta, qué tumba el arranque y qué solo se avisa— se
    /// pueden probar de verdad, con certificados fabricados en memoria, sin instalar nada en el
    /// almacén de quien corra las pruebas.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Cuando la configuración pide cifrar con un certificado que no se puede usar. Está razonado en
    /// la cabecera de la clase: es el único caso en que este archivo prefiere no arrancar.
    /// </exception>
    public static PlanDelLlavero Planificar(
        string? llaveDeKeyVault,
        HuellasDelLlavero huellas,
        Func<string, X509Certificate2?> buscarPorHuella)
    {
        var hayKeyVault = !string.IsNullOrWhiteSpace(llaveDeKeyVault);
        var avisos = new List<string>();

        // Sin ninguna huella configurada no se toca el almacén siquiera. Es el camino de la máquina
        // de alguien, de docker-compose y de las pruebas, y tiene que seguir comportándose como si
        // nada de esto existiera.
        if (huellas.Actual is null && huellas.Anteriores.Count == 0)
            return new PlanDelLlavero(
                hayKeyVault ? ModoDeCifrado.KeyVault : ModoDeCifrado.Ninguno, null, [], avisos);

        // Anteriores sin actual y sin Key Vault: nadie cifraría lo nuevo y lo viejo seguiría cifrado.
        // Suele ser alguien que quitó la huella actual creyendo que así «se desactiva el cifrado».
        // No se desactiva: se parte el llavero en dos mitades que ya no se hablan.
        if (huellas.Actual is null && !hayKeyVault)
            throw new InvalidOperationException(
                $"{Seccion}:CertificadosAnteriores tiene huellas pero {Seccion}:Certificado está " +
                "vacío y tampoco hay Key Vault. Eso no desactiva el cifrado del llavero: dejaría las " +
                "llaves nuevas SIN cifrar junto a las viejas —ya cifradas— y ninguna de las dos " +
                $"mitades serviría para lo que se espera. Pon en {Seccion}:Certificado la huella del " +
                "certificado con el que se debe cifrar, o vacía también la lista de anteriores si de " +
                "verdad quieres el llavero en claro.");

        X509Certificate2? paraCifrar = null;
        var paraDescifrar = new List<X509Certificate2>();

        if (huellas.Actual is not null)
        {
            // Si hay Key Vault, quien cifra es el vault y el certificado «actual» pasa a ser uno más
            // de los que solo descifran. Es la migración de certificado a Key Vault, y funciona por el
            // mismo mecanismo que la rotación entre certificados.
            var esQuienCifra = !hayKeyVault;
            var certificado = buscarPorHuella(huellas.Actual);

            if (certificado is null)
            {
                if (esQuienCifra) throw new InvalidOperationException(NoAparece(huellas.Actual));
                avisos.Add($"El certificado {huellas.Actual} ({Seccion}:Certificado) no aparece. Quien " +
                           "cifra es Key Vault, así que el arranque sigue y lo nuevo queda protegido; " +
                           "pero lo que se cifró con ese certificado no se podrá descifrar mientras " +
                           "no vuelva a estar." + PistaDeAppService());
            }
            else if (!certificado.HasPrivateKey)
            {
                // Cifrar solo necesita la parte pública, así que sin esta comprobación todo iría bien
                // hasta el siguiente reinicio: ahí habría que leer lo cifrado, haría falta la privada
                // y no estaría. El fallo llegaría días después y sin relación aparente con este cambio.
                if (esQuienCifra) throw new InvalidOperationException(SinLlavePrivada(huellas.Actual));
                avisos.Add($"El certificado {huellas.Actual} ({Seccion}:Certificado) está sin llave " +
                           "privada y no sirve para descifrar. Se ignora.");
            }
            else
            {
                paraDescifrar.Add(certificado);

                if (esQuienCifra)
                {
                    paraCifrar = certificado;
                    var caducidad = SobreLaCaducidad(certificado);
                    if (caducidad is not null) avisos.Add(caducidad);
                }
            }
        }

        foreach (var huella in huellas.Anteriores)
        {
            var certificado = buscarPorHuella(huella);

            // Que un anterior no aparezca NO tumba el arranque —está razonado en la cabecera—, pero se
            // dice con nombre y apellidos: es el único momento en que alguien puede darse cuenta antes
            // de que a un compañero le deje de funcionar el PAT sin explicación.
            if (certificado is null)
            {
                avisos.Add($"El certificado ANTERIOR {huella} ({Seccion}:CertificadosAnteriores) no " +
                           "aparece. Las llaves del llavero cifradas con él son ILEGIBLES, y los PAT " +
                           "que dependieran de ellas se perdieron: hay que recapturarlos en «Mis " +
                           "tickets DevOps». Vuelve a subirlo si lo tienes; si ya no existe, no hay " +
                           "nada que recuperar y lo único sensato es quitar su huella de la lista para " +
                           "que este aviso no se repita para siempre." + PistaDeAppService());
                continue;
            }

            if (!certificado.HasPrivateKey)
            {
                avisos.Add($"El certificado ANTERIOR {huella} está en el almacén pero sin llave " +
                           "privada, así que no puede descifrar nada. Súbelo con su llave privada " +
                           "(un .pfx, no un .cer).");
                continue;
            }

            // Aquí NO se mira la caducidad a propósito: un anterior caducado es lo normal y es
            // precisamente para lo que existe la lista. Filtrarlo rompería la rotación en silencio.
            paraDescifrar.Add(certificado);
        }

        if (hayKeyVault && huellas.Actual is not null)
            avisos.Add($"Hay Key Vault y certificado configurados a la vez ({Seccion}:LlaveDeKeyVault " +
                       $"y {Seccion}:Certificado). Para CIFRAR gana Key Vault; los certificados se " +
                       "conservan solo para descifrar lo que ya estaba. Si no es lo que quieres, " +
                       "quita uno de los dos: aplicar ambos no es posible.");

        var modo = hayKeyVault ? ModoDeCifrado.KeyVault
                 : paraCifrar is not null ? ModoDeCifrado.Certificado
                 : ModoDeCifrado.Ninguno;

        return new PlanDelLlavero(modo, paraCifrar, paraDescifrar, avisos);
    }

    /// <summary>
    /// Deja la huella como la espera el almacén: sin separadores y en mayúsculas.
    ///
    /// <para>No es remilgo. Copiada del diálogo de certificados de Windows, una huella llega con
    /// espacios cada dos dígitos y con una marca de dirección invisible (U+200E) pegada delante que
    /// no se ve en ningún editor; de otras herramientas llega con dos puntos entre pares. Cualquiera
    /// de las tres hace que la búsqueda no encuentre nada y que el mensaje resultante sea «no está el
    /// certificado» cuando el certificado sí está. Se limpia aquí, una vez, y no se piensa más.</para>
    ///
    /// <para>Lo que NO se hace es validar que lo que queda sea hexadecimal y descartarlo si no: un
    /// valor con errata desaparecería y el llavero acabaría sin cifrar, que es el peor final posible.
    /// Se deja pasar tal cual, no se encuentra, y el mensaje de arranque señala la errata.</para>
    /// </summary>
    private static string? Normalizar(string? valor)
    {
        if (valor is null) return null;

        // Las dos marcas van escritas con su código y no con el carácter: son INVISIBLES, y pegadas
        // aquí tal cual el siguiente que lea esta línea vería dos comillas con nada en medio.
        var limpio = new string(valor.Where(c =>
            !char.IsWhiteSpace(c) && c != ':' && c != '-' && c != '\u200E' && c != '\u200F').ToArray());

        return limpio.Length == 0 ? null : limpio.ToUpperInvariant();
    }

    private static IEnumerable<string?> AnterioresCrudas(IConfiguration configuracion)
    {
        var enUnaLinea = configuracion[$"{Seccion}:CertificadosAnteriores"];

        if (!string.IsNullOrWhiteSpace(enUnaLinea))
            return enUnaLinea.Split([',', ';', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return configuracion.GetSection($"{Seccion}:CertificadosAnteriores")
                            .GetChildren()
                            .Select(hijo => hijo.Value);
    }

    /// <summary>
    /// Busca un certificado por huella donde la plataforma lo deja.
    ///
    /// <para><b>En el almacén del usuario actual</b>, que es como lo expone App Service en Windows:
    /// se sube el certificado y se pone <c>WEBSITE_LOAD_CERTIFICATES</c> con su huella (o con
    /// <c>*</c>), y entonces aparece en <c>CurrentUser\My</c>.</para>
    ///
    /// <para><b>Y en <c>/var/ssl/private</c> si estamos en Linux</b>, porque ahí App Service NO
    /// puebla el almacén: deja los certificados como archivos <c>.p12</c> con la huella por nombre.
    /// Esta aplicación se despliega en App Service Linux, así que sin este segundo sitio la función
    /// entera no encontraría nunca nada en producción. No es un .pfx con contraseña metido en la
    /// configuración —eso sería un secreto para proteger secretos—: es un archivo que pone la
    /// plataforma, sin contraseña, y al que solo llega el proceso de la aplicación.</para>
    ///
    /// <para><b><c>validOnly: false</c> es deliberado.</b> Con la validación puesta, un certificado
    /// caducado simplemente «no existe» — y el certificado caducado es justo el que hace falta
    /// encontrar para descifrar lo antiguo. Con <c>true</c> la rotación fallaría en silencio, que es
    /// el fallo que este archivo entero intenta evitar.</para>
    /// </summary>
    private static X509Certificate2? BuscarPorHuella(string huella)
    {
        try
        {
            using var almacen = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            almacen.Open(OpenFlags.ReadOnly);

            var encontrados = almacen.Certificates
                .Find(X509FindType.FindByThumbprint, huella, validOnly: false)
                .OfType<X509Certificate2>()
                .ToList();

            // Puede haber dos entradas con la misma huella: la misma pública importada dos veces, una
            // con llave privada y otra sin. Gana la que sirve para descifrar.
            var util = encontrados.FirstOrDefault(c => c.HasPrivateKey) ?? encontrados.FirstOrDefault();
            if (util is not null) return util;
        }
        catch
        {
            // Un almacén que no se puede abrir se trata como un almacén vacío: el que decide qué
            // hacer con «no aparece» es Planificar, y ahí el mensaje ya explica dónde mirar.
        }

        if (!OperatingSystem.IsLinux() || !Directory.Exists(CarpetaDeCertificadosEnLinux)) return null;

        try
        {
            // Se busca comparando nombres y no componiendo la ruta: el archivo lo nombra la
            // plataforma con la huella, pero no está garantizado en qué caja, y un Path.Combine con
            // la huella en mayúsculas no encontraría «…abc123.p12» en un sistema que distingue
            // mayúsculas de minúsculas. Fallar aquí significaría tumbar el arranque acusando de
            // ausente a un certificado que está a la vista.
            var ruta = Directory.EnumerateFiles(CarpetaDeCertificadosEnLinux, "*.p12").FirstOrDefault(
                a => string.Equals(Path.GetFileNameWithoutExtension(a), huella, StringComparison.OrdinalIgnoreCase));

            if (ruta is null) return null;

            var certificado = X509CertificateLoader.LoadPkcs12FromFile(ruta, (string?)null);

            // Que el archivo se llame como la huella no demuestra que lo sea. Devolver un certificado
            // distinto del pedido cifraría con el que no toca, y eso no se notaría hasta mucho después.
            return certificado.Thumbprint == huella ? certificado : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Donde App Service Linux deja los certificados de <c>WEBSITE_LOAD_CERTIFICATES</c>.</summary>
    private const string CarpetaDeCertificadosEnLinux = "/var/ssl/private";

    /// <summary>
    /// El aviso de caducidad del certificado que cifra, o <c>null</c> si le queda cuerda de sobra.
    ///
    /// Cifrar con un certificado caducado funciona —es RSA, no le importa la fecha—, así que esto no
    /// impide nada; existe porque el aviso llega en el arranque y un arranque puede tardar semanas en
    /// repetirse. Enterarse con un mes de margen es la diferencia entre rotar con calma y rotar el
    /// día que ya se rompió algo.
    /// </summary>
    private static string? SobreLaCaducidad(X509Certificate2 certificado)
    {
        var quedan = certificado.NotAfter - DateTime.Now;

        if (quedan < TimeSpan.Zero)
            return $"El certificado del llavero ({certificado.Thumbprint}) CADUCÓ el " +
                   $"{certificado.NotAfter:yyyy-MM-dd}. Se sigue cifrando con él, pero rótalo: el " +
                   "procedimiento está en GUIA-DE-PUESTA-EN-MARCHA.md, y lo importante es que el " +
                   $"actual pase a {Seccion}:CertificadosAnteriores en vez de borrarse.";

        if (quedan < TimeSpan.FromDays(30))
            return $"El certificado del llavero ({certificado.Thumbprint}) caduca el " +
                   $"{certificado.NotAfter:yyyy-MM-dd}, dentro de {(int)quedan.TotalDays} día(s). " +
                   "Rótalo con calma siguiendo GUIA-DE-PUESTA-EN-MARCHA.md; lo que NO se puede hacer " +
                   "es sustituirlo borrando el viejo.";

        return null;
    }

    private static string NoAparece(string huella) =>
        $"El llavero está configurado para cifrarse con el certificado de huella {huella} " +
        $"({Seccion}:Certificado) y ese certificado NO aparece. La aplicación no arranca a propósito: " +
        "seguir escribiría llaves nuevas SIN cifrar en el mismo sitio donde están las viejas —que sin " +
        "el certificado ya no se pueden descifrar—, así que se perderían todos los PAT guardados Y el " +
        "llavero quedaría legible, mientras la configuración sigue afirmando que está cifrado. " +
        "Sube el certificado y reinicia; si estás ROTANDO, el anterior NO se borra: su huella pasa a " +
        $"{Seccion}:CertificadosAnteriores." +
        (huella.All(char.IsAsciiHexDigit)
            ? ""
            : " Y revisa el valor: una huella son solo dígitos hexadecimales, y ése tiene otra cosa.") +
        PistaDeAppService();

    private static string SinLlavePrivada(string huella) =>
        $"El certificado {huella} ({Seccion}:Certificado) está pero SIN llave privada. La aplicación " +
        "no arranca a propósito: cifrar solo necesita la parte pública, así que hoy parecería " +
        "funcionar y en el siguiente reinicio no habría forma de descifrar nada de lo escrito hasta " +
        "entonces — se perderían los PAT guardados desde ahora. Sube el certificado con su llave " +
        "privada (un .pfx, no un .cer)." + PistaDeAppService();

    /// <summary>
    /// La causa más frecuente de «el certificado está subido y no aparece», dicha solo cuando
    /// estamos en App Service: sin <c>WEBSITE_LOAD_CERTIFICATES</c>, subir el certificado al recurso
    /// no lo pone al alcance del proceso.
    /// </summary>
    private static string PistaDeAppService()
    {
        if (Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") is null) return "";

        return Environment.GetEnvironmentVariable("WEBSITE_LOAD_CERTIFICATES") is null
            ? " Estás en App Service y WEBSITE_LOAD_CERTIFICATES no está puesta: sube el certificado " +
              "al App Service y añade esa variable con su huella (o con '*'). Es la causa habitual."
            : " Comprueba que la huella esté incluida en WEBSITE_LOAD_CERTIFICATES.";
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
