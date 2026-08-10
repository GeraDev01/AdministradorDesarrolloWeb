using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>Una sugerencia junto con su total de votos y si el usuario actual ya la votó.</summary>
public record SuggestionConVotos(Suggestion Sug, int Votos, bool YoVote);

/// <summary>
/// Sugerencias y propuestas de mejora del producto o del departamento. El desarrollador las envía y
/// da seguimiento a las suyas; el administrador las revisa, les cambia el estado y las responde.
///
/// Reparto de responsabilidades (mismo criterio que el resto de la app):
///  · cualquiera con sesión puede ENVIAR una sugerencia y ver/eliminar las suyas mientras estén «Nueva»;
///  · solo el administrador LISTA todas y RESPONDE (cambia estado + texto de respuesta).
/// </summary>
public class SuggestionService(
    AppDbContext db, ICurrentUser currentUser, AuditService audit, NotificationService notifications)
{
    public const int MaxTitulo = 150;
    public const int MaxCuerpo = 4000;

    /// <summary>Una sugerencia solo la puede borrar su autor mientras siga «Nueva» (o el administrador, siempre).</summary>
    public static bool PuedeEliminar(SuggestionStatus estado) => estado == SuggestionStatus.Nueva;

    // ── Envío (desarrollador o cualquier usuario con sesión) ─────────────────────

    public async Task<(bool ok, string mensaje, Suggestion? sugerencia)> EnviarAsync(
        SuggestionCategory categoria, string? titulo, string? cuerpo, bool anonima,
        SuggestionVisibility visibilidad = SuggestionVisibility.Publica,
        bool abiertaAVotacion = true,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId)
            return (false, "No hay una sesión válida.", null);

        titulo = (titulo ?? "").Trim();
        cuerpo = (cuerpo ?? "").Trim();
        if (titulo.Length < 3) return (false, "Escribe un título (al menos 3 caracteres).", null);
        if (titulo.Length > MaxTitulo) return (false, $"El título no puede pasar de {MaxTitulo} caracteres.", null);
        if (cuerpo.Length < 5) return (false, "Describe tu sugerencia (al menos 5 caracteres).", null);
        if (cuerpo.Length > MaxCuerpo) return (false, $"La descripción no puede pasar de {MaxCuerpo} caracteres.", null);

        var sug = new Suggestion
        {
            DeveloperId     = currentUser.DeveloperId,
            CreatedByUserId = userId,
            Title           = titulo,
            Body            = cuerpo,
            Category        = categoria,
            Status          = SuggestionStatus.Nueva,
            Anonymous       = anonima,
            Visibility      = visibilidad,
            // Lo que solo ve el administrador no se vota nunca: dejar la bandera encendida daría a
            // entender que hay una votación en marcha que el equipo ni siquiera puede ver.
            OpenToVoting    = visibilidad == SuggestionVisibility.Publica && abiertaAVotacion,
            CreatedAt       = DateTime.UtcNow
        };
        db.Suggestions.Add(sug);
        await db.SaveChangesAsync(ct);

        // Anonimato: la bitácora NO debe delatar al autor. Un registro normal estampa su usuario junto
        // al título, y el administrador (que ve la Bitácora) podría cruzarlo con la sugerencia «Anónima».
        // Por eso, si es anónima, se registra como acción del sistema, sin usuario ni título.
        if (anonima)
            await audit.RecordSystemAsync(AuditAction.Create,
                $"Sugerencia anónima recibida ({EtiquetaCategoria(categoria)}).", ct);
        else
            await audit.RecordAsync(AuditAction.Create, "Suggestion", sug.Id.ToString(),
                $"Sugerencia enviada ({EtiquetaCategoria(categoria)}): {titulo}", ct);

        // Avisar a los administradores. NotificationService no tiene difusión por rol, así que se
        // recorren los usuarios administradores activos. El autor anónimo no se revela en el aviso.
        var deQuien = anonima ? "Anónima" : (currentUser.Username ?? "un desarrollador");
        foreach (var adminId in await db.Users.Where(u => u.Role == UserRole.Admin && u.IsActive)
                     .Select(u => u.Id).ToListAsync(ct))
            await notifications.NotifyAsync(adminId, NotificationKind.General,
                "💡 Nueva sugerencia",
                $"{EtiquetaCategoria(categoria)} — {titulo}  ({deQuien})",
                dedupeKey: $"sug-new:{sug.Id}:{adminId}", ct: ct);

        return (true, "Sugerencia enviada. ¡Gracias!", sug);
    }

    // ── Consultas ────────────────────────────────────────────────────────────────

    /// <summary>Las sugerencias que envió el usuario actual (para «Mis sugerencias»).</summary>
    public Task<List<Suggestion>> MiasAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        var userId = currentUser.UserId ?? -1;
        return db.Suggestions
            .Where(s => s.CreatedByUserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    /// <summary>
    /// Todas las sugerencias, con filtros opcionales (solo administrador). Incluye a propósito las
    /// marcadas «solo administrador»: es justo a quien van dirigidas.
    /// </summary>
    public Task<List<Suggestion>> TodasAsync(SuggestionStatus? estado = null, SuggestionCategory? categoria = null,
        SuggestionVisibility? visibilidad = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);
        var q = db.Suggestions.Include(s => s.Developer).AsQueryable();
        if (estado is { } es) q = q.Where(s => s.Status == es);
        if (categoria is { } cat) q = q.Where(s => s.Category == cat);
        if (visibilidad is { } vis) q = q.Where(s => s.Visibility == vis);
        return q.OrderByDescending(s => s.CreatedAt).AsNoTracking().ToListAsync(ct);
    }

    // ── Votos (apoyo del equipo) ─────────────────────────────────────────────────

    /// <summary>
    /// Alterna el voto del usuario actual sobre una sugerencia (votar / quitar el voto). Devuelve si
    /// quedó votada y el total de votos. El índice único evita votos duplicados.
    /// </summary>
    public async Task<(bool ok, bool votado, int total)> VotarAsync(int suggestionId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId) return (false, false, 0);

        // Se comprueba aquí y no solo escondiendo el botón: una propuesta que su autor no abrió a
        // votación —o que solo va dirigida al administrador— no debe poder votarse por otra ruta.
        // En la web esto pesa aún más: cualquiera puede llamar al endpoint sin pasar por la pantalla.
        var sug = await db.Suggestions.AsNoTracking()
            .Where(s => s.Id == suggestionId)
            .Select(s => new { s.Visibility, s.OpenToVoting })
            .FirstOrDefaultAsync(ct);
        if (sug == null) return (false, false, 0);
        if (sug.Visibility != SuggestionVisibility.Publica || !sug.OpenToVoting)
            return (false, false, await db.SuggestionVotes.CountAsync(v => v.SuggestionId == suggestionId, ct));

        var existente = await db.SuggestionVotes
            .FirstOrDefaultAsync(v => v.SuggestionId == suggestionId && v.UserId == userId, ct);
        bool votado;
        if (existente != null) { db.SuggestionVotes.Remove(existente); votado = false; }
        else { db.SuggestionVotes.Add(new SuggestionVote { SuggestionId = suggestionId, UserId = userId, CreatedAt = DateTime.UtcNow }); votado = true; }
        await db.SaveChangesAsync(ct);

        int total = await db.SuggestionVotes.CountAsync(v => v.SuggestionId == suggestionId, ct);
        return (true, votado, total);
    }

    /// <summary>
    /// Tablero de propuestas del equipo: las sugerencias PÚBLICAS con su total de votos y si el
    /// usuario actual ya votó, ordenadas por más votadas. Es la pantalla donde el equipo apoya ideas.
    ///
    /// Las marcadas «solo administrador» no salen aquí ni para quien las escribió: si su propio
    /// autor las viera en el tablero del equipo, no habría forma de saber que nadie más las ve. Para
    /// darles seguimiento está «Mis sugerencias», que sí son suyas y solo suyas.
    /// </summary>
    public async Task<List<SuggestionConVotos>> EquipoAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        var userId = currentUser.UserId ?? -1;

        var sugs = await db.Suggestions.Include(s => s.Developer).AsNoTracking()
            .Where(s => s.Visibility == SuggestionVisibility.Publica)
            .ToListAsync(ct);
        var votos = (await db.SuggestionVotes.AsNoTracking()
            .GroupBy(v => v.SuggestionId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.N);
        var mios = (await db.SuggestionVotes.AsNoTracking().Where(v => v.UserId == userId)
            .Select(v => v.SuggestionId).ToListAsync(ct)).ToHashSet();

        return sugs
            .Select(s => new SuggestionConVotos(s, votos.TryGetValue(s.Id, out var n) ? n : 0, mios.Contains(s.Id)))
            .OrderByDescending(x => x.Votos).ThenByDescending(x => x.Sug.CreatedAt)
            .ToList();
    }

    /// <summary>Total de votos por sugerencia (para el panel del administrador).</summary>
    public async Task<Dictionary<int, int>> ContarVotosAsync(CancellationToken ct = default) =>
        (await db.SuggestionVotes.AsNoTracking()
            .GroupBy(v => v.SuggestionId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.N);

    // ── Revisión (administrador) ─────────────────────────────────────────────────

    /// <summary>Cambia el estado de la sugerencia y guarda la respuesta del administrador; avisa al autor.</summary>
    public async Task<(bool ok, string mensaje)> ResponderAsync(
        int id, SuggestionStatus nuevoEstado, string? respuesta, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var sug = await db.Suggestions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sug == null) return (false, "La sugerencia ya no existe. Actualiza la lista.");

        sug.Status = nuevoEstado;
        sug.AdminResponse = string.IsNullOrWhiteSpace(respuesta) ? null : respuesta.Trim();
        sug.ReviewedByUserId = currentUser.UserId;
        sug.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "Suggestion", sug.Id.ToString(),
            $"Sugerencia marcada como {EtiquetaEstado(nuevoEstado)}: {sug.Title}", ct);

        // Avisar al autor (aunque la haya enviado anónima: el aviso es privado, para él).
        if (sug.DeveloperId is int devId)
            await notifications.NotifyDeveloperAsync(devId, NotificationKind.General,
                "💡 Respondieron tu sugerencia",
                $"«{sug.Title}» → {EtiquetaEstado(nuevoEstado)}",
                dedupeKey: $"sug-resp:{sug.Id}:{(int)nuevoEstado}", ct: ct);

        return (true, $"Sugerencia marcada como {EtiquetaEstado(nuevoEstado)}.");
    }

    /// <summary>Elimina una sugerencia: el autor solo si sigue «Nueva»; el administrador, cualquiera.</summary>
    public async Task<(bool ok, string mensaje)> EliminarAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var sug = await db.Suggestions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sug == null) return (false, "La sugerencia ya no existe. Actualiza la lista.");

        bool esAutor = currentUser.UserId == sug.CreatedByUserId;
        if (!currentUser.IsAdmin)
        {
            if (!esAutor)
                return (false, "Solo puedes eliminar tus propias sugerencias.");
            if (!PuedeEliminar(sug.Status))
                return (false, "Ya no puedes eliminarla: el líder empezó a atenderla.");
        }

        bool eraAnonima = sug.Anonymous;
        db.Suggestions.Remove(sug);
        await db.SaveChangesAsync(ct);
        // Igual que al enviarla: si era anónima, no se estampa autor ni título en la bitácora.
        if (eraAnonima)
            await audit.RecordSystemAsync(AuditAction.Delete, "Sugerencia anónima eliminada.", ct);
        else
            await audit.RecordAsync(AuditAction.Delete, "Suggestion", id.ToString(),
                $"Sugerencia eliminada: {sug.Title}", ct);
        return (true, "Sugerencia eliminada.");
    }

    // ── Etiquetas legibles ───────────────────────────────────────────────────────

    public static string EtiquetaCategoria(SuggestionCategory c) => c switch
    {
        SuggestionCategory.Producto     => "Producto",
        SuggestionCategory.Departamento => "Departamento",
        _                               => "Otro"
    };

    public static string EtiquetaVisibilidad(SuggestionVisibility v) => v switch
    {
        SuggestionVisibility.SoloAdministrador => "Solo líder",
        _                                      => "Pública (todo el equipo)"
    };

    /// <summary>Una línea para la lista: quién la ve y si se puede votar.</summary>
    public static string EtiquetaAlcance(Suggestion s) => s.Visibility switch
    {
        SuggestionVisibility.SoloAdministrador => "🔒 Solo líder",
        _ when s.OpenToVoting                  => "👥 Pública · se vota",
        _                                      => "👥 Pública · sin votación"
    };

    public static string EtiquetaEstado(SuggestionStatus s) => s switch
    {
        SuggestionStatus.Nueva        => "Nueva",
        SuggestionStatus.EnRevision   => "En revisión",
        SuggestionStatus.Aceptada     => "Aceptada",
        SuggestionStatus.Rechazada    => "Rechazada",
        SuggestionStatus.Implementada => "Implementada",
        _                             => s.ToString()
    };
}
