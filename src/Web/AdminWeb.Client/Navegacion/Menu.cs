using AdminWeb.Shared.Enums;

namespace AdminWeb.Client.Navegacion;

/// <summary>Una entrada del menú lateral.</summary>
/// <param name="Icono">
/// Nombre de un icono de Material Symbols, la fuente que Radzen ya trae EMPAQUETADA (no se pide a
/// ninguna red externa).
///
/// <para>Antes aquí iba un emoji. Se cambiaron por dos razones concretas, no por gusto: los emojis
/// los dibuja el sistema operativo, así que la misma pantalla se veía distinta en Windows, en un Mac
/// y en un móvil —y algunos, como ⏱ o 🖊, se pintaban en color aunque el resto de la interfaz fuera
/// monocroma—; y no heredan el color del texto, de modo que en el tema oscuro seguían brillando con
/// sus colores de siempre sobre el fondo nuevo. Un icono de trazo hereda <c>currentColor</c> y se ve
/// igual en todas partes.</para>
///
/// <para>OJO al elegir uno nuevo: la fuente que se sirve es un RECORTE de Material Symbols, y los
/// 177 nombres que trae están listados en <c>wwwroot/fuentes/iconos.txt</c>. Un nombre que no esté
/// ahí no falla ni deja hueco: se pinta la PALABRA dentro del menú, y de eso nadie se entera hasta
/// que lo ve en pantalla. Comprueba el nombre en esa lista antes de escribirlo; el compilador no te
/// va a avisar.</para>
/// </param>
/// <param name="Texto">Lo que se lee.</param>
/// <param name="Ruta">La dirección de la página. En el escritorio era una clave interna; aquí es una
/// URL de verdad, así que se gana poder compartir el enlace y usar el botón de atrás.</param>
/// <param name="Disponible">False mientras la pantalla no exista todavía: se muestra apagada, para
/// que se vea qué falta en lugar de fingir que el menú está completo.</param>
public record ItemDeMenu(string Icono, string Texto, string Ruta, bool Disponible = true);

/// <summary>Un grupo plegable del menú.</summary>
public record GrupoDeMenu(string Titulo, IReadOnlyList<ItemDeMenu> Items);

/// <summary>
/// El menú lateral, por rol.
///
/// Es el equivalente del <c>PuedeVer</c> del escritorio, con una diferencia que conviene tener
/// clara: <b>esto es comodidad, no seguridad</b>. El cliente corre en la máquina de cada persona y
/// se puede manipular; quien quiera puede llamar a la API sin pasar por aquí. La barrera real son
/// las políticas declaradas en los endpoints y las guardas dentro de los servicios. Este menú solo
/// evita enseñar puertas que no se pueden abrir.
///
/// Un rol no reconocido se queda SIN menú a propósito, igual que en el escritorio: antes caía en el
/// «si no» final y heredaba en silencio los permisos de Operaciones.
/// </summary>
public static class Menu
{
    public static IReadOnlyList<GrupoDeMenu> Para(UserRole? rol) => rol switch
    {
        UserRole.Admin => Admin,
        UserRole.Desarrollador => Desarrollador,
        UserRole.Operaciones => Operaciones,
        _ => []
    };

    /// <summary>
    /// Lo que va arriba del todo, fuera de cualquier grupo.
    ///
    /// Los AVISOS sí los ve todo el mundo, y tienen que verlos: son la correspondencia de la cuenta
    /// —un despliegue que falló, por ejemplo— y a un operativo le llegan igual que a cualquiera.
    ///
    /// El FORO y el Dashboard NO los ve Operaciones. El foro es la conversación del equipo de
    /// desarrollo, y el Dashboard enseña carga del equipo, recordatorios internos y ranking de
    /// desempeño: nada de eso le corresponde a un área cuyo alcance son los despliegues.
    ///
    /// <b>Esconderlos de aquí no es el permiso</b> —lo dice el comentario de la clase—: la barrera de
    /// verdad la ponen las políticas de los grupos <c>/api/foro</c> y <c>/api/jornada</c>, más las
    /// guardas dentro de los servicios. Esto solo evita enseñar una puerta que contestaría 403.
    /// </summary>
    public static IReadOnlyList<ItemDeMenu> Sueltos(UserRole? rol)
    {
        var items = new List<ItemDeMenu>
        {
            new("notifications", "Avisos", "avisos"),
        };

        // «speed» (un velocímetro) y no «dashboard»: ese eran cuatro rectángulos que no dicen nada, y
        // encima se confundía a simple vista con el «space_dashboard» de Mi Panel, que es la MISMA
        // idea para otro rol. El velocímetro es el tablero de control de toda la vida y no compite
        // con ninguna de las pantallas de gráficas (monitoring, query_stats, leaderboard).
        if (rol is UserRole.Admin or UserRole.Desarrollador)
        {
            items.Add(new ItemDeMenu("forum", "Foro", "foro"));
            items.Add(new ItemDeMenu("speed", "Dashboard", "dashboard"));
        }

        return items;
    }

