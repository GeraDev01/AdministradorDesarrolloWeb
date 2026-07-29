namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Convierte un DOCX a PDF sin depender de Microsoft Office. La implementación
/// por defecto usa LibreOffice headless; queda detrás de esta interfaz para poder
/// sustituirla (p. ej. por un motor en-proceso) sin tocar el resto de la app.
/// </summary>
public interface IDocxToPdfConverter
{
    /// <summary>Convierte el DOCX y devuelve el PDF en memoria.</summary>
    Task<byte[]> ConvertAsync(byte[] docxBytes, CancellationToken ct = default);

    /// <summary>Indica si el motor está disponible; si no, <paramref name="diagnostic"/> explica por qué.</summary>
    bool IsAvailable(out string? diagnostic);
}
