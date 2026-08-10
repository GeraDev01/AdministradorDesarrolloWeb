using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Desempeno;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Consultas de SOLO LECTURA de las pantallas de desempeño: el ranking del administrador
/// («/desempeno») y el panel del desarrollador («/mi-panel»).
///
/// No calcula nada por su cuenta: el ranking sale entero de <see cref="PerformanceScoringService"/>,
/// que es la fuente única de la puntuación. Lo que aporta este servicio es el RECORTE por rol —qué
/// campos existen para quién— y las frases que el escritorio ya tenía resueltas (la posición, el
/// nivel), para que la pantalla web no las reinvente distinto.
///
/// Vive aparte y no dentro de PerformanceScoringService porque aquel es lógica de negocio compartida
/// con las fases 2 y 3 (registrar, aprobar); esto es la vista de dos pantallas concretas y depende de
/// los DTOs del contrato web.
/// </summary>
public class DesempenoQueryService(AppDbContext db, PerformanceScoringService puntuacion, ICurrentUser actual)
{
    /// <summary>
    /// Los dos rankings del mes para el administrador.
    ///
    /// <paramref name="incluirNivelLead"/> se conserva del escritorio: los de nivel Lead no compiten,
    /// pero el administrador necesita poder verlos y seleccionarlos (allí para ajustar o limpiar sus
    /// puntos). Entran FUERA DE CONCURSO: con corona, sin medalla y sin ocupar lugar.
    /// </summary>
    public async Task<DesempenoAdminDto> RankingAdminAsync(
        int anio, int mes, bool incluirNivelLead, CancellationToken ct = default)
    {
        var individual = await puntuacion.IndividualRankingAsync(anio, mes, incluirNivelLead, ct);
        var equipos = await puntuacion.TeamRankingAsync(anio, mes, ct);

        var filasIndividual = new List<RankingIndividualFilaDto>(individual.Count);
        int lugar = 0;   // solo avanza con competidores: un Lead no ocupa lugar
        foreach (var r in individual)
        {
            string medalla;
            int posicion;
            if (r.EsNivelLead)
            {
                medalla = "👑";
                posicion = 0;
            }
            else
            {
                medalla = Medalla(lugar);
                posicion = ++lugar;
            }

            filasIndividual.Add(new RankingIndividualFilaDto(
                posicion, medalla, r.DeveloperId, r.FullName,
                Total: r.Total, Premio: r.Positive, Penalizacion: r.Negative,
                Entradas: r.Count, EsNivelLead: r.EsNivelLead));
        }

        var filasEquipo = equipos.Select((t, i) => new RankingEquipoFilaDto(
            i + 1, Medalla(i), t.TeamId, t.Name,
            Total: t.Total, PuntosIntegrantes: t.MembersSum, PuntosEquipo: t.TeamOwn,
            Miembros: t.MemberCount)).ToList();

        return new DesempenoAdminDto(anio, mes, incluirNivelLead, filasIndividual, filasEquipo);
    }

    /// <summary>
    /// El panel de quien está usando la aplicación.
    ///
    /// El desarrollador NO se pasa por parámetro: sale de la identidad de la petición. Un id en la
    /// ruta convertiría esta pantalla en «el panel de cualquiera» en cuanto alguien probara otro
    /// número, y aquí van sus puntos rechazados y su posición.
    ///
    /// Si la cuenta no tiene ficha ligada se devuelve el panel igual, con los rankings (que son
    /// públicos dentro de la empresa) y la posición diciendo que no compite: es lo que el escritorio
    /// hacía, y es mejor que una pantalla vacía sin explicación.
    /// </summary>
    public async Task<MiPanelDto> MiPanelAsync(int anio, int mes, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(actual);
        var devId = actual.DeveloperId;

        var yo = devId is int id
            ? await db.Developers.AsNoTracking()
                .Where(d => d.Id == id)
                .Select(d => new { d.TeamId, d.TeamRole, d.Seniority, NombreEquipo = d.Team != null ? d.Team.Name : null })
                .FirstOrDefaultAsync(ct)
            : null;

        // Los dos rankings, ya recortados a nombre y total (ver RankingPublicoFilaDto).
        var individual = await puntuacion.IndividualRankingAsync(anio, mes, ct: ct);
        var equipos = await puntuacion.TeamRankingAsync(anio, mes, ct);

        var filasIndividual = individual.Select((r, i) => new RankingPublicoFilaDto(
            i + 1, Medalla(i), r.FullName, r.Total, EsMio: r.DeveloperId == devId)).ToList();

        var filasEquipo = equipos.Select((t, i) => new RankingPublicoFilaDto(
            i + 1, Medalla(i), t.Name, t.Total, EsMio: yo?.TeamId != null && t.TeamId == yo.TeamId)).ToList();

        // Totales del mes. Sin ficha no hay puntos que sumar, y ceros es lo honesto: no hay nada.
        var totales = devId is int dev
            ? await puntuacion.DevMonthlyTotalsAsync(dev, anio, mes, ct)
            : new DevMonthly(0, 0, 0, 0);

        return new MiPanelDto(
            anio, mes,
            TieneFicha: yo != null,
            Nivel: EtiquetaDeNivel(yo?.Seniority),
            ExplicacionNivel: ExplicacionDeNivel(yo?.Seniority),
            Equipo: yo?.NombreEquipo ?? "Sin equipo",
            RolEnEquipo: yo?.NombreEquipo == null ? null : EtiquetaDeRol(yo.TeamRole),
            Aprobado: totales.Approved,
            EnRevision: totales.Pending,
            Rechazadas: totales.RejectedCount,
            Posicion: await DescribirPosicionAsync(devId, yo?.Seniority, individual.Count, filasIndividual, ct),
            Individual: filasIndividual,
            Equipos: filasEquipo);
    }

