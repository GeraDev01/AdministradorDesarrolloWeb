using FluentFTP;

namespace AdminWeb.Infrastructure.Integraciones;

/// <summary>
/// Política de conexión y reintentos para FTP/FTPS, portada del escritorio y tomada a su vez de la
/// herramienta Blobup que el equipo ya usa en producción. Los valores no son arbitrarios: los
/// timeouts por omisión de FluentFTP (15 s) disparaban prematuramente en canales pasivos lentos y
/// eso era lo que provocaba los reinicios de transferencia.
///
/// Dos niveles de reintento, deliberadamente:
///  · <b>por sitio</b>: ante cualquier fallo se DESCARTA el cliente (la conexión puede estar muerta
///    aunque parezca viva) y se reconecta desde cero;
///  · <b>por archivo</b>: reintentos baratos sin tirar la sesión, para el fallo puntual de un
///    archivo suelto.
/// Si un archivo agota sus intentos se aborta la pasada completa del sitio, para que el siguiente
/// intento arranque con una conexión limpia en vez de insistir sobre una sesión degradada.
///
/// <para><b>Lo único que cambia respecto al escritorio</b> es dónde se espera: allí el backoff
/// bloqueaba el hilo de la interfaz de quien desplegaba; aquí bloquea un hilo del servidor que no
/// tiene a nadie esperando delante, y por eso la espera se puede permitir el tope completo sin que
/// nadie vea la aplicación congelada.</para>
/// </summary>
public static class FtpRetryPolicy
{
    public const int MaxSiteAttempts = 6;
    public const int MaxFileAttempts = 4;

    /// <summary>Backoff exponencial 2s, 4s, 8s, 16s… con tope de 30 s.</summary>
    public static int BackoffMs(int intento) => Math.Min(30, (int)Math.Pow(2, intento)) * 1000;

    /// <summary>
    /// Configura un cliente con los timeouts y el keep-alive que aguantan una conexión lenta.
    /// El NOOP periódico evita que el servidor tire el canal de control por inactividad mientras
    /// un archivo grande viaja por el canal de datos.
    /// </summary>
    public static void Configurar(FtpConfig config, bool tlsExplicito)
    {
        config.EncryptionMode = tlsExplicito ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None;

        // Los servidores de despliegue suelen tener certificados autofirmados o de App Service.
        // Es una concesión consciente heredada del escritorio: sin esto no se puede publicar, pero
        // elimina la protección contra un intermediario. No usar en redes no confiables.
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
    public static (string host, bool tlsExplicito) PartirHost(string? hostCrudo)
    {
        var host = (hostCrudo ?? "").Trim();
        bool tls = host.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase);
        if (tls) host = host[7..];
        else if (host.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase)) host = host[6..];
        return (host.TrimEnd('/'), tls);
    }

    /// <summary>
    /// Ejecuta <paramref name="operacion"/> reintentando con backoff. Cada intento recibe el número
    /// de intento (base 0) para poder reportarlo. Propaga la cancelación sin esperar el backoff:
    /// quien cancela un despliegue no debe quedarse mirando media hora de reintentos.
    /// </summary>
    public static async Task<T> ConReintentosAsync<T>(
        Func<int, Task<T>> operacion,
        int maxIntentos,
        IProgress<string>? bitacora,
        string queEs,
        CancellationToken ct)
    {
        Exception? ultimo = null;
        for (int intento = 0; intento < maxIntentos; intento++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await operacion(intento);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                ultimo = ex;
                if (intento == maxIntentos - 1) break;
                int espera = BackoffMs(intento);
                bitacora?.Report(
                    $"      ⚠ {queEs}: intento {intento + 1}/{maxIntentos} falló ({ex.Message}). " +
                    $"Reintentando en {espera / 1000}s…");
                await Task.Delay(espera, ct);
            }
        }
        throw new IOException($"{queEs}: agotados {maxIntentos} intentos. Último error: {ultimo?.Message}", ultimo);
    }
}
