using System.Text.Json;

namespace AdminWeb.Application.Services;

/// <summary>
/// Política de SLA automático para una prioridad de Azure DevOps: cuántas horas hay para atender el
/// ticket y cada cuántas horas recordar que hay que comentarlo. La prioridad en DevOps va de 1 (la
/// más alta) a 4 (la más baja).
///
/// <b>Los nombres de sus propiedades son un contrato, no una preferencia de estilo.</b> Estas cuatro
/// políticas se guardan como JSON en una sola fila de configuración que la aplicación de escritorio
/// lee y escribe hasta el corte; traducir los nombres al español dejaría al escritorio leyendo un
/// JSON que no entiende y cayendo en silencio a los valores por omisión — es decir, desactivando los
/// SLA automáticos sin que nadie lo note.
/// </summary>
public readonly record struct SlaPolicy(int Priority, bool Enabled, int Hours, int ReminderEveryHours);

/// <summary>
/// Guarda y resuelve las políticas de SLA por prioridad. Se persisten como JSON en una sola clave de
/// configuración (<see cref="ClaveDeConfiguracion"/>); toda la lógica de parseo, saneo y resolución
/// es pura (no toca base ni red) y siempre trabaja con exactamente una política por prioridad 1..4.
/// </summary>
public static class SlaPolicyStore
{
    /// <summary>Clave en AppSettings donde se guarda el JSON con las cuatro políticas.</summary>
    public const string ClaveDeConfiguracion = "SlaAutoPolicies";

    /// <summary>Prioridades de Azure DevOps, de la más alta (1) a la más baja (4).</summary>
    public static readonly int[] Prioridades = [1, 2, 3, 4];

    /// <summary>Nombre legible de una prioridad de DevOps.</summary>
    public static string NombrePrioridad(int p) => p switch
    {
        1 => "Muy alta",
        2 => "Alta",
        3 => "Media",
        4 => "Baja",
        _ => $"P{p}"
    };

    /// <summary>Horas sugeridas por prioridad mientras el líder no las ajuste.</summary>
    private static int HorasPorDefecto(int p) => p switch
    {
        1 => 4,
        2 => 8,
        3 => 24,
        4 => 72,
        _ => 24
    };

    /// <summary>
    /// Políticas por defecto: todas DESACTIVADAS. Así, mientras el líder no active nada, no se crea
    /// ningún SLA automático — que es el comportamiento seguro por omisión.
    /// </summary>
    public static List<SlaPolicy> PorDefecto() =>
        Prioridades.Select(p => new SlaPolicy(p, false, HorasPorDefecto(p), 24)).ToList();

    /// <summary>
    /// Lee las políticas del JSON guardado y SIEMPRE devuelve exactamente una por cada prioridad
    /// 1..4, rellenando con las de por defecto lo que falte o esté corrupto. Nunca lanza: un JSON
    /// inválido cae a los valores por defecto en vez de dejar la pantalla en blanco.
    /// </summary>
    public static List<SlaPolicy> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return PorDefecto();

        List<SlaPolicy>? leidas;
        try { leidas = JsonSerializer.Deserialize<List<SlaPolicy>>(json); }
        catch { leidas = null; }

        return leidas == null ? PorDefecto() : Completar(leidas);
    }

    /// <summary>Serializa las cuatro políticas saneadas y en orden, para un contenido predecible.</summary>
    public static string Serialize(IEnumerable<SlaPolicy> politicas) =>
        JsonSerializer.Serialize(Completar(politicas));

    /// <summary>
    /// Resuelve la política que aplica a un ticket con la prioridad textual de DevOps ("1".."4").
    /// Devuelve null si la prioridad no es un número válido o si su política está desactivada.
    /// </summary>
    public static SlaPolicy? Resolver(IEnumerable<SlaPolicy> politicas, string? prioridadDevOps)
    {
        if (!int.TryParse((prioridadDevOps ?? "").Trim(), out var pr)) return null;
        foreach (var p in politicas)
            if (p.Priority == pr)
                return p.Enabled ? p : null;
        return null;
    }

    /// <summary>Normaliza una lista cualquiera a exactamente las cuatro prioridades, saneadas y ordenadas.</summary>
    private static List<SlaPolicy> Completar(IEnumerable<SlaPolicy> politicas)
    {
        var mapa = politicas
            .Where(p => Prioridades.Contains(p.Priority))
            .GroupBy(p => p.Priority)
            .ToDictionary(g => g.Key, g => Sanear(g.Last()));

        return Prioridades
            .Select(p => mapa.TryGetValue(p, out var pol) ? pol : new SlaPolicy(p, false, HorasPorDefecto(p), 24))
            .ToList();
    }

    /// <summary>Acota horas (1..8760) y recordatorio (0..720) a rangos con sentido.</summary>
    private static SlaPolicy Sanear(SlaPolicy p) => p with
    {
        Hours = Math.Clamp(p.Hours <= 0 ? HorasPorDefecto(p.Priority) : p.Hours, 1, 8760),
        ReminderEveryHours = Math.Clamp(p.ReminderEveryHours, 0, 720)
    };
}
