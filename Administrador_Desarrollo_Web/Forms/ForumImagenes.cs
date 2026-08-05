using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms;

/// <summary>
/// Prepara para el foro las imágenes que llegan del disco o del portapapeles, y las vuelve a sacar
/// cuando alguien las abre o las guarda.
///
/// Está en la capa de formularios porque todo esto es System.Drawing: decodificar, reescalar y
/// sacar la miniatura. <see cref="ForumService"/> no toca píxeles — solo comprueba que lo que
/// recibe sea de verdad una imagen y quepa.
///
/// Dos decisiones que valen la pena:
///  · Se REESCALA lo que pase de <see cref="LadoMaximo"/>. Una foto de móvil son 8 MB y 4000 px
///    para acabar viéndose en un recuadro; guardarla entera engorda una base compartida que se lee
///    por red sin que nadie note la diferencia en pantalla.
///  · Se guarda además una MINIATURA. Es lo que pinta el hilo y el muro, de modo que abrir el foro
///    no arrastre los originales de cada captura.
/// </summary>
public static class ForumImagenes
{
    /// <summary>Lado máximo del original guardado. Por encima, se reescala.</summary>
    public const int LadoMaximo = 1600;

    /// <summary>Lado de la miniatura que se pinta en el hilo y en el muro.</summary>
    public const int LadoMiniatura = 280;

    public const string FiltroArchivos =
        "Imágenes (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp";

    // ── Entrada ──────────────────────────────────────────────────────────────────

