using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El acceso, ahora verificado en el servidor.
///
/// Lo que estas pruebas cuidan es la promesa del port: que **las contraseñas que ya existen sigan
/// funcionando** (mismos hashes BCrypt), que el bloqueo por intentos siga comportándose igual, y
/// que las dos reglas nuevas de la web —no fijar la sesión desde el servicio, e invalidar las
/// sesiones abiertas al cambiar la contraseña— hagan lo que dicen.
/// </summary>
public class AuthServiceTests
{
    private static User CrearUsuario(AppDbContext db, string usuario, string contrasena,
        UserRole rol = UserRole.Desarrollador, bool activo = true, int? developerId = null)
    {
        var u = new User
        {
            Username = usuario,
            FullName = usuario,
            PasswordHash = PasswordHasher.Hash(contrasena),
            Role = rol,
            IsActive = activo,
            DeveloperId = developerId
        };
        db.Users.Add(u);
        db.SaveChanges();
        return u;
    }

    // ── Lo que no puede cambiar con la mudanza ───────────────────────────────────

    [Fact]
    public async Task UnHashCreadoPorElEscritorio_SigueValidando()
    {
        var db = TestDb.New();
        // Hash generado con el MISMO algoritmo y factor de trabajo que usa el escritorio (BCrypt,
        // workFactor 12). Si alguien tocara PasswordHasher, esta prueba avisa antes de que todo el
        // equipo se quede fuera el día del corte.
        db.Users.Add(new User
        {
            Username = "ana",
            FullName = "Ana",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Contrasena123", workFactor: 12),
            Role = UserRole.Desarrollador,
            IsActive = true
        });
        db.SaveChanges();

        var r = await Fabrica.Auth(db).LoginAsync("ana", "Contrasena123");

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal("ana", r.Usuario!.Username);
    }

    [Fact]
    public async Task ContrasenaIncorrecta_NoEntraYCuentaElIntento()
    {
        var db = TestDb.New();
        CrearUsuario(db, "ana", "Contrasena123");

        var r = await Fabrica.Auth(db).LoginAsync("ana", "otra");

        Assert.False(r.Exito);
        Assert.Equal(1, db.Users.AsNoTracking().Single().FailedLoginCount);
    }

    [Fact]
    public async Task UsuarioInexistente_DaElMismoMensajeQueContrasenaIncorrecta()
    {
        var db = TestDb.New();
        CrearUsuario(db, "ana", "Contrasena123");
        var svc = Fabrica.Auth(db);

        var sinUsuario = await svc.LoginAsync("nadie", "loquesea");
        var malaClave = await svc.LoginAsync("ana", "otra");

        // Distinguirlos le diría a un desconocido qué nombres de usuario existen.
        Assert.Equal(sinUsuario.Mensaje, malaClave.Mensaje);
    }

    [Fact]
    public async Task CuentaInactiva_NoEntra()
    {
        var db = TestDb.New();
        CrearUsuario(db, "ana", "Contrasena123", activo: false);

        Assert.False((await Fabrica.Auth(db).LoginAsync("ana", "Contrasena123")).Exito);
    }

