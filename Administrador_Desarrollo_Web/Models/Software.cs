namespace Administrador_Desarrollo_Web.Models;

public enum SoftwareCategory
{
    IDE            = 0,
    ControlVersiones = 1,
    BaseDeDatos    = 2,
    Navegador      = 3,
    Comunicacion   = 4,
    Disenio        = 5,
    Seguridad      = 6,
    DevOps         = 7,
    Productividad  = 8,
    UtileriaRed    = 9,
    Otro           = 10
}

public enum SoftwareLicenseType
{
    Gratuita    = 0,
    OpenSource  = 1,
    Comercial   = 2,
    Prueba      = 3,
    Suscripcion = 4
}

public enum SoftwareStatus
{
    EnUso        = 0,
    Instalado    = 1,
    Desinstalado = 2,
    Expirado     = 3
}

public class Software
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public SoftwareCategory Category { get; set; }
    public SoftwareLicenseType LicenseType { get; set; }
    public SoftwareStatus Status { get; set; }
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public string? LicenseKey { get; set; }
    public DateTime? LicenseExpiry { get; set; }
    public string? InstalledOn { get; set; }
    public string? Url { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
