using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Reportes de planeación: precisión de estimaciones (estimado vs real) y capacidad del equipo.
/// Lógica pura, sin base ni red.
/// </summary>
public class EstimationCapacityStatsTests
{
    // ── Estimación ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(10, 10, EstimationClass.Preciso)]      // ratio 1.0
    [InlineData(10, 11, EstimationClass.Preciso)]      // ratio 1.1 (dentro de ±20 %)
    [InlineData(10, 13, EstimationClass.Subestimado)]  // ratio 1.3 (tomó más)
    [InlineData(10, 5,  EstimationClass.Sobreestimado)] // ratio 0.5 (tomó menos)
    [InlineData(0,  5,  EstimationClass.SinEstimacion)]
    [InlineData(10, 0,  EstimationClass.SinTiempo)]
    public void Clasificar(double est, double act, EstimationClass esperado)
        => Assert.Equal(esperado, EstimationStats.Clasificar(est, act));

    [Fact]
    public void Fila_convierteSegundosAHorasYRatio()
    {
        var f = EstimationStats.Fila(new EstimationInput(1, "R", 2m, 3600, RequirementStatus.EnDesarrollo));
        Assert.Equal(2.0, f.EstimateHrs);
        Assert.Equal(1.0, f.ActualHrs);          // 3600 s = 1 h
        Assert.Equal(-1.0, f.DeltaHrs);
        Assert.Equal(0.5, f.Ratio);
        Assert.Equal(EstimationClass.Sobreestimado, f.Clase);
    }

    [Fact]
    public void Resumen_soloCuentaLoQueTieneEstimacionYTiempo()
    {
        var filas = EstimationStats.Filas(new[]
        {
            new EstimationInput(1, "A", 10m, 36000, RequirementStatus.Entregado),  // 10 h reales → preciso
            new EstimationInput(2, "B", 10m, 54000, RequirementStatus.Entregado),  // 15 h → subestimado
            new EstimationInput(3, "C", 10m, 0,      RequirementStatus.EnDesarrollo), // sin tiempo → no cuenta
        });
        var r = EstimationStats.Resumen(filas);
        Assert.Equal(2, r.ConDatos);
        Assert.Equal(1, r.Precisos);
        Assert.Equal(1, r.Subestimados);
        Assert.Equal(0, r.Sobreestimados);
        Assert.Equal(30.0, r.HorasEstimadas);   // 10+10+10
        Assert.Equal(25.0, r.HorasReales);      // 10+15+0
    }

    // ── Capacidad ────────────────────────────────────────────────────────────────

    [Fact]
    public void Clasificar_disponibilidad()
    {
        Assert.Equal(Disponibilidad.DeVacaciones, CapacityStats.Clasificar(true, 5, 100, 40));
        Assert.Equal(Disponibilidad.Libre,        CapacityStats.Clasificar(false, 0, 0, 40));
        Assert.Equal(Disponibilidad.Ocupado,      CapacityStats.Clasificar(false, 3, 20, 40));
        Assert.Equal(Disponibilidad.Sobrecargado, CapacityStats.Clasificar(false, 3, 60, 40));
    }

    [Theory]
    // vacación jul 10-20, rango jul 15 - ago 15 → solapa jul 15-20 = 6 días
    [InlineData("2026-07-10", "2026-07-20", "2026-07-15", "2026-08-15", 6)]
    // vacación completamente dentro del rango → sus 5 días
    [InlineData("2026-07-16", "2026-07-20", "2026-07-15", "2026-08-15", 5)]
    // sin solape
    [InlineData("2026-06-01", "2026-06-05", "2026-07-15", "2026-08-15", 0)]
    public void DiasVacacionEnRango(string vi, string vf, string ri, string rf, int esperado)
        => Assert.Equal(esperado, CapacityStats.DiasVacacionEnRango(DateTime.Parse(vi), DateTime.Parse(vf), DateTime.Parse(ri), DateTime.Parse(rf)));

    [Fact]
    public void EnVacacion_incluyeExtremos()
    {
        var ini = new DateTime(2026, 7, 10);
        var fin = new DateTime(2026, 7, 20);
        Assert.True(CapacityStats.EnVacacion(ini, fin, new DateTime(2026, 7, 10)));
        Assert.True(CapacityStats.EnVacacion(ini, fin, new DateTime(2026, 7, 15)));
        Assert.True(CapacityStats.EnVacacion(ini, fin, new DateTime(2026, 7, 20)));
        Assert.False(CapacityStats.EnVacacion(ini, fin, new DateTime(2026, 7, 21)));
    }
}
