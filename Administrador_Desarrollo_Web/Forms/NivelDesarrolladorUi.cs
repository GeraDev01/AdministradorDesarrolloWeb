namespace Administrador_Desarrollo_Web.Forms;

/// <summary>
/// Cómo se pinta el nivel (seniority) de un desarrollador. Vive en la UI y no en el modelo porque
/// el icono y el color son decisiones visuales; el dato guardado sigue siendo el texto de siempre.
///
/// El campo es texto libre —viene de una ficha que pudo capturarse a mano antes de que fuera un
/// combo cerrado—, así que se compara sin distinguir mayúsculas ni espacios y todo lo que no
/// reconozca se muestra tal cual en vez de esconderse.
/// </summary>
internal static class NivelDesarrolladorUi
{
    /// <summary>Etiqueta con icono: «🌳 Senior». Sin nivel capturado devuelve un aviso, no vacío.</summary>
    public static string Etiqueta(string? nivel)
    {
        var n = (nivel ?? "").Trim();
        if (n.Length == 0) return "— sin nivel registrado —";

        return n.ToLowerInvariant() switch
        {
            "junior"     => "🌱 Junior",
            "mid"        => "🌿 Mid",
            "senior"     => "🌳 Senior",
            "lead"       => "🧭 Lead",
            "arquitecto" => "🏛 Arquitecto",
            _            => n   // un nivel escrito a mano se respeta tal cual
        };
    }

    public static Color Color(string? nivel) => (nivel ?? "").Trim().ToLowerInvariant() switch
    {
        "junior"     => AppTheme.Success,
        "mid"        => AppTheme.SidebarActive,
        "senior"     => AppTheme.HeaderBg,
        "lead"       => AppTheme.Warning,
        "arquitecto" => AppTheme.Warning,
        ""           => AppTheme.TextSecondary,
        _            => AppTheme.TextPrimary
    };

    /// <summary>Qué significa el nivel, para el tooltip. Null si no hay nada que explicar.</summary>
    public static string? Explicacion(string? nivel) => (nivel ?? "").Trim().ToLowerInvariant() switch
    {
        "junior" => "Estás aprendiendo el oficio y el proyecto. Se espera que preguntes pronto y a menudo.",
        "mid"    => "Trabajas de forma autónoma en lo tuyo y respondes por lo que entregas.",
        "senior" => "Además de tu trabajo, se espera que guíes decisiones técnicas y apoyes a los demás.",
        "lead"   => "Coordinas y evalúas: por eso tu nivel queda fuera del ranking individual.",
        "arquitecto" => "Defines el rumbo técnico más allá de un proyecto concreto.",
        ""       => "Tu ficha no tiene nivel capturado. Pídeselo al líder si te hace falta.",
        _        => null
    };
}
