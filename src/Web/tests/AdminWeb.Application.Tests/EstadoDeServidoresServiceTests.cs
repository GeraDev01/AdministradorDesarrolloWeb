using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// La foto de «qué versión tiene cada servidor».
///
/// <para>Lo que estas pruebas cuidan es la pregunta que la pantalla contesta: <b>a cuál le falta</b>.
/// Y sobre todo el criterio con el que se decide cuál es la última versión publicada, que es donde
/// está el error fácil: comparar el texto de la versión pondría «1.9» por delante de «1.10» y la
/// pantalla diría que un servidor al día está atrasado.</para>
/// </summary>
public class EstadoDeServidoresServiceTests
{
    private static EstadoDeServidoresService Servicio(AppDbContext db, ICurrentUser? quien = null) =>
        new(db, quien ?? UsuarioDePrueba.Como(UserRole.Admin));

    private static AppSystem SembrarSistema(AppDbContext db, string nombre = "Portal")
    {
        var sistema = new AppSystem { Name = nombre, IsActive = true };
        db.AppSystems.Add(sistema);
        db.SaveChanges();
        return sistema;
    }

    private static AppRelease SembrarVersion(AppDbContext db, AppSystem sistema, string version, DateTime creadaUtc)
    {
        var release = new AppRelease { AppSystemId = sistema.Id, Version = version, CreatedAt = creadaUtc };
        db.AppReleases.Add(release);
        db.SaveChanges();
        return release;
    }

    private static DeploymentTarget SembrarServidor(AppDbContext db, string nombre,
        AppRelease? version = null, DateTime? desplegadoUtc = null, int? porUsuario = null, bool activo = true)
    {
        var servidor = new DeploymentTarget
        {
            Nombre = nombre,
            Host = $"ftps://{nombre}",
            Usuario = "publicador",
            RutaRemota = "/site/wwwroot",
            IsActive = activo,
            LastReleaseId = version?.Id,
            LastDeployedAt = desplegadoUtc,
            LastDeployedById = porUsuario
        };
        db.DeploymentTargets.Add(servidor);
        db.SaveChanges();
        return servidor;
    }

    [Fact]
    public async Task Un_servidor_con_la_ultima_version_esta_al_dia()
    {
        using var db = TestDb.New();
        var sistema = SembrarSistema(db);
        var version = SembrarVersion(db, sistema, "1.0.0", DateTime.UtcNow.AddDays(-1));
        SembrarServidor(db, "web01", version, DateTime.UtcNow.AddHours(-2));

        var estado = await Servicio(db).ObtenerAsync();

        var fila = Assert.Single(estado.Servidores);
        Assert.False(fila.Atrasado);
        Assert.Equal("Al día", fila.EstadoTexto);
        Assert.Equal(1, estado.AlDia);
    }

    [Fact]
    public async Task Un_servidor_con_una_version_vieja_sale_atrasado()
    {
        using var db = TestDb.New();
        var sistema = SembrarSistema(db);
        var vieja = SembrarVersion(db, sistema, "1.0.0", DateTime.UtcNow.AddDays(-10));
        SembrarVersion(db, sistema, "1.1.0", DateTime.UtcNow.AddDays(-1));
        SembrarServidor(db, "web01", vieja, DateTime.UtcNow.AddDays(-9));

        var estado = await Servicio(db).ObtenerAsync();

        var fila = Assert.Single(estado.Servidores);
        Assert.True(fila.Atrasado);
        Assert.Equal("Atrasado", fila.EstadoTexto);
        Assert.Equal("1.1.0", fila.UltimaVersionDelSistema);
        Assert.Equal(1, estado.Atrasados);
    }

    [Fact]
    public async Task La_ultima_version_se_decide_por_fecha_de_alta_y_no_por_el_texto()
    {
        using var db = TestDb.New();
        var sistema = SembrarSistema(db);

        // Alfabéticamente «1.9» va DESPUÉS de «1.10», que es justo lo contrario de la realidad. Por
        // eso la comparación es por fecha de alta: funciona con cualquier convención de numeración.
        SembrarVersion(db, sistema, "1.9", DateTime.UtcNow.AddDays(-20));
        var ultima = SembrarVersion(db, sistema, "1.10", DateTime.UtcNow.AddDays(-2));
        SembrarServidor(db, "web01", ultima, DateTime.UtcNow.AddDays(-1));

        var estado = await Servicio(db).ObtenerAsync();

        var fila = Assert.Single(estado.Servidores);
        Assert.Equal("1.10", fila.UltimaVersionDelSistema);
        Assert.False(fila.Atrasado);
    }

