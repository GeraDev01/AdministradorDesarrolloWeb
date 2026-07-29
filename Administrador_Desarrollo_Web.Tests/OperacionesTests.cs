using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Alcance del rol Operaciones sobre despliegues: qué puede hacer con los servidores y qué queda
/// registrado. Las reglas viven en el servicio, no en la pantalla — estas pruebas verifican
/// justamente eso, invocando el servicio directamente como lo haría cualquier otra ruta.
/// </summary>
public class OperacionesTests
{
    private sealed record Entorno(AppDbContext Db, DeploymentTargetService Targets, CurrentUserContext User);

    private static Entorno Nuevo(UserRole rol)
    {
        var db = TestDb.New();
        var user = Ctx.As(rol);
        var audit = new AuditService(db, user);
        return new Entorno(db, new DeploymentTargetService(db, user, audit), user);
    }

    private static DeploymentTarget Servidor(string nombre = "web-prod-01") => new()
    {
        Nombre = nombre,
        Host = "ftps://web.empresa.com",
        Puerto = 21,
        Usuario = "deploy",
        Contrasena = "cifrada",
        RutaRemota = "/site/wwwroot",
        URL = "https://web.empresa.com"
    };

    // ── Alta ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Operaciones)]
    public void AdminYOperacionesPuedenCrearServidores(UserRole rol)
    {
        var e = Nuevo(rol);

        var (ok, _, creado) = e.Targets.Crear(Servidor());

        Assert.True(ok);
        Assert.True(creado!.IsActive);
        Assert.Single(e.Db.DeploymentTargets);
    }

    [Fact]
    public void UnDesarrolladorNoPuedeCrearServidores()
    {
        var e = Nuevo(UserRole.Desarrollador);
        Assert.Throws<AuthorizationException>(() => e.Targets.Crear(Servidor()));
        Assert.Empty(e.Db.DeploymentTargets);
    }

    [Fact]
    public void NoSeAceptanDosServidoresConElMismoNombre()
    {
        var e = Nuevo(UserRole.Admin);
        e.Targets.Crear(Servidor("web-01"));

        var (ok, mensaje, _) = e.Targets.Crear(Servidor("web-01"));

        Assert.False(ok);
        Assert.Contains("Ya existe", mensaje);
        Assert.Single(e.Db.DeploymentTargets);
    }

    [Theory]
    [InlineData("", "web.com", 21, "u", "/r")]           // sin nombre
    [InlineData("n", "", 21, "u", "/r")]                  // sin host
    [InlineData("n", "web.com", 0, "u", "/r")]            // puerto inválido
    [InlineData("n", "web.com", 21, "", "/r")]            // sin usuario
    [InlineData("n", "web.com", 21, "u", "")]             // sin ruta remota
    public void LaValidacionRechazaServidoresIncompletos(string nombre, string host, int puerto, string usuario, string ruta)
    {
        var e = Nuevo(UserRole.Operaciones);

        var (ok, _, _) = e.Targets.Crear(new DeploymentTarget
        {
            Nombre = nombre, Host = host, Puerto = puerto, Usuario = usuario,
            Contrasena = "x", RutaRemota = ruta
        });

        Assert.False(ok);
        Assert.Empty(e.Db.DeploymentTargets);
    }

    // ── Edición y baja ──────────────────────────────────────────────────────────

