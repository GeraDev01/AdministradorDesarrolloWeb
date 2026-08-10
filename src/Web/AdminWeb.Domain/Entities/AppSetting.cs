namespace AdminWeb.Domain.Entities;

public class AppSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string? Value { get; set; }
    public bool IsSecret { get; set; } = false;
    public string? Description { get; set; }
}
