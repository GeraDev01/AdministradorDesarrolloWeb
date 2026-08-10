using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// «Mis actividades»: lo que la pantalla recibe de una sola vez.
///
/// No se prueba aquí nada de negocio —registrar, corregir y replicar siguen siendo de
/// <see cref="PerformanceScoringService"/>, y la evidencia de <see cref="DevActivityService"/>—, sino
/// lo que esta consulta decide por su cuenta: qué entra en la ventana de historial, qué catálogos se
/// conservan para que corregir una entrada no le cambie el criterio por sorpresa, y que ninguna
/// captura ajena salga por el camino.
/// </summary>
public class AutocalificacionQueryServiceTests
{
    private static AutocalificacionQueryService Svc(AppDbContext db, ICurrentUser quien)
    {
        var bitacora = new AuditService(db, quien, new OrigenDePrueba());
        var cronometros = new WorkSessionService(db, quien, bitacora);
        return new AutocalificacionQueryService(db, quien, new DevActivityService(db, quien, bitacora, cronometros));
    }

    private static (AppDbContext db, Developer dev, ScoringCriterion crit, UsuarioDePrueba yo) Entorno()
    {
        var db = TestDb.New();
        var dev = new Developer { FullName = "Ana", IsActive = true, Seniority = "Senior" };
        db.Developers.Add(dev);
        var crit = new ScoringCriterion
        {
            Name = "Iniciativa", Description = "Propusiste e implementaste una mejora.",
            DefaultPoints = 5, IsActive = true, Scope = CriterionScope.Individual
        };
        db.ScoringCriteria.Add(crit);
        db.SaveChanges();

        return (db, dev, crit, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id, userId: 10));
    }

    private static PointEntry Entrada(Developer dev, ScoringCriterion crit, int anio, int mes,
        PointApprovalStatus estado, bool propia = true) => new()
    {
        DeveloperId = dev.Id, CriterionId = crit.Id, Points = crit.DefaultPoints,
        Year = anio, Month = mes, Date = new DateTime(anio, mes, 1, 12, 0, 0, DateTimeKind.Utc),
        ApprovalStatus = estado, SubmittedByDeveloperId = propia ? dev.Id : null
    };

    /// <summary>Un período de hace <paramref name="meses"/> meses, en (año, mes).</summary>
    private static (int anio, int mes) Hace(int meses)
    {
        var d = DateTime.UtcNow.AddMonths(-meses);
        return (d.Year, d.Month);
    }

    // ── La ventana de historial ──────────────────────────────────────────────────

    [Fact]
    public async Task LaVentana_DejaFueraLoAprobadoYViejo()
    {
        // Es lo único que cambia respecto al escritorio: allí la consulta era local y traía todo. Se
        // recorta porque cada fila viaja por red con su comentario y su historial.
        var (db, dev, crit, yo) = Entorno();
        var (anio, mes) = Hace(AutocalificacionQueryService.MesesDeHistorial + 2);
        db.PointEntries.Add(Entrada(dev, crit, anio, mes, PointApprovalStatus.Aprobado));
        db.SaveChanges();

        var mias = await Svc(db, yo).MisActividadesAsync();

        Assert.Empty(mias.Autocalificaciones);
    }

    [Fact]
    public async Task LaVentana_ConservaLoQueSigueSinAprobarPorViejoQueSea()
    {
        // Son las únicas sobre las que todavía se puede actuar: dejar fuera un rechazo antiguo le
        // quitaría al desarrollador la única vía de replicarlo dentro de la aplicación.
        var (db, dev, crit, yo) = Entorno();
        var (anio, mes) = Hace(AutocalificacionQueryService.MesesDeHistorial + 6);
        db.PointEntries.Add(Entrada(dev, crit, anio, mes, PointApprovalStatus.Rechazado));
        db.PointEntries.Add(Entrada(dev, crit, anio, mes, PointApprovalStatus.Pendiente));
        db.SaveChanges();

        var mias = await Svc(db, yo).MisActividadesAsync();

        Assert.Equal(2, mias.Autocalificaciones.Count);
        Assert.All(mias.Autocalificaciones, e => Assert.NotEqual(PointApprovalStatus.Aprobado, e.Estado));
    }

    [Fact]
    public async Task LoDeOtroDesarrollador_NoSeMezcla()
    {
        var (db, dev, crit, yo) = Entorno();
        var otro = new Developer { FullName = "Luis", IsActive = true };
        db.Developers.Add(otro);
        db.SaveChanges();

        var (anio, mes) = Hace(0);
        db.PointEntries.Add(Entrada(dev, crit, anio, mes, PointApprovalStatus.Pendiente));
        db.PointEntries.Add(Entrada(otro, crit, anio, mes, PointApprovalStatus.Pendiente));
        db.SaveChanges();

        var mias = await Svc(db, yo).MisActividadesAsync();

        Assert.Single(mias.Autocalificaciones);
    }

    /// <summary>
    /// Los puntos que reparte el líder se enseñan junto a los propios —el desarrollador quiere ver de
    /// una vez todo lo que suma o resta— pero marcados, porque no se corrigen ni se replican.
    /// </summary>
    [Fact]
    public async Task LosPuntosQueAsignoElLider_SeVenPeroNoSePuedenTocar()
    {
        var (db, dev, crit, yo) = Entorno();
        var (anio, mes) = Hace(0);
        db.PointEntries.Add(Entrada(dev, crit, anio, mes, PointApprovalStatus.Pendiente, propia: false));
        db.SaveChanges();

        var e = Assert.Single((await Svc(db, yo).MisActividadesAsync()).Autocalificaciones);

        Assert.False(e.EsAutocalificacion);
        Assert.False(e.SePuedeCorregir);
        Assert.False(e.SePuedeReplicar);
    }

    [Fact]
    public async Task UnaAprobada_NoOfreceCorregirse()
    {
        // La regla la aplica PerformanceScoringService; el contrato la refleja para que la pantalla
        // no enseñe un botón que va a fallar.
        var (db, dev, crit, yo) = Entorno();
        var (anio, mes) = Hace(0);
        db.PointEntries.Add(Entrada(dev, crit, anio, mes, PointApprovalStatus.Aprobado));
        db.SaveChanges();

        var e = Assert.Single((await Svc(db, yo).MisActividadesAsync()).Autocalificaciones);

        Assert.False(e.SePuedeCorregir);
        Assert.False(e.SePuedeReplicar);
    }

    [Fact]
    public async Task UnaRechazadaPropia_SeCorrigeYSeReplica()
    {
        var (db, dev, crit, yo) = Entorno();
        var (anio, mes) = Hace(0);
        db.PointEntries.Add(Entrada(dev, crit, anio, mes, PointApprovalStatus.Rechazado));
        db.SaveChanges();

        var e = Assert.Single((await Svc(db, yo).MisActividadesAsync()).Autocalificaciones);

        Assert.True(e.SePuedeCorregir);
        Assert.True(e.SePuedeReplicar);
    }

    // ── Catálogos de los formularios ─────────────────────────────────────────────

    [Fact]
    public async Task SoloSeOfrecenCriteriosIndividualesYPositivos()
    {
        // Los descuentos los aplica el líder, y un criterio de equipo no se autocalifica.
        var (db, dev, _, yo) = Entorno();
        db.ScoringCriteria.AddRange(
            new ScoringCriterion { Name = "De equipo", DefaultPoints = 3, IsActive = true, Scope = CriterionScope.Equipo },
            new ScoringCriterion { Name = "Penalización", DefaultPoints = -5, IsActive = true, Scope = CriterionScope.Individual },
            new ScoringCriterion { Name = "Retirado", DefaultPoints = 4, IsActive = false, Scope = CriterionScope.Individual });
        db.SaveChanges();

        var criterios = (await Svc(db, yo).MisActividadesAsync()).Criterios;

        Assert.Equal(["Iniciativa"], criterios.Select(c => c.Nombre));
        Assert.True(criterios.Single().Disponible);
    }

    [Fact]
    public async Task ElCriterioRetiradoQueYaUsaUnaEntrada_SeConservaMarcado()
    {
        // Decisión del escritorio: si desapareciera de la lista, corregir esa entrada la guardaría
        // con OTRO criterio sin avisar.
        var (db, dev, _, yo) = Entorno();
        var retirado = new ScoringCriterion
        {
            Name = "Retirado", DefaultPoints = 4, IsActive = false, Scope = CriterionScope.Individual
        };
        db.ScoringCriteria.Add(retirado);
        db.SaveChanges();

        var (anio, mes) = Hace(0);
        db.PointEntries.Add(Entrada(dev, retirado, anio, mes, PointApprovalStatus.Pendiente));
        db.SaveChanges();

        var criterios = (await Svc(db, yo).MisActividadesAsync()).Criterios;

        var suyo = Assert.Single(criterios, c => c.Nombre == "Retirado");
        Assert.False(suyo.Disponible);
        Assert.Contains("ya no disponible", suyo.Texto);
    }

    [Fact]
    public async Task ElRequerimientoEntregadoQueYaUsaUnaEntrada_SeConserva()
    {
        // Mismo criterio: en su día era el correcto, y corregir la entrada no debe cambiárselo.
        var (db, dev, crit, yo) = Entorno();
        var entregado = new Requirement { Title = "Portal de clientes", Status = RequirementStatus.Entregado };
        var vivo = new Requirement { Title = "Facturación", Status = RequirementStatus.EnDesarrollo };
        db.Requirements.AddRange(entregado, vivo);
        db.SaveChanges();
        db.Assignments.AddRange(
            new Assignment { RequirementId = entregado.Id, DeveloperId = dev.Id },
            new Assignment { RequirementId = vivo.Id, DeveloperId = dev.Id });
        db.SaveChanges();

        var (anio, mes) = Hace(0);
        var e = Entrada(dev, crit, anio, mes, PointApprovalStatus.Pendiente);
        e.RequirementId = entregado.Id;
        db.PointEntries.Add(e);
        db.SaveChanges();

        var mias = await Svc(db, yo).MisActividadesAsync();

        Assert.Equal(2, mias.Requerimientos.Count);
        Assert.Contains(mias.Requerimientos, r => r.Id == entregado.Id);
        Assert.Equal(entregado.Id, Assert.Single(mias.Autocalificaciones).RequerimientoId);
    }

    [Fact]
    public async Task LosRequerimientosDeOtro_NoSeOfrecen()
    {
        var (db, dev, _, yo) = Entorno();
        var otro = new Developer { FullName = "Luis", IsActive = true };
        db.Developers.Add(otro);
        var req = new Requirement { Title = "Ajeno", Status = RequirementStatus.EnDesarrollo };
        db.Requirements.Add(req);
        db.SaveChanges();
        db.Assignments.Add(new Assignment { RequirementId = req.Id, DeveloperId = otro.Id });
        db.SaveChanges();

        Assert.Empty((await Svc(db, yo).MisActividadesAsync()).Requerimientos);
    }

    // ── Actividades libres ───────────────────────────────────────────────────────

    [Fact]
    public async Task LasActividadesLibres_TraenSuTiempoYSuCuentaDeEvidencias()
    {
        var (db, dev, _, yo) = Entorno();
        var actividad = new DevActivity { DeveloperId = dev.Id, Title = "Apoyo a soporte" };
        db.DevActivities.Add(actividad);
        db.SaveChanges();

        db.WorkSessions.AddRange(
            new WorkSession
            {
                DeveloperId = dev.Id, ActivityId = actividad.Id,
                AccumulatedSeconds = 3600, Status = WorkSessionStatus.Detenida
            },
            new WorkSession
            {
                DeveloperId = dev.Id, ActivityId = actividad.Id,
                AccumulatedSeconds = 600, Status = WorkSessionStatus.Pausada
            });
        db.SaveChanges();

        var servicios = Svc(db, yo);
        var bitacora = new AuditService(db, yo, new OrigenDePrueba());
        var actividades = new DevActivityService(db, yo, bitacora, new WorkSessionService(db, yo, bitacora));
        await actividades.AgregarEvidenciaAsync(actividad.Id, "captura.png", [1, 2, 3], "La conversación");

        var fila = Assert.Single((await servicios.MisActividadesAsync()).ActividadesLibres);

        Assert.Equal(4200, fila.SegundosCronometrados);
        Assert.Equal(1, fila.Evidencias);
        Assert.True(fila.EstaAbierta);
    }

    [Fact]
    public async Task ElTiempoDeOtraActividad_NoSeSuma()
    {
        var (db, dev, _, yo) = Entorno();
        var otro = new Developer { FullName = "Luis", IsActive = true };
        db.Developers.Add(otro);
        db.SaveChanges();

        var mia = new DevActivity { DeveloperId = dev.Id, Title = "Mía" };
        var ajena = new DevActivity { DeveloperId = otro.Id, Title = "Ajena" };
        db.DevActivities.AddRange(mia, ajena);
        db.SaveChanges();

        db.WorkSessions.Add(new WorkSession
        {
            DeveloperId = otro.Id, ActivityId = ajena.Id,
            AccumulatedSeconds = 9999, Status = WorkSessionStatus.Detenida
        });
        db.SaveChanges();

        var fila = Assert.Single((await Svc(db, yo).MisActividadesAsync()).ActividadesLibres);

        Assert.Equal("Mía", fila.Titulo);
        Assert.Equal(0, fila.SegundosCronometrados);
    }

    // ── La captura ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaCaptura_NoViajaConLaLista()
    {
        // Con una imagen por entrada, mandarlas todas convertiría una lista en varios megabytes.
        var (db, dev, crit, yo) = Entorno();
        var (anio, mes) = Hace(0);
        var e = Entrada(dev, crit, anio, mes, PointApprovalStatus.Pendiente);
        e.Screenshot = [9, 9, 9];
        e.ScreenshotFileName = "captura.png";
        db.PointEntries.Add(e);
        db.SaveChanges();

        var fila = Assert.Single((await Svc(db, yo).MisActividadesAsync()).Autocalificaciones);
        Assert.True(fila.TieneCaptura);

        var (bytes, nombre) = await Svc(db, yo).CapturaDeAsync(e.Id);
        Assert.Equal(3, bytes.Length);
        Assert.Equal("captura.png", nombre);
    }

    [Fact]
    public async Task LaCaptura_DeOtroDesarrolladorNoSale()
    {
        var (db, dev, crit, yo) = Entorno();
        var (anio, mes) = Hace(0);
        var e = Entrada(dev, crit, anio, mes, PointApprovalStatus.Pendiente);
        e.Screenshot = [9, 9, 9];
        db.PointEntries.Add(e);
        db.SaveChanges();

        var intruso = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id + 99, userId: 77);

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, intruso).CapturaDeAsync(e.Id));
    }

    [Fact]
    public async Task LaCapturaQueNoExiste_VuelveVaciaEnVezDeReventar()
    {
        var (db, _, _, yo) = Entorno();

        var (bytes, nombre) = await Svc(db, yo).CapturaDeAsync(12345);

        Assert.Empty(bytes);
        Assert.Equal("", nombre);
    }

    // ── Casos de borde ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SinFichaDeDesarrollador_DevuelveLaPantallaVaciaPeroConLosTopes()
    {
        // Igual que «Mi panel»: la pantalla se enseña y explica por qué no hay nada, en vez de
        // quedarse en blanco.
        using var db = TestDb.New();
        var sinFicha = UsuarioDePrueba.Como(UserRole.Admin, developerId: null, userId: 1);

        var mias = await Svc(db, sinFicha).MisActividadesAsync();

        Assert.False(mias.TieneFicha);
        Assert.Empty(mias.Autocalificaciones);
        Assert.Empty(mias.ActividadesLibres);
        Assert.Equal(PerformanceScoringService.MaxArgumento, mias.Limites.MaxArgumento);
        Assert.Equal(PerformanceScoringService.MaxMinutosDeclarados, mias.Limites.MaxMinutosDeclarados);
    }

    [Fact]
    public async Task ElNivel_ViajaPorqueDecideQueActividadesTocan()
    {
        var (db, _, _, yo) = Entorno();

        Assert.Equal("Senior", (await Svc(db, yo).MisActividadesAsync()).Nivel);
    }

    [Fact]
    public async Task SinSesion_NoDevuelveNada()
    {
        using var db = TestDb.New();

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, UsuarioDePrueba.Anonimo()).MisActividadesAsync());
    }
}
