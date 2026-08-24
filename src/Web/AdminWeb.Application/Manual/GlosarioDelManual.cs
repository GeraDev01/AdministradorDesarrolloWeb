namespace AdminWeb.Application.Manual;

/// <summary>
/// El GLOSARIO del manual: las palabras que aquí significan algo concreto, en dos líneas cada una.
///
/// <para><b>Por qué son artículos y no una pantalla de glosario.</b> Porque en esta base un término
/// del glosario ya ES un artículo corto etiquetado, y esa decisión es de la propia base de
/// conocimiento, no de aquí: hay un solo camino para escribir, uno para revisar y uno para buscar.
/// Un glosario aparte habría obligado a buscar dos veces y a decidir en cuál de los dos sitios va
/// cada cosa el día que un término crece hasta convertirse en una explicación — que es exactamente
/// lo que le pasa a la mitad de ellos.</para>
///
/// <para><b>Y por qué son cortos de verdad.</b> Un glosario se consulta de pasada, en mitad de otra
/// cosa: quien llega aquí venía leyendo otro artículo y se topó con una palabra. Si la definición no
/// se resuelve en dos líneas, deja de ser un glosario y se convierte en una segunda lectura. Lo que
/// necesite más espacio va al artículo de su área, y desde aquí se le manda.</para>
///
/// <para>El título lleva el prefijo «Glosario:» por dos razones prácticas: agrupa los términos en la
/// lista y en el buscador, y evita títulos de tres letras —el título tiene un mínimo de cinco, así
/// que «SLA» a secas ni siquiera sería un título válido—.</para>
/// </summary>
internal static class GlosarioDelManual
{
    /// <summary>El prefijo del título de cada término.</summary>
    private const string Prefijo = "Glosario: ";

    /// <summary>Etiquetas que llevan todos, además de la propia del término.</summary>
    private const string Comunes = ManualDeUso.Etiqueta + ", " + ManualDeUso.EtiquetaDelGlosario;

