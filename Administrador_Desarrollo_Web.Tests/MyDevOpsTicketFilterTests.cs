using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Filtros de «Mis tickets DevOps». La pantalla traía el historial COMPLETO: años de work items
/// cerrados encima de los tres que la persona tiene abiertos hoy.
///
/// Lo delicado no es esconder de más por gusto, sino esconder trabajo PENDIENTE sin que nada lo
/// delate — por eso hay pruebas específicas de la ventana de días y del ticket sin fecha.
/// </summary>
public class MyDevOpsTicketFilterTests
{
    private static readonly DateTime Ahora = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);

    private static DevOpsTicket T(int externo, string titulo, string estado = "Active",
        string tipo = "Bug", string iteracion = "Webpro\\Sprint 12", int diasSinTocar = 1) =>
        new()
        {
            Id = externo, ExternalId = externo, Title = titulo, State = estado,
            WorkItemType = tipo, IterationPath = iteracion,
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
        var r = MyDevOpsTicketFilter.Aplicar(Lote(),
            new MyDevOpsFilter(UltimosDias: MyDevOpsTicketFilter.DiasPorOmision), Ahora);

        Assert.Equal([101, 102], r.Select(t => t.ExternalId).ToArray());
    }

    [Fact]
    public void SinVentana_SeVeTodo()
    {
        var r = MyDevOpsTicketFilter.Aplicar(Lote(), new MyDevOpsFilter(UltimosDias: null), Ahora);
        Assert.Equal(5, r.Count);
    }

    [Fact]
    public void UnTicketSinFechaDeDevOps_NoSeEsconde()
    {
        // Le falta un dato de sincronización, no deja de ser trabajo real. Esconderlo por eso sería
        // justo el fallo silencioso que estas pruebas existen para evitar.
        var sinFecha = T(200, "Sin fecha");
        sinFecha.UpdatedAtExternal = null;

        var r = MyDevOpsTicketFilter.Aplicar([sinFecha], new MyDevOpsFilter(UltimosDias: 30), Ahora);

        Assert.Single(r);
    }

    [Fact]
    public void ElBordeDeLaVentanaEntra()
    {
        var justo = T(300, "Justo en el borde", diasSinTocar: 30);
        var r = MyDevOpsTicketFilter.Aplicar([justo], new MyDevOpsFilter(UltimosDias: 30), Ahora);
        Assert.Single(r);
    }

    // ── Solo sin cerrar ──────────────────────────────────────────────────────────

    [Fact]
    public void SoloAbiertos_EscondeCerradosYCancelados_NoLosResueltos()
    {
        var r = MyDevOpsTicketFilter.Aplicar(Lote(),
            new MyDevOpsFilter(SoloAbiertos: true, UltimosDias: null), Ahora);

        // Resolved sigue siendo trabajo vivo: puede rebotar de pruebas.
        Assert.Equal([101, 102, 104], r.Select(t => t.ExternalId).ToArray());
    }

    [Fact]
    public void SoloAbiertosApagado_SeVenLosCerrados()
    {
        var r = MyDevOpsTicketFilter.Aplicar(Lote(),
            new MyDevOpsFilter(SoloAbiertos: false, UltimosDias: null), Ahora);
        Assert.Contains(r, t => t.State == "Closed");
    }

    // ── Texto y combos ───────────────────────────────────────────────────────────

    [Fact]
    public void Texto_BuscaPorTituloNumeroEstadoTipoEIteracion()
    {
        var todos = Lote();
        var sinVentana = (string q) => MyDevOpsTicketFilter.Aplicar(todos, new MyDevOpsFilter(Texto: q), Ahora);

        Assert.Equal(2, sinVentana("Error").Count);
        Assert.Single(sinVentana("103"));
        Assert.Single(sinVentana("User Story"));
        Assert.Equal(5, sinVentana("Sprint 12").Count);
    }

    [Fact]
    public void TextoEnBlanco_NoDejaLaListaEnCero()
    {
        foreach (var q in new[] { "", "   ", null })
            Assert.Equal(5, MyDevOpsTicketFilter.Aplicar(Lote(), new MyDevOpsFilter(Texto: q), Ahora).Count);
    }

    [Fact]
    public void Combos_EmpatanPorValorCompleto()
    {
        var tickets = new List<DevOpsTicket> { T(1, "a", estado: "New"), T(2, "b", estado: "Newer") };
        var r = MyDevOpsTicketFilter.Aplicar(tickets, new MyDevOpsFilter(Estado: "New"), Ahora);

        Assert.Single(r);
        Assert.Equal("New", r[0].State);
    }

    [Fact]
    public void Combos_IgnoranMayusculas()
    {
        var r = MyDevOpsTicketFilter.Aplicar(Lote(), new MyDevOpsFilter(Tipo: "bug", UltimosDias: null), Ahora);
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Filtros_SeAcumulan()
    {
        var r = MyDevOpsTicketFilter.Aplicar(Lote(),
            new MyDevOpsFilter(Texto: "Error", Tipo: "Bug", SoloAbiertos: true, UltimosDias: 7), Ahora);

        Assert.Single(r);
        Assert.Equal(101, r[0].ExternalId);
    }

    [Fact]
    public void Opciones_SonLasQueExisten_SinRepetirNiVacios()
    {
        var opciones = MyDevOpsTicketFilter.Opciones(Lote().Select(t => t.WorkItemType));
        Assert.Equal(["Bug", "Task", "User Story"], opciones);
    }

    // ── Pie de la lista ──────────────────────────────────────────────────────────

    [Fact]
    public void Resumen_ElPendienteNoDependeDelFiltro()
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
