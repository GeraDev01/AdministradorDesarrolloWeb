namespace AdminWeb.Shared.Enums;

/// <summary>
/// Los textos con los que se presentan los enums de los catálogos de consulta.
///
/// Viven en Shared —y no en el servidor— porque los necesitan los dos lados: la API para llenar los
/// campos «…Texto» de los DTO, y el cliente para pintar los desplegables de filtro sin tener que
/// pedirle al servidor una lista de opciones que no cambia nunca.
///
/// Los textos van SIN EMOJI, y no se les vuelven a poner. Los emoji los dibuja EL SISTEMA OPERATIVO,
/// no nosotros: se ven distintos en cada equipo, NO heredan el color del texto —en el tema oscuro se
/// quedaban con el suyo mientras la palabra de al lado cambiaba— y donde no hay fuente de emoji
/// instalada salen como un CUADRO VACÍO. Eso último no es una hipótesis: la columna Prioridad de
/// Requerimientos enseñaba un cuadro delante de cada palabra.
///
/// Aquí decía antes que se copiaban AL PIE DE LA LETRA de los formularios de detalle del escritorio
/// (<c>SoftwareDetailForm.CategoryLabel</c>, <c>AzureResourceDetailForm.TypeLabel</c>,
/// <c>TeamsControl.RoleLabel</c>), EMOJIS INCLUIDOS, para que quien mirara las dos aplicaciones
/// leyera lo mismo. Esa razón era buena mientras convivían y ya no aplica: el escritorio se retira, y
/// la paridad ya se rompió a propósito en otros sitios (el punto de «sin leer» de Avisos). No la
/// restaures.
///
/// Estas cadenas alimentan <c>TextProperty</c> de <c>RadzenDropDown</c> y columnas de rejilla, que
/// son CADENAS y no admiten marcado: aquí dentro no cabe un icono, y poner el nombre de uno pintaría
/// la PALABRA. Lo que el emoji comunicaba de verdad —la urgencia, el ambiente, el acceso denegado— se
/// recupera EN LA PANTALLA, con un punto de color pintado con una variable del tema y la palabra
/// siempre al lado; el patrón está en <c>Componentes/BotonDeEstado.razor</c>.
///
/// La errata «Utiliería de red» viene del escritorio y de momento SE QUEDA. Ojo con el motivo, porque
/// ya no es el que había: se conservaba por esa paridad que acaba de caducar, así que hoy no hay
/// ninguna razón técnica para mantenerla — corregirla es una decisión del usuario, y nadie la ha
/// tomado todavía. Cámbiala cuando la pida, no de paso.
/// </summary>
public static class EtiquetasDeCatalogo
{
    // ── Programas (software) ─────────────────────────────────────────────────────

    public static string Categoria(SoftwareCategory c) => c switch
    {
        SoftwareCategory.IDE              => "IDE / Editor",
        SoftwareCategory.ControlVersiones => "Control de versiones",
        SoftwareCategory.BaseDeDatos      => "Base de datos",
        SoftwareCategory.Navegador        => "Navegador",
        SoftwareCategory.Comunicacion     => "Comunicación",
        SoftwareCategory.Disenio          => "Diseño",
        SoftwareCategory.Seguridad        => "Seguridad",
        SoftwareCategory.DevOps           => "DevOps / CI-CD",
        SoftwareCategory.Productividad    => "Productividad",
        SoftwareCategory.UtileriaRed      => "Utiliería de red",
        _                                 => "Otro"
    };

    public static string Licencia(SoftwareLicenseType l) => l switch
    {
        SoftwareLicenseType.Gratuita    => "Gratuita",
        SoftwareLicenseType.OpenSource  => "Open Source",
        SoftwareLicenseType.Comercial   => "Comercial",
        SoftwareLicenseType.Prueba      => "Prueba / Trial",
        SoftwareLicenseType.Suscripcion => "Suscripción",
        _                               => l.ToString()
    };

