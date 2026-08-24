using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// CALIFICAR UNA ACTIVIDAD LIBRE: que el trabajo que un desarrollador registra por su cuenta —lo que
/// no cabe en el pool ni viene de un ticket— pueda contar en su desempeño.
///
/// <para><b>Lo que estas pruebas cuidan.</b> Que se pague UNA vez y solo una; que no se pague dos
/// veces el mismo trabajo por dos caminos —una actividad del pool arrastra una actividad libre como
/// percha del cronómetro, y ésa ya cobra por el pool—; y que los minutos que quedan escritos sean los
/// MEDIDOS por el cronómetro y no un número que alguien declaró, que es lo que separa esto de la
/// vieja autocalificación libre.</para>
/// </summary>
public class CalificarActividadLibreTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var ctx in _contextos) ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext OtroContexto(AppDbContext db)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Database.GetConnectionString()).Options;
        var ctx = new AppDbContext(opts);
        _contextos.Add(ctx);
        return ctx;
    }

    private static UsuarioDePrueba Admin(int userId = 9) => UsuarioDePrueba.Como(UserRole.Admin, userId: userId);

    private DevActivityService Servicio(AppDbContext db, ICurrentUser cu)
    {
        var ctx = OtroContexto(db);
        var bitacora = new AuditService(ctx, cu, new OrigenDePrueba());
        return new DevActivityService(ctx, cu, bitacora, new WorkSessionService(ctx, cu, bitacora));
    }

    private static async Task<(AppDbContext db, int devId, int criterioId)> BaseListaAsync()
    {
        var db = TestDb.New();

        var dev = new Developer { FullName = "Ana Pérez", IsActive = true };
        db.Developers.Add(dev);

        var criterio = new ScoringCriterion
        {
            Name = "Resolviste un incidente crítico", Description = "Un problema urgente, sacado rápido.",
            DefaultPoints = 10, IsActive = true, Scope = CriterionScope.Individual
        };
        db.ScoringCriteria.Add(criterio);

        await db.SaveChangesAsync();
        return (db, dev.Id, criterio.Id);
    }

    /// <param name="poolActivityId">La marca de percha. Es lo que el servicio mira desde que
    /// preguntar por <c>PoolActivity.LinkedDevActivityId</c> resultó ser una fuga: ese vínculo lo
    /// borra <c>SoltarReclamo</c> al devolver la actividad, y entonces la percha dejaba de
    /// reconocerse. Sembrar solo el vínculo de vuelta ya no basta para armar una percha.</param>
    private static async Task<int> ActividadCerradaAsync(AppDbContext db, int devId, string titulo = "Investigar la caída",
        int? poolActivityId = null)
    {
        var actividad = new DevActivity
        {
            DeveloperId = devId, Title = titulo,
            Status = DevActivityStatus.Cerrada, ClosedAt = DateTime.UtcNow,
            PoolActivityId = poolActivityId
        };
        db.DevActivities.Add(actividad);
        await db.SaveChangesAsync();
        return actividad.Id;
    }

    private static Task<DevActivity> LeerAsync(AppDbContext db, int id) =>
        db.DevActivities.AsNoTracking().FirstAsync(a => a.Id == id);

    // ── 1. El camino bueno ───────────────────────────────────────────────────────

    [Fact]
    public async Task Calificar_abonaLosPuntosYDejaLaTraza()
    {
        var (db, devId, criterioId) = await BaseListaAsync();
        using var _ = db;

        int actividadId = await ActividadCerradaAsync(db, devId);

        var (ok, mensaje) = await Servicio(db, Admin())
            .CalificarAsync(actividadId, criterioId, 10, "Lo sacó en dos horas un domingo.");

        Assert.True(ok, mensaje);

        var entrada = Assert.Single(await db.PointEntries.AsNoTracking().ToListAsync());
        Assert.Equal(devId, entrada.DeveloperId);
        Assert.Equal(criterioId, entrada.CriterionId);
        Assert.Equal(10, entrada.Points);
        Assert.Contains("Investigar la caída", entrada.Comment);
        Assert.Contains("domingo", entrada.Comment);

        // Nace APROBADA: el juicio lo hizo quien podía hacerlo. Mandarla a la cola de aprobación
        // sería pedirle al líder que se apruebe a sí mismo.
        Assert.Equal(PointApprovalStatus.Aprobado, entrada.ApprovalStatus);

        // Y la traza en la actividad, que es la guarda contra pagar dos veces.
        Assert.Equal(entrada.Id, (await LeerAsync(db, actividadId)).PointEntryId);
    }

    /// <summary>
    /// Los puntos los escribe el LÍDER y pueden no ser los del criterio: el criterio dice de qué se
    /// premia, no cuánto vale este caso. Es el mismo reparto que al publicar un artículo.
    /// </summary>
    [Fact]
    public async Task Calificar_admiteUnValorDistintoAlDelCriterio()
    {
        var (db, devId, criterioId) = await BaseListaAsync();
        using var _ = db;

        int actividadId = await ActividadCerradaAsync(db, devId);
        await Servicio(db, Admin()).CalificarAsync(actividadId, criterioId, 4, null);

        Assert.Equal(4, (await db.PointEntries.AsNoTracking().SingleAsync()).Points);
    }

    /// <summary>Y admite valores NEGATIVOS: una actividad libre puede ser el sitio donde consta algo
    /// que salió mal, igual que en el catálogo hay criterios que restan.</summary>
    [Fact]
    public async Task Calificar_admitePuntosNegativos()
    {
        var (db, devId, _) = await BaseListaAsync();
        using var _d = db;

        var malo = new ScoringCriterion
        {
            Name = "Se te fue un bug a producción", DefaultPoints = -15,
            IsActive = true, Scope = CriterionScope.Individual
        };
        db.ScoringCriteria.Add(malo);
        await db.SaveChangesAsync();

        int actividadId = await ActividadCerradaAsync(db, devId);
        var (ok, mensaje) = await Servicio(db, Admin()).CalificarAsync(actividadId, malo.Id, -15, null);

        Assert.True(ok, mensaje);
        Assert.Equal(-15, (await db.PointEntries.AsNoTracking().SingleAsync()).Points);
    }

    // ── 2. Lo que no se califica ─────────────────────────────────────────────────

    [Fact]
    public async Task Abierta_noSeCalifica()
    {
        var (db, devId, criterioId) = await BaseListaAsync();
        using var _ = db;

        var abierta = new DevActivity { DeveloperId = devId, Title = "En marcha" };
        db.DevActivities.Add(abierta);
        await db.SaveChangesAsync();

        var (ok, mensaje) = await Servicio(db, Admin()).CalificarAsync(abierta.Id, criterioId, 5, null);

        Assert.False(ok);
        Assert.Contains("cerradas", mensaje);
        Assert.Empty(await db.PointEntries.AsNoTracking().ToListAsync());
    }

    /// <summary><b>No se paga dos veces.</b> Es la guarda que sostiene todo lo demás.</summary>
    [Fact]
    public async Task DosVeces_seNiega()
    {
        var (db, devId, criterioId) = await BaseListaAsync();
        using var _ = db;

        int actividadId = await ActividadCerradaAsync(db, devId);
        await Servicio(db, Admin()).CalificarAsync(actividadId, criterioId, 10, null);

        var (ok, mensaje) = await Servicio(db, Admin()).CalificarAsync(actividadId, criterioId, 10, null);

        Assert.False(ok);
        Assert.Contains("ya se calificó", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await db.PointEntries.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// <b>La percha del cronómetro de una actividad del pool NO se califica aquí.</b> Ese trabajo
    /// cobra por el pool, con los puntos que la matriz congeló antes de que nadie lo tomara;
    /// calificarla además sería pagar el mismo trabajo dos veces por dos caminos distintos.
    /// </summary>
    [Fact]
    public async Task LaDelPool_noSeCalifica()
    {
        var (db, devId, criterioId) = await BaseListaAsync();
        using var _ = db;

        var delPool = new PoolActivity
        {
            Title = "Corregir el cálculo", WorkType = PoolWorkType.Bug, Complexity = PoolComplexity.Media,
            Points = 8, Status = PoolActivityStatus.Tomada, ClaimedByDeveloperId = devId
        };
        db.PoolActivities.Add(delPool);
        await db.SaveChangesAsync();

        int actividadId = await ActividadCerradaAsync(db, devId, "Pool #7: Corregir el cálculo",
            poolActivityId: delPool.Id);
        delPool.LinkedDevActivityId = actividadId;
        await db.SaveChangesAsync();

        var (ok, mensaje) = await Servicio(db, Admin()).CalificarAsync(actividadId, criterioId, 10, null);

        Assert.False(ok);
        Assert.Contains("pool", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.PointEntries.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ConCriterioDeEquipo_seNiega()
    {
        var (db, devId, _) = await BaseListaAsync();
        using var _d = db;

        var deEquipo = new ScoringCriterion
        {
            Name = "Objetivo de sprint cumplido (equipo)", DefaultPoints = 15,
            IsActive = true, Scope = CriterionScope.Equipo
        };
        db.ScoringCriteria.Add(deEquipo);
        await db.SaveChangesAsync();

        int actividadId = await ActividadCerradaAsync(db, devId);
        var (ok, mensaje) = await Servicio(db, Admin()).CalificarAsync(actividadId, deEquipo.Id, 15, null);

        Assert.False(ok);
        Assert.Contains("equipo", mensaje);
    }

    [Fact]
    public async Task ConCeroPuntos_seNiega()
    {
        var (db, devId, criterioId) = await BaseListaAsync();
        using var _ = db;

        int actividadId = await ActividadCerradaAsync(db, devId);
        var (ok, mensaje) = await Servicio(db, Admin()).CalificarAsync(actividadId, criterioId, 0, null);

        Assert.False(ok);
        Assert.Contains("0 puntos", mensaje);
    }

    /// <summary>Calificar es del líder: repartir mérito no es algo que cada quien haga sobre sí mismo.</summary>
    [Fact]
    public async Task UnDesarrollador_noPuedeCalificar()
    {
        var (db, devId, criterioId) = await BaseListaAsync();
        using var _ = db;

        int actividadId = await ActividadCerradaAsync(db, devId);
        var suyo = UsuarioDePrueba.Como(UserRole.Desarrollador, devId, userId: 2);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Servicio(db, suyo).CalificarAsync(actividadId, criterioId, 10, null));
    }

    // ── 3. Lo que el líder ve antes de decidir ───────────────────────────────────

    /// <summary>
    /// La pantalla sabe, sin preguntar fila por fila, qué está calificado y qué cobra por el pool.
    /// Viaja resuelto para no ofrecer un botón que el servidor va a rechazar.
    /// </summary>
    [Fact]
    public async Task CalificacionDe_diceLoCalificadoYLoQueEsDelPool()
    {
        var (db, devId, criterioId) = await BaseListaAsync();
        using var _ = db;

        int calificada = await ActividadCerradaAsync(db, devId, "Ya valorada");
        int sinCalificar = await ActividadCerradaAsync(db, devId, "Todavía no");
        int delPool = await ActividadCerradaAsync(db, devId, "Pool #7: algo", poolActivityId: 77);

        await Servicio(db, Admin()).CalificarAsync(calificada, criterioId, 7, null);

        var mapa = await Servicio(db, Admin())
            .CalificacionDeAsync([calificada, sinCalificar, delPool]);

        Assert.Equal(7, mapa[calificada].Puntos);
        Assert.False(mapa[calificada].EsDelPool);

        Assert.Null(mapa[sinCalificar].Puntos);
        Assert.False(mapa[sinCalificar].EsDelPool);

        Assert.Null(mapa[delPool].Puntos);
        Assert.True(mapa[delPool].EsDelPool);
    }

    /// <summary>La lista de criterios para calificar deja fuera los del propio pool, que valen 0 y
    /// existen solo para que las actividades del pool cuelguen de algo.</summary>
    [Fact]
    public async Task LosCriteriosParaCalificar_dejanFueraLosDelPool()
    {
        var (db, _, _c) = await BaseListaAsync();
        using var _d = db;
        await PoolSeed.SembrarAsync(db);

        var criterios = await Servicio(db, Admin()).CriteriosParaCalificarAsync();

        Assert.Contains(criterios, c => c.Nombre == "Resolviste un incidente crítico");
        Assert.DoesNotContain(criterios, c => c.Nombre.StartsWith(PoolSeed.PrefijoCriterio));
    }
}
