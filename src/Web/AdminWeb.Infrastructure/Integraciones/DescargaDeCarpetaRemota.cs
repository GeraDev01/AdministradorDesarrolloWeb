using FluentFTP;

namespace AdminWeb.Infrastructure.Integraciones;

/// <summary>
/// A qué carpeta remota hay que ir a por el respaldo previo al despliegue.
/// </summary>
/// <param name="Host">Tal como está guardado, con esquema incluido («ftps://…»).</param>
/// <param name="Contrasena">Ya EN CLARO. Quien llama la descifra; aquí solo se usa para conectar.</param>
public sealed record CredencialesDeCarpetaRemota(
    string Host, int Puerto, string Usuario, string Contrasena, string RutaRemota)
{
    /// <summary>
    /// Igual que en las credenciales de Blob: el <c>ToString</c> de serie de un record imprimiría la
    /// contraseña del servidor de despliegue en cualquier traza o mensaje de error donde alguien
    /// interpole esto. No es hipotético: un mensaje así acaba en la bitácora, que la lee más gente.
    /// </summary>
    public override string ToString() =>
        $"CredencialesDeCarpetaRemota {{ Host = {Host}, Puerto = {Puerto}, Usuario = {Usuario}, " +
        $"RutaRemota = {RutaRemota}, Contrasena = (oculta) }}";
}

/// <summary>
/// Trae a una carpeta local lo que hay hoy en la carpeta remota de un servidor.
///
/// <para>Es una interfaz porque el respaldo previo se prueba sin red: lo que importa comprobar —que
/// sin respaldo NO se despliega, que una carpeta remota vacía no es un error— es decisión, no
/// transferencia.</para>
/// </summary>
public interface IDescargaDeCarpetaRemota
{
    /// <summary>
    /// Descarga la carpeta remota completa. Devuelve cuántos archivos se bajaron; <c>0</c> cuando la
    /// carpeta remota todavía no existe, que es el primer despliegue a ese servidor y no un fallo.
    /// </summary>
    Task<int> DescargarAsync(CredencialesDeCarpetaRemota credenciales, string carpetaLocal,
        IProgress<string>? avance, CancellationToken ct);
}

/// <summary>
/// La descarga de verdad, por FTP/FTPS.
///
/// <para>Los tiempos de espera y el keep-alive son los que el escritorio tomó de la herramienta que
/// el equipo ya usa en producción, y no son adorno: los de fábrica de FluentFTP (15 s) disparaban
/// antes de tiempo en canales pasivos lentos, y eso era justo lo que provocaba los reinicios de
/// transferencia. El NOOP periódico evita que el servidor tire el canal de control por inactividad
/// mientras un archivo grande viaja por el de datos.</para>
/// </summary>
public sealed class DescargaDeCarpetaRemotaFtp : IDescargaDeCarpetaRemota
{
    /// <summary>Intentos por sitio, con reconexión limpia entre uno y otro.</summary>
    private const int MaxIntentos = 6;

    public async Task<int> DescargarAsync(CredencialesDeCarpetaRemota credenciales, string carpetaLocal,
        IProgress<string>? avance, CancellationToken ct)
    {
        var (host, tlsExplicito) = SepararEsquema(credenciales.Host);

        Exception? ultimo = null;
        for (int intento = 0; intento < MaxIntentos; intento++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (intento > 0) avance?.Report("      ↻ Reintentando la descarga del respaldo…");
                return await UnIntentoAsync(host, tlsExplicito, credenciales, carpetaLocal, avance, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Ante cualquier fallo se DESCARTA el cliente y se reconecta desde cero: la conexión
                // puede estar muerta aunque parezca viva, e insistir sobre una sesión degradada solo
                // gasta los intentos que quedan.
                ultimo = ex;
                if (intento == MaxIntentos - 1) break;

                int espera = Math.Min(30, (int)Math.Pow(2, intento)) * 1000;
                avance?.Report($"      ⚠ Respaldo: intento {intento + 1}/{MaxIntentos} falló ({ex.Message}). " +
                               $"Reintentando en {espera / 1000}s…");
                await Task.Delay(espera, ct);
            }
        }

        throw new IOException(
            $"Respaldo de la carpeta remota: agotados {MaxIntentos} intentos. Último error: {ultimo?.Message}",
            ultimo);
    }

    private static async Task<int> UnIntentoAsync(string host, bool tlsExplicito,
        CredencialesDeCarpetaRemota credenciales, string carpetaLocal,
        IProgress<string>? avance, CancellationToken ct)
    {
        await using var cliente = new AsyncFtpClient(
            host, credenciales.Usuario, credenciales.Contrasena, credenciales.Puerto);

        Configurar(cliente.Config, tlsExplicito);
        await cliente.AutoConnect(ct);

        if (!await cliente.DirectoryExists(credenciales.RutaRemota, ct))
        {
            avance?.Report("      · La carpeta remota no existe todavía: nada que respaldar.");
            return 0;
        }

        var resultados = await cliente.DownloadDirectory(carpetaLocal, credenciales.RutaRemota,
            FtpFolderSyncMode.Update, FtpLocalExists.Overwrite, FtpVerify.None, null, null, ct);

        var fallidos = resultados.Where(r => !r.IsSuccess).ToList();
        if (fallidos.Count > 0)
            throw new IOException(
                $"No se pudieron descargar {fallidos.Count} archivo(s) del respaldo (p. ej. {fallidos[0].Name}).");

        await cliente.Disconnect(ct);
        return resultados.Count(r => r.IsSuccess);
    }

    private static void Configurar(FtpConfig config, bool tlsExplicito)
    {
        config.EncryptionMode = tlsExplicito ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None;

        // Concesión consciente, heredada del escritorio: los servidores de despliegue suelen llevar
        // certificados autofirmados o de App Service. Sin esto no se puede publicar, pero se pierde
        // la protección contra un intermediario. No usar en redes no confiables.
        config.ValidateAnyCertificate = true;
        config.DataConnectionType = FtpDataConnectionType.AutoPassive;
        config.UploadDataType = FtpDataType.Binary;
        config.DownloadDataType = FtpDataType.Binary;

        config.ConnectTimeout = 30_000;
        config.ReadTimeout = 120_000;
        config.DataConnectionConnectTimeout = 30_000;
        config.DataConnectionReadTimeout = 120_000;

        config.SocketKeepAlive = true;
        config.Noop = true;
        config.NoopInterval = 45_000;
    }

    /// <summary>Quita el esquema y la barra final del host; devuelve además si pide TLS explícito.</summary>
    public static (string host, bool tlsExplicito) SepararEsquema(string? hostGuardado)
    {
        var host = (hostGuardado ?? "").Trim();
        bool tls = host.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase);

        if (tls) host = host[7..];
        else if (host.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase)) host = host[6..];

        return (host.TrimEnd('/'), tls);
    }
}
