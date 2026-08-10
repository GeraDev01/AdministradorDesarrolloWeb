using System.Text.RegularExpressions;

namespace AdminWeb.Application.Services;

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
/// <b>Solo http y https.</b> Todo lo demás —<c>file://</c> hacia un recurso de red,
/// <c>javascript:</c>, <c>data:</c>, una ruta a un <c>.exe</c>— se queda como texto llano y no se
/// vuelve pulsable. La validación es la MISMA que en el escritorio, pero lo que evita ya no es lo
/// mismo: allí un enlace acababa en <c>Process.Start</c> y un esquema sin filtrar convertía una
/// publicación en un lanzador de programas; aquí acaba en el <c>href</c> de un ancla, y un
/// <c>javascript:</c> o un <c>data:text/html</c> se ejecutan en el navegador de quien lee, con su
/// sesión. Por eso <see cref="EsEnlaceSeguro"/> se vuelve a llamar justo antes de pintar, y no solo
/// aquí.
///
/// <b>CÓMO SE EVITA EL XSS ALMACENADO AQUÍ.</b> El cuerpo de una entrada lo escribe cualquiera con
/// sesión y lo leen todos los demás: es el vector clásico. La defensa son tres cosas, y ninguna es
/// opcional:
/// <list type="number">
/// <item>El cuerpo <b>nunca sale hacia el navegador como texto crudo</b>: el DTO viaja ya troceado
/// en <see cref="ForumSegmento"/>, resultado de <see cref="Analizar"/> en el servidor.</item>
/// <item>El componente que lo pinta emite el texto <b>como texto</b> — nunca con
/// <c>MarkupString</c> ni <c>innerHTML</c>—, y las anclas las construye a partir de esos segmentos.</item>
/// <item>Un segmento solo es enlace si su dirección pasó <see cref="EsEnlaceSeguro"/>. La decisión
/// se toma en el servidor: el cliente corre en la máquina de quien lee y se puede manipular.</item>
/// </list>
/// Por eso <see cref="Sanear"/> no escapa HTML: no haría falta y sí estropearía el texto de la
/// gente. Si algún día el cuerpo pasara a admitir marcado de verdad, esas tres reglas dejarían de
/// bastar y haría falta un saneador de HTML —y ese es el momento de volver a leer esto—.
/// </summary>
public static class ForumRichText
{
    /// <summary>Tope defensivo: un cuerpo puede traer 20 000 caracteres, pero no infinitos enlaces.</summary>
    public const int MaxEnlaces = 200;

    /// <summary>Tope del cuerpo de una entrada. Lo aplica el saneado, no solo la pantalla.</summary>
    public const int MaxCuerpo = 20_000;

    /// <summary>
    /// Limpia el cuerpo de una entrada ANTES de guardarlo. Es el único sitio por donde pasa el texto
    /// que escribe una persona y leen todas las demás.
    ///
    /// <para><b>Deliberadamente NO escapa HTML, y eso no es un descuido.</b> El cuerpo de una entrada
    /// no es marcado: es texto llano con enlaces en forma de <c>[etiqueta](url)</c>. Nunca llega al
    /// navegador como HTML — el DTO viaja troceado en <see cref="ForumSegmento"/> y el componente lo
    /// emite como texto, que es donde está la protección real contra el XSS almacenado. Escapar aquí
    /// además de allí no protegería de nada y sí estropearía lo que la gente escribe: quien
    /// comentara «hay que revisar si a &lt; b» acabaría leyendo «a &amp;lt; b» en su propia
    /// publicación.</para>
    ///
    /// <para><b>La regla que sostiene todo esto</b>: el cuerpo se pinta como TEXTO y los enlaces se
    /// construyen a partir de los segmentos que devuelve <see cref="Analizar"/>, con la URL ya
    /// validada por <see cref="EsEnlaceSeguro"/>. Pintarlo con <c>MarkupString</c> o
    /// <c>innerHTML</c> abre un XSS almacenado para todo el equipo — y entonces sí haría falta un
    /// saneador de HTML de verdad, no este método.</para>
    ///
    /// Lo que sí hace, que es lo que le corresponde a un texto llano: uniformar los saltos de línea,
    /// quitar los caracteres de control invisibles (rompen el pintado, ensucian la bitácora y sirven
    /// para disfrazar una dirección) y recortar al tope.
    /// </summary>
    public static string Sanear(string? cuerpo)
    {
        if (string.IsNullOrEmpty(cuerpo)) return "";

        // Saltos de línea uniformes: quien escribe desde el navegador manda \r\n y quien lo hace
        // desde otro sitio manda \n; sin uniformar, el mismo texto ocupa distinto y se recorta
        // distinto según de dónde venga.
        var texto = cuerpo.Replace("\r\n", "\n").Replace('\r', '\n');

        var limpio = new System.Text.StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            // Se conservan el salto de línea y el tabulador: son formato, no basura.
            if (c is '\n' or '\t') { limpio.Append(c); continue; }

            // Fuera los demás controles, incluidos los de dirección del texto (U+202A..U+202E), que
            // sirven para que una dirección se lea al revés de como se abre.
            if (char.IsControl(c) || (c >= '‪' && c <= '‮') || c == '﻿') continue;

            limpio.Append(c);
        }

        var resultado = limpio.ToString().Trim();
        return resultado.Length <= MaxCuerpo ? resultado : resultado[..MaxCuerpo];
    }

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

    /// <summary>¿Hay al menos un enlace? Evita montar el marcado del enlace cuando no hace falta.</summary>
    public static bool TieneEnlaces(string? texto) => Analizar(texto).Any(s => s.EsEnlace);

    /// <summary>
    /// Valida y normaliza una dirección. Devuelve false para todo lo que no sea http/https —
    /// incluidos los esquemas que ejecutarían código en el navegador de quien lee.
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
