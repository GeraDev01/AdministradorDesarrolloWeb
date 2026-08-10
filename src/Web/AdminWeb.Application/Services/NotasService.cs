using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Notas;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Notas y pendientes del líder: lo que le comentan de paso y no debe olvidársele.
///
/// En el escritorio esto vivía como segunda pestaña de la pantalla de vacaciones, y era un
/// accidente de la interfaz: <see cref="Note"/> no tiene una sola relación con
/// <see cref="VacationRequest"/> ni comparte con ella un ciclo de vida. Aquí es un módulo con su
/// propia dirección, y las reglas que allá estaban repartidas entre la rejilla y la ventana de
/// detalle quedan en un solo sitio.
///
/// <para><b>Es del líder, entero.</b> Una nota puede decir «Ana pidió cambiar de proyecto» o
/// «revisar el sueldo de Beto»: se apunta a una persona pero no se escribe para ella, y por eso ni
/// se listan ni se cuentan por desarrollador. El campo del desarrollador es de ORIGEN —quién lo
/// comentó—, no un destinatario. El dashboard ya aplicaba esa misma distinción antes de que este
/// servicio existiera.</para>
///
/// <para><b>Sobre las fechas.</b> El recordatorio es un DÍA del calendario y se guarda a mediodía
/// UTC, la misma regla que <see cref="MinutasService"/>. El escritorio guardaba ahí la medianoche
/// local y pinta esa columna sin convertir, así que las dos formas se leen igual mientras convivan;
/// a mediodía, además, el día aguanta once horas de desfase en las dos direcciones si algún día
/// alguien mete un <c>ToLocalTime()</c> por el camino. La fecha de alta sí es un instante y sigue
/// siendo <c>UtcNow</c>, como en el escritorio.</para>
///
/// <para>El contenido es texto que escribe una persona y sale de aquí como texto: no se interpreta
/// como HTML en ningún punto del camino.</para>
/// </summary>
public class NotasService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
{
    /// <summary>Tope del título. Es el renglón de una lista, no un párrafo.</summary>
    public const int MaxTitulo = 200;

    /// <summary>
    /// Tope de las notas adicionales. El escritorio no tenía ninguno porque el contenido nunca salía
    /// de la máquina; aquí la lista lo lleva consigo, así que sin tope el tamaño de la respuesta lo
    /// decidiría quien pegue un correo entero dentro de una nota.
    /// </summary>
    public const int MaxContenido = 4000;

    // ── Consulta ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// La lista completa, de la más reciente a la más antigua.
    /// </summary>
    /// <param name="soloPendientes">Deja fuera las completadas. Es AÑADIDO respecto al escritorio, y
    /// por omisión está apagado para que lo que se vea al entrar sea lo mismo de siempre: allá las
    /// completadas se quedaban en la rejilla en gris, que funciona mientras quepan en una pantalla
    /// densa y deja de funcionar cuando hay que paginar para llegar a lo que falta por hacer.</param>
    public async Task<NotasDto> ListarAsync(bool soloPendientes = false, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var consulta = db.Notes.AsNoTracking().Include(n => n.Developer).AsQueryable();
        if (soloPendientes) consulta = consulta.Where(n => !n.IsCompleted);

        // El mismo orden del escritorio: lo último que alguien te dijo, arriba.
        var notas = await consulta.OrderByDescending(n => n.CreatedAt).ToListAsync(ct);

        // «Hoy» se lee UNA vez y sirve para marcar las filas Y para contar las vencidas: leyendo el
        // reloj dos veces, una nota justo en el límite podría pintarse atrasada y no entrar en la
        // cuenta que la anuncia.
        var hoy = DateTime.Today;

        var filas = notas.Select(n => new NotaDto(
            n.Id,
            n.Title,
            n.Content,
            n.DeveloperId,
            n.Developer?.FullName,
            n.Priority,
            Etiqueta(n.Priority),
            n.ReminderDate,
            n.IsCompleted,
            Atrasada: EstaAtrasada(n.IsCompleted, n.ReminderDate, hoy),
            n.CreatedAt)).ToList();

        var desarrolladores = await db.Developers.AsNoTracking()
            .Where(d => d.IsActive).OrderBy(d => d.FullName)
            .Select(d => new OpcionDto(d.Id, d.FullName))
            .ToListAsync(ct);

        // Los contadores se cuentan contra la BASE y no contra `filas`: con el filtro puesto, contar
        // lo que se enseña haría que esconder las completadas cambiara la cifra de pendientes.
        int pendientes = await db.Notes.CountAsync(n => !n.IsCompleted, ct);
        int vencidas = await db.Notes
            .CountAsync(n => !n.IsCompleted && n.ReminderDate != null && n.ReminderDate < hoy, ct);

        return new NotasDto(filas, Prioridades, desarrolladores, pendientes, vencidas);
    }

