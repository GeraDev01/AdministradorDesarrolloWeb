using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.DevOps;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Arma de una sola vez lo que enseña cada pantalla de Azure DevOps y lo traduce a contratos propios.
///
/// <para>Existe por dos motivos, ninguno de negocio: las reglas siguen enteras en
/// <see cref="DevOpsService"/>, que es quien decide y quien habla con DevOps.</para>
///
/// <para>El primero es que ningún tipo de EF puede cruzar al navegador. Un <see cref="DevOpsTicket"/>
/// arrastra la descripción completa del work item —que puede ser un documento entero—, el correo del
/// asignado y sus vínculos con Freshdesk; nada de eso pinta una rejilla y todo eso viajaría en cada
/// respuesta.</para>
///
/// <para>El segundo son los desplegables. En el escritorio la pantalla recorría la lista en memoria
/// cada vez que hacía falta saber qué estados existen o a quién se puede reasignar; aquí eso se
/// resuelve una vez por petición y viaja con los datos, para no obligar al navegador a encadenar
/// cinco llamadas para pintar una pantalla.</para>
/// </summary>
public class DevOpsQueryService(AppDbContext db, ICurrentUser usuario, DevOpsService devops)
{
    /// <summary>
    /// El tablero completo del líder.
    ///
    /// <para>Trae TODOS los tickets sincronizados, igual que el escritorio, y a propósito: el filtro
    /// por columna, la búsqueda global y las estadísticas trabajan sobre el conjunto entero, y
    /// paginar del lado del servidor convertiría «cuántos bugs hay sin asignar» en una respuesta que
    /// depende de en qué página estés. Si algún día la tabla creciera hasta que esto pesara, lo que
    /// hay que mover al servidor es el filtro completo, no solo la paginación.</para>
    /// </summary>
    public async Task<TableroDevOpsDto> TableroAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuario);

        var tickets = await db.DevOpsTickets.AsNoTracking()
            .OrderByDescending(t => t.UpdatedAtExternal)
            .ToListAsync(ct);

        var vigilados = await VigiladosAsync(ct);

        var filtros = await db.DevOpsSavedFilters.AsNoTracking()
            .OrderBy(f => f.Name)
            .Select(f => new FiltroGuardadoDto(
                f.Id, f.Name, f.GlobalSearch, f.TitleContains, f.ColumnFiltersJson))
            .ToListAsync(ct);

        var reglas = await db.DevOpsAssignmentRules.AsNoTracking()
            .OrderBy(r => r.Order).ThenBy(r => r.Id)
            .Select(r => new ReglaDeAsignacionDto(
                r.Id, r.Match, "", r.MatchValue, r.DeveloperId,
                r.Developer.FullName, r.Order, r.IsActive))
            .ToListAsync(ct);

        // La etiqueta de la condición se pone aquí y no en la consulta: traducirla dentro del árbol de
        // LINQ obligaría a que el proveedor supiera de un switch en C#.
        reglas = reglas.Select(r => r with { CondicionTexto = EtiquetaDeCondicion(r.Condicion) }).ToList();

        var desarrolladores = await db.Developers.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.FullName)
            .Select(d => new DesarrolladorDevOpsDto(d.Id, d.FullName, d.Email))
            .ToListAsync(ct);

        var estado = await devops.EstadoAsync(ct);

        return new TableroDevOpsDto(
            estado,
            tickets.Select(t => AVista(t, vigilados)).ToList(),
            filtros,
            reglas,
            desarrolladores,
            Distintos(tickets.Select(t => t.State)),
            Distintos(tickets.Select(t => t.WorkItemType)),
            // El SELLO de la última sincronización correcta manda sobre el MAX(SyncedAt) de los
            // tickets. Deducirlo de los tickets mentía cuando una sincronización no traía ninguno
            // —la fecha se quedaba vieja— y cuando el filtro dejaba la tabla vacía —desaparecía—.
            // El cálculo viejo se conserva de respaldo para las bases que ya sincronizaban antes de
            // que el sello existiera; como la sincronización es MANUAL, esta fecha es lo único que
            // distingue «esto es de hoy» de «esto lleva una semana sin tocarse».
            estado.UltimaSincronizacionUtc
                ?? (tickets.Count == 0 ? null : tickets.Max(t => t.SyncedAt)),
            tickets.Count(t => t.SinPrioridadDefinida && !DevOpsService.EsCerrado(t.State)),
            vigilados.Count);
    }

    /// <summary>
    /// El tablero por etiqueta: qué cliente o categoría acumula más bugs, tareas y solicitudes.
    /// </summary>
    /// <param name="dentroDe">Entrar a una etiqueta para ver el desglose dentro de ella.</param>
    /// <param name="soloAbiertos">Excluir lo cerrado.</param>
    public async Task<TableroPorEtiquetaDto> PorEtiquetaAsync(
        string? dentroDe = null, bool soloAbiertos = false, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuario);

        // Se proyecta a lo mínimo que el cálculo necesita: traer los tickets enteros para contar
        // etiquetas movería las descripciones completas de miles de work items.
        var tickets = await db.DevOpsTickets.AsNoTracking()
            .Select(t => new TicketParaEtiquetas(t.Tags, t.WorkItemType, t.State))
            .ToListAsync(ct);

        var etiquetas = DevOpsTagStats.EtiquetasDistintas(tickets);

        // Una etiqueta que ya no existe (porque se renombró en DevOps y se re-sincronizó) se ignora en
        // vez de devolver un tablero vacío que parece un fallo.
        if (dentroDe != null && !etiquetas.Any(e => e.Equals(dentroDe, StringComparison.OrdinalIgnoreCase)))
            dentroDe = null;

        var filas = DevOpsTagStats.Agregar(tickets, dentroDe, soloAbiertos);
        var (delSubconjunto, bugs, tareas, historias) =
            DevOpsTagStats.Indicadores(tickets, dentroDe, soloAbiertos);

        var resumen = dentroDe == null
            ? $"{filas.Count} etiqueta(s) sobre {tickets.Count} tickets sincronizados."
            : $"Dentro de «{dentroDe}»: {filas.Count} etiqueta(s) que coexisten.";

        return new TableroPorEtiquetaDto(
            etiquetas,
            filas.Select(f => new FilaPorEtiquetaDto(
                f.Etiqueta, f.Total, f.Abiertos, f.Bugs, f.Tareas, f.UserStories, f.Otros)).ToList(),
            dentroDe,
            delSubconjunto, bugs, tareas, historias,
            resumen);
    }

    /// <summary>
    /// «Mis tickets DevOps»: los work items a nombre de quien tiene la sesión, ya filtrados.
    ///
    /// El filtrado se hace en el SERVIDOR aunque la lista sea corta, porque la ventana de días y el
    /// «solo sin estimar» son reglas portadas y probadas: repetirlas en la pantalla dejaría dos copias
    /// y la segunda es la que se desincroniza.
    /// </summary>
    public async Task<MisTicketsDevOpsDto> MisTicketsAsync(
        FiltroDeMisTickets filtro, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(usuario, "de los tickets de Azure DevOps");

        var estado = await devops.EstadoAsync(ct);
        bool puedoSincronizar = estado.Configurada && estado.TengoPatPropio;

        if (usuario.DeveloperId is not int developerId)
            return Vacio(estado, puedoSincronizar, TieneFicha: false,
                "Tu cuenta no está ligada a una ficha de desarrollador, así que no se puede saber qué " +
                "tickets son tuyos. Pídeselo al líder.");

        var dev = await db.Developers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == developerId, ct);
        if (dev == null)
            return Vacio(estado, puedoSincronizar, TieneFicha: false,
                "No se encontró tu ficha de desarrollador.");

        var mios = await DevOpsTicketQuery.DeDesarrolladorAsync(db, dev, ct);

        string? aviso = null;
        if (mios.Count == 0)
        {
            // Los dos casos llevan a acciones distintas y por eso se distinguen: «no hay nada» se
            // arregla sincronizando; «hay tickets pero ninguno tuyo» suele ser que el correo de la
            // ficha no coincide con el de la cuenta de DevOps, y la sincronización con @Me lo salva.
            bool hayAlgo = await db.DevOpsTickets.AnyAsync(ct);
            aviso = hayAlgo
                ? $"No encontramos tickets de DevOps a tu nombre ({dev.FullName}). " +
                  "Pulsa «Sincronizar mis tickets»: pregunta a DevOps por lo asignado a la cuenta de TU " +
                  "token, así que funciona aunque el correo de tu ficha no coincida con el de tu cuenta."
                : "Todavía no hay tickets de DevOps sincronizados. " +
                  "Pulsa «Sincronizar mis tickets» para traer los tuyos con tu token personal.";
        }

        var vigilados = await VigiladosAsync(ct);
        var filtrados = MyDevOpsTicketFilter.Aplicar(mios, filtro, DateTime.UtcNow);

        int abiertos = mios.Count(t => !DevOpsService.EsCerrado(t.State));
        int sinEstimar = mios.Count(t => t.SinEstimar && !DevOpsService.EsCerrado(t.State));
        DateTime? ultima = mios.Count == 0 ? null : mios.Max(t => t.SyncedAt);

        return new MisTicketsDevOpsDto(
            TieneFicha: true,
            estado.TengoPatPropio,
            puedoSincronizar,
            aviso,
            filtrados.Select(t => AVista(t, vigilados)).ToList(),
            MyDevOpsTicketFilter.Opciones(mios.Select(t => t.State)),
            MyDevOpsTicketFilter.Opciones(mios.Select(t => t.WorkItemType)),
            MyDevOpsTicketFilter.Opciones(mios.Select(t => t.IterationPath)),
            mios.Count,
            abiertos,
            sinEstimar,
            ultima,
            MyDevOpsTicketFilter.Resumen(filtrados.Count, mios.Count, abiertos, ultima));
    }

    // ── Exportación ──────────────────────────────────────────────────────────────

    /// <summary>
    /// El tablero completo en una hoja de cálculo.
    ///
    /// Va SIN filtrar, a diferencia del escritorio, que exportaba lo que la rejilla estuviera
    /// enseñando. El motivo es que aquí el filtro por columna lo aplica el navegador y el servidor no
    /// lo conoce; exportar «lo que se ve» exigiría mandar el estado de la rejilla en la petición, y
    /// una exportación que a veces trae 40 filas y a veces 4000 según lo que nadie recuerda haber
    /// filtrado es peor que una que siempre trae todo.
    /// </summary>
    public async Task<byte[]> ExportarTableroAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuario);

        var tickets = await db.DevOpsTickets.AsNoTracking()
            .OrderByDescending(t => t.UpdatedAtExternal)
            .ToListAsync(ct);

        var columnas = new[]
        {
            "ID", "Tipo", "Título", "Estado", "Prioridad", "Asignado a", "Iteración", "Área",
            "Etiquetas", "Story points", "Horas estimadas", "Comentarios", "Actualizado", "URL"
        };

        var filas = tickets.Select(t => new object?[]
        {
            t.ExternalId, t.WorkItemType, t.Title, t.State, t.Priority, t.AssignedTo,
            t.IterationPath, t.AreaPath, t.Tags, t.StoryPoints, t.EstimatedHours, t.CommentCount,
            t.UpdatedAtExternal?.ToLocalTime(), t.Url
        }).ToList();

        return HojaDeCalculo.Escribir(columnas, filas, "Azure DevOps");
    }

    /// <summary>El tablero por etiqueta en una hoja de cálculo, con el mismo corte que se está viendo.</summary>
    public async Task<byte[]> ExportarPorEtiquetaAsync(
        string? dentroDe = null, bool soloAbiertos = false, CancellationToken ct = default)
    {
        var tablero = await PorEtiquetaAsync(dentroDe, soloAbiertos, ct);

        var columnas = new[] { "Etiqueta", "Tickets", "Abiertos", "Bugs", "Tareas", "User Stories", "Otros" };
        var filas = tablero.Filas
            .Select(f => new object?[] { f.Etiqueta, f.Total, f.Abiertos, f.Bugs, f.Tareas, f.UserStories, f.Otros })
            .ToList();

        return HojaDeCalculo.Escribir(columnas, filas, "DevOps por etiqueta");
    }

    // ── Interno ──────────────────────────────────────────────────────────────────

    private static MisTicketsDevOpsDto Vacio(
        EstadoDevOpsDto estado, bool puedoSincronizar, bool TieneFicha, string aviso) =>
        new(TieneFicha, estado.TengoPatPropio, puedoSincronizar, aviso,
            [], [], [], [], 0, 0, 0, null,
            MyDevOpsTicketFilter.Resumen(0, 0, 0, null));

    /// <summary>Los tickets que vigila quien hace la petición. Vigilar es personal.</summary>
    private async Task<HashSet<int>> VigiladosAsync(CancellationToken ct)
    {
        var quien = usuario.Username ?? "";
        if (quien.Length == 0) return [];

        return (await db.WatchedTickets.AsNoTracking()
                .Where(w => w.WatchedByUser == quien)
                .Select(w => w.DevOpsTicketId)
                .ToListAsync(ct))
            .ToHashSet();
    }

    private static TicketDevOpsDto AVista(DevOpsTicket t, IReadOnlySet<int> vigilados) => new(
        t.Id,
        t.ExternalId,
        t.Title,
        t.WorkItemType,
        t.State,
        t.Priority,
        t.AssignedTo,
        t.IterationPath,
        t.AreaPath,
        t.Tags,
        t.StoryPoints,
        t.EstimatedHours,
        t.CommentCount,
        t.UpdatedAtExternal,
        t.SyncedAt,
        t.Url,
        vigilados.Contains(t.Id),
        t.SinPrioridadDefinida,
        t.SinEstimar,
        DevOpsService.EsCerrado(t.State));

    private static List<string> Distintos(IEnumerable<string?> valores) =>
        valores.Where(v => !string.IsNullOrWhiteSpace(v))
               .Select(v => v!.Trim())
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
               .ToList();

    /// <summary>Cómo se lee una condición de regla. Mismos textos que el escritorio.</summary>
    public static string EtiquetaDeCondicion(DevOpsRuleMatch condicion) => condicion switch
    {
        DevOpsRuleMatch.AreaPathContiene => "Área contiene",
        DevOpsRuleMatch.TipoEsIgual => "Tipo es igual a",
        DevOpsRuleMatch.TituloContiene => "Título contiene",
        DevOpsRuleMatch.TagContiene => "Etiqueta contiene",
        DevOpsRuleMatch.AsignadoAContiene => "Asignado a contiene",
        _ => condicion.ToString()
    };
}
