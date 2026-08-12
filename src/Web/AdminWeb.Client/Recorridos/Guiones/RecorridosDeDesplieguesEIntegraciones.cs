namespace AdminWeb.Client.Recorridos.Guiones;

/// <summary>
/// Los recorridos de las pantallas que sacan trabajo FUERA de esta aplicación: los despliegues y su
/// agenda, el almacén de paquetes, el estado del rack, los dos tableros de Azure DevOps, Freshdesk,
/// los vínculos entre ambos y el correo del área.
///
/// <para>Tienen un hilo común, y por eso están juntas: todas comandan algo que ocurre en otro sitio
/// —un servidor remoto, un contenedor de Azure, la cuenta de DevOps, el buzón— y la mitad de lo que
/// hay que explicar es justamente lo que no se ve desde aquí. Que el despliegue lo ejecuta el
/// servidor y no la pestaña; que sincronizar no borra lo que no vino; que un enlace firmado sirve
/// para quien no entra a la aplicación. Nada de eso está escrito en la etiqueta de ningún botón.</para>
///
/// <para>Los pasos que señalan controles de una pestaña que no está abierta —o de un panel que
/// todavía no se ha desplegado— se saltan solos. Están puestos a propósito: quien lanza el recorrido
/// desde esa pestaña sí los ve, y ahí es donde hacen falta.</para>
/// </summary>
public sealed class RecorridosDeDesplieguesEIntegraciones : IFuenteDeRecorridos
{
    public IEnumerable<Recorrido> Recorridos() =>
    [
        // ══════════════════════════════ DESPLIEGUES ══════════════════════════════

        new Recorrido("/despliegues", "Cómo se despliega una versión",

            Paso.Portada("Los despliegues",
                "Desde aquí se sube una versión de un sistema a los servidores. Lo ejecuta el " +
                "servidor, no esta pestaña: puedes cerrarla o irte a otra pantalla sin interrumpir " +
                "nada, y al volver la vista retoma sola el despliegue en curso. Las otras pestañas " +
                "son el catálogo de sistemas y versiones, el inventario de servidores y el historial."),

            new Paso("despliegues-version", "Qué se va a subir",
                "El sistema y la versión salen del catálogo de la pestaña «Sistemas y versiones», " +
                "donde también está el changelog de cada una. Si el servidor ya no encuentra el " +
                ".zip de la versión elegida, aquí abajo aparece el aviso: hay que volver a dejar el " +
                "paquete antes de intentarlo."),

            new Paso("despliegues-servidores", "A dónde va, y con red",
                "Cada renglón es un servidor y la columna «Tiene hoy» dice qué versión está puesta " +
                "en él ahora mismo. Al marcar uno se activa sola su casilla de respaldo: quien no se " +
                "pronuncia se lleva la copia de la carpeta remota, y quitarla deja a ese servidor " +
                "sin nada a lo que volver si la versión rompe algo. Los que necesitan recapturar su " +
                "contraseña no se dejan elegir."),

            new Paso("despliegues-atajos", "Perfiles como atajo",
                "Un perfil es un destino con nombre. Pulsarlo marca de golpe sus servidores en vez " +
                "de irlos eligiendo uno por uno, y después la selección se puede corregir a mano: " +
                "lo que se despliega es lo que quede marcado en la tabla."),

            new Paso("despliegues-preparar", "El checklist es obligatorio",
                "No lanza nada todavía. Abre un checklist que hay que marcar punto por punto, más " +
                "una nota escrita con el motivo: un ticket, un CAB, algo. Lo exige también el " +
                "servidor y no solo este formulario, y queda guardado como evidencia junto al " +
                "despliegue para poder descargarlo después desde el historial."),

            new Paso("despliegues-en-curso", "Mientras corre",
                "Aquí sale el avance y la consola del servidor, renglón por renglón, y lo mismo lo " +
                "ve cualquier otra persona que abra esta pantalla. Cancelar hay que pedirlo con su " +
                "botón: cerrar la pestaña no detiene nada, y cancelar a media subida puede dejar ese " +
                "servidor con archivos de dos versiones mezclados.")),

        // ═════════════════════════════ ALMACENAMIENTO ════════════════════════════

        new Recorrido("/almacenamiento", "El almacén de paquetes",

            Paso.Portada("El almacén",
                "El explorador del contenedor de Azure Blob Storage: aquí viven los paquetes de las " +
                "versiones, los respaldos que dejan los despliegues y los artefactos de la " +
                "compilación automática. Lo que se borra desde esta pantalla se borra de verdad en " +
                "Azure; no hay papelera."),

            new Paso("almacen-probar", "Comprobar antes de necesitarlo",
                "La cadena de conexión se captura en Configuración y no se puede leer desde aquí. " +
                "Esto es lo que la sustituye: lanza una llamada de prueba y contesta si el " +
                "contenedor existe y si además la cadena puede firmar enlaces de descarga, que es lo " +
                "que conviene saber antes de necesitar uno."),

            new Paso("almacen-carpeta", "La carpeta que se mira",
                "Cada consulta trae el contenido de una sola carpeta, y se abre en la de versiones " +
                "porque es la que casi siempre se viene a ver. Cambiarla vuelve a preguntarle a " +
                "Azure."),

            new Paso("almacen-eliminar-carpeta", "Borrar una carpeta",
                "Antes de preguntar se cuenta lo que hay dentro, así que la confirmación dice " +
                "cuántos elementos se van, subcarpetas incluidas, y avisa aparte si es una carpeta " +
                "base del sistema, porque eso se lleva el histórico entero. Hay que escribir BORRAR " +
                "para que se haga, y no se puede deshacer."),

            new Paso("almacen-subir", "Subir un archivo",
                "Azure sobrescribe sin preguntar, así que sin marcar la casilla el servidor rechaza " +
                "el nombre que ya exista en esa carpeta. El tope es de 256 MB por archivo y lo " +
                "vuelve a comprobar el servidor."),

            new Paso("almacen-filtro", "Buscar sin volver a Azure",
                "El filtro y el orden trabajan sobre lo que ya se trajo, así que no cuestan una " +
                "consulta por tecla. El filtro mira también los metadatos: el nombre de un cliente " +
                "o un checksum encuentran el archivo aunque no aparezcan en su nombre. Y el orden " +
                "por versión compara números, así que «1.10» queda después de «1.9» y no antes."),

            new Paso("almacen-archivos", "Descargar, enlazar, etiquetar",
                "«Enlace» genera una URL firmada con caducidad, y es para dársela a alguien que NO " +
                "entra a la aplicación: quien la tenga baja ese archivo sin más, y queda registrado " +
                "quién la generó y hasta cuándo sirve. «Metadatos» reemplaza el conjunto completo, " +
                "no lo mezcla: lo que quede en el formulario es lo que queda en el archivo.")),

        // ══════════════════════════ ESTADO DE SERVIDORES ═════════════════════════

        new Recorrido("/estado-de-servidores", "Qué versión tiene cada servidor",

            Paso.Portada("El estado del rack",
                "El historial de despliegues contesta qué se subió y cuándo. Esta pantalla contesta " +
                "la otra mitad, que es la que se pregunta en caliente: qué versión está puesta ahora " +
                "en cada servidor, quién se la puso y a cuál le falta actualizarse."),

            new Paso("servidores-atrasados", "Qué es estar atrasado",
                "Un servidor está atrasado cuando tiene una versión y no es la última publicada de " +
                "su sistema. La última se decide por fecha de alta y no comparando el texto: «1.10» " +
                "es posterior a «1.9» aunque alfabéticamente vaya antes, y no todos los sistemas " +
                "numeran igual. Este filtro trabaja en el navegador, así que el resumen de arriba " +
                "sigue contando el total."),

            new Paso("servidores-bajas", "Los dados de baja",
                "Un servidor no se borra, se da de baja: el historial tiene que seguir siendo " +
                "legible. Marcando esto vuelven a la lista, apagados, para poder consultar qué " +
                "tenían cuando dejaron de usarse."),

            new Paso("servidores-excel", "La exportación respeta el filtro",
                "Baja exactamente lo que la tabla está enseñando, con los dos interruptores " +
                "incluidos. Es lo que se busca: marcar «solo atrasados» y exportar da la lista corta " +
                "de máquinas por actualizar para mandársela a alguien, no el inventario entero."),

            new Paso("servidores-rejilla", "Cómo leer la tabla",
                "El punto de la columna «Estado» agrupa por color y la palabra de al lado es la que " +
                "identifica, así que se puede barrer el rack sin leer renglón por renglón. Un guion " +
                "en «Quién lo desplegó» no es un descuido: son despliegues anteriores a que eso se " +
                "empezara a registrar.")),

        // ═══════════════════════════════ PROGRAMADOS ═════════════════════════════

        new Recorrido("/programados", "La agenda de despliegues",

            Paso.Portada("Los despliegues programados",
                "La agenda: se deja apuntado qué versión sale a qué destino y a qué hora, y el " +
                "servidor lo ejecuta solo cuando llega el momento. Ya no hace falta que nadie tenga " +
                "nada abierto a esa hora, que era la limitación del escritorio."),

            new Paso("programados-disparador", "Mientras esto esté aquí",
                "El disparador del servidor está apagado, así que lo que se agende quedará esperando " +
                "y se marcará como perdido al pasar su tolerancia. Es lo correcto mientras la " +
                "aplicación de escritorio siga en producción —son sus temporizadores los que " +
                "ejecutan los programados— porque con los dos encendidos el despliegue saldría por " +
                "duplicado."),

            new Paso("programados-nueva", "Agendar una cita",
                "Abre el formulario: la versión, el destino —un perfil o servidores sueltos—, la " +
                "hora y la tolerancia. La hora se captura y se enseña en tu horario y viaja al " +
                "servidor en UTC, porque el servidor puede estar en otra zona. Y la selección de " +
                "servidores se congela al agendar: se desplegará a los que elegiste aunque después " +
                "cambie el perfil."),

            new Paso("programados-pendientes", "Solo pendientes",
                "La lista viene acotada a lo que todavía no ha pasado. Quitando la marca aparecen " +
                "también las citas cumplidas, canceladas y perdidas, apagadas: es el historial de la " +
                "agenda."),

            new Paso("programados-citas", "«Perdido» no es lo mismo que «fallido»",
                "Una cita se marca como perdida cuando se le pasó su margen de tolerancia sin " +
                "dispararse, y entonces ya no se ejecuta: desplegar a deshora, cuando nadie lo " +
                "espera, es peor que no desplegar. «Resultado» trae lo que contestó el servidor. " +
                "Mientras una cita siga en «Programado» se puede cancelar desde su renglón.")),

        // ══════════════════════════════ AZURE DEVOPS ═════════════════════════════

        new Recorrido("/devops", "El tablero de Azure DevOps",

            Paso.Portada("El tablero de DevOps",
                "Los work items del proyecto traídos a esta base: desde aquí se miran, se reparten y " +
                "se mueven sin abrir DevOps. El token con el que se habla con DevOps vive cifrado en " +
                "el servidor y nunca baja al navegador; esta pantalla solo sabe si hay uno puesto."),

            new Paso("devops-sincronizar", "Traer de DevOps",
                "Este botón sale a Azure DevOps: tarda, puede fallar a medias y escribe en nuestra " +
                "base. El de al lado solo vuelve a leer lo que ya está aquí. Al terminar avisa de lo " +
                "que cambió en los tickets que vigilas, que es el único momento en que se puede " +
                "saber."),

            new Paso("devops-selectiva", "Traer solo una parte",
                "Acota qué se trae: tipos, estados, personas y una ventana de días, y los criterios " +
                "se combinan entre sí. Sin marcar nada se traen todos. Sirve cuando la " +
                "sincronización completa se ha vuelto lenta y solo interesa una parte del tablero."),

            new Paso("devops-vista", "Vistas guardadas",
                "Una vista guarda las dos búsquedas de esta barra y, además, el estado de la tabla: " +
                "sus filtros por columna, el orden, los anchos y qué columnas se ven. Se empatan por " +
                "nombre, así que volver a guardar «Bugs abiertos» actualiza el que ya existe en vez " +
                "de dejar dos iguales."),

            new Paso("devops-indicadores", "Los indicadores siguen al filtro",
                "Cuentan sobre lo que la tabla está enseñando, filtros por columna incluidos, y no " +
                "sobre el total: filtrar por «Bug» y que el contador de bugs siguiera diciendo el " +
                "total no serviría de nada. La única cifra que no se mueve es la de «sin prioridad» " +
                "del renglón de abajo, que es trabajo pendiente del líder."),

            new Paso("devops-reglas", "Reglas de reparto",
                "Deciden a quién se le asigna cada work item cuando se importa como requerimiento. " +
                "Se evalúan por orden y gana la primera que coincide; si ninguna lo hace, se intenta " +
                "empatar al asignado del ticket con la ficha del desarrollador por correo o por " +
                "nombre."),

            new Paso("devops-importar", "Importar a requerimientos",
                "Da de alta como requerimientos los work items asignados y abiertos que todavía no " +
                "existan aquí, repartiéndolos con esas reglas. Lo que ya está cerrado no se da de " +
                "alta, y lo que ya existe no se duplica."),

            new Paso("devops-rejilla", "Lo que se hace en cada renglón",
                "Los botones del final comentan, reasignan, mueven de estado, cambian la prioridad, " +
                "abren la ficha de regresiones y devoluciones, y encienden la vigilancia. No son " +
                "cambios locales: se hacen en DevOps de verdad y quedan a nombre de quien los hace.")),

        // ══════════════════════════════ MIS TICKETS ══════════════════════════════

        new Recorrido("/mis-tickets", "Mis tickets de DevOps",

            Paso.Portada("Mis tickets",
                "Los work items que tienes asignados en Azure DevOps, sin depender de que el líder " +
                "sincronice: desde aquí los traes tú. Es también donde se estiman, se comentan y se " +
                "marcan para vigilarlos."),

            new Paso("mis-tickets-token", "Tu token, primero",
                "La consulta se resuelve contra la cuenta de tu token y no contra el correo de tu " +
                "ficha, así que sin él no puedes traer nada por tu cuenta. Se guarda cifrado en el " +
                "servidor, no vuelve a mostrarse y no se pierde al cambiar de computadora. Lo que la " +
                "aplicación publique en DevOps queda a tu nombre, y por eso no se comparte."),

            new Paso("mis-tickets-sincronizar", "Traer lo mío",
                "Le pide a DevOps lo asignado a la cuenta de tu token, con la misma ventana de " +
                "tiempo que tengas puesta abajo. Funciona aunque el correo de tu ficha no coincida " +
                "con el de tu cuenta de DevOps. «Actualizar», el de al lado, solo relee lo que ya " +
                "está guardado aquí."),

            new Paso("mis-tickets-ventana", "La ventana de tiempo",
                "Viene en noventa días a propósito: sin acotar, la lista enseña años de tickets " +
                "cerrados encima de los tres que tienes abiertos hoy. Dejándola en blanco se ve todo " +
                "el historial guardado, aunque al sincronizar el servidor lo acota a un año."),

            new Paso("mis-tickets-sin-estimar", "Lo que falta estimar",
                "El número entre paréntesis es cuánto tienes pendiente de estimar, y este filtro " +
                "deja a la vista solo eso. Avisa pero no bloquea nada: una sincronización que trae " +
                "doscientos tickets viejos no debe dejarte sin trabajar hasta estimarlos todos."),

            new Paso("mis-tickets-rejilla", "Estimar, comentar y vigilar",
                "La estimación se guarda aquí y en el campo Effort del work item, para que valga " +
                "también fuera de esta aplicación, y se puede corregir las veces que haga falta. La " +
                "campana marca los tickets que vigilas: lo que cambie en ellos te lo dirá la " +
                "siguiente sincronización.")),

        // ═════════════════════════════ DEVOPS POR TAG ════════════════════════════

        new Recorrido("/devops-tags", "El tablero por etiqueta",

            Paso.Portada("Por etiqueta",
                "Reparte los work items ya sincronizados por sus etiquetas —el cliente, la " +
                "categoría, el tipo de atención— para ver quién acumula más trabajo. No habla con " +
                "DevOps: se calcula sobre lo que ya está en esta base, así que abrirla nunca depende " +
                "de que la integración conteste."),

            new Paso("etiquetas-entrar", "Entrar a una etiqueta",
                "Acota el tablero a los tickets que la llevan y vuelve a repartirlos por las DEMÁS " +
                "etiquetas que tengan: es como se ve, dentro de un cliente, cuánto es bug y cuánto " +
                "es soporte. La etiqueta en la que se entra no se lista a sí misma, porque contarla " +
                "no diría nada. Pulsar un nombre en la tabla hace lo mismo."),

            new Paso("etiquetas-abiertos", "Aquí «cerrado» incluye «resolved»",
                "Es la única pantalla donde se cuenta así, y es a propósito: ésta mide trabajo " +
                "pendiente de ATENDER, y algo resuelto ya no lo está aunque el ticket siga sin " +
                "cerrarse formalmente."),

            new Paso("etiquetas-indicadores", "Se cuenta por ticket",
                "Un ticket con tres etiquetas suma un renglón en cada una, así que la columna " +
                "«Tickets» de la tabla puede sumar más que el total. Estos indicadores, en cambio, " +
                "cuentan por ticket: son la respuesta a «¿cuántos bugs tiene este cliente?»."),

            new Paso("etiquetas-exportar", "La exportación",
                "Baja a hoja de cálculo el mismo reparto, con la etiqueta en la que estés y el " +
                "filtro de abiertos aplicados. Lo que no viaja es la búsqueda ni el orden de la " +
                "barra: ésos los resuelve el navegador sobre lo ya calculado.")),

        // ═══════════════════════════════ FRESHDESK ═══════════════════════════════

        new Recorrido("/freshdesk", "Los tickets de Freshdesk",

            Paso.Portada("Freshdesk",
                "Una copia de la cola de Freshdesk traída a esta base: cuántos tickets hay de cada " +
                "estado, cuáles son y quién los lleva. Sirve para mirar y para cruzarlos con DevOps; " +
                "contestarlos se sigue haciendo en Freshdesk."),

            new Paso("freshdesk-sincronizar", "Traer de Freshdesk",
                "Sale a la cuenta de Freshdesk, tarda y escribe en nuestra base; «Recargar» solo " +
                "relee lo que ya está aquí. Antes de traer nada guarda el filtro de abajo, porque es " +
                "lo que decide qué se va a traer: sincronizar con un criterio distinto del que se " +
                "está viendo sería una trampa."),

            new Paso("freshdesk-filtro-sync", "Qué se trae",
                "Acota la sincronización a lo asignado a ti o a un grupo; sin ningún criterio se " +
                "traen todos los tickets de la cuenta. El grupo admite el nombre o el ID numérico, y " +
                "el ID no es un capricho: cuando la clave de API es de agente, Freshdesk no deja " +
                "listar los grupos y no hay forma de resolver el nombre."),

            new Paso("freshdesk-tarjetas", "Las cifras de la cola",
                "Cuentan siempre sobre todos los tickets guardados, no sobre lo que la búsqueda esté " +
                "enseñando: son el estado de la cola, y una cifra que se moviera con cada búsqueda " +
                "no serviría de referencia."),

            new Paso("freshdesk-buscar", "Buscar y acotar",
                "La búsqueda mira a la vez el número, el asunto, el agente y el solicitante, así que " +
                "no hay que elegir campo. La resuelve el servidor, o sea que cada cambio cuesta un " +
                "viaje; si la lista se recorta por tamaño, se avisa a la derecha y hay que afinar."),

            new Paso("freshdesk-rejilla", "El detalle, y lo que no se borra",
                "Al seleccionar una fila se abre abajo el ticket completo con su descripción tal " +
                "como la escribió quien lo abrió. Y conviene saber esto: sincronizar NO borra lo que " +
                "no vino. La API solo devuelve unos treinta días, así que un ticket ausente no es un " +
                "ticket borrado, y nunca se retira uno que tenga un vínculo con DevOps.")),

        // ═══════════════════════════ VÍNCULOS DE TICKETS ═════════════════════════

        new Recorrido("/vinculos", "Atar un ticket con su work item",

            Paso.Portada("Vínculos de tickets",
                "Ata un work item de Azure DevOps con el ticket de Freshdesk que lo originó. Nadie " +
                "puede deducir de forma fiable que «no puedo timbrar» es «corregir el redondeo del " +
                "timbrado»: lo sabe una persona, y aquí es donde lo dice. Por eso el vínculo guarda " +
                "quién lo hizo y cuándo. Esta pestaña lista lo ya atado; en la otra se ata."),

            new Paso("vinculos-tarjetas", "Cuánto queda por atar",
                "Las tres primeras cuentan cosas —work items, tickets y vínculos— y las tres últimas " +
                "dicen cómo vamos. «Sin vincular» suma los dos lados: es el trabajo pendiente real " +
                "de esta pantalla."),

            new Paso("vinculos-buscar", "Buscar entre lo ya atado",
                "Mira el título del work item, el asunto del ticket, los dos números y las notas del " +
                "vínculo. Lo filtra el servidor; si la lista se recorta por tamaño la pantalla lo " +
                "dice, y el pie sigue contando la verdad."),

            new Paso("vinculos-rejilla", "Deshacer un vínculo",
                "«Quitar» deshace nada más la relación: no borra ninguno de los dos tickets ni toca " +
                "nada en DevOps ni en Freshdesk. La columna «Notas» es el único sitio donde queda " +
                "escrito por qué se ataron, así que vale la pena llenarla."),

            new Paso("vinculos-sin-vincular", "Por omisión se ve todo",
                "Las dos listas enseñan todos los tickets y no solo los que están sin atar, y es " +
                "deliberado: un work item puede tener varios tickets detrás, y esconder lo ya " +
                "vinculado impediría añadirle el segundo. Esta casilla es para cuando lo que se " +
                "quiere es justamente ver lo pendiente."),

            new Paso("vinculos-vincular", "Atar los dos",
                "Se elige una fila a la izquierda y una a la derecha, y este botón los ata; hasta " +
                "que haya las dos selecciones está apagado. Que no estuvieran ya vinculados lo " +
                "comprueba el servidor. Las notas de aquí arriba son opcionales y son lo único que " +
                "explicará, dentro de seis meses, por qué son el mismo trabajo.")),

        // ═════════════════════════════════ CORREO ════════════════════════════════

        new Recorrido("/correo", "El correo del área",

            Paso.Portada("El correo",
                "Tres cosas con el buzón del área: mirar lo que llega y convertirlo en " +
                "requerimientos, mandar un correo desde la cuenta de la aplicación, y ver el resumen " +
                "del equipo antes de que salga. La contraseña de la cuenta no se captura ni se " +
                "enseña aquí: eso está en Configuración y no vuelve nunca al navegador."),

            new Paso("correo-probar", "Saber si el buzón sigue sirviendo",
                "Como la contraseña no se puede mirar, esto es lo que la sustituye: intenta entrar y " +
                "dice si el rechazo vino de la cuenta, del puerto o de la red. Cada uno se arregla " +
                "en un sitio distinto, y por eso el mensaje se enseña tal como lo manda el servidor."),

            new Paso("correo-carpeta", "La carpeta que se mira",
                "Se abre en la carpeta que se ingiere, que es la que interesa. Ojo con los dos " +
                "botones: «Cargar» sale a hablar con el buzón —tarda y depende de un servidor " +
                "ajeno—, mientras que «Recargar», el de arriba, solo relee nuestra propia " +
                "configuración."),

            new Paso("correo-importar", "Importar de golpe",
                "Crea un requerimiento por cada correo que siga SIN LEER en la carpeta y lo marca " +
                "como leído: esa marca es lo único que evita que se vuelva a importar. O sea que si " +
                "marcas correos a mano desde tu cliente de correo, estás decidiendo qué se importa y " +
                "qué no."),

            new Paso("correo-bandeja", "Convertir uno solo",
                "«Requerimiento» crea uno a partir de ese correo. Al servidor solo se le manda la " +
                "carpeta y el identificador del mensaje, no el texto: el servidor relee el correo " +
                "del buzón, para que lo que quede guardado sea el original y siga sirviendo como " +
                "evidencia."),

            new Paso("correo-enviar", "Redactar",
                "Sale desde la cuenta del área y como texto plano; varios destinatarios se separan " +
                "con coma o punto y coma. Desde aquí no se adjunta nada, porque en el servidor no " +
                "hay archivos tuyos: lo que sí se manda con adjunto es lo que el propio servidor " +
                "genera, y eso se hace desde Reportes."),

            new Paso("correo-resumen", "El resumen del equipo",
                "Arma el resumen con las cifras de ahora y lo enseña sin mandarlo, que es lo que en " +
                "el escritorio no se podía hacer: cuando no llegaba, no había forma de saber si era " +
                "que no había nada que reportar o que algo falló. Si todas las cifras están en cero, " +
                "el envío automático se salta el período. «Enviar ahora» lo manda y cuenta como el " +
                "de este período."))
    ];
}
