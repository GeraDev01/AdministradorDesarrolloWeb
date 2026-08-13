namespace AdminWeb.Client.Recorridos.Guiones;

/// <summary>
/// Los recorridos de las pantallas de PERSONAS, CATÁLOGOS, AUSENCIAS y JORNADA.
///
/// <para>Son diecisiete pantallas que comparten un mismo asunto —la gente del área: quién es, quién
/// está, quién falta y qué se le debe— y por eso los guiones viven juntos: lo que hay que explicar en
/// una es casi siempre la frontera con otra, y tenerlos delante es lo que evita contar dos veces la
/// misma regla o, peor, contarla distinto en cada sitio. El caso claro son los días de vacaciones:
/// «Desarrolladores» captura el número que se imprime, «Mis vacaciones» enseña el saldo que se
/// calcula y «Vacaciones del equipo» resuelve la petición; ninguno de los tres se entiende sin saber
/// qué hacen los otros dos.</para>
///
/// <para>Qué se cuenta en cada paso: lo que el control HACE y lo que no se ve —que una acción avisa a
/// alguien, que queda en la bitácora, que no se puede deshacer, que el saldo avisa pero no bloquea—.
/// Lo que ya dice la etiqueta del botón no se repite: un paso que no añade nada enseña a saltarse los
/// que sí lo hacen.</para>
///
/// <para>Cada marca que se cita aquí está puesta a mano en el marcado de su pantalla y hay una prueba
/// que lo comprueba una por una (<c>RecorridosGuiadosTests</c>). Si al quitar un control se pone
/// roja, se arregla moviendo la marca o quitando el paso, nunca ajustando la prueba.</para>
/// </summary>
public sealed class RecorridosDePersonasYAusencias : IFuenteDeRecorridos
{
    public IEnumerable<Recorrido> Recorridos() =>
    [
        .. Personas,
        .. Catalogos,
        .. MisAusencias,
        .. AusenciasDelLider,
        .. Jornada
    ];

    // ── Personas ─────────────────────────────────────────────────────────────────