    [Fact]
    public void OperacionesNoPuedeEditarUnServidorExistente()
    {
        var e = Nuevo(UserRole.Operaciones);
        var (_, _, t) = e.Targets.Crear(Servidor());

        t!.Host = "ftps://otro-host.com";
        var ex = Assert.Throws<AuthorizationException>(() => e.Targets.Editar(t, null));

        Assert.Contains("administrador", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OperacionesNoPuedeDarDeBajaUnServidor()
    {
        var e = Nuevo(UserRole.Operaciones);
        var (_, _, t) = e.Targets.Crear(Servidor());

        Assert.Throws<AuthorizationException>(() => e.Targets.Desactivar(t!.Id, null));
        Assert.True(e.Db.DeploymentTargets.Find(t.Id)!.IsActive);
    }

    [Fact]
    public void ElAdministradorSiPuedeEditarYDarDeBaja()
    {
        var e = Nuevo(UserRole.Admin);
        var (_, _, t) = e.Targets.Crear(Servidor());

        t!.URL = "https://nuevo.empresa.com";
        var (okEdit, _) = e.Targets.Editar(t, new { t.URL });
        var (okBaja, _) = e.Targets.Desactivar(t.Id, "servidor retirado");

        Assert.True(okEdit);
        Assert.True(okBaja);
        Assert.False(e.Db.DeploymentTargets.Find(t.Id)!.IsActive);
    }

    [Fact]
    public void LaBajaEsLogica_ElServidorSeConservaParaElHistorial()
    {
        var e = Nuevo(UserRole.Admin);
        var (_, _, t) = e.Targets.Crear(Servidor());

        e.Targets.Desactivar(t!.Id, null);

        // Sigue existiendo la fila: el historial de despliegues que lo usó sigue siendo legible.
        var guardado = e.Db.DeploymentTargets.Find(t.Id);
        Assert.NotNull(guardado);
        Assert.False(guardado!.IsActive);
    }

    [Fact]
    public void UnServidorDadoDeBajaSePuedeReactivar()
    {
        var e = Nuevo(UserRole.Admin);
        var (_, _, t) = e.Targets.Crear(Servidor());
        e.Targets.Desactivar(t!.Id, null);

        var (ok, _) = e.Targets.Reactivar(t.Id);

        Assert.True(ok);
        Assert.True(e.Db.DeploymentTargets.Find(t.Id)!.IsActive);
    }

    // ── Bitácora ────────────────────────────────────────────────────────────────

    [Fact]
    public void ElAltaQuedaEnBitacoraConElEstadoResultante()
    {
        var e = Nuevo(UserRole.Operaciones);
        e.Targets.Crear(Servidor("web-99"));

        var log = e.Db.AuditLogs.Single(l => l.EntityType == "DeploymentTarget");

        Assert.Equal(AuditAction.Create, log.Action);
        Assert.Equal(AuditOutcome.Exito, log.Outcome);
        Assert.Contains("web-99", log.NewValues);
        Assert.NotNull(log.Origin);          // equipo desde donde se hizo
    }

    [Fact]
    public void LaEdicionGuardaElAntesYElDespues()
    {
        var e = Nuevo(UserRole.Admin);
        var (_, _, t) = e.Targets.Crear(Servidor("web-01"));
        var previo = new { t!.Host };

        t.Host = "ftps://nuevo.empresa.com";
        e.Targets.Editar(t, previo);

        var log = e.Db.AuditLogs.Single(l => l.Action == AuditAction.Update && l.EntityType == "DeploymentTarget");
        Assert.Contains("web.empresa.com", log.OldValues);
        Assert.Contains("nuevo.empresa.com", log.NewValues);
    }

    [Fact]
    public void LaContrasenaNuncaLlegaALaBitacora()
    {
        var e = Nuevo(UserRole.Admin);
        var s = Servidor();
        s.Contrasena = "SUPERSECRETO123";
        e.Targets.Crear(s);

        foreach (var log in e.Db.AuditLogs.ToList())
        {
            Assert.DoesNotContain("SUPERSECRETO123", log.NewValues ?? "");
            Assert.DoesNotContain("SUPERSECRETO123", log.OldValues ?? "");
            Assert.DoesNotContain("SUPERSECRETO123", log.Details ?? "");
        }
    }

    [Fact]
    public void UnIntentoDenegadoTambienQuedaRegistrado()
    {
        var e = Nuevo(UserRole.Operaciones);
        var (_, _, t) = e.Targets.Crear(Servidor());

        Assert.Throws<AuthorizationException>(() => e.Targets.Desactivar(t!.Id, null));

        var denegado = e.Db.AuditLogs.Single(l => l.Outcome == AuditOutcome.Denegado);
        Assert.Equal(AuditAction.Delete, denegado.Action);
        Assert.Contains("sin ser administrador", denegado.Details);
    }

    // ── Guarda de rol ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Operaciones)]
    public void LaGuardaDeDespliegueAceptaAdminYOperaciones(UserRole rol)
    {
        var user = Ctx.As(rol);
        AuthorizationGuard.RequireAdminOrOperaciones(user);   // no debe lanzar
    }

    [Fact]
    public void LaGuardaDeDespliegueRechazaADesarrolladoresYAnonimos()
    {
        Assert.Throws<AuthorizationException>(() =>
            AuthorizationGuard.RequireAdminOrOperaciones(Ctx.As(UserRole.Desarrollador, 1)));
        Assert.Throws<AuthorizationException>(() =>
            AuthorizationGuard.RequireAdminOrOperaciones(Ctx.Anonymous()));
    }

    [Fact]
    public void IsOperacionesSeComprubaDeFormaPositiva()
    {
        Assert.True(Ctx.As(UserRole.Operaciones).IsOperaciones);
        Assert.False(Ctx.As(UserRole.Admin).IsOperaciones);
        Assert.False(Ctx.As(UserRole.Desarrollador, 1).IsOperaciones);
        Assert.False(Ctx.Anonymous().IsOperaciones);
    }
}
