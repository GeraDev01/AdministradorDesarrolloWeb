using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Un blob del contenedor, con lo necesario para listarlo y administrarlo.</summary>
public sealed record BlobItem(
    string Name,
    long SizeBytes,
    DateTimeOffset? LastModified,
    IDictionary<string, string> Metadata)
{
    /// <summary>Nombre sin el prefijo de carpeta.</summary>
    public string ShortName => Name.Contains('/') ? Name[(Name.LastIndexOf('/') + 1)..] : Name;

    public string SizeLegible => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024d:0.0} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / 1024d / 1024d:0.0} MB",
        _ => $"{SizeBytes / 1024d / 1024d / 1024d:0.00} GB"
    };
}

public class BlobStorageService
{
    private readonly SettingsService _settings;

    public BlobStorageService(SettingsService settings) => _settings = settings;

    /// <summary>
    /// La conexión sale de la base y la lee cualquier equipo: se guarda cifrada con
    /// <c>SharedSecretProtector</c>, no con la cuenta de Windows de quien la capturó. Un
    /// administrador la captura una vez y con eso queda para todos.
    /// </summary>
    public bool IsConfigured => ConexionEnEfecto() != null;

    private string? ConexionEnEfecto() =>
        _settings.Get(SettingsService.Keys.AzureBlobConnectionString) is { Length: > 0 } guardada
            ? guardada
            : null;

    /// <summary>Contenedor configurado, o el de por omisión. Un valor vacío cuenta como no puesto:
    /// guardarlo en blanco es la forma de volver al valor por omisión.</summary>
    private string ContenedorEnEfecto() =>
        _settings.Get(SettingsService.Keys.AzureBlobContainer) is { Length: > 0 } guardado
            ? guardado
            : "despliegues";

    // ── Carpetas configurables ──────────────────────────────────────────────────

    public const string PrefijoVersionesPorDefecto = "releases";
    public const string PrefijoRespaldosBdPorDefecto = "backups";
    public const string PrefijoRespaldosDesplieguePorDefecto = "respaldos-despliegue";

    /// <summary>Carpeta donde se guardan los ZIP de las versiones.</summary>
    public string PrefijoVersiones =>
        Normalizar(_settings.Get(SettingsService.Keys.AzureBlobReleasesPrefix), PrefijoVersionesPorDefecto);

    /// <summary>Carpeta donde se guardan los respaldos de la base de datos.</summary>
    public string PrefijoRespaldosBd =>
        Normalizar(_settings.Get(SettingsService.Keys.AzureBlobBackupsPrefix), PrefijoRespaldosBdPorDefecto);

    /// <summary>Carpeta donde se guardan los respaldos previos de las carpetas remotas.</summary>
    public string PrefijoRespaldosDespliegue =>
        Normalizar(_settings.Get(SettingsService.Keys.AzureBlobDeployBackupsPrefix), PrefijoRespaldosDesplieguePorDefecto);

    /// <summary>
    /// Marcador con el que se materializa una carpeta vacía. En Azure las carpetas no existen: son
    /// solo el prefijo del nombre del blob. Para que una carpeta recién creada se vea (aquí y en el
    /// portal) antes de tener archivos, se sube un blob de cero bytes cuyo nombre termina en «/»,
    /// que es la convención que usa el propio Explorador de Azure Storage.
    /// </summary>
    public static bool EsMarcadorDeCarpeta(string blobName) => blobName.EndsWith('/');

    /// <summary>Ruta completa donde se guarda una versión de una subcarpeta dada.</summary>
    public string RutaVersiones(string? subcarpeta)
    {
        var sub = Normalizar(subcarpeta, "");
        return sub.Length == 0 ? PrefijoVersiones : $"{PrefijoVersiones}/{sub}";
    }

    /// <summary>
    /// Deriva las subcarpetas inmediatas de <paramref name="prefijoBase"/> a partir de los nombres
    /// de blob. Está separado de la llamada a Azure para poder probarlo sin red.
    /// </summary>
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
        return carpetas.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Subcarpetas que existen hoy bajo una carpeta dada (por ejemplo, bajo «releases»).</summary>
    public async Task<List<string>> ListarSubcarpetasAsync(string prefijoBase, CancellationToken ct = default)
    {
        var nombres = new List<string>();
        var raiz = Normalizar(prefijoBase, "");
        await foreach (var b in Contenedor().GetBlobsAsync(BlobTraits.None, BlobStates.None, raiz + "/", ct))
            nombres.Add(b.Name);
        return DerivarSubcarpetas(nombres, raiz);
    }

