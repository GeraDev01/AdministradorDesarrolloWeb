namespace AdminWeb.Application.Services;

/// <summary>
/// Reconoce el tipo de una imagen por sus BYTES, no por su extensión.
///
/// En el escritorio importaba porque lo adjuntado se volcaba a un archivo temporal que se abría con
/// el programa asociado: llamar «foto.png» a un ejecutable habría bastado para que abrir la
/// «imagen» de un compañero lo ejecutara. En la web el peligro cambia de forma pero no desaparece:
/// lo adjuntado se sirve por HTTP con el Content-Type que aquí se decide, y un HTML o un SVG
/// servidos como imagen se ejecutan como script en el navegador de quien los abre —con la sesión
/// de esa persona. Por eso el tipo lo dictan los bytes y solo pasan formatos que de verdad son
/// imágenes de mapa de bits.
///
/// Quien sirva estos bytes debe además mandarlos con el Content-Type que devuelve
/// <see cref="TipoDeImagen"/> y con <c>X-Content-Type-Options: nosniff</c>, para que el navegador
/// no adivine por su cuenta.
/// </summary>
public static class ForumMedia
{
    /// <summary>Tipo MIME de la imagen, o null si esos bytes no son una imagen reconocida.</summary>
    public static string? TipoDeImagen(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 12) return null;

        // WEBP queda fuera. En el escritorio el motivo era técnico —GDI+ no sabía decodificarlo, así
        // que aceptarlo era guardar una imagen que después no se podía enseñar—; el navegador sí
        // sabe, pero la lista se conserva TAL CUAL a propósito: el escritorio sigue en producción
        // contra ESTA MISMA BASE hasta el corte, y una imagen subida desde la web que allá no se
        // pueda pintar deja un hilo roto en la mitad del equipo.
        if (Empieza(bytes, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return "image/png";
        if (Empieza(bytes, 0xFF, 0xD8, 0xFF))                               return "image/jpeg";
        if (Empieza(bytes, 0x47, 0x49, 0x46, 0x38))                         return "image/gif";   // GIF8
        if (Empieza(bytes, 0x42, 0x4D))                                     return "image/bmp";

        return null;
    }

    /// <summary>Extensión con la que ofrecer la imagen al descargarla.</summary>
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
