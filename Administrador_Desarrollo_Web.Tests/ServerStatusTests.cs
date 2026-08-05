using System.Linq;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// «¿Qué versión tiene cada servidor, quién se la puso y cuándo?».
///
/// Aquí vive también la regresión del defecto que motivó la pantalla: las columnas de versión y
/// última actualización se quedaban congeladas. La causa NO era que no se guardaran —el despliegue
/// escribe bien— sino que se leían con el <c>AppDbContext</c> Singleton, que devolvía las instancias
/// rastreadas de ANTES del despliegue. Por eso hay una prueba que actualiza desde OTRO contexto,
/// igual que hace el despliegue de verdad.
/// </summary>
public class ServerStatusTests
{
    /// <summary>Segundo contexto sobre la MISMA base: así se reproduce que el despliegue corre en su
    /// propio contexto mientras la pantalla sigue con el suyo.</summary>
    private static AppDbContext OtroContextoSobreLaMisma(AppDbContext db)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Database.GetConnectionString()!).Options;
        return new AppDbContext(opts);
    }

    private static DeploymentTarget Servidor(string nombre, bool activo = true) => new()
    {
        Nombre = nombre, Host = "ftps://web.empresa.com", Puerto = 21, Usuario = "deploy",
        Contrasena = "x", RutaRemota = "/site/wwwroot", URL = $"https://{nombre}.example.com",
        IsActive = activo
    };

    private static AppRelease Version(AppSystem sistema, string version, int diasDeAntiguedad) => new()
    {
        AppSystem = sistema, Version = version,
        CreatedAt = DateTime.UtcNow.AddDays(-diasDeAntiguedad)
    };

    private sealed record Entorno(AppDbContext Db, ServerStatusService Estado, AppSystem Sistema);

    private static Entorno Nuevo()
    {
        var db = TestDb.New();
        var sistema = new AppSystem { Name = "Expediente Digital" };
        db.AppSystems.Add(sistema);
        db.SaveChanges();
        return new Entorno(db, new ServerStatusService(db), sistema);
    }

    // ── La regresión ────────────────────────────────────────────────────────────

    /// <summary>
    /// El defecto tal cual: se despliega (otro contexto escribe LastDeployedAt / LastReleaseId) y la
    /// pantalla, que ya había leído esos servidores, tiene que mostrar lo nuevo. Sin AsNoTracking
    /// esta prueba falla devolviendo la versión anterior.
    /// </summary>
    [Fact]
    public void TrasUnDespliegueDesdeOtroContexto_MuestraLaVersionNueva()
    {
        var e = Nuevo();
        var srv = Servidor("web-01");
        var v1 = Version(e.Sistema, "26.08.01.01", 2);
        e.Db.DeploymentTargets.Add(srv);
        e.Db.AppReleases.Add(v1);
        e.Db.SaveChanges();

        // La pantalla ya leyó una vez: a partir de aquí el servidor queda rastreado con datos viejos.
        Assert.Equal("Nunca", e.Estado.Obtener().Single().DesplegadoUtc?.ToString() ?? "Nunca");

        // Y ahora despliega, desde su propio contexto, como hace DeploymentService.
        using (var otro = OtroContextoSobreLaMisma(e.Db))
        {
            var t = otro.DeploymentTargets.Single();
            t.LastDeployedAt      = DateTime.UtcNow;
            t.LastReleaseId       = v1.Id;
            t.LastDeployedById    = 7;
            t.LastDeploymentJobId = 42;
            otro.SaveChanges();
        }

        var estado = e.Estado.Obtener().Single();
        Assert.Equal("26.08.01.01", estado.Version);
        Assert.NotNull(estado.DesplegadoUtc);
        Assert.Equal(42, estado.JobId);
    }

    /// <summary>
    /// La otra mitad del mismo defecto: editar un servidor tiene que partir de lo que hay en la base,
    /// no de la copia que quedó rastreada. Si no, se guarda encima de lo que cambió otro equipo.
    /// </summary>
    [Fact]
    public void ParaEditar_TraeLoQueHayEnLaBase_NoLaCopiaVieja()
    {
        var db = TestDb.New();
        var user = Ctx.As(UserRole.Admin);
        var targets = new DeploymentTargetService(db, user, new AuditService(db, user));

        db.DeploymentTargets.Add(Servidor("web-01"));
        db.SaveChanges();
        int id = db.DeploymentTargets.Single().Id;

        // Se deja rastreada la instancia con el host viejo.
        Assert.Equal("ftps://web.empresa.com", db.DeploymentTargets.Find(id)!.Host);

        using (var otro = OtroContextoSobreLaMisma(db))
        {
            otro.DeploymentTargets.Single().Host = "ftps://nuevo.empresa.com";
            otro.SaveChanges();
        }

        var paraEditar = targets.ParaEditar(id);

        Assert.NotNull(paraEditar);
        Assert.Equal("ftps://nuevo.empresa.com", paraEditar!.Host);
        // Y sigue rastreada: si no, guardar los cambios del formulario no haría nada.
        Assert.NotEqual(EntityState.Detached, db.Entry(paraEditar).State);
    }

    [Fact]
    public void ParaEditar_DevuelveNull_SiOtroEquipoLoElimino()
    {
        var db = TestDb.New();
        var user = Ctx.As(UserRole.Admin);
        var targets = new DeploymentTargetService(db, user, new AuditService(db, user));

        db.DeploymentTargets.Add(Servidor("web-01"));
        db.SaveChanges();
        int id = db.DeploymentTargets.Single().Id;
        _ = db.DeploymentTargets.Find(id);   // queda rastreado

        using (var otro = OtroContextoSobreLaMisma(db))
        {
            otro.DeploymentTargets.RemoveRange(otro.DeploymentTargets);
            otro.SaveChanges();
        }

        Assert.Null(targets.ParaEditar(id));
    }

    // ── Qué informa la pantalla ─────────────────────────────────────────────────

    [Fact]
    public void ResuelveElNombreDeQuienDesplego()
    {
        var e = Nuevo();
        e.Db.Users.Add(new User { Username = "gtellez", FullName = "Gerardo Téllez", Role = UserRole.Operaciones, PasswordHash = "x", IsActive = true });
        e.Db.SaveChanges();
        int userId = e.Db.Users.Single().Id;

        var v = Version(e.Sistema, "1.0", 1);
        e.Db.AppReleases.Add(v);
        var srv = Servidor("web-01");
        e.Db.DeploymentTargets.Add(srv);
        e.Db.SaveChanges();

        srv.LastDeployedAt = DateTime.UtcNow; srv.LastReleaseId = v.Id; srv.LastDeployedById = userId;
        e.Db.SaveChanges();

        Assert.Equal("Gerardo Téllez", e.Estado.Obtener().Single().Quien);
    }

    /// <summary>Los despliegues anteriores a que se guardara «quién» no inventan un nombre: quedan
    /// en null para que la pantalla los muestre como «no se registró».</summary>
    [Fact]
    public void SinRegistroDeQuien_QuedaEnNulo()
    {
        var e = Nuevo();
        var srv = Servidor("web-01");
        e.Db.DeploymentTargets.Add(srv);
        e.Db.SaveChanges();
        srv.LastDeployedAt = DateTime.UtcNow;   // despliegue viejo: sin LastDeployedById
        e.Db.SaveChanges();

        Assert.Null(e.Estado.Obtener().Single().Quien);
    }

    /// <summary>
    /// Lo que hace útil la pantalla: distinguir al que ya tiene lo último del que se quedó atrás.
    /// «Última publicada» se decide por fecha de alta, no comparando el texto — «1.10» es posterior
    /// a «1.9» pero menor alfabéticamente.
    /// </summary>
    [Fact]
    public void MarcaAtrasadoAlQueNoTieneLaUltimaVersion()
    {
        var e = Nuevo();
        var vieja  = Version(e.Sistema, "1.9",  10);
        var nueva  = Version(e.Sistema, "1.10", 1);
        e.Db.AppReleases.AddRange(vieja, nueva);

        var alDia    = Servidor("web-al-dia");
        var atrasado = Servidor("web-atrasado");
        var virgen   = Servidor("web-virgen");
        e.Db.DeploymentTargets.AddRange(alDia, atrasado, virgen);
        e.Db.SaveChanges();

        alDia.LastReleaseId = nueva.Id;    alDia.LastDeployedAt = DateTime.UtcNow;
        atrasado.LastReleaseId = vieja.Id; atrasado.LastDeployedAt = DateTime.UtcNow.AddDays(-9);
        e.Db.SaveChanges();

        var estado = e.Estado.Obtener().ToDictionary(x => x.Servidor);

        Assert.False(estado["web-al-dia"].Atrasado);
        Assert.Equal("1.10", estado["web-al-dia"].UltimaVersionDelSistema);

        Assert.True(estado["web-atrasado"].Atrasado);
        Assert.Equal("1.9", estado["web-atrasado"].Version);
        Assert.Equal("1.10", estado["web-atrasado"].UltimaVersionDelSistema);

        // El que nunca recibió nada no es «atrasado»: es otra cosa, y mezclarlos haría que la cuenta
        // de pendientes no cuadre con lo que hay que hacer.
        Assert.False(estado["web-virgen"].Atrasado);
        Assert.True(estado["web-virgen"].NuncaDesplegado);
    }

    [Fact]
    public void LosDadosDeBaja_SoloAparecenSiSePiden()
    {
        var e = Nuevo();
        e.Db.DeploymentTargets.AddRange(Servidor("web-activo"), Servidor("web-baja", activo: false));
        e.Db.SaveChanges();

        Assert.Single(e.Estado.Obtener());
        Assert.Equal(2, e.Estado.Obtener(incluirDadosDeBaja: true).Count);
    }

    [Fact]
    public void SinServidores_DevuelveListaVacia_SinConsultarDeMas()
    {
        var e = Nuevo();
        Assert.Empty(e.Estado.Obtener());
    }
}
