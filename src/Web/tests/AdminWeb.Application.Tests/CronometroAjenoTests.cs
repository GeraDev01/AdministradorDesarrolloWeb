using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// NADIE CRONOMETRA LA ACTIVIDAD DE OTRO.
///
/// <para><b>De dónde sale esto.</b> <see cref="WorkSessionService.StartOrResumeAsync"/> comprobaba de
/// quién era la SESIÓN —y el identificador que comprueba es el del propio llamante, así que esa
/// guarda siempre pasaba— pero nunca de quién era el OBJETIVO, cuyo número llega en el cuerpo de la
/// petición. Mandar el identificador de la actividad de otra persona abría un cronómetro contra su
/// trabajo.</para>
///
/// <para>Era un fallo de contabilidad interna mientras el cronómetro solo escribía aquí. Deja de
/// serlo en cuanto arrancar publica un aviso en Azure DevOps: entonces es escribir en el ticket de
/// un cliente ajeno, firmado con el token de la instalación y con tu nombre dentro del texto.</para>
///
/// <para>La comprobación está DOS veces a propósito: en el servicio, que es donde vale para
/// cualquiera que lo llame, y como consulta previa para que el endpoint pueda contestar un 400 con
/// su motivo en vez del 500 en que se convierte una <see cref="ArgumentException"/>.</para>
/// </summary>
public class CronometroAjenoTests
{
    private static WorkSessionService Svc(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, new AuditService(db, cu, new OrigenDePrueba()));

    /// <summary>Dos desarrolladores y una actividad de cada uno.</summary>
    private static (int mio, int suyo, int actividadMia, int actividadSuya) Escenario(AppDbContext db)
    {
        var ana = new Developer { FullName = "Ana", IsActive = true };
        var beto = new Developer { FullName = "Beto", IsActive = true };
        db.Developers.AddRange(ana, beto);
        db.SaveChanges();

        var deAna = new DevActivity { DeveloperId = ana.Id, Title = "Soporte", Status = DevActivityStatus.Abierta };
        var deBeto = new DevActivity { DeveloperId = beto.Id, Title = "Junta", Status = DevActivityStatus.Abierta };
        db.DevActivities.AddRange(deAna, deBeto);
        db.SaveChanges();

        return (ana.Id, beto.Id, deAna.Id, deBeto.Id);
    }

    [Fact]
    public async Task NoSePuedeCronometrarLaActividadDeOtro()
    {
        using var db = TestDb.New();
        var (mio, _, _, actividadSuya) = Escenario(db);
        var work = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: mio));

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            work.StartOrResumeAsync(mio, WorkTarget.Actividad(actividadSuya)));

        Assert.Empty(db.WorkSessions);
    }

    /// <summary>Y el endpoint puede saberlo ANTES, que es lo que convierte el 500 en un 400 con motivo.</summary>
    [Fact]
    public async Task LaConsultaPrevia_loDiceConSuMotivo()
    {
        using var db = TestDb.New();
        var (mio, _, _, actividadSuya) = Escenario(db);
        var work = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: mio));

        var (puede, motivo) = await work.PuedeCronometrarAsync(mio, WorkTarget.Actividad(actividadSuya));

        Assert.False(puede);
        Assert.Contains("otra persona", motivo);
    }

    /// <summary>La propia, por supuesto, sí.</summary>
    [Fact]
    public async Task LaActividadPropiaSeCronometraNormal()
    {
        using var db = TestDb.New();
        var (mio, _, actividadMia, _) = Escenario(db);
        var work = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: mio));

        var (puede, _) = await work.PuedeCronometrarAsync(mio, WorkTarget.Actividad(actividadMia));
        Assert.True(puede);

        var sesion = await work.StartOrResumeAsync(mio, WorkTarget.Actividad(actividadMia));
        Assert.Equal(WorkSessionStatus.Activa, sesion.Status);
    }

    /// <summary>
    /// El líder cronometrando EN NOMBRE de alguien sigue funcionando, y lo que tiene que cuadrar es
    /// la actividad con esa persona, no con él. Por eso la comprobación es contra el identificador
    /// que se recibe y no contra quien está en sesión: al revés, un líder no podría arrancar el
    /// cronómetro de nadie y a la vez podría arrancar el suyo sobre la actividad de cualquiera.
    /// </summary>
    [Fact]
    public async Task ElLiderPuedeCronometrarEnNombreDelDuenno_peroNoCruzarActividades()
    {
        using var db = TestDb.New();
        var (mio, suyo, actividadMia, actividadSuya) = Escenario(db);
        var work = Svc(db, UsuarioDePrueba.Como(UserRole.Admin, developerId: null));

        // A nombre de Beto, sobre la actividad de Beto: correcto.
        var sesion = await work.StartOrResumeAsync(suyo, WorkTarget.Actividad(actividadSuya));
        Assert.Equal(suyo, sesion.DeveloperId);

        // A nombre de Beto, sobre la actividad de Ana: no.
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            work.StartOrResumeAsync(suyo, WorkTarget.Actividad(actividadMia)));
    }

    /// <summary>
    /// Una actividad CERRADA ya rindió cuentas: su tiempo está consolidado y, si el líder la
    /// calificó, también pagado. Volver a medir contra ella cambiaría un total que ya se usó para
    /// dar puntos.
    /// </summary>
    [Fact]
    public async Task UnaActividadCerradaNoSeVuelveACronometrar()
    {
        using var db = TestDb.New();
        var (mio, _, actividadMia, _) = Escenario(db);
        db.DevActivities.Find(actividadMia)!.Status = DevActivityStatus.Cerrada;
        db.SaveChanges();

        var work = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: mio));

        var (puede, motivo) = await work.PuedeCronometrarAsync(mio, WorkTarget.Actividad(actividadMia));
        Assert.False(puede);
        Assert.Contains("cerrada", motivo, StringComparison.OrdinalIgnoreCase);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            work.StartOrResumeAsync(mio, WorkTarget.Actividad(actividadMia)));
    }

    /// <summary>Una actividad que no existe se cuenta como petición mal formada, no como un 500.</summary>
    [Fact]
    public async Task UnaActividadQueNoExisteSeRechazaConMotivo()
    {
        using var db = TestDb.New();
        var (mio, _, _, _) = Escenario(db);
        var work = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: mio));

        var (puede, motivo) = await work.PuedeCronometrarAsync(mio, WorkTarget.Actividad(99999));

        Assert.False(puede);
        Assert.Contains("no existe", motivo);
    }

    /// <summary>Los requerimientos siguen como estaban: esta guarda no los toca, y decirlo en una
    /// prueba evita que alguien «arregle» de paso quince pruebas que siembran requerimientos sin
    /// asignación. Lo que sí está tapado es que ese trabajo salga hacia DevOps.</summary>
    [Fact]
    public async Task LosRequerimientosNoCambianDeComportamiento()
    {
        using var db = TestDb.New();
        var (mio, _, _, _) = Escenario(db);
        db.Requirements.Add(new Requirement
        {
            Title = "De nadie", Status = RequirementStatus.Estimado, CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
        int reqId = db.Requirements.Single().Id;

        var work = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: mio));

        var (puede, _) = await work.PuedeCronometrarAsync(mio, WorkTarget.Requerimiento(reqId));
        Assert.True(puede);
    }
}
