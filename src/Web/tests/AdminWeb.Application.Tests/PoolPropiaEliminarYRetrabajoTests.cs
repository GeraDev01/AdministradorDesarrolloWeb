using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Las tres salidas nuevas de una actividad del pool y el tipo que resta.
///
/// <list type="bullet">
///   <item><b>La hizo el líder</b>: se cierra sin pasar por el pool y SIN abonar puntos a nadie.</item>
///   <item><b>Eliminar</b>: se borra de verdad, y solo lo que no deja nada colgando.</item>
///   <item><b>Retrabajo</b>: un bug sobre algo ya entregado, que RESTA.</item>
/// </list>
///
/// <para>Las tres comparten un riesgo que estas pruebas son las únicas que miran: cerrar una
/// actividad por un camino nuevo puede dejarla eternamente en la lista de «pendiente de mandar a
/// DevOps», porque esa lista se calcula a partir del estado. Ver
/// <see cref="PoolActivity.EstadoCerradoSinEntrega"/>.</para>
/// </summary>
public class PoolPropiaEliminarYRetrabajoTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    private PoolActivityService Svc(AppDbContext db, ICurrentUser cu)
    {
        var ctx = OtroContexto(db);
        return new PoolActivityService(ctx, cu, new AuditService(ctx, cu, new OrigenDePrueba()),
                                       new NotificationService(ctx),
                                       new SettingsService(ctx, cu, new AuditService(ctx, cu, new OrigenDePrueba())));
    }

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

    private static async Task<AppDbContext> BaseConPoolAsync()
    {
        var db = TestDb.New();
        await PoolSeed.SembrarAsync(db);
        return db;
    }

    private static int NuevoDesarrollador(AppDbContext db, string nombre)
    {
        var d = new Developer { FullName = nombre, IsActive = true };
        db.Developers.Add(d);
        db.SaveChanges();
        return d.Id;
    }

    private static UsuarioDePrueba Admin(int userId = 9) => UsuarioDePrueba.Como(UserRole.Admin, userId: userId);
    private static UsuarioDePrueba Dev(int developerId, int userId = 1) =>
        UsuarioDePrueba.Como(UserRole.Desarrollador, developerId, userId);

    private const decimal PlazoDelBug = 16m;
    private const decimal EstimacionAlTomar = 4m;

    private static PoolActivity Borrador(
        PoolWorkType tipo = PoolWorkType.Bug,
        PoolComplexity complejidad = PoolComplexity.Alta,
        string titulo = "Corregir el cálculo de facturación") =>
        new()
        {
            Title          = titulo,
            WorkType       = tipo,
            Complexity     = complejidad,
            HorasLimite    = tipo.ComoBug() ? PlazoDelBug : null,
            HorasEstimadas = tipo.ComoBug() ? null : 6m
        };

    private async Task<PoolActivity> PublicarAsync(AppDbContext db, ICurrentUser admin, PoolActivity? borrador = null)
    {
        var (ok, mensaje, actividad) = await Svc(db, admin).CrearAsync(borrador ?? Borrador());
        Assert.True(ok, mensaje);
        return actividad!;
    }

    private static PoolActivity Releer(AppDbContext db, int id) =>
        db.PoolActivities.AsNoTracking().Single(a => a.Id == id);

    // ── «La hice yo» ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Propia_cierraLaActividadSinAbonarPuntosANadie()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        var actividad = await PublicarAsync(db, admin);
        Assert.True(actividad.Points > 0);   // la matriz le puso su valor al publicarla

        var (ok, mensaje) = await Svc(db, admin).MarcarComoPropiaAsync(actividad.Id);

        Assert.True(ok, mensaje);
        var releida = Releer(db, actividad.Id);
        Assert.Equal(PoolActivityStatus.Propia, releida.Status);
        // Ni entrada de puntos, ni traza a ninguna, ni dueño a quien abonárselos.
        Assert.Null(releida.PointEntryId);
        Assert.Null(releida.ClaimedByDeveloperId);
        Assert.Empty(db.PointEntries.AsNoTracking().ToList());
        // Los puntos congelados se ponen a cero: la rejilla los pinta, y un «12» al lado de un
        // estado que no abonó nada se lee como un abono perdido.
        Assert.Equal(0, releida.Points);
        // Pero cuánto valía no se pierde.
        Assert.Contains("Valía", releida.ReviewHistory ?? "");
    }

    [Fact]
    public async Task Propia_dejaDeEstarDisponible_asiQueNadiePuedeTomarla()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin);

        Assert.True((await Svc(db, admin).MarcarComoPropiaAsync(actividad.Id)).ok);

        Assert.Empty(await Svc(db, Dev(dev)).DisponiblesAsync());
        var (ok, mensaje) = await Svc(db, Dev(dev)).TomarAsync(actividad.Id, dev, EstimacionAlTomar);
        Assert.False(ok);
        Assert.NotEmpty(mensaje);
    }

    [Fact]
    public async Task Propia_noSePuedeSobreAlgoQueAlguienYaTomo()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin);
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);

        var (ok, mensaje) = await Svc(db, admin).MarcarComoPropiaAsync(actividad.Id);

        Assert.False(ok);
        Assert.Contains("Liberar", mensaje);
        Assert.Equal(PoolActivityStatus.Tomada, Releer(db, actividad.Id).Status);
    }

    [Fact]
    public async Task Propia_esSoloDelLider()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, Admin());

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, Dev(dev)).MarcarComoPropiaAsync(actividad.Id));
    }

    // ── Eliminar ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Eliminar_borraLoQueSigueLibre_yNoDejaNada()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        var actividad = await PublicarAsync(db, admin);

        var (ok, mensaje) = await Svc(db, admin).EliminarAsync(actividad.Id);

        Assert.True(ok, mensaje);
        Assert.Empty(db.PoolActivities.AsNoTracking().Where(a => a.Id == actividad.Id).ToList());
    }

    [Fact]
    public async Task Eliminar_seLlevaElChecklistDeLaActividad()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin);

        // Tomarla es lo que le copia el checklist; se libera después para poder borrarla.
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);
        Assert.True((await Svc(db, admin).LiberarAsync(actividad.Id, "ya no aplica")).ok);

        Assert.True((await Svc(db, admin).EliminarAsync(actividad.Id)).ok);

        Assert.Empty(db.PoolActivityChecklistItems.AsNoTracking()
            .Where(c => c.PoolActivityId == actividad.Id).ToList());
    }

    [Fact]
    public async Task Eliminar_noBorraLoQueAlguienTieneTomado()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin);
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);

        var (ok, mensaje) = await Svc(db, admin).EliminarAsync(actividad.Id);

        Assert.False(ok);
        Assert.Contains("Liberar", mensaje);
        Assert.NotNull(Releer(db, actividad.Id));
    }

    [Fact]
    public async Task Eliminar_nuncaBorraLoQueYaAbonoPuntos()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin);

        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);
        var svc = Svc(db, Dev(dev));
        foreach (var item in await svc.ChecklistDeAsync(actividad.Id))
            Assert.True((await svc.MarcarItemAsync(item.Id, dev, true,
                item.RequiereEvidencia ? "https://dev.azure.com/o/p/_git/r/pullrequest/1" : null)).ok);
        Assert.True((await Svc(db, Dev(dev)).EntregarAsync(actividad.Id, dev)).ok);
        Assert.True((await Svc(db, admin).AceptarAsync(actividad.Id)).ok);

        var (ok, mensaje) = await Svc(db, admin).EliminarAsync(actividad.Id);

        Assert.False(ok);
        Assert.Contains("puntos", mensaje);
        Assert.Equal(PoolActivityStatus.Aceptada, Releer(db, actividad.Id).Status);
    }

    [Fact]
    public async Task Eliminar_esSoloDelLider()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, Admin());

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, Dev(dev)).EliminarAsync(actividad.Id));
    }

    // ── Lo cerrado no se queda pendiente de DevOps ───────────────────────────────

    /// <summary>
    /// La trampa que motivó <see cref="PoolActivity.EstadoCerradoSinEntrega"/>.
    ///
    /// <para>Una actividad que entró sola desde un work item nace SIN CLASIFICAR, y mientras lo esté
    /// no tiene nada que mandarle a DevOps. Al cerrarla —descartándola o declarándola propia— deja de
    /// estar sin clasificar, y con eso se encendía «la prioridad no ha llegado allá»: su prioridad
    /// nunca se mandó y el campo no es nulable, así que toda actividad nace en «Media». El resultado
    /// era una fila metida para siempre en el aviso ámbar del líder, que es la forma conocida de que
    /// una lista de pendientes deje de mirarse.</para>
    /// </summary>
    [Theory]
    [InlineData(PoolActivityStatus.Retirada)]
    [InlineData(PoolActivityStatus.Propia)]
    public void LoCerradoSinEntregar_noSeQuedaPendienteDeDevOps(PoolActivityStatus estado)
    {
        var actividad = new PoolActivity
        {
            Title = "Vino de un work item", DevOpsWorkItemId = 4321,
            Priority = PoolPriority.Media, DevOpsPrioridadEnviada = null,
            Status = estado
        };

        Assert.False(actividad.PendienteDeEnviarADevOps);
    }

    /// <summary>
    /// Y la otra mitad: lo que SÍ sigue vivo se queda en la lista. Sin esto, la guarda de arriba
    /// podría estar apagando el aviso entero y la prueba seguiría en verde.
    /// </summary>
    [Fact]
    public void LoQueSigueVivo_siSeQuedaPendienteDeDevOps()
    {
        var actividad = new PoolActivity
        {
            Title = "Publicada y ligada", DevOpsWorkItemId = 4321,
            Priority = PoolPriority.Media, DevOpsPrioridadEnviada = null,
            Status = PoolActivityStatus.Disponible
        };

        Assert.True(actividad.PendienteDeEnviarADevOps);
    }

    // ── Retrabajo: el tipo que resta ─────────────────────────────────────────────

    [Fact]
    public async Task Retrabajo_naceConLosPuntosNegativosDeLaMatriz()
    {
        var db = await BaseConPoolAsync();

        var actividad = await PublicarAsync(db, Admin(), Borrador(PoolWorkType.Retrabajo));

        var esperados = PoolSeed.Matriz
            .Single(m => m.Tipo == PoolWorkType.Retrabajo && m.Complejidad == PoolComplexity.Alta).Puntos;
        Assert.True(esperados < 0);
        Assert.Equal(esperados, actividad.Points);
    }

    /// <summary>
    /// Un retrabajo es un BUG en todo lo que toca a las horas: el plazo lo pone el líder —y sin él no
    /// se publica— y el esfuerzo lo estima quien lo toma. Es lo que asegura
    /// <c>TipoDeTrabajoDelPool.ComoBug</c>, y sin esa frase compartida esta regla se habría quedado
    /// escrita solo para el tipo «Bug».
    /// </summary>
    [Fact]
    public async Task Retrabajo_seComportaComoUnBugConLasHoras()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();

        var sinPlazo = Borrador(PoolWorkType.Retrabajo);
        sinPlazo.HorasLimite = null;
        var (ok, mensaje, _) = await Svc(db, admin).CrearAsync(sinPlazo);
        Assert.False(ok);
        Assert.Contains("plazo", mensaje);

        var conEsfuerzo = Borrador(PoolWorkType.Retrabajo);
        conEsfuerzo.HorasEstimadas = 5m;
        (ok, mensaje, _) = await Svc(db, admin).CrearAsync(conEsfuerzo);
        Assert.False(ok);
        Assert.Contains("esfuerzo", mensaje);

        // Y quien lo toma sí tiene que estimarlo.
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin, Borrador(PoolWorkType.Retrabajo));
        var (tomo, porQue) = await Svc(db, Dev(dev)).TomarAsync(actividad.Id, dev);
        Assert.False(tomo);
        Assert.NotEmpty(porQue);
    }

    [Fact]
    public async Task Aceptar_unRetrabajo_leRestaPuntosAQuienLoHizo()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin, Borrador(PoolWorkType.Retrabajo));

        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);
        var svc = Svc(db, Dev(dev));
        foreach (var item in await svc.ChecklistDeAsync(actividad.Id))
            Assert.True((await svc.MarcarItemAsync(item.Id, dev, true,
                item.RequiereEvidencia ? "https://dev.azure.com/o/p/_git/r/pullrequest/1" : null)).ok);
        Assert.True((await Svc(db, Dev(dev)).EntregarAsync(actividad.Id, dev)).ok);

        var (ok, mensaje) = await Svc(db, admin).AceptarAsync(actividad.Id);

        Assert.True(ok, mensaje);
        Assert.Contains("DESCONTARON", mensaje);
        var entrada = Assert.Single(db.PointEntries.AsNoTracking().ToList());
        Assert.True(entrada.Points < 0);
        Assert.Equal(dev, entrada.DeveloperId);
    }

    // ── La matriz: el signo lo manda el tipo ─────────────────────────────────────

    [Fact]
    public async Task Matriz_unRetrabajoEnPositivoSeRechaza()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        var matriz = await Svc(db, admin).ObtenerMatrizAsync();
        matriz.First(m => m.WorkType == PoolWorkType.Retrabajo).Points = 5;

        var (ok, mensaje) = await Svc(db, admin).GuardarMatrizAsync(matriz);

        Assert.False(ok);
        Assert.Contains("negativos", mensaje);
    }

    [Fact]
    public async Task Matriz_unBugEnNegativoSeRechaza()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        var matriz = await Svc(db, admin).ObtenerMatrizAsync();
        matriz.First(m => m.WorkType == PoolWorkType.Bug).Points = -5;

        var (ok, mensaje) = await Svc(db, admin).GuardarMatrizAsync(matriz);

        Assert.False(ok);
        Assert.Contains("mayores que cero", mensaje);
    }
}
