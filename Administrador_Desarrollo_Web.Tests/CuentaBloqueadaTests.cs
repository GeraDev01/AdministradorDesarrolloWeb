using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Security;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Bloqueo por intentos fallidos y su levantamiento manual.
///
/// El bloqueo automático ya existía; lo que se prueba aquí es que el administrador pueda quitarlo
/// sin esperar los 15 minutos, y que al hacerlo la cuenta quede REALMENTE utilizable —no basta con
/// borrar la fecha si el contador de intentos se queda a un fallo de volver a bloquearla.
/// </summary>
public class CuentaBloqueadaTests
{
    private const string Clave = "Password123!";

    private static AuthService Como(AppDbContext db, UserRole rol, int userId)
    {
        var cu = Ctx.As(rol, developerId: null, userId: userId);
        return new AuthService(db, cu, new AuditService(db, cu));
    }

    private static User CrearUsuario(AppDbContext db, string username = "ana")
    {
        var u = new User
        {
            Username = username, FullName = username, Role = UserRole.Desarrollador,
            IsActive = true, PasswordHash = PasswordHasher.Hash(Clave), CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(u); db.SaveChanges();
        return u;
    }

    private static void FallarHastaBloquear(AuthService auth, string username)
    {
        for (int i = 0; i < AuthService.MaxFailedAttempts; i++)
            auth.Login(username, "clave-incorrecta");
    }

    [Fact]
    public void CincoFallos_BloqueanLaCuenta()
    {
        var db = TestDb.New();
        var u = CrearUsuario(db);
        var auth = Como(db, UserRole.Desarrollador, 500);

        FallarHastaBloquear(auth, u.Username);

        var fresco = db.Users.AsNoTracking().Single();
        Assert.True(AuthService.EstaBloqueado(fresco));
        // El contador se reinicia al bloquear: si quedara en 5, al vencer el plazo el primer error
        // volvería a bloquear de inmediato.
        Assert.Equal(0, fresco.FailedLoginCount);

        // Con la contraseña BUENA tampoco entra mientras dure el bloqueo.
        var (ok, mensaje, _) = auth.Login(u.Username, Clave);
        Assert.False(ok);
        Assert.Contains("bloqueada", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Desbloquear_DejaEntrarDeInmediato()
    {
        var db = TestDb.New();
        var u = CrearUsuario(db);
        FallarHastaBloquear(Como(db, UserRole.Desarrollador, 500), u.Username);

        var (ok, mensaje) = Como(db, UserRole.Admin, 900).DesbloquearCuenta(u.Id);

        Assert.True(ok);
        Assert.Contains("desbloqueada", mensaje, StringComparison.OrdinalIgnoreCase);

        var fresco = db.Users.AsNoTracking().Single();
        Assert.Null(fresco.LockoutUntil);
        Assert.Equal(0, fresco.FailedLoginCount);

        var (entra, _, usuario) = Como(db, UserRole.Desarrollador, 500).Login(u.Username, Clave);
        Assert.True(entra);
        Assert.NotNull(usuario);
    }

    /// <summary>Un bloqueo levantado a medias volvería a saltar al primer error: el contador de
    /// intentos tiene que quedar en cero, no solo la fecha.</summary>
    [Fact]
    public void Desbloquear_TambienReiniciaElContadorDeIntentos()
    {
        var db = TestDb.New();
        var u = CrearUsuario(db);
        var auth = Como(db, UserRole.Desarrollador, 500);

        // Cuatro fallos: todavía no bloquea, pero deja el contador cargado.
        for (int i = 0; i < AuthService.MaxFailedAttempts - 1; i++) auth.Login(u.Username, "mal");
        Assert.Equal(AuthService.MaxFailedAttempts - 1, db.Users.AsNoTracking().Single().FailedLoginCount);

        var (ok, _) = Como(db, UserRole.Admin, 900).DesbloquearCuenta(u.Id);

        Assert.True(ok);   // no estaba bloqueada, pero sí había algo que limpiar
        Assert.Equal(0, db.Users.AsNoTracking().Single().FailedLoginCount);
    }

    [Fact]
    public void Desbloquear_SinNadaQueLevantar_NoMienteDiciendoQueSi()
    {
        var db = TestDb.New();
        var u = CrearUsuario(db);

        var (ok, mensaje) = Como(db, UserRole.Admin, 900).DesbloquearCuenta(u.Id);

        Assert.False(ok);
        Assert.Contains("no está bloqueado", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(UserRole.Desarrollador)]
    [InlineData(UserRole.Operaciones)]
    public void Desbloquear_SoloAdministrador(UserRole rol)
    {
        var db = TestDb.New();
        var u = CrearUsuario(db);
        FallarHastaBloquear(Como(db, UserRole.Desarrollador, 500), u.Username);

        Assert.Throws<AuthorizationException>(() => Como(db, rol, 501).DesbloquearCuenta(u.Id));

        // Y sigue bloqueada: el intento fallido no puede dejar la cuenta a medias.
        Assert.True(AuthService.EstaBloqueado(db.Users.AsNoTracking().Single()));
    }

    [Fact]
    public void Desbloquear_UsuarioInexistente_NoRevienta()
    {
        var db = TestDb.New();
        var (ok, mensaje) = Como(db, UserRole.Admin, 900).DesbloquearCuenta(4242);

        Assert.False(ok);
        Assert.Contains("no encontrado", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Entrar bien limpia el rastro: si no, el contador se arrastraría entre sesiones y
    /// cinco despistes repartidos en semanas acabarían bloqueando a alguien que sí sabe su clave.</summary>
    [Fact]
    public void LoginCorrecto_LimpiaElContadorDeIntentos()
    {
        var db = TestDb.New();
        var u = CrearUsuario(db);
        var auth = Como(db, UserRole.Desarrollador, 500);

        auth.Login(u.Username, "mal");
        auth.Login(u.Username, "mal");
        Assert.Equal(2, db.Users.AsNoTracking().Single().FailedLoginCount);

        Assert.True(auth.Login(u.Username, Clave).success);
        Assert.Equal(0, db.Users.AsNoTracking().Single().FailedLoginCount);
    }
}
