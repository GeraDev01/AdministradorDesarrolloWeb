using System.Text.RegularExpressions;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Un trozo del cuerpo de una entrada: texto llano, o texto que abre un enlace.</summary>
/// <param name="Texto">Lo que se lee en pantalla.</param>
/// <param name="Url">Null en el texto llano; la dirección ya validada y normalizada en un enlace.</param>
public readonly record struct ForumSegmento(string Texto, string? Url)
{
    public bool EsEnlace => Url != null;
}

/// <summary>
/// Convierte el cuerpo de una entrada del foro en trozos de texto y enlaces pulsables.
///
/// Se reconocen dos formas: la dirección escrita tal cual (<c>https://…</c>, <c>www.…</c>) y la
/// forma con etiqueta, <c>[ver el ticket](https://…)</c>, que es la que salva a un hilo de quedar
/// empapelado de URLs de Azure DevOps de doscientos caracteres.
///
/// <b>Solo http y https.</b> Todo lo demás —<c>file://</c> hacia un recurso de red, <c>javascript:</c>,
/// una ruta a un <c>.exe</c>— se queda como texto llano y no se vuelve pulsable. Es la parte que de
/// verdad importa: un enlace del foro acaba en <c>Process.Start</c>, así que un esquema no filtrado
/// convertiría una publicación cualquiera en un lanzador de programas para todo el equipo. Por eso
/// <see cref="EsEnlaceSeguro"/> se vuelve a llamar justo antes de abrir, y no solo aquí.
///
/// Vive fuera del formulario porque es lo que se puede probar solo y lo que no puede fallar en
/// silencio.
/// </summary>
public static class ForumRichText
{
    /// <summary>Tope defensivo: un cuerpo puede traer 20 000 caracteres, pero no infinitos enlaces.</summary>
    public const int MaxEnlaces = 200;

    // Primero la forma con etiqueta; si no, una dirección suelta. El orden importa: sin él, la URL
    // de dentro de [texto](url) se detectaría sola y se comería los paréntesis.
    private static readonly Regex Patron = new(
        @"\[(?<txt>[^\]\r\n]{1,300})\]\((?<url>[^)\s]{1,2000})\)" +
        @"|(?<suelta>(?:https?://|www\.)[^\s<>""]{1,2000})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parte el texto en segmentos, en orden. Concatenar los <c>Texto</c> devuelve exactamente lo
    /// que el lector ve, de modo que quien pinte esto puede fijar los rangos de enlace por posición.
    /// </summary>
    public static List<ForumSegmento> Analizar(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return [];

        var salida = new List<ForumSegmento>();
        int cursor = 0, enlaces = 0;

        foreach (Match m in Patron.Matches(texto))
        {
            if (m.Index < cursor) continue;   // solapamiento: no debería ocurrir, pero no vale arriesgarse

            var (visible, url) = Interpretar(m);

            // Ni es un enlace válido ni hay cupo: se deja tal cual estaba escrito.
            if (url == null || enlaces >= MaxEnlaces) continue;

            if (m.Index > cursor) salida.Add(new ForumSegmento(texto[cursor..m.Index], null));

            salida.Add(new ForumSegmento(visible, url));
            enlaces++;

            // La cola recortada de una dirección suelta (el punto final de la frase, por ejemplo)
            // vuelve al texto: se lee igual, pero no forma parte del enlace.
            cursor = m.Index + (m.Groups["suelta"].Success ? visible.Length : m.Length);
        }

        if (cursor < texto.Length) salida.Add(new ForumSegmento(texto[cursor..], null));
        return salida;
    }

    /// <summary>¿Hay al menos un enlace? Evita construir un LinkLabel cuando no hace falta.</summary>
    public static bool TieneEnlaces(string? texto) => Analizar(texto).Any(s => s.EsEnlace);

    /// <summary>
    /// Valida y normaliza una dirección. Devuelve false para todo lo que no sea http/https —
    /// incluidos los esquemas que abrirían un programa en la máquina de quien lee.
    /// </summary>
    public static bool EsEnlaceSeguro(string? url, out string normalizada)
    {
        normalizada = "";
        if (string.IsNullOrWhiteSpace(url)) return false;

        var bruta = url.Trim();
        // Un salto o un carácter de control dentro de la cadena permitiría partir la dirección en
        // dos y enseñar una cosa mientras se abre otra.
        if (bruta.Any(char.IsControl)) return false;
        if (bruta.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) bruta = "https://" + bruta;

        if (!Uri.TryCreate(bruta, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrEmpty(uri.Host)) return false;

        normalizada = uri.AbsoluteUri;
        return true;
    }

    // ── Interno ──────────────────────────────────────────────────────────────────

    /// <summary>Saca de una coincidencia el texto que se ve y la dirección, o (…, null) si no vale.</summary>
    private static (string visible, string? url) Interpretar(Match m)
    {
        if (m.Groups["suelta"].Success)
        {
            var visible = RecortarCola(m.Groups["suelta"].Value);
            return EsEnlaceSeguro(visible, out var norm) ? (visible, norm) : (m.Value, null);
        }

        var etiqueta = m.Groups["txt"].Value.Trim();
        if (etiqueta.Length == 0) return (m.Value, null);
        return EsEnlaceSeguro(m.Groups["url"].Value, out var n) ? (etiqueta, n) : (m.Value, null);
    }

    /// <summary>
    /// Quita de una dirección suelta la puntuación que en realidad era de la frase: «mira
    /// https://ejemplo.com/guia.» no enlaza al punto final. Los cierres solo se quitan cuando
    /// sobran, porque hay URLs que los llevan de verdad (las de Wikipedia, por ejemplo).
    /// </summary>
    private static string RecortarCola(string s)
    {
        const string Puntuacion = ".,;:!?«»‹›…\"'";

        while (s.Length > 0)
        {
            char c = s[^1];

            if (c is ')' or ']' or '}')
            {
                char abre = c switch { ')' => '(', ']' => '[', _ => '{' };
                if (s.Count(x => x == c) <= s.Count(x => x == abre)) break;   // está balanceado: es parte de la URL
            }
            else if (Puntuacion.IndexOf(c) < 0) break;

            s = s[..^1];
        }
        return s;
    }
}
