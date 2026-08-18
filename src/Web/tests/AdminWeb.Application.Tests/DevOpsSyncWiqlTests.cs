using AdminWeb.Infrastructure.Integraciones;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Armado de la consulta WIQL de sincronización: base más los filtros por tipo, estado y persona, que
/// es lo que evita traer miles de items y hace la sincronización rápida.
///
/// Se prueba la CONSULTA y no la llamada: una WIQL mal armada no falla, simplemente devuelve menos
/// tickets de los que debía, y eso no lo delata nada. Portadas del escritorio.
/// </summary>
public class DevOpsSyncWiqlTests
{
    private static string Wiql(FiltroDeSincronizacion? filtro, string proyecto = "Webpro") =>
        AzureDevOpsService.ConstruirWiqlDeSincronizacion(proyecto, filtro);

    [Fact]
    public void SinFiltro_TraeTodoMenosRemovidos()
    {
        var wiql = Wiql(null);

        Assert.Contains("[System.TeamProject] = 'Webpro'", wiql);
        Assert.Contains("[System.State] <> 'Removed'", wiql);
        Assert.DoesNotContain(" IN (", wiql);
    }

    [Fact]
    public void FiltroVacio_EquivaleASinFiltro()
    {
        Assert.DoesNotContain(" IN (", Wiql(FiltroDeSincronizacion.Vacio));
    }

    [Fact]
    public void FiltraPorEstado()
    {
        Assert.Contains("[System.State] IN ('New','Active')",
            Wiql(new FiltroDeSincronizacion([], [], ["New", "Active"])));
    }

    [Fact]
    public void CombinaTipoEstadoYPersona()
    {
        var wiql = Wiql(new FiltroDeSincronizacion(["Bug"], ["ana@x.com"], ["Active"]));

        Assert.Contains("[System.WorkItemType] IN ('Bug')", wiql);
        Assert.Contains("[System.State] IN ('Active')", wiql);
        Assert.Contains("[System.AssignedTo] IN ('ana@x.com')", wiql);
    }

    [Fact]
    public void EscapaComillasSimples_ParaNoRomperLaConsulta()
    {
        var wiql = Wiql(new FiltroDeSincronizacion([], [], ["Won't Do"]), proyecto: "O'Brien");

        Assert.Contains("'O''Brien'", wiql);
        Assert.Contains("'Won''t Do'", wiql);
    }

    // ── Sincronización del propio desarrollador ──────────────────────────────────

    [Fact]
    public void SoloMisAsignados_UsaLaMacroDeDevOps_NoUnCorreo()
    {
        // @Me lo resuelve el servidor contra el dueño del token. Por eso funciona aunque el correo de
        // la ficha no coincida con el de la cuenta de DevOps, que es lo que rompía el empate.
        var wiql = Wiql(new FiltroDeSincronizacion([], [], [], SoloMisAsignados: true));

        Assert.Contains("[System.AssignedTo] = @Me", wiql);
        Assert.DoesNotContain("'@Me'", wiql);   // entrecomillarlo lo convertiría en un nombre literal
    }

    [Fact]
    public void VentanaDeDias_AcotaPorFechaDeCambio()
    {
        var wiql = Wiql(new FiltroDeSincronizacion([], [], [], CambiadosEnDias: 90));

        Assert.Contains("[System.ChangedDate] >= @Today - 90", wiql);
        Assert.DoesNotContain("'90'", wiql);    // es aritmética de fechas, no una cadena
    }

