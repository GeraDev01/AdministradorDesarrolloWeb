using AdminWeb.Shared.Enums;

namespace AdminWeb.Application.Services;

/// <summary>Datos mínimos de un requerimiento para medir precisión de estimación (independiente de EF).</summary>
public readonly record struct EstimationInput(int ReqId, string Title, decimal? EstimateHours, int ActualSeconds, RequirementStatus Status);

/// <summary>Cómo salió la estimación frente al tiempo real.</summary>
public enum EstimationClass
{
    /// <summary>Sin horas estimadas: no se puede medir.</summary>
    SinEstimacion,
    /// <summary>Con estimación pero aún sin tiempo cronometrado.</summary>
    SinTiempo,
    /// <summary>Real dentro de ±20 % del estimado.</summary>
    Preciso,
    /// <summary>Tomó bastante MÁS de lo estimado (ratio &gt; 1.2).</summary>
    Subestimado,
    /// <summary>Tomó bastante MENOS de lo estimado (ratio &lt; 0.8).</summary>
    Sobreestimado
}

/// <summary>Una fila del reporte: lo estimado, lo real y cómo se clasifica.</summary>
public record EstimationRow(int ReqId, string Title, RequirementStatus Estado, double EstimateHrs, double ActualHrs, EstimationClass Clase)
{
    public double DeltaHrs => Math.Round(ActualHrs - EstimateHrs, 2);
    public double? Ratio => EstimateHrs > 0 && ActualHrs > 0 ? Math.Round(ActualHrs / EstimateHrs, 2) : null;
}

/// <summary>Los agregados del reporte de estimación.</summary>
public record EstimationSummary(int ConDatos, int Precisos, int Subestimados, int Sobreestimados,
    double RatioPromedio, double HorasEstimadas, double HorasReales);

/// <summary>
/// Precisión de las estimaciones: compara las horas estimadas de un requerimiento con el tiempo real
/// cronometrado. Lógica pura y testeable (no toca base ni red).
///
/// <para><b>Copia literal del escritorio</b>, igual que <see cref="PerformanceScoringService"/> y
/// <see cref="WorkSessionService"/>: se conservan los nombres originales de tipos y miembros a
/// propósito. Estos umbrales llevan meses produciendo números que el equipo ya interpreta, y un
/// renombrado deja el mismo cálculo sin poder cotejarse contra el control del que salió mientras las
/// dos aplicaciones convivan. Lo único que cambia es el espacio de nombres y de dónde vienen los
/// enums (<c>AdminWeb.Shared.Enums</c>).</para>
/// </summary>
public static class EstimationStats
{
    /// <summary>Segundos cronometrados a horas, con dos decimales.</summary>
    public static double SegundosAHoras(int seg) => Math.Round(seg / 3600.0, 2);

    /// <summary>Clasifica según el ratio real/estimado (preciso dentro de ±20 %).</summary>
    public static EstimationClass Clasificar(double estimateHrs, double actualHrs)
    {
        if (estimateHrs <= 0) return EstimationClass.SinEstimacion;
        if (actualHrs   <= 0) return EstimationClass.SinTiempo;
        var ratio = actualHrs / estimateHrs;
        if (ratio > 1.2) return EstimationClass.Subestimado;
        if (ratio < 0.8) return EstimationClass.Sobreestimado;
        return EstimationClass.Preciso;
    }

    /// <summary>Una fila del reporte a partir de los datos crudos del requerimiento.</summary>
    public static EstimationRow Fila(EstimationInput i)
    {
        double est = (double)(i.EstimateHours ?? 0);
        double act = SegundosAHoras(i.ActualSeconds);
        return new EstimationRow(i.ReqId, i.Title, i.Status, est, act, Clasificar(est, act));
    }

    /// <summary>Las filas de todos los requerimientos que se le pasen.</summary>
    public static List<EstimationRow> Filas(IEnumerable<EstimationInput> items) => items.Select(Fila).ToList();

    /// <summary>Resumen agregado; el promedio y los conteos solo cuentan lo que tiene estimación Y tiempo.</summary>
    public static EstimationSummary Resumen(IEnumerable<EstimationRow> filas)
    {
        var lista = filas.ToList();
        var conDatos = lista.Where(f => f.Clase is EstimationClass.Preciso or EstimationClass.Subestimado or EstimationClass.Sobreestimado).ToList();

        int prec = conDatos.Count(f => f.Clase == EstimationClass.Preciso);
        int sub  = conDatos.Count(f => f.Clase == EstimationClass.Subestimado);
        int sob  = conDatos.Count(f => f.Clase == EstimationClass.Sobreestimado);
        double ratioProm = conDatos.Count == 0 ? 0 : Math.Round(conDatos.Average(f => f.ActualHrs / f.EstimateHrs), 2);
        double horEst  = Math.Round(lista.Sum(f => f.EstimateHrs), 1);
        double horReal = Math.Round(lista.Sum(f => f.ActualHrs), 1);

        return new EstimationSummary(conDatos.Count, prec, sub, sob, ratioProm, horEst, horReal);
    }

    /// <summary>Cómo se lee la clasificación en pantalla.</summary>
    public static string EtiquetaClase(EstimationClass c) => c switch
    {
        EstimationClass.SinEstimacion  => "Sin estimación",
        EstimationClass.SinTiempo      => "Sin tiempo aún",
        EstimationClass.Preciso        => "✓ Preciso",
        EstimationClass.Subestimado    => "▲ Subestimado (tomó más)",
        EstimationClass.Sobreestimado  => "▼ Sobreestimado (tomó menos)",
        _                              => c.ToString()
    };
}
