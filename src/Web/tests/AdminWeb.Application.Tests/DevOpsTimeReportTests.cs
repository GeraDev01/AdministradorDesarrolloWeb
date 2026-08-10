using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Reporte del tiempo cronometrado hacia Azure DevOps.
///
/// Se prueba el cálculo puro (modo, delta, si aplica) y las guardas de la orquestación, con un
/// cliente de mentira: los campos de trabajo de DevOps son ADITIVOS, así que una marca de agua mal
/// llevada mete horas de más en el ticket de alguien y nadie se entera hasta que las cuentan.
/// Portadas del escritorio.
/// </summary>
public class DevOpsTimeReportTests
{
    // ── Cálculo puro ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("comentario", DevOpsTimeMode.Comentario)]
    [InlineData("campos", DevOpsTimeMode.Campos)]
    [InlineData("fields", DevOpsTimeMode.Campos)]
    [InlineData("ambos", DevOpsTimeMode.Ambos)]
    [InlineData("both", DevOpsTimeMode.Ambos)]
    [InlineData(null, DevOpsTimeMode.Comentario)]
    [InlineData("basura", DevOpsTimeMode.Comentario)]
    public void LeerModo_interpreta(string? texto, DevOpsTimeMode esperado) =>
        Assert.Equal(esperado, DevOpsTimeReport.LeerModo(texto));

    [Theory]
    [InlineData(DevOpsTimeMode.Comentario)]
    [InlineData(DevOpsTimeMode.Campos)]
    [InlineData(DevOpsTimeMode.Ambos)]
    public void ModoATexto_haceIdaYVuelta(DevOpsTimeMode modo) =>
        Assert.Equal(modo, DevOpsTimeReport.LeerModo(DevOpsTimeReport.ModoATexto(modo)));

    [Fact]
    public void ActualizaCampos_y_Comenta_dependenDelModo()
    {
        Assert.True(DevOpsTimeReport.Comenta(DevOpsTimeMode.Comentario));
        Assert.False(DevOpsTimeReport.ActualizaCampos(DevOpsTimeMode.Comentario));
        Assert.True(DevOpsTimeReport.ActualizaCampos(DevOpsTimeMode.Campos));
        Assert.False(DevOpsTimeReport.Comenta(DevOpsTimeMode.Campos));
        Assert.True(DevOpsTimeReport.ActualizaCampos(DevOpsTimeMode.Ambos));
        Assert.True(DevOpsTimeReport.Comenta(DevOpsTimeMode.Ambos));
    }

    [Theory]
    [InlineData(3600, 0, 3600)]
    [InlineData(3600, 1800, 1800)]
    [InlineData(1800, 1800, 0)]      // ya reportado
    [InlineData(1000, 3600, 0)]      // nunca negativo
    public void DeltaSegundos(int total, int reportado, int esperado) =>
        Assert.Equal(esperado, DevOpsTimeReport.DeltaSegundos(total, reportado));

    [Theory]
    [InlineData(3600, 1.0)]
    [InlineData(1800, 0.5)]
    [InlineData(30, 0.01)]
    [InlineData(15, 0.0)]            // menos de ~18 s redondea a 0 h
    public void SegundosAHoras(int segundos, double horas) =>
        Assert.Equal(horas, DevOpsTimeReport.SegundosAHoras(segundos));

    [Theory]
    [InlineData(1.0, 3600)]
    [InlineData(0.5, 1800)]
    [InlineData(0.01, 36)]           // el redondeo a 0.01 h son 36 s
    public void HorasASegundos(double horas, int segundos) =>
        Assert.Equal(segundos, DevOpsTimeReport.HorasASegundos(horas));

    [Fact]
    public void HorasASegundos_arrastraElRestoDelRedondeo()
    {
        // 50 s reales → 0.01 h → 36 s enviados: la marca de agua avanza 36 y no 50, así que quedan
        // 14 s para el próximo reporte en vez de perderse. Sin esto, Completed Work se va separando
        // del tiempo real un poco en cada parada del cronómetro.
        const int deltaSegundos = 50;
        double horas = DevOpsTimeReport.SegundosAHoras(deltaSegundos);
        int enviados = DevOpsTimeReport.HorasASegundos(horas);

        Assert.Equal(36, enviados);
        Assert.True(enviados < deltaSegundos);
    }

