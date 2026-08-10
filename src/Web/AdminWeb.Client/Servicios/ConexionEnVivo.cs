using AdminWeb.Shared.Dtos.Despliegues;
using AdminWeb.Shared.Enums;
using AdminWeb.Shared.TiempoReal;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace AdminWeb.Client.Servicios;

/// <summary>
/// La conexión en vivo con el servidor. Una sola para toda la aplicación.
///
/// <para><b>Es lo que sustituye a «la aplicación abierta en la bandeja».</b> En el escritorio,
/// estar conectado era tener el proceso corriendo; aquí es tener esta conexión abierta. Y mientras
/// esté abierta hace dos cosas que el escritorio hacía con temporizadores propios: late para que se
/// sepa que la persona sigue ahí, y late por el cronómetro para que cerrar la pestaña no tire el
/// trabajo del día.</para>
///
/// <para>Se conecta sola al entrar y se corta sola al salir. Una desconexión NO es un error que
/// haya que enseñar: el wifi se cae, el portátil se duerme, y SignalR reconecta por su cuenta. Lo
/// que se enseña —y solo si tarda— es que la conexión está caída, para que nadie crea que su
/// cronómetro sigue contando cuando no.</para>
/// </summary>
public class ConexionEnVivo(NavigationManager navegacion, ILogger<ConexionEnVivo> log) : IAsyncDisposable
{
    private HubConnection? _hub;
    private Timer? _latido;

    /// <summary>Qué cronómetro hay que mantener vivo, si es que hay alguno.</summary>
    private (int? requerimiento, int? actividad)? _cronometro;

    /// <summary>Avisa a quien lo escuche de que el estado de la conexión cambió.</summary>
    public event Action? EstadoCambio;

    /// <summary>Un aviso nuevo para esta persona: el menú debe repintar su contador.</summary>
    public event Action? AvisoNuevo;

    /// <summary>El tablero de presencia quedó viejo.</summary>
    public event Action? PresenciaCambiada;

    // ── Despliegues ──────────────────────────────────────────────────────────────
    //
    // El despliegue corre en el SERVIDOR, no aquí. Esta pantalla lo comanda y lo mira; lo que ve
    // llega por estos tres avisos, que son los dos IProgress del escritorio (bitácora y porcentaje)
    // más el cierre. Suscribirse y darse de baja no arranca ni cancela nada: cerrar la pestaña deja
    // el despliegue corriendo, que es justo la mejora sobre el escritorio.

    /// <summary>Cambió el porcentaje, el servidor o el detalle de un despliegue que se está mirando.</summary>
    public event Action<AvanceDeDespliegueDto>? DespliegueAvanzo;

    /// <summary>Un renglón nuevo de la bitácora de un despliegue.</summary>
    public event Action<RenglonDeBitacoraDto>? DespliegueRegistro;

    /// <summary>Un despliegue terminó.</summary>
    public event Action<FinDeDespliegueDto>? DespliegueTermino;

    /// <summary>
    /// Los despliegues que esta pestaña está mirando.
    ///
    /// Se recuerdan porque los grupos de SignalR NO sobreviven a una reconexión: sin esto, un wifi
    /// que parpadea deja la consola muda para siempre y quien mira cree que el despliegue se colgó.
    /// </summary>
    private readonly HashSet<int> _desplieguesSeguidos = [];

    public bool Conectado => _hub?.State == HubConnectionState.Connected;

    /// <summary>Reconectando: la interfaz puede avisarlo sin tratarlo como error.</summary>
    public bool Reconectando => _hub?.State is HubConnectionState.Reconnecting or HubConnectionState.Connecting;

    public async Task IniciarAsync()
    {
        if (_hub != null) return;

        _hub = new HubConnectionBuilder()
            .WithUrl(navegacion.ToAbsoluteUri(RutasDeTiempoReal.Hub))
            // Reintentos crecientes y sin tope: quien deja el portátil dormido toda la noche debe
            // encontrárselo reconectado, no con un error de hace ocho horas.
            .WithAutomaticReconnect(new ReintentosCrecientes())
            .Build();

        _hub.On(Eventos.AvisoNuevo, () => AvisoNuevo?.Invoke());
        _hub.On(Eventos.ContadoresCambiaron, () => AvisoNuevo?.Invoke());
        _hub.On(Eventos.PresenciaCambiada, () => PresenciaCambiada?.Invoke());

        _hub.On<AvanceDeDespliegueDto>(Eventos.DespliegueAvanzo, a => DespliegueAvanzo?.Invoke(a));
        _hub.On<RenglonDeBitacoraDto>(Eventos.DespliegueRegistro, r => DespliegueRegistro?.Invoke(r));
        _hub.On<FinDeDespliegueDto>(Eventos.DespliegueTermino, f => DespliegueTermino?.Invoke(f));

        _hub.Reconnecting += _ => { EstadoCambio?.Invoke(); return Task.CompletedTask; };
        _hub.Reconnected += async _ =>
        {
            // Volver a apuntarse a los despliegues que se estaban mirando: la reconexión trae una
            // conexión NUEVA y el servidor no sabe a qué grupos pertenecía la anterior. Lo que se
            // perdió mientras tanto lo recupera la pantalla pidiendo la foto completa del trabajo.
            await ReengancharDesplieguesAsync();
            EstadoCambio?.Invoke();
        };
        _hub.Closed += _ => { EstadoCambio?.Invoke(); return Task.CompletedTask; };

        try
        {
            await _hub.StartAsync();
            EstadoCambio?.Invoke();
        }
        catch (Exception ex)
        {
            // Sin conexión en vivo la aplicación sigue funcionando: solo se pierde el tiempo real.
            // Por eso esto se registra y no se le echa encima al usuario.
            log.LogWarning(ex, "No se pudo abrir la conexión en vivo.");
        }

        // El temporizador es único y siempre corre: manda el latido de presencia y, si hay
        // cronómetro, también el suyo. Dos temporizadores separados era lo que hacía el escritorio y
        // era una fuente segura de que uno se quedara vivo al cerrar la pantalla.
        _latido = new Timer(async _ => await LatirAsync(), null,
            WorkSessionServiceIntervalos.Latido, WorkSessionServiceIntervalos.Latido);
    }

