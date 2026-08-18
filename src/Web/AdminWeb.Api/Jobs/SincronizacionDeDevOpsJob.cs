using AdminWeb.Application.Services;
using AdminWeb.Infrastructure.Integraciones;

namespace AdminWeb.Api.Jobs;

/// <summary>
/// Sincroniza con Azure DevOps cada tanto y trae al pool los work items nuevos, como actividades sin
/// clasificar que esperan al líder.
///
/// <para><b>Por qué hacía falta.</b> En la web, hasta ahora, los tickets solo entraban cuando alguien
/// pulsaba «Sincronizar»: no había ningún trabajo de fondo de DevOps. El ajuste «cada cuántos minutos
/// se sincroniza» existía y la pantalla de configuración lo ofrecía, pero <b>no lo leía nadie</b> —lo
/// honraba el escritorio, con un temporizador y solo mientras esa pantalla estuviera abierta—. Esto
/// cumple lo que ese ajuste ya prometía.</para>
///
/// <para><b>Interruptor PROPIO, y no el general.</b> Los otros seis trabajos esperan al corte del
/// escritorio porque sus temporizadores hacen lo mismo y se duplicarían los avisos. Éste no duplica
/// nada: los avisos de asignación cuelgan de la sincronización PERSONAL, no de ésta, y traer un
/// ticket dos veces es escribir la misma fila dos veces. Por eso puede encenderse antes, y por eso
/// tiene su propia llave: encender el general para tener éste encendería los otros cinco.</para>
///
/// <para><b>El tic es fijo y el intervalo se lee de la base.</b> No es un capricho: el
/// <c>PeriodicTimer</c> de la clase base se construye UNA vez con <see cref="Cada"/>, así que un
/// intervalo que viviera ahí se congelaría al arrancar y cambiarlo en la pantalla no serviría de
/// nada. Se despierta seguido, mira qué dice la configuración y decide si toca.</para>
/// </summary>
public class SincronizacionDeDevOpsJob(IServiceScopeFactory ambitos, ILogger<SincronizacionDeDevOpsJob> log)
    : TrabajoPeriodico(ambitos, log)
{
    /// <summary>
    /// Cada cuánto se DESPIERTA, que no es cada cuánto sincroniza. Es la resolución con la que puede
    /// honrar el intervalo configurado: con un tic de un minuto, pedir «cada 10» significa entre 10 y
    /// 11, que es exacto de sobra para esto.
    /// </summary>
    protected override TimeSpan Cada => TimeSpan.FromMinutes(1);

    protected override string Nombre => "sincronización con Azure DevOps y alta en el pool";

    /// <summary>
    /// Cuánto se espera antes de reintentar cuando la sincronización falla.
    ///
    /// <para>Hace falta un sello de ÚLTIMO INTENTO aparte del de última sincronización correcta,
    /// porque aquél solo se escribe cuando todo salió bien: con DevOps caído, compararse solo contra
    /// él haría que el trabajo lo reintentara en cada tic —consulta WIQL y descarga de work items
    /// incluidas— hasta que volviera. Con esto, un servidor que no contesta se pregunta cada cinco
    /// minutos y no cada uno.</para>
    /// </summary>
    private static readonly TimeSpan EsperaTrasFallo = TimeSpan.FromMinutes(5);

    /// <summary>Cuándo se intentó por última vez, saliera bien o mal. En memoria a propósito: es una
    /// cortesía para no machacar a un servidor caído, no un dato que deba sobrevivir al reinicio.</summary>
    private DateTime? _ultimoIntento;

    protected override async Task EjecutarVueltaAsync(IServiceProvider servicios, CancellationToken ct)
    {
        var configuracion = servicios.GetRequiredService<SettingsService>();

        // 0 o vacío = apagado, y por eso el valor por omisión que se pide es 0: quien no lo haya
        // configurado nunca no quiere que esto corra solo.
        int minutos = await configuracion.ObtenerEnteroAsync(
            SettingsService.Claves.DevOpsSyncIntervalMinutes, 0, ct);
        if (minutos <= 0) return;

        var ahora = DateTime.UtcNow;
        if (_ultimoIntento is DateTime intento && ahora - intento < EsperaTrasFallo) return;

        var ultima = await configuracion.ObtenerAsync(SettingsService.Claves.UltimaSincronizacionDevOps, ct);
        if (DateTime.TryParse(ultima, null, System.Globalization.DateTimeStyles.RoundtripKind, out var sello)
            && ahora - sello.ToUniversalTime() < TimeSpan.FromMinutes(minutos))
            return;

        _ultimoIntento = ahora;

        var devops = servicios.GetRequiredService<DevOpsService>();

        // La ventana por fecha de CREACIÓN es la misma que usa el alta, para no traerse el proyecto
        // entero en cada vuelta: lo que se quiere ver es lo que acaba de aparecer.
        int dias = await configuracion.ObtenerEnteroAsync(
            SettingsService.Claves.PoolDevOpsDiasDeAlta, PoolDesdeDevOpsService.DiasPorOmision, ct);

        var filtro = new FiltroDeSincronizacion([], [], [], CreadosEnDias: dias > 0 ? dias : PoolDesdeDevOpsService.DiasPorOmision);

        var sincronizacion = await devops.SincronizarProgramadaAsync(filtro, ct);
        if (!sincronizacion.Ok)
        {
            log.LogWarning("Sincronización automática con DevOps: {Motivo}", sincronizacion.Mensaje);
            return;
        }

        // El sello de «terminó bien» ya lo escribió la sincronización, así que el reloj del intervalo
        // vuelve a contar desde aquí. Se limpia el de reintento para no arrastrar la espera larga.
        _ultimoIntento = null;

        var alPool = servicios.GetRequiredService<PoolDesdeDevOpsService>();
        var alta = await alPool.LlevarAlPoolProgramadoAsync(ct);

        if (alta.Creadas > 0)
            log.LogInformation("Alta automática en el pool: {N} actividad(es) por clasificar{Resto}.",
                alta.Creadas, alta.Fuera > 0 ? $", {alta.Fuera} en espera" : "");
    }
}
