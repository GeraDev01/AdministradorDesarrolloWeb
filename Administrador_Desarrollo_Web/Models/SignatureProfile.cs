namespace Administrador_Desarrollo_Web.Models;

/// <summary>
/// Firma reutilizable (imagen PNG transparente). Puede pertenecer a un
/// desarrollador (OwnerDeveloperId) o ser compartida/del gerente (null).
/// La imagen NO se cifra con DPAPI: no es un secreto y DPAPI (scope CurrentUser)
/// rompería el compartir en modo multiusuario (Azure SQL).
/// </summary>
public class SignatureProfile
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = "";
    public byte[] PngBytes { get; set; } = [];
    public int WidthPx { get; set; }
    public int HeightPx { get; set; }
    /// <summary>null = firma compartida / del gerente.</summary>
    public int? OwnerDeveloperId { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public byte[]? RowVersion { get; set; }

    public Developer? OwnerDeveloper { get; set; }
}
