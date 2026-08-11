using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El pool de actividades valoradas.
///
/// Lo que estas pruebas cuidan es la promesa del sistema: que el valor de una actividad esté fijado
/// ANTES de trabajarla y no se pueda mover después, que lo que se le exige a alguien sea lo que se
/// le pidió al tomarla, y que aceptar una actividad abone sus puntos exactamente una vez.
/// </summary>
public class PoolActivityServiceTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    /// <summary>
    /// Cada servicio recibe su PROPIO contexto contra la misma base. Es la única diferencia real
    /// con las pruebas del escritorio, y no es un capricho: allí el contexto era Singleton y los
    /// servicios se defendían con <c>Reload()</c> antes de decidir; aquí es uno por petición, así
    /// que dos llamadas seguidas tienen que ver la base y no lo que quedó rastreado por la anterior.
    /// Compartir un contexto entre llamadas probaría un escenario que en la web no existe.
    /// </summary>
    private PoolActivityService Svc(AppDbContext db, ICurrentUser cu)
    {
        var ctx = OtroContexto(db);
        return new PoolActivityService(ctx, cu, new AuditService(ctx, cu, new OrigenDePrueba()),
                                       new NotificationService(ctx),
                                       new SettingsService(ctx, cu, new AuditService(ctx, cu, new OrigenDePrueba())));
    }

    private PerformanceScoringService Puntos(AppDbContext db, ICurrentUser cu) =>
        new(OtroContexto(db), cu);

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

    /// <summary>Base ya sembrada con la matriz, los checklists y los criterios del pool.</summary>
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

    /// <summary>
    /// El PLAZO que el líder concede a un bug, en horas. En un bug es obligatorio y lo pone él:
    /// publicarlo sin este número se rechaza, y por eso el borrador de abajo lo trae siempre.
    /// </summary>
    private const decimal PlazoDelBug = 16m;

    /// <summary>El ESFUERZO que el líder estima al publicar una tarea o un requerimiento, en horas.</summary>
    private const decimal EsfuerzoDelLider = 6m;

    /// <summary>
    /// Lo que dice quien toma un BUG: en cuántas horas cree resolverlo. Aparece en cada
    /// <c>TomarAsync</c> de este archivo porque sin él el bug no se puede tomar — esa regla se prueba
    /// aparte, en <see cref="PoolHorasTests"/>; aquí solo hace falta que las tomas sean válidas.
    /// </summary>
    private const decimal EstimacionAlTomar = 4m;

    /// <summary>
    /// Un borrador VÁLIDO del tipo que se pida, con el número que ese tipo exige y solo ése:
    /// el BUG lleva plazo del líder y ninguna estimación (la pone quien lo tome); la tarea y el
    /// requerimiento llevan estimación del líder y ningún plazo a mano (sale de la matriz).
    /// </summary>
    private static PoolActivity Borrador(string titulo = "Corregir el cálculo de facturación",
        PoolWorkType tipo = PoolWorkType.Bug, PoolComplexity complejidad = PoolComplexity.Alta) =>
        new()
        {
            Title          = titulo,
            WorkType       = tipo,
            Complexity     = complejidad,
            HorasLimite    = tipo == PoolWorkType.Bug ? PlazoDelBug : null,
            HorasEstimadas = tipo == PoolWorkType.Bug ? null : EsfuerzoDelLider
        };

    /// <summary>Crea una actividad y devuelve la entidad ya persistida.</summary>
    private async Task<PoolActivity> PublicarAsync(AppDbContext db, ICurrentUser admin, PoolActivity? borrador = null)
    {
        var (ok, mensaje, actividad) = await Svc(db, admin).CrearAsync(borrador ?? Borrador());
        Assert.True(ok, mensaje);
        return actividad!;
    }

    /// <summary>Marca todo el checklist de una actividad, poniendo evidencia donde hace falta.</summary>
    private async Task CompletarChecklistAsync(AppDbContext db, ICurrentUser cu, int actividadId, int devId)
    {
        var svc = Svc(db, cu);
        foreach (var item in await svc.ChecklistDeAsync(actividadId))
        {
            var (ok, mensaje) = await svc.MarcarItemAsync(item.Id, devId, true,
                item.RequiereEvidencia ? "https://dev.azure.com/org/proj/_git/repo/pullrequest/42" : null);
            Assert.True(ok, mensaje);
        }
    }

    // ── Crear: el valor se congela ───────────────────────────────────────────────

    [Fact]
    public async Task Crear_TomaLosPuntosDeLaMatriz_NoDelBorrador()
    {
        var db = await BaseConPoolAsync();
        // Aunque el borrador venga con un valor inflado, el que manda es el de la matriz.
        var borrador = Borrador();
        borrador.Points = 999;

        var actividad = await PublicarAsync(db, Admin(), borrador);

        var esperados = PoolSeed.Matriz.Single(m => m.Tipo == PoolWorkType.Bug && m.Complejidad == PoolComplexity.Alta).Puntos;
        Assert.Equal(esperados, actividad.Points);
    }

    [Fact]
    public async Task Crear_CongelaLosPuntos_CambiarLaMatrizDespuesNoLosMueve()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        var actividad = await PublicarAsync(db, admin);
        int puntosOriginales = actividad.Points;

        var matriz = await Svc(db, admin).ObtenerMatrizAsync();
        foreach (var celda in matriz) celda.Points = 1;
        Assert.True((await Svc(db, admin).GuardarMatrizAsync(matriz)).ok);

        var releida = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.Equal(puntosOriginales, releida.Points);
        Assert.NotEqual(1, releida.Points);
    }

    [Fact]
    public async Task Crear_RequiereAdmin()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, Dev(dev)).CrearAsync(Borrador()));
    }

    [Fact]
    public async Task Crear_SinTitulo_SeRechaza()
    {
        var db = await BaseConPoolAsync();
        var (ok, mensaje, _) = await Svc(db, Admin()).CrearAsync(Borrador("   "));

        Assert.False(ok);
        Assert.Contains("título", mensaje);
    }

    [Fact]
    public async Task Crear_ConEnlaceNoHttp_SeRechaza()
    {
        var db = await BaseConPoolAsync();
        var borrador = Borrador();
        borrador.ExternalUrl = "file:///C:/algo.bat";

        var (ok, mensaje, _) = await Svc(db, Admin()).CrearAsync(borrador);

        Assert.False(ok);
        Assert.Contains("http", mensaje);
    }

    [Fact]
    public async Task Editar_SoloMientrasSigueLibre()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin);
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);

        var (ok, mensaje) = await Svc(db, admin).EditarAsync(actividad.Id, Borrador("Otro título"));

        Assert.False(ok);
        Assert.Contains("tomó", mensaje);
    }

    [Fact]
    public async Task Editar_CambiarLaComplejidad_RecongelaLosPuntos()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        var actividad = await PublicarAsync(db, admin, Borrador(tipo: PoolWorkType.Tarea, complejidad: PoolComplexity.Baja));

        var cambios = Borrador("Ahora es más grande", PoolWorkType.Tarea, PoolComplexity.MuyAlta);
        Assert.True((await Svc(db, admin).EditarAsync(actividad.Id, cambios)).ok);

        var releida = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        var esperados = PoolSeed.Matriz.Single(m => m.Tipo == PoolWorkType.Tarea && m.Complejidad == PoolComplexity.MuyAlta).Puntos;
        Assert.Equal(esperados, releida.Points);
    }

    [Fact]
    public async Task Retirar_SoloLoQueSigueLibre()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var libre = await PublicarAsync(db, admin);
        var tomada = await PublicarAsync(db, admin, Borrador("Otra"));
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(tomada.Id, dev, EstimacionAlTomar)).ok);

        Assert.True((await Svc(db, admin).RetirarAsync(libre.Id)).ok);
        Assert.False((await Svc(db, admin).RetirarAsync(tomada.Id)).ok);
    }

    // ── Tomar ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tomar_CopiaElChecklistYFijaLaFechaLimite()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, Admin());

        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);

        var checklist = await Svc(db, Dev(dev)).ChecklistDeAsync(actividad.Id);
        int esperados = PoolSeed.Checklists.Count(c => c.Tipo == PoolWorkType.Bug);
        Assert.Equal(esperados, checklist.Count);
        Assert.Contains(checklist, c => c.RequiereEvidencia);

        var releida = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.Equal(PoolActivityStatus.Tomada, releida.Status);
        Assert.Equal(dev, releida.ClaimedByDeveloperId);
        Assert.NotNull(releida.ClaimDeadlineAt);
        // Y queda una actividad libre para poder cronometrar.
        Assert.NotNull(releida.LinkedDevActivityId);
        Assert.Contains($"Pool #{actividad.Id}", db.DevActivities.AsNoTracking().Single().Title);
    }

    [Fact]
    public async Task Tomar_ElChecklistQuedaCongelado_CambiarLaPlantillaDespuesNoLoToca()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin);
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);
        int antes = (await Svc(db, Dev(dev)).ChecklistDeAsync(actividad.Id)).Count;

        Assert.True((await Svc(db, admin).GuardarPlantillaItemAsync(new PoolChecklistTemplateItem
        {
            WorkType = PoolWorkType.Bug, Text = "Punto agregado a media obra"
        })).ok);

        Assert.Equal(antes, (await Svc(db, Dev(dev)).ChecklistDeAsync(actividad.Id)).Count);
    }

    [Fact]
    public async Task Tomar_RespetaElTopeSimultaneo()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");

        for (int i = 0; i < PoolActivityService.MaxTomadasPorOmision; i++)
        {
            var a = await PublicarAsync(db, admin, Borrador($"Actividad {i}"));
            Assert.True((await Svc(db, Dev(dev)).TomarAsync(a.Id, dev, EstimacionAlTomar)).ok);
        }

        var extra = await PublicarAsync(db, admin, Borrador("Una más"));
        var (ok, mensaje) = await Svc(db, Dev(dev)).TomarAsync(extra.Id, dev, EstimacionAlTomar);

        Assert.False(ok);
        Assert.Contains("tope", mensaje);
    }

    [Fact]
    public async Task Tomar_LaQueYaTomaronOtro_Falla()
    {
        var db = await BaseConPoolAsync();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        var actividad = await PublicarAsync(db, Admin());
        Assert.True((await Svc(db, Dev(ana)).TomarAsync(actividad.Id, ana, EstimacionAlTomar)).ok);

        var (ok, mensaje) = await Svc(db, Dev(beto, userId: 2)).TomarAsync(actividad.Id, beto, EstimacionAlTomar);

        Assert.False(ok);
        Assert.Contains("Actualiza la lista", mensaje);
    }

    [Fact]
    public async Task Tomar_ANombreDeOtro_LanzaAuthorization()
    {
        var db = await BaseConPoolAsync();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        var actividad = await PublicarAsync(db, Admin());

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, Dev(ana)).TomarAsync(actividad.Id, beto, EstimacionAlTomar));
    }

    // ── Checklist y entrega ──────────────────────────────────────────────────────

    [Fact]
    public async Task MarcarItem_QueExigeEvidencia_SinEnlace_Falla()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, Admin());
        var svc = Svc(db, Dev(dev));
        Assert.True((await svc.TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);

        var conEvidencia = (await svc.ChecklistDeAsync(actividad.Id)).First(c => c.RequiereEvidencia);
        var (ok, mensaje) = await svc.MarcarItemAsync(conEvidencia.Id, dev, true, null);

        Assert.False(ok);
        Assert.Contains("enlace", mensaje);
    }

    [Fact]
    public async Task MarcarItem_ConEnlaceNoHttp_Falla()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, Admin());
        var svc = Svc(db, Dev(dev));
        Assert.True((await svc.TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);

        var item = (await svc.ChecklistDeAsync(actividad.Id)).First(c => c.RequiereEvidencia);
        var (ok, _) = await svc.MarcarItemAsync(item.Id, dev, true, "file:///C:/evidencia.txt");

        Assert.False(ok);
        Assert.True((await svc.MarcarItemAsync(item.Id, dev, true, "https://dev.azure.com/pr/1")).ok);
    }

    [Fact]
    public async Task MarcarItem_DeUnaActividadAjena_Falla()
    {
        var db = await BaseConPoolAsync();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        var actividad = await PublicarAsync(db, Admin());
        Assert.True((await Svc(db, Dev(ana)).TomarAsync(actividad.Id, ana, EstimacionAlTomar)).ok);
        int itemId = (await Svc(db, Dev(ana)).ChecklistDeAsync(actividad.Id)).First().Id;

        var (ok, mensaje) = await Svc(db, Dev(beto, userId: 2)).MarcarItemAsync(itemId, beto, true, null);

        Assert.False(ok);
        Assert.Contains("no es tuya", mensaje);
    }

    [Fact]
    public async Task Entregar_ConChecklistIncompleto_Falla()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, Admin());
        var svc = Svc(db, Dev(dev));
        Assert.True((await svc.TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);

        var (ok, mensaje) = await svc.EntregarAsync(actividad.Id, dev);

        Assert.False(ok);
        Assert.Contains("faltan", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Entregar_CompletoPasaAEnRevision_YAvisaAlLider()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        db.Users.Add(new User { Id = 9, Username = "jefe", FullName = "Jefe", Role = UserRole.Admin, IsActive = true, PasswordHash = "x" });
        db.SaveChanges();

        var actividad = await PublicarAsync(db, Admin());
        var cu = Dev(dev);
        Assert.True((await Svc(db, cu).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);
        await CompletarChecklistAsync(db, cu, actividad.Id, dev);

        Assert.True((await Svc(db, cu).EntregarAsync(actividad.Id, dev)).ok);

        var releida = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.Equal(PoolActivityStatus.EnRevision, releida.Status);
        Assert.NotNull(releida.DeliveredAt);
        Assert.Contains(db.Notifications.AsNoTracking(), n => n.ForUserId == 9);
    }

    // ── Aceptar: los puntos ──────────────────────────────────────────────────────

    [Fact]
    public async Task Aceptar_GeneraPointEntryAprobadoConLosPuntosCongelados_YAparecenEnElRanking()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin);
        var cu = Dev(dev);
        Assert.True((await Svc(db, cu).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);
        await CompletarChecklistAsync(db, cu, actividad.Id, dev);
        Assert.True((await Svc(db, cu).EntregarAsync(actividad.Id, dev)).ok);

        var (ok, mensaje) = await Svc(db, admin).AceptarAsync(actividad.Id);
        Assert.True(ok, mensaje);

        var entrada = db.PointEntries.AsNoTracking().Include(p => p.Criterion).Single();
        Assert.Equal(actividad.Points, entrada.Points);
        Assert.Equal(PointApprovalStatus.Aprobado, entrada.ApprovalStatus);
        Assert.Equal(PoolSeed.NombreCriterio(PoolWorkType.Bug), entrada.Criterion.Name);
        Assert.Contains($"Pool #{actividad.Id}", entrada.Comment);
        Assert.Equal(DateTime.Now.Year, entrada.Year);
        Assert.Equal(DateTime.Now.Month, entrada.Month);
        // La evidencia del checklist viaja a la entrada, para que se pueda auditar después.
        Assert.False(string.IsNullOrWhiteSpace(entrada.EvidenceUrl));

        // Lo que de verdad importa: el ranking existente los suma sin cambiarle una línea.
        var ranking = await Puntos(db, admin).IndividualRankingAsync(DateTime.Now.Year, DateTime.Now.Month);
        Assert.Equal(actividad.Points, ranking.Single(r => r.DeveloperId == dev).Total);
    }

    [Fact]
    public async Task Aceptar_EsIdempotente_NoDuplicaLosPuntos()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin);
        var cu = Dev(dev);
        Assert.True((await Svc(db, cu).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);
        await CompletarChecklistAsync(db, cu, actividad.Id, dev);
        Assert.True((await Svc(db, cu).EntregarAsync(actividad.Id, dev)).ok);
        Assert.True((await Svc(db, admin).AceptarAsync(actividad.Id)).ok);

        var (ok, mensaje) = await Svc(db, admin).AceptarAsync(actividad.Id);

        Assert.False(ok);
        Assert.Contains("ya estaba aceptada", mensaje);
        Assert.Single(db.PointEntries.AsNoTracking());
    }

    [Fact]
    public async Task Aceptar_CierraLaActividadLibreDelCronometro_YNoLaDeOtraPersona()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");

        // Una actividad libre ajena creada ANTES, para que su Id NO coincida con el de la actividad
        // del pool. Sin ese desfase la prueba pasaba aunque el código cerrara la actividad
        // equivocada: en una base recién creada los dos primeros Id valen 1 y el error no se veía.
        var deBeto = new DevActivity { DeveloperId = beto, Title = "Soporte de Beto", Status = DevActivityStatus.Abierta };
        db.DevActivities.Add(deBeto);
        await db.SaveChangesAsync();

        var actividad = await PublicarAsync(db, admin);
        var cu = Dev(ana);
        Assert.True((await Svc(db, cu).TomarAsync(actividad.Id, ana, EstimacionAlTomar)).ok);
        await CompletarChecklistAsync(db, cu, actividad.Id, ana);
        Assert.True((await Svc(db, cu).EntregarAsync(actividad.Id, ana)).ok);
        Assert.True((await Svc(db, admin).AceptarAsync(actividad.Id)).ok);

        var releida = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        var delCronometro = db.DevActivities.AsNoTracking().Single(a => a.Id == releida.LinkedDevActivityId);
        Assert.Equal(DevActivityStatus.Cerrada, delCronometro.Status);

        // Y la de Beto sigue abierta: no es de esta actividad y nadie la cerró.
        Assert.Equal(DevActivityStatus.Abierta,
            db.DevActivities.AsNoTracking().Single(a => a.Id == deBeto.Id).Status);
    }

    [Fact]
    public async Task Aceptar_DosVecesEnParalelo_SoloAbonaUnaVez()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin);
        var cu = Dev(dev);
        Assert.True((await Svc(db, cu).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);
        await CompletarChecklistAsync(db, cu, actividad.Id, dev);
        Assert.True((await Svc(db, cu).EntregarAsync(actividad.Id, dev)).ok);

        // Dos líderes revisando la cola a la vez: cada uno con su propia instancia del servicio, los
        // dos leyeron «Por verificar» antes de que ninguno aceptara. Quien pierda la carrera no debe
        // abonar un segundo pago — de eso se encarga el UPDATE condicional, no una comprobación previa.
        var lider1 = Svc(db, Admin(userId: 9));
        var lider2 = Svc(db, Admin(userId: 10));

        var r1 = await lider1.AceptarAsync(actividad.Id);
        var r2 = await lider2.AceptarAsync(actividad.Id);

        Assert.True(r1.ok);
        Assert.False(r2.ok);
        Assert.Single(db.PointEntries.AsNoTracking());
        Assert.Equal(actividad.Points,
            (await Puntos(db, admin).IndividualRankingAsync(DateTime.Now.Year, DateTime.Now.Month))
                .Single(x => x.DeveloperId == dev).Total);
    }

    [Fact]
    public async Task Tomar_DosVecesEnParalelo_SoloUnoGanaYElChecklistNoSeDuplica()
    {
        var db = await BaseConPoolAsync();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        var actividad = await PublicarAsync(db, Admin());

        var r1 = await Svc(db, Dev(ana)).TomarAsync(actividad.Id, ana, EstimacionAlTomar);
        var r2 = await Svc(db, Dev(beto, userId: 2)).TomarAsync(actividad.Id, beto, EstimacionAlTomar);

        Assert.True(r1.ok);
        Assert.False(r2.ok);

        var releida = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.Equal(ana, releida.ClaimedByDeveloperId);
        // Un solo checklist: dos copias obligarían a marcar cada punto dos veces para entregar.
        int esperados = PoolSeed.Checklists.Count(c => c.Tipo == PoolWorkType.Bug);
        Assert.Equal(esperados, db.PoolActivityChecklistItems.AsNoTracking().Count(c => c.PoolActivityId == actividad.Id));
        Assert.Single(db.DevActivities.AsNoTracking());
    }

    [Fact]
    public async Task Devolver_DosVeces_NoInflaLaCuentaDeDevoluciones()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, Admin());
        var cu = Dev(dev);
        Assert.True((await Svc(db, cu).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);
        Assert.True((await Svc(db, cu).DevolverAsync(actividad.Id, dev, null)).ok);

        // La segunda no procede (ya no es suya) y no debe volver a contar.
        Assert.False((await Svc(db, cu).DevolverAsync(actividad.Id, dev, null)).ok);

        Assert.Equal(1, db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id).ReturnedCount);
    }

    [Fact]
    public async Task Devolver_CierraLaActividadLibreDelCronometro()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, Admin());
        var cu = Dev(dev);
        Assert.True((await Svc(db, cu).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);
        Assert.True((await Svc(db, cu).DevolverAsync(actividad.Id, dev, "no alcanzo")).ok);

        Assert.Equal(DevActivityStatus.Cerrada, db.DevActivities.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task Entregar_SinChecklist_SeRechaza()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, Admin());
        var cu = Dev(dev);
        Assert.True((await Svc(db, cu).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);

        // Un checklist vacío no prueba nada: entregar así sería cobrar los puntos sin verificación.
        db.PoolActivityChecklistItems.RemoveRange(
            db.PoolActivityChecklistItems.Where(c => c.PoolActivityId == actividad.Id));
        db.SaveChanges();

        var (ok, mensaje) = await Svc(db, cu).EntregarAsync(actividad.Id, dev);

        Assert.False(ok);
        Assert.Contains("checklist", mensaje);
        Assert.Empty(db.PointEntries.AsNoTracking());
    }

    [Fact]
    public async Task Aceptar_SinEntregar_Falla()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin);
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);

        var (ok, _) = await Svc(db, admin).AceptarAsync(actividad.Id);

        Assert.False(ok);
        Assert.Empty(db.PointEntries.AsNoTracking());
    }

    [Fact]
    public async Task Aceptar_RequiereAdmin()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, Admin());

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, Dev(dev)).AceptarAsync(actividad.Id));
    }

    // ── Rechazar y devolver ──────────────────────────────────────────────────────

    [Fact]
    public async Task Rechazar_ExigeMotivo_YConservaElHistorialEntreVueltas()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin);
        var cu = Dev(dev);
        Assert.True((await Svc(db, cu).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);
        await CompletarChecklistAsync(db, cu, actividad.Id, dev);
        Assert.True((await Svc(db, cu).EntregarAsync(actividad.Id, dev)).ok);

        Assert.False((await Svc(db, admin).RechazarAsync(actividad.Id, "   ")).ok);
        Assert.True((await Svc(db, admin).RechazarAsync(actividad.Id, "falta la prueba en QA")).ok);

        var tras1 = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.Equal(PoolActivityStatus.Devuelta, tras1.Status);
        Assert.Equal(1, tras1.ReviewRound);
        Assert.Equal(dev, tras1.ClaimedByDeveloperId);   // sigue siendo suya, no vuelve al pool

        // Segunda vuelta: el motivo anterior no se pierde.
        Assert.True((await Svc(db, cu).EntregarAsync(actividad.Id, dev)).ok);
        Assert.True((await Svc(db, admin).RechazarAsync(actividad.Id, "sigue faltando el despliegue")).ok);

        var tras2 = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.Equal(2, tras2.ReviewRound);
        Assert.Contains("falta la prueba en QA", tras2.ReviewHistory);
        Assert.Contains("sigue faltando el despliegue", tras2.ReviewHistory);
    }

    [Fact]
    public async Task Devuelta_SePuedeCorregirYVolverAEntregar()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin);
        var cu = Dev(dev);
        Assert.True((await Svc(db, cu).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);
        await CompletarChecklistAsync(db, cu, actividad.Id, dev);
        Assert.True((await Svc(db, cu).EntregarAsync(actividad.Id, dev)).ok);
        Assert.True((await Svc(db, admin).RechazarAsync(actividad.Id, "falta evidencia")).ok);

        // El checklist se conserva: lo hecho está hecho.
        Assert.All(await Svc(db, cu).ChecklistDeAsync(actividad.Id), c => Assert.True(c.IsDone));
        Assert.True((await Svc(db, cu).EntregarAsync(actividad.Id, dev)).ok);
        Assert.True((await Svc(db, admin).AceptarAsync(actividad.Id)).ok);
        Assert.Single(db.PointEntries.AsNoTracking());
    }

    [Fact]
    public async Task Devolver_RegresaAlPool_LimpiaElChecklistYCuenta()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, Admin());
        var cu = Dev(dev);
        Assert.True((await Svc(db, cu).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);

        var (ok, _) = await Svc(db, cu).DevolverAsync(actividad.Id, dev, "me asignaron otra cosa");
        Assert.True(ok);

        var releida = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.Equal(PoolActivityStatus.Disponible, releida.Status);
        Assert.Null(releida.ClaimedByDeveloperId);
        Assert.Null(releida.ClaimDeadlineAt);
        Assert.Equal(1, releida.ReturnedCount);
        Assert.Empty(await Svc(db, cu).ChecklistDeAsync(actividad.Id));
        Assert.Contains("me asignaron otra cosa", releida.ReviewHistory);
        // Y vuelve a estar disponible para cualquiera.
        Assert.Contains(await Svc(db, cu).DisponiblesAsync(), a => a.Id == actividad.Id);
    }

    [Fact]
    public async Task Devolver_UnaActividadAjena_Falla()
    {
        var db = await BaseConPoolAsync();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        var actividad = await PublicarAsync(db, Admin());
        Assert.True((await Svc(db, Dev(ana)).TomarAsync(actividad.Id, ana, EstimacionAlTomar)).ok);

        var (ok, mensaje) = await Svc(db, Dev(beto, userId: 2)).DevolverAsync(actividad.Id, beto, null);

        Assert.False(ok);
        Assert.Contains("no es tuya", mensaje);
    }

    [Fact]
    public async Task Liberar_RequiereAdmin_DevuelveAlPoolYAvisaAlDesarrollador()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        db.Users.Add(new User { Id = 5, Username = "ana", FullName = "Ana", Role = UserRole.Desarrollador, DeveloperId = dev, IsActive = true, PasswordHash = "x" });
        db.SaveChanges();

        var actividad = await PublicarAsync(db, admin);
        Assert.True((await Svc(db, Dev(dev, userId: 5)).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, Dev(dev, userId: 5)).LiberarAsync(actividad.Id, "porque sí"));

        Assert.True((await Svc(db, admin).LiberarAsync(actividad.Id, "se fue de vacaciones")).ok);
        var releida = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.Equal(PoolActivityStatus.Disponible, releida.Status);
        Assert.Contains(db.Notifications.AsNoTracking(), n => n.ForUserId == 5);
    }

    // ── Frontera con la autocalificación libre ───────────────────────────────────

    [Fact]
    public async Task Autocalificacion_ConUnCriterioDelPool_SeRechaza()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        var criterio = db.ScoringCriteria.AsNoTracking().Single(c => c.Name == PoolSeed.NombreCriterio(PoolWorkType.Bug));

        // Los criterios del pool valen 0 puntos justamente para que nadie pueda registrarse a mano
        // trabajo del pool que no hizo.
        var (ok, mensaje, _) = await Puntos(db, Dev(dev)).RegistrarAutocalificacionAsync(
            new PointEntry { DeveloperId = dev, CriterionId = criterio.Id, Year = DateTime.Now.Year, Month = DateTime.Now.Month });

        Assert.False(ok);
        Assert.Contains("no otorga puntos positivos", mensaje);
    }

    // ── Matriz y plantillas ──────────────────────────────────────────────────────

    [Fact]
    public async Task GuardarMatriz_RechazaPuntosNoPositivos()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        var matriz = await Svc(db, admin).ObtenerMatrizAsync();
        matriz[0].Points = 0;

        var (ok, mensaje) = await Svc(db, admin).GuardarMatrizAsync(matriz);

        Assert.False(ok);
        Assert.Contains("mayores que cero", mensaje);
    }

    [Fact]
    public async Task GuardarMatriz_RequiereAdmin()
    {
        var db = await BaseConPoolAsync();
        int dev = NuevoDesarrollador(db, "Ana");
        var matriz = await Svc(db, Admin()).ObtenerMatrizAsync();

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, Dev(dev)).GuardarMatrizAsync(matriz));
    }

    [Fact]
    public async Task DesactivarPlantillaItem_DejaDePedirseEnLasActividadesNuevas()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var item = (await Svc(db, admin).PlantillaAsync(PoolWorkType.Bug)).First();

        Assert.True((await Svc(db, admin).DesactivarPlantillaItemAsync(item.Id)).ok);

        var actividad = await PublicarAsync(db, admin, Borrador("Nueva tras desactivar"));
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);

        var checklist = await Svc(db, Dev(dev)).ChecklistDeAsync(actividad.Id);
        Assert.DoesNotContain(checklist, c => c.Text == item.Text);
        // Se desactiva, no se borra: el punto sigue en el catálogo para consultar el histórico.
        Assert.Contains(await Svc(db, admin).PlantillaAsync(PoolWorkType.Bug, incluirInactivos: true), t => t.Id == item.Id);
    }

    // ── Consultas ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Disponibles_SoloLoLibre_YFiltraPorTipo()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var bug = await PublicarAsync(db, admin, Borrador("Un bug", PoolWorkType.Bug, PoolComplexity.Media));
        await PublicarAsync(db, admin, Borrador("Una tarea", PoolWorkType.Tarea, PoolComplexity.Media));
        var tomada = await PublicarAsync(db, admin, Borrador("Otro bug", PoolWorkType.Bug, PoolComplexity.Baja));
        Assert.True((await Svc(db, Dev(dev)).TomarAsync(tomada.Id, dev, EstimacionAlTomar)).ok);

        var soloBugs = await Svc(db, Dev(dev)).DisponiblesAsync(PoolWorkType.Bug);

        Assert.Single(soloBugs);
        Assert.Equal(bug.Id, soloBugs[0].Id);
    }

    [Fact]
    public async Task MisDelPool_SoloLasPropias()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        var deAna = await PublicarAsync(db, admin, Borrador("De Ana"));
        var deBeto = await PublicarAsync(db, admin, Borrador("De Beto"));
        Assert.True((await Svc(db, Dev(ana)).TomarAsync(deAna.Id, ana, EstimacionAlTomar)).ok);
        Assert.True((await Svc(db, Dev(beto, userId: 2)).TomarAsync(deBeto.Id, beto, EstimacionAlTomar)).ok);

        var mias = await Svc(db, Dev(ana)).MisDelPoolAsync(ana);

        Assert.Single(mias);
        Assert.Equal(deAna.Id, mias[0].Id);
        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, Dev(ana)).MisDelPoolAsync(beto));
    }

    [Fact]
    public async Task CuentaPendientesDeVerificar_CuentaSoloLoEntregado()
    {
        var db = await BaseConPoolAsync();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = await PublicarAsync(db, admin);
        var cu = Dev(dev);
        Assert.True((await Svc(db, cu).TomarAsync(actividad.Id, dev, EstimacionAlTomar)).ok);
        Assert.Equal(0, await Svc(db, admin).CuentaPendientesDeVerificarAsync());

        await CompletarChecklistAsync(db, cu, actividad.Id, dev);
        Assert.True((await Svc(db, cu).EntregarAsync(actividad.Id, dev)).ok);
        Assert.Equal(1, await Svc(db, admin).CuentaPendientesDeVerificarAsync());

        Assert.True((await Svc(db, admin).AceptarAsync(actividad.Id)).ok);
        Assert.Equal(0, await Svc(db, admin).CuentaPendientesDeVerificarAsync());
    }
}

