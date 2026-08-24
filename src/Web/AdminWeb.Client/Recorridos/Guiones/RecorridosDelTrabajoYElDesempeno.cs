namespace AdminWeb.Client.Recorridos.Guiones;

/// <summary>
/// Los recorridos del trabajo del día a día: los requerimientos y su sprint, el pool de actividades,
/// lo que cada quien registra sobre sí mismo y las dos pantallas donde eso se lee como desempeño.
///
/// <para>Van juntos en un archivo porque cuentan una sola historia y hay que poder leerla seguida:
/// un requerimiento se da de alta en «Requerimientos», se compromete en «Sprint», se cronometra en
/// «Mis asignaciones» y acaba pesando en «Desempeño». Los textos se remiten unos a otros —«eso se
/// hace en tal pantalla»—, y esas remisiones envejecen mal si viven en archivos distintos.</para>
///
/// <para>Dos criterios que explican por qué unos controles tienen paso y otros no. El primero es que
/// solo se explica lo que no se lee en la pantalla: los botones de recargar, los filtros evidentes y
/// los formularios cuyos campos ya llevan su rótulo no llevan paso, porque un recorrido que los
/// nombra alarga la lectura sin añadir nada. El segundo es que sí se explica todo efecto que ocurre
/// FUERA de la pantalla —a quién se le avisa, qué se escribe en Azure DevOps, qué deja de poder
/// deshacerse—, que es exactamente lo que nadie descubre mirando.</para>
///
/// <para>Varios pasos señalan controles que no siempre están: los que solo ve el líder, los que
/// dependen de qué pestaña esté abierta o de si hay un cronómetro corriendo. El motor se los salta
/// en silencio, así que el mismo guion sirve para los dos roles sin partirlo en dos.</para>
/// </summary>
public sealed class RecorridosDelTrabajoYElDesempeno : IFuenteDeRecorridos
{
    public IEnumerable<Recorrido> Recorridos() =>
    [
        // ── El tablero de entrada ────────────────────────────────────────────
        new Recorrido("/dashboard", "El tablero de entrada",

            Paso.Portada("El tablero",
                "Es el resumen del área con el que se entra a trabajar: cuánto hay en cada punto " +
                "del ciclo, qué vence esta semana y cómo va el mes. No tiene un solo control que " +
                "cambie algo; todo lo que se ve aquí se edita en su propia pantalla."),

            new Paso("dashboard-tarjetas", "Cuánto hay de cada cosa",
                "Los cinco primeros contadores son estados del requerimiento y cuentan todo lo que " +
                "hay abierto. «Pendientes» no es un estado: son las notas y los recordatorios de " +
                "«Notas y pendientes», y por eso sale de otro color y no del semáforo."),

            new Paso("dashboard-entregas", "Lo que vence esta semana",
                "Solo lo comprometido para los próximos siete días. Las filas en rojo van " +
                "atrasadas —el compromiso ya pasó y no se ha entregado—, y quien decide eso es el " +
                "reloj del servidor y no el de tu equipo. El punto de color de la columna Estado " +
                "es el mismo que en Requerimientos y significa lo mismo."),

            new Paso("dashboard-carga", "Quién tiene qué",
                "Cuánto trabajo activo lleva cada quien y cuánto tiene por entregar; sirve para " +
                "repartir sin ir a preguntar. Este panel y los dos siguientes solo llegan si tu " +
                "cuenta puede verlos: cuando no te corresponden, el servidor ni los consulta."),

            new Paso("dashboard-recordatorios", "Lo que se prometió mirar",
                "Las notas con fecha de recordatorio que siguen sin cerrarse, y en rojo las que ya " +
                "se pasaron de fecha. Se dan de alta y se cierran en «Notas y pendientes»; aquí " +
                "solo se asoman para que no se pierdan de vista."),

            new Paso("dashboard-podio", "Cómo va el mes",
                "Los primeros lugares del ranking de puntos del mes en curso, contando únicamente " +
                "lo que ya se aprobó. El detalle completo, con las actividades de cada quien, está " +
                "en «Desempeño».")),

        // ── Mi panel ─────────────────────────────────────────────────────────
        new Recorrido("/mi-panel", "Mi desempeño del mes",

            Paso.Portada("Tu mes",
                "Aquí se consulta cómo vas de puntos y en qué lugar quedas, tú y tu equipo. Es " +
                "solo de lectura: registrar una actividad, corregirla o replicar un rechazo se " +
                "hace en «Mis actividades»."),

            new Paso("mi-panel-sin-ficha", "Sin ficha de desarrollador",
                "Los puntos se abonan a una ficha de desarrollador, no a la cuenta con la que " +
                "entras. Mientras tu cuenta no esté ligada a una, puedes mirar los rankings pero " +
                "no vas a aparecer en ellos. Lo resuelve el líder."),

            new Paso("mi-panel-periodo", "El mes que se mira",
                "Cambiar el mes o el año vuelve a pedirlo todo: tus indicadores y los dos rankings " +
                "se recalculan para ese período. No toca ninguno de tus datos, solo lo que se ve."),

            new Paso("mi-panel-indicadores", "Qué dice cada número",
                "«Aprobado» son los puntos que ya cuentan en el ranking. «En revisión» son los que " +
                "registraste y siguen esperando al líder, así que todavía pueden cambiar de valor " +
                "o no llegar. «Rechazado» cuenta las que te devolvieron: ésas se corrigen o se " +
                "replican en «Mis actividades», y hasta entonces no suman nada."),

            new Paso("mi-panel-rankings", "Los dos rankings",
                "El individual y el de equipos, del mismo período. Tu fila —y la de tu equipo— " +
                "llevan una barra en el canto izquierdo para encontrarlas sin buscarlas. De los " +
                "demás solo llega el nombre y el total: sus actividades, comentarios y evidencias " +
                "no salen de aquí.")),

        // ── Desempeño (líder) ────────────────────────────────────────────────
        new Recorrido("/desempeno", "Desempeño y aprobaciones",

            Paso.Portada("El desempeño del área",
                "Dos cosas en la misma pantalla: los rankings del mes y la cola de puntos que los " +
                "desarrolladores se autoasignaron y esperan tu decisión. Lo que apruebes aquí es " +
                "exactamente lo que va a contar en el ranking."),

            new Paso("desempeno-periodo", "El período",
                "El mes y el año mandan sobre los dos rankings y nada más. La cola de pendientes " +
                "NO se filtra por ellos: lo que espera aprobación se ve completo, venga del mes " +
                "que venga, porque filtrarlo escondería justo lo que lleva más tiempo esperando."),

            new Paso("desempeno-lead", "El nivel Lead",
                "Un Lead reparte parte de los puntos, así que no compite contra los niveles que " +
                "evalúa y por omisión no sale en el ranking individual. Con esto entra fuera de " +
                "concurso: se ve su total pero no ocupa lugar. En el ranking por equipo cuenta " +
                "siempre, porque ahí lo que compite son los equipos."),

            new Paso("desempeno-vistas", "Las tres vistas",
                "Ranking individual, ranking por equipo —que suma los puntos de sus integrantes " +
                "más los que se le dieron al equipo entero— y la cola de aprobación, que lleva " +
                "entre paréntesis cuántas actividades quedan por decidir."),

            new Paso("desempeno-cola", "Lo que espera decisión",
                "De lo más antiguo a lo más reciente, y se puede marcar más de una fila para " +
                "resolverlas en tanda. La columna «Vueltas» y la barra del canto marcan las que ya " +
                "se rechazaron una vez y el desarrollador replicó: eso no es una propuesta nueva " +
                "sino una discusión abierta, y conviene leerla antes de repetir el mismo «no»."),

            new Paso("desempeno-acciones", "Aprobar, rechazar, ajustar",
                "Aprobar abona los puntos en el acto y deja la entrada cerrada para siempre: ya " +
                "quedó contada en un ranking que la gente vio. Rechazar exige un motivo, que es lo " +
                "único con lo que la otra persona puede corregir. Y «Ajustar puntos» cambia el " +
                "valor pero NO aprueba nada: la entrada sigue esperando tu decisión."),

            new Paso("desempeno-evidencia", "Con qué se decide",
                "Al marcar una fila aparece aquí su evidencia completa: el comentario tal y como " +
                "se escribió, el enlace, el tiempo declarado, la captura y el historial de las " +
                "vueltas anteriores. Está siempre a la vista y no detrás de un botón porque " +
                "revisar es mirar esto.")),

        // ── Mis asignaciones ─────────────────────────────────────────────────
        new Recorrido("/mis-asignaciones", "Mis asignaciones y el cronómetro",

            Paso.Portada("Lo que te toca",
                "Los requerimientos que están a tu nombre y el cronómetro con el que se registra " +
                "el tiempo de cada uno. Lo que midas aquí es lo que después se contrasta con lo " +
                "que se estimó, así que es el registro que sostiene toda la planeación."),

            new Paso("asignaciones-devops", "Traer lo de DevOps",
                "Da de alta como requerimientos los work items abiertos que están a tu nombre y te " +
                "los asigna, para poder cronometrarlos. Trabaja sobre lo que ya se sincronizó, así " +
                "que es inmediato; para ir a buscar novedades a DevOps está «Mis tickets»."),

            new Paso("asignaciones-sin-conexion", "Sin conexión en vivo",
                "Mientras esta señal esté puesta, el cronómetro no está mandando latidos al " +
                "servidor. Lo trabajado hasta ahora no se pierde, pero si el corte se alarga el " +
                "servidor cierra la sesión con el último latido que recibió, y el tiempo posterior " +
                "no queda registrado."),

            new Paso("asignaciones-cronometro", "El que está corriendo",
                "Solo puede haber un cronómetro en marcha por persona: arrancar otro pausa éste, " +
                "sea de otro requerimiento o de una actividad del pool. El contador se calcula con " +
                "la hora del servidor, así que un equipo con el reloj mal puesto sigue midiendo " +
                "bien. Y cerrar la pestaña ya no detiene nada."),

            new Paso("asignaciones-detener", "Pausar o detener",
                "Pausar guarda lo llevado y deja la sesión lista para seguir. Detener la cierra: " +
                "consolida el tiempo y, si el líder activó el reporte, lo escribe también en el " +
                "ticket de Azure DevOps. Si esa parte falla, el cronómetro se detiene igual y el " +
                "tiempo queda guardado aquí; el mensaje de la operación lo dice."),

            new Paso("asignaciones-tabla", "Tus requerimientos",
                "Las filas en rojo tienen el compromiso vencido y sin entregar. «Tiempo dedicado» " +
                "es lo acumulado de todas las sesiones, y la fila que esté corriendo enseña el " +
                "contador en vivo para que no discrepe del reloj grande de arriba."),

            new Paso("asignaciones-arrancar", "Empezar a medir",
                "Arranca el cronómetro de esa fila. Si el requerimiento está todavía en «Por " +
                "estimar» o «Estimado», arrancarlo lo mueve a «En desarrollo» en la misma " +
                "operación —se avisa antes de hacerlo—, porque empezar a medir tiempo es empezar " +
                "a desarrollarlo y pedir además el cambio a mano garantizaba que el tablero " +
                "mintiera.")),

        // ── Requerimientos (líder) ───────────────────────────────────────────
        new Recorrido("/requerimientos", "Los requerimientos",

            Paso.Portada("El trabajo del área",
                "Aquí se dan de alta los requerimientos, se corrigen, se decide quién los trabaja " +
                "y se cancelan. Es la lista de la que salen el sprint, las asignaciones de cada " +
                "quien y los cronómetros: lo que no esté aquí no existe para el resto."),

            new Paso("requerimientos-filtros", "Acotar la lista",
                "Los tres filtros se combinan, y el de texto busca en el título y en la " +
                "descripción. A la derecha, el resumen dice cuántos requerimientos quedan a la " +
                "vista y cuántos de ellos tienen el compromiso vencido."),

            new Paso("requerimientos-importar", "Traer de Azure DevOps",
                "Da de alta como requerimientos los work items asignados y abiertos que todavía no " +
                "existan aquí, y los reparte según las reglas de auto-asignación, lo que significa " +
                "avisarle a quien le toque. Lo que ya existía solo se actualiza y lo cerrado no " +
                "entra. Se confirma antes porque escribe en lote y sobre datos de todo el equipo."),

            new Paso("requerimientos-tabla", "Cómo leer la tabla",
                "La fila entera en rojo es compromiso vencido y sin entregar, con la fecha del " +
                "servidor. Los puntos de color de Estado y de Prioridad son los mismos que en Mis " +
                "asignaciones y en Sprint. «Origen» resalta lo que vino de DevOps, y «Docs» cuenta " +
                "los documentos sin llegar a bajarlos."),

            new Paso("requerimientos-asignar", "Quién lo trabaja",
                "Abre la lista de desarrolladores; lo que quede marcado es la asignación final, " +
                "así que desmarcar a alguien es quitárselo. Solo se le avisa a quien no lo tenía " +
                "antes: volver a guardar sin cambios no manda ningún aviso a nadie."),

            new Paso("requerimientos-documentos", "Los dos documentos",
                "Cada requerimiento lleva dos clases de documento —el del requerimiento y el de la " +
                "estimación—, y por eso hay un botón de subir para cada una en vez de una lista " +
                "donde elegir. Los archivos se piden solo al abrir este panel; en la tabla nada " +
                "más viaja el número. Ojo: aquí «Eliminar» sí borra, y no se puede deshacer."),

            new Paso("requerimientos-cancelar", "Cancelar no es borrar",
                "Pasa el requerimiento a estado «Cancelado» y ahí se queda, a la vista y sin " +
                "contar en el avance del sprint. No se borra a propósito: con él se irían su " +
                "tiempo cronometrado, sus asignaciones y su historia en el sprint donde estuvo " +
                "comprometido.")),

        // ── Sprint ───────────────────────────────────────────────────────────
        new Recorrido("/sprint", "El sprint y su avance",

            Paso.Portada("El sprint",
                "El seguimiento de lo comprometido para este período: qué entró, cómo va y cuánto " +
                "tiempo queda. Toda la pantalla se lee con una sola comparación —si el avance va " +
                "por detrás del tiempo consumido, vamos atrasados—. El desarrollador la ve en " +
                "modo consulta; armar el sprint es del líder."),

            new Paso("sprint-selector", "Qué sprint se mira",
                "Se abre en el que corre hoy y, si no hay ninguno en curso, en el más reciente. " +
                "Cambiarlo trae su seguimiento completo, y los sprints ya cerrados se siguen " +
                "pudiendo consultar desde aquí."),

            new Paso("sprint-solo-mios", "Solo los míos",
                "Esconde de la lista lo que no está a tu nombre. Los porcentajes de arriba NO " +
                "cambian: el avance se calcula siempre sobre el sprint entero, porque dos " +
                "porcentajes distintos para el mismo sprint serían dos verdades."),

            new Paso("sprint-requerimientos", "Colgarle trabajo",
                "Marca aquí lo que entra al sprint; lo que desmarques vuelve al backlog sin perder " +
                "nada de lo suyo. No se listan los requerimientos que ya están en otro sprint ni " +
                "los cancelados, así que nada puede quedar comprometido en dos sitios a la vez."),

            new Paso("sprint-tarjetas", "El estado en cinco números",
                "El veredicto resume la comparación de las dos barras de abajo. «Avance real» es " +
                "cuánto del trabajo está hecho y «Tiempo consumido» cuánto del calendario se " +
                "gastó; lo cancelado queda fuera del cálculo. «Días restantes» son días de " +
                "calendario, no jornadas."),

            new Paso("sprint-linea", "La línea de tiempo",
                "Arriba, el calendario del sprint con los fines de semana sombreados y la línea de " +
                "HOY. Los triángulos marcan compromisos sobre su día: llenos los ya entregados, " +
                "huecos los que siguen pendientes. Abajo, las dos barras comparables: la gris es " +
                "el tiempo gastado y la de color el avance real. Si la de color va detrás, el " +
                "sprint va atrasado."),

            new Paso("sprint-comprometidos", "Lo comprometido",
                "Cada fila es un requerimiento del sprint, y la silueta de la primera columna " +
                "marca los que están a tu nombre. La fecha de compromiso en rojo ya venció. Esta " +
                "lista no se edita aquí: el estado y el avance de cada requerimiento se cambian en " +
                "«Requerimientos»."),

            new Paso("sprint-velocidad", "La velocidad del equipo",
                "El promedio de requerimientos entregados por sprint ya cerrado. Es el número con " +
                "el que se decide cuánto comprometer en el siguiente: sin él, el compromiso es un " +
                "deseo. Aparece en cuanto cierre el primer sprint, y la línea punteada de la " +
                "gráfica lo dibuja para ver de un vistazo qué sprint se salió del promedio.")),

        // ── Mis actividades ──────────────────────────────────────────────────
        new Recorrido("/mis-actividades", "Mis actividades y mis puntos",

            Paso.Portada("Lo que hiciste",
                "Esta pantalla quedó de CONSULTA: registrar puntos por tu cuenta se retiró, y ahora " +
                "el trabajo se propone al pool para que el líder le ponga valor antes de hacerlo. " +
                "Lo que sigue vivo aquí es lo que ya mandaste —corregirlo y replicar un rechazo— y " +
                "las actividades libres, que nunca dieron puntos y siguen guardando tiempo y " +
                "evidencia."),

            new Paso("actividades-vistas", "Las dos pestañas",
                "«Actividades y puntos» es el historial de lo que registraste cuando esto daba " +
                "puntos, con lo que todavía esté pendiente o rechazado. «Actividades libres» es " +
                "para el trabajo que no corresponde a ningún requerimiento asignado —soporte, " +
                "juntas, investigación, apoyo a otro equipo—: ésas nunca dieron puntos, sirven " +
                "para que su tiempo y su evidencia queden en algún lado."),

            new Paso("actividades-registrar", "Registrar una actividad: se retiró",
                "Está deshabilitado y no va a volver a encenderse. Registrar puntos por tu cuenta " +
                "era ponerle valor a algo ya hecho, y el pool hace lo contrario: se sabe cuánto " +
                "vale antes de empezar. Lo que hacías aquí se hace ahora en «Mi trabajo» con " +
                "«Proponer trabajo»: la propuesta nace a tu nombre y el líder le pone tipo y " +
                "complejidad, de donde salen los puntos."),

            new Paso("actividades-lista", "Cómo va cada una",
                "El punto de color dice si está aprobada, pendiente o rechazada, y la insignia con " +
                "la flecha cuenta las veces que ha ido y vuelto. «Motivo del líder» es lo que hay " +
                "que leer antes de tocar nada: sin eso, corregir es adivinar."),

            new Paso("actividades-corregir", "Corregir",
                "Cambia lo que mandaste, y solo aparece mientras la entrada no esté aprobada: una " +
                "aprobada ya se contó en un ranking. Corregir NO la devuelve a revisión ni le " +
                "contesta a nadie; si te la rechazaron y quieres que la vuelvan a mirar, eso es " +
                "«Replicar»."),

            new Paso("actividades-replicar", "Replicar",
                "No duplica la entrada: le contesta al líder que la rechazó. Se escribe un " +
                "argumento —con el motivo del rechazo delante, para no volver a discutir lo que ya " +
                "se dijo— y la entrada regresa a la cola de revisión contando una vuelta más."),

            new Paso("actividades-libre-nueva", "Una actividad libre",
                "Para el trabajo que no cuelga de ninguno de tus requerimientos. Se le va " +
                "acumulando el tiempo de las sesiones de cronómetro y se le adjunta evidencia, y " +
                "mientras esté abierta se puede editar. Cerrarla detiene el cronómetro si estaba " +
                "corriendo y consolida el tiempo; para volver a tocarla hay que reabrirla."),

            new Paso("actividades-evidencia", "La evidencia",
                "Es lo que convierte «estuve seis horas en esto» en algo que el líder puede " +
                "revisar: se adjuntan archivos con una nota de qué aporta cada uno. El panel se " +
                "abre y se cierra con este mismo botón, y en una actividad ya cerrada la evidencia " +
                "se puede consultar pero no cambiar.")),

        // ── Mi trabajo ───────────────────────────────────────────────────────
        //
        // La ruta sigue diciendo «mi-pool» y el recorrido dice «Mi trabajo»: el rótulo cambió cuando
        // ésta pasó a ser la única pantalla de trabajo del desarrollador, y la ruta se quedó porque
        // cambiarla habría dejado con 404 los avisos que guardaron «pool» como destino.
        new Recorrido("/mi-pool", "Mi trabajo: proponer, tomar y entregar",

            Paso.Portada("Mi trabajo",
                "El pool es trabajo publicado con su valor en puntos ya fijado, así que se sabe " +
                "cuánto vale antes de tomarlo. En «Disponibles» está lo que todavía no ha tomado " +
                "nadie; en esta primera pestaña, lo que ya está a tu nombre, con el checklist que " +
                "hay que cumplir para poder entregarlo. Y si lo que tienes que hacer no está " +
                "publicado, se propone desde aquí: ésta es la única puerta por la que entra tu " +
                "trabajo al desempeño."),

            new Paso("mi-pool-proponer", "Proponer trabajo",
                "Para lo que hay que hacer y nadie ha publicado: una investigación, un " +
                "apagafuegos, ayudar a otro equipo. Se manda el título, el detalle y un enlace, y " +
                "NADA MÁS: ni tipo, ni horas, ni puntos. Eso lo pone el líder, y por eso vas a " +
                "saber cuánto vale antes de hacerlo, que es justo lo que la vieja autocalificación " +
                "no podía dar. Cuando la clasifique te la encuentras tomada, con su plazo y su " +
                "checklist; mientras tanto ocupa un sitio de tu tope, igual que una tomada."),

            new Paso("mi-pool-mias", "Lo que tienes tomado",
                "«Entregar antes de» es el PLAZO, y lleva la hora porque son horas de reloj: " +
                "corren de noche y en fin de semana. «Esfuerzo» es otra cosa: cuánto trabajo se " +
                "dijo que costaría, y es contra lo que se va a comparar tu cronómetro. Si el plazo " +
                "vence, la actividad se resalta pero sigue siendo tuya; quitártela solo puede el " +
                "líder."),

            new Paso("mi-pool-checklist", "Qué hay que cumplir",
                "No es un trámite: es la lista, escrita de antemano, de lo que hace falta para dar " +
                "la actividad por terminada. Los puntos marcados con «¿Exige enlace?» piden la " +
                "dirección del pull request o del ticket al marcarlos, y mientras queden puntos " +
                "sin hacer el servidor no acepta la entrega."),

            new Paso("mi-pool-cronometro", "Medir el tiempo",
                "Arranca el cronómetro sobre esta actividad. Solo puede haber uno corriendo a la " +
                "vez: si tenías otro, se pausa. Si el líder activó el aviso de inicio y la actividad " +
                "viene de un work item de Azure DevOps, al arrancar se comenta allá con la hora a la " +
                "que empezaste —una vez, no en cada reanudación—. Para pausar o detener, Mi jornada."),

            new Paso("mi-pool-entregar", "Entregar",
                "Manda la actividad a la cola de verificación del líder y deja de estar en tus " +
                "manos hasta que él la acepte —y entonces se abonan los puntos— o te la devuelva " +
                "con un motivo, que aparecerá aquí mismo en un aviso la próxima vez que la abras."),

            new Paso("mi-pool-devolver", "Devolver al pool",
                "La suelta y vuelve a quedar libre para cualquiera. No resta puntos ni queda " +
                "registrado en tu contra, y es a propósito: penalizarlo haría que nadie se " +
                "atreviera con lo difícil, que es lo contrario de lo que se busca. El motivo es " +
                "opcional."),

            new Paso("mi-pool-disponibles", "Lo que hay libre",
                "Se elige mirando tres datos: los puntos, el plazo —el tiempo que vas a tener " +
                "desde que la tomes— y «Además se evalúa», que son los criterios extra que suman " +
                "de más si se cumplen. Eso último se anuncia aquí a propósito: enterarse después " +
                "de que también había que documentarla convertiría el extra en una trampa."),

            new Paso("mi-pool-tomar", "Tomarla",
                "Todavía no la toma: abre un panel que enseña primero cuánto vale y cuánto tiempo " +
                "hay. Si es un bug, ahí se te pide en cuántas horas crees resolverlo y el botón no " +
                "deja seguir sin ese número; se pregunta en ese momento porque es el único en que " +
                "la estimación es honesta. Si alguien más la toma mientras decides, el panel se " +
                "cierra solo. Y si la actividad viene de un ticket de Azure DevOps, tomarla lo " +
                "pone a tu nombre y en «en progreso» allá, sin que tengas que abrir DevOps."),

            new Paso("mi-pool-fuentes", "Dónde se fueron las otras pantallas",
                "Requerimientos, tickets de DevOps, SLA y actividades libres SIGUEN AHÍ: dejaron de " +
                "estar en el menú, no de existir, y esta sección lleva a cada una. Salieron porque " +
                "no son sitios a los que ir a trabajar sino FUENTES de trabajo: cuando de alguna " +
                "sale algo que hacer, se convierte en una actividad del pool, que es donde vive su " +
                "valor y su plazo. Antes había nueve pantallas y el mismo requerimiento salía en " +
                "cuatro; ahora lo que tienes que hacer está en una.")),

        // ── El pool, del lado del líder ──────────────────────────────────────
        new Recorrido("/pool", "El pool del líder",

            Paso.Portada("El pool, del lado del líder",
                "Tres trabajos en una pantalla: publicar actividades con su valor, verificar lo " +
                "que se entrega y definir cuánto vale cada cosa. El valor no se teclea nunca —sale " +
                "de una matriz de tipo por complejidad—, y eso es lo que hace comparables las " +
                "actividades de una persona con las de otra."),

            new Paso("pool-publicar", "Publicar una actividad",
                "Abre el formulario. Los puntos son una etiqueta que se actualiza sola al elegir " +
                "tipo y complejidad, no un campo. Y hay un solo campo de horas que cambia de " +
                "significado: en un Bug es el PLAZO —el esfuerzo lo estimará quien lo tome— y en " +
                "una Tarea o un Requerimiento es el ESFUERZO, porque ahí el plazo lo pone la " +
                "matriz."),

            new Paso("pool-descuento", "Quitar puntos",
                "Es la única puerta por la que se aplica algo negativo, y por eso vive aquí: un " +
                "descuento ES una actividad del pool que nace pagada, y así hay un solo sitio en " +
                "toda la aplicación donde se escriben puntos. Se elige a la persona y un criterio " +
                "del catálogo de los que restan, y el MOTIVO es obligatorio: aquí no hay entrega ni " +
                "checklist que expliquen nada, así que esa frase es lo único que va a poder leer " +
                "quien lo reciba. Se puede anular después, y entonces quedan las dos anotaciones a " +
                "la vista. Y si lo que hay es trabajo que HACER —aunque no debiera haber hecho " +
                "falta—, eso no es un descuento: es un Retrabajo, y se publica con el botón de " +
                "arriba."),

            new Paso("pool-pendiente-devops", "Lo que DevOps no tiene",
                "Estas actividades están ligadas a un work item al que no le llegó algo que el " +
                "pool sí dice: el esfuerzo, la prioridad, a nombre de quién tiene que estar o que " +
                "pase a «en progreso». El aviso del momento lo vio una persona y cerró la " +
                "pestaña, así que sin esta lista el ticket se quedaría mal sin que nadie lo " +
                "supiera. El botón con el número abre la tarjeta del vínculo, que trae el detalle " +
                "y el reintento."),

            new Paso("pool-tabla", "El pool completo",
                "Mientras una actividad siga libre se puede editar y retirar. En cuanto tiene " +
                "dueño ya no: cambiarle el alcance o el valor a quien la está trabajando sería " +
                "cambiarle el trato a medio camino. Lo único que queda entonces es «Liberar», que " +
                "la devuelve al pool y le avisa a esa persona."),

            new Paso("pool-vistas", "Las tres pestañas",
                "«Pool» es todo lo publicado. «Verificación» es la cola de lo que la gente entregó " +
                "y espera tu decisión. «Configuración» es donde se define cuánto vale cada cosa y " +
                "qué checklist recibe cada tipo de actividad."),

            new Paso("pool-verificacion", "Lo entregado",
                "Al marcar una fila se abre debajo su checklist con las evidencias que dejó quien " +
                "la trabajó. La columna «Vuelta» avisa de las que ya se devolvieron alguna vez, y " +
                "eso conviene saberlo antes de abrirlas. Aceptar abona los puntos en el acto y " +
                "cuentan en el ranking del mes; devolver exige motivo, que es lo único con lo que " +
                "la otra persona puede arreglarlo."),

            new Paso("pool-criterios", "Los criterios extra",
                "Son los puntos que se anunciaron de más al publicar la actividad y que solo suman " +
                "si se cumplieron. Hay que contestarlos todos, sí o no, antes de poder aceptar: se " +
                "avisa aquí y no al fallar el botón porque, una vez aceptada la entrega, esta " +
                "evaluación ya no se cambia. La lista que se ofrece al publicar es corta a " +
                "propósito: solo criterios que se pueden mirar sobre ESTA actividad y contestar " +
                "sin interpretar."),

            new Paso("pool-configuracion", "Cuánto vale cada cosa",
                "Arriba, la matriz: los puntos y las horas de cada combinación de tipo y " +
                "complejidad. Son horas de RELOJ, con noches y fines de semana dentro, así que 64 " +
                "h no son ocho jornadas. Cambiar la matriz no revalúa lo ya publicado: cada " +
                "actividad conserva el valor con el que salió. Abajo, el checklist que recibirá " +
                "quien tome una actividad de cada tipo, que tampoco toca las que ya están en " +
                "curso."))
    ];
}
