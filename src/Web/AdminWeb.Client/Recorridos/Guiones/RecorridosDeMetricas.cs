namespace AdminWeb.Client.Recorridos.Guiones;

/// <summary>
/// Los recorridos de las dos pantallas de medición: «Métricas» y «Estimación y capacidad».
///
/// <para>Las dos son de solo lectura, así que aquí no hay controles que expliquen qué hacen: lo que
/// hay que contar es CÓMO SE CALCULA CADA NÚMERO. Que la edad de un requerimiento entregado se
/// cuente hasta la entrega y no hasta hoy, que un plazo vencido cuente como atraso aunque nadie lo
/// haya marcado, o que la ventana de vacaciones incluya hoy, son las cosas que hacen que dos
/// personas lean la misma tabla de forma distinta.</para>
/// </summary>
public sealed class RecorridosDeMetricas : IFuenteDeRecorridos
{
    public IEnumerable<Recorrido> Recorridos() =>
    [
        // ── Métricas ─────────────────────────────────────────────────────────
        new Recorrido("/metricas", "Métricas de tiempo de vida",

            Paso.Portada("Métricas",
                "El medidor de tiempo de vida: cuánto lleva vivo cada requerimiento, cuánto lleva " +
                "atorado en el estado en el que está y cómo va de carga y de atrasos cada " +
                "desarrollador. No hay nada que capturar ni que mantener al día — todo se deriva " +
                "de fechas que ya existen."),

            new Paso("metricas-indicadores", "Los indicadores",
                "El resumen de todo lo que hay debajo; cada tarjeta explica al pasar el ratón cómo " +
                "se calcula. «Entregados a tiempo» enseña un guion mientras no haya ninguna " +
                "entrega comprometida: un 0 % ahí se leería como si se hubiera incumplido todo."),

            new Paso("metricas-vistas", "Las dos vistas",
                "La primera lista requerimiento por requerimiento. La segunda resume por persona " +
                "—activos, atrasados, edad promedio y el más antiguo— y pone carga y atrasos en el " +
                "mismo eje, porque la pregunta útil no es cuántos tiene alguien sino cuántos de " +
                "los suyos van tarde. En la gráfica entran las primeras doce personas; la tabla " +
                "que va debajo las trae todas."),

            new Paso("metricas-requerimientos", "Vivo y atorado",
                "«Días vivo» se cuenta hasta la entrega en lo ya entregado y hasta hoy en lo que " +
                "sigue abierto; si no, lo entregado hace un año seguiría envejeciendo y taparía lo " +
                "que de verdad está detenido. «Días en estado» es lo que lleva sin moverse, que es " +
                "donde se ven los atorados, y la columna de plazo se pinta en rojo en cuanto la " +
                "fecha comprometida pasó, la haya marcado alguien o no."),

            new Paso("metricas-recalcular", "Recalcular",
                "No vuelve a leer lo mismo: vuelve a derivar todas las edades y todos los atrasos " +
                "contra la fecha de hoy. Es lo que hace falta cuando la pantalla lleva abierta " +
                "desde ayer, porque un requerimiento puede pasar a atrasado sin que nadie lo haya " +
                "tocado.")),

        // ── Estimación y capacidad ───────────────────────────────────────────
        new Recorrido("/estimacion", "Estimación y capacidad",

            Paso.Portada("Estimación y capacidad",
                "Dos preguntas en una pantalla: qué tan bien estamos estimando, y a quién se le " +
                "puede asignar lo siguiente. Es de solo lectura — se mira para decidir, no se " +
                "captura nada aquí."),

            new Paso("estimacion-vistas", "Las dos preguntas",
                "«Estimación vs real» mira hacia atrás: compara las horas que se calcularon con " +
                "las que costó de verdad. «Capacidad del equipo» mira hacia adelante: cuántas " +
                "horas estimadas tiene pendientes cada quien, cuántos días va a estar de " +
                "vacaciones y si está sobrecargado."),

            new Paso("estimacion-indicadores", "Cómo estimamos",
                "Solo entran los requerimientos CON horas estimadas: sin estimación no hay nada " +
                "contra qué comparar, y contarlos como cero hundiría el promedio. Cada tarjeta " +
                "explica al pasar el ratón qué está contando."),

            new Paso("estimacion-tabla", "El ratio",
                "El ratio es lo real entre lo estimado. Por encima de 1.20 se subestimó —costó más " +
                "de lo que se dijo— y por debajo de 0.80 se sobreestimó; lo de en medio cuenta " +
                "como preciso. Lo que todavía no tiene tiempo registrado sale con un guion y no " +
                "entra en el promedio."),

            new Paso("estimacion-ventana", "La ventana de vacaciones",
                "Cuántos días hacia adelante se miran para saber quién va a estar fuera. Cuenta " +
                "desde hoy inclusive: treinta días son hoy y los veintinueve siguientes. Cambiarla " +
                "vuelve a pedir los datos, y si el servidor la recorta se ajusta al número que de " +
                "verdad se usó."),

            new Paso("estimacion-capacidad", "A quién asignarle",
                "«Horas pendientes» son las estimadas de lo que tiene abierto, no las que ha " +
                "trabajado. «Disponibilidad» es la columna que se barre para decidir: sobrecargado " +
                "es tener más horas pendientes que la capacidad, y estar de vacaciones gana a todo " +
                "lo demás — da igual cuánto tenga abierto quien esos días no está."))
    ];
}
