using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El filtrado de las dos listas del vinculador.
///
/// Son cinco condiciones que se combinan sobre datos que vienen de dos sistemas externos, y
/// equivocarse en una no da un error: <b>esconde tickets que sí había que vincular</b> y no queda
/// nada en pantalla que lo delate. De eso van todas estas pruebas.
/// </summary>
public class TicketLinkFilterTests
{
    private static DevOpsTicket W(int id, string titulo, string estado = "Active",
        string tipo = "Bug", string asignado = "Ana López") => new()
    {
        Id = id, ExternalId = 1000 + id, Title = titulo,
        State = estado, WorkItemType = tipo, AssignedTo = asignado
    };

    private static FreshDeskTicket T(int id, string asunto, int estado = 2, int prioridad = 2,
        string agente = "Ana López", string solicitante = "Cliente Uno") => new()
    {
        Id = id, ExternalId = 5000 + id, Subject = asunto,
        Status = estado, Priority = prioridad, AgentName = agente, RequesterName = solicitante
    };

    private static readonly IReadOnlySet<int> Ninguno = new HashSet<int>();

    // ── Texto ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Texto_BuscaPorContenido_NoPorElPrincipio()
    {
        var todos = new[] { W(1, "Corregir el redondeo del timbrado"), W(2, "Otra cosa") };

        var r = TicketLinkFilter.Aplicar(todos, new FiltroDeWorkItems(Texto: "redondeo"), Ninguno);

        Assert.Single(r);
        Assert.Equal(1, r[0].Id);
    }

    [Fact]
    public void Texto_TambienMiraElNumeroYElAsignado()
    {
        var todos = new[] { W(1, "Uno", asignado: "Beto Ruiz"), W(2, "Dos", asignado: "Ana López") };

        Assert.Single(TicketLinkFilter.Aplicar(todos, new FiltroDeWorkItems(Texto: "1001"), Ninguno));
        Assert.Single(TicketLinkFilter.Aplicar(todos, new FiltroDeWorkItems(Texto: "Beto"), Ninguno));
    }

    [Fact]
    public void Texto_IgnoraMayusculasYEspaciosDeSobra()
    {
        var todos = new[] { W(1, "Corregir el REDONDEO") };

        Assert.Single(TicketLinkFilter.Aplicar(todos, new FiltroDeWorkItems(Texto: "  redondeo  "), Ninguno));
    }

    [Fact]
    public void Texto_EnBlanco_NoDejaLaListaEnCero()
    {
        var todos = new[] { W(1, "Uno"), W(2, "Dos") };

        foreach (var q in new[] { "", "   ", null })
            Assert.Equal(2, TicketLinkFilter.Aplicar(todos, new FiltroDeWorkItems(Texto: q), Ninguno).Count);
    }

    // ── Combos ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Combo_EmpataPorValorCompleto_NoPorPrefijo()
    {
        // Elegir «New» no debe traer «Newer»: el combo ofrece valores exactos y filtrar por trozos
        // mezclaría dos estados distintos sin que nadie lo note.
        var todos = new[] { W(1, "Uno", estado: "New"), W(2, "Dos", estado: "Newer") };

        var r = TicketLinkFilter.Aplicar(todos, new FiltroDeWorkItems(Estado: "New"), Ninguno);

        Assert.Single(r);
        Assert.Equal("New", r[0].State);
    }

    [Fact]
    public void Combo_IgnoraComoSeEscribieronLasMayusculas()
    {
        // Los nombres vienen de DevOps y de Freshdesk, donde nadie garantiza la capitalización.
        var todos = new[] { W(1, "Uno", asignado: "ana lópez") };

        Assert.Single(TicketLinkFilter.Aplicar(todos, new FiltroDeWorkItems(Asignado: "Ana López"), Ninguno));
    }

    [Fact]
    public void Combo_SinElegirNada_NoFiltra()
    {
        var todos = new[] { W(1, "Uno"), W(2, "Dos", tipo: "Task") };

        Assert.Equal(2, TicketLinkFilter.Aplicar(todos, new FiltroDeWorkItems(), Ninguno).Count);
    }

    // ── Solo sin vincular ────────────────────────────────────────────────────────

    [Fact]
    public void SoloSinVincular_DejaFueraLoQueYaTieneVinculo()
    {
        var todos = new[] { W(1, "Uno"), W(2, "Dos") };
        var vinculados = new HashSet<int> { 1 };

        // Apagado se ven los dos: un work item puede tener varios tickets detrás, así que esconder lo
        // ya vinculado por omisión impediría añadirle el segundo.
        Assert.Equal(2, TicketLinkFilter.Aplicar(todos, new FiltroDeWorkItems(), vinculados).Count);

        var r = TicketLinkFilter.Aplicar(todos, new FiltroDeWorkItems(SoloSinVincular: true), vinculados);
        Assert.Single(r);
        Assert.Equal(2, r[0].Id);
    }

    // ── Lado de Freshdesk ────────────────────────────────────────────────────────

    [Fact]
    public void Freshdesk_EstadoYPrioridadSeFiltranPorSuEtiqueta()
    {
        // La pantalla enseña «Abierto» y «Urgente», no 2 y 4: el filtro tiene que hablar el mismo
        // idioma que el combo, o elegir una opción no traería nada.
        var todos = new[]
        {
            T(1, "Uno", estado: 2, prioridad: 4),
            T(2, "Dos", estado: 4, prioridad: 1)
        };

        Assert.Single(TicketLinkFilter.Aplicar(todos, new FiltroDeTickets(Estado: "Abierto"), Ninguno));
        Assert.Single(TicketLinkFilter.Aplicar(todos, new FiltroDeTickets(Prioridad: "Urgente"), Ninguno));
    }

    [Fact]
    public void Freshdesk_TextoMiraAsuntoNumeroYSolicitante()
    {
        var todos = new[] { T(1, "No puedo timbrar", solicitante: "Cliente Uno"), T(2, "Otra cosa") };

        Assert.Single(TicketLinkFilter.Aplicar(todos, new FiltroDeTickets(Texto: "timbrar"), Ninguno));
        Assert.Single(TicketLinkFilter.Aplicar(todos, new FiltroDeTickets(Texto: "5001"), Ninguno));
        Assert.Equal(2, TicketLinkFilter.Aplicar(todos, new FiltroDeTickets(Texto: "Cliente"), Ninguno).Count);
    }

    // ── Opciones de los combos ───────────────────────────────────────────────────

    [Fact]
    public void Opciones_SinVaciosSinRepetirYOrdenadas()
    {
        var valores = new[] { "Beto", "ana", "", "  ", null, "Ana", "beto" };

        Assert.Equal(["ana", "Beto"], TicketLinkFilter.Opciones(valores));
    }

    // ── Pie de la lista ──────────────────────────────────────────────────────────

    [Fact]
    public void Resumen_DiceSiempreCuantosQuedanSinVincular()
    {
        // Aunque el filtro de turno no los esté mostrando: es el trabajo pendiente real de la pantalla.
        Assert.Equal("3 de 10 ticket(s)  ·  7 sin vincular", TicketLinkFilter.Resumen(3, 10, 7, "ticket"));
        Assert.Equal("10 ticket(s)  ·  7 sin vincular", TicketLinkFilter.Resumen(10, 10, 7, "ticket"));
    }
}
