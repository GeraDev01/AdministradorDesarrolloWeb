namespace AdminWeb.Shared.Enums;

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
