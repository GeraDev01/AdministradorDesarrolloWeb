using AdminWeb.Shared.Dtos.Conocimiento;
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
/// <param name="Marca">
/// Cuántas cosas esperan a quien mira, detrás de esa puerta. Cero —lo normal— significa que la
/// entrada se pinta tal cual.
///
/// <para>No es un adorno: es lo único que sostiene una cola de revisión. Una cola que hay que
/// acordarse de abrir no se abre, y en cuanto dos artículos se quedan esperando, quien los escribió
/// deja de escribir. El número tiene que estar donde ya se está mirando.</para>
/// </param>
/// <param name="MarcaTitulo">Qué son esas cosas, para el tooltip. Un número suelto no dice si son
/// tuyas o de otro, ni desde cuándo llevan ahí.</param>
public record ItemDeMenu(string Icono, string Texto, string Ruta, bool Disponible = true,
    int Marca = 0, string? MarcaTitulo = null);

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
        // «Mi cuenta» se engancha AQUÍ, en un solo sitio, y no dentro de cada una de las tres listas.
        // Es el mismo grupo para los tres roles, y escribirlo tres veces es la forma segura de que el
        // día que se le añada algo se le añada a dos.
        UserRole.Admin => [.. Admin, MiCuenta],
        UserRole.Desarrollador => [.. Desarrollador, MiCuenta],
        UserRole.Operaciones => [.. Operaciones, MiCuenta],
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
    /// <param name="conocimiento">
    /// Lo que espera a quien mira en la base de conocimiento. Null mientras no se haya podido
    /// preguntar —o antes de que haya sesión—, y entonces la entrada va sin número: enseñar un cero
    /// que en realidad significa «todavía no lo sé» sería peor que no enseñar nada, porque el cero
    /// se lee como «no hay nada pendiente» y se deja de mirar.
    /// </param>
    public static IReadOnlyList<ItemDeMenu> Sueltos(UserRole? rol, ConocimientoPendientesDto? conocimiento = null)
    {
        var items = new List<ItemDeMenu>
        {
            new("notifications", "Avisos", "avisos"),
        };

        // La base de conocimiento la ve TODO el mundo, Operaciones incluida, y no es un descuido: un
        // artículo publicado es documentación de trabajo —cómo se despliega algo, qué significa un
        // término, qué hacer cuando falla—, y dejar fuera justo a quien despliega convertiría la base
        // en un sitio donde no se puede escribir lo que más falta hace. Escribir y revisar siguen
        // siendo del líder y de los desarrolladores, y eso lo deciden las políticas de
        // /api/conocimiento, no esta línea.
        //
        // «description» —una hoja escrita— y no «library_books»: esa ya es Plantillas, y dos entradas
        // de menú con el mismo glifo a 20 px son la misma entrada repetida. Comprobado en
        // wwwroot/fuentes/iconos.txt, que es la lista completa de lo que trae el recorte de la fuente;
        // un nombre que no esté ahí no deja hueco, pinta la PALABRA dentro del menú.
        var (marca, titulo) = MarcaDeConocimiento(conocimiento);
        items.Add(new ItemDeMenu("description", "Conocimiento", "conocimiento",
            Marca: marca, MarcaTitulo: titulo));

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

    /// <summary>
    /// El número de la entrada de conocimiento y su explicación.
    ///
    /// <para><b>Cuenta los DOS lados de la cola</b>, porque una cola de revisión se muere igual por
    /// arriba que por abajo: al líder le pesa lo que le falta revisar, y a quien escribe le pesa lo
    /// que le devolvieron y todavía no ha corregido. Las dos cosas son «algo que me está esperando
    /// ahí dentro», que es lo único que un número en un menú puede significar sin confundir.</para>
    ///
    /// <para>Los BORRADORES quedan fuera del número a propósito, aunque también sean propios: un
    /// borrador no espera a nadie, es trabajo en curso, y un menú que marca 4 porque alguien tiene
    /// cuatro cosas a medias deja de significar «hay algo que atender» al segundo día. Sí se cuentan
    /// dentro de la pantalla, que es donde sirven.</para>
    ///
    /// <para>La antigüedad del más viejo va en el tooltip y no en el número: es el dato que de verdad
    /// avergüenza —«el más viejo lleva nueve días»— pero no cabe en una marca de dos dígitos.</para>
    /// </summary>
    private static (int Marca, string? Titulo) MarcaDeConocimiento(ConocimientoPendientesDto? p)
    {
        if (p == null) return (0, null);

        // Aquí NO se mira el rol. El servidor ya devuelve cero en «por revisar» a quien no revisa, y
        // repetir esa decisión en el cliente sería un segundo sitio donde equivocarse — con el
        // agravante de que este corre en la máquina de quien mira.
        var partes = new List<string>();
        if (p.PorRevisar > 0)
            partes.Add(p.DiasDelMasAntiguo > 0
                ? $"{p.PorRevisar} por revisar (el más viejo lleva {p.DiasDelMasAntiguo} día(s))"
                : $"{p.PorRevisar} por revisar");
        if (p.MisDevueltos > 0) partes.Add($"{p.MisDevueltos} que te devolvieron");
        if (p.MisBorradores > 0) partes.Add($"{p.MisBorradores} borrador(es) tuyo(s)");

        int marca = p.PorRevisar + p.MisDevueltos;
        return (marca, partes.Count == 0 ? null : string.Join(" · ", partes));
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
        // ── DE NUEVE ENTRADAS A CUATRO ───────────────────────────────────────────
        //
        // Esto es la mitad visible de la puerta única, y la más barata de las dos. El desarrollador
        // tenía NUEVE pantallas de trabajo y ninguna era «la suya»: para saber qué hacer había que
        // recorrerlas, y el mismo requerimiento aparecía en cuatro de ellas con dos estimaciones
        // distintas y ningún puente. La pregunta que había que eliminar no era «¿dónde registro
        // esto?» sino la de antes: «¿dónde miro qué tengo que hacer?».
        //
        // LAS QUE SALEN NO SE BORRAN, y es una decisión, no una transición a medias:
        // /mis-asignaciones, /mis-tickets, /mis-actividades y /mis-sla siguen VIVAS —con su
        // [Authorize] intacto, su recorrido guiado intacto y sus enlaces profundos intactos—, solo
        // que ya no son un sitio al que ir por costumbre sino uno al que se llega desde un enlace
        // concreto. Requerimientos, tickets y SLA son FUENTES de trabajo, no listas que repasar: lo
        // que hay que hacer con ellos se convierte en una actividad del pool, que es donde vive el
        // valor y el plazo.
        //
        // El precio, dicho sin adornos: /mis-asignaciones era el ÚNICO sitio donde el cronómetro
        // corría sobre un requerimiento, y fuera del menú ese cronometraje muere para el
        // desarrollador. Se acepta porque es coherente con «el pool es la unidad de trabajo», y
        // porque en los datos de demostración el 100 % de las sesiones cuelgan de una actividad y
        // ninguna de un requerimiento. Es evidencia, no prueba.
        new("Lo mío",
        [
            // «space_dashboard» era indistinguible del «dashboard» del líder a 20 px, y las dos
            // pantallas conviven en la misma aplicación con roles distintos. Ésta es la pantalla de
            // aterrizaje del desarrollador: «home» lo dice sin leer y no compite con ninguna gráfica.
            new("home", "Mi Panel", "mi-panel"),
            // LA PANTALLA. Se llama «Mi trabajo» y no «Pool de actividades» porque el pool es cómo
            // funciona por dentro y esto es lo que el desarrollador viene a hacer. La RUTA no cambia:
            // una nueva obligaría a un recorrido nuevo, a reescribir el manual y dejaría con 404 los
            // avisos que guardaron «pool» como destino, todo a cambio de nada.
            new("inventory_2", "Mi trabajo", "mi-pool"),
            new("schedule", "Mi jornada", "mi-jornada"),
            new("fact_check", "Mis Evaluaciones", "mis-evaluaciones"),
        ]),
        new("Herramientas",
        [
            // El sprint baja aquí desde «Lo mío»: es del EQUIPO y en solo consulta, así que no es
            // trabajo suyo que repasar sino contexto que a veces se mira. Puesto arriba competía por
            // la atención con lo que sí hay que hacer.
            new("date_range", "Sprint", "sprint"),
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

    /// <summary>
    /// Lo de la CUENTA de quien mira, que no es lo mismo que su trabajo.
    ///
    /// <para>Los demás grupos de este archivo nombran áreas —el equipo, el trabajo, los despliegues,
    /// las integraciones—, y en ninguna de ellas se le ocurre entrar a quien viene a revisar en qué
    /// navegadores dejó de pedírsele el código: eso se busca donde está lo de uno mismo. Arriba,
    /// suelto, tampoco era el sitio: ahí van las puertas que se abren TODOS los días —Avisos y
    /// Conocimiento para cualquiera, más Foro y Dashboard para quien los tenga (ver
    /// <see cref="Sueltos"/>)—, y esto se toca dos veces al año. Mezclarlo con ellas le daba a un
    /// ajuste anual el mismo peso visual que a la bandeja de avisos.</para>
    ///
    /// <para><b>El grupo es propio y no una entrada colgada de un grupo que ya existiera</b> porque
    /// toda cuenta tiene contraseña y segundo factor, sea cual sea el rol, y no hay ni un solo grupo
    /// que vean los tres: «Administración» es del líder, «Herramientas» del desarrollador y
    /// «Despliegue» de operaciones. Colgarlo de cualquiera de ellos lo dejaría fuera para dos de cada
    /// tres personas; colgarlo de uno distinto en cada rol haría que «está en tal sitio» dejara de
    /// poder decirse por teléfono, que es justo cuando se dice —cuando alguien perdió el teléfono y
    /// hay que guiarle a ciegas—.</para>
    ///
    /// <para>Y va el ÚLTIMO de todos: no es trabajo, así que no debe empujar hacia abajo lo que sí se
    /// abre a diario.</para>
    ///
    /// <para><b>Hoy dentro solo está «Mi acceso», que administra el SEGUNDO FACTOR</b> —códigos de
    /// rescate y equipos recordados— y nada más. Queda dicho para que el párrafo de arriba no se lea
    /// como que la contraseña también está aquí: <c>/cambiar-contrasena</c> existe como pantalla pero
    /// no tiene entrada en ningún sitio del menú, ni antes ni ahora — solo se llega si el servidor
    /// empuja a ella con <c>MUST_CHANGE_PASSWORD</c> o escribiendo la dirección. Este grupo es su
    /// sitio natural el día que se decida darle una puerta; no se le puso aquí porque añadir una
    /// entrada nueva al menú no era parte de mover ésta.</para>
    /// </summary>
    private static readonly GrupoDeMenu MiCuenta = new("Mi cuenta",
    [
        // El segundo factor lo tiene TODO el mundo —es obligatorio— y por eso esta entrada no depende
        // del rol. No está aquí para activarlo: eso ocurre solo la primera vez y el servidor lleva a
        // esa pantalla sin que nadie la busque. Está para lo de después, que es lo que si no no
        // tendría por dónde alcanzarse: ver en qué navegadores se dejó de pedir el código y dejar de
        // confiar en ellos, y emitir códigos de rescate nuevos cuando quedan pocos.
        //
        // «shield» y no «lock»: el candado ya significa «cerrado / privado» en otras pantallas, y
        // «key» es lo que se usa para credenciales de integraciones. Comprobado en iconos.txt, que es
        // la lista de lo que trae el recorte de la fuente; un nombre que no esté ahí no deja hueco,
        // pinta la PALABRA dentro del menú.
        new("shield", "Mi acceso", "segundo-factor"),
    ]);
}
