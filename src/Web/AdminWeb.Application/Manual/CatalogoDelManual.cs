namespace AdminWeb.Application.Manual;

/// <summary>
/// Los artículos de ÁREA del manual: uno por asunto, no uno por pantalla.
///
/// <para><b>Por qué por área.</b> Hay más de sesenta pantallas y un artículo por cada una serían
/// sesenta artículos que nadie lee, porque quien llega nuevo no tiene una duda de pantalla: tiene
/// una duda de trámite —«cómo pido vacaciones», «qué pasa si pierdo el teléfono»—, y la respuesta
/// casi siempre cruza dos o tres pantallas. Lo que explica una pantalla control por control es el
/// recorrido guiado que lanza el botón de ayuda; lo que explica el trámite entero es esto.</para>
///
/// <para><b>Para quién está escrito.</b> Para quien entra al equipo un martes por la mañana y no
/// sabe nada: ni qué es el pool, ni qué significa un plazo, ni por qué le piden marcar entrada. No
/// para quien construyó la aplicación. De ahí que se expliquen cosas que a un veterano le parecerán
/// obvias, y de ahí que casi ningún párrafo dé por sabida una palabra sin definirla antes o sin
/// mandar al glosario.</para>
///
/// <para><b>Cada párrafo y cada punto de una lista van en UNA SOLA LÍNEA</b>, por larga que quede.
/// No es descuido de formato, es la única forma correcta: el cuerpo de un artículo se pinta con
/// <c>white-space: pre-wrap</c> —los saltos que escribe su autor se respetan—, así que un párrafo
/// partido a mano saldría cortado exactamente por donde lo partió el editor de código, y un punto de
/// lista partido en dos líneas cerraría la lista y convertiría su continuación en un párrafo suelto.
/// Ver <c>ConocimientoTexto.Analizar</c> y <c>CuerpoDeConocimiento.razor</c>.</para>
///
/// <para>El resto del marcado es el mínimo que entiende la base: <c>#</c> para títulos, <c>-</c>
/// para listas, <c>**negrita**</c> y acentos graves para código. Las rutas de pantalla van entre
/// acentos graves para que salgan en monoespaciado y no se confundan con el texto.</para>
/// </summary>
internal static class CatalogoDelManual
{
    internal static readonly ArticuloDelManual[] Areas =
    [
        new("primeros-pasos",
            "Primeros pasos: qué es esta plataforma y cómo moverte por ella",
            "manual, primeros pasos, roles, menu",
            """
            Esta plataforma es donde el equipo de desarrollo lleva su trabajo diario: lo que hay que hacer, quién lo está haciendo, cuánto tiempo llevó, quién falta hoy, qué se liberó ayer y lo que el equipo va aprendiendo por el camino. Si acabas de entrar, empieza por aquí y sigue con el artículo del área que te toque.

            # Lo primero, el día uno

            La primera vez que entras, la plataforma te obliga a hacer dos cosas antes de dejarte ver nada: cambiar la contraseña con la que te dieron de alta y activar el segundo factor, el código de seis dígitos del teléfono. No se puede posponer ni saltar. Los dos trámites tienen su propio artículo en este manual: **Entrar a la plataforma** y **El segundo factor**.

            # Tu rol decide lo que ves

            Hay tres roles, y el menú de la izquierda cambia entero según cuál tengas. Que una pantalla no te aparezca no es que esté escondida: es que no te corresponde, y el servidor la niega aunque escribas la dirección a mano.

            - **Desarrollador**. Su trabajo (`Mi Panel`, `Mis Asignaciones`, `Mis tickets DevOps`, `Mis Actividades`), su tiempo (`Mi jornada`), sus ausencias (`Mis Vacaciones`, `Mis Permisos`), el pool de actividades y sus evaluaciones. Además, el foro y esta base de conocimiento.
            - **Líder** (en la aplicación aparece como administrador). Lo mismo pero del equipo entero, más despliegues, integraciones, usuarios, configuración y bitácora. Es quien aprueba: vacaciones, permisos, puntos, actividades del pool y artículos de esta base.
            - **Operaciones**. Despliegues, despliegues programados y estado de servidores. La lista es corta a propósito: su alcance son las liberaciones. No registra jornada y no entra al foro; sí lee esta base de conocimiento, porque aquí es donde está escrito cómo se despliega cada cosa.

            # Cómo está organizado el menú

            Arriba del todo, fuera de los grupos, va lo que se abre todos los días: **Avisos** y **Conocimiento** (esta base), que los ve cualquier rol, más **Foro** y **Dashboard** para quien los tenga. Debajo, los grupos que te correspondan por rol: «Lo mío» y «Herramientas» si eres desarrollador; «Equipo», «Trabajo», «Despliegue e Infraestructura», «Integraciones y Correo» y «Administración» si eres líder; «Despliegue» si eres de operaciones. Y al final del todo, para los tres roles, **«Mi cuenta»**: lo tuyo, no lo de tu trabajo. Ahí está `Mi acceso`, que es donde se administra tu segundo factor.

            Cuando una entrada del menú lleva un número al lado, ese número es algo que te está esperando ahí dentro. Pasa el ratón por encima y el globo te dice qué es.

            # El botón de ayuda de cada pantalla

            Arriba a la derecha, junto al interruptor de tema claro y oscuro, hay un botón con un signo de interrogación. Lanza un **recorrido guiado de la pantalla en la que estés**: va señalando los controles uno por uno y explicando para qué sirve cada cual. Cambiar de pantalla cambia el recorrido sin que tengas que hacer nada.

            Si el botón se ve apagado es que esa pantalla todavía no tiene recorrido. No desaparece, para que no tengas que averiguar si la ayuda existe o no.

            # Dónde preguntar cuando algo no cuadra

            - **Cómo funciona algo de la plataforma**: búscalo aquí, en `/conocimiento`. Este manual son los artículos etiquetados `manual`, y las palabras sueltas están en los de `glosario`.
            - **Algo del trabajo**: el foro (`/foro`), que es la conversación del equipo.
            - **Un permiso que no tienes, un dato mal puesto, una cuenta bloqueada**: tu líder.

            Y si encuentras algo mal explicado en este manual, corrígelo. Estos artículos se editan como cualquier otro y nadie los va a devolver a su versión original: el manual es del equipo, no del programa.
            """),

        new("acceso",
            "Entrar a la plataforma: contraseña, sesión y bloqueos",
            "manual, acceso, contrasena, sesion",
            """
            Todo empieza en `/acceso`, con tu usuario y tu contraseña. Este artículo cubre esa parte; el código de seis dígitos que se pide justo después tiene el suyo.

            # La primera entrada

            Tu líder da de alta la cuenta y te entrega una contraseña temporal. En cuanto entras con ella, la plataforma te lleva a `/cambiar-contrasena` y no te deja ir a ningún otro sitio hasta que la cambies. Después te pide activar el segundo factor. Los dos pasos son obligatorios y solo ocurren una vez.

            La contraseña nueva debe tener **al menos 8 caracteres**. Nadie más la conoce: no se guarda en ninguna parte de la que se pueda leer, así que si se te olvida no hay quien te la diga. Hay que reiniciarla, y eso lo hace tu líder desde `/usuarios`.

            # Si te equivocas al teclearla

            A los **5 intentos fallidos** la cuenta se bloquea durante **15 minutos**. El contador se pone a cero en cuanto entras bien. Si te urge y no quieres esperar, tu líder puede desbloquearla desde `/usuarios`.

            El bloqueo no distingue quién está tecleando: existe para que nadie pueda ir probando contraseñas a mano contra tu cuenta. Si te bloqueas sin haber intentado entrar, dilo; no lo dejes pasar.

            # Tu sesión

            La sesión dura mientras la uses. Si cierras el navegador y vuelves, es normal que te pida otra vez usuario y contraseña; el código del teléfono, en cambio, puede que no te lo pida, si marcaste ese equipo como de confianza.

            El botón **Salir** está arriba a la derecha, junto a tu nombre. Úsalo si compartes el equipo con alguien.

            # Qué queda registrado

            Cada entrada, cada intento fallido y cada cambio de contraseña quedan anotados en la bitácora (`/bitacora`, que solo ve el líder) con la fecha, la dirección desde la que se hizo y el navegador. No es vigilancia del trabajo: es lo que permite responder «¿quién entró a esta cuenta el martes?» el día que haya que preguntarlo.

            # Tu cuenta y tu ficha no son lo mismo

            La **cuenta** es con lo que entras. La **ficha de desarrollador** es tu expediente de trabajo: antigüedad, puesto, días de vacaciones, puntos. A la ficha es a lo que se enganchan las vacaciones, el desempeño y el pool.

            Casi siempre van unidas y no tienes que hacer nada. Si alguna pantalla te dice que no tienes ficha, avísale a tu líder: sin ella no se pueden pedir vacaciones ni ganar puntos.
            """),

        new("segundo-factor",
            "El segundo factor y qué hacer si pierdes el teléfono",
            "manual, acceso, segundo factor, codigos de rescate",
            """
            El segundo factor es el código de seis dígitos que la plataforma te pide después de la contraseña. Es obligatorio para todas las cuentas, sin excepción, y se administra desde `Mi acceso` (`/segundo-factor`), en el grupo «Mi cuenta» que el menú deja al final para los tres roles.

            # Por qué se pide

            Una contraseña se adivina, se reutiliza en otro sitio que la filtra o se ve por encima del hombro. El código cambia cada treinta segundos y solo lo produce tu teléfono, así que saber tu contraseña ya no alcanza para entrar con tu nombre. Y con tu nombre se aprueban vacaciones, se anotan puntos y se despliega a producción.

            # Cómo se da de alta

            Necesitas una aplicación de autenticación en el teléfono; sirve cualquiera que lea códigos de tiempo. El orden es éste y no se puede alterar:

            1. La plataforma te enseña un código QR y, debajo, el mismo secreto en letras por si no puedes escanear.
            2. Lo agregas en la aplicación del teléfono, que empieza a mostrarte códigos de seis dígitos.
            3. **Tecleas uno de esos códigos para confirmar.** Hasta que uno no coincide, el segundo factor no queda activado.

            Ese tercer paso es el que evita el problema clásico: si el escaneo salió mal, la aplicación del teléfono no avisa de nada, sigue enseñando códigos de seis dígitos que no son los buenos, y te enterarías al día siguiente cuando ya no puedas entrar. Aquí no puede pasar, porque hasta que no teclees un código bueno no se da nada por hecho.

            Si recargas la pantalla a medias, el secreto anterior se descarta y se empieza otra vez con uno nuevo. Es normal.

            # Los códigos de rescate

            Al terminar el alta se te entregan **8 códigos de rescate**, y se enseñan una sola vez. Guárdalos donde no estén junto al teléfono: en el gestor de contraseñas, o impresos en un cajón.

            Cada código sirve **una sola vez**: en cuanto lo usas, se quema. Cuando te queden dos o menos, la plataforma te lo empieza a decir al entrar. Puedes emitir ocho nuevos cuando quieras desde `Mi acceso`; al hacerlo, **los anteriores dejan de valer todos**.

            # Perdí el teléfono, se rompió o lo cambié

            Con calma, en este orden:

            1. **Si tienes códigos de rescate**, entra con uno en lugar del código de seis dígitos. Ya dentro, ve a `Mi acceso`, reinicia el segundo factor y vuelve a darlo de alta en el teléfono nuevo. Se acabó.
            2. **Si no los tienes**, no puedes entrar tú solo: pídeselo a tu líder, que reinicia el segundo factor de tu cuenta desde `/usuarios`. La próxima vez que entres, la plataforma te pedirá darlo de alta otra vez, como el primer día.

            Reiniciar el segundo factor invalida el secreto viejo y los códigos de rescate viejos. Un teléfono perdido deja de servir para entrar en cuanto se hace.

            # «No volver a pedirme el código en este equipo»

            Al teclear el código puedes marcar esa casilla, y entonces la plataforma deja de pedírtelo **en ese navegador durante 30 días**. Es por navegador y por equipo: marcarlo en tu portátil no afecta al teléfono ni al equipo de al lado.

            No lo marques en un equipo compartido ni en uno prestado. Y si lo hiciste y te arrepientes, en `Mi acceso` tienes la lista de equipos recordados y el botón para dejar de confiar en ellos, de uno o de todos a la vez, que es lo que hay que hacer el día que te roben el portátil.

            # El código no me lo acepta

            Casi siempre es la hora del teléfono. Estos códigos se calculan con el reloj: si el teléfono va desfasado más de medio minuto, ninguno va a coincidir. Ponlo en hora automática y vuelve a intentarlo.
            """),

        new("vacaciones",
            "Cómo se piden las vacaciones, quién las firma y cómo se cuentan los días",
            "manual, vacaciones, ausencias, firma",
            """
            Las vacaciones se piden desde `Mis Vacaciones` (`/mis-vacaciones`) y el líder las resuelve desde `/vacaciones`. Todo el trámite —la petición, la respuesta y el papel firmado— ocurre dentro de la plataforma, para que dentro de un año se pueda contestar «¿qué pedí y qué me contestaron?» sin buscar en un chat.

            # Cuántos días tengo

            En `Mis Vacaciones` verás tu **saldo**: los días que has generado, los que ya tomaste y los que te quedan. No es un número que alguien teclee; se calcula solo a partir de tu fecha de ingreso.

            Lo que corresponde por año de antigüedad sale de la tabla de la Ley Federal del Trabajo: 12 días al cumplir el primer año, 14 al segundo, 16 al tercero, 18 al cuarto, 20 al quinto, 22 del sexto al décimo, y dos días más por cada cinco años a partir de ahí.

            Dos cosas que sorprenden y conviene saber de entrada:

            - **El primer año no genera días hasta que se cumple.** Los 12 días aparecen el día de tu primer aniversario, no proporcionalmente antes.
            - **Lo que no gozas se arrastra, pero caduca.** Por omisión, los días de un periodo caducan a los **18 meses**; ese plazo lo configura la empresa y puede ser otro. El saldo te enseña qué está a punto de caducar, precisamente para que no lo pierdas por no mirarlo.

            # Cómo se pide

            1. En `Mis Vacaciones` das de alta la solicitud con la fecha de inicio, la de fin y el motivo si hace falta.
            2. La solicitud nace **Pendiente**. Nadie se autoriza a sí mismo.
            3. Mientras siga pendiente puedes corregirla, colgarle un respaldo (un correo, una autorización previa) o cancelarla.
            4. **Fírmala.** Tu firma es un trazo que dibujas con el ratón o con el dedo, y se guarda en `/firmas` para poder reutilizarla. Lo que firmas es tu petición, no la respuesta.

            # Quién firma

            Firman dos personas, y firman cosas distintas.

            - **Tú**, el colaborador, firmas que estás pidiendo esos días. Se firma mientras la solicitud está pendiente.
            - **Tu jefe** firma la autorización. Esa firma la prepara el líder en `/firmas` y se aplica al documento cuando resuelve la solicitud.

            Con las dos firmas y la solicitud resuelta, el líder emite el **documento definitivo**, que es el papel que se archiva. Antes de eso se puede generar un borrador sin firmar para revisarlo.

            Si en algún momento tu firma deja de valer —por ejemplo, porque se borró del gestor de firmas— el documento no se puede archivar y la plataforma te pedirá que vuelvas a firmar. Puedes hacerlo aunque la solicitud ya esté resuelta: lo que firmas de nuevo es la misma petición tal como está hoy.

            # Los cuatro estados

            - **Pendiente**: esperando respuesta del líder. Es el único estado en el que puedes corregir, adjuntar o borrar.
            - **Aprobada**: autorizada. Los días descuentan de tu saldo.
            - **Rechazada**: el líder la negó, y el motivo queda escrito.
            - **Cancelada**: la retiraste tú, o estaba aprobada y al final no la vas a tomar.

            Una solicitud aprobada o rechazada **no se borra**: es historial. Lo que se hace es cancelarla, y queda dicho.

            # Antes de pedir

            Mira `/presencia` y las ausencias ya aprobadas del equipo. Si tres personas van a estar fuera esa semana, lo más probable es que te la nieguen, y es mejor descubrirlo antes de comprar el vuelo.
            """),

        new("permisos",
            "Permisos y ausencias cortas: cuándo se pide un permiso y no vacaciones",
            "manual, permisos, ausencias, incapacidad, horas",
            """
            Un **permiso** es una ausencia corta y con motivo: una cita médica, un asunto familiar, una incapacidad, un curso. Se pide desde `Mis Permisos` (`/mis-permisos`) y el líder los resuelve en `/permisos`.

            # Permiso o vacaciones

            La diferencia no es la duración, es de dónde salen los días.

            - Las **vacaciones** consumen tu saldo generado por antigüedad. Se piden con tiempo y llevan documento firmado.
            - Un **permiso** no toca ese saldo. Se pide por un motivo concreto y a veces se justifica con un papel.

            Si dudas, mira el motivo: si tienes uno que explicar, es un permiso.

            # Los tipos

            Al pedirlo eliges el tipo, que es lo que le dice al líder de qué se trata: permiso personal, incapacidad, cita médica, asunto familiar, capacitación u otro. El tipo no cambia las reglas del trámite; sirve para entenderlo de un vistazo y para las cuentas de fin de año.

            # Días completos o por horas

            La **duración** se elige al pedirlo, y son dos formas distintas de ausentarse:

            - **Días completos**, como siempre: uno o varios días seguidos.
            - **Por horas**: un tramo dentro de un solo día —«de 9:00 a 11:00»—, que es lo que de verdad se pide cuando uno va al dentista y vuelve a trabajar. El tramo no puede pasar de la jornada ni pisar otro permiso tuyo que siga vivo ese día; si necesitas el día entero, pídelo como días completos.

            En el recuento del año los dos no se mezclan: hay una tarjeta de días y otra de horas. Un rato de una mañana no son «medio día» para todo el mundo, así que no se convierte nada.

            # Cómo se pide

            1. En `Mis Permisos` das de alta la solicitud: tipo, fecha, duración y motivo. **El motivo es obligatorio**, porque es lo único que le permite al líder resolver sin tener que preguntarte.
            2. Si tienes justificante —la constancia médica, el correo del curso— cuélgalo. Se admite hasta 15 MB, y solo mientras la solicitud siga pendiente: una vez resuelta, lo que el líder aprobó fue el justificante que tenía delante, y cambiarlo por debajo dejaría su decisión hablando de otro documento.
            3. Nace **Pendiente**, y ahí espera respuesta.

            Los estados son los mismos que en vacaciones —Pendiente, Aprobada, Rechazada, Cancelada— y se comportan igual, a propósito: pedir un permiso y pedir vacaciones son el mismo trámite para quien lo pide.

            # Cuando el permiso ya se dio fuera de la aplicación

            Pasa: te lo autorizaron en el pasillo o por teléfono. Entonces el líder lo captura ya concedido, para que quede el registro. No lo des de alta tú como si lo estuvieras pidiendo: el historial tiene que reflejar lo que ocurrió de verdad.

            # Por qué importa registrarlo

            Vacaciones y permisos aprobados alimentan la misma vista de ausencias que usa el líder para repartir el trabajo y armar el sprint. Si no está en la plataforma, quien reparte no lo sabe, por mucho que ya lo hayas hablado.
            """),

        new("jornada",
            "Cómo se registra la jornada y qué es el estado de presencia",
            "manual, jornada, asistencia, presencia",
            """
            Tu jornada se registra en `Mi jornada` (`/mi-jornada`). Son dos cosas distintas que conviene no mezclar: lo que **tú declaras** y lo que la **aplicación observa**.

            # Lo que tú declaras: entrada y salida

            Al empezar el día marcas **entrada**, y al terminar marcas **salida**. Eso es el registro oficial de asistencia y lo pones tú, nadie más; se marca con un botón desde `Mi jornada`.

            El líder y los desarrolladores registran jornada. El área de operaciones no, porque su alcance son los despliegues.

            # Lo que la aplicación observa: la telemetría

            Mientras tienes la plataforma abierta, ésta anota que hay actividad. Ese rastro **no es tu asistencia** y no la sustituye: una aplicación abierta en un equipo encendido no prueba que alguien esté trabajando, y el trabajo hecho sin abrirla tampoco deja de contar.

            Lo que se hace con ese rastro es **contrastarlo** con lo que marcaste. De ahí salen dos señales que el líder ve en su tablero:

            - **Sin marcar**: hubo actividad tuya pero no marcaste nada. Casi siempre es un olvido.
            - **Discrepancia**: marcaste una hora y la actividad empezó o terminó bastante lejos de ella.

            Ninguna de las dos es una acusación: son las dos formas en que un olvido se hace visible el mismo día, en vez de a fin de mes cuando ya nadie se acuerda de qué pasó.

            # Me equivoqué al marcar

            Desde `Mi jornada` puedes **pedir una corrección** del día explicando qué ocurrió: «marqué salida a las 14:00 por error», «olvidé marcar entrada, llegué a las 8:30». La petición le llega al líder, que ajusta el registro.

            Tú no reescribes tus propias horas, y es a propósito: un registro que su dueño puede cambiar sin dejar rastro no sirve como registro.

            # El estado de presencia

            Aparte de la jornada está tu **estado**, que es lo que el equipo ve en `/presencia` y que eliges tú en todo momento: Disponible, Ocupado, En reunión, Comiendo, Ausente o Descanso.

            Lo eliges tú y nadie te lo pone. Sirve para lo de siempre: saber si se puede interrumpir a alguien sin tener que preguntárselo primero por escrito. «Descanso» existe como estado propio porque es el que más se usa y el que más incomoda tener que explicar: se marca y se quita con un botón.

            # El cronómetro

            En las pantallas de trabajo —tickets, actividades, pool— hay un cronómetro para medir el tiempo que le dedicas a una cosa concreta. Tampoco es la jornada: la jornada dice a qué hora llegaste, el cronómetro dice en qué se te fue el día. Se puede iniciar, pausar y detener, y lo que suma es lo que después se compara con el esfuerzo que se había estimado.
            """),

        new("trabajo",
            "De dónde llega el trabajo: requerimientos, tickets, sprint y SLA",
            "manual, trabajo, requerimientos, tickets, sprint, sla",
            """
            El trabajo entra por varias puertas y todas terminan en la misma pregunta: qué tengo que hacer hoy. Este artículo explica cada puerta y dónde ver lo tuyo.

            # Las puertas de entrada

            - **Requerimientos** (`/requerimientos`). Lo que pide un área de la empresa: una funcionalidad nueva, un cambio, un reporte. Llegan por correo o los captura el líder, se estiman y se asignan.
            - **Azure DevOps** (`/devops`, y `Mis tickets DevOps` para lo tuyo). Los work items del repositorio: bugs, tareas, historias. La plataforma los sincroniza y los enseña junto al resto para no tener que vivir en dos pestañas.
            - **Freshdesk** (`/freshdesk`). Los tickets que levanta el soporte a clientes.
            - **El pool de actividades**. Trabajo publicado sin dueño que cualquiera puede tomar. Tiene su propio artículo en este manual.
            - **Actividades libres** (`Mis Actividades`). Lo que haces y no cabe en ninguna de las anteriores, y que igualmente hay que registrar para que el tiempo cuadre.

            # Dónde veo lo mío

            `Mi Panel` (`/mi-panel`) reúne lo que tienes asignado, lo que vence pronto y lo que te está esperando. Si solo vas a abrir una pantalla al día, que sea ésa.

            Después, cada puerta tiene su lista: `Mis Asignaciones`, `Mis tickets DevOps`, `Mis Actividades`, `Mi Pool` y `Mis SLA`.

            # El sprint

            El **sprint** es la ventana de tiempo —normalmente dos semanas— con el trabajo que el equipo se comprometió a terminar dentro de ella. Se ve en `/sprint`: el líder lo arma y lo cierra, y los desarrolladores lo consultan.

            Lo importante del sprint no es la ceremonia: es que lo que está dentro es lo que se prometió, y meter cosas a mitad de camino tiene un costo que alguien va a pagar. Si te cae algo nuevo y urgente, dilo en vez de absorberlo en silencio.

            # Estimar: esfuerzo y plazo

            Al aceptar trabajo se anotan dos números que se confunden todo el tiempo. El **esfuerzo** es cuántas horas de trabajo cuesta hacerlo. El **plazo** es para cuándo tiene que estar.

            Ocho horas de esfuerzo con plazo de una semana es perfectamente normal. Confundirlos es lo que hace que alguien prometa para el jueves algo que cuesta cuarenta horas.

            La estimación se registra en horas, con el cuarto de hora como unidad mínima. Y sirve para algo concreto: al terminar se compara con lo que marcó el cronómetro, y de esa comparación sale si el equipo estima bien o no. No es una nota: es la única forma de aprender a prometer fechas realistas.

            # El SLA

            Un **SLA** es un compromiso con fecha: «esto se responde en tanto tiempo» o «esto queda cerrado antes de tal día». Lo tuyo está en `Mis SLA`; el líder lo sigue en `/sla` y `/cumplimiento-sla`.

            Un SLA tiene cuatro estados: **Activo** (vigente, o vencido pero todavía exigible), **Cumplido** (el líder lo dio por atendido), **Vencido** (pasó la fecha sin cerrarse) y **Cancelado** (se dejó sin efecto).

            La plataforma avisa antes de que venza, no después. Si vas a incumplir uno, dilo mientras todavía sirva de algo: un compromiso renegociado a tiempo es una conversación; el mismo compromiso reconocido al día siguiente es un problema.
            """),

        new("pool",
            "El pool de actividades: cómo se toma una y qué significa cada estado",
            "manual, pool, actividades, puntos",
            """
            El pool es una lista de trabajo **sin dueño** que el líder publica y que cualquier desarrollador puede tomar. Lo tuyo está en `Mi Pool` (`/mi-pool`); el líder lo administra desde `/pool`.

            # Para qué existe

            Antes, los puntos de desempeño salían de que cada quien registrara lo que había hecho y el líder juzgara después cuánto valía. Dos decisiones subjetivas sobre trabajo ya hecho, y ninguna comparable entre personas.

            En el pool el valor está fijado **antes** de que nadie toque la actividad: se publica ya con sus puntos y su plazo. Así, verificar no es negociar cuánto vale, sino comprobar que está hecho.

            # Qué trae cada actividad

            - **Tipo**: bug, tarea o requerimiento.
            - **Complejidad**: baja, media, alta o muy alta. La fija el líder al publicarla.
            - **Puntos**: salen de la combinación de tipo y complejidad, y quedan **congelados** en la actividad. Si mañana cambia la tabla de puntos, lo que ya tomaste sigue valiendo lo que valía.
            - **Plazo**: cuántas horas tienes desde que la tomas. Ojo, son horas de reloj y no días hábiles: 40 horas es pasado mañana.
            - **Prioridad**: baja, media, alta o crítica. Ordena la lista y **no cambia los puntos**; si los cambiara, publicar «crítica» sería la manera de regalar puntos.
            - **Checklist**: lo que hay que poder afirmar para entregarla. Algunos puntos exigen evidencia —el enlace al pull request, por ejemplo— y sin ella no se entrega.
            - **Criterios extra** («además se evalúa»): trabajo adicional que el líder pide en voz alta y que **suma puntos encima de la base**, pero solo si lo da por cumplido al verificar. Son pocos y concretos —pruebas automatizadas, documentación, comentar el ticket con evidencias—: la idea es que se puedan mirar y contestar sí o no, no que haya que interpretarlos.

            Todo eso se ve **antes** de tomarla. Léelo antes de decidir.

            # El recorrido de una actividad

            - **Libre en el pool**: nadie la ha tomado. Se la lleva el primero que pulse; si dos lo hacen a la vez, solo uno se la lleva y el otro recibe un aviso.
            - **Tomada**: es tuya y el plazo corre. Puedes tener varias a la vez, hasta el tope que configure el líder (por omisión, **3**). El tope existe para que nadie aparte media lista y la deje muriendo.
            - **Por verificar**: la entregaste con su checklist marcado. Ahora espera al líder.
            - **Devuelta**: el líder la regresó con un motivo. Sigue siendo tuya: corriges y vuelves a entregar.
            - **Aceptada**: verificada. **Aquí, y solo aquí, se abonan los puntos.** Es un estado final.
            - **Retirada**: el líder la quitó del pool antes de que nadie la tomara.

            # Cuándo se cobran los puntos

            Al aceptarla, ni un momento antes. Tomar una actividad no paga nada, y entregarla tampoco. Los puntos se abonan una sola vez y aparecen en tu desempeño como una entrada ya aprobada: no vuelven a pasar por ninguna cola, porque la verificación del líder ya fue la revisión.

            # Consejos de quien ya se quemó

            - No tomes por puntos, toma por prioridad. Lo urgente está marcado.
            - Mira el plazo antes de tomar, no después. Es en horas y corre desde ya.
            - Si te vas a pasar del plazo, dilo antes de que se cumpla, no después.
            - Marca el checklist **de verdad**. La casilla que exige evidencia pide el enlace porque es lo único que el líder puede mirar sin volver a hacer tu trabajo.
            """),

        new("desempeno",
            "Los puntos y las evaluaciones: de dónde sale tu calificación",
            "manual, desempeno, puntos, evaluaciones",
            """
            El desempeño se lleva con **puntos**. Los tuyos están en `Mis Evaluaciones` (`/mis-evaluaciones`); el líder los administra en `/desempeno` y `/evaluaciones`.

            # Qué es un punto

            Un punto es la unidad con la que se reconoce el trabajo que va más allá de cumplir. Los puntos se acumulan por mes y por año, alimentan el ranking del equipo y son la base de la evaluación.

            No son horas ni tareas: son **reconocimiento**. Cerrar tus tickets es tu trabajo; lo que puntúa es lo que suma por encima de eso.

            # De dónde salen

            Hay tres caminos, y conviene distinguirlos.

            - **Del pool.** Tomas una actividad, la entregas, el líder la verifica y los puntos se abonan solos con el valor que traía congelado. Es el camino más objetivo, porque el valor estaba fijado antes de empezar.
            - **De la autocalificación.** Registras algo que hiciste eligiendo un criterio del catálogo, y queda **pendiente de aprobación**. El líder lo aprueba, lo ajusta o lo rechaza. Registrarlo no es ganarlo.
            - **Del líder, directamente.** Puede otorgar puntos por algo que vio, y también anotar puntos negativos cuando corresponde. Todo lleva su criterio y su comentario.

            Además, publicar un artículo en esta base de conocimiento puede otorgar puntos: lo decide el líder al aprobarlo, y no todos los artículos puntúan.

            # El catálogo de criterios

            Un punto nunca se otorga «porque sí»: siempre bajo un **criterio** del catálogo, que dice qué se está premiando y cuánto vale por omisión. Los criterios los mantiene el líder, y los hay de dos alcances: **individuales**, que se abonan a una persona, y **de equipo**, que son del grupo.

            Que todo cuelgue de un criterio es lo que hace comparables los puntos de dos personas distintas. Si te otorgan algo y no entiendes bajo qué criterio fue, pregúntalo: está escrito.

            # Las evaluaciones

            Cada cierto tiempo el líder cierra una **evaluación**: una foto de tu periodo con los puntos, los comentarios y lo acordado para el siguiente. La ves en `Mis Evaluaciones`.

            La evaluación no inventa números, los toma de lo que ya está anotado. Que es exactamente por lo que conviene que lo tuyo esté registrado a tiempo y no reconstruido de memoria la última semana.

            # Una cosa paga una sola vez

            Vale la pena decirlo porque sorprende: una actividad del pool abona sus puntos la primera vez que se acepta, y un artículo de conocimiento la primera vez que se publica. Si después se corrige y se vuelve a aprobar, se publica de nuevo pero **no se paga otra vez**. No es un castigo por corregir: es que ya se pagó.
            """),

        new("despliegues",
            "Cómo se despliega y qué hace el respaldo previo",
            "manual, despliegues, operaciones, respaldo",
            """
            Desplegar es publicar una versión de un programa en los servidores donde la gente la usa. Se hace desde `/despliegues`, y lo pueden hacer el líder y el área de operaciones.

            Éste es el artículo que más conviene leer entero antes de tocar nada: es la operación con más consecuencias por clic de toda la plataforma.

            # Las piezas

            - **Programa** (`/programas`): la aplicación que se despliega.
            - **Versión**: el paquete concreto que se va a publicar, ya subido al almacenamiento.
            - **Servidor destino**: dónde se publica, con su carpeta y sus credenciales.
            - **Perfil**: un conjunto de servidores guardado con nombre, para no ir eligiéndolos a mano cada vez. El líder decide qué perfiles puede usar el área de operaciones.

            # El checklist previo

            Antes de lanzar hay que marcar cuatro casillas. No es burocracia y no se puede saltar: la exigencia vive en el servidor, no en la pantalla.

            - **Verifiqué que la versión es la correcta.** El error más caro es desplegar la de ayer, y es el más fácil de cometer.
            - **El despliegue está autorizado para esta ventana de tiempo.** Fuera de la ventana acordada, una caída de dos minutos le pega a quien está trabajando.
            - **Avisé a quien corresponde.** Si algo se cae, alguien tiene que saber que fue el despliegue y no una falla.
            - **Sé cómo revertir si algo sale mal.** El momento de averiguarlo no es cuando ya está roto.

            Además hay que escribir una **nota**, y es obligatoria. Las casillas responden «¿se revisó?» y siempre salen marcadas, porque de otro modo no se despliega; la nota es lo único que responde «¿por qué este despliegue, ahora?». Escríbela pensando en quien la lea dentro de seis meses buscando qué cambió.

            Todo eso queda guardado dentro del despliegue, junto al hecho que documenta y no en un archivo aparte.

            # El respaldo previo

            Antes de sobrescribir la carpeta del servidor, la plataforma la **descarga entera, la comprime y la sube al almacenamiento**, bajo la carpeta de respaldos de despliegue. Es lo que permite revertir: sin él, un despliegue malo no tiene vuelta atrás.

            Y la regla que importa: **si el respaldo falla, no se despliega**. Un respaldo que «se intentó» no sirve de nada el día que hay que revertir, y desplegar creyendo que hay red de seguridad es peor que desplegar sabiendo que no la hay.

            Si la carpeta remota está vacía o no existe, no hay nada que respaldar y el despliegue sigue: es el primer despliegue a ese servidor.

            El respaldo se puede desactivar desde la configuración, y entonces cada despliegue lo avisa en su registro con todas sus letras. Si ves ese aviso y no sabías que estaba desactivado, detente y pregunta.

            # Mientras corre

            La pantalla enseña el avance en vivo, servidor por servidor. Al terminar, el despliegue queda con uno de estos estados: **Completado**, **Fallido**, **Cancelado** o **Parcial**, que es cuando unos servidores salieron bien y otros no. «Parcial» existe porque antes eso se reportaba como completado, que es la peor manera posible de enterarse.

            Dos despliegues no pueden ir a la vez al mismo destino: el segundo se rechaza. Y si el servidor que estaba ejecutando uno se detiene a media faena, el arranque siguiente lo cierra como interrumpido y deja escrito hasta dónde se llegó a saber.

            # Despliegues programados

            En `/programados` se deja un despliegue agendado para una hora. Corre solo, con las mismas reglas: mismo checklist, mismo respaldo, mismo registro. Programar algo no lo exime de nada, y sigue habiendo alguien responsable de mirar cómo salió.

            # Después

            Revisa `/estado-de-servidores` y confirma que lo desplegado responde. Un despliegue que terminó bien no es lo mismo que un sistema que funciona.
            """),

        new("foro",
            "El foro del equipo: qué se publica y cómo se escribe",
            "manual, foro, comunicacion",
            """
            El foro (`/foro`) es la conversación del equipo de desarrollo. Lo ven y escriben el líder y los desarrolladores; el área de operaciones no participa, porque su alcance son los despliegues.

            # Para qué sirve, y para qué no

            Sirve para lo que le interesa a más de una persona y conviene que quede escrito: una idea, una duda que otro puede tener mañana, algo que aprendiste a las malas, un anuncio.

            No sirve para lo urgente. Si necesitas respuesta en diez minutos, éste no es el sitio.

            Y no sirve para documentar. Cuando algo pasa de «lo que le contesté a fulano» a «cómo se hace esto», su lugar es la base de conocimiento: el foro se lee por orden de llegada y se entierra solo, mientras que la documentación se busca.

            # Los temas

            Al publicar eliges uno: **Idea**, **Pregunta**, **Aprendizaje**, **Anuncio** u **Otro**. Sirven para filtrar y para que el muro no sea una sopa. «Aprendizaje» es el que más se desaprovecha: es para lo que descubriste y no quieres que se pierda.

            # Cómo se escribe

            Una publicación lleva título, texto y etiquetas. Se pueden colgar imágenes —capturas, sobre todo— y los comentarios se anidan, así que puedes responder a un comentario concreto y no solo al hilo.

            El texto admite un marcado mínimo: negrita con doble asterisco, código entre acentos graves y enlaces. No se admite HTML, y no es una carencia: es lo que impide que una publicación ejecute algo en el navegador de quien la lea.

            # Buenas costumbres

            - **Título que se entienda solo.** «Duda» no es un título; «¿por qué el proceso nocturno duplica filas cuando falla el primer intento?» sí lo es.
            - **Pega el error completo**, no lo describas de memoria.
            - **Cierra tus hilos.** Si preguntaste y lo resolviste, escribe cómo. La mitad del valor de un foro está en las preguntas que ya tienen respuesta.
            - **Etiqueta.** Es por donde se encuentra lo de hace tres meses.

            # Correcciones y borrados

            Puedes editar y retirar lo tuyo. Al retirar, el texto deja de verse pero la entrada permanece, para que el hilo no quede con agujeros y las respuestas sigan teniendo sentido.

            # No lo confundas con los comunicados

            Un comunicado (`/comunicados`) va en una sola dirección: el líder anuncia y todo el mundo lo ve. El foro es conversación. Si esperas respuestas, publica en el foro.
            """),

        new("conocimiento",
            "La base de conocimiento: escribir un artículo, revisarlo y publicarlo",
            "manual, conocimiento, documentacion, glosario",
            """
            Esto que estás leyendo es la base de conocimiento (`/conocimiento`): la memoria escrita del equipo. Aquí va lo que hay que poder consultar dentro de un año — cómo se despliega un sistema, qué significa un término, qué se hace cuando algo falla.

            La lee todo el mundo, incluida el área de operaciones. Escribir y revisar es del líder y de los desarrolladores.

            # Buscar antes que escribir

            Arriba hay un buscador por texto libre y, al lado, la lista de **etiquetas** con cuántos artículos lleva cada una. Las etiquetas son el índice de verdad: pulsando una ves todo lo que habla de ese tema. Van siempre en minúsculas para que «SQL», «Sql» y «sql» no partan en tres el mismo asunto.

            El manual que estás leyendo son los artículos etiquetados `manual`. Las palabras sueltas —SLA, sprint, plazo, punto— son artículos cortos etiquetados `glosario`.

            # Los cuatro estados

            - **Borrador**. Solo lo ves tú. Ni siquiera el líder, y es deliberado: si alguien pudiera leer lo que está a medias, «borrador» dejaría de significar nada y la gente escribiría en otro lado hasta tenerlo presentable.
            - **Por revisar**. Lo mandaste y espera al líder. Lo ven él y tú, nadie más.
            - **Publicado**. Lo lee todo el equipo.
            - **Devuelto**. El líder lo regresó con un motivo. Lo ven él y tú, nadie más.

            # Cómo se escribe uno

            1. **Nuevo artículo**: título, texto y etiquetas. Nace en borrador y es privado.
            2. Trabájalo con calma. Un borrador puede estar como sea, para eso es un borrador.
            3. **Mándalo a revisar** cuando esté. Se exige un mínimo razonable de texto: hacer que el líder abra tres renglones sueltos es la forma más rápida de que deje de abrir la cola.
            4. El líder lo publica —con puntos o sin ellos— o te lo devuelve con un motivo que dice qué corregir.

            Cada envío es una **vuelta** numerada, así que en la tercera insistencia sobre lo mismo queda claro que lo es.

            # El marcado

            El texto admite lo justo, y que sea poco es parte de la defensa.

            - `#`, `##` y `###` al principio de la línea: títulos de tres niveles.
            - `-` o `*` al principio: punto de una lista. `1.`: lista numerada.
            - Doble asterisco para la **negrita** y acentos graves para el `código`, dentro de la misma línea.
            - Enlaces, escritos tal cual o con etiqueta.
            - Tres acentos graves en su propia línea abren y cierran un bloque de código, y dentro de él no se interpreta nada.
            - `![lo que se ve](imagen:12)`, sola en su renglón: una imagen. No la escribes tú — pegas la captura con Ctrl+V en el recuadro del editor, se sube en ese momento y la marca aparece puesta al final del texto para que la muevas donde la quieras.

            De las imágenes solo se incrustan las que subas ahí. Una dirección de otro sitio se queda en enlace, y es a propósito: lo que ilustra un artículo revisado no puede cambiar después sin que nadie se entere, ni contarle a un tercero quién lo está leyendo.

            No hay HTML ni tablas. Y ojo con una cosa: **los saltos de línea se respetan tal como los escribas**, así que un párrafo se escribe de corrido y se deja una línea en blanco para empezar el siguiente.

            Un artículo cabe en unas ocho páginas. Si no cabe, pártelo en varios y enlázalos, que además es como se encuentran después.

            # Editar algo ya publicado

            Si corriges un artículo tuyo que ya estaba publicado, **vuelve a la cola de revisión**. Es incómodo para arreglar una coma y aun así se hace, porque lo publicado es lo que el equipo lee dando por hecho que alguien lo revisó: si una edición posterior se colara sin pasar por ahí, bastaría con publicar algo inocuo y cambiarlo después.

            El líder sí puede corregir lo publicado sin sacarlo, porque su edición ya está revisada por definición: él es el revisor.

            # Borrar

            Solo se borra lo que nunca llegó a publicarse: borradores y devueltos. Lo publicado no se borra, porque alguien puede tenerlo enlazado; se **retira** con un motivo, y queda dicho por qué dejó de ser cierto. Y nada que haya otorgado puntos se borra jamás: esa fila es lo que justifica un abono en el desempeño de alguien.

            # Los puntos

            Al publicar, el líder puede otorgar puntos bajo un criterio del catálogo. No todos los artículos puntúan, y no pasa nada. Un artículo paga **una sola vez en su vida**: si se corrige y se vuelve a aprobar, se publica otra vez pero no se vuelve a pagar.

            # Qué escribir

            Lo que te costó averiguar. La regla práctica: si tuviste que preguntarle a alguien, o reconstruirlo leyendo código, o te tomó más de media hora entender por qué algo funciona así, eso es un artículo.

            Escríbelo el mismo día, mientras todavía recuerdas qué era lo que no entendías. Ésa es la parte que se olvida primero y la única que le sirve al siguiente.
            """),

        new("avisos",
            "Avisos, comunicados y correo: por dónde te llegan las cosas",
            "manual, avisos, comunicados, notificaciones",
            """
            La plataforma te habla por varios canales, y cada uno significa algo distinto. Vale la pena saber cuál es cuál para no acabar ignorando el que importa.

            # Avisos

            `/avisos` es tu correspondencia dentro de la plataforma: te resolvieron una solicitud, te devolvieron un artículo, un despliegue falló, un SLA está por vencer, te asignaron un ticket. Los tiene todo el mundo, incluida el área de operaciones.

            La entrada del menú lleva el número de lo que no has leído. Un aviso no se repite aunque el hecho que lo provocó se reintente; pero si corriges un artículo y lo vuelves a mandar, ése sí es un aviso nuevo, porque es otra revisión distinta.

            # Avisos en el navegador

            Puedes permitir que la plataforma te avise aunque no la tengas abierta, con las notificaciones del navegador. Se activa desde tus preferencias y se puede quitar cuando quieras. Si no lo activas no te pierdes nada: los avisos siguen estando en `/avisos`.

            # Comunicados

            `/comunicados` es lo que el líder anuncia al equipo: una ventana de mantenimiento, un cambio de proceso, un recordatorio de cierre. Va en una sola dirección —nadie contesta un comunicado— y por eso está separado del foro, que es donde sí se conversa.

            # Correo

            La plataforma también lee un buzón para dar de alta requerimientos que llegan por correo (`/correo`, solo el líder). Si alguna vez te preguntas cómo apareció un requerimiento que nadie capturó, suele ser por ahí.

            # Qué mirar cada día

            - `Mi Panel`: lo que tienes que hacer.
            - `/avisos`: lo que te ha pasado.
            - El número junto a **Conocimiento** en el menú: lo que te devolvieron, o lo que te toca revisar.

            Con esas tres no se te escapa nada que sea tuyo.
            """),
    ];
}
