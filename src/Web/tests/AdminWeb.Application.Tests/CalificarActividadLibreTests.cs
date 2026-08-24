using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// CALIFICAR UNA ACTIVIDAD LIBRE: RETIRADO, y esto es lo que lo sujeta.
///
/// <para><b>Qué era.</b> El líder podía poner puntos a una actividad que el desarrollador había
/// abierto por su cuenta —el trabajo que no cabe en el pool ni viene de un ticket—, mirando el tiempo
/// MEDIDO por el cronómetro y la evidencia adjunta. Se hizo a propósito y con un argumento decente:
/// ese trabajo acumulaba horas y evidencia y no daba puntos por ninguna ruta.</para>
///
/// <para><b>Por qué se fue.</b> Seguía siendo ponerle valor a algo ya hecho, que es exactamente lo
/// que el pool existe para evitar: allí el precio se fija ANTES de trabajar, y por eso es comparable
/// entre personas. Con el pool como unidad de trabajo, esto era el segundo camino de puntos del líder
/// y sobraba. Lo que había que reconocer se publica al pool y se verifica; lo que había que penalizar
/// se aplica como DESCUENTO, que es lo que heredó los criterios negativos del catálogo.</para>
///
/// <para><b>Qué queda vivo aquí, y por qué importa.</b> Tres cosas. Que la AUTORIZACIÓN se compruebe
/// antes que el apagado —un desarrollador que intente calificar sigue recibiendo una excepción, no un
/// «se retiró»—. Que la CONSULTA de la rejilla del líder siga contestando bien: la pantalla se quedó
/// de solo lectura, pero sigue teniendo que decir qué está calificado y qué cobra por el pool, porque
/// las entradas de antes del corte no se borraron. Y que la lista de criterios llegue VACÍA, que es
/// lo que apaga el botón sin tocar el marcado.</para>
///
/// <para>Lo que NO se prueba ya aquí es la guarda de la percha del pool: dejó de ser distinguible
/// —hoy se rechaza todo— y su prueba de verdad está en <c>PerchaDelPoolTests</c>, donde cubre los
/// cinco métodos y no solo éste.</para>
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

    /// <summary>
    /// Una actividad YA CALIFICADA, de las de antes del corte.
    ///
    /// <para>Se arma a mano porque el método que la armaba es justo el que se apagó, y describe con
    /// más fidelidad lo que hay en la base el día del corte: una <c>DevActivity</c> apuntando con
    /// <c>PointEntryId</c> a una entrada aprobada. Nada de esto se migró ni se borró —la regla del
    /// corte es que ningún tramo toca una fila de <c>PointEntry</c>—, así que la rejilla del líder
    /// tiene que seguir sabiendo leerlo.</para>
    /// </summary>
    private static async Task CalificadaAntesDelCorteAsync(
        AppDbContext db, int actividadId, int devId, int criterioId, int puntos)
    {
        var entrada = new PointEntry
        {
            DeveloperId = devId, CriterionId = criterioId, Points = puntos,
            Year = 2026, Month = 8, Date = DateTime.UtcNow,
            ApprovalStatus = PointApprovalStatus.Aprobado,
            AssignedByUserId = 9, ReviewedByUserId = 9, ReviewedAt = DateTime.UtcNow
        };
        db.PointEntries.Add(entrada);
        await db.SaveChangesAsync();

        var actividad = await db.DevActivities.FirstAsync(a => a.Id == actividadId);
        actividad.PointEntryId = entrada.Id;
        await db.SaveChangesAsync();
    }

    // ── 1. Apagado ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Con todo a favor —actividad cerrada, criterio individual y activo, puntos positivos— sigue
    /// rechazando. Que no quede fila es la mitad importante: un apagado que rechaza pero deja una
    /// <c>PointEntry</c> suelta sería peor que no haberlo apagado.
    /// </summary>
    [Fact]
    public async Task Calificar_seRetiro_YNoDejaFila()
    {
        var (db, devId, criterioId) = await BaseListaAsync();
        using var _ = db;

        int actividadId = await ActividadCerradaAsync(db, devId);

        var (ok, mensaje) = await Servicio(db, Admin())
            .CalificarAsync(actividadId, criterioId, 10, "Lo sacó en dos horas un domingo.");

        Assert.False(ok);
        Assert.Contains("pool", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.PointEntries.AsNoTracking().ToListAsync());
        Assert.Null((await db.DevActivities.AsNoTracking().FirstAsync(a => a.Id == actividadId)).PointEntryId);
    }

    /// <summary>
    /// Y rechaza ANTES de mirar nada. Con una actividad abierta —que la validación de abajo habría
    /// rechazado por su cuenta, con «solo se califican actividades cerradas»— el motivo que llega
    /// sigue siendo el del apagado.
    ///
    /// <para>Sin esta prueba, la de arriba pasaría igual con la guarda puesta al final del método,
    /// que es donde no sirve de nada.</para>
    /// </summary>
    [Fact]
    public async Task Calificar_seRetiro_AntesDeValidarNada()
    {
        var (db, devId, criterioId) = await BaseListaAsync();
        using var _ = db;

        var abierta = new DevActivity { DeveloperId = devId, Title = "En marcha" };
        db.DevActivities.Add(abierta);
        await db.SaveChangesAsync();

        var (ok, mensaje) = await Servicio(db, Admin()).CalificarAsync(abierta.Id, criterioId, 5, null);

        Assert.False(ok);
        Assert.Contains("pool", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cerradas", mensaje);
        Assert.Empty(await db.PointEntries.AsNoTracking().ToListAsync());
    }

    /// <summary>
    /// La autorización se comprueba ANTES que el apagado: un desarrollador sigue recibiendo una
    /// excepción, no un «se retiró».
    ///
    /// <para>No es una sutileza. Si el apagado se hubiera puesto por delante, la comprobación de rol
    /// dejaría de ejercitarse y nadie se enteraría de que se cayó; el día que este método se reabra
    /// —o que alguien lo copie para otra cosa— la puerta ya no estaría donde se cree que está.</para>
    /// </summary>
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

    // ── 2. Lo que el líder sigue viendo ──────────────────────────────────────────

    /// <summary>
    /// La rejilla sabe, sin preguntar fila por fila, qué está calificado y qué cobra por el pool.
    ///
    /// <para>Sigue viva porque las entradas de antes del corte no se borraron: la pantalla quedó de
    /// consulta, pero una actividad calificada en julio tiene que seguir enseñando sus puntos. Si
    /// esta consulta se cayera, el histórico se vería vacío y parecería que el corte borró datos —que
    /// es justo lo que el corte prometió no hacer.</para>
    /// </summary>
    [Fact]
    public async Task CalificacionDe_diceLoCalificadoYLoQueEsDelPool()
    {
        var (db, devId, criterioId) = await BaseListaAsync();
        using var _ = db;

        int calificada = await ActividadCerradaAsync(db, devId, "Ya valorada");
        int sinCalificar = await ActividadCerradaAsync(db, devId, "Todavía no");
        int delPool = await ActividadCerradaAsync(db, devId, "Pool #7: algo", poolActivityId: 77);

        await CalificadaAntesDelCorteAsync(db, calificada, devId, criterioId, 7);

        var mapa = await Servicio(db, Admin())
            .CalificacionDeAsync([calificada, sinCalificar, delPool]);

        Assert.Equal(7, mapa[calificada].Puntos);
        Assert.False(mapa[calificada].EsDelPool);

        Assert.Null(mapa[sinCalificar].Puntos);
        Assert.False(mapa[sinCalificar].EsDelPool);

        Assert.Null(mapa[delPool].Puntos);
        Assert.True(mapa[delPool].EsDelPool);
    }

    /// <summary>
    /// Y la lista de criterios llega VACÍA. Es lo que apaga el formulario del líder sin tocar una
    /// línea de marcado: la rejilla esconde el botón cuando no hay con qué calificar.
    ///
    /// <para>Se siembra el catálogo del pool ENTERO a propósito, para que la lista vacía no pueda
    /// confundirse con «no había criterios»: hay decenas, activos e individuales, y aun así no se
    /// ofrece ninguno. Es la diferencia entre un apagado y una base sin sembrar.</para>
    /// </summary>
    [Fact]
    public async Task LosCriteriosParaCalificar_yaNoOfrecenNada()
    {
        var (db, _, _c) = await BaseListaAsync();
        using var _d = db;
        await PoolSeed.SembrarAsync(db);

        // El catálogo está lleno: si la lista de abajo sale vacía, es porque se apagó la oferta.
        Assert.NotEmpty(await db.ScoringCriteria
            .Where(c => c.IsActive && c.Scope == CriterionScope.Individual).ToListAsync());

        Assert.Empty(await Servicio(db, Admin()).CriteriosParaCalificarAsync());
    }
}
