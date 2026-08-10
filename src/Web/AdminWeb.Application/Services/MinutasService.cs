using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Administracion;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Las minutas del equipo y los compromisos que salen de ellas.
///
/// En el escritorio esto no era un servicio: la pantalla hablaba directamente con el contexto de
/// datos y la ventana de detalle devolvía la minuta entera armada. Aquí tiene que serlo — la
/// pantalla corre en el navegador y no puede tocar la base—, y de paso las reglas que estaban
/// repartidas entre la lista y el formulario quedan en un solo sitio.
///
/// <para><b>Sobre las fechas.</b> La fecha de una minuta es un DÍA del calendario, no un instante:
/// «la daily del martes». Se guarda a mediodía UTC y no a medianoche, y esa es la única diferencia
/// real con el escritorio. El motivo es que el escritorio pinta esa columna con
/// <c>ToLocalTime()</c>: una fecha guardada a medianoche UTC se le convierte en el día anterior en
/// cualquier huso al oeste de Greenwich —el nuestro—, y mientras las dos aplicaciones convivan la
/// misma minuta no puede aparecer en días distintos según dónde se mire. A mediodía el día aguanta
/// once horas de desfase en las dos direcciones.</para>
///
/// <para>El contenido es texto que escribe una persona y sale de aquí como texto: no se interpreta
/// como HTML en ningún punto del camino.</para>
/// </summary>
public class MinutasService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
{
    /// <summary>
    /// La lista con sus filtros, más lo que la pantalla necesita para pintarse: los tipos y a quién
    /// se le puede encargar un compromiso.
    /// </summary>
    /// <param name="desde">Primer día incluido. Null = sin límite por abajo.</param>
    /// <param name="hasta">Último día incluido; se compara hasta el final de ese día.</param>
    public async Task<MinutasDto> ListarAsync(
        MinuteType? tipo, DateTime? desde, DateTime? hasta, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var consulta = db.Minutes.AsNoTracking().Include(m => m.ActionItems).AsQueryable();

        if (tipo is { } t) consulta = consulta.Where(m => m.Type == t);
        if (desde is { } d) consulta = consulta.Where(m => m.Date >= d.Date);
        // El día «hasta» entra completo: nadie entiende «hasta el 5» como «hasta las 00:00 del 5».
        if (hasta is { } h) consulta = consulta.Where(m => m.Date < h.Date.AddDays(1));

        var minutas = await consulta.OrderByDescending(m => m.Date).ToListAsync(ct);

        var responsables = await db.Developers.AsNoTracking()
            .Where(x => x.IsActive).OrderBy(x => x.FullName)
            .Select(x => new OpcionDto(x.Id, x.FullName))
            .ToListAsync(ct);

        return new MinutasDto(
            minutas.Select(m => new MinutaEnListaDto(
                m.Id, m.Type, Etiqueta(m.Type), m.Date, m.Title,
                m.ActionItems.Count,
                m.ActionItems.Count(i => !i.IsCompleted))).ToList(),
            Tipos,
            responsables);
    }

    /// <summary>Una minuta completa, con su contenido y sus compromisos. Null si ya no está.</summary>
    public async Task<MinutaDto?> ObtenerAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var minuta = await db.Minutes.AsNoTracking()
            .Include(m => m.ActionItems).ThenInclude(i => i.ResponsibleDeveloper)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (minuta == null) return null;

