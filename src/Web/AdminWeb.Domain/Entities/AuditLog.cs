using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

/// <summary>
/// Entrada de la bitácora. Misma tabla que el escritorio, con una diferencia de contenido: el campo
/// <see cref="Origin"/> guardaba «MÁQUINA\usuario» porque cada quien corría su propio .exe. En la
/// web todas las operaciones salen del mismo servidor, así que ahí va la IP y el agente del cliente
/// — que es la información equivalente y, de paso, lo que distingue a los dos sistemas mientras
/// convivan durante el corte.
/// </summary>
public class AuditLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int? UserId { get; set; }
    public string UserName { get; set; } = "";
    public AuditAction Action { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }

    /// <summary>Estado de la entidad ANTES del cambio, en JSON. Null en altas y consultas.</summary>
    public string? OldValues { get; set; }

    /// <summary>Estado DESPUÉS del cambio, en JSON. Null en bajas.</summary>
    public string? NewValues { get; set; }

    /// <summary>De dónde salió: en la web, IP + agente del navegador.</summary>
    public string? Origin { get; set; }

    public AuditOutcome Outcome { get; set; } = AuditOutcome.Exito;

    /// <summary>Agrupa todas las entradas de una misma operación larga (un despliegue completo).</summary>
    public string? CorrelationId { get; set; }
}