    // ── Alta, edición y baja ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Alta o edición de una nota. <c>Id</c> 0 es un alta; un <c>Id</c> que ya no existe se rechaza
    /// en vez de convertirse en alta, porque quien lo mandó creía estar corrigiendo algo.
    /// </summary>
    public async Task<(bool ok, string mensaje, int id)> GuardarAsync(
        GuardarNotaRequest cambio, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var titulo = (cambio.Titulo ?? "").Trim();
        if (titulo.Length == 0) return (false, "El título es obligatorio.", 0);
        if (titulo.Length > MaxTitulo)
            return (false, $"El título no puede pasar de {MaxTitulo} caracteres.", 0);

        var contenido = (cambio.Contenido ?? "").Trim();
        if (contenido.Length > MaxContenido)
            return (false, $"Las notas adicionales no pueden pasar de {MaxContenido} caracteres.", 0);

        // Una prioridad fuera del enum se guardaría como número y luego caería en el «si no» de la
        // etiqueta, apareciendo como «Baja» sin que nadie lo hubiera pedido.
        if (!Enum.IsDefined(cambio.Prioridad))
            return (false, "Esa prioridad no existe. Recarga la pantalla.", 0);

        // Un desarrollador inexistente reventaría al guardar con un error de clave foránea, que no le
        // dice nada a quien está capturando. Se comprueba antes y se explica.
        if (cambio.DesarrolladorId is int desarrollador
            && !await db.Developers.AnyAsync(d => d.Id == desarrollador, ct))
            return (false, "Ese desarrollador ya no existe. Recarga la lista y vuelve a elegirlo.", 0);

        var nota = cambio.Id > 0
            ? await db.Notes.FirstOrDefaultAsync(n => n.Id == cambio.Id, ct)
            : null;

        bool esAlta = nota == null;
        if (esAlta)
        {
            if (cambio.Id > 0) return (false, "Esa nota ya no existe. Actualiza la lista.", 0);
            nota = new Note { CreatedAt = DateTime.UtcNow };
            db.Notes.Add(nota);
        }

        nota!.Title        = titulo;
        nota.Content       = contenido.Length == 0 ? null : contenido;
        nota.DeveloperId   = cambio.DesarrolladorId;
        nota.Priority      = cambio.Prioridad;
        nota.ReminderDate  = cambio.Recordatorio is { } fecha ? ADiaDeCalendario(fecha) : null;
        nota.IsCompleted   = cambio.Completada;

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(esAlta ? AuditAction.Create : AuditAction.Update,
            "Note", nota.Id.ToString(), nota.Title, ct);

        return (true,
            esAlta ? $"Nota «{nota.Title}» creada." : $"Nota «{nota.Title}» actualizada.",
            nota.Id);
    }

    /// <summary>
    /// Cierra o reabre una nota sin pasar por el formulario.
    /// </summary>
    /// <remarks>
    /// El escritorio solo sabía cerrar desde la rejilla: para deshacer un clic equivocado había que
    /// abrir la ventana de detalle y quitar la palomita. Es el mismo dato y la misma operación en las
    /// dos direcciones, así que aquí la lleva el estado deseado —no un «alternar»— y con eso dos
    /// pestañas abiertas sobre la misma lista no se deshacen la una a la otra.
    /// </remarks>
    public async Task<(bool ok, string mensaje)> CompletarAsync(
        int id, bool completada, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var nota = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (nota == null) return (false, "Esa nota ya no existe. Actualiza la lista.");

        if (nota.IsCompleted == completada)
            return (false, completada
                ? "Esa nota ya estaba completada."
                : "Esa nota ya estaba pendiente.");

        nota.IsCompleted = completada;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "Note", nota.Id.ToString(),
            completada ? $"Completada: {nota.Title}" : $"Reabierta: {nota.Title}", ct);

