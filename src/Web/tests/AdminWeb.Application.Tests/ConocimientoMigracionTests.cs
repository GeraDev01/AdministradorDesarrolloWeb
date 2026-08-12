using AdminWeb.Application.Services;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LA MIGRACIÓN de la base de conocimiento contra una base que YA EXISTE.
///
/// <para><b>Por qué esto no sobra.</b> Las tablas las crea solas <c>EnsureCreated</c> en una base
/// nueva, así que sin esta prueba los CREATE TABLE del migrador —los que de verdad van a correr
/// contra la base de producción, que lleva años de datos— no se ejecutarían NUNCA en la suite. Y ese
/// es exactamente el camino que ya falló una vez y en silencio: una sentencia mal escrita aborta la
/// suya y todas las que vienen detrás, mientras el arranque anuncia «Esquema al día». Aquí se le
/// quitan a la base las dos —los artículos y sus imágenes— y se obliga al migrador a crearlas de
/// verdad.</para>
///
/// <para><b>Solo se ejerce SQLite</b>, que es el motor que se puede levantar en una prueba. La rama
/// de SQL Server escribe LAS MISMAS columnas con los tipos de aquel motor —corchetes, nvarchar,
/// datetime2, IF OBJECT_ID— y vive al lado en el mismo archivo, precisamente para que quien toque
/// una vea la otra. No son la misma sentencia: están traducidas, y copiar una en la rama de la otra
/// es el error que hay que evitar.</para>
/// </summary>
public class ConocimientoMigracionTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var c in _contextos) c.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Una base con la forma que tiene una que todavía no conoce la base de conocimiento.</summary>
    private AppDbContext BaseSinConocimiento()
    {
        var db = TestDb.New();
        _contextos.Add(db);

        // Las imágenes PRIMERO: cuelgan de los artículos con clave foránea, y SQLite no deja tirar
        // la tabla a la que apunta una que sigue en pie.
        db.Database.ExecuteSqlRaw(@"DROP TABLE ""KnowledgeImages""");
        db.Database.ExecuteSqlRaw(@"DROP TABLE ""KnowledgeArticles""");
        Assert.False(Existe(db, "KnowledgeArticles"),
            "La base de partida no debería traer ya la tabla: si la trae, el migrador no tendría " +
            "que crearla y esa mitad del trabajo se quedaría sin probar.");
        Assert.False(Existe(db, "KnowledgeImages"));

        return db;
    }

    private static bool Existe(AppDbContext db, string tabla) =>
        db.Database.SqlQueryRaw<int>(
            $@"SELECT COUNT(*) AS ""Value"" FROM sqlite_master WHERE type='table' AND name='{tabla}'")
          .AsEnumerable().First() > 0;

    private static List<string> Columnas(AppDbContext db, string tabla) =>
        db.Database.SqlQueryRaw<string>($@"SELECT name AS ""Value"" FROM pragma_table_info('{tabla}')")
          .AsEnumerable().ToList();

    [Fact]
    public void Migrador_CreaLaTablaConTodasSusColumnas()
    {
        var db = BaseSinConocimiento();

        var fallidas = DatabaseMigrator.EnsureUpToDate(db);

        // Una lista vacía es la única señal honesta de que todo se aplicó (en SQLite siempre viene
        // vacía por diseño; se comprueba igual para que la regla no se olvide).
        Assert.Empty(fallidas);
        Assert.True(Existe(db, "KnowledgeArticles"));

        var columnas = Columnas(db, "KnowledgeArticles");
        foreach (var esperada in new[]
                 {
                     "Id", "Title", "Body", "Tags", "Status",
                     "AuthorUserId", "AuthorName", "AuthorDeveloperId",
                     "CreatedAtUtc", "UpdatedAtUtc", "SubmittedAtUtc",
                     "ReviewedByUserId", "ReviewerName", "ReviewedAtUtc", "ReviewComment",
                     "ReviewRound", "ReviewHistory", "PublishedAtUtc",
                     "PointEntryId", "PointsAwarded"
                 })
            Assert.Contains(esperada, columnas);

        // RowVersion NO se crea en SQLite: no existe el tipo y el modelo la ignora en este motor.
        Assert.DoesNotContain("RowVersion", columnas);
    }

    [Fact]
    public void Migrador_CreaTambienLaTablaDeLasImagenes()
    {
        // Por el mismo motivo que la de arriba: en una base nueva la crea EnsureCreated desde el
        // modelo, así que sin esta prueba el CREATE TABLE que de verdad va a correr contra la base
        // de producción no se ejecutaría NUNCA en la suite. Y esta cuelga de la anterior con una
        // clave foránea, que es justo lo que aborta una sentencia cuando se escribe en mal orden.
        var db = BaseSinConocimiento();

        var fallidas = DatabaseMigrator.EnsureUpToDate(db);

        Assert.Empty(fallidas);
        Assert.True(Existe(db, "KnowledgeImages"));

        var columnas = Columnas(db, "KnowledgeImages");
        foreach (var esperada in new[]
                 {
                     "Id", "ArticleId", "FileName", "ContentType",
                     "Bytes", "SizeBytes", "UploadedByUserId", "CreatedAtUtc"
                 })
            Assert.Contains(esperada, columnas);
    }

    [Fact]
    public void Migrador_EsIdempotente_CorreDosVecesSinRomperNada()
    {
        // La API arranca en cada despliegue, en cada reinicio y en cada instancia nueva: el migrador
        // corre muchas veces contra la misma base. Un parche que no fuera idempotente no fallaría el
        // día que se escribe sino en el segundo arranque, con la aplicación ya en marcha.
        var db = BaseSinConocimiento();

        DatabaseMigrator.EnsureUpToDate(db);
        var segunda = DatabaseMigrator.EnsureUpToDate(db);

        Assert.Empty(segunda);
        Assert.True(Existe(db, "KnowledgeArticles"));
    }

    [Fact]
    public async Task LaTablaQueCreaElMigrador_SirveDeVerdad()
    {
        // Que exista con los nombres correctos no basta: lo que cuenta es que el servicio pueda
        // escribir y leer contra ELLA, no contra la que crea EnsureCreated desde el modelo.
        var db = BaseSinConocimiento();
        DatabaseMigrator.EnsureUpToDate(db);

        ICurrentUser ana = new UsuarioDePrueba
        { UserId = 7, Username = "ana", FullName = "Ana", Role = UserRole.Desarrollador };

        var svc = new ConocimientoService(db, ana, new AuditService(db, ana, new OrigenDePrueba()),
                                          new NotificationService(db));

        var (ok, _, articulo) = await svc.CrearAsync(
            "Glosario: SLA", "Compromiso de atención medido en horas hábiles desde que entra el ticket.", "glosario");

        Assert.True(ok);
        Assert.NotNull(await svc.LeerAsync(articulo!.Id));
    }
}
