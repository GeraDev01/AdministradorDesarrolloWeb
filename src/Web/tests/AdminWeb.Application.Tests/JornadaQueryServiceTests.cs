using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// «Mi jornada»: lo que la pantalla recibe de una sola vez.
///
/// Esta consulta no decide nada de negocio —eso lo siguen haciendo <see cref="AttendanceService"/> y
/// <see cref="WorkSessionService"/>—, así que lo que se prueba aquí es distinto: que lo que se junta
/// sea lo que la pantalla necesita para no equivocarse.
///
/// Lo más delicado es el CRONÓMETRO. En el escritorio el contador corría contra el reloj del propio
/// equipo, que era el mismo que escribía en la base. En la web son dos relojes distintos: el del
/// servidor, que es el que manda, y el del navegador, que puede estar desajustado —el de un portátil
/// recién despertado suele estarlo—. Por eso el DTO lleva la hora del servidor: sin ella, la
/// pantalla enseñaría tiempo que nadie trabajó.
/// </summary>
public class JornadaQueryServiceTests
{
    private static JornadaQueryService Svc(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, new AttendanceService(db, cu, new AuditService(db, cu, new OrigenDePrueba()), new OrigenDePrueba()),
            new PresenceService(db, cu, new OrigenDePrueba()));

    private static ICurrentUser Sembrar(AppDbContext db, int userId = 1, int? devId = 7,
        UserRole rol = UserRole.Desarrollador)
    {
        db.Users.Add(new User
        {
            Id = userId, Username = "ana", FullName = "Ana", Role = rol, IsActive = true, PasswordHash = "x"
        });
        if (devId is int d)
            db.Developers.Add(new Developer { Id = d, FullName = "Ana", IsActive = true });
        db.SaveChanges();

        return new UsuarioDePrueba
        {
            UserId = userId, Username = "ana", FullName = "Ana", Role = rol, DeveloperId = devId
        };
    }

    // ── El marcaje del día ───────────────────────────────────────────────────────

    [Fact]
    public async Task SinHaberMarcadoNada_OfreceMarcarEntrada()
    {
        using var db = TestDb.New();
        var jornada = Svc(db, Sembrar(db));

        var mia = await jornada.MiJornadaAsync();

        Assert.Null(mia.EntradaUtc);
        Assert.True(mia.PuedeMarcarEntrada);
        Assert.Empty(mia.Historial);
    }

    [Fact]
    public async Task ConLaEntradaAbierta_YaNoOfreceMarcarla()
    {
        using var db = TestDb.New();
        var cu = Sembrar(db);
        var asistencia = new AttendanceService(db, cu, new AuditService(db, cu, new OrigenDePrueba()), new OrigenDePrueba());
        await asistencia.MarcarEntradaAsync();

        var mia = await Svc(db, cu).MiJornadaAsync();

        Assert.NotNull(mia.EntradaUtc);
        Assert.False(mia.PuedeMarcarEntrada);
    }

    [Fact]
    public async Task ConLaJornadaDeHoyYaCerrada_NoDejaAbrirOtra()
    {
        // Sin esto, quien se equivoca al marcar su salida abriría una segunda jornada del mismo día
        // en vez de pedir la corrección, que es lo que debe hacer. El registro dejaría de cuadrar.
        using var db = TestDb.New();
        var cu = Sembrar(db);
        var asistencia = new AttendanceService(db, cu, new AuditService(db, cu, new OrigenDePrueba()), new OrigenDePrueba());
        await asistencia.MarcarEntradaAsync();
        await asistencia.MarcarSalidaAsync();

        var mia = await Svc(db, cu).MiJornadaAsync();

        Assert.Null(mia.EntradaUtc);
        Assert.False(mia.PuedeMarcarEntrada);
        Assert.Single(mia.Historial);
    }

    [Fact]
    public async Task ElBotonDeLaBarra_DiceLoMismoQueLaPantallaCompleta()
    {
        // Son dos consultas distintas —la del botón no trae el historial— y tienen que coincidir: si
        // discreparan, el botón diría «marcar entrada» sobre una jornada ya abierta.
        using var db = TestDb.New();
        var cu = Sembrar(db);
        var jornada = Svc(db, cu);
        var asistencia = new AttendanceService(db, cu, new AuditService(db, cu, new OrigenDePrueba()), new OrigenDePrueba());
        await asistencia.MarcarEntradaAsync();

        var completa = await jornada.MiJornadaAsync();
        var boton = await jornada.EstadoDeMarcajeAsync();

        Assert.Equal(completa.EntradaUtc, boton.EntradaUtc);
        Assert.Equal(completa.PuedeMarcarEntrada, boton.PuedeMarcarEntrada);
        Assert.True(boton.DentroDeJornada);
    }

    [Fact]
    public async Task LaCorreccionPedida_SeVeEnElHistorial()
    {
        using var db = TestDb.New();
        var cu = Sembrar(db);
        var asistencia = new AttendanceService(db, cu, new AuditService(db, cu, new OrigenDePrueba()), new OrigenDePrueba());
        await asistencia.MarcarEntradaAsync();
        await asistencia.MarcarSalidaAsync();
        int id = db.AttendanceRecords.Single().Id;
        await asistencia.SolicitarCorreccionAsync(id, "Salí a las 18:00, no a las 17:00.");

        var mia = await Svc(db, cu).MiJornadaAsync();

        var dia = Assert.Single(mia.Historial);
        Assert.True(dia.CorreccionSolicitada);
        Assert.Contains("18:00", dia.NotaCorreccion);
    }

