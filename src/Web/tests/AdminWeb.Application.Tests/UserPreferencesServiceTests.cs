using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Las preferencias de interfaz, que en la web viven en la base para que sigan a la persona entre
/// equipos (en el escritorio eran un archivo por máquina y se perdían al cambiar de computadora).
/// </summary>
public class UserPreferencesServiceTests
{
    private static UserPreferencesService Svc(AppDbContext db, ICurrentUser usuario) => new(db, usuario);

    private static User CrearUsuario(AppDbContext db, int id, string nombre)
    {
        var u = new User { Id = id, Username = nombre, FullName = nombre, PasswordHash = "x", IsActive = true };
        db.Users.Add(u);
        db.SaveChanges();
        return u;
    }

    [Fact]
    public async Task SinPreferenciaGuardada_DevuelveNulo()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");

        // No es un error: es alguien que todavía no ha tocado nada, que es el caso normal.
        Assert.Null(await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador)).LeerAsync("columnas.pool"));
    }

    [Fact]
    public async Task GuardarYLeer_DevuelveLoGuardado()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador));

        var (ok, _) = await svc.GuardarAsync("columnas.pool", "[\"Detalle\"]");

        Assert.True(ok);
        Assert.Equal("[\"Detalle\"]", await svc.LeerAsync("columnas.pool"));
    }

    [Fact]
    public async Task GuardarDosVeces_ReemplazaEnVezDeDuplicar()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador));

        await svc.GuardarAsync("columnas.pool", "[\"A\"]");
        await svc.GuardarAsync("columnas.pool", "[\"B\"]");

        Assert.Equal("[\"B\"]", await svc.LeerAsync("columnas.pool"));
        Assert.Single(db.UserPreferences.AsNoTracking());
    }

    [Fact]
    public async Task GuardarVacio_BorraLaPreferencia()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador));
        await svc.GuardarAsync("columnas.pool", "[\"A\"]");

        var (ok, _) = await svc.GuardarAsync("columnas.pool", null);

        // Borrar y no guardar una lista vacía es lo que hace que una columna nueva le aparezca a
        // quien restableció su configuración.
        Assert.True(ok);
        Assert.Empty(db.UserPreferences.AsNoTracking());
        Assert.Null(await svc.LeerAsync("columnas.pool"));
    }

    [Fact]
    public async Task LasPreferenciasDeCadaQuien_SonSuyas()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        CrearUsuario(db, 2, "beto");

        await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 1))
            .GuardarAsync("columnas.pool", "[\"lo de Ana\"]");

        // Beto no ve lo de Ana. No hay parámetro de usuario en la firma justamente para que esto no
        // pueda romperse desde un llamador futuro.
        var deBeto = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 2))
            .LeerAsync("columnas.pool");

        Assert.Null(deBeto);
    }

    [Fact]
    public async Task SinSesion_NoSePuedeLeerNiGuardar()
    {
        var db = TestDb.New();
        var svc = Svc(db, UsuarioDePrueba.Anonimo());

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.LeerAsync("columnas.pool"));
        await Assert.ThrowsAsync<AuthorizationException>(() => svc.GuardarAsync("columnas.pool", "[]"));
    }

    [Fact]
    public async Task ClaveVacia_SeRechaza()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");

        var (ok, mensaje) = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador))
            .GuardarAsync("   ", "[]");

        Assert.False(ok);
        Assert.Contains("clave", mensaje);
    }

    [Fact]
    public async Task BorrarLaCuenta_SeLlevaSusPreferencias()
    {
        var db = TestDb.New();
        var u = CrearUsuario(db, 1, "ana");
        await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador)).GuardarAsync("columnas.pool", "[\"A\"]");

        db.Users.Remove(u);
        await db.SaveChangesAsync();

        // En cascada a propósito: una preferencia sin dueño no significa nada. Es lo contrario de la
        // bitácora o la asistencia, que son histórico y sobreviven a la baja de la cuenta.
        Assert.Empty(db.UserPreferences.AsNoTracking());
    }
}
