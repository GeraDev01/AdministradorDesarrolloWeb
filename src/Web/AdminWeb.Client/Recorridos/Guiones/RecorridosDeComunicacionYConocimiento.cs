namespace AdminWeb.Client.Recorridos.Guiones;

/// <summary>
/// Los recorridos de lo que el equipo se dice entre sí y de lo que deja escrito: el foro, la base de
/// conocimiento, la bandeja de avisos, la bitácora, las notas del líder, las sugerencias y las
/// pantallas sueltas —la portada y el cambio de contraseña—.
///
/// <para>NO HAY RECORRIDO PARA EL ACCESO NI PARA EL SEGUNDO FACTOR, y no es un olvido: las dos van
/// con <c>LayoutVacio</c>, que no tiene barra superior y por tanto tampoco el botón que lanza los
/// recorridos. Un guion escrito para ellas no se podría lanzar nunca y solo serviría para que alguien
/// lo diera por probado. Las dos explican lo suyo en la propia pantalla, que es donde hace falta
/// cuando todavía no se ha entrado.</para>
/// </summary>
public sealed class RecorridosDeComunicacionYConocimiento : IFuenteDeRecorridos
{
    public IEnumerable<Recorrido> Recorridos() =>
    [
        // ── Las pantallas sueltas ────────────────────────────────────────────────
        //
        // La raíz «/» NO tiene recorrido, y no es un olvido: dejó de ser una pantalla. Ahora es un
        // desvío que manda a cada rol donde empieza su trabajo —al Dashboard, o a Despliegues si es
        // operaciones— y nadie llega a verla. La prueba de cobertura tampoco lo exige, porque va con
        // LayoutVacio y ahí no hay barra donde pintar el botón de ayuda.

        new Recorrido("/cambiar-contrasena", "Cambiar tu contraseña",

            Paso.Portada("Tu contraseña",
                "Desde aquí se cambia la contraseña con la que entras. Se puede hacer cuando " +
                "quieras, y hay un caso en el que la aplicación te trae sola: cuando la que tienes " +
                "es temporal."),

            new Paso("contrasena-temporal", "Por qué acabaste aquí",
                "Entraste con una contraseña de un solo uso, de las que se dan al abrir una cuenta o " +
                "al reponer un acceso. Mientras siga puesta, el resto de la aplicación no responde: " +
                "el servidor rechaza todo lo demás hasta que pongas una tuya."),

            new Paso("contrasena-nueva", "Las dos casillas",
                "Lo único que se comprueba en tu equipo es que las dos coincidan; si no coinciden, " +
                "no se manda nada. El largo mínimo y lo demás lo revisa el servidor, y su respuesta " +
                "sale aquí mismo."),

            new Paso("contrasena-guardar", "Lo que pasa al guardar",
                "Al cambiarla se cierran las sesiones que tengas abiertas en otros equipos o " +
                "navegadores: habrá que volver a entrar en cada uno con la nueva. Si sale bien, la " +
                "aplicación te devuelve al inicio.")),

        // ── Avisos ───────────────────────────────────────────────────────────────

        new Recorrido("/avisos", "La bandeja de avisos",

            Paso.Portada("Tus avisos",
                "Aquí llega lo que la aplicación tiene que decirte: que te asignaron algo, que " +
                "contestaron tu sugerencia, un comunicado. La bandeja es personal y no hay forma de " +
                "mirar la de otra persona — el destinatario sale de tu sesión, no de un filtro de " +
                "esta pantalla."),

            new Paso("avisos-rejilla", "Leer un aviso",
                "Los que no has abierto van en negritas y con un punto en la primera columna. El " +
                "doble clic marca el aviso como leído y, si trae enlace, abre en otra pestaña la " +
                "pantalla de la que habla; si no lo trae, saca el texto completo, que en la tabla " +
                "sale recortado."),

            new Paso("avisos-marcar-todo", "Dejar la bandeja al día",
                "Marca todo lo pendiente de una vez y baja el contador del menú. No borra nada: los " +
                "avisos se quedan aquí y se pueden volver a leer cuando haga falta."),

            new Paso("avisos-push", "Avisos con la pestaña cerrada",
                "Es lo que la aplicación de escritorio hacía desde la bandeja del sistema. El " +
                "permiso lo concede el navegador y solo lo pide al pulsar aquí; si se niega, no " +
                "vuelve a preguntar y hay que reactivarlo a mano en su configuración. Vale para este " +
                "navegador y este equipo, no para tu cuenta.")),

        // ── Bitácora ─────────────────────────────────────────────────────────────

        new Recorrido("/bitacora", "La bitácora",

            Paso.Portada("Qué se registró",
                "El registro de lo que ha pasado en el sistema: quién entró, quién dio de alta o " +
                "cambió algo, quién desplegó. Solo la ve el líder, nada de lo que entra aquí se " +
                "borra, y es la pantalla que se abre cuando hay que reconstruir un incidente."),

            new Paso("bitacora-filtros", "Acotar antes de leer",
                "Los filtros se aplican a la vez y cada cambio vuelve a preguntarle al servidor " +
                "desde la primera página. El rango arranca en los últimos 30 días a propósito: ésta " +
                "es la tabla que más crece de todo el sistema. «Hasta» toma el día completo, no su " +
                "medianoche."),

            new Paso("bitacora-accion", "Buscar por tipo de operación",
                "Acota a una sola clase de hecho —entrar, dar de alta, desplegar— y es por donde se " +
                "empieza cuando no se recuerda ni quién fue ni qué día. Con el rango puesto, deja la " +
                "lista en algo que se puede leer entero."),

            new Paso("bitacora-texto", "Buscar dentro del detalle",
                "Busca en los detalles y en la entidad de cada entrada, no en el nombre del usuario. " +
                "Sirve para seguirle la pista a un identificador concreto o al nombre de un programa " +
                "a lo largo de varias operaciones."),

            new Paso("bitacora-rejilla", "Cómo se lee una fila",
                "Cada renglón es un intento, saliera bien o mal. El punto de «Resultado» separa lo " +
                "que falló —ámbar, la operación no se pudo completar— de lo denegado —rojo, alguien " +
                "intentó algo que no le toca—, que son dos cosas distintas y las dos que se vienen a " +
                "buscar aquí. «Origen» es la IP y el navegador desde donde se hizo, porque en la web " +
                "todo sale del mismo servidor; «Correlación» agrupa las entradas de una misma " +
                "operación larga, como un despliegue completo."),

            new Paso("bitacora-total", "Cuántas hay de verdad",
                "Es el total que cumple el filtro, no lo que se ve en pantalla: la rejilla trae del " +
                "servidor únicamente la página que estás mirando. Si el número sale enorme, aprieta " +
                "el rango antes de ponerte a pasar páginas.")),

        // ── Notas y pendientes ───────────────────────────────────────────────────

        new Recorrido("/notas", "Notas y pendientes",

            Paso.Portada("Lo que no se debe olvidar",
                "El cuaderno del líder: lo que alguien le comenta de pasada y no puede perderse. " +
                "Cada nota puede llevar una fecha de recordatorio y se cierra cuando se atiende."),

            new Paso("notas-contadores", "Cuántas reclaman algo",
                "Las cifras las cuenta el servidor sobre TODAS las notas, así que no se mueven al " +
                "filtrar la lista. Lo vencido lo decide además el reloj del servidor y no el de tu " +
                "equipo: una computadora con la fecha mal puesta no puede opinar sobre qué va con " +
                "retraso."),

            new Paso("notas-nueva", "Apuntar una",
                "Abre el panel de captura debajo, en esta misma pantalla. Lo único obligatorio es el " +
                "título: una nota sirve igual con dos palabras apuntadas al vuelo que con el detalle " +
                "completo."),

            new Paso("notas-excel", "Sacarlo a un archivo",
                "Exporta la lista con el mismo filtro que tengas puesto: con «Solo pendientes» " +
                "encendido, el archivo trae solo lo pendiente. Es lo que se lleva a una reunión sin " +
                "copiar renglones a mano."),

            new Paso("notas-recordatorio", "Con fecha o sin ella",
                "Vaciar este campo es «sin recordatorio»; no hace falta ninguna casilla aparte. Con " +
                "fecha puesta, el día que se pase la nota empieza a contar como vencida y su renglón " +
                "se pinta en rojo hasta que se cierre."),

            new Paso("notas-rejilla", "Leer la lista de un vistazo",
                "El renglón entero lleva el aviso: en rojo y negrita lo vencido, en gris lo cerrado. " +
                "La columna «Dev (origen)» dice quién te lo comentó, no a quién se le encarga — una " +
                "nota no se le asigna a nadie."),

            new Paso("notas-acciones", "Abrir, cerrar y borrar",
                "«Abrir» trae la nota al panel de arriba para cambiarla. «Completar» es de ida y " +
                "vuelta —lo cerrado se reabre sin perder nada— y por eso no pregunta. «Eliminar» sí " +
                "pregunta, y avisa aparte cuando la nota seguía pendiente.")),

        // ── Sugerencias ──────────────────────────────────────────────────────────

        new Recorrido("/mis-sugerencias", "Sugerencias",

            Paso.Portada("Proponer y apoyar",
                "Dos vistas de lo mismo: en esta pestaña van las sugerencias que mandaste tú, con la " +
                "respuesta del líder cuando llega; en la de al lado, las que el equipo dejó públicas " +
                "para poder apoyarlas."),

            new Paso("sugerencias-nueva", "Mandar una",
                "Abre el formulario aquí mismo, y dentro se decide quién la ve y si va con tu " +
                "nombre. Cerrarlo tira lo escrito: no queda ningún borrador guardado."),

            new Paso("sugerencias-visibilidad", "Quién la ve",
                "Pública la lee el equipo entero; la otra opción la deja solo para el líder. Al " +
                "elegir ésa, la casilla de apoyos se apaga sola — abrir a votación algo que nadie " +
                "más puede leer solo haría creer que hay una votación."),

            new Paso("sugerencias-anonima", "Mandarla sin tu nombre",
                "Anónima significa que ni siquiera el líder sabe quién la escribió: el nombre no " +
                "sale del servidor, así que no es que esta pantalla lo esconda. Aun así se te avisa " +
                "en privado cuando la contesten."),

            new Paso("sugerencias-mias", "El seguimiento de las tuyas",
                "El punto de color dice en qué va cada una, y la columna de la derecha trae la " +
                "respuesta del líder cuando la escribe. El botón de eliminar existe solo mientras la " +
                "sugerencia siga en «Nueva»: en cuanto el líder empieza a atenderla desaparece, " +
                "porque borrarla dejaría su respuesta colgando de algo que ya no está."),

            new Paso("sugerencias-propuestas", "Lo que propuso el equipo",
                "Las públicas de todos, de la más apoyada a la menos. Un guion en «Apoyos» en lugar " +
                "de un número quiere decir que su autor la dejó fuera de votación: se lee, pero no " +
                "se apoya."),

            new Paso("sugerencias-apoyar", "Apoyar una propuesta",
                "Suma tu voto, y volver a pulsar lo quita. Al cambiar el número la lista se reordena " +
                "sola, así que la propuesta puede moverse de sitio delante de ti.")),

        // ── Foro ─────────────────────────────────────────────────────────────────

        new Recorrido("/foro", "El muro del foro",

            Paso.Portada("El foro del equipo",
                "Donde el equipo de desarrollo conversa: una idea, una pregunta, algo que " +
                "aprendiste y no debería perderse. Cada publicación abre un hilo con sus " +
                "comentarios. El foro es del líder y de los desarrolladores; Operaciones no entra."),

            new Paso("foro-publicar", "Publicar algo",
                "Despliega el formulario en su sitio, sin abrir ninguna ventana. El texto se guarda " +
                "tal cual —las direcciones se vuelven enlaces solas y se pueden pegar capturas con " +
                "Ctrl+V—, y al publicar la pantalla te lleva directo al hilo recién creado. Cerrar " +
                "el formulario tira lo que llevaras escrito."),

            new Paso("foro-filtros", "Encontrar algo de hace tiempo",
                "La búsqueda se manda al salir del campo o con Enter, no mientras tecleas: cada " +
                "cambio es una consulta al servidor. Los filtros se combinan entre ellos, y el de " +
                "tiempo es el que sirve cuando recuerdas más o menos cuándo se habló de algo."),

            new Paso("foro-tarjeta", "Cada tarjeta es un hilo",
                "El clic en cualquier parte de la tarjeta lo abre. El corazón y el número de " +
                "comentarios cuentan lo que ya pasó dentro —desde aquí no se pulsan— y el corazón " +
                "lleno dice que tú ya lo apoyaste. Las fijadas van primero y el resto por última " +
                "actividad, así que un hilo viejo que alguien revive vuelve a subir."),

            new Paso("foro-auditoria", "Quién dijo qué",
                "La pestaña de auditoría, solo para el líder: todas las entradas del foro en una " +
                "tabla, comentarios incluidos, con lo editado y lo retirado marcados. Es para " +
                "reconstruir una conversación, no para participar; el doble clic abre el hilo. Nada " +
                "se borra nunca, pero el texto de lo retirado tampoco se devuelve aquí.")),

        new Recorrido("/foro/{Id:int}", "Un hilo del foro",

            Paso.Portada("La conversación",
                "La publicación arriba y debajo sus comentarios. Esta dirección es propia del hilo, " +
                "así que se puede pegar en un mensaje y quien la abra cae justo aquí."),

            new Paso("hilo-entrada", "Quién contesta a quién",
                "La tarjeta con la franja de color a la izquierda es la publicación que abrió el " +
                "hilo; las demás son comentarios, y su sangría dice de cuál cuelga cada uno. La " +
                "sangría se corta a los cinco niveles para que el texto no se vaya al margen. Un " +
                "«(editado)» junto a la fecha avisa de que su autor la cambió después."),

            new Paso("hilo-me-gusta", "Apoyar una entrada",
                "Pone o quita tu apoyo sobre esa entrada en concreto, no sobre el hilo entero. El " +
                "número que queda es el que devuelve el servidor y no una suma hecha aquí, así que " +
                "recoge también lo que otros hayan apoyado mientras leías."),

            new Paso("hilo-responder", "Responder",
                "Abre el recuadro para contestarle a ESA entrada, y tu comentario queda colgando de " +
                "ella, sangrado. Para contestarle a la publicación y no a un comentario está el " +
                "botón del final del hilo. Un hilo cerrado se sigue leyendo, pero ya no admite " +
                "comentarios nuevos."),

            new Paso("hilo-editar", "Corregir lo tuyo",
                "Sale solo sobre lo que escribiste tú, y no borra el rastro: la entrada queda " +
                "marcada como editada. Desde aquí se pueden quitar además las capturas que ya " +
                "tenía, marcando una por una las que sobran."),

            new Paso("hilo-retirar", "Retirar, que no es borrar",
                "La entrada conserva su hueco en la conversación —si desapareciera, las respuestas " +
                "que le contestan dejarían de tener sentido— y su texto deja de mostrarse. No se " +
                "puede deshacer desde la aplicación, y por eso pregunta antes. El líder puede " +
                "retirar cualquier entrada, no solo la suya.")),

        // ── Base de conocimiento ─────────────────────────────────────────────────

        new Recorrido("/conocimiento", "La base de conocimiento",

            Paso.Portada("La documentación del equipo",
                "Cómo se hace tal cosa, qué significa tal término, por qué aquello quedó así. Aquí " +
                "se entra con una pregunta concreta más que a mirar qué hay, y por eso el buscador y " +
                "los temas están arriba del todo."),

            new Paso("conocimiento-buscar", "Buscar",
                "Mira en el título y también DENTRO del texto de cada artículo, así que vale con una " +
                "palabra que recuerdes del contenido. Se manda al salir del campo o con Enter, no " +
                "mientras tecleas."),

            new Paso("conocimiento-temas", "El índice de temas",
                "Los temas son el índice de verdad, y el glosario está ahí dentro: un término es un " +
                "artículo corto etiquetado. El número dice cuántos artículos publicados usan cada " +
                "tema, que es lo que distingue algo documentado de una palabra que alguien escribió " +
                "una vez. Pulsar el tema que ya está encendido lo quita."),

            new Paso("conocimiento-estado", "Lo publicado y lo tuyo",
                "La lista arranca en «Publicado» porque lo que se consulta tiene que ser lo " +
                "revisado. Cambiando este filtro —o marcando «Solo lo mío»— salen tus borradores y " +
                "lo que te devolvieron. De los demás no verás nada sin publicar: no es que se " +
                "esconda aquí, es que no sale del servidor."),

            new Paso("conocimiento-escribir", "Escribir uno",
                "Abre el editor. Lo que guardes empieza como borrador privado: no lo ve nadie más, " +
                "tampoco el líder, hasta que tú lo mandes a revisar."),

            new Paso("conocimiento-devueltos", "Lo que te devolvieron",
                "Aparece cuando el líder te regresó algo con un motivo, y lleva a esa lista ya " +
                "filtrada. Está a la vista a propósito: un artículo devuelto que se queda en un " +
                "rincón no lo corrige nadie."),

            new Paso("conocimiento-cola", "La cola del líder",
                "Lo que el equipo mandó y sigue esperando respuesta, con cuántos son. Solo la ve el " +
                "líder, y el mismo número aparece en el menú para no tener que entrar a mirar.")),

        new Recorrido("/conocimiento/{Id:int}", "Un artículo",

            Paso.Portada("Un artículo abierto",
                "La pantalla de leer. Trae quién lo escribió, cuándo se publicó y cuándo se tocó por " +
                "última vez: en documentación esa fecha importa más que en ningún otro sitio, porque " +
                "un procedimiento de hace dos años y otro de la semana pasada no se leen igual."),

            new Paso("articulo-temas", "Lo demás que habla de esto",
                "Los temas son pulsables: cada uno lleva a la lista con todo lo que está etiquetado " +
                "igual. Es la mitad de lo que hace que etiquetar sirva de algo."),

            new Paso("articulo-revision", "Lo que decidió el líder",
                "El motivo de la última respuesta y, desplegando, el ida y vuelta completo de las " +
                "veces anteriores. Esto solo lo ven su autor y el líder: para el resto del equipo un " +
                "artículo publicado es el resultado, no el expediente de cómo se llegó a él."),

            new Paso("articulo-acciones", "Lo que puedes hacer",
                "Salen únicamente los botones que van a funcionar: quién puede editar, mandar a " +
                "revisar o borrar lo decide el servidor y viene dicho en el propio artículo. Editar " +
                "algo ya publicado lo devuelve a la cola de revisión —salvo que quien edite sea el " +
                "líder—, y borrar existe solo mientras el artículo no se haya publicado nunca."),

            new Paso("articulo-retirar", "Retirar de circulación",
                "Para cuando lo que dice deja de ser cierto. Lo publicado no se borra —alguien puede " +
                "tenerlo enlazado en un mensaje—: se retira dejando dicho por qué, y su autor puede " +
                "corregirlo y volver a mandarlo.")),

        new Recorrido("/conocimiento/revision", "La cola de revisión",

            Paso.Portada("Lo que espera respuesta",
                "Los artículos que el equipo mandó y que todavía nadie ha resuelto. Se leen y se " +
                "resuelven aquí mismo, uno detrás de otro, sin cambiar de pantalla."),

            new Paso("revision-cola", "Primero lo más viejo",
                "Esta lista va al revés que todas las demás: arriba lo que lleva más tiempo " +
                "esperando. La etiqueta de días se enciende con el tiempo porque a los tres días " +
                "quien escribió el artículo ya se pregunta si alguien va a leerlo, y a la semana ha " +
                "decidido que no. «vuelta 2» avisa de que ese artículo ya pasó por aquí antes."),

            new Paso("revision-articulo", "Leer y decidir",
                "El artículo completo y, debajo, la decisión. Publicar puede otorgar puntos: el " +
                "criterio y la cantidad viajan dentro de la misma aprobación, para que no se queden " +
                "sin dar. Devolver exige un motivo —el botón está apagado hasta que lo escribas— " +
                "porque quien recibe algo de vuelta sin explicación no corrige, abandona. Al " +
                "resolver uno se abre solo el siguiente."),

            new Paso("revision-historial", "Lo que ya se le pidió",
                "En una segunda o tercera vuelta esto es lo primero que hay que mirar: sin saber qué " +
                "se le pidió la vez pasada no hay forma de juzgar si lo corrigió."),

            new Paso("revision-actualizar", "Volver a pedir la cola",
                "Trae lo que haya llegado mientras tanto. Lo que tuvieras abierto se queda abierto " +
                "si sigue esperando: quitarlo de delante a media lectura sería peor que no " +
                "actualizar.")),

        // Dos rutas, dos recorridos, y los mismos pasos: escribir uno nuevo y corregir uno que ya
        // existe son la misma pantalla, pero se entra a ellas por direcciones distintas y con una
        // pregunta distinta en la cabeza. Con un solo recorrido, la mitad de las veces el botón se
        // quedaría apagado sin que nadie entendiera por qué.
        new Recorrido("/conocimiento/nuevo", "Escribir un artículo",
        [
            Paso.Portada("Escribir un artículo",
                "El editor de la base de conocimiento. Lo que guardes empieza como borrador " +
                "privado: no lo ve nadie más, ni el líder, hasta que tú lo mandes a revisar — y es a " +
                "propósito, para que se pueda escribir a medias sin nadie mirando por encima."),

            .. PasosDeEscribir()
        ]),

        new Recorrido("/conocimiento/{Id:int}/editar", "Editar el artículo",
        [
            Paso.Portada("Editar el artículo",
                "El mismo editor, con lo que ya estaba escrito. Se edita el texto TAL COMO se " +
                "tecleó, con sus marcas sin interpretar: eso es lo que se guarda, y lo que se lee en " +
                "la pantalla del artículo es el resultado de interpretarlo."),

            .. PasosDeEscribir()
        ])
    ];

