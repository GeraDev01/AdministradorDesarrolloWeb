using AdminWeb.Application.Services;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Personas;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LA COLUMNA DEL PADRE contra una base de equipos que YA EXISTE.
///
/// <para><b>Por qué esta prueba y no solo las del árbol.</b> <c>Teams.EquipoPadreId</c> es una columna
/// nueva. En una base nueva la crea sola <c>EnsureCreated</c> desde el modelo, así que la suite entera
/// puede pasar en verde sin que nadie ejerza el parche; pero <c>EnsureCreated</c> NO altera una base
/// que ya existe, y contra la de producción —la que lleva años de equipos— la columna solo aparece si
/// el migrador la añade. Sin ella, EF la pide en CADA consulta de <c>Teams</c>: no se rompería el
/// organigrama, se rompería todo lo que lea un equipo.</para>
///
/// <para><b>Y aquí NO hay nada que rellenar</b>, que es la otra mitad de lo que se comprueba. Al sello
/// de sesión le pasó lo contrario y dejó fuera a todo el mundo: una columna nueva en NULL sobre una
/// propiedad que el modelo declara NO anulable hace que EF no pueda materializar la fila. Ésta es
/// <c>int?</c> y el NULL significa «equipo raíz», que es exactamente lo que son todos los equipos que
/// ya existían. La comprobación que lo demuestra es la de leerlos por el modelo, no la de mirar el
/// esquema.</para>
///
/// <para>Se ejerce SQLite, que es el motor que se puede levantar en una prueba. La rama de SQL Server
/// escribe la MISMA columna con los tipos de aquel motor —<c>IF COL_LENGTH</c>, <c>int NULL</c> y su
/// clave foránea aparte— y vive al lado en el mismo archivo, traducida y no copiada.</para>
/// </summary>
public class SubequiposMigracionTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var c in _contextos) c.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Una base con la tabla de equipos tal como era ANTES de que existieran los subequipos, y con
    /// equipos dentro.
    ///
    /// <para>Se REHACE la tabla en vez de quitarle la columna: SQLite no deja soltar una columna que
    /// está en una clave foránea, y ésta lo está —se apunta a sí misma—. Rehacerla entera es además
    /// lo más parecido a lo que hay de verdad allá afuera: una tabla creada cuando la columna no se
    /// había inventado.</para>
    /// </summary>
    private AppDbContext BaseComoAntesDelPadre(params string[] equipos)
    {
        var db = TestDb.New();
        _contextos.Add(db);

        // Se tira ANTES de meter nada: sin filas hijas de por medio, soltar la tabla no arrastra a
        // nadie, y al volver a crearla con el mismo nombre las demás tablas la vuelven a encontrar.
        db.Database.ExecuteSqlRaw(@"DROP TABLE ""Teams""");
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE ""Teams"" (
                ""Id""              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""Name""            TEXT    NOT NULL,
                ""Description""     TEXT,
                ""ColorHex""        TEXT,
                ""LeadDeveloperId"" INTEGER,
                ""CreatedAt""       TEXT    NOT NULL
            );");

        for (int i = 0; i < equipos.Length; i++)
        {
            db.Database.ExecuteSqlRaw(
                @"INSERT INTO ""Teams"" (""Id"", ""Name"", ""CreatedAt"") VALUES ({0}, {1}, '2024-01-01 00:00:00')",
                i + 1, equipos[i]);
        }

        Assert.DoesNotContain("EquipoPadreId", Columnas(db));
        return db;
    }

    private static List<string> Columnas(AppDbContext db) =>
        db.Database.SqlQueryRaw<string>(@"SELECT name AS ""Value"" FROM pragma_table_info('Teams')")
          .AsEnumerable().ToList();

    [Fact]
    public void ElMigrador_AgregaLaColumnaDelPadreAUnaBaseQueYaExistia()
    {
        var db = BaseComoAntesDelPadre("Plataforma", "Producto");

        var fallidas = DatabaseMigrator.EnsureUpToDate(db);

        // Una lista vacía es la única señal honesta de que todo se aplicó.
        Assert.Empty(fallidas);
        Assert.Contains("EquipoPadreId", Columnas(db));
    }

    [Fact]
    public void ElMigrador_EsIdempotente_CorreDosVecesSinRomperNada()
    {
        // La API arranca en cada despliegue y en cada instancia nueva: el parche corre muchas veces
        // contra la misma base. Un ADD COLUMN que no sea idempotente no falla el día que se escribe,
        // falla en el segundo arranque con la aplicación ya en marcha.
        var db = BaseComoAntesDelPadre("Plataforma");

        DatabaseMigrator.EnsureUpToDate(db);
        Assert.Empty(DatabaseMigrator.EnsureUpToDate(db));
        Assert.Contains("EquipoPadreId", Columnas(db));
    }

    [Fact]
    public void LosEquiposDeAntes_SeLeenPorElModelo_yQuedanComoRaiz()
    {
        // ESTA es la comprobación que importa: no que la columna exista, sino que EF pueda
        // materializar las filas que ya estaban. Con la propiedad en «int?» el NULL es un valor
        // legítimo —equipo raíz— y no hay ningún relleno que hacer; si algún día alguien la declara
        // no anulable, esta prueba se pone roja aquí y no en producción.
        var db = BaseComoAntesDelPadre("Plataforma", "Producto");

        DatabaseMigrator.EnsureUpToDate(db);
        db.ChangeTracker.Clear();

        var equipos = db.Teams.AsNoTracking().OrderBy(t => t.Id).ToList();

        Assert.Equal(2, equipos.Count);
        Assert.All(equipos, e => Assert.Null(e.EquipoPadreId));
    }

    [Fact]
    public async Task TrasMigrar_unEquipoDeAntesPuedeColgarDeOtro_yElOrganigramaLoEnsena()
    {
        // Que la columna exista con el nombre correcto no basta: lo que cuenta es que se pueda
        // escribir y leer contra ELLA, no contra la que crearía EnsureCreated desde el modelo.
        var db = BaseComoAntesDelPadre("Plataforma", "Producto");
        DatabaseMigrator.EnsureUpToDate(db);

        var servicio = ServicioDePersonas(db);

        var (ok, _) = await servicio.GuardarEquipoAsync(new GuardarEquipoRequest(2, "Producto", null, null, 1));
        Assert.True(ok);

        var organigrama = await servicio.OrganigramaAsync();
        var producto = organigrama.Equipos.Single(e => e.Nombre == "Producto");

        Assert.Equal(1, producto.EquipoPadreId);
        Assert.Equal(1, producto.Nivel);
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
