using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace AdminWeb.Infrastructure.Integraciones;

// ── Nota sobre el alcance ────────────────────────────────────────────────────────
//
// En el escritorio, BlobStorageService hacía dos trabajos a la vez: hablaba con Azure y, de paso,
// leía de la configuración la cadena, el contenedor y los prefijos. Aquí se parte en dos por el
// mismo motivo que AzureDevOpsService: esta capa no puede referenciar a la de aplicación —donde
// viven la configuración y los secretos— y, sobre todo, un cliente sin base de datos es lo que
// permite falsearlo en las pruebas SIN TOCAR LA RED. Quién decide con qué credenciales se habla es
// AlmacenamientoService, en Application.

/// <summary>
/// Con qué cuenta y contenedor se habla en una llamada concreta.
/// </summary>
public sealed record CredencialesDeBlob(string CadenaDeConexion, string Contenedor)
{
    /// <summary>
    /// El <c>ToString</c> que genera un record imprime todos sus miembros, así que el de serie
    /// escupiría la cadena de conexión entera —clave de la cuenta incluida— en cuanto alguien
    /// interpolara estas credenciales en un mensaje de error o en una traza. Se sobrescribe por eso.
    /// </summary>
    public override string ToString() =>
        $"CredencialesDeBlob {{ Contenedor = {Contenedor}, CadenaDeConexion = (oculta) }}";
}

/// <summary>Un blob del contenedor, con lo necesario para listarlo y administrarlo.</summary>
public sealed record BlobDelContenedor(
    string Nombre,
    long Bytes,
    DateTimeOffset? Modificado,
    IReadOnlyDictionary<string, string> Metadatos);

/// <summary>
/// Las reglas de nombres de blob que NO dependen de Azure: normalizar prefijos, deducir carpetas,
/// validar lo que teclea alguien. Están aparte de la llamada de red a propósito, que es la misma
/// razón que anotó el escritorio: es lo delicado y se puede probar sin conexión.
/// </summary>
public static class RutasDeBlob
{
    public const string PrefijoVersionesPorOmision = "releases";
    public const string PrefijoRespaldosDeDesplieguePorOmision = "respaldos-despliegue";

    /// <summary>
    /// Marcador con el que se materializa una carpeta vacía. En Azure las carpetas no existen: son
    /// solo el prefijo del nombre del blob. Para que una carpeta recién creada se vea —aquí y en el
    /// portal— antes de tener archivos, se sube un blob de cero bytes cuyo nombre termina en «/»,
    /// que es la convención del propio Explorador de Azure Storage.
    /// </summary>
    public static bool EsMarcadorDeCarpeta(string nombreDeBlob) => nombreDeBlob.EndsWith('/');

    /// <summary>
    /// Deja el prefijo en la forma que espera Azure: sin barras al inicio ni al final, sin espacios
    /// y con «\» convertido a «/». Un prefijo mal escrito («/releases/») no falla: crea una carpeta
    /// vacía con nombre raro en el contenedor, así que conviene sanearlo antes de usarlo.
    /// </summary>
    public static string Normalizar(string? valor, string porOmision)
    {
        var v = (valor ?? "").Replace('\\', '/').Trim().Trim('/');
        if (v.Length == 0) return porOmision;

        // Los segmentos vacíos («a//b») producen rutas inválidas.
        var partes = v.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return partes.Length == 0 ? porOmision : string.Join('/', partes);
    }