    private static IReadOnlyList<Recorrido> Personas =>
    [
        new Recorrido("/usuarios", "Las cuentas de acceso",

            Paso.Portada("Las cuentas de acceso",
                "Aquí se dan de alta las cuentas con las que se entra a la aplicación, se les pone " +
                "rol y se resuelven los líos de acceso: contraseñas olvidadas, cuentas bloqueadas y " +
                "segundos factores perdidos. La ficha de desarrollador es otra cosa y vive en " +
                "«Desarrolladores»; aquí solo se administra la llave."),

            new Paso("usuarios-nuevo", "Crear una cuenta",
                "La contraseña no la escribes tú: la genera el servidor y se enseña una sola vez al " +
                "guardar, y quien entre con ella tendrá que cambiarla de inmediato. La ficha de " +
                "desarrollador que le ligues es la que hace que los comunicados y los puntos lleguen " +
                "a la persona correcta; hay cuentas sin ficha y fichas sin cuenta."),

            new Paso("usuarios-bloqueo", "Cuándo se bloquea sola",
                "Una cuenta se bloquea sola tras varios intentos fallidos y se suelta sola al pasar " +
                "los minutos que dice esta línea. «Desbloquear», en la fila, levanta ese bloqueo ya " +
                "mismo y no toca la contraseña: es lo que se usa cuando la persona está esperando " +
                "al lado."),

            new Paso("usuarios-2fa", "El segundo factor",
                "Es obligatorio para todas las cuentas, así que «sin activar» no es alguien que " +
                "decidió no usarlo: es una cuenta a la que se le va a exigir en cuanto entre. La " +
                "columna dice desde cuándo lo tiene y cuántos códigos de rescate le quedan, y ese " +
                "número es el aviso previo: quien se queda sin códigos y pierde el teléfono ya no " +
                "puede salir del paso por su cuenta."),

            new Paso("usuarios-contrasena", "Restablecer la contraseña",
                "Genera una temporal, la enseña una vez y cierra la sesión que esa persona tuviera " +
                "abierta. No se guarda en ningún sitio en claro: si se pierde antes de dársela, hay " +
                "que volver a restablecerla."),

            new Paso("usuarios-reiniciar-2fa", "Reiniciar el segundo factor",
                "Es la salida para quien perdió el teléfono y ya no tiene códigos de rescate: la " +
                "cuenta queda como si nunca lo hubiera tenido y se le exigirá darlo de alta otra vez " +
                "al entrar. Como esto devuelve el acceso a una cuenta, comprueba antes por una vía " +
                "que reconozcas que quien te lo pide es esa persona. Queda anotado en la bitácora a " +
                "tu nombre."),

            new Paso("usuarios-eliminar", "Eliminar, y por qué casi nunca",
                "Borrar la cuenta deja sin atribución los registros del histórico que la apuntaban: " +
                "puntos asignados, revisiones, despliegues. Si solo quieres quitarle el acceso una " +
                "temporada, «Desactivar» hace eso y no toca nada más. Una cuenta de líder no se " +
                "puede eliminar.")),

        new Recorrido("/perfiles", "Perfil y desarrollo",

            Paso.Portada("Perfil y desarrollo",
                "La libreta del líder sobre cada desarrollador: fortalezas, áreas de mejora, " +
                "tecnologías que domina, expectativas de crecimiento y salario. Es material para " +
                "preparar una conversación de desarrollo o un aumento, no para el día a día."),

            new Paso("perfiles-aviso", "Confidencial de verdad",
                "El salario no viaja con la lista de la izquierda: se pide solo al abrir la ficha de " +
                "una persona y solo con cuenta de líder. En la bitácora queda que la ficha cambió, " +
                "quién y cuándo, pero nunca el importe."),

            new Paso("perfiles-lista", "Elegir a quién",
                "Salen únicamente los desarrolladores activos: la ficha de quien ya no está no se " +
                "borra, pero no estorba aquí. La palomita de la derecha dice quién tiene ya algo " +
                "capturado, así que la columna contesta de un vistazo a quién le falta."),

            new Paso("perfiles-ficha", "Lo que se captura",
                "Todo es texto libre y sin formato obligado; la idea es poder releerlo entero antes " +
                "de sentarse a hablar con la persona. Se guarda por completo cada vez, y abajo queda " +
                "la fecha de la última actualización para saber si lo que estás leyendo es de este " +
                "año o de hace tres."),

            new Paso("perfiles-salario", "El salario",
                "Un cero significa «sin registrar» y así se guarda: dejar un cero como importe haría " +
                "creer que a alguien se le paga cero. La moneda de al lado es texto libre a " +
                "propósito, porque una contratación puntual puede venir en una que nadie tenía " +
                "prevista.")),

        new Recorrido("/presencia", "Quién está y qué se marcó",

            Paso.Portada("Quién está y qué se marcó",
                "Tres vistas del mismo día: quién tiene la aplicación abierta ahora mismo, la " +
                "asistencia que cada quien declaró marcando entrada y salida, y las jornadas que la " +
                "aplicación registró por su cuenta. Es la pantalla del líder para saber con quién " +
                "cuenta hoy y para arreglar los marcajes que salieron mal."),

            new Paso("presencia-pestanas", "Tres cosas distintas",
                "Ninguna de las tres se deduce de las otras y por eso siguen separadas: «Asistencia» " +
                "es lo que la persona declara, «Registro de jornadas» es lo que la aplicación vio " +
                "sola, y la diferencia entre las dos es justo lo que delata un olvido. Lo que no hay, " +
                "a propósito, es un histórico de estados: un registro minutado de las pausas de " +
                "alguien es vigilancia, no asistencia."),

            new Paso("presencia-tablero", "Quién está ahora",
                "El punto de color es el mismo estado que cada quien elige en la barra de arriba. Las " +
                "filas apagadas son de gente desconectada, y a ésa el servidor le pone " +
                "«Desconectado» solo, cuando lleva un rato sin dar señales: «ausente» no lo elige " +
                "nadie a mano."),

            new Paso("presencia-actualizar", "No hace falta insistir",
                "El tablero se repinta solo cada treinta segundos, y además en cuanto alguien cambia " +
                "de estado. Este botón sirve para no esperar; los días pasados que estés mirando no " +
                "se refrescan solos porque no cambian por sí mismos."),

            new Paso("presencia-dia-olvidado", "Un día que nadie marcó",
                "Da de alta la asistencia de quien trabajó y se le pasó marcar. Se captura sobre el " +
                "día que tengas puesto arriba, y el motivo es obligatorio: una fila creada a mano y " +
                "sin explicación no se distingue de una inventada."),

            new Paso("presencia-corregir", "Corregir un marcaje",
                "Cambia la entrada y la salida de un registro que ya existe. Dejar la salida vacía " +
                "significa jornada abierta, que es lo que hay que poner cuando alguien no marcó su " +
                "salida en vez de inventarle una hora. El motivo es obligatorio, y el servidor " +
                "rechaza los horarios que se encimen con otro registro de la misma persona."),

            new Paso("presencia-excel", "Bajar el día",
                "Baja el día que está en pantalla, no el histórico entero, y con las filas de quien " +
                "NO marcó incluidas: esa ausencia suele ser justo el dato por el que se exporta.")),

        new Recorrido("/comunicados", "Comunicados al equipo",

            Paso.Portada("Comunicados al equipo",
                "Desde aquí se manda un aviso a la bandeja de los desarrolladores que elijas. No es " +
                "correo ni un tablón: aparece dentro de la aplicación, en la misma bandeja que ya " +
                "miran todos los días."),

            new Paso("comunicados-mensaje", "El texto del aviso",
                "Se entrega tal cual se escribe: no hay negritas, enlaces ni formato, y es a " +
                "propósito. Al enviarlo, el título y el mensaje se vacían para que el siguiente " +
                "comunicado no salga con restos del anterior."),

            new Paso("comunicados-destinatarios", "A quién le llega",
                "Salen todos los desarrolladores activos, incluidos los que todavía no tienen cuenta, " +
                "marcados como «no recibe». Se enseñan aposta: quien aparece así es alguien a quien " +
                "le falta el alta, y esconderlo lo dejaría fuera sin que nadie se enterara."),

            new Paso("comunicados-todos", "Marcar y desmarcar",
                "Marca o desmarca a todos de golpe, y solo se ve marcada cuando de verdad lo están " +
                "todos. Al abrir la pantalla vienen marcados únicamente los que tienen cuenta, " +
                "porque son los únicos a los que les puede llegar algo."),

            new Paso("comunicados-enviar", "Antes de mandarlo",
                "Pide confirmación diciendo a cuántas personas les va a llegar de verdad y cuántas de " +
                "las que marcaste se quedan fuera por no tener cuenta. Ése es el número con el que " +
                "conviene pensárselo: el aviso cae en la bandeja de cada quien y desde aquí no hay " +
                "forma de retirarlo."))
    ];

