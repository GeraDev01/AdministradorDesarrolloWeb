using AdminWeb.Shared.Enums;
using Radzen;

namespace AdminWeb.Client.Paginas.Conocimiento;

/// <summary>
/// Las decisiones de presentación que comparten las cuatro pantallas de la base de conocimiento:
/// con qué color se pinta cada estado, cómo se parten las etiquetas y a partir de cuántos días una
/// espera deja de ser normal.
///
/// <para>Están en un solo sitio porque son las que se desincronizan sin que nadie lo note: si la
/// lista pintara «Devuelto» en gris y el artículo lo pintara en rojo, la misma cosa parecería dos
/// cosas distintas según por dónde se llegue.</para>
/// </summary>
internal static class PresentacionDeConocimiento
{
    /// <summary>
    /// El color de cada estado.
    ///
    /// <para>Publicado va en VERDE y no en gris neutro: es el único estado que significa que el
    /// trabajo terminó, y quien recorre su propia lista busca precisamente distinguir lo que ya
    /// llegó de lo que sigue en el aire. «Por revisar» va en ámbar —está esperando a alguien— y
    /// «Devuelto» en rojo, porque pide una acción de su autor y es lo único de la lista que se puede
    /// quedar parado para siempre si no se ve.</para>
    /// </summary>
    public static BadgeStyle DeEstado(KnowledgeStatus estado) => estado switch
    {
        KnowledgeStatus.Publicado  => BadgeStyle.Success,
        KnowledgeStatus.PorRevisar => BadgeStyle.Warning,
        KnowledgeStatus.Rechazado  => BadgeStyle.Danger,
        _                          => BadgeStyle.Light
    };

    /// <summary>
    /// El color de una espera en la cola del líder.
    ///
    /// <para>Los cortes son 3 y 7 días, y no son redondos por casualidad: tres días es lo que tarda
    /// quien escribió algo en preguntarse si alguien lo va a leer, y una semana es cuando decide que
    /// no. La cola se ordena por antigüedad, pero el orden por sí solo no dice si el primero lleva
    /// dos horas o nueve días — el color sí.</para>
    /// </summary>
    public static BadgeStyle DeEspera(int dias) => dias switch
    {
        >= 7 => BadgeStyle.Danger,
        >= 3 => BadgeStyle.Warning,
        _    => BadgeStyle.Light
    };

    /// <summary>«hoy», «ayer», «hace 4 días» — cuánto lleva algo esperando, en palabras.</summary>
    public static string Espera(int dias) => dias switch
    {
        0 => "hoy",
        1 => "ayer",
        _ => $"hace {dias} días"
    };

    /// <summary>
    /// Parte la cadena de etiquetas en etiquetas sueltas para poder pintarlas una a una y hacer que
    /// cada una filtre. El servidor las guarda separadas por coma y ya normalizadas en minúsculas;
    /// aquí no se cambia ninguna, solo se separan.
    /// </summary>
    public static IReadOnlyList<string> Etiquetas(string? etiquetas) =>
        string.IsNullOrWhiteSpace(etiquetas)
            ? []
            : etiquetas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
