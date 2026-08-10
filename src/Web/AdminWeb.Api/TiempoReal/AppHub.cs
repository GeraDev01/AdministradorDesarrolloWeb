using AdminWeb.Application.Services;
using AdminWeb.Domain.Security;
using AdminWeb.Shared.Dtos.Despliegues;
using AdminWeb.Shared.Enums;
using AdminWeb.Shared.TiempoReal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AdminWeb.Api.TiempoReal;

/// <summary>
/// La conexión en vivo con cada navegador abierto.
///
/// <para><b>Por qué existe.</b> En el escritorio, «estar conectado» era que la aplicación estuviera
/// abierta —aunque fuera escondida en la bandeja— y eso lo sostenía un latido cada dos minutos. En
/// un navegador esa idea no se sostiene: la pestaña se cierra sin avisar y no hay proceso que siga
/// vivo. Aquí «conectado» es <b>tener esta conexión abierta</b>, que además avisa al instante cuando
/// se corta, en lugar de esperar los diez minutos de tolerancia que hacían falta antes.</para>
///
/// <para><b>Lo que NO cambia</b>: la asistencia oficial la sigue marcando cada quien a mano y es la
/// que cuenta. Esto es telemetría, igual que lo era en el escritorio.</para>
///
/// Un grupo por usuario para poder avisarle solo a él, y uno para el tablero de presencia. Al
/// desconectar, la jornada se cierra de inmediato.
/// </summary>
[Authorize]
public class AppHub(
    PresenceService presencia,
    WorkSessionService cronometros,
    ICurrentUser usuario,
    RegistroDeConexiones conexiones,
    ILogger<AppHub> log) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (usuario.UserId is int id)
            await Groups.AddToGroupAsync(Context.ConnectionId, Grupos.Usuario(id));

        if (usuario.IsAdmin)
            await Groups.AddToGroupAsync(Context.ConnectionId, Grupos.Administradores);

        if (usuario.UserId is int quien) conexiones.Entra(quien);

        // Abrir la conexión ES entrar: no hace falta que la pantalla lo pida aparte. Se llama SIEMPRE
        // y no solo en la primera conexión, porque EntrarAsync ya sabe reutilizar la jornada abierta;
        // llamarlo también en la segunda pestaña es lo que refresca el latido de quien tenía la
        // primera en segundo plano.
        try { await presencia.EntrarAsync(DescribirOrigen(), Context.ConnectionAborted); }
        catch (Exception ex) { log.LogWarning(ex, "No se pudo abrir la jornada al conectar."); }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? excepcion)
    {
        // Cerrar aquí es la ventaja de tener conexión: en el escritorio había que esperar a que
        // alguien dejara de latir diez minutos para darlo por ido.
        //
        // Pero solo si era la ÚLTIMA pestaña. Antes se cerraba con cualquiera, así que cerrar la del
        // foro dejando abierta la del sprint daba a esa persona por ida y por terminada su jornada.
        bool ultima = usuario.UserId is not int quien || conexiones.Sale(quien);

        if (ultima)
        {
            try { await presencia.SalirAsync(); }
            catch (Exception ex) { log.LogWarning(ex, "No se pudo cerrar la jornada al desconectar."); }
        }

        await base.OnDisconnectedAsync(excepcion);
    }

    /// <summary>
    /// Latido de presencia. Lo manda el navegador cada pocos minutos aunque la conexión siga viva:
    /// una conexión puede quedarse colgada sin que ninguno de los dos lados se entere, y el latido es
    /// lo que permite distinguir «sigue ahí» de «se le murió el equipo».
    /// </summary>
    public async Task LatidoPresencia()
    {
        try { await presencia.LatirAsync(Context.ConnectionAborted); }
        catch (Exception ex) { log.LogWarning(ex, "Latido de presencia fallido."); }
    }

    /// <summary>
    /// Latido del cronómetro que esté corriendo.
    ///
    /// Es lo que hace que cerrar la pestaña no tire el trabajo del día. En el escritorio el tiempo
    /// se consolidaba al cerrar la ventana y el cierre sucio era la excepción; en el navegador es lo
    /// normal —se cierra la pestaña, se duerme el portátil, se cae el wifi—, así que en vez de
    /// descartar el tramo colgante se consolida hasta el último latido.
    /// </summary>
    public async Task LatidoCronometro(int requerimientoId, int actividadId)
    {
        if (usuario.DeveloperId is not int dev) return;

        try
        {
            var objetivo = requerimientoId > 0
                ? WorkTarget.Requerimiento(requerimientoId)
                : WorkTarget.Actividad(actividadId);

            await cronometros.RegistrarLatidoAsync(dev, objetivo, Context.ConnectionAborted);
        }
        catch (Exception ex) { log.LogWarning(ex, "Latido de cronómetro fallido."); }
    }

    /// <summary>
    /// Cambia el estado propio (disponible, ocupado, comiendo…) y lo difunde al tablero.
    ///
    /// El estado NO se historiza, y eso es una decisión de producto que viene del escritorio y se
    /// conserva: un registro minutado de las pausas de alguien es vigilancia, no asistencia.
    /// </summary>
    public async Task CambiarEstado(PresenceState estado, string? nota)
    {
        var (ok, _) = await presencia.CambiarEstadoAsync(estado, nota, Context.ConnectionAborted);
        if (ok) await Clients.Group(Grupos.Administradores).SendAsync(Eventos.PresenciaCambiada);
    }

    /// <summary>
    /// Empieza a mirar un despliegue.
    ///
    /// <para><b>Mirar y ejecutar son cosas distintas</b>, y aquí está la diferencia de fondo con el
    /// escritorio: allí el despliegue ERA el proceso de quien lo lanzó, así que cerrarlo lo mataba.
    /// Aquí corre en el servidor y esto solo suscribe la pestaña a su relato: entrar y salir del
    /// grupo no arranca, no pausa y no cancela nada.</para>
    ///
    /// <para>Se exige el rol del área de despliegues porque por este canal viajan rutas remotas y
    /// respuestas de servidores ajenos, que no son cosa de todo el mundo. Y se vuelve a llamar
    /// después de cada reconexión: los grupos no sobreviven a que se caiga la conexión.</para>
    /// </summary>
    public async Task SeguirDespliegue(int jobId)
    {
        if (!usuario.IsAdmin && !usuario.IsOperaciones) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, Grupos.Despliegue(jobId));
    }

    /// <summary>Deja de mirarlo. Lo dice la pantalla al cerrarse; <b>no cancela el despliegue</b>.</summary>
    public async Task DejarDespliegue(int jobId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, Grupos.Despliegue(jobId));

    /// <summary>
    /// De dónde se conectó. Sustituye al «MÁQUINA\usuario» del escritorio, que en la web sería
    /// siempre el nombre del servidor y no distinguiría a nadie.
    /// </summary>
    private string DescribirOrigen()
    {
        var ctx = Context.GetHttpContext();
        if (ctx == null) return "(navegador)";

        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
        var agente = ctx.Request.Headers.UserAgent.ToString();
        if (agente.Length > 100) agente = agente[..100];

        return string.IsNullOrWhiteSpace(agente) ? ip : $"{ip} · {agente}";
    }
}

