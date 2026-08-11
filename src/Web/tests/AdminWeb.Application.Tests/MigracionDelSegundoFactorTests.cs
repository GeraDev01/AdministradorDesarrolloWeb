using AdminWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LA MIGRACIÓN DEL SEGUNDO FACTOR contra una base que ya existe.
///
/// <para><b>Qué se prueba y por qué importa tanto.</b> La base de producción lleva años de datos y
/// once cuentas dentro. El segundo factor le añade tres columnas a <c>Users</c> y dos tablas, y eso
/// tiene que ocurrir sin borrar ni renombrar nada y sin depender de que el arranque sea el primero:
/// la API arranca en cada despliegue, en cada reinicio y en cada instancia nueva, así que el
/// migrador corre muchas veces contra la misma base. Si algún parche no fuera idempotente, el fallo
/// no saldría el día que se escribe sino en el segundo arranque, con la aplicación ya en marcha.</para>
///
/// <para><b>Cómo se simula la base vieja.</b> Se crea con el modelo de hoy y se le QUITA lo del
/// segundo factor, que es exactamente la forma que tiene una base que todavía no lo ha visto. Así el
/// migrador tiene que crearlo de verdad; si se le diera ya hecho, esa mitad del trabajo se quedaría
/// sin probar.</para>
///
/// <para>Solo se ejerce el dialecto de SQLite, que es el que se puede levantar en una prueba. La
/// rama de SQL Server escribe LAS MISMAS columnas con los tipos de ese motor y va guardada al lado
/// en el mismo archivo, para que quien toque una vea la otra.</para>
/// </summary>
public class MigracionDelSegundoFactorTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var ctx in _contextos) ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Una base con la forma que tiene una que todavía no conoce el segundo factor: con sus usuarios
    /// dentro y sin ninguna de las piezas nuevas.
    /// </summary>
    private AppDbContext BaseSinSegundoFactor()
    {
        var db = TestDb.New();
        _contextos.Add(db);

        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Users"" DROP COLUMN ""SegundoFactorActivo""");
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Users"" DROP COLUMN ""SegundoFactorDesdeUtc""");
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Users"" DROP COLUMN ""SegundoFactorUltimaVentana""");
        db.Database.ExecuteSqlRaw(@"DROP TABLE ""UserRecoveryCodes""");
        db.Database.ExecuteSqlRaw(@"DROP TABLE ""UserTrustedDevices""");

        Assert.False(TieneColumna(db, "Users", "SegundoFactorActivo"),
            "La base de partida no debería traer ya lo del segundo factor: si lo trae, el migrador " +
            "no tendría que crearlo y esa mitad del trabajo se quedaría sin probar.");

        // Un usuario de los de siempre, escrito con SQL crudo porque el modelo de EF ya conoce
        // columnas que esta base todavía no tiene.
        db.Database.ExecuteSqlRaw(
            @"INSERT INTO ""Users"" (""Username"", ""PasswordHash"", ""FullName"", ""Role"", ""IsActive"",
                                     ""MustChangePassword"", ""CreatedAt"", ""FailedLoginCount"", ""SecurityStamp"")
              VALUES ('ana', 'hash-de-siempre', 'Ana', 1, 1, 0, {0}, 0, 'sello')", DateTime.UtcNow);

        return db;
    }

    private static bool TieneColumna(AppDbContext db, string tabla, string columna)
    {
        var conn = db.Database.GetDbConnection();
        bool abrir = conn.State != System.Data.ConnectionState.Open;
        if (abrir) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info('{tabla}')";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (string.Equals(r.GetString(1), columna, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        finally { if (abrir) conn.Close(); }
    }

    private static bool TieneTabla(AppDbContext db, string tabla) =>
        Escalar(db, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tabla}'") > 0;

    private static bool TieneIndice(AppDbContext db, string indice) =>
        Escalar(db, $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{indice}'") > 0;

    private static long Escalar(AppDbContext db, string sql)
    {
        var conn = db.Database.GetDbConnection();
        bool abrir = conn.State != System.Data.ConnectionState.Open;
        if (abrir) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
        finally { if (abrir) conn.Close(); }
    }

    [Fact]
    public void ElMigrador_CreaLasTresColumnasYLasDosTablas()
    {
        var db = BaseSinSegundoFactor();

        DatabaseMigrator.EnsureUpToDate(db);

        Assert.True(TieneColumna(db, "Users", "SegundoFactorActivo"));
        Assert.True(TieneColumna(db, "Users", "SegundoFactorDesdeUtc"));
        Assert.True(TieneColumna(db, "Users", "SegundoFactorUltimaVentana"));

        Assert.True(TieneTabla(db, "UserRecoveryCodes"));
        Assert.True(TieneTabla(db, "UserTrustedDevices"));

        // Los índices únicos son parte de la regla, no un adorno: sin ellos, un mismo código de
        // rescate podría darse de alta dos veces y un testigo de navegador podría duplicarse.
        Assert.True(TieneIndice(db, "UX_UserRecoveryCodes_User_Hash"));
        Assert.True(TieneIndice(db, "UX_UserTrustedDevices_Token"));
    }

    [Fact]
    public void LasCuentasQueYaExistian_QuedanSinSegundoFactor()
    {
        // Es lo que hace que sea obligatorio para todos al desplegar: nadie lo tiene, así que a todo
        // el mundo se le exige darlo de alta. Si la columna llegara en cierto, once cuentas se
        // quedarían marcadas como protegidas sin tener ningún secreto detrás — o sea, fuera.
        var db = BaseSinSegundoFactor();

        DatabaseMigrator.EnsureUpToDate(db);

        Assert.Equal(0, Escalar(db, @"SELECT COUNT(*) FROM ""Users"" WHERE ""SegundoFactorActivo"" <> 0"));
        Assert.Equal(1, Escalar(db, @"SELECT COUNT(*) FROM ""Users"" WHERE ""SegundoFactorActivo"" = 0"));
    }

    [Fact]
    public void ElMigrador_SePuedeCorrerLasVecesQueHagaFalta()
    {
        // La API arranca en cada despliegue, en cada reinicio y en cada instancia nueva. Un parche
        // que solo aguantara la primera pasada rompería el segundo arranque, con todo ya en marcha.
        var db = BaseSinSegundoFactor();

        DatabaseMigrator.EnsureUpToDate(db);
        DatabaseMigrator.EnsureUpToDate(db);
        DatabaseMigrator.EnsureUpToDate(db);

        Assert.True(TieneColumna(db, "Users", "SegundoFactorUltimaVentana"));
        Assert.True(TieneTabla(db, "UserRecoveryCodes"));
        Assert.True(TieneTabla(db, "UserTrustedDevices"));

        // Y ni una columna ni un índice duplicados por el camino.
        Assert.Equal(1, Escalar(db,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='UX_UserRecoveryCodes_User_Hash'"));
    }

    [Fact]
    public void ElMigrador_NoSeLlevaPorDelanteLoQueYaHabia()
    {
        // La regla que no se negocia: se añade, nunca se borra ni se renombra. Los usuarios y sus
        // contraseñas tienen que sobrevivir intactos a la migración.
        var db = BaseSinSegundoFactor();

        DatabaseMigrator.EnsureUpToDate(db);

        Assert.Equal(1, Escalar(db,
            @"SELECT COUNT(*) FROM ""Users"" WHERE ""Username"" = 'ana' AND ""PasswordHash"" = 'hash-de-siempre'"));
    }

    [Fact]
    public void SobreUnaBaseNUEVA_TodoQuedaIgualQueTrasMigrarUnaVieja()
    {
        // Las bases nuevas las crea EF con el modelo, no el migrador. Si las dos formas no
        // coincidieran, el código funcionaría en un sitio y no en el otro — y el sitio donde
        // fallaría sería producción, que es la vieja.
        var nueva = TestDb.New();
        _contextos.Add(nueva);

        DatabaseMigrator.EnsureUpToDate(nueva);

        Assert.True(TieneColumna(nueva, "Users", "SegundoFactorActivo"));
        Assert.True(TieneColumna(nueva, "Users", "SegundoFactorDesdeUtc"));
        Assert.True(TieneColumna(nueva, "Users", "SegundoFactorUltimaVentana"));
        Assert.True(TieneTabla(nueva, "UserRecoveryCodes"));
        Assert.True(TieneTabla(nueva, "UserTrustedDevices"));
    }

    [Fact]
    public void LaTablaDeCodigos_NoDejaGuardarDosVecesElMismo()
    {
        var db = BaseSinSegundoFactor();
        DatabaseMigrator.EnsureUpToDate(db);

        db.Database.ExecuteSqlRaw(
            @"INSERT INTO ""UserRecoveryCodes"" (""UserId"", ""CodigoHash"", ""CreatedAtUtc"") VALUES (1, 'abc', {0})",
            DateTime.UtcNow);

        Assert.ThrowsAny<Exception>(() => db.Database.ExecuteSqlRaw(
            @"INSERT INTO ""UserRecoveryCodes"" (""UserId"", ""CodigoHash"", ""CreatedAtUtc"") VALUES (1, 'abc', {0})",
            DateTime.UtcNow));
    }

    [Fact]
    public void AlBorrarUnaCuenta_SeVanSusCodigosYSusEquipos()
    {
        // En cascada a propósito: un código de rescate o un navegador de confianza sin dueño no
        // significan nada, y dejarlos sería dejar hashes de llaves de acceso huérfanos en la base.
        var db = BaseSinSegundoFactor();
        DatabaseMigrator.EnsureUpToDate(db);

        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON");
        db.Database.ExecuteSqlRaw(
            @"INSERT INTO ""UserRecoveryCodes"" (""UserId"", ""CodigoHash"", ""CreatedAtUtc"") VALUES (1, 'abc', {0})",
            DateTime.UtcNow);
        db.Database.ExecuteSqlRaw(
            @"INSERT INTO ""UserTrustedDevices"" (""UserId"", ""TokenHash"", ""CreatedAtUtc"", ""ExpiraEnUtc"")
              VALUES (1, 'def', {0}, {1})", DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

        db.Database.ExecuteSqlRaw(@"DELETE FROM ""Users"" WHERE ""Id"" = 1");

        Assert.Equal(0, Escalar(db, @"SELECT COUNT(*) FROM ""UserRecoveryCodes"""));
        Assert.Equal(0, Escalar(db, @"SELECT COUNT(*) FROM ""UserTrustedDevices"""));
    }
}
