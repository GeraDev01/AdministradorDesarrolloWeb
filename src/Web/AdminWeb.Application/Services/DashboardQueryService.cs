using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Dashboard;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace AdminWeb.Application.Services;

/// <summary>
/// Las consultas del dashboard. Solo lectura: esta pantalla no tiene un solo botón que cambie nada,
/// ni en el escritorio ni aquí.
///
/// Traducción de <c>DashboardControl</c>, con la decisión de fondo intacta y el lugar donde se aplica
/// cambiado. Allá el control NI SIQUIERA CONSTRUÍA los tres paneles de datos de equipo cuando el rol
/// no debía verlos, y por eso las tres consultas no llegaban a correr: los datos del equipo nunca
/// entraban en el proceso. Aquí el proceso es el servidor y la pantalla vive en la máquina de cada
/// quien, así que el mismo razonamiento —«no basta con no pintarlos»— obliga a decidirlo ANTES de
/// consultar y a que la respuesta salga sin esos campos. Un desarrollador no recibe la carga del
/// equipo, ni los recordatorios, ni el ranking: no es que se le oculten, es que no viajan.
/// </summary>
public class DashboardQueryService(AppDbContext db, ICurrentUser currentUser, PerformanceScoringService scoring)
{
    /// <summary>Cuántos días hacia adelante mira «Próximas entregas». Igual que el escritorio.</summary>
    private const int DiasDeVentana = 7;

    /// <summary>Tope de filas de cada panel, heredado del escritorio (15 entregas, 10 notas, 5 del podio).</summary>
    private const int MaxEntregas = 15;
    private const int MaxRecordatorios = 10;
    private const int MaxPodio = 5;

    /// <summary>
    /// El período del ranking se rotula en el servidor. Podría formatearlo el navegador, pero
    /// WebAssembly publica recortado y la cultura que acabe cargada ahí no es una promesa: un
    /// «August 2026» en una pantalla en español sería el resultado más probable de confiar en ella.
    /// </summary>
    private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-MX");

    public async Task<DashboardDto> ObtenerAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        // La carga por desarrollador, los recordatorios internos y el ranking son datos del EQUIPO.
        // Se decide una sola vez y aquí arriba, porque de este booleano depende que tres consultas
        // se ejecuten o no: si corren, los datos ya salieron de la base aunque después no se envíen.
        bool veDatosDeEquipo = currentUser.IsAdmin;

        // Ficha a la que hay que acotar las cifras. Un administrador ve el área completa aunque tenga
        // ficha ligada, que es lo que necesita para dirigir.
        //
        // OJO con el caso que el escritorio no cubría: allí «acota a mi ficha» era
        // `IsAdmin ? null : DeveloperId`, y una cuenta NO administradora SIN ficha caía en null, es
        // decir, en «ver el área completa». Aquí ese caso se acota a una ficha que no existe (0), o
        // sea a cero filas. Un fallo de configuración no puede terminar abriendo los datos de todos.
        int fichaPropia = currentUser.DeveloperId ?? 0;

        var requerimientos = db.Requirements.AsNoTracking();
        if (!veDatosDeEquipo)
            requerimientos = requerimientos.Where(r => r.Assignments.Any(a => a.DeveloperId == fichaPropia));

        // Un solo agrupado en la base en vez de los seis conteos en memoria del escritorio: allí se
        // materializaba la tabla entera de requerimientos para contarla, cosa que funciona con
        // trescientas filas y deja de funcionar cuando son treinta mil.
        var porEstado = await requerimientos
            .GroupBy(r => r.Status)
            .Select(g => new { Estado = g.Key, Cuantos = g.Count() })
            .ToDictionaryAsync(x => x.Estado, x => x.Cuantos, ct);

        int Cuantos(RequirementStatus s) => porEstado.TryGetValue(s, out var n) ? n : 0;

        // Los recordatorios son una herramienta del administrador; a un desarrollador solo le cuentan
        // los que llevan su nombre.
        var notasPendientes = db.Notes.AsNoTracking().Where(n => !n.IsCompleted);
        if (!veDatosDeEquipo) notasPendientes = notasPendientes.Where(n => n.DeveloperId == fichaPropia);
        int pendientes = await notasPendientes.CountAsync(ct);

        var tarjetas = new TarjetasDto(
            PorEstimar:   Cuantos(RequirementStatus.PorEstimar),
            EnDesarrollo: Cuantos(RequirementStatus.EnDesarrollo),
            PorEntregar:  Cuantos(RequirementStatus.PorEntregar),
            Entregados:   Cuantos(RequirementStatus.Entregado),
            Cancelados:   Cuantos(RequirementStatus.Cancelado),
            Pendientes:   pendientes);

