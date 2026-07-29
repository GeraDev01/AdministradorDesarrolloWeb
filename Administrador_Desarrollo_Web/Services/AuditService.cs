using System.Text.Json;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Services;

public class AuditService
{
    private readonly AppDbContext _db;
    private readonly CurrentUserContext _currentUser;

    public AuditService(AppDbContext db, CurrentUserContext currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>Equipo y usuario de Windows: responde "desde dónde salió esto".</summary>
    private static string Origen()
    {
        try { return $"{Environment.MachineName}\\{Environment.UserName}"; }
        catch { return "(desconocido)"; }
    }

    public void Record(AuditAction action, string? entityType = null,
        string? entityId = null, string? details = null)
        => Registrar(action, entityType, entityId, details, AuditOutcome.Exito, null, null, null);

    /// <summary>
    /// Registro completo, para lo que necesita servir de evidencia: qué había antes, qué quedó
    /// después, cómo terminó y a qué operación larga pertenece.
    /// </summary>
    public void RecordDetailed(
        AuditAction action,
        string entityType,
        string? entityId,
        string? details,
        AuditOutcome outcome = AuditOutcome.Exito,
        object? oldValues = null,
        object? newValues = null,
        string? correlationId = null)
        => Registrar(action, entityType, entityId, details, outcome,
                     Serializar(oldValues), Serializar(newValues), correlationId);

    /// <summary>
    /// Deja constancia de un intento RECHAZADO. Una bitácora que solo guarda lo que sí ocurrió no
    /// sirve para detectar a alguien intentando lo que no le toca.
    /// </summary>
    public void RecordDenied(AuditAction action, string entityType, string? entityId, string motivo,
        string? correlationId = null)
        => Registrar(action, entityType, entityId, motivo, AuditOutcome.Denegado, null, null, correlationId);

    public void RecordSystem(AuditAction action, string? details = null)
    {
        var entry = new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            UserName = "sistema",
            Action = action,
            Details = details,
            Origin = Origen()
        };
        _db.AuditLogs.Add(entry);
        _db.SaveChanges();
    }

    private void Registrar(AuditAction action, string? entityType, string? entityId, string? details,
        AuditOutcome outcome, string? oldJson, string? newJson, string? correlationId)
    {
        var entry = new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            UserId = _currentUser.User?.Id,
            UserName = _currentUser.User?.Username ?? "sistema",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            Outcome = outcome,
            OldValues = oldJson,
            NewValues = newJson,
            CorrelationId = correlationId,
            Origin = Origen()
        };
        _db.AuditLogs.Add(entry);
        _db.SaveChanges();
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    /// <summary>Serializa el estado de una entidad para la bitácora, sin reventar si algo falla.</summary>
    private static string? Serializar(object? valores)
    {
        if (valores == null) return null;
        try { return JsonSerializer.Serialize(valores, JsonOpts); }
        catch (Exception ex) { return $"{{\"error\":\"no serializable: {ex.Message}\"}}"; }
    }

    /// <summary>Identificador para agrupar todas las entradas de una misma operación larga.</summary>
    public static string NuevaCorrelacion() => Guid.NewGuid().ToString("N")[..12];
}
