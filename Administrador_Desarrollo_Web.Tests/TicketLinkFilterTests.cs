using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Filtros de la pantalla «Vincular tickets». Lo que se prueba aquí no es cosmético: un filtro que
/// esconde de más deja work items sin vincular sin que nada lo delate en pantalla, y esta pantalla
/// es justamente la que sirve para encontrar lo que falta.
/// </summary>
public class TicketLinkFilterTests
{
    private static readonly IReadOnlySet<int> SinVinculos = new HashSet<int>();

    private static DevOpsTicket Do(int id, int externo, string titulo,
        string estado = "Active", string tipo = "Bug", string asignado = "Ana López") =>
        new() { Id = id, ExternalId = externo, Title = titulo, State = estado, WorkItemType = tipo, AssignedTo = asignado };

    private static FreshDeskTicket Fd(int id, long externo, string asunto,
        int estado = 2, int prioridad = 2, string agente = "Ana López", string solicitante = "Cliente SA") =>
        new() { Id = id, ExternalId = externo, Subject = asunto, Status = estado, Priority = prioridad,
                AgentName = agente, RequesterName = solicitante };

    private static List<DevOpsTicket> LoteDo() =>
    [
        Do(1, 101, "Error al guardar factura", "Active",   "Bug",        "Ana López"),
        Do(2, 102, "Alta de proveedores",      "New",      "User Story", "Beto Ruiz"),
        Do(3, 103, "Ajustar reporte mensual",  "Closed",   "Task",       "Ana López"),
        Do(4, 104, "Error de sesión",          "Resolved", "Bug",        "Beto Ruiz"),
    ];

    private static List<FreshDeskTicket> LoteFd() =>
    [
        Fd(1, 5001, "No puedo entrar",        estado: 2, prioridad: 4, agente: "Ana López", solicitante: "ACME"),
        Fd(2, 5002, "Factura duplicada",      estado: 3, prioridad: 2, agente: "Beto Ruiz", solicitante: "Globex"),
        Fd(3, 5003, "Solicito acceso nuevo",  estado: 4, prioridad: 1, agente: "Ana López", solicitante: "ACME"),
        Fd(4, 5004, "Error al imprimir",      estado: 5, prioridad: 3, agente: "",          solicitante: "Initech"),
    ];

    // ── Sin filtro ───────────────────────────────────────────────────────────────

    [Fact]
    public void SinFiltro_NoEsconde_Nada()
    {
        Assert.Equal(4, TicketLinkFilter.Aplicar(LoteDo(), new DevOpsLinkFilter(), SinVinculos).Count);
        Assert.Equal(4, TicketLinkFilter.Aplicar(LoteFd(), new FreshDeskLinkFilter(), SinVinculos).Count);
    }

    [Fact]
    public void TextoEnBlanco_CuentaComoSinFiltro()
    {
        // La caja de búsqueda vacía o con solo espacios no debe dejar la lista en cero.
        foreach (var texto in new[] { "", "   ", null })
            Assert.Equal(4, TicketLinkFilter.Aplicar(LoteDo(), new DevOpsLinkFilter(Texto: texto), SinVinculos).Count);
    }

    // ── Búsqueda por texto ───────────────────────────────────────────────────────

    [Fact]
    public void Texto_BuscaPorTituloPorNumeroYPorPersona()
    {
        var todos = LoteDo();
        Assert.Equal(2, TicketLinkFilter.Aplicar(todos, new DevOpsLinkFilter(Texto: "Error"), SinVinculos).Count);
        Assert.Single(TicketLinkFilter.Aplicar(todos, new DevOpsLinkFilter(Texto: "103"), SinVinculos));
        Assert.Equal(2, TicketLinkFilter.Aplicar(todos, new DevOpsLinkFilter(Texto: "Beto"), SinVinculos).Count);
    }

    [Fact]
    public void Texto_IgnoraMayusculasYEspaciosDeSobra()
    {
        var r = TicketLinkFilter.Aplicar(LoteDo(), new DevOpsLinkFilter(Texto: "  FACTURA  "), SinVinculos);
        Assert.Single(r);
        Assert.Equal(101, r[0].ExternalId);
    }

    [Fact]
    public void Texto_EnFreshdesk_BuscaAsuntoNumeroYSolicitante()
    {
        var todos = LoteFd();
        Assert.Single(TicketLinkFilter.Aplicar(todos, new FreshDeskLinkFilter(Texto: "imprimir"), SinVinculos));
        Assert.Single(TicketLinkFilter.Aplicar(todos, new FreshDeskLinkFilter(Texto: "5002"), SinVinculos));
        Assert.Equal(2, TicketLinkFilter.Aplicar(todos, new FreshDeskLinkFilter(Texto: "ACME"), SinVinculos).Count);
    }

    // ── Combos: empate por valor completo ────────────────────────────────────────

    [Fact]
    public void Combo_EmpataPorValorCompleto_NoPorTrozo()
    {
        // Elegir el estado «New» no debe arrastrar «Newer»: si empatara por contenido, filtrar por
        // un estado traería los de otro y nadie lo notaría.
        var tickets = new List<DevOpsTicket> { Do(1, 1, "a", estado: "New"), Do(2, 2, "b", estado: "Newer") };

        var r = TicketLinkFilter.Aplicar(tickets, new DevOpsLinkFilter(Estado: "New"), SinVinculos);

        Assert.Single(r);
        Assert.Equal("New", r[0].State);
    }

