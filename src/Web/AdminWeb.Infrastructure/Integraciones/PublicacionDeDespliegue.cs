using FluentFTP;

namespace AdminWeb.Infrastructure.Integraciones;

/// <summary>
/// A dónde se publica y con qué credenciales. <b>Vive y muere dentro del servidor</b>: la contraseña
/// llega descifrada aquí y no sale de esta capa ni hacia un DTO, ni hacia la bitácora, ni hacia un
/// mensaje de error (ver <see cref="PublicacionFtp"/>).
/// </summary>
public sealed record DestinoDeDespliegue(
    string Nombre, string Host, int Puerto, string Usuario, string Contrasena, string RutaRemota);

/// <summary>Un archivo del paquete ya leído en memoria: su nombre relativo y su contenido.</summary>
public sealed record ArchivoDelPaquete(string RutaRelativa, byte[] Contenido);

/// <summary>
/// La frontera con el FTP.
///
/// <para><b>Existe para que el despliegue se pueda probar sin red.</b> En el escritorio la
/// conversación con FluentFTP estaba incrustada en el propio servicio de despliegue, así que
/// cualquier prueba de la lógica —qué se respalda, cuándo se marca un servidor como fallido, cómo
/// se cierra el trabajo al cancelar— habría exigido un servidor FTP de verdad. Aquí la conversación
/// está detrás de esta interfaz y la lógica se prueba con una implementación de mentira.</para>
///
/// <para>Solo dos operaciones, que son las dos que el despliegue necesita: bajar la carpeta remota
/// para respaldarla y subir el paquete encima.</para>
/// </summary>
public interface IPublicacionDeDespliegue
{
    /// <summary>
    /// Sube todos los archivos del paquete a la ruta remota del destino. Devuelve cuántos quedaron
    /// publicados.
    /// </summary>
    /// <param name="avance">
    /// Se llama con (archivos ya subidos, ruta relativa del que va ahora). Es lo que alimenta el
    /// porcentaje y el renglón de estado de la pantalla.
    /// </param>
    Task<int> PublicarAsync(
        DestinoDeDespliegue destino,
        IReadOnlyList<ArchivoDelPaquete> archivos,
        IProgress<string> bitacora,
        Action<int, string> avance,
        CancellationToken ct);

    /// <summary>
    /// Baja la carpeta remota completa a <paramref name="carpetaLocal"/> para poder respaldarla.
    /// Devuelve cuántos archivos se bajaron; <b>0 significa que no había nada</b> (primer despliegue
    /// a ese servidor), que no es un error.
    /// </summary>
    Task<int> DescargarCarpetaAsync(
        DestinoDeDespliegue destino,
        string carpetaLocal,
        IProgress<string> bitacora,
        CancellationToken ct);
}

/// <summary>
/// La implementación de verdad, con FluentFTP. Es el trozo del <c>DeploymentService</c> del
/// escritorio que hablaba con el servidor, movido aquí tal cual: mismos reintentos a dos niveles,
/// misma subida desde memoria sin tocar disco, misma configuración de conexión.
///
/// <para><b>Lo que se añadió al portarlo</b>: todo lo que sale de aquí —bitácora y mensajes de
/// error— pasa por un filtro que borra la contraseña. En el escritorio el riesgo era teórico porque
/// el texto se quedaba en la ventana de quien desplegaba; aquí ese mismo texto viaja por el canal en
/// vivo a varios navegadores y se guarda en <c>DeploymentLogEntry</c>, que lee más gente. Un
/// servidor FTP que devuelve la línea de comando en su respuesta de error es suficiente para
/// filtrarla, y no es una hipótesis rebuscada.</para>
/// </summary>
public sealed class PublicacionFtp : IPublicacionDeDespliegue
{
    public async Task<int> PublicarAsync(
        DestinoDeDespliegue destino,
        IReadOnlyList<ArchivoDelPaquete> archivos,
        IProgress<string> bitacora,
        Action<int, string> avance,
        CancellationToken ct)
    {
        var (host, tls) = FtpRetryPolicy.PartirHost(destino.Host);
        var limpia = new BitacoraSinSecretos(bitacora, destino.Contrasena);

        // Los ya subidos se recuerdan ENTRE intentos de sitio: si la conexión se cae al archivo 900
        // de 1000, el reintento no vuelve a empezar por el primero.
        var subidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await FtpRetryPolicy.ConReintentosAsync(async intento =>
            {
                if (intento > 0)
                    limpia.Report($"  ↻  Reintento {intento + 1}/{FtpRetryPolicy.MaxSiteAttempts} sobre {host} " +
                                  $"({subidos.Count}/{archivos.Count} ya subidos)");

                await using var cliente = new AsyncFtpClient(host, destino.Usuario, destino.Contrasena, destino.Puerto);
                FtpRetryPolicy.Configurar(cliente.Config, tls);
                await cliente.AutoConnect(ct);
                limpia.Report($"  ✓  Conectado a {host}:{destino.Puerto}");

                foreach (var archivo in archivos)
                {
                    ct.ThrowIfCancellationRequested();
                    if (subidos.Contains(archivo.RutaRelativa)) continue;   // ya fue en una pasada anterior

                    var remoto = destino.RutaRemota.TrimEnd('/') + "/" + archivo.RutaRelativa;
                    avance(subidos.Count, archivo.RutaRelativa);

                    // Reintento barato por archivo, sin tirar la sesión.
                    await FtpRetryPolicy.ConReintentosAsync(async _ =>
                    {
                        if (!cliente.IsConnected) await cliente.AutoConnect(ct);
                        using var flujo = new MemoryStream(archivo.Contenido, writable: false);
                        var estado = await cliente.UploadStream(flujo, remoto, FtpRemoteExists.Overwrite,
                            createRemoteDir: true, progress: null, token: ct);
                        if (estado == FtpStatus.Failed)
                            throw new IOException($"El servidor rechazó {archivo.RutaRelativa}");
                        return estado;
                    }, FtpRetryPolicy.MaxFileAttempts, limpia, archivo.RutaRelativa, ct);

                    subidos.Add(archivo.RutaRelativa);
                    avance(subidos.Count, archivo.RutaRelativa);
                }

                await cliente.Disconnect(ct);
                return true;
            }, FtpRetryPolicy.MaxSiteAttempts, limpia, $"servidor {destino.Nombre}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Depurar(ex, destino.Contrasena);
        }

        return subidos.Count;
    }