    [Fact]
    public void SinVentana_NoAcotaPorFecha()
    {
        Assert.DoesNotContain("@Today",
            Wiql(new FiltroDeSincronizacion([], [], [], SoloMisAsignados: true, CambiadosEnDias: null)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void VentanaAbsurda_SeIgnora(int dias)
    {
        // «Últimos 0 días» no debe generar una consulta que no devuelva nada.
        Assert.DoesNotContain("@Today", Wiql(new FiltroDeSincronizacion([], [], [], CambiadosEnDias: dias)));
    }

    [Fact]
    public void MisTickets_CombinaAmbasCosas()
    {
        var wiql = Wiql(new FiltroDeSincronizacion([], [], [], SoloMisAsignados: true, CambiadosEnDias: 30));

        Assert.Contains("[System.AssignedTo] = @Me", wiql);
        Assert.Contains("[System.ChangedDate] >= @Today - 30", wiql);
        Assert.Contains("[System.State] <> 'Removed'", wiql);
    }

    [Fact]
    public void UnFiltroConSoloMisAsignados_NoCuentaComoVacio()
    {
        // Si se diera por vacío, el armado se saltaría el bloque entero y la sincronización del
        // desarrollador se traería los tickets de todo el equipo.
        Assert.False(new FiltroDeSincronizacion([], [], [], SoloMisAsignados: true).EstaVacio);
        Assert.False(new FiltroDeSincronizacion([], [], [], CambiadosEnDias: 30).EstaVacio);
        Assert.True(FiltroDeSincronizacion.Vacio.EstaVacio);
    }

    [Fact]
    public void AsignadosAbiertos_DescartaLoObvioEnElServidor()
    {
        var wiql = AzureDevOpsService.WiqlDeAsignadosAbiertos("Webpro");

        Assert.Contains("[System.AssignedTo] <> ''", wiql);
        Assert.Contains("[System.State] NOT IN ('Removed','Done','Closed','Completed')", wiql);
    }

    /// <summary>
    /// El <c>ToString</c> que genera un record imprime todos sus miembros, así que sin sobrescribirlo
    /// bastaría con interpolar las credenciales en un mensaje de error para publicar el token.
    /// </summary>
    [Fact]
    public void LasCredenciales_NoEnsenanElTokenAlImprimirse()
    {
        var credenciales = new CredencialesDevOps("https://dev.azure.com/org", "Webpro", "token-secreto");

        Assert.DoesNotContain("token-secreto", credenciales.ToString());
        Assert.DoesNotContain("token-secreto", $"{credenciales}");
    }

    // ── La ventana por fecha de CREACIÓN ─────────────────────────────────────────

    /// <summary>
    /// Acota por <c>CreatedDate</c> y NO por <c>ChangedDate</c>, que son cosas distintas: un work
    /// item de hace dos años que alguien comentó ayer entra por «cambiados» y no por «creados». Para
    /// traer al pool lo que acaba de aparecer, la fecha que importa es la de creación — si no, cada
    /// comentario en un ticket viejo lo volvería a proponer como trabajo nuevo.
    /// </summary>
    [Fact]
    public void CreadosEnDias_usaCreatedDate_yNoChangedDate()
    {
        var wiql = AzureDevOpsService.ConstruirWiqlDeSincronizacion(
            "Webpro", new FiltroDeSincronizacion([], [], [], CreadosEnDias: 7));

        Assert.Contains("[System.CreatedDate] >= @Today - 7", wiql);
        Assert.DoesNotContain("[System.ChangedDate] >=", wiql);
    }

    /// <summary>Las dos ventanas se pueden pedir a la vez y se suman, como cualquier otro par de
    /// cláusulas.</summary>
    [Fact]
    public void LasDosVentanas_seSuman()
    {
        var wiql = AzureDevOpsService.ConstruirWiqlDeSincronizacion(
            "Webpro", new FiltroDeSincronizacion([], [], [], CambiadosEnDias: 3, CreadosEnDias: 7));

        Assert.Contains("[System.ChangedDate] >= @Today - 3", wiql);
        Assert.Contains("[System.CreatedDate] >= @Today - 7", wiql);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void VentanaDeCreacionAbsurda_seIgnora(int dias)
    {
        var wiql = AzureDevOpsService.ConstruirWiqlDeSincronizacion(
            "Webpro", new FiltroDeSincronizacion([], [], [], CreadosEnDias: dias));

        Assert.DoesNotContain("CreatedDate", wiql);
    }

    /// <summary>
    /// <b>La prueba que caza el fallo que no se ve.</b> Un filtro que SOLO trae la ventana de
    /// creación no puede parecer vacío: si <c>EstaVacio</c> no contara ese campo, el bloque entero de
    /// cláusulas no se emitiría y la sincronización se traería el proyecto COMPLETO. No rompe nada y
    /// no da ningún error; solo trae diez mil work items.
    /// </summary>
    [Fact]
    public void SoloCreadosEnDias_noCuentaComoFiltroVacio()
    {
        var filtro = new FiltroDeSincronizacion([], [], [], CreadosEnDias: 7);

        Assert.False(filtro.EstaVacio);
        Assert.Contains("CreatedDate",
            AzureDevOpsService.ConstruirWiqlDeSincronizacion("Webpro", filtro));
    }
}
