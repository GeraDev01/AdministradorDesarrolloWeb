using AdminWeb.Application.Services;

namespace AdminWeb.Api.Jobs;

/// <summary>
/// Vigila los compromisos: cierra lo que DevOps ya dio por terminado, marca lo que se pasó de fecha,
/// escala el incumplimiento al líder y le recuerda a cada quien lo suyo.
///
/// <para><b>Esta es la mejora que justifica portar los SLA.</b> En el escritorio todo esto lo hacía
/// un temporizador de la ventana principal, y el propio código lo dejaba escrito: el escalamiento
/// solo corría «si la sesión es de administración», y los recordatorios solo le llegaban a quien
/// tuviera la aplicación abierta. Es decir, si el líder no abría la aplicación, los SLA vencidos no
/// se escalaban — el aviso dependía de un hábito, y precisamente en las semanas de más trabajo, que
/// son cuando los compromisos se incumplen, es cuando menos se abre la pantalla de gestión. Aquí lo
/// hace el servidor: ocurre siempre, una sola vez, y sin que nadie tenga que acordarse.</para>
///
/// <para><b>Cada cuarto de hora y no cada cinco minutos.</b> El escritorio revisaba cada cinco porque
/// no sabía cuánto iba a seguir abierto: había que aprovechar la ventana de tiempo que hubiera. Un
/// trabajo del servidor no tiene esa prisa, y la unidad más fina de un SLA es la hora —el
/// recordatorio se configura en horas—, así que quince minutos dan de sobra la resolución que la
/// regla necesita, con una sexta parte de las consultas.</para>
///
/// <para>Los avisos de la fecha comprometida de un requerimiento viajan en la misma vuelta. Son la
/// misma pregunta —«¿qué plazo se está por incumplir?»— y en el escritorio también compartían ciclo;
/// separarlos en dos trabajos sería duplicar el andamiaje para recorrer la misma base dos veces.</para>
/// </summary>
public class EscalamientoDeSlaJob(IServiceScopeFactory ambitos, ILogger<EscalamientoDeSlaJob> log)
    : TrabajoPeriodico(ambitos, log)
{
    protected override TimeSpan Cada => TimeSpan.FromMinutes(15);
    protected override string Nombre => "escalamiento de SLA";

    protected override async Task EjecutarVueltaAsync(IServiceProvider servicios, CancellationToken ct)
    {
        var avisosDeSla = servicios.GetRequiredService<SlaNotificationService>();

        var escalamiento = await avisosDeSla.EscalarIncumplimientosAsync(ct);
        if (escalamiento.SinDestinatario)
            log.LogWarning(
                "SLA: {N} compromiso(s) vencido(s) sin nadie a quien escalarlos; quedan marcados pero " +
                "nadie recibió el aviso. Revisa «{Clave}» en Configuración o deja un líder activo.",
                escalamiento.Vencidos, SettingsService.Claves.SlaEscalationEmail);
        else if (escalamiento.Vencidos > 0)
            log.LogInformation("SLA: {N} compromiso(s) vencido(s) escalado(s).", escalamiento.Vencidos);

        // El correo es el extra del escalamiento, así que su fallo no interrumpe nada: se registra.
        // Sin esta línea, un buzón mal configurado dejaría de mandar correos durante meses sin que
        // nada lo dijera, porque el aviso dentro de la aplicación seguiría llegando igual.
        if (escalamiento.Vencidos > 0 && escalamiento.FalloDelCorreo is string falloDelCorreo)
            log.LogWarning(
                "SLA: el escalamiento no salió por correo ({Motivo}). El aviso dentro de la "
                + "aplicación sí se entregó.", falloDelCorreo);

        var recordatorios = await avisosDeSla.AvisarPendientesAsync(ct: ct);
        if (recordatorios.Avisados > 0)
            log.LogInformation(
                "SLA: {N} recordatorio(s) enviados de {P} pendiente(s).",
                recordatorios.Avisados, recordatorios.Pendientes);
        if (recordatorios.SinCuenta > 0)
            log.LogWarning(
                "SLA: {N} pendiente(s) de personas sin cuenta activa; no hay forma de avisarles.",
                recordatorios.SinCuenta);

        var compromisos = servicios.GetRequiredService<CommitmentAlertService>();
        int avisosDeEntrega = await compromisos.RevisarYAvisarAsync(ct: ct);
        if (avisosDeEntrega > 0)
            log.LogInformation("Compromisos de entrega: {N} aviso(s) creados.", avisosDeEntrega);
    }
}
