namespace AdminWeb.Domain.Entities;

public class MinuteActionItem
{
    public int Id { get; set; }
    public int MinuteId { get; set; }
    public string Description { get; set; } = "";
    public int? ResponsibleDeveloperId { get; set; }
    public DateTime? DueDate { get; set; }
    public bool IsCompleted { get; set; } = false;

    public Minute Minute { get; set; } = null!;
    public Developer? ResponsibleDeveloper { get; set; }
}
