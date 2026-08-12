using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// EL TRAMO DE HORAS CONTRA UNA BASE QUE YA TIENE PERMISOS.
///
/// <para>Las dos columnas nuevas —<c>HoraInicio</c> y <c>HoraFin</c>— se agregan a una tabla que en
/// producción lleva años llenándose. Lo que esta clase comprueba es lo que ninguna otra prueba puede
/// ver: en las bases de prueba las filas se crean POR EL MODELO, así que la situación real —filas
/// escritas antes de que la columna existiera— no aparece jamás. Solo sale contra la base de verdad,
/// que es donde no hay segundo intento.</para>
///
/// <para><b>La comprobación que importa es la última de cada prueba: leer los permisos POR EL
/// MODELO.</b> No que la columna exista, sino que EF pueda materializar la fila. Es la lección de
/// <see cref="SelloDeSesionMigracionTests"/>: allí una columna nueva en NULL sobre una propiedad NO
/// anulable impedía entrar a toda la plantilla, y las 2 042 pruebas de entonces pasaban. Aquí las dos
/// propiedades son <c>TimeOnly?</c> —el NULL significa «permiso de día completo», que es la verdad de
/// todo el histórico— y por eso NO hay relleno; si alguien las volviera no anulables, esta clase se
/// pone roja antes de que la base real se entere.</para>
///
/// <para>La base de partida se construye quitándole a la de hoy las dos columnas, que es exactamente
/// la forma que tiene una que solo ha visto el escritorio. Dándoselas ya creadas, la mitad del
/// trabajo del migrador —crearlas— se quedaría sin probar.</para>
/// </summary>
public class PermisosPorHorasMigracionTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var ctx in _contextos) ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext BaseComoAntesDeLasHoras()
    {
        var db = TestDb.New();
        _contextos.Add(db);

        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""LeaveRequests"" DROP COLUMN ""HoraInicio""");
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""LeaveRequests"" DROP COLUMN ""HoraFin""");

        Assert.False(TieneColumna(db, "HoraInicio"),
            "La base de partida no debería traer ya las columnas del tramo: si las trae, el migrador " +
            "no tendría que crearlas y esa mitad del trabajo se quedaría sin probar.");
        return db;
    }

    /// <summary>Otro contexto contra la misma base, para leer con EF lo que escribió el SQL crudo.</summary>
    private AppDbContext OtroContexto(AppDbContext db)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Database.GetConnectionString()).Options;
        var ctx = new AppDbContext(opts);
        _contextos.Add(ctx);
        return ctx;
    }

    private static bool TieneColumna(AppDbContext db, string columna) =>
        db.Database.SqlQueryRaw<string>(@"SELECT name AS ""Value"" FROM pragma_table_info('LeaveRequests')")
          .AsEnumerable()
          .Any(c => string.Equals(c, columna, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Un permiso como los escribía el escritorio, con SQL crudo porque la tabla todavía no tiene las
    /// columnas del tramo y el modelo de hoy no la sabe escribir sin ellas.
    /// </summary>
    private static void SembrarPermisoDeSiempre(AppDbContext db, int developerId, int dias)
    {
        db.Database.ExecuteSqlRaw(
            @"INSERT INTO ""LeaveRequests""
                (""DeveloperId"", ""Type"", ""Date"", ""DaysCount"", ""Reason"", ""Status"", ""CreatedAt"")
              VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
            developerId, (int)LeaveType.CitaMedica, new DateTime(2026, 9, 10), dias,
            "Cita de control", (int)LeaveStatus.Aprobada, DateTime.UtcNow);
    }

    private static int Desarrollador(AppDbContext db)
    {
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev);
        db.SaveChanges();
        return dev.Id;
    }

    [Fact]
    public void ElMigrador_CREA_LAS_COLUMNAS_DEL_TRAMO_EN_UNA_BASE_QUE_NO_LAS_TENIA()
    {
        var db = BaseComoAntesDeLasHoras();
        SembrarPermisoDeSiempre(db, Desarrollador(db), dias: 2);

        var fallidas = DatabaseMigrator.EnsureUpToDate(db);

        Assert.Empty(fallidas);
        Assert.True(TieneColumna(db, "HoraInicio"));
        Assert.True(TieneColumna(db, "HoraFin"));
    }

    /// <summary>
    /// ESTA es la comprobación que importa, y la que fallaría si algún día las columnas dejaran de ser
    /// anulables sin rellenarlas: que la consulta de «Mis permisos» pueda materializar las filas que ya
    /// estaban. Un permiso viejo se lee y es lo que siempre fue, uno de día completo.
    /// </summary>
    [Fact]
    public void TrasMigrar_LOS_PERMISOS_DE_ANTES_SE_LEEN_POR_EL_MODELO_Y_SON_DE_DIA_COMPLETO()
    {
        var db = BaseComoAntesDeLasHoras();
        SembrarPermisoDeSiempre(db, Desarrollador(db), dias: 2);

        DatabaseMigrator.EnsureUpToDate(db);

        var leido = OtroContexto(db).LeaveRequests.AsNoTracking().Single();

        Assert.Null(leido.HoraInicio);
        Assert.Null(leido.HoraFin);
        Assert.Equal(2, leido.DaysCount);
        Assert.Equal(new DateTime(2026, 9, 11), leido.EndDate);   // dos días: el 10 y el 11
    }

    /// <summary>
    /// Y la base migrada acepta lo nuevo: un tramo se escribe y se vuelve a leer con EF. Una columna
    /// creada con el tipo equivocado se vería justo aquí y no en producción.
    /// </summary>
    [Fact]
    public void TrasMigrar_SE_PUEDE_GUARDAR_UN_PERMISO_POR_HORAS()
    {
        var db = BaseComoAntesDeLasHoras();
        int devId = Desarrollador(db);
        SembrarPermisoDeSiempre(db, devId, dias: 1);

        DatabaseMigrator.EnsureUpToDate(db);

        var escritor = OtroContexto(db);
        escritor.LeaveRequests.Add(new LeaveRequest
        {
            DeveloperId = devId,
            Type = LeaveType.CitaMedica,
            Date = new DateTime(2026, 9, 15),
            DaysCount = 1,
            HoraInicio = new TimeOnly(9, 0),
            HoraFin = new TimeOnly(11, 30),
            Reason = "Dentista",
            Status = LeaveStatus.Pendiente,
            CreatedAt = DateTime.UtcNow
        });
        escritor.SaveChanges();

        var guardado = OtroContexto(db).LeaveRequests.AsNoTracking()
            .Single(l => l.Date == new DateTime(2026, 9, 15));

        Assert.Equal(new TimeOnly(9, 0), guardado.HoraInicio);
        Assert.Equal(new TimeOnly(11, 30), guardado.HoraFin);
    }

    /// <summary>
    /// El migrador corre en CADA arranque —cada despliegue, cada reinicio, cada instancia—, así que
    /// pasarlo tres veces tiene que dejar la base como la deja una: sin tocar el tramo que alguien
    /// haya capturado entre dos arranques.
    /// </summary>
    [Fact]
    public void Aplicarlo_VARIAS_VECES_NO_TOCA_LO_QUE_YA_HAY()
    {
        var db = BaseComoAntesDeLasHoras();
        int devId = Desarrollador(db);
        SembrarPermisoDeSiempre(db, devId, dias: 2);

        DatabaseMigrator.EnsureUpToDate(db);

        var escritor = OtroContexto(db);
        escritor.LeaveRequests.Add(new LeaveRequest
        {
            DeveloperId = devId,
            Type = LeaveType.PermisoPersonal,
            Date = new DateTime(2026, 9, 20),
            DaysCount = 1,
            HoraInicio = new TimeOnly(13, 0),
            HoraFin = new TimeOnly(15, 0),
            Reason = "Trámite en el banco",
            Status = LeaveStatus.Aprobada,
            CreatedAt = DateTime.UtcNow
        });
        escritor.SaveChanges();

        DatabaseMigrator.EnsureUpToDate(db);   // el arranque siguiente
        DatabaseMigrator.EnsureUpToDate(db);   // y el de después

        var leido = OtroContexto(db);
        var conTramo = leido.LeaveRequests.AsNoTracking().Single(l => l.Date == new DateTime(2026, 9, 20));
        Assert.Equal(new TimeOnly(13, 0), conTramo.HoraInicio);
        Assert.Equal(new TimeOnly(15, 0), conTramo.HoraFin);

        // Y el viejo sigue sin tramo: nadie le inventó una jornada de oficina.
        var deSiempre = leido.LeaveRequests.AsNoTracking().Single(l => l.Date == new DateTime(2026, 9, 10));
        Assert.Null(deSiempre.HoraInicio);
        Assert.Null(deSiempre.HoraFin);
    }
}
