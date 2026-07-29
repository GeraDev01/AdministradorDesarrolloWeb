using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Security;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Eliminación de usuarios: solo un administrador, y solo cuentas que NO sean de administrador.
/// </summary>
public class DeleteUserTests
{
    /// <summary>
    /// Id de la sesión, deliberadamente alto: los usuarios que crean las pruebas reciben ids
    /// autoincrementales desde 1, y si coincidieran con el de la sesión el servicio los tomaría
    /// por "mi propia cuenta" y rechazaría el borrado por el motivo equivocado.
    /// </summary>
    private const int SesionUserId = 9001;

    private sealed record Entorno(AppDbContext Db, AuthService Auth, CurrentUserContext User);

    private static Entorno Nuevo(UserRole rolSesion = UserRole.Admin, int sesionUserId = SesionUserId)
    {
        var db = TestDb.New();
        var user = Ctx.As(rolSesion, developerId: rolSesion == UserRole.Desarrollador ? 1 : null, userId: sesionUserId);
        var audit = new AuditService(db, user);
        return new Entorno(db, new AuthService(db, user, audit), user);
    }

    /// <summary>Crea el servicio otra vez sobre la misma base, pero con otra identidad de sesión.</summary>
    private static AuthService ComoUsuario(AppDbContext db, UserRole rol, int userId)
    {
        var user = Ctx.As(rol, developerId: null, userId: userId);
        return new AuthService(db, user, new AuditService(db, user));
    }

    private static User Crear(AppDbContext db, string username, UserRole rol)
    {
        var u = new User
        {
            Username = username,
            FullName = username,
            Role = rol,
            IsActive = true,
            PasswordHash = PasswordHasher.Hash("Password123!"),
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(u);
        db.SaveChanges();
        return u;
    }

    // ── Permitido ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.Operaciones)]
    [InlineData(UserRole.Desarrollador)]
    public void ElAdministradorPuedeEliminarUsuariosNoAdministradores(UserRole rol)
    {
        var e = Nuevo();
        var victima = Crear(e.Db, "objetivo", rol);

        var (ok, _) = e.Auth.DeleteUser(victima.Id);

        Assert.True(ok);
        Assert.Null(e.Db.Users.Find(victima.Id));
    }

    // ── Prohibido ───────────────────────────────────────────────────────────────

    [Fact]
    public void NoSePuedeEliminarAOtroAdministrador()
    {
        var e = Nuevo();
        var otroAdmin = Crear(e.Db, "otro_admin", UserRole.Admin);

        var (ok, mensaje) = e.Auth.DeleteUser(otroAdmin.Id);

        Assert.False(ok);
        Assert.Contains("administrador", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(e.Db.Users.Find(otroAdmin.Id));
    }

    [Fact]
    public void NoSePuedeEliminarLaPropiaCuenta()
    {
        var e = Nuevo();
        // Se crea el usuario y luego se abre sesión CON ESE id, para que sea de verdad "yo".
        var yo = Crear(e.Db, "yo", UserRole.Operaciones);
        var auth = ComoUsuario(e.Db, UserRole.Admin, yo.Id);

        var (ok, mensaje) = auth.DeleteUser(yo.Id);

        Assert.False(ok);
        Assert.Contains("propia cuenta", mensaje);
        Assert.NotNull(e.Db.Users.Find(yo.Id));
    }

    [Theory]
    [InlineData(UserRole.Operaciones)]
    [InlineData(UserRole.Desarrollador)]
    public void UnNoAdministradorNoPuedeEliminarUsuarios(UserRole rolSesion)
    {
        var e = Nuevo(rolSesion);
        var victima = Crear(e.Db, "objetivo", UserRole.Operaciones);

        Assert.Throws<AuthorizationException>(() => e.Auth.DeleteUser(victima.Id));
        Assert.NotNull(e.Db.Users.Find(victima.Id));
    }

    [Fact]
    public void EliminarUnUsuarioInexistenteNoRevienta()
    {
        var e = Nuevo();
        var (ok, mensaje) = e.Auth.DeleteUser(9999);

        Assert.False(ok);
        Assert.Contains("no encontrado", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    // ── Bitácora e histórico ────────────────────────────────────────────────────

    [Fact]
    public void LaBitacoraConservaElUsuarioEliminado()
    {
        var e = Nuevo();
        var victima = Crear(e.Db, "se_va", UserRole.Operaciones);

        e.Auth.DeleteUser(victima.Id);

        var log = e.Db.AuditLogs.Single(l => l.Action == AuditAction.Delete && l.EntityType == "User");
        Assert.Equal(AuditOutcome.Exito, log.Outcome);
        Assert.Contains("se_va", log.OldValues);   // los datos sobreviven al borrado
    }

    [Fact]
    public void UnIntentoSobreUnAdministradorQuedaRegistradoComoDenegado()
    {
        var e = Nuevo();
        var otroAdmin = Crear(e.Db, "intocable", UserRole.Admin);

        e.Auth.DeleteUser(otroAdmin.Id);

        var denegado = e.Db.AuditLogs.Single(l => l.Outcome == AuditOutcome.Denegado);
        Assert.Contains("intocable", denegado.Details);
    }

    [Fact]
    public void SeAvisaCuantosRegistrosDelHistoricoQuedanSinAtribucion()
    {
        var e = Nuevo();
        var victima = Crear(e.Db, "revisor", UserRole.Operaciones);

        var dev = new Developer { FullName = "Dev", IsActive = true };
        e.Db.Developers.Add(dev);
        e.Db.SaveChanges();
        e.Db.VacationRequests.Add(new VacationRequest
        {
            DeveloperId = dev.Id,
            StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(1),
            Status = VacationStatus.Aprobada,
            ReviewedById = victima.Id,       // atribución que quedará huérfana
            CreatedAt = DateTime.UtcNow
        });
        e.Db.SaveChanges();

        Assert.Equal(1, e.Auth.ContarReferencias(victima.Id));

        var (ok, mensaje) = e.Auth.DeleteUser(victima.Id);

        Assert.True(ok);
        Assert.Contains("sin atribuir", mensaje);
    }

    [Fact]
    public void SinHistoricoElMensajeNoAsustaDeMas()
    {
        var e = Nuevo();
        var victima = Crear(e.Db, "recien_creado", UserRole.Operaciones);

        var (ok, mensaje) = e.Auth.DeleteUser(victima.Id);

        Assert.True(ok);
        Assert.DoesNotContain("sin atribuir", mensaje);
    }
}
