using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Comunicados del administrador al equipo: se entregan como AVISOS (notificaciones) por usuario, así
/// aparecen en la bandeja de Avisos y en el globo de la bandeja del sistema. Solo el administrador.
/// </summary>
public class AnnouncementService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly AuditService _audit;

    public AnnouncementService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
    {
        _db = db; _currentUser = currentUser; _audit = audit;
    }

    /// <summary><see cref="TieneCuenta"/> = tiene una cuenta de usuario activa; solo ésos reciben el
    /// aviso (un desarrollador sin login no puede recibir una notificación in-app).</summary>
    public sealed record Destinatario(int DeveloperId, string Nombre, bool TieneCuenta);

    /// <summary>TODOS los desarrolladores activos, marcando cuáles tienen cuenta (los que sí reciben el
    /// aviso). Se listan todos para que el administrador vea al equipo completo. Solo administrador.</summary>
    public List<Destinatario> DestinatariosPosibles()
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        var conCuenta = _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.DeveloperId != null)
            .Select(u => u.DeveloperId!.Value)
            .ToHashSet();
        return _db.Developers.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.FullName)
            .Select(d => new { d.Id, d.FullName })
            .ToList()
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
    public (bool ok, string mensaje, int enviados) Enviar(string? titulo, string? cuerpo, IReadOnlyCollection<int> developerIds)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        titulo = (titulo ?? "").Trim();
        cuerpo = (cuerpo ?? "").Trim();
        if (titulo.Length == 0) return (false, "El título del comunicado es obligatorio.", 0);
        if (cuerpo.Length == 0) return (false, "El mensaje del comunicado es obligatorio.", 0);
        if (developerIds.Count == 0) return (false, "Selecciona al menos un desarrollador.", 0);

        // Un aviso por desarrollador con cuenta activa (todos en el mismo lote, sin guardar aún).
        var nuevos = new List<Notification>();
        foreach (var devId in developerIds.Distinct())
        {
            var userId = _db.Users.Where(u => u.DeveloperId == devId && u.IsActive)
                .Select(u => (int?)u.Id).FirstOrDefault();
            if (userId == null) continue;
            var aviso = new Notification
            {
                ForUserId = userId.Value, Kind = NotificationKind.Comunicado,
                Title = titulo, Message = cuerpo, CreatedAt = DateTime.UtcNow
            };
            _db.Notifications.Add(aviso);
            nuevos.Add(aviso);
        }
        if (nuevos.Count == 0)
            return (false, "Ninguno de los seleccionados tiene cuenta de usuario activa; no se envió a nadie.", 0);

        try
        {
            _db.SaveChanges();   // atómico: todos los avisos o ninguno
        }
        catch (Exception ex)
        {
            // Rollback: nada se entregó. Se despegan del contexto (singleton) para que el reintento no duplique.
            foreach (var n in nuevos) _db.Entry(n).State = EntityState.Detached;
            return (false, $"No se pudo enviar el comunicado: {ex.InnerException?.Message ?? ex.Message}", 0);
        }

        // La bitácora deja constancia del comunicado y a cuántos llegó (sin volcar todo el cuerpo).
        _audit.Record(AuditAction.Create, "Announcement", null,
            $"Comunicado «{titulo}» enviado a {nuevos.Count} desarrollador(es).");

        return (true, $"Comunicado enviado a {nuevos.Count} desarrollador(es).", nuevos.Count);
    }
}
