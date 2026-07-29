namespace Administrador_Desarrollo_Web.Models;

public enum MinuteType { Daily = 0, Sesion = 1, Otro = 2 }

public class Minute
{
    public int Id { get; set; }
    public MinuteType Type { get; set; } = MinuteType.Daily;
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string Title { get; set; } = "";
    public string? Content { get; set; }
    public int? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MinuteActionItem> ActionItems { get; set; } = new List<MinuteActionItem>();
}
