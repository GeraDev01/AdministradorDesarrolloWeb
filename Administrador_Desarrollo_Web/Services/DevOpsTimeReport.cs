using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Cómo se reporta el tiempo cronometrado al ticket de Azure DevOps.</summary>
public enum DevOpsTimeMode
{
    /// <summary>Un comentario con el tiempo dedicado (funciona en cualquier tipo de work item).</summary>
    Comentario = 0,
    /// <summary>Los campos Completed Work / Remaining Work (solo tipos que los tengan: Task, Bug…).</summary>
    Campos = 1,
    /// <summary>Ambos: comentario y campos.</summary>
    Ambos = 2
}

/// <summary>
/// Lógica pura del reporte de tiempo a DevOps: claves de configuración, parseo del modo y el cálculo
/// del delta aún no reportado. Sin base ni red, para poder probarla.
/// </summary>
public static class DevOpsTimeReport
{
    public const string KeyEnabled         = "DevOpsTimeReportEnabled";
    public const string KeyMode            = "DevOpsTimeReportMode";
    public const string KeyReduceRemaining = "DevOpsTimeReportReduceRemaining";

    public static DevOpsTimeMode ParseMode(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "campos" or "fields" => DevOpsTimeMode.Campos,
        "ambos"  or "both"   => DevOpsTimeMode.Ambos,
        _                    => DevOpsTimeMode.Comentario
    };

    public static string ModeToString(DevOpsTimeMode m) => m switch
    {
        DevOpsTimeMode.Campos => "campos",
        DevOpsTimeMode.Ambos  => "ambos",
        _                     => "comentario"
    };

    public static bool ActualizaCampos(DevOpsTimeMode m) => m is DevOpsTimeMode.Campos or DevOpsTimeMode.Ambos;
    public static bool Comenta(DevOpsTimeMode m)         => m is DevOpsTimeMode.Comentario or DevOpsTimeMode.Ambos;

    /// <summary>Segundos nuevos aún no reportados (nunca negativo).</summary>
    public static int DeltaSegundos(int totalSegundos, int reportadoSegundos) =>
        Math.Max(0, totalSegundos - reportadoSegundos);

    /// <summary>Redondea segundos a horas con 2 decimales (la unidad de Completed/Remaining Work).</summary>
    public static double SegundosAHoras(int segundos) => Math.Round(segundos / 3600.0, 2);

    /// <summary>
    /// Segundos que representan unas horas ya redondeadas a 2 decimales. Sirve para avanzar el
    /// «watermark» de tiempo reportado SOLO por lo que de verdad se envió a Completed Work, y así
    /// arrastrar el resto (el redondeo) al próximo reporte en vez de perderlo.
    /// </summary>
    public static int HorasASegundos(double horas) => (int)Math.Round(horas * 3600.0);

    /// <summary>
    /// true si al requerimiento se le puede reportar tiempo a DevOps: es de origen Azure DevOps y su
    /// ExternalId es un número de work item válido (devuelto en <paramref name="workItemId"/>).
    /// </summary>
    public static bool AplicaA(RequirementSource source, string? externalId, out int workItemId)
    {
        workItemId = 0;
        return source == RequirementSource.AzureDevOps
            && int.TryParse((externalId ?? "").Trim(), out workItemId)
            && workItemId > 0;
    }
}
