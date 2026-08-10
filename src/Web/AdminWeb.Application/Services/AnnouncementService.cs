using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Comunicados del administrador al equipo: se entregan como AVISOS (notificaciones) por usuario, así
/// aparecen en la bandeja de Avisos y en el contador del menú. Solo el administrador.
/// </summary>
/// <param name="avisos">Opcional a propósito: las pruebas comprueban que el comunicado se CREA, y
/// obligarlas a montar el envío push no probaría nada más. En la aplicación siempre viene puesto.</param>
public class AnnouncementService(
    AppDbContext db, ICurrentUser currentUser, AuditService audit, NotificationService? avisos = null)
{
    /// <summary><see cref="TieneCuenta"/> = tiene una cuenta de usuario activa; solo ésos reciben el
    /// aviso (un desarrollador sin login no puede recibir una notificación in-app).</summary>
    public sealed record Destinatario(int DeveloperId, string Nombre, bool TieneCuenta);

    /// <summary>TODOS los desarrolladores activos, marcando cuáles tienen cuenta (los que sí reciben el
    /// aviso). Se listan todos para que el administrador vea al equipo completo. Solo administrador.</summary>
    public async Task<List<Destinatario>> DestinatariosPosiblesAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);
        var conCuenta = (await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.DeveloperId != null)
            .Select(u => u.DeveloperId!.Value)
            .ToListAsync(ct))
            .ToHashSet();
        return (await db.Developers.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.FullName)
            .Select(d => new { d.Id, d.FullName })
            .ToListAsync(ct))
            .Select(d => new Destinatario(d.Id, d.FullName, conCuenta.Contains(d.Id)))
            .ToList();
    }

    /// <summary>
    /// Envía un comunicado a los desarrolladores indicados (por developerId). Devuelve a cuántos llegó
    /// realmente (los que tienen cuenta de usuario activa). Solo administrador.
    ///
    /// La entrega es ATÓMICA: todos los avisos se guardan en un solo SaveChanges, así un fallo a la
    /// mitad (p. ej. la BD se cae) no deja unos entregados y otros no —no se guarda nada— y el reintento
    /// no duplica. Los avisos ya se ven en la bandeja de cada uno; no se usa dedupeKey a propósito, para
    /// que el admin pueda mandar dos comunicados con el mismo texto si así lo quiere.
    /// </summary>
    public async Task<(bool ok, string mensaje, int enviados)> EnviarAsync(
        string? titulo, string? cuerpo, IReadOnlyCollection<int> developerIds, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);
        titulo = (titulo ?? "").Trim();
        cuerpo = (cuerpo ?? "").Trim();
        if (titulo.Length == 0) return (false, "El título del comunicado es obligatorio.", 0);
        if (cuerpo.Length == 0) return (false, "El mensaje del comunicado es obligatorio.", 0);
        if (developerIds.Count == 0) return (false, "Selecciona al menos un desarrollador.", 0);

        // Un aviso por desarrollador con cuenta activa (todos en el mismo lote, sin guardar aún).
        var nuevos = new List<Notification>();
        foreach (var devId in developerIds.Distinct())
        {
            var userId = await db.Users.Where(u => u.DeveloperId == devId && u.IsActive)
                .Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
            if (userId == null) continue;
            var aviso = new Notification
            {
                ForUserId = userId.Value, Kind = NotificationKind.Comunicado,
                Title = titulo, Message = cuerpo, CreatedAt = DateTime.UtcNow
            };
            db.Notifications.Add(aviso);
            nuevos.Add(aviso);
        }
        if (nuevos.Count == 0)
            return (false, "Ninguno de los seleccionados tiene cuenta de usuario activa; no se envió a nadie.", 0);

        try
        {
            await db.SaveChangesAsync(ct);   // atómico: todos los avisos o ninguno
        }
        catch (Exception ex)
        {
            // Rollback: nada se entregó. El contexto es de esta petición y muere con ella, así que
            // no hay que desanclar nada a mano — el reintento llega con un contexto limpio.
            return (false, $"No se pudo enviar el comunicado: {ex.InnerException?.Message ?? ex.Message}", 0);
        }

        // El empujón va DESPUÉS del guardado atómico. Sin esto un comunicado solo se veía al entrar
        // a la web, que para un comunicado —cuyo sentido es enterarse ahora— lo dejaba a medias.
        if (avisos != null) await avisos.EmpujarGuardadosAsync(nuevos, ct);

        // La bitácora deja constancia del comunicado y a cuántos llegó (sin volcar todo el cuerpo).
        await audit.RecordAsync(AuditAction.Create, "Announcement", null,
            $"Comunicado «{titulo}» enviado a {nuevos.Count} desarrollador(es).", ct);

        return (true, $"Comunicado enviado a {nuevos.Count} desarrollador(es).", nuevos.Count);
    }
}