    // ── Catálogos ────────────────────────────────────────────────────────────────

    private static IReadOnlyList<Recorrido> Catalogos =>
    [
        new Recorrido("/desarrolladores", "El catálogo de desarrolladores",

            Paso.Portada("El catálogo de desarrolladores",
                "La ficha de cada persona del equipo: contacto, nivel, fecha de ingreso, equipo y " +
                "días de vacaciones. Desde aquí también se le crea su cuenta de acceso y se revisa o " +
                "corrige su saldo de vacaciones."),

            new Paso("desarrolladores-solo-activos", "Solo activos",
                "Viene marcado porque dar de baja a alguien no borra su ficha, y sin este filtro la " +
                "lista del día a día se llenaría de gente que ya no está. Al desmarcarlo aparecen " +
                "también las bajas, apagadas, para que se distingan de un vistazo."),

            new Paso("desarrolladores-nuevo", "Alta y edición de la ficha",
                "«Días de vacaciones» es el número que se imprime en el documento de RH, no el saldo " +
                "real. Dentro del formulario, «Calcular (LFT)» propone los días que corresponden por " +
                "antigüedad y los deja editables: es una sugerencia, no un cálculo que se imponga."),

            new Paso("desarrolladores-acceso", "Crear su cuenta",
                "Crea la cuenta de acceso con rol Desarrollador y enseña su contraseña temporal una " +
                "sola vez, aquí en la pantalla. Si la persona ya tiene cuenta, se dice y ahí acaba: " +
                "restablecerle la contraseña es otra decisión y se toma en «Usuarios»."),

            new Paso("desarrolladores-saldo", "El saldo de verdad",
                "El saldo no está guardado en ningún campo: se calcula desde la fecha de ingreso con " +
                "la tabla de la ley, descontando lo gozado, lo que caducó y lo que espera respuesta. " +
                "Aquí se ve ese desglose periodo por periodo y se captura el ajuste manual, que " +
                "exige motivo, reemplaza al ajuste anterior y queda en la bitácora con tu nombre."),

            new Paso("desarrolladores-desactivar", "Dar de baja",
                "No borra nada: la ficha y su historial se conservan y la fila se queda apagada. Es " +
                "así porque sus asignaciones, evaluaciones y apuntes de bitácora apuntan a esta " +
                "ficha, y borrarla dejaría huérfano el trabajo de alguien que sí estuvo aquí."),

            new Paso("desarrolladores-excel", "Bajar la lista",
                "Baja exactamente lo que el filtro está enseñando. Si acabas de buscar algo, la hoja " +
                "sale con esa búsqueda aplicada y no con el catálogo entero.")),

        new Recorrido("/equipos", "Los equipos y el organigrama",

            Paso.Portada("Los equipos y el organigrama",
                "Aquí se crean los equipos, se reparte a la gente entre ellos y se les asigna rol y " +
                "función. Cada movimiento queda registrado como una rotación, con su motivo, y de " +
                "todo esto sale el organigrama."),

            new Paso("equipos-pestanas", "Dos pestañas",
                "«Organización» son listas con botones; el «Organigrama» es lo mismo dibujado como " +
                "árbol, y también se edita: se arrastra a una persona hasta otra caja, o la cabecera " +
                "de un equipo sobre otro para colgarlo de él. No son dos formas de guardar sino dos " +
                "maneras de llegar a la misma: soltar a alguien pide el motivo y registra la rotación " +
                "igual que el botón. Todo se puede hacer también sin ratón desde «Organización»."),

            new Paso("equipos-nuevo", "Crear un equipo",
                "El color se escribe como «#RRGGBB» y no es solo de pantalla: es el que sale en el " +
                "organigrama y en el PDF. Si se deja vacío o mal escrito, el equipo se dibuja con el " +
                "color de la aplicación en los dos sitios. En «Cuelga de» se elige de qué equipo es " +
                "subequipo este; vacío es un equipo raíz. Eso agrupa el organigrama y suma la rama " +
                "aparte en el ranking, y no cambia lo que nadie puede hacer: eso lo decide el rol de " +
                "su cuenta, no el equipo."),

            new Paso("equipos-sin-equipo", "Quién no tiene equipo",
                "Esta lista con su contador es la pregunta que el tablero contesta de un vistazo. " +
                "Marca aquí a quien quieras colocar, elige el equipo en el desplegable de la derecha " +
                "y usa el botón de en medio."),

            new Paso("equipos-mover", "Mover al equipo",
                "Pide un motivo y guarda el movimiento como rotación, que es lo que después permite " +
                "explicar por qué alguien cambió. A quien se mueve se le quitan el rol y la función " +
                "que tenía y suelta el cargo de líder del equipo que deja: llega al nuevo sin nada " +
                "asignado."),

            new Paso("equipos-funcion", "Rol y función",
                "El rol dice de qué es la persona y lo comparte con otros; la función dice qué hace " +
                "ella en concreto y no lo comparte con nadie. Se guarda al salir de la caja y no en " +
                "cada tecla, y sale impresa en el organigrama debajo del nombre."),

            new Paso("equipos-historial", "Las rotaciones",
                "Abre y cierra el registro de quién se movió, de dónde a dónde, cuándo y con qué " +
                "motivo. Es lo único que queda de cada cambio: las tarjetas de abajo solo enseñan la " +
                "foto de hoy.")),

        new Recorrido("/contactos", "La agenda de contactos",

            Paso.Portada("La agenda de contactos",
                "Los contactos de fuera del equipo de desarrollo: quién es cada quien, en qué " +
                "empresa o área está y cómo se le escribe. Es una agenda y nada más depende de " +
                "ella, lo que cambia lo que se puede hacer aquí."),

            new Paso("contactos-nuevo", "Alta y edición",
                "Además del correo y el teléfono hay un campo para el enlace de la conversación de " +
                "Teams: pegarlo aquí es lo que enciende el botón de la columna «Teams», que abre esa " +
                "conversación sin tener que buscar a la persona."),

            new Paso("contactos-escribir", "Escribirle",
                "Abre el programa de correo de tu equipo con la dirección ya puesta. El mensaje sale " +
                "de tu cuenta y no de la aplicación, así que queda en tus enviados como cualquier " +
                "otro correo tuyo."),

            new Paso("contactos-eliminar", "Aquí sí se borra",
                "Éste es el único catálogo donde eliminar borra de verdad, y es correcto: un contacto " +
                "no está referenciado por asignaciones, evaluaciones ni bitácora, así que su baja no " +
                "deja historial huérfano. En desarrolladores, en cambio, la baja solo desactiva."),

            new Paso("contactos-excel", "Bajar la agenda",
                "Baja lo que el buscador está enseñando en ese momento, no la agenda completa. Es la " +
                "forma rápida de sacar los contactos de una empresa concreta.")),

        new Recorrido("/plantillas", "La biblioteca de plantillas",

            Paso.Portada("La biblioteca de plantillas",
                "Textos y scripts que el equipo reutiliza: correos tipo, consultas, guiones de " +
                "despliegue. Se buscan, se leen y se copian ya rellenados; en esta versión no se " +
                "crean ni se editan desde la web."),

            new Paso("plantillas-tipos", "Qué te toca ver",
                "La lista de tipos no es la misma para todo el mundo: solo se ofrecen los que tu " +
                "cuenta puede consultar, para que no elijas uno y te encuentres la lista vacía. Qué " +
                "plantillas ve cada rol lo decide el servidor, no esta pantalla."),

            new Paso("plantillas-archivadas", "Ver archivadas",
                "Archivar no borra: deja la plantilla fuera de la lista del día a día. Esta casilla " +
                "las devuelve a la vista, y en la columna del título salen marcadas con su icono."),

            new Paso("plantillas-lista", "La lista",
                "Junto al título, el clip dice que la plantilla trae un archivo adjunto y la caja que " +
                "está archivada. «Usos» cuenta cuántas veces se ha copiado, así que ordenar por esa " +
                "columna enseña cuáles se usan de verdad y cuáles nadie ha abierto nunca."),

            new Paso("plantillas-copiar", "Copiar ya rellenada",
                "Si la plantilla tiene huecos, te los pregunta uno por uno y deja en el portapapeles " +
                "el texto ya sustituido. Cancelar cualquiera de las preguntas cancela la copia " +
                "entera: no se copia un texto a medio rellenar."),

            new Paso("plantillas-guardar", "Bajarla como archivo",
                "Saca el contenido a un archivo en tu equipo, con la extensión que le toca. La " +
                "aplicación nunca ejecuta lo que guarda aquí: si es un script, lo entrega y tú " +
                "decides dónde y con qué credenciales se corre.")),

        new Recorrido("/programas", "Programas y licencias",

            Paso.Portada("Programas y licencias",
                "El inventario del software del área: qué programa es, en qué máquina está instalado, " +
                "qué tipo de licencia tiene y cuándo vence. Existe para que ninguna licencia caduque " +
                "sin que nadie se entere."),

            new Paso("programas-nuevo", "Alta y edición",
                "La fecha de vencimiento es la que enciende los avisos de la lista: un programa sin " +
                "fecha no va a avisar nunca de nada, así que conviene capturarla aunque sea " +
                "aproximada. La categoría y el estado son los que después dejan acotar el inventario."),

            new Paso("programas-rejilla", "Los colores de la lista",
                "La fila en rojo es una licencia que ya venció y la ámbar, una que vence dentro de " +
                "treinta días. Quién está en cada caso lo decide el servidor contra su propio reloj, " +
                "así que un equipo con la fecha mal puesta no tiñe de rojo licencias que están al día."),

            new Paso("programas-ver-clave", "Ver la clave",
                "Las claves no viajan con la lista: no están en la pantalla aunque no se vean. Este " +
                "botón pide una sola, la de la fila elegida, y deja anotado en la bitácora que tú la " +
                "consultaste. Por eso avisa antes: quien pulsó por curiosidad tiene que poder " +
                "echarse atrás sabiéndolo."),

            new Paso("programas-clave", "El campo de la clave",
                "El formulario se abre siempre con este campo en blanco, aunque el programa ya tenga " +
                "clave: la pantalla no la conoce. En blanco significa «déjala como está», así que " +
                "corregir la versión de un programa no se lleva su clave por delante. Para quitarla " +
                "de verdad hay que escribir un espacio y guardar.")),

        new Recorrido("/recursos-azure", "El inventario de Azure",

            Paso.Portada("El inventario de Azure",
                "Lo que el área tiene levantado en Azure, anotado a mano. La aplicación no consulta a " +
                "Azure ni presume de estar al día: dice lo que alguien capturó, y por eso se puede " +
                "corregir aquí mismo."),

            new Paso("recursos-filtros", "Filtrar, y el costo",
                "Los filtros acotan la lista y también el costo mensual que se suma arriba: si " +
                "filtras por producción, ese total es el de producción y no el de la suscripción " +
                "entera. Es la forma de contestar «cuánto nos cuesta este ambiente»."),

            new Paso("recursos-ambiente", "El ambiente",
                "El punto ordena por riesgo —producción, staging, compartido, desarrollo— y es lo que " +
                "dice de un vistazo qué recursos no se tocan. La palabra va siempre al lado, así que " +
                "la lista se sigue leyendo impresa o en blanco y negro."),

            new Paso("recursos-nuevo", "Dar de alta un recurso",
                "El costo mensual es una estimación capturada a mano —la que sirve para explicar la " +
                "factura—, no una lectura de Azure. Si pones la dirección del recurso en el portal, " +
                "su nombre en la lista se convierte en enlace."),

            new Paso("recursos-editar", "Corregir, porque no hay baja",
                "No existe eliminar, y no falta: un recurso que se dio por terminado se marca con el " +
                "estado que le toca y se queda, porque su costo pasado sigue explicando las facturas " +
                "de entonces. Editar es lo que se usa cuando algo cambia."))
    ];

