using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

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
///
/// La única adaptación del port es de dónde sale el «equipo» de la jornada: en el escritorio era
/// <c>MÁQUINA\usuario</c>, que aquí sería siempre el nombre del servidor y no distinguiría nada, así
/// que se inyecta <see cref="IRequestOrigin"/> igual que en la bitácora.
/// </summary>
public class PresenceService(AppDbContext db, ICurrentUser currentUser, IRequestOrigin origin)
{
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
    /// <summary>
    /// Cuánto puede haber estado cerrada una jornada para que volver a conectarse la REANUDE en vez
    /// de abrir otra.
    ///
    /// <para>Dos minutos cubren lo que de verdad ocurre: recargar con F5, que el portátil se
    /// suspenda un momento, que el wifi parpadee. Más allá de eso ya es alguien que se fue y volvió,
    /// y ahí dos tramos separados describen mejor el día que un único bloque que se comió el hueco.</para>
    /// </summary>
    private static readonly TimeSpan VentanaDeReanudacion = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Empieza —o RETOMA— la jornada de quien acaba de conectarse.
    ///
    /// <para><b>Antes abría siempre una fila nueva, y eso partía el día en pedazos.</b> En el
    /// escritorio había una instancia por sesión de Windows y la jornada era una sola de la mañana a
    /// la noche; en un navegador, cada F5 y cada pestaña es una conexión más, así que el registro
    /// acababa con ocho o diez jornadas de minutos donde hubo una de ocho horas. De paso se perdía
    /// el estado —cada conexión lo devolvía a «Disponible»— y con él la nota de «vuelvo a las 15:30».</para>
    ///
    /// <para>Ahora hay tres casos, en este orden:</para>
    /// <list type="number">
    ///   <item>Ya hay una jornada ABIERTA: es otra pestaña del mismo día. Se reutiliza tal cual,
    ///   conservando estado y nota.</item>
    ///   <item>La última se cerró hace muy poco: fue un F5 o un parpadeo de red. Se REANUDA
    ///   —se le quita la marca de cierre— para no dejar dos tramos por un hueco de segundos.</item>
    ///   <item>No hay nada reciente: empieza una de verdad, en «Disponible».</item>
    /// </list>
    ///
    /// <para>Que el estado sobreviva a recargar es consecuencia, no un arreglo aparte: si la fila es
    /// la misma, lo que había en ella sigue ahí.</para>
    /// </summary>
    public async Task<WorkPresence?> EntrarAsync(string? origen = null, CancellationToken ct = default)
    {
        if (currentUser.UserId is not int userId) return null;

        var ahora = DateTime.UtcNow;

        // (1) Una abierta Y CON LATIDO RECIENTE: es otra pestaña. Solo se refresca el latido; el
        //     estado y la nota no se tocan.
        //
        //     Lo de «con latido reciente» no es un detalle: sin esa condición, una jornada que se
        //     quedó colgada ayer —el proceso murió, nadie la selló— se reutilizaría hoy y quedaría
        //     una sola de dieciocho horas. Se usa la MISMA tolerancia que el barrido por latido para
        //     que las dos rutas coincidan en qué consideran «viva»; si no, una podría reutilizar lo
        //     que la otra acaba de dar por muerto.
        var vivaDesde = ahora - ToleranciaSinLatido;
        var abierta = await db.WorkPresences
            .Where(p => p.UserId == userId && p.EndedAtUtc == null && p.LastSeenUtc >= vivaDesde)
            .OrderByDescending(p => p.StartedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (abierta != null)
        {
            abierta.LastSeenUtc = ahora;
            await db.SaveChangesAsync(ct);
            return abierta;
        }

        // (2) Una recién cerrada: fue una recarga, no una salida. Se reabre la MISMA fila.
        var desde = ahora - VentanaDeReanudacion;
        var reciente = await db.WorkPresences
            .Where(p => p.UserId == userId && p.EndedAtUtc != null && p.EndedAtUtc >= desde)
            .OrderByDescending(p => p.EndedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (reciente != null)
        {
            reciente.EndedAtUtc = null;
            reciente.EndReason  = null;
            reciente.LastSeenUtc = ahora;
            await db.SaveChangesAsync(ct);
            return reciente;
        }

        // (3) Una nueva. Antes de abrirla se sellan las que quedaran colgando de un cierre sucio
        // viejo —fuera de la ventana de reanudación—, con la hora de su último latido.
        await CerrarAbiertasDeAsync(userId, PresenceEnd.SinLatido, ct);

        var jornada = new WorkPresence
        {
            UserId       = userId,
            DeveloperId  = currentUser.DeveloperId,
            DisplayName  = currentUser.FullName ?? currentUser.Username ?? $"Usuario #{userId}",
            StartedAtUtc = ahora,
            LastSeenUtc  = ahora,
            State        = PresenceState.Disponible,
            Origin       = origen ?? Equipo()
        };
        db.WorkPresences.Add(jornada);
        await db.SaveChangesAsync(ct);
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
    public async Task LatirAsync(CancellationToken ct = default)
    {
        await CerrarCaidasAsync(ct);

        if (currentUser.UserId is int userId)
        {
            var mia = await AbiertaAsync(userId, ct);
            if (mia != null) { mia.LastSeenUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
            else await EntrarAsync(ct: ct);   // se cerró (corte de red, suspensión): se abre otra
        }
    }

    /// <summary>Cierra la jornada al cerrar sesión o al salir de la aplicación.</summary>
    public async Task SalirAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is not int userId) return;
        await CerrarAbiertasDeAsync(userId, PresenceEnd.CierreNormal, ct);
    }

    /// <summary>Cambia el estado propio. Nadie puede cambiar el de otra persona.</summary>
    public async Task<(bool ok, string mensaje)> CambiarEstadoAsync(PresenceState estado, string? nota = null,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.");

        var mia = await AbiertaAsync(userId, ct);
        if (mia == null)
        {
            mia = await EntrarAsync(ct: ct);
            if (mia == null) return (false, "No hay una sesión válida.");
        }

        nota = (nota ?? "").Trim();
        mia.State = estado;
        mia.StateNote = nota.Length == 0 ? null : (nota.Length > 200 ? nota[..200] : nota);
        mia.LastSeenUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (true, $"Estado: {Etiqueta(estado)}.");
    }

    /// <summary>El estado propio ahora mismo, para pintar el selector.</summary>
    public async Task<(PresenceState estado, string? nota)> MiEstadoAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is int userId && await AbiertaAsync(userId, ct) is { } mia)
            return (mia.State, mia.StateNote);
        return (PresenceState.Disponible, null);
    }

