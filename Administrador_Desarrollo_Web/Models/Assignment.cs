namespace Administrador_Desarrollo_Web.Models;

public class Assignment
{
    public int Id { get; set; }
    public int RequirementId { get; set; }
    public int DeveloperId { get; set; }
    public string? Role { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    public Requirement Requirement { get; set; } = null!;
    public Developer Developer { get; set; } = null!;
}
