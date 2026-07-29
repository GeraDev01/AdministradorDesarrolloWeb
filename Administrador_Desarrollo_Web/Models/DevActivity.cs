namespace Administrador_Desarrollo_Web.Models;

public enum DevActivityStatus { Abierta = 0, Cerrada = 1 }

/// <summary>
/// Actividad libre de un desarrollador: trabajo real que no corresponde a ninguno de sus
/// requerimientos asignados (soporte, juntas, investigación, apoyo a otro equipo…). Existe para
/// que ese tiempo se pueda cronometrar y quede registrado, en lugar de perderse o colgarse de un
/// requerimiento que no le corresponde.
///
/// La medición vive en <see cref="WorkSession"/>, igual que la de los requerimientos: una
/// actividad puede acumular varias sesiones a lo largo de días.
/// </summary>
public class DevActivity
{
    public int Id { get; set; }
    public int DeveloperId { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public DevActivityStatus Status { get; set; } = DevActivityStatus.Abierta;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    public Developer Developer { get; set; } = null!;
}