    /// <summary>
    /// Los pasos que comparten las dos direcciones del editor. Están aquí y no copiados dos veces
    /// para que no puedan separarse: el día que cambie el botón de mandar a revisar, se arregla en un
    /// sitio y no en uno de los dos.
    /// </summary>
    private static Paso[] PasosDeEscribir() =>
    [
        new Paso("escribir-cuerpo", "El texto, sin vista previa",
            "No hay vista previa, y es a propósito: quien decide qué es un título, un enlace o un " +
            "bloque de código es el servidor, y una vista dibujada aquí acabaría enseñando algo " +
            "distinto de lo que se publica. Al guardar se abre el artículo tal como quedó de verdad."),

        new Paso("escribir-marcas", "Las marcas que se entienden",
            "Son seis y están desplegadas aquí para no tener que acordarse de ellas. Todo lo demás " +
            "es texto tal cual: no hay tablas ni HTML, y dentro de un bloque de código no se " +
            "interpreta nada, así que un script lleno de comentarios no se convierte en títulos."),

        new Paso("escribir-imagenes", "Diagramas y capturas",
            "Pulsa dentro del recuadro y pega la captura con Ctrl+V; también puedes elegir el " +
            "archivo. Se sube en ese momento y su marca queda puesta al final del texto, para que la " +
            "muevas donde la quieras. Cambia lo que va entre corchetes por lo que describe la " +
            "imagen: es lo que se lee cuando no carga. Solo se incrustan las imágenes que subas " +
            "aquí — una dirección de otro sitio se queda en enlace, porque lo que ilustra un " +
            "artículo revisado no puede cambiar después sin que nadie se entere."),

        new Paso("escribir-borrador", "Guardar y seguir otro día",
            "Guarda sin mandárselo a nadie. Un borrador puede quedarse a medias el tiempo que haga " +
            "falta; aparece en tu lista al cambiar el filtro de estado, y solo lo ves tú."),

        new Paso("escribir-mandar", "Mandarlo a revisar",
            "Guarda y lo pone en la cola del líder en el mismo gesto. Van juntos porque en dos pasos " +
            "el segundo se olvida y el artículo se queda en un borrador que nadie va a leer nunca. " +
            "Pide un mínimo de texto escrito: por debajo de eso no vale la pena que nadie abra la cola.")
    ];
}