    [Fact]
    public void AplicaA_soloTicketsDeDevOpsConNumeroValido()
    {
        Assert.True(DevOpsTimeReport.AplicaA(RequirementSource.AzureDevOps, "123", out var numero) && numero == 123);
        Assert.False(DevOpsTimeReport.AplicaA(RequirementSource.Manual, "123", out _));
        Assert.False(DevOpsTimeReport.AplicaA(RequirementSource.AzureDevOps, "", out _));
        Assert.False(DevOpsTimeReport.AplicaA(RequirementSource.AzureDevOps, "abc", out _));
        Assert.False(DevOpsTimeReport.AplicaA(RequirementSource.AzureDevOps, "0", out _));
    }

    // ── Orquestación, con el cliente de mentira ──────────────────────────────────

    private static (DevOpsService servicio, AppDbContext db, ClienteDevOpsDePrueba cliente) Nuevo(
        AppDbContext db, ClienteDevOpsDePrueba? cliente = null)
    {
        ICurrentUser usuario = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 1);
        var bitacora = new AuditService(db, usuario, new OrigenDePrueba());
        var configuracion = new SettingsService(db, usuario, bitacora);
        var secretos = new UserSecretsService(db, usuario, new ProtectorSimulado(), bitacora);

        cliente ??= new ClienteDevOpsDePrueba();

