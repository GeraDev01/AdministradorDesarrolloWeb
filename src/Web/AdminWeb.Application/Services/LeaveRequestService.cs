using System.Globalization;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Permisos: el desarrollador los SOLICITA y el administrador los RESUELVE.
///
/// Antes esta pantalla era la libreta del administrador —él capturaba el permiso ya concedido— y
/// el desarrollador ni la veía. El trámite ocurría por fuera (un mensaje, un pasillo) y lo único
/// que quedaba era el apunte de quien lo anotó. Ahora la solicitud y su respuesta viven aquí, que
/// es lo que permite responder «¿pedí eso?, ¿qué me contestaron?» sin buscar en un chat.
///
/// Las reglas viven en el servicio y no en la UI, igual que en <see cref="VacationRequestService"/>:
/// una pantalla que decide por su cuenta acaba enseñando un botón que el servicio luego rechaza.
///
/// <para><b>DE DÍAS COMPLETOS Y DE HORAS.</b> Un permiso puede ser de uno o varios días enteros
/// —como toda la vida, y como es todo el histórico— o de UN TRAMO de un solo día: «de 9:00 a 11:00».
/// Lo elige quien lo pide. Lo segundo se guarda en <see cref="LeaveRequest.HoraInicio"/> y
/// <see cref="LeaveRequest.HoraFin"/>; sin tramo, el permiso es de día completo y todo se comporta
/// exactamente igual que antes de que esto existiera.</para>
/// </summary>
public class LeaveRequestService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
{
    /// <summary>Tope del justificante. Mismo criterio que el resto de adjuntos de la aplicación.</summary>
    public const int MaxAdjuntoBytes = 15 * 1024 * 1024;

    public const int MaxDias = 365;

    /// <summary>
    /// Lo que dura una jornada, y por tanto lo más largo que puede ser un permiso POR HORAS: más que
    /// eso ya es el día entero y se pide como día completo.
    ///
    /// <para>Es el MISMO ocho con el que el pool convirtió sus plazos de días a horas (ver
    /// <c>PoolSeed</c> y la conversión del migrador). La empresa no tiene una jornada configurable
    /// por persona, así que inventar aquí un segundo número —o leerlo de la ficha, que no lo trae—
    /// sería fabricar una regla que nadie ha decidido.</para>
    /// </summary>
    public const decimal HorasDeLaJornada = 8m;

    // ── Días completos y horas ───────────────────────────────────────────────
    //
    // Estas ayudas son ESTÁTICAS y toman los campos sueltos en vez de la entidad, a propósito: las
    // pantallas se arman desde proyecciones ligeras —«Mis permisos» no trae los 15 MB del
    // justificante— y sin esto habría dos formas de decir cuánto dura un permiso, la de la entidad y
    // la de la proyección. Dos formas es como se acaba enseñando «1 día» en una lista y «2 h» en la
    // otra para la misma fila.

    /// <summary>
    /// El permiso es de un tramo de horas y no de días completos. Basta con que el par esté puesto:
    /// las dos horas viajan juntas o no viajan (lo garantiza la validación).
    /// </summary>
    public static bool EsPorHoras(TimeOnly? inicio, TimeOnly? fin) => inicio is not null && fin is not null;

    /// <summary>
    /// Cuántas horas cubre el tramo. Cero para un permiso de día completo: <b>no</b> se convierte la
    /// jornada en horas, porque un día de ausencia no son ocho horas de ausencia para todo el mundo
    /// y ese número inventado acabaría sumándose en algún indicador.
    /// </summary>
    public static decimal Horas(TimeOnly? inicio, TimeOnly? fin) =>
        EsPorHoras(inicio, fin) && fin!.Value > inicio!.Value
            ? (decimal)(fin.Value - inicio.Value).TotalHours
            : 0m;

    /// <summary>
    /// Cuánto dura el permiso, escrito para leerse: «2 día(s)» o «2 h (de 09:00 a 11:00)».
    ///
    /// <para>La escribe el SERVIDOR y viaja hecha a las dos pantallas —la de quien pide y la de quien
    /// resuelve— por lo mismo que las etiquetas de tipo y estado: dos plantillas de texto para el
    /// mismo dato acaban diciendo cosas distintas y nadie lo nota hasta que alguien compara.</para>
    ///
    /// <para>Para un permiso de días completos devuelve <b>exactamente</b> el texto de siempre. Eso
    /// importa porque esta cadena acaba en la BITÁCORA a través de <see cref="Describir"/>: los
    /// asientos viejos y los nuevos de un permiso de días tienen que seguir leyéndose igual.</para>
    /// </summary>
    public static string Duracion(int dias, TimeOnly? inicio, TimeOnly? fin) =>
        EsPorHoras(inicio, fin)
            ? $"{EnPalabras(fin!.Value - inicio!.Value)} (de {Hhmm(inicio.Value)} a {Hhmm(fin.Value)})"
            : $"{dias} día(s)";

