using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

/// <summary>
/// Una publicación del foro, o un comentario dentro de ella.
///
/// Es una sola tabla con <see cref="ParentId"/>: null = publicación de primer nivel; con valor =
/// comentario colgando de otra entrada. Así un comentario puede colgar de otro comentario y salen
/// los subhilos sin una segunda tabla ni reglas distintas para cada nivel.
///
/// <see cref="RootId"/> guarda siempre la publicación raíz del hilo. Es redundante —se podría subir
/// por ParentId hasta arriba— y está a propósito: sin él, contar comentarios o traer un hilo
/// completo obligaría a una consulta recursiva por cada tarjeta del muro.
///
/// Nada se borra de verdad: se marca <see cref="DeletedAtUtc"/> y el texto se sustituye por un
/// aviso. Un foro que sirve para auditar no puede tener huecos — un hilo con respuestas que
/// contestan a algo que ya no está es peor que ver «mensaje eliminado».
/// </summary>
public class ForumPost
{
    public int Id { get; set; }

    /// <summary>Null en una publicación; el Id de la entrada a la que responde en un comentario.</summary>
    public int? ParentId { get; set; }

    /// <summary>La publicación raíz del hilo. En una publicación, su propio Id.</summary>
    public int RootId { get; set; }

    /// <summary>0 en la publicación, 1 en un comentario, 2 en la respuesta a un comentario…</summary>
    public int Depth { get; set; }

    public int AuthorUserId { get; set; }

    /// <summary>Nombre con el que mostrarlo aunque después se borre la cuenta. El foro es histórico.</summary>
    public string AuthorName { get; set; } = "";

    public int? AuthorDeveloperId { get; set; }

    /// <summary>Solo en las publicaciones; los comentarios no llevan título.</summary>
    public string? Title { get; set; }

    public string Body { get; set; } = "";

    public ForumTopic Topic { get; set; } = ForumTopic.Idea;

    /// <summary>Etiquetas separadas por coma, para buscar y filtrar.</summary>
    public string? Tags { get; set; }

    /// <summary>El autor (o un administrador) lo fijó arriba del muro.</summary>
    public bool Pinned { get; set; }

    /// <summary>Nadie más puede comentar. La conversación se cierra pero se conserva.</summary>
    public bool Locked { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EditedAtUtc { get; set; }

    /// <summary>Con valor, la entrada está retirada: se ve el hueco pero no el contenido.</summary>
    public DateTime? DeletedAtUtc { get; set; }
    public int? DeletedByUserId { get; set; }

    public bool EsPublicacion => ParentId == null;
    public bool Eliminado => DeletedAtUtc != null;

    /// <summary>Lo que se muestra: el cuerpo, o el aviso si la entrada se retiró.</summary>
    public string TextoVisible => Eliminado ? "(contenido eliminado por su autor)" : Body;
}