    /// <summary>
    /// Crea una carpeta subiendo su marcador. Es idempotente: si ya existe no hace nada, así que
    /// volver a crearla no borra lo que tenga dentro.
    /// </summary>
    public async Task<(bool creada, string ruta)> CrearCarpetaAsync(string ruta, CancellationToken ct = default)
    {
        var error = ValidarPrefijo(ruta);
        if (error != null) throw new ArgumentException(error, nameof(ruta));

        var normal = Normalizar(ruta, "");
        if (normal.Length == 0) throw new ArgumentException("Escribe el nombre de la carpeta.", nameof(ruta));

        var contenedor = Contenedor();
        await contenedor.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var marcador = contenedor.GetBlobClient(normal + "/");
        if (await marcador.ExistsAsync(ct)) return (false, normal);

        using var vacio = new MemoryStream();
        await marcador.UploadAsync(vacio, overwrite: false, cancellationToken: ct);
        return (true, normal);
    }

    /// <summary>
    /// Deja el prefijo en la forma que espera Azure: sin barras al inicio ni al final, sin espacios
    /// y con «\» convertido a «/». Un prefijo mal escrito («/releases/») crea una carpeta vacía con
    /// nombre raro en el contenedor en lugar de fallar, así que conviene sanearlo antes de usarlo.
    /// </summary>
    public static string Normalizar(string? valor, string porDefecto)
    {
        var v = (valor ?? "").Replace('\\', '/').Trim().Trim('/');
        if (v.Length == 0) return porDefecto;

        // Los segmentos vacíos ("a//b") producen rutas inválidas.
        var partes = v.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return partes.Length == 0 ? porDefecto : string.Join('/', partes);
    }

    /// <summary>Valida un prefijo tecleado por el usuario. Devuelve null si es aceptable.</summary>
    public static string? ValidarPrefijo(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;   // vacío = se usa el de por omisión

        var v = valor.Replace('\\', '/').Trim().Trim('/');
        if (v.Length > 200) return "El nombre de la carpeta es demasiado largo (máximo 200).";

        // Azure acepta casi cualquier cosa en el nombre del blob, pero estos caracteres provocan
        // rutas imposibles de manejar después desde la propia aplicación o desde el portal.
        foreach (var c in v)
            if (char.IsControl(c) || c is '"' or '<' or '>' or '|' or ':' or '*' or '?')
                return $"El carácter «{c}» no se puede usar en el nombre de la carpeta.";

        return null;
    }

    // ── Operaciones ─────────────────────────────────────────────────────────────

    private BlobContainerClient Contenedor()
    {
        var connStr = ConexionEnEfecto()
            ?? throw new InvalidOperationException("Azure Blob Storage no está configurado. Ve a Configuración y agrega la connection string.");
        return new BlobContainerClient(connStr, ContenedorEnEfecto());
    }

