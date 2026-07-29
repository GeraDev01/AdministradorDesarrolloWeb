using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Services;

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

public record EstimationRow(int ReqId, string Title, RequirementStatus Estado, double EstimateHrs, double ActualHrs, EstimationClass Clase)
{
    public double DeltaHrs => Math.Round(ActualHrs - EstimateHrs, 2);
    public double? Ratio => EstimateHrs > 0 && ActualHrs > 0 ? Math.Round(ActualHrs / EstimateHrs, 2) : null;
}

public record EstimationSummary(int ConDatos, int Precisos, int Subestimados, int Sobreestimados,
    double RatioPromedio, double HorasEstimadas, double HorasReales);

/// <summary>
/// Precisión de las estimaciones: compara las horas estimadas de un requerimiento con el tiempo real
/// cronometrado. Lógica pura y testeable (no toca base ni red).
/// </summary>
public static class EstimationStats
{
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

    public static EstimationRow Fila(EstimationInput i)
    {
        double est = (double)(i.EstimateHours ?? 0);
        double act = SegundosAHoras(i.ActualSeconds);
        return new EstimationRow(i.ReqId, i.Title, i.Status, est, act, Clasificar(est, act));
    }

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
