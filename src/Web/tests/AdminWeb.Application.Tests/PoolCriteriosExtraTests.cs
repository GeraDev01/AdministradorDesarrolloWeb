using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Lo que se decide al PUBLICAR una actividad del pool: su urgencia, cuántas HORAS de plazo se
/// conceden y qué criterios extra se van a evaluar.
///
/// <para>El archivo entero gira alrededor de una asimetría deliberada: <b>el plazo y la prioridad se
/// ajustan por actividad, los puntos base NO.</b> Aflojar un plazo o subir una urgencia no vale
/// puntos, así que ninguno de los dos sirve para regalarlos; los criterios extra sí suman, pero solo
/// si el líder los da por cumplidos al verificar la entrega, que es lo que los separa de un aumento
/// de puntos disfrazado.</para>
///
/// <para>El plazo se dice en HORAS y no en días: es lo que permite contrastarlo con lo que miden los
/// cronómetros, que registran horas. Las reglas completas del modelo de horas —quién pone cada
/// número y qué pasa al tomar— viven en <see cref="PoolHorasTests"/>; aquí solo la parte que se
/// decide al publicar.</para>
///
/// <para>Va en archivo aparte de <see cref="PoolActivityServiceTests"/> porque prueba una decisión
/// distinta: aquélla cuida que el valor esté congelado antes de trabajar; ésta, que lo que se añade
/// encima se gane.</para>
/// </summary>
public class PoolCriteriosExtraTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    /// <summary>Un contexto por llamada, como en el resto de las pruebas del pool: en la web es uno
    /// por petición, así que compartirlo probaría un escenario que no existe.</summary>
    private PoolActivityService Svc(AppDbContext db, ICurrentUser cu)
    {
        var ctx = OtroContexto(db);
        var bitacora = new AuditService(ctx, cu, new OrigenDePrueba());
        return new PoolActivityService(ctx, cu, bitacora, new NotificationService(ctx),
                                       new SettingsService(ctx, cu, bitacora));
    }

    private PoolQueryService Consultas(AppDbContext db, ICurrentUser cu)
    {
        var ctx = OtroContexto(db);
        return new PoolQueryService(ctx, cu, Svc(db, cu));
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

    private static UsuarioDePrueba Admin(int userId = 9) => UsuarioDePrueba.Como(UserRole.Admin, userId: userId);
    private static UsuarioDePrueba Dev(int developerId, int userId = 1) =>
        UsuarioDePrueba.Como(UserRole.Desarrollador, developerId, userId);

    private static int NuevoDesarrollador(AppDbContext db, string nombre)
    {
        var d = new Developer { FullName = nombre, IsActive = true };
        db.Developers.Add(d);
        db.SaveChanges();
        return d.Id;
    }

    private static int NuevoCriterio(AppDbContext db, string nombre, int puntos,
        CriterionScope alcance = CriterionScope.Individual, bool activo = true)
    {
        var c = new ScoringCriterion
        {
            Name = nombre, DefaultPoints = puntos, Scope = alcance, IsActive = activo
        };
        db.ScoringCriteria.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    /// <summary>El plazo que el líder concede al bug de estas pruebas, en horas.</summary>
    private const decimal PlazoDelBug = 16m;

    /// <summary>Lo que dice quien toma el bug: en cuántas horas cree resolverlo. Sin esto no se puede tomar.</summary>
    private const decimal EstimacionAlTomar = 4m;

    /// <summary>Un bug válido: con su plazo en horas, que en un bug lo pone el líder y es obligatorio.</summary>
    private static PoolActivity Borrador(string titulo = "Corregir el cálculo de facturación") =>
        new()
        {
            Title = titulo, WorkType = PoolWorkType.Bug, Complexity = PoolComplexity.Alta,
            HorasLimite = PlazoDelBug
        };

    /// <summary>Publica, la toma, completa el checklist y la entrega: la deja lista para verificar.</summary>
    private async Task<PoolActivity> HastaRevisionAsync(
        AppDbContext db, ICurrentUser admin, int dev, IReadOnlyList<int>? extras = null)
    {
        var (ok, mensaje, actividad) = await Svc(db, admin).CrearAsync(Borrador(), extras);
        Assert.True(ok, mensaje);

        var cu = Dev(dev);
        Assert.True((await Svc(db, cu).TomarAsync(actividad!.Id, dev, EstimacionAlTomar)).ok);

        var svc = Svc(db, cu);
        foreach (var item in await svc.ChecklistDeAsync(actividad.Id))
        {
            var (marcado, error) = await svc.MarcarItemAsync(item.Id, dev, true,
                item.RequiereEvidencia ? "https://dev.azure.com/org/proj/_git/repo/pullrequest/42" : null);
            Assert.True(marcado, error);
        }

        Assert.True((await Svc(db, cu).EntregarAsync(actividad.Id, dev)).ok);
        return actividad;
    }

    private static List<PoolActivityExtraCriterion> ExtrasDe(AppDbContext db, int actividadId) =>
        [.. db.PoolActivityExtraCriteria.AsNoTracking().Where(x => x.PoolActivityId == actividadId)];

    // ── Prioridad y plazo ────────────────────────────────────────────────────────

    [Fact]
    public async Task Crear_GuardaLaPrioridadYElPlazoPropioEnHoras()
    {
        var db = await BaseConPoolAsync();
        var borrador = Borrador();
        borrador.Priority = PoolPriority.Critica;
        borrador.HorasLimite = 2m;

        var (ok, mensaje, actividad) = await Svc(db, Admin()).CrearAsync(borrador);
        Assert.True(ok, mensaje);

        var guardada = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad!.Id);
        Assert.Equal(PoolPriority.Critica, guardada.Priority);
        Assert.Equal(2m, guardada.HorasLimite);

        // Y la columna vieja de DÍAS se queda como estaba. La web ya no la escribe: mantener las dos
        // sería mantener dos verdades sobre lo mismo, que se contradirían a la primera edición.
        Assert.Null(guardada.DiasLimite);
    }

    /// <summary>
    /// La asimetría que sostiene todo el diseño: si la urgencia o el plazo movieran los puntos,
    /// publicar «Crítica» sería la manera de regalarlos y la matriz dejaría de significar nada.
    /// </summary>
    [Fact]
    public async Task NiLaPrioridadNiElPlazo_MuevenLosPuntos()
    {
        var db = await BaseConPoolAsync();
        var (_, _, normal) = await Svc(db, Admin()).CrearAsync(Borrador("Normal"));

        var urgente = Borrador("Urgente");
        urgente.Priority = PoolPriority.Critica;
        urgente.HorasLimite = 1m;
        var (_, _, apurada) = await Svc(db, Admin()).CrearAsync(urgente);

        Assert.Equal(normal!.Points, apurada!.Points);
    }

    [Fact]
    public async Task Las_horas_de_la_actividad_mandan_sobre_las_de_la_matriz()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");

        // La celda Bug/Alta de la matriz da 64 h; este bug dice 10 y son las que valen.
        var borrador = Borrador();
        borrador.HorasLimite = 10m;
        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(borrador);

        var antesDeTomar = DateTime.UtcNow;
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev, EstimacionAlTomar)).ok);

        var tomada = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.NotNull(tomada.ClaimDeadlineAt);
        // El plazo cuenta desde que se TOMA y no desde que se publicó: si contara desde la
        // publicación, una actividad que esperó dos semanas en el pool llegaría ya vencida.
        Assert.InRange((tomada.ClaimDeadlineAt!.Value - antesDeTomar).TotalHours, 9.9, 10.1);
    }

    [Fact]
    public async Task Cero_horas_significa_SIN_fecha_limite()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");

        var borrador = Borrador();
        borrador.HorasLimite = 0m;
        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(borrador);
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad!.Id, dev, EstimacionAlTomar)).ok);

        Assert.Null(db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id).ClaimDeadlineAt);
    }

    /// <summary>
    /// El tope de arriba, 2 920 h, son los 365 días de antes por una jornada de ocho: el mismo techo
    /// dicho en la unidad nueva, para que el cambio de unidad no ampliara de tapadillo lo que se
    /// puede prometer.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(2921)]
    public async Task Un_plazo_fuera_de_rango_se_rechaza(int horas)
    {
        var db = await BaseConPoolAsync();
        var borrador = Borrador();
        borrador.HorasLimite = horas;

        var (ok, mensaje, _) = await Svc(db, Admin()).CrearAsync(borrador);

        Assert.False(ok);
        Assert.Contains("entre 0 y 2920 horas", mensaje);
        Assert.Empty(db.PoolActivities.AsNoTracking());
    }

    // ── Los criterios extra, al publicar ─────────────────────────────────────────

    [Fact]
    public async Task Los_criterios_extra_se_copian_con_sus_puntos_CONGELADOS()
    {
        var db = await BaseConPoolAsync();
        int idCriterio = NuevoCriterio(db, "Pruebas automatizadas", 5);

        var (ok, mensaje, actividad) = await Svc(db, Admin()).CrearAsync(Borrador(), [idCriterio]);
        Assert.True(ok, mensaje);

        // El líder baja el criterio DESPUÉS de publicar, mientras alguien podría estar trabajándola.
        var c = db.ScoringCriteria.Single(x => x.Id == idCriterio);
        c.DefaultPoints = 1;
        db.SaveChanges();

        var copia = Assert.Single(ExtrasDe(db, actividad!.Id));
        Assert.Equal(5, copia.Points);                      // cobra lo que se vio al publicarla
        Assert.Equal("Pruebas automatizadas", copia.Name);
        Assert.Equal(idCriterio, copia.ScoringCriterionId);
        Assert.Null(copia.IsMet);                           // nadie lo ha evaluado todavía
    }

    [Fact]
    public async Task Un_criterio_de_EQUIPO_no_puede_sumar_en_una_actividad_del_pool()
    {
        var db = await BaseConPoolAsync();
        int idEquipo = NuevoCriterio(db, "Entrega del equipo a tiempo", 8, CriterionScope.Equipo);

        var (ok, mensaje, _) = await Svc(db, Admin()).CrearAsync(Borrador(), [idEquipo]);

        Assert.False(ok);
        Assert.Contains("criterio de equipo", mensaje);
        Assert.Empty(db.PoolActivityExtraCriteria.AsNoTracking());
    }

    [Fact]
    public async Task Un_criterio_inactivo_o_inexistente_se_ignora_sin_tumbar_la_publicacion()
    {
        var db = await BaseConPoolAsync();
        int inactivo = NuevoCriterio(db, "Ya no se pide", 4, activo: false);
        int bueno = NuevoCriterio(db, "Documentación", 3);

        var (ok, _, actividad) = await Svc(db, Admin()).CrearAsync(Borrador(), [inactivo, bueno, 99999]);

        Assert.True(ok);
        var unico = Assert.Single(ExtrasDe(db, actividad!.Id));
        Assert.Equal("Documentación", unico.Name);
    }

    [Fact]
    public async Task El_mismo_criterio_repetido_solo_cuenta_una_vez()
    {
        var db = await BaseConPoolAsync();
        int id = NuevoCriterio(db, "Documentación", 3);

        var (ok, _, actividad) = await Svc(db, Admin()).CrearAsync(Borrador(), [id, id, id]);

        Assert.True(ok);
        Assert.Single(ExtrasDe(db, actividad!.Id));
    }

    [Fact]
    public async Task Editar_reemplaza_los_criterios_mientras_la_actividad_siga_libre()
    {
        var db = await BaseConPoolAsync();
        int docu = NuevoCriterio(db, "Documentación", 3);
        int test = NuevoCriterio(db, "Pruebas automatizadas", 5);

        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(Borrador(), [docu]);

        var cambios = Borrador();
        cambios.Priority = PoolPriority.Alta;
        var (ok, mensaje) = await Svc(db, Admin()).EditarAsync(actividad!.Id, cambios, [test]);
        Assert.True(ok, mensaje);

        var unico = Assert.Single(ExtrasDe(db, actividad.Id));
        Assert.Equal("Pruebas automatizadas", unico.Name);
        Assert.Equal(PoolPriority.Alta,
            db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id).Priority);
    }

    // ── La evaluación, al verificar ──────────────────────────────────────────────

    /// <summary>La prueba central del archivo: solo suman los criterios dados por CUMPLIDOS.</summary>
    [Fact]
    public async Task Al_aceptar_solo_suman_los_criterios_CUMPLIDOS()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        int docu = NuevoCriterio(db, "Documentación", 3);
        int test = NuevoCriterio(db, "Pruebas automatizadas", 5);

        var actividad = await HastaRevisionAsync(db, admin, dev, [docu, test]);
        int puntosBase = actividad.Points;

        var extras = ExtrasDe(db, actividad.Id);
        var deDocu = extras.Single(x => x.Name == "Documentación");
        var deTest = extras.Single(x => x.Name == "Pruebas automatizadas");

        Assert.True((await Svc(db, admin).EvaluarCriterioExtraAsync(deDocu.Id, true, "Quedó en la wiki")).ok);
        Assert.True((await Svc(db, admin).EvaluarCriterioExtraAsync(deTest.Id, false, "No trajo pruebas")).ok);

        var (ok, mensaje) = await Svc(db, admin).AceptarAsync(actividad.Id);
        Assert.True(ok, mensaje);

        var entrada = db.PointEntries.AsNoTracking().Single(p => p.DeveloperId == dev);
        Assert.Equal(puntosBase + 3, entrada.Points);              // +3 de documentación, NO +5
        Assert.Contains("Documentación", entrada.Comment);
        Assert.DoesNotContain("Pruebas automatizadas", entrada.Comment);
    }

    /// <summary>
    /// Aceptar con criterios sin evaluar se BLOQUEA en vez de contarlos como no cumplidos. Contarlos
    /// en silencio perdería los puntos sin que nadie lo decidiera, y como después de aceptar ya no
    /// se pueden tocar, el error no tendría arreglo.
    /// </summary>
    [Fact]
    public async Task No_se_puede_aceptar_con_criterios_SIN_evaluar()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        int docu = NuevoCriterio(db, "Documentación", 3);

        var actividad = await HastaRevisionAsync(db, admin, dev, [docu]);

        var (ok, mensaje) = await Svc(db, admin).AceptarAsync(actividad.Id);

        Assert.False(ok);
        Assert.Contains("Falta evaluar", mensaje);
        Assert.Contains("Documentación", mensaje);
        Assert.Empty(db.PointEntries.AsNoTracking());              // no se abonó nada
        Assert.Equal(PoolActivityStatus.EnRevision,
            db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id).Status);
    }

    [Fact]
    public async Task Sin_criterios_extra_todo_sigue_exactamente_igual_que_antes()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");

        var actividad = await HastaRevisionAsync(db, admin, dev);
        Assert.True((await Svc(db, admin).AceptarAsync(actividad.Id)).ok);

        Assert.Equal(actividad.Points, db.PointEntries.AsNoTracking().Single().Points);
    }

    [Fact]
    public async Task Los_criterios_NO_se_evaluan_antes_de_que_haya_una_entrega()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int docu = NuevoCriterio(db, "Documentación", 3);
        var (_, _, actividad) = await Svc(db, admin).CrearAsync(Borrador(), [docu]);

        var extra = Assert.Single(ExtrasDe(db, actividad!.Id));

        var (ok, mensaje) = await Svc(db, admin).EvaluarCriterioExtraAsync(extra.Id, true, null);

        Assert.False(ok);
        Assert.Contains("cuando hay una entrega", mensaje);
    }

    [Fact]
    public async Task Despues_de_aceptar_los_criterios_ya_no_se_tocan()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        int docu = NuevoCriterio(db, "Documentación", 3);

        var actividad = await HastaRevisionAsync(db, admin, dev, [docu]);
        var extra = Assert.Single(ExtrasDe(db, actividad.Id));

        Assert.True((await Svc(db, admin).EvaluarCriterioExtraAsync(extra.Id, false, null)).ok);
        Assert.True((await Svc(db, admin).AceptarAsync(actividad.Id)).ok);

        var (ok, mensaje) = await Svc(db, admin).EvaluarCriterioExtraAsync(extra.Id, true, "Me equivoqué");

        Assert.False(ok);
        Assert.Contains("ya se aceptó", mensaje);
        Assert.Equal(actividad.Points, db.PointEntries.AsNoTracking().Single().Points);
    }

    [Fact]
    public async Task Solo_el_lider_evalua_los_criterios_extra()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        int docu = NuevoCriterio(db, "Documentación", 3);

        var actividad = await HastaRevisionAsync(db, admin, dev, [docu]);
        var extra = Assert.Single(ExtrasDe(db, actividad.Id));

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, Dev(dev)).EvaluarCriterioExtraAsync(extra.Id, true, null));
    }

    // ── Lo que se ve antes de tomarla ────────────────────────────────────────────

    /// <summary>
    /// Quien mira el pool tiene que ver los criterios extra ANTES de tomar la actividad: enterarse
    /// después de que «además había que documentarla» convertiría el extra en una trampa.
    /// </summary>
    [Fact]
    public async Task El_desarrollador_ve_los_criterios_y_el_maximo_antes_de_tomarla()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        int docu = NuevoCriterio(db, "Documentación", 3);
        int test = NuevoCriterio(db, "Pruebas automatizadas", 5);

        var (_, _, actividad) = await Svc(db, Admin()).CrearAsync(Borrador(), [docu, test]);

        var vista = await Consultas(db, Dev(dev)).MiPoolAsync();
        var libre = Assert.Single(vista.Disponibles);

        Assert.Equal(actividad!.Points, libre.Puntos);
        Assert.Equal(actividad.Points + 8, libre.PuntosMaximos);
        Assert.Equal(2, libre.CriteriosExtra.Count);
        Assert.All(libre.CriteriosExtra, c => Assert.Null(c.Cumplido));
    }

    [Fact]
    public async Task El_catalogo_para_elegir_deja_fuera_los_de_equipo_y_los_de_cero_puntos()
    {
        var db = await BaseConPoolAsync();
        NuevoCriterio(db, "Documentación", 3);
        NuevoCriterio(db, "Entrega del equipo", 8, CriterionScope.Equipo);
        NuevoCriterio(db, "No vale nada", 0);
        NuevoCriterio(db, "Retirado", 4, activo: false);

        var catalogo = await Consultas(db, Admin()).CriteriosExtraDisponiblesAsync();

        Assert.Contains(catalogo, c => c.Nombre == "Documentación");
        Assert.DoesNotContain(catalogo, c => c.Nombre == "Entrega del equipo");
        Assert.DoesNotContain(catalogo, c => c.Nombre == "No vale nada");
        Assert.DoesNotContain(catalogo, c => c.Nombre == "Retirado");
    }
}
