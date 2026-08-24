using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LA MARCA DE PERCHA contra una base que YA EXISTE, y el relleno de las que ya estaban.
///
/// <para>Mismo motivo que <see cref="FuncionDeEquipoMigracionTests"/>: en una base nueva la columna
/// la crea <c>EnsureCreated</c> a partir del modelo, así que la suite entera pasaría en verde sin que
/// ninguna prueba ejerciera el parche — y contra la base real, que lleva años de datos,
/// <c>EnsureCreated</c> no altera nada y la columna no aparecería. EF la pide en cada consulta de
/// <c>DevActivities</c>: no se rompería «mis actividades», se rompería todo lo que lea una actividad
/// libre.</para>
///
/// <para>Y aquí hay una segunda mitad que la columna sola no cubre: el RELLENO. La marca solo sirve
/// si las perchas que ya existían la tienen, y de las que perdieron su vínculo al devolverse no queda
/// más rastro que el título.</para>
/// </summary>
public class PerchaDelPoolMigracionTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var c in _contextos) c.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext BaseSinLaColumna()
    {
        var db = TestDb.New();
        _contextos.Add(db);

        // El ÍNDICE primero: SQLite se niega a tirar una columna que un índice todavía
        // referencia. En la base real no hace falta —allá nunca existieron ni la columna ni el
        // índice—, pero aquí los acaba de crear EnsureCreated desde el modelo de hoy.
        db.Database.ExecuteSqlRaw(@"DROP INDEX IF EXISTS ""IX_DevActivities_PoolActivityId""");
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""DevActivities"" DROP COLUMN ""PoolActivityId""");
        Assert.DoesNotContain("PoolActivityId", Columnas(db, "DevActivities"));

        return db;
    }

    private static List<string> Columnas(AppDbContext db, string tabla) =>
        db.Database.SqlQueryRaw<string>(@"SELECT name AS ""Value"" FROM pragma_table_info({0})", tabla)
          .AsEnumerable().ToList();

    /// <summary>
    /// Inserta una actividad libre por SQL crudo, que es la única forma de sembrarla mientras la
    /// columna no existe.
    /// </summary>
    private static void SembrarActividad(AppDbContext db, int devId, string titulo) =>
        db.Database.ExecuteSqlRaw(@"
            INSERT INTO ""DevActivities"" (""DeveloperId"", ""Title"", ""Status"", ""CreatedAt"")
            VALUES ({0}, {1}, 1, {2})", devId, titulo, DateTime.UtcNow);

    private static int NuevoDesarrollador(AppDbContext db)
    {
        var d = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(d);
        db.SaveChanges();
        return d.Id;
    }

    private static int? MarcaDe(AppDbContext db, string titulo) =>
        db.DevActivities.AsNoTracking().Single(a => a.Title == titulo).PoolActivityId;

    private static int MarcasDeConversion(AppDbContext db) =>
        db.AppSettings.AsNoTracking().Count(s => s.Key == "PoolPerchasMarcadas");

    // ── La columna ───────────────────────────────────────────────────────────────

    [Fact]
    public void ElMigrador_AgregaLaMarcaDePerchaAUnaBaseQueYaExistia()
    {
        var db = BaseSinLaColumna();

        var fallidas = DatabaseMigrator.EnsureUpToDate(db);

        // Una lista vacía es la única señal honesta de que todo se aplicó.
        Assert.Empty(fallidas);
        Assert.Contains("PoolActivityId", Columnas(db, "DevActivities"));
    }

    [Fact]
    public void ElMigrador_EsIdempotente_ConLaMarcaDePercha()
    {
        var db = BaseSinLaColumna();

        Assert.Empty(DatabaseMigrator.EnsureUpToDate(db));
        Assert.Empty(DatabaseMigrator.EnsureUpToDate(db));

        Assert.Contains("PoolActivityId", Columnas(db, "DevActivities"));
        Assert.Equal(1, MarcasDeConversion(db));   // la marca no se duplica
    }

    // ── El relleno ───────────────────────────────────────────────────────────────

    /// <summary>
    /// La percha que TODAVÍA tiene su vínculo se recupera exacta: se sabe de qué actividad del pool
    /// es. Es el caso fácil y el único que se puede resolver sin heurísticas.
    /// </summary>
    [Fact]
    public void ElRelleno_RecuperaExactoLoQueConservaSuVinculo()
    {
        var db = BaseSinLaColumna();
        int devId = NuevoDesarrollador(db);
        SembrarActividad(db, devId, "Pool #7: corregir el importador");

        // Por SQL crudo y no por EF: mientras la columna no exista, cualquier consulta de
        // DevActivities por el modelo pide PoolActivityId y revienta. Es exactamente lo que le
        // pasaría a la aplicación entera si el parche no corriera contra la base real.
        int perchaId = db.Database
            .SqlQueryRaw<int>(@"SELECT ""Id"" AS ""Value"" FROM ""DevActivities""")
            .AsEnumerable().Single();
        db.PoolActivities.Add(new PoolActivity
        {
            Title = "corregir el importador", WorkType = PoolWorkType.Bug,
            Complexity = PoolComplexity.Alta, Points = 12,
            Status = PoolActivityStatus.Tomada, ClaimedByDeveloperId = devId,
            LinkedDevActivityId = perchaId
        });
        db.SaveChanges();
        int poolId = db.PoolActivities.AsNoTracking().Single().Id;

        DatabaseMigrator.EnsureUpToDate(db);

        Assert.Equal(poolId, MarcaDe(db, "Pool #7: corregir el importador"));
    }

    /// <summary>
    /// LA HUÉRFANA: la percha de una actividad que se devolvió, donde <c>SoltarReclamo</c> ya borró el
    /// vínculo. No hay de dónde recuperar CUÁL era, así que se marca con -1 —«fue percha, no sé de
    /// cuál»—, que es lo único honesto y basta para lo que la marca tiene que hacer: bloquear las
    /// cinco operaciones y esconderla de la lista.
    ///
    /// <para>Se reconoce por el título, cuyo formato lo escribe únicamente
    /// <c>CrearActividadEnlazada</c>. Es una heurística y va declarada como tal.</para>
    /// </summary>
    [Fact]
    public void ElRelleno_MarcaConMenosUnoLasHuerfanasQueSeReconocenPorElTitulo()
    {
        var db = BaseSinLaColumna();
        int devId = NuevoDesarrollador(db);
        SembrarActividad(db, devId, "Pool #41: lo que se devolvió");

        DatabaseMigrator.EnsureUpToDate(db);

        Assert.Equal(-1, MarcaDe(db, "Pool #41: lo que se devolvió"));
    }

    /// <summary>
    /// Y NO toca las actividades libres de verdad. Es la mitad que importa del relleno: marcar de más
    /// dejaría a alguien sin poder cerrar ni renombrar su propio trabajo, y sin ninguna pista de por
    /// qué.
    /// </summary>
    [Fact]
    public void ElRelleno_NoToca_lasActividadesLibresDeVerdad()
    {
        var db = BaseSinLaColumna();
        int devId = NuevoDesarrollador(db);
        SembrarActividad(db, devId, "Investigar la caída del viernes");
        SembrarActividad(db, devId, "Reunión con el área de finanzas");

        DatabaseMigrator.EnsureUpToDate(db);

        Assert.Null(MarcaDe(db, "Investigar la caída del viernes"));
        Assert.Null(MarcaDe(db, "Reunión con el área de finanzas"));
    }

    /// <summary>
    /// La marca en <c>AppSettings</c> y no una condición sobre los datos, por lo mismo que la
    /// conversión de los plazos: nulo es un valor LEGÍTIMO en esta columna —significa «actividad libre
    /// de verdad»— así que <c>WHERE PoolActivityId IS NULL</c> volvería a correr en cada arranque y
    /// alcanzaría a cualquier actividad nueva que alguien titulara empezando por «Pool #».
    /// </summary>
    [Fact]
    public void ElRelleno_NoVuelveACorrer_aunqueAparezcaUnTituloParecidoDespues()
    {
        var db = BaseSinLaColumna();
        int devId = NuevoDesarrollador(db);

        DatabaseMigrator.EnsureUpToDate(db);
        Assert.Equal(1, MarcasDeConversion(db));

        // Alguien titula a mano una actividad suya como si fuera del pool, DESPUÉS del relleno.
        db.DevActivities.Add(new DevActivity
        {
            DeveloperId = devId, Title = "Pool #99: esto lo escribí yo",
            Status = DevActivityStatus.Abierta
        });
        db.SaveChanges();

        DatabaseMigrator.EnsureUpToDate(db);

        Assert.Null(MarcaDe(db, "Pool #99: esto lo escribí yo"));
    }
}
