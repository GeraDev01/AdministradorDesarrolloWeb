using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Avisos in-app persistentes. Un aviso se dirige a un USUARIO (no a un desarrollador): así se lee
/// cuando esa persona inicia sesión, sin importar en qué equipo esté abierta la app.
///
/// <para><b>El aviso guardado es el que cuenta.</b> Además se empuja al navegador de esa persona
/// para que se entere sin tener la aplicación abierta —lo que en el escritorio hacía el globo de la
/// bandeja del sistema—, pero eso es un extra: si no hay llaves configuradas, si el navegador no dio
/// permiso o si la entrega falla, el aviso sigue ahí para cuando vuelva. Por eso el empujón no puede
/// hacer fallar a quien avisó.</para>
/// </summary>
public class NotificationService(AppDbContext db, AvisosPushService? push = null)
{
    public Task<int> CountUnreadAsync(int userId, CancellationToken ct = default) =>
        db.Notifications.CountAsync(n => n.ForUserId == userId && n.ReadAt == null, ct);

    public Task<List<Notification>> RecentAsync(int userId, int take = 100, CancellationToken ct = default) =>
        db.Notifications.Where(n => n.ForUserId == userId)
            .OrderByDescending(n => n.CreatedAt).Take(take).ToListAsync(ct);

    /// <summary>
    /// Crea un aviso para un usuario. Con <paramref name="dedupeKey"/> no lo repite si ya existe
    /// uno con la misma clave para ese usuario. Devuelve el aviso creado, o null si se dedujo.
    ///
    /// La clave de deduplicación importa MÁS en la web que en el escritorio: allí un solo proceso
    /// generaba los avisos; aquí puede haber varias instancias de la API —o varias peticiones
    /// simultáneas— intentando avisar de lo mismo a la vez. Sin la clave, el mismo hecho se
    /// notificaría tantas veces como ejecutores lo detecten.
    /// </summary>
    public async Task<Notification?> NotifyAsync(int forUserId, NotificationKind kind, string title, string message,
        string? url = null, string? dedupeKey = null, CancellationToken ct = default)
    {
        if (dedupeKey != null &&
            await db.Notifications.AnyAsync(n => n.ForUserId == forUserId && n.DedupeKey == dedupeKey, ct))
            return null;

        var n = new Notification
        {
            ForUserId = forUserId, Kind = kind, Title = title, Message = message,
            Url = url, DedupeKey = dedupeKey, CreatedAt = DateTime.UtcNow
        };
        db.Notifications.Add(n);
        await db.SaveChangesAsync(ct);

        // DESPUÉS de guardar, y sin dejar que un fallo suyo se propague: lo que importa ya está en
        // la base. Es opcional en el constructor para que las pruebas de los servicios que avisan no
        // tengan que montar el envío push solo para comprobar que se creó el aviso.
        if (push != null)
            await push.EmpujarAsync(forUserId, title, message, url, ct);

        return n;
    }

    /// <summary>
    /// Manda al navegador los avisos que otro servicio ya guardó por su cuenta.
    ///
    /// <para><b>Por qué existe.</b> Tres servicios —compromisos de entrega, comunicados y la
    /// detección de asignados de Freshdesk— insertan sus avisos EN LOTE, con su propia
    /// deduplicación y, en el caso de Freshdesk, en la misma transacción que la marca de «ya lo
    /// vi». Pasarlos por <see cref="NotifyAsync"/> habría partido ese lote en un
    /// <c>SaveChanges</c> por aviso y, en Freshdesk, roto la atomicidad entre el aviso y su marca:
    /// un fallo a media lista habría dejado tickets marcados como avisados sin haber avisado.</para>
    ///
    /// <para>Así que lo que les faltaba no era la ruta entera, era solo el empujón. Sin esto sus
    /// avisos aparecían en el centro de avisos al entrar, pero nunca llegaban al navegador cerrado
    /// —justo lo que en el escritorio hacía el globo de la bandeja—.</para>
    ///
    /// <para>Se llama DESPUÉS de guardar y traga sus propios fallos: lo que importa ya está en la
    /// base, y un push que no sale no puede tumbar la operación que lo originó.</para>
    /// </summary>
    public async Task EmpujarGuardadosAsync(
        IEnumerable<Notification> avisos, CancellationToken ct = default)
    {
        if (push == null) return;

        foreach (var aviso in avisos)
        {
            try
            {
                await push.EmpujarAsync(aviso.ForUserId, aviso.Title, aviso.Message, aviso.Url, ct);
            }
            catch
            {
                // Uno que falle no puede dejar sin empujón a los demás de la lista.
            }
        }
    }

    /// <summary>
    /// Avisa al desarrollador, resolviendo su cuenta de usuario. Devuelve true si se creó el aviso
    /// (false si el desarrollador no tiene cuenta activa o si se dedujo).
    /// </summary>
    public async Task<bool> NotifyDeveloperAsync(int developerId, NotificationKind kind, string title, string message,
        string? url = null, string? dedupeKey = null, CancellationToken ct = default)
    {
        var userId = await db.Users
            .Where(u => u.DeveloperId == developerId && u.IsActive)
            .Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
        if (userId == null) return false;
        return await NotifyAsync(userId.Value, kind, title, message, url, dedupeKey, ct) != null;
    }

    public async Task MarkReadAsync(int notificationId, CancellationToken ct = default)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == notificationId, ct);
        if (n != null && n.ReadAt == null) { n.ReadAt = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
    }

    public async Task MarkAllReadAsync(int userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var pend = await db.Notifications.Where(n => n.ForUserId == userId && n.ReadAt == null).ToListAsync(ct);
        foreach (var n in pend) n.ReadAt = now;
        if (pend.Count > 0) await db.SaveChangesAsync(ct);
    }
}