    /// <summary>
    /// En qué situación está el programa instalado.
    ///
    /// Estas tres tablas —categoría, licencia y estado— se quedan con la PALABRA SOLA, sin punto de
    /// color en la pantalla, y no es un olvido: la alarma de Programas ya está puesta a nivel de
    /// FILA, que se pinta entera en rojo cuando la licencia está vencida y en ámbar cuando está por
    /// vencer. Añadir aquí un segundo lenguaje de color encima del primero garantiza que un día los
    /// dos discrepen. Categoría y licencia, además, no son ninguna escala: sus emoji eran adorno.
    /// </summary>
    public static string EstadoDePrograma(SoftwareStatus s) => s switch
    {
        SoftwareStatus.EnUso        => "En uso",
        SoftwareStatus.Instalado    => "Instalado",
        SoftwareStatus.Desinstalado => "Desinstalado",
        SoftwareStatus.Expirado     => "Expirado",
        _                           => s.ToString()
    };

    // ── Recursos de Azure ────────────────────────────────────────────────────────

    public static string TipoDeRecurso(AzureResourceType t) => t switch
    {
        AzureResourceType.AppService        => "App Service",
        AzureResourceType.SqlDatabase       => "SQL Database",
        AzureResourceType.StorageAccount    => "Storage Account",
        AzureResourceType.KeyVault          => "Key Vault",
        AzureResourceType.FunctionApp       => "Function App",
        AzureResourceType.ContainerRegistry => "Container Registry",
        AzureResourceType.ServiceBus        => "Service Bus",
        AzureResourceType.CosmosDb          => "Cosmos DB",
        AzureResourceType.RedisCache        => "Redis Cache",
        AzureResourceType.VirtualMachine    => "Máquina Virtual",
        _                                   => "Otro"
    };

    public static string EstadoDeRecurso(AzureResourceStatus s) => s switch
    {
        AzureResourceStatus.EnUso     => "En uso",
        AzureResourceStatus.NoUsado   => "Sin usar",
        AzureResourceStatus.EnPrueba  => "En prueba",
        AzureResourceStatus.Archivado => "Archivado",
        _                             => s.ToString()
    };

    /// <summary>
    /// Para qué se levantó el recurso.
    ///
    /// Los cuatro valores son una escala de riesgo de verdad —producción &gt; staging &gt; compartido &gt;
    /// desarrollo— y el círculo rojo que llevaba «Producción» era lo que decía de un vistazo, en un
    /// inventario largo, qué NO se toca. Esa señal no se tira: la repone el punto de color de la
    /// columna Ambiente en <c>RecursosAzure.razor</c>, que además obedece al tema. El enum viaja en el
    /// DTO junto a este texto, así que la pantalla no tiene que releer la palabra para saber cuál es.
    /// El color de ese punto es <see cref="ColoresDeEstado.ColorDeAmbiente"/>, que ya vive en este
    /// mismo proyecto: la palabra y el color se deciden a un paso el uno del otro, no en dos capas
    /// distintas.
    /// De paso se arregla una confusión vieja: ese rojo competía con el de «Redis Cache», que era el
    /// LOGOTIPO del producto y se leía como una alarma que nunca fue.
    /// </summary>
    public static string Ambiente(AzureEnvironment e) => e switch
    {
        AzureEnvironment.Produccion => "Producción",
        AzureEnvironment.Staging    => "Staging",
        AzureEnvironment.Desarrollo => "Desarrollo",
        AzureEnvironment.Compartido => "Compartido",
        _                           => e.ToString()
    };

    // ── Equipos ──────────────────────────────────────────────────────────────────

    public static string RolDeEquipo(TeamRole r) => r switch
    {
        TeamRole.Lider     => "Líder",
        TeamRole.Frontend  => "Frontend Dev",
        TeamRole.Backend   => "Backend Dev",
        TeamRole.Fullstack => "Fullstack",
        TeamRole.QA        => "QA",
        TeamRole.DevOps    => "DevOps",
        TeamRole.UX        => "UX / Diseño",
        TeamRole.Otro      => "Otro",
        _                  => "Sin rol"
    };

    /// <summary>
    /// Color del rol, en hex para la web. Son los mismos ARGB que pintaba el tablero del escritorio
    /// (<c>TeamsControl.RoleColor</c>): la gente ya asocia «verde = backend» de un vistazo y perder
    /// esa asociación en la mudanza costaría más que conservarla.
    /// </summary>
    public static string ColorDeRol(TeamRole r) => r switch
    {
        TeamRole.Lider     => "#CA8A04",
        TeamRole.Frontend  => "#2563EB",
        TeamRole.Backend   => "#16A34A",
        TeamRole.Fullstack => "#7C3AED",
        TeamRole.QA        => "#EA580C",
        TeamRole.DevOps    => "#0D9488",
        TeamRole.UX        => "#DB2777",
        _                  => "#64748B"
    };

