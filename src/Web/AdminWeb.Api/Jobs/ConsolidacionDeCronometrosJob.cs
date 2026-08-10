using AdminWeb.Application.Services;

namespace AdminWeb.Api.Jobs;

/// <summary>
/// Consolida los cronómetros que dejaron de dar señales, hasta su último latido.
///
/// <para><b>Es el trabajo que evita que la web pierda el tiempo trabajado.</b> En el escritorio, al
/// cerrar la ventana se consolidaban los cronómetros abiertos, y el cierre sucio —un cuelgue, un
/// apagón— era la excepción: por eso ahí el tramo colgante se descartaba, que es lo prudente cuando
/// pasa una vez al mes. En un navegador la proporción se invierte: cerrar la pestaña, dormir el
/// portátil o perder el wifi son lo normal, y el aviso de cierre no es fiable. Portar aquel criterio
/// tal cual habría hecho que casi todo el tiempo trabajado se tirara.</para>
///
/// <para>Así que se aplica la política que <see cref="PresenceService"/> ya usaba para las jornadas:
/// no se descarta el tramo, se cierra <b>hasta el último latido</b>. Ni se regalan las horas que la
/// máquina pasó dormida, ni se le quita a nadie lo que sí trabajó.</para>
/// </summary>
public class ConsolidacionDeCronometrosJob(IServiceScopeFactory ambitos, ILogger<ConsolidacionDeCronometrosJob> log)
    : TrabajoPeriodico(ambitos, log)
{
    protected override TimeSpan Cada => TimeSpan.FromMinutes(1);
    protected override string Nombre => "consolidación de cronómetros";

    protected override async Task EjecutarVueltaAsync(IServiceProvider servicios, CancellationToken ct)
    {
        var cronometros = servicios.GetRequiredService<WorkSessionService>();
        int consolidadas = await cronometros.ConsolidarSesionesSinLatidoAsync(ct);

        if (consolidadas > 0)
            log.LogInformation(
                "Consolidación de cronómetros: {N} sesión(es) cerradas hasta su último latido.", consolidadas);
    }
}
