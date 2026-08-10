using AdminWeb.Application.Services;

namespace AdminWeb.Api.Jobs;

/// <summary>
/// Dispara los despliegues programados a su hora.
///
/// <para><b>Es el cambio de fondo de esta vertical.</b> En el escritorio, una cita solo se ejecutaba
/// si alguien tenía la aplicación abierta a esa hora —bastaba con dejarla en la bandeja, y la propia
/// pantalla lo advertía—; si no había nadie, el despliegue se marcaba como perdido y no ocurría
/// nunca. Justo el caso para el que existe agendar —la madrugada, el fin de semana— era el que peor
/// funcionaba. Aquí lo dispara el servidor, así que ocurre siempre.</para>
///
/// <para><b>La toma atómica se conserva igualmente.</b> Podría parecer que sobra con una sola
/// instancia, pero es la única garantía de que un programado se ejecute UNA vez: con dos instancias
/// —o durante un despliegue de la propia API, cuando la nueva ya arrancó y la vieja no ha muerto—
/// las dos verían «Programado» y el despliegue saldría por duplicado. Quien la gana es la base, con
/// un UPDATE condicional; ver <see cref="ProgramadosService.TomarSiguienteAsync"/>.</para>
///
/// <para><b>El orden importa</b> y se conserva del escritorio: primero se marcan las perdidas y
/// después se toma. Una cita de anoche no debe dispararse porque el servidor se reinició esta
/// mañana; desplegar a deshora, cuando ya nadie lo espera, es peor que no desplegar.</para>
/// </summary>
public class DesplieguesProgramadosJob(IServiceScopeFactory ambitos, ILogger<DesplieguesProgramadosJob> log)
    : TrabajoPeriodico(ambitos, log)
{
    /// <summary>
    /// Cada minuto. La tolerancia mínima que acepta la agenda son 5 minutos, así que con esta
    /// frecuencia ninguna cita puede expirar entre dos vueltas.
    /// </summary>
    protected override TimeSpan Cada => TimeSpan.FromMinutes(1);

    protected override string Nombre => "despliegues programados";

    protected override async Task EjecutarVueltaAsync(IServiceProvider servicios, CancellationToken ct)
    {
        var agenda = servicios.GetRequiredService<ProgramadosService>();

        int perdidas = await agenda.MarcarPerdidasAsync(ct: ct);
        if (perdidas > 0)
            log.LogWarning("Despliegues programados: {N} cita(s) marcadas como perdidas por pasar su tolerancia.",
                perdidas);

        if (!agenda.HayEjecutor)
        {
            // No se toma nada: tomar y no poder ejecutar dejaría la cita marcada como fallida por un
            // motivo que no tiene nada que ver con el despliegue. Se avisa y se espera a la siguiente
            // vuelta, que es cuando el servicio ya puede estar registrado.
            log.LogError("Despliegues programados: no hay servicio de despliegue registrado; " +
                         "las citas vencidas se quedan esperando.");
            return;
        }

        // Se vacía la cola en la misma vuelta en vez de tomar una sola cita: varias pueden haber
        // vencido a la vez (un reinicio, un fin de semana), y hacerlas de una por minuto retrasaría
        // la última hasta pasarse su propia tolerancia.
        while (await agenda.TomarSiguienteAsync(ct: ct) is int citaId)
        {
            log.LogInformation("Despliegue programado #{Cita}: tomado, ejecutando…", citaId);

            // EjecutarAsync no lanza: guarda el fallo en la cita y lo devuelve. Aquí solo se registra,
            // porque un despliegue que sale mal no puede tumbar el trabajo y dejar sin disparar a los
            // que vienen detrás.
            var (ok, mensaje) = await agenda.EjecutarAsync(citaId, ct);

            if (ok) log.LogInformation("Despliegue programado #{Cita}: {Mensaje}", citaId, mensaje);
            else log.LogError("Despliegue programado #{Cita} FALLÓ: {Mensaje}", citaId, mensaje);

            // La cancelación del apagado se atiende entre citas: cortar a mitad de un despliegue
            // dejaría servidores a medio actualizar, que es peor que terminar y salir después.
            if (ct.IsCancellationRequested) return;
        }
    }
}