    /// <summary>
    /// Devuelve el NOMBRE del blob a partir de lo guardado en <c>AppRelease.ZipBlobUrl</c>, venga
    /// como nombre relativo o como URL absoluta.
    ///
    /// <para><b>Por qué hace falta.</b> Esa columna la comparten las dos aplicaciones sobre la misma
    /// base, y cada una la llenó de una forma: el escritorio guarda la URL completa
    /// (<c>https://cuenta.blob.core.windows.net/contenedor/releases/x.zip</c>) y la retraduce al
    /// leerla; la web guarda directamente <c>releases/x.zip</c>. Sin esta traducción, cualquier
    /// versión dada de alta desde el escritorio —o sea, TODAS las que ya existen el día del corte—
    /// haría que la web buscara un blob llamado literalmente «https://…», y el despliegue fallaría
    /// sin que el mensaje diera ninguna pista.</para>
    ///
    /// <para>Se quita el nombre del contenedor si la URL lo lleva delante, porque el cliente ya
    /// trabaja dentro de su contenedor y dejarlo produciría <c>contenedor/contenedor/releases/x.zip</c>.
    /// Un valor que no sea una URL se devuelve tal cual: ya es el nombre.</para>
    /// </summary>
    public static string? NombreDeBlobDesde(string? guardado, string? contenedor = null)
    {
        if (string.IsNullOrWhiteSpace(guardado)) return null;

        if (!Uri.TryCreate(guardado, UriKind.Absolute, out var uri) || !uri.Scheme.StartsWith("http"))
            return guardado.Trim();   // ya es un nombre de blob

        var ruta = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');   // «contenedor/blob»
        if (ruta.Length == 0) return null;

        if (!string.IsNullOrWhiteSpace(contenedor))
        {
            var prefijo = contenedor.Trim().Trim('/') + "/";
            if (ruta.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
                return ruta[prefijo.Length..];
        }

        // Sin saber el contenedor, el primer segmento SIEMPRE lo es en una URL de Azure Blob
        // («/{contenedor}/{blob}»), así que se descarta.
        var corte = ruta.IndexOf('/');
        return corte >= 0 && corte < ruta.Length - 1 ? ruta[(corte + 1)..] : ruta;
    }

    /// <summary>Valida un prefijo tecleado por alguien. Devuelve null si es aceptable.</summary>
    public static string? ValidarPrefijo(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;   // vacío = se usa el de por omisión

        var v = valor.Replace('\\', '/').Trim().Trim('/');
        if (v.Length > 200) return "El nombre de la carpeta es demasiado largo (máximo 200).";

        // Azure acepta casi cualquier cosa en el nombre del blob, pero estos caracteres provocan
        // rutas imposibles de manejar después, ni desde la aplicación ni desde el portal.
        foreach (var c in v)
            if (char.IsControl(c) || c is '"' or '<' or '>' or '|' or ':' or '*' or '?')
                return $"El carácter «{c}» no se puede usar en el nombre de la carpeta.";

        return null;
    }

    /// <summary>
    /// Los nombres de metadato de Azure deben ser identificadores ASCII: letras a–z/A–Z, dígitos y
    /// guion bajo, sin empezar por dígito. Si no, la petición falla con un error poco descriptivo
    /// del SDK en vez de decir cuál es el problema.
    ///
    /// <para>Ojo con los acentos: <c>char.IsLetterOrDigit</c> los da por buenos porque son letras
    /// Unicode, pero Azure los rechaza. Por eso la comprobación es explícitamente ASCII y no la del
    /// framework.</para>
    /// </summary>
    public static string? ValidarClaveDeMetadato(string? clave)
    {
        if (string.IsNullOrWhiteSpace(clave)) return "El nombre del metadato no puede estar vacío.";
        if (clave[0] is >= '0' and <= '9') return $"«{clave}»: no puede empezar con un número.";

        foreach (var c in clave)
        {
            bool ascii = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_';
            if (!ascii)
                return $"«{clave}»: solo se permiten letras sin acento, números y guion bajo " +
                       $"(el carácter «{c}» no).";
        }
        return null;
    }

    /// <summary>Subcarpetas inmediatas de <paramref name="prefijoBase"/>, deducidas de los nombres de blob.</summary>
    public static List<string> DerivarSubcarpetas(IEnumerable<string> nombresDeBlob, string prefijoBase)
    {
        var baseNorm = Normalizar(prefijoBase, "");
        var raiz = baseNorm.Length == 0 ? "" : baseNorm + "/";
        var carpetas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var nombre in nombresDeBlob)
        {
            if (!nombre.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) continue;

            var resto = nombre[raiz.Length..];
            int i = resto.IndexOf('/');
            if (i > 0) carpetas.Add(resto[..i]);   // hay al menos un nivel más
        }
        return [.. carpetas.OrderBy(c => c, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Nombre sin la carpeta, que es lo que se lee en una lista.</summary>
    public static string NombreCorto(string nombreDeBlob) =>
        nombreDeBlob.Contains('/') ? nombreDeBlob[(nombreDeBlob.LastIndexOf('/') + 1)..] : nombreDeBlob;

    /// <summary>Tamaño en unidades que una persona pueda leer de un vistazo.</summary>
    public static string TamanoLegible(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:0.0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        _ => $"{bytes / 1024d / 1024d / 1024d:0.00} GB"
    };
}

/// <summary>
/// Lo que se puede hacer contra el contenedor de Azure Blob Storage.
///
/// <para>Es una interfaz y no una clase concreta por una razón concreta: las pruebas de esta
/// vertical NO deben tocar la red. Con esto, la lógica que sí importa —qué carpeta se lista, qué se
/// valida antes de borrar, qué se audita— se comprueba con un doble en memoria.</para>
/// </summary>
public interface IClienteDeBlobs
{
    Task<(bool ok, string mensaje)> ProbarAsync(CredencialesDeBlob cred, CancellationToken ct = default);

    Task<IReadOnlyList<BlobDelContenedor>> ListarAsync(
        CredencialesDeBlob cred, string prefijo, CancellationToken ct = default);

    /// <summary>Solo los nombres, sin metadatos: es lo que hace falta para deducir carpetas.</summary>
    Task<IReadOnlyList<string>> ListarNombresAsync(
        CredencialesDeBlob cred, string? prefijo, CancellationToken ct = default);

    /// <summary>Crea la carpeta subiendo su marcador. Devuelve false si ya existía.</summary>
    Task<bool> CrearCarpetaAsync(CredencialesDeBlob cred, string ruta, CancellationToken ct = default);

    Task<bool> ExisteAsync(CredencialesDeBlob cred, string blob, CancellationToken ct = default);

    Task<bool> EliminarAsync(CredencialesDeBlob cred, string blob, CancellationToken ct = default);

    Task<int> ContarEnCarpetaAsync(CredencialesDeBlob cred, string carpeta, CancellationToken ct = default);

    Task<int> EliminarCarpetaAsync(CredencialesDeBlob cred, string carpeta, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string>> ObtenerMetadatosAsync(
        CredencialesDeBlob cred, string blob, CancellationToken ct = default);

    Task ActualizarMetadatosAsync(
        CredencialesDeBlob cred, string blob, IDictionary<string, string> metadatos, CancellationToken ct = default);

    Task SubirAsync(CredencialesDeBlob cred, string blob, Stream contenido,
        IDictionary<string, string>? metadatos = null, CancellationToken ct = default);

    /// <summary>Abre el blob para leerlo en flujo, sin materializarlo entero en memoria.</summary>
    Task<Stream> AbrirLecturaAsync(CredencialesDeBlob cred, string blob, CancellationToken ct = default);

    Task<long> TamanoAsync(CredencialesDeBlob cred, string blob, CancellationToken ct = default);

    /// <summary>URL temporal de solo lectura para UN archivo, sin dar acceso al contenedor.</summary>
    Uri GenerarEnlaceDeDescarga(CredencialesDeBlob cred, string blob, double horasDeVigencia);

    /// <summary>Traduce un fallo del SDK a algo accionable. Se enseña tal cual.</summary>
    string Explicar(Exception ex);
}

/// <summary>El cliente de verdad, sobre el SDK de Azure.</summary>
public sealed class ClienteDeBlobsAzure : IClienteDeBlobs
{
    private static BlobContainerClient Contenedor(CredencialesDeBlob cred) =>
        new(cred.CadenaDeConexion, cred.Contenedor);

    /// <summary>
    /// Comprueba que se pueda hablar con la cuenta.
    ///
    /// <para>No se limita a conectar: informa si el contenedor existe y si la cadena permite FIRMAR
    /// enlaces SAS, porque una cadena basada solo en SAS conecta bien pero deja los enlaces de
    /// descarga inutilizables — y eso conviene saberlo aquí, no el día que haga falta uno.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> ProbarAsync(CredencialesDeBlob cred, CancellationToken ct = default)
    {
        try
        {
            var cliente = Contenedor(cred);

            if (!await cliente.ExistsAsync(ct))
                return (true, $"Conexión correcta con la cuenta, pero el contenedor «{cred.Contenedor}» todavía " +
                              "no existe. Se creará solo la primera vez que se suba algo.");

            // Se cuenta poco: solo interesa confirmar que se puede leer, no recorrer el contenedor.
            int muestra = 0;
            await foreach (var _ in cliente.GetBlobsAsync(cancellationToken: ct))
                if (++muestra >= 25) break;

            var firma = cliente.GetBlobClient("prueba").CanGenerateSasUri
                ? "Se pueden generar enlaces de descarga."
                : "ATENCIÓN: esta cadena no incluye la clave de la cuenta, así que NO se podrán generar " +
                  "enlaces de descarga.";

            return (true, $"Conexión correcta con el contenedor «{cred.Contenedor}» " +
                          $"({(muestra >= 25 ? "25+" : muestra.ToString())} archivo(s) visibles). {firma}");
        }
        catch (Exception ex)
        {
            return (false, Explicar(ex));
        }
    }

    /// <summary>
    /// Listado con tamaño, fecha y metadatos. Los marcadores de carpeta se omiten: son un detalle
    /// de implementación, no archivos.
    /// </summary>
    public async Task<IReadOnlyList<BlobDelContenedor>> ListarAsync(
        CredencialesDeBlob cred, string prefijo, CancellationToken ct = default)
    {
        var resultado = new List<BlobDelContenedor>();
        await foreach (var b in Contenedor(cred).GetBlobsAsync(BlobTraits.Metadata, BlobStates.None, prefijo, ct))
        {
            if (RutasDeBlob.EsMarcadorDeCarpeta(b.Name)) continue;
            resultado.Add(new BlobDelContenedor(
                b.Name,
                b.Properties.ContentLength ?? 0,
                b.Properties.LastModified,
                b.Metadata is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(b.Metadata)));
        }
        return resultado;
    }

    public async Task<IReadOnlyList<string>> ListarNombresAsync(
        CredencialesDeBlob cred, string? prefijo, CancellationToken ct = default)
    {
        var nombres = new List<string>();
        await foreach (var b in Contenedor(cred).GetBlobsAsync(BlobTraits.None, BlobStates.None, prefijo, ct))
            nombres.Add(b.Name);
        return nombres;
    }

    /// <summary>
    /// Idempotente: si la carpeta ya existe no hace nada, así que volver a crearla no borra lo que
    /// tenga dentro.
    /// </summary>
    public async Task<bool> CrearCarpetaAsync(CredencialesDeBlob cred, string ruta, CancellationToken ct = default)
    {
        var contenedor = Contenedor(cred);
        await contenedor.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var marcador = contenedor.GetBlobClient(ruta + "/");
        if (await marcador.ExistsAsync(ct)) return false;

        using var vacio = new MemoryStream();
        await marcador.UploadAsync(vacio, overwrite: false, cancellationToken: ct);
        return true;
    }

    public async Task<bool> ExisteAsync(CredencialesDeBlob cred, string blob, CancellationToken ct = default)
        => await Contenedor(cred).GetBlobClient(blob).ExistsAsync(ct);

    public async Task<bool> EliminarAsync(CredencialesDeBlob cred, string blob, CancellationToken ct = default)
    {
        var respuesta = await Contenedor(cred).GetBlobClient(blob)
            .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
        return respuesta.Value;
    }

    public async Task<int> ContarEnCarpetaAsync(CredencialesDeBlob cred, string carpeta, CancellationToken ct = default)
    {
        int n = 0;
        await foreach (var _ in Contenedor(cred).GetBlobsAsync(BlobTraits.None, BlobStates.None, carpeta + "/", ct))
            n++;
        return n;
    }

    /// <summary>
    /// Borra todos los blobs bajo el prefijo, incluidas sus subcarpetas y el marcador. El «/» final
    /// evita llevarse por error una carpeta hermana con nombre parecido: «QA» no se lleva a «QA2».
    /// </summary>
    public async Task<int> EliminarCarpetaAsync(CredencialesDeBlob cred, string carpeta, CancellationToken ct = default)
    {
        var contenedor = Contenedor(cred);

        var nombres = new List<string>();
        await foreach (var b in contenedor.GetBlobsAsync(BlobTraits.None, BlobStates.None, carpeta + "/", ct))
            nombres.Add(b.Name);

        int borrados = 0;
        foreach (var n in nombres)
        {
            ct.ThrowIfCancellationRequested();
            if ((await contenedor.GetBlobClient(n)
                    .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct)).Value)
                borrados++;
        }
        return borrados;
    }

    public async Task<IReadOnlyDictionary<string, string>> ObtenerMetadatosAsync(
        CredencialesDeBlob cred, string blob, CancellationToken ct = default)
    {
        var props = await Contenedor(cred).GetBlobClient(blob).GetPropertiesAsync(cancellationToken: ct);
        return props.Value.Metadata is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(props.Value.Metadata);
    }

    public Task ActualizarMetadatosAsync(
        CredencialesDeBlob cred, string blob, IDictionary<string, string> metadatos, CancellationToken ct = default)
        => Contenedor(cred).GetBlobClient(blob).SetMetadataAsync(metadatos, cancellationToken: ct);

    public async Task SubirAsync(CredencialesDeBlob cred, string blob, Stream contenido,
        IDictionary<string, string>? metadatos = null, CancellationToken ct = default)
    {
        var contenedor = Contenedor(cred);
        await contenedor.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        await contenedor.GetBlobClient(blob).UploadAsync(
            contenido, new BlobUploadOptions { Metadata = metadatos }, ct);
    }

    public async Task<Stream> AbrirLecturaAsync(CredencialesDeBlob cred, string blob, CancellationToken ct = default)
        => await Contenedor(cred).GetBlobClient(blob).OpenReadAsync(cancellationToken: ct);

    public async Task<long> TamanoAsync(CredencialesDeBlob cred, string blob, CancellationToken ct = default)
    {
        var props = await Contenedor(cred).GetBlobClient(blob).GetPropertiesAsync(cancellationToken: ct);
        return props.Value.ContentLength;
    }

    /// <summary>
    /// Requiere que la cadena incluya la clave de la cuenta: con una que solo lleve SAS, el SDK no
    /// puede firmar y hay que decirlo claro en lugar de fallar de forma incomprensible.
    /// </summary>
    public Uri GenerarEnlaceDeDescarga(CredencialesDeBlob cred, string blob, double horasDeVigencia)
    {
        var cliente = Contenedor(cred).GetBlobClient(blob);
        if (!cliente.CanGenerateSasUri)
            throw new InvalidOperationException(
                "La cadena de conexión configurada no incluye la clave de la cuenta, así que no se " +
                "puede firmar un enlace de descarga. Usa la cadena completa de la cuenta de almacenamiento.");

        return cliente.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(horasDeVigencia));
    }

    /// <summary>
    /// Traduce los fallos típicos de Azure Storage a algo accionable.
    ///
    /// <para>Ninguno de estos textos repite la cadena de conexión ni parte de ella: el mensaje del
    /// SDK sí puede llevar el nombre de la cuenta, pero nunca la clave, y es lo que hace falta para
    /// entender qué pasa.</para>
    /// </summary>
    public string Explicar(Exception ex)
    {
        var msg = ex.Message;

        if (ex is FormatException || msg.Contains("connection string", StringComparison.OrdinalIgnoreCase))
            return "La cadena de conexión no tiene el formato esperado. Cópiala completa desde el portal de " +
                   "Azure (Cuenta de almacenamiento → Claves de acceso → Cadena de conexión).";

        if (ex is Azure.RequestFailedException fallo)
            return fallo.Status switch
            {
                403 => $"Acceso denegado (403). La clave puede estar revocada o sin permisos sobre el contenedor. Detalle: {msg}",
                404 => $"No se encontró la cuenta o el contenedor (404). Revisa el nombre. Detalle: {msg}",
                409 => $"Conflicto (409): el nombre del contenedor puede estar en uso con otra configuración. Detalle: {msg}",
                _ => $"Azure respondió {fallo.Status}. {msg}"
            };

        if (msg.Contains("No such host", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("nombre no", StringComparison.OrdinalIgnoreCase))
            return "No se alcanzó la cuenta de almacenamiento. Revisa el nombre de la cuenta y la conexión del servidor.";

        return msg;
    }
}
