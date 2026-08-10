using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Asistencia oficial: la que cada quien marca a mano.
///
/// Lo delicado aquí son las HORAS y de dónde salen. Una salida olvidada que se cerrara «ahora» le
/// regalaría a alguien la noche entera; una que el desarrollador pudiera teclear dejaría de probar
/// nada. Casi todas estas pruebas miran eso, no la mecánica del botón.
/// </summary>
public class AttendanceServiceTests
{
    private static AttendanceService Svc(AppDbContext db, CurrentUserContext cu) =>
        new(db, cu, new AuditService(db, cu));

    private static CurrentUserContext Usuario(AppDbContext db, int userId, string nombre,
        UserRole rol = UserRole.Desarrollador)
    {
        db.Users.Add(new User
        {
            Id = userId, Username = nombre.ToLowerInvariant(), FullName = nombre,
            Role = rol, IsActive = true, PasswordHash = "x"
        });
        db.SaveChanges();

        var cu = new CurrentUserContext();
        cu.SetUser(db.Users.AsNoTracking().Single(u => u.Id == userId));
        return cu;
    }

    /// <summary>Mueve una entrada al pasado para simular el día anterior.</summary>
    private static void EnvejecerEntrada(AppDbContext db, int registroId, TimeSpan cuanto)
    {
        var r = db.AttendanceRecords.Single(a => a.Id == registroId);
        r.CheckInUtc = r.CheckInUtc - cuanto;
        db.SaveChanges();
        db.Entry(r).State = EntityState.Detached;
    }

    /// <summary>Siembra telemetría (jornada automática ya cerrada) para un momento dado.</summary>
    private static void SembrarTelemetria(AppDbContext db, int userId, DateTime inicioUtc, DateTime finUtc)
    {
        db.WorkPresences.Add(new WorkPresence
        {
            UserId = userId, DisplayName = "x",
            StartedAtUtc = inicioUtc, LastSeenUtc = finUtc, EndedAtUtc = finUtc,
            EndReason = PresenceEnd.CierreNormal
        });
        db.SaveChanges();
    }

    // ── Marcar ───────────────────────────────────────────────────────────────────

    [Fact]
    public void MarcarEntrada_AbreElRegistroConHoraUtcYOrigen()
    {
        var db = TestDb.New();
        var (ok, _) = Svc(db, Usuario(db, 1, "Ana")).MarcarEntrada();

        Assert.True(ok);
        var r = db.AttendanceRecords.Single();
        Assert.Equal(1, r.UserId);
        Assert.Equal("Ana", r.DisplayName);
        Assert.True(r.Abierto);
        Assert.Null(r.CloseKind);
        Assert.False(string.IsNullOrWhiteSpace(r.CheckInOrigin));
        Assert.True((DateTime.UtcNow - r.CheckInUtc).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void LasMarcasSeGuardanEnUtc()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.MarcarEntrada();
        svc.MarcarSalida();

        var r = db.AttendanceRecords.Single();
        // Si se guardara la hora local, en un huso distinto de UTC la diferencia sería de horas.
        Assert.True((DateTime.UtcNow - r.CheckInUtc).Duration() < TimeSpan.FromMinutes(1));
        Assert.True((DateTime.UtcNow - r.CheckOutUtc!.Value).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void MarcarEntrada_ConEntradaAbiertaHoy_SeRechaza()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.MarcarEntrada();

        var (ok, mensaje) = svc.MarcarEntrada();

        Assert.False(ok);
        Assert.Contains("Ya marcaste tu entrada", mensaje);
        Assert.Single(db.AttendanceRecords);
    }

    [Fact]
    public void MarcarEntrada_ConSalidaYaMarcadaHoy_SeRechaza()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.MarcarEntrada();
        svc.MarcarSalida();

        // Un solo registro oficial por día: la reentrada tras comer no se marca.
        var (ok, mensaje) = svc.MarcarEntrada();

        Assert.False(ok);
        Assert.Contains("ya quedó marcada", mensaje);
        Assert.Single(db.AttendanceRecords);
    }

    [Fact]
    public void MarcarSalida_CierraElRegistroComoManual()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.MarcarEntrada();

        var (ok, _) = svc.MarcarSalida();

        Assert.True(ok);
        var r = db.AttendanceRecords.AsNoTracking().Single();
        Assert.False(r.Abierto);
        Assert.Equal(AttendanceCloseKind.Manual, r.CloseKind);
        Assert.NotNull(r.Duracion);
    }