        return new MinutaDto(
            minuta.Id, minuta.Type, minuta.Date, minuta.Title, minuta.Content,
            minuta.ActionItems
                .OrderBy(i => i.Id)
                .Select(i => new CompromisoDeMinutaDto(
                    i.Id, i.Description, i.ResponsibleDeveloperId,
                    i.ResponsibleDeveloper?.FullName, i.DueDate, i.IsCompleted))
                .ToList());
    }

    /// <summary>
    /// Alta o edición, con sus compromisos de una sola vez.
    /// </summary>
    /// <remarks>
    /// Los compromisos llegan COMPLETOS y se sincronizan contra lo que hay: se borra lo que ya no
    /// viene, se actualiza lo que sigue y se agrega lo nuevo. Es lo que hacía el escritorio, con una
    /// corrección: allí el conjunto de «ids que se conservan» se tomaba tal cual del formulario, así
    /// que un id de OTRA minuta bastaba para salvar de la baja a un compromiso ajeno. Aquí solo se
    /// consideran los ids que de verdad pertenecen a esta minuta; el resto se trata como altas.
    /// </remarks>
    public async Task<(bool ok, string mensaje, int id)> GuardarAsync(
        GuardarMinutaRequest cambio, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var titulo = (cambio.Titulo ?? "").Trim();
        if (titulo.Length == 0) return (false, "El título es obligatorio.", 0);

        var compromisos = (cambio.Compromisos ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Descripcion))
            .ToList();

        // Un responsable inexistente reventaría al guardar con un error de clave foránea, que no le
        // dice nada a quien está capturando. Se comprueba antes y se explica.
        var responsables = compromisos.Where(c => c.ResponsableId is int).Select(c => c.ResponsableId!.Value).Distinct().ToList();
        if (responsables.Count > 0)
        {
            int existen = await db.Developers.CountAsync(d => responsables.Contains(d.Id), ct);
            if (existen != responsables.Count)
                return (false, "Alguno de los responsables ya no existe. Recarga la minuta y vuelve a asignarlo.", 0);
        }

        var minuta = cambio.Id > 0
            ? await db.Minutes.Include(m => m.ActionItems).FirstOrDefaultAsync(m => m.Id == cambio.Id, ct)
            : null;

        bool esAlta = minuta == null;
        if (esAlta)
        {
            if (cambio.Id > 0) return (false, "Esa minuta ya no existe. Actualiza la lista.", 0);
            minuta = new Minute { CreatedAt = DateTime.UtcNow, CreatedById = currentUser.UserId };
            db.Minutes.Add(minuta);
        }

        minuta!.Type = cambio.Tipo;
        minuta.Date = ADiaDeCalendario(cambio.Fecha);
        minuta.Title = titulo;
        minuta.Content = string.IsNullOrWhiteSpace(cambio.Contenido) ? null : cambio.Contenido.Trim();

        // Los ids que de verdad son de esta minuta; cualquier otro entra como compromiso nuevo.
        var propios = minuta.ActionItems.ToDictionary(i => i.Id);
        var conservados = compromisos.Where(c => propios.ContainsKey(c.Id)).Select(c => c.Id).ToHashSet();

        foreach (var sobra in minuta.ActionItems.Where(i => !conservados.Contains(i.Id)).ToList())
            db.MinuteActionItems.Remove(sobra);

        foreach (var c in compromisos)
        {
            if (propios.TryGetValue(c.Id, out var existente) && conservados.Contains(c.Id))
            {
                existente.Description = c.Descripcion.Trim();
                existente.ResponsibleDeveloperId = c.ResponsableId;
                existente.DueDate = c.Limite is { } l ? ADiaDeCalendario(l) : null;
                existente.IsCompleted = c.Cumplido;
            }
            else
            {
                minuta.ActionItems.Add(new MinuteActionItem
                {
                    Description = c.Descripcion.Trim(),
                    ResponsibleDeveloperId = c.ResponsableId,
                    DueDate = c.Limite is { } l ? ADiaDeCalendario(l) : null,
                    IsCompleted = c.Cumplido
                });
            }
        }

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(esAlta ? AuditAction.Create : AuditAction.Update,
            "Minute", minuta.Id.ToString(), minuta.Title, ct);

        int pendientes = compromisos.Count(c => !c.Cumplido);
        return (true,
            esAlta
                ? $"Minuta «{minuta.Title}» creada con {compromisos.Count} compromiso(s), {pendientes} pendiente(s)."
                : $"Minuta «{minuta.Title}» actualizada: {compromisos.Count} compromiso(s), {pendientes} pendiente(s).",
            minuta.Id);
    }

    /// <summary>Borra una minuta con sus compromisos (la FK va en cascada).</summary>
    public async Task<(bool ok, string mensaje)> EliminarAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var minuta = await db.Minutes.Include(m => m.ActionItems).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (minuta == null) return (false, "Esa minuta ya no existe. Actualiza la lista.");

        int pendientes = minuta.ActionItems.Count(i => !i.IsCompleted);
        var titulo = minuta.Title;

        db.Minutes.Remove(minuta);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Delete, "Minute", id.ToString(), titulo, ct);

        return (true, pendientes == 0
            ? $"Minuta «{titulo}» eliminada."
            : $"Minuta «{titulo}» eliminada junto con {pendientes} compromiso(s) que seguían pendientes.");
    }

    /// <summary>
    /// Las minutas en una hoja de cálculo, con las mismas columnas del escritorio.
    ///
    /// Se exporta LO QUE EL FILTRO ESTÁ ENSEÑANDO. El escritorio exportaba siempre la tabla entera,
    /// ignorando el tipo y el rango de fechas de su propia barra; el botón está justo al lado de esos
    /// filtros y bajar otra cosa distinta de lo que se ve sorprende a quien acaba de acotar el mes.
    /// </summary>
    public async Task<byte[]> ExcelAsync(
        MinuteType? tipo, DateTime? desde, DateTime? hasta, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var consulta = db.Minutes.AsNoTracking().Include(m => m.ActionItems).AsQueryable();
        if (tipo is { } t) consulta = consulta.Where(m => m.Type == t);
        if (desde is { } d) consulta = consulta.Where(m => m.Date >= d.Date);
        if (hasta is { } h) consulta = consulta.Where(m => m.Date < h.Date.AddDays(1));

        var minutas = await consulta.OrderByDescending(m => m.Date).ToListAsync(ct);

        var filas = minutas.Select(m => new object?[]
        {
            m.Id, Etiqueta(m.Type), m.Date.ToString("dd/MM/yyyy"), m.Title, m.Content,
            m.ActionItems.Count, m.ActionItems.Count(i => !i.IsCompleted)
        }).ToList();

        return HojaDeCalculo.Escribir(
            ["ID", "Tipo", "Fecha", "Título", "Contenido", "# Items", "Pendientes"],
            filas, "Minutas");
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un día del calendario, guardado a mediodía UTC. Ver la nota de fechas de la clase: es lo que
    /// impide que la misma minuta salga en días distintos en la web y en el escritorio.
    /// </summary>
    private static DateTime ADiaDeCalendario(DateTime fecha) =>
        DateTime.SpecifyKind(fecha.Date.AddHours(12), DateTimeKind.Utc);

    /// <summary>Los tipos con su texto. Los manda el servidor para que no haya una segunda copia en la pantalla.</summary>
    private static readonly IReadOnlyList<TipoDeMinutaDto> Tipos =
    [
        new(MinuteType.Daily, "Daily"),
        new(MinuteType.Sesion, "Sesión"),
        new(MinuteType.Otro, "Otro"),
    ];

    private static string Etiqueta(MinuteType t) => t switch
    {
        MinuteType.Daily => "Daily",
        MinuteType.Sesion => "Sesión",
        _ => "Otro"
    };
}