    /// <summary>
    /// Comprueba que se pueda hablar con la cuenta de almacenamiento.
    ///
    /// Acepta valores sueltos para poder probar lo que el usuario acaba de teclear sin obligarlo a
    /// guardarlo antes: guardar una connection string equivocada y luego descubrirlo es peor.
    ///
    /// No se limita a conectar: informa si el contenedor existe y si la cadena permite FIRMAR
    /// enlaces SAS, porque una cadena basada solo en SAS conecta bien pero deja los enlaces de
    /// descarga inutilizables — y eso conviene saberlo aquí, no el día que haga falta uno.
    /// </summary>
    public async Task<(bool ok, string mensaje)> ProbarConexionAsync(
        string? connectionString = null, string? contenedor = null, CancellationToken ct = default)
    {
        var connStr = string.IsNullOrWhiteSpace(connectionString)
            ? ConexionEnEfecto()
            : connectionString.Trim();
        if (string.IsNullOrWhiteSpace(connStr))
            return (false, "Falta la connection string de Azure Blob Storage.");

        var nombre = string.IsNullOrWhiteSpace(contenedor)
            ? ContenedorEnEfecto()
            : contenedor.Trim();

        try
        {
            var client = new BlobContainerClient(connStr, nombre);

            bool existe = await client.ExistsAsync(ct);
            if (!existe)
                return (true, $"Conexión correcta con la cuenta, pero el contenedor «{nombre}» todavía no existe. " +
                              "Se creará solo la primera vez que se suba algo.");

            // Se cuenta poco: solo interesa confirmar que se puede leer, no recorrer el contenedor.
            int muestra = 0;
            await foreach (var _ in client.GetBlobsAsync(cancellationToken: ct))
                if (++muestra >= 25) break;

            var firma = client.GetBlobClient("prueba").CanGenerateSasUri
                ? "Se pueden generar enlaces SAS de descarga."
                : "ATENCIÓN: esta cadena no incluye la clave de la cuenta, así que NO se podrán generar enlaces SAS.";

            return (true, $"Conexión correcta con el contenedor «{nombre}» " +
                          $"({(muestra >= 25 ? "25+" : muestra.ToString())} archivo(s) visibles). {firma}");
        }
        catch (Exception ex)
        {
            return (false, Explicar(ex));
        }
    }