    [Fact]
    public void MarcarSalida_SinEntradaAbierta_SeRechaza()
    {
        var db = TestDb.New();

        var (ok, mensaje) = Svc(db, Usuario(db, 1, "Ana")).MarcarSalida();

        Assert.False(ok);
        Assert.Contains("marca primero tu entrada", mensaje);
        Assert.Empty(db.AttendanceRecords);
    }

    [Fact]
    public void MarcarSalida_DosVeces_LaSegundaSeRechaza()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));
        svc.MarcarEntrada();
        svc.MarcarSalida();

        var (ok, _) = svc.MarcarSalida();

        Assert.False(ok);
    }

    // ── Olvidos: de dónde sale la hora de cierre ─────────────────────────────────

    [Fact]
    public void Olvido_SeCierraConLaUltimaSenalAutomaticaDeEseDia_NoConLaHoraDeAhora()
    {
        var db = TestDb.New();
        var cu = Usuario(db, 1, "Ana");
        var svc = Svc(db, cu);

        svc.MarcarEntrada();
        int id = db.AttendanceRecords.AsNoTracking().Single().Id;

        // La entrada olvidada se ancla a un día PASADO concreto y a una hora concreta, en vez de
        // restarle veinticuatro horas a «ahora». Con el desplazamiento relativo, una ejecución que
        // arranque de madrugada coloca la entrada y la telemetría en días distintos y la prueba falla
        // sin que nada esté mal: el defecto estaba en la prueba, no en el servicio.
        var dia = DateTime.Now.Date.AddDays(-3);
        var entrada = dia.AddHours(9);
        EnvejecerEntrada(db, id, DateTime.UtcNow - entrada.ToUniversalTime());

        // La telemetría vio a Ana hasta las 18:00 de ese día.
        var salidaReal = dia.AddHours(18).ToUniversalTime();
        SembrarTelemetria(db, 1, entrada.ToUniversalTime(), salidaReal);

        svc.MarcarEntrada();   // la de hoy dispara el cierre del olvido

        var olvidado = db.AttendanceRecords.AsNoTracking().Single(a => a.Id == id);
        Assert.Equal(AttendanceCloseKind.Olvido, olvidado.CloseKind);
        Assert.Equal(salidaReal, olvidado.CheckOutUtc!.Value, TimeSpan.FromSeconds(1));
        // Lo que importa: NO se le regaló la noche.
        Assert.True(olvidado.CheckOutUtc < DateTime.UtcNow.AddHours(-1));
    }

    [Fact]
    public void Olvido_SinTelemetria_SeCierraConDuracionCero_YQuedaMarcado()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));

        svc.MarcarEntrada();
        int id = db.AttendanceRecords.AsNoTracking().Single().Id;
        EnvejecerEntrada(db, id, TimeSpan.FromDays(2));

        svc.MarcarEntrada();

        var olvidado = db.AttendanceRecords.AsNoTracking().Single(a => a.Id == id);
        Assert.Equal(AttendanceCloseKind.Olvido, olvidado.CloseKind);
        // Duración cero: inconfundible con una jornada real, obliga a que el líder la corrija.
        Assert.Equal(TimeSpan.Zero, olvidado.Duracion);
    }

    [Fact]
    public void MarcarSalida_ConEntradaDeOtroDia_LaCierraComoOlvidoYNoLaCobraHoy()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));

        svc.MarcarEntrada();
        int id = db.AttendanceRecords.AsNoTracking().Single().Id;
        EnvejecerEntrada(db, id, TimeSpan.FromDays(1));

        var (ok, mensaje) = svc.MarcarSalida();

        Assert.False(ok);
        Assert.Contains("olvido", mensaje, StringComparison.OrdinalIgnoreCase);
        var r = db.AttendanceRecords.AsNoTracking().Single(a => a.Id == id);
        Assert.Equal(AttendanceCloseKind.Olvido, r.CloseKind);
        Assert.True(r.CheckOutUtc < DateTime.UtcNow.AddHours(-1));
    }

    [Fact]
    public void TurnoQueCruzaLaMedianoche_SePuedeCerrarNormalmente()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));

        // Entró anoche y sale de madrugada: es otro día del calendario, pero la misma jornada.
        svc.MarcarEntrada();
        int id = db.AttendanceRecords.AsNoTracking().Single().Id;
        EnvejecerEntrada(db, id, TimeSpan.FromHours(5));

        var (ok, _) = svc.MarcarSalida();

        Assert.True(ok);
        var r = db.AttendanceRecords.AsNoTracking().Single();
        // Su salida es real, no una estimación: decidir por fecha la habría tirado.
        Assert.Equal(AttendanceCloseKind.Manual, r.CloseKind);
        Assert.True(r.Duracion > TimeSpan.FromHours(4));
    }

    [Fact]
    public void Olvido_ConSenalAnteriorALaEntrada_NoDaDuracionNegativa()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));

        svc.MarcarEntrada();
        int id = db.AttendanceRecords.AsNoTracking().Single().Id;
        EnvejecerEntrada(db, id, TimeSpan.FromDays(1));

        // Tuvo la aplicación abierta por la mañana y marcó su entrada por la tarde: la última señal
        // del día es ANTERIOR a la marca.
        var ayer = DateTime.Now.Date.AddDays(-1);
        var entradaLocal = db.AttendanceRecords.AsNoTracking().Single().CheckInUtc.ToLocalTime();
        SembrarTelemetria(db, 1, ayer.AddHours(6).ToUniversalTime(), entradaLocal.AddHours(-1).ToUniversalTime());

        svc.MarcarEntrada();

        var r = db.AttendanceRecords.AsNoTracking().Single(a => a.Id == id);
        Assert.Equal(AttendanceCloseKind.Olvido, r.CloseKind);
        Assert.True(r.Duracion >= TimeSpan.Zero, "una jornada no puede durar menos que nada");
    }

    [Fact]
    public void DosRegistrosConsecutivosQueSoloSeTocan_SeAceptan()
    {
        var db = TestDb.New();
        Usuario(db, 1, "Ana");
        var jefe = Usuario(db, 9, "Jefe", UserRole.Admin);
        var svc = Svc(db, jefe);

        var dia = DateTime.Today.AddDays(-1);
        Assert.True(svc.CrearRegistroManual(1, dia.AddHours(8), dia.AddHours(14), "mañana").ok);

        // El segundo empieza justo donde acabó el primero: no se encima con nada.
        var (ok, mensaje) = svc.CrearRegistroManual(1, dia.AddHours(14), dia.AddHours(18), "tarde");

        Assert.True(ok, mensaje);
    }

    [Fact]
    public void CrearRegistroManual_SobreUnaJornadaAbierta_SeRechaza()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var jefe = Usuario(db, 9, "Jefe", UserRole.Admin);
        Svc(db, ana).MarcarEntrada();   // queda abierta ahora mismo

        // El alta también queda abierta, así que se extiende sobre la jornada en curso: ninguna de
        // las dos es un instante y encimarlas dejaría dos jornadas abiertas de la misma persona.
        var (ok, mensaje) = Svc(db, jefe)
            .CrearRegistroManual(1, DateTime.Now.AddMinutes(-30), null, "alta encimada");

        Assert.False(ok);
        Assert.Contains("encima", mensaje);
    }

    // ── Lo propio y lo ajeno ─────────────────────────────────────────────────────

    [Fact]
    public void MisRegistros_SoloDevuelveLosPropios()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var beto = Usuario(db, 2, "Beto");
        Svc(db, ana).MarcarEntrada();
        Svc(db, beto).MarcarEntrada();

        var mios = Svc(db, ana).MisRegistros(DateTime.Today, DateTime.Today);

        Assert.Single(mios);
        Assert.Equal(1, mios[0].UserId);
    }

    [Fact]
    public void SinSesion_MarcarLanzaAuthorizationException()
    {
        var db = TestDb.New();
        var svc = Svc(db, Ctx.Anonymous());

        Assert.Throws<AuthorizationException>(() => svc.MarcarEntrada());
        Assert.Throws<AuthorizationException>(() => svc.MarcarSalida());
    }

    [Fact]
    public void SolicitarCorreccion_SobreRegistroAjeno_SeRechaza()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var beto = Usuario(db, 2, "Beto");
        Svc(db, ana).MarcarEntrada();
        int idDeAna = db.AttendanceRecords.AsNoTracking().Single().Id;

        Assert.Throws<AuthorizationException>(() =>
            Svc(db, beto).SolicitarCorreccion(idDeAna, "no era mi horario"));
    }

    [Fact]
    public void SolicitarCorreccion_ExigeMotivo_YLoGuarda()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var svc = Svc(db, ana);
        svc.MarcarEntrada();
        int id = db.AttendanceRecords.AsNoTracking().Single().Id;

        Assert.False(svc.SolicitarCorreccion(id, "   ").ok);

        var (ok, _) = svc.SolicitarCorreccion(id, "entré a las 8, no a las 9");
        Assert.True(ok);
        var r = db.AttendanceRecords.AsNoTracking().Single();
        Assert.NotNull(r.CorrectionRequestedAtUtc);
        Assert.Equal("entré a las 8, no a las 9", r.CorrectionRequestNote);
    }

    // ── Tablero del administrador ────────────────────────────────────────────────

    [Fact]
    public void AsistenciaDelDia_RequiereAdmin()
    {
        var db = TestDb.New();
        var svc = Svc(db, Usuario(db, 1, "Ana"));

        Assert.Throws<AuthorizationException>(() => svc.AsistenciaDelDia(DateTime.Today));
    }

    [Fact]
    public void AsistenciaDelDia_CruzaOficialConTelemetria_YCalculaLosDeltas()
    {
        var db = TestDb.New();
        Usuario(db, 1, "Ana");
        var jefe = Usuario(db, 9, "Jefe", UserRole.Admin);

        // Un día ya cerrado, con horas fijas: así la prueba no depende de a qué hora se ejecute.
        // Con «hace media hora» sobre la hora actual, una corrida pasada la medianoche siembra la
        // telemetría en el día local ANTERIOR y el cruce dejaría de encontrarla.
        var dia = DateTime.Today.AddDays(-1);
        Assert.True(Svc(db, jefe).CrearRegistroManual(1, dia.AddHours(9), dia.AddHours(18), "alta de prueba").ok);

        // La máquina la vio media hora antes de que marcara, y hasta cinco minutos después de irse.
        SembrarTelemetria(db, 1, dia.AddHours(8.5).ToUniversalTime(), dia.AddHours(18).AddMinutes(5).ToUniversalTime());

        var fila = Svc(db, jefe).AsistenciaDelDia(dia).Single(f => f.UserId == 1);

        Assert.NotNull(fila.EntradaOficialUtc);
        Assert.NotNull(fila.PrimeraSenalAutoUtc);
        Assert.NotNull(fila.DeltaEntrada);
        Assert.True(fila.DeltaEntrada!.Value > TimeSpan.FromMinutes(25));
        Assert.True(fila.HayDiscrepancia);
    }

    [Fact]
    public void AsistenciaDelDia_SenalaAQuienTuvoTelemetriaPeroNoMarco()
    {
        var db = TestDb.New();
        Usuario(db, 1, "Ana");
        var jefe = Usuario(db, 9, "Jefe", UserRole.Admin);

        var dia = DateTime.Today.AddDays(-1);
        SembrarTelemetria(db, 1, dia.AddHours(9).ToUniversalTime(), dia.AddHours(17).ToUniversalTime());

        var fila = Svc(db, jefe).AsistenciaDelDia(dia).Single(f => f.UserId == 1);

        Assert.Null(fila.RegistroId);
        Assert.True(fila.SinMarcar);
    }

    // ── Correcciones del líder ───────────────────────────────────────────────────

    [Fact]
    public void CorregirRegistro_RequiereAdminYExigeMotivo()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var jefe = Usuario(db, 9, "Jefe", UserRole.Admin);
        Svc(db, ana).MarcarEntrada();
        int id = db.AttendanceRecords.AsNoTracking().Single().Id;

        Assert.Throws<AuthorizationException>(() =>
            Svc(db, ana).CorregirRegistro(id, DateTime.Now.AddHours(-3), DateTime.Now.AddHours(-1), "porque sí"));

        Assert.False(Svc(db, jefe).CorregirRegistro(id, DateTime.Now.AddHours(-3), null, "  ").ok);
    }

    [Fact]
    public void CorregirRegistro_SalidaAntesDeEntrada_SeRechaza()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var jefe = Usuario(db, 9, "Jefe", UserRole.Admin);
        Svc(db, ana).MarcarEntrada();
        int id = db.AttendanceRecords.AsNoTracking().Single().Id;

        var (ok, mensaje) = Svc(db, jefe)
            .CorregirRegistro(id, DateTime.Now.AddHours(-1), DateTime.Now.AddHours(-3), "dedazo");

        Assert.False(ok);
        Assert.Contains("posterior", mensaje);
    }

    [Fact]
    public void CorregirRegistro_EnElFuturo_SeRechaza()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var jefe = Usuario(db, 9, "Jefe", UserRole.Admin);
        Svc(db, ana).MarcarEntrada();
        int id = db.AttendanceRecords.AsNoTracking().Single().Id;

        var (ok, mensaje) = Svc(db, jefe)
            .CorregirRegistro(id, DateTime.Now.AddHours(2), DateTime.Now.AddHours(4), "adelanto");

        Assert.False(ok);
        Assert.Contains("futuro", mensaje);
    }

    [Fact]
    public void CorregirRegistro_GuardaRastroEnLaFila_YViejoYNuevoEnLaBitacora()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var jefe = Usuario(db, 9, "Jefe", UserRole.Admin);
        var svcAna = Svc(db, ana);
        svcAna.MarcarEntrada();
        int id = db.AttendanceRecords.AsNoTracking().Single().Id;
        svcAna.SolicitarCorreccion(id, "entré antes");

        var entrada = DateTime.Now.AddHours(-8);
        var salida  = DateTime.Now.AddHours(-1);
        var (ok, _) = Svc(db, jefe).CorregirRegistro(id, entrada, salida, "llegó a las 8, lo confirmé");

        Assert.True(ok);
        var r = db.AttendanceRecords.AsNoTracking().Single();
        Assert.Equal(AttendanceCloseKind.Admin, r.CloseKind);
        Assert.Equal(9, r.CorrectedByUserId);
        Assert.NotNull(r.CorrectedAtUtc);
        Assert.Equal("llegó a las 8, lo confirmé", r.CorrectionReason);
        // La solicitud queda atendida, no repitiendo la petición para siempre.
        Assert.Null(r.CorrectionRequestedAtUtc);

        var log = db.AuditLogs.AsNoTracking()
            .Where(l => l.EntityType == "Asistencia" && l.OldValues != null)
            .OrderByDescending(l => l.Id).First();
        Assert.Contains("Entrada", log.OldValues!);
        Assert.Contains("Entrada", log.NewValues!);
    }

    [Fact]
    public void CrearRegistroManual_RequiereAdmin_YQuedaConRastro()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var jefe = Usuario(db, 9, "Jefe", UserRole.Admin);

        Assert.Throws<AuthorizationException>(() =>
            Svc(db, ana).CrearRegistroManual(1, DateTime.Now.AddHours(-5), DateTime.Now.AddHours(-1), "trabajó sin la app"));

        var (ok, _) = Svc(db, jefe)
            .CrearRegistroManual(1, DateTime.Now.AddHours(-5), DateTime.Now.AddHours(-1), "trabajó sin la app");

        Assert.True(ok);
        var r = db.AttendanceRecords.AsNoTracking().Single();
        Assert.Equal(1, r.UserId);
        Assert.Equal("Ana", r.DisplayName);
        Assert.Equal(AttendanceCloseKind.Admin, r.CloseKind);
        Assert.Equal("trabajó sin la app", r.CorrectionReason);
    }

    [Fact]
    public void CrearRegistroManual_QueSeEncimaConOtro_SeRechaza()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var jefe = Usuario(db, 9, "Jefe", UserRole.Admin);
        var svcJefe = Svc(db, jefe);
        svcJefe.CrearRegistroManual(1, DateTime.Now.AddHours(-5), DateTime.Now.AddHours(-1), "primero");

        var (ok, mensaje) = svcJefe.CrearRegistroManual(1, DateTime.Now.AddHours(-3), DateTime.Now.AddHours(-2), "encimado");

        Assert.False(ok);
        Assert.Contains("encima", mensaje);
    }

    [Fact]
    public void CorreccionesPendientes_CuentaSoloLasQueEsperanRespuesta()
    {
        var db = TestDb.New();
        var ana = Usuario(db, 1, "Ana");
        var jefe = Usuario(db, 9, "Jefe", UserRole.Admin);
        var svcAna = Svc(db, ana);
        svcAna.MarcarEntrada();
        int id = db.AttendanceRecords.AsNoTracking().Single().Id;

        Assert.Equal(0, Svc(db, jefe).CorreccionesPendientes());

        svcAna.SolicitarCorreccion(id, "revisen mi hora");
        Assert.Equal(1, Svc(db, jefe).CorreccionesPendientes());

        Svc(db, jefe).CorregirRegistro(id, DateTime.Now.AddHours(-4), null, "corregido");
        Assert.Equal(0, Svc(db, jefe).CorreccionesPendientes());
    }
}
