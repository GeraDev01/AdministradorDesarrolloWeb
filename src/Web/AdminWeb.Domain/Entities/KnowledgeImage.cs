namespace AdminWeb.Domain.Entities;

/// <summary>
/// Una imagen incrustada en un artículo de la base de conocimiento: el diagrama de una arquitectura,
/// la captura de la pantalla que hay que tocar, el ejemplo de cómo queda algo bien hecho.
///
/// <para><b>El contenido va inline como BLOB</b>, igual que las capturas del foro y que el resto de
/// documentos de la aplicación. Un procedimiento de despliegue cuya única captura vive en un almacén
/// externo deja de explicarse solo el día que ese almacén no contesta, y la documentación se lee
/// justamente los días en que algo va mal.</para>
///
/// <para><b>Sin miniatura, y ahí se separa del foro a propósito.</b>
/// <see cref="ForumAttachment"/> guarda dos copias porque el muro pinta veinte capturas de golpe y
/// solo necesita recuadros de 200 píxeles. Aquí lo que se pinta es un diagrama al ancho de la
/// columna de texto: una miniatura de 400 píxeles saldría ilegible, así que se serviría el original
/// de todas formas y la segunda copia solo ocuparía sitio. Lo que sostiene esa decisión es el tope
/// por imagen —ver <c>ConocimientoService.MaxBytesImagen</c>—: lo que se guarda es exactamente lo
/// que se va a bajar, así que tiene que ser pequeño de entrada.</para>
///
/// <para><b>El cuerpo la referencia por NÚMERO</b>, con la marca <c>![descripción](imagen:12)</c>.
/// No hay ninguna dirección escrita por una persona en el camino: el servidor devuelve el
/// identificador y el navegador arma con él la ruta de <c>/api/adjuntos/conocimiento/{id}</c>. Es lo
/// que impide que meter imágenes abra el agujero que el cuerpo del artículo lleva cerrado desde el
/// principio.</para>
/// </summary>
public class KnowledgeImage
{
    public int Id { get; set; }

    /// <summary>El artículo al que pertenece. Se va con él si el artículo se borra.</summary>
    public int ArticleId { get; set; }

    /// <summary>Nombre con el que se subió, ya limpio. Es la descripción por omisión de la marca.</summary>
    public string FileName { get; set; } = "";

    /// <summary>image/png, image/jpeg… Decidido por los BYTES, nunca por la extensión del nombre.</summary>
    public string ContentType { get; set; } = "image/png";

    public byte[] Bytes { get; set; } = [];

    public long SizeBytes { get; set; }

    /// <summary>Quién la subió. Queda en la fila porque los bytes se sirven por una ruta propia.</summary>
    public int UploadedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public KnowledgeArticle? Article { get; set; }
}
