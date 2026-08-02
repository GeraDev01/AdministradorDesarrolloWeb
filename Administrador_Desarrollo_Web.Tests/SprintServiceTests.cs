using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Sprints. Lo delicado está en dos sitios: los permisos (es una herramienta SOLO del
/// administrador) y la aritmética de CalcularAvance — el veredicto «atrasado» que ve el jefe
/// sale de ahí, y un cálculo torcido acusa o absuelve al equipo sin razón.
/// </summary>
public class SprintServiceTests
{
    private static SprintService Svc(AppDbContext db, CurrentUserContext cu) =>
        new(db, cu, new AuditService(db, cu));

    private static SprintService Admin(AppDbContext db) => Svc(db, Ctx.As(UserRole.Admin, userId: 99));

    private static Requirement Req(AppDbContext db, string titulo, RequirementStatus estado = RequirementStatus.Estimado,
        int avance = 0, int? sprintId = null)
    {
        var r = new Requirement { Title = titulo, Status = estado, ProgressPercent = avance, SprintId = sprintId };
        db.Requirements.Add(r); db.SaveChanges();
        return r;
    }

    private static Sprint NuevoSprint(AppDbContext db, string nombre = "Sprint 1",
        DateTime? inicio = null, DateTime? fin = null)
    {
        var (ok, _, s) = Admin(db).Crear(nombre, null,
            inicio ?? DateTime.Today.AddDays(-5), fin ?? DateTime.Today.AddDays(8));
        Assert.True(ok);
        return s!;
    }

    // ── Permisos ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ElDesarrollador_LEE_elSprint()
    {
        // La mitad del valor del sprint es que el equipo vea la misma verdad.
        var db = TestDb.New();
        var s = NuevoSprint(db);
        var dev = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 3));

        Assert.Single(dev.Listar());
        Assert.Empty(dev.Requerimientos(s.Id));   // no lanza: consulta permitida
        Assert.NotNull(dev.Avance(s.Id));
    }

    [Fact]
    public void ElDesarrollador_NO_ESCRIBE_elSprint()
    {
        // El alcance lo compromete el administrador y él responde por él.
        var db = TestDb.New();
        var s = NuevoSprint(db);
        var dev = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 3));

        Assert.Throws<AuthorizationException>(() => dev.Crear("Sprint X", null, DateTime.Today, DateTime.Today.AddDays(10)));
        Assert.Throws<AuthorizationException>(() => dev.Actualizar(s.Id, "Otro nombre", null, DateTime.Today, DateTime.Today.AddDays(5)));
        Assert.Throws<AuthorizationException>(() => dev.FijarRequerimientos(s.Id, []));
        Assert.Throws<AuthorizationException>(() => dev.Eliminar(s.Id));
        Assert.Throws<AuthorizationException>(() => dev.Historico());   // la velocidad es del jefe
    }

    [Fact]
    public void Operaciones_QuedaFueraDeTodoElSprint()
    {
        // Guarda positiva por rol: abrir la lectura al desarrollador no puede regalársela a
        // Operaciones de paso. Su alcance son los despliegues.
        var db = TestDb.New();
        var s = NuevoSprint(db);
        var ops = Svc(db, Ctx.As(UserRole.Operaciones, userId: 4));

        Assert.Throws<AuthorizationException>(() => ops.Listar());
        Assert.Throws<AuthorizationException>(() => ops.Requerimientos(s.Id));
        Assert.Throws<AuthorizationException>(() => ops.Avance(s.Id));
        Assert.Throws<AuthorizationException>(() => ops.MisRequerimientos(s.Id));
    }

    [Fact]
    public void MisRequerimientos_SoloLosAsignadosAQuienConsulta()
    {
        var db = TestDb.New();
        var s = NuevoSprint(db);
        var yo   = new Developer { FullName = "Ana", IsActive = true };
        var otro = new Developer { FullName = "Beto", IsActive = true };
        db.Developers.AddRange(yo, otro); db.SaveChanges();

        var mio   = Req(db, "mío", sprintId: s.Id);
        var ajeno = Req(db, "ajeno", sprintId: s.Id);
        db.Assignments.AddRange(
            new Assignment { RequirementId = mio.Id,   DeveloperId = yo.Id },
            new Assignment { RequirementId = ajeno.Id, DeveloperId = otro.Id });
        db.SaveChanges();

        var dev = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 3, developerId: yo.Id));
        var mios = dev.MisRequerimientos(s.Id);

        Assert.Equal([mio.Id], mios);
        // Pero SÍ ve el sprint completo: el resaltado es una ayuda, no un filtro de seguridad.
        Assert.Equal(2, dev.Requerimientos(s.Id).Count);
    }

    [Fact]
    public void MisRequerimientos_SinFichaLigada_DevuelveVacioSinLanzar()
    {
        // Caso real: cuentas sin ficha de desarrollador. La pantalla lo dice en vez de reventar.
        var db = TestDb.New();
        var s = NuevoSprint(db);
        Req(db, "algo", sprintId: s.Id);

        var sinFicha = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 3));
        Assert.Empty(sinFicha.MisRequerimientos(s.Id));
    }

    // ── Alta y validación ────────────────────────────────────────────────────────

    [Fact]
    public void Crear_GuardaYNormalizaFechasADia()
    {
        var db = TestDb.New();
        var (ok, _, s) = Admin(db).Crear("  Sprint 14  ", "Cerrar el módulo de pagos",
            new DateTime(2026, 8, 3, 14, 30, 0), new DateTime(2026, 8, 14, 9, 0, 0));

        Assert.True(ok);
        Assert.Equal("Sprint 14", s!.Name);
        Assert.Equal(new DateTime(2026, 8, 3), s.StartDate);    // la hora se descarta: son DÍAS
        Assert.Equal(new DateTime(2026, 8, 14), s.EndDate);
    }

    [Theory]
    [InlineData("", "nombre")]
    [InlineData("ab", "nombre")]
    public void Crear_RechazaNombreCorto(string nombre, string fragmento)
    {
        var db = TestDb.New();
        var (ok, mensaje, _) = Admin(db).Crear(nombre, null, DateTime.Today, DateTime.Today.AddDays(5));
        Assert.False(ok);
        Assert.Contains(fragmento, mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Crear_RechazaFinAnteriorAlInicio_YSprintsEternos()
    {
        var db = TestDb.New();
        var admin = Admin(db);

        Assert.False(admin.Crear("Sprint X", null, DateTime.Today, DateTime.Today.AddDays(-1)).ok);
        Assert.False(admin.Crear("Sprint X", null, DateTime.Today, DateTime.Today.AddDays(400)).ok);
        Assert.True(admin.Crear("Sprint X", null, DateTime.Today, DateTime.Today).ok);   // 1 día es válido
    }

    // ── Requerimientos del sprint ────────────────────────────────────────────────

    [Fact]
    public void FijarRequerimientos_LoMarcadoQueda_LoDemasVuelveAlBacklog()
    {
        var db = TestDb.New();
        var s = NuevoSprint(db);
        var a = Req(db, "A", sprintId: s.Id);
        var b = Req(db, "B", sprintId: s.Id);
        var c = Req(db, "C");

        // Queda {B, C}: A sale, C entra.
        var (ok, _) = Admin(db).FijarRequerimientos(s.Id, [b.Id, c.Id]);

        Assert.True(ok);
        var porId = db.Requirements.AsNoTracking().ToDictionary(r => r.Id, r => r.SprintId);
        Assert.Null(porId[a.Id]);
        Assert.Equal(s.Id, porId[b.Id]);
        Assert.Equal(s.Id, porId[c.Id]);
    }

    [Fact]
    public void FijarRequerimientos_NoDesligaLosCancelados()
    {
        // El diálogo no lista cancelados (no hay nada que seguirles), así que la selección llega
        // sin ellos — y «no venía en la lista» no puede significar «bórralo de la historia».
        var db = TestDb.New();
        var s = NuevoSprint(db);
        var vivo = Req(db, "A", sprintId: s.Id);
        var cancelado = Req(db, "C", RequirementStatus.Cancelado, sprintId: s.Id);

        Admin(db).FijarRequerimientos(s.Id, [vivo.Id]);

        Assert.Equal(s.Id, db.Requirements.AsNoTracking().Single(r => r.Id == cancelado.Id).SprintId);
    }

    [Fact]
    public void Eliminar_ConInstanciaRastreadaVieja_NoDejaHuerfanos()
    {
        // El gotcha del contexto Singleton: una pantalla dejó el requerimiento RASTREADO con
        // SprintId=null y otra máquina lo metió al sprint por fuera. Eliminar debe operar sobre
        // la verdad de la BASE (releyendo fresco), no sobre la instancia vieja — si no, el sprint
        // se borra sin desligar y la fila queda apuntando a un sprint inexistente para siempre.
        var db = TestDb.New();
        var s = NuevoSprint(db);
        var r = Req(db, "X");   // sin sprint; la instancia queda rastreada así
        db.Database.ExecuteSqlRaw($"UPDATE Requirements SET SprintId = {s.Id} WHERE Id = {r.Id}");

        var (ok, _) = Admin(db).Eliminar(s.Id);

        Assert.True(ok);
        Assert.Null(db.Requirements.AsNoTracking().Single(x => x.Id == r.Id).SprintId);
    }

    [Fact]
    public void Eliminar_RegresaLosRequerimientosAlBacklog_SinBorrarlos()
    {
        // La FK no existe en la base real (la columna se agregó por ALTER): el servicio desliga
        // a mano, y esta prueba es la que lo garantiza.
        var db = TestDb.New();
        var s = NuevoSprint(db);
        var a = Req(db, "A", sprintId: s.Id);

        var (ok, mensaje) = Admin(db).Eliminar(s.Id);

        Assert.True(ok);
        Assert.Contains("1 requerimiento", mensaje);
        Assert.Empty(db.Sprints.AsNoTracking().ToList());
        var vivo = db.Requirements.AsNoTracking().Single(r => r.Id == a.Id);
        Assert.Null(vivo.SprintId);   // sin sprint, pero VIVO
    }

    // ── Histórico y velocidad ────────────────────────────────────────────────────

    [Fact]
    public void Historico_VaDelMasViejoAlMasNuevo_YCuentaBien()
    {
        var db = TestDb.New();
        var viejo = NuevoSprint(db, "Sprint 1", DateTime.Today.AddDays(-40), DateTime.Today.AddDays(-27));
        var nuevo = NuevoSprint(db, "Sprint 2", DateTime.Today.AddDays(-5), DateTime.Today.AddDays(8));
        Req(db, "a", RequirementStatus.Entregado, sprintId: viejo.Id);
        Req(db, "b", RequirementStatus.Entregado, sprintId: viejo.Id);
        Req(db, "c", RequirementStatus.EnDesarrollo, sprintId: viejo.Id);
        Req(db, "d", RequirementStatus.Cancelado, sprintId: viejo.Id);
        Req(db, "e", RequirementStatus.EnDesarrollo, sprintId: nuevo.Id);

        var h = Admin(db).Historico();

        Assert.Equal(["Sprint 1", "Sprint 2"], h.Select(x => x.Name));   // cronológico: la gráfica se lee así
        var s1 = h[0];
        Assert.Equal(3, s1.Total);          // el cancelado NO cuenta como comprometido
        Assert.Equal(1, s1.Cancelados);     // pero se reporta aparte
        Assert.Equal(2, s1.Entregados);
        Assert.Equal(67, s1.CompletadoPct); // 2/3 redondeado lejos del cero
        Assert.True(s1.Cerrado);
        Assert.False(h[1].Cerrado);         // el que corre hoy no está cerrado
    }

    [Fact]
    public void Velocidad_SoloPromediaSprintsCerrados()
    {
        // Un sprint que empezó ayer arrastraría el promedio hacia abajo y haría creer que el
        // equipo rinde menos de lo que rinde.
        var cerrado1 = new SprintResumen(1, "S1", DateTime.Today.AddDays(-30), DateTime.Today.AddDays(-20), 5, 4, 0, 80, 11, Cerrado: true);
        var cerrado2 = new SprintResumen(2, "S2", DateTime.Today.AddDays(-19), DateTime.Today.AddDays(-9), 6, 6, 0, 100, 11, Cerrado: true);
        var enCurso  = new SprintResumen(3, "S3", DateTime.Today.AddDays(-1), DateTime.Today.AddDays(9), 6, 0, 0, 0, 11, Cerrado: false);

        var (velocidad, cuantos) = SprintService.Velocidad([cerrado1, cerrado2, enCurso]);

        Assert.Equal(5.0, velocidad);   // (4 + 6) / 2, sin el que corre
        Assert.Equal(2, cuantos);
    }

    [Fact]
    public void Velocidad_SinSprintsCerrados_EsCeroYLoDice()
    {
        var enCurso = new SprintResumen(1, "S1", DateTime.Today, DateTime.Today.AddDays(10), 4, 1, 0, 25, 11, Cerrado: false);

        var (velocidad, cuantos) = SprintService.Velocidad([enCurso]);

        Assert.Equal(0, velocidad);
        Assert.Equal(0, cuantos);   // la UI usa esto para poner «—» en vez de un 0 engañoso
    }

    [Fact]
    public void Historico_SprintVacio_NoDividePorCero()
    {
        var db = TestDb.New();
        NuevoSprint(db, "Vacío", DateTime.Today.AddDays(-20), DateTime.Today.AddDays(-10));

        var h = Assert.Single(Admin(db).Historico());

        Assert.Equal(0, h.Total);
        Assert.Equal(0, h.CompletadoPct);
    }

    [Fact]
    public void Historico_EsSoloDelAdministrador()
    {
        var db = TestDb.New();
        Assert.Throws<AuthorizationException>(() => Svc(db, Ctx.As(UserRole.Desarrollador, userId: 3)).Historico());
    }

    // ── CalcularAvance: la aritmética ────────────────────────────────────────────

    private static Sprint SprintDe(string inicio, string fin) => new()
    {
        Name = "S", StartDate = DateTime.Parse(inicio), EndDate = DateTime.Parse(fin)
    };

    private static Requirement ReqPuro(RequirementStatus st, int pct = 0) =>
        new() { Title = "r", Status = st, ProgressPercent = pct };

    [Fact]
    public void Avance_AntesDeEmpezar_TiempoCeroYVeredictoClaro()
    {
        var a = SprintService.CalcularAvance(
            SprintDe("2026-08-10", "2026-08-21"), [ReqPuro(RequirementStatus.Estimado)],
            hoyLocal: new DateTime(2026, 8, 5));

        Assert.Equal(0, a.TiempoPct);
        Assert.Equal(0, a.DiasTranscurridos);
        Assert.Equal(12, a.DiasTotales);          // extremos inclusive
        Assert.Equal("Aún no empieza", a.Veredicto);
    }

    [Fact]
    public void Avance_ExtremosInclusive_ElUltimoDiaEsCienPorCientoDeTiempo()
    {
        var s = SprintDe("2026-08-03", "2026-08-12");   // 10 días

        var primero = SprintService.CalcularAvance(s, [ReqPuro(RequirementStatus.EnDesarrollo, 50)], new DateTime(2026, 8, 3));
        Assert.Equal(1, primero.DiasTranscurridos);
        Assert.Equal(10, primero.TiempoPct);

        var ultimo = SprintService.CalcularAvance(s, [ReqPuro(RequirementStatus.EnDesarrollo, 50)], new DateTime(2026, 8, 12));
        Assert.Equal(100, ultimo.TiempoPct);
        Assert.Equal(0, ultimo.DiasRestantes);
    }

    [Fact]
    public void Avance_ElVeredictoComparaAvanceContraTiempo_ConTolerancia()
    {
        var s = SprintDe("2026-08-01", "2026-08-10");            // 10 días
        var hoy = new DateTime(2026, 8, 5);                      // tiempo = 50%

        // 50% de avance con 50% de tiempo → al día (dentro de ±10).
        Assert.Equal("Al día", SprintService.CalcularAvance(s, [ReqPuro(RequirementStatus.EnDesarrollo, 50)], hoy).Veredicto);
        // 75% → adelantado.
        Assert.Equal("Adelantado", SprintService.CalcularAvance(s, [ReqPuro(RequirementStatus.EnDesarrollo, 75)], hoy).Veredicto);
        // 20% → atrasado.
        Assert.Equal("Atrasado", SprintService.CalcularAvance(s, [ReqPuro(RequirementStatus.EnDesarrollo, 20)], hoy).Veredicto);
    }

    [Fact]
    public void Avance_AlTerminar_DistingueCompletoDeIncompleto()
    {
        var s = SprintDe("2026-07-01", "2026-07-10");
        var despues = new DateTime(2026, 7, 20);

        Assert.Equal("Terminado ✓", SprintService.CalcularAvance(
            s, [ReqPuro(RequirementStatus.Entregado)], despues).Veredicto);
        Assert.Equal("Terminó incompleto", SprintService.CalcularAvance(
            s, [ReqPuro(RequirementStatus.Entregado), ReqPuro(RequirementStatus.EnDesarrollo, 80)], despues).Veredicto);
    }

    [Fact]
    public void Avance_ElEntregadoCuentaComoCien_AunqueSuPorcentajeDigaOtraCosa()
    {
        // Un requerimiento entregado con ProgressPercent=60 capturado a medias no debe bajar el
        // avance del sprint: entregado ES cien.
        var s = SprintDe("2026-08-01", "2026-08-10");
        var a = SprintService.CalcularAvance(s,
            [ReqPuro(RequirementStatus.Entregado, 60), ReqPuro(RequirementStatus.EnDesarrollo, 0)],
            new DateTime(2026, 8, 2));

        Assert.Equal(50, a.AvanceRealPct);   // (100 + 0) / 2
        Assert.Equal(1, a.Entregados);
        Assert.Equal(1, a.EnCurso);
    }

    [Fact]
    public void Avance_LosCanceladosNoCuentan_PeroSeReportanAparte()
    {
        var s = SprintDe("2026-08-01", "2026-08-10");
        var a = SprintService.CalcularAvance(s,
            [ReqPuro(RequirementStatus.Entregado), ReqPuro(RequirementStatus.Cancelado, 30)],
            new DateTime(2026, 8, 5));

        Assert.Equal(1, a.TotalRequerimientos);
        Assert.Equal(1, a.Cancelados);
        Assert.Equal(100, a.AvanceRealPct);   // el cancelado no diluye
    }

    [Fact]
    public void Avance_SinRequerimientos_NoRevientaYLoDice()
    {
        var s = SprintDe("2026-08-01", "2026-08-10");
        var a = SprintService.CalcularAvance(s, [], new DateTime(2026, 8, 5));

        Assert.Equal(0, a.TotalRequerimientos);
        Assert.Equal(0, a.AvanceRealPct);
        Assert.Equal("Sin requerimientos", a.Veredicto);
    }

    [Fact]
    public void Avance_SprintDeUnDia_NoDividePorCero()
    {
        var s = SprintDe("2026-08-05", "2026-08-05");
        var a = SprintService.CalcularAvance(s, [ReqPuro(RequirementStatus.EnDesarrollo, 50)], new DateTime(2026, 8, 5));

        Assert.Equal(1, a.DiasTotales);
        Assert.Equal(100, a.TiempoPct);
    }

    [Fact]
    public void Avance_MedioPunto_RedondeaHaciaArriba()
    {
        // Math.Round por omisión redondea al par (60.5 → 60): un avance que «baja» respecto de lo
        // capturado se reporta como error. AwayFromZero lo deja en 61.
        var s = SprintDe("2026-08-01", "2026-08-10");
        var a = SprintService.CalcularAvance(s,
            [ReqPuro(RequirementStatus.EnDesarrollo, 60), ReqPuro(RequirementStatus.EnDesarrollo, 61)],
            new DateTime(2026, 8, 5));

        Assert.Equal(61, a.AvanceRealPct);
    }

    [Fact]
    public void Avance_PorcentajesFueraDeRango_SeAcotan()
    {
        // ProgressPercent es un int capturable a mano en otras pantallas: un 150 o un -20 no
        // pueden torcer el promedio del sprint.
        var s = SprintDe("2026-08-01", "2026-08-10");
        var a = SprintService.CalcularAvance(s,
            [ReqPuro(RequirementStatus.EnDesarrollo, 150), ReqPuro(RequirementStatus.EnDesarrollo, -20)],
            new DateTime(2026, 8, 5));

        Assert.Equal(50, a.AvanceRealPct);   // (100 + 0) / 2, acotados
    }
}
