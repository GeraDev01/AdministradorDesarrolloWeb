using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Personas;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LA COLUMNA NUEVA de <c>Developers</c> contra una base que YA EXISTE.
///
/// <para><b>Por qué esto no sobra, y por qué faltaba.</b> <c>Developers.TeamFunction</c> es un campo
/// nuevo, y en una base nueva la crea sola <c>EnsureCreated</c> a partir del modelo: toda la suite
/// pasaba en verde sin que ninguna prueba ejerciera el parche. Pero <c>EnsureCreated</c> NO altera
/// una base que ya existe, así que contra la base real —la que lleva años de datos— la columna
/// sencillamente no aparecería, y EF la pide en CADA consulta de <c>Developers</c>: no se rompería el
/// organigrama, se rompería todo lo que lea una persona.</para>
///
/// <para>Se ejerce SQLite, que es el motor que se puede levantar en una prueba. La rama de SQL Server
/// escribe la MISMA columna con los tipos de aquel motor —<c>IF COL_LENGTH</c> y <c>nvarchar(200)</c>—
/// y vive al lado en el mismo archivo, traducida y no copiada.</para>
/// </summary>
public class FuncionDeEquipoMigracionTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var c in _contextos) c.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Una base con la forma que tenía antes de que existiera la función de equipo: se recrea
    /// <c>Developers</c> sin esa columna, que es lo que el migrador se va a encontrar de verdad.
    /// </summary>
    private AppDbContext BaseSinLaColumna()
    {
        var db = TestDb.New();
        _contextos.Add(db);

        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" DROP COLUMN ""TeamFunction""");
        Assert.DoesNotContain("TeamFunction", Columnas(db, "Developers"));

        return db;
    }

    private static List<string> Columnas(AppDbContext db, string tabla) =>
        db.Database.SqlQueryRaw<string>(@"SELECT name AS ""Value"" FROM pragma_table_info({0})", tabla)
          .AsEnumerable().ToList();

    [Fact]
    public void ElMigrador_AgregaLaFuncionDeEquipoAUnaBaseQueYaExistia()
    {
        var db = BaseSinLaColumna();

        var fallidas = DatabaseMigrator.EnsureUpToDate(db);

        // Una lista vacía es la única señal honesta de que todo se aplicó.
        Assert.Empty(fallidas);
        Assert.Contains("TeamFunction", Columnas(db, "Developers"));
    }

    [Fact]
    public void ElMigrador_EsIdempotente_CorreDosVecesSinRomperNada()
    {
        // La API arranca en cada despliegue y en cada instancia nueva: el parche corre muchas veces
        // contra la misma base. Un ADD COLUMN que no sea idempotente no falla el día que se escribe,
        // falla en el segundo arranque con la aplicación ya en marcha.
        var db = BaseSinLaColumna();

        DatabaseMigrator.EnsureUpToDate(db);
        Assert.Empty(DatabaseMigrator.EnsureUpToDate(db));
        Assert.Contains("TeamFunction", Columnas(db, "Developers"));
    }

    [Fact]
    public async Task ConLaColumnaReciénCreada_elOrganigramaSeLeeYLaFuncionSeGuarda()
    {
        // Que la columna exista con el nombre correcto no basta: lo que cuenta es que EF pueda leer y
        // escribir contra ELLA, no contra la que crea EnsureCreated desde el modelo. Sin el parche
        // esta prueba no falla por el organigrama: falla al leer cualquier desarrollador.
        var db = BaseSinLaColumna();
        DatabaseMigrator.EnsureUpToDate(db);

        db.Teams.Add(new Team { Id = 1, Name = "Equipo Web" });
        db.Developers.Add(new Developer
        { Id = 1, FullName = "Ana", IsActive = true, TeamId = 1, TeamRole = TeamRole.Lider });
        await db.SaveChangesAsync();

        var servicio = ServicioDePersonas(db);

        var (ok, _) = await servicio.GuardarFuncionAsync(
            new GuardarFuncionRequest(1, "Mantiene la pasarela de pagos"));
        Assert.True(ok);

        var organigrama = await servicio.OrganigramaAsync();
        Assert.Equal("Mantiene la pasarela de pagos",
            organigrama.Equipos.Single().Integrantes.Single().Funcion);
    }

    private static PersonasQueryService ServicioDePersonas(AppDbContext db)
    {
        ICurrentUser lider = new UsuarioDePrueba
        { UserId = 1, Username = "lider", FullName = "Líder", Role = UserRole.Admin };

        var origen = new OrigenDePrueba();
        var bitacora = new AuditService(db, lider, origen);
        return new PersonasQueryService(
            db, lider, bitacora,
            new AuthService(db, lider, bitacora),
            new PresenceService(db, lider, origen),
            new AttendanceService(db, lider, bitacora, origen),
            new DeveloperProfileService(db, lider, bitacora),
            new AnnouncementService(db, lider, bitacora));
    }
}