    /// <summary>Abre el diálogo de archivos y devuelve las imágenes que se hayan podido preparar.</summary>
    public static List<ForumImagenNueva> Elegir(IWin32Window? owner, int cupo)
    {
        var salida = new List<ForumImagenNueva>();
        if (cupo <= 0)
        {
            MessageBox.Show($"Ya hay {ForumService.MaxImagenes} imágenes en esta entrada.",
                "Sin sitio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return salida;
        }

        using var dlg = new OpenFileDialog
        {
            Title = "Seleccionar imagen(es)",
            Filter = FiltroArchivos,
            Multiselect = true
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return salida;

        var errores = new List<string>();
        foreach (var ruta in dlg.FileNames)
        {
            if (salida.Count >= cupo)
            {
                errores.Add($"Solo caben {cupo} más; el resto no se adjuntó.");
                break;
            }
            var (img, error) = DesdeArchivo(ruta);
            if (img != null) salida.Add(img); else if (error != null) errores.Add(error);
        }

        if (errores.Count > 0)
            MessageBox.Show(string.Join("\n", errores), "Algunas no se pudieron adjuntar",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return salida;
    }

    /// <summary>La imagen del portapapeles (Win+Shift+S y pegar), ya preparada.</summary>
    public static ForumImagenNueva? Pegar(IWin32Window? owner)
    {
        if (!Clipboard.ContainsImage())
        {
            MessageBox.Show("No hay una imagen en el portapapeles.\n(Usa Win+Shift+S para recortar la pantalla.)",
                "Portapapeles", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        try
        {
            using var img = Clipboard.GetImage();
            if (img == null) return null;
            using var bmp = new Bitmap(img);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);

            var (preparada, error) = Preparar(ms.ToArray(), $"captura_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            if (preparada == null) MessageBox.Show(error, "No se pudo pegar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return preparada;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo pegar la imagen:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }

    public static (ForumImagenNueva? imagen, string? error) DesdeArchivo(string ruta)
    {
        try
        {
            var info = new FileInfo(ruta);
            if (!info.Exists) return (null, $"«{Path.GetFileName(ruta)}» ya no está.");
            // 40 MB es el tope de lo que se lee del disco; después se reescala a algo mucho menor.
            if (info.Length > 40L * 1024 * 1024) return (null, $"«{info.Name}» pesa demasiado para abrirla.");
            return Preparar(File.ReadAllBytes(ruta), info.Name);
        }
        catch (Exception ex)
        {
            return (null, $"«{Path.GetFileName(ruta)}»: {ex.Message}");
        }
    }

    /// <summary>
    /// Comprueba que los bytes sean una imagen, la reescala si hace falta y le saca la miniatura.
    /// El tipo se decide por los bytes, no por la extensión.
    /// </summary>
    public static (ForumImagenNueva? imagen, string? error) Preparar(byte[] bytes, string nombre)
    {
        var tipo = ForumMedia.TipoDeImagen(bytes);
        if (tipo == null) return (null, $"«{nombre}» no es una imagen (se admiten PNG, JPG, GIF y BMP).");

        Image original;
        try { original = Image.FromStream(new MemoryStream(bytes), false, true); }
        catch { return (null, $"«{nombre}» está dañada o no se puede leer."); }

        using (original)
        {
            int ancho = original.Width, alto = original.Height;
            var guardar = bytes;

            bool cabeDeLado = ancho <= LadoMaximo && alto <= LadoMaximo;
            if (!cabeDeLado || bytes.Length > ForumService.MaxBytesImagen)
            {
                var (reducida, w, h, error) = Reducir(original, tipo, nombre);
                if (reducida == null) return (null, error);
                guardar = reducida; ancho = w; alto = h;
            }

            if (guardar.Length > ForumService.MaxBytesImagen)
                return (null, $"«{nombre}» sigue pasando de {ForumMedia.Tamano(ForumService.MaxBytesImagen)} " +
                              "incluso reducida. Recórtala o guárdala como JPG.");

            return (new ForumImagenNueva(nombre, guardar, Miniatura(original), ancho, alto), null);
        }
    }

    /// <summary>
    /// Reescala a <see cref="LadoMaximo"/> y vuelve a codificar. PNG conserva mejor una captura de
    /// pantalla (texto nítido); si aun así no cabe, se pasa a JPG bajando calidad, que es lo que de
    /// verdad recorta el tamaño de una foto.
    /// </summary>
    private static (byte[]? bytes, int ancho, int alto, string? error) Reducir(Image original, string tipo, string nombre)
    {
        double escala = Math.Min(1.0, (double)LadoMaximo / Math.Max(original.Width, original.Height));
        int w = Math.Max(1, (int)Math.Round(original.Width * escala));
        int h = Math.Max(1, (int)Math.Round(original.Height * escala));

        using var destino = Dibujar(original, w, h, Color.White);

        // Una captura suele venir en PNG y ahí PNG gana; una foto viene en JPG y ahí JPG gana.
        if (tipo != "image/jpeg")
        {
            using var ms = new MemoryStream();
            destino.Save(ms, ImageFormat.Png);
            if (ms.Length <= ForumService.MaxBytesImagen) return (ms.ToArray(), w, h, null);
        }

        foreach (var calidad in new[] { 88L, 75L, 60L })
        {
            var jpg = ComoJpeg(destino, calidad);
            if (jpg.Length <= ForumService.MaxBytesImagen) return (jpg, w, h, null);
        }

        return (null, 0, 0, $"«{nombre}» no cabe en {ForumMedia.Tamano(ForumService.MaxBytesImagen)} ni reducida.");
    }

    /// <summary>Miniatura de <see cref="LadoMiniatura"/> px como JPG: es lo que viaja en cada refresco.</summary>
    private static byte[] Miniatura(Image original)
    {
        try
        {
            double escala = Math.Min(1.0, (double)LadoMiniatura / Math.Max(original.Width, original.Height));
            int w = Math.Max(1, (int)Math.Round(original.Width * escala));
            int h = Math.Max(1, (int)Math.Round(original.Height * escala));
            using var mini = Dibujar(original, w, h, Color.White);
            return ComoJpeg(mini, 82L);
        }
        catch { return []; }   // sin miniatura el servicio guarda el original; se ve igual, solo pesa más
    }

    /// <summary>Fondo blanco a propósito: lo transparente en JPG saldría negro.</summary>
    private static Bitmap Dibujar(Image origen, int ancho, int alto, Color fondo)
    {
        var bmp = new Bitmap(ancho, alto, PixelFormat.Format24bppRgb);
        bmp.SetResolution(96, 96);
        using var g = Graphics.FromImage(bmp);
        g.Clear(fondo);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.DrawImage(origen, new Rectangle(0, 0, ancho, alto));
        return bmp;
    }

    private static byte[] ComoJpeg(Image img, long calidad)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
        using var parametros = new EncoderParameters(1);
        parametros.Param[0] = new EncoderParameter(Encoder.Quality, calidad);
        using var ms = new MemoryStream();
        img.Save(ms, codec, parametros);
        return ms.ToArray();
    }

    // ── Salida ───────────────────────────────────────────────────────────────────

    /// <summary>Un Bitmap desde bytes, o null si no se pueden pintar. Nunca tira la ventana abajo.</summary>
    public static Image? Cargar(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try { return Image.FromStream(new MemoryStream(bytes), false, true); }
        catch { return null; }
    }

    /// <summary>
    /// Vuelca la imagen a un temporal y la abre con el visor del sistema —el mismo camino que usan
    /// los demás adjuntos de la app, y así se puede acercar, girar e imprimir sin escribir un visor.
    /// </summary>
    public static void Abrir(byte[] bytes, string nombre, string tipo, IWin32Window? owner)
    {
        if (bytes.Length == 0)
        {
            MessageBox.Show("Esa imagen ya no está disponible.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "advweb_foro_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var ruta = Path.Combine(dir, NombreParaDisco(nombre, tipo));
            File.WriteAllBytes(ruta, bytes);
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir la imagen:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public static void GuardarComo(byte[] bytes, string nombre, string tipo, IWin32Window? owner)
    {
        if (bytes.Length == 0) return;
        var sugerido = NombreParaDisco(nombre, tipo);
        var ext = Path.GetExtension(sugerido);
        using var dlg = new SaveFileDialog { FileName = sugerido, Filter = $"(*{ext})|*{ext}|Todos los archivos (*.*)|*.*" };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return;
        try { File.WriteAllBytes(dlg.FileName, bytes); }
        catch (Exception ex) { MessageBox.Show($"No se pudo guardar:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    /// <summary>
    /// Nombre seguro para el disco, con la extensión que corresponde a los BYTES. Si alguien subió
    /// «captura.png» y en realidad era otra cosa, se abre con lo suyo y no con lo que decía.
    /// </summary>
    private static string NombreParaDisco(string? nombre, string tipo)
    {
        var n = Path.GetFileName((nombre ?? "").Trim());
        foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        if (string.IsNullOrWhiteSpace(n)) n = "imagen";

        var esperada = ForumMedia.ExtensionDe(tipo);
        if (!n.EndsWith(esperada, StringComparison.OrdinalIgnoreCase))
            n = Path.GetFileNameWithoutExtension(n) + esperada;
        return n;
    }
}
