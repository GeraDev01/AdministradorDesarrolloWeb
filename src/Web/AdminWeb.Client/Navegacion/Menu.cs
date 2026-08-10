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
    /// Avisos y Foro los ve todo el mundo. El Dashboard NO lo ve Operaciones: enseña carga del
    /// equipo, recordatorios internos y ranking de desempeño, y nada de eso le corresponde a un
    /// área cuyo alcance son los despliegues.
    /// </summary>
    public static IReadOnlyList<ItemDeMenu> Sueltos(UserRole? rol)
    {
        var items = new List<ItemDeMenu>
        {
            new("notifications", "Avisos", "avisos"),
            new("forum", "Foro", "foro"),
        };

        if (rol is UserRole.Admin or UserRole.Desarrollador)
            items.Add(new ItemDeMenu("dashboard", "Dashboard", "dashboard"));

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
            new("flag", "Sprint", "sprint"),
            new("monitoring", "Métricas", "metricas"),
            new("summarize", "Reportes", "reportes"),
            new("balance", "Estimación y capacidad", "estimacion"),
            new("edit_note", "Minutas", "minutas"),
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
            new("query_stats", "Cumplimiento SLA", "cumplimiento-sla"),
            new("lightbulb", "Sugerencias", "sugerencias"),
        ]),
        new("Despliegue e Infraestructura",
        [
            new("rocket_launch", "Despliegues", "despliegues"),
            new("event", "Programados", "programados"),
            new("dns", "Estado de servidores", "estado-de-servidores"),
            new("cloud", "Almacenamiento", "almacenamiento"),
            new("cloud_circle", "Recursos Azure", "recursos-azure"),
            new("apps", "Programas", "programas"),
        ]),
        new("Integraciones y Correo",
        [
            new("mail", "Correo", "correo"),
            new("account_tree", "Azure DevOps", "devops"),
            new("label", "Dashboard por tag", "devops-tags"),
            new("confirmation_number", "Freshdesk", "freshdesk"),
            new("link", "Vínculos tickets", "vinculos"),
        ]),
        new("Administración",
        [
            new("library_books", "Plantillas", "plantillas"),
            // Material reutilizable que mantiene el líder, igual que las plantillas. Antes solo se
            // llegaba entrando a las vacaciones de otra persona, así que preparar la firma del jefe
            // antes de la primera solicitud no tenía sitio.
            new("draw", "Firmas", "firmas"),
            new("admin_panel_settings", "Usuarios", "usuarios"),
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
            new("space_dashboard", "Mi Panel", "mi-panel"),
            new("inventory_2", "Pool de actividades", "mi-pool"),
            new("assignment_ind", "Mis Asignaciones", "mis-asignaciones"),
            new("account_tree", "Mis tickets DevOps", "mis-tickets"),
            new("extension", "Mis Actividades", "mis-actividades"),
            new("fact_check", "Mis Evaluaciones", "mis-evaluaciones"),
            new("timer", "Mis SLA", "mis-sla"),
            new("schedule", "Mi jornada", "mi-jornada"),
            // El sprint del equipo, en solo consulta: la misma pantalla del líder sin sus botones de
            // escritura. Va con lo demás «suyo» porque es su trabajo comprometido.
            new("flag", "Sprint", "sprint"),
        ]),
        new("Herramientas",
        [
            new("library_books", "Plantillas", "plantillas"),
            new("beach_access", "Mis Vacaciones", "mis-vacaciones"),
            new("event_available", "Mis Permisos", "mis-permisos"),
            new("lightbulb", "Sugerencias", "mis-sugerencias"),
        ]),
    ];

    private static readonly IReadOnlyList<GrupoDeMenu> Operaciones =
    [
        new("Despliegue",
        [
            new("rocket_launch", "Despliegues", "despliegues"),
            new("event", "Programados", "programados"),
            // «Almacenamiento» NO va aquí: es SoloAdmin. Ahí se ve y se borra el contenido del
            // almacén, incluidos los respaldos, y ese alcance no es el de operaciones.
            new("dns", "Estado de servidores", "estado-de-servidores"),
        ]),
        new("Lo mío",
        [
            // La asistencia es de la CUENTA y no de la ficha de desarrollador: operaciones también
            // marca entrada y salida, y el botón de la barra ya se lo enseña. Sin esta entrada no
            // tendría dónde repasar sus días ni dónde pedir una corrección.
            new("schedule", "Mi jornada", "mi-jornada"),
        ]),
    ];
}
