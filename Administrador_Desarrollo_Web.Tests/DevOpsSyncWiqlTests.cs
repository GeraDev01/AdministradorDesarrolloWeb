using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Armado de la WIQL de sincronización selectiva: base + filtros por tipo, estado y persona, que es
/// lo que evita traer miles de items y hace la sync rápida.
/// </summary>
public class DevOpsSyncWiqlTests
{
    [Fact]
    public void SinFiltro_TraeTodoMenosRemovidos()
    {
        var wiql = AzureDevOpsService.BuildSyncWiql("Webpro", null);
        Assert.Contains("[System.TeamProject] = 'Webpro'", wiql);
        Assert.Contains("[System.State] <> 'Removed'", wiql);
        Assert.DoesNotContain(" IN (", wiql);
    }

    [Fact]
    public void FiltroVacio_EquivaleASinFiltro()
    {
        var wiql = AzureDevOpsService.BuildSyncWiql("Webpro", new DevOpsSyncFilter([], [], []));
        Assert.DoesNotContain(" IN (", wiql);
    }

    [Fact]
    public void FiltraPorEstado()
    {
        var wiql = AzureDevOpsService.BuildSyncWiql("Webpro", new DevOpsSyncFilter([], [], ["New", "Active"]));
        Assert.Contains("[System.State] IN ('New','Active')", wiql);
    }

    [Fact]
    public void CombinaTipoEstadoYPersona()
    {
        var wiql = AzureDevOpsService.BuildSyncWiql("Webpro",
            new DevOpsSyncFilter(["Bug"], ["ana@x.com"], ["Active"]));
        Assert.Contains("[System.WorkItemType] IN ('Bug')", wiql);
        Assert.Contains("[System.State] IN ('Active')", wiql);
        Assert.Contains("[System.AssignedTo] IN ('ana@x.com')", wiql);
    }

    [Fact]
    public void EscapaComillasSimples_ParaNoRomperLaConsulta()
    {
        var wiql = AzureDevOpsService.BuildSyncWiql("O'Brien", new DevOpsSyncFilter([], [], ["Won't Do"]));
        Assert.Contains("'O''Brien'", wiql);
        Assert.Contains("'Won''t Do'", wiql);
    }

    // ── Sincronización del propio desarrollador ──────────────────────────────────

    [Fact]
    public void SoloMisAsignados_UsaLaMacroDeDevOps_NoUnCorreo()
    {
        // @Me lo resuelve el servidor contra el dueño del PAT. Por eso funciona aunque el correo de
        // la ficha no coincida con el de la cuenta de DevOps, que es lo que rompía el empate.
        var wiql = AzureDevOpsService.BuildSyncWiql("Webpro",
            new DevOpsSyncFilter([], [], [], SoloMisAsignados: true));

        Assert.Contains("[System.AssignedTo] = @Me", wiql);
        Assert.DoesNotContain("'@Me'", wiql);   // entrecomillarlo lo convertiría en un nombre literal
    }

    [Fact]
    public void VentanaDeDias_AcotaPorFechaDeCambio()
    {
        var wiql = AzureDevOpsService.BuildSyncWiql("Webpro",
            new DevOpsSyncFilter([], [], [], CambiadosEnDias: 90));

        Assert.Contains("[System.ChangedDate] >= @Today - 90", wiql);
        Assert.DoesNotContain("'90'", wiql);    // es aritmética de fechas, no una cadena
    }

    [Fact]
    public void SinVentana_NoAcotaPorFecha()
    {
        var wiql = AzureDevOpsService.BuildSyncWiql("Webpro",
            new DevOpsSyncFilter([], [], [], SoloMisAsignados: true, CambiadosEnDias: null));
        Assert.DoesNotContain("@Today", wiql);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void VentanaAbsurda_SeIgnora(int dias)
    {
        // «Últimos 0 días» no debe generar una consulta que no devuelva nada.
        var wiql = AzureDevOpsService.BuildSyncWiql("Webpro",
            new DevOpsSyncFilter([], [], [], CambiadosEnDias: dias));
        Assert.DoesNotContain("@Today", wiql);
    }

    [Fact]
    public void MisTickets_CombinaAmbasCosas()
    {
        var wiql = AzureDevOpsService.BuildSyncWiql("Webpro",
            new DevOpsSyncFilter([], [], [], SoloMisAsignados: true, CambiadosEnDias: 30));

        Assert.Contains("[System.AssignedTo] = @Me", wiql);
        Assert.Contains("[System.ChangedDate] >= @Today - 30", wiql);
        Assert.Contains("[System.State] <> 'Removed'", wiql);
    }

    [Fact]
    public void FiltroConSoloMisAsignados_NoCuentaComoVacio()
    {
        // Si IsEmpty lo diera por vacío, BuildSyncWiql se saltaría el bloque entero y la
        // sincronización del desarrollador se traería los tickets de todo el equipo.
        Assert.False(new DevOpsSyncFilter([], [], [], SoloMisAsignados: true).IsEmpty);
        Assert.False(new DevOpsSyncFilter([], [], [], CambiadosEnDias: 30).IsEmpty);
        Assert.True(new DevOpsSyncFilter([], [], []).IsEmpty);
    }
}
