using System.Text.Json;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Application.Services;

/// <summary>
/// De dónde salió una operación. En el escritorio era «MÁQUINA\usuario» porque cada quien corría su
/// propio ejecutable; en la web todo sale del mismo servidor, así que lo que identifica al cliente
/// es su IP y su navegador. Lo provee la API por petición.
/// </summary>
public interface IRequestOrigin
{
    string Describir();
}

/// <summary>
/// La bitácora. Copiada del escritorio con dos cambios: es async (todo lo es en la API) y el origen
/// se inyecta en vez de leerse de <c>Environment.MachineName</c>, que en el servidor sería siempre
/// el mismo nombre y no diría nada.
/// </summary>
public class AuditService(AppDbContext db, ICurrentUser currentUser, IRequestOrigin origin)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public Task RecordAsync(AuditAction action, string? entityType = null,
        string? entityId = null, string? details = null, CancellationToken ct = default)
        => RegistrarAsync(action, entityType, entityId, details, AuditOutcome.Exito, null, null, null, ct);

    /// <summary>
    /// Registro completo, para lo que necesita servir de evidencia: qué había antes, qué quedó
    /// después, cómo terminó y a qué operación larga pertenece.
    /// </summary>
    public Task RecordDetailedAsync(
        AuditAction action,
        string entityType,
        string? entityId,
        string? details,
        AuditOutcome outcome = AuditOutcome.Exito,
        object? oldValues = null,
        object? newValues = null,
        string? correlationId = null,
        CancellationToken ct = default)
        => RegistrarAsync(action, entityType, entityId, details, outcome,
                          Serializar(oldValues), Serializar(newValues), correlationId, ct);

    /// <summary>
    /// Deja constancia de un intento RECHAZADO. Una bitácora que solo guarda lo que sí ocurrió no
    /// sirve para detectar a alguien intentando lo que no le toca.
    /// </summary>
    public Task RecordDeniedAsync(AuditAction action, string entityType, string? entityId, string motivo,
        string? correlationId = null, CancellationToken ct = default)
        => RegistrarAsync(action, entityType, entityId, motivo, AuditOutcome.Denegado, null, null, correlationId, ct);

    /// <summary>Operación del sistema (un job), sin usuario detrás.</summary>
    public Task RecordSystemAsync(AuditAction action, string? details = null, CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            UserName = "sistema",
            Action = action,
            Details = details,
            Origin = origin.Describir()
        });
        return db.SaveChangesAsync(ct);
    }

    private Task RegistrarAsync(AuditAction action, string? entityType, string? entityId, string? details,
        AuditOutcome outcome, string? oldJson, string? newJson, string? correlationId, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            UserId = currentUser.UserId,
            UserName = currentUser.Username ?? "sistema",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            Outcome = outcome,
            OldValues = oldJson,
            NewValues = newJson,
            CorrelationId = correlationId,
            Origin = origin.Describir()
        });
        return db.SaveChangesAsync(ct);
    }

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
