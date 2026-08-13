using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Equipos;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Fuente ÚNICA de la lógica de puntuación/ranking. Antes estaba duplicada en 5 controles de
/// UI (PerformanceControl, DashboardControl, ReportsControl, MyDevPerformanceControl,
/// TeamPointsDetailForm). Regla central: en cualquier agregado del ranking SOLO cuentan las
/// entradas con <see cref="PointApprovalStatus.Aprobado"/>; las Pendiente/Rechazado nunca suman.
/// Extraerla aquí la hace testeable en aislamiento y reutilizable por el futuro portal web.
///
/// Copiado del escritorio con dos cambios de forma, ninguno de fondo:
/// 1. La identidad llega por constructor (<see cref="ICurrentUser"/>) en vez de método a método. En
///    el escritorio el contexto de usuario era un singleton del proceso y se pasaba a mano; aquí lo
///    inyecta la petición, que es quien lo sabe.
/// 2. Desaparecen los <c>Entry(x).Reload()</c> que había antes de decidir sobre una entrada. Estaban
///    para desconfiar de una entidad rastreada por un contexto Singleton compartido; con un contexto
///    por petición, lo que se lee ya es fresco por definición.
/// </summary>
public class PerformanceScoringService(AppDbContext db, ICurrentUser currentUser)
{
    /// <summary>
    /// Quiénes tienen el NIVEL «Lead» (Developer.Seniority). Ese nivel es lo que saca del ranking:
    /// un Lead evalúa y reparte parte de los puntos, así que no compite contra Junior/Mid/Senior.
    /// OJO: ser LÍDER DE EQUIPO (TeamRole.Lider / Team.LeadDeveloperId) NO excluye — un Senior
    /// puede coordinar un equipo y sigue compitiendo; se corrigió tras confundirse ambas cosas.
    /// La comparación ignora mayúsculas y espacios: la ficha vieja pudo capturarse a mano antes de
    /// que Seniority fuera un combo cerrado. AsNoTracking y proyección a Id porque es una consulta
    /// de solo lectura: nada de esto se va a modificar y así no se rastrea de más.
    /// </summary>
    public async Task<HashSet<int>> IdsConNivelLeadAsync(CancellationToken ct = default) =>
        (await db.Developers.AsNoTracking()
            .Where(d => d.Seniority != null && d.Seniority.Trim().ToLower() == "lead")
            .Select(d => d.Id)
            .ToListAsync(ct))
            .ToHashSet();

