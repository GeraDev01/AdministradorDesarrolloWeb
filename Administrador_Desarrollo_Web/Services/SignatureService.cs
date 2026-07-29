using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// CRUD de firmas reutilizables (SignatureProfile). Una firma con
/// OwnerDeveloperId = null es compartida / del gerente.
/// </summary>
public class SignatureService
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public SignatureService(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>Todas las firmas (para el gestor del Admin).</summary>
    public List<SignatureProfile> GetAll() =>
        _db.SignatureProfiles.AsNoTracking().OrderBy(s => s.DisplayName).ToList();

    /// <summary>Firmas visibles para un contexto: las compartidas + las del desarrollador dado.</summary>
    public List<SignatureProfile> GetVisible(int? ownerDeveloperId)
    {
        var q = _db.SignatureProfiles.AsNoTracking()
            .Where(s => s.OwnerDeveloperId == null || s.OwnerDeveloperId == ownerDeveloperId);
        return q.OrderByDescending(s => s.IsDefault).ThenBy(s => s.DisplayName).ToList();
    }

    /// <summary>Firma predeterminada compartida/del gerente (OwnerDeveloperId = null).</summary>
    public SignatureProfile? GetSharedDefault() =>
        _db.SignatureProfiles.AsNoTracking()
            .Where(s => s.OwnerDeveloperId == null)
            .OrderByDescending(s => s.IsDefault).ThenByDescending(s => s.Id)
            .FirstOrDefault();

    public SignatureProfile? GetById(int id) =>
        _db.SignatureProfiles.AsNoTracking().FirstOrDefault(s => s.Id == id);

    public SignatureProfile Create(string displayName, byte[] png, int widthPx, int heightPx, int? ownerDeveloperId, bool isDefault)
    {
        var sig = new SignatureProfile
        {
            DisplayName      = displayName.Trim(),
            PngBytes         = png,
            WidthPx          = widthPx,
            HeightPx         = heightPx,
            OwnerDeveloperId = ownerDeveloperId,
            IsDefault        = isDefault,
            CreatedAtUtc     = DateTime.UtcNow
        };
        if (isDefault) ClearDefaultFlag(ownerDeveloperId);
        _db.SignatureProfiles.Add(sig);
        _db.SaveChanges();
        _audit.Record(AuditAction.Create, "SignatureProfile", sig.Id.ToString(), sig.DisplayName);
        return sig;
    }

    public void Rename(int id, string displayName)
    {
        var sig = _db.SignatureProfiles.FirstOrDefault(s => s.Id == id);
        if (sig == null) return;
        sig.DisplayName = displayName.Trim();
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "SignatureProfile", id.ToString(), sig.DisplayName);
    }

    public void SetDefault(int id)
    {
        var sig = _db.SignatureProfiles.FirstOrDefault(s => s.Id == id);
        if (sig == null) return;
        ClearDefaultFlag(sig.OwnerDeveloperId);
        sig.IsDefault = true;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "SignatureProfile", id.ToString(), $"Predeterminada: {sig.DisplayName}");
    }

    public void Delete(int id)
    {
        var sig = _db.SignatureProfiles.FirstOrDefault(s => s.Id == id);
        if (sig == null) return;
        _db.SignatureProfiles.Remove(sig);
        _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "SignatureProfile", id.ToString(), sig.DisplayName);
    }

    private void ClearDefaultFlag(int? ownerDeveloperId)
    {
        foreach (var other in _db.SignatureProfiles.Where(s => s.OwnerDeveloperId == ownerDeveloperId && s.IsDefault))
            other.IsDefault = false;
    }
}
