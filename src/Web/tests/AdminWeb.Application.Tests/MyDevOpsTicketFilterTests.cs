using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Filtros de «Mis tickets DevOps». La pantalla traía el historial COMPLETO: años de work items
/// cerrados encima de los tres que la persona tiene abiertos hoy.
///
/// Lo delicado no es esconder de más por gusto, sino esconder trabajo PENDIENTE sin que nada lo
/// delate — por eso hay pruebas específicas de la ventana de días y del ticket sin fecha. Portadas
/// del escritorio.
/// </summary>
public class MyDevOpsTicketFilterTests
{
    private static readonly DateTime Ahora = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);

    private static DevOpsTicket T(int numero, string titulo, string estado = "Active",
        string tipo = "Bug", string iteracion = "Webpro\\Sprint 12", int diasSinTocar = 1) =>
        new()
        {
            Id = numero,
            ExternalId = numero,
            Title = titulo,
            State = estado,
            WorkItemType = tipo,
            IterationPath = iteracion,
            UpdatedAtExternal = Ahora.AddDays(-diasSinTocar),
        };

    private static List<DevOpsTicket> Lote() =>
    [
        T(101, "Error al guardar factura", "Active",   "Bug",        diasSinTocar: 2),
        T(102, "Alta de proveedores",      "New",      "User Story", diasSinTocar: 10),
        T(103, "Ajustar reporte mensual",  "Closed",   "Task",       diasSinTocar: 400),
        T(104, "Error de sesión",          "Resolved", "Bug",        diasSinTocar: 200),
        T(105, "Tarea vieja terminada",    "Done",     "Task",       diasSinTocar: 800),
    ];

    // ── Ventana de tiempo: el motivo de todo esto ────────────────────────────────

    [Fact]
    public void PorOmision_NoArrastraElHistorialCompleto()
    {
        var filtradas = MyDevOpsTicketFilter.Aplicar(Lote(),
            new FiltroDeMisTickets(UltimosDias: MyDevOpsTicketFilter.DiasPorOmision), Ahora);

        Assert.Equal([101, 102], filtradas.Select(t => t.ExternalId).ToArray());
    }

    [Fact]
    public void SinVentana_SeVeTodo()
    {
        var filtradas = MyDevOpsTicketFilter.Aplicar(Lote(), new FiltroDeMisTickets(UltimosDias: null), Ahora);
        Assert.Equal(5, filtradas.Count);
    }

    [Fact]
    public void UnTicketSinFechaDeDevOps_NoSeEsconde()
    {
        // Le falta un dato de sincronización, no deja de ser trabajo real. Esconderlo por eso sería
        // justo el fallo silencioso que estas pruebas existen para evitar.
        var sinFecha = T(200, "Sin fecha");
        sinFecha.UpdatedAtExternal = null;

        Assert.Single(MyDevOpsTicketFilter.Aplicar(
            [sinFecha], new FiltroDeMisTickets(UltimosDias: 30), Ahora));
    }

    [Fact]
    public void ElBordeDeLaVentanaEntra()
    {
        var justo = T(300, "Justo en el borde", diasSinTocar: 30);
        Assert.Single(MyDevOpsTicketFilter.Aplicar(
            [justo], new FiltroDeMisTickets(UltimosDias: 30), Ahora));
    }

    // ── Solo sin cerrar ──────────────────────────────────────────────────────────

    [Fact]
    public void SoloAbiertos_EscondeCerradosYCancelados_NoLosResueltos()
    {
        var filtradas = MyDevOpsTicketFilter.Aplicar(Lote(),
            new FiltroDeMisTickets(SoloAbiertos: true, UltimosDias: null), Ahora);

        // «Resolved» sigue siendo trabajo vivo: puede rebotar de pruebas.
        Assert.Equal([101, 102, 104], filtradas.Select(t => t.ExternalId).ToArray());
    }

    [Fact]
    public void SoloAbiertosApagado_SeVenLosCerrados()
    {
        var filtradas = MyDevOpsTicketFilter.Aplicar(Lote(),
            new FiltroDeMisTickets(SoloAbiertos: false, UltimosDias: null), Ahora);

        Assert.Contains(filtradas, t => t.State == "Closed");
    }

    // ── Pendientes de estimar ────────────────────────────────────────────────────

    [Fact]
    public void SoloSinEstimar_DejaLoAsignadoYAbiertoQueNadieEstimo()
    {
        var sinEstimar = T(1, "Mío y sin estimar");
        sinEstimar.AssignedTo = "Ana";

        var yaEstimado = T(2, "Mío y estimado");
        yaEstimado.AssignedTo = "Ana";
        yaEstimado.EstimatedHours = 4;

        // Cerrado: pedir su estimación no sirve para planear nada.
        var cerrado = T(3, "Cerrado sin estimar", estado: "Closed");
        cerrado.AssignedTo = "Ana";

        var filtradas = MyDevOpsTicketFilter.Aplicar(
            [sinEstimar, yaEstimado, cerrado],
            new FiltroDeMisTickets(SoloSinEstimar: true, UltimosDias: null), Ahora);

        Assert.Equal([1], filtradas.Select(t => t.ExternalId).ToArray());
    }

    // ── Texto y desplegables ─────────────────────────────────────────────────────

    [Fact]
    public void Texto_BuscaPorTituloNumeroEstadoTipoEIteracion()
    {
        var todos = Lote();
        List<DevOpsTicket> Buscar(string q) =>
            MyDevOpsTicketFilter.Aplicar(todos, new FiltroDeMisTickets(Texto: q), Ahora);

        Assert.Equal(2, Buscar("Error").Count);
        Assert.Single(Buscar("103"));
        Assert.Single(Buscar("User Story"));
        Assert.Equal(5, Buscar("Sprint 12").Count);
    }

    [Fact]
    public void TextoEnBlanco_NoDejaLaListaEnCero()
    {
        foreach (var q in new[] { "", "   ", null })
            Assert.Equal(5, MyDevOpsTicketFilter.Aplicar(Lote(), new FiltroDeMisTickets(Texto: q), Ahora).Count);
    }

    [Fact]
    public void LosDesplegables_EmpatanPorValorCompleto()
    {
        var tickets = new List<DevOpsTicket> { T(1, "a", estado: "New"), T(2, "b", estado: "Newer") };

        var filtradas = MyDevOpsTicketFilter.Aplicar(tickets, new FiltroDeMisTickets(Estado: "New"), Ahora);

        Assert.Single(filtradas);
        Assert.Equal("New", filtradas[0].State);
    }

    [Fact]
    public void LosDesplegables_IgnoranMayusculas()
    {
        var filtradas = MyDevOpsTicketFilter.Aplicar(Lote(),
            new FiltroDeMisTickets(Tipo: "bug", UltimosDias: null), Ahora);

        Assert.Equal(2, filtradas.Count);
    }

    [Fact]
    public void LosFiltros_SeAcumulan()
    {
        var filtradas = MyDevOpsTicketFilter.Aplicar(Lote(),
            new FiltroDeMisTickets(Texto: "Error", Tipo: "Bug", SoloAbiertos: true, UltimosDias: 7), Ahora);

        Assert.Single(filtradas);
        Assert.Equal(101, filtradas[0].ExternalId);
    }

    [Fact]
    public void Opciones_SonLasQueExisten_SinRepetirNiVacias()
    {
        var opciones = MyDevOpsTicketFilter.Opciones(Lote().Select(t => t.WorkItemType));
        Assert.Equal(["Bug", "Task", "User Story"], opciones);
    }

    // ── Pie de la lista ──────────────────────────────────────────────────────────

    [Fact]
    public void Resumen_LoPendienteNoDependeDelFiltro()
    {
        // Aunque el filtro deje 1 a la vista, los 4 sin cerrar siguen siendo el trabajo real.
        var texto = MyDevOpsTicketFilter.Resumen(1, 20, 4, Ahora);

        Assert.Contains("1 de 20", texto);
        Assert.Contains("4 sin cerrar", texto);
    }

    [Fact]
    public void Resumen_SinSincronizarLoDice()
    {
        Assert.Contains("nunca", MyDevOpsTicketFilter.Resumen(0, 0, 0, null));
    }
}