    // ── Ausencias: el lado de cada quien ─────────────────────────────────────────

    private static IReadOnlyList<Recorrido> MisAusencias =>
    [
        new Recorrido("/mis-vacaciones", "Mis vacaciones",

            Paso.Portada("Mis vacaciones",
                "Desde aquí pides tus días, ves en qué quedó cada solicitud y bajas el documento que " +
                "se archiva en tu expediente. También es donde se firma la petición, si prefieres no " +
                "imprimirla para firmarla a mano."),

            new Paso("vacaciones-saldo", "Los días que tienes",
                "Es el saldo calculado desde tu fecha de ingreso: descuenta lo que ya gozaste, lo que " +
                "caducó y lo que tienes esperando respuesta. No te bloquea: puedes pedir más días de " +
                "los que salen aquí y decide el líder, porque hay acuerdos que el cálculo no conoce."),

            new Paso("vacaciones-nueva", "Pedir vacaciones",
                "Abre un asistente de cinco pasos: tu saldo con el desglose, las fechas —con los días " +
                "hábiles y el día en que te reincorporas—, quién más del equipo estará fuera esos " +
                "días, el comentario y el respaldo, y el resumen, que es donde se firma. Se puede ir " +
                "y volver entre pasos sin perder lo escrito."),

            new Paso("vacaciones-lista", "Tus solicitudes",
                "Cada fila es una petición tuya con su estado. Lo que se viene a leer aquí es la " +
                "respuesta del líder: si te rechazaron algo, ahí está el motivo, y cuando el motivo " +
                "falta se dice que falta en vez de dejar la celda en blanco."),

            new Paso("vacaciones-firma", "Tu firma",
                "Lo que firmas es TU petición —esas fechas y ese comentario—, no la respuesta del " +
                "jefe. Es opcional: sin ella el documento sale con la raya en blanco para firmarlo a " +
                "mano. Y si la solicitud cambia después, tu firma deja de valer y la columna lo dice " +
                "con todas las letras: hay que volver a trazarla."),

            new Paso("vacaciones-documentos", "El papel",
                "El PDF y el Word salen desde que pides los días, con las dos casillas en blanco: son " +
                "el borrador que se lleva a firmar. «Firmado» solo aparece cuando el líder ya archivó " +
                "el definitivo, que es el que lleva las dos firmas."),

            new Paso("vacaciones-acciones", "Cancelar o eliminar",
                "Cancelar algo ya aprobado avisa de que no vas a tomar esos días y deja de contarlos " +
                "como gozados; cancelar algo pendiente solo retira la petición. Eliminar se lleva por " +
                "delante los documentos que se hubieran generado y no se puede deshacer. Una " +
                "solicitud resuelta no ofrece nada: ya es historial.")),

        new Recorrido("/mis-permisos", "Mis permisos",

            Paso.Portada("Mis permisos",
                "Aquí pides permisos —una cita médica, un trámite, un asunto personal—, ves en qué " +
                "quedaron y cuelgas el justificante. También aparecen los que el líder registró por " +
                "su cuenta, marcados como suyos."),

            new Paso("permisos-indicadores", "Cómo vas",
                "Lo que sigue esperando respuesta, cuántos permisos te aprobaron este año y cuánto " +
                "suman. Los días y las horas se cuentan en tarjetas distintas y no se mezclan: la de " +
                "días suma solo los permisos de día completo y la de horas, los tramos, porque un " +
                "rato de una mañana no son «medio día» para todo el mundo. No hay saldo que gastar " +
                "como en vacaciones: esto es un recuento, no un límite."),

            new Paso("permisos-solicitar", "Pedir un permiso",
                "Son dos pasos y no cinco a propósito, porque un permiso se pide con prisa: en el " +
                "primero van tipo, fecha, duración y motivo —con las notas y el justificante plegados " +
                "abajo, que casi nunca hacen falta— y el segundo es el resumen. La duración se elige: " +
                "días completos, como siempre, o un tramo de horas de un solo día —«de 9:00 a 11:00»—, " +
                "que es lo que de verdad se pide cuando uno va al dentista y vuelve. El motivo es " +
                "obligatorio: es lo único que el líder va a leer para decidir."),

            new Paso("permisos-lista", "Tus solicitudes",
                "Cada fila es un permiso tuyo. La columna de la respuesta del líder es la que se " +
                "viene a leer: en un rechazo sin motivo se dice que falta, en vez de dejarte con una " +
                "negativa y nada que hacer con ella. Lo que lleva la insignia «Del líder» lo " +
                "registró él y no es una solicitud tuya."),

            new Paso("permisos-acciones", "Lo que puedes hacer",
                "El justificante solo se cuelga o se cambia mientras la solicitud siga pendiente: " +
                "después ya es la respuesta del líder sobre lo que había. Cancelar retira lo " +
                "pendiente o avisa de que no tomarás lo aprobado, y eliminar no se deshace. Los " +
                "botones que salen son los que el servidor permite, así que lo que no está es " +
                "que no se puede."))
    ];

