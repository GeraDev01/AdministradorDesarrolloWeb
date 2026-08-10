using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// El pool de actividades valoradas.
///
/// Lo que estas pruebas cuidan es la promesa del sistema: que el valor de una actividad esté fijado
/// ANTES de trabajarla y no se pueda mover después, que lo que se le exige a alguien sea lo que se
/// le pidió al tomarla, y que aceptar una actividad abone sus puntos exactamente una vez.
/// </summary>
public class PoolActivityServiceTests
{
    private static PoolActivityService Svc(AppDbContext db, CurrentUserContext cu)
    {
        var audit = new AuditService(db, cu);
        return new PoolActivityService(db, cu, audit, new NotificationService(db), new SettingsService(db, audit));
    }

    /// <summary>Base ya sembrada con la matriz, los checklists y los criterios del pool.</summary>
    private static AppDbContext BaseConPool()
    {
        var db = TestDb.New();
        PoolSeed.Sembrar(db);
        return db;
    }

    private static int NuevoDesarrollador(AppDbContext db, string nombre)
    {
        var d = new Developer { FullName = nombre, IsActive = true };
        db.Developers.Add(d);
        db.SaveChanges();
        return d.Id;
    }

    private static CurrentUserContext Admin(int userId = 9) => Ctx.As(UserRole.Admin, userId: userId);
    private static CurrentUserContext Dev(int developerId, int userId = 1) =>
        Ctx.As(UserRole.Desarrollador, developerId, userId);

    private static PoolActivity Borrador(string titulo = "Corregir el cálculo de facturación",
        PoolWorkType tipo = PoolWorkType.Bug, PoolComplexity complejidad = PoolComplexity.Alta) =>
        new() { Title = titulo, WorkType = tipo, Complexity = complejidad };

    /// <summary>Crea una actividad y devuelve la entidad ya persistida.</summary>
    private static PoolActivity Publicar(AppDbContext db, CurrentUserContext admin, PoolActivity? borrador = null)
    {
        var (ok, mensaje, actividad) = Svc(db, admin).Crear(borrador ?? Borrador());
        Assert.True(ok, mensaje);
        return actividad!;
    }

    /// <summary>Marca todo el checklist de una actividad, poniendo evidencia donde hace falta.</summary>
    private static void CompletarChecklist(AppDbContext db, CurrentUserContext cu, int actividadId, int devId)
    {
        var svc = Svc(db, cu);
        foreach (var item in svc.ChecklistDe(actividadId))
        {
            var (ok, mensaje) = svc.MarcarItem(item.Id, devId, true,
                item.RequiereEvidencia ? "https://dev.azure.com/org/proj/_git/repo/pullrequest/42" : null);
            Assert.True(ok, mensaje);
        }
    }

    // ── Crear: el valor se congela ───────────────────────────────────────────────

    [Fact]
    public void Crear_TomaLosPuntosDeLaMatriz_NoDelBorrador()
    {
        var db = BaseConPool();
        // Aunque el borrador venga con un valor inflado, el que manda es el de la matriz.
        var borrador = Borrador();
        borrador.Points = 999;

        var actividad = Publicar(db, Admin(), borrador);

        var esperados = PoolSeed.Matriz.Single(m => m.Tipo == PoolWorkType.Bug && m.Complejidad == PoolComplexity.Alta).Puntos;
        Assert.Equal(esperados, actividad.Points);
    }

    [Fact]
    public void Crear_CongelaLosPuntos_CambiarLaMatrizDespuesNoLosMueve()
    {
        var db = BaseConPool();
        var admin = Admin();
        var actividad = Publicar(db, admin);
        int puntosOriginales = actividad.Points;

        var matriz = Svc(db, admin).ObtenerMatriz();
        foreach (var celda in matriz) celda.Points = 1;
        Assert.True(Svc(db, admin).GuardarMatriz(matriz).ok);

        var releida = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.Equal(puntosOriginales, releida.Points);
        Assert.NotEqual(1, releida.Points);
    }

    [Fact]
    public void Crear_RequiereAdmin()
    {
        var db = BaseConPool();
        int dev = NuevoDesarrollador(db, "Ana");

        Assert.Throws<AuthorizationException>(() => Svc(db, Dev(dev)).Crear(Borrador()));
    }

