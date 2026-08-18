using System.Net;
using System.Text.RegularExpressions;

namespace AdminWeb.Application.Services;

/// <summary>
/// Lo que llega de Azure DevOps en HTML, convertido en texto.
///
/// <para>Existe para que haya UNA sola limpieza y no tres. Lo que viene de allá —comentarios y
/// descripciones de work item— es HTML que escribe gente de FUERA del equipo, y se limpia en el
/// SERVIDOR y no en la pantalla, para que del lado del navegador no exista siquiera un texto con
/// marcado que alguien pueda acabar pintando. Tres copias de esa regla serían tres oportunidades de
/// que una se quedara atrás el día que haga falta endurecerla.</para>
/// </summary>
public static partial class TextoDeDevOps
{
    /// <summary>
    /// Quita el marcado y deja texto corrido.
    ///
    /// <para>La decodificación va DESPUÉS de quitar las etiquetas: al revés, un
    /// «&amp;lt;script&amp;gt;» se convertiría en una etiqueta de verdad.</para>
    /// </summary>
    public static string ATextoPlano(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var sinEtiquetas = EtiquetasHtml().Replace(html, " ");
        var texto = WebUtility.HtmlDecode(sinEtiquetas);
        return string.Join(' ', texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    // El tiempo de espera acota lo que puede costar un documento rebuscado: el HTML lo escribe
    // cualquiera al otro lado y llega por la red, así que no puede decidir cuánto trabaja el servidor.
    [GeneratedRegex("<[^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex EtiquetasHtml();
}