    /// <summary>Traduce los fallos típicos de Azure Storage a algo accionable.</summary>
    private static string Explicar(Exception ex)
    {
        var msg = ex.Message;

        if (ex is FormatException || msg.Contains("connection string", StringComparison.OrdinalIgnoreCase))
            return "La connection string no tiene el formato esperado. Cópiala completa desde el portal de Azure " +
                   "(Cuenta de almacenamiento → Claves de acceso → Cadena de conexión).";

        if (ex is Azure.RequestFailedException rf)
            return rf.Status switch
            {
                403 => $"Acceso denegado (403). La clave puede estar revocada o sin permisos sobre el contenedor.\n\nDetalle: {msg}",
                404 => $"No se encontró la cuenta o el contenedor (404). Revisa el nombre.\n\nDetalle: {msg}",
                409 => $"Conflicto (409): el nombre del contenedor puede estar en uso con otra configuración.\n\nDetalle: {msg}",
                _   => $"Azure respondió {rf.Status}. {msg}"
            };

        if (msg.Contains("No such host", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("nombre no", StringComparison.OrdinalIgnoreCase))
            return "No se alcanzó la cuenta de almacenamiento. Revisa el nombre de la cuenta y tu conexión a internet.";

        return msg;
    }

    public async Task<string> UploadFileAsync(string localPath, string blobName,
        IProgress<string>? progress = null, CancellationToken ct = default,
        IDictionary<string, string>? metadata = null)
    {
        var containerClient = Contenedor();
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var blobClient = containerClient.GetBlobClient(blobName);
        progress?.Report($"  ⬆  Subiendo a Azure Blob: {blobName}");

        using var stream = File.OpenRead(localPath);
        await blobClient.UploadAsync(stream, new BlobUploadOptions { Metadata = metadata }, ct);

        progress?.Report($"  ✓  Subido: {blobClient.Uri}");
        return blobClient.Uri.ToString();
    }

    /// <summary>true si ya existe un blob con ese nombre (para avisar antes de sobrescribir).</summary>
    public async Task<bool> ExisteBlobAsync(string blobName, CancellationToken ct = default)
        => await Contenedor().GetBlobClient(blobName).ExistsAsync(ct);

    /// <summary>
    /// Sube un archivo local al contenedor (sobrescribe si ya existe: la confirmación se hace en la
    /// interfaz). Reporta bytes transferidos para una barra de progreso, igual que Blobup.
    /// </summary>
    public async Task<string> SubirArchivoAsync(string localPath, string blobName,
        IProgress<long>? progress = null, IDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var container = Contenedor();
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var blobClient = container.GetBlobClient(blobName);
        using var stream = File.OpenRead(localPath);
        var options = new BlobUploadOptions { ProgressHandler = progress, Metadata = metadata };
        await blobClient.UploadAsync(stream, options, ct);
        return blobClient.Uri.ToString();
    }

    /// <summary>URI (sin SAS) del blob dentro del contenedor.</summary>
    public Uri UriDeBlob(string blobName) => Contenedor().GetBlobClient(blobName).Uri;

    /// <summary>
    /// Extrae el nombre de blob (relativo al contenedor) de una URL completa de Azure Blob. Devuelve
    /// null si la URL no corresponde a este contenedor. Sirve para volver a ubicar el paquete de una
    /// versión a partir de su <c>ZipBlobUrl</c>.
    /// </summary>
    public string? BlobNameFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');   // "{contenedor}/{blob}"
        var prefijo = Contenedor().Name + "/";
        return path.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase) ? path[prefijo.Length..] : null;
    }

    /// <summary>
    /// Abre un blob como stream de lectura CON posicionamiento (lecturas por rango): no descarga el
    /// blob entero, lo lee bajo demanda. Es lo que necesita <see cref="System.IO.Compression.ZipArchive"/>
    /// en modo lectura para publicar en streaming, igual que Blobup (NO uses una descarga a stream
    /// normal: esa no admite Seek y ZipArchive la rechaza).
    /// </summary>
    public async Task<Stream> OpenReadBlobAsync(string blobName, CancellationToken ct = default)
        => await Contenedor().GetBlobClient(blobName).OpenReadAsync(cancellationToken: ct);

    /// <summary>Tamaño en bytes de un blob (de sus propiedades, sin descargarlo).</summary>
    public async Task<long> TamanoBlobAsync(string blobName, CancellationToken ct = default)
    {
        var props = await Contenedor().GetBlobClient(blobName).GetPropertiesAsync(cancellationToken: ct);
        return props.Value.ContentLength;
    }

    /// <summary>Descarga un blob a un archivo local (creando la carpeta destino si hace falta).</summary>
    public async Task DescargarBlobAsync(string blobName, string localPath, CancellationToken ct = default)
    {
        var blobClient = Contenedor().GetBlobClient(blobName);
        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await using var fs = File.Create(localPath);
        await blobClient.DownloadToAsync(fs, ct);
    }

    public async Task<string> BackupDatabaseAsync(string dbPath,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var blobName = $"{PrefijoRespaldosBd}/app_{DateTime.UtcNow:yyyyMMdd_HHmmss}.db";
        return await UploadFileAsync(dbPath, blobName, progress, ct);
    }

    public async Task<List<string>> ListBlobsAsync(string prefix = "", CancellationToken ct = default)
    {
        var result = new List<string>();
        await foreach (var blob in Contenedor().GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct))
            result.Add(blob.Name);
        return result;
    }

    /// <summary>
    /// Listado con tamaño, fecha y metadatos, para administrarlos desde la aplicación.
    /// Los marcadores de carpeta se omiten: son un detalle de implementación, no archivos.
    /// </summary>
    public async Task<List<BlobItem>> ListarDetalladoAsync(string prefix = "", CancellationToken ct = default)
    {
        var result = new List<BlobItem>();
        await foreach (var b in Contenedor().GetBlobsAsync(BlobTraits.Metadata, BlobStates.None, prefix, ct))
        {
            if (EsMarcadorDeCarpeta(b.Name)) continue;
            result.Add(new BlobItem(
                b.Name,
                b.Properties.ContentLength ?? 0,
                b.Properties.LastModified,
                b.Metadata ?? new Dictionary<string, string>()));
        }
        return result;
    }

    /// <summary>Elimina un blob (archivo) del contenedor. Devuelve true si existía y se borró.</summary>
    public async Task<bool> EliminarBlobAsync(string blobName, CancellationToken ct = default)
    {
        var resp = await Contenedor().GetBlobClient(blobName)
            .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
        return resp.Value;
    }

    /// <summary>
    /// Cuenta cuántos blobs cuelgan de una carpeta (archivos, subcarpetas y el marcador), para poder
    /// avisar del alcance antes de borrarla.
    /// </summary>
    public async Task<int> ContarBlobsEnCarpetaAsync(string carpeta, CancellationToken ct = default)
    {
        var normal = Normalizar(carpeta, "");
        if (normal.Length == 0) return 0;
        int n = 0;
        await foreach (var _ in Contenedor().GetBlobsAsync(BlobTraits.None, BlobStates.None, normal + "/", ct))
            n++;
        return n;
    }

    /// <summary>
    /// Elimina una carpeta COMPLETA del contenedor: todos los blobs bajo ese prefijo (incluidas sus
    /// subcarpetas) y el marcador de la propia carpeta. El «/» final evita borrar por error una
    /// carpeta hermana con nombre parecido («QA» no se lleva a «QA2»). Devuelve cuántos se borraron.
    /// </summary>
    public async Task<int> EliminarCarpetaAsync(string carpeta, CancellationToken ct = default)
    {
        var normal = Normalizar(carpeta, "");
        if (normal.Length == 0) throw new ArgumentException("Indica la carpeta a eliminar.", nameof(carpeta));

        var contenedor = Contenedor();
        var nombres = new List<string>();
        await foreach (var b in contenedor.GetBlobsAsync(BlobTraits.None, BlobStates.None, normal + "/", ct))
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

    /// <summary>
    /// Carpetas que existen hoy en el contenedor, hasta dos niveles: interesa distinguir
    /// «releases/QA» de «releases/Productivo», no solo ver «releases».
    /// </summary>
    public async Task<List<string>> ListarCarpetasAsync(CancellationToken ct = default)
    {
        var carpetas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var b in Contenedor().GetBlobsAsync(BlobTraits.None, BlobStates.None, null, ct))
        {
            var partes = b.Name.Split('/');
            if (partes.Length > 1) carpetas.Add(partes[0]);
            if (partes.Length > 2) carpetas.Add($"{partes[0]}/{partes[1]}");
        }
        return carpetas.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Genera una URL temporal de solo lectura para descargar el archivo completo sin dar acceso
    /// al contenedor. Requiere que la connection string incluya la clave de la cuenta: con una que
    /// solo lleve SAS, el SDK no puede firmar y hay que decirlo claro en lugar de fallar raro.
    /// </summary>
    public Uri GenerarSasDescarga(string blobName, double horasVigencia)
    {
        if (horasVigencia is < 1 or > 8760)
            throw new ArgumentOutOfRangeException(nameof(horasVigencia), "La vigencia debe estar entre 1 hora y 1 año.");

        var client = Contenedor().GetBlobClient(blobName);
        if (!client.CanGenerateSasUri)
            throw new InvalidOperationException(
                "La connection string configurada no incluye la clave de la cuenta, así que no se " +
                "puede firmar un enlace SAS. Usa la cadena completa de la cuenta de almacenamiento.");

        return client.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(horasVigencia));
    }

    public async Task<IDictionary<string, string>> ObtenerMetadatosAsync(string blobName, CancellationToken ct = default)
    {
        var props = await Contenedor().GetBlobClient(blobName).GetPropertiesAsync(cancellationToken: ct);
        return props.Value.Metadata ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// Reemplaza los metadatos del blob. Azure NO hace merge: lo que se manda es lo que queda, así
    /// que quien llama debe enviar el conjunto completo.
    /// </summary>
    public async Task ActualizarMetadatosAsync(string blobName, IDictionary<string, string> metadata, CancellationToken ct = default)
    {
        foreach (var clave in metadata.Keys)
        {
            var error = ValidarClaveMetadato(clave);
            if (error != null) throw new ArgumentException(error);
        }
        await Contenedor().GetBlobClient(blobName).SetMetadataAsync(metadata, cancellationToken: ct);
    }

    /// <summary>
    /// Los nombres de metadato de Azure deben ser identificadores válidos ASCII: letras a–z/A–Z,
    /// dígitos y guion bajo, sin empezar por dígito. Si no, la petición falla con un error poco
    /// descriptivo del SDK en lugar de decir cuál es el problema.
    ///
    /// Ojo con los acentos: char.IsLetterOrDigit los da por buenos porque son letras Unicode, pero
    /// Azure los rechaza. Por eso la comprobación es explícitamente ASCII y no la del framework.
    /// </summary>
    public static string? ValidarClaveMetadato(string? clave)
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
}
