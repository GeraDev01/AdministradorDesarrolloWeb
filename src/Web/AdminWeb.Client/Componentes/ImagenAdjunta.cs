namespace AdminWeb.Client.Componentes;

/// <summary>
/// Una imagen ya leída en el navegador, esperando a que se envíe el formulario que la acompaña.
///
/// No se sube al elegirla: se sube con el resto del formulario. Es lo que evita el adjunto huérfano
/// —una imagen guardada en la base cuya publicación nunca llegó a escribirse porque el usuario se
/// arrepintió— que en el escritorio no existía porque allí todo se guardaba de una vez.
/// </summary>
/// <param name="Nombre">El nombre con el que la eligieron. El servidor lo vuelve a limpiar.</param>
/// <param name="Contenido">Los bytes tal cual. El servidor decide el tipo mirándolos.</param>
/// <param name="TipoDeContenido">Lo que dijo el navegador. Sirve para la vista previa, nada más.</param>
/// <param name="Miniatura">
/// La versión pequeña, generada aquí con un lienzo. Viaja con la imagen porque es lo que después se
/// pinta en el muro y en el hilo: sin ella, cada refresco de un hilo con capturas se traería los
/// originales enteros desde la base —y también en la aplicación de escritorio, que lee esa misma
/// columna y sigue en producción—. Vacía si el navegador no supo decodificar el original.
/// </param>
public record ImagenAdjunta(
    string Nombre,
    byte[] Contenido,
    string TipoDeContenido,
    byte[] Miniatura)
{
    /// <summary>Para pintarla antes de subirla, sin pedirle nada al servidor.</summary>
    public string ComoDatosEnLinea() =>
        $"data:{TipoDeContenido};base64,{Convert.ToBase64String(Contenido)}";
}

/// <summary>Cómo viajan las imágenes en un formulario. Un solo sitio, para que los tres que las
/// mandan —publicar, comentar y editar— no puedan hacerlo cada uno a su manera.</summary>
public static class EnvioDeImagenes
{
    /// <summary>
    /// Añade las imágenes y sus miniaturas al formulario.
    ///
    /// Van en dos colecciones ALINEADAS por posición: la miniatura número tres es la de la imagen
    /// número tres. Por eso se añade siempre una parte por imagen, vacía cuando no se pudo generar
    /// —si se omitieran las que faltan, las posiciones se correrían y cada imagen acabaría con la
    /// miniatura de otra—.
    ///
    /// El tipo de contenido no se declara a propósito: el servidor decide qué es cada archivo
    /// mirando sus bytes, y creerle al navegador sería justo lo que se quiere evitar.
    /// </summary>
    public static void Adjuntar(MultipartFormDataContent formulario, IEnumerable<ImagenAdjunta> imagenes)
    {
        foreach (var imagen in imagenes)
        {
            formulario.Add(new ByteArrayContent(imagen.Contenido), "imagenes", imagen.Nombre);
            formulario.Add(new ByteArrayContent(imagen.Miniatura), "miniaturas", imagen.Nombre);
        }
    }
}