    [Fact]
    public async Task Un_servidor_sin_desplegar_no_cuenta_como_atrasado()
    {
        using var db = TestDb.New();
        SembrarSistema(db);
        SembrarServidor(db, "web-nuevo");

        var estado = await Servicio(db).ObtenerAsync();

        var fila = Assert.Single(estado.Servidores);
        Assert.True(fila.NuncaDesplegado);
        Assert.False(fila.Atrasado);
        Assert.Equal("Sin desplegar", fila.EstadoTexto);
        Assert.Equal("—", fila.Antiguedad);
        Assert.Equal(1, estado.SinDesplegar);
    }

    [Fact]
    public async Task Los_dados_de_baja_quedan_fuera_salvo_que_se_pidan()
    {
        using var db = TestDb.New();
        var sistema = SembrarSistema(db);
        var version = SembrarVersion(db, sistema, "1.0.0", DateTime.UtcNow.AddDays(-1));
        SembrarServidor(db, "web01", version, DateTime.UtcNow.AddHours(-1));
        SembrarServidor(db, "web-retirado", version, DateTime.UtcNow.AddDays(-200), activo: false);

        // Fuera por omisión: mezclarlos infla la cuenta de atrasados con máquinas que ya no existen.
        Assert.Single((await Servicio(db).ObtenerAsync()).Servidores);
        Assert.Equal(2, (await Servicio(db).ObtenerAsync(incluirDadosDeBaja: true)).Servidores.Count);
    }

    [Fact]
    public async Task Se_resuelve_quien_lo_desplego_y_su_ausencia_no_es_un_error()
    {
        using var db = TestDb.New();
        var sistema = SembrarSistema(db);
        var version = SembrarVersion(db, sistema, "1.0.0", DateTime.UtcNow.AddDays(-1));

        var usuario = new User { Username = "ops", FullName = "Luis Prado", Role = UserRole.Operaciones };
        db.Users.Add(usuario);
        db.SaveChanges();

        SembrarServidor(db, "web01", version, DateTime.UtcNow.AddHours(-1), porUsuario: usuario.Id);

        // Sin registro de quién: son los despliegues anteriores a que eso se guardara. Es null, no un
        // fallo, y la pantalla lo enseña como «no se registró».
        SembrarServidor(db, "web02", version, DateTime.UtcNow.AddHours(-1));

        var estado = await Servicio(db).ObtenerAsync();

        Assert.Equal("Luis Prado", estado.Servidores.Single(s => s.Servidor == "web01").Quien);
        Assert.Null(estado.Servidores.Single(s => s.Servidor == "web02").Quien);
    }

    [Fact]
    public async Task Sin_saber_cual_es_la_ultima_no_se_afirma_que_esta_al_dia()
    {
        using var db = TestDb.New();
        SembrarSistema(db);

        // Se desplegó, pero no quedó registrada QUÉ versión: son los despliegues anteriores a que eso
        // se guardara. Sin poder comparar, decir «al día» sería afirmar algo que no se comprobó.
        SembrarServidor(db, "web01", version: null, desplegadoUtc: DateTime.UtcNow.AddHours(-1));

        var estado = await Servicio(db).ObtenerAsync();

        var fila = Assert.Single(estado.Servidores);
        Assert.Equal("Desplegado", fila.EstadoTexto);
        Assert.False(fila.NuncaDesplegado);
        Assert.False(fila.Atrasado);
    }

    [Fact]
    public async Task Un_desarrollador_no_ve_el_estado_de_los_servidores()
    {
        using var db = TestDb.New();
        SembrarSistema(db);

        var servicio = Servicio(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1));

        await Assert.ThrowsAsync<AuthorizationException>(() => servicio.ObtenerAsync());
    }

    [Fact]
    public async Task Operaciones_si_lo_ve()
    {
        using var db = TestDb.New();
        SembrarSistema(db);

        var estado = await Servicio(db, UsuarioDePrueba.Como(UserRole.Operaciones)).ObtenerAsync();

        Assert.Empty(estado.Servidores);
    }
}
