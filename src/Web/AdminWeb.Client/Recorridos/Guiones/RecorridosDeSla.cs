namespace AdminWeb.Client.Recorridos.Guiones;

/// <summary>
/// Los recorridos de las tres pantallas de SLA: la del líder («SLA y recordatorios»), la de cada
/// quien («Mis SLA») y el reporte de cumplimiento.
///
/// <para>El hilo de las tres es la misma distinción, y es la que más se confunde: aplazar un aviso
/// no es atender un compromiso, y cerrar un compromiso no es lo mismo que cancelarlo. A eso se
/// suma que aquí hay dos relojes —el del vencimiento y el del recordatorio— y que los colores de
/// las filas hablan del segundo tanto como del primero.</para>
/// </summary>
public sealed class RecorridosDeSla : IFuenteDeRecorridos
{
    public IEnumerable<Recorrido> Recorridos() =>
    [
        // ── SLA y recordatorios (líder) ──────────────────────────────────────
        new Recorrido("/sla", "SLA y recordatorios",

            Paso.Portada("SLA y recordatorios",
                "Los compromisos de atención del equipo: a quién se le exige qué, para cuándo, y " +
                "cada cuánto se le recuerda que deje constancia en el ticket de Azure DevOps. " +
                "Desde aquí se asignan, se cierran y se ajustan los plazos automáticos."),

            new Paso("sla-resumen", "Cómo vamos",
                "«Vencen en 24 h» se cuenta desde este momento y no «hoy»: el día del servidor no " +
                "tiene por qué ser el de quien mira, y con un corte de medianoche el mismo " +
                "compromiso entraría o no en el recuento según dónde esté hospedada la aplicación."),

            new Paso("sla-estado", "El filtro de estado",
                "Arranca en «Activos», así que lo cumplido y lo cancelado no está a la vista hasta " +
                "que lo pidas. Es la razón habitual de que un compromiso que sí existe parezca que " +
                "no está."),

            new Paso("sla-asignar", "Asignar un compromiso",
                "Abre el formulario de alta. Al elegir el objetivo se rellenan solos el " +
                "responsable —quien ya lo tiene asignado— y el número de ticket si vino de DevOps; " +
                "sin ticket ligado, después no habrá dónde dejar constancia. La cadencia dice cada " +
                "cuántas horas se recuerda, y «solo al vencer» también es una opción válida."),

            new Paso("sla-revisar", "Revisar vencidos",
                "No recarga la lista: vuelve a evaluar los vencimientos y ESCALA a los líderes lo " +
                "que encuentre incumplido. El servidor ya lo hace por su cuenta cada cierto " +
                "tiempo; esto sirve para no esperar al siguiente ciclo."),

            new Paso("sla-rejilla", "Lo que dicen los colores",
                "La fila en rojo está fuera de plazo. La ámbar es «toca comentar»: el recordatorio " +
                "ya venció y todavía no hay constancia en el ticket. El punto de la columna de " +
                "estado dice cuál de los dos es exactamente. «Cumplido» lo da por hecho; " +
                "«Cancelar» no borra nada, deja el compromiso sin efecto y deja de contar como " +
                "incumplimiento."),

            new Paso("sla-politicas", "Plazos automáticos",
                "Cuando la sincronización con Azure DevOps asigne un ticket, se le creará un SLA " +
                "con el plazo de la prioridad que traiga. Lo que esté desactivado no crea nada, y " +
                "ese es el valor por omisión para que nadie se encuentre compromisos que no puso. " +
                "Los cambios de aquí no se aplican hasta pulsar «Guardar plazos».")),

        // ── Mis SLA ──────────────────────────────────────────────────────────
        new Recorrido("/mis-sla", "Mis SLA",

            Paso.Portada("Mis SLA",
                "Lo que tú tienes comprometido: qué, para cuándo y cuánto falta. Los recordatorios " +
                "llegan a «Avisos» aunque no tengas la aplicación abierta, así que aquí se viene a " +
                "atender lo que reclama, no a enterarse de que existe."),

            new Paso("mis-sla-cerrados", "Ver los cerrados",
                "Por omisión solo se enseña lo vigente. Se marca para consultar lo que ya se " +
                "cumplió o se canceló, normalmente para saber cuándo se cerró algo."),

            new Paso("mis-sla-rejilla", "Cuánto falta",
                "La columna «Falta» se calcula contra el reloj de tu equipo, así que se mueve " +
                "mientras la pantalla está abierta. La fila en rojo está fuera de plazo; la ámbar " +
                "dice que el recordatorio ya venció y todavía no has dejado constancia en el " +
                "ticket."),

            new Paso("mis-sla-avance", "Registrar avance",
                "Abre el recuadro para escribir qué se avanzó, con capturas si hacen falta. Lo que " +
                "escribas se PUBLICA como comentario en el ticket de Azure DevOps y va firmado con " +
                "tu token, así que allá queda a tu nombre. Solo cuenta como avance si el " +
                "comentario llega al ticket: si DevOps lo rechaza, el compromiso sigue reclamando. " +
                "Sin ticket ligado el botón no aparece."),

            new Paso("mis-sla-posponer", "Posponer",
                "Aplaza el AVISO cuatro horas, y nunca más allá del vencimiento; la fecha límite " +
                "no se mueve ni un minuto. Sirve para decir «estoy en ello», no para dar el " +
                "compromiso por atendido.")),

        // ── Cumplimiento de SLA ──────────────────────────────────────────────
        new Recorrido("/cumplimiento-sla", "Cumplimiento de SLA",

            Paso.Portada("Cumplimiento de SLA",
                "Cuántos compromisos se cumplieron a tiempo y cuántos se vencieron en el período " +
                "que elijas, en total y desglosado. Es de solo lectura: se usa para reportar y " +
                "para ver dónde se está fallando."),

            new Paso("cumplimiento-filtros", "Qué entra en el período",
                "Entran los compromisos cuya FECHA LÍMITE cae dentro del rango, no los que se " +
                "cerraron dentro de él. Mover cualquiera de los tres controles vuelve a calcular " +
                "en el acto."),

            new Paso("cumplimiento-agrupar", "El corte",
                "Decide cómo se parte la tabla de abajo: por persona, por prioridad, por cliente o " +
                "por mes. No cambia los totales de arriba, solo la forma de repartirlos; es lo que " +
                "se toca para pasar de «cómo vamos» a «dónde se rompe»."),

            new Paso("cumplimiento-tarjetas", "El porcentaje",
                "El cumplimiento se mide SOLO sobre lo ya resuelto, o sea cumplidos más vencidos. " +
                "Si todavía no se ha resuelto nada sale un guion y no un 0 %, que se leería como " +
                "haber incumplido todo. Lo cancelado no cuenta como fallo: cancelar deja el " +
                "compromiso sin efecto."),

            new Paso("cumplimiento-tabla", "Vencido sin cerrar",
                "Un compromiso todavía abierto cuya fecha ya pasó cuenta como vencido aunque nadie " +
                "lo haya cerrado: se mide el plazo, no la etiqueta. Por eso este reporte puede " +
                "empeorar solo, sin que nadie toque nada."))
    ];
}
