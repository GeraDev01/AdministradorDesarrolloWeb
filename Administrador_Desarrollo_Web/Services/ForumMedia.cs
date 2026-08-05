namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Reconoce el tipo de una imagen por sus BYTES, no por su extensión.
///
/// Importa porque lo adjuntado se guarda y después se vuelca a un archivo temporal que se abre con
/// el programa asociado: si bastara con llamar «foto.png» a un ejecutable para que el foro lo
/// aceptara, abrir la «imagen» de un compañero lo ejecutaría. Aquí solo pasan formatos que de
/// verdad son imágenes.
/// </summary>
public static class ForumMedia
{
    /// <summary>Tipo MIME de la imagen, o null si esos bytes no son una imagen reconocida.</summary>
    public static string? TipoDeImagen(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 12) return null;

        // WEBP queda fuera a propósito: GDI+ (lo que usa WinForms para pintar y para sacar la
        // miniatura) no sabe decodificarlo, así que aceptarlo aquí sería guardar una imagen que
        // después no se puede enseñar.
        if (Empieza(bytes, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return "image/png";
        if (Empieza(bytes, 0xFF, 0xD8, 0xFF))                               return "image/jpeg";
        if (Empieza(bytes, 0x47, 0x49, 0x46, 0x38))                         return "image/gif";   // GIF8
        if (Empieza(bytes, 0x42, 0x4D))                                     return "image/bmp";

        return null;
    }

    /// <summary>Extensión con la que volcar la imagen al abrirla o guardarla.</summary>
    public static string ExtensionDe(string? tipo) => tipo switch
    {
        "image/jpeg" => ".jpg",
        "image/gif"  => ".gif",
        "image/bmp"  => ".bmp",
        _            => ".png"
    };

    /// <summary>«1.4 MB», para decirle a alguien por qué no cabe su captura.</summary>
    public static string Tamano(long bytes)
    {
        string[] u = ["B", "KB", "MB", "GB"];
        double n = bytes; int i = 0;
        while (n >= 1024 && i < u.Length - 1) { n /= 1024; i++; }
        return $"{n:0.#} {u[i]}";
    }

    private static bool Empieza(byte[] bytes, params byte[] firma)
    {
        if (bytes.Length < firma.Length) return false;
        for (int i = 0; i < firma.Length; i++) if (bytes[i] != firma[i]) return false;
        return true;
    }
}
