using AdminWeb.Application.Services;

namespace AdminWeb.Api.Jobs;

/// <summary>
/// Convierte en requerimientos los correos que llegan a la carpeta configurada.
///
/// <para>En el escritorio esto NO corría solo: había que abrir la bandeja de correo y pulsar
/// «Importar no leídos». Funcionaba, pero significaba que un requerimiento pedido por correo el
/// viernes no existía en el sistema hasta que alguien se acordara de entrar. Aquí el servidor mira
/// el buzón cada diez minutos; el botón sigue estando en la pantalla, y hace exactamente lo mismo
/// sobre la misma carpeta.</para>
///
/// <para>Diez minutos y no uno: cada vuelta abre una conexión IMAP y autentica, y un correo que
/// tarda diez minutos en aparecer como requerimiento no le cambia el día a nadie. Un buzón sin
/// configurar no es un error — el servicio no hace nada y la vuelta termina sin ruido.</para>
/// </summary>
public class IngestaDeCorreoJob(IServiceScopeFactory ambitos, ILogger<IngestaDeCorreoJob> log)
    : TrabajoPeriodico(ambitos, log)
{
    protected override TimeSpan Cada => TimeSpan.FromMinutes(10);
    protected override string Nombre => "ingesta de requerimientos por correo";

    protected override async Task EjecutarVueltaAsync(IServiceProvider servicios, CancellationToken ct)
    {
        var ingesta = servicios.GetRequiredService<IngestaDeCorreoService>();
        int creados = await ingesta.EjecutarIngestaProgramadaAsync(ct);

        if (creados > 0)
            log.LogInformation("Ingesta de correo: {N} requerimiento(s) creados.", creados);
    }
}
