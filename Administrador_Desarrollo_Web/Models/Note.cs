namespace Administrador_Desarrollo_Web.Models;

public enum NotePriority { Baja = 0, Media = 1, Alta = 2 }

public class Note
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Content { get; set; }
    public int? DeveloperId { get; set; }
    public DateTime? ReminderDate { get; set; }
    public bool IsCompleted { get; set; } = false;
    public NotePriority Priority { get; set; } = NotePriority.Media;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Developer? Developer { get; set; }
}
