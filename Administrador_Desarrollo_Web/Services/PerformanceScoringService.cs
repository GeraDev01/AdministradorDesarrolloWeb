using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Fuente ÚNICA de la lógica de puntuación/ranking. Antes estaba duplicada en 5 controles de
/// UI (PerformanceControl, DashboardControl, ReportsControl, MyDevPerformanceControl,
/// TeamPointsDetailForm). Regla central: en cualquier agregado del ranking SOLO cuentan las
/// entradas con <see cref="PointApprovalStatus.Aprobado"/>; las Pendiente/Rechazado nunca suman.
/// Extraerla aquí la hace testeable en aislamiento y reutilizable por el futuro portal web.
/// </summary>
public class PerformanceScoringService
{
    private readonly AppDbContext _db;
    public PerformanceScoringService(AppDbContext db) => _db = db;

    /// <summary>
    /// Quiénes tienen el NIVEL «Lead» (Developer.Seniority). Ese nivel es lo que saca del ranking:
    /// un Lead evalúa y reparte parte de los puntos, así que no compite contra Junior/Mid/Senior.
    /// OJO: ser LÍDER DE EQUIPO (TeamRole.Lider / Team.LeadDeveloperId) NO excluye — un Senior
    /// puede coordinar un equipo y sigue compitiendo; se corrigió tras confundirse ambas cosas.
    /// La comparación ignora mayúsculas y espacios: la ficha vieja pudo capturarse a mano antes de
    /// que Seniority fuera un combo cerrado. AsNoTracking y proyección a Id porque el AppDbContext
    /// es Singleton: una entidad rastreada traería un nivel viejo si otra máquina lo cambió.
    /// </summary>
    public HashSet<int> IdsConNivelLead() =>
        _db.Developers.AsNoTracking()
            .Where(d => d.Seniority != null && d.Seniority.Trim().ToLower() == "lead")
            .Select(d => d.Id)
            .ToHashSet();

    /// <summary>
    /// Ranking individual del período (solo puntos aprobados), por desarrollador activo, mayor
    /// total primero. Los de NIVEL Lead quedan fuera por omisión: reparten parte de los puntos y
    /// no compiten contra los niveles que evalúan. Sus puntos SÍ siguen contando para su equipo
    /// (ver <see cref="TeamRanking"/>) y su panel personal no cambia.
    /// <paramref name="incluirNivelLead"/> existe para la pantalla del administrador, que necesita
    /// seleccionarlos para ajustar o limpiar sus puntos.
    /// </summary>
    public List<DevScore> IndividualRanking(int year, int month, bool incluirNivelLead = false)
    {
        var entries = _db.PointEntries
            .Include(p => p.Developer).Include(p => p.Criterion).Include(p => p.Requirement)
            .Where(p => p.Year == year && p.Month == month && p.ApprovalStatus == PointApprovalStatus.Aprobado)
            .ToList();

        var nivelLead = IdsConNivelLead();
        var devs = _db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).ToList()
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
    /// </summary>
    public List<TeamScore> TeamRanking(int year, int month)
    {
        var teams = _db.Teams.OrderBy(t => t.Name).AsNoTracking().ToList();
        var devs = _db.Developers.Where(d => d.IsActive).Select(d => new { d.Id, d.TeamId }).ToList();
        var indiv = _db.PointEntries
            .Where(p => p.Year == year && p.Month == month && p.ApprovalStatus == PointApprovalStatus.Aprobado)
            .Select(p => new { p.DeveloperId, p.Points }).ToList();
        var teamPts = _db.TeamPointEntries.Where(p => p.Year == year && p.Month == month)
            .Select(p => new { p.TeamId, p.Points }).ToList();

        return teams.Select(t =>
        {
            var memberIds = devs.Where(d => d.TeamId == t.Id).Select(d => d.Id).ToHashSet();
            int membersSum = indiv.Where(e => memberIds.Contains(e.DeveloperId)).Sum(e => e.Points);
            int teamOwn = teamPts.Where(e => e.TeamId == t.Id).Sum(e => e.Points);
            return new TeamScore(t.Id, t.Name, membersSum, teamOwn, membersSum + teamOwn, memberIds.Count);
        })
        .OrderByDescending(r => r.Total).ThenBy(r => r.Name)
        .ToList();
    }

