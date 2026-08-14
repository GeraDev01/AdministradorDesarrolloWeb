namespace AdminWeb.Domain.Calculo;

/// <summary>
/// Días de vacaciones que marca la Ley Federal del Trabajo de México tras la reforma
/// "Vacaciones Dignas" (DOF 27-dic-2022, vigente desde el 1-ene-2023), artículo 76.
///
/// Art. 76: al cumplir el 1.er año son 12 días y aumentan 2 por año hasta llegar a 20 al 5.º año;
/// a partir del 6.º año el periodo sube 2 días por cada 5 años de servicio (6-10→22, 11-15→24, …).
/// Art. 79 §2: quien aún no cumple un año NO tiene días que disfrutar. Lo que le corresponde es la
/// REMUNERACIÓN proporcional a los días trabajados, y solo al terminar la relación de trabajo: es
/// finiquito, no saldo. Aquí decía «art. 77», que es el de trabajadores discontinuos y de temporada
/// y no viene al caso.
///
/// El número NO es acumulativo: es el periodo anual que corresponde a esa antigüedad.
/// Cálculo puro (sin dependencias) para poder probarlo con precisión.
///
/// <para>Copiado del escritorio SIN cambios de comportamiento, a propósito: es una regla de ley, y
/// las dos aplicaciones tienen que dar el mismo número el día del corte. Lo único que cambia es
/// dónde vive —aquí es del dominio, no de una pantalla— y que la fecha de corte se pasa siempre
/// como parámetro en vez de leer el reloj por dentro. Eso último no es adorno: en un servidor el
/// reloj es UTC y quien pregunta está en otro huso, así que la fecha del "hoy" la decide el
/// llamador, que es el único que sabe de qué día está hablando.</para>
/// </summary>
public static class LftVacaciones
{
    /// <summary>Años de servicio COMPLETOS entre la fecha de ingreso y la fecha de corte.</summary>
    public static int AniosCumplidos(DateTime ingreso, DateTime corte)
    {
        var a = ingreso.Date;
        var b = corte.Date;
        if (b <= a) return 0;

        int anios = b.Year - a.Year;
        // Si el aniversario de este año todavía no llega, aún no se cumple ese año de servicio.
        if (b < AniversarioEn(a, a.Year + anios)) anios--;
        return Math.Max(0, anios);
    }

    /// <summary>
    /// Días del periodo anual que corresponden a esa antigüedad en años cumplidos (Art. 76).
    /// Para 0 años devuelve 0: la parte proporcional del primer año se resuelve en
    /// <see cref="DiasCorrespondientes"/>.
    /// </summary>
    public static int DiasPorAnios(int aniosCumplidos)
    {
        if (aniosCumplidos <= 0) return 0;
        if (aniosCumplidos <= 5) return 10 + 2 * aniosCumplidos;   // 1→12, 2→14, 3→16, 4→18, 5→20
        return 22 + 2 * ((aniosCumplidos - 6) / 5);                // 6-10→22, 11-15→24, 16-20→26, …
    }

    /// <summary>
    /// Días que le corresponden al trabajador a la fecha de corte. Si aún no cumple un año,
    /// devuelve la parte proporcional de los 12 días del primer año según los días trabajados
    /// (Art. 79 §2: es la remuneración del finiquito, no días que se puedan pedir).
    /// </summary>
    public static int DiasCorrespondientes(DateTime ingreso, DateTime corte)
    {
        int anios = AniosCumplidos(ingreso, corte);
        if (anios >= 1) return DiasPorAnios(anios);

        // Primer año incompleto: proporcional de 12 días sobre los días trabajados.
        int diasTrabajados = Math.Max(0, (corte.Date - ingreso.Date).Days);
        return (int)Math.Round(12.0 * Math.Min(diasTrabajados, 365) / 365.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// La nota que se enseña junto al campo, ya redactada. En el escritorio este texto se armaba
    /// dentro del formulario; aquí vive con el cálculo para que la pantalla no tenga que repetir la
    /// regla de "menos de un año" y las dos aplicaciones digan exactamente lo mismo.
    /// </summary>
    public static string Nota(DateTime ingreso, DateTime corte)
    {
        int anios = AniosCumplidos(ingreso, corte);
        return anios >= 1
            ? $"LFT: {anios} año(s) de antigüedad → {DiasPorAnios(anios)} días al año."
            : "LFT: menos de un año → proporcional del primer año (12 días).";
    }

    /// <summary>El aniversario de <paramref name="ingreso"/> en el año dado, tratando el 29-feb
    /// como el último día de febrero cuando el año destino no es bisiesto.</summary>
    private static DateTime AniversarioEn(DateTime ingreso, int anio)
    {
        int dia = Math.Min(ingreso.Day, DateTime.DaysInMonth(anio, ingreso.Month));
        return new DateTime(anio, ingreso.Month, dia);
    }
}
