using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LA MARCA DEL AVISO DE INICIO contra una base que YA EXISTE, y la siembra que impide la avalancha.
///
/// <para><b>Por qué la siembra no es opcional.</b> <c>WorkSessions.InicioComentadoEnUtc</c> nace nula,
/// y nulo significa «a esta sesión todavía no se le ha avisado». En una base nueva eso es cierto; en
/// la base real son años de sesiones. Sin sembrar, el primer barrido tras el despliegue leería como
/// pendientes todas las sesiones vivas del equipo y publicaría un comentario por cada una en tickets
/// de clientes —que no se pueden retirar—. A nadie se le avisa hacia atrás.</para>
///
/// <para>Y el candado importa tanto como la siembra: se siembra SOLO en la pasada que crea la
/// columna. Si se sembrara en cada arranque, cada reinicio del servidor mataría en silencio los
/// avisos que estuvieran esperando su turno.</para>
///
/// <para>Se ejerce SQLite, que es el motor que se puede levantar en una prueba; la rama de SQL Server
/// hace lo mismo con <c>IF COL_LENGTH</c> y el <c>UPDATE</c> dentro de un <c>EXEC</c>, y vive al lado
/// en el mismo archivo.</para>
/// </summary>
public class AvisoDeInicioMigracionTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var c in _contextos) c.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>La base tal como era antes de que existiera el aviso, con sesiones ya dentro.</summary>
    private AppDbContext BaseVieja(int cuantasSesiones = 3)
    {
        var db = TestDb.New();
        _contextos.Add(db);

        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev);
        db.SaveChanges();

        for (int i = 0; i < cuantasSesiones; i++)
        {
            var actividad = new DevActivity
            {
                DeveloperId = dev.Id, Title = $"Actividad {i}", Status = DevActivityStatus.Abierta
            };
            db.DevActivities.Add(actividad);
            db.SaveChanges();

            db.WorkSessions.Add(new WorkSession
            {
                DeveloperId = dev.Id,
                ActivityId = actividad.Id,
                StartedAt = DateTime.UtcNow.AddHours(-2),
                LastResumedAt = DateTime.UtcNow.AddHours(-2),
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                Status = WorkSessionStatus.Activa
            });
        }
        db.SaveChanges();

        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""WorkSessions"" DROP COLUMN ""InicioComentadoEnUtc""");
        Assert.DoesNotContain("InicioComentadoEnUtc", Columnas(db, "WorkSessions"));

        return db;
    }

    private static List<string> Columnas(AppDbContext db, string tabla) =>
        db.Database.SqlQueryRaw<string>(@"SELECT name AS ""Value"" FROM pragma_table_info({0})", tabla)
          .AsEnumerable().ToList();

    private static int SesionesSinMarca(AppDbContext db) =>
        db.WorkSessions.AsNoTracking().Count(w => w.InicioComentadoEnUtc == null);

    [Fact]
    public void ElMigrador_creaLaColumnaEnUnaBaseQueYaExistia()
    {
        var db = BaseVieja();

        DatabaseMigrator.EnsureUpToDate(db);

        Assert.Contains("InicioComentadoEnUtc", Columnas(db, "WorkSessions"));
    }

    /// <summary>
    /// La mitad que de verdad protege: al crear la columna, NINGUNA de las sesiones que ya estaban
    /// queda pendiente de aviso.
    /// </summary>
    [Fact]
    public void AlCrearla_ningunaSesionVieja_quedaPendienteDeAviso()
    {
        var db = BaseVieja(cuantasSesiones: 5);

        DatabaseMigrator.EnsureUpToDate(db);
        db.ChangeTracker.Clear();

        Assert.Equal(5, db.WorkSessions.Count());
        Assert.Equal(0, SesionesSinMarca(db));
    }

    /// <summary>
    /// Y la otra mitad, que es la que impide que la siembra se coma lo bueno: una sesión creada
    /// DESPUÉS de la migración sigue pendiente aunque el migrador vuelva a correr. Sin el candado de
    /// «la columna no existía», cada reinicio del servidor la sellaría y su aviso no saldría nunca.
    /// </summary>
    [Fact]
    public void UnaSesionPosterior_sigueEsperandoSuAviso_aunqueElMigradorVuelvaACorrer()
    {
        var db = BaseVieja();
        DatabaseMigrator.EnsureUpToDate(db);
        db.ChangeTracker.Clear();

        int devId = db.Developers.AsNoTracking().First().Id;
        var nueva = new DevActivity
        {
            DeveloperId = devId, Title = "Posterior", Status = DevActivityStatus.Abierta
        };
        db.DevActivities.Add(nueva);
        db.SaveChanges();

        db.WorkSessions.Add(new WorkSession
        {
            DeveloperId = devId,
            ActivityId = nueva.Id,
            StartedAt = DateTime.UtcNow,
            LastResumedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Status = WorkSessionStatus.Activa
        });
        db.SaveChanges();
        Assert.Equal(1, SesionesSinMarca(db));

        DatabaseMigrator.EnsureUpToDate(db);
        db.ChangeTracker.Clear();

        Assert.Equal(1, SesionesSinMarca(db));
    }

    /// <summary>
    /// El centinela es <see cref="DateTime.MinValue"/> y no una fecha cualquiera, para que se
    /// distinga de un aviso publicado de verdad cuando alguien mire la tabla a mano.
    /// </summary>
    [Fact]
    public void ElCentinelaEsInconfundible()
    {
        var db = BaseVieja(cuantasSesiones: 1);

        DatabaseMigrator.EnsureUpToDate(db);
        db.ChangeTracker.Clear();

        Assert.Equal(DateTime.MinValue, db.WorkSessions.AsNoTracking().Single().InicioComentadoEnUtc);
    }
}