    /// <summary>Suma de puntos individuales APROBADOS de los integrantes de un equipo (para el detalle de equipo).</summary>
    public int TeamMembersApprovedSum(int teamId, int year, int month)
    {
        var memberIds = _db.Developers.Where(d => d.TeamId == teamId && d.IsActive).Select(d => d.Id).ToList();
        return _db.PointEntries
            .Where(p => p.Year == year && p.Month == month && p.ApprovalStatus == PointApprovalStatus.Aprobado
                     && memberIds.Contains(p.DeveloperId))
            .Sum(p => p.Points);
    }

    /// <summary>Totales del período de un desarrollador, separados por estado de aprobación.</summary>
    public DevMonthly DevMonthlyTotals(int devId, int year, int month)
    {
        var mine = _db.PointEntries
            .Where(p => p.DeveloperId == devId && p.Year == year && p.Month == month)
            .Select(p => new { p.Points, p.ApprovalStatus }).ToList();
        return new DevMonthly(
            Approved: mine.Where(p => p.ApprovalStatus == PointApprovalStatus.Aprobado).Sum(p => p.Points),
            Pending:  mine.Where(p => p.ApprovalStatus == PointApprovalStatus.Pendiente).Sum(p => p.Points),
            Rejected: mine.Where(p => p.ApprovalStatus == PointApprovalStatus.Rechazado).Sum(p => p.Points),
            RejectedCount: mine.Count(p => p.ApprovalStatus == PointApprovalStatus.Rechazado));
    }

    /// <summary>
    /// Posición 1-based del desarrollador en el ranking individual del período (empates por total,
    /// luego nombre). <c>position = null</c> significa que NO COMPITE (nivel Lead, inactivo o no
    /// existe); antes se devolvía un 0 ambiguo que en pantalla se leería «#0 de N».
    /// </summary>
    public (int? position, int total) PositionOf(int devId, int year, int month)
    {
        var ranking = IndividualRanking(year, month);
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
    public (bool ok, string mensaje, PointEntry? entrada) RegistrarAutocalificacion(
        PointEntry borrador, ICurrentUser currentUser)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, borrador.DeveloperId);

        var criterio = _db.ScoringCriteria.FirstOrDefault(c => c.Id == borrador.CriterionId);
        if (criterio == null)      return (false, "El criterio seleccionado ya no existe.", null);
        if (!criterio.IsActive)    return (false, $"El criterio «{criterio.Name}» fue desactivado.", null);
        if (criterio.Scope == CriterionScope.Equipo)
            return (false, $"«{criterio.Name}» es un criterio de equipo: no se puede autocalificar.", null);
        if (criterio.DefaultPoints <= 0)
            return (false, $"«{criterio.Name}» no otorga puntos positivos. Los descuentos los aplica el administrador.", null);

        if (borrador.Month is < 1 or > 12) return (false, "Mes inválido.", null);

        // Campos que el desarrollador NO decide.
        borrador.Points = criterio.DefaultPoints;
        borrador.ApprovalStatus = PointApprovalStatus.Pendiente;
        borrador.SubmittedByDeveloperId = borrador.DeveloperId;
        borrador.AssignedByUserId = null;
        borrador.ReviewedByUserId = null;
        borrador.ReviewedAt = null;
        borrador.ReviewComment = null;
        borrador.Date = DateTime.UtcNow;

        _db.PointEntries.Add(borrador);
        _db.SaveChanges();
        return (true, $"Actividad registrada (+{borrador.Points} pts). Queda pendiente de aprobación.", borrador);
    }
}

// EsNivelLead va al FINAL y con default: el record es posicional y hay construcciones que no lo pasan.
public sealed record DevScore(int DeveloperId, string FullName, int Total, int Positive, int Negative, int Count, List<PointEntry> Entries, bool EsNivelLead = false);
public sealed record TeamScore(int TeamId, string Name, int MembersSum, int TeamOwn, int Total, int MemberCount);
public sealed record DevMonthly(int Approved, int Pending, int Rejected, int RejectedCount);
