using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El resumen del equipo que sale por correo.
///
/// <para>Se prueba <b>sin red</b>: el envío pasa por <c>IClienteDeCorreo</c> y aquí se le pone un
/// buzón de mentira (<see cref="CorreoDeMentira"/>). Eso permite comprobar lo que de verdad importa
/// —qué dice el correo, a quién le llega y cuándo se manda o no— sin depender de un servidor SMTP,
/// que era exactamente lo que en el escritorio no se podía probar.</para>
///
/// <para>Las dos reglas que evitan que el resumen se vuelva ruido: <b>no se manda un correo con todo
/// en cero</b> y <b>el período solo se da por cumplido cuando el envío salió bien</b>. La segunda es
/// la que hace que un servidor de correo caído retrase el resumen en vez de saltárselo.</para>
/// </summary>
public class DigestServiceTests
{
    private static async Task<SettingsService> AjustesAsync(
        AppDbContext db, bool resumenActivo = true, int cadaDias = 1, string? destinatarios = "jefe@empresa.com")
    {
        var admin = UsuarioDePrueba.Como(UserRole.Admin);
        var ajustes = new SettingsService(db, admin, new AuditService(db, admin, new OrigenDePrueba()));

        await ajustes.GuardarAsync(SettingsService.Claves.EmailEnabled, "true");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailAddress, "aplicacion@empresa.com");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailPassword, "contrasena-de-aplicacion");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailSmtpHost, "smtp.empresa.com");

        await ajustes.GuardarAsync(DigestService.ClaveActivo, resumenActivo ? "true" : "false");
        await ajustes.GuardarAsync(DigestService.ClaveFrecuencia, cadaDias.ToString());
        await ajustes.GuardarAsync(DigestService.ClaveDestinatarios, destinatarios);

        return ajustes;
    }

    private static DigestService Svc(AppDbContext db, SettingsService ajustes, CorreoDeMentira buzon,
        ICurrentUser? usuario = null)
    {
        var actual = usuario ?? UsuarioDePrueba.Como(UserRole.Admin);
        return new DigestService(db, ajustes, buzon, new AuditService(db, actual, new OrigenDePrueba()), actual);
    }

    /// <summary>Una sugerencia sin atender basta para que haya algo que reportar.</summary>
    private static async Task SembrarAlgoQueReportarAsync(AppDbContext db)
    {
        db.Suggestions.Add(new Suggestion
        {
            CreatedByUserId = 1,
            Title = "Automatizar el corte",
            Body = "Se puede automatizar.",
            Status = SuggestionStatus.Nueva
        });
        await db.SaveChangesAsync();
    }

    // ── El armado del texto (puro) ───────────────────────────────────────────────

    [Fact]
    public void Componer_LlevaTodasLasCifras()
    {
        var datos = new DatosDelResumen(
            SlaVencidos: 3, SlaPorVencer24: 2, SlaActivos: 9, SugerenciasNuevas: 4,
            DesplieguesUlt24: 1, TicketsSyncUlt24: 12, ReqPorEntregar7d: 5);

        var texto = DigestService.Componer(datos, new DateTime(2026, 8, 5, 9, 0, 0), 1);

        Assert.Contains("3 vencido(s)", texto);
        Assert.Contains("2 por vencer en las próximas 24 h", texto);
        Assert.Contains("9 activo(s) en total", texto);
        Assert.Contains("4 sin atender", texto);
        Assert.Contains("1 despliegue(s) iniciados", texto);
        Assert.Contains("12 ticket(s) de DevOps", texto);
        Assert.Contains("5 requerimiento(s) por entregar", texto);
    }

    /// <summary>
    /// El encabezado distingue diario de semanal, que es lo único que cambia entre las dos
    /// frecuencias que el escritorio ofrecía.
    /// </summary>
    [Theory]
    [InlineData(1, "Resumen del equipo")]
    [InlineData(7, "Resumen semanal del equipo")]
    public void Componer_DiceSiEsDiarioOSemanal(int cadaDias, string encabezado)
    {
        var texto = DigestService.Componer(EnCero() with { SugerenciasNuevas = 1 },
            new DateTime(2026, 8, 5, 9, 0, 0), cadaDias);

        Assert.StartsWith(encabezado, texto);
    }

    /// <summary>
    /// La fecha se escribe en español pase lo que pase. El servidor puede correr en inglés —en Azure
    /// es lo normal— y sin fijar la cultura el resumen empezaría por «Wednesday».
    /// </summary>
    [Fact]
    public void Componer_EscribeLaFechaEnEspanol()
    {
        var texto = DigestService.Componer(EnCero(), new DateTime(2026, 8, 5, 9, 0, 0), 1);

        Assert.Contains("miércoles 05/08/2026", texto);
    }

    [Fact]
    public void HayAlgoQueReportar_ConTodoEnCero_EsFalso()
    {
        Assert.False(DigestService.HayAlgoQueReportar(EnCero()));
        Assert.True(DigestService.HayAlgoQueReportar(EnCero() with { SlaActivos = 1 }));
    }

    // ── A quién le llega ─────────────────────────────────────────────────────────

    [Fact]
    public void Destinatarios_PrefiereLosConfigurados()
    {
        var destinos = DigestService.Destinatarios(
            "jefe@empresa.com; segunda@empresa.com", "sla@empresa.com", "aplicacion@empresa.com");

        Assert.Equal(new[] { "jefe@empresa.com", "segunda@empresa.com" }, destinos);
    }

    /// <summary>
    /// La cascada del escritorio: sin destinatarios propios se usa el buzón de escalamiento de SLA y,
    /// si tampoco lo hay, la propia cuenta. El resumen llega a alguien aunque nadie configure nada.
    /// </summary>
    [Fact]
    public void Destinatarios_SinConfigurar_CaeAlEscalamientoYLuegoALaPropiaCuenta()
    {
        Assert.Equal(new[] { "sla@empresa.com" },
            DigestService.Destinatarios(null, "sla@empresa.com", "aplicacion@empresa.com"));

        Assert.Equal(new[] { "aplicacion@empresa.com" },
            DigestService.Destinatarios(null, null, "aplicacion@empresa.com"));

        Assert.Empty(DigestService.Destinatarios(null, null, null));
    }

    [Fact]
    public void Destinatarios_DescartaLoQueNoEsUnaDireccionYNoRepite()
    {
        var destinos = DigestService.Destinatarios(
            "jefe@empresa.com, pendiente-de-preguntar, JEFE@empresa.com", null, null);

        Assert.Equal(new[] { "jefe@empresa.com" }, destinos);
    }

    // ── Las cifras ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Recopilar_SeparaLosSlaVencidosDeLosQueEstanPorVencer()
    {
        var db = TestDb.New();
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev);
        await db.SaveChangesAsync();

        var ahora = new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc);
        db.SlaCommitments.AddRange(
            new SlaCommitment { DeveloperId = dev.Id, DueAtUtc = ahora.AddHours(-5), Status = SlaStatus.Activo },
            new SlaCommitment { DeveloperId = dev.Id, DueAtUtc = ahora.AddHours(6), Status = SlaStatus.Activo },
            new SlaCommitment { DeveloperId = dev.Id, DueAtUtc = ahora.AddDays(4), Status = SlaStatus.Activo },
            new SlaCommitment { DeveloperId = dev.Id, DueAtUtc = ahora.AddDays(-9), Status = SlaStatus.Vencido });
        await db.SaveChangesAsync();

        var datos = await Svc(db, await AjustesAsync(db), new CorreoDeMentira()).RecopilarAsync(ahora);

        // Los tres activos cuentan como activos; el que ya pasó su fecha cuenta ADEMÁS como vencido,
        // junto con el que el sistema ya dio por vencido. Es la suma del escritorio.
        Assert.Equal(3, datos.SlaActivos);
        Assert.Equal(2, datos.SlaVencidos);
        Assert.Equal(1, datos.SlaPorVencer24);
    }

    [Fact]
    public async Task Recopilar_CuentaLasSugerenciasSinAtender()
    {
        var db = TestDb.New();
        db.Suggestions.AddRange(
            new Suggestion { CreatedByUserId = 1, Title = "Una", Body = "…", Status = SuggestionStatus.Nueva },
            new Suggestion { CreatedByUserId = 1, Title = "Otra", Body = "…", Status = SuggestionStatus.Nueva },
            new Suggestion { CreatedByUserId = 1, Title = "Vieja", Body = "…", Status = SuggestionStatus.Aceptada });
        await db.SaveChangesAsync();

        var datos = await Svc(db, await AjustesAsync(db), new CorreoDeMentira())
            .RecopilarAsync(DateTime.UtcNow);

        Assert.Equal(2, datos.SugerenciasNuevas);
    }

    // ── El disparo automático ────────────────────────────────────────────────────

    [Fact]
    public async Task RevisarYEnviar_MandaElResumenALosDestinatarios()
    {
        var db = TestDb.New();
        await SembrarAlgoQueReportarAsync(db);
        var buzon = new CorreoDeMentira();

        bool enviado = await Svc(db, await AjustesAsync(db), buzon).RevisarYEnviarAsync();

        Assert.True(enviado);
        var correo = Assert.Single(buzon.Enviados);
        Assert.Equal(new[] { "jefe@empresa.com" }, correo.Destinatarios);
        Assert.StartsWith("Resumen del equipo —", correo.Asunto);
        Assert.Contains("1 sin atender", correo.Cuerpo);
    }

    /// <summary>
    /// Lo que evita el correo repetido: la marca del último envío. En el escritorio hacía falta
    /// además un freno en memoria porque el latido era de segundos y el servicio un Singleton; aquí
    /// el freno es el período del trabajo de fondo, y esta marca es la que decide de verdad.
    /// </summary>
    [Fact]
    public async Task RevisarYEnviar_NoRepiteDentroDelMismoPeriodo()
    {
        var db = TestDb.New();
        await SembrarAlgoQueReportarAsync(db);
        var buzon = new CorreoDeMentira();
        var servicio = Svc(db, await AjustesAsync(db), buzon);

        Assert.True(await servicio.RevisarYEnviarAsync());
        Assert.False(await servicio.RevisarYEnviarAsync());

        Assert.Single(buzon.Enviados);
    }

    [Fact]
    public async Task RevisarYEnviar_SinNadaQueReportar_NoMandaNadaPeroDaElPeriodoPorCumplido()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);
        var buzon = new CorreoDeMentira();

        bool enviado = await Svc(db, ajustes, buzon).RevisarYEnviarAsync();

        Assert.False(enviado);
        Assert.Empty(buzon.Enviados);

        // El período queda marcado igual: si no, se volvería a recopilar en cada vuelta del trabajo
        // por si acaso, que es justo lo que el escritorio decidió no hacer.
        Assert.NotNull((await Svc(db, ajustes, buzon).ConfiguracionAsync()).UltimaCorridaUtc);
    }

    /// <summary>
    /// Un servidor de correo caído no puede saltarse el resumen del día: si el envío falla, el
    /// período NO se marca y el resumen sale en cuanto el correo vuelva.
    /// </summary>
    [Fact]
    public async Task RevisarYEnviar_SiElEnvioFalla_NoMarcaElPeriodoYLoIntentaDespues()
    {
        var db = TestDb.New();
        await SembrarAlgoQueReportarAsync(db);
        var ajustes = await AjustesAsync(db);
        var buzon = new CorreoDeMentira { FallaElEnvio = true };

        Assert.False(await Svc(db, ajustes, buzon).RevisarYEnviarAsync());
        Assert.Null((await Svc(db, ajustes, buzon).ConfiguracionAsync()).UltimaCorridaUtc);

        buzon.FallaElEnvio = false;
        Assert.True(await Svc(db, ajustes, buzon).RevisarYEnviarAsync());
    }

    [Fact]
    public async Task RevisarYEnviar_ConElResumenApagado_NoMandaNada()
    {
        var db = TestDb.New();
        await SembrarAlgoQueReportarAsync(db);
        var buzon = new CorreoDeMentira();

        bool enviado = await Svc(db, await AjustesAsync(db, resumenActivo: false), buzon).RevisarYEnviarAsync();

        Assert.False(enviado);
        Assert.Empty(buzon.Enviados);
    }

    [Fact]
    public async Task RevisarYEnviar_SinNingunDestinatario_NoMandaNada()
    {
        var db = TestDb.New();
        await SembrarAlgoQueReportarAsync(db);
        var buzon = new CorreoDeMentira();

        // Ni destinatarios propios, ni escalamiento de SLA, ni cuenta propia con arroba: nadie a
        // quien mandárselo.
        var admin = UsuarioDePrueba.Como(UserRole.Admin);
        var ajustes = new SettingsService(db, admin, new AuditService(db, admin, new OrigenDePrueba()));
        await ajustes.GuardarAsync(SettingsService.Claves.EmailEnabled, "true");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailAddress, "sin-arroba");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailPassword, "contrasena-de-aplicacion");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailSmtpHost, "smtp.empresa.com");
        await ajustes.GuardarAsync(DigestService.ClaveActivo, "true");

        Assert.False(await Svc(db, ajustes, buzon).RevisarYEnviarAsync());
        Assert.Empty(buzon.Enviados);
    }

    // ── A petición ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnviarAhora_LoExigeElLider()
    {
        var db = TestDb.New();
        var servicio = Svc(db, await AjustesAsync(db), new CorreoDeMentira(),
            UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 2));

        await Assert.ThrowsAsync<AuthorizationException>(() => servicio.EnviarAhoraAsync());
    }

    [Fact]
    public async Task VistaPrevia_LaExigeElLider()
    {
        var db = TestDb.New();
        var servicio = Svc(db, await AjustesAsync(db), new CorreoDeMentira(),
            UsuarioDePrueba.Como(UserRole.Operaciones, userId: 3));

        await Assert.ThrowsAsync<AuthorizationException>(() => servicio.VistaPreviaAsync());
    }

    [Fact]
    public async Task VistaPrevia_AvisaCuandoNoHayNadaQueReportar()
    {
        var db = TestDb.New();

        var vista = await Svc(db, await AjustesAsync(db), new CorreoDeMentira()).VistaPreviaAsync();

        Assert.False(vista.HayAlgoQueReportar);
        Assert.Equal(new[] { "jefe@empresa.com" }, vista.Destinatarios);
    }

    [Fact]
    public async Task EnviarAhora_SinNadaQueReportar_LoExplicaYNoMandaCorreoVacio()
    {
        var db = TestDb.New();
        var buzon = new CorreoDeMentira();

        var (ok, mensaje) = await Svc(db, await AjustesAsync(db), buzon).EnviarAhoraAsync();

        Assert.False(ok);
        Assert.Contains("no se envía un correo vacío", mensaje);
        Assert.Empty(buzon.Enviados);
    }

    [Fact]
    public async Task EnviarAhora_MandaYCuentaComoElDelPeriodo()
    {
        var db = TestDb.New();
        await SembrarAlgoQueReportarAsync(db);
        var ajustes = await AjustesAsync(db);
        var buzon = new CorreoDeMentira();

        var (ok, _) = await Svc(db, ajustes, buzon).EnviarAhoraAsync();

        Assert.True(ok);
        Assert.Single(buzon.Enviados);

        // Y el automático no manda un segundo correo idéntico minutos después.
        Assert.False(await Svc(db, ajustes, buzon).RevisarYEnviarAsync());
        Assert.Single(buzon.Enviados);
    }

    private static DatosDelResumen EnCero() => new(0, 0, 0, 0, 0, 0, 0);
}