    [Fact]
    public void Crear_SinTitulo_SeRechaza()
    {
        var db = BaseConPool();
        var (ok, mensaje, _) = Svc(db, Admin()).Crear(Borrador("   "));

        Assert.False(ok);
        Assert.Contains("título", mensaje);
    }

    [Fact]
    public void Crear_ConEnlaceNoHttp_SeRechaza()
    {
        var db = BaseConPool();
        var borrador = Borrador();
        borrador.ExternalUrl = "file:///C:/algo.bat";

        var (ok, mensaje, _) = Svc(db, Admin()).Crear(borrador);

        Assert.False(ok);
        Assert.Contains("http", mensaje);
    }

    [Fact]
    public void Editar_SoloMientrasSigueLibre()
    {
        var db = BaseConPool();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, admin);
        Assert.True(Svc(db, Dev(dev)).Tomar(actividad.Id, dev).ok);

        var (ok, mensaje) = Svc(db, admin).Editar(actividad.Id, Borrador("Otro título"));

        Assert.False(ok);
        Assert.Contains("tomó", mensaje);
    }

    [Fact]
    public void Editar_CambiarLaComplejidad_RecongelaLosPuntos()
    {
        var db = BaseConPool();
        var admin = Admin();
        var actividad = Publicar(db, admin, Borrador(tipo: PoolWorkType.Tarea, complejidad: PoolComplexity.Baja));

        var cambios = Borrador("Ahora es más grande", PoolWorkType.Tarea, PoolComplexity.MuyAlta);
        Assert.True(Svc(db, admin).Editar(actividad.Id, cambios).ok);

        var releida = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        var esperados = PoolSeed.Matriz.Single(m => m.Tipo == PoolWorkType.Tarea && m.Complejidad == PoolComplexity.MuyAlta).Puntos;
        Assert.Equal(esperados, releida.Points);
    }

    [Fact]
    public void Retirar_SoloLoQueSigueLibre()
    {
        var db = BaseConPool();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var libre = Publicar(db, admin);
        var tomada = Publicar(db, admin, Borrador("Otra"));
        Assert.True(Svc(db, Dev(dev)).Tomar(tomada.Id, dev).ok);

        Assert.True(Svc(db, admin).Retirar(libre.Id).ok);
        Assert.False(Svc(db, admin).Retirar(tomada.Id).ok);
    }

    // ── Tomar ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Tomar_CopiaElChecklistYFijaLaFechaLimite()
    {
        var db = BaseConPool();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, Admin());

        Assert.True(Svc(db, Dev(dev)).Tomar(actividad.Id, dev).ok);

        var checklist = Svc(db, Dev(dev)).ChecklistDe(actividad.Id);
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
    public void Tomar_ElChecklistQuedaCongelado_CambiarLaPlantillaDespuesNoLoToca()
    {
        var db = BaseConPool();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, admin);
        Assert.True(Svc(db, Dev(dev)).Tomar(actividad.Id, dev).ok);
        int antes = Svc(db, Dev(dev)).ChecklistDe(actividad.Id).Count;

        Assert.True(Svc(db, admin).GuardarPlantillaItem(new PoolChecklistTemplateItem
        {
            WorkType = PoolWorkType.Bug, Text = "Punto agregado a media obra"
        }).ok);

        Assert.Equal(antes, Svc(db, Dev(dev)).ChecklistDe(actividad.Id).Count);
    }

    [Fact]
    public void Tomar_RespetaElTopeSimultaneo()
    {
        var db = BaseConPool();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");

        for (int i = 0; i < PoolActivityService.MaxTomadasPorOmision; i++)
        {
            var a = Publicar(db, admin, Borrador($"Actividad {i}"));
            Assert.True(Svc(db, Dev(dev)).Tomar(a.Id, dev).ok);
        }

        var extra = Publicar(db, admin, Borrador("Una más"));
        var (ok, mensaje) = Svc(db, Dev(dev)).Tomar(extra.Id, dev);

        Assert.False(ok);
        Assert.Contains("tope", mensaje);
    }

    [Fact]
    public void Tomar_LaQueYaTomaronOtro_Falla()
    {
        var db = BaseConPool();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        var actividad = Publicar(db, Admin());
        Assert.True(Svc(db, Dev(ana)).Tomar(actividad.Id, ana).ok);

        var (ok, mensaje) = Svc(db, Dev(beto, userId: 2)).Tomar(actividad.Id, beto);

        Assert.False(ok);
        Assert.Contains("Actualiza la lista", mensaje);
    }

    [Fact]
    public void Tomar_ANombreDeOtro_LanzaAuthorization()
    {
        var db = BaseConPool();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        var actividad = Publicar(db, Admin());

        Assert.Throws<AuthorizationException>(() => Svc(db, Dev(ana)).Tomar(actividad.Id, beto));
    }

    // ── Checklist y entrega ──────────────────────────────────────────────────────

    [Fact]
    public void MarcarItem_QueExigeEvidencia_SinEnlace_Falla()
    {
        var db = BaseConPool();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, Admin());
        var svc = Svc(db, Dev(dev));
        Assert.True(svc.Tomar(actividad.Id, dev).ok);

        var conEvidencia = svc.ChecklistDe(actividad.Id).First(c => c.RequiereEvidencia);
        var (ok, mensaje) = svc.MarcarItem(conEvidencia.Id, dev, true, null);

        Assert.False(ok);
        Assert.Contains("enlace", mensaje);
    }

    [Fact]
    public void MarcarItem_ConEnlaceNoHttp_Falla()
    {
        var db = BaseConPool();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, Admin());
        var svc = Svc(db, Dev(dev));
        Assert.True(svc.Tomar(actividad.Id, dev).ok);

        var item = svc.ChecklistDe(actividad.Id).First(c => c.RequiereEvidencia);
        var (ok, _) = svc.MarcarItem(item.Id, dev, true, "file:///C:/evidencia.txt");

        Assert.False(ok);
        Assert.True(svc.MarcarItem(item.Id, dev, true, "https://dev.azure.com/pr/1").ok);
    }

    [Fact]
    public void MarcarItem_DeUnaActividadAjena_Falla()
    {
        var db = BaseConPool();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        var actividad = Publicar(db, Admin());
        Assert.True(Svc(db, Dev(ana)).Tomar(actividad.Id, ana).ok);
        int itemId = Svc(db, Dev(ana)).ChecklistDe(actividad.Id).First().Id;

        var (ok, mensaje) = Svc(db, Dev(beto, userId: 2)).MarcarItem(itemId, beto, true, null);

        Assert.False(ok);
        Assert.Contains("no es tuya", mensaje);
    }

    [Fact]
    public void Entregar_ConChecklistIncompleto_Falla()
    {
        var db = BaseConPool();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, Admin());
        var svc = Svc(db, Dev(dev));
        Assert.True(svc.Tomar(actividad.Id, dev).ok);

        var (ok, mensaje) = svc.Entregar(actividad.Id, dev);

        Assert.False(ok);
        Assert.Contains("faltan", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Entregar_CompletoPasaAEnRevision_YAvisaAlLider()
    {
        var db = BaseConPool();
        int dev = NuevoDesarrollador(db, "Ana");
        db.Users.Add(new User { Id = 9, Username = "jefe", FullName = "Jefe", Role = UserRole.Admin, IsActive = true, PasswordHash = "x" });
        db.SaveChanges();

        var actividad = Publicar(db, Admin());
        var cu = Dev(dev);
        Assert.True(Svc(db, cu).Tomar(actividad.Id, dev).ok);
        CompletarChecklist(db, cu, actividad.Id, dev);

        Assert.True(Svc(db, cu).Entregar(actividad.Id, dev).ok);

        var releida = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.Equal(PoolActivityStatus.EnRevision, releida.Status);
        Assert.NotNull(releida.DeliveredAt);
        Assert.Contains(db.Notifications.AsNoTracking(), n => n.ForUserId == 9);
    }

    // ── Aceptar: los puntos ──────────────────────────────────────────────────────

    [Fact]
    public void Aceptar_GeneraPointEntryAprobadoConLosPuntosCongelados_YAparecenEnElRanking()
    {
        var db = BaseConPool();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, admin);
        var cu = Dev(dev);
        Assert.True(Svc(db, cu).Tomar(actividad.Id, dev).ok);
        CompletarChecklist(db, cu, actividad.Id, dev);
        Assert.True(Svc(db, cu).Entregar(actividad.Id, dev).ok);

        var (ok, mensaje) = Svc(db, admin).Aceptar(actividad.Id);
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
        var ranking = new PerformanceScoringService(db).IndividualRanking(DateTime.Now.Year, DateTime.Now.Month);
        Assert.Equal(actividad.Points, ranking.Single(r => r.DeveloperId == dev).Total);
    }

    [Fact]
    public void Aceptar_EsIdempotente_NoDuplicaLosPuntos()
    {
        var db = BaseConPool();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, admin);
        var cu = Dev(dev);
        Assert.True(Svc(db, cu).Tomar(actividad.Id, dev).ok);
        CompletarChecklist(db, cu, actividad.Id, dev);
        Assert.True(Svc(db, cu).Entregar(actividad.Id, dev).ok);
        Assert.True(Svc(db, admin).Aceptar(actividad.Id).ok);

        var (ok, mensaje) = Svc(db, admin).Aceptar(actividad.Id);

        Assert.False(ok);
        Assert.Contains("ya estaba aceptada", mensaje);
        Assert.Single(db.PointEntries.AsNoTracking());
    }

    [Fact]
    public void Aceptar_CierraLaActividadLibreDelCronometro_YNoLaDeOtraPersona()
    {
        var db = BaseConPool();
        var admin = Admin();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");

        // Una actividad libre ajena creada ANTES, para que su Id NO coincida con el de la actividad
        // del pool. Sin este desfase la prueba pasaba aunque el código cerrara la actividad
        // equivocada: en una base recién creada los dos primeros Id valen 1 y el error no se veía.
        var deBeto = new DevActivity { DeveloperId = beto, Title = "Soporte de Beto", Status = DevActivityStatus.Abierta };
        db.DevActivities.Add(deBeto);
        db.SaveChanges();

        var actividad = Publicar(db, admin);
        var cu = Dev(ana);
        Assert.True(Svc(db, cu).Tomar(actividad.Id, ana).ok);
        CompletarChecklist(db, cu, actividad.Id, ana);
        Assert.True(Svc(db, cu).Entregar(actividad.Id, ana).ok);
        Assert.True(Svc(db, admin).Aceptar(actividad.Id).ok);

        var releida = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        var delCronometro = db.DevActivities.AsNoTracking().Single(a => a.Id == releida.LinkedDevActivityId);
        Assert.Equal(DevActivityStatus.Cerrada, delCronometro.Status);

        // Y la de Beto sigue abierta: no es de esta actividad y nadie la cerró.
        Assert.Equal(DevActivityStatus.Abierta,
            db.DevActivities.AsNoTracking().Single(a => a.Id == deBeto.Id).Status);
    }

    [Fact]
    public void Aceptar_DosVecesEnParalelo_SoloAbonaUnaVez()
    {
        var db = BaseConPool();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, admin);
        var cu = Dev(dev);
        Assert.True(Svc(db, cu).Tomar(actividad.Id, dev).ok);
        CompletarChecklist(db, cu, actividad.Id, dev);
        Assert.True(Svc(db, cu).Entregar(actividad.Id, dev).ok);

        // Dos líderes revisando la cola a la vez: cada uno con su propia instancia del servicio, los
        // dos leyeron «Por verificar» antes de que ninguno aceptara. Quien pierda la carrera no debe
        // abonar un segundo pago — de eso se encarga el UPDATE condicional, no una comprobación previa.
        var lider1 = Svc(db, Admin(userId: 9));
        var lider2 = Svc(db, Admin(userId: 10));

        var r1 = lider1.Aceptar(actividad.Id);
        var r2 = lider2.Aceptar(actividad.Id);

        Assert.True(r1.ok);
        Assert.False(r2.ok);
        Assert.Single(db.PointEntries.AsNoTracking());
        Assert.Equal(actividad.Points,
            new PerformanceScoringService(db).IndividualRanking(DateTime.Now.Year, DateTime.Now.Month)
                .Single(x => x.DeveloperId == dev).Total);
    }

    [Fact]
    public void Tomar_DosVecesEnParalelo_SoloUnoGanaYElChecklistNoSeDuplica()
    {
        var db = BaseConPool();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        var actividad = Publicar(db, Admin());

        var r1 = Svc(db, Dev(ana)).Tomar(actividad.Id, ana);
        var r2 = Svc(db, Dev(beto, userId: 2)).Tomar(actividad.Id, beto);

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
    public void Devolver_DosVeces_NoInflaLaCuentaDeDevoluciones()
    {
        var db = BaseConPool();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, Admin());
        var cu = Dev(dev);
        Assert.True(Svc(db, cu).Tomar(actividad.Id, dev).ok);
        Assert.True(Svc(db, cu).Devolver(actividad.Id, dev, null).ok);

        // La segunda no procede (ya no es suya) y no debe volver a contar.
        Assert.False(Svc(db, cu).Devolver(actividad.Id, dev, null).ok);

        Assert.Equal(1, db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id).ReturnedCount);
    }

    [Fact]
    public void Devolver_CierraLaActividadLibreDelCronometro()
    {
        var db = BaseConPool();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, Admin());
        var cu = Dev(dev);
        Assert.True(Svc(db, cu).Tomar(actividad.Id, dev).ok);
        Assert.True(Svc(db, cu).Devolver(actividad.Id, dev, "no alcanzo").ok);

        Assert.Equal(DevActivityStatus.Cerrada, db.DevActivities.AsNoTracking().Single().Status);
    }

    [Fact]
    public void Entregar_SinChecklist_SeRechaza()
    {
        var db = BaseConPool();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, Admin());
        var cu = Dev(dev);
        Assert.True(Svc(db, cu).Tomar(actividad.Id, dev).ok);

        // Un checklist vacío no prueba nada: entregar así sería cobrar los puntos sin verificación.
        db.PoolActivityChecklistItems.RemoveRange(
            db.PoolActivityChecklistItems.Where(c => c.PoolActivityId == actividad.Id));
        db.SaveChanges();

        var (ok, mensaje) = Svc(db, cu).Entregar(actividad.Id, dev);

        Assert.False(ok);
        Assert.Contains("checklist", mensaje);
        Assert.Empty(db.PointEntries.AsNoTracking());
    }

    [Fact]
    public void Aceptar_SinEntregar_Falla()
    {
        var db = BaseConPool();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, admin);
        Assert.True(Svc(db, Dev(dev)).Tomar(actividad.Id, dev).ok);

        var (ok, _) = Svc(db, admin).Aceptar(actividad.Id);

        Assert.False(ok);
        Assert.Empty(db.PointEntries.AsNoTracking());
    }

    [Fact]
    public void Aceptar_RequiereAdmin()
    {
        var db = BaseConPool();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, Admin());

        Assert.Throws<AuthorizationException>(() => Svc(db, Dev(dev)).Aceptar(actividad.Id));
    }

    // ── Rechazar y devolver ──────────────────────────────────────────────────────

    [Fact]
    public void Rechazar_ExigeMotivo_YConservaElHistorialEntreVueltas()
    {
        var db = BaseConPool();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, admin);
        var cu = Dev(dev);
        Assert.True(Svc(db, cu).Tomar(actividad.Id, dev).ok);
        CompletarChecklist(db, cu, actividad.Id, dev);
        Assert.True(Svc(db, cu).Entregar(actividad.Id, dev).ok);

        Assert.False(Svc(db, admin).Rechazar(actividad.Id, "   ").ok);
        Assert.True(Svc(db, admin).Rechazar(actividad.Id, "falta la prueba en QA").ok);

        var tras1 = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.Equal(PoolActivityStatus.Devuelta, tras1.Status);
        Assert.Equal(1, tras1.ReviewRound);
        Assert.Equal(dev, tras1.ClaimedByDeveloperId);   // sigue siendo suya, no vuelve al pool

        // Segunda vuelta: el motivo anterior no se pierde.
        Assert.True(Svc(db, cu).Entregar(actividad.Id, dev).ok);
        Assert.True(Svc(db, admin).Rechazar(actividad.Id, "sigue faltando el despliegue").ok);

        var tras2 = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.Equal(2, tras2.ReviewRound);
        Assert.Contains("falta la prueba en QA", tras2.ReviewHistory);
        Assert.Contains("sigue faltando el despliegue", tras2.ReviewHistory);
    }

    [Fact]
    public void Devuelta_SePuedeCorregirYVolverAEntregar()
    {
        var db = BaseConPool();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, admin);
        var cu = Dev(dev);
        Assert.True(Svc(db, cu).Tomar(actividad.Id, dev).ok);
        CompletarChecklist(db, cu, actividad.Id, dev);
        Assert.True(Svc(db, cu).Entregar(actividad.Id, dev).ok);
        Assert.True(Svc(db, admin).Rechazar(actividad.Id, "falta evidencia").ok);

        // El checklist se conserva: lo hecho está hecho.
        Assert.All(Svc(db, cu).ChecklistDe(actividad.Id), c => Assert.True(c.IsDone));
        Assert.True(Svc(db, cu).Entregar(actividad.Id, dev).ok);
        Assert.True(Svc(db, admin).Aceptar(actividad.Id).ok);
        Assert.Single(db.PointEntries.AsNoTracking());
    }

    [Fact]
    public void Devolver_RegresaAlPool_LimpiaElChecklistYCuenta()
    {
        var db = BaseConPool();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, Admin());
        var cu = Dev(dev);
        Assert.True(Svc(db, cu).Tomar(actividad.Id, dev).ok);

        var (ok, _) = Svc(db, cu).Devolver(actividad.Id, dev, "me asignaron otra cosa");
        Assert.True(ok);

        var releida = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.Equal(PoolActivityStatus.Disponible, releida.Status);
        Assert.Null(releida.ClaimedByDeveloperId);
        Assert.Null(releida.ClaimDeadlineAt);
        Assert.Equal(1, releida.ReturnedCount);
        Assert.Empty(Svc(db, cu).ChecklistDe(actividad.Id));
        Assert.Contains("me asignaron otra cosa", releida.ReviewHistory);
        // Y vuelve a estar disponible para cualquiera.
        Assert.Contains(Svc(db, cu).Disponibles(), a => a.Id == actividad.Id);
    }

    [Fact]
    public void Devolver_UnaActividadAjena_Falla()
    {
        var db = BaseConPool();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        var actividad = Publicar(db, Admin());
        Assert.True(Svc(db, Dev(ana)).Tomar(actividad.Id, ana).ok);

        var (ok, mensaje) = Svc(db, Dev(beto, userId: 2)).Devolver(actividad.Id, beto, null);

        Assert.False(ok);
        Assert.Contains("no es tuya", mensaje);
    }

    [Fact]
    public void Liberar_RequiereAdmin_DevuelveAlPoolYAvisaAlDesarrollador()
    {
        var db = BaseConPool();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        db.Users.Add(new User { Id = 5, Username = "ana", FullName = "Ana", Role = UserRole.Desarrollador, DeveloperId = dev, IsActive = true, PasswordHash = "x" });
        db.SaveChanges();

        var actividad = Publicar(db, admin);
        Assert.True(Svc(db, Dev(dev, userId: 5)).Tomar(actividad.Id, dev).ok);

        Assert.Throws<AuthorizationException>(() => Svc(db, Dev(dev, userId: 5)).Liberar(actividad.Id, "porque sí"));

        Assert.True(Svc(db, admin).Liberar(actividad.Id, "se fue de vacaciones").ok);
        var releida = db.PoolActivities.AsNoTracking().Single(a => a.Id == actividad.Id);
        Assert.Equal(PoolActivityStatus.Disponible, releida.Status);
        Assert.Contains(db.Notifications.AsNoTracking(), n => n.ForUserId == 5);
    }

    // ── Frontera con la autocalificación libre ───────────────────────────────────

    [Fact]
    public void Autocalificacion_ConUnCriterioDelPool_SeRechaza()
    {
        var db = BaseConPool();
        int dev = NuevoDesarrollador(db, "Ana");
        var criterio = db.ScoringCriteria.AsNoTracking().Single(c => c.Name == PoolSeed.NombreCriterio(PoolWorkType.Bug));

        // Los criterios del pool valen 0 puntos justamente para que nadie pueda registrarse a mano
        // trabajo del pool que no hizo.
        var (ok, mensaje, _) = new PerformanceScoringService(db).RegistrarAutocalificacion(
            new PointEntry { DeveloperId = dev, CriterionId = criterio.Id, Year = DateTime.Now.Year, Month = DateTime.Now.Month },
            Dev(dev));

        Assert.False(ok);
        Assert.Contains("no otorga puntos positivos", mensaje);
    }

    // ── Matriz y plantillas ──────────────────────────────────────────────────────

    [Fact]
    public void GuardarMatriz_RechazaPuntosNoPositivos()
    {
        var db = BaseConPool();
        var admin = Admin();
        var matriz = Svc(db, admin).ObtenerMatriz();
        matriz[0].Points = 0;

        var (ok, mensaje) = Svc(db, admin).GuardarMatriz(matriz);

        Assert.False(ok);
        Assert.Contains("mayores que cero", mensaje);
    }

    [Fact]
    public void GuardarMatriz_RequiereAdmin()
    {
        var db = BaseConPool();
        int dev = NuevoDesarrollador(db, "Ana");
        var matriz = Svc(db, Admin()).ObtenerMatriz();

        Assert.Throws<AuthorizationException>(() => Svc(db, Dev(dev)).GuardarMatriz(matriz));
    }

    [Fact]
    public void DesactivarPlantillaItem_DejaDePedirseEnLasActividadesNuevas()
    {
        var db = BaseConPool();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var item = Svc(db, admin).Plantilla(PoolWorkType.Bug).First();

        Assert.True(Svc(db, admin).DesactivarPlantillaItem(item.Id).ok);

        var actividad = Publicar(db, admin, Borrador("Nueva tras desactivar"));
        Assert.True(Svc(db, Dev(dev)).Tomar(actividad.Id, dev).ok);

        var checklist = Svc(db, Dev(dev)).ChecklistDe(actividad.Id);
        Assert.DoesNotContain(checklist, c => c.Text == item.Text);
        // Se desactiva, no se borra: el punto sigue en el catálogo para consultar el histórico.
        Assert.Contains(Svc(db, admin).Plantilla(PoolWorkType.Bug, incluirInactivos: true), t => t.Id == item.Id);
    }

    // ── Consultas ────────────────────────────────────────────────────────────────

    [Fact]
    public void Disponibles_SoloLoLibre_YFiltraPorTipo()
    {
        var db = BaseConPool();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var bug = Publicar(db, admin, Borrador("Un bug", PoolWorkType.Bug, PoolComplexity.Media));
        Publicar(db, admin, Borrador("Una tarea", PoolWorkType.Tarea, PoolComplexity.Media));
        var tomada = Publicar(db, admin, Borrador("Otro bug", PoolWorkType.Bug, PoolComplexity.Baja));
        Assert.True(Svc(db, Dev(dev)).Tomar(tomada.Id, dev).ok);

        var soloBugs = Svc(db, Dev(dev)).Disponibles(PoolWorkType.Bug);

        Assert.Single(soloBugs);
        Assert.Equal(bug.Id, soloBugs[0].Id);
    }

    [Fact]
    public void MisDelPool_SoloLasPropias()
    {
        var db = BaseConPool();
        var admin = Admin();
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");
        var deAna = Publicar(db, admin, Borrador("De Ana"));
        var deBeto = Publicar(db, admin, Borrador("De Beto"));
        Assert.True(Svc(db, Dev(ana)).Tomar(deAna.Id, ana).ok);
        Assert.True(Svc(db, Dev(beto, userId: 2)).Tomar(deBeto.Id, beto).ok);

        var mias = Svc(db, Dev(ana)).MisDelPool(ana);

        Assert.Single(mias);
        Assert.Equal(deAna.Id, mias[0].Id);
        Assert.Throws<AuthorizationException>(() => Svc(db, Dev(ana)).MisDelPool(beto));
    }

    [Fact]
    public void CuentaPendientesDeVerificar_CuentaSoloLoEntregado()
    {
        var db = BaseConPool();
        var admin = Admin();
        int dev = NuevoDesarrollador(db, "Ana");
        var actividad = Publicar(db, admin);
        var cu = Dev(dev);
        Assert.True(Svc(db, cu).Tomar(actividad.Id, dev).ok);
        Assert.Equal(0, Svc(db, admin).CuentaPendientesDeVerificar());

        CompletarChecklist(db, cu, actividad.Id, dev);
        Assert.True(Svc(db, cu).Entregar(actividad.Id, dev).ok);
        Assert.Equal(1, Svc(db, admin).CuentaPendientesDeVerificar());

        Assert.True(Svc(db, admin).Aceptar(actividad.Id).ok);
        Assert.Equal(0, Svc(db, admin).CuentaPendientesDeVerificar());
    }
}

/// <summary>La siembra del pool: valores de partida y, sobre todo, que no duplique nada.</summary>
public class PoolSeedTests
{
    [Fact]
    public void Sembrar_EsIdempotente()
    {
        var db = TestDb.New();

        int primera = PoolSeed.Sembrar(db);
        int segunda = PoolSeed.Sembrar(db);

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
    public void LosCriteriosDelPoolNacenConCeroPuntos()
    {
        var db = TestDb.New();
        PoolSeed.Sembrar(db);

        var criterios = db.ScoringCriteria.AsNoTracking()
            .Where(c => c.Name.StartsWith(PoolSeed.PrefijoCriterio)).ToList();

        Assert.NotEmpty(criterios);
        Assert.All(criterios, c => Assert.Equal(0, c.DefaultPoints));
    }
}
