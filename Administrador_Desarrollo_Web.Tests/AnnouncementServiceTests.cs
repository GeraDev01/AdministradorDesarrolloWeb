using System;
using System.Linq;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Comunicados del administrador: llegan como AVISOS a los desarrolladores con cuenta activa, solo el
/// admin puede enviarlos, y se validan título/cuerpo/destinatarios.
/// </summary>
public class AnnouncementServiceTests
{
    private static (AnnouncementService svc, AppDbContext db) Nuevo(UserRole rol)
    {
        var db = TestDb.New();
        var user = Ctx.As(rol);
        return (new AnnouncementService(db, user, new AuditService(db, user)), db);
    }

    private static int SeedDevConCuenta(AppDbContext db, string nombre, int userId, bool userActivo = true)
    {
        var dev = new Developer { FullName = nombre, IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();
        db.Users.Add(new User { Id = userId, Username = nombre, Role = UserRole.Desarrollador, DeveloperId = dev.Id, IsActive = userActivo });
        db.SaveChanges();
        return dev.Id;
    }

    [Fact]
    public void Admin_EnviaComunicado_LlegaComoAviso()
    {
        var (svc, db) = Nuevo(UserRole.Admin);
        int dev1 = SeedDevConCuenta(db, "Ana", 10);
        int dev2 = SeedDevConCuenta(db, "Beto", 11);

        var (ok, _, enviados) = svc.Enviar("Junta el viernes", "A las 10am en la sala.", new[] { dev1, dev2 });

        Assert.True(ok);
        Assert.Equal(2, enviados);
        var avisos = db.Notifications.ToList();
        Assert.Equal(2, avisos.Count);
        Assert.All(avisos, n => Assert.Equal(NotificationKind.Comunicado, n.Kind));
        Assert.All(avisos, n => Assert.Equal("Junta el viernes", n.Title));
        Assert.All(avisos, n => Assert.Equal("A las 10am en la sala.", n.Message));
        Assert.Contains(avisos, n => n.ForUserId == 10);
        Assert.Contains(avisos, n => n.ForUserId == 11);
    }

    [Fact]
    public void DesarrolladorSinCuentaActiva_NoRecibe()
    {
        var (svc, db) = Nuevo(UserRole.Admin);
        int dev1 = SeedDevConCuenta(db, "Ana", 10);
        int dev2 = SeedDevConCuenta(db, "Inactivo", 11, userActivo: false);

        var (ok, _, enviados) = svc.Enviar("Hola", "cuerpo", new[] { dev1, dev2 });

        Assert.True(ok);
        Assert.Equal(1, enviados);                 // solo Ana
        Assert.Single(db.Notifications);
    }

    [Fact]
    public void NoAdmin_EsRechazado()
    {
        var (svc, _) = Nuevo(UserRole.Desarrollador);
        Assert.Throws<AuthorizationException>(() => svc.Enviar("t", "c", new[] { 1 }));
        Assert.Throws<AuthorizationException>(() => svc.DestinatariosPosibles());
    }

    [Fact]
    public void TituloOCuerpoVacio_NoEnvia()
    {
        var (svc, db) = Nuevo(UserRole.Admin);
        int dev1 = SeedDevConCuenta(db, "Ana", 10);
        Assert.False(svc.Enviar("", "cuerpo", new[] { dev1 }).ok);
        Assert.False(svc.Enviar("titulo", "   ", new[] { dev1 }).ok);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public void SinDestinatarios_NoEnvia()
    {
        var (svc, _) = Nuevo(UserRole.Admin);
        Assert.False(svc.Enviar("t", "c", Array.Empty<int>()).ok);
    }

    [Fact]
    public void ElCuerpoLargo_NoSeVuelcaEnLaBitacora()
    {
        var (svc, db) = Nuevo(UserRole.Admin);
        int dev1 = SeedDevConCuenta(db, "Ana", 10);
        svc.Enviar("Aviso", "cuerpo-secreto-1234567890", new[] { dev1 });

        Assert.DoesNotContain(db.AuditLogs, a => (a.Details ?? "").Contains("cuerpo-secreto"));
        Assert.Contains(db.AuditLogs, a => a.EntityType == "Announcement");
    }

    [Fact]
    public void DestinatariosPosibles_ListaTodosLosActivos_MarcandoQuienTieneCuenta()
    {
        var (svc, db) = Nuevo(UserRole.Admin);
        SeedDevConCuenta(db, "Ana", 10);                        // con cuenta activa
        SeedDevConCuenta(db, "Beto", 11, userActivo: false);    // cuenta inactiva → no recibe
        db.Developers.Add(new Developer { FullName = "Caro", IsActive = true });    // sin cuenta
        db.Developers.Add(new Developer { FullName = "Zoe",  IsActive = false });   // inactivo → NO aparece
        db.SaveChanges();

        var lista = svc.DestinatariosPosibles();

        Assert.Equal(3, lista.Count);                          // Ana, Beto, Caro (todos los ACTIVOS)
        Assert.True(lista.Single(d => d.Nombre == "Ana").TieneCuenta);
        Assert.False(lista.Single(d => d.Nombre == "Beto").TieneCuenta);
        Assert.False(lista.Single(d => d.Nombre == "Caro").TieneCuenta);
        Assert.DoesNotContain(lista, d => d.Nombre == "Zoe");
    }
}
