namespace Administrador_Desarrollo_Web.Models;

/// <summary>
/// Ficha de información general del desarrollador para el ADMINISTRADOR: fortalezas, debilidades,
/// stack técnico, salario y expectativas de crecimiento. Es 1:1 con el desarrollador (índice único en
/// DeveloperId). Contiene datos sensibles (salario), así que su acceso está restringido al admin.
/// </summary>
public class DeveloperProfile
{
    public int Id { get; set; }
    public int DeveloperId { get; set; }

    public string? Strengths { get; set; }            // Fortalezas
    public string? Weaknesses { get; set; }           // Debilidades / áreas de mejora
    public string? TechStack { get; set; }            // Stack técnico
    public decimal? Salary { get; set; }              // Salario (dato sensible)
    public string? Currency { get; set; }             // Moneda del salario (MXN, USD…)
    public string? GrowthExpectations { get; set; }   // Expectativas de crecimiento
    public string? Notes { get; set; }                // Notas generales

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Developer Developer { get; set; } = null!;
}
