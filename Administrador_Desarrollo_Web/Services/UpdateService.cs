using Microsoft.Extensions.Logging;
using Velopack;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Cómo se puede actualizar esta instalación.</summary>
public enum ModoActualizacion
{
    /// <summary>No hay nada configurado, o esta copia no se instaló con Velopack: no se ofrece nada.</summary>
    Ninguno = 0,
    /// <summary>Copia portátil: se avisa y se da el enlace, pero la descarga la hace la persona.</summary>
    AvisoManual = 1,
    /// <summary>Instalada con Velopack y con feed: se descarga y se aplica sola.</summary>
    Automatica = 2
}

/// <summary>
/// Lo que hay que ofrecerle a la persona, ya decidido. Lleva dentro el <paramref name="Info"/> del
/// feed: la ventana descarga EXACTAMENTE lo que anunció, y no lo que el servicio tenga guardado
/// (que es Singleton y podría haber cambiado entre medias).
/// </summary>
public sealed record OfertaActualizacion(
    ModoActualizacion Modo, string VersionTexto, string? Novedades, string? Url, UpdateInfo? Info = null);

/// <summary>
/// Actualización de la aplicación, con DOS caminos según cómo se haya repartido esta copia:
///
///  · <b>Instalada con Velopack</b> — se consulta el feed y se descarga en segundo plano. Las
///    actualizaciones son DELTA: baja lo que cambió, no los 150 MB del ejecutable entero.
///  · <b>Copia portátil</b> (el .exe único) — Velopack no puede actualizarla: un ejecutable suelto
///    no tiene dónde instalarse ni a qué volver. Ahí se cae al aviso manual, leyendo la versión
///    publicada de AppSettings.
///
/// CUÁNDO SE APLICA (esto es lo delicado y costó tres intentos hacerlo bien):
/// el updater de Velopack solo espera <b>60 segundos</b> a ver morir a este proceso, y esta
/// aplicación vive en la bandeja durante horas. Por eso NO se arma al terminar la descarga —ahí se
/// rendiría mucho antes de que alguien cierre— sino en el cierre REAL, desde
/// <c>MainForm.OnFormClosing</c>. Y siempre se arma UNO SOLO: dos updaters compitiendo por el
/// mismo bloqueo dejan la instalación a medias.
/// </summary>
public class UpdateService
{
    private readonly SettingsService _settings;
    private readonly ILogger<UpdateService> _logger;

    private UpdateManager? _mgr;
    /// <summary>Con qué feed se construyó <see cref="_mgr"/>, para rehacerlo si lo cambian.</summary>
    private string? _feedDeMgr;
    /// <summary>Al aplicar en el cierre, además hay que volver a abrir la aplicación.</summary>
    private bool _reiniciarAlAplicar;

    /// <summary>Dónde vive el feed de Velopack (la carpeta o URL con los releases.*.json).</summary>
    public const string KeyFeedUrl = "VelopackFeedUrl";

    public UpdateService(SettingsService settings, ILogger<UpdateService> logger)
    {
        _settings = settings; _logger = logger;
    }

    /// <summary>Hay una descarga en marcha: cerrar ahora la aborta y habría que bajarla de cero.</summary>
    public bool DescargaEnCurso { get; private set; }

    /// <summary>Versión ya descargada esperando a que la aplicación cierre, o null.</summary>
    public string? VersionListaParaAplicar
    {
        get { try { return Gestor()?.UpdatePendingRestart?.Version.ToString(); } catch { return null; } }
    }

    /// <summary>
    /// True si esta copia se instaló con Velopack. En una copia portátil o corriendo desde Visual
    /// Studio es false, y entonces no hay actualización automática posible.
    /// </summary>
    public bool EsInstalacionGestionada
    {
        get
        {
            try { return Gestor()?.IsInstalled == true; }
            catch (Exception ex) { _logger.LogDebug(ex, "No se pudo determinar si la instalación es gestionada"); return false; }
        }
    }

    private UpdateManager? Gestor()
    {
        var feed = _settings.Get(KeyFeedUrl)?.Trim();
        if (string.IsNullOrWhiteSpace(feed)) return null;

        // Se rehace si el feed cambió: corregir una errata en Configuración debe surtir efecto sin
        // reiniciar — el servicio es Singleton y el proceso vive días en la bandeja.
        if (_mgr != null && string.Equals(feed, _feedDeMgr, StringComparison.OrdinalIgnoreCase))
            return _mgr;

        _mgr = new UpdateManager(feed);
        _feedDeMgr = feed;
        return _mgr;
    }

