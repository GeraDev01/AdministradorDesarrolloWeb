namespace AdminWeb.Domain.Calculo;

/// <summary>
/// Los días de descanso OBLIGATORIO del artículo 74 de la Ley Federal del Trabajo.
///
/// <para><b>Son reglas y no una lista de fechas</b>, y esa es toda la razón de que esta clase exista.
/// Cuatro de los días del artículo 74 se mueven cada año —«el primer lunes de febrero», «el tercer
/// lunes de marzo», «el tercer lunes de noviembre»— y uno aparece una vez cada seis años. Una lista
/// escrita a mano se queda vieja en enero sin que nada falle: los días simplemente dejan de contarse
/// y el saldo de vacaciones de toda la plantilla sale mal en silencio. Aquí se calculan, y la tabla
/// de la base se siembra DESDE aquí.</para>
///
/// <para><b>Por qué importa para las vacaciones.</b> El artículo 76 concede días LABORABLES. Un día
/// de descanso obligatorio no es laborable, así que no se consume: quien pide del 24 al 26 de
/// diciembre gasta dos días, no tres. Contarlos sería cobrarle de más a quien los pide.</para>
///
/// <para><b>Lo que aquí NO se inventa.</b> La fracción IX del artículo 74 —el día de la jornada
/// electoral— no se genera. Depende de las leyes electorales federales y locales de cada año y de si
/// la elección es ordinaria; adivinarlo sería peor que no tenerlo. En la práctica la jornada federal
/// cae en domingo desde la reforma de 2014, así que no consume ningún día laborable; las locales sí
/// pueden caer entre semana, y para eso la tabla admite días añadidos a mano.</para>
/// </summary>
public static class FestivosDeLey
{
    /// <summary>
    /// El primer año que se sabe calcular bien. Antes de 2023 la tabla del artículo 74 era la misma,
    /// pero esta casa no tiene datos anteriores y afirmar que se cubre lo que no se ha comprobado es
    /// justo lo que hace que alguien confíe de más.
    /// </summary>
    public const int PrimerAnioCubierto = 2023;

    /// <summary>
    /// Un año en que hubo —o habrá— transmisión del Poder Ejecutivo Federal, para anclar el ciclo de
    /// seis años de la fracción VII. 2024 es el último ocurrido (1 de octubre de 2024).
    /// </summary>
    private const int AnioDeTransmision = 2024;

    /// <summary>Un día de descanso obligatorio: cuándo cae y de qué fracción sale.</summary>
    /// <param name="Motivo">Con qué palabras se le explica a quien mire el calendario. Lleva la
    /// fracción porque cuando alguien discute un día, lo primero que hace falta es saber cuál es.</param>
    public readonly record struct DiaDeLey(DateOnly Fecha, string Motivo);

    /// <summary>
    /// Los días de descanso obligatorio de un año, en orden.
    /// </summary>
    public static IReadOnlyList<DiaDeLey> DelAnio(int anio)
    {
        var dias = new List<DiaDeLey>
        {
            new(new DateOnly(anio, 1, 1),   "Año nuevo (art. 74 fr. I)"),
            new(LunesNDe(anio, 2, 1),       "Primer lunes de febrero, por el 5 de febrero (art. 74 fr. II)"),
            new(LunesNDe(anio, 3, 3),       "Tercer lunes de marzo, por el 21 de marzo (art. 74 fr. III)"),
            new(new DateOnly(anio, 5, 1),   "Día del trabajo (art. 74 fr. IV)"),
            new(new DateOnly(anio, 9, 16),  "Independencia (art. 74 fr. V)"),
            new(LunesNDe(anio, 11, 3),      "Tercer lunes de noviembre, por el 20 de noviembre (art. 74 fr. VI)"),
            new(new DateOnly(anio, 12, 25), "Navidad (art. 74 fr. VIII)")
        };

        // La transmisión del Poder Ejecutivo Federal: el 1 de octubre, una vez cada seis años.
        if (HayTransmision(anio))
            dias.Add(new DiaDeLey(new DateOnly(anio, 10, 1),
                "Transmisión del Poder Ejecutivo Federal (art. 74 fr. VII)"));

        return [.. dias.OrderBy(d => d.Fecha)];
    }

    /// <summary>Los días de un rango de años, ambos incluidos.</summary>
    public static IReadOnlyList<DiaDeLey> DeLosAnios(int desde, int hasta) =>
        desde > hasta
            ? []
            : [.. Enumerable.Range(desde, hasta - desde + 1).SelectMany(DelAnio)];

    /// <summary>
    /// ¿Toca transmisión del Poder Ejecutivo este año? Cada seis, anclado en 2024.
    ///
    /// <para>Se calcula con módulo y no con una lista para que no haga falta volver aquí en 2030. El
    /// resto se normaliza porque en C# el operador de módulo conserva el signo del dividendo, y con
    /// un año anterior al ancla daría negativo y no acertaría nunca.</para>
    /// </summary>
    private static bool HayTransmision(int anio) => ((anio - AnioDeTransmision) % 6 + 6) % 6 == 0;

    /// <summary>
    /// El enésimo lunes de un mes. <paramref name="cual"/> es 1 para el primero, 3 para el tercero.
    ///
    /// <para>Se cuenta hacia adelante desde el día 1 y no hacia atrás desde el final: «el tercer
    /// lunes» es el tercero que hay, no «el último menos uno», y en un mes que empieza en lunes esas
    /// dos cuentas dan días distintos.</para>
    /// </summary>
    private static DateOnly LunesNDe(int anio, int mes, int cual)
    {
        var primero = new DateOnly(anio, mes, 1);

        // Cuántos días faltan del día 1 hasta el primer lunes. Si el 1 ya es lunes, ninguno.
        int hastaElPrimerLunes = ((int)DayOfWeek.Monday - (int)primero.DayOfWeek + 7) % 7;

        return primero.AddDays(hastaElPrimerLunes + 7 * (cual - 1));
    }
}
