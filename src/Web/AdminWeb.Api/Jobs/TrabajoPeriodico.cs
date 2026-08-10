namespace AdminWeb.Api.Jobs;

/// <summary>
/// Base de los trabajos que se repiten cada tanto.
///
/// <para><b>Por qué esto es una mejora y no una traducción.</b> En el escritorio todo lo periódico
/// —vigilar los SLA, cerrar jornadas caídas, sincronizar tickets, lanzar los despliegues
/// programados— eran temporizadores dentro de la ventana principal. Eso tenía dos consecuencias que
/// el propio código documentaba: si nadie tenía la aplicación abierta, no ocurría nada («no había
/// ninguna aplicación abierta a su hora»), y si la tenían varios, lo mismo se ejecutaba N veces y
/// hacía falta inventar defensas para que no se duplicara. Aquí corre en el servidor: una sola vez y
/// siempre.</para>
///
/// <para>Cada vuelta abre su propio ámbito porque el contexto de datos es scoped y estos trabajos no
/// tienen petición de la que colgarse. Y cada vuelta captura sus errores: un fallo de red no puede
/// matar el trabajo, o dejaría de vigilarse hasta el siguiente despliegue.</para>
/// </summary>
public abstract class TrabajoPeriodico(IServiceScopeFactory ambitos, ILogger log) : BackgroundService
{
    protected abstract TimeSpan Cada { get; }

    /// <summary>Nombre para el registro. Lo que se lee cuando algo va mal a las tres de la mañana.</summary>
    protected abstract string Nombre { get; }

    protected abstract Task EjecutarVueltaAsync(IServiceProvider servicios, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation("Trabajo «{Nombre}» activo, cada {Cada}.", Nombre, Cada);

        // Un respiro antes de la primera vuelta: al arrancar, la API está migrando el esquema y
        // sembrando, y no tiene sentido competir con eso.
        try { await Task.Delay(TimeSpan.FromSeconds(20), ct); }
        catch (OperationCanceledException) { return; }

        using var reloj = new PeriodicTimer(Cada);
        do
        {
            try
            {
                using var ambito = ambitos.CreateScope();
                await EjecutarVueltaAsync(ambito.ServiceProvider, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;   // el servidor se está apagando: no es un error
            }
            catch (Exception ex)
            {
                log.LogError(ex, "El trabajo «{Nombre}» falló en una vuelta; se reintenta en {Cada}.", Nombre, Cada);
            }
        }
        while (await EsperarSiguienteAsync(reloj, ct));
    }

    private static async Task<bool> EsperarSiguienteAsync(PeriodicTimer reloj, CancellationToken ct)
    {
        try { return await reloj.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
