using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Avisos in-app: creación, deduplicación, resolución desarrollador→usuario, y la detección de
/// «me asignaron un ticket en DevOps» con su línea base (para no soltar un aluvión la primera vez).
/// </summary>
public class NotificationTests
{
    private static (AzureDevOpsService svc, AppDbContext db, NotificationService notif) Nuevo()
    {
        var db = TestDb.New();
        var user = Ctx.As(UserRole.Admin);
        var settings = new SettingsService(db, new AuditService(db, user));
        var notif = new NotificationService(db);
        return (new AzureDevOpsService(settings, db, new AuditService(db, user), notif), db, notif);
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

    private static void SeedTicket(AppDbContext db, int externalId, string state, string display, string email)
        => db.DevOpsTickets.Add(new DevOpsTicket
        {
            ExternalId = externalId, Title = $"WI {externalId}", WorkItemType = "Task",
            State = state, AssignedTo = display, AssignedToUniqueName = email, Url = $"http://d/{externalId}"
        });

    [Fact]
    public void Notify_Deduplica_PorClave()
    {
        var (_, db, notif) = Nuevo();
        Assert.NotNull(notif.Notify(7, NotificationKind.General, "Hola", "1", dedupeKey: "k1"));
        Assert.Null(notif.Notify(7, NotificationKind.General, "Hola otra vez", "2", dedupeKey: "k1"));
        Assert.Equal(1, notif.CountUnread(7));
    }

    [Fact]
    public void NotifyDeveloper_ResuelveLaCuentaDelDesarrollador()
    {
        var (_, db, notif) = Nuevo();
        SeedDevConUsuario(db, "Angel Palma", "angel@x.com", out var userId);
        var dev = db.Developers.First();

        Assert.True(notif.NotifyDeveloper(dev.Id, NotificationKind.RequirementAssigned, "Asignado", "#1"));
        Assert.Equal(1, notif.CountUnread(userId));
    }

    [Fact]
    public void NotifyDeveloper_SinCuenta_NoRompeYDevuelveFalse()
    {
        var (_, db, notif) = Nuevo();
        var dev = new Developer { FullName = "Sin cuenta", Email = "sc@x.com", IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();

        Assert.False(notif.NotifyDeveloper(dev.Id, NotificationKind.General, "x", "y"));
    }

    [Fact]
    public void MarkAllRead_DejaElContadorEnCero()
    {
        var (_, db, notif) = Nuevo();
        notif.Notify(5, NotificationKind.General, "a", "1");
        notif.Notify(5, NotificationKind.General, "b", "2");
        Assert.Equal(2, notif.CountUnread(5));
        notif.MarkAllRead(5);
        Assert.Equal(0, notif.CountUnread(5));
    }

    [Fact]
    public void DetectarAsignadosNuevos_PrimeraVez_FijaLineaBaseSinAvisar()
    {
        var (svc, db, notif) = Nuevo();
        var dev = SeedDevConUsuario(db, "Angel Palma", "angel@x.com", out var userId);
        SeedTicket(db, 100, "Active", "Angel Palma", "angel@x.com");
        SeedTicket(db, 101, "Active", "Angel Palma", "angel@x.com");
        db.SaveChanges();

        var nuevos = svc.DetectarAsignadosNuevos(userId, dev);

        Assert.Empty(nuevos);                       // no avisa del backlog inicial
        Assert.Equal(0, notif.CountUnread(userId));
    }

    [Fact]
    public void DetectarAsignadosNuevos_TrasLaBase_AvisaSoloDeLoNuevo()
    {
        var (svc, db, notif) = Nuevo();
        var dev = SeedDevConUsuario(db, "Angel Palma", "angel@x.com", out var userId);
        SeedTicket(db, 100, "Active", "Angel Palma", "angel@x.com");
        db.SaveChanges();
        svc.DetectarAsignadosNuevos(userId, dev);   // establece la línea base

        // Llega un ticket nuevo asignado a mí.
        SeedTicket(db, 200, "Active", "Angel Palma", "angel@x.com");
        db.SaveChanges();

        var nuevos = svc.DetectarAsignadosNuevos(userId, dev);

        Assert.Single(nuevos);
        Assert.Equal(200, nuevos[0].ExternalId);
        Assert.Equal(1, notif.CountUnread(userId));

        // Y no lo repite en la siguiente pasada.
        Assert.Empty(svc.DetectarAsignadosNuevos(userId, dev));
        Assert.Equal(1, notif.CountUnread(userId));
    }

    [Fact]
    public void DetectarAsignadosNuevos_IgnoraLosCerrados()
    {
        var (svc, db, notif) = Nuevo();
        var dev = SeedDevConUsuario(db, "Angel Palma", "angel@x.com", out var userId);
        svc.DetectarAsignadosNuevos(userId, dev);   // base vacía

        SeedTicket(db, 300, "Done", "Angel Palma", "angel@x.com");   // cerrado: no debe avisar
        db.SaveChanges();

        Assert.Empty(svc.DetectarAsignadosNuevos(userId, dev));
        Assert.Equal(0, notif.CountUnread(userId));
    }
}