    private static readonly IReadOnlyList<GrupoDeMenu> Admin =
    [
        new("Equipo",
        [
            new("badge", "Desarrolladores", "desarrolladores"),
            new("sensors", "Quién está", "presencia"),
            new("trending_up", "Perfil y desarrollo", "perfiles"),
            new("campaign", "Comunicados", "comunicados"),
            new("groups", "Equipos", "equipos"),
            new("contacts", "Contactos", "contactos"),
        ]),
        new("Trabajo",
        [
            new("assignment", "Requerimientos", "requerimientos"),
            // Una bandera puede ser meta, incidencia, idioma o marcador: no decía nada. Un sprint se
            // define por su ventana de fechas, y esta pantalla dibuja literalmente una línea de
            // tiempo de rangos, así que «date_range» es lo que se ve al entrar. Mismo cambio en la
            // entrada de Sprint del desarrollador, que es la misma pantalla.
            new("date_range", "Sprint", "sprint"),
            new("monitoring", "Métricas", "metricas"),
            new("summarize", "Reportes", "reportes"),
            new("balance", "Estimación y capacidad", "estimacion"),
            // «note_alt» es una hoja escrita, que es exactamente lo que es una minuta. El lápiz
            // («edit») se queda para la ACCIÓN de editar, que se repite en decenas de pantallas: una
            // entrada de menú nombra un sitio, no una acción, y no debe llevar su mismo glifo.
            new("note_alt", "Minutas", "minutas"),
            // Notas y pendientes tiene pantalla PROPIA, y no una pestaña dentro de vacaciones como en
            // el escritorio: allí convivían por accidente de la interfaz, no porque tengan relación.
            // Por eso «Vacaciones» se llama así a secas.
            new("push_pin", "Notas y pendientes", "notas"),
            new("beach_access", "Vacaciones", "vacaciones"),
            new("event_available", "Permisos", "permisos"),
            new("emoji_events", "Desempeño", "desempeno"),
            new("inventory_2", "Pool de actividades", "pool"),
            new("fact_check", "Evaluaciones", "evaluaciones"),
            new("extension", "Actividades libres", "actividades"),
            new("timer", "SLA y recordatorios", "sla"),
            // «query_stats» (lupa sobre barras) decía «análisis», y ya hay tres pantallas de análisis
            // en este mismo grupo. Ésta contesta a una sola pregunta —¿se cumplió o no?— y «rule» es
            // la palomita y la tacha sobre renglones, que es exactamente «cumple / no cumple».
            new("rule", "Cumplimiento SLA", "cumplimiento-sla"),
            new("lightbulb", "Sugerencias", "sugerencias"),
        ]),
        new("Despliegue e Infraestructura",
        [
            new("rocket_launch", "Despliegues", "despliegues"),
            // «event» era un calendario a secas: no distinguía una cita de un festivo ni de un plazo,
            // y dejaba tres entradas de calendario indistinguibles entre Sprint, Permisos y ésta. Un
            // despliegue programado se dispara solo a una hora: eso es un despertador. Además libera
            // la familia calendario para las otras dos.
            new("alarm", "Programados", "programados"),
            new("dns", "Estado de servidores", "estado-de-servidores"),
            // Estas dos se INTERCAMBIAN la nube, y el orden importa: aquí no se habla de un proveedor
            // de nube sino del almacén de archivos y respaldos de la aplicación, así que «storage»
            // (discos apilados) dice «aquí viven los datos»…
            new("storage", "Almacenamiento", "almacenamiento"),
            // …y la nube limpia se la queda la única pantalla que sí habla de un servicio en la nube.
            // Antes iban «cloud» y «cloud_circle» seguidas: dos nubes casi idénticas en el mismo grupo.
            new("cloud", "Recursos Azure", "recursos-azure"),
            // «apps» era la cuadrícula de puntos, el genérico más vacío del menú. Esto es el catálogo
            // de aplicaciones desplegables y «category» (tres formas distintas) lee «catálogo de cosas
            // de tipos distintos». Es un SUSTITUTO, no el ideal: lo suyo sería «deployed_code», que no
            // está en el recorte de la fuente. Si alguna vez se regenera, éste es el primero a revisar.
            new("category", "Programas", "programas"),
        ]),
        new("Integraciones y Correo",
        [
            new("mail", "Correo", "correo"),
            new("account_tree", "Azure DevOps", "devops"),
            // La etiqueta ya la dice el TEXTO de la entrada; lo que el icono tiene que decir es en qué
            // se diferencia de la pantalla de DevOps de arriba, y la diferencia es que aquí se compara
            // y se cuenta: «leaderboard» son barras ordenadas. De paso libera «label», que también se
            // pisaba con la acción de enlace.
            new("leaderboard", "Dashboard por tag", "devops-tags"),
            new("confirmation_number", "Freshdesk", "freshdesk"),
            // «link» era a la vez esta pantalla y la ACCIÓN «abrir la URL» de Mis Actividades: el mismo
            // glifo para un menú y para un botón. Esta pantalla correlaciona dos sistemas ajenos entre
            // sí, y «hub» (nodo central con radios) es justo eso. La cadena se queda como acción y sólo
            // como acción.
            new("hub", "Vínculos tickets", "vinculos"),
        ]),
        new("Administración",
        [
            new("library_books", "Plantillas", "plantillas"),
            // Material reutilizable que mantiene el líder, igual que las plantillas. Antes solo se
            // llegaba entrando a las vacaciones de otra persona, así que preparar la firma del jefe
            // antes de la primera solicitud no tenía sitio.
            new("draw", "Firmas", "firmas"),
            // «admin_panel_settings» (escudo con persona) lleva un engranaje implícito y se confundía
            // con «Configuración», que está dos filas más abajo. «manage_accounts» (persona con
            // engranaje) dice «gestionar personas», que es la pantalla, y deja el engranaje SOLO a
            // Configuración: por eso Configuración se queda con «settings» aunque sea el genérico de
            // manual — aquí el genérico es el específico, y lo que sobraba era el de arriba.
            new("manage_accounts", "Usuarios", "usuarios"),
            new("history", "Bitácora", "bitacora"),
            new("settings", "Configuración", "configuracion"),
            // Va al final y separada a propósito: es la única operación sin vuelta atrás, y no debe
            // quedar a un resbalón de las que se usan a diario.
            new("delete_sweep", "Limpieza de datos", "limpieza"),
        ]),
    ];

