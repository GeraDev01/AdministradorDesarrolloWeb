namespace AdminWeb.Application.Services;

/// <summary>
/// Comprobaciones de lo que llega subido, para no repetirlas en cada endpoint que acepta un archivo.
///
/// Todas se hacen <b>en el servidor</b>. El cliente puede filtrar por comodidad —para avisar antes
/// de subir 20 MB por nada—, pero no cuenta como barrera: la API se puede llamar sin pasar por él.
///
/// Reconocer imágenes es cosa de <see cref="ForumMedia"/> y aquí se le delega: dos detectores que se
/// fueran separando acabarían aceptando cada uno lo que el otro rechaza, y el que manda es el que
/// decide el Content-Type con el que salen los bytes.
/// </summary>
public static class ArchivosSubidos
{
    /// <summary>Tope de un archivo. El mismo que el escritorio aplicaba a capturas y adjuntos.</summary>
    public const long MaxBytes = 15 * 1024 * 1024;

    /// <summary>
    /// Cuántas capturas caben en un comentario a un work item. Cada una es una subida aparte a Azure
    /// DevOps, y por eso hay tope.
    ///
    /// <para>Vive aquí, en un solo sitio, porque son TRES los caminos que acaban publicando por ahí
    /// —la pantalla de tickets, el avance de un SLA y el panel del pool— y tres números que se fueran
    /// separando harían que la misma persona pudiera adjuntar más desde una pantalla que desde otra
    /// sin que nada lo explicara.</para>
    /// </summary>
    public const int MaxEvidenciasPorComentario = 5;

    /// <summary>
    /// Extensiones que no se aceptan nunca. Vienen del escritorio, donde un adjunto acababa
    /// abriéndose con el programa asociado; aquí el peligro es distinto pero el criterio sigue
    /// valiendo: nada de lo que se guarda como evidencia necesita ser ejecutable.
    /// </summary>
    private static readonly HashSet<string> Prohibidas = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
        ".msi", ".msp", ".scr", ".hta", ".cpl", ".jar", ".reg", ".lnk", ".dll", ".sh",
        // SVG es marcado, no mapa de bits: servido como imagen ejecuta scripts con la sesión de
        // quien lo abre. Por eso ForumMedia tampoco lo reconoce.
        ".svg", ".svgz", ".htm", ".html", ".xhtml", ".mhtml"
    };

    /// <summary>
    /// Comprueba tamaño, nombre y extensión. Devuelve el nombre ya saneado, listo para guardar.
    ///
    /// Con <paramref name="soloImagenes"/> la comprobación que cuenta es la de los bytes, no la de la
    /// extensión: quien sube el archivo escribe el nombre y puede mentir.
    /// </summary>
    public static (bool ok, string error, string nombreSeguro) Validar(
        string? nombreOriginal, byte[] contenido, bool soloImagenes = false)
    {
        if (contenido.Length == 0) return (false, "El archivo llegó vacío.", "");
        if (contenido.LongLength > MaxBytes)
            return (false, $"El archivo pasa de {ForumMedia.Tamano(MaxBytes)}.", "");

        var nombre = NombreSeguro(nombreOriginal);
        if (nombre.Length == 0) return (false, "El archivo no trae nombre.", "");

        var extension = Path.GetExtension(nombre);
        if (Prohibidas.Contains(extension))
            return (false, $"No se admiten archivos «{extension}».", "");

        if (soloImagenes && ForumMedia.TipoDeImagen(contenido) == null)
            return (false, $"«{nombre}» no es una imagen (se admiten PNG, JPG, GIF y BMP).", "");

        return (true, "", nombre);
    }

    /// <summary>
    /// Deja el nombre en algo que se pueda guardar y mostrar sin sorpresas.
    ///
    /// Quita la ruta, los caracteres que no valen en un nombre de archivo y los de control. El
    /// recorte conserva la extensión: cortar por el final la perdería y el archivo dejaría de
    /// abrirse solo.
    /// </summary>
    public static string NombreSeguro(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return "";

        // Los dos separadores a mano, sin Path.GetFileName. Este código corre en el servidor y el
        // servidor es Linux: allí «\» no es separador, así que GetFileName devolvería
        // «C:\ruta\foto.png» entero. El nombre lo escribe un navegador que puede ser de Windows.
        var soloNombre = nombre.Replace('\\', '/');
        soloNombre = soloNombre[(soloNombre.LastIndexOf('/') + 1)..].Trim();

        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new string([.. soloNombre.Where(c => !invalidos.Contains(c) && !char.IsControl(c))]);

        // Los puntos del principio y del final: «..» a secas y los nombres ocultos de Unix.
        limpio = limpio.Trim(' ', '.');

        if (limpio.Length == 0) return "";
        if (limpio.Length <= 120) return limpio;

        var ext = Path.GetExtension(limpio);
        return ext.Length is > 0 and < 20
            ? string.Concat(Path.GetFileNameWithoutExtension(limpio).AsSpan(0, 120 - ext.Length), ext)
            : limpio[..120];
    }

    /// <summary>
    /// El tipo de contenido a partir de los BYTES, no del nombre.
    ///
    /// Es una decisión que viene del escritorio y conviene no perder: la extensión la escribe quien
    /// sube el archivo y puede mentir. Al servirlo se manda además <c>X-Content-Type-Options:
    /// nosniff</c>, para que el navegador tampoco intente adivinar por su cuenta.
    /// </summary>
    public static string TipoDeContenido(byte[] bytes)
    {
        if (ForumMedia.TipoDeImagen(bytes) is { } imagen) return imagen;

        if (bytes.Length >= 5 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
            return "application/pdf";

        // Lo que no se reconoce se sirve como binario: el navegador lo descarga en vez de intentar
        // interpretarlo, que es exactamente lo que se quiere de algo desconocido. Un .docx o un .xlsx
        // caen aquí (son ZIP por dentro) y descargarse es justo lo que se espera de ellos.
        return "application/octet-stream";
    }

    /// <summary>¿Se puede enseñar dentro de la página, o hay que descargarlo?</summary>
    public static bool SePuedePrevisualizar(string tipoDeContenido) =>
        tipoDeContenido.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        || tipoDeContenido == "application/pdf";
}
