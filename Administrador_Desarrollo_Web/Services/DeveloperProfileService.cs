using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Ficha de información general del desarrollador (fortalezas, debilidades, stack, salario, crecimiento).
/// Solo el ADMINISTRADOR puede leerla o guardarla: el salario es un dato sensible, así que la guarda
/// va en el servicio (no solo en el menú) y NUNCA se escribe el salario en la bitácora.
/// </summary>
public class DeveloperProfileService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly AuditService _audit;

    public DeveloperProfileService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
    {
        _db = db; _currentUser = currentUser; _audit = audit;
    }

    /// <summary>La ficha del desarrollador (null si aún no tiene). Solo administrador.</summary>
    public DeveloperProfile? Obtener(int developerId)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        return _db.DeveloperProfiles.AsNoTracking().FirstOrDefault(p => p.DeveloperId == developerId);
    }

    /// <summary>Crea o actualiza la ficha del desarrollador. Solo administrador.</summary>
    public (bool ok, string mensaje) Guardar(int developerId, string? fortalezas, string? debilidades,
        string? stack, decimal? salario, string? moneda, string? crecimiento, string? notas)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        if (!_db.Developers.Any(d => d.Id == developerId))
            return (false, "El desarrollador no existe.");
        if (salario is < 0)
            return (false, "El salario no puede ser negativo.");

        var p = _db.DeveloperProfiles.FirstOrDefault(x => x.DeveloperId == developerId);
        if (p == null) { p = new DeveloperProfile { DeveloperId = developerId }; _db.DeveloperProfiles.Add(p); }

        p.Strengths          = Limpiar(fortalezas);
        p.Weaknesses         = Limpiar(debilidades);
        p.TechStack          = Limpiar(stack);
        p.Salary             = salario;
        p.Currency           = string.IsNullOrWhiteSpace(moneda) ? null : moneda.Trim();
        p.GrowthExpectations = Limpiar(crecimiento);
        p.Notes              = Limpiar(notas);
        p.UpdatedAt          = DateTime.UtcNow;
        _db.SaveChanges();

        // La bitácora deja constancia del CAMBIO, pero SIN el salario ni el detalle (dato sensible).
        _audit.Record(AuditAction.Update, "DeveloperProfile", developerId.ToString(),
            "Ficha de perfil del desarrollador actualizada");
        return (true, "Ficha guardada.");
    }

    private static string? Limpiar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
