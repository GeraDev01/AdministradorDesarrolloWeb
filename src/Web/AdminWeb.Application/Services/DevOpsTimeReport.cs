using AdminWeb.Shared.Enums;

namespace AdminWeb.Application.Services;

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
/// Lógica pura del reporte de tiempo a DevOps: claves de configuración, lectura del modo y el
/// cálculo del delta aún no reportado.
///
/// <para>Conserva el nombre del escritorio para poder cotejarlo con el original durante el corte. No
/// toca la base ni la red a propósito: el delta mal calculado duplica horas en el ticket de alguien,
/// y eso tiene que poder probarse.</para>
/// </summary>
public static class DevOpsTimeReport
{
    public const string ClaveHabilitado = "DevOpsTimeReportEnabled";
    public const string ClaveModo = "DevOpsTimeReportMode";
    public const string ClaveReducirRestante = "DevOpsTimeReportReduceRemaining";

    public static DevOpsTimeMode LeerModo(string? valor) => (valor ?? "").Trim().ToLowerInvariant() switch
    {
        "campos" or "fields" => DevOpsTimeMode.Campos,
        "ambos" or "both" => DevOpsTimeMode.Ambos,
        _ => DevOpsTimeMode.Comentario
    };

    public static string ModoATexto(DevOpsTimeMode modo) => modo switch
    {
        DevOpsTimeMode.Campos => "campos",
        DevOpsTimeMode.Ambos => "ambos",
        _ => "comentario"
    };

    public static bool ActualizaCampos(DevOpsTimeMode modo) =>
        modo is DevOpsTimeMode.Campos or DevOpsTimeMode.Ambos;

    public static bool Comenta(DevOpsTimeMode modo) =>
        modo is DevOpsTimeMode.Comentario or DevOpsTimeMode.Ambos;

    /// <summary>Segundos nuevos aún no reportados. Nunca negativo.</summary>
    public static int DeltaSegundos(int totalSegundos, int reportadoSegundos) =>
        Math.Max(0, totalSegundos - reportadoSegundos);

    /// <summary>Redondea segundos a horas con 2 decimales, que es la unidad de Completed/Remaining Work.</summary>
    public static double SegundosAHoras(int segundos) => Math.Round(segundos / 3600.0, 2);

    /// <summary>
    /// Segundos que representan unas horas ya redondeadas a 2 decimales.
    ///
    /// Sirve para avanzar la marca de tiempo reportado SOLO por lo que de verdad se envió a Completed
    /// Work, y así arrastrar el resto —el redondeo— al próximo reporte en vez de perderlo.
    /// </summary>
    public static int HorasASegundos(double horas) => (int)Math.Round(horas * 3600.0);

    /// <summary>
    /// Si al requerimiento se le puede reportar tiempo a DevOps: es de origen Azure DevOps y su
    /// identificador externo es un número de work item válido.
    /// </summary>
    public static bool AplicaA(RequirementSource origen, string? identificadorExterno, out int numeroDeWorkItem)
    {
        numeroDeWorkItem = 0;
        return origen == RequirementSource.AzureDevOps
            && int.TryParse((identificadorExterno ?? "").Trim(), out numeroDeWorkItem)
            && numeroDeWorkItem > 0;
    }
}
