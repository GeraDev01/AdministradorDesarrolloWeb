using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Evaluaciones;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Evaluaciones de líder e hitos.
///
/// Lo que se protege: que ESCRIBIR sea solo del líder, que un desarrollador no pueda leer las
/// evaluaciones de otro por ninguna vía del servicio, y que editar no cambie ni el evaluador ni el
/// dueño — el nombre grabado es el de quien hizo la valoración, no el de quien corrige una errata
/// meses después.
/// </summary>
public class EvaluacionesServiceTests
{
    private static EvaluacionesService Svc(AppDbContext db, ICurrentUser quien) =>
        new(db, quien, new AuditService(db, quien, new OrigenDePrueba()));

    private static (AppDbContext db, Developer ana, Developer beto, UsuarioDePrueba lider) Entorno()
    {
        var db = TestDb.New();
        var ana = new Developer { FullName = "Ana", IsActive = true };
        var beto = new Developer { FullName = "Beto", IsActive = true };
        db.Developers.AddRange(ana, beto);
        db.SaveChanges();

        var lider = new UsuarioDePrueba
        {
            UserId = 900, Username = "gerardo", FullName = "Gerardo Manjarrez", Role = UserRole.Admin
        };
        return (db, ana, beto, lider);
    }

    private static GuardarEvaluacionRequest Evaluacion(int devId, int? calificacion = 4, int? id = null) =>
        new(id, devId, new DateTime(2026, 8, 1), "2026 Q3", calificacion,
            "Aprende rápido", "Le cuesta pedir ayuda pronto", "Buen semestre");

