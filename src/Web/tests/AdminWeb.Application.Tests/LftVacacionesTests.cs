using AdminWeb.Domain.Calculo;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Tabla de vacaciones de la LFT (reforma "Vacaciones Dignas", Art. 76/77). Los valores ancla
/// se confirmaron contra el decreto del DOF (27-dic-2022) y la tabla de PROFEDET.
///
/// <para>Es una COPIA LITERAL del archivo del escritorio, con dos casos más al final para la
/// nota que se enseña junto al campo. Se duplica a propósito y no se comparte: es una regla de
/// ley, y hasta el corte las dos aplicaciones tienen que dar el mismo número. Si un día una de
/// las dos copias se toca, la otra suite lo dice.</para>
/// </summary>
public class LftVacacionesTests
{
    [Theory]
    [InlineData(1, 12)]
    [InlineData(2, 14)]
    [InlineData(3, 16)]
    [InlineData(4, 18)]
    [InlineData(5, 20)]
    [InlineData(6, 22)]
    [InlineData(10, 22)]
    [InlineData(11, 24)]
    [InlineData(15, 24)]
    [InlineData(16, 26)]
    [InlineData(20, 26)]
    [InlineData(21, 28)]
    [InlineData(25, 28)]
    [InlineData(26, 30)]
    public void DiasPorAnios_SigueLaTablaDeLaLey(int anios, int esperado)
        => Assert.Equal(esperado, LftVacaciones.DiasPorAnios(anios));

    [Fact]
    public void DiasPorAnios_CeroOMenos_EsCero()
    {
        Assert.Equal(0, LftVacaciones.DiasPorAnios(0));
        Assert.Equal(0, LftVacaciones.DiasPorAnios(-3));
    }

    [Theory]
    // ingreso, corte, años completos esperados
    [InlineData("2020-03-01", "2026-07-28", 6)]   // pasó el aniversario de este año
    [InlineData("2020-03-01", "2026-02-15", 5)]   // aún no llega el aniversario → un año menos
    [InlineData("2020-03-01", "2026-03-01", 6)]   // justo el aniversario cuenta como cumplido
    [InlineData("2026-01-01", "2026-07-28", 0)]   // menos de un año
    [InlineData("2025-07-28", "2026-07-28", 1)]   // exactamente un año
    public void AniosCumplidos_CuentaAniversarios(string ingreso, string corte, int esperado)
        => Assert.Equal(esperado, LftVacaciones.AniosCumplidos(DateTime.Parse(ingreso), DateTime.Parse(corte)));

    [Fact]
    public void AniosCumplidos_29Feb_UsaFinDeFebreroEnAniosNoBisiestos()
    {
        // Ingreso el 29-feb-2020; al 28-feb-2025 (no bisiesto) ya cumplió 5 años.
        Assert.Equal(5, LftVacaciones.AniosCumplidos(new DateTime(2020, 2, 29), new DateTime(2025, 2, 28)));
    }

    [Fact]
    public void DiasCorrespondientes_PrimerAnio_EsProporcional()
    {
        // ~208 días trabajados de 365 → 12 * 208/365 ≈ 6.84 → 7.
        var dias = LftVacaciones.DiasCorrespondientes(new DateTime(2026, 1, 1), new DateTime(2026, 7, 28));
        Assert.Equal(7, dias);
    }

    [Fact]
    public void DiasCorrespondientes_AlCumplirAnios_UsaLaTabla()
    {
        Assert.Equal(12, LftVacaciones.DiasCorrespondientes(new DateTime(2025, 7, 28), new DateTime(2026, 7, 28)));
        Assert.Equal(22, LftVacaciones.DiasCorrespondientes(new DateTime(2020, 1, 1), new DateTime(2026, 7, 28)));
    }

    [Fact]
    public void DiasCorrespondientes_FechaFuturaOIgual_EsCero()
    {
        Assert.Equal(0, LftVacaciones.DiasCorrespondientes(new DateTime(2026, 7, 28), new DateTime(2026, 7, 28)));
        Assert.Equal(0, LftVacaciones.DiasCorrespondientes(new DateTime(2027, 1, 1), new DateTime(2026, 7, 28)));
    }

    [Fact]
    public void Nota_ConUnAnoOMas_DiceLaAntiguedadYLosDias()
    {
        var nota = LftVacaciones.Nota(new DateTime(2020, 1, 1), new DateTime(2026, 7, 28));

        Assert.Contains("6 año(s)", nota);
        Assert.Contains("22 días", nota);
    }

    [Fact]
    public void Nota_SinCumplirElAno_AnunciaElProporcional()
    {
        var nota = LftVacaciones.Nota(new DateTime(2026, 1, 1), new DateTime(2026, 7, 28));

        Assert.Contains("menos de un año", nota);
        Assert.Contains("12 días", nota);
    }
}
