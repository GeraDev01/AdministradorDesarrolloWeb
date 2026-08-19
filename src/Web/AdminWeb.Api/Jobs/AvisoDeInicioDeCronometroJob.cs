using AdminWeb.Application.Services;

namespace AdminWeb.Api.Jobs;

/// <summary>
/// Publica en Azure DevOps los arranques de cronómetro que nadie avisó todavía.
///
/// <para><b>Existe por la aplicación de escritorio.</b> Cuando el cronómetro arranca desde la web, el
/// propio endpoint avisa al instante y esto no encuentra nada que hacer. Pero el escritorio sigue en
/// producción, comparte esta misma base y tiene su propio botón de arrancar: lo que se arranca allá
/// no pasa por ningún endpoint nuestro. La única forma de enterarse es mirar la tabla, que es lo que
/// hace esto. De paso recoge lo que el aviso inmediato no logró publicar por un corte de red.</para>
///
/// <para><b>Con su propia llave y no colgado del interruptor general</b>, por lo mismo que la
/// sincronización con DevOps: encender aquél para tener éste encendería otros cinco trabajos que
/// duplicarían lo que el escritorio ya hace con sus temporizadores. Éste no duplica nada —el
/// escritorio no avisa de nada en DevOps al arrancar— y además la función entera lleva su propio
/// interruptor en Configuración, así que con la llave puesta y la función apagada esto no hace ni
/// una consulta.</para>
///
/// <para>Cada dos minutos y no cada uno: un aviso de inicio no es urgente al segundo, y la pasada
/// mira una ventana de dos horas, así que nada se pierde por esperar.</para>
/// </summary>
public class AvisoDeInicioDeCronometroJob(IServiceScopeFactory ambitos, ILogger<AvisoDeInicioDeCronometroJob> log)
    : TrabajoPeriodico(ambitos, log)
{
    protected override TimeSpan Cada => TimeSpan.FromMinutes(2);
    protected override string Nombre => "aviso de inicio de cronómetro";

    protected override async Task EjecutarVueltaAsync(IServiceProvider servicios, CancellationToken ct)
    {
        var avisos = servicios.GetRequiredService<AvisoDeInicioEnDevOpsService>();
        int publicados = await avisos.AvisarIniciosPendientesAsync(ct);

        if (publicados > 0)
            log.LogInformation(
                "Aviso de inicio de cronómetro: {N} arranque(s) publicados en Azure DevOps.", publicados);
    }
}
