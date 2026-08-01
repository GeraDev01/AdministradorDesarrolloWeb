namespace Administrador_Desarrollo_Web.Models;

/// <summary>
/// Para qué sirve la plantilla. Determina cómo se agrupa en la pantalla, con qué extensión se
/// guarda en disco y con qué icono se lista.
/// </summary>
public enum TemplateKind
{
    /// <summary>Cuerpo para ABRIR un ticket en Freshdesk (reporte de incidencia, solicitud).</summary>
    TicketFreshdesk = 0,
    /// <summary>Respuesta al cliente dentro de un ticket de Freshdesk.</summary>
    RespuestaFreshdesk = 1,
    /// <summary>Comentario u observación sobre un requerimiento.</summary>
    ComentarioRequerimiento = 2,
    /// <summary>Comentario para un work item de Azure DevOps.</summary>
    ComentarioDevOps = 3,
    /// <summary>Documento con el que se entrega una estimación al cliente o al área.</summary>
    DocumentoEstimacion = 4,
    ScriptSql = 5,
    ScriptPowerShell = 6,
    ScriptBash = 7,
    Otro = 8
}

/// <summary>
/// Texto reutilizable del área: el cuerpo de un ticket de Freshdesk, la observación que se repite en
/// cada requerimiento, el esqueleto del documento de estimación o un script de utilería (SQL,
/// PowerShell, Bash). Vive en la base del equipo, así que se captura una vez y la ve todo el que
/// entre con cuenta de administrador.
///
/// El cuerpo admite <b>marcadores</b> con la forma <c>{{nombre}}</c> que se rellenan al copiar
/// (ver <c>TemplateService.Marcadores</c>). Los documentos que no son texto —un .docx de
/// estimación— viajan en <see cref="FileBytes"/>; el cuerpo queda entonces como la explicación de
/// cómo se llena.
///
/// La aplicación NUNCA ejecuta lo que aquí se guarda: un script solo se copia o se guarda a disco.
/// </summary>
public class Template
{
    public int Id { get; set; }

    public TemplateKind Kind { get; set; } = TemplateKind.Otro;

    public string Title { get; set; } = "";

    /// <summary>Cuándo usarla / qué hay que revisar antes de mandarla. Se ve en la vista previa.</summary>
    public string? Description { get; set; }

    /// <summary>El texto reutilizable en sí. Puede venir vacío si la plantilla es solo un archivo.</summary>
    public string Body { get; set; } = "";

    /// <summary>Etiquetas separadas por coma, para buscar («cierre, cliente, urgente»).</summary>
    public string? Tags { get; set; }

    /// <summary>Archivada = fuera de la lista del día a día, pero sin perderla.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Archivo adjunto (p. ej. el .docx de la estimación). Null si la plantilla es solo texto.</summary>
    public byte[]? FileBytes { get; set; }

    public string? FileName { get; set; }

    /// <summary>Cuántas veces se ha copiado. Sirve para subir arriba lo que de verdad se usa.</summary>
    public int UsageCount { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
