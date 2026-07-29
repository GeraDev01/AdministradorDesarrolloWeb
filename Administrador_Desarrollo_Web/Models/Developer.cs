namespace Administrador_Desarrollo_Web.Models;

/// <summary>Rol funcional de un desarrollador dentro de su equipo.</summary>
public enum TeamRole { SinRol = 0, Lider = 1, Frontend = 2, Backend = 3, Fullstack = 4, QA = 5, DevOps = 6, UX = 7, Otro = 8 }

public class Developer
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    /// <summary>Teléfono de contacto. Libre a propósito: admite extensión, lada o formato local.</summary>
    public string? Phone { get; set; }
    public string? Seniority { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public DateTime? HireDate { get; set; }
    public string? Address { get; set; }
    /// <summary>Número de serie del equipo (computadora) asignado al desarrollador.</summary>
    public string? EquipmentSerial { get; set; }
    public int VacationDaysLeft { get; set; } = 15;
    public int? TeamId { get; set; }
    public TeamRole TeamRole { get; set; } = TeamRole.SinRol;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Team? Team { get; set; }
    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();
}
