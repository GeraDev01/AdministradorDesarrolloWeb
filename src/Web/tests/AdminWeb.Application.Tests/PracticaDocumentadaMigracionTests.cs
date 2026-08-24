using AdminWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LAS TRES COLUMNAS NUEVAS DE <c>PoolActivityExtraCriteria</c> contra una base que YA EXISTE.
///
/// <para><b>Por qué esta tabla es especial y merece su propia prueba.</b> Casi todas las tablas de
/// esta aplicación las crea EF desde el modelo. Ésta NO: la crea el migrador a mano, con
/// <c>CREATE TABLE</c> escrito en SQL crudo, y en las DOS ramas —SQLite y SQL Server—. Eso significa
/// que una columna nueva suya va en <b>tres</b> sitios y no en dos:</para>
/// <list type="number">
/// <item>la ENTIDAD, para las instalaciones nuevas, donde la tabla nace de <c>EnsureCreated</c>;</item>
/// <item>el CUERPO del <c>CREATE TABLE</c> de cada rama, por lo mismo;</item>
/// <item>y un <c>ALTER</c> idempotente en cada rama, para las bases que ya existían.</item>
/// </list>
///
/// <para><b>Y por qué falla en silencio si se olvida.</b> En una base nueva la columna aparece
/// igualmente —la pone EF desde el modelo— así que la suite entera pasaría en verde sin que ninguna
/// prueba ejerciera el parche. Contra la base real, que lleva años de datos, <c>EnsureCreated</c> no
/// altera nada: la columna no existiría, EF la pediría en cada consulta, y no se rompería «la
/// pantalla nueva» — se rompería <b>todo lo que lea la tabla</b>, incluidos los criterios extra de
/// las actividades que ya estaban en curso. Es el fallo de <c>Developers.TeamFunction</c>, y por eso
/// esta prueba tira las columnas antes de correr el migrador: es la única forma de ejercer el camino
/// que la base real va a recorrer.</para>
///
/// <para>Mismo mecanismo que <c>PerchaDelPoolMigracionTests</c> y <c>FuncionDeEquipoMigracionTests</c>.
/// Aquí NO hay relleno que probar: las tres columnas nacen nulas y nulo es su valor legítimo —«este
/// extra no exigía citar nada»—, así que no hay nada que convertir.</para>
/// </summary>
public class PracticaDocumentadaMigracionTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var c in _contextos) c.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Las tres, por su nombre exacto: son las que hay que buscar en las tres listas.</summary>
    private static readonly string[] LasTres =
        ["Justificacion", "KnowledgeArticleId", "KnowledgeArticleTitle"];

    private AppDbContext BaseSinLasColumnas()
    {
        var db = TestDb.New();
        _contextos.Add(db);

        foreach (var columna in LasTres)
            db.Database.ExecuteSqlRaw(
                $@"ALTER TABLE ""PoolActivityExtraCriteria"" DROP COLUMN ""{columna}""");

        foreach (var columna in LasTres)
            Assert.DoesNotContain(columna, Columnas(db));

        return db;
    }

    private static List<string> Columnas(AppDbContext db) =>
        db.Database.SqlQueryRaw<string>(
              @"SELECT name AS ""Value"" FROM pragma_table_info('PoolActivityExtraCriteria')")
          .AsEnumerable().ToList();

    [Fact]
    public void ElMigrador_AgregaLasTresAUnaBaseQueYaExistia()
    {
        var db = BaseSinLasColumnas();

        var fallidas = DatabaseMigrator.EnsureUpToDate(db);

        // Una lista vacía es la única señal honesta de que todo se aplicó: un parche que revienta
        // aborta los que vienen detrás mientras el arranque anuncia «Esquema al día».
        Assert.Empty(fallidas);
        foreach (var columna in LasTres)
            Assert.Contains(columna, Columnas(db));
    }

    [Fact]
    public void ElMigrador_EsIdempotente_ConLasTres()
    {
        var db = BaseSinLasColumnas();

        Assert.Empty(DatabaseMigrator.EnsureUpToDate(db));
        Assert.Empty(DatabaseMigrator.EnsureUpToDate(db));

        foreach (var columna in LasTres)
            Assert.Contains(columna, Columnas(db));
    }

    /// <summary>
    /// Y EL CUERPO DEL <c>CREATE TABLE</c> las trae, que es el sitio que más fácil se olvida: el
    /// <c>ALTER</c> se escribe pensando en «la base que ya existe» y el <c>CREATE</c> se queda como
    /// estaba, porque en desarrollo la tabla siempre nace de EF y nadie nota la diferencia.
    ///
    /// <para>Se comprueba tirando la tabla ENTERA —no solo sus columnas— y dejando que el migrador la
    /// vuelva a crear. Es exactamente lo que pasa en una instalación nueva sobre SQL Server, donde EF
    /// no crea nada y el <c>CREATE TABLE</c> del migrador es lo único que hay.</para>
    /// </summary>
    [Fact]
    public void ElCreateTable_TraeLasTres_EnUnaBaseDondeLaTablaNoExistia()
    {
        var db = TestDb.New();
        _contextos.Add(db);

        db.Database.ExecuteSqlRaw(@"DROP TABLE ""PoolActivityExtraCriteria""");
        Assert.Empty(Columnas(db));

        Assert.Empty(DatabaseMigrator.EnsureUpToDate(db));

        var columnas = Columnas(db);
        Assert.NotEmpty(columnas);
        foreach (var columna in LasTres)
            Assert.Contains(columna, columnas);

        // Y las de siempre siguen ahí: recrear la tabla no puede haber perdido nada por el camino.
        foreach (var vieja in new[] { "PoolActivityId", "ScoringCriterionId", "Name", "Points",
                                      "IsMet", "EvaluatedAtUtc", "Comment" })
            Assert.Contains(vieja, columnas);
    }
}