    /// <summary>
    /// La frase de la tarjeta «Mi posición». Se resuelve en el servidor porque cada caso viene de una
    /// decisión de producto y no de una preferencia de pintado:
    ///
    ///  · Sin ficha ligada → no compite. Antes esto era un «—» que se reportaba como error.
    ///  · Nivel Lead → «Fuera de ranking (nivel Lead)». Un Lead reparte parte de los puntos y no
    ///    compite contra los niveles que evalúa; decirlo evita que lea un «— de N» como un fallo.
    ///  · En el ranking → «#3 de 12».
    ///  · Activo pero sin aparecer (ficha inactiva) → «— de N», sin inventarle un lugar.
    /// </summary>
    private async Task<string> DescribirPosicionAsync(
        int? devId, string? nivel, int total, List<RankingPublicoFilaDto> filas, CancellationToken ct)
    {
        if (devId == null)
            return "No compites: tu cuenta no está ligada a una ficha de desarrollador.";

        // El nivel Lead se comprueba contra el servicio y no contra el texto de la ficha: es él quien
        // sabe cómo se normaliza («Lead», « lead », «LEAD» son la misma cosa).
        var nivelLead = await puntuacion.IdsConNivelLeadAsync(ct);
        if (nivelLead.Contains(devId.Value)) return "Fuera de ranking (nivel Lead)";

        var mia = filas.FirstOrDefault(f => f.EsMio);
        if (mia != null) return $"#{mia.Posicion} de {total}";

        return total > 0 ? $"— de {total}" : "—";
    }

    /// <summary>🥇🥈🥉 para el podio y «#n» para el resto, con el índice 0-based del ranking.</summary>
    private static string Medalla(int indice) =>
        indice == 0 ? "🥇" : indice == 1 ? "🥈" : indice == 2 ? "🥉" : $"#{indice + 1}";

    /// <summary>
    /// Etiqueta del nivel con icono, igual que en el escritorio. Sin nivel capturado devuelve el
    /// aviso y no una cadena vacía: el hueco no explica nada y el nivel decide qué actividades le
    /// tocan y si compite.
    /// </summary>
    private static string EtiquetaDeNivel(string? nivel)
    {
        var n = (nivel ?? "").Trim();
        if (n.Length == 0) return "— sin nivel registrado —";

        return n.ToLowerInvariant() switch
        {
            "junior" => "🌱 Junior",
            "mid" => "🌿 Mid",
            "senior" => "🌳 Senior",
            "lead" => "🧭 Lead",
            "arquitecto" => "🏛 Arquitecto",
            _ => n   // un nivel escrito a mano se respeta tal cual
        };
    }

    /// <summary>Qué se espera de ese nivel. Null cuando no hay nada que explicar.</summary>
    private static string? ExplicacionDeNivel(string? nivel) => (nivel ?? "").Trim().ToLowerInvariant() switch
    {
        "junior" => "Estás aprendiendo el oficio y el proyecto. Se espera que preguntes pronto y a menudo.",
        "mid" => "Trabajas de forma autónoma en lo tuyo y respondes por lo que entregas.",
        "senior" => "Además de tu trabajo, se espera que guíes decisiones técnicas y apoyes a los demás.",
        "lead" => "Coordinas y evalúas: por eso tu nivel queda fuera del ranking individual.",
        "arquitecto" => "Defines el rumbo técnico más allá de un proyecto concreto.",
        "" => "Tu ficha no tiene nivel capturado. Pídeselo al líder si te hace falta.",
        _ => null
    };

    private static string EtiquetaDeRol(TeamRole rol) => rol switch
    {
        TeamRole.Lider => "Líder del equipo",
        TeamRole.SinRol => "Integrante",
        _ => rol.ToString()
    };
}
