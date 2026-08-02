using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Una persona en el tablero de presencia: su jornada abierta o su última jornada.</summary>
public record PresenciaDeUsuario(
    int UserId,
    string Nombre,
    bool Conectado,
    PresenceState Estado,
    string? Nota,
    DateTime? DesdeUtc,
    DateTime UltimoLatidoUtc);

/// <summary>
/// Una jornada propia, para la pantalla «Mi jornada». Es una proyección y no la entidad: el
/// ESTADO (comiendo, descanso…) no sale de la capa de servicio, porque no se historiza a
/// propósito — un registro minutado de las pausas de alguien es vigilancia, no asistencia.
/// </summary>
/// <param name="Cierre">Null mientras sigue abierta; SinLatido si se cayó.</param>
public record MiJornada(
    DateTime InicioUtc,
    DateTime? FinUtc,
    TimeSpan Duracion,
    PresenceEnd? Cierre,
    string? Equipo);

/// <summary>
/// Quién tiene la aplicación abierta ahora y desde cuándo, más el registro de jornadas.
///
/// Se sostiene en un LATIDO, no en el par inicio/cierre de sesión: cerrar con la X deja la
/// aplicación viva en la bandeja (y eso ES estar conectado), pero un cuelgue o un apagón no avisan
/// de nada. Sin latido, esa persona se quedaría marcada como conectada para siempre y el tablero
/// mentiría. Con latido, quien deja de dar señales se cierra solo y su jornada se cierra con la
/// hora del ÚLTIMO latido, no con la de ahora — que sería regalarle horas que nadie trabajó.
///
/// El estado (comiendo, en el baño…) se guarda en la jornada abierta y se sobrescribe: no queda
/// histórico de cuánto tiempo estuvo alguien en cada uno. Es deliberado — un registro minutado de
/// las pausas de una persona es vigilancia, no asistencia.
/// </summary>
public class PresenceService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public PresenceService(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    /// <summary>Cada cuánto late la aplicación.</summary>
    public static readonly TimeSpan IntervaloLatido = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Sin latido en este tiempo, se da por desconectado. Es varias veces el intervalo a propósito:
    /// una pantalla bloqueada, un equipo que suspende un momento o una red que parpadea no deben
    /// marcar a nadie como ausente.
    /// </summary>
    public static readonly TimeSpan ToleranciaSinLatido = TimeSpan.FromMinutes(10);

    // ── Ciclo de la jornada ──────────────────────────────────────────────────────

    /// <summary>
    /// Abre la jornada de quien acaba de iniciar sesión. Si ya había una abierta de este mismo
    /// usuario (por ejemplo porque su equipo anterior se colgó), se cierra antes con la hora de su
    /// último latido: dos jornadas abiertas a la vez harían que el tablero contara doble.
    /// </summary>
    public WorkPresence? Entrar(string? origen = null)
    {
        if (_currentUser.UserId is not int userId) return null;

        CerrarAbiertasDe(userId, PresenceEnd.SinLatido);

        var jornada = new WorkPresence
        {
            UserId       = userId,
            DeveloperId  = _currentUser.DeveloperId,
            DisplayName  = _currentUser.User?.FullName ?? _currentUser.Username ?? $"Usuario #{userId}",
            StartedAtUtc = DateTime.UtcNow,
            LastSeenUtc  = DateTime.UtcNow,
            State        = PresenceState.Disponible,
            Origin       = origen ?? Equipo()
        };
        _db.WorkPresences.Add(jornada);
        _db.SaveChanges();
        return jornada;
    }

    /// <summary>
    /// El latido. Refresca la marca de vida de la jornada abierta y, de paso, cierra las de quienes
    /// dejaron de dar señales — así el barrido no necesita un servicio aparte: lo hace cualquier
    /// aplicación que siga viva.
    ///
    /// El barrido va PRIMERO porque la jornada caída puede ser la de ESTE equipo: tras una
    /// suspensión o hibernación larga, el primer latido al despertar encontraría la jornada de
    /// anoche todavía abierta y, refrescándola, le regalaría a la persona todas las horas que la
    /// máquina pasó dormida. Con este orden la vieja se sella con su último latido real y el mismo
    /// latido abre la jornada nueva. El precio asumido: una pausa mayor a la tolerancia parte el
    /// día en dos filas — que es la verdad.
    /// </summary>
    public void Latir()
    {
        CerrarCaidas();

        if (_currentUser.UserId is int userId)
        {
            var mia = Abierta(userId);
            if (mia != null) { mia.LastSeenUtc = DateTime.UtcNow; _db.SaveChanges(); }
            else Entrar();   // se cerró (corte de red, suspensión): se abre otra
        }
    }

    /// <summary>Cierra la jornada al cerrar sesión o al salir de la aplicación.</summary>
    public void Salir()
    {
        if (_currentUser.UserId is not int userId) return;
        CerrarAbiertasDe(userId, PresenceEnd.CierreNormal);
    }

    /// <summary>Cambia el estado propio. Nadie puede cambiar el de otra persona.</summary>
    public (bool ok, string mensaje) CambiarEstado(PresenceState estado, string? nota = null)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        if (_currentUser.UserId is not int userId) return (false, "No hay una sesión válida.");

        var mia = Abierta(userId);
        if (mia == null)
        {
            mia = Entrar();
            if (mia == null) return (false, "No hay una sesión válida.");
        }

        nota = (nota ?? "").Trim();
        mia.State = estado;
        mia.StateNote = nota.Length == 0 ? null : (nota.Length > 200 ? nota[..200] : nota);
        mia.LastSeenUtc = DateTime.UtcNow;
        _db.SaveChanges();
        return (true, $"Estado: {Etiqueta(estado)}.");
    }

    /// <summary>El estado propio ahora mismo, para pintar el selector.</summary>
    public (PresenceState estado, string? nota) MiEstado()
    {
        if (_currentUser.UserId is int userId && Abierta(userId) is { } mia)
            return (mia.State, mia.StateNote);
        return (PresenceState.Disponible, null);
    }

    // ── Tablero (administrador) ──────────────────────────────────────────────────

    /// <summary>
    /// Quién está conectado y en qué anda, más quién no lo está y desde cuándo. Incluye a TODAS las
    /// cuentas activas, no solo a las que han abierto la aplicación alguna vez: si alguien falta,
    /// esa ausencia es el dato.
    /// </summary>
    public List<PresenciaDeUsuario> Tablero()
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        CerrarCaidas();

        var corte = DateTime.UtcNow - ToleranciaSinLatido;
        var usuarios = _db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new { u.Id, u.FullName, u.Username })
            .ToList();

        // Las jornadas abiertas son pocas (una por persona conectada): se traen enteras.
        var abiertas = _db.WorkPresences.AsNoTracking()
            .Where(p => p.EndedAtUtc == null)
            .ToList()
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.StartedAtUtc).First());

        // Para los desconectados basta con cuándo se les vio por última vez; se resuelve con un
        // agregado en la base en vez de arrastrar el historial completo a memoria.
        var ultimoVisto = _db.WorkPresences.AsNoTracking()
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Ultimo = g.Max(p => p.LastSeenUtc) })
            .ToList()
            .ToDictionary(x => x.UserId, x => x.Ultimo);

        return usuarios
            .Select(u =>
            {
                abiertas.TryGetValue(u.Id, out var abierta);
                bool conectado = abierta != null && abierta.LastSeenUtc >= corte;
                return new PresenciaDeUsuario(
                    u.Id,
                    string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName,
                    conectado,
                    conectado ? abierta!.State : PresenceState.Ausente,
                    conectado ? abierta!.StateNote : null,
                    conectado ? abierta!.StartedAtUtc : null,
                    ultimoVisto.TryGetValue(u.Id, out var visto) ? visto : DateTime.MinValue);
            })
            .OrderByDescending(x => x.Conectado)
            .ThenBy(x => x.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Las jornadas de un día, para el registro de asistencia. Todas si no se indica usuario.</summary>
    public List<WorkPresence> JornadasDelDia(DateTime diaLocal, int? userId = null)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        // El registro también se consulta cuando nadie más tuvo la aplicación abierta para barrer
        // (lunes por la mañana, tras un feriado): sin esto, una jornada caída se listaría como
        // «en curso». Es idempotente y casi siempre afecta 0 filas.
        CerrarCaidas();

        var desde = diaLocal.Date.ToUniversalTime();
        var hasta = diaLocal.Date.AddDays(1).ToUniversalTime();

        var q = _db.WorkPresences.AsNoTracking()
            .Where(p => p.StartedAtUtc >= desde && p.StartedAtUtc < hasta);
        if (userId is int uid) q = q.Where(p => p.UserId == uid);

        return q.OrderBy(p => p.DisplayName).ThenBy(p => p.StartedAtUtc).ToList();
    }

    // ── Mi jornada (cada quien la suya) ──────────────────────────────────────────

    /// <summary>
    /// Las jornadas PROPIAS de un rango de días locales, para que cada quien vea su asistencia.
    ///
    /// SIN parámetro de usuario a propósito: no es un descuido de ergonomía, es lo que hace que
    /// este método no pueda convertirse nunca en un IDOR. Un id por parámetro —aunque hoy lo
    /// protegiera una guarda— basta que un llamador futuro lo pase mal para filtrar la asistencia
    /// de otra persona. Aquí solo hay un usuario posible: el de la sesión.
    ///
    /// Tampoco barre las caídas (CerrarCaidas cierra las de TODOS y convertiría una consulta en
    /// escritura de filas ajenas): una jornada caída aún sin barrer se muestra «en curso» con su
    /// duración calculada hasta el último latido, que es la verdad disponible.
    /// </summary>
    public List<MiJornada> MisJornadas(DateTime desdeLocal, DateTime hastaLocal)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        if (_currentUser.UserId is not int userId) return [];

        // Por UserId y NO por DeveloperId: DeveloperId es una copia opcional tomada al entrar, y
        // las jornadas anteriores a ligar la ficha lo tienen en null — se perderían del total.
        var desde = desdeLocal.Date.ToUniversalTime();
        var hasta = hastaLocal.Date.AddDays(1).ToUniversalTime();   // el último día, completo

        return _db.WorkPresences.AsNoTracking()
            .Where(p => p.UserId == userId && p.StartedAtUtc >= desde && p.StartedAtUtc < hasta)
            .OrderBy(p => p.StartedAtUtc)
            .ToList()
            // Se proyecta y no se devuelve la entidad: State y StateNote NO salen de aquí. El
            // estado es del momento y no se historiza (ver WorkPresence); devolver la fila entera
            // dejaría esa política sostenida solo por disciplina de quien la consuma.
            .Select(p => new MiJornada(p.StartedAtUtc, p.EndedAtUtc, p.Duracion, p.EndReason, p.Origin))
            .ToList();
    }

    // ── Interno ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cierra las jornadas que llevan demasiado sin latir, sellándolas con la hora del ÚLTIMO
    /// latido. Cerrarlas con la hora actual le regalaría a alguien todas las horas que su equipo
    /// pasó apagado.
    /// </summary>
    public int CerrarCaidas()
    {
        var corte = DateTime.UtcNow - ToleranciaSinLatido;
        var caidas = _db.WorkPresences
            .Where(p => p.EndedAtUtc == null && p.LastSeenUtc < corte)
            .ToList();
        if (caidas.Count == 0) return 0;

        int selladas = 0;
        foreach (var p in caidas)
        {
            // Reload antes de sellar: el contexto es Singleton y la resolución de identidad puede
            // devolver una instancia rastreada VIEJA — se sellaría con un LastSeenUtc caducado, o
            // se cerraría una jornada que otra máquina acaba de refrescar o cerrar. Releída, se
            // vuelve a comprobar que de verdad sigue caída. Son pocas filas: el costo es trivial.
            _db.Entry(p).Reload();
            if (p.EndedAtUtc != null || p.LastSeenUtc >= corte) continue;

            p.EndedAtUtc = p.LastSeenUtc;
            p.EndReason  = PresenceEnd.SinLatido;
            selladas++;
        }
        if (selladas == 0) return 0;

        try { _db.SaveChanges(); }
        catch
        {
            // Si el guardado falla (red, Azure SQL), las jornadas del barrido no pueden quedar
            // Modified en el contexto compartido: el siguiente SaveChanges de CUALQUIER servicio
            // las cometería en silencio con este cierre viejo. Detached —no Unchanged— para que la
            // próxima consulta las relea frescas de la base.
            foreach (var p in caidas) _db.Entry(p).State = EntityState.Detached;
            throw;
        }
        return selladas;
    }

    private WorkPresence? Abierta(int userId) =>
        _db.WorkPresences
            .Where(p => p.UserId == userId && p.EndedAtUtc == null)
            .OrderByDescending(p => p.StartedAtUtc)
            .FirstOrDefault();

    private void CerrarAbiertasDe(int userId, PresenceEnd motivo)
    {
        var abiertas = _db.WorkPresences.Where(p => p.UserId == userId && p.EndedAtUtc == null).ToList();
        if (abiertas.Count == 0) return;

        var ahora = DateTime.UtcNow;
        foreach (var p in abiertas)
        {
            // En un cierre normal vale la hora de ahora; en uno caído, la del último latido.
            p.EndedAtUtc = motivo == PresenceEnd.CierreNormal ? ahora : p.LastSeenUtc;
            p.EndReason  = motivo;
        }
        _db.SaveChanges();
    }

    private static string Equipo()
    {
        try { return $"{Environment.MachineName}\\{Environment.UserName}"; }
        catch { return "(desconocido)"; }
    }

    // ── Etiquetas ────────────────────────────────────────────────────────────────

    public static string Etiqueta(PresenceState e) => e switch
    {
        PresenceState.Disponible => "Disponible",
        PresenceState.Ocupado    => "Ocupado",
        PresenceState.EnReunion  => "En reunión",
        PresenceState.Comiendo   => "Comiendo",
        PresenceState.Descanso   => "En un descanso",
        _                        => "Ausente"
    };

    public static string Icono(PresenceState e) => e switch
    {
        PresenceState.Disponible => "🟢",
        PresenceState.Ocupado    => "🔴",
        PresenceState.EnReunion  => "📅",
        PresenceState.Comiendo   => "🍽",
        PresenceState.Descanso   => "☕",
        _                        => "⚪"
    };

    /// <summary>«2 h 15 min», para la duración de una jornada.</summary>
    public static string Duracion(TimeSpan t) =>
        t.TotalMinutes < 1 ? "menos de 1 min"
        : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min"
        : $"{(int)t.TotalHours} h {t.Minutes:00} min";
}