    /// <summary>
    /// Qué se le puede ofrecer a esta persona ahora mismo, o null si nada. No lanza nunca: un fallo
    /// de red al arrancar no puede impedir trabajar.
    /// </summary>
    public async Task<OfertaActualizacion?> BuscarAsync(string? yaAvisada, CancellationToken ct = default)
    {
        bool gestionada = false;
        try
        {
            if (Gestor() is { IsInstalled: true } mgr)
            {
                gestionada = true;
                var info = await mgr.CheckForUpdatesAsync().WaitAsync(ct);
                if (info == null) return null;

                var v = info.TargetFullRelease.Version.ToString();
                // vpk no recibe --releaseNotes, así que NotesMarkdown suele venir vacío: se cae a
                // lo que el administrador capturó en Configuración, que ya alimenta el otro camino.
                var notas = info.TargetFullRelease.NotesMarkdown;
                if (string.IsNullOrWhiteSpace(notas)) notas = _settings.Get(UpdateNotice.KeyReleaseNotes);

                return new OfertaActualizacion(ModoActualizacion.Automatica, v,
                    string.IsNullOrWhiteSpace(notas) ? null : notas, Url: null, Info: info);
            }
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo consultar el feed de actualizaciones");
        }

        // Una copia GESTIONADA no cae al aviso manual aunque el feed falle: ofrecerle el .exe
        // portátil a quien tiene una instalación Velopack lo llevaría a descomprimirlo encima y
        // romper la instalación. Mejor no decir nada y reintentar en el próximo arranque.
        if (gestionada) return null;

        var aviso = UpdateNotice.Evaluar(
            _settings.Get(UpdateNotice.KeyLatestVersion),
            _settings.Get(UpdateNotice.KeyDownloadUrl),
            _settings.Get(UpdateNotice.KeyReleaseNotes),
            AppVersion.Actual, yaAvisada);

        return aviso == null ? null
            : new OfertaActualizacion(ModoActualizacion.AvisoManual, aviso.VersionTexto, aviso.Novedades, aviso.Url);
    }

    /// <summary>
    /// Descarga la actualización de <paramref name="oferta"/> y la deja preparada en disco. NO arma
    /// el updater todavía (ver la nota de la clase): eso ocurre al cerrar.
    /// </summary>
    public async Task<(bool ok, string mensaje)> DescargarAsync(
        OfertaActualizacion oferta, IProgress<int>? avance = null, CancellationToken ct = default)
    {
        if (oferta.Info == null || Gestor() is not { } mgr)
            return (false, "Esta copia no se puede actualizar sola.");

        DescargaEnCurso = true;
        try
        {
            await mgr.DownloadUpdatesAsync(oferta.Info, p => avance?.Report(p), ct);
            return (true, "Descargada.");
        }
        catch (OperationCanceledException) { return (false, "Descarga cancelada."); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falló la descarga de la actualización");
            return (false, $"No se pudo descargar: {ex.Message}");
        }
        finally { DescargaEnCurso = false; }
    }

    /// <summary>
    /// Pide que, al aplicar en el cierre, la aplicación se vuelva a abrir. Lo llama «Reiniciar
    /// ahora»; el cierre en sí lo hace la ventana principal por su camino de siempre, que es donde
    /// se avisa del despliegue en curso, se cierra la jornada y se pausan los cronómetros.
    /// </summary>
    public void PedirReinicioTrasAplicar() => _reiniciarAlAplicar = true;

    /// <summary>
    /// Arma el updater para que se aplique en cuanto este proceso termine. Se llama UNA sola vez,
    /// desde el cierre real de la aplicación. Devuelve false si no había nada preparado.
    /// </summary>
    public bool AplicarAlSalir()
    {
        UpdateManager? mgr;
        try { mgr = Gestor(); } catch { return false; }
        if (mgr?.UpdatePendingRestart is not { } paquete) return false;

        try
        {
            mgr.WaitExitThenApplyUpdates(paquete, silent: true, restart: _reiniciarAlAplicar);
            _logger.LogInformation("Actualización {v} preparada; se aplica al terminar el proceso.", paquete.Version);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo programar la actualización al cerrar");
            return false;
        }
    }
}