/// <summary>
/// Por dónde sale el avance de un despliegue hacia los navegadores que lo están mirando.
///
/// <para>Implementa el contrato que declara la capa de aplicación (<see cref="IAvisosDeDespliegue"/>)
/// y vive aquí porque el canal en vivo es de la capa web: el servicio dice QUÉ hay que contar, esto
/// sabe POR DÓNDE. Es el mismo arreglo que <c>IRequestOrigin</c>.</para>
///
/// <para>Es <b>Singleton-seguro</b>, y tiene que serlo: quien lo usa es el ejecutor de despliegues,
/// que también es singleton porque un despliegue sobrevive a la petición que lo pidió.
/// <c>IHubContext</c> está pensado exactamente para eso —hablarle al hub desde fuera de una
/// conexión—, que es la situación de un trabajo de fondo.</para>
/// </summary>
public class AvisosDeDespliegueEnVivo(IHubContext<AppHub> hub) : IAvisosDeDespliegue
{
    public Task AvanceAsync(int jobId, AvanceDeDespliegueDto avance) =>
        hub.Clients.Group(Grupos.Despliegue(jobId)).SendAsync(Eventos.DespliegueAvanzo, avance);

    public Task RegistroAsync(int jobId, RenglonDeBitacoraDto renglon) =>
        hub.Clients.Group(Grupos.Despliegue(jobId)).SendAsync(Eventos.DespliegueRegistro, renglon);

    public Task FinAsync(int jobId, FinDeDespliegueDto fin) =>
        hub.Clients.Group(Grupos.Despliegue(jobId)).SendAsync(Eventos.DespliegueTermino, fin);
}
