namespace Administrador_Desarrollo_Web.Models;

public enum DevOpsRuleMatch
{
    AreaPathContiene = 0,
    TipoEsIgual      = 1,
    TituloContiene   = 2,
    TagContiene      = 3,
    AsignadoAContiene = 4
}

/// <summary>
/// Regla para auto-asignar un work item de Azure DevOps (importado como requerimiento)
/// a un desarrollador cuando cumple una condición. Se evalúan por orden.
/// </summary>
public class DevOpsAssignmentRule
{
    public int Id { get; set; }
    public DevOpsRuleMatch Match { get; set; } = DevOpsRuleMatch.AreaPathContiene;
    public string MatchValue { get; set; } = "";
    public int DeveloperId { get; set; }
    public Developer Developer { get; set; } = null!;
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
