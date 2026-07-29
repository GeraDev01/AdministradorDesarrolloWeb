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
}
