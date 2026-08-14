using AdminWeb.Domain.Calculo;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LOS DÍAS DE DESCANSO OBLIGATORIO del artículo 74 de la LFT.
///
/// <para><b>Las fechas van ESCRITAS a mano, una por una, y no calculadas otra vez aquí.</b> Es lo
/// único que hace que esta prueba valga algo: repetir la fórmula del código en la prueba comprueba
/// que la fórmula es igual a sí misma, y pasaría en verde con la cuenta mal hecha. Cada fecha de
/// abajo se comprobó contra un calendario, y por eso una equivocación en el código sale roja.</para>
///
/// <para>Y esto no es tipografía: un festivo que no se cuenta es un día de vacaciones que se le
/// descuenta de más a alguien, todos los años, en silencio.</para>
/// </summary>
public class FestivosDeLeyTests
{
    private static DateOnly[] Fechas(int anio) =>
        [.. FestivosDeLey.DelAnio(anio).Select(d => d.Fecha)];

    private static DateOnly D(int a, int m, int d) => new(a, m, d);

    /// <summary>
    /// <b>2026 al completo</b>, que es el primer año que va a contar de verdad.
    /// </summary>
    [Fact]
    public void DOSMIL_VEINTISEIS_los_siete_dias_y_ninguno_mas()
    {
        Assert.Equal(
            [
                D(2026, 1, 1),    // año nuevo
                D(2026, 2, 2),    // primer lunes de febrero
                D(2026, 3, 16),   // tercer lunes de marzo
                D(2026, 5, 1),    // día del trabajo
                D(2026, 9, 16),   // independencia
                D(2026, 11, 16),  // tercer lunes de noviembre
                D(2026, 12, 25)   // navidad
            ],
            Fechas(2026));
    }

    /// <summary>
    /// Los tres días que SE MUEVEN, año por año hasta 2030. Son los que una lista escrita a mano deja
    /// viejos sin que nada falle.
    /// </summary>
    [Theory]
    // año   primer lunes feb   tercer lunes mar   tercer lunes nov
    [InlineData(2026, 2, 2, 3, 16, 11, 16)]
    [InlineData(2027, 2, 1, 3, 15, 11, 15)]
    [InlineData(2028, 2, 7, 3, 20, 11, 20)]
    [InlineData(2029, 2, 5, 3, 19, 11, 19)]
    [InlineData(2030, 2, 4, 3, 18, 11, 18)]
    public void LOS_QUE_SE_MUEVEN_caen_donde_toca(
        int anio, int mesFeb, int diaFeb, int mesMar, int diaMar, int mesNov, int diaNov)
    {
        var fechas = Fechas(anio);

        Assert.Contains(D(anio, mesFeb, diaFeb), fechas);
        Assert.Contains(D(anio, mesMar, diaMar), fechas);
        Assert.Contains(D(anio, mesNov, diaNov), fechas);

        // Y todos caen en lunes, que es lo que dice el artículo.
        foreach (var f in new[] { D(anio, mesFeb, diaFeb), D(anio, mesMar, diaMar), D(anio, mesNov, diaNov) })
            Assert.Equal(DayOfWeek.Monday, f.DayOfWeek);
    }

    /// <summary>
    /// El 1 de febrero de 2027 CAE en lunes. Es el caso que distingue «el primer lunes» de «el primer
    /// lunes después del día 1», y con la cuenta escrita al revés daría el 8.
    /// </summary>
    [Fact]
    public void SI_EL_DIA_UNO_YA_ES_LUNES_ese_es_el_primero()
    {
        Assert.Contains(D(2027, 2, 1), Fechas(2027));
        Assert.DoesNotContain(D(2027, 2, 8), Fechas(2027));
    }

    // ── La transmisión del Poder Ejecutivo ───────────────────────────────────────

    /// <summary>
    /// El 1 de octubre solo es festivo cada SEIS años. Dentro del rango sembrado le toca únicamente a
    /// 2030; el anterior fue 2024.
    /// </summary>
    [Fact]
    public void EL_PRIMERO_DE_OCTUBRE_solo_cada_seis_anios()
    {
        Assert.Contains(D(2030, 10, 1), Fechas(2030));
        Assert.Contains(D(2024, 10, 1), Fechas(2024));

        foreach (int anio in new[] { 2025, 2026, 2027, 2028, 2029 })
            Assert.DoesNotContain(D(anio, 10, 1), Fechas(anio));
    }

    /// <summary>
    /// Y hacia atrás también, que es donde el módulo de C# muerde: conserva el signo del dividendo, así
    /// que sin normalizar el resto un año anterior al ancla no acertaría nunca.
    /// </summary>
    [Fact]
    public void HACIA_ATRAS_el_ciclo_tambien_cuadra()
    {
        Assert.Contains(D(2018, 10, 1), Fechas(2018));
        Assert.Contains(D(2012, 10, 1), Fechas(2012));
        Assert.DoesNotContain(D(2019, 10, 1), Fechas(2019));
    }

    // ── Lo que NO se inventa ─────────────────────────────────────────────────────

    /// <summary>
    /// <b>La jornada electoral (fracción IX) NO se genera</b>, y está probado a propósito para que
    /// nadie la añada sin pensarlo: depende de las leyes electorales de cada año y de si la elección
    /// es ordinaria. Adivinarla sería peor que no tenerla. Los días que la casa quiera añadir van a
    /// mano en la tabla.
    /// </summary>
    [Fact]
    public void LA_JORNADA_ELECTORAL_no_se_adivina()
    {
        // 2027 y 2030 son años de elección federal intermedia. No aparece ninguna fecha de junio.
        Assert.DoesNotContain(Fechas(2027), f => f.Month == 6);
        Assert.DoesNotContain(Fechas(2030), f => f.Month == 6);
    }

    /// <summary>Un rango de años los trae todos y en orden, sin repetir ni saltarse ninguno.</summary>
    [Fact]
    public void UN_RANGO_los_trae_todos_y_en_orden()
    {
        var dias = FestivosDeLey.DeLosAnios(2026, 2030);

        // 7 al año, más el 1 de octubre de 2030.
        Assert.Equal(7 * 5 + 1, dias.Count);
        Assert.Equal(dias.Select(d => d.Fecha).Distinct().Count(), dias.Count);
        Assert.Equal([.. dias.Select(d => d.Fecha).OrderBy(f => f)], [.. dias.Select(d => d.Fecha)]);
    }

    /// <summary>Un rango al revés no revienta: devuelve nada.</summary>
    [Fact]
    public void UN_RANGO_AL_REVES_no_revienta()
    {
        Assert.Empty(FestivosDeLey.DeLosAnios(2030, 2026));
    }

    /// <summary>Cada día dice de qué fracción sale. Cuando alguien discute un festivo, lo primero que
    /// hace falta es saber cuál es.</summary>
    [Fact]
    public void CADA_DIA_DICE_DE_DONDE_SALE()
    {
        Assert.All(FestivosDeLey.DelAnio(2026), d =>
            Assert.Contains("art. 74", d.Motivo, StringComparison.Ordinal));
    }
}
