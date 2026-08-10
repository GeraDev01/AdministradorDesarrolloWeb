using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Trabajo;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Lo que reciben las tres pantallas del trabajo, de una sola vez.
///
/// Esta consulta no decide nada de negocio —eso siguen haciéndolo <see cref="RequirementService"/> y
/// <see cref="SprintService"/>—, así que lo que se prueba aquí es otra cosa: que lo que se junta sea
/// lo que la pantalla necesita para no equivocarse. Y lo que la pantalla puede equivocar es
/// concreto:
///
///  · El AVANCE del sprint tiene que salir sobre TODO el sprint, no sobre lo que el filtro deja ver.
///  · Lo VENCIDO lo decide el servidor con SU fecha; si lo decidiera el navegador, un equipo con el
///    reloj corrido pintaría de rojo lo que no lo está.
///  · El TIEMPO de cada asignación se suma agrupado y en una sola consulta, incluyendo el tramo en
///    curso: si no incluyera el tramo, el cronómetro corriendo enseñaría el total de la última pausa.
/// </summary>
public class TrabajoQueryServiceTests
{
    private static TrabajoQueryService Svc(AppDbContext db, ICurrentUser cu)
    {
        var audit = new AuditService(db, cu, new OrigenDePrueba());
        return new TrabajoQueryService(
            db, cu,
            new RequirementService(db, cu, audit, new NotificationService(db)),
            new RequirementAttachmentService(db, cu, audit),
            new SprintService(db, cu, audit),
            new JornadaQueryService(db, cu, new AttendanceService(db, cu, audit, new OrigenDePrueba()),
                                    new PresenceService(db, cu, new OrigenDePrueba())));
    }

    private static TrabajoQueryService Admin(AppDbContext db, int? devId = null) =>
        Svc(db, UsuarioDePrueba.Como(UserRole.Admin, devId, userId: 99));

    private static Developer Dev(AppDbContext db, string nombre, bool activo = true)
    {
        var d = new Developer { FullName = nombre, IsActive = activo };
        db.Developers.Add(d);
        db.SaveChanges();
        return d;
    }

    private static Requirement Req(AppDbContext db, string titulo,
        RequirementStatus estado = RequirementStatus.Estimado, int avance = 0, int? sprintId = null,
        DateTime? compromiso = null, DateTime? entrega = null, int? asignadoA = null)
    {
        var r = new Requirement
        {
            Title = titulo, Status = estado, ProgressPercent = avance, SprintId = sprintId,
            CommittedDeliveryDate = compromiso, ActualDeliveryDate = entrega
        };
        db.Requirements.Add(r);
        db.SaveChanges();

        if (asignadoA is int dev)
        {
            db.Assignments.Add(new Assignment { RequirementId = r.Id, DeveloperId = dev });
            db.SaveChanges();
        }
        return r;
    }

    private static async Task<Sprint> NuevoSprint(AppDbContext db,
        DateTime? inicio = null, DateTime? fin = null)
    {
        var cu = UsuarioDePrueba.Como(UserRole.Admin, userId: 99);
        var (ok, _, s) = await new SprintService(db, cu, new AuditService(db, cu, new OrigenDePrueba()))
            .CrearAsync("Sprint 1", "Cerrar pagos", inicio ?? DateTime.Today.AddDays(-5),
                fin ?? DateTime.Today.AddDays(8));
        Assert.True(ok);
        return s!;
    }

    // ── Pantalla de requerimientos ───────────────────────────────────────────────

    [Fact]
    public async Task Requerimientos_TraenElNombreDeQuienLosTiene_AunqueEsaFichaEsteDeBaja()
    {
        // Un requerimiento asignado a alguien que ya no está debe seguir diciendo a quién. Un hueco
        // ahí obliga a ir a buscarlo a otra pantalla justo cuando hay que reasignarlo.
        using var db = TestDb.New();
        var exEmpleado = Dev(db, "Ex", activo: false);
        Req(db, "Reporte", asignadoA: exEmpleado.Id);

        var datos = await Admin(db).RequerimientosAsync();

        Assert.Equal("Ex", Assert.Single(datos.Filas).DesarrolladoresTexto);
        Assert.Empty(datos.Desarrolladores);   // pero ya no se le puede asignar nada nuevo
    }