    // ── Ausencias: el lado del líder ─────────────────────────────────────────────

    private static IReadOnlyList<Recorrido> AusenciasDelLider =>
    [
        new Recorrido("/vacaciones", "Las vacaciones del equipo",

            Paso.Portada("Las vacaciones del equipo",
                "Aquí se resuelven las solicitudes de vacaciones y se emite el documento que se " +
                "archiva en el expediente de cada quien. Ese papel lleva dos firmas: la tuya, que se " +
                "elige de las que tengas guardadas, y la que la persona puso al pedir sus días."),

            new Paso("vacaciones-lider-pendientes", "Lo que espera respuesta",
                "Este contador se cuenta sobre todas las solicitudes y no sobre lo que el filtro esté " +
                "enseñando: es el número que dice si queda trabajo, y esconderlo al filtrar haría " +
                "creer que no hay nada que resolver."),

            new Paso("vacaciones-lider-pestanas", "Solicitudes y firmas",
                "La segunda pestaña guarda tus firmas: se trazan una vez —con el dedo, el lápiz o el " +
                "ratón— y se reutilizan en cada documento. Está aquí y no solo en Administración " +
                "porque uno descubre que le falta justo cuando va a firmar."),

            new Paso("vacaciones-lider-excel", "Bajar lo filtrado",
                "Baja lo que los dos desplegables de al lado están enseñando y no el histórico " +
                "entero. Por eso el botón va pegado a ellos: quien acaba de acotar por persona o por " +
                "estado espera esa hoja y no otra."),

            new Paso("vacaciones-lider-resolver", "Aprobar, rechazar, cancelar",
                "Rechazar exige motivo, que es lo único que la persona va a leer. Cancelar no es lo " +
                "mismo que rechazar: alcanza también a lo que ya estaba aprobado, y avisa de ello, " +
                "porque esos días la persona ya los tenía apartados y probablemente planeados."),

            new Paso("vacaciones-lider-su-firma", "La firma de quien pidió",
                "Son tres situaciones y no dos: firmada, sin firmar, y «dejó de valer» —firmó, pero " +
                "la solicitud cambió después, así que lo que firmó ya no es lo que dice el papel—. " +
                "La tercera es la única que obliga a hacer algo: mientras siga así, el documento " +
                "definitivo no se archiva por el camino normal."),

            new Paso("vacaciones-lider-documento", "El documento",
                "Abre el panel donde se elige tu firma, se ve el borrador, se baja en Word sobre la " +
                "plantilla de RH y se archiva el definitivo. Si la firma de la persona dejó de valer, " +
                "desde ahí hay dos salidas: pedirle que vuelva a firmar —vale aunque la solicitud ya " +
                "esté resuelta— o archivarlo reconociéndolo, con un motivo que sale impreso en el " +
                "papel y queda en la bitácora.")),

        new Recorrido("/permisos", "Los permisos del equipo",

            Paso.Portada("Los permisos del equipo",
                "Aquí se resuelven los permisos que pide el equipo y se consulta el histórico. " +
                "También es donde se registran los que se acordaron fuera de la aplicación, para que " +
                "no se queden sin constancia."),

            new Paso("permisos-lider-estado", "Arranca en pendientes",
                "La pantalla se abre filtrada por «Pendiente» a propósito: es a lo que se viene. Para " +
                "ver el histórico hay que limpiar este filtro. El contador de arriba, en cambio, " +
                "cuenta siempre sobre todos y no sobre lo filtrado, porque es el que dice si queda " +
                "trabajo."),

            new Paso("permisos-lider-registrar", "Registrar lo acordado fuera",
                "Para el permiso que se dio por teléfono o en el pasillo. La fila nace ya aprobada " +
                "porque registrarla es concederla: no queda nada que resolver después. En la lista " +
                "se distingue por la columna «Origen»."),

            new Paso("permisos-lider-rechazar", "Aprobar y rechazar",
                "Al aprobar, el comentario es opcional. Al rechazar es obligatorio, y no por trámite: " +
                "es lo único que la persona va a leer para entender la negativa, y un rechazo sin " +
                "explicación es lo que hace que la gente deje de pedir las cosas por aquí."),

            new Paso("permisos-lider-corregir", "Corregir un pendiente",
                "Arregla los datos capturados —tipo, fecha, duración, motivo y notas— sin tocar nada " +
                "más: el justificante que subió quien lo pidió sigue adjunto tal cual, la solicitud " +
                "no cambia de dueño y sigue esperando tu respuesta. La duración se corrige entera, " +
                "días completos o tramo de horas, y por eso este panel lleva el mismo desplegable que " +
                "el de registrar: un permiso capturado por error como día entero no se podría " +
                "enmendar sin borrarlo. Solo se puede sobre lo que todavía está pendiente."),

            new Paso("permisos-lider-eliminar", "Eliminar",
                "Borra el permiso del historial y no se puede deshacer. No es lo mismo que rechazar: " +
                "rechazar deja constancia de que se pidió y de por qué se dijo que no, que suele ser " +
                "justo lo que hace falta después.")),

        new Recorrido("/actividades", "Las actividades libres del equipo",

            Paso.Portada("Las actividades libres del equipo",
                "El trabajo que los desarrolladores registran fuera de sus requerimientos asignados. " +
                "Contesta a «¿en qué se fue el tiempo que no aparece en ningún requerimiento?», y es " +
                "solo de lectura: lo que hay aquí es de su dueño."),

            new Paso("actividades-indicadores", "Los tres números",
                "Cuántas actividades hay con el filtro puesto, cuántas siguen abiertas y cuánto " +
                "tiempo suman entre todas. Ese tiempo total es el que no está imputado a ningún " +
                "requerimiento, que es la cifra por la que se abre esta pantalla."),

            new Paso("actividades-rejilla", "La lista",
                "Cada fila es una actividad con su tiempo cronometrado y su descripción entera, que " +
                "se enseña sin abrir nada porque es lo que explica de qué va. La columna «Tiempo» " +
                "ordena por segundos y no por el texto: ordenando el texto, «59m» quedaría por " +
                "encima de «2h»."),

            new Paso("actividades-detalle", "La ficha completa",
                "Abre, en solo lectura, las sesiones de cronómetro una por una —con su inicio, su fin " +
                "y su duración— y la evidencia que la persona adjuntó. Los archivos los enseña el " +
                "navegador y no dejan copias en tu equipo."),

            new Paso("actividades-excel", "Bajar lo que se ve",
                "Baja lo que los filtros están enseñando, no el histórico completo. La hoja añade los " +
                "segundos en crudo, que es lo que permite sumarlos y ordenarlos bien fuera de aquí.")),

        new Recorrido("/sugerencias", "Las sugerencias del equipo",

            Paso.Portada("Las sugerencias del equipo",
                "Lo que el equipo propone, con su estado y con tu respuesta. Contestar cada una es lo " +
                "que hace que la gente siga proponiendo, así que la pantalla está montada alrededor " +
                "de eso."),

            new Paso("sugerencias-visibilidad", "Quién la ve",
                "Cada sugerencia se manda con un alcance: unas las ve todo el equipo y otras solo tú. " +
                "Las que son solo para ti salen resaltadas en la lista, porque si no las atiendes no " +
                "las va a atender nadie más."),

            new Paso("sugerencias-votos", "Más votadas primero",
                "Es un interruptor y no el orden normal: por omisión se atienden por antigüedad, para " +
                "que ninguna se quede enterrada. El apoyo del equipo es una segunda lectura, útil " +
                "para decidir por dónde empezar cuando hay muchas."),

            new Paso("sugerencias-autor", "Quién la mandó",
                "Las anónimas no traen nombre y no hay forma de averiguarlo desde aquí: el servidor " +
                "no lo manda, así que no es que la pantalla lo esconda. «Sin ficha ligada» es alguien " +
                "con cuenta pero sin ficha de desarrollador."),

            new Paso("sugerencias-atender", "Atender y responder",
                "Abre la ficha con el texto completo, propone el estado siguiente y deja escribir la " +
                "respuesta. El estado y la respuesta se guardan en un solo acto a propósito: un " +
                "«Rechazada» sin explicación es justo lo que hace que la gente deje de proponer."),

            new Paso("sugerencias-eliminar", "Eliminar",
                "Borra la sugerencia y no se puede deshacer. Si lo que quieres es cerrarla, cámbiale " +
                "el estado y contéstala: así queda constancia de que se propuso y de qué se decidió."))
    ];

