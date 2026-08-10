using AdminWeb.Application.Services;

namespace AdminWeb.Api.Jobs;

/// <summary>
/// Manda el resumen del equipo cuando toca.
///
/// <para>En el escritorio esto colgaba del latido de la ventana principal y solo si quien la tenía
/// abierta era el líder. Bastaba con que ese día no encendiera la aplicación para que el resumen no
/// saliera —y era justo el día en que más falta hacía saber cuántos SLA estaban vencidos—. Aquí lo
/// manda el servidor.</para>
///
/// <para><b>Media hora y no un minuto</b>, a diferencia de los otros trabajos. El resumen se manda
/// una vez al día (o a la semana): revisar más seguido no lo adelantaría, porque quien decide es la
/// marca del último envío. Y hay una segunda razón — cuando el envío falla, el período NO se marca a
/// propósito, para que el resumen salga en cuanto el correo vuelva; con un temporizador de un minuto
/// eso serían sesenta intentos por hora contra un servidor que ya está rechazando la conexión. En el
/// escritorio ese freno era una variable en memoria del servicio, que aquí no existe porque el
/// servicio es scoped: el freno es este período.</para>
/// </summary>
public class ResumenDiarioJob(IServiceScopeFactory ambitos, ILogger<ResumenDiarioJob> log)
    : TrabajoPeriodico(ambitos, log)
{
    protected override TimeSpan Cada => TimeSpan.FromMinutes(30);
    protected override string Nombre => "resumen del equipo por correo";

    protected override async Task EjecutarVueltaAsync(IServiceProvider servicios, CancellationToken ct)
    {
        var resumen = servicios.GetRequiredService<DigestService>();

        // Solo se registra cuando SÍ salió el correo: la inmensa mayoría de las vueltas son «todavía
        // no toca», y anotarlas llenaría el registro de ruido que esconde lo que sí importa.
        if (await resumen.RevisarYEnviarAsync(ct))
            log.LogInformation("Resumen del equipo enviado.");
    }
}
