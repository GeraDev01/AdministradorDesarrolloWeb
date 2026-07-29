namespace Administrador_Desarrollo_Web.Models;

/// <summary>Filtro predefinido para la pantalla de tickets de Azure DevOps.</summary>
public class DevOpsSavedFilter
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? GlobalSearch { get; set; }
    public string? TitleContains { get; set; }
    /// <summary>Filtros por columna serializados: {"State":["Active"],"WorkItemType":["Bug"]}.</summary>
    public string? ColumnFiltersJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
