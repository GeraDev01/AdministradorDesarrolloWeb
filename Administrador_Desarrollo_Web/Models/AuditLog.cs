namespace Administrador_Desarrollo_Web.Models;

public enum AuditAction
{
    Login = 0,
    Logout = 1,
    Create = 2,
    Update = 3,
    Delete = 4,
    Deploy = 5,
    Backup = 6,
    PasswordChange = 7,
    ConfigChange = 8
}

/// <summary>Cómo terminó la operación registrada. Antes un intento fallido y uno exitoso se veían igual.</summary>
public enum AuditOutcome { Exito = 0, Fallo = 1, Denegado = 2 }

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

    // ── Campos para una bitácora que sirva de evidencia ──────────
    // Los cuatro nacieron de la misma carencia: con solo (quién, qué, texto libre) no se podía
    // reconstruir un despliegue ni demostrar quién cambió la configuración de un servidor.

    /// <summary>Estado de la entidad ANTES del cambio, en JSON. Null en altas y consultas.</summary>
    public string? OldValues { get; set; }

    /// <summary>Estado DESPUÉS del cambio, en JSON. Null en bajas.</summary>
    public string? NewValues { get; set; }

    /// <summary>Equipo y usuario de Windows desde donde se hizo. Responde "desde dónde salió esto".</summary>
    public string? Origin { get; set; }

    /// <summary>Resultado. Permite auditar también los intentos rechazados, no solo lo que sí pasó.</summary>
    public AuditOutcome Outcome { get; set; } = AuditOutcome.Exito;

    /// <summary>
    /// Agrupa todas las entradas de una misma operación larga (por ejemplo un despliegue completo:
    /// respaldo, subida a cada servidor y cierre). Sin esto, las filas quedaban sueltas.
    /// </summary>
    public string? CorrelationId { get; set; }
}