    // ── El cronómetro ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElCronometroActivo_ViajaConLaHoraDelServidor()
    {
        // La razón de que AhoraUtc exista: el navegador cuenta desde ella y no desde su propio reloj.
        using var db = TestDb.New();
        var cu = Sembrar(db);
        db.DevActivities.Add(new DevActivity { Id = 3, DeveloperId = 7, Title = "Revisar el informe" });
        db.SaveChanges();

        var cronometros = new WorkSessionService(db, cu, new AuditService(db, cu, new OrigenDePrueba()));
        await cronometros.StartOrResumeAsync(7, WorkTarget.Actividad(3));

        var c = await Svc(db, cu).CronometroAsync();

        Assert.NotNull(c);
        Assert.Equal(3, c!.ActividadId);
        Assert.Null(c.RequerimientoId);
        Assert.Contains("Revisar el informe", c.Titulo);
        Assert.InRange(c.AhoraUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task UnCronometroPausado_NoSeEnsenaComoSiCorriera()
    {
        // Enseñar un contador que avanza sobre una sesión pausada sería mentir sobre el tiempo que se
        // está registrando, que es exactamente el dato por el que existe esta pantalla.
        using var db = TestDb.New();
        var cu = Sembrar(db);
        db.DevActivities.Add(new DevActivity { Id = 3, DeveloperId = 7, Title = "Algo" });
        db.SaveChanges();

        var cronometros = new WorkSessionService(db, cu, new AuditService(db, cu, new OrigenDePrueba()));
        await cronometros.StartOrResumeAsync(7, WorkTarget.Actividad(3));
        await cronometros.PauseAsync(7, WorkTarget.Actividad(3));

        Assert.Null(await Svc(db, cu).CronometroAsync());
    }

    [Fact]
    public async Task UnaCuentaSinFichaDeDesarrollador_NoTieneCronometroPeroSiJornada()
    {
        // Lo que esta prueba cubre —que la pantalla funcione para una cuenta SIN ficha de
        // desarrollador, en vez de reventar— sigue importando: la jornada es de la CUENTA, y el líder
        // no tiene ficha y marca igual. Lo que se esconde solo cuando no hay ficha es el cronómetro,
        // que cuenta trabajo de desarrollo.
        //
        // La cuenta era de Operaciones y ya no puede serlo: Operaciones dejó de registrar jornada y
        // aquí lanzaría. No se ablanda la prueba, se le cambia el sujeto por el otro caso real de
        // cuenta sin ficha, que es el del líder.
        using var db = TestDb.New();
        var cu = Sembrar(db, devId: null, rol: UserRole.Admin);

        var mia = await Svc(db, cu).MiJornadaAsync();

        Assert.Null(mia.Cronometro);
        Assert.True(mia.PuedeMarcarEntrada);
    }

    // ── Lo cronometrado por día ──────────────────────────────────────────────────

    [Fact]
    public async Task ElHistorial_TraeLoCronometradoDeCadaDia()
    {
        // Es el contraste que hace útil la tabla: lo marcado frente a lo cronometrado.
        using var db = TestDb.New();
        var cu = Sembrar(db);
        var asistencia = new AttendanceService(db, cu, new AuditService(db, cu, new OrigenDePrueba()), new OrigenDePrueba());
        await asistencia.MarcarEntradaAsync();
        await asistencia.MarcarSalidaAsync();

        db.WorkIntervals.Add(new WorkInterval
        {
            DeveloperId = 7, ActivityId = 3,
            StartUtc = DateTime.UtcNow.AddHours(-3), EndUtc = DateTime.UtcNow.AddHours(-1),
            Seconds = 7200, LocalDate = DateTime.Today
        });
        db.SaveChanges();

        var mia = await Svc(db, cu).MiJornadaAsync();

        Assert.Equal(7200, Assert.Single(mia.Historial).SegundosCronometrados);
    }

    [Fact]
    public async Task LoCronometradoPorOtraPersona_NoSeMezcla()
    {
        using var db = TestDb.New();
        var cu = Sembrar(db);
        var asistencia = new AttendanceService(db, cu, new AuditService(db, cu, new OrigenDePrueba()), new OrigenDePrueba());
        await asistencia.MarcarEntradaAsync();
        await asistencia.MarcarSalidaAsync();

        db.WorkIntervals.Add(new WorkInterval
        {
            DeveloperId = 99, ActivityId = 1,
            StartUtc = DateTime.UtcNow.AddHours(-3), EndUtc = DateTime.UtcNow.AddHours(-1),
            Seconds = 7200, LocalDate = DateTime.Today
        });
        db.SaveChanges();

        var mia = await Svc(db, cu).MiJornadaAsync();

        Assert.Equal(0, Assert.Single(mia.Historial).SegundosCronometrados);
    }

    // ── Sin sesión ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SinSesion_NoDevuelveNada()
    {
        using var db = TestDb.New();
        var jornada = Svc(db, UsuarioDePrueba.Anonimo());

        await Assert.ThrowsAsync<AuthorizationException>(() => jornada.MiJornadaAsync());
    }
}