    internal static readonly ArticuloDelManual[] Terminos =
    [
        Termino("sla", "SLA", "sla, compromisos",
            """
            Un compromiso con fecha: en cuánto tiempo se responde algo o para cuándo tiene que estar cerrado. Sus estados son Activo, Cumplido, Vencido y Cancelado.

            La plataforma avisa **antes** de que venza. Un compromiso renegociado a tiempo es una conversación; el mismo compromiso reconocido al día siguiente es un problema.
            """),

        Termino("sprint", "sprint", "sprint, planeacion",
            """
            La ventana de tiempo —normalmente dos semanas— con el trabajo que el equipo se comprometió a terminar dentro de ella. Se ve en `/sprint`.

            Lo que está dentro es lo que se prometió. Meter cosas a mitad de camino tiene un costo, y lo paga alguien.
            """),

        Termino("pool", "pool", "pool, actividades",
            """
            La lista de trabajo sin dueño que el líder publica y que cualquier desarrollador puede tomar. Cada actividad viene ya con sus puntos y su plazo fijados de antemano.

            Se toma por decisión propia, no por asignación. El artículo del pool explica los estados y las reglas.
            """),

        Termino("plazo", "plazo", "plazo, tiempos",
            """
            Para cuándo tiene que estar algo. En el pool se cuenta en **horas de reloj** desde que tomas la actividad, no en días hábiles: 40 horas de plazo es pasado mañana, no dentro de una semana.

            No lo confundas con el esfuerzo, que es cuánto cuesta hacerlo.
            """),

        Termino("esfuerzo", "esfuerzo", "esfuerzo, estimacion",
            """
            Cuántas horas de trabajo cuesta hacer algo. Se estima antes de empezar, en horas y con el cuarto de hora como unidad mínima.

            Al terminar se compara con lo que marcó el cronómetro. De ahí sale si el equipo estima bien, que es lo único que enseña a prometer fechas realistas.
            """),

        Termino("punto", "punto", "puntos, desempeno",
            """
            La unidad con la que se reconoce el trabajo que va más allá de cumplir. Se acumulan por mes y por año, y son la base de la evaluación.

            Siempre se otorgan bajo un **criterio** del catálogo, nunca sueltos: es lo que los hace comparables entre dos personas distintas.
            """),

        Termino("retrabajo", "retrabajo", "pool, retrabajo, puntos",
            """
            Un bug sobre algo que **ya se entregó**. Es el único tipo de actividad del pool que da puntos **negativos**: al aceptarla se le restan a quien la trabajó.

            No castiga corregir errores —eso es un bug corriente y se paga—; lo que evita es que entregar antes de tiempo y arreglarlo después salga a cuenta. Lo clasifica el líder al publicarla.
            """),

        Termino("complejidad", "complejidad", "pool, complejidad",
            """
            Qué tan complicada es una actividad del pool: baja, media, alta o muy alta. La fija el líder al publicarla, antes de que nadie la trabaje.

            Junto con el tipo —bug, tarea o requerimiento— es lo que determina cuántos puntos vale.
            """),

        Termino("prioridad-del-pool", "prioridad", "pool, prioridad",
            """
            Con qué urgencia hay que tomar una actividad del pool: baja, media, alta o crítica.

            Ordena la lista y se resalta, pero **no cambia los puntos**. Si los cambiara, publicar «crítica» sería la forma de regalar puntos.
            """),

        Termino("requerimiento", "requerimiento", "requerimientos, trabajo",
            """
            Lo que pide un área de la empresa: una funcionalidad nueva, un cambio, un reporte. Llega por correo o lo captura el líder, se estima y se asigna.

            No es lo mismo que un bug: un bug es algo que ya existía y dejó de funcionar.
            """),

        Termino("work-item", "work item", "devops, tickets",
            """
            Cada una de las fichas de trabajo de Azure DevOps: bugs, tareas, historias. La plataforma las sincroniza y las enseña junto al resto del trabajo, en `/devops` y en `Mis tickets DevOps`.

            En conversación se les dice «ticket», igual que a los de Freshdesk, que son otra cosa: aquéllos los levanta el soporte a clientes.
            """),

        Termino("respaldo-previo", "respaldo previo", "despliegues, respaldo",
            """
            La copia comprimida de la carpeta del servidor que se toma **antes** de sobrescribirla al desplegar. Es lo que permite revertir un despliegue malo.

            Regla que no se negocia: si el respaldo falla, no se despliega.
            """),

        Termino("ventana", "ventana de despliegue", "despliegues, ventana",
            """
            El rango de horas acordado para liberar sin molestar a quien está trabajando. Confirmarla es uno de los cuatro puntos del checklist previo.

            Fuera de la ventana, una caída de dos minutos le pega a alguien que estaba a mitad de algo.
            """),

        Termino("autocalificacion", "autocalificación", "desempeno, puntos",
            """
            **Retirada.** Era registrar tú mismo algo que hiciste, eligiendo un criterio del catálogo, para que contara en tu desempeño; quedaba pendiente de la aprobación del líder.

            Se retiró porque era ponerle valor a algo **ya hecho**, y el pool hace lo contrario: se sabe cuánto vale antes de empezar. En su lugar se **propone** el trabajo al pool.

            El término se conserva aquí porque sigue habiendo entradas de antes: las que estén pendientes o rechazadas se pueden corregir y replicar hasta que se cierren.
            """),

        Termino("proponer", "proponer trabajo", "pool, desempeno, puntos",
            """
            Mandar al pool algo que hay que hacer y nadie ha publicado, desde `Mi trabajo`. Se manda el título, el detalle y un enlace — **ni tipo, ni horas, ni puntos**: eso lo pone el líder.

            Ésa es toda la idea: quien hace el trabajo dice QUÉ hay que hacer y el líder dice CUÁNTO vale, antes de que empiece. Mientras espera aparece como «Por clasificar» y ocupa un sitio de tu tope; al clasificarla te la encuentras tomada, con plazo y checklist.
            """),

        Termino("descuento", "descuento", "pool, desempeno, puntos",
            """
            Puntos que el líder **resta** por un hecho concreto: una actividad del pool que nace pagada, cerrada y en negativo, con su criterio del catálogo y un motivo obligatorio.

            No es lo mismo que un **retrabajo**, y la regla cabe en una línea: si hay algo que hacer, es un retrabajo —se toma, se cronometra y se entrega—; si no hay nada que hacer, es un descuento.

            Se te avisa con el motivo, y se puede anular: entonces quedan las dos anotaciones a la vista y el neto es cero. Aquí nada que haya pagado se borra.
            """),

        Termino("ficha-de-desarrollador", "ficha de desarrollador", "ficha, cuentas",
            """
            Tu expediente de trabajo: antigüedad, puesto, días de vacaciones, puntos. Es distinta de la **cuenta**, que es con lo que entras a la plataforma.

            Sin ficha no se pueden pedir vacaciones ni ganar puntos, porque no hay a quién abonárselos. Si una pantalla te dice que no la tienes, avísale a tu líder.
            """),
    ];

    private static ArticuloDelManual Termino(string clave, string termino, string etiquetas, string definicion) =>
        new($"glosario-{clave}", Prefijo + termino, $"{Comunes}, {etiquetas}", definicion);
}
