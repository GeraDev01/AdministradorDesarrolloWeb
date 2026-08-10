using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Avisos in-app: creación, deduplicación y resolución desarrollador→usuario.
///
/// Las de «me asignaron un ticket en DevOps» (<c>DetectarAsignadosNuevos</c>) NO están aquí: viven
/// con su servicio, en las pruebas de la integración con Azure DevOps. Es donde tienen sentido —
/// necesitan el doble del cliente— y donde alguien que toque esa lógica las va a encontrar.
/// </summary>
public class NotificationTests
{
    private static (AppDbContext db, NotificationService notif) Nuevo()
    {
        var db = TestDb.New();
        return (db, new NotificationService(db));
    }

    private static Developer SeedDevConUsuario(AppDbContext db, string name, string email, out int userId)
    {
        var dev = new Developer { FullName = name, Email = email, IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();
        var u = new User { Username = email, Role = UserRole.Desarrollador, DeveloperId = dev.Id, IsActive = true, PasswordHash = "x" };
        db.Users.Add(u); db.SaveChanges();
        userId = u.Id;
        return dev;
    }

    [Fact]
    public async Task Notify_Deduplica_PorClave()
    {
        var (_, notif) = Nuevo();
        Assert.NotNull(await notif.NotifyAsync(7, NotificationKind.General, "Hola", "1", dedupeKey: "k1"));
        Assert.Null(await notif.NotifyAsync(7, NotificationKind.General, "Hola otra vez", "2", dedupeKey: "k1"));
        Assert.Equal(1, await notif.CountUnreadAsync(7));
    }

    [Fact]
    public async Task NotifyDeveloper_ResuelveLaCuentaDelDesarrollador()
    {
        var (db, notif) = Nuevo();
        SeedDevConUsuario(db, "Angel Palma", "angel@x.com", out var userId);
        var dev = db.Developers.First();

        Assert.True(await notif.NotifyDeveloperAsync(dev.Id, NotificationKind.RequirementAssigned, "Asignado", "#1"));
        Assert.Equal(1, await notif.CountUnreadAsync(userId));
    }

    [Fact]
    public async Task NotifyDeveloper_SinCuenta_NoRompeYDevuelveFalse()
    {
        var (db, notif) = Nuevo();
        var dev = new Developer { FullName = "Sin cuenta", Email = "sc@x.com", IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();

        Assert.False(await notif.NotifyDeveloperAsync(dev.Id, NotificationKind.General, "x", "y"));
    }

    [Fact]
    public async Task MarkAllRead_DejaElContadorEnCero()
    {
        var (_, notif) = Nuevo();
        await notif.NotifyAsync(5, NotificationKind.General, "a", "1");
        await notif.NotifyAsync(5, NotificationKind.General, "b", "2");
        Assert.Equal(2, await notif.CountUnreadAsync(5));
        await notif.MarkAllReadAsync(5);
        Assert.Equal(0, await notif.CountUnreadAsync(5));
    }

    [Fact]
    public async Task MarkRead_MarcaSoloElAviso_YEsIdempotente()
    {
        var (db, notif) = Nuevo();
        var a = await notif.NotifyAsync(5, NotificationKind.General, "a", "1");
        await notif.NotifyAsync(5, NotificationKind.General, "b", "2");

        await notif.MarkReadAsync(a!.Id);
        await notif.MarkReadAsync(a.Id);   // repetir no debe romper ni cambiar la fecha

        Assert.Equal(1, await notif.CountUnreadAsync(5));
        Assert.NotNull(db.Notifications.AsNoTracking().Single(n => n.Id == a.Id).ReadAt);
    }
}
