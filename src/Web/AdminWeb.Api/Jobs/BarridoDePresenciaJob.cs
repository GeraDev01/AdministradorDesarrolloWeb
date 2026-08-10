using AdminWeb.Application.Services;

namespace AdminWeb.Api.Jobs;

/// <summary>
/// Cierra las jornadas de quien dejó de dar señales.
///
/// <para>En el escritorio este barrido lo hacía «cualquier aplicación que siguiera viva», de paso,
/// al latir. Funcionaba mientras alguien tuviera la aplicación abierta — y fallaba justo cuando más
/// falta hacía: un viernes por la tarde en que todos cerraban, las jornadas se quedaban abiertas
/// hasta que alguien volviera el lunes. Aquí corre en el servidor y no depende de nadie.</para>
///
/// <para>La regla que se conserva intacta del escritorio: una jornada caída se sella con la hora del
/// <b>último latido</b>, no con la de ahora. Cerrarla con la hora actual le regalaría a esa persona
/// todas las horas que su equipo pasó apagado.</para>
/// </summary>
public class BarridoDePresenciaJob(IServiceScopeFactory ambitos, ILogger<BarridoDePresenciaJob> log)
    : TrabajoPeriodico(ambitos, log)
{
    protected override TimeSpan Cada => TimeSpan.FromMinutes(1);
    protected override string Nombre => "barrido de presencia";

    protected override async Task EjecutarVueltaAsync(IServiceProvider servicios, CancellationToken ct)
    {
        var presencia = servicios.GetRequiredService<PresenceService>();
        int cerradas = await presencia.CerrarCaidasAsync(ct);

        // Solo se registra cuando hubo algo que cerrar: una línea por minuto diciendo «cero» llena
        // el registro de ruido y esconde lo que sí importa.
        if (cerradas > 0)
            log.LogInformation("Barrido de presencia: {N} jornada(s) cerradas por falta de señales.", cerradas);
    }
}