    // ── Tablero (administrador) ──────────────────────────────────────────────────

    /// <summary>
    /// Quién está conectado y en qué anda, más quién no lo está y desde cuándo. Incluye a TODAS las
    /// cuentas activas, no solo a las que han abierto la aplicación alguna vez: si alguien falta,
    /// esa ausencia es el dato.
    /// </summary>
    public async Task<List<PresenciaDeUsuario>> TableroAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);
        await CerrarCaidasAsync(ct);

        var corte = DateTime.UtcNow - ToleranciaSinLatido;
        var usuarios = await db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new { u.Id, u.FullName, u.Username })
            .ToListAsync(ct);

        // Las jornadas abiertas son pocas (una por persona conectada): se traen enteras.
        var abiertas = (await db.WorkPresences.AsNoTracking()
            .Where(p => p.EndedAtUtc == null)
            .ToListAsync(ct))
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.StartedAtUtc).First());

        // Para los desconectados basta con cuándo se les vio por última vez; se resuelve con un
        // agregado en la base en vez de arrastrar el historial completo a memoria.
        var ultimoVisto = (await db.WorkPresences.AsNoTracking()
            .GroupBy(p => p.UserId)
            .Select(g => new { UserId = g.Key, Ultimo = g.Max(p => p.LastSeenUtc) })
            .ToListAsync(ct))
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
    public async Task<List<WorkPresence>> JornadasDelDiaAsync(DateTime diaLocal, int? userId = null,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        // El registro también se consulta cuando nadie más tuvo la aplicación abierta para barrer
        // (lunes por la mañana, tras un feriado): sin esto, una jornada caída se listaría como
        // «en curso». Es idempotente y casi siempre afecta 0 filas.
        await CerrarCaidasAsync(ct);

        var desde = diaLocal.Date.ToUniversalTime();
        var hasta = diaLocal.Date.AddDays(1).ToUniversalTime();

        var q = db.WorkPresences.AsNoTracking()
            .Where(p => p.StartedAtUtc >= desde && p.StartedAtUtc < hasta);
        if (userId is int uid) q = q.Where(p => p.UserId == uid);

        return await q.OrderBy(p => p.DisplayName).ThenBy(p => p.StartedAtUtc).ToListAsync(ct);
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
    public async Task<List<MiJornada>> MisJornadasAsync(DateTime desdeLocal, DateTime hastaLocal,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId) return [];

        // Por UserId y NO por DeveloperId: DeveloperId es una copia opcional tomada al entrar, y
        // las jornadas anteriores a ligar la ficha lo tienen en null — se perderían del total.
        var desde = desdeLocal.Date.ToUniversalTime();
        var hasta = hastaLocal.Date.AddDays(1).ToUniversalTime();   // el último día, completo

        return (await db.WorkPresences.AsNoTracking()
            .Where(p => p.UserId == userId && p.StartedAtUtc >= desde && p.StartedAtUtc < hasta)
            .OrderBy(p => p.StartedAtUtc)
            .ToListAsync(ct))
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
    ///
    /// Dos peticiones simultáneas pueden barrer a la vez, y no pasa nada: ambas sellan la fila con
    /// el MISMO valor (su propio LastSeenUtc), así que la segunda escritura no cambia el resultado.
    /// </summary>
    public async Task<int> CerrarCaidasAsync(CancellationToken ct = default)
    {
        var corte = DateTime.UtcNow - ToleranciaSinLatido;
        var caidas = await db.WorkPresences
            .Where(p => p.EndedAtUtc == null && p.LastSeenUtc < corte)
            .ToListAsync(ct);
        if (caidas.Count == 0) return 0;

        foreach (var p in caidas)
        {
            p.EndedAtUtc = p.LastSeenUtc;
            p.EndReason  = PresenceEnd.SinLatido;
        }
        await db.SaveChangesAsync(ct);
        return caidas.Count;
    }

    private Task<WorkPresence?> AbiertaAsync(int userId, CancellationToken ct) =>
        db.WorkPresences
            .Where(p => p.UserId == userId && p.EndedAtUtc == null)
            .OrderByDescending(p => p.StartedAtUtc)
            .FirstOrDefaultAsync(ct);

    private async Task CerrarAbiertasDeAsync(int userId, PresenceEnd motivo, CancellationToken ct)
    {
        var abiertas = await db.WorkPresences.Where(p => p.UserId == userId && p.EndedAtUtc == null).ToListAsync(ct);
        if (abiertas.Count == 0) return;

        var ahora = DateTime.UtcNow;
        foreach (var p in abiertas)
        {
            // En un cierre normal vale la hora de ahora; en uno caído, la del último latido.
            p.EndedAtUtc = motivo == PresenceEnd.CierreNormal ? ahora : p.LastSeenUtc;
            p.EndReason  = motivo;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// De dónde se abrió la jornada. En el escritorio era el nombre del equipo y del usuario de
    /// Windows; aquí todo corre en el mismo servidor, así que lo que distingue una jornada de otra
    /// del mismo día es el cliente: su IP y su navegador.
    /// </summary>
    private string Equipo() => origin.Describir();

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