    /// <summary>
    /// Empieza a latir por un cronómetro. Lo llama la pantalla que lo arrancó.
    ///
    /// El latido sigue aunque se navegue a otra pantalla, y eso es deliberado: el cronómetro no se
    /// para por cambiar de vista, igual que en el escritorio no se paraba por cambiar de pestaña.
    /// </summary>
    public void SeguirCronometro(int? requerimientoId, int? actividadId) =>
        _cronometro = (requerimientoId, actividadId);

    /// <summary>Deja de latir por el cronómetro (al pausarlo o detenerlo).</summary>
    public void OlvidarCronometro() => _cronometro = null;

    private async Task LatirAsync()
    {
        if (_hub is not { State: HubConnectionState.Connected }) return;

        try
        {
            await _hub.SendAsync(Acciones.LatidoPresencia);

            if (_cronometro is { } c)
                // El hub espera dos enteros; el que no aplica va en 0. Nulos por SignalR obligan a
                // declarar los parámetros como nullable en el servidor y complican el contrato para
                // no ganar nada.
                await _hub.SendAsync(Acciones.LatidoCronometro, c.requerimiento ?? 0, c.actividad ?? 0);
        }
        catch (Exception ex)
        {
            // Un latido perdido no es nada: el siguiente llega en dos minutos y la consolidación
            // usa el ÚLTIMO recibido, no la cuenta de los que llegaron.
            log.LogDebug(ex, "Latido no entregado.");
        }
    }

    /// <summary>Cambia el estado propio de presencia (disponible, ocupado, comiendo…).</summary>
    public async Task CambiarEstadoAsync(PresenceState estado, string? nota = null)
    {
        if (_hub is not { State: HubConnectionState.Connected }) return;
        try { await _hub.SendAsync(Acciones.CambiarEstado, estado, nota); }
        catch (Exception ex) { log.LogWarning(ex, "No se pudo cambiar el estado de presencia."); }
    }

    // ── Despliegues ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Empieza a recibir el avance de un despliegue. <b>Mirar no es ejecutar</b>: esto no arranca
    /// nada, y dejar de mirar tampoco cancela nada.
    /// </summary>
    public async Task SeguirDespliegueAsync(int jobId)
    {
        _desplieguesSeguidos.Add(jobId);
        if (_hub is not { State: HubConnectionState.Connected }) return;

        try { await _hub.SendAsync(Acciones.SeguirDespliegue, jobId); }
        catch (Exception ex)
        {
            // La pantalla sigue siendo útil sin esto: pierde el vivo, no el despliegue. Y al
            // reconectar se reengancha sola porque el identificador ya quedó apuntado.
            log.LogWarning(ex, "No se pudo seguir el despliegue {JobId} en vivo.", jobId);
        }
    }

    /// <summary>Deja de mirar un despliegue. <b>No lo cancela</b>: para eso está su botón.</summary>
    public async Task DejarDespliegueAsync(int jobId)
    {
        _desplieguesSeguidos.Remove(jobId);
        if (_hub is not { State: HubConnectionState.Connected }) return;

        try { await _hub.SendAsync(Acciones.DejarDespliegue, jobId); }
        catch (Exception ex) { log.LogDebug(ex, "No se pudo dejar de seguir el despliegue {JobId}.", jobId); }
    }

    private async Task ReengancharDesplieguesAsync()
    {
        foreach (var jobId in _desplieguesSeguidos.ToList())
        {
            try { await _hub!.SendAsync(Acciones.SeguirDespliegue, jobId); }
            catch (Exception ex) { log.LogWarning(ex, "No se pudo reenganchar el despliegue {JobId}.", jobId); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_latido != null) await _latido.DisposeAsync();
        if (_hub != null) await _hub.DisposeAsync();
    }

    /// <summary>
    /// Espera cada vez más entre reintentos, y no se rinde. El tope de un minuto evita machacar al
    /// servidor cuando lo que está caído es el servidor y no la red de una persona.
    /// </summary>
    private sealed class ReintentosCrecientes : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext contexto) => contexto.PreviousRetryCount switch
        {
            0 => TimeSpan.Zero,
            1 => TimeSpan.FromSeconds(2),
            2 => TimeSpan.FromSeconds(10),
            3 => TimeSpan.FromSeconds(30),
            _ => TimeSpan.FromMinutes(1)
        };
    }
}

/// <summary>
/// El intervalo del latido, copiado del servidor.
///
/// Es un valor y no una referencia a <c>WorkSessionService</c> porque el cliente no puede ver la
/// capa de aplicación —solo Shared—, y meterlo en Shared por un número obligaría a arrastrar allí
/// media configuración del cronómetro. Lo que importa es que sea holgadamente menor que la
/// tolerancia sin latido del servidor (diez minutos), y dos lo es.
/// </summary>
internal static class WorkSessionServiceIntervalos
{
    public static readonly TimeSpan Latido = TimeSpan.FromMinutes(2);
}