    private static readonly IReadOnlyList<GrupoDeMenu> Desarrollador =
    [
        new("Lo mío",
        [
            // «space_dashboard» era indistinguible del «dashboard» del líder a 20 px, y las dos
            // pantallas conviven en la misma aplicación con roles distintos. Ésta es la pantalla de
            // aterrizaje del desarrollador: «home» lo dice sin leer y no compite con ninguna gráfica.
            new("home", "Mi Panel", "mi-panel"),
            new("inventory_2", "Pool de actividades", "mi-pool"),
            new("assignment_ind", "Mis Asignaciones", "mis-asignaciones"),
            new("account_tree", "Mis tickets DevOps", "mis-tickets"),
            new("extension", "Mis Actividades", "mis-actividades"),
            new("fact_check", "Mis Evaluaciones", "mis-evaluaciones"),
            new("timer", "Mis SLA", "mis-sla"),
            new("schedule", "Mi jornada", "mi-jornada"),
            // El sprint del equipo, en solo consulta: la misma pantalla del líder sin sus botones de
            // escritura. Va con lo demás «suyo» porque es su trabajo comprometido.
            new("date_range", "Sprint", "sprint"),
        ]),
        new("Herramientas",
        [
            new("library_books", "Plantillas", "plantillas"),
            new("beach_access", "Mis Vacaciones", "mis-vacaciones"),
            new("event_available", "Mis Permisos", "mis-permisos"),
            new("lightbulb", "Sugerencias", "mis-sugerencias"),
        ]),
    ];

    /// <summary>
    /// Operaciones: un solo grupo, y es todo lo que hay. <b>La lista es corta a propósito y no es un
    /// olvido a medio terminar.</b>
    ///
    /// El alcance de esta área son los despliegues y mirar el estado de los servidores. No registra
    /// jornada —su estado es siempre «Disponible» y lo pone el servidor, así que no hay nada suyo que
    /// repasar en «Mi jornada»— y no participa del foro del equipo de desarrollo. Aquí había un grupo
    /// «Lo mío» con «Mi jornada»; se quitó cuando se quitó el marcaje, no antes.
    ///
    /// Y como siempre en este archivo: quitar la entrada del menú NO es el permiso. Ese está en las
    /// políticas de <c>/api/jornada</c> y <c>/api/foro</c> y en las guardas de los servicios; esto
    /// solo evita enseñar puertas que contestarían 403.
    /// </summary>
    private static readonly IReadOnlyList<GrupoDeMenu> Operaciones =
    [
        new("Despliegue",
        [
            new("rocket_launch", "Despliegues", "despliegues"),
            new("alarm", "Programados", "programados"),
            // «Almacenamiento» NO va aquí: es SoloAdmin. Ahí se ve y se borra el contenido del
            // almacén, incluidos los respaldos, y ese alcance no es el de operaciones.
            new("dns", "Estado de servidores", "estado-de-servidores"),
        ]),
    ];
}