    /// <summary>
    /// Orden en el que se agrupan los integrantes dentro de una columna del organigrama. Es el
    /// <c>RoleOrder</c> del escritorio; el líder va aparte, arriba de todo.
    /// </summary>
    public static readonly TeamRole[] OrdenDeRoles =
        [TeamRole.Frontend, TeamRole.Backend, TeamRole.Fullstack, TeamRole.QA,
         TeamRole.DevOps, TeamRole.UX, TeamRole.Otro, TeamRole.SinRol];

    // ── Pool de actividades ──────────────────────────────────────────────────────

    /// <summary>
    /// La urgencia de una actividad del pool. Es lo primero que se mira al decidir qué tomar, y por
    /// eso el color —cuando lo hay— lo pone LA PANTALLA: esta función devuelve la palabra sola.
    ///
    /// <para>Lo que había escrito aquí decía que la etiqueta «lleva su marca de color». No era cierto
    /// —estas cuatro llevan tiempo saliendo limpias— y llevaba a reponer un color que nunca estuvo,
    /// así que se corrige: en la cadena no va color, y no debe volver.</para>
    ///
    /// <para>OJO: a diferencia de las demás de este archivo, esta etiqueta SÍ SE GUARDA. Se interpola
    /// en la descripción que <c>PoolActivityService</c> escribe en la BITÁCORA al publicar y al
    /// actualizar una actividad, y eso ya quedó grabado: cambiar la palabra no repinta lo viejo, deja
    /// asientos históricos diciendo una cosa y los nuevos otra. Por eso nunca llevó emoji y por eso
    /// este lote no la tocó. Si algún día hay que cambiarla, es una migración, no una edición.</para>
    /// </summary>
    public static string PrioridadDelPool(PoolPriority p) => p switch
    {
        PoolPriority.Baja    => "Baja",
        PoolPriority.Media   => "Media",
        PoolPriority.Alta    => "Alta",
        PoolPriority.Critica => "Crítica",
        _                    => p.ToString()
    };

    // ── Bitácora ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Qué se hizo, en español. El escritorio pintaba el nombre crudo del enum («PasswordChange»)
    /// porque la rejilla se llenaba con <c>ToString()</c>; en la bitácora, que es lo que se lee
    /// cuando hay que explicar un incidente, el texto legible ahorra la traducción mental.
    /// El valor del enum viaja igual en el DTO, así que el filtro sigue siendo exacto.
    /// </summary>
    public static string Accion(AuditAction a) => a switch
    {
        AuditAction.Login          => "Inicio de sesión",
        AuditAction.Logout         => "Cierre de sesión",
        AuditAction.Create         => "Alta",
        AuditAction.Update         => "Modificación",
        AuditAction.Delete         => "Baja",
        AuditAction.Deploy         => "Despliegue",
        AuditAction.Backup         => "Respaldo",
        AuditAction.PasswordChange => "Cambio de contraseña",
        AuditAction.ConfigChange   => "Cambio de configuración",
        AuditAction.Read           => "Consulta de dato sensible",
        _                          => a.ToString()
    };

    /// <summary>
    /// Cómo acabó lo que se intentó.
    ///
    /// De todo este archivo es donde más pesaba el símbolo que se fue: «Denegado» es justo la fila
    /// que se busca recorriendo cientos de renglones cuando hay que explicar un incidente, y las tres
    /// palabras solas pesan lo mismo en la vista. La distinción se recupera en la pantalla, con el
    /// punto de color de la columna Resultado de <c>Bitacora.razor</c>; el enum viaja en el DTO al
    /// lado de este texto, así que no hay que volver a interpretar la palabra para pintarlo.
    /// </summary>
    public static string Resultado(AuditOutcome o) => o switch
    {
        AuditOutcome.Exito    => "Éxito",
        AuditOutcome.Fallo    => "Falló",
        AuditOutcome.Denegado => "Denegado",
        _                     => o.ToString()
    };
}
