using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Reporte del tiempo cronometrado hacia Azure DevOps. Se prueba la lógica pura (modo, delta, si
/// aplica) y las guardas de la orquestación que NO tocan la red (desactivado, no aplica, sin delta),
/// más que un fallo de reporte NO marca el tiempo como reportado (para reintentar sin duplicar).
/// </summary>
public class DevOpsTimeReportTests
{
    // ── Lógica pura ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("comentario", DevOpsTimeMode.Comentario)]
    [InlineData("campos", DevOpsTimeMode.Campos)]
    [InlineData("fields", DevOpsTimeMode.Campos)]
    [InlineData("ambos", DevOpsTimeMode.Ambos)]
    [InlineData("both", DevOpsTimeMode.Ambos)]
    [InlineData(null, DevOpsTimeMode.Comentario)]
    [InlineData("basura", DevOpsTimeMode.Comentario)]
    public void ParseMode_interpreta(string? texto, DevOpsTimeMode esperado)
        => Assert.Equal(esperado, DevOpsTimeReport.ParseMode(texto));

    [Theory]
    [InlineData(DevOpsTimeMode.Comentario)]
    [InlineData(DevOpsTimeMode.Campos)]
    [InlineData(DevOpsTimeMode.Ambos)]
    public void ModeToString_haceRoundTrip(DevOpsTimeMode m)
        => Assert.Equal(m, DevOpsTimeReport.ParseMode(DevOpsTimeReport.ModeToString(m)));

    [Fact]
    public void ActualizaCampos_y_Comenta_porModo()
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
    public void DeltaSegundos(int total, int reportado, int esperado)
        => Assert.Equal(esperado, DevOpsTimeReport.DeltaSegundos(total, reportado));

    [Theory]
    [InlineData(3600, 1.0)]
    [InlineData(1800, 0.5)]
    [InlineData(30, 0.01)]
    [InlineData(15, 0.0)]            // menos de ~18 s redondea a 0 h
    public void SegundosAHoras(int seg, double horas)
        => Assert.Equal(horas, DevOpsTimeReport.SegundosAHoras(seg));

    [Theory]
    [InlineData(1.0, 3600)]
    [InlineData(0.5, 1800)]
    [InlineData(0.01, 36)]           // el redondeo a 0.01 h son 36 s
    public void HorasASegundos(double horas, int segundos)
        => Assert.Equal(segundos, DevOpsTimeReport.HorasASegundos(horas));

    [Fact]
    public void HorasASegundos_arrastraElRestoDelRedondeo()
    {
        // 50 s reales → 0.01 h → 36 s enviados: el watermark avanza 36, no 50, y quedan 14 s para el
        // próximo reporte (así Completed Work no se desincroniza del tiempo real por el redondeo).
        int deltaSeg = 50;
        double horas = DevOpsTimeReport.SegundosAHoras(deltaSeg);       // 0.01
        int enviados = DevOpsTimeReport.HorasASegundos(horas);         // 36
        Assert.Equal(36, enviados);
        Assert.True(enviados < deltaSeg);                              // se arrastra el resto
    }

    [Fact]
    public void AplicaA_soloTicketsDevOpsConIdValido()
    {
        Assert.True(DevOpsTimeReport.AplicaA(RequirementSource.AzureDevOps, "123", out var id) && id == 123);
        Assert.False(DevOpsTimeReport.AplicaA(RequirementSource.Manual, "123", out _));
        Assert.False(DevOpsTimeReport.AplicaA(RequirementSource.AzureDevOps, "", out _));
        Assert.False(DevOpsTimeReport.AplicaA(RequirementSource.AzureDevOps, "abc", out _));
        Assert.False(DevOpsTimeReport.AplicaA(RequirementSource.AzureDevOps, "0", out _));
    }

    // ── Orquestación (guardas sin red) ───────────────────────────────────────────

    private static (AzureDevOpsService svc, AppDbContext db, SettingsService settings) Nuevo()
    {
        var db = TestDb.New();
        var user = Ctx.As(UserRole.Desarrollador, developerId: 1, userId: 1);
        var settings = new SettingsService(db, new AuditService(db, user));
        var svc = new AzureDevOpsService(settings, db, new AuditService(db, user), new NotificationService(db));
        return (svc, db, settings);
    }

    private static Requirement SeedReq(AppDbContext db, RequirementSource source, string? extId, int reportado = 0)
    {
        var r = new Requirement { Title = "T", Source = source, ExternalId = extId, DevOpsReportedSeconds = reportado, CreatedAt = DateTime.UtcNow };
        db.Requirements.Add(r); db.SaveChanges();
        return r;
    }

    private static (bool intentado, bool ok, string mensaje) Reportar(AzureDevOpsService svc, int reqId, int total)
        => svc.ReportarTiempoDevOpsAsync(reqId, total).GetAwaiter().GetResult();

    [Fact]
    public void Desactivado_noHaceNada()
    {
        var (svc, db, _) = Nuevo();
        var r = SeedReq(db, RequirementSource.AzureDevOps, "123");
        // KeyEnabled no está puesto (default desactivado).
        var (intentado, ok, _) = Reportar(svc, r.Id, 3600);
        Assert.False(intentado);
        Assert.False(ok);
    }

    [Fact]
    public void NoEsTicketDevOps_noHaceNada()
    {
        var (svc, db, settings) = Nuevo();
        settings.Set(DevOpsTimeReport.KeyEnabled, "true");
        var r = SeedReq(db, RequirementSource.Manual, null);
        Assert.False(Reportar(svc, r.Id, 3600).intentado);
    }

    [Fact]
    public void SinTiempoNuevo_noHaceNada()
    {
        var (svc, db, settings) = Nuevo();
        settings.Set(DevOpsTimeReport.KeyEnabled, "true");
        var r = SeedReq(db, RequirementSource.AzureDevOps, "123", reportado: 3600);
        Assert.False(Reportar(svc, r.Id, 3600).intentado);   // total == reportado
    }

    [Fact]
    public void FalloAlReportar_noMarcaComoReportado()
    {
        // Activado + ticket DevOps + hay delta, pero SIN configuración de DevOps: GetConfig lanza y el
        // reporte falla. Debe informar el fallo y NO marcar el tiempo (para reintentar sin duplicar).
        var (svc, db, settings) = Nuevo();
        settings.Set(DevOpsTimeReport.KeyEnabled, "true");   // modo por defecto: comentario
        var r = SeedReq(db, RequirementSource.AzureDevOps, "123");

        var (intentado, ok, _) = Reportar(svc, r.Id, 3600);

        Assert.True(intentado);
        Assert.False(ok);
        Assert.Equal(0, db.Requirements.Find(r.Id)!.DevOpsReportedSeconds);   // no se marcó
    }
}
