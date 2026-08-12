using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// EL SELLO DE SESIÓN contra cuentas que ya existían antes de que la columna existiera.
///
/// <para><b>Esto tumbaba el corte entero.</b> <c>Users.SecurityStamp</c> es una columna nueva: el
/// migrador la agrega anulable —no hay forma de agregarla NOT NULL sin dar un valor— y durante un
/// tiempo se quedó ahí, con un comentario que decía que el NULL era deliberado. El razonamiento era
/// sobre el SIGNIFICADO del sello y no era descabellado. El problema ocurre mucho antes de que el
/// significado importe: <c>User.SecurityStamp</c> es <c>string</c> NO anulable, así que una fila con
/// NULL no es «una cuenta sin sello», es una fila que EF no puede materializar. La consulta de
/// <c>AuthService.LoginAsync</c> revienta con <c>SqlNullValueException</c> y <b>no entra nadie</b>.</para>
///
/// <para><b>Por qué no lo veía ninguna prueba.</b> En las bases de prueba los usuarios se crean por
/// el modelo, que ya trae el sello puesto en su inicializador, así que el NULL no aparece jamás. Solo
/// sale contra una base que YA tenía cuentas cuando se agregó la columna — o sea, contra producción y
/// nada más. Se encontró levantando la aplicación contra una copia de la base real; las 2.042 pruebas
/// pasaban.</para>
///
/// <para>Aquí se reconstruye esa situación a mano: una cuenta con el sello en NULL, como quedan todas
/// las que existían antes. Si el relleno del migrador desaparece, la última comprobación —leer al
/// usuario por el modelo, que es lo que hace el login— vuelve a fallar.</para>
/// </summary>
public class SelloDeSesionMigracionTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var c in _contextos) c.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Una base con cuentas como las que había ANTES de que la columna del sello existiera.
    ///
    /// <para>Hay que reconstruir la tabla, y esa incomodidad es justo la razón por la que el defecto
    /// llegó hasta producción. <c>EnsureCreated</c> crea <c>SecurityStamp</c> como NOT NULL, porque
    /// el modelo la declara no anulable: en una base de prueba <b>el estado de producción no se puede
    /// ni escribir</b> — un <c>UPDATE ... SET SecurityStamp = NULL</c> lo rechaza el propio motor. La
    /// única forma de reproducirlo es dejar la tabla como estaba antes, sin esa columna, y dejar que
    /// el migrador la agregue él, que es lo que pasó de verdad contra la base real.</para>
    /// </summary>
    private AppDbContext BaseComoAntesDelSello(params string[] usuarios)
    {
        var db = TestDb.New();
        _contextos.Add(db);

        foreach (var n in usuarios)
        {
            db.Users.Add(new User
            {
                Username = n,
                PasswordHash = "$2a$12$loquesea",
                FullName = "Cuenta de antes",
                Role = UserRole.Admin,
                IsActive = true,
            });
        }
        db.SaveChanges();

        QuitarLaColumnaDelSello(db);
        Assert.DoesNotContain("SecurityStamp", Columnas(db));

        return db;
    }

    /// <summary>
    /// Deja la tabla de usuarios sin la columna del sello, conservando los datos. SQLite no sabe
    /// quitar una columna, así que se rehace la tabla con las demás; las columnas se leen del propio
    /// esquema para que esto no caduque en cuanto alguien agregue una.
    /// </summary>
    private static void QuitarLaColumnaDelSello(AppDbContext db)
    {
        var otras = Columnas(db).Where(c => c != "SecurityStamp").ToList();
        var lista = string.Join(", ", otras.Select(c => $@"""{c}"""));

        // Concatenado y no interpolado: con interpolación, el analizador de EF avisa de inyección de
        // SQL. Aquí los nombres salen del propio esquema y no de nadie de fuera, pero un aviso
        // apagado con un comentario envejece peor que evitarlo.
        var rehacer = @"CREATE TABLE ""Users"" AS SELECT " + lista + @" FROM ""Users_antes""";

        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Users"" RENAME TO ""Users_antes""");
        db.Database.ExecuteSqlRaw(rehacer);
        db.Database.ExecuteSqlRaw(@"DROP TABLE ""Users_antes""");
    }

    private static List<string> Columnas(AppDbContext db) =>
        db.Database.SqlQueryRaw<string>(@"SELECT name AS ""Value"" FROM pragma_table_info('Users')")
          .AsEnumerable().ToList();

    private static int SinSello(AppDbContext db) =>
        db.Database.SqlQueryRaw<int>(
            @"SELECT COUNT(*) AS ""Value"" FROM ""Users"" WHERE ""SecurityStamp"" IS NULL")
          .AsEnumerable().First();

    [Fact]
    public void ElMigrador_LE_PONE_SELLO_A_LAS_CUENTAS_QUE_YA_ESTABAN()
    {
        var db = BaseComoAntesDelSello("antigua");

        var fallidas = DatabaseMigrator.EnsureUpToDate(db);

        Assert.Empty(fallidas);
        Assert.Contains("SecurityStamp", Columnas(db));
        Assert.Equal(0, SinSello(db));
    }

    [Fact]
    public void TrasMigrar_LA_CUENTA_ANTIGUA_SE_PUEDE_LEER_POR_EL_MODELO()
    {
        // ESTA es la comprobación que importa, y la que fallaba: no que la columna tenga algo, sino
        // que la consulta del login pueda materializar la fila. Es la misma forma que usa
        // AuthService.LoginAsync —buscar por nombre y cuenta activa— porque es donde reventaba.
        var db = BaseComoAntesDelSello("antigua");

        DatabaseMigrator.EnsureUpToDate(db);
        db.ChangeTracker.Clear();

        var usuario = db.Users.AsNoTracking()
            .FirstOrDefault(u => u.Username == "antigua" && u.IsActive);

        Assert.NotNull(usuario);
        Assert.False(string.IsNullOrWhiteSpace(usuario!.SecurityStamp),
            "La cuenta se leyó pero sin sello: el relleno del migrador no corrió.");
    }

    [Fact]
    public void CadaCuenta_RECIBE_SU_PROPIO_SELLO_Y_NO_UNO_COMPARTIDO()
    {
        // Un sello igual para todos serviría para materializar la fila y dejaría el agujero abierto:
        // el sello existe para invalidar las sesiones de UNA cuenta —al cambiar su contraseña o
        // darla de baja—, y si todas comparten valor, cambiar el de una no distingue a nadie.
        var db = BaseComoAntesDelSello("una", "otra", "tercera");

        DatabaseMigrator.EnsureUpToDate(db);
        db.ChangeTracker.Clear();

        var sellos = db.Users.AsNoTracking().Select(u => u.SecurityStamp).ToList();

        Assert.Equal(3, sellos.Count);
        Assert.All(sellos, s => Assert.False(string.IsNullOrWhiteSpace(s)));
        Assert.Equal(3, sellos.Distinct().Count());
    }
}
