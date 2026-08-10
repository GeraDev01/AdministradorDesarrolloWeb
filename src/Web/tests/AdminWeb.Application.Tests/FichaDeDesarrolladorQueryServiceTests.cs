using AdminWeb.Application.Services;
using AdminWeb.Domain.Documentos;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Documentos;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// La ficha del desarrollador: qué datos se reúnen y quién puede pedirla.
///
/// Del reporte del escritorio solo se porta la mitad que RECOGE los datos; maquetarlos ya es cosa de
/// <see cref="IGeneradorDeDocumentos"/> y se prueba aparte. Lo que se protege aquí es que los números
/// del encabezado —puntos del año, tiempo registrado, asignaciones activas— cuenten lo que decían
/// contar, y que un desarrollador no pueda imprimir la ficha de otro.
/// </summary>
public class FichaDeDesarrolladorQueryServiceTests
{
    private static FichaDeDesarrolladorQueryService Svc(AppDbContext db, ICurrentUser quien)
    {
        var bitacora = new AuditService(db, quien, new OrigenDePrueba());
        return new FichaDeDesarrolladorQueryService(
            db, quien, new WorkSessionService(db, quien, bitacora), new GeneradorDeDocumentosQuestPdf());
    }

    private static Developer Dev(AppDbContext db, string nombre = "Ana")
    {
        var d = new Developer
        {
            FullName = nombre, IsActive = true, Email = "ana@ejemplo.com",
            Seniority = "Senior", HireDate = new DateTime(2023, 3, 1)
        };
        db.Developers.Add(d);
        db.SaveChanges();
        return d;
    }

    private static ScoringCriterion Crit(AppDbContext db)
    {
        var c = new ScoringCriterion { Name = "C", DefaultPoints = 5, IsActive = true, Scope = CriterionScope.Individual };
        db.ScoringCriteria.Add(c);
        db.SaveChanges();
        return c;
    }