    // ── Autorización ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Escribir_evaluaciones_e_hitos_es_solo_del_lider()
    {
        var (db, ana, _, _) = Entorno();
        using var _db = db;
        var yo = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: ana.Id, userId: 10);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, yo).GuardarEvaluacionAsync(Evaluacion(ana.Id)));
        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, yo).GuardarHitoAsync(new GuardarHitoRequest(
                null, ana.Id, DateTime.Today, MilestoneKind.Logro, "Certificación", null)));
        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, yo).EliminarEvaluacionAsync(1));
        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, yo).EliminarHitoAsync(1));
    }

    [Fact]
    public async Task Un_desarrollador_no_puede_leer_la_ficha_de_otro()
    {
        var (db, ana, beto, _) = Entorno();
        using var _db = db;
        var yo = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: ana.Id, userId: 10);

        // No hay una versión «de otro» de esta consulta: la del líder exige serlo, y la propia se
        // resuelve de la sesión sin recibir ningún identificador.
        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, yo).DeDesarrolladorAsync(beto.Id));
        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, yo).DesarrolladoresAsync());
    }

    [Fact]
    public async Task Mis_evaluaciones_traen_solo_las_mias()
    {
        var (db, ana, beto, lider) = Entorno();
        using var _db = db;
        await Svc(db, lider).GuardarEvaluacionAsync(Evaluacion(ana.Id));
        await Svc(db, lider).GuardarEvaluacionAsync(Evaluacion(beto.Id));

        var yo = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: ana.Id, userId: 10);
        var mias = await Svc(db, yo).MisAsync();

        Assert.True(mias.TieneFicha);
        Assert.Equal("Ana", mias.Desarrollador);
        Assert.Single(mias.Evaluaciones);
    }

    [Fact]
    public async Task Sin_ficha_ligada_la_pantalla_se_enseña_vacia_y_lo_dice()
    {
        var (db, _, _, _) = Entorno();
        using var _db = db;

        // Devolver un error aquí dejaría una pantalla en blanco sin explicación, que es peor.
        var sinFicha = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 11)).MisAsync();

        Assert.False(sinFicha.TieneFicha);
        Assert.Empty(sinFicha.Evaluaciones);
        Assert.Empty(sinFicha.Hitos);
    }

    // ── Alta y edición ───────────────────────────────────────────────────────

    [Fact]
    public async Task Al_registrar_queda_grabado_quien_evaluo()
    {
        var (db, ana, _, lider) = Entorno();
        using var _db = db;

        var (ok, _) = await Svc(db, lider).GuardarEvaluacionAsync(Evaluacion(ana.Id));

        Assert.True(ok);
        var guardada = db.DeveloperEvaluations.Single();
        Assert.Equal("Gerardo Manjarrez", guardada.EvaluatorName);
        Assert.Equal(900, guardada.EvaluatorUserId);
        Assert.Equal(4, guardada.OverallRating);
    }

    [Fact]
    public async Task Editar_no_cambia_al_evaluador_ni_al_desarrollador()
    {
        var (db, ana, beto, lider) = Entorno();
        using var _db = db;
        await Svc(db, lider).GuardarEvaluacionAsync(Evaluacion(ana.Id));
        int id = db.DeveloperEvaluations.Single().Id;

        var otro = new UsuarioDePrueba
        {
            UserId = 901, Username = "otra", FullName = "Otra Persona", Role = UserRole.Admin
        };

        // Aunque la petición venga con OTRO desarrollador y la firme otra cuenta: el dueño y el
        // evaluador son los que se grabaron.
        var (ok, _) = await Svc(db, otro).GuardarEvaluacionAsync(
            new GuardarEvaluacionRequest(id, beto.Id, new DateTime(2026, 9, 1), "2026 Q4", 5,
                "Corregido", null, null));

        Assert.True(ok);
        var guardada = db.DeveloperEvaluations.Single();
        Assert.Equal(ana.Id, guardada.DeveloperId);
        Assert.Equal("Gerardo Manjarrez", guardada.EvaluatorName);
        Assert.Equal("Corregido", guardada.Strengths);
        Assert.Equal(5, guardada.OverallRating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task La_calificacion_fuera_de_la_escala_se_rechaza(int calificacion)
    {
        var (db, ana, _, lider) = Entorno();
        using var _db = db;

        var (ok, mensaje) = await Svc(db, lider).GuardarEvaluacionAsync(Evaluacion(ana.Id, calificacion));

        Assert.False(ok);
        Assert.Contains("de 1 a 5", mensaje);
        Assert.Empty(db.DeveloperEvaluations);
    }

    [Fact]
    public async Task Sin_calificar_es_una_respuesta_legitima()
    {
        var (db, ana, _, lider) = Entorno();
        using var _db = db;

        var (ok, _) = await Svc(db, lider).GuardarEvaluacionAsync(Evaluacion(ana.Id, calificacion: null));

        Assert.True(ok);
        Assert.Null(db.DeveloperEvaluations.Single().OverallRating);
    }

    [Fact]
    public async Task No_se_evalua_a_un_desarrollador_que_no_existe()
    {
        var (db, _, _, lider) = Entorno();
        using var _db = db;

        var (ok, mensaje) = await Svc(db, lider).GuardarEvaluacionAsync(Evaluacion(devId: 9999));

        Assert.False(ok);
        Assert.Contains("no existe", mensaje);
    }

    [Fact]
    public async Task Editar_algo_que_ya_se_borro_lo_dice_en_vez_de_crear_otro()
    {
        var (db, ana, _, lider) = Entorno();
        using var _db = db;

        var (ok, mensaje) = await Svc(db, lider).GuardarEvaluacionAsync(Evaluacion(ana.Id, id: 4242));

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje);
        Assert.Empty(db.DeveloperEvaluations);
    }

    // ── Hitos ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_hito_sin_titulo_no_se_guarda()
    {
        var (db, ana, _, lider) = Entorno();
        using var _db = db;

        var (ok, mensaje) = await Svc(db, lider).GuardarHitoAsync(
            new GuardarHitoRequest(null, ana.Id, DateTime.Today, MilestoneKind.Logro, "   ", null));

        Assert.False(ok);
        Assert.Contains("título", mensaje);
        Assert.Empty(db.DeveloperMilestones);
    }

    [Fact]
    public async Task El_hito_llega_a_la_ficha_con_su_tipo_ya_en_palabras()
    {
        var (db, ana, _, lider) = Entorno();
        using var _db = db;
        await Svc(db, lider).GuardarHitoAsync(new GuardarHitoRequest(
            null, ana.Id, new DateTime(2026, 7, 1), MilestoneKind.Certificacion, "AZ-204", "Aprobada"));

        var ficha = await Svc(db, lider).DeDesarrolladorAsync(ana.Id);

        Assert.NotNull(ficha);
        var hito = Assert.Single(ficha!.Hitos);
        Assert.Equal("🎓 Certificación", hito.TipoTexto);
        Assert.Equal("AZ-204", hito.Titulo);
    }

    [Fact]
    public async Task Eliminar_quita_la_evaluacion_y_el_hito()
    {
        var (db, ana, _, lider) = Entorno();
        using var _db = db;
        await Svc(db, lider).GuardarEvaluacionAsync(Evaluacion(ana.Id));
        await Svc(db, lider).GuardarHitoAsync(new GuardarHitoRequest(
            null, ana.Id, DateTime.Today, MilestoneKind.Logro, "Entrega grande", null));

        await Svc(db, lider).EliminarEvaluacionAsync(db.DeveloperEvaluations.Single().Id);
        await Svc(db, lider).EliminarHitoAsync(db.DeveloperMilestones.Single().Id);

        Assert.Empty(db.DeveloperEvaluations);
        Assert.Empty(db.DeveloperMilestones);
    }

    // ── Contrato ─────────────────────────────────────────────────────────────

    [Fact]
    public void Las_estrellas_se_leen_igual_para_el_lider_y_para_el_desarrollador()
    {
        var conCalificacion = new EvaluacionDto(1, DateTime.Today, null, 4, null, null, null, null);
        var sinCalificacion = new EvaluacionDto(2, DateTime.Today, null, null, null, null, null, null);

        Assert.Equal("★★★★☆  (4/5)", conCalificacion.Estrellas);
        Assert.Equal("Sin calificar", sinCalificacion.Estrellas);
    }

    [Fact]
    public void El_resumen_junta_fortalezas_y_debilidades_en_una_linea()
    {
        var e = new EvaluacionDto(1, DateTime.Today, null, null,
            "Aprende\nrápido", "Le cuesta\npedir ayuda", null, null);

        // Los saltos de línea se aplanan: es una celda de rejilla, no un párrafo.
        Assert.Equal("Aprende rápido   ·   Le cuesta pedir ayuda", e.Resumen);
        Assert.Equal("—", new EvaluacionDto(2, DateTime.Today, null, null, null, null, null, null).Resumen);
    }
}
