using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// EL DESCUENTO: una actividad del pool que nace pagada, cerrada y en negativo.
///
/// <para><b>Por qué existe.</b> El catálogo tiene cuarenta y un criterios de castigo y, desde que el
/// pool es la unidad de trabajo, ninguna puerta por la que aplicarlos: calificar una actividad libre
/// era la única. Sin esto, el líder se queda sin forma de anotar nada que salió mal.</para>
///
/// <para><b>Y no es un <c>Retrabajo</c>.</b> Si hay algo que hacer, es retrabajo —se publica, se toma,
/// se cronometra, se entrega— y su precio sale de la matriz. Si no hay nada que hacer, es un descuento
/// y su precio sale de un criterio que nombra el hecho. Fundirlos daría una fila que a veces tiene
/// reclamo y cronómetro y a veces no, con el mismo tipo.</para>
/// </summary>
public class DescuentoDelPoolTests : IDisposable
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
        foreach (var c in _contextos) c.Dispose();
        GC.SuppressFinalize(this);
    }

    private PoolActivityService Svc(AppDbContext db, ICurrentUser cu)
    {
        var ctx = OtroContexto(db);
        return new PoolActivityService(ctx, cu, new AuditService(ctx, cu, new OrigenDePrueba()),
                                       new NotificationService(ctx),
                                       new SettingsService(ctx, cu, new AuditService(ctx, cu, new OrigenDePrueba())));
    }

    private static UsuarioDePrueba Admin(int userId = 9) => UsuarioDePrueba.Como(UserRole.Admin, userId: userId);
    private static UsuarioDePrueba Dev(int developerId, int userId = 1) =>
        UsuarioDePrueba.Como(UserRole.Desarrollador, developerId, userId);

    private static ScoringCriterion Criterio(AppDbContext db, string nombre, int puntos,
        CriterionScope alcance = CriterionScope.Individual, bool activo = true)
    {
        var c = new ScoringCriterion
        {
            Name = nombre, Description = "Lo que sea.", DefaultPoints = puntos,
            IsActive = activo, Scope = alcance
        };
        db.ScoringCriteria.Add(c);
        db.SaveChanges();
        return c;
    }

    /// <summary>Una base con el pool sembrado, Ana activa y un criterio que resta.</summary>
    private static async Task<(AppDbContext db, int devId, int criterioId)> BaseAsync()
    {
        var db = TestDb.New();
        await PoolSeed.SembrarAsync(db);

        var ana = new Developer { FullName = "Ana Pérez", IsActive = true };
        db.Developers.Add(ana);
        await db.SaveChangesAsync();

        var criterio = Criterio(db, "Subiste secretos al repositorio", -20);
        return (db, ana.Id, criterio.Id);
    }

    private static PoolActivity ElDescuento(AppDbContext db) =>
        db.PoolActivities.AsNoTracking().Single(a => a.Status == PoolActivityStatus.Descuento);

    // ── El camino bueno ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Publicar_dejaLaActividadPagadaYCerradaEnNegativo()
    {
        var (db, devId, criterioId) = await BaseAsync();
        using var _ = db;

        var (ok, mensaje) = await Svc(db, Admin()).PublicarDescuentoAsync(
            devId, criterioId, -20, "Un token en el repositorio", "Estaba en el appsettings del PR 412.");

        Assert.True(ok, mensaje);

        var d = ElDescuento(db);
        Assert.Equal(-20, d.Points);
        Assert.Equal(devId, d.ClaimedByDeveloperId);
        Assert.NotNull(d.PointEntryId);
        Assert.Null(d.AnulacionPointEntryId);
        // El motivo queda en el historial: es lo único que la persona puede leer.
        Assert.Contains("appsettings del PR 412", d.ReviewHistory ?? "");

        var entrada = Assert.Single(db.PointEntries.AsNoTracking().ToList());
        Assert.Equal(-20, entrada.Points);
        Assert.Equal(devId, entrada.DeveloperId);
        Assert.Equal(criterioId, entrada.CriterionId);
        Assert.Equal(PointApprovalStatus.Aprobado, entrada.ApprovalStatus);
        Assert.Equal(d.PointEntryId, entrada.Id);
    }

    /// <summary>
    /// El líder puede pesarlo distinto de lo que propone el criterio: un mismo hecho no siempre cuesta
    /// lo mismo. Lo que el servidor impone es el signo y el suelo, no la cantidad.
    /// </summary>
    [Fact]
    public async Task Publicar_admiteUnPesoDistintoDelQueProponeElCriterio()
    {
        var (db, devId, criterioId) = await BaseAsync();
        using var _ = db;

        Assert.True((await Svc(db, Admin()).PublicarDescuentoAsync(
            devId, criterioId, -5, "Un token, pero de pruebas", "Era de un entorno de juguete.")).ok);

        Assert.Equal(-5, ElDescuento(db).Points);
    }

    /// <summary>
    /// Un descuento NO sale hacia DevOps. Es la trampa que <c>EstadoCerradoSinEntrega</c> existe para
    /// cerrar: sin ella, una fila cerrada con work item se quedaría para siempre en el aviso ámbar del
    /// líder. Un descuento no lleva work item, pero la puerta se comprueba igual — el día que alguien
    /// le ponga uno, esta prueba es la que dice qué debe pasar.
    /// </summary>
    [Fact]
    public void UnDescuento_noSeQuedaPendienteDeDevOps()
    {
        var d = new PoolActivity
        {
            Title = "Con ticket, por si acaso", DevOpsWorkItemId = 4321,
            Priority = PoolPriority.Media, DevOpsPrioridadEnviada = null,
            Status = PoolActivityStatus.Descuento
        };

        Assert.False(d.PendienteDeEnviarADevOps);
    }

    // ── Lo que se rechaza ────────────────────────────────────────────────────────

    [Fact]
    public async Task Publicar_conPuntosPositivosOCero_seRechaza()
    {
        var (db, devId, criterioId) = await BaseAsync();
        using var _ = db;
        var svc = Svc(db, Admin());

        foreach (int puntos in new[] { 0, 5 })
        {
            var (ok, mensaje) = await svc.PublicarDescuentoAsync(devId, criterioId, puntos, "Algo", "Un motivo.");
            Assert.False(ok);
            Assert.Contains("resta", mensaje);
        }

        Assert.Empty(db.PointEntries.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Publicar_conUnDescuentoDesmesurado_seRechaza()
    {
        var (db, devId, criterioId) = await BaseAsync();
        using var _ = db;

        var (ok, mensaje) = await Svc(db, Admin())
            .PublicarDescuentoAsync(devId, criterioId, -500, "Todo el mes", "Se enfadó alguien.");

        Assert.False(ok);
        Assert.Contains("en dos", mensaje);   // «hazlo en dos y que cada uno diga su motivo»
    }

    [Fact]
    public async Task Publicar_conUnCriterioQueSuma_seRechaza()
    {
        var (db, devId, _) = await BaseAsync();
        using var _d = db;
        var positivo = Criterio(db, "Entregaste a tiempo", 5);

        var (ok, mensaje) = await Svc(db, Admin())
            .PublicarDescuentoAsync(devId, positivo.Id, -5, "Algo", "Un motivo.");

        Assert.False(ok);
        Assert.Contains("no es un criterio de descuento", mensaje);
    }

    [Fact]
    public async Task Publicar_conUnCriterioDeEquipoOInactivo_seRechaza()
    {
        var (db, devId, _) = await BaseAsync();
        using var _d = db;
        var deEquipo = Criterio(db, "El equipo incumplió el sprint", -15, CriterionScope.Equipo);
        var retirado = Criterio(db, "Algo que ya no se usa", -10, activo: false);
        var svc = Svc(db, Admin());

        Assert.Contains("individual",
            (await svc.PublicarDescuentoAsync(devId, deEquipo.Id, -15, "Algo", "Un motivo.")).mensaje);
        Assert.Contains("retirado",
            (await svc.PublicarDescuentoAsync(devId, retirado.Id, -10, "Algo", "Un motivo.")).mensaje);
    }

    /// <summary>
    /// Con los criterios del pool tampoco: son los que usa el abono de las actividades y valen 0 en el
    /// catálogo. Un descuento lleva un criterio que nombre lo que pasó.
    /// </summary>
    [Fact]
    public async Task Publicar_conUnCriterioDelPool_seRechaza()
    {
        var (db, devId, _) = await BaseAsync();
        using var _d = db;
        int delPool = db.ScoringCriteria.AsNoTracking()
            .First(c => c.Name == PoolSeed.NombreCriterio(PoolWorkType.Retrabajo)).Id;

        var (ok, mensaje) = await Svc(db, Admin())
            .PublicarDescuentoAsync(devId, delPool, -5, "Algo", "Un motivo.");

        Assert.False(ok);
        // Vale 0 en el catálogo, así que lo caza la comprobación del signo antes que la del prefijo.
        // Cualquiera de las dos razones sirve; lo que importa es que no pase.
        Assert.NotEmpty(mensaje);
    }

    [Fact]
    public async Task Publicar_sinMotivo_seRechaza()
    {
        var (db, devId, criterioId) = await BaseAsync();
        using var _ = db;

        var (ok, mensaje) = await Svc(db, Admin())
            .PublicarDescuentoAsync(devId, criterioId, -20, "Un token en el repositorio", "   ");

        Assert.False(ok);
        Assert.Contains("motivo", mensaje);
        Assert.Empty(db.PointEntries.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Publicar_aAlguienDadoDeBaja_seRechaza()
    {
        var (db, _, criterioId) = await BaseAsync();
        using var _d = db;
        var baja = new Developer { FullName = "Ya no está", IsActive = false };
        db.Developers.Add(baja);
        await db.SaveChangesAsync();

        var (ok, mensaje) = await Svc(db, Admin())
            .PublicarDescuentoAsync(baja.Id, criterioId, -20, "Algo", "Un motivo.");

        Assert.False(ok);
        Assert.Contains("baja", mensaje);
    }

    [Fact]
    public async Task Publicar_esSoloDelLider()
    {
        var (db, devId, criterioId) = await BaseAsync();
        using var _ = db;

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, Dev(devId))
            .PublicarDescuentoAsync(devId, criterioId, -20, "Algo", "Un motivo."));
    }

    // ── Anular ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Anular_devuelveLosPuntosConUnaEntradaNueva_ySinBorrarNada()
    {
        var (db, devId, criterioId) = await BaseAsync();
        using var _ = db;

        Assert.True((await Svc(db, Admin()).PublicarDescuentoAsync(
            devId, criterioId, -20, "Un token", "Estaba en el PR 412.")).ok);
        int descuentoId = ElDescuento(db).Id;

        var (ok, mensaje) = await Svc(db, Admin())
            .AnularDescuentoAsync(descuentoId, "Era de un entorno de juguete, no contaba.");

        Assert.True(ok, mensaje);

        // Las DOS entradas siguen ahí y el neto es cero: la conversación que produjo el descuento
        // existió, y el histórico se lee para explicarla.
        var entradas = db.PointEntries.AsNoTracking().OrderBy(p => p.Id).ToList();
        Assert.Equal(2, entradas.Count);
        Assert.Equal(-20, entradas[0].Points);
        Assert.Equal(20, entradas[1].Points);
        Assert.Equal(0, entradas.Sum(p => p.Points));
        Assert.Equal(criterioId, entradas[1].CriterionId);   // el mismo criterio, el signo contrario

        var d = ElDescuento(db);
        Assert.Equal(entradas[1].Id, d.AnulacionPointEntryId);
        Assert.Equal(entradas[0].Id, d.PointEntryId);        // la original no se toca
        Assert.Contains("Era de un entorno de juguete", d.ReviewHistory ?? "");
    }

    [Fact]
    public async Task Anular_dosVeces_noDevuelveLosPuntosDosVeces()
    {
        var (db, devId, criterioId) = await BaseAsync();
        using var _ = db;

        Assert.True((await Svc(db, Admin()).PublicarDescuentoAsync(
            devId, criterioId, -20, "Un token", "Estaba en el PR.")).ok);
        int descuentoId = ElDescuento(db).Id;

        Assert.True((await Svc(db, Admin()).AnularDescuentoAsync(descuentoId, "Me equivoqué.")).ok);
        var (ok, mensaje) = await Svc(db, Admin()).AnularDescuentoAsync(descuentoId, "Otra vez.");

        Assert.False(ok);
        Assert.Contains("ya estaba anulado", mensaje);
        Assert.Equal(2, db.PointEntries.AsNoTracking().Count());
    }

    [Fact]
    public async Task Anular_sinMotivo_seRechaza()
    {
        var (db, devId, criterioId) = await BaseAsync();
        using var _ = db;

        Assert.True((await Svc(db, Admin()).PublicarDescuentoAsync(
            devId, criterioId, -20, "Un token", "Estaba en el PR.")).ok);

        var (ok, mensaje) = await Svc(db, Admin()).AnularDescuentoAsync(ElDescuento(db).Id, " ");

        Assert.False(ok);
        Assert.Contains("por qué", mensaje);
    }

    /// <summary>Anular es de descuentos y de nada más: una actividad aceptada no se «desacepta».</summary>
    [Fact]
    public async Task Anular_algoQueNoEsUnDescuento_seRechaza()
    {
        var (db, _, _) = await BaseAsync();
        using var _d = db;

        var (creada, mensaje, actividad) = await Svc(db, Admin()).CrearAsync(new PoolActivity
        {
            Title = "Un bug corriente", WorkType = PoolWorkType.Bug,
            Complexity = PoolComplexity.Alta, HorasLimite = 16m
        });
        Assert.True(creada, mensaje);

        var (ok, porQue) = await Svc(db, Admin()).AnularDescuentoAsync(actividad!.Id, "Un motivo.");

        Assert.False(ok);
        Assert.Contains("Solo se anulan descuentos", porQue);
    }

    [Fact]
    public async Task Anular_esSoloDelLider()
    {
        var (db, devId, criterioId) = await BaseAsync();
        using var _ = db;

        Assert.True((await Svc(db, Admin()).PublicarDescuentoAsync(
            devId, criterioId, -20, "Un token", "Estaba en el PR.")).ok);

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, Dev(devId))
            .AnularDescuentoAsync(ElDescuento(db).Id, "Que me lo quiten."));
    }

    // ── Y la razón de ser de todo el lote ────────────────────────────────────────

    /// <summary>
    /// HAY UN SOLO SITIO QUE PAGA, y esta prueba es la que lo dice.
    ///
    /// <para>Aceptar una entrega y aplicar un descuento son las dos únicas cosas que abonan puntos
    /// por el pool, y las dos pasan por <c>AbonarAsync</c>: la transacción, la reserva condicional
    /// contra el doble pago y la traza de vuelta se escriben UNA vez. Si alguien añadiera un tercer
    /// camino copiando el bloque, esto se pone rojo — y es el aviso que importa, porque el bloque
    /// copiado FUNCIONA: lo que se pierde no es la funcionalidad, es la garantía.</para>
    ///
    /// <para><b>Lo que se cuenta es quién marca la actividad como PAGADA</b>, no cuántas veces se
    /// inserta una entrada. Hay dos inserciones en el archivo y está bien que las haya: la del
    /// abono y la COMPENSATORIA de anular un descuento, que no paga nada — deshace, con su propia
    /// guarda (<c>AnulacionPointEntryId</c>) y su propia condición. Contar inserciones mezclaría
    /// «cobrar» con «devolver», que son actos distintos.</para>
    ///
    /// <para>Es la hermana de <c>ProductoresDePuntosTests</c>, que vigila lo mismo entre archivos.
    /// Aquí se vigila DENTRO del archivo que de verdad reparte trabajo.</para>
    /// </summary>
    [Fact]
    public void ElPool_pagaEnUnSoloSitio()
    {
        var servicio = Path.Combine(
            LocalizarAplicacion(), "Services", "PoolActivityService.cs");
        var texto = File.ReadAllText(servicio);

        // Marcar la actividad con su entrada de puntos es lo que la deja pagada, y solo se escribe
        // dentro de AbonarAsync.
        int marcasDePagada = texto.Split("SetProperty(a => a.PointEntryId, entrada.Id)").Length - 1;
        Assert.Equal(1, marcasDePagada);

        // Y las dos inserciones que hay son las dos que tiene que haber: el abono y la
        // compensatoria de anular.
        int inserciones = texto.Split("PointEntries.Add").Length - 1;
        Assert.Equal(2, inserciones);
        Assert.Contains("db.PointEntries.Add(entrada);", texto);
        Assert.Contains("db.PointEntries.Add(compensatoria);", texto);
    }

    private static string LocalizarAplicacion()
    {
        const string relativa = "src/Web/AdminWeb.Application/AdminWeb.Application.csproj";

        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta != null)
        {
            var candidato = Path.Combine(carpeta.FullName, relativa);
            if (File.Exists(candidato)) return Path.GetDirectoryName(candidato)!;
            carpeta = carpeta.Parent;
        }

        throw new DirectoryNotFoundException($"No se encontró «{relativa}».");
    }
}
