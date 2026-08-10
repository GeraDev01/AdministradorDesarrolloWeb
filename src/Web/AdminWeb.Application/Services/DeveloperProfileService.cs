using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Ficha de información general del desarrollador (fortalezas, debilidades, stack, salario, crecimiento).
/// Solo el ADMINISTRADOR puede leerla o guardarla: el salario es un dato sensible, así que la guarda
/// va en el servicio (no solo en el menú) y NUNCA se escribe el salario en la bitácora.
/// </summary>
public class DeveloperProfileService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
{
    /// <summary>La ficha del desarrollador (null si aún no tiene). Solo administrador.</summary>
    public Task<DeveloperProfile?> ObtenerAsync(int developerId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);
        return db.DeveloperProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.DeveloperId == developerId, ct);
    }

    /// <summary>Crea o actualiza la ficha del desarrollador. Solo administrador.</summary>
    public async Task<(bool ok, string mensaje)> GuardarAsync(int developerId, string? fortalezas, string? debilidades,
        string? stack, decimal? salario, string? moneda, string? crecimiento, string? notas,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);
        if (!await db.Developers.AnyAsync(d => d.Id == developerId, ct))
            return (false, "El desarrollador no existe.");
        if (salario is < 0)
            return (false, "El salario no puede ser negativo.");

        var p = await db.DeveloperProfiles.FirstOrDefaultAsync(x => x.DeveloperId == developerId, ct);
        if (p == null) { p = new DeveloperProfile { DeveloperId = developerId }; db.DeveloperProfiles.Add(p); }

        p.Strengths          = Limpiar(fortalezas);
        p.Weaknesses         = Limpiar(debilidades);
        p.TechStack          = Limpiar(stack);
        p.Salary             = salario;
        p.Currency           = string.IsNullOrWhiteSpace(moneda) ? null : moneda.Trim();
        p.GrowthExpectations = Limpiar(crecimiento);
        p.Notes              = Limpiar(notas);
        p.UpdatedAt          = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // La bitácora deja constancia del CAMBIO, pero SIN el salario ni el detalle (dato sensible).
        await audit.RecordAsync(AuditAction.Update, "DeveloperProfile", developerId.ToString(),
            "Ficha de perfil del desarrollador actualizada", ct);
        return (true, "Ficha guardada.");
    }

    private static string? Limpiar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