    // ── Jornada ──────────────────────────────────────────────────────────────────

    private static IReadOnlyList<Recorrido> Jornada =>
    [
        new Recorrido("/mi-jornada", "Mi jornada",

            Paso.Portada("Mi jornada",
                "Marcar tu entrada y tu salida del día, ver el cronómetro que tengas corriendo y " +
                "repasar lo que llevas registrado. La jornada cuelga de tu cuenta, así que se marca " +
                "igual desde cualquier equipo."),

            new Paso("jornada-marcaje", "Entrada y salida",
                "Marcar entrada no pide confirmación y marcar salida sí, y no es un descuido: entrar " +
                "de más se arregla saliendo, mientras que salir de más cierra tu jornada del día y " +
                "eso ya solo lo corrige el líder. La nota de cada marcaje es opcional y el marcaje " +
                "se guarda la escribas o no."),

            new Paso("jornada-cronometro", "El cronómetro",
                "Aparece cuando tienes tiempo corriendo sobre un requerimiento o una actividad, y se " +
                "puede parar desde aquí sin volver a esa pantalla. Cuenta desde la hora del servidor " +
                "y no la de tu equipo: un portátil con el reloj desajustado enseñaría tiempo que " +
                "nadie trabajó. Sin conexión deja de mandar señales, y el tiempo se consolida hasta " +
                "la última que llegó."),

            new Paso("jornada-rango", "Qué periodo se ve",
                "El historial no son treinta días fijos: los atajos cubren hoy, la semana, el mes y " +
                "los últimos treinta días, y las dos fechas de la derecha sirven para cualquier otro " +
                "periodo. La semana empieza en lunes, que es el calendario del equipo y no el que " +
                "trae el sistema."),

            new Paso("jornada-app-abierta", "Lo que vio la aplicación",
                "Al lado de lo que marcaste está lo que la aplicación registró por su cuenta: la " +
                "primera señal, la última y cuánto estuvo abierta. Una diferencia grande no es una " +
                "falta —se puede trabajar sin la web abierta—, pero es lo que conviene mirar antes " +
                "de que te lo pregunten. «Se cerró sola» quiere decir que esa hora de salida es la " +
                "última señal, no una salida real."),

            new Paso("jornada-correccion", "Pedir corrección",
                "Nadie edita sus propias horas: se pide el cambio con un motivo y lo resuelve el " +
                "líder, y la fila se queda marcada como «Corrección pedida» hasta que lo atienda. Es " +
                "así porque un registro que su dueño puede reescribir no prueba nada."))
    ];
}
