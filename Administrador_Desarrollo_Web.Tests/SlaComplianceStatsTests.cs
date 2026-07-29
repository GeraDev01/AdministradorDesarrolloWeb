using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Reporte de cumplimiento de SLA: clasificación (a tiempo / vencido / en curso / cancelado) y las
/// agregaciones por desarrollador, prioridad, cliente (tag) y mes. Lógica pura, sin base ni red.
/// </summary>
public class SlaComplianceStatsTests
{
    private static readonly DateTime Ahora = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

    private static SlaComplianceInput S(SlaStatus st, DateTime due, string dev = "Ana", string? prio = "1", string? tags = "Bepensa")
        => new(due, st, dev, prio, tags);

    [Fact]
    public void Clasificar_porEstadoYFecha()
    {
        Assert.Equal(SlaOutcome.Cumplido,  SlaComplianceStats.Clasificar(S(SlaStatus.Cumplido, Ahora.AddDays(-1)), Ahora));
        Assert.Equal(SlaOutcome.Vencido,   SlaComplianceStats.Clasificar(S(SlaStatus.Vencido, Ahora.AddDays(-1)), Ahora));
        Assert.Equal(SlaOutcome.Cancelado, SlaComplianceStats.Clasificar(S(SlaStatus.Cancelado, Ahora.AddDays(-1)), Ahora));
        // Activo con fecha futura → en curso; activo ya vencido → cuenta como vencido.
        Assert.Equal(SlaOutcome.EnCurso, SlaComplianceStats.Clasificar(S(SlaStatus.Activo, Ahora.AddDays(1)), Ahora));
        Assert.Equal(SlaOutcome.Vencido, SlaComplianceStats.Clasificar(S(SlaStatus.Activo, Ahora.AddDays(-1)), Ahora));
    }

    [Fact]
    public void Resumen_cuentaYCalculaPorcentaje()
    {
        var items = new[]
        {
            S(SlaStatus.Cumplido, Ahora.AddDays(-2)),
            S(SlaStatus.Cumplido, Ahora.AddDays(-3)),
            S(SlaStatus.Cumplido, Ahora.AddDays(-4)),
            S(SlaStatus.Vencido,  Ahora.AddDays(-1)),
            S(SlaStatus.Activo,   Ahora.AddDays(2)),    // en curso, no cuenta
            S(SlaStatus.Cancelado, Ahora.AddDays(-1)),  // no cuenta
        };
        var r = SlaComplianceStats.Resumen(items, Ahora);
        Assert.Equal(3, r.Cumplidos);
        Assert.Equal(1, r.Vencidos);
        Assert.Equal(1, r.EnCurso);
        Assert.Equal(1, r.Cancelados);
        Assert.Equal(6, r.Total);
        Assert.Equal(4, r.Resueltos);
        Assert.Equal(75.0, r.PorcentajeCumplimiento);   // 3 de 4 resueltos
    }

    [Fact]
    public void Resumen_sinResueltos_esCeroPorciento()
    {
        var r = SlaComplianceStats.Resumen(new[] { S(SlaStatus.Activo, Ahora.AddDays(2)) }, Ahora);
        Assert.Equal(0, r.PorcentajeCumplimiento);
        Assert.Equal(0, r.Resueltos);
    }

    [Fact]
    public void PorDesarrollador_agrupaYOrdena()
    {
        var items = new[]
        {
            S(SlaStatus.Cumplido, Ahora.AddDays(-1), dev: "Ana"),
            S(SlaStatus.Vencido,  Ahora.AddDays(-1), dev: "Ana"),
            S(SlaStatus.Cumplido, Ahora.AddDays(-1), dev: "Beto"),
        };
        var rows = SlaComplianceStats.PorDesarrollador(items, Ahora);
        Assert.Equal("Ana", rows[0].Grupo);         // 2 supera a 1
        Assert.Equal(50.0, rows[0].PorcentajeCumplimiento);
        Assert.Equal(100.0, rows.Single(x => x.Grupo == "Beto").PorcentajeCumplimiento);
    }

    [Fact]
    public void PorTag_cuentaEnCadaTag()
    {
        var items = new[]
        {
            S(SlaStatus.Cumplido, Ahora.AddDays(-1), tags: "Bepensa;Bug"),
            S(SlaStatus.Vencido,  Ahora.AddDays(-1), tags: "Bepensa"),
        };
        var rows = SlaComplianceStats.PorTag(items, Ahora);
        var bep = rows.Single(r => r.Grupo == "Bepensa");
        Assert.Equal(2, bep.Total);
        Assert.Equal(50.0, bep.PorcentajeCumplimiento);
        Assert.Contains(rows, r => r.Grupo == "Bug");
    }

    [Fact]
    public void PorTag_losSinTagNoDesaparecen()
    {
        var items = new[]
        {
            S(SlaStatus.Vencido, Ahora.AddDays(-1), tags: "Bepensa"),
            S(SlaStatus.Vencido, Ahora.AddDays(-1), tags: null),   // sin tag
            S(SlaStatus.Vencido, Ahora.AddDays(-1), tags: ""),     // sin tag
        };
        var rows = SlaComplianceStats.PorTag(items, Ahora);
        var sinTag = rows.Single(r => r.Grupo == "(sin tag)");
        Assert.Equal(2, sinTag.Total);   // ninguno se pierde del desglose
    }

    [Fact]
    public void PorPrioridad_usaNombreYAgrupaSinPrioridad()
    {
        var items = new[]
        {
            S(SlaStatus.Cumplido, Ahora.AddDays(-1), prio: "1"),
            S(SlaStatus.Cumplido, Ahora.AddDays(-1), prio: ""),
        };
        var rows = SlaComplianceStats.PorPrioridad(items, Ahora);
        Assert.Contains(rows, r => r.Grupo.Contains("Muy alta"));
        Assert.Contains(rows, r => r.Grupo == "(sin prioridad)");
    }

    [Fact]
    public void PorMes_ordenaDelMasRecienteAlMasAntiguo()
    {
        var items = new[]
        {
            S(SlaStatus.Cumplido, new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc)),
            S(SlaStatus.Cumplido, new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc)),
        };
        var rows = SlaComplianceStats.PorMes(items, Ahora);
        Assert.Equal("2026-07", rows[0].Grupo);
        Assert.Equal("2026-06", rows[1].Grupo);
    }
}
