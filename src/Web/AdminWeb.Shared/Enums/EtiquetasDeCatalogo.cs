namespace AdminWeb.Shared.Enums;

/// <summary>
/// Los textos con los que se presentan los enums de los catálogos de consulta.
///
/// Viven en Shared —y no en el servidor— porque los necesitan los dos lados: la API para llenar los
/// campos «…Texto» de los DTO, y el cliente para pintar los desplegables de filtro sin tener que
/// pedirle al servidor una lista de opciones que no cambia nunca.
///
/// Se copian AL PIE DE LA LETRA de los formularios de detalle del escritorio
/// (<c>SoftwareDetailForm.CategoryLabel</c>, <c>AzureResourceDetailForm.TypeLabel</c>,
/// <c>TeamsControl.RoleLabel</c>), emojis incluidos: mientras las dos aplicaciones convivan, quien
/// mire una y otra tiene que leer lo mismo. La errata «Utiliería» del escritorio se conserva a
/// propósito por lo mismo; corregirla es una decisión de producto, no una limpieza de port.
/// </summary>
public static class EtiquetasDeCatalogo
{
    // ── Programas (software) ─────────────────────────────────────────────────────

    public static string Categoria(SoftwareCategory c) => c switch
    {
        SoftwareCategory.IDE              => "💻 IDE / Editor",
        SoftwareCategory.ControlVersiones => "🔀 Control de versiones",
        SoftwareCategory.BaseDeDatos      => "🗄 Base de datos",
        SoftwareCategory.Navegador        => "🌐 Navegador",
        SoftwareCategory.Comunicacion     => "💬 Comunicación",
        SoftwareCategory.Disenio          => "🎨 Diseño",
        SoftwareCategory.Seguridad        => "🔒 Seguridad",
        SoftwareCategory.DevOps           => "🚀 DevOps / CI-CD",
        SoftwareCategory.Productividad    => "📋 Productividad",
        SoftwareCategory.UtileriaRed      => "🔌 Utiliería de red",
        _                                 => "📦 Otro"
    };

    public static string Licencia(SoftwareLicenseType l) => l switch
    {
        SoftwareLicenseType.Gratuita    => "🆓 Gratuita",
        SoftwareLicenseType.OpenSource  => "🌱 Open Source",
        SoftwareLicenseType.Comercial   => "💳 Comercial",
        SoftwareLicenseType.Prueba      => "⏳ Prueba / Trial",
        SoftwareLicenseType.Suscripcion => "🔄 Suscripción",
        _                               => l.ToString()
    };

    public static string EstadoDePrograma(SoftwareStatus s) => s switch
    {
        SoftwareStatus.EnUso        => "✅ En uso",
        SoftwareStatus.Instalado    => "📥 Instalado",
        SoftwareStatus.Desinstalado => "❌ Desinstalado",
        SoftwareStatus.Expirado     => "⚠ Expirado",
        _                           => s.ToString()
    };

    // ── Recursos de Azure ────────────────────────────────────────────────────────

    public static string TipoDeRecurso(AzureResourceType t) => t switch
    {
        AzureResourceType.AppService        => "🌐 App Service",
        AzureResourceType.SqlDatabase       => "🗄 SQL Database",
        AzureResourceType.StorageAccount    => "📦 Storage Account",
        AzureResourceType.KeyVault          => "🔑 Key Vault",
        AzureResourceType.FunctionApp       => "⚡ Function App",
        AzureResourceType.ContainerRegistry => "📋 Container Registry",
        AzureResourceType.ServiceBus        => "📨 Service Bus",
        AzureResourceType.CosmosDb          => "🌌 Cosmos DB",
        AzureResourceType.RedisCache        => "🔴 Redis Cache",
        AzureResourceType.VirtualMachine    => "💻 Máquina Virtual",
        _                                   => "☁ Otro"
    };

    public static string EstadoDeRecurso(AzureResourceStatus s) => s switch
    {
        AzureResourceStatus.EnUso     => "✅ En uso",
        AzureResourceStatus.NoUsado   => "⬜ Sin usar",
        AzureResourceStatus.EnPrueba  => "🧪 En prueba",
        AzureResourceStatus.Archivado => "🗃 Archivado",
        _                             => s.ToString()
    };

    public static string Ambiente(AzureEnvironment e) => e switch
    {
        AzureEnvironment.Produccion => "🔴 Producción",
        AzureEnvironment.Staging    => "🟡 Staging",
        AzureEnvironment.Desarrollo => "🟢 Desarrollo",
        AzureEnvironment.Compartido => "🔵 Compartido",
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

    // ── Bitácora ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Qué se hizo, en español. El escritorio pintaba el nombre crudo del enum («PasswordChange»)
    /// porque la rejilla se llenaba con <c>ToString()</c>; en la bitácora, que es lo que se lee
    /// cuando hay que explicar un incidente, el texto legible ahorra la traducción mental.
    /// El valor del enum viaja igual en el DTO, así que el filtro sigue siendo exacto.
    /// </summary>
    /// <summary>La urgencia de una actividad del pool. Lleva su marca de color porque es lo que se
    /// mira primero al decidir qué tomar.</summary>
    public static string PrioridadDelPool(PoolPriority p) => p switch
    {
        PoolPriority.Baja    => "Baja",
        PoolPriority.Media   => "Media",
        PoolPriority.Alta    => "Alta",
        PoolPriority.Critica => "Crítica",
        _                    => p.ToString()
    };

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

    public static string Resultado(AuditOutcome o) => o switch
    {
        AuditOutcome.Exito    => "✅ Éxito",
        AuditOutcome.Fallo    => "⚠ Falló",
        AuditOutcome.Denegado => "⛔ Denegado",
        _                     => o.ToString()
    };
}