        return (true, completada
            ? $"«{nota.Title}» queda completada."
            : $"«{nota.Title}» vuelve a pendientes.");
    }

    /// <summary>Borra una nota. No hay papelera, igual que en el escritorio.</summary>
    public async Task<(bool ok, string mensaje)> EliminarAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var nota = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (nota == null) return (false, "Esa nota ya no existe. Actualiza la lista.");

        var titulo = nota.Title;
        bool seguiaPendiente = !nota.IsCompleted;

        db.Notes.Remove(nota);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Delete, "Note", id.ToString(), titulo, ct);

        return (true, seguiaPendiente
            ? $"Nota «{titulo}» eliminada; seguía pendiente."
            : $"Nota «{titulo}» eliminada.");
    }

    // ── Exportación ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Las notas en una hoja de cálculo, con las mismas columnas del escritorio.
    ///
    /// Se exporta LO QUE EL FILTRO ESTÁ ENSEÑANDO, igual que en minutas y por el mismo motivo: el
    /// botón está al lado del filtro y bajar otra cosa distinta de lo que se ve sorprende a quien
    /// acaba de acotar la lista.
    /// </summary>
    public async Task<byte[]> ExcelAsync(bool soloPendientes = false, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var consulta = db.Notes.AsNoTracking().Include(n => n.Developer).AsQueryable();
        if (soloPendientes) consulta = consulta.Where(n => !n.IsCompleted);

        var notas = await consulta.OrderByDescending(n => n.CreatedAt).ToListAsync(ct);

        var filas = notas.Select(n => new object?[]
        {
            n.Id,
            n.Title,
            n.Developer?.FullName,
            Etiqueta(n.Priority),
            n.ReminderDate,
            n.IsCompleted,
            // La fecha de alta sí es un instante en UTC: se pasa a local para que la columna coincida
            // con la que enseña la pantalla.
            n.CreatedAt.ToLocalTime()
        }).ToList();

        return HojaDeCalculo.Escribir(
            ["ID", "Título", "Dev origen", "Prioridad", "Recordatorio", "Completado", "Alta"],
            filas, "Notas");
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pendiente y con el recordatorio ya pasado. Es la regla exacta del escritorio, que pintaba
    /// esas filas en rojo: una nota completada nunca está atrasada, y una sin fecha tampoco.
    /// </summary>
    private static bool EstaAtrasada(bool completada, DateTime? recordatorio, DateTime hoy) =>
        !completada && recordatorio is { } fecha && fecha < hoy;

    /// <summary>
    /// Un día del calendario, guardado a mediodía UTC. Ver la nota de fechas de la clase: es lo que
    /// mantiene el mismo día en la web y en el escritorio mientras las dos convivan.
    /// </summary>
    private static DateTime ADiaDeCalendario(DateTime fecha) =>
        DateTime.SpecifyKind(fecha.Date.AddHours(12), DateTimeKind.Utc);

    /// <summary>Las prioridades con su texto. Las manda el servidor para que la pantalla no lleve una segunda copia.</summary>
    private static readonly IReadOnlyList<PrioridadDeNotaDto> Prioridades =
    [
        new(NotePriority.Alta,  "🔴 Alta"),
        new(NotePriority.Media, "🟡 Media"),
        new(NotePriority.Baja,  "🔵 Baja"),
    ];

    /// <summary>Las mismas etiquetas del escritorio, con su color en el emoji.</summary>
    private static string Etiqueta(NotePriority prioridad) => prioridad switch
    {
        NotePriority.Alta  => "🔴 Alta",
        NotePriority.Media => "🟡 Media",
        _                  => "🔵 Baja"
    };
}