    [Fact]
    public void Combo_IgnoraMayusculas()
    {
        // Los datos vienen de sistemas externos: nadie garantiza cómo se capitalizó un nombre.
        var r = TicketLinkFilter.Aplicar(LoteDo(), new DevOpsLinkFilter(Asignado: "ana lópez"), SinVinculos);
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Combos_DevOps_FiltranEstadoTipoYAsignado()
    {
        var todos = LoteDo();
        Assert.Equal(2, TicketLinkFilter.Aplicar(todos, new DevOpsLinkFilter(Tipo: "Bug"), SinVinculos).Count);
        Assert.Single(TicketLinkFilter.Aplicar(todos, new DevOpsLinkFilter(Estado: "Closed"), SinVinculos));
        Assert.Equal(2, TicketLinkFilter.Aplicar(todos, new DevOpsLinkFilter(Asignado: "Beto Ruiz"), SinVinculos).Count);
    }

    [Fact]
    public void Combos_Freshdesk_FiltranPorLasEtiquetasQueSeVenEnPantalla()
    {
        var todos = LoteFd();
        // Se filtra por «Abierto», no por el 2 interno: es lo que la persona lee en el combo.
        Assert.Single(TicketLinkFilter.Aplicar(todos, new FreshDeskLinkFilter(Estado: "Abierto"), SinVinculos));
        Assert.Single(TicketLinkFilter.Aplicar(todos, new FreshDeskLinkFilter(Prioridad: "Urgente"), SinVinculos));
        Assert.Equal(2, TicketLinkFilter.Aplicar(todos, new FreshDeskLinkFilter(Agente: "Ana López"), SinVinculos).Count);
    }

    [Fact]
    public void Filtros_SeAcumulan()
    {
        var r = TicketLinkFilter.Aplicar(LoteDo(),
            new DevOpsLinkFilter(Texto: "Error", Tipo: "Bug", Asignado: "Ana López"), SinVinculos);

        Assert.Single(r);
        Assert.Equal(101, r[0].ExternalId);
    }

    [Fact]
    public void Filtros_QueNoDejanNada_DevuelvenListaVacia_NoTodo()
    {
        Assert.Empty(TicketLinkFilter.Aplicar(LoteDo(),
            new DevOpsLinkFilter(Estado: "Closed", Tipo: "Bug"), SinVinculos));
    }

    // ── Solo sin vincular ────────────────────────────────────────────────────────

    [Fact]
    public void SoloSinVincular_EscondeLosQueYaTienenVinculo()
    {
        IReadOnlySet<int> vinculados = new HashSet<int> { 1, 3 };

        var todos = TicketLinkFilter.Aplicar(LoteDo(), new DevOpsLinkFilter(), vinculados);
        var pendientes = TicketLinkFilter.Aplicar(LoteDo(), new DevOpsLinkFilter(SoloSinVincular: true), vinculados);

        Assert.Equal(4, todos.Count);                                   // apagado: se ven todos
        Assert.Equal([2, 4], pendientes.Select(t => t.Id).ToArray());
    }

    [Fact]
    public void SoloSinVincular_SeCombinaConLosDemasFiltros()
    {
        IReadOnlySet<int> vinculados = new HashSet<int> { 1 };

        var r = TicketLinkFilter.Aplicar(LoteDo(),
            new DevOpsLinkFilter(Tipo: "Bug", SoloSinVincular: true), vinculados);

        Assert.Single(r);
        Assert.Equal(4, r[0].Id);   // el bug 1 ya estaba vinculado
    }

    [Fact]
    public void SoloSinVincular_TambienEnFreshdesk()
    {
        IReadOnlySet<int> vinculados = new HashSet<int> { 2, 4 };
        var r = TicketLinkFilter.Aplicar(LoteFd(), new FreshDeskLinkFilter(SoloSinVincular: true), vinculados);
        Assert.Equal([1, 3], r.Select(t => t.Id).ToArray());
    }

    // ── Opciones de los combos ───────────────────────────────────────────────────

    [Fact]
    public void Opciones_SonLasQueExisten_SinRepetirNiVacios()
    {
        var opciones = TicketLinkFilter.Opciones(LoteDo().Select(t => t.WorkItemType));
        Assert.Equal(["Bug", "Task", "User Story"], opciones);   // ordenadas y sin duplicar «Bug»
    }

    [Fact]
    public void Opciones_DescartaLosVaciosYRecortaEspacios()
    {
        var opciones = TicketLinkFilter.Opciones(["  Ana  ", "", null, "   ", "Ana", "Beto"]);
        Assert.Equal(["Ana", "Beto"], opciones);
    }

    [Fact]
    public void Opciones_DeUnaListaSinDatos_QuedaVacia()
    {
        // El combo se queda solo con su «Todos»: no hay nada por lo que filtrar.
        Assert.Empty(TicketLinkFilter.Opciones(new List<FreshDeskTicket>().Select(t => t.AgentName)));
        Assert.Empty(TicketLinkFilter.Opciones(LoteFd().Where(t => t.Id == 4).Select(t => t.AgentName)));
    }

    // ── Pie de la lista ──────────────────────────────────────────────────────────

    [Fact]
    public void Resumen_DiceCuantosSeVenYCuantosFaltanPorVincular()
    {
        Assert.Equal("4 work item(s)  ·  4 sin vincular", TicketLinkFilter.Resumen(4, 4, 4, "work item"));
        Assert.Equal("2 de 4 work item(s)  ·  1 sin vincular", TicketLinkFilter.Resumen(2, 4, 1, "work item"));
    }

    [Fact]
    public void Resumen_ElPendienteNoDependeDelFiltroDeTurno()
    {
        // Aunque el filtro deje la lista en cero, el pendiente real se sigue viendo: es el trabajo
        // que falta, no lo que la vista actual alcanza a mostrar.
        Assert.Contains("3 sin vincular", TicketLinkFilter.Resumen(0, 10, 3, "ticket"));
    }
}