    /// <summary>
    /// Ranking individual del período (solo puntos aprobados), por desarrollador activo, mayor
    /// total primero. Los de NIVEL Lead quedan fuera por omisión: reparten parte de los puntos y
    /// no compiten contra los niveles que evalúan. Sus puntos SÍ siguen contando para su equipo
    /// (ver <see cref="TeamRankingAsync"/>) y su panel personal no cambia.
    /// <paramref name="incluirNivelLead"/> existe para la pantalla del administrador, que necesita
    /// seleccionarlos para ajustar o limpiar sus puntos.
    /// </summary>
    public async Task<List<DevScore>> IndividualRankingAsync(
        int year, int month, bool incluirNivelLead = false, CancellationToken ct = default)
    {
        var entries = await db.PointEntries
            .Include(p => p.Developer).Include(p => p.Criterion).Include(p => p.Requirement)
            .Where(p => p.Year == year && p.Month == month && p.ApprovalStatus == PointApprovalStatus.Aprobado)
            .ToListAsync(ct);

        var nivelLead = await IdsConNivelLeadAsync(ct);
        var devs = (await db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).ToListAsync(ct))
            .Where(d => incluirNivelLead || !nivelLead.Contains(d.Id))
            .ToList();
        return devs.Select(dev =>
        {
            var de = entries.Where(e => e.DeveloperId == dev.Id).OrderByDescending(e => e.Date).ToList();
            return new DevScore(dev.Id, dev.FullName,
                Total: de.Sum(e => e.Points),
                Positive: de.Where(e => e.Points > 0).Sum(e => e.Points),
                Negative: de.Where(e => e.Points < 0).Sum(e => e.Points),
                Count: de.Count, Entries: de,
                EsNivelLead: nivelLead.Contains(dev.Id));
        })
        .OrderByDescending(r => r.Total)
        .ToList();
    }

    /// <summary>
    /// Ranking por equipo: puntos aprobados de sus integrantes + puntos propios del equipo. Mayor
    /// total primero. Aquí TODOS cuentan —también los de nivel Lead, en la suma y en MemberCount—:
    /// la competencia es entre equipos y cada quien es parte del suyo. Restar los puntos del Lead
    /// castigaría justo a los equipos cuyo Lead más trabaja, y crearía el incentivo de no
    /// registrarle actividad.
    ///
    /// <para><b>UN EQUIPO CON SUBEQUIPOS NO COMPITE: no sale en la tabla.</b> Es la regla del dueño
    /// —«el ranking de equipos es de subequipos, el padre no juega»— y resuelve de raíz el problema
    /// que tenían las dos alternativas. Si el padre compitiera sumando su rama, ganaría siempre a
    /// sus propios hijos porque lleva los puntos de ellos dentro; y si compitiera solo con lo suyo,
    /// un padre que es un paraguas sin gente directa saldría eternamente a cero, que se lee como que
    /// va perdiendo cuando lo que pasa es que no juega.</para>
    ///
    /// <para><b>Consecuencia que hay que conocer:</b> los puntos de quien esté asignado
    /// DIRECTAMENTE a un equipo padre no cuentan para ningún equipo. En el ranking individual
    /// cuentan igual que siempre; lo que desaparece es su aportación a la competición entre equipos.
    /// No se reparten entre los hijos —serían puntos que esos equipos no ganaron— ni se le apuntan
    /// al padre, que no está. La pantalla lo DICE en vez de dejar que alguien note a fin de mes que
    /// su equipo ya no sale; ver Desempeño.</para>
    ///
    /// <para>Con esto <c>TotalConSubequipos</c> se quedó sin sentido y se fue: existía para enseñarle
    /// la suma de la rama a un padre que ya no aparece.</para>
    /// </summary>
    public async Task<List<TeamScore>> TeamRankingAsync(int year, int month, CancellationToken ct = default)
    {
        var teams = await db.Teams.OrderBy(t => t.Name).AsNoTracking().ToListAsync(ct);
        var devs = await db.Developers.Where(d => d.IsActive).Select(d => new { d.Id, d.TeamId }).ToListAsync(ct);
        var indiv = await db.PointEntries
            .Where(p => p.Year == year && p.Month == month && p.ApprovalStatus == PointApprovalStatus.Aprobado)
            .Select(p => new { p.DeveloperId, p.Points }).ToListAsync(ct);
        var teamPts = await db.TeamPointEntries.Where(p => p.Year == year && p.Month == month)
            .Select(p => new { p.TeamId, p.Points }).ToListAsync(ct);

        // Se filtra ANTES de puntuar y no después: recorrer los puntos de un equipo que no va a salir
        // es trabajo tirado, y sobre todo deja un total calculado rondando por ahí que alguien
        // acabaría enseñando en algún sitio.
        var jerarquia = JerarquiaDeEquipos.De(teams);

        return teams
            .Where(t => !jerarquia.TieneSubequipos(t.Id))
            .Select(t =>
            {
                var memberIds = devs.Where(d => d.TeamId == t.Id).Select(d => d.Id).ToHashSet();
                int membersSum = indiv.Where(e => memberIds.Contains(e.DeveloperId)).Sum(e => e.Points);
                int teamOwn = teamPts.Where(e => e.TeamId == t.Id).Sum(e => e.Points);
                return new TeamScore(t.Id, t.Name, membersSum, teamOwn, membersSum + teamOwn, memberIds.Count);
            })
            .OrderByDescending(r => r.Total).ThenBy(r => r.Name)
            .ToList();
    }

    /// <summary>
    /// Los equipos que NO compiten por tener subequipos colgando, con cuánta gente tienen asignada
    /// directamente. Es lo que la pantalla necesita para explicar la ausencia en vez de dejar un
    /// hueco: un equipo que desaparece de una tabla sin decir por qué se lee como un fallo.
    ///
    /// <para>La cuenta de gente directa importa porque es la que avisa del caso que duele: un padre
    /// con integrantes propios tiene puntos que no cuentan para ningún equipo. Con cero, no hay nada
    /// que echar en falta.</para>
    /// </summary>
    public async Task<List<(int TeamId, string Name, int PersonasDirectas)>> EquiposQueNoCompitenAsync(
        CancellationToken ct = default)
    {
        var teams = await db.Teams.OrderBy(t => t.Name).AsNoTracking().ToListAsync(ct);
        var jerarquia = JerarquiaDeEquipos.De(teams);
        var devs = await db.Developers.Where(d => d.IsActive)
            .Select(d => new { d.TeamId }).ToListAsync(ct);

        return teams
            .Where(t => jerarquia.TieneSubequipos(t.Id))
            .Select(t => (t.Id, t.Name, devs.Count(d => d.TeamId == t.Id)))
            .ToList();
    }

    /// <summary>Suma de puntos individuales APROBADOS de los integrantes de un equipo (para el detalle de equipo).</summary>
    public async Task<int> TeamMembersApprovedSumAsync(int teamId, int year, int month, CancellationToken ct = default)
    {
        var memberIds = await db.Developers.Where(d => d.TeamId == teamId && d.IsActive).Select(d => d.Id).ToListAsync(ct);
        return await db.PointEntries
            .Where(p => p.Year == year && p.Month == month && p.ApprovalStatus == PointApprovalStatus.Aprobado
                     && memberIds.Contains(p.DeveloperId))
            .SumAsync(p => p.Points, ct);
    }

    /// <summary>Totales del período de un desarrollador, separados por estado de aprobación.</summary>
    public async Task<DevMonthly> DevMonthlyTotalsAsync(int devId, int year, int month, CancellationToken ct = default)
    {
        var mine = await db.PointEntries
            .Where(p => p.DeveloperId == devId && p.Year == year && p.Month == month)
            .Select(p => new { p.Points, p.ApprovalStatus, p.MinutesSpent }).ToListAsync(ct);
        return new DevMonthly(
            Approved: mine.Where(p => p.ApprovalStatus == PointApprovalStatus.Aprobado).Sum(p => p.Points),
            Pending:  mine.Where(p => p.ApprovalStatus == PointApprovalStatus.Pendiente).Sum(p => p.Points),
            Rejected: mine.Where(p => p.ApprovalStatus == PointApprovalStatus.Rechazado).Sum(p => p.Points),
            RejectedCount: mine.Count(p => p.ApprovalStatus == PointApprovalStatus.Rechazado),
            // El tiempo declarado se contabiliza aparte de los puntos: son dos medidas distintas
            // (cuánto trabajó vs. cuánto se le reconoció) y mezclarlas escondería una de las dos.
            MinutesApproved: mine.Where(p => p.ApprovalStatus == PointApprovalStatus.Aprobado).Sum(p => p.MinutesSpent ?? 0),
            MinutesPending:  mine.Where(p => p.ApprovalStatus == PointApprovalStatus.Pendiente).Sum(p => p.MinutesSpent ?? 0));
    }

    /// <summary>
    /// Minutos declarados por un desarrollador en el período, contando solo lo aprobado más lo que
    /// sigue en revisión. Lo rechazado no suma: si el jefe no reconoció la actividad, su tiempo
    /// tampoco debe engrosar el total.
    /// </summary>
    public async Task<int> MinutosDeclaradosAsync(int devId, int year, int month, CancellationToken ct = default)
    {
        var t = await DevMonthlyTotalsAsync(devId, year, month, ct);
        return t.MinutesApproved + t.MinutesPending;
    }

    /// <summary>
    /// Posición 1-based del desarrollador en el ranking individual del período (empates por total,
    /// luego nombre). <c>position = null</c> significa que NO COMPITE (nivel Lead, inactivo o no
    /// existe); antes se devolvía un 0 ambiguo que en pantalla se leería «#0 de N».
    /// </summary>
    public async Task<(int? position, int total)> PositionOfAsync(
        int devId, int year, int month, CancellationToken ct = default)
    {
        var ranking = await IndividualRankingAsync(year, month, ct: ct);
        for (int i = 0; i < ranking.Count; i++)
            if (ranking[i].DeveloperId == devId) return (i + 1, ranking.Count);
        return (null, ranking.Count);
    }

    /// <summary>
    /// Registra la autocalificación de un desarrollador.
    ///
    /// El puntaje NO se toma de lo que venga en <paramref name="borrador"/>: se relee del criterio
    /// en la base y ese es el que se guarda. Solo el administrador fija cuánto vale cada actividad
    /// (editando el criterio o ajustando la entrada al aprobarla); el desarrollador elige QUÉ
    /// actividad registra, no CUÁNTO vale. Aunque la pantalla ya no deja escribir el número, la
    /// regla se aplica aquí para que no dependa de la UI.
    /// </summary>
    public async Task<(bool ok, string mensaje, PointEntry? entrada)> RegistrarAutocalificacionAsync(
        PointEntry borrador, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, borrador.DeveloperId);

        var (valido, error, criterio, enlace) = await ValidarBorradorAsync(borrador, ct);
        if (!valido) return (false, error, null);

        // Campos que el desarrollador NO decide.
        borrador.Points = criterio!.DefaultPoints;
        borrador.EvidenceUrl = enlace;
        borrador.ApprovalStatus = PointApprovalStatus.Pendiente;
        borrador.SubmittedByDeveloperId = borrador.DeveloperId;
        borrador.AssignedByUserId = null;
        borrador.ReviewedByUserId = null;
        borrador.ReviewedAt = null;
        borrador.ReviewComment = null;
        borrador.Date = DateTime.UtcNow;

        db.PointEntries.Add(borrador);
        await db.SaveChangesAsync(ct);
        return (true, $"Actividad registrada (+{borrador.Points} pts). Queda pendiente de aprobación.", borrador);
    }

    /// <summary>
    /// Corrige una autocalificación que el desarrollador ya envió: criterio, período, comentario,
    /// tiempo dedicado, enlace de evidencia, captura y requerimiento.
    ///
    /// Se puede mientras NO esté aprobada — pendiente o rechazada. Una rechazada suele estarlo
    /// justamente por algo que se puede arreglar (faltaba el enlace, el período estaba mal), y
    /// obligar a registrarla de nuevo hacía perder la captura, el comentario y el hilo de la
    /// conversación. Aprobada sí se cierra: cambiar después del visto bueno aquello sobre lo que se
    /// dio el visto bueno vaciaría de sentido la aprobación.
    ///
    /// Corregir NO la devuelve a revisión: para eso está <see cref="ReplicarAsync"/>, que es donde el
    /// desarrollador argumenta. Son dos actos distintos y mezclarlos dejaría al administrador
    /// entradas reabiertas sin una palabra que explique por qué.
    ///
    /// Igual que al registrar, el puntaje se reevalúa desde el criterio: si el desarrollador
    /// cambia de criterio al corregir, los puntos siguen al criterio nuevo.
    /// </summary>
    public async Task<(bool ok, string mensaje)> EditarAutocalificacionAsync(
        int entryId, PointEntry cambios, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var entrada = await db.PointEntries.FirstOrDefaultAsync(p => p.Id == entryId, ct);
        if (entrada == null) return (false, "La actividad ya no existe. Actualiza la lista.");

        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, entrada.DeveloperId);

        if (entrada.SubmittedByDeveloperId == null)
            return (false, "Esa entrada la asignó el líder, no se edita desde aquí.");

        if (entrada.ApprovalStatus == PointApprovalStatus.Aprobado)
            return (false, "La actividad ya fue aprobada y no se puede modificar.");

        // El desarrollador nunca cambia de dueño la entrada: se valida sobre el dueño real.
        cambios.DeveloperId = entrada.DeveloperId;
        var (valido, error, criterio, enlace) = await ValidarBorradorAsync(cambios, ct);
        if (!valido) return (false, error);

        entrada.CriterionId    = cambios.CriterionId;
        entrada.Points         = criterio!.DefaultPoints;
        entrada.Year           = cambios.Year;
        entrada.Month          = cambios.Month;
        entrada.Comment        = string.IsNullOrWhiteSpace(cambios.Comment) ? null : cambios.Comment.Trim();
        entrada.RequirementId  = cambios.RequirementId;
        entrada.MinutesSpent   = cambios.MinutesSpent;
        entrada.EvidenceUrl    = enlace;
        entrada.Screenshot     = cambios.Screenshot;
        entrada.ScreenshotFileName = cambios.ScreenshotFileName;

        await db.SaveChangesAsync(ct);
        return (true, entrada.ApprovalStatus == PointApprovalStatus.Rechazado
            ? $"Actividad corregida (+{entrada.Points} pts). Sigue rechazada: usa «Replicar» para mandarla otra vez a revisión."
            : $"Actividad actualizada (+{entrada.Points} pts). Sigue pendiente de aprobación.");
    }

    /// <summary>Tope del argumento de una réplica. Da para explicarse, no para un ensayo.</summary>
    public const int MaxArgumento = 1000;

    /// <summary>
    /// El desarrollador responde a un rechazo y devuelve la actividad a revisión.
    ///
    /// Antes un rechazo era el final del camino: si el jefe se había equivocado, o si faltaba un
    /// dato que sí existía, no había forma de decirlo dentro de la aplicación y la discusión se iba
    /// a un chat donde no queda constancia. Ahora el desacuerdo vive en la misma entrada.
    ///
    /// El argumento es OBLIGATORIO: una réplica vacía es volver a mandar lo mismo esperando otra
    /// respuesta, y le devuelve al administrador un trabajo que ya hizo sin darle nada nuevo que
    /// valorar. El motivo del rechazo no se pierde: pasa al historial antes de limpiarse.
    /// </summary>
    public async Task<(bool ok, string mensaje)> ReplicarAsync(
        int entryId, string? argumento, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var entrada = await db.PointEntries.FirstOrDefaultAsync(p => p.Id == entryId, ct);
        if (entrada == null) return (false, "La actividad ya no existe. Actualiza la lista.");

        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, entrada.DeveloperId);

        if (entrada.SubmittedByDeveloperId == null)
            return (false, "Esa entrada la asignó el líder; no es una autocalificación tuya que puedas replicar.");

        if (entrada.ApprovalStatus != PointApprovalStatus.Rechazado)
            return (false, entrada.ApprovalStatus == PointApprovalStatus.Aprobado
                ? "Esta actividad ya fue aprobada: no hay nada que replicar."
                : "Esta actividad ya está en revisión; espera la respuesta.");

        argumento = (argumento ?? "").Trim();
        if (argumento.Length == 0)
            return (false, "Escribe por qué crees que debería aprobarse: es lo que el líder va a leer.");
        if (argumento.Length > MaxArgumento)
            return (false, $"El argumento no puede pasar de {MaxArgumento} caracteres.");

        // El motivo del rechazo se guarda ANTES de limpiarlo: es la mitad de la conversación que
        // se está discutiendo, y dejar la entrada «pendiente» con un comentario de rechazo pegado
        // haría creer que ya la volvieron a responder.
        if (!string.IsNullOrWhiteSpace(entrada.ReviewComment))
            AnotarEnHistorial(entrada, $"Rechazada: {entrada.ReviewComment}");

        AnotarEnHistorial(entrada, $"Réplica de {currentUser.Username ?? "el desarrollador"}: {argumento}");

        entrada.ApprovalStatus = PointApprovalStatus.Pendiente;
        entrada.ReviewRound++;
        entrada.ReviewComment = null;
        entrada.ReviewedByUserId = null;
        entrada.ReviewedAt = null;

        await db.SaveChangesAsync(ct);
        return (true, $"Enviada de nuevo a revisión (vuelta {entrada.ReviewRound + 1}). El líder verá tu argumento.");
    }

    /// <summary>Tope del historial. Un ida y vuelta muy largo no debe crecer sin límite.</summary>
    private const int MaxHistorial = 8000;

    /// <summary>
    /// Agrega una línea fechada al historial de revisión sin borrar lo anterior. Lo usan tanto la
    /// réplica del desarrollador como el rechazo del administrador, para que la conversación se lea
    /// completa y en orden desde los dos lados.
    /// </summary>
    public static void AnotarEnHistorial(PointEntry entrada, string linea)
    {
        var sello = $"[{DateTime.Now:dd/MM/yyyy HH:mm}] {linea.Trim()}";
        entrada.ReviewHistory = string.IsNullOrWhiteSpace(entrada.ReviewHistory)
            ? sello
            : entrada.ReviewHistory + "\n" + sello;

        // Se recorta por el PRINCIPIO: lo último que se dijo es lo que hace falta para decidir.
        if (entrada.ReviewHistory.Length > MaxHistorial)
            entrada.ReviewHistory = "(…)\n" + entrada.ReviewHistory[^MaxHistorial..];
    }

    /// <summary>
    /// Tope del tiempo declarable en UNA entrada: los minutos que caben en el mes al que se
    /// atribuye. No es un límite de política sino de realidad — sirve para atajar el dedazo
    /// («600» por «60») sin rechazar un registro legítimamente grande.
    /// </summary>
    public const int MaxMinutosDeclarados = 31 * 24 * 60;

    /// <summary>
    /// Reglas comunes a registrar y editar. Devuelve el criterio ya resuelto y el enlace
    /// normalizado para que quien llama no vuelva a consultarlos.
    /// </summary>
    private async Task<(bool ok, string error, ScoringCriterion? criterio, string? enlace)> ValidarBorradorAsync(
        PointEntry b, CancellationToken ct)
    {
        var criterio = await db.ScoringCriteria.FirstOrDefaultAsync(c => c.Id == b.CriterionId, ct);
        if (criterio == null)   return (false, "El criterio seleccionado ya no existe.", null, null);
        if (!criterio.IsActive) return (false, $"El criterio «{criterio.Name}» fue desactivado.", null, null);
        if (criterio.Scope == CriterionScope.Equipo)
            return (false, $"«{criterio.Name}» es un criterio de equipo: no se puede autocalificar.", null, null);
        if (criterio.DefaultPoints <= 0)
            return (false, $"«{criterio.Name}» no otorga puntos positivos. Los descuentos los aplica el líder.", null, null);

        if (b.Month is < 1 or > 12) return (false, "Mes inválido.", null, null);

        if (b.MinutesSpent is int min)
        {
            if (min < 0) return (false, "El tiempo dedicado no puede ser negativo.", null, null);
            if (min > MaxMinutosDeclarados)
                return (false, $"El tiempo dedicado no puede pasar de {MaxMinutosDeclarados / 60} horas en un solo registro.", null, null);
            if (min == 0) b.MinutesSpent = null;   // «0 minutos» y «no lo capturé» son lo mismo
        }

        var (enlaceOk, enlaceError, enlace) = NormalizarEnlace(b.EvidenceUrl);
        if (!enlaceOk) return (false, enlaceError, null, null);

        return (true, "", criterio, enlace);
    }

    /// <summary>
    /// Valida el enlace de evidencia. Solo se aceptan http y https porque el administrador lo abre
    /// con el navegador al revisar: admitir cualquier esquema (file:, un protocolo registrado por
    /// otra aplicación…) convertiría un campo de texto que llena el desarrollador en una forma de
    /// hacer que el jefe ejecute algo con un clic.
    /// </summary>
    public static (bool ok, string error, string? url) NormalizarEnlace(string? enlace)
    {
        enlace = (enlace ?? "").Trim();
        if (enlace.Length == 0) return (true, "", null);
        if (enlace.Length > 500) return (false, "El enlace no puede pasar de 500 caracteres.", null);

        if (!Uri.TryCreate(enlace, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return (false, "El enlace debe ser una dirección completa que empiece con http:// o https:// " +
                           "(copia la del PR, work item o ticket desde la barra del navegador).", null);

        return (true, "", enlace);
    }
}

// EsNivelLead va al FINAL y con default: el record es posicional y hay construcciones que no lo pasan.
public sealed record DevScore(int DeveloperId, string FullName, int Total, int Positive, int Negative, int Count, List<PointEntry> Entries, bool EsNivelLead = false);
// Aquí solo llegan equipos que COMPITEN. Los que tienen subequipos no entran en el ranking, así que
// no hay ningún campo para «lo que suma mi rama»: ese número existía para enseñárselo a un padre que
// ya no aparece en la tabla. Ver TeamRankingAsync.
public sealed record TeamScore(
    int TeamId, string Name, int MembersSum, int TeamOwn, int Total, int MemberCount);
// Los minutos van al FINAL y con default: el record es posicional y hay construcciones previas
// (y pruebas) que solo pasan los cuatro campos de puntos.
public sealed record DevMonthly(int Approved, int Pending, int Rejected, int RejectedCount,
                                int MinutesApproved = 0, int MinutesPending = 0);