    public async Task<int> DescargarCarpetaAsync(
        DestinoDeDespliegue destino,
        string carpetaLocal,
        IProgress<string> bitacora,
        CancellationToken ct)
    {
        var (host, tls) = FtpRetryPolicy.PartirHost(destino.Host);
        var limpia = new BitacoraSinSecretos(bitacora, destino.Contrasena);

        try
        {
            return await FtpRetryPolicy.ConReintentosAsync(async intento =>
            {
                if (intento > 0) limpia.Report("      ↻ Reintentando la descarga del respaldo…");

                await using var cliente = new AsyncFtpClient(host, destino.Usuario, destino.Contrasena, destino.Puerto);
                FtpRetryPolicy.Configurar(cliente.Config, tls);
                await cliente.AutoConnect(ct);

                // Carpeta remota vacía o inexistente: no hay nada que respaldar y no es un error
                // (es el primer despliegue a ese servidor).
                if (!await cliente.DirectoryExists(destino.RutaRemota, ct))
                {
                    limpia.Report("      · La carpeta remota no existe todavía: nada que respaldar.");
                    return 0;
                }

                var resultados = await cliente.DownloadDirectory(carpetaLocal, destino.RutaRemota,
                    FtpFolderSyncMode.Update, FtpLocalExists.Overwrite, FtpVerify.None, null, null, ct);

                // Un respaldo incompleto es peor que ninguno: aparenta red de seguridad. Se falla y
                // el despliegue no continúa, que es la regla que traía el escritorio.
                var fallidos = resultados.Where(r => !r.IsSuccess).ToList();
                if (fallidos.Count > 0)
                    throw new IOException(
                        $"No se pudieron descargar {fallidos.Count} archivo(s) del respaldo (p.ej. {fallidos[0].Name}).");

                await cliente.Disconnect(ct);
                return resultados.Count(r => r.IsSuccess);
            }, FtpRetryPolicy.MaxSiteAttempts, limpia, $"respaldo de {destino.Nombre}", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Depurar(ex, destino.Contrasena);
        }
    }

    /// <summary>
    /// Devuelve el fallo con la contraseña borrada del mensaje. Se conserva la excepción original
    /// como interna para no perder el rastro en el registro del servidor, que no llega al navegador.
    /// </summary>
    private static IOException Depurar(Exception ex, string contrasena) =>
        new(BitacoraSinSecretos.Borrar(ex.Message, contrasena), ex);

    /// <summary>
    /// Envuelve la bitácora del despliegue para que ninguna línea pueda llevar la contraseña. La
    /// alternativa —confiar en que ninguna librería ni ningún servidor la devuelva jamás en un
    /// mensaje— es una apuesta que solo se puede perder una vez.
    /// </summary>
    private sealed class BitacoraSinSecretos(IProgress<string> interior, string contrasena) : IProgress<string>
    {
        public void Report(string valor) => interior.Report(Borrar(valor, contrasena));

        public static string Borrar(string texto, string contrasena) =>
            string.IsNullOrEmpty(contrasena) || string.IsNullOrEmpty(texto)
                ? texto
                : texto.Replace(contrasena, "«contraseña oculta»", StringComparison.Ordinal);
    }
}