    [Fact]
    public async Task Requerimientos_MarcanVencidoSoloLoQueSiguePendiente()
    {
        // La misma regla del escritorio: entregado y cancelado no se marcan nunca, porque ya no hay
        // nada que llegue tarde.
        using var db = TestDb.New();
        var ayer = DateTime.Today.AddDays(-1);
        Req(db, "Tarde", RequirementStatus.EnDesarrollo, compromiso: ayer);
        Req(db, "Entregado tarde", RequirementStatus.Entregado, compromiso: ayer);
        Req(db, "Cancelado", RequirementStatus.Cancelado, compromiso: ayer);
        Req(db, "Aún a tiempo", RequirementStatus.EnDesarrollo, compromiso: DateTime.Today.AddDays(3));

        var filas = (await Admin(db).RequerimientosAsync()).Filas;

        Assert.Equal(["Tarde"], filas.Where(f => f.Vencido).Select(f => f.Titulo));
    }

    // ── Pantalla de sprint ───────────────────────────────────────────────────────

    [Fact]
    public async Task Seguimiento_DeUnSprintQueYaNoExiste_DevuelveNuloEnVezDeReventar()
    {
        // Alguien pudo borrarlo mientras esta pantalla estaba abierta.
        using var db = TestDb.New();
        Assert.Null(await Admin(db).SeguimientoAsync(9999));
    }

