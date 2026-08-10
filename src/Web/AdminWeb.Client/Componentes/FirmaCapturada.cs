namespace AdminWeb.Client.Componentes;

/// <summary>
/// Una firma recién trazada, lista para guardarse o para acompañar a un documento.
/// </summary>
/// <param name="Png">La imagen con fondo transparente, recortada a lo dibujado.</param>
/// <param name="Ancho">Ancho en píxeles del recorte.</param>
/// <param name="Alto">Alto en píxeles del recorte.</param>
/// <remarks>
/// Las medidas viajan con la imagen y no se deducen después: la ficha de firma las guarda en sus
/// propias columnas y esa tabla la comparten la web y la aplicación de escritorio, que las usa para
/// colocar la firma en el documento sin deformarla.
/// </remarks>
public record FirmaCapturada(byte[] Png, int Ancho, int Alto);