    // ── Autorización ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_desarrollador_no_puede_imprimir_la_ficha_de_otro()
    {
        using var db = TestDb.New();
        var ana = Dev(db, "Ana");
        var beto = Dev(db, "Beto");

        var yo = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: ana.Id, userId: 10);

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, yo).FichaAsync(beto.Id));

        // La suya sí: es su propia evaluación.
        var (pdf, _) = await Svc(db, yo).FichaAsync(ana.Id);
        Assert.NotNull(pdf);
    }

    [Fact]
    public async Task El_lider_puede_imprimir_la_de_cualquiera()
    {
        using var db = TestDb.New();
        var ana = Dev(db);
        var lider = UsuarioDePrueba.Como(UserRole.Admin, userId: 900);

        var (pdf, nombre) = await Svc(db, lider).FichaAsync(ana.Id);

        Assert.NotNull(pdf);
        Assert.Equal("%PDF"u8.ToArray(), pdf![..4]);
        Assert.StartsWith("Ficha_Ana_", nombre);
        Assert.EndsWith(".pdf", nombre);
    }

    [Fact]
    public async Task Una_ficha_de_alguien_que_no_existe_lo_dice_en_vez_de_reventar()
    {
        using var db = TestDb.New();
        var lider = UsuarioDePrueba.Como(UserRole.Admin, userId: 900);

        var (pdf, mensaje) = await Svc(db, lider).FichaAsync(9999);

        Assert.Null(pdf);
        Assert.Contains("no existe", mensaje);
    }

    // ── Datos reunidos ───────────────────────────────────────────────────────

    [Fact]
    public async Task Los_puntos_del_encabezado_son_los_APROBADOS_del_año_en_curso()
    {
        using var db = TestDb.New();
        var ana = Dev(db);
        var crit = Crit(db);
        int anio = DateTime.Today.Year;

        void Punto(int puntos, int año, PointApprovalStatus estado) => db.PointEntries.Add(new PointEntry
        {
            DeveloperId = ana.Id, CriterionId = crit.Id, Points = puntos,
            Year = año, Month = 1, Date = DateTime.UtcNow, ApprovalStatus = estado
        });

        Punto(10, anio, PointApprovalStatus.Aprobado);
        Punto(5, anio, PointApprovalStatus.Aprobado);
        Punto(50, anio, PointApprovalStatus.Pendiente);      // aún no se le reconoció
        Punto(50, anio, PointApprovalStatus.Rechazado);      // no se le reconoció
        Punto(99, anio - 1, PointApprovalStatus.Aprobado);   // otro ejercicio
        db.SaveChanges();

        var datos = await Svc(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 900)).ReunirAsync(ana.Id);

        Assert.NotNull(datos);
        Assert.Equal(15, datos!.PuntosAprobadosDelAnio);
    }

    [Fact]
    public async Task Las_asignaciones_activas_no_cuentan_lo_entregado_ni_lo_cancelado()
    {
        using var db = TestDb.New();
        var ana = Dev(db);

        void Asignar(RequirementStatus estado)
        {
            var r = new Requirement { Title = estado.ToString(), Status = estado };
            db.Requirements.Add(r);
            db.SaveChanges();
            db.Assignments.Add(new Assignment { DeveloperId = ana.Id, RequirementId = r.Id });
            db.SaveChanges();
        }

        Asignar(RequirementStatus.EnDesarrollo);
        Asignar(RequirementStatus.EnPruebas);
        Asignar(RequirementStatus.Entregado);
        Asignar(RequirementStatus.Cancelado);

        var datos = await Svc(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 900)).ReunirAsync(ana.Id);

        Assert.Equal(2, datos!.AsignacionesActivas);
    }

    [Fact]
    public async Task Sin_equipo_no_se_inventa_un_rol()
    {
        using var db = TestDb.New();
        var ana = Dev(db);

        // «Sin rol» al lado de «Sin equipo» se leería como dos huecos distintos cuando es el mismo.
        var datos = await Svc(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 900)).ReunirAsync(ana.Id);

        Assert.Null(datos!.Equipo);
        Assert.Null(datos.RolEnElEquipo);
    }

    [Fact]
    public async Task Con_equipo_se_enseña_el_rol_en_palabras()
    {
        using var db = TestDb.New();
        var equipo = new Team { Name = "Alfa", CreatedAt = DateTime.UtcNow };
        db.Teams.Add(equipo);
        db.SaveChanges();

        var ana = Dev(db);
        ana.TeamId = equipo.Id;
        ana.TeamRole = TeamRole.Backend;
        db.SaveChanges();

        var datos = await Svc(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 900)).ReunirAsync(ana.Id);

        Assert.Equal("Alfa", datos!.Equipo);
        Assert.Equal("Backend Dev", datos.RolEnElEquipo);
    }

    [Fact]
    public async Task Las_evaluaciones_y_los_hitos_llegan_de_lo_mas_reciente_a_lo_mas_antiguo()
    {
        using var db = TestDb.New();
        var ana = Dev(db);

        db.DeveloperEvaluations.AddRange(
            new DeveloperEvaluation { DeveloperId = ana.Id, EvaluationDate = new DateTime(2025, 1, 1), Strengths = "vieja" },
            new DeveloperEvaluation { DeveloperId = ana.Id, EvaluationDate = new DateTime(2026, 1, 1), Strengths = "nueva" });
        db.DeveloperMilestones.AddRange(
            new DeveloperMilestone { DeveloperId = ana.Id, Date = new DateTime(2025, 6, 1), Title = "viejo" },
            new DeveloperMilestone { DeveloperId = ana.Id, Date = new DateTime(2026, 6, 1), Title = "nuevo" });
        db.SaveChanges();

        var datos = await Svc(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 900)).ReunirAsync(ana.Id);

        Assert.Equal("nueva", datos!.Evaluaciones[0].Fortalezas);
        Assert.Equal("nuevo", datos.Hitos[0].Titulo);
    }

    [Fact]
    public async Task Una_ficha_recien_creada_se_imprime_igual_sin_evaluaciones_ni_hitos()
    {
        using var db = TestDb.New();
        var ana = Dev(db);

        // El caso que más veces se pide: alguien acaba de entrar y todavía no tiene nada. Que el PDF
        // reviente ahí sería fallar justo en el estreno.
        var (pdf, _) = await Svc(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 900)).FichaAsync(ana.Id);

        Assert.NotNull(pdf);
        Assert.True(pdf!.Length > 1000);
    }
}