    /// <summary>La hora, siempre en 24 h y con la misma cara en cualquier servidor.</summary>
    public static string Hhmm(TimeOnly hora) => hora.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Un rato, en palabras: «45 min», «2 h», «2 h 30 min». Se dice así y no «2.5 h» porque el
    /// separador decimal cambia con la cultura del servidor y «2.5 h» en una pantalla y «2,5 h» en
    /// otra es justo la clase de diferencia que hace dudar del número.
    /// </summary>
    private static string EnPalabras(TimeSpan duracion)
    {
        int horas = (int)duracion.TotalHours;
        int minutos = duracion.Minutes;

        return (horas, minutos) switch
        {
            (0, _) => $"{minutos} min",
            (_, 0) => $"{horas} h",
            _      => $"{horas} h {minutos} min"
        };
    }

    /// <summary>Se puede cancelar mientras siga viva: pendiente, o aprobada pero ya no se va a tomar.</summary>
    public static bool PuedeCancelar(LeaveStatus estado) =>
        estado is LeaveStatus.Pendiente or LeaveStatus.Aprobada;

    /// <summary>El desarrollador solo corrige lo que aún no ha sido resuelto.</summary>
    public static bool PuedeEditar(LeaveStatus estado) => estado == LeaveStatus.Pendiente;

    /// <summary>
    /// Solo se borra lo que nunca llegó a ser una decisión. Una aprobada o rechazada es historial:
    /// se cancela, no se borra. (El administrador sí puede depurar cualquier fila; ver Eliminar.)
    /// </summary>
    public static bool PuedeEliminarElDesarrollador(LeaveStatus estado) =>
        estado is LeaveStatus.Pendiente or LeaveStatus.Cancelada;

    // ── Lectura ──────────────────────────────────────────────────────────────

    /// <summary>Los permisos de un desarrollador. Suyos o de quien administre.</summary>
    public async Task<List<LeaveRequest>> DeDesarrolladorAsync(int developerId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, developerId);
        return await db.LeaveRequests
            .Include(l => l.Developer)
            .Where(l => l.DeveloperId == developerId)
            .OrderByDescending(l => l.Date).ThenByDescending(l => l.Id)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    /// <summary>Todos los permisos del equipo, para la pantalla del administrador.</summary>
    public async Task<List<LeaveRequest>> TodasAsync(int? developerId = null, LeaveStatus? estado = null,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var q = db.LeaveRequests.Include(l => l.Developer).AsQueryable();
        if (developerId is int dev) q = q.Where(l => l.DeveloperId == dev);
        if (estado is LeaveStatus e) q = q.Where(l => l.Status == e);

        // Las pendientes primero: son las únicas que piden una acción del administrador.
        return await q.OrderByDescending(l => l.Status == LeaveStatus.Pendiente)
                      .ThenByDescending(l => l.Date).ThenByDescending(l => l.Id)
                      .AsNoTracking()
                      .ToListAsync(ct);
    }

    /// <summary>Cuántas esperan respuesta. Para el contador del menú.</summary>
    public Task<int> PendientesCountAsync(CancellationToken ct = default) =>
        currentUser.IsAdmin
            ? db.LeaveRequests.CountAsync(l => l.Status == LeaveStatus.Pendiente, ct)
            : Task.FromResult(0);