    [Fact]
    public async Task AlQuintoIntentoFallido_SeBloqueaLaCuenta()
    {
        var db = TestDb.New();
        CrearUsuario(db, "ana", "Contrasena123");
        var svc = Fabrica.Auth(db);

        for (int i = 0; i < AuthService.MaxFailedAttempts; i++)
            await svc.LoginAsync("ana", "mal");

        var u = db.Users.AsNoTracking().Single();
        Assert.True(AuthService.EstaBloqueado(u));
        // El contador se reinicia al bloquear: al vencer el bloqueo se vuelve a tener margen.
        Assert.Equal(0, u.FailedLoginCount);

        // Y con la contraseña BUENA tampoco entra mientras dure el bloqueo.
        var r = await svc.LoginAsync("ana", "Contrasena123");
        Assert.False(r.Exito);
        Assert.Contains("bloqueada", r.Mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginCorrecto_LimpiaLosIntentosFallidosPrevios()
    {
        var db = TestDb.New();
        CrearUsuario(db, "ana", "Contrasena123");
        var svc = Fabrica.Auth(db);
        await svc.LoginAsync("ana", "mal");
        await svc.LoginAsync("ana", "mal");

        Assert.True((await svc.LoginAsync("ana", "Contrasena123")).Exito);
        Assert.Equal(0, db.Users.AsNoTracking().Single().FailedLoginCount);
    }

    // ── Lo que es nuevo de la web ────────────────────────────────────────────────

    [Fact]
    public async Task Login_NoFijaLaSesion_SoloDevuelveElUsuario()
    {
        var db = TestDb.New();
        CrearUsuario(db, "ana", "Contrasena123");
        var identidad = UsuarioDePrueba.Anonimo();
        var svc = new AuthService(db, identidad, new AuditService(db, identidad, new OrigenDePrueba()));

        var r = await svc.LoginAsync("ana", "Contrasena123");

        // El servicio no puede fijar la sesión: quién eres lo transporta la cookie, y de eso se
        // encarga el endpoint. En el escritorio Login mutaba el singleton como efecto colateral.
        Assert.True(r.Exito);
        Assert.False(identidad.IsLoggedIn);
    }

    [Fact]
    public async Task CambiarContrasena_RenuevaElSello_YEchaALasSesionesAbiertas()
    {
        var db = TestDb.New();
        var u = CrearUsuario(db, "ana", "Contrasena123");
        var selloAntes = u.SecurityStamp;

        var (ok, _) = await Fabrica.Auth(db, UsuarioDePrueba.Como(UserRole.Desarrollador))
            .ChangePasswordAsync(u.Id, "NuevaClave123");

        Assert.True(ok);
        var releido = db.Users.AsNoTracking().Single();
        Assert.NotEqual(selloAntes, releido.SecurityStamp);
        Assert.False(releido.MustChangePassword);
        // Y la contraseña nueva es la que vale.
        Assert.True((await Fabrica.Auth(db).LoginAsync("ana", "NuevaClave123")).Exito);
        Assert.False((await Fabrica.Auth(db).LoginAsync("ana", "Contrasena123")).Exito);
    }

    [Fact]
    public async Task CambiarContrasena_ExigeLongitudMinima()
    {
        var db = TestDb.New();
        var u = CrearUsuario(db, "ana", "Contrasena123");

        var (ok, mensaje) = await Fabrica.Auth(db).ChangePasswordAsync(u.Id, "corta");

        Assert.False(ok);
        Assert.Contains("8", mensaje);
    }

    [Fact]
    public async Task ResetPassword_RequiereAdmin()
    {
        var db = TestDb.New();
        var u = CrearUsuario(db, "ana", "Contrasena123");
        var svcDev = Fabrica.Auth(db, UsuarioDePrueba.Como(UserRole.Desarrollador));

        await Assert.ThrowsAsync<AuthorizationException>(() => svcDev.ResetPasswordAsync(u.Id));
    }

    [Fact]
    public async Task ResetPassword_DejaContrasenaTemporalQueObligaACambiarla()
    {
        var db = TestDb.New();
        var u = CrearUsuario(db, "ana", "Contrasena123");

        var (ok, _, temporal) = await Fabrica.Auth(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 9))
            .ResetPasswordAsync(u.Id);

        Assert.True(ok);
        Assert.False(string.IsNullOrWhiteSpace(temporal));

        var login = await Fabrica.Auth(db).LoginAsync("ana", temporal!);
        Assert.True(login.Exito);
        Assert.True(login.Usuario!.MustChangePassword);
    }

    [Fact]
    public async Task ResetPassword_TambienLevantaElBloqueo()
    {
        var db = TestDb.New();
        var u = CrearUsuario(db, "ana", "Contrasena123");
        var svc = Fabrica.Auth(db);
        for (int i = 0; i < AuthService.MaxFailedAttempts; i++) await svc.LoginAsync("ana", "mal");
        Assert.True(AuthService.EstaBloqueado(db.Users.AsNoTracking().Single()));

        await Fabrica.Auth(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 9)).ResetPasswordAsync(u.Id);

        Assert.False(AuthService.EstaBloqueado(db.Users.AsNoTracking().Single()));
    }

