using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Programados;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// La agenda de despliegues programados.
///
/// <para>Lo que estas pruebas cuidan es la promesa del sistema: que un despliegue agendado se
/// ejecute <b>exactamente una vez</b> y no dos, que uno cuya hora ya pasó de largo no se dispare a
/// deshora, y que quien no puede desplegar a un perfil tampoco pueda agendarlo.</para>
///
/// <para><b>La toma atómica es lo principal.</b> Hoy hay una sola instancia de la API y podría
/// parecer que no hace falta probarla; se prueba justamente por eso — el día que haya dos, o durante
/// un despliegue de la propia API en que la nueva ya arrancó y la vieja no ha muerto, es cuando el
/// fallo aparecería, y ahí ya sería un despliegue duplicado en producción. Dos servicios con su
/// propio contexto contra la MISMA base es lo más cerca que se puede estar de ese escenario sin
/// levantar dos servidores.</para>
/// </summary>
public class ProgramadosServiceTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var ctx in _contextos) ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Andamiaje ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Otro contexto contra la misma base. Es lo que permite simular dos ejecutores distintos: cada
    /// uno ve la base, no lo que quedó rastreado en el contexto del otro.
    /// </summary>
    private AppDbContext OtroContexto(AppDbContext db)
    {
        var opciones = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Database.GetConnectionString()).Options;
        var ctx = new AppDbContext(opciones);
        _contextos.Add(ctx);
        return ctx;
    }

    private ProgramadosService Servicio(AppDbContext db, ICurrentUser quien, IEjecutorDeDespliegues? ejecutor = null)
    {
        var ctx = OtroContexto(db);
        return new ProgramadosService(ctx, quien, new AuditService(ctx, quien, new OrigenDePrueba()), ejecutor);
    }

    private static UsuarioDePrueba Admin(int userId = 9) => UsuarioDePrueba.Como(UserRole.Admin, userId: userId);
    private static UsuarioDePrueba Operaciones(int userId = 3) =>
        UsuarioDePrueba.Como(UserRole.Operaciones, userId: userId);

    /// <summary>Base con un sistema, una versión y un perfil listos para agendar.</summary>
    private static (AppDbContext db, int versionId, int perfilId) BaseConVersionYPerfil(
        bool perfilParaOperaciones = true)
    {
        var db = TestDb.New();

        var sistema = new AppSystem { Name = "Portal", IsActive = true };
        db.AppSystems.Add(sistema);
        db.SaveChanges();

        var version = new AppRelease { AppSystemId = sistema.Id, Version = "1.0.0", CreatedAt = DateTime.UtcNow };
        db.AppReleases.Add(version);

        var perfil = new DeploymentProfile { Name = "Productivo", AllowedForOperaciones = perfilParaOperaciones };
        db.DeploymentProfiles.Add(perfil);
        db.SaveChanges();

        return (db, version.Id, perfil.Id);
    }

    /// <summary>Deja una cita ya vencida en la base, sin pasar por el servicio.</summary>
    private static int SembrarCita(AppDbContext db, int versionId, int perfilId,
        DateTime cuandoUtc, int toleranciaMinutos = 60,
        ScheduledDeploymentStatus estado = ScheduledDeploymentStatus.Programado,
        int? creadaPor = null)
    {
        var cita = new ScheduledDeployment
        {
            AppReleaseId = versionId,
            DeploymentProfileId = perfilId,
            ScheduledAtUtc = cuandoUtc,
            ToleranciaMinutos = toleranciaMinutos,
            Status = estado,
            CreatedByUserId = creadaPor,
            CreatedAt = cuandoUtc.AddHours(-2)
        };
        db.ScheduledDeployments.Add(cita);
        db.SaveChanges();
        return cita.Id;
    }

    /// <summary>Un ejecutor de mentira: cuenta cuántas veces lo llamaron y con qué evidencia.</summary>
    private sealed class EjecutorDePrueba(JobStatus resultado = JobStatus.Completado) : IEjecutorDeDespliegues
    {
        public int Llamadas;
        public string? UltimaEvidencia;
        public int UltimoPerfil;

        public Task<ResultadoDeDespliegue> DesplegarAsync(
            int versionId, int perfilId, string evidenciaDeChecklist, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Llamadas);
            UltimaEvidencia = evidenciaDeChecklist;
            UltimoPerfil = perfilId;
            return Task.FromResult(new ResultadoDeDespliegue(77, resultado, 2, 0));
        }

        public Task<int> CrearPerfilCongeladoAsync(
            IReadOnlyList<int> servidorIds, string etiqueta, CancellationToken ct = default) =>
            Task.FromResult(-1);
    }

    // ── La toma atómica ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Dos_ejecutores_no_se_llevan_la_misma_cita()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();
        SembrarCita(db, versionId, perfilId, DateTime.UtcNow.AddMinutes(-5));

        // Dos servicios, cada uno con su contexto: es el escenario de dos instancias de la API.
        var primero = Servicio(db, Admin());
        var segundo = Servicio(db, Admin());

        var tomadaPorElPrimero = await primero.TomarSiguienteAsync();
        var tomadaPorElSegundo = await segundo.TomarSiguienteAsync();

        Assert.NotNull(tomadaPorElPrimero);
        Assert.Null(tomadaPorElSegundo);   // la base decidió: solo uno afectó una fila
    }

    [Fact]
    public async Task Tomar_deja_constancia_de_quien_se_la_llevo()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();
        int citaId = SembrarCita(db, versionId, perfilId, DateTime.UtcNow.AddMinutes(-5));

        await Servicio(db, Admin()).TomarSiguienteAsync();

        var cita = await OtroContexto(db).ScheduledDeployments.AsNoTracking()
            .FirstAsync(s => s.Id == citaId);

        Assert.Equal(ScheduledDeploymentStatus.EnEjecucion, cita.Status);
        Assert.False(string.IsNullOrWhiteSpace(cita.ClaimedBy));
        Assert.NotNull(cita.ClaimedAtUtc);
    }

    [Fact]
    public async Task Con_varias_vencidas_cada_ejecutor_se_lleva_una_distinta()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();
        int primera = SembrarCita(db, versionId, perfilId, DateTime.UtcNow.AddMinutes(-10));
        int segunda = SembrarCita(db, versionId, perfilId, DateTime.UtcNow.AddMinutes(-5));

        // Perder la carrera por una no puede dejar sin hacer las demás: se prueba con la siguiente.
        var a = await Servicio(db, Admin()).TomarSiguienteAsync();
        var b = await Servicio(db, Admin()).TomarSiguienteAsync();

        Assert.Equal(primera, a);
        Assert.Equal(segunda, b);
    }

    [Fact]
    public async Task No_se_toma_una_cita_que_ya_paso_su_tolerancia()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();
        SembrarCita(db, versionId, perfilId, DateTime.UtcNow.AddHours(-3), toleranciaMinutos: 30);

        Assert.Null(await Servicio(db, Admin()).TomarSiguienteAsync());
    }

    [Fact]
    public async Task No_se_toma_una_cita_cuya_hora_no_ha_llegado()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();
        SembrarCita(db, versionId, perfilId, DateTime.UtcNow.AddHours(2));

        Assert.Null(await Servicio(db, Admin()).TomarSiguienteAsync());
    }

    // ── Las perdidas ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Las_vencidas_se_marcan_perdidas_y_no_se_ejecutan_despues()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();
        int citaId = SembrarCita(db, versionId, perfilId, DateTime.UtcNow.AddHours(-5), toleranciaMinutos: 60);

        var agenda = Servicio(db, Admin());
        Assert.Equal(1, await agenda.MarcarPerdidasAsync());

        var cita = await OtroContexto(db).ScheduledDeployments.AsNoTracking().FirstAsync(s => s.Id == citaId);
        Assert.Equal(ScheduledDeploymentStatus.Perdido, cita.Status);
        Assert.Contains("tolerancia", cita.ResultMessage);

        // Y ya no se puede tomar: es justo lo que evita el despliegue a deshora.
        Assert.Null(await agenda.TomarSiguienteAsync());
    }

    [Fact]
    public async Task Marcar_perdidas_no_toca_a_la_que_todavia_esta_dentro_de_su_margen()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();
        SembrarCita(db, versionId, perfilId, DateTime.UtcNow.AddMinutes(-10), toleranciaMinutos: 60);

        Assert.Equal(0, await Servicio(db, Admin()).MarcarPerdidasAsync());
    }

    // ── Ejecución ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ejecutar_guarda_el_resultado_y_deja_evidencia_automatica()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();

        var lider = new User { Username = "lider", FullName = "Ana Ruiz", Role = UserRole.Admin };
        db.Users.Add(lider);
        db.SaveChanges();

        int citaId = SembrarCita(db, versionId, perfilId, DateTime.UtcNow.AddMinutes(-2), creadaPor: lider.Id);

        var ejecutor = new EjecutorDePrueba();
        var agenda = Servicio(db, Admin(lider.Id), ejecutor);

        Assert.Equal(citaId, await agenda.TomarSiguienteAsync());
        var (ok, mensaje) = await agenda.EjecutarAsync(citaId);

        Assert.True(ok);
        Assert.Contains("77", mensaje);
        Assert.Equal(1, ejecutor.Llamadas);

        // La evidencia sustituye al checklist que nadie llenó: sin ella, el expediente atribuiría la
        // decisión al servidor en vez de a quien la agendó.
        Assert.Contains("DESPLIEGUE PROGRAMADO", ejecutor.UltimaEvidencia);
        Assert.Contains("Ana Ruiz", ejecutor.UltimaEvidencia);

        var cita = await OtroContexto(db).ScheduledDeployments.AsNoTracking().FirstAsync(s => s.Id == citaId);
        Assert.Equal(ScheduledDeploymentStatus.Completado, cita.Status);
        Assert.Equal(77, cita.DeploymentJobId);
    }

    [Fact]
    public async Task Un_despliegue_fallido_deja_la_cita_como_fallida()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();
        int citaId = SembrarCita(db, versionId, perfilId, DateTime.UtcNow.AddMinutes(-2));

        var agenda = Servicio(db, Admin(), new EjecutorDePrueba(JobStatus.Fallido));
        await agenda.TomarSiguienteAsync();

        var (ok, _) = await agenda.EjecutarAsync(citaId);

        Assert.False(ok);
        var cita = await OtroContexto(db).ScheduledDeployments.AsNoTracking().FirstAsync(s => s.Id == citaId);
        Assert.Equal(ScheduledDeploymentStatus.Fallido, cita.Status);
    }

    [Fact]
    public async Task Sin_servicio_de_despliegue_la_cita_no_se_queda_en_ejecucion_para_siempre()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();
        int citaId = SembrarCita(db, versionId, perfilId, DateTime.UtcNow.AddMinutes(-2));

        var agenda = Servicio(db, Admin());   // sin ejecutor registrado
        Assert.False(agenda.HayEjecutor);

        await agenda.TomarSiguienteAsync();
        var (ok, mensaje) = await agenda.EjecutarAsync(citaId);

        Assert.False(ok);
        Assert.Contains("servicio de despliegue", mensaje);

        var cita = await OtroContexto(db).ScheduledDeployments.AsNoTracking().FirstAsync(s => s.Id == citaId);
        Assert.Equal(ScheduledDeploymentStatus.Fallido, cita.Status);
    }

    // ── Agendar ─────────────────────────────────────────────────────────────────

    private static ProgramarDespliegueRequest Peticion(
        int versionId, int? perfilId = null, DateTime? cuandoUtc = null,
        int tolerancia = 60, IReadOnlyList<int>? servidores = null) =>
        new(versionId, perfilId, servidores ?? [], cuandoUtc ?? DateTime.UtcNow.AddHours(1), tolerancia, null);

    [Fact]
    public async Task Programar_guarda_la_cita()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();

        var (ok, _) = await Servicio(db, Admin()).ProgramarAsync(Peticion(versionId, perfilId));

        Assert.True(ok);
        Assert.Equal(1, await OtroContexto(db).ScheduledDeployments.CountAsync());
    }

    [Fact]
    public async Task No_se_puede_programar_en_el_pasado()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();

        var (ok, mensaje) = await Servicio(db, Admin())
            .ProgramarAsync(Peticion(versionId, perfilId, DateTime.UtcNow.AddMinutes(-1)));

        Assert.False(ok);
        Assert.Contains("futuro", mensaje);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1441)]
    public async Task La_tolerancia_tiene_limites(int tolerancia)
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();

        var (ok, mensaje) = await Servicio(db, Admin())
            .ProgramarAsync(Peticion(versionId, perfilId, tolerancia: tolerancia));

        Assert.False(ok);
        Assert.Contains("tolerancia", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_se_puede_programar_una_version_que_no_existe()
    {
        var (db, _, perfilId) = BaseConVersionYPerfil();

        var (ok, mensaje) = await Servicio(db, Admin()).ProgramarAsync(Peticion(9999, perfilId));

        Assert.False(ok);
        Assert.Contains("versión no existe", mensaje);
    }

    [Fact]
    public async Task Operaciones_no_puede_programar_un_perfil_que_no_tiene_habilitado()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil(perfilParaOperaciones: false);

        var agenda = Servicio(db, Operaciones());

        await Assert.ThrowsAsync<AuthorizationException>(
            () => agenda.ProgramarAsync(Peticion(versionId, perfilId)));

        // El intento queda en la bitácora: quien agenda un despliegue a producción para la madrugada
        // tiene que ser rastreable, se le deje o no.
        var denegados = await OtroContexto(db).AuditLogs.AsNoTracking()
            .CountAsync(a => a.Outcome == AuditOutcome.Denegado);
        Assert.Equal(1, denegados);
    }

    [Fact]
    public async Task Operaciones_si_puede_programar_un_perfil_habilitado()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil(perfilParaOperaciones: true);

        var (ok, _) = await Servicio(db, Operaciones()).ProgramarAsync(Peticion(versionId, perfilId));

        Assert.True(ok);
    }

    [Fact]
    public async Task Un_desarrollador_no_toca_la_agenda()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();
        var agenda = Servicio(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1));

        await Assert.ThrowsAsync<AuthorizationException>(() => agenda.ProgramarAsync(Peticion(versionId, perfilId)));
        await Assert.ThrowsAsync<AuthorizationException>(() => agenda.ListarAsync());
    }

    [Fact]
    public async Task Programar_a_servidores_sueltos_congela_la_seleccion()
    {
        var (db, versionId, _) = BaseConVersionYPerfil();

        var servidor = new DeploymentTarget { Nombre = "web01", Host = "ftps://web01", Usuario = "u" };
        db.DeploymentTargets.Add(servidor);

        // El perfil congelado lo crea el servicio de despliegue; aquí se hace pasar por creado para
        // comprobar que la agenda se lo PIDE con los servidores elegidos y una etiqueta reconocible.
        var congelado = new DeploymentProfile { Name = "⏱ web01", IsAdHoc = true };
        db.DeploymentProfiles.Add(congelado);
        db.SaveChanges();

        var ejecutor = new PerfilCongeladoDePrueba(congelado.Id);
        var (ok, _) = await Servicio(db, Admin(), ejecutor)
            .ProgramarAsync(Peticion(versionId, perfilId: null, servidores: [servidor.Id]));

        Assert.True(ok);
        Assert.Equal([servidor.Id], ejecutor.ServidoresPedidos);
        Assert.Contains("web01", ejecutor.Etiqueta);

        // Y la cita queda apuntando al perfil congelado, no a ninguno de los de a mano.
        var cita = await OtroContexto(db).ScheduledDeployments.AsNoTracking().FirstAsync();
        Assert.Equal(congelado.Id, cita.DeploymentProfileId);
    }

    [Fact]
    public async Task Sin_perfil_ni_servidores_no_se_agenda_nada()
    {
        var (db, versionId, _) = BaseConVersionYPerfil();

        var (ok, mensaje) = await Servicio(db, Admin(), new EjecutorDePrueba())
            .ProgramarAsync(Peticion(versionId, perfilId: null));

        Assert.False(ok);
        Assert.Contains("perfil", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ejecutor que solo sirve para ver qué se le pide congelar.</summary>
    private sealed class PerfilCongeladoDePrueba(int perfilQueDevuelve) : IEjecutorDeDespliegues
    {
        public IReadOnlyList<int> ServidoresPedidos = [];
        public string Etiqueta = "";

        public Task<ResultadoDeDespliegue> DesplegarAsync(
            int versionId, int perfilId, string evidenciaDeChecklist, CancellationToken ct = default) =>
            Task.FromResult(new ResultadoDeDespliegue(1, JobStatus.Completado, 1, 0));

        public Task<int> CrearPerfilCongeladoAsync(
            IReadOnlyList<int> servidorIds, string etiqueta, CancellationToken ct = default)
        {
            ServidoresPedidos = servidorIds;
            Etiqueta = etiqueta;
            return Task.FromResult(perfilQueDevuelve);
        }
    }

    // ── Cancelar ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancelar_una_cita_pendiente_la_saca_de_la_cola()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();
        int citaId = SembrarCita(db, versionId, perfilId, DateTime.UtcNow.AddHours(1));

        var (ok, _) = await Servicio(db, Admin()).CancelarAsync(citaId);

        Assert.True(ok);
        var cita = await OtroContexto(db).ScheduledDeployments.AsNoTracking().FirstAsync(s => s.Id == citaId);
        Assert.Equal(ScheduledDeploymentStatus.Cancelado, cita.Status);
    }

    [Fact]
    public async Task No_se_cancela_lo_que_ya_esta_ejecutandose()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();
        int citaId = SembrarCita(db, versionId, perfilId, DateTime.UtcNow.AddMinutes(-1),
            estado: ScheduledDeploymentStatus.EnEjecucion);

        var (ok, mensaje) = await Servicio(db, Admin()).CancelarAsync(citaId);

        Assert.False(ok);
        Assert.Contains("En ejecución", mensaje);
    }

    [Fact]
    public async Task Cancelar_pierde_contra_el_ejecutor_que_ya_la_tomo()
    {
        var (db, versionId, perfilId) = BaseConVersionYPerfil();
        int citaId = SembrarCita(db, versionId, perfilId, DateTime.UtcNow.AddMinutes(-1));

        // El trabajo de fondo se la lleva justo antes de que alguien pulse cancelar. El servicio de
        // la pantalla ya leyó «Programado» en su propio contexto, así que sin el UPDATE condicional
        // dejaría un despliegue corriendo marcado como cancelado.
        var pantalla = Servicio(db, Admin());
        var citas = await pantalla.ListarAsync(soloPendientes: true);
        Assert.Single(citas);

        await Servicio(db, Admin()).TomarSiguienteAsync();

        var (ok, _) = await pantalla.CancelarAsync(citaId);

        Assert.False(ok);

        // Lo que no puede pasar: que quede marcada como cancelada mientras el despliegue corre.
        var cita = await OtroContexto(db).ScheduledDeployments.AsNoTracking().FirstAsync(s => s.Id == citaId);
        Assert.Equal(ScheduledDeploymentStatus.EnEjecucion, cita.Status);
    }
}
