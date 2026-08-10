namespace AdminWeb.Domain.Entities;

/// <summary>
/// Un «me gusta» sobre una publicación o comentario del foro. Uno por persona y entrada, garantizado
/// por índice único.
/// </summary>
public class ForumLike
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ForumPost? Post { get; set; }
}
