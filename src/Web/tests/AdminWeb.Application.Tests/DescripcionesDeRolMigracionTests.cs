using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LA MIGRACIÓN de las descripciones de rol contra una base que YA EXISTE.
///
/// <para><b>Por qué no sobra.</b> La tabla la crea sola <c>EnsureCreated</c> en una base nueva, así
/// que sin esta prueba el <c>CREATE TABLE</c> del migrador —el que de verdad va a correr contra la
/// base de producción, que lleva años de datos— no se ejecutaría NUNCA en la suite. Es el camino que
/// ya falló una vez y en silencio: una sentencia mal escrita aborta la suya y todas las que vienen
/// detrás mientras el arranque anuncia «Esquema al día». Aquí se le quita la tabla a la base y se
/// obliga al migrador a crearla de verdad.</para>
///
/// <para><b>Solo se ejerce SQLite</b>, que es el motor que se puede levantar en una prueba. La rama
/// de SQL Server escribe LAS MISMAS columnas con los tipos de aquel motor —corchetes, nvarchar,
/// IF OBJECT_ID, sys.indexes— y vive al lado en el mismo archivo, precisamente para que quien toque
/// una vea la otra. No son la misma sentencia: están traducidas.</para>
/// </summary>
public class DescripcionesDeRolMigracionTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var c in _contextos) c.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Una base con la forma que tiene una que todavía no conoce las descripciones de rol.</summary>
    private AppDbContext BaseSinDescripciones()
    {
        var db = TestDb.New();
        _contextos.Add(db);

        db.Database.ExecuteSqlRaw(@"DROP TABLE ""DescripcionesDeRolDeEquipo""");
        Assert.False(Existe(db, "DescripcionesDeRolDeEquipo"),
            "La base de partida no debería traer ya la tabla: si la trae, el migrador no tendría que " +
            "crearla y esa mitad del trabajo se quedaría sin probar.");

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
        var db = BaseSinDescripciones();

        var fallidas = DatabaseMigrator.EnsureUpToDate(db);

        // Una lista vacía es la única señal honesta de que todo se aplicó. En SQLite viene vacía por
        // diseño; se comprueba igual para que la regla no se olvide el día que alguien la lea.
        Assert.Empty(fallidas);
        Assert.True(Existe(db, "DescripcionesDeRolDeEquipo"));

        var columnas = Columnas(db, "DescripcionesDeRolDeEquipo");
        Assert.Contains("Id", columnas);
        Assert.Contains("TeamId", columnas);
        Assert.Contains("Rol", columnas);
        Assert.Contains("Descripcion", columnas);
    }

    [Fact]
    public void Migrador_SePuedeVolverAPasar()
    {
        var db = BaseSinDescripciones();

        Assert.Empty(DatabaseMigrator.EnsureUpToDate(db));
        Assert.Empty(DatabaseMigrator.EnsureUpToDate(db));   // la segunda no puede reventar

        Assert.True(Existe(db, "DescripcionesDeRolDeEquipo"));
    }

    /// <summary>
    /// Después de migrar, la tabla se USA por el modelo: se escribe y se lee con EF, que es como la va
    /// a tocar la aplicación. Comprobar solo que las columnas están deja pasar el caso en que el tipo
    /// o el nombre no cuadran con la entidad, y eso solo se ve en ejecución.
    /// </summary>
    [Fact]
    public void TrasMigrar_ElModeloEscribeYLee()
    {
        var db = BaseSinDescripciones();
        DatabaseMigrator.EnsureUpToDate(db);

        db.Teams.Add(new Team { Id = 1, Name = "Soporte" });
        db.SaveChanges();

        db.DescripcionesDeRolDeEquipo.Add(new DescripcionDeRolDeEquipo
        {
            TeamId = 1,
            Rol = TeamRole.Fullstack,
            Descripcion = "Resuelve los bugs reportados por operación en tiempo y forma."
        });
        db.SaveChanges();
        db.ChangeTracker.Clear();

        var leida = db.DescripcionesDeRolDeEquipo.Single();
        Assert.Equal(1, leida.TeamId);
        Assert.Equal(TeamRole.Fullstack, leida.Rol);
        Assert.StartsWith("Resuelve los bugs", leida.Descripcion);
    }

    /// <summary>
    /// <b>Una sola descripción por rol dentro de cada equipo, y lo dice la BASE.</b>
    ///
    /// <para>No es una comprobación de más: sin el índice único, dos pestañas guardando a la vez dejan
    /// dos filas para el mismo puesto y quien lea se queda con la que le devuelva el motor. La función
    /// de media plantilla cambiaría de frase según el día, sin que nada fallara.</para>
    /// </summary>
    [Fact]
    public void ELMISMO_ROL_DOS_VECES_EN_UN_EQUIPO_lo_rechaza_la_base()
    {
        var db = BaseSinDescripciones();
        DatabaseMigrator.EnsureUpToDate(db);

        db.Teams.Add(new Team { Id = 1, Name = "Soporte" });
        db.SaveChanges();

        db.DescripcionesDeRolDeEquipo.Add(new DescripcionDeRolDeEquipo
        { TeamId = 1, Rol = TeamRole.Fullstack, Descripcion = "Una." });
        db.SaveChanges();

        db.DescripcionesDeRolDeEquipo.Add(new DescripcionDeRolDeEquipo
        { TeamId = 1, Rol = TeamRole.Fullstack, Descripcion = "Otra." });

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    /// <summary>
    /// El MISMO rol en OTRO equipo sí se puede: es justo lo que se pidió — un «Fullstack» de soporte
    /// no hace lo mismo que uno de desarrollo.
    /// </summary>
    [Fact]
    public void ELMISMO_ROL_EN_OTRO_EQUIPO_si_se_puede()
    {
        var db = BaseSinDescripciones();
        DatabaseMigrator.EnsureUpToDate(db);

        db.Teams.Add(new Team { Id = 1, Name = "Soporte" });
        db.Teams.Add(new Team { Id = 2, Name = "Desarrollo" });
        db.SaveChanges();

        db.DescripcionesDeRolDeEquipo.Add(new DescripcionDeRolDeEquipo
        { TeamId = 1, Rol = TeamRole.Fullstack, Descripcion = "Resuelve bugs." });
        db.DescripcionesDeRolDeEquipo.Add(new DescripcionDeRolDeEquipo
        { TeamId = 2, Rol = TeamRole.Fullstack, Descripcion = "Desarrolla características nuevas." });
        db.SaveChanges();

        Assert.Equal(2, db.DescripcionesDeRolDeEquipo.Count());
    }

    /// <summary>
    /// Al borrar el equipo se van sus descripciones. Describen un puesto DENTRO de ese equipo, así que
    /// dejarlas sueltas sería guardar la definición de un puesto que ya no existe — y, con el índice
    /// único puesto, bloquear el identificador para siempre si mañana se recrea el equipo.
    /// </summary>
    [Fact]
    public void ALBORRAR_EL_EQUIPO_se_van_sus_descripciones()
    {
        var db = BaseSinDescripciones();
        DatabaseMigrator.EnsureUpToDate(db);

        db.Teams.Add(new Team { Id = 1, Name = "Soporte" });
        db.SaveChanges();
        db.DescripcionesDeRolDeEquipo.Add(new DescripcionDeRolDeEquipo
        { TeamId = 1, Rol = TeamRole.QA, Descripcion = "Certifica entregas." });
        db.SaveChanges();

        db.Teams.Remove(db.Teams.Find(1)!);
        db.SaveChanges();
        db.ChangeTracker.Clear();

        Assert.Empty(db.DescripcionesDeRolDeEquipo.ToList());
    }
}
