using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Avisos in-app persistentes. Un aviso se dirige a un USUARIO (no a un desarrollador): así se lee
/// cuando esa persona inicia sesión, sin importar en qué equipo esté abierta la app.
/// </summary>
public class NotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db) { _db = db; }

    public int CountUnread(int userId) =>
        _db.Notifications.Count(n => n.ForUserId == userId && n.ReadAt == null);

    public List<Notification> Recent(int userId, int take = 100) =>
        _db.Notifications.Where(n => n.ForUserId == userId)
            .OrderByDescending(n => n.CreatedAt).Take(take).ToList();

    /// <summary>
    /// Crea un aviso para un usuario. Con <paramref name="dedupeKey"/> no lo repite si ya existe
    /// uno con la misma clave para ese usuario. Devuelve el aviso creado, o null si se dedujo.
    /// </summary>
    public Notification? Notify(int forUserId, NotificationKind kind, string title, string message,
        string? url = null, string? dedupeKey = null)
    {
        if (dedupeKey != null && _db.Notifications.Any(n => n.ForUserId == forUserId && n.DedupeKey == dedupeKey))
            return null;

        var n = new Notification
        {
            ForUserId = forUserId, Kind = kind, Title = title, Message = message,
            Url = url, DedupeKey = dedupeKey, CreatedAt = DateTime.UtcNow
        };
        _db.Notifications.Add(n);
        _db.SaveChanges();
        return n;
    }

    /// <summary>
    /// Avisa al desarrollador, resolviendo su cuenta de usuario. Devuelve true si se creó el aviso
    /// (false si el desarrollador no tiene cuenta activa o si se dedujo).
    /// </summary>
    public bool NotifyDeveloper(int developerId, NotificationKind kind, string title, string message,
        string? url = null, string? dedupeKey = null)
    {
        var userId = _db.Users
            .Where(u => u.DeveloperId == developerId && u.IsActive)
            .Select(u => (int?)u.Id).FirstOrDefault();
        if (userId == null) return false;
        return Notify(userId.Value, kind, title, message, url, dedupeKey) != null;
    }

    public void MarkRead(int notificationId)
    {
        var n = _db.Notifications.Find(notificationId);
        if (n != null && n.ReadAt == null) { n.ReadAt = DateTime.UtcNow; _db.SaveChanges(); }
    }

    public void MarkAllRead(int userId)
    {
        var now = DateTime.UtcNow;
        var pend = _db.Notifications.Where(n => n.ForUserId == userId && n.ReadAt == null).ToList();
        foreach (var n in pend) n.ReadAt = now;
        if (pend.Count > 0) _db.SaveChanges();
    }
}