/// <summary>La siembra del pool: valores de partida y, sobre todo, que no duplique nada.</summary>
public class PoolSeedTests
{
    [Fact]
    public async Task Sembrar_EsIdempotente()
    {
        var db = TestDb.New();

        int primera = await PoolSeed.SembrarAsync(db);
        int segunda = await PoolSeed.SembrarAsync(db);

        Assert.True(primera > 0);
        Assert.Equal(0, segunda);
        Assert.Equal(PoolSeed.Matriz.Length, db.PoolPointsMatrix.AsNoTracking().Count());
        Assert.Equal(PoolSeed.Checklists.Length, db.PoolChecklistTemplateItems.AsNoTracking().Count());
        Assert.Equal(3, db.ScoringCriteria.AsNoTracking().Count(c => c.Name.StartsWith(PoolSeed.PrefijoCriterio)));
    }

    [Fact]
    public void LaMatrizCubreTodasLasCombinaciones()
    {
        foreach (var tipo in Enum.GetValues<PoolWorkType>())
            foreach (var complejidad in Enum.GetValues<PoolComplexity>())
                Assert.Contains(PoolSeed.Matriz, m => m.Tipo == tipo && m.Complejidad == complejidad);
    }

    [Fact]
    public void LosPuntosCrecenConLaComplejidad()
    {
        // Si «muy alta» valiera casi lo mismo que «baja», tomar solo lo fácil sería siempre la mejor
        // estrategia y el pool dejaría de repartir el trabajo difícil.
        foreach (var tipo in Enum.GetValues<PoolWorkType>())
        {
            var puntos = PoolSeed.Matriz.Where(m => m.Tipo == tipo)
                .OrderBy(m => m.Complejidad).Select(m => m.Puntos).ToList();
            for (int i = 1; i < puntos.Count; i++)
                Assert.True(puntos[i] > puntos[i - 1], $"{tipo}: los puntos no crecen con la complejidad.");
        }
    }

    [Fact]
    public void CadaTipoTieneUnPuntoDeChecklistQueExigeEvidencia()
    {
        foreach (var tipo in Enum.GetValues<PoolWorkType>())
            Assert.Contains(PoolSeed.Checklists, c => c.Tipo == tipo && c.Evidencia);
    }

    [Fact]
    public async Task LosCriteriosDelPoolNacenConCeroPuntos()
    {
        var db = TestDb.New();
        await PoolSeed.SembrarAsync(db);

        var criterios = db.ScoringCriteria.AsNoTracking()
            .Where(c => c.Name.StartsWith(PoolSeed.PrefijoCriterio)).ToList();

        Assert.NotEmpty(criterios);
        Assert.All(criterios, c => Assert.Equal(0, c.DefaultPoints));
    }
}