    [Fact]
    public async Task Seguimiento_ElAvanceSaleSobreTodoElSprint_YMarcaLoMio()
    {
        // LA prueba del sprint: el 👤 es una ayuda visual, no un filtro. El avance incluye lo ajeno,
        // porque dos porcentajes distintos para el mismo sprint serían dos verdades.
        using var db = TestDb.New();
        var s = await NuevoSprint(db);
        var yo = Dev(db, "Ana");
        var otro = Dev(db, "Beto");

        Req(db, "mío", RequirementStatus.EnDesarrollo, avance: 100, sprintId: s.Id, asignadoA: yo.Id);
        Req(db, "ajeno", RequirementStatus.EnDesarrollo, avance: 0, sprintId: s.Id, asignadoA: otro.Id);

        var seg = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, yo.Id, userId: 3))
            .SeguimientoAsync(s.Id);

        Assert.NotNull(seg);
        Assert.Equal(2, seg!.Requerimientos.Count);
        Assert.Equal(1, seg.Mios);
        Assert.Equal(["mío"], seg.Requerimientos.Where(r => r.Mio).Select(r => r.Titulo));
        Assert.Equal(50, seg.Avance.AvanceRealPct);          // (100 + 0) / 2, con lo ajeno dentro
        Assert.Equal(2, seg.Avance.TotalRequerimientos);
    }

    [Fact]
    public async Task Seguimiento_ElEntregadoSeEnseniaAlCien_AunqueSuPorcentajeDigaOtraCosa()
    {
        // Es la misma regla con la que el servicio calcula el avance; la columna no puede
        // contradecir al KPI que tiene encima.
        using var db = TestDb.New();
        var s = await NuevoSprint(db);
        Req(db, "entregado a medio capturar", RequirementStatus.Entregado, avance: 60, sprintId: s.Id);

        var seg = await Admin(db).SeguimientoAsync(s.Id);

        Assert.Equal(100, Assert.Single(seg!.Requerimientos).AvancePct);
    }

    [Fact]
    public async Task Seguimiento_MarcaElCompromisoVencido_SoloSiSigueVivo()
    {
        using var db = TestDb.New();
        var s = await NuevoSprint(db);
        var ayer = DateTime.Today.AddDays(-1);
        Req(db, "tarde", RequirementStatus.EnPruebas, sprintId: s.Id, compromiso: ayer);
        Req(db, "cerrado", RequirementStatus.Entregado, sprintId: s.Id, compromiso: ayer, entrega: ayer);

        var seg = await Admin(db).SeguimientoAsync(s.Id);

        Assert.Equal(["tarde"],
            seg!.Requerimientos.Where(r => r.CompromisoVencido).Select(r => r.Titulo));
    }

    [Fact]
    public async Task Candidatos_NiLosCancelados_NiLosDeOtroSprint()
    {
        // Mudar un requerimiento de sprint tiene que ser una decisión tomada desde el otro sprint, no
        // el accidente de una casilla; y a un cancelado no hay nada que seguirle.
        using var db = TestDb.New();
        var mio = await NuevoSprint(db);
        var otro = await NuevoSprint(db, DateTime.Today.AddDays(20), DateTime.Today.AddDays(30));

        var dentro = Req(db, "ya está dentro", sprintId: mio.Id);
        var backlog = Req(db, "en el backlog");
        Req(db, "de otro sprint", sprintId: otro.Id);
        Req(db, "cancelado", RequirementStatus.Cancelado);

        var candidatos = await Admin(db).CandidatosAsync(mio.Id);

        Assert.Equal([dentro.Id, backlog.Id], candidatos.Select(c => c.Id));   // primero lo que ya está
        Assert.True(candidatos[0].EnElSprint);
        Assert.False(candidatos[1].EnElSprint);
    }

    [Fact]
    public async Task Candidatos_SonDelLider()
    {
        using var db = TestDb.New();
        var s = await NuevoSprint(db);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 3)).CandidatosAsync(s.Id));
    }

    [Fact]
    public async Task Historico_ResumeVelocidadYCumplimientoDeLosCerradosConTrabajo()
    {
        using var db = TestDb.New();
        var cerrado = await NuevoSprint(db, DateTime.Today.AddDays(-40), DateTime.Today.AddDays(-27));
        var vacio = await NuevoSprint(db, DateTime.Today.AddDays(-26), DateTime.Today.AddDays(-15));
        Req(db, "a", RequirementStatus.Entregado, sprintId: cerrado.Id);
        Req(db, "b", RequirementStatus.EnDesarrollo, sprintId: cerrado.Id);

        var h = await Admin(db).HistoricoAsync();

        Assert.Equal(2, h.Sprints.Count);
        Assert.Equal(1, h.SprintsContados);      // el vacío no cuenta: su 0 no es un fracaso
        Assert.Equal(1.0, h.Velocidad);
        Assert.Equal(50, h.CumplimientoPromedio);
        Assert.Equal(vacio.Id, h.Sprints[1].Id); // cronológico: la gráfica se lee de izquierda a derecha
    }

    // ── Pantalla de mis asignaciones ─────────────────────────────────────────────

    [Fact]
    public async Task MisAsignaciones_SinFichaLigada_LoDiceEnVezDeReventar()
    {
        // Caso real: cuentas de administración sin ficha. El tiempo se registra contra una ficha.
        using var db = TestDb.New();

        var mias = await Admin(db).MisAsignacionesAsync();

        Assert.False(mias.TieneFicha);
        Assert.Empty(mias.Filas);
        Assert.Null(mias.Cronometro);
    }

    [Fact]
    public async Task MisAsignaciones_SoloLasMias()
    {
        using var db = TestDb.New();
        var yo = Dev(db, "Ana");
        var otro = Dev(db, "Beto");
        Req(db, "mío", asignadoA: yo.Id);
        Req(db, "ajeno", asignadoA: otro.Id);
        Req(db, "de nadie");

        var mias = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, yo.Id, userId: 3))
            .MisAsignacionesAsync();

        Assert.True(mias.TieneFicha);
        Assert.Equal(["mío"], mias.Filas.Select(f => f.Titulo));
    }

    [Fact]
    public async Task MisAsignaciones_SumanElTiempoDeTodasLasSesiones_ConElTramoEnCurso()
    {
        // Sin el tramo en curso, la fila que está corriendo enseñaría el total de la última pausa y
        // parecería congelada mientras el reloj de arriba avanza.
        using var db = TestDb.New();
        var yo = Dev(db, "Ana");
        var req = Req(db, "mío", asignadoA: yo.Id);

        db.WorkSessions.AddRange(
            new WorkSession
            {
                DeveloperId = yo.Id, RequirementId = req.Id, AccumulatedSeconds = 600,
                Status = WorkSessionStatus.Detenida
            },
            new WorkSession
            {
                DeveloperId = yo.Id, RequirementId = req.Id, AccumulatedSeconds = 60,
                Status = WorkSessionStatus.Activa, LastResumedAt = DateTime.UtcNow.AddSeconds(-30)
            });
        db.SaveChanges();

        var fila = Assert.Single((await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, yo.Id, userId: 3))
            .MisAsignacionesAsync()).Filas);

        Assert.InRange(fila.SegundosDedicados, 688, 695);   // 600 + 60 + ~30
        Assert.Equal(EstadoDelCronometro.Activo, fila.Cronometro);
        Assert.StartsWith("11m", fila.TiempoTexto);
    }

    [Fact]
    public async Task MisAsignaciones_DistinguenPausadoDeSinSesion()
    {
        // Es lo que decide si el botón dice «Iniciar» o «Reanudar», y si además hay que ofrecer
        // «Detener»: una sesión pausada sigue abierta y hay que poder cerrarla.
        using var db = TestDb.New();
        var yo = Dev(db, "Ana");
        var pausado = Req(db, "pausado", asignadoA: yo.Id, compromiso: DateTime.Today.AddDays(2));
        var nunca = Req(db, "sin empezar", asignadoA: yo.Id, compromiso: DateTime.Today.AddDays(1));

        db.WorkSessions.Add(new WorkSession
        {
            DeveloperId = yo.Id, RequirementId = pausado.Id, AccumulatedSeconds = 120,
            Status = WorkSessionStatus.Pausada
        });
        db.SaveChanges();

        var filas = (await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, yo.Id, userId: 3))
            .MisAsignacionesAsync()).Filas;

        Assert.Equal(EstadoDelCronometro.Pausado, filas.Single(f => f.Id == pausado.Id).Cronometro);
        Assert.Equal(EstadoDelCronometro.SinSesion, filas.Single(f => f.Id == nunca.Id).Cronometro);
        Assert.Equal("00m 00s", filas.Single(f => f.Id == nunca.Id).TiempoTexto);
    }

    [Fact]
    public async Task MisAsignaciones_NoCuentanElTiempoDeOtroDesarrollador()
    {
        // Dos personas pueden cronometrar el mismo requerimiento: cada una ve el suyo.
        using var db = TestDb.New();
        var yo = Dev(db, "Ana");
        var otro = Dev(db, "Beto");
        var req = Req(db, "compartido", asignadoA: yo.Id);
        db.Assignments.Add(new Assignment { RequirementId = req.Id, DeveloperId = otro.Id });
        db.WorkSessions.AddRange(
            new WorkSession { DeveloperId = yo.Id, RequirementId = req.Id, AccumulatedSeconds = 100, Status = WorkSessionStatus.Detenida },
            new WorkSession { DeveloperId = otro.Id, RequirementId = req.Id, AccumulatedSeconds = 900, Status = WorkSessionStatus.Detenida });
        db.SaveChanges();

        var fila = Assert.Single((await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, yo.Id, userId: 3))
            .MisAsignacionesAsync()).Filas);

        Assert.Equal(100, fila.SegundosDedicados);
    }

    [Fact]
    public async Task MisAsignaciones_FiltranPorEstado()
    {
        using var db = TestDb.New();
        var yo = Dev(db, "Ana");
        Req(db, "en curso", RequirementStatus.EnDesarrollo, asignadoA: yo.Id);
        Req(db, "entregado", RequirementStatus.Entregado, asignadoA: yo.Id);

        var mias = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, yo.Id, userId: 3))
            .MisAsignacionesAsync(RequirementStatus.EnDesarrollo);

        Assert.Equal(["en curso"], mias.Filas.Select(f => f.Titulo));
    }

    [Fact]
    public async Task MisAsignaciones_QuedanFueraDelAlcanceDeOperaciones()
    {
        // Guarda positiva por rol: abrir esto al desarrollador no puede regalárselo a Operaciones,
        // cuyo alcance son los despliegues.
        using var db = TestDb.New();

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, UsuarioDePrueba.Como(UserRole.Operaciones, 5, userId: 4)).MisAsignacionesAsync());
    }
}
