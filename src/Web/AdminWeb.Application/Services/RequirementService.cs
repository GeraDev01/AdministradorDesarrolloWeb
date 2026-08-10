using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Los requerimientos: darlos de alta, corregirlos, cancelarlos y decidir quién los trabaja.
///
/// En el escritorio esta lógica no tenía servicio propio: vivía dentro de <c>RequirementsControl</c>,
/// entre el código que armaba la rejilla. Aquí tiene que existir porque la pantalla ya no puede
/// tocar la base —está en el navegador— y porque las reglas que estaban ahí sueltas son justamente
/// las que hay que poder probar.
///
/// DOS DECISIONES DEL ESCRITORIO QUE SE CONSERVAN, y que son el motivo de que este servicio no sea
/// un CRUD cualquiera:
///
///  · <b>Eliminar NO borra: cancela.</b> <see cref="CancelarAsync"/> cambia el estado a Cancelado y
///    deja el requerimiento en su sitio. Un requerimiento borrado se lleva por delante su tiempo
///    cronometrado, sus asignaciones y su historia en el sprint donde estuvo comprometido, y la
///    pregunta «¿qué pasó con aquello?» se queda sin respuesta para siempre. La bitácora lo anota
///    igualmente como una baja, porque para quien lo pidió eso es lo que ocurrió.
///  · <b>Al asignar se avisa SOLO a quien no estaba antes.</b> Guardar la misma asignación dos veces
///    no vuelve a notificar a nadie; si no, cada retoque de la lista llenaría de avisos repetidos a
///    gente que ya sabía de su requerimiento y acabarían por no leerse.
///
/// Todo es del LÍDER. La pantalla es suya en el escritorio y aquí lleva la política
/// <c>SoloAdmin</c> en el endpoint; la guarda de este servicio es la segunda barrera, la que sigue
/// en pie cuando alguien llama a la API sin pasar por el navegador. Lo que el desarrollador ve de
/// sus requerimientos —«Mis asignaciones» y el sprint— se lee por otro camino y con su propia guarda.
/// </summary>
public class RequirementService(
    AppDbContext db,
    ICurrentUser currentUser,
    AuditService audit,
    NotificationService avisos)
{
    public const int MaxTitulo = 200;
    public const int MaxDescripcion = 4000;

    /// <summary>Tope de la estimación. Mil horas son medio año de una persona: ahí ya no es un requerimiento.</summary>
    public const decimal MaxHoras = 1000m;

    // ── Consultas ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Los requerimientos que cumplen el filtro, el más reciente primero.
    ///
    /// El filtrado se hace EN LA BASE y no en memoria como en el escritorio. Allí la lista entera
    /// estaba a un acceso de disco local y filtrarla en el control era gratis; aquí cada fila que se
    /// descarta después de traerla es red pagada por nada.
    /// </summary>
    /// <param name="developerId">Filtra por desarrollador ASIGNADO. Es por identificador y no por
    /// nombre como en el escritorio: dos personas pueden llamarse igual, y el nombre cambia cuando
    /// alguien corrige una ficha.</param>
    public async Task<List<Requirement>> ListarAsync(
        RequirementStatus? estado = null, int? developerId = null, string? busqueda = null,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var q = db.Requirements.AsNoTracking().Include(r => r.Assignments).AsQueryable();

        if (estado is { } e) q = q.Where(r => r.Status == e);
        if (developerId is { } dev) q = q.Where(r => r.Assignments.Any(a => a.DeveloperId == dev));

        var texto = (busqueda ?? "").Trim().ToLower();
        if (texto.Length > 0)
            // ToLower a los dos lados, igual que el escritorio: SQL Server suele comparar sin
            // distinguir mayúsculas por su intercalación y SQLite no, así que dejarlo al motor daría
            // un buscador que se comporta distinto según dónde esté desplegado.
            q = q.Where(r => r.Title.ToLower().Contains(texto)
                          || (r.Description != null && r.Description.ToLower().Contains(texto)));

        return await q.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).ToListAsync(ct);
    }

    /// <summary>Los desarrolladores a los que se puede asignar trabajo: los activos, por nombre.</summary>
    public async Task<List<Developer>> DesarrolladoresAsignablesAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);
        return await db.Developers.AsNoTracking()
            .Where(d => d.IsActive).OrderBy(d => d.FullName).ToListAsync(ct);
    }

    // ── Alta, edición y baja ─────────────────────────────────────────────────────

    /// <summary>Da de alta un requerimiento con los datos del formulario.</summary>
    public async Task<(bool ok, string mensaje, Requirement? requerimiento)> CrearAsync(
        DatosDeRequerimiento datos, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var (valido, error) = Validar(datos);
        if (!valido) return (false, error, null);

        var ahora = DateTime.UtcNow;
        var req = new Requirement { CreatedAt = ahora, StatusChangedAt = ahora };
        Aplicar(req, datos);

        db.Requirements.Add(req);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Create, "Requirement", req.Id.ToString(), req.Title, ct);
        return (true, $"Requerimiento «{req.Title}» creado.", req);
    }

    /// <summary>
    /// Guarda los cambios de un requerimiento.
    ///
    /// El cambio de ESTADO sella la hora en <c>StatusChangedAt</c> y solo cuando el estado cambió de
    /// verdad: es lo que permite medir cuánto lleva un requerimiento parado donde está, y tocarlo en
    /// cada guardado pondría ese contador a cero cada vez que alguien corrige una falta de ortografía.
    /// </summary>
    public async Task<(bool ok, string mensaje)> ActualizarAsync(
        int id, DatosDeRequerimiento datos, string? sello, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var (valido, error) = Validar(datos);
        if (!valido) return (false, error);

        var req = await db.Requirements.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (req == null) return (false, "Ese requerimiento ya no existe. Actualiza la lista.");

        if (!AplicarSello(req, sello, out var errorDeSello)) return (false, errorDeSello);

        var estadoPrevio = req.Status;
        Aplicar(req, datos);
        if (req.Status != estadoPrevio) req.StatusChangedAt = DateTime.UtcNow;

        // El choque de concurrencia NO se atrapa aquí a propósito: el filtro global de la API lo
        // traduce a un 409 con su explicación, y esa es la única respuesta que pide una acción
        // distinta de reintentar —recargar antes de volver a guardar—. Devolverlo como (false,
        // mensaje) lo dejaría indistinguible de un rechazo por validación.
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "Requirement", req.Id.ToString(), req.Title, ct);
        return (true, $"Requerimiento «{req.Title}» guardado.");
    }

    /// <summary>
    /// Cancela el requerimiento. <b>No lo borra</b>, y esa es la decisión del escritorio que este
    /// método existe para conservar: el botón se llamaba «Eliminar» y lo único que hacía era pasar
    /// el estado a Cancelado.
    ///
    /// Se anota en la bitácora como una BAJA —no como una modificación— porque para quien pidió el
    /// requerimiento eso es lo que pasó, y quien lea la bitácora buscando «quién lo eliminó» tiene
    /// que encontrarlo ahí.
    /// </summary>
    public async Task<(bool ok, string mensaje)> CancelarAsync(
        int id, string? sello, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var req = await db.Requirements.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (req == null) return (false, "Ese requerimiento ya no existe. Actualiza la lista.");
        if (req.Status == RequirementStatus.Cancelado)
            return (false, $"«{req.Title}» ya estaba cancelado.");

        if (!AplicarSello(req, sello, out var errorDeSello)) return (false, errorDeSello);

        req.Status = RequirementStatus.Cancelado;
        req.StatusChangedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Delete, "Requirement", req.Id.ToString(),
            $"Cancelado: {req.Title}", ct);
        return (true, $"«{req.Title}» quedó cancelado. No se borró: sigue en la lista con ese estado.");
    }

    /// <summary>
    /// Deja el requerimiento EXACTAMENTE con estos desarrolladores: es la semántica del diálogo de
    /// casillas del escritorio —lo marcado es lo que queda—.
    ///
    /// Avisa SOLO a quienes no estaban asignados antes. Sin esa resta, cada guardado volvería a
    /// notificar a todo el equipo del requerimiento y los avisos dejarían de leerse, que es la forma
    /// más segura de que el que sí importaba pase desapercibido.
    /// </summary>
    public async Task<(bool ok, string mensaje)> AsignarAsync(
        int requirementId, IReadOnlyCollection<int> developerIds, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var req = await db.Requirements.FirstOrDefaultAsync(r => r.Id == requirementId, ct);
        if (req == null) return (false, "Ese requerimiento ya no existe. Actualiza la lista.");

        // Solo fichas que existan y sigan activas. Lo que llegue de más se descarta en silencio y no
        // rompe la asignación: la lista pudo armarse con una ficha que se dio de baja mientras el
        // navegador tenía la pantalla abierta, y eso no es motivo para no asignar al resto.
        var validos = await db.Developers
            .Where(d => developerIds.Contains(d.Id) && d.IsActive)
            .Select(d => d.Id)
            .ToListAsync(ct);

        var actuales = await db.Assignments.Where(a => a.RequirementId == req.Id).ToListAsync(ct);
        var antes = actuales.Select(a => a.DeveloperId).ToHashSet();

        db.Assignments.RemoveRange(actuales);
        var ahora = DateTime.UtcNow;
        foreach (var devId in validos)
            db.Assignments.Add(new Assignment { RequirementId = req.Id, DeveloperId = devId, AssignedAt = ahora });

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "Assignment", req.Id.ToString(),
            $"{validos.Count} devs asignados a '{req.Title}'", ct);

        // Sin clave de deduplicación: la resta de arriba ES la deduplicación, y una clave fija por
        // requerimiento silenciaría el aviso legítimo del día que a alguien se le vuelve a asignar
        // algo de lo que se le había quitado.
        int avisados = 0;
        foreach (var devId in validos.Where(d => !antes.Contains(d)))
            if (await avisos.NotifyDeveloperAsync(devId, NotificationKind.RequirementAssigned,
                    "Nuevo requerimiento asignado", $"#{req.Id} — {req.Title}", req.ExternalUrl, ct: ct))
                avisados++;

        return (true, validos.Count == 0
            ? $"«{req.Title}» quedó sin desarrolladores asignados."
            : $"{validos.Count} desarrollador(es) en «{req.Title}»." +
              (avisados > 0 ? $" Se avisó a {avisados} que no lo tenían antes." : ""));
    }

    // ── Interno ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Los campos que captura el formulario, en un solo bulto. Es lo mismo que el escritorio pasaba
    /// dentro de una entidad <c>Requirement</c> a medio llenar; aquí va aparte para que ninguna
    /// entidad de EF tenga que cruzar la frontera de la API con campos que nadie debe poder mandar
    /// —el origen, la URL externa o los segundos ya reportados a DevOps no se capturan a mano—.
    /// </summary>
    public record DatosDeRequerimiento(
        string? Titulo,
        string? Detalle,
        RequirementStatus Estado,
        RequirementPriority Prioridad,
        decimal? HorasEstimadas,
        DateTime? FechaSolicitud,
        DateTime? FechaCompromiso,
        DateTime? FechaEntrega,
        int AvancePct);

    private static void Aplicar(Requirement req, DatosDeRequerimiento d)
    {
        req.Title = d.Titulo!.Trim();
        req.Description = string.IsNullOrWhiteSpace(d.Detalle) ? null : d.Detalle.Trim();
        req.Status = d.Estado;
        req.Priority = d.Prioridad;
        req.EstimateHours = d.HorasEstimadas is > 0 ? d.HorasEstimadas : null;
        req.RequestDate = d.FechaSolicitud?.Date;
        req.CommittedDeliveryDate = d.FechaCompromiso?.Date;
        req.ActualDeliveryDate = d.FechaEntrega?.Date;
        req.ProgressPercent = Math.Clamp(d.AvancePct, 0, 100);
    }

    private static (bool ok, string error) Validar(DatosDeRequerimiento d)
    {
        var titulo = (d.Titulo ?? "").Trim();
        if (titulo.Length == 0) return (false, "El título es obligatorio.");
        if (titulo.Length > MaxTitulo) return (false, $"El título no puede pasar de {MaxTitulo} caracteres.");
        if ((d.Detalle ?? "").Length > MaxDescripcion)
            return (false, $"La descripción no puede pasar de {MaxDescripcion} caracteres.");
        if (d.HorasEstimadas is < 0) return (false, "La estimación no puede ser negativa.");
        if (d.HorasEstimadas > MaxHoras)
            return (false, $"Una estimación de más de {MaxHoras:0} horas no es un requerimiento: divídelo.");
        if (d.FechaEntrega is { } entrega && d.FechaSolicitud is { } solicitud && entrega.Date < solicitud.Date)
            return (false, "La fecha de entrega no puede ser anterior a la de solicitud.");
        return (true, "");
    }

    /// <summary>
    /// Pone el sello que trajo el cliente como valor ORIGINAL de la fila, que es lo que hace que EF
    /// compare al guardar y lance el choque de concurrencia si alguien tocó el registro mientras
    /// tanto. Sin esto el sello viajaría de adorno y el segundo en guardar pisaría al primero.
    ///
    /// Si el sello no viene, se guarda sin comprobar: es lo que pasa al dar de alta y, sobre todo,
    /// cuando la base es SQLite —ahí <c>RowVersion</c> ni siquiera está mapeado (ver
    /// <c>AppDbContext</c>) y pedirle a EF el valor original de una propiedad que no existe reventaría
    /// la petición entera—.
    /// </summary>
    private bool AplicarSello(Requirement req, string? sello, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(sello)) return true;

        // Se descifra ANTES de mirar si la columna existe, para que un sello corrupto se rechace
        // igual en SQL Server que en SQLite: un error que solo aparece en producción es peor que el
        // error mismo.
        var destino = new byte[((sello.Length * 3) + 3) / 4];
        if (!Convert.TryFromBase64String(sello, destino, out int escritos))
        {
            error = "La ficha llegó con un sello de concurrencia ilegible. Recarga la lista e intenta otra vez.";
            return false;
        }

        var entrada = db.Entry(req);
        if (entrada.Metadata.FindProperty(nameof(Requirement.RowVersion)) is null) return true;

        entrada.Property(r => r.RowVersion).OriginalValue = destino[..escritos];
        return true;
    }

    /// <summary>
    /// El sello de una fila, listo para viajar. Nulo cuando la base no lo lleva; la pantalla lo
    /// devuelve tal cual y el servicio sabe qué hacer con un nulo.
    /// </summary>
    public static string? SelloDe(Requirement req) =>
        req.RowVersion is { Length: > 0 } v ? Convert.ToBase64String(v) : null;
}
