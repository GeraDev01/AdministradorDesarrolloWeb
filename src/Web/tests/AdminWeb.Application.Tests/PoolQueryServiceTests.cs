using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Lo que arma las dos pantallas del pool.
///
/// Aquí NO se vuelve a probar la lógica del pool —eso ya lo cubre
/// <see cref="PoolActivityServiceTests"/>—, sino lo único que esta capa aporta: el avance del
/// checklist contado de una vez para todas las actividades, qué se le enseña a una cuenta sin ficha
/// de desarrollador, cuándo acompaña el motivo de una devolución y que la configuración sea del
/// líder.
/// </summary>
public class PoolQueryServiceTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    /// <summary>
    /// Cada servicio con su PROPIO contexto contra la misma base, igual que en las pruebas del pool:
    /// en la web hay uno por petición, así que una consulta tiene que ver lo que la escritura
    /// anterior dejó en la base y no lo que quedó rastreado en memoria.
    /// </summary>
    private PoolActivityService Pool(AppDbContext db, ICurrentUser cu)
    {
        var ctx = OtroContexto(db);
        return new PoolActivityService(ctx, cu, new AuditService(ctx, cu, new OrigenDePrueba()),
                                       new NotificationService(ctx),
                                       new SettingsService(ctx, cu, new AuditService(ctx, cu, new OrigenDePrueba())));
    }

    private PoolQueryService Consultas(AppDbContext db, ICurrentUser cu) =>
        new(OtroContexto(db), cu, Pool(db, cu));

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

    private static int NuevoDesarrollador(AppDbContext db, string nombre = "Ana Ruiz")
    {
        var d = new Developer { FullName = nombre, IsActive = true };
        db.Developers.Add(d);
        db.SaveChanges();
        return d.Id;
    }

    private static UsuarioDePrueba Admin() => UsuarioDePrueba.Como(UserRole.Admin, userId: 9);
    private static UsuarioDePrueba Dev(int? developerId) =>
        UsuarioDePrueba.Como(UserRole.Desarrollador, developerId, userId: 1);

    /// <summary>El plazo que el líder le pone al bug de estas pruebas; en un bug es obligatorio.</summary>
    private const decimal PlazoDelBug = 16m;

    /// <summary>Lo que dice quien lo toma: en cuántas horas cree resolverlo. Sin esto no se puede tomar.</summary>
    private const decimal EstimacionAlTomar = 4m;

    private async Task<PoolActivity> PublicarAsync(AppDbContext db, ICurrentUser admin)
    {
        var (ok, mensaje, actividad) = await Pool(db, admin).CrearAsync(new PoolActivity
        {
            Title = "Corregir el cálculo de facturación",
            WorkType = PoolWorkType.Bug,
            Complexity = PoolComplexity.Alta,
            HorasLimite = PlazoDelBug
        });
        Assert.True(ok, mensaje);
        return actividad!;
    }

    /// <summary>Marca todo el checklist, con evidencia donde se exige.</summary>
    private async Task CompletarChecklistAsync(AppDbContext db, ICurrentUser cu, int actividadId, int devId)
    {
        var svc = Pool(db, cu);
        foreach (var item in await svc.ChecklistDeAsync(actividadId))
        {
            var (ok, mensaje) = await svc.MarcarItemAsync(item.Id, devId, true,
                item.RequiereEvidencia ? "https://dev.azure.com/org/proj/_git/repo/pullrequest/42" : null);
            Assert.True(ok, mensaje);
        }
    }

    // ── Avance del checklist ─────────────────────────────────────────────────────

    [Fact]
    public async Task MiPool_CuentaElAvanceDelChecklistDeCadaActividad()
    {
        var db = await BaseConPoolAsync();
        int devId = NuevoDesarrollador(db);
        var dev = Dev(devId);

        var actividad = await PublicarAsync(db, Admin());
        var (tomada, mensaje) = await Pool(db, dev).TomarAsync(actividad.Id, devId, EstimacionAlTomar);
        Assert.True(tomada, mensaje);

        var puntos = await Pool(db, dev).ChecklistDeAsync(actividad.Id);
        var primero = puntos.First(p => !p.RequiereEvidencia);
        await Pool(db, dev).MarcarItemAsync(primero.Id, devId, true, null);

        var mia = Assert.Single((await Consultas(db, dev).MiPoolAsync()).Mias);
        Assert.Equal(1, mia.ChecklistHechos);
        Assert.Equal(puntos.Count, mia.ChecklistTotal);
        Assert.Equal($"1/{puntos.Count}", mia.Avance);
    }

    [Fact]
    public async Task MiPool_LoLibreSigueEnElPoolYNoCuentaComoMio()
    {
        var db = await BaseConPoolAsync();
        int devId = NuevoDesarrollador(db);
        await PublicarAsync(db, Admin());

        var datos = await Consultas(db, Dev(devId)).MiPoolAsync();

        Assert.True(datos.TieneFicha);
        Assert.Single(datos.Disponibles);
        Assert.Empty(datos.Mias);
    }

    // ── Cuenta sin ficha ─────────────────────────────────────────────────────────

    [Fact]
    public async Task MiPool_SinFicha_VeElPoolPeroNoTieneActividades()
    {
        var db = await BaseConPoolAsync();
        await PublicarAsync(db, Admin());

        // Mirar qué hay no le hace daño a nadie; tomarlo sí sería un problema, porque los puntos se
        // abonan a una ficha y esta cuenta no tiene ninguna.
        var datos = await Consultas(db, Dev(developerId: null)).MiPoolAsync();

        Assert.False(datos.TieneFicha);
        Assert.Single(datos.Disponibles);
        Assert.Empty(datos.Mias);
    }

    // ── El motivo de la devolución ───────────────────────────────────────────────

    [Fact]
    public async Task MiPool_ElMotivoSoloAcompanaALaDevuelta()
    {
        var db = await BaseConPoolAsync();
        int devId = NuevoDesarrollador(db);
        var dev = Dev(devId);
        var admin = Admin();

        var actividad = await PublicarAsync(db, admin);
        await Pool(db, dev).TomarAsync(actividad.Id, devId, EstimacionAlTomar);
        await CompletarChecklistAsync(db, dev, actividad.Id, devId);
        await Pool(db, dev).EntregarAsync(actividad.Id, devId);

        var (rechazada, mensaje) = await Pool(db, admin).RechazarAsync(actividad.Id, "Falta la prueba en producción.");
        Assert.True(rechazada, mensaje);

        var devuelta = Assert.Single((await Consultas(db, dev).MiPoolAsync()).Mias);
        Assert.Equal(PoolActivityStatus.Devuelta, devuelta.Estado);
        Assert.Equal("Falta la prueba en producción.", devuelta.MotivoDeDevolucion);

        // Al volver a entregarla el comentario sigue guardado, pero enseñarlo haría creer que
        // todavía hay algo que corregir.
        await Pool(db, dev).EntregarAsync(actividad.Id, devId);

        var reentregada = Assert.Single((await Consultas(db, dev).MiPoolAsync()).Mias);
        Assert.Equal(PoolActivityStatus.EnRevision, reentregada.Estado);
        Assert.Null(reentregada.MotivoDeDevolucion);
    }

    // ── Pantalla del líder ───────────────────────────────────────────────────────

    [Fact]
    public async Task PoolDelLider_LaSegundaEntregaSeVeComoSegundaVuelta()
    {
        var db = await BaseConPoolAsync();
        int devId = NuevoDesarrollador(db, "Ana Ruiz");
        var dev = Dev(devId);
        var admin = Admin();

        var actividad = await PublicarAsync(db, admin);
        await Pool(db, dev).TomarAsync(actividad.Id, devId, EstimacionAlTomar);
        await CompletarChecklistAsync(db, dev, actividad.Id, devId);
        await Pool(db, dev).EntregarAsync(actividad.Id, devId);
        await Pool(db, admin).RechazarAsync(actividad.Id, "Falta la prueba en producción.");
        await Pool(db, dev).EntregarAsync(actividad.Id, devId);

        var datos = await Consultas(db, admin).PoolDelLiderAsync();

        var pendiente = Assert.Single(datos.Pendientes);
        Assert.Equal(2, pendiente.Vuelta);
        Assert.Equal("Ana Ruiz", pendiente.QuienLaEntrego);
        Assert.Contains("Entregada por", pendiente.UltimoComentario);

        var enElPool = Assert.Single(datos.Actividades);
        Assert.Equal("Ana Ruiz", enElPool.QuienLaTiene);
        Assert.Equal(actividad.Points, enElPool.Puntos);
    }

    // ── El plazo que se enseña antes de tomarla ──────────────────────────────────

    /// <summary>
    /// El plazo EFECTIVO que se enseña en el pool tiene que ser el mismo que se va a aplicar al
    /// tomar la actividad. La regla —«el de la actividad manda; si no hay, el de su celda»— está
    /// escrita en dos sitios: la resuelve esta capa para pintarla y la vuelve a resolver
    /// <c>TomarAsync</c> para calcular el instante. Dos copias de una regla divergen tarde o
    /// temprano, y aquí la divergencia sería de las peores: alguien tomaría una actividad creyendo
    /// que tiene un plazo y se encontraría con otro.
    ///
    /// <para>Por eso la prueba no compara la vista contra un número escrito a mano, sino contra el
    /// plazo REAL que quedó tras tomarla. Así, si una de las dos copias cambia, esto se pone rojo.</para>
    /// </summary>
    [Fact]
    public async Task MiPool_ElPlazoQueSeEnsena_EsElMismoQueSeAplicaAlTomarla()
    {
        var db = await BaseConPoolAsync();
        int devId = NuevoDesarrollador(db);
        var dev = Dev(devId);
        var admin = Admin();

        // Una TAREA, que es el caso donde el plazo no está en la actividad sino en la matriz: es
        // justo el que se rompería si esta capa se olvidara de resolverlo.
        var (creada, mensaje, actividad) = await Pool(db, admin).CrearAsync(new PoolActivity
        {
            Title = "Migrar el reporte mensual",
            WorkType = PoolWorkType.Tarea,
            Complexity = PoolComplexity.Media,
            HorasEstimadas = 6m
        });
        Assert.True(creada, mensaje);

        var enElPool = Assert.Single((await Consultas(db, dev).MiPoolAsync()).Disponibles);
        Assert.Null(enElPool.Horas);                 // no tiene plazo propio…
        Assert.NotNull(enElPool.HorasEfectivas);     // …pero se le enseña el de su celda, ya resuelto
        Assert.Equal(PoolSeed.Matriz.Single(m => m.Tipo == PoolWorkType.Tarea &&
                                                 m.Complejidad == PoolComplexity.Media).Horas,
                     enElPool.HorasEfectivas);

        var antesDeTomar = DateTime.UtcNow;
        Assert.True((await Pool(db, dev).TomarAsync(actividad!.Id, devId)).ok);

        var mia = Assert.Single((await Consultas(db, dev).MiPoolAsync()).Mias);
        Assert.NotNull(mia.LimiteUtc);
        var concedidas = (mia.LimiteUtc!.Value - antesDeTomar).TotalHours;
        Assert.InRange(concedidas, (double)enElPool.HorasEfectivas! - 0.1, (double)enElPool.HorasEfectivas! + 0.1);
    }

    /// <summary>
    /// El líder ve el ESFUERZO que escribió quien tomó el bug. Es el pago de todo el cambio: sin ese
    /// número en su pantalla, no hay nada que contrastar con lo que marque el cronómetro.
    /// </summary>
    [Fact]
    public async Task PoolDelLider_MuestraElPlazoYLaEstimacionDeQuienLoTomo()
    {
        var db = await BaseConPoolAsync();
        int devId = NuevoDesarrollador(db, "Ana Ruiz");
        var dev = Dev(devId);
        var admin = Admin();

        var actividad = await PublicarAsync(db, admin);
        Assert.True((await Pool(db, dev).TomarAsync(actividad.Id, devId, 3.5m)).ok);

        var enElPool = Assert.Single((await Consultas(db, admin).PoolDelLiderAsync()).Actividades);
        Assert.Equal(PlazoDelBug, enElPool.Horas);          // el plazo lo puso él
        Assert.Equal(3.5m, enElPool.HorasEstimadas);        // el esfuerzo lo puso quien lo tomó
        Assert.Equal("Ana Ruiz", enElPool.QuienLaTiene);
    }

    // ── Configuración ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Configuracion_EsDelLider()
    {
        var db = await BaseConPoolAsync();
        int devId = NuevoDesarrollador(db);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Consultas(db, Dev(devId)).ConfiguracionAsync());
    }

    [Fact]
    public async Task Configuracion_TraeLaMatrizYLosPuntosDesactivados()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();

        var punto = (await Pool(db, admin).PlantillaAsync(PoolWorkType.Bug)).First();
        var (ok, mensaje) = await Pool(db, admin).DesactivarPlantillaItemAsync(punto.Id);
        Assert.True(ok, mensaje);

        var config = await Consultas(db, admin).ConfiguracionAsync();

        Assert.Equal(PoolSeed.Matriz.Length, config.Matriz.Count);
        Assert.Equal(PoolSeed.Checklists.Length, config.Plantilla.Count);

        // El desactivado sigue en la lista: el líder tiene que poder reactivarlo, y uno que
        // desaparece de la pantalla no deja hacerlo.
        var desactivado = config.Plantilla.Single(p => p.Id == punto.Id);
        Assert.False(desactivado.Activo);

        // Los desplegables los llena el servidor con las etiquetas del escritorio.
        Assert.Equal(Enum.GetValues<PoolWorkType>().Length, config.Tipos.Count);
        Assert.Equal(Enum.GetValues<PoolComplexity>().Length, config.Complejidades.Count);
        Assert.Equal(Enum.GetValues<PoolActivityStatus>().Length, config.Estados.Count);
        Assert.Contains(config.Estados, e => e.Texto == "Por verificar");
    }
}
