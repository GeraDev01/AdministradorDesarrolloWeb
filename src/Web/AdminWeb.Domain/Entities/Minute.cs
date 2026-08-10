using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

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
