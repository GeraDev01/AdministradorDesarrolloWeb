using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Utilidades GDI+ para firmas: convierte fondo (casi) blanco en transparente,
/// recorta al contenido y serializa a PNG. Sin dependencias externas.
/// </summary>
public static class SignatureImaging
{
    /// <summary>Serializa un bitmap a PNG en memoria.</summary>
    public static byte[] ToPng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Carga un PNG (o cualquier imagen) desde bytes como bitmap ARGB.</summary>
    public static Bitmap FromBytes(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var loaded = new Bitmap(ms);
        return new Bitmap(loaded); // copia desligada del stream
    }

    /// <summary>
    /// Devuelve una copia con los píxeles cercanos al blanco vueltos transparentes.
    /// Útil para firmas escaneadas sobre papel blanco.
    /// </summary>
    public static Bitmap MakeNearWhiteTransparent(Image src, int tolerance = 45)
    {
        var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, src.Width, src.Height);

        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int bytes = Math.Abs(data.Stride) * bmp.Height;
            var buffer = new byte[bytes];
            Marshal.Copy(data.Scan0, buffer, 0, bytes);
            int thr = 255 - tolerance;
            for (int i = 0; i < bytes; i += 4)
            {
                byte b = buffer[i], gr = buffer[i + 1], r = buffer[i + 2];
                if (b >= thr && gr >= thr && r >= thr)
                    buffer[i + 3] = 0; // alpha -> transparente
            }
            Marshal.Copy(buffer, 0, data.Scan0, bytes);
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }

    /// <summary>
    /// Recorta el bitmap al rectángulo que contiene píxeles con alpha significativo.
    /// Devuelve null si la imagen está vacía (todo transparente).
    /// </summary>
    public static Bitmap? AutoCrop(Bitmap src, int alphaThreshold = 12, int padding = 6)
    {
        var rect = new Rectangle(0, 0, src.Width, src.Height);
        var data = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
        try
        {
            int stride = data.Stride;
            int bytes = Math.Abs(stride) * src.Height;
            var buffer = new byte[bytes];
            Marshal.Copy(data.Scan0, buffer, 0, bytes);
            for (int y = 0; y < src.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < src.Width; x++)
                {
                    byte a = buffer[row + x * 4 + 3];
                    if (a > alphaThreshold)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
        }
        finally { src.UnlockBits(data); }

        if (maxX < 0) return null; // vacío

        minX = Math.Max(0, minX - padding);
        minY = Math.Max(0, minY - padding);
        maxX = Math.Min(src.Width - 1, maxX + padding);
        maxY = Math.Min(src.Height - 1, maxY + padding);

        var crop = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        var outBmp = new Bitmap(crop.Width, crop.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(outBmp))
            g.DrawImage(src, new Rectangle(0, 0, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);
        return outBmp;
    }
}
