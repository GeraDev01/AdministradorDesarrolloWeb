namespace AdminWeb.Client.Recorridos.Guiones;

/// <summary>
/// Los recorridos de las dos caras de la misma información: «Evaluaciones», donde el líder la
/// escribe, y «Mis evaluaciones», donde cada quien lee lo que se escribió sobre sí.
///
/// <para>Lo que se cuenta en los dos es lo que no se ve al pulsar: que lo escrito lo va a leer esa
/// persona, que el nombre de quien evaluó queda grabado y no cambia al corregir una errata, y que
/// «sin calificar» es una respuesta legítima y distinta de una nota baja.</para>
/// </summary>
public sealed class RecorridosDeEvaluaciones : IFuenteDeRecorridos
{
    public IEnumerable<Recorrido> Recorridos() =>
    [
        // ── Evaluaciones (líder) ─────────────────────────────────────────────
        new Recorrido("/evaluaciones", "Evaluaciones e hitos",

            Paso.Portada("Evaluaciones",
                "Donde el líder deja por escrito las fortalezas, las debilidades y la calificación " +
                "de cada quien, registra sus hitos y saca la ficha en PDF que se lleva a la " +
                "conversación de evaluación."),

            new Paso("evaluaciones-desarrollador", "Primero, de quién",
                "Todo lo de abajo cuelga de esta elección, y cambiarla cambia la pantalla entera. " +
                "Se elige antes de escribir nada a propósito: es lo que evita registrarle a " +
                "alguien la evaluación de otro."),

            new Paso("evaluaciones-ficha", "La ficha en PDF",
                "Junta las evaluaciones y los hitos de quien esté elegido en un documento " +
                "imprimible. Se abre en otra pestaña en lugar de descargarse, y lo arma el " +
                "servidor: no depende de nada instalado en tu equipo."),

            new Paso("evaluaciones-pestanas", "Evaluaciones e hitos",
                "Son dos cosas distintas de la misma persona. Una evaluación es una valoración de " +
                "un período, con nota. Un hito es un punto marcado en su camino —un logro, una " +
                "certificación, un ascenso— y no lleva calificación. Las dos salen en la ficha."),

            new Paso("evaluaciones-nueva", "Registrar una evaluación",
                "Abre el panel de captura. La calificación es OPCIONAL: dejarla vacía queda como " +
                "«sin calificar», que no es lo mismo que un 1. Lo que sí hace falta es escribir " +
                "algo, porque una evaluación en blanco no le dice nada a nadie — y conviene " +
                "recordar que lo que se escriba aquí lo va a leer esa persona en su ficha."),

            new Paso("evaluaciones-rejilla", "Quién evaluó",
                "La columna «Evaluó» guarda el nombre de quien hizo esa valoración y NO cambia si " +
                "más adelante otra persona corrige una errata: la evaluación sigue siendo de quien " +
                "la escribió. Eliminar una no se puede deshacer.")),

        // ── Mis evaluaciones ─────────────────────────────────────────────────
        new Recorrido("/mis-evaluaciones", "Mis evaluaciones",

            Paso.Portada("Mis evaluaciones",
                "Lo que tu líder registró sobre ti: fortalezas, debilidades, calificación e hitos. " +
                "Es de solo lectura —aquí no hay nada que contestar ni que corregir— y desde esta " +
                "pantalla no se puede consultar la ficha de nadie más."),

            new Paso("mis-evaluaciones-ficha", "Tu ficha en PDF",
                "El mismo documento que arma tu líder, con tus evaluaciones y tus hitos, listo " +
                "para imprimir. Está apagado si tu cuenta todavía no está ligada a una ficha de " +
                "desarrollador, que es también el motivo de que la pantalla se vea vacía."),

            new Paso("mis-evaluaciones-tarjeta", "Una evaluación completa",
                "Cada evaluación se enseña entera y no resumida: la fecha, el período, quién la " +
                "hizo y los tres apartados tal como se escribieron. Las estrellas llevan siempre " +
                "el número al lado; si no hay ninguna es que quedó «sin calificar», que no es un " +
                "cero."),

            new Paso("mis-evaluaciones-hitos", "Tus hitos",
                "Los puntos marcados en tu camino —un logro, una certificación, un ascenso— que tu " +
                "líder fue registrando. No llevan calificación y también salen en la ficha."))
    ];
}
