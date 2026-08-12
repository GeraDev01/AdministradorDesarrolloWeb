namespace AdminWeb.Client.Recorridos.Guiones;

/// <summary>
/// Los recorridos de las pantallas de Administración: configuración, firmas, limpieza, minutas y
/// reportes.
///
/// <para>Son las cinco pantallas que solo ve el líder, y las cinco tienen el mismo problema: hacen
/// cosas cuyo efecto no se ve desde donde se pulsa. Guardar la configuración no enseña la
/// contraseña que quedó, borrar un apartado de limpieza se lleva tablas que no se nombran en el
/// renglón, y eliminar una minuta arrastra sus compromisos pendientes. Eso es lo que cuentan estos
/// guiones; lo que ya está escrito en la pantalla no se repite.</para>
/// </summary>
public sealed class RecorridosDeAdministracion : IFuenteDeRecorridos
{
    public IEnumerable<Recorrido> Recorridos() =>
    [
        // ── Configuración ────────────────────────────────────────────────────
        new Recorrido("/configuracion", "La configuración compartida",

            Paso.Portada("La configuración",
                "Aquí viven las claves con las que la aplicación habla con los servicios de fuera " +
                "—el correo, el almacenamiento, Azure DevOps— y los ajustes que se cambian sin que " +
                "nadie tenga que volver a compilar nada. Se toca lo que haga falta y se guarda todo " +
                "junto al final."),

            new Paso("configuracion-secretos", "Los secretos no se leen",
                "Las contraseñas, los tokens y las cadenas de conexión nunca salen del servidor: " +
                "desde aquí solo se sabe si están puestos. La consecuencia práctica es que para " +
                "cambiar uno hay que volver a escribirlo entero — no se puede corregir la última " +
                "letra de una cadena de conexión."),

            new Paso("configuracion-guardar", "Guardar, al final",
                "Nada se guarda al salir de una caja. Este botón manda de una sola vez SOLO las " +
                "claves que tocaste, y debajo aparece un renglón por cada una diciendo si quedó " +
                "guardada o por qué no. Una caja de secreto que dejaste vacía significa «no lo " +
                "toques», nunca «bórralo»."),

            new Paso("configuracion-probar", "Probar la conexión",
                "Intenta conectarse de verdad al servicio de ese apartado y explica lo que " +
                "encontró: que el contenedor todavía no existe, que no aceptaron las credenciales " +
                "o qué carpeta del buzón falta. Prueba lo que está GUARDADO y no lo que acabas de " +
                "teclear; si el apartado tiene cambios pendientes, te avisa antes."),

            new Paso("configuracion-quitar", "Quitar un secreto",
                "Apunta esa clave para que se borre, pero todavía no borra nada: hasta que pulses " +
                "Guardar tienes un «Deshacer» al lado. Es la forma de dejar una integración sin " +
                "credenciales, que es distinto de reemplazarlas por otras."),

            new Paso("configuracion-plantilla", "La plantilla del Word",
                "La solicitud de vacaciones en Word se rellena sobre este archivo, así que cambiar " +
                "una palabra del formato no exige tocar el programa. Descárgala, edítala " +
                "conservando los marcadores entre llaves y vuelve a subirla: si falta alguno se " +
                "rechaza y se te dice cuál. El PDF no depende de esto, ese lo maqueta el código.")),

        // ── Firmas ───────────────────────────────────────────────────────────
        new Recorrido("/firmas", "Las firmas del líder",

            Paso.Portada("Firmas",
                "Las firmas que la aplicación estampa en los documentos que autoriza el jefe. Se " +
                "guardan una vez y se reutilizan; esta pantalla existe para prepararlas con calma, " +
                "antes de que llegue la primera solicitud que haya que firmar."),

            new Paso("firmas-compartidas", "No son firmas tuyas",
                "Son las del jefe y las comparte toda el área: quien entre aquí administra las " +
                "mismas. Borrar una no cambia los documentos ya firmados con ella, porque la " +
                "imagen viajó dentro del PDF archivado, que es la prueba de que se autorizó."),

            new Paso("gestor-firmas-nueva", "Trazar una firma",
                "Se dibuja dentro del recuadro con el ratón o con el dedo y se guarda con un " +
                "nombre. El nombre es obligatorio porque es por lo que se elige después, al firmar " +
                "un documento: la imagen ahí no se ve hasta que ya está estampada."),

            new Paso("gestor-firmas-escaneada", "Partir de un escaneo",
                "Es lo normal para la firma del jefe: se firma en papel una vez y se escanea, " +
                "porque con el ratón nunca sale igual. Al cargarla se le quita el fondo del papel " +
                "y se recorta al trazo, y la vista previa aparece sobre un damero para que se note " +
                "qué quedó transparente. Se guarda con el mismo botón de arriba."),

            new Paso("gestor-firmas-lista", "Las firmas guardadas",
                "La marcada como predeterminada es la que se ofrece primero cuando toca firmar, y " +
                "solo puede haber una. Desde cada renglón se renombra, se cambia cuál es la " +
                "predeterminada o se elimina.")),

        // ── Limpieza ─────────────────────────────────────────────────────────
        new Recorrido("/limpieza", "Limpieza de datos",

            Paso.Portada("Limpieza de datos",
                "Sirve para vaciar de un tirón los registros que se van acumulando mientras se " +
                "prepara la aplicación. Borra TODO lo que haya en el apartado que marques, no solo " +
                "lo que parezca de prueba, y no hay papelera ni deshacer."),

            new Paso("limpieza-barra", "Marcar en bloque",
                "«Recontar» vuelve a preguntar cuántos registros hay en cada apartado, y «Marcar " +
                "lo que tenga datos» selecciona de golpe todos los que no estén en cero. El texto " +
                "de la derecha lleva la cuenta de cuántos registros se llevaría la selección tal " +
                "como está ahora mismo."),

            new Paso("limpieza-apartados", "Qué se lleva cada uno",
                "Bajo el nombre de cada apartado está lo que ARRASTRA además de sus propias filas " +
                "—las horas y los comentarios de un requerimiento, por ejemplo—, que es justo lo " +
                "que no se sabe cuando se borra fila por fila desde su pantalla. Lo que ya está en " +
                "cero sale atenuado y no se puede marcar."),

            new Paso("limpieza-contrasena", "Tu contraseña",
                "La comprueba el SERVIDOR antes de tocar nada: tener la sesión abierta no basta. " +
                "La barrera está porque a esta pantalla se llega por una dirección que alguien " +
                "pudo compartir, o desde una sesión que quedó abierta en un equipo prestado."),

            new Paso("limpieza-borrar", "El borrado",
                "Antes de borrar enseña la lista de apartados y pide escribir a mano una palabra " +
                "de confirmación. Al terminar informa apartado por apartado de cuántas filas se " +
                "fueron; si alguno falló lo dice ahí, y los demás sí se borraron.")),

        // ── Minutas ──────────────────────────────────────────────────────────
        new Recorrido("/minutas", "Minutas y compromisos",

            Paso.Portada("Minutas",
                "El registro de las reuniones del equipo y, sobre todo, de los compromisos que " +
                "salieron de ellas. Se entra a saber qué se acordó y quién quedó de hacer qué, no " +
                "solo a guardar la nota."),

            new Paso("minutas-filtros", "El período que se ve",
                "Arranca en los últimos tres meses. Lo que estos filtros dejen a la vista es " +
                "EXACTAMENTE lo que baja el botón de Excel: si acotas a un mes, el archivo trae " +
                "ese mes y no la tabla entera."),

            new Paso("minutas-nueva", "Capturar una minuta",
                "Abre el panel de captura en esta misma pantalla. Dentro se escribe lo que se " +
                "habló y se van agregando los compromisos, cada uno con su responsable y su fecha " +
                "límite; los renglones que se dejen sin descripción no se guardan, así que uno " +
                "vacío no estorba."),

            new Paso("minutas-rejilla", "La columna que importa",
                "«Pendientes» son los compromisos de esa minuta que nadie ha marcado como " +
                "cumplidos, y van en ámbar. Es lo que distingue una minuta que ya es solo archivo " +
                "de una que todavía es trabajo de alguien. «Abrir» trae el contenido completo, que " +
                "la lista no carga porque puede ser largo."),

            new Paso("minutas-eliminar", "Eliminar",
                "Se lleva la minuta con todos sus compromisos, incluidos los que siguen " +
                "pendientes; la confirmación te dice cuántos son antes de que decidas. No se puede " +
                "deshacer.")),

        // ── Reportes ─────────────────────────────────────────────────────────
        new Recorrido("/reportes", "El centro de reportes",

            Paso.Portada("Reportes",
                "Se elige un reporte, un período y a quién se mira; el resultado se ve en tabla y " +
                "resumido en un tablero, y de ahí se baja a Excel o se manda por correo. Los " +
                "cálculos los hace el servidor, así que un período largo tarda un poco en volver."),

            new Paso("reportes-catalogo", "Elegir el reporte",
                "Elegir uno lo genera al momento: no hay que pulsar nada después. Como varios " +
                "tienen nombres parecidos, bajo la barra queda siempre la DESCRIPCIÓN de lo que " +
                "mide el que está puesto, que es lo que de verdad los distingue."),

            new Paso("reportes-personas", "Acotar a unas personas",
                "Vacío significa todas. Al elegir a alguien el reporte se regenera solo, igual que " +
                "al mover las fechas: no hay un momento en que la tabla enseñe una cosa y los " +
                "filtros digan otra."),

            new Paso("reportes-excel", "Bajar a Excel",
                "Baja TODAS las filas del reporte, no la página que se está viendo. La tabla de " +
                "abajo va paginada para que el navegador no tenga que pintar mil renglones de " +
                "golpe, pero el archivo viene completo."),

            new Paso("reportes-correo", "Mandarlo por correo",
                "Este botón solo abre el panel para escribir a quién se manda. Lo que va es el " +
                "reporte que está en pantalla —con este período y este filtro— adjunto en una hoja " +
                "de cálculo; el que envía de verdad es el «Enviar» de dentro. Si el envío falla, " +
                "lo escrito se queda para corregir la dirección y reintentar."),

            new Paso("reportes-vistas", "Tabla o tablero",
                "Las dos pestañas enseñan lo mismo de dos formas. «Tablero» lo resume en cifras " +
                "grandes y una gráfica con las categorías mayores; qué columna agrupa y cuál suma " +
                "lo decide cada reporte, no se configura desde aquí."))
    ];
}
