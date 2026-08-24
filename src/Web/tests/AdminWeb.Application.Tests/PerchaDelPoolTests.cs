using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LA PERCHA DEL POOL: la actividad libre que el pool fabrica al tomar una actividad para poder
/// cronometrar el trabajo con el cronómetro de siempre.
///
/// <para>No es una actividad libre de nadie: es fontanería. Estas pruebas fijan las dos cosas que
/// eso implica y que hasta ahora no se cumplían.</para>
///
/// <para><b>Fallo 1 — se podía tocar desde la otra pantalla.</b> La guarda estaba solo en
/// <c>CalificarAsync</c> y no en <c>ObtenerPropiaAsync</c>, el embudo por el que pasan renombrar,
/// cerrar, reabrir, eliminar y adjuntar evidencia. Un desarrollador podía cerrar el cronómetro de su
/// propia actividad del pool a media entrega, o borrarlo mientras no hubiera medido nada — y entonces
/// <c>LinkedDevActivityId</c> se quedaba apuntando a una fila que ya no existe.</para>
///
/// <para><b>Fallo 2 — la percha devuelta se podía cobrar dos veces.</b> La guarda preguntaba por
/// <c>PoolActivity.LinkedDevActivityId</c>, y <c>SoltarReclamo</c> borra ese vínculo al devolver o
/// liberar la actividad. Quedaba una percha cerrada, con tiempo medido y sin pagar, que ya no era
/// reconocible: el líder podía calificarla por puntos y, cuando otra persona terminara el mismo
/// trabajo, el pool volvía a pagar. Lo que lo cierra es que la marca viva en la PROPIA FILA
/// (<see cref="DevActivity.PoolActivityId"/>) y no se borre nunca.</para>
/// </summary>
public class PerchaDelPoolTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    private AppDbContext OtroContexto(AppDbContext db)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Database.GetConnectionString()).Options;
        var ctx = new AppDbContext(opts);
        _contextos.Add(ctx);
        return ctx;
    }

    public void Dispose()
    {
        foreach (var ctx in _contextos) ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    private PoolActivityService Pool(AppDbContext db, ICurrentUser cu)
    {
        var ctx = OtroContexto(db);
        return new PoolActivityService(ctx, cu, new AuditService(ctx, cu, new OrigenDePrueba()),
                                       new NotificationService(ctx),
                                       new SettingsService(ctx, cu, new AuditService(ctx, cu, new OrigenDePrueba())));
    }

    private DevActivityService Libres(AppDbContext db, ICurrentUser cu)
    {
        var ctx = OtroContexto(db);
        var bitacora = new AuditService(ctx, cu, new OrigenDePrueba());
        return new DevActivityService(ctx, cu, bitacora, new WorkSessionService(ctx, cu, bitacora));
    }

    private static UsuarioDePrueba Admin(int userId = 9) => UsuarioDePrueba.Como(UserRole.Admin, userId: userId);
    private static UsuarioDePrueba Dev(int developerId, int userId = 1) =>
        UsuarioDePrueba.Como(UserRole.Desarrollador, developerId, userId);

    private const decimal PlazoDelBug = 16m;
    private const decimal EstimacionAlTomar = 4m;

    private static async Task<(AppDbContext db, int devId)> BaseConPoolAsync()
    {
        var db = TestDb.New();
        await PoolSeed.SembrarAsync(db);
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev);
        await db.SaveChangesAsync();
        return (db, dev.Id);
    }

    /// <summary>Publica un bug y lo toma: es lo que fabrica la percha por el camino de verdad.</summary>
    private async Task<(int poolId, int perchaId)> TomadaAsync(AppDbContext db, int devId)
    {
        var (ok, mensaje, actividad) = await Pool(db, Admin()).CrearAsync(new PoolActivity
        {
            Title = "Corregir el importador", WorkType = PoolWorkType.Bug,
            Complexity = PoolComplexity.Alta, HorasLimite = PlazoDelBug
        });
        Assert.True(ok, mensaje);

        var (tomada, porQue) = await Pool(db, Dev(devId)).TomarAsync(actividad!.Id, devId, EstimacionAlTomar);
        Assert.True(tomada, porQue);

        int perchaId = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id)
                         .LinkedDevActivityId ?? 0;
        Assert.True(perchaId > 0);
        return (actividad.Id, perchaId);
    }

    private static DevActivity LeerPercha(AppDbContext db, int perchaId) =>
        db.DevActivities.AsNoTracking().Single(a => a.Id == perchaId);

    // ── La marca se escribe y no se borra ────────────────────────────────────────

    [Fact]
    public async Task Tomar_dejaLaPerchaMarcadaConSuActividadDelPool()
    {
        var (db, devId) = await BaseConPoolAsync();
        using var _ = db;

        var (poolId, perchaId) = await TomadaAsync(db, devId);

        var percha = LeerPercha(db, perchaId);
        Assert.Equal(poolId, percha.PoolActivityId);
        Assert.True(percha.EsPerchaDelPool);
    }

    /// <summary>
    /// EL FALLO 2, fijado. Devolver la actividad borra el vínculo de vuelta —eso es correcto y
    /// deliberado, la actividad ya no es de nadie— pero la marca de la percha sobrevive, que es lo
    /// único que permite seguir reconociéndola.
    /// </summary>
    [Fact]
    public async Task Devolver_borraElVinculoPeroNoLaMarca()
    {
        var (db, devId) = await BaseConPoolAsync();
        using var _ = db;

        var (poolId, perchaId) = await TomadaAsync(db, devId);
        Assert.True((await Pool(db, Dev(devId)).DevolverAsync(poolId, devId, "se me atravesó otra cosa")).ok);

        // El vínculo de vuelta se fue con el reclamo…
        Assert.Null(db.PoolActivities.AsNoTracking().Single(a => a.Id == poolId).LinkedDevActivityId);
        // …y la marca sigue en la percha.
        Assert.Equal(poolId, LeerPercha(db, perchaId).PoolActivityId);
    }

    [Fact]
    public async Task Liberar_tampocoBorraLaMarca()
    {
        var (db, devId) = await BaseConPoolAsync();
        using var _ = db;

        var (poolId, perchaId) = await TomadaAsync(db, devId);
        Assert.True((await Pool(db, Admin()).LiberarAsync(poolId, "cambió la prioridad")).ok);

        Assert.Null(db.PoolActivities.AsNoTracking().Single(a => a.Id == poolId).LinkedDevActivityId);
        Assert.Equal(poolId, LeerPercha(db, perchaId).PoolActivityId);
    }

    // ── Calificar: ni la viva ni la devuelta ─────────────────────────────────────

    [Fact]
    public async Task LaPerchaDevuelta_noSePuedeCalificarPorPuntos()
    {
        var (db, devId) = await BaseConPoolAsync();
        using var _ = db;

        var criterio = new ScoringCriterion
        {
            Name = "Resolviste un incidente crítico", Description = "Urgente, sacado rápido.",
            DefaultPoints = 10, IsActive = true, Scope = CriterionScope.Individual
        };
        db.ScoringCriteria.Add(criterio);
        await db.SaveChangesAsync();

        var (poolId, perchaId) = await TomadaAsync(db, devId);
        // Devolverla deja la percha cerrada, con su tiempo y sin pagar: el escenario del fallo.
        Assert.True((await Pool(db, Dev(devId)).DevolverAsync(poolId, devId, "no pude")).ok);
        Assert.Equal(DevActivityStatus.Cerrada, LeerPercha(db, perchaId).Status);

        var (ok, mensaje) = await Libres(db, Admin()).CalificarAsync(perchaId, criterio.Id, 10, null);

        Assert.False(ok);
        Assert.Contains("pool", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.PointEntries.AsNoTracking().ToList());
    }

    // ── Las cinco operaciones del dueño ──────────────────────────────────────────
    //
    // Son cinco y comparten embudo (ObtenerPropiaAsync), que es justo por lo que la guarda va ahí:
    // la que se olvidara sería la que rompiera el cronómetro de otra pantalla.

    [Fact]
    public async Task ElDuenno_noPuedeRenombrarLaPercha()
    {
        var (db, devId) = await BaseConPoolAsync();
        using var _ = db;
        var (_, perchaId) = await TomadaAsync(db, devId);

        var (ok, mensaje) = await Libres(db, Dev(devId)).RenombrarAsync(perchaId, "Mío y de nadie más", null);

        Assert.False(ok);
        Assert.Contains("cronómetro", mensaje);
        Assert.StartsWith("Pool #", LeerPercha(db, perchaId).Title);
    }

    [Fact]
    public async Task ElDuenno_noPuedeCerrarLaPercha()
    {
        var (db, devId) = await BaseConPoolAsync();
        using var _ = db;
        var (_, perchaId) = await TomadaAsync(db, devId);

        var (ok, mensaje) = await Libres(db, Dev(devId)).CerrarAsync(perchaId);

        Assert.False(ok);
        Assert.Contains("cronómetro", mensaje);
        Assert.Equal(DevActivityStatus.Abierta, LeerPercha(db, perchaId).Status);
    }

    [Fact]
    public async Task ElDuenno_noPuedeEliminarLaPercha()
    {
        var (db, devId) = await BaseConPoolAsync();
        using var _ = db;
        var (_, perchaId) = await TomadaAsync(db, devId);

        // Sin tiempo medido: por el otro camino (EliminarAsync mira los segundos) esta llamada
        // habría pasado, que es exactamente lo que hacía el agujero alcanzable.
        var (ok, mensaje) = await Libres(db, Dev(devId)).EliminarAsync(perchaId);

        Assert.False(ok);
        Assert.Contains("cronómetro", mensaje);
        Assert.NotNull(LeerPercha(db, perchaId));
    }

    [Fact]
    public async Task ElDuenno_noPuedeReabrirLaPerchaQueElPoolCerro()
    {
        var (db, devId) = await BaseConPoolAsync();
        using var _ = db;
        var (poolId, perchaId) = await TomadaAsync(db, devId);
        Assert.True((await Pool(db, Dev(devId)).DevolverAsync(poolId, devId, "no pude")).ok);

        var (ok, mensaje) = await Libres(db, Dev(devId)).ReabrirAsync(perchaId);

        Assert.False(ok);
        Assert.Contains("cronómetro", mensaje);
        Assert.Equal(DevActivityStatus.Cerrada, LeerPercha(db, perchaId).Status);
    }

    // ── Y no se ve donde no debe ─────────────────────────────────────────────────

    [Fact]
    public async Task LaPercha_noSaleEnLaListaDeActividadesLibres()
    {
        var (db, devId) = await BaseConPoolAsync();
        using var _ = db;

        db.DevActivities.Add(new DevActivity
        {
            DeveloperId = devId, Title = "Investigar la caída del viernes",
            Status = DevActivityStatus.Abierta
        });
        await db.SaveChangesAsync();

        await TomadaAsync(db, devId);

        var suyas = await Libres(db, Dev(devId)).DeDesarrolladorAsync(devId);

        var unica = Assert.Single(suyas);
        Assert.Equal("Investigar la caída del viernes", unica.Title);
    }

    /// <summary>
    /// Lo que la guarda NO puede romper: el propio pool sigue cerrando la percha al aceptar la
    /// entrega. <c>CerrarActividadEnlazadaAsync</c> escribe la entidad directamente y no pasa por
    /// <c>ObtenerPropiaAsync</c>, así que no se autobloquea. Sin esta prueba, poner la guarda en el
    /// embudo podría haber dejado perchas abiertas para siempre sin que nada fallara.
    /// </summary>
    [Fact]
    public async Task ElPool_siCierraLaPerchaAlAceptarLaEntrega()
    {
        var (db, devId) = await BaseConPoolAsync();
        using var _ = db;

        var (poolId, perchaId) = await TomadaAsync(db, devId);

        var svc = Pool(db, Dev(devId));
        foreach (var item in await svc.ChecklistDeAsync(poolId))
            Assert.True((await svc.MarcarItemAsync(item.Id, devId, true,
                item.RequiereEvidencia ? "https://dev.azure.com/o/p/_git/r/pullrequest/1" : null)).ok);
        Assert.True((await Pool(db, Dev(devId)).EntregarAsync(poolId, devId)).ok);
        Assert.True((await Pool(db, Admin()).AceptarAsync(poolId)).ok);

        Assert.Equal(DevActivityStatus.Cerrada, LeerPercha(db, perchaId).Status);
    }
}
