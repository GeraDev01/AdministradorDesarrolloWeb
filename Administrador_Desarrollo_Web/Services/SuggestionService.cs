using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Sugerencias y propuestas de mejora del producto o del departamento. El desarrollador las envía y
/// da seguimiento a las suyas; el administrador las revisa, les cambia el estado y las responde.
///
/// Reparto de responsabilidades (mismo criterio que el resto de la app):
///  · cualquiera con sesión puede ENVIAR una sugerencia y ver/eliminar las suyas mientras estén «Nueva»;
///  · solo el administrador LISTA todas y RESPONDE (cambia estado + texto de respuesta).
/// </summary>
/// <summary>Una sugerencia junto con su total de votos y si el usuario actual ya la votó.</summary>
public record SuggestionConVotos(Suggestion Sug, int Votos, bool YoVote);

public class SuggestionService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly AuditService _audit;
    private readonly NotificationService _notifications;

    public SuggestionService(AppDbContext db, ICurrentUser currentUser, AuditService audit, NotificationService notifications)
    {
        _db = db; _currentUser = currentUser; _audit = audit; _notifications = notifications;
    }

    public const int MaxTitulo = 150;
    public const int MaxCuerpo = 4000;

    /// <summary>Una sugerencia solo la puede borrar su autor mientras siga «Nueva» (o el administrador, siempre).</summary>
    public static bool PuedeEliminar(SuggestionStatus estado) => estado == SuggestionStatus.Nueva;

    // ── Envío (desarrollador o cualquier usuario con sesión) ─────────────────────

    public (bool ok, string mensaje, Suggestion? sugerencia) Enviar(
        SuggestionCategory categoria, string? titulo, string? cuerpo, bool anonima,
        SuggestionVisibility visibilidad = SuggestionVisibility.Publica,
        bool abiertaAVotacion = true)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        if (_currentUser.UserId is not int userId)
            return (false, "No hay una sesión válida.", null);

        titulo = (titulo ?? "").Trim();
        cuerpo = (cuerpo ?? "").Trim();
        if (titulo.Length < 3) return (false, "Escribe un título (al menos 3 caracteres).", null);
        if (titulo.Length > MaxTitulo) return (false, $"El título no puede pasar de {MaxTitulo} caracteres.", null);
        if (cuerpo.Length < 5) return (false, "Describe tu sugerencia (al menos 5 caracteres).", null);
        if (cuerpo.Length > MaxCuerpo) return (false, $"La descripción no puede pasar de {MaxCuerpo} caracteres.", null);

        var sug = new Suggestion
        {
            DeveloperId     = _currentUser.DeveloperId,
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
        _db.Suggestions.Add(sug);
        _db.SaveChanges();

        // Anonimato: la bitácora NO debe delatar al autor. Un registro normal estampa su usuario junto
        // al título, y el administrador (que ve la Bitácora) podría cruzarlo con la sugerencia «Anónima».
        // Por eso, si es anónima, se registra como acción del sistema, sin usuario ni título.
        if (anonima)
            _audit.RecordSystem(AuditAction.Create, $"Sugerencia anónima recibida ({EtiquetaCategoria(categoria)}).");
        else
            _audit.Record(AuditAction.Create, "Suggestion", sug.Id.ToString(),
                $"Sugerencia enviada ({EtiquetaCategoria(categoria)}): {titulo}");

        // Avisar a los administradores. NotificationService no tiene difusión por rol, así que se
        // recorren los usuarios administradores activos. El autor anónimo no se revela en el aviso.
        var deQuien = anonima ? "Anónima" : (_currentUser.Username ?? "un desarrollador");
        foreach (var adminId in _db.Users.Where(u => u.Role == UserRole.Admin && u.IsActive).Select(u => u.Id).ToList())
            _notifications.Notify(adminId, NotificationKind.General,
                "💡 Nueva sugerencia",
                $"{EtiquetaCategoria(categoria)} — {titulo}  ({deQuien})",
                dedupeKey: $"sug-new:{sug.Id}:{adminId}");

        return (true, "Sugerencia enviada. ¡Gracias!", sug);
    }

    // ── Consultas ────────────────────────────────────────────────────────────────

    /// <summary>Las sugerencias que envió el usuario actual (para «Mis sugerencias»).</summary>
    public List<Suggestion> Mias()
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        var userId = _currentUser.UserId ?? -1;
        return _db.Suggestions
            .Where(s => s.CreatedByUserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .AsNoTracking()
            .ToList();
    }

    /// <summary>
    /// Todas las sugerencias, con filtros opcionales (solo administrador). Incluye a propósito las
    /// marcadas «solo administrador»: es justo a quien van dirigidas.
    /// </summary>
    public List<Suggestion> Todas(SuggestionStatus? estado = null, SuggestionCategory? categoria = null,
        SuggestionVisibility? visibilidad = null)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        var q = _db.Suggestions.Include(s => s.Developer).AsQueryable();
        if (estado is { } es) q = q.Where(s => s.Status == es);
        if (categoria is { } cat) q = q.Where(s => s.Category == cat);
        if (visibilidad is { } vis) q = q.Where(s => s.Visibility == vis);
        return q.OrderByDescending(s => s.CreatedAt).AsNoTracking().ToList();
    }

    // ── Votos (apoyo del equipo) ─────────────────────────────────────────────────

    /// <summary>
    /// Alterna el voto del usuario actual sobre una sugerencia (votar / quitar el voto). Devuelve si
    /// quedó votada y el total de votos. El índice único evita votos duplicados.
    /// </summary>
    public (bool ok, bool votado, int total) Votar(int suggestionId)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        if (_currentUser.UserId is not int userId) return (false, false, 0);

        // Se comprueba aquí y no solo escondiendo el botón: una propuesta que su autor no abrió a
        // votación —o que solo va dirigida al administrador— no debe poder votarse por otra ruta.
        var sug = _db.Suggestions.AsNoTracking()
            .Where(s => s.Id == suggestionId)
            .Select(s => new { s.Visibility, s.OpenToVoting })
            .FirstOrDefault();
        if (sug == null) return (false, false, 0);
        if (sug.Visibility != SuggestionVisibility.Publica || !sug.OpenToVoting)
            return (false, false, _db.SuggestionVotes.Count(v => v.SuggestionId == suggestionId));

        var existente = _db.SuggestionVotes.FirstOrDefault(v => v.SuggestionId == suggestionId && v.UserId == userId);
        bool votado;
        if (existente != null) { _db.SuggestionVotes.Remove(existente); votado = false; }
        else { _db.SuggestionVotes.Add(new SuggestionVote { SuggestionId = suggestionId, UserId = userId, CreatedAt = DateTime.UtcNow }); votado = true; }
        _db.SaveChanges();

        int total = _db.SuggestionVotes.Count(v => v.SuggestionId == suggestionId);
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
    public List<SuggestionConVotos> Equipo()
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        var userId = _currentUser.UserId ?? -1;

        var sugs = _db.Suggestions.Include(s => s.Developer).AsNoTracking()
            .Where(s => s.Visibility == SuggestionVisibility.Publica)
            .ToList();
        var votos = _db.SuggestionVotes.AsNoTracking()
            .GroupBy(v => v.SuggestionId).ToDictionary(g => g.Key, g => g.Count());
        var mios = _db.SuggestionVotes.AsNoTracking().Where(v => v.UserId == userId)
            .Select(v => v.SuggestionId).ToHashSet();

        return sugs
            .Select(s => new SuggestionConVotos(s, votos.TryGetValue(s.Id, out var n) ? n : 0, mios.Contains(s.Id)))
            .OrderByDescending(x => x.Votos).ThenByDescending(x => x.Sug.CreatedAt)
            .ToList();
    }

    /// <summary>Total de votos por sugerencia (para el panel del administrador).</summary>
    public Dictionary<int, int> ContarVotos() =>
        _db.SuggestionVotes.AsNoTracking().GroupBy(v => v.SuggestionId).ToDictionary(g => g.Key, g => g.Count());

    // ── Revisión (administrador) ─────────────────────────────────────────────────

    /// <summary>Cambia el estado de la sugerencia y guarda la respuesta del administrador; avisa al autor.</summary>
    public (bool ok, string mensaje) Responder(int id, SuggestionStatus nuevoEstado, string? respuesta)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var sug = _db.Suggestions.FirstOrDefault(s => s.Id == id);
        if (sug == null) return (false, "La sugerencia ya no existe. Actualiza la lista.");

        sug.Status = nuevoEstado;
        sug.AdminResponse = string.IsNullOrWhiteSpace(respuesta) ? null : respuesta.Trim();
        sug.ReviewedByUserId = _currentUser.UserId;
        sug.ReviewedAt = DateTime.UtcNow;
        _db.SaveChanges();

        _audit.Record(AuditAction.Update, "Suggestion", sug.Id.ToString(),
            $"Sugerencia marcada como {EtiquetaEstado(nuevoEstado)}: {sug.Title}");

        // Avisar al autor (aunque la haya enviado anónima: el aviso es privado, para él).
        if (sug.DeveloperId is int devId)
            _notifications.NotifyDeveloper(devId, NotificationKind.General,
                "💡 Respondieron tu sugerencia",
                $"«{sug.Title}» → {EtiquetaEstado(nuevoEstado)}",
                dedupeKey: $"sug-resp:{sug.Id}:{(int)nuevoEstado}");

        return (true, $"Sugerencia marcada como {EtiquetaEstado(nuevoEstado)}.");
    }

    /// <summary>Elimina una sugerencia: el autor solo si sigue «Nueva»; el administrador, cualquiera.</summary>
    public (bool ok, string mensaje) Eliminar(int id)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);

        var sug = _db.Suggestions.FirstOrDefault(s => s.Id == id);
        if (sug == null) return (false, "La sugerencia ya no existe. Actualiza la lista.");

        bool esAutor = _currentUser.UserId == sug.CreatedByUserId;
        if (!_currentUser.IsAdmin)
        {
            if (!esAutor)
                return (false, "Solo puedes eliminar tus propias sugerencias.");
            if (!PuedeEliminar(sug.Status))
                return (false, "Ya no puedes eliminarla: el administrador empezó a atenderla.");
        }

        bool eraAnonima = sug.Anonymous;
        _db.Suggestions.Remove(sug);
        _db.SaveChanges();
        // Igual que al enviarla: si era anónima, no se estampa autor ni título en la bitácora.
        if (eraAnonima)
            _audit.RecordSystem(AuditAction.Delete, "Sugerencia anónima eliminada.");
        else
            _audit.Record(AuditAction.Delete, "Suggestion", id.ToString(), $"Sugerencia eliminada: {sug.Title}");
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
        SuggestionVisibility.SoloAdministrador => "Solo administrador",
        _                                      => "Pública (todo el equipo)"
    };

    /// <summary>Una línea para la lista: quién la ve y si se puede votar.</summary>
    public static string EtiquetaAlcance(Suggestion s) => s.Visibility switch
    {
        SuggestionVisibility.SoloAdministrador => "🔒 Solo administrador",
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