    // ── Desbloqueo manual ────────────────────────────────────────────────────────

    [Fact]
    public async Task Desbloquear_RequiereAdmin()
    {
        var db = TestDb.New();
        var u = CrearUsuario(db, "ana", "Contrasena123");

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            Fabrica.Auth(db, UsuarioDePrueba.Como(UserRole.Desarrollador)).DesbloquearCuentaAsync(u.Id));
    }

    [Fact]
    public async Task Desbloquear_LimpiaFechaYContador()
    {
        var db = TestDb.New();
        var u = CrearUsuario(db, "ana", "Contrasena123");
        var svc = Fabrica.Auth(db);
        for (int i = 0; i < AuthService.MaxFailedAttempts; i++) await svc.LoginAsync("ana", "mal");

        var (ok, _) = await Fabrica.Auth(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 9))
            .DesbloquearCuentaAsync(u.Id);

        Assert.True(ok);
        var releido = db.Users.AsNoTracking().Single();
        Assert.Null(releido.LockoutUntil);
        // El contador también: dejarlo en 4 haría que el siguiente error volviera a bloquear.
        Assert.Equal(0, releido.FailedLoginCount);
        Assert.True((await Fabrica.Auth(db).LoginAsync("ana", "Contrasena123")).Exito);
    }

    [Fact]
    public async Task Desbloquear_UnaCuentaQueNoLoEsta_LoDice()
    {
        var db = TestDb.New();
        var u = CrearUsuario(db, "ana", "Contrasena123");

        var (ok, mensaje) = await Fabrica.Auth(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 9))
            .DesbloquearCuentaAsync(u.Id);

        Assert.False(ok);
        Assert.Contains("no está bloqueado", mensaje);
    }

    // ── Siembra del administrador inicial ────────────────────────────────────────

    [Fact]
    public async Task SeedAdmin_CreaLaCuentaSoloSiNoHayNinguna()
    {
        var db = TestDb.New();
        var svc = Fabrica.Auth(db);

        var temporal = await svc.SeedAdminAsync();

        Assert.False(string.IsNullOrWhiteSpace(temporal));
        var admin = db.Users.AsNoTracking().Single();
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.True(admin.MustChangePassword);
        Assert.True((await Fabrica.Auth(db).LoginAsync("admin", temporal!)).Exito);

        // Segunda pasada: no vuelve a sembrar ni devuelve contraseña.
        Assert.Null(await svc.SeedAdminAsync());
        Assert.Single(db.Users.AsNoTracking());
    }

    [Fact]
    public async Task LaContrasenaTemporal_NoTraeCaracteresQueSeConfundenAlDictarla()
    {
        var db = TestDb.New();

        var temporal = await Fabrica.Auth(db).SeedAdminAsync();

        // Esta contraseña se lee en voz alta o se manda por chat: un cero que alguien teclea como O
        // es un bloqueo de cuenta garantizado.
        Assert.NotNull(temporal);
        Assert.DoesNotContain(temporal!, c => c is '0' or 'O' or '1' or 'l' or 'I');
        Assert.Equal(12, temporal!.Length);
    }

    // ── Bitácora ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginExitoso_QuedaEnLaBitacoraConSuOrigen()
    {
        var db = TestDb.New();
        CrearUsuario(db, "ana", "Contrasena123");

        await Fabrica.Auth(db).LoginAsync("ana", "Contrasena123");

        var log = db.AuditLogs.AsNoTracking().Single();
        Assert.Equal(AuditAction.Login, log.Action);
        Assert.Equal("prueba", log.Origin);
    }
}
