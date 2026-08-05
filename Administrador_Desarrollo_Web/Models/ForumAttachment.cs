namespace Administrador_Desarrollo_Web.Models;

/// <summary>
/// Una imagen incrustada en una publicación o comentario del foro. El contenido va inline como
/// BLOB, igual que el resto de documentos de la app: el foro tiene que seguir leyéndose aunque no
/// haya red hacia ningún almacén externo, y una captura que se pierde deja el hilo sin sentido.
///
/// Se guardan DOS copias: <see cref="Bytes"/> —la imagen tal cual se verá al abrirla— y
/// <see cref="Thumb"/>, una miniatura de pocos KB. El muro y el hilo pintan SIEMPRE la miniatura;
/// el original solo viaja cuando alguien pulsa para verlo. Sin esa separación, abrir un muro con
/// veinte capturas se traería decenas de MB desde SQL Server en cada refresco.
/// </summary>
public class ForumAttachment
{
    public int Id { get; set; }

    /// <summary>La entrada (publicación o comentario) a la que pertenece.</summary>
    public int PostId { get; set; }

    /// <summary>Nombre con el que se adjuntó, para guardarla o abrirla después.</summary>
    public string FileName { get; set; } = "";

    /// <summary>image/png, image/jpeg… Determinado por los bytes reales, no por la extensión.</summary>
    public string ContentType { get; set; } = "image/png";

    public byte[] Bytes { get; set; } = [];

    /// <summary>Miniatura para pintar sin traer el original.</summary>
    public byte[] Thumb { get; set; } = [];

    public long SizeBytes { get; set; }

    /// <summary>Dimensiones del original, para reservarle sitio antes de cargarlo.</summary>
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>Posición dentro de la entrada: se ven en el orden en que se adjuntaron.</summary>
    public int Orden { get; set; }

    public int UploadedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ForumPost? Post { get; set; }
}