    /// <summary>El justificante de un permiso. Vacío si no tiene o si no hay derecho a verlo.</summary>
    public async Task<(byte[] bytes, string nombre)> AdjuntoAsync(int requestId, CancellationToken ct = default)
    {
        var l = await db.LeaveRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == requestId, ct);
        if (l == null) return ([], "");
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, l.DeveloperId);
        return l.AttachmentBytes is { Length: > 0 }
            ? (l.AttachmentBytes, NombreSeguro(l.AttachmentFileName))
            : ([], "");
    }

    // ── Alta ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// El desarrollador pide un permiso para sí mismo. Nace Pendiente: nadie se autoriza solo.
    /// </summary>
    public async Task<(bool ok, string mensaje, LeaveRequest? solicitud)> SolicitarAsync(
        LeaveRequest borrador, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, borrador.DeveloperId);

        var (valido, error) = Validar(borrador, exigirMotivo: true);
        if (!valido) return (false, error, null);

        var (libre, choque) = await SinPisarOtroPermisoAsync(borrador, excluir: null, ct);
        if (!libre) return (false, choque, null);

        borrador.Status = LeaveStatus.Pendiente;
        borrador.RequestedByDeveloperId = borrador.DeveloperId;
        borrador.ApprovedBy = null;
        borrador.ReviewedById = null;
        borrador.ReviewedAt = null;
        borrador.ReviewComment = null;
        borrador.CreatedAt = DateTime.UtcNow;

        db.LeaveRequests.Add(borrador);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Create, "LeaveRequest", borrador.Id.ToString(),
            $"Permiso solicitado: {Describir(borrador)}", ct);
        return (true, "Solicitud enviada. Queda pendiente de que el líder la resuelva.", borrador);
    }

    /// <summary>
    /// El administrador captura un permiso ya concedido (el trámite ocurrió fuera de la app). Nace
    /// Aprobada porque el acto de registrarlo ES la aprobación: dejarlo Pendiente le crearía a él
    /// mismo un trámite que ya resolvió.
    /// </summary>
    public async Task<(bool ok, string mensaje, LeaveRequest? solicitud)> RegistrarPorAdministradorAsync(
        LeaveRequest borrador, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var (valido, error) = Validar(borrador, exigirMotivo: false);
        if (!valido) return (false, error, null);

        var (libre, choque) = await SinPisarOtroPermisoAsync(borrador, excluir: null, ct);
        if (!libre) return (false, choque, null);

        borrador.Status = LeaveStatus.Aprobada;
        borrador.RequestedByDeveloperId = null;
        borrador.ReviewedById = currentUser.UserId;
        borrador.ReviewedAt = DateTime.UtcNow;
        borrador.CreatedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(borrador.ApprovedBy)) borrador.ApprovedBy = currentUser.Username;

        db.LeaveRequests.Add(borrador);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Create, "LeaveRequest", borrador.Id.ToString(),
            $"Permiso registrado por el líder: {Describir(borrador)}", ct);
        return (true, "Permiso registrado.", borrador);
    }

    // ── Edición y cancelación (del solicitante) ──────────────────────────────

    public async Task<(bool ok, string mensaje)> EditarAsync(int requestId, LeaveRequest cambios,
        CancellationToken ct = default)
    {
        var (l, error) = await ObtenerPropiaAsync(requestId, ct);
        if (l == null) return (false, error!);

        if (!PuedeEditar(l.Status))
            return (false, $"No se puede modificar una solicitud «{Etiqueta(l.Status)}». " +
                           "Si necesitas cambiarla, cancélala y crea otra.");

        cambios.DeveloperId = l.DeveloperId;   // nunca cambia de dueño
        var (valido, errorVal) = Validar(cambios, exigirMotivo: l.EsSolicitudDelDesarrollador);
        if (!valido) return (false, errorVal);

        // Excluyéndose a sí misma: si no, la solicitud que se está editando se contaría como el
        // permiso que ya ocupa ese tramo y no habría forma de guardarla.
        var (libre, choque) = await SinPisarOtroPermisoAsync(cambios, excluir: l.Id, ct);
        if (!libre) return (false, choque);

        l.Type = cambios.Type;
        l.Date = cambios.Date.Date;
        l.DaysCount = cambios.DaysCount;
        l.HoraInicio = cambios.HoraInicio;
        l.HoraFin = cambios.HoraFin;
        l.Reason = Limpiar(cambios.Reason);
        l.Notes = Limpiar(cambios.Notes);
        l.AttachmentBytes = cambios.AttachmentBytes;
        l.AttachmentFileName = Limpiar(cambios.AttachmentFileName);

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "LeaveRequest", l.Id.ToString(),
            $"Permiso actualizado: {Describir(l)}", ct);
        return (true, "Solicitud actualizada.");
    }

    /// <summary>
    /// Corrige los campos capturados de una solicitud pendiente <b>sin tocar el justificante</b>.
    /// </summary>
    /// <remarks>
    /// <para>Existe aparte de <see cref="EditarAsync"/> por un motivo concreto: aquel reemplaza la
    /// solicitud ENTERA con lo que llegue, adjunto incluido. Sirve para «Mis permisos», donde el
    /// formulario vuelve a subir el archivo si lo hay; usarlo desde una pantalla que no sube archivos
    /// —la del líder— borraría el justificante de quien pidió el permiso al corregirle una palabra
    /// del motivo. Por eso aquí los parámetros son los campos, uno a uno, y no una solicitud
    /// completa: no hay forma de pasar un adjunto ni de olvidarse de él.</para>
    ///
    /// <para><b>Las columnas del adjunto ni se leen ni se escriben.</b> Se comprueba con una
    /// proyección —de quién es y en qué estado está— y se escribe con una actualización directa de
    /// las siete columnas que cambian (las cinco de siempre más el tramo de horas). Cargar la entidad
    /// habría traído los 15 MB del justificante a la memoria del servidor para acabar cambiando una
    /// frase.</para>
    ///
    /// <para>El precio de escribir así es que se pierde el sello de concurrencia
    /// (<c>RowVersion</c>), que solo actúa al guardar una entidad rastreada. A cambio, la condición
    /// «sigue pendiente» viaja dentro del propio <c>UPDATE</c>: lo que protege ese sello aquí es que
    /// nadie corrija por detrás una solicitud que el líder acaba de resolver, y eso lo cubre el
    /// filtro. Dos correcciones simultáneas del mismo texto siguen ganándolas la última, que es lo
    /// mismo que pasaba en el escritorio.</para>
    /// </remarks>
    /// <param name="horaInicio">El tramo, si el permiso corregido es por horas. Los dos en nulo lo
    /// dejan como permiso de día completo, que es también la forma de quitarle las horas a uno que
    /// se capturó por error como tramo.</param>
    public async Task<(bool ok, string mensaje)> CorregirPendienteAsync(
        int requestId, LeaveType tipo, DateTime fecha, int dias, string? motivo, string? notas,
        TimeOnly? horaInicio = null, TimeOnly? horaFin = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        // Proyección y no la entidad: aquí solo hace falta saber de quién es, en qué estado está y si
        // la pidió el desarrollador (que es lo que decide si el motivo es obligatorio).
        var ficha = await db.LeaveRequests.AsNoTracking()
            .Where(x => x.Id == requestId)
            .Select(x => new { x.DeveloperId, x.Status, x.RequestedByDeveloperId })
            .FirstOrDefaultAsync(ct);

        if (ficha == null) return (false, "La solicitud ya no existe. Actualiza la lista.");

        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, ficha.DeveloperId);

        if (!PuedeEditar(ficha.Status))
            return (false, $"No se puede modificar una solicitud «{Etiqueta(ficha.Status)}». " +
                           "Si necesitas cambiarla, cancélala y crea otra.");

        // Se valida con las MISMAS reglas del alta, sobre un borrador de usar y tirar. Repetirlas
        // aquí dejaría dos listas de topes que se desincronizan a la primera.
        var borrador = new LeaveRequest
        {
            DeveloperId = ficha.DeveloperId,
            Type = tipo,
            Date = fecha,
            DaysCount = dias,
            HoraInicio = horaInicio,
            HoraFin = horaFin,
            Reason = motivo,
            Notes = notas
        };

        var (valido, error) = ValidarDatos(borrador, exigirMotivo: ficha.RequestedByDeveloperId != null);
        if (!valido) return (false, error);

        var (libre, choque) = await SinPisarOtroPermisoAsync(borrador, excluir: requestId, ct);
        if (!libre) return (false, choque);

        int filas = await db.LeaveRequests
            .Where(x => x.Id == requestId && x.Status == LeaveStatus.Pendiente)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Type, borrador.Type)
                .SetProperty(x => x.Date, borrador.Date)
                .SetProperty(x => x.DaysCount, borrador.DaysCount)
                // El tramo se escribe SIEMPRE, también cuando llega vacío: es lo que permite
                // devolverle a un permiso su condición de día completo. Sin esta pareja de líneas,
                // quitarle las horas desde la pantalla del líder no habría cambiado nada en la base.
                .SetProperty(x => x.HoraInicio, borrador.HoraInicio)
                .SetProperty(x => x.HoraFin, borrador.HoraFin)
                .SetProperty(x => x.Reason, borrador.Reason)
                .SetProperty(x => x.Notes, borrador.Notes), ct);

        if (filas == 0)
            return (false, "Esa solicitud dejó de estar pendiente mientras la corregías; no se cambió nada. "
                         + "Actualiza la lista.");

        await audit.RecordAsync(AuditAction.Update, "LeaveRequest", requestId.ToString(),
            $"Permiso corregido sin tocar el justificante: {Describir(borrador)}", ct);
        return (true, "Solicitud corregida. El justificante sigue como estaba.");
    }

    public async Task<(bool ok, string mensaje)> CancelarAsync(int requestId, string? motivo = null,
        CancellationToken ct = default)
    {
        var (l, error) = await ObtenerPropiaAsync(requestId, ct);
        if (l == null) return (false, error!);

        if (!PuedeCancelar(l.Status))
            return (false, $"No se puede cancelar una solicitud «{Etiqueta(l.Status)}».");

        l.Status = LeaveStatus.Cancelada;

        // Se anota en ReviewComment y NO en ReviewedById/ReviewedAt: esos campos significan «quién
        // la resolvió» y llenarlos aquí haría pasar una cancelación propia por una decisión del jefe.
        var quien = currentUser.Username ?? "el solicitante";
        var nota = $"Cancelada por {quien} el {DateTime.Now:dd/MM/yyyy HH:mm}"
                 + (string.IsNullOrWhiteSpace(motivo) ? "." : $": {motivo.Trim()}");
        l.ReviewComment = string.IsNullOrWhiteSpace(l.ReviewComment) ? nota : $"{nota}\n{l.ReviewComment}";

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "LeaveRequest", l.Id.ToString(),
            $"Permiso cancelado: {Describir(l)}", ct);
        return (true, "Solicitud cancelada.");
    }

    /// <summary>
    /// Elimina la solicitud. Al desarrollador solo se le permite sobre lo que nunca fue una
    /// decisión; el administrador puede depurar cualquier fila (es quien mantiene el registro).
    /// </summary>
    public async Task<(bool ok, string mensaje)> EliminarAsync(int requestId, CancellationToken ct = default)
    {
        var (l, error) = await ObtenerPropiaAsync(requestId, ct);
        if (l == null) return (false, error!);

        if (!currentUser.IsAdmin && !PuedeEliminarElDesarrollador(l.Status))
            return (false,
                $"No se puede eliminar una solicitud «{Etiqueta(l.Status)}»: es parte del historial. " +
                "Si ya no la vas a tomar, cancélala.");

        var descripcion = Describir(l);
        db.LeaveRequests.Remove(l);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Delete, "LeaveRequest", requestId.ToString(),
            $"Permiso eliminado: {descripcion}", ct);
        return (true, "Solicitud eliminada.");
    }

    // ── Resolución (del administrador) ───────────────────────────────────────

    public Task<(bool ok, string mensaje)> AprobarAsync(int requestId, string? comentario = null,
        CancellationToken ct = default) =>
        ResolverAsync(requestId, LeaveStatus.Aprobada, comentario, ct);

    public Task<(bool ok, string mensaje)> RechazarAsync(int requestId, string? motivo, CancellationToken ct = default)
    {
        // Un rechazo sin motivo deja al solicitante sin nada que hacer con la respuesta.
        if (string.IsNullOrWhiteSpace(motivo))
            return Task.FromResult((false, "Escribe el motivo del rechazo: es lo único que el solicitante va a leer."));
        return ResolverAsync(requestId, LeaveStatus.Rechazada, motivo, ct);
    }

    private async Task<(bool ok, string mensaje)> ResolverAsync(int requestId, LeaveStatus destino,
        string? comentario, CancellationToken ct)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var l = await db.LeaveRequests.Include(x => x.Developer).FirstOrDefaultAsync(x => x.Id == requestId, ct);
        if (l == null) return (false, "La solicitud ya no existe. Actualiza la lista.");

        // La comprobación sigue haciendo falta aunque lo leído sea fresco: el solicitante pudo
        // cancelarla mientras esta pantalla estaba abierta, y resolver algo ya resuelto sería
        // pisar su decisión.
        if (l.Status != LeaveStatus.Pendiente)
            return (false, $"Esa solicitud ya está «{Etiqueta(l.Status)}»; no hay nada que resolver.");

        l.Status = destino;
        l.ReviewedById = currentUser.UserId;
        l.ReviewedAt = DateTime.UtcNow;
        l.ReviewComment = Limpiar(comentario);
        if (destino == LeaveStatus.Aprobada && string.IsNullOrWhiteSpace(l.ApprovedBy))
            l.ApprovedBy = currentUser.Username;

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "LeaveRequest", l.Id.ToString(),
            $"Permiso {Etiqueta(destino).ToLowerInvariant()}: {Describir(l)}", ct);

        return (true, destino == LeaveStatus.Aprobada ? "Permiso aprobado." : "Permiso rechazado.");
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────

    private async Task<(LeaveRequest? l, string? error)> ObtenerPropiaAsync(int requestId, CancellationToken ct)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        var l = await db.LeaveRequests.FirstOrDefaultAsync(x => x.Id == requestId, ct);
        if (l == null) return (null, "La solicitud ya no existe. Actualiza la lista.");

        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, l.DeveloperId);
        return (l, null);
    }

    /// <summary>
    /// Las reglas de los campos capturados, sin mirar el adjunto.
    ///
    /// Está separado de <see cref="Validar"/> porque <see cref="CorregirPendienteAsync"/> no tiene
    /// adjunto que validar —ni debe tocarlo— y aun así tiene que aplicar exactamente los mismos topes
    /// que el alta. Copiarlos allí dejaría dos listas que se desincronizarían.
    /// </summary>
    private static (bool ok, string error) ValidarDatos(LeaveRequest l, bool exigirMotivo)
    {
        if (l.DeveloperId <= 0) return (false, "Falta indicar de quién es el permiso.");
        if (l.DaysCount < 1) return (false, "El permiso tiene que ser de al menos un día.");
        if (l.DaysCount > MaxDias) return (false, $"El permiso no puede pasar de {MaxDias} días.");
        if (l.Date == default) return (false, "Indica la fecha de inicio.");

        var (tramoOk, errorDelTramo) = ValidarElTramo(l);
        if (!tramoOk) return (false, errorDelTramo);

        if (exigirMotivo && string.IsNullOrWhiteSpace(l.Reason))
            return (false, "Escribe el motivo: es lo que el líder va a leer para decidir.");

        l.Reason = Limpiar(l.Reason);
        l.Notes = Limpiar(l.Notes);
        l.Date = l.Date.Date;
        return (true, "");
    }

    /// <summary>
    /// Las reglas del TRAMO, cuando el permiso es por horas. Sin tramo no hay nada que comprobar: es
    /// un permiso de días completos y sale por aquí sin tocarse, que es lo que mantiene intacto el
    /// comportamiento de todo lo anterior.
    /// </summary>
    private static (bool ok, string error) ValidarElTramo(LeaveRequest l)
    {
        // Una hora suelta no es un tramo: sin las dos no se sabe cuánto dura, y guardarlo así dejaría
        // una fila que ninguna pantalla puede contar. Se dice cuál falta.
        if (l.HoraInicio is null && l.HoraFin is null) return (true, "");
        if (l.HoraInicio is null) return (false, "Falta la hora de inicio del permiso.");
        if (l.HoraFin is null) return (false, "Falta la hora de fin del permiso.");

        // Un permiso no se mide en segundos. Solo pueden llegar llamando a la API a mano —el selector
        // va a saltos de quince minutos—, y si se guardaran, la ficha diría «2 h» mientras el total
        // del año sumaría 2,0003: dos números que no cuadran y nadie sabe cuál creer.
        var inicio = new TimeOnly(l.HoraInicio.Value.Hour, l.HoraInicio.Value.Minute);
        var fin = new TimeOnly(l.HoraFin.Value.Hour, l.HoraFin.Value.Minute);
        l.HoraInicio = inicio;
        l.HoraFin = fin;

        // Al revés o de cero: las dos cosas se dicen con la misma frase porque son el mismo error de
        // captura —la hora de fin no es posterior a la de inicio— y separarlas solo daría dos mensajes
        // para el mismo arreglo.
        if (fin <= inicio)
            return (false, "El tramo está al revés o no dura nada: la hora de fin tiene que ser " +
                           "posterior a la de inicio.");

        var horas = (decimal)(fin - inicio).TotalHours;
        if (horas > HorasDeLaJornada)
            return (false, $"Un permiso por horas no puede pasar de {HorasDeLaJornada:0} horas, que es " +
                           "la jornada. Si necesitas el día entero, pídelo como día completo.");

        // Un tramo vive dentro de UN día: pedir «de 9:00 a 11:00» durante tres días no es un tramo,
        // son tres tramos, y guardarlo como uno solo haría que la ficha dijera 2 h cuando fueron 6.
        if (l.DaysCount > 1)
            return (false, "Un permiso por horas es de un solo día. Deja los días en 1 y elige el " +
                           "tramo, o pídelo como días completos.");

        return (true, "");
    }

    /// <summary>
    /// Que el permiso no PISE otro que ya exista ese día.
    ///
    /// <para><b>Solo se comprueba cuando el permiso es POR HORAS</b>, y es una decisión, no un
    /// descuido. Pedir dos tramos que se solapan el mismo día es contradictorio —la persona no puede
    /// estar dos veces fuera de 9 a 11— y hasta hoy nadie podía equivocarse así porque los tramos no
    /// existían. Poner el mismo veto a los permisos de DÍAS COMPLETOS cambiaría el comportamiento de
    /// lo que lleva años funcionando: hoy el líder puede capturar dos permisos que se pisan (una
    /// incapacidad que se alarga sobre un permiso ya concedido, por ejemplo) y rechazárselo de golpe
    /// sería quitarle una salida sin que nadie lo haya pedido.</para>
    ///
    /// <para>Lo que sí mira el tramo es TODO lo que cubra ese día, incluidos los permisos de días
    /// completos: pedir dos horas de un día que ya está entero de permiso no tiene sentido.</para>
    ///
    /// <para>Solo estorban los permisos VIVOS —pendientes y aprobados—. Uno rechazado o cancelado no
    /// ocupa nada, y tratarlo como si ocupara impediría volver a pedir lo que a uno le negaron.</para>
    /// </summary>
    /// <param name="excluir">El propio permiso cuando se está corrigiendo: si no, se pisaría a sí mismo.</param>
    private async Task<(bool ok, string error)> SinPisarOtroPermisoAsync(
        LeaveRequest l, int? excluir, CancellationToken ct)
    {
        if (!EsPorHoras(l.HoraInicio, l.HoraFin)) return (true, "");

        var dia = l.Date.Date;

        // Se traen los candidatos y se decide en memoria. El filtro fino —«¿este permiso llega hasta
        // ese día?»— depende de Date + DaysCount, y una consulta con AddDays sobre una columna es lo
        // que cada motor traduce a su manera. El acotado sí viaja a la base y basta para que esto no
        // recorra la tabla: nada que empiece más de MaxDias antes puede alcanzar el día, porque ése es
        // el permiso más largo que se deja pedir.
        var candidatos = await db.LeaveRequests.AsNoTracking()
            .Where(x => x.DeveloperId == l.DeveloperId
                     && (excluir == null || x.Id != excluir)
                     && (x.Status == LeaveStatus.Pendiente || x.Status == LeaveStatus.Aprobada)
                     && x.Date <= dia
                     && x.Date >= dia.AddDays(-MaxDias))
            .Select(x => new { x.Date, x.DaysCount, x.HoraInicio, x.HoraFin, x.Status })
            .ToListAsync(ct);

        // El tramo que se está pidiendo. Se saca aquí porque EsPorHoras ya garantizó que están los dos.
        var inicio = l.HoraInicio!.Value;
        var fin = l.HoraFin!.Value;

        foreach (var otro in candidatos)
        {
            var ultimoDia = otro.Date.Date.AddDays(Math.Max(1, otro.DaysCount) - 1);
            if (ultimoDia < dia) continue;   // termina antes; no toca ese día

            // Sin tramo es un permiso de día completo, y ése ocupa el día entero.
            if (otro.HoraInicio is not TimeOnly otroInicio || otro.HoraFin is not TimeOnly otroFin)
                return (false, $"Ese día ya tiene un permiso de día completo ({Etiqueta(otro.Status)}). " +
                               "Pedir además unas horas del mismo día no cambiaría nada: cancela aquél " +
                               "o corrígelo.");

            // Dos tramos que solo se TOCAN por el extremo —de 9 a 11 y de 11 a 13— no se pisan: son
            // dos ausencias seguidas, y son perfectamente pedibles.
            if (inicio < otroFin && otroInicio < fin)
                return (false, $"Ese día ya hay un permiso {Etiqueta(otro.Status).ToLowerInvariant()} " +
                               $"de {Hhmm(otroInicio)} a {Hhmm(otroFin)}, y el tramo que pides se " +
                               "pisa con él.");
        }

        return (true, "");
    }

    private (bool ok, string error) Validar(LeaveRequest l, bool exigirMotivo)
    {
        var (ok, error) = ValidarDatos(l, exigirMotivo);
        if (!ok) return (false, error);

        if (l.AttachmentBytes is { Length: > 0 })
        {
            if (l.AttachmentBytes.Length > MaxAdjuntoBytes)
                return (false, $"El justificante supera {MaxAdjuntoBytes / (1024 * 1024)} MB.");
            if (string.IsNullOrWhiteSpace(l.AttachmentFileName))
                l.AttachmentFileName = "justificante";
        }
        else
        {
            // Sin bytes no debe quedar un nombre suelto: la pantalla mostraría un adjunto que no existe.
            l.AttachmentBytes = null;
            l.AttachmentFileName = null;
        }

        return (true, "");
    }

    private static string? Limpiar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// El nombre lo eligió quien subió el archivo: se limpia antes de que viaje en la cabecera
    /// <c>Content-Disposition</c> y acabe en el disco de quien lo descarga. La limpieza es la de
    /// <see cref="ArchivosSubidos.NombreSeguro"/>, común a todo lo que se sube.
    /// </summary>
    public static string NombreSeguro(string? nombre)
    {
        var n = ArchivosSubidos.NombreSeguro(nombre);
        return n.Length == 0 ? "justificante" : n;
    }

    /// <summary>
    /// Lo que se escribe en la BITÁCORA de cada movimiento del permiso.
    ///
    /// <para>Para un permiso de días completos dice lo mismo de siempre —«🩺 Cita médica 10/09/2026
    /// (2 día(s))»— y eso es deliberado: los asientos ya escritos se tienen que poder leer y buscar
    /// junto a los nuevos. Lo que cambia es que un permiso por horas dice sus horas en vez de fingir
    /// que fue un día entero.</para>
    /// </summary>
    private static string Describir(LeaveRequest l) =>
        $"{EtiquetaTipo(l.Type)} {l.Date:dd/MM/yyyy} ({Duracion(l.DaysCount, l.HoraInicio, l.HoraFin)})";

    /// <summary>
    /// En qué situación está la solicitud, con la PALABRA SOLA.
    ///
    /// <para>Llevaba delante un símbolo (⏳ ✅ ❌ 🚫) y se fue. Los dibuja EL SISTEMA OPERATIVO y no
    /// nosotros: se ven distintos en cada equipo, NO heredan el color del texto —en el tema oscuro se
    /// quedaban con el suyo mientras la palabra de al lado cambiaba— y donde no hay fuente de emoji
    /// instalada salen como un CUADRO VACÍO. Eso último se vio en una captura; no es una precaución
    /// inventada.</para>
    ///
    /// <para><b>Ojo, que ésta no es solo un adorno de rejilla: SE GUARDA.</b> Va dentro de la
    /// descripción que <c>ResolverAsync</c> escribe en la BITÁCORA —«Permiso aprobada: …»— y aparece
    /// además en cuatro mensajes de rechazo. Se cambia igualmente, y el motivo es que aquí solo cae el
    /// SÍMBOLO: la palabra —que es lo que alguien lee en un asiento viejo y lo que teclea si busca
    /// «rechazada»— no se toca. Los asientos anteriores dicen «Permiso ✅ aprobada» y los nuevos dirán
    /// «Permiso aprobada»; los dos se leen igual y una búsqueda por la palabra encuentra los dos. Es
    /// justo lo contrario de <c>EtiquetasDeCatalogo.PrioridadDelPool</c>, que no se toca porque allí
    /// cambiaría la PALABRA y eso sí partiría el histórico en dos.</para>
    ///
    /// <para>Nadie coteja esta cadena por igualdad: las decisiones se toman sobre
    /// <see cref="LeaveStatus"/>, que viaja en el DTO al lado del texto. Que siga así.</para>
    /// </summary>
    public static string Etiqueta(LeaveStatus s) => s switch
    {
        LeaveStatus.Pendiente => "Pendiente",
        LeaveStatus.Aprobada  => "Aprobada",
        LeaveStatus.Rechazada => "Rechazada",
        _                     => "Cancelada"
    };

    // El color de cada estado lo pone la UI (ver LeaveStatusUi): un servicio no debe depender del
    // tema visual, y con esto sigue siendo utilizable desde el futuro portal web.

    /// <summary>
    /// De qué es el permiso.
    ///
    /// <para><b>Estos SÍ conservan su emoji, y no es que se olvidaran</b> en la limpieza que dejó
    /// limpio a <see cref="Etiqueta"/> aquí al lado. La diferencia es dónde acaba cada uno: el tipo
    /// pasa por <see cref="Describir"/>, y <see cref="Describir"/> está metido en SEIS descripciones
    /// de bitácora —alta, registro del líder, edición, corrección, cancelación y baja—, o sea que este
    /// texto ya está grabado en el histórico de casi todo lo que se ha hecho con un permiso. Quitarlo
    /// sigue siendo defendible con el mismo argumento que allí (cae el símbolo, no la palabra), pero
    /// el usuario acotó esta tanda a los ESTADOS y ésta no es una decisión que se tome de paso.
    /// Cuando la pida, se quitan los seis dibujos de aquí abajo y no hace falta nada más: nadie
    /// compara estas cadenas.</para>
    /// </summary>
    public static string EtiquetaTipo(LeaveType t) => t switch
    {
        LeaveType.PermisoPersonal => "🙋 Permiso personal",
        LeaveType.Incapacidad     => "🏥 Incapacidad",
        LeaveType.CitaMedica      => "🩺 Cita médica",
        LeaveType.AsuntoFamiliar  => "👨‍👩‍👧 Asunto familiar",
        LeaveType.Capacitacion    => "📚 Capacitación",
        _                         => "📋 Otro"
    };
}