        // «Hoy» se fija una vez y se usa para filtrar Y para decidir qué está atrasado: si se leyera
        // el reloj dos veces, una entrega justo en el límite podría filtrarse con un día y pintarse
        // con otro.
        var hoy = DateTime.Today;
        var limite = hoy.AddDays(DiasDeVentana);

        var entregas = await requerimientos
            .Where(r => r.CommittedDeliveryDate != null
                     && r.CommittedDeliveryDate <= limite
                     && r.Status != RequirementStatus.Entregado
                     && r.Status != RequirementStatus.Cancelado)
            .OrderBy(r => r.CommittedDeliveryDate)
            .Take(MaxEntregas)
            .Select(r => new { r.Id, r.Title, r.Status, r.CommittedDeliveryDate })
            .ToListAsync(ct);

        var proximasEntregas = entregas
            .Select(r => new EntregaProximaDto(
                r.Id, r.Title, r.Status, EtiquetaDeEstado(r.Status),
                r.CommittedDeliveryDate,
                Atrasado: r.CommittedDeliveryDate < hoy))
            .ToList();

        var periodo = Capitalizar(new DateTime(hoy.Year, hoy.Month, 1).ToString("MMMM yyyy", Espanol));

        // A partir de aquí, nada se consulta si el rol no tiene derecho a verlo.
        if (!veDatosDeEquipo)
            return new DashboardDto(tarjetas, proximasEntregas, null, null, null, periodo);

        var carga = await db.Developers.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.FullName)
            .Select(d => new CargaDesarrolladorDto(
                d.Id,
                d.FullName,
                d.Assignments.Count(a => a.Requirement.Status != RequirementStatus.Entregado
                                      && a.Requirement.Status != RequirementStatus.Cancelado),
                d.Assignments.Count(a => a.Requirement.Status == RequirementStatus.PorEntregar)))
            .ToListAsync(ct);

        // Sin fecha de recordatorio va primero, igual que en el escritorio: son las notas que nadie
        // ha calendarizado y las que más fácil se quedan olvidadas.
        var notas = await db.Notes.AsNoTracking()
            .Where(n => !n.IsCompleted)
            .OrderBy(n => n.ReminderDate)
            .Take(MaxRecordatorios)
            .Select(n => new { n.Id, n.Title, n.Priority, n.ReminderDate })
            .ToListAsync(ct);

        var recordatorios = notas
            .Select(n => new RecordatorioDto(n.Id, n.Title, n.Priority, n.ReminderDate,
                Atrasado: n.ReminderDate != null && n.ReminderDate < hoy))
            .ToList();

        // El podio se delega en el servicio de puntuación para no divergir de la pantalla de
        // Desempeño. Esa era la copia más desviada del escritorio (agrupaba sobre entradas y no sobre
        // desarrolladores) y habría seguido enseñando al líder en primer lugar cuando el ranking
        // oficial ya lo excluye. El filtro por Count > 0 conserva la regla de siempre: sin entradas
        // no hay podio, para que nadie suba al primer puesto con cero puntos.
        var ranking = await scoring.IndividualRankingAsync(hoy.Year, hoy.Month, ct: ct);
        var podio = ranking
            .Where(r => r.Count > 0)
            .Take(MaxPodio)
            .Select((r, i) => new PuestoRankingDto(i + 1, r.DeveloperId, r.FullName, r.Total, r.Count))
            .ToList();

        return new DashboardDto(tarjetas, proximasEntregas, carga, recordatorios, podio, periodo);
    }

    /// <summary>
    /// Nombre legible del estado. Se resuelve en el servidor y viaja junto al enum: así la pantalla
    /// no tiene que repetir la tabla de traducción, que es donde el escritorio la fue duplicando.
    /// </summary>
    private static string EtiquetaDeEstado(RequirementStatus s) => s switch
    {
        RequirementStatus.PorEstimar   => "Por estimar",
        RequirementStatus.Estimado     => "Estimado",
        RequirementStatus.EnDesarrollo => "En desarrollo",
        RequirementStatus.EnPruebas    => "En pruebas",
        RequirementStatus.PorEntregar  => "Por entregar",
        RequirementStatus.Entregado    => "Entregado",
        RequirementStatus.Cancelado    => "Cancelado",
        _                              => s.ToString()
    };

    /// <summary>En español el nombre del mes va en minúscula; como rótulo se ve mejor capitalizado.</summary>
    private static string Capitalizar(string texto) =>
        texto.Length == 0 ? texto : char.ToUpper(texto[0], Espanol) + texto[1..];
}