        return (new DevOpsService(db, usuario, configuracion, secretos, bitacora,
                                  new NotificationService(db), cliente),
                db, cliente);
    }

    private static async Task ConfigurarAsync(AppDbContext db, string? modo = null, bool habilitado = true)
    {
        var admin = UsuarioDePrueba.Como(UserRole.Admin, userId: 99);
        var settings = new SettingsService(db, admin, new AuditService(db, admin, new OrigenDePrueba()));

        await settings.GuardarAsync(SettingsService.Claves.AzureDevOpsOrgUrl, "https://dev.azure.com/org");
        await settings.GuardarAsync(SettingsService.Claves.AzureDevOpsProject, "Webpro");
        await settings.GuardarAsync(SettingsService.Claves.AzureDevOpsPat, "token-de-la-instalacion");
        await settings.GuardarAsync(DevOpsTimeReport.ClaveHabilitado, habilitado ? "true" : "false");
        if (modo != null) await settings.GuardarAsync(DevOpsTimeReport.ClaveModo, modo);
    }

    private static Requirement SembrarRequerimiento(
        AppDbContext db, RequirementSource origen, string? identificadorExterno, int reportado = 0)
    {
        var requerimiento = new Requirement
        {
            Title = "Trabajo",
            Source = origen,
            ExternalId = identificadorExterno,
            DevOpsReportedSeconds = reportado,
            CreatedAt = DateTime.UtcNow
        };
        db.Requirements.Add(requerimiento);
        db.SaveChanges();
        return requerimiento;
    }

    [Fact]
    public async Task Desactivado_NoHaceNada()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db, habilitado: false);
        var requerimiento = SembrarRequerimiento(db, RequirementSource.AzureDevOps, "123");

        var (servicio, _, cliente) = Nuevo(db);
        var (intentado, ok, _) = await servicio.ReportarTiempoAsync(requerimiento.Id, 3600);

        Assert.False(intentado);
        Assert.False(ok);
        Assert.Empty(cliente.Llamadas);
    }

    [Fact]
    public async Task SiNoEsUnTicketDeDevOps_NoHaceNada()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db);
        var requerimiento = SembrarRequerimiento(db, RequirementSource.Manual, null);

        var (servicio, _, _) = Nuevo(db);

        Assert.False((await servicio.ReportarTiempoAsync(requerimiento.Id, 3600)).intentado);
    }

    [Fact]
    public async Task SinTiempoNuevo_NoHaceNada()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db);
        var requerimiento = SembrarRequerimiento(db, RequirementSource.AzureDevOps, "123", reportado: 3600);

        var (servicio, _, cliente) = Nuevo(db);

        Assert.False((await servicio.ReportarTiempoAsync(requerimiento.Id, 3600)).intentado);
        Assert.Empty(cliente.Llamadas);
    }

    [Fact]
    public async Task ConComentario_ReportaElDeltaExactoYAvanzaLaMarcaDeAgua()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db, modo: "comentario");
        var requerimiento = SembrarRequerimiento(db, RequirementSource.AzureDevOps, "123", reportado: 1800);

        var (servicio, _, cliente) = Nuevo(db);
        var (intentado, ok, _) = await servicio.ReportarTiempoAsync(requerimiento.Id, 5400);

        Assert.True(intentado);
        Assert.True(ok);
        Assert.Single(cliente.ComentariosPublicados);
        Assert.Equal(5400, db.Requirements.AsNoTracking().Single().DevOpsReportedSeconds);
    }

    [Fact]
    public async Task ConCampos_LaMarcaDeAguaAvanzaSoloPorLoQueDeVerdadSeEnvio()
    {
        // 50 s se redondean a 0.01 h = 36 s. Si la marca avanzara los 50, los 14 s de diferencia se
        // perderían para siempre y Completed Work quedaría por debajo del tiempo real.
        var db = TestDb.New();
        await ConfigurarAsync(db, modo: "campos");
        var requerimiento = SembrarRequerimiento(db, RequirementSource.AzureDevOps, "123");

        var (servicio, _, cliente) = Nuevo(db);
        var (_, ok, _) = await servicio.ReportarTiempoAsync(requerimiento.Id, 50);

        Assert.True(ok);
        Assert.Equal(0.01, cliente.HorasDeTrabajoSumadas);
        Assert.Equal(36, db.Requirements.AsNoTracking().Single().DevOpsReportedSeconds);
    }

    [Fact]
    public async Task SiElTipoDeWorkItemNoAdmiteHoras_SeDiceYNoSeMarcaComoReportado()
    {
        var db = TestDb.New();
        await ConfigurarAsync(db, modo: "campos");
        var requerimiento = SembrarRequerimiento(db, RequirementSource.AzureDevOps, "123");

        var (servicio, _, _) = Nuevo(db, new ClienteDevOpsDePrueba { AceptaHorasDeTrabajo = false });
        var (intentado, ok, mensaje) = await servicio.ReportarTiempoAsync(requerimiento.Id, 3600);

        Assert.True(intentado);
        Assert.False(ok);
        Assert.Contains("no admite horas", mensaje);
        Assert.Equal(0, db.Requirements.AsNoTracking().Single().DevOpsReportedSeconds);
    }

    [Fact]
    public async Task SiElReporteFalla_NoSeMarcaComoReportado()
    {
        // Es lo que permite reintentar sin duplicar: si se marcara igual, esas horas no llegarían
        // nunca al ticket y nadie lo notaría.
        var db = TestDb.New();
        await ConfigurarAsync(db, modo: "ambos");
        var requerimiento = SembrarRequerimiento(db, RequirementSource.AzureDevOps, "123");

        var cliente = new ClienteDevOpsDePrueba
        {
            Fallo = new AdminWeb.Infrastructure.Integraciones.ErrorDeAzureDevOps(
                "No se pudo contactar con Azure DevOps: se agotó el tiempo.")
        };
        var (servicio, _, _) = Nuevo(db, cliente);

        var (intentado, ok, mensaje) = await servicio.ReportarTiempoAsync(requerimiento.Id, 3600);

        Assert.True(intentado);
        Assert.False(ok);
        Assert.Contains("No se pudo contactar", mensaje);
        Assert.Equal(0, db.Requirements.AsNoTracking().Single().DevOpsReportedSeconds);
    }

    [Fact]
    public async Task SinTokenNiConfiguracion_SeExplicaEnVezDeReventar()
    {
        var db = TestDb.New();

        // Solo el interruptor, sin organización ni token: el reporte no puede salir y tiene que
        // decirlo, no lanzar.
        var admin = UsuarioDePrueba.Como(UserRole.Admin, userId: 99);
        var settings = new SettingsService(db, admin, new AuditService(db, admin, new OrigenDePrueba()));
        await settings.GuardarAsync(DevOpsTimeReport.ClaveHabilitado, "true");

        var requerimiento = SembrarRequerimiento(db, RequirementSource.AzureDevOps, "123");
        var (servicio, _, _) = Nuevo(db);

        var (intentado, ok, mensaje) = await servicio.ReportarTiempoAsync(requerimiento.Id, 3600);

        Assert.True(intentado);
        Assert.False(ok);
        Assert.NotEmpty(mensaje);
        Assert.Equal(0, db.Requirements.AsNoTracking().Single().DevOpsReportedSeconds);
    }
}
