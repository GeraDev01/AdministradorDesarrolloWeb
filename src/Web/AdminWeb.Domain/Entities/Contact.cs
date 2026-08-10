namespace AdminWeb.Domain.Entities;

/// <summary>Contacto de una persona relevante en la empresa (correo y/o enlace de Teams).</summary>
public class Contact
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? JobTitle { get; set; }
    public string? Company { get; set; }
    public string? Email { get; set; }
    public string? TeamsLink { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
