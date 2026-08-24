using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Dtos.DevOps;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// La integración con Azure DevOps vista desde el negocio: quién puede hacer qué, con qué token se
/// firma cada llamada y qué se guarda en la base cuando DevOps contesta.
///
/// <para><b>El token nunca sale del servidor.</b> Esta clase es el único sitio que lo descifra, y lo
/// hace para pasárselo al cliente HTTP y nada más. Ningún método devuelve un PAT, ni entero ni
/// recortado, ni lo escribe en la bitácora. En el escritorio el token vivía en la máquina de cada
/// quien cifrado con DPAPI —lo que además lo perdía al cambiar de equipo—; aquí vive cifrado del lado
/// del servidor y sigue a la persona.</para>
///
/// <para><b>Un fallo de la integración no es un fallo de la aplicación.</b> Sin token, con un token
/// caducado o con DevOps caído, los métodos devuelven <c>(false, mensaje)</c> con un texto que se
/// entiende, no una excepción que acabe en un 500. Es la diferencia entre «tu token expiró, genera
/// otro» y «se produjo un error inesperado».</para>
///
/// <para><b>Las guardas están aquí además de en los endpoints.</b> El cliente Blazor corre en la
/// máquina de cada persona y a la API se la puede llamar sin pasar por él.</para>
/// </summary>
public partial class DevOpsService(
    AppDbContext db,
    ICurrentUser usuario,
    SettingsService configuracion,
    UserSecretsService secretos,
    AuditService bitacora,
    NotificationService avisos,
    IClienteAzureDevOps devops)
{
    /// <summary>Ámbito para el mensaje de las guardas, para que diga de qué operación se habla.</summary>
    private const string Ambito = "de los tickets de Azure DevOps";

    // ── Estados ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Traduce el estado del work item al estado local.
    ///
    /// Es público porque no solo sirve para importar: distinguir «entregado» de «cancelado» es lo que
    /// permite cerrar bien el compromiso de un ticket que ya se acabó. Copiado del escritorio sin
    /// tocar una línea: los nombres de estado dependen de la plantilla de proceso (Agile, Scrum,
    /// Basic) y esta tabla es la que ya cubre las tres en la organización.
    /// </summary>
    public static RequirementStatus MapearEstado(string? estado) => (estado ?? "").ToLowerInvariant() switch
    {
        "new" or "to do" or "proposed" => RequirementStatus.PorEstimar,
        "approved" or "committed" or "backlog" => RequirementStatus.Estimado,
        "active" or "doing" or "in progress" or "in development" => RequirementStatus.EnDesarrollo,
        "testing" or "in testing" or "ready for test" => RequirementStatus.EnPruebas,
        "resolved" or "ready for release" => RequirementStatus.PorEntregar,
        "closed" or "done" or "completed" => RequirementStatus.Entregado,
        "removed" or "cancelled" or "canceled" => RequirementStatus.Cancelado,
        _ => RequirementStatus.PorEstimar
    };

    /// <summary>
    /// Si el estado del work item cuenta como CERRADO. Se apoya en <see cref="MapearEstado"/> para
    /// tolerar los distintos nombres según la plantilla de proceso.
    /// </summary>
    public static bool EsCerrado(string? estado) =>
        MapearEstado(estado) is RequirementStatus.Entregado or RequirementStatus.Cancelado;

    // ── Estado de la integración y token personal ────────────────────────────────

    /// <summary>
    /// Con qué se cuenta para hablar con DevOps. De los secretos solo dice SI están puestos, que es lo
    /// único que se le puede contestar a una pantalla sobre ellos.
    /// </summary>
    public async Task<EstadoDevOpsDto> EstadoAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuario);

        var organizacion = (await configuracion.ObtenerAsync(SettingsService.Claves.AzureDevOpsOrgUrl, ct))?.TrimEnd('/');
        var proyecto = await configuracion.ObtenerAsync(SettingsService.Claves.AzureDevOpsProject, ct);
        bool habilitada = await configuracion.ObtenerBooleanoAsync(SettingsService.Claves.AzureDevOpsEnabled, ct);
        bool hayPatDeOrganizacion = !string.IsNullOrEmpty(
            await configuracion.ObtenerAsync(SettingsService.Claves.AzureDevOpsPat, ct));
        bool tengoPatPropio = await secretos.TengoConfiguradoAsync(PropositosDeSecreto.PatDevOps, ct);

        bool configurada = !string.IsNullOrEmpty(organizacion) && !string.IsNullOrEmpty(proyecto);

        var explicacion =
            !configurada
                ? "Falta la URL de organización o el proyecto de Azure DevOps. Pídeselo al líder."
                : !tengoPatPropio && !hayPatDeOrganizacion
                    ? "No hay ningún token de Azure DevOps configurado. Captura el tuyo para que lo que " +
                      "publiques quede a tu nombre."
                    : !tengoPatPropio
                        ? "Se usará el token de la instalación. Captura el tuyo para que los comentarios y " +
                          "los cambios queden firmados con tu cuenta en DevOps."
                        : "Listo: se usará tu token personal.";

        return new EstadoDevOpsDto(
            configurada, organizacion, proyecto, habilitada, tengoPatPropio, hayPatDeOrganizacion, explicacion,
            await configuracion.UltimaSincronizacionAsync(
                SettingsService.Claves.UltimaSincronizacionDevOps, ct));
    }

    /// <summary>
    /// Guarda o reemplaza el token personal. El valor viaja HACIA el servidor y ahí se queda cifrado:
    /// no hay ningún camino de vuelta, ni siquiera para quien lo capturó.
    /// </summary>
    public async Task<(bool ok, string mensaje)> GuardarMiPatAsync(string? pat, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuario);

        pat = (pat ?? "").Trim();
        if (pat.Length == 0)
            return (false, "Pega tu token antes de guardar.");

        // Se delega en el servicio de secretos entero: es él quien cifra, quien apunta el HECHO en la
        // bitácora (jamás el valor) y quien garantiza que cada persona solo escribe el suyo.
        return await secretos.GuardarMioAsync(PropositosDeSecreto.PatDevOps, pat, ct);
    }

    /// <summary>Quita el token personal. A partir de ahí se cae al de la instalación, si lo hay.</summary>
    public async Task<(bool ok, string mensaje)> BorrarMiPatAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuario);
        return await secretos.BorrarMioAsync(PropositosDeSecreto.PatDevOps, ct);
    }

    /// <summary>
    /// Prueba la conexión.
    ///
    /// Con <paramref name="patCandidato"/> se prueba un token recién escrito SIN guardarlo, igual que
    /// el diálogo del escritorio: así se sabe si sirve antes de reemplazar el que ya funciona. El
    /// candidato no se guarda, no se apunta y no vuelve en la respuesta.
    /// </summary>
    public async Task<(bool ok, string mensaje)> ProbarAsync(
        string? patCandidato, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuario);

        var (credenciales, problema) = await CredencialesAsync(
            exigirPropio: false, patCandidato: patCandidato, ct: ct);

        return credenciales is null
            ? (false, problema)
            : await devops.ProbarCredencialesAsync(credenciales, ct);
    }

    // ── Sincronización ───────────────────────────────────────────────────────────

    /// <summary>
    /// La sincronización del LÍDER: trae los work items del proyecto a la base local.
    ///
    /// Exige el interruptor <c>AzureDevOpsEnabled</c>, igual que en el escritorio: es la sincronización
    /// completa de la instalación y la enciende el administrador.
    /// </summary>
    /// <param name="filtro">Filtro selectivo. Nulo o vacío trae todo lo no removido.</param>
    public async Task<ResultadoDeSincronizacionDto> SincronizarAsync(
        FiltroDeSincronizacion? filtro = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuario);
        return await SincronizarProgramadaAsync(filtro, ct);
    }

    /// <summary>
    /// La misma sincronización, para cuando la dispara un trabajo de fondo.
    ///
    /// <para><b>Sin guarda de rol, y no es un descuido</b>: ahí no hay sesión, así que exigir
    /// administrador sería exigir algo que no puede existir. Lo que autoriza esta ejecución es la
    /// configuración del servidor. Es el mismo reparto que ya hacen la ingesta de correo y el resumen
    /// diario: el método que llama el trabajo no lleva guarda, y el que se puede pedir desde fuera
    /// —<see cref="SincronizarAsync"/>— sí.</para>
    ///
    /// <para><b>Las credenciales salen DIRECTAS del ajuste de la instalación</b> y no de
    /// <c>CredencialesAsync</c>: aquél busca primero el token personal, y para eso consulta la tabla
    /// de secretos, que exige sesión. Llamarlo desde un ámbito de fondo revienta dentro de esa
    /// guarda antes de llegar a la red.</para>
    ///
    /// <para>Y por lo mismo NO se toca lo que es personal: ni la foto de los tickets vigilados —que
    /// se filtra por el nombre de quien sincroniza y aquí saldría vacía— ni los avisos de asignación,
    /// que cuelgan de la sincronización personal y no de ésta.</para>
    /// </summary>
    public async Task<ResultadoDeSincronizacionDto> SincronizarProgramadaAsync(
        FiltroDeSincronizacion? filtro = null, CancellationToken ct = default)
    {
        // La comprobación del interruptor vive AQUÍ y no en el gemelo con guarda: si se quedara allá,
        // el trabajo de fondo sincronizaría con la integración apagada.
        if (!await configuracion.ObtenerBooleanoAsync(SettingsService.Claves.AzureDevOpsEnabled, ct))
            return Fallo("La integración con Azure DevOps está apagada. Enciéndela en Configuración.");

        var (credenciales, problema) = await CredencialesDeLaInstalacionAsync(ct);
        if (credenciales is null) return Fallo(problema);

        try
        {
            var wiql = AzureDevOpsService.ConstruirWiqlDeSincronizacion(credenciales.Proyecto, filtro);
            var ids = await devops.ConsultarIdsAsync(credenciales, wiql, ct);
            if (ids.Count == 0)
                return new ResultadoDeSincronizacionDto(
                    true, "Azure DevOps no devolvió ningún work item con ese filtro.", 0, 0, []);

            // Foto ANTES de escribir, para poder decir qué se movió de lo vigilado. Se toma solo de lo
            // que vigila quien sincroniza: vigilar es personal.
            var vigilados = await IdsVigiladosAsync(ct);
            var antes = await FotoDeVigiladosAsync(vigilados, ct);

            var items = await devops.ObtenerWorkItemsAsync(credenciales, ids, ct);
            var (nuevos, actualizados) = await GuardarTicketsAsync(items, ct);

            var cambios = await CambiosEnVigiladosAsync(antes, ct);

            await bitacora.RecordAsync(AuditAction.Create, "DevOpsTicket", null,
                $"Sincronización con Azure DevOps: {nuevos} nuevos, {actualizados} actualizados", ct);

            // El sello se escribe SOLO al terminar bien. Si se marcara al empezar, una sincronización
            // que reventó a medias dejaría la pantalla diciendo que los datos están al día.
            await configuracion.MarcarSincronizacionAsync(
                SettingsService.Claves.UltimaSincronizacionDevOps, DateTime.UtcNow, ct);

            return new ResultadoDeSincronizacionDto(
                true, $"Sincronización completada: {nuevos} nuevo(s), {actualizados} actualizado(s).",
                nuevos, actualizados, cambios);
        }
        catch (ErrorDeAzureDevOps ex)
        {
            return Fallo(ex.Message);
        }
    }

    /// <summary>
    /// La sincronización del DESARROLLADOR: pide a DevOps lo asignado a la cuenta de SU token (macro
    /// <c>@Me</c>) dentro de una ventana de días.
    ///
    /// <para>Exige token PROPIO y no se cae al de la instalación, y eso es lo que la hace correcta:
    /// <c>@Me</c> lo resuelve DevOps contra el dueño del token, así que con el de la instalación
    /// traería los tickets de otra cuenta.</para>
    ///
    /// <para>A propósito NO exige el interruptor del administrador, igual que en el escritorio: si
    /// dependiera de él, nadie podría actualizar su propia lista hasta que el líder sincronizara, que
    /// era exactamente el problema que esta pantalla vino a resolver.</para>
    /// </summary>
    public async Task<ResultadoDeSincronizacionDto> SincronizarMisTicketsAsync(
        int dias = MyDevOpsTicketFilter.DiasPorOmision, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(usuario, Ambito);

        var (credenciales, problema) = await CredencialesAsync(exigirPropio: true, ct: ct);
        if (credenciales is null) return Fallo(problema);

        if (dias <= 0) dias = MyDevOpsTicketFilter.DiasMaximosDeSincronizacion;

        try
        {
            var filtro = new FiltroDeSincronizacion([], [], [], SoloMisAsignados: true, CambiadosEnDias: dias);
            var wiql = AzureDevOpsService.ConstruirWiqlDeSincronizacion(credenciales.Proyecto, filtro);

            var ids = await devops.ConsultarIdsAsync(credenciales, wiql, ct);
            if (ids.Count == 0)
                return new ResultadoDeSincronizacionDto(
                    true,
                    $"Azure DevOps no tiene work items a tu nombre movidos en los últimos {dias} días.",
                    0, 0, []);

            var items = await devops.ObtenerWorkItemsAsync(credenciales, ids, ct);
            var (nuevos, actualizados) = await GuardarTicketsAsync(items, ct);

            // Justo después de traer lo suyo es cuando tiene sentido avisar de lo que le acaban de
            // asignar: los datos están recién puestos al día y la línea base evita el aluvión.
            await DetectarAsignadosNuevosAsync(ct);

            return new ResultadoDeSincronizacionDto(
                true, $"Sincronizado: {nuevos} nuevo(s), {actualizados} actualizado(s).",
                nuevos, actualizados, []);
        }
        catch (ErrorDeAzureDevOps ex)
        {
            return Fallo(ex.Message);
        }
    }

    /// <summary>
    /// Avisa de los work items ABIERTOS recién asignados a quien tiene la sesión.
    ///
    /// La PRIMERA vez para esa cuenta fija una línea base SIN avisar, para no soltar de golpe un aviso
    /// por cada ticket de su historial. Se marca con un centinela <c>ExternalId = 0</c>, para que
    /// «línea base establecida» valga también cuando la persona no tiene ningún ticket todavía.
    /// </summary>
    private async Task DetectarAsignadosNuevosAsync(CancellationToken ct)
    {
        if (usuario.UserId is not int userId || usuario.DeveloperId is not int developerId) return;

        var dev = await db.Developers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == developerId, ct);
        if (dev == null) return;

        var mios = (await DevOpsTicketQuery.DeDesarrolladorAsync(db, dev, ct))
            .Where(t => !EsCerrado(t.State))
            .ToList();

        var vistos = await db.DevOpsAssignmentsSeen.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => s.ExternalId)
            .ToListAsync(ct);

        if (vistos.Count == 0)
        {
            db.DevOpsAssignmentsSeen.Add(new DevOpsAssignmentSeen { UserId = userId, ExternalId = 0 });
            foreach (var t in mios)
                db.DevOpsAssignmentsSeen.Add(new DevOpsAssignmentSeen { UserId = userId, ExternalId = t.ExternalId });

            await db.SaveChangesAsync(ct);
            return;
        }

        var conocidos = vistos.ToHashSet();
        var nuevos = mios.Where(t => !conocidos.Contains(t.ExternalId)).ToList();
        if (nuevos.Count == 0) return;

        foreach (var t in nuevos)
        {
            db.DevOpsAssignmentsSeen.Add(new DevOpsAssignmentSeen { UserId = userId, ExternalId = t.ExternalId });
            await avisos.NotifyAsync(userId, NotificationKind.DevOpsAssigned,
                $"Te asignaron el ticket #{t.ExternalId}",
                $"{t.WorkItemType}: {t.Title}", t.Url,
                dedupeKey: $"devops-assign:{t.ExternalId}", ct);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Vuelca a la base lo que devolvió DevOps. Devuelve cuántos se dieron de alta y cuántos se
    /// actualizaron.
    /// </summary>
    private async Task<(int nuevos, int actualizados)> GuardarTicketsAsync(
        IReadOnlyList<WorkItemDevOps> items, CancellationToken ct)
    {
        if (items.Count == 0) return (0, 0);

        // Una sola lectura para todo el lote. La versión del escritorio consultaba la base ticket a
        // ticket dentro del bucle, que contra una base remota son cientos de viajes por sincronización.
        var numeros = items.Select(i => i.Id).ToList();
        var existentes = await db.DevOpsTickets
            .Where(t => numeros.Contains(t.ExternalId))
            .ToDictionaryAsync(t => t.ExternalId, ct);

        var ahora = DateTime.UtcNow;
        int nuevos = 0, actualizados = 0;

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            if (existentes.TryGetValue(item.Id, out var ticket))
            {
                actualizados++;
            }
            else
            {
                ticket = new DevOpsTicket { ExternalId = item.Id };
                db.DevOpsTickets.Add(ticket);
                nuevos++;
            }

            ticket.Title = item.Titulo;
            ticket.WorkItemType = item.Tipo;
            ticket.State = item.Estado;
            ticket.Priority = item.Prioridad;
            ticket.AssignedTo = item.AsignadoA;
            ticket.AssignedToUniqueName = string.IsNullOrWhiteSpace(item.CorreoAsignado) ? null : item.CorreoAsignado;
            ticket.AreaPath = item.Area;
            ticket.IterationPath = item.Iteracion;
            ticket.Tags = item.Etiquetas;
            ticket.Description = item.Descripcion;
            ticket.StoryPoints = item.Puntos;
            ticket.UpdatedAtExternal = item.ActualizadoUtc;
            ticket.SyncedAt = ahora;
            ticket.Url = item.Url;
            ticket.CommentCount = item.Comentarios;

            // La fecha de creación solo se pone al dar de alta: en DevOps no cambia, y reescribirla en
            // cada pasada solo daría ocasión de perderla si un día viniera vacía.
            if (ticket.CreatedAtExternal == null) ticket.CreatedAtExternal = item.CreadoUtc;

            // DevOps manda cuando trae valor; si allá está vacío se conserva lo capturado aquí, que
            // puede ser de hace un momento y aún no haberse escrito allá.
            ticket.EstimatedHours = item.HorasEstimadas ?? ticket.EstimatedHours;
        }

        await db.SaveChangesAsync(ct);
        return (nuevos, actualizados);
    }

    // ── Comentarios ──────────────────────────────────────────────────────────────

    /// <summary>Los comentarios del ticket, en TEXTO PLANO y del más antiguo al más reciente.</summary>
    public async Task<(bool ok, string mensaje, ComentariosDevOpsDto? datos)> ComentariosAsync(
        int numero, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(usuario, Ambito);

        var (ticket, motivo) = await TicketOperableAsync(numero, ct);
        if (ticket is null) return (false, motivo, null);

        var (credenciales, problema) = await CredencialesAsync(exigirPropio: false, ct: ct);
        if (credenciales is null) return (false, problema, null);

        try
        {
            var comentarios = await devops.ObtenerComentariosAsync(credenciales, numero, ct);

            return (true, "", new ComentariosDevOpsDto(numero, ticket.Title,
                comentarios
                    .OrderBy(c => c.CreadoUtc)
                    .Select(c => new ComentarioDevOpsDto(ATextoPlano(c.Texto), c.Autor, c.CreadoUtc))
                    .ToList()));
        }
        catch (ErrorDeAzureDevOps ex)
        {
            return (false, ex.Message, null);
        }
    }

    /// <summary>
    /// Publica un comentario en el ticket, con evidencias (capturas) embebidas si las hay.
    ///
    /// Se firma con el token de quien lo pide, para que en DevOps quede a su nombre. El texto se
    /// escapa antes de armar el HTML: lo escribe una persona y acaba dentro de un documento que otras
    /// leen.
    /// </summary>
    public async Task<(bool ok, string mensaje)> ComentarAsync(
        int numero, string? texto, IReadOnlyList<(string nombre, byte[] contenido)> evidencias,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(usuario, Ambito);

        texto = (texto ?? "").Trim();
        if (texto.Length == 0 && evidencias.Count == 0)
            return (false, "Escribe algo o adjunta una evidencia antes de enviar.");

        var (ticket, motivo) = await TicketOperableAsync(numero, ct);
        if (ticket is null) return (false, motivo);

        // Que los bytes sean de verdad una imagen se comprueba AQUÍ y no en el navegador: a esta ruta
        // se puede llamar sin pasar por él, y lo que se suba acaba servido desde el dominio de DevOps.
        foreach (var (nombre, contenido) in evidencias)
        {
            var (ok, error, _) = ArchivosSubidos.Validar(nombre, contenido, soloImagenes: true);
            if (!ok) return (false, error);
        }

        var (credenciales, problema) = await CredencialesAsync(exigirPropio: false, ct: ct);
        if (credenciales is null) return (false, problema);

        try
        {
            var html = new StringBuilder();
            html.Append(WebUtility.HtmlEncode(texto).Replace("\n", "<br>"));

            foreach (var (nombre, contenido) in evidencias)
            {
                // La dirección la devuelve DevOps, así que es la única parte del HTML que no hace
                // falta escapar; el nombre sí, porque lo escribió quien subió el archivo.
                var url = await devops.SubirAdjuntoAsync(credenciales, contenido, nombre, ct);
                html.Append("<br><br>📎 <b>")
                    .Append(WebUtility.HtmlEncode(ArchivosSubidos.NombreSeguro(nombre)))
                    .Append("</b><br><img src=\"").Append(url).Append("\" width=\"640\" />");
            }

            await devops.PublicarComentarioAsync(credenciales, numero, html.ToString(), ct);

            // El contador local se sube a mano para que la rejilla lo refleje sin re-sincronizar; la
            // próxima sincronización traerá el valor autoritativo de DevOps.
            ticket.CommentCount++;
            await db.SaveChangesAsync(ct);

            await bitacora.RecordAsync(AuditAction.Update, "DevOpsTicket", numero.ToString(),
                evidencias.Count == 0
                    ? $"Comentario publicado en DevOps en el ticket #{numero}"
                    : $"Comentario con {evidencias.Count} evidencia(s) publicado en DevOps en el ticket #{numero}",
                ct);

            return (true, "Comentario publicado en Azure DevOps a tu nombre.");
        }
        catch (ErrorDeAzureDevOps ex)
        {
            return (false, ex.Message);
        }
    }

    // ── Cambios sobre el work item ───────────────────────────────────────────────

    /// <summary>
    /// Reasigna el work item en DevOps. Con el correo vacío lo DESASIGNA. Es del líder: repartir el
    /// trabajo no es algo que cada quien haga sobre sí mismo.
    /// </summary>
    public async Task<(bool ok, string mensaje)> ReasignarAsync(
        int numero, string? correo, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuario);

        var ticket = await db.DevOpsTickets.FirstOrDefaultAsync(t => t.ExternalId == numero, ct);
        if (ticket == null) return (false, NoEstaSincronizado(numero));

        var (credenciales, problema) = await CredencialesAsync(exigirPropio: false, ct: ct);
        if (credenciales is null) return (false, problema);

        try
        {
            var (nombre, correoResultante) = await devops.ReasignarAsync(credenciales, numero, correo, ct);

            ticket.AssignedTo = nombre;
            ticket.AssignedToUniqueName = string.IsNullOrWhiteSpace(correoResultante) ? null : correoResultante;
            await db.SaveChangesAsync(ct);

            await bitacora.RecordAsync(AuditAction.Update, "DevOpsTicket", numero.ToString(),
                $"Reasignado en DevOps a «{(string.IsNullOrWhiteSpace(nombre) ? "(sin asignar)" : nombre)}»", ct);

            // Aviso a quien le acaba de tocar, si tiene cuenta. Se empata por identidad con lo que
            // devolvió DevOps y no con lo que se pidió: si allá lo resolvió a otra persona, el aviso
            // tiene que ir a esa.
            var dev = DevOpsIdentityMatcher.Buscar(nombre, correoResultante,
                await db.Developers.AsNoTracking().Where(d => d.IsActive).ToListAsync(ct));

            if (dev != null)
                await avisos.NotifyDeveloperAsync(dev.Id, NotificationKind.DevOpsAssigned,
                    $"Te asignaron el ticket #{numero}",
                    $"{ticket.WorkItemType}: {ticket.Title}", ticket.Url,
                    dedupeKey: $"devops-assign:{numero}", ct);

            return (true, string.IsNullOrWhiteSpace(nombre)
                ? $"El ticket #{numero} quedó sin asignar en Azure DevOps."
                : $"El ticket #{numero} quedó a nombre de «{nombre}» en Azure DevOps.");
        }
        catch (ErrorDeAzureDevOps ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Mueve el ticket de columna (System.State). DevOps valida la transición según la plantilla de
    /// proceso; si la rechaza, se enseña lo que contestó.
    /// </summary>
    public async Task<(bool ok, string mensaje)> CambiarEstadoAsync(
        int numero, string? nuevoEstado, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuario);

        nuevoEstado = (nuevoEstado ?? "").Trim();
        if (nuevoEstado.Length == 0) return (false, "Indica a qué estado se mueve el ticket.");

        var ticket = await db.DevOpsTickets.FirstOrDefaultAsync(t => t.ExternalId == numero, ct);
        if (ticket == null) return (false, NoEstaSincronizado(numero));

        var (credenciales, problema) = await CredencialesAsync(exigirPropio: false, ct: ct);
        if (credenciales is null) return (false, problema);

        try
        {
            var estado = await devops.CambiarEstadoAsync(credenciales, numero, nuevoEstado, ct);

            ticket.State = estado;
            await db.SaveChangesAsync(ct);

            await bitacora.RecordAsync(AuditAction.Update, "DevOpsTicket", numero.ToString(),
                $"Estado cambiado a «{estado}» en DevOps", ct);

            return (true, $"El ticket #{numero} quedó en «{estado}».");
        }
        catch (ErrorDeAzureDevOps ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Cambia la prioridad del work item (1 muy alta … 4 baja) y deja constancia de que alguien la
    /// pensó: el campo por sí solo no sirve para saberlo, porque DevOps le pone 2 por omisión a todo.
    ///
    /// <para>El desarrollador puede cambiarla en LO SUYO, igual que en el escritorio, donde su
    /// pantalla solo listaba sus tickets. Allí la restricción la daba la interfaz; aquí se comprueba
    /// contra la fila, porque a la API se la puede llamar sin pasar por la pantalla.</para>
    ///
    /// <para><b>Cambiar la prioridad REAJUSTA el compromiso de SLA</b>, como en el escritorio: subir
    /// un ticket a «muy alta» y dejar corriendo el plazo de la prioridad vieja convertía el cambio en
    /// un gesto decorativo — el aviso seguía llegando cuando ya no servía de nada. Lo hace
    /// <see cref="ReconciliacionDePrioridadDeDevOps"/>, en el mismo guardado; y lo hace también el
    /// empuje del pool, que llama exactamente a lo mismo para que la prioridad signifique igual se
    /// cambie desde donde se cambie.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> CambiarPrioridadAsync(
        int numero, int prioridad, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(usuario, Ambito);

        if (prioridad is < 1 or > 4)
            return (false, "La prioridad de DevOps va de 1 (muy alta) a 4 (baja).");

        var (ticket, motivo) = await TicketOperableAsync(numero, ct);
        if (ticket is null) return (false, motivo);

        var (credenciales, problema) = await CredencialesAsync(exigirPropio: false, ct: ct);
        if (credenciales is null) return (false, problema);

        try
        {
            await devops.CambiarPrioridadAsync(credenciales, numero, prioridad, ct);

            // Va DESPUÉS de que DevOps aceptara y nunca antes: si la escritura de allá falla, aquí no
            // se toca nada y el compromiso sigue con el plazo que le correspondía.
            await ReconciliacionDePrioridadDeDevOps.ReconciliarAsync(
                db, configuracion, usuario.UserId, numero, prioridad, ct);

            await bitacora.RecordAsync(AuditAction.Update, "DevOpsTicket", numero.ToString(),
                $"Prioridad cambiada a {prioridad} en DevOps", ct);

            return (true, $"La prioridad del ticket #{numero} quedó en {prioridad}.");
        }
        catch (ErrorDeAzureDevOps ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Guarda la estimación del ticket en la base local Y en el campo Effort del work item.
    ///
    /// Escribirlo en DevOps es el punto: si solo viviera aquí, para el resto de la empresa el ticket
    /// seguiría sin estimar. Si DevOps rechaza el campo —hay plantillas donde Effort no existe en ese
    /// tipo de work item— la estimación se guarda igual en local y se devuelve el aviso, en lugar de
    /// perder lo que la persona acaba de capturar.
    /// </summary>
    public async Task<(bool ok, string mensaje)> EstimarAsync(
        int numero, double horas, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(usuario, Ambito);

        if (horas <= 0) return (false, "La estimación tiene que ser mayor que cero.");
        if (horas > 1000)
            return (false, "Esa estimación no parece real. Si de verdad son más de 1000 horas, " +
                           "pártelo en varios tickets.");

        horas = Math.Round(horas, 2);

        var (ticket, motivo) = await TicketOperableAsync(numero, ct);
        if (ticket is null) return (false, motivo);

        var (credenciales, problema) = await CredencialesAsync(exigirPropio: false, ct: ct);
        if (credenciales is null) return (false, problema);

        bool escrito = false;
        string aviso;
        try
        {
            (escrito, aviso) = await devops.EscribirEstimacionAsync(credenciales, numero, horas, ct);
        }
        catch (ErrorDeAzureDevOps ex)
        {
            aviso = ex.Message;
        }

        ticket.EstimatedHours = horas;
        ticket.EstimatedAt = DateTime.UtcNow;
        ticket.EstimatedByDeveloperId = usuario.DeveloperId;
        await db.SaveChangesAsync(ct);

        await bitacora.RecordAsync(AuditAction.Update, "DevOpsTicket", numero.ToString(),
            $"Estimado en {horas:0.##} h" + (escrito ? " (escrito en el campo Effort)" : " (solo en local)"), ct);

        // Devuelve ok en los dos casos a propósito: la estimación SÍ quedó guardada, y decir que no se
        // pudo haría que la persona la volviera a capturar una y otra vez.
        return (true, escrito
            ? $"Estimado en {horas:0.##} h y guardado en el campo Effort del ticket."
            : $"Estimado en {horas:0.##} h, pero solo quedó guardado aquí. {aviso}");
    }

    // ── Ficha: regresiones y devoluciones ────────────────────────────────────────

    /// <summary>
    /// Trae de DevOps lo que no cabe en los campos del ticket: los bugs colgados como hijos —cada uno
    /// es una regresión que provocó— y el historial de asignaciones.
    ///
    /// Son dos llamadas aparte y a propósito no se hacen durante la sincronización: expandir
    /// relaciones y bajar revisiones de CADA ticket convertiría una sincronización de doscientos en
    /// varios cientos de peticiones. Esto se pide cuando alguien abre la ficha de UNO.
    /// </summary>
    public async Task<(bool ok, string mensaje, FichaDeTicketDto? ficha)> FichaAsync(
        int numero, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(usuario, Ambito);

        var (ticket, motivo) = await TicketOperableAsync(numero, ct);
        if (ticket is null) return (false, motivo, null);

        var (credenciales, problema) = await CredencialesAsync(exigirPropio: false, ct: ct);
        if (credenciales is null) return (false, problema, null);

        try
        {
            var bugs = await devops.ObtenerBugsHijosAsync(credenciales, numero, ct);
            var asignaciones = await devops.ObtenerHistorialDeAsignacionAsync(credenciales, numero, ct);

            var ficha = new DevOpsTicketFicha(numero, bugs, asignaciones);

            return (true, "", new FichaDeTicketDto(
                numero,
                ticket.Title,
                ticket.AssignedTo,
                bugs.Select(b => new RegresionDto(b.Id, b.Titulo, b.Estado, b.Url, EsCerrado(b.Estado))).ToList(),
                ficha.RegresionesAbiertas,
                asignaciones.Select(c => new CambioDeAsignacionDto(c.Fecha, c.De, c.A)).ToList(),
                ficha.DevolucionesA(ticket.AssignedTo, ticket.AssignedToUniqueName),
                ficha.Manos));
        }
        catch (ErrorDeAzureDevOps ex)
        {
            return (false, ex.Message, null);
        }
    }

    // ── Vigilancia ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Empieza o deja de vigilar un ticket. Vigilar es PERSONAL: cada quien tiene su lista, y por eso
    /// la fila se ata al nombre de usuario de la sesión y no lleva identificador por parámetro.
    /// </summary>
    public async Task<(bool ok, string mensaje)> AlternarVigilanciaAsync(
        int numero, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(usuario, Ambito);

        var quien = usuario.Username ?? "";
        if (quien.Length == 0) return (false, "No hay una sesión válida.");

        var ticket = await db.DevOpsTickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.ExternalId == numero, ct);
        if (ticket == null) return (false, NoEstaSincronizado(numero));

        var vigilancia = await db.WatchedTickets
            .FirstOrDefaultAsync(w => w.DevOpsTicketId == ticket.Id && w.WatchedByUser == quien, ct);

        if (vigilancia != null)
        {
            db.WatchedTickets.Remove(vigilancia);
            await db.SaveChangesAsync(ct);
            return (true, $"Dejaste de vigilar el ticket #{numero}.");
        }

        db.WatchedTickets.Add(new WatchedTicket
        {
            DevOpsTicketId = ticket.Id,
            WatchedByUser = quien,
            WatchedSince = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);

        return (true, $"Vigilando el ticket #{numero}: se te avisará de sus cambios al sincronizar.");
    }

    private async Task<List<int>> IdsVigiladosAsync(CancellationToken ct)
    {
        var quien = usuario.Username ?? "";
        if (quien.Length == 0) return [];

        return await db.WatchedTickets.AsNoTracking()
            .Where(w => w.WatchedByUser == quien)
            .Select(w => w.DevOpsTicketId)
            .ToListAsync(ct);
    }

    private async Task<Dictionary<int, (DateTime? actualizado, int comentarios)>> FotoDeVigiladosAsync(
        IReadOnlyList<int> vigilados, CancellationToken ct)
    {
        if (vigilados.Count == 0) return [];

        return (await db.DevOpsTickets.AsNoTracking()
                .Where(t => vigilados.Contains(t.Id))
                .Select(t => new { t.Id, t.UpdatedAtExternal, t.CommentCount })
                .ToListAsync(ct))
            .ToDictionary(t => t.Id, t => ((DateTime?)t.UpdatedAtExternal, t.CommentCount));
    }

    /// <summary>Qué se movió de lo vigilado, ya redactado para enseñarlo.</summary>
    private async Task<List<string>> CambiosEnVigiladosAsync(
        Dictionary<int, (DateTime? actualizado, int comentarios)> antes, CancellationToken ct)
    {
        if (antes.Count == 0) return [];

        var ids = antes.Keys.ToList();
        var despues = await db.DevOpsTickets.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.ExternalId, t.UpdatedAtExternal, t.CommentCount })
            .ToListAsync(ct);

        var cambios = new List<string>();
        foreach (var t in despues)
        {
            if (!antes.TryGetValue(t.Id, out var foto)) continue;

            var motivos = new List<string>();
            if (t.UpdatedAtExternal != foto.actualizado) motivos.Add("estado o campos actualizados");
            if (t.CommentCount > foto.comentarios)
                motivos.Add($"{t.CommentCount - foto.comentarios} comentario(s) nuevo(s)");

            if (motivos.Count > 0)
                cambios.Add($"#{t.ExternalId} — {string.Join(", ", motivos)}");
        }
        return cambios;
    }

    // ── Filtros guardados ────────────────────────────────────────────────────────

    /// <summary>
    /// Guarda o reemplaza un filtro con nombre de la rejilla grande. Se empata por NOMBRE, igual que
    /// en el escritorio: volver a guardar «Bugs abiertos» actualiza el que ya existe en vez de dejar
    /// dos iguales.
    /// </summary>
    public async Task<(bool ok, string mensaje)> GuardarFiltroAsync(
        GuardarFiltroRequest peticion, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuario);

        var nombre = (peticion.Nombre ?? "").Trim();
        if (nombre.Length == 0) return (false, "Ponle nombre al filtro.");
        if (nombre.Length > 100) return (false, "El nombre del filtro no puede pasar de 100 caracteres.");

        var filtro = await db.DevOpsSavedFilters.FirstOrDefaultAsync(f => f.Name == nombre, ct);
        if (filtro == null)
        {
            filtro = new DevOpsSavedFilter { Name = nombre, CreatedAt = DateTime.UtcNow };
            db.DevOpsSavedFilters.Add(filtro);
        }

        filtro.GlobalSearch = Vacio(peticion.Busqueda);
        filtro.TitleContains = Vacio(peticion.TituloContiene);
        filtro.ColumnFiltersJson = Vacio(peticion.AjustesDeRejilla);

        await db.SaveChangesAsync(ct);
        return (true, $"Filtro «{nombre}» guardado.");
    }

    public async Task<(bool ok, string mensaje)> BorrarFiltroAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuario);

        var filtro = await db.DevOpsSavedFilters.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (filtro == null) return (false, "Ese filtro ya no existe. Actualiza la lista.");

        db.DevOpsSavedFilters.Remove(filtro);
        await db.SaveChangesAsync(ct);
        return (true, $"Filtro «{filtro.Name}» eliminado.");
    }

    // ── Reglas de auto-asignación ────────────────────────────────────────────────

    /// <summary>Alta o edición de una regla de auto-asignación. <c>Id = 0</c> significa alta.</summary>
    public async Task<(bool ok, string mensaje)> GuardarReglaAsync(
        GuardarReglaRequest peticion, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuario);

        var valor = (peticion.Valor ?? "").Trim();
        if (valor.Length == 0) return (false, "Indica el valor que tiene que cumplir el work item.");

        if (!await db.Developers.AnyAsync(d => d.Id == peticion.DesarrolladorId, ct))
            return (false, "Ese desarrollador ya no existe. Actualiza la lista.");

        var regla = peticion.Id > 0
            ? await db.DevOpsAssignmentRules.FirstOrDefaultAsync(r => r.Id == peticion.Id, ct)
            : null;

        if (peticion.Id > 0 && regla == null)
            return (false, "Esa regla ya no existe. Actualiza la lista.");

        if (regla == null)
        {
            regla = new DevOpsAssignmentRule { CreatedAt = DateTime.UtcNow };
            db.DevOpsAssignmentRules.Add(regla);
        }

        regla.Match = peticion.Condicion;
        regla.MatchValue = valor;
        regla.DeveloperId = peticion.DesarrolladorId;
        regla.Order = peticion.Orden;
        regla.IsActive = peticion.Activa;

        await db.SaveChangesAsync(ct);
        return (true, peticion.Id > 0 ? "Regla actualizada." : "Regla creada.");
    }

    public async Task<(bool ok, string mensaje)> BorrarReglaAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuario);

        var regla = await db.DevOpsAssignmentRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (regla == null) return (false, "Esa regla ya no existe. Actualiza la lista.");

        db.DevOpsAssignmentRules.Remove(regla);
        await db.SaveChangesAsync(ct);
        return (true, "Regla eliminada.");
    }

    // ── Importación a requerimientos ─────────────────────────────────────────────

    /// <summary>
    /// Da de alta como REQUERIMIENTOS los work items asignados y abiertos, y se los reparte según las
    /// reglas.
    ///
    /// <para>No da de alta nada que ya esté cerrado: el objetivo es dar seguimiento a trabajo vivo, y
    /// un requerimiento nuevo ya entregado no aporta nada que cronometrar. Los que ya existían sí se
    /// actualizan, para que reflejen que pasaron a Entregado o Cancelado.</para>
    ///
    /// <para>El reparto va por reglas y, si ninguna coincide, por IDENTIDAD: el asignado del work item
    /// contra los desarrolladores activos. Ese respaldo es lo que hace que el item aparezca en «Mis
    /// Asignaciones» de su dueño real sin tener que escribir una regla por cada persona.</para>
    /// </summary>
    public async Task<ResultadoDeImportacionDto> ImportarComoRequerimientosAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuario);

        var (credenciales, problema) = await CredencialesAsync(exigirPropio: false, ct: ct);
        if (credenciales is null) return new ResultadoDeImportacionDto(false, problema, []);

        List<WorkItemDevOps> items;
        try
        {
            var wiql = AzureDevOpsService.WiqlDeAsignadosAbiertos(credenciales.Proyecto);
            var ids = await devops.ConsultarIdsAsync(credenciales, wiql, ct);

            items = (await devops.ObtenerWorkItemsAsync(credenciales, ids, ct))
                // El filtro autoritativo se hace aquí y no en la WIQL: la plantilla de proceso puede
                // usar nombres de estado que la consulta no conoce.
                .Where(i => !EsCerrado(i.Estado) && !string.IsNullOrWhiteSpace(i.AsignadoA))
                .ToList();
        }
        catch (ErrorDeAzureDevOps ex)
        {
            return new ResultadoDeImportacionDto(false, ex.Message, []);
        }

        if (items.Count == 0)
            return new ResultadoDeImportacionDto(
                true, "No hay work items asignados y abiertos que importar.", []);

        var reglas = await db.DevOpsAssignmentRules
            .Where(r => r.IsActive)
            .OrderBy(r => r.Order).ThenBy(r => r.Id)
            .Select(r => new { r.Match, r.MatchValue, r.DeveloperId, Nombre = r.Developer.FullName })
            .ToListAsync(ct);

        var desarrolladores = await db.Developers.Where(d => d.IsActive).ToListAsync(ct);

        var identificadores = items.Select(i => i.Id.ToString()).ToList();
        var existentes = await db.Requirements
            .Where(r => r.Source == RequirementSource.AzureDevOps
                        && r.ExternalId != null && identificadores.Contains(r.ExternalId))
            .ToDictionaryAsync(r => r.ExternalId!, ct);

        var ahora = DateTime.UtcNow;
        int nuevos = 0, actualizados = 0;
        var altas = new List<string>();
        var aAvisar = new List<(int developerId, string identificador, string titulo, string url)>();

        foreach (var item in items)
        {
            var identificador = item.Id.ToString();

            if (existentes.TryGetValue(identificador, out var requerimiento))
            {
                requerimiento.Title = item.Titulo;
                if (!string.IsNullOrEmpty(item.Descripcion)) requerimiento.Description = item.Descripcion;
                requerimiento.Status = MapearEstado(item.Estado);
                requerimiento.ExternalUrl = item.Url;
                actualizados++;
                continue;
            }

            var nuevo = new Requirement
            {
                Title = item.Titulo,
                Description = item.Descripcion,
                Status = MapearEstado(item.Estado),
                Priority = RequirementPriority.Media,
                Source = RequirementSource.AzureDevOps,
                ExternalId = identificador,
                ExternalUrl = item.Url,
                CreatedAt = ahora,
                StatusChangedAt = ahora
            };
            db.Requirements.Add(nuevo);

            // Primera regla activa que coincida; si ninguna, el respaldo por identidad.
            var regla = reglas.FirstOrDefault(r => ReglaCoincide(r.Match, r.MatchValue, item));
            int? developerId = regla?.DeveloperId
                ?? DevOpsIdentityMatcher.Buscar(item.AsignadoA, item.CorreoAsignado, desarrolladores)?.Id;

            string? aQuien = regla?.Nombre
                ?? (developerId is int id ? desarrolladores.FirstOrDefault(d => d.Id == id)?.FullName : null);

            if (developerId is int devId)
            {
                db.Assignments.Add(new Assignment
                {
                    Requirement = nuevo, DeveloperId = devId, AssignedAt = ahora
                });
                aAvisar.Add((devId, identificador, item.Titulo, item.Url));
            }

            altas.Add(aQuien == null ? item.Titulo : $"{item.Titulo} → {aQuien}");
            nuevos++;
        }

        await db.SaveChangesAsync(ct);

        // Los avisos van DESPUÉS de guardar: si el guardado fallara, se habría avisado de trabajo que
        // nadie tiene asignado.
        foreach (var (developerId, identificador, titulo, url) in aAvisar)
            await avisos.NotifyDeveloperAsync(developerId, NotificationKind.RequirementAssigned,
                "Nuevo requerimiento asignado", $"#{identificador} — {titulo}", url,
                dedupeKey: $"req-assign:{developerId}:{identificador}", ct);

        await bitacora.RecordAsync(AuditAction.Create, "Requirement", null,
            $"Importación desde Azure DevOps: {nuevos} nuevos, {actualizados} actualizados, " +
            $"{aAvisar.Count} auto-asignados", ct);

        return new ResultadoDeImportacionDto(
            true,
            $"Importación completada: {nuevos} nuevo(s), {actualizados} actualizado(s), " +
            $"{aAvisar.Count} auto-asignado(s).",
            altas);
    }

    private static bool ReglaCoincide(DevOpsRuleMatch condicion, string valor, WorkItemDevOps item)
    {
        if (string.IsNullOrWhiteSpace(valor)) return false;
        var v = valor.Trim();

        return condicion switch
        {
            DevOpsRuleMatch.AreaPathContiene => item.Area.Contains(v, StringComparison.OrdinalIgnoreCase),
            DevOpsRuleMatch.TipoEsIgual => item.Tipo.Equals(v, StringComparison.OrdinalIgnoreCase),
            DevOpsRuleMatch.TituloContiene => item.Titulo.Contains(v, StringComparison.OrdinalIgnoreCase),
            DevOpsRuleMatch.TagContiene => item.Etiquetas.Contains(v, StringComparison.OrdinalIgnoreCase),
            DevOpsRuleMatch.AsignadoAContiene => item.AsignadoA.Contains(v, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    /// <summary>
    /// Trae a las asignaciones de quien tiene la sesión sus tickets de DevOps ABIERTOS: los da de
    /// alta como requerimientos y se los asigna, que es lo que les pone cronómetro.
    ///
    /// <para><b>No toca la red.</b> Trabaja sobre lo ya sincronizado, y por eso la pantalla puede
    /// llamarlo cada vez que se abre sin gastar una petición a DevOps por visita. Quien quiera datos
    /// frescos sincroniza antes con <see cref="SincronizarMisTicketsAsync"/>.</para>
    ///
    /// <para>Es idempotente —empata por número de work item— y en los que ya existían solo refresca
    /// el título y el enlace: el ESTADO y el avance NO se tocan, porque reflejan lo que la persona
    /// lleva medido aquí y el ticket no sabe nada de eso.</para>
    ///
    /// <para>Después pone el SLA automático por prioridad a TODO lo materializado y no solo a lo
    /// recién creado, igual que en el escritorio: así las asignaciones viejas también acaban con su
    /// compromiso. Lo que ya tuvo uno —aunque esté cerrado o cancelado— se respeta: alguien lo cerró
    /// a propósito y resucitarlo sería deshacer esa decisión.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> MaterializarMisAsignadosAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(usuario, Ambito);

        if (usuario.DeveloperId is not int developerId)
            return (false, "Tu cuenta no tiene ficha de desarrollador ligada, así que no se puede saber " +
                           "qué tickets son tuyos. Pídeselo al líder.");

        var dev = await db.Developers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == developerId, ct);
        if (dev == null) return (false, "No se encontró tu ficha de desarrollador.");

        var tickets = (await DevOpsTicketQuery.DeDesarrolladorAsync(db, dev, ct))
            .Where(t => !EsCerrado(t.State))
            .ToList();

        if (tickets.Count == 0)
            return (true, "No hay tickets de Azure DevOps abiertos a tu nombre entre los sincronizados. " +
                          "Sincroniza los tuyos si esperabas alguno.");

        var identificadores = tickets.Select(t => t.ExternalId.ToString()).ToList();
        var existentes = await db.Requirements
            .Where(r => r.Source == RequirementSource.AzureDevOps
                        && r.ExternalId != null && identificadores.Contains(r.ExternalId))
            .ToDictionaryAsync(r => r.ExternalId!, ct);

        var yaAsignados = (await db.Assignments.AsNoTracking()
                .Where(a => a.DeveloperId == developerId)
                .Select(a => a.RequirementId)
                .ToListAsync(ct))
            .ToHashSet();

        var ahora = DateTime.UtcNow;
        int nuevos = 0, actualizados = 0;
        var materializados = new List<(Requirement requerimiento, DevOpsTicket ticket)>();

        foreach (var ticket in tickets)
        {
            ct.ThrowIfCancellationRequested();
            var identificador = ticket.ExternalId.ToString();

            if (existentes.TryGetValue(identificador, out var requerimiento))
            {
                requerimiento.Title = ticket.Title;
                requerimiento.ExternalUrl = ticket.Url;

                if (!yaAsignados.Contains(requerimiento.Id))
                    db.Assignments.Add(new Assignment
                    {
                        RequirementId = requerimiento.Id, DeveloperId = developerId, AssignedAt = ahora
                    });

                actualizados++;
            }
            else
            {
                requerimiento = new Requirement
                {
                    Title = ticket.Title,
                    Description = ticket.Description,
                    Status = MapearEstado(ticket.State),
                    Priority = RequirementPriority.Media,
                    Source = RequirementSource.AzureDevOps,
                    ExternalId = identificador,
                    ExternalUrl = ticket.Url,
                    CreatedAt = ahora,
                    StatusChangedAt = ahora
                };
                db.Requirements.Add(requerimiento);
                db.Assignments.Add(new Assignment
                {
                    Requirement = requerimiento, DeveloperId = developerId, AssignedAt = ahora
                });

                nuevos++;
            }

            materializados.Add((requerimiento, ticket));
        }

        // Se guarda ANTES de los compromisos: los requerimientos recién dados de alta necesitan su
        // identificador real para que el SLA pueda apuntar a ellos.
        await db.SaveChangesAsync(ct);

        int compromisos = await PonerSlaAutomaticoAsync(materializados, developerId, ahora, ct);

        await bitacora.RecordAsync(AuditAction.Create, "Requirement", null,
            $"Tickets de DevOps traídos a las asignaciones propias: {nuevos} nuevo(s), " +
            $"{actualizados} ya existente(s), {compromisos} SLA automático(s)", ct);

        var slas = compromisos == 0 ? "" : $" Se les puso SLA automático a {compromisos}.";

        return (true, nuevos == 0
            ? $"No hay tickets nuevos de DevOps a tu nombre; los {actualizados} que hay ya estaban en " +
              $"tus asignaciones.{slas}"
            : $"Se trajeron {nuevos} ticket(s) de DevOps a tus asignaciones.{slas}");
    }

    // ── SLA automático por prioridad ─────────────────────────────────────────────

    /// <summary>
    /// Crea el compromiso que le toca a cada requerimiento recién materializado según la prioridad
    /// de su ticket. Devuelve cuántos creó.
    ///
    /// Solo se le pone a lo que NUNCA tuvo compromiso: uno cumplido o cancelado ya tiene su historia
    /// y volver a abrirlo por una pasada de mantenimiento reabriría discusiones ya cerradas.
    ///
    /// <para>El compromiso lo arma <see cref="ReconciliacionDePrioridadDeDevOps"/>, que es quien
    /// tiene la regla del SLA que dicta una prioridad: el que se pone aquí y el que se reajusta al
    /// cambiar la prioridad tienen que ser el mismo compromiso, no dos parecidos.</para>
    /// </summary>
    private async Task<int> PonerSlaAutomaticoAsync(
        IReadOnlyList<(Requirement requerimiento, DevOpsTicket ticket)> materializados,
        int developerId, DateTime ahora, CancellationToken ct)
    {
        var politicas = await ReconciliacionDePrioridadDeDevOps.PoliticasAsync(configuracion, ct);
        if (!politicas.Any(p => p.Enabled)) return 0;

        var yaTuvieron = (await db.SlaCommitments.AsNoTracking()
                .Where(s => s.RequirementId != null)
                .Select(s => s.RequirementId!.Value)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        int creados = 0;
        foreach (var (requerimiento, ticket) in materializados)
        {
            if (yaTuvieron.Contains(requerimiento.Id)) continue;
            if (SlaPolicyStore.Resolver(politicas, ticket.Priority) is not SlaPolicy politica) continue;

            ReconciliacionDePrioridadDeDevOps.AgregarSlaAutomatico(
                db, requerimiento.Id, ticket, politica, developerId, ahora);
            creados++;
        }

        if (creados > 0) await db.SaveChangesAsync(ct);
        return creados;
    }

    // ── Reporte de tiempo cronometrado ───────────────────────────────────────────

    /// <summary>
    /// Registra en el ticket de DevOps el tiempo cronometrado de un requerimiento.
    ///
    /// <para>Envía SOLO el delta aún no reportado, así que repetir la operación no duplica horas. No
    /// tiene endpoint propio a propósito: lo dispara el cronómetro al detenerse, y esa ruta pertenece
    /// a la pantalla de jornada. Aquí vive porque es una operación contra la integración y su regla
    /// —la marca de agua— es de las que, mal escritas, meten horas de más en el ticket de alguien.</para>
    /// </summary>
    /// <returns>
    /// <c>intentado</c> dice si había algo que reportar y la configuración lo permitía; <c>ok</c>, si
    /// se registró.
    /// </returns>
    public async Task<(bool intentado, bool ok, string mensaje)> ReportarTiempoAsync(
        int requerimientoId, int totalSegundos, CancellationToken ct = default)
    {
        if (!await configuracion.ObtenerBooleanoAsync(DevOpsTimeReport.ClaveHabilitado, ct))
            return (false, false, "");

        var info = await db.Requirements.AsNoTracking()
            .Where(r => r.Id == requerimientoId)
            .Select(r => new { r.Source, r.ExternalId, r.DevOpsReportedSeconds })
            .FirstOrDefaultAsync(ct);

        if (info == null) return (false, false, "");
        if (!DevOpsTimeReport.AplicaA(info.Source, info.ExternalId, out var numero)) return (false, false, "");

        int reportado = info.DevOpsReportedSeconds;
        int deltaSegundos = DevOpsTimeReport.DeltaSegundos(totalSegundos, reportado);
        double horasDelta = DevOpsTimeReport.SegundosAHoras(deltaSegundos);
        if (deltaSegundos <= 0 || horasDelta <= 0) return (false, false, "");   // nada nuevo que reportar

        var (credenciales, problema) = await CredencialesAsync(exigirPropio: false, ct: ct);
        if (credenciales is null) return (true, false, problema);

        var modo = DevOpsTimeReport.LeerModo(await configuracion.ObtenerAsync(DevOpsTimeReport.ClaveModo, ct));
        bool reducir = await configuracion.ObtenerBooleanoAsync(DevOpsTimeReport.ClaveReducirRestante, ct);

        bool camposOk = false, comentarioOk = false;
        var partes = new List<string>();
        string? errorTransitorio = null;

        // 1) Campos de trabajo. Son ADITIVOS y por tanto no idempotentes: en cuanto la escritura tiene
        //    éxito se avanza la marca de agua de inmediato —y SOLO por las horas realmente enviadas,
        //    redondeadas, arrastrando el resto— para que ni un fallo posterior ni el redondeo hagan que
        //    se vuelvan a sumar las mismas horas.
        if (DevOpsTimeReport.ActualizaCampos(modo))
        {
            try
            {
                camposOk = await devops.SumarTrabajoCompletadoAsync(credenciales, numero, horasDelta, reducir, ct);
                if (camposOk)
                {
                    await MarcarReportadoAsync(
                        requerimientoId, reportado + DevOpsTimeReport.HorasASegundos(horasDelta), ct);
                    partes.Add($"Completed Work +{horasDelta:0.##} h");
                }
                else
                {
                    partes.Add("este tipo de work item no admite horas de trabajo");
                }
            }
            catch (ErrorDeAzureDevOps ex) { errorTransitorio = ex.Message; }
        }

        // 2) Comentario, que reporta el delta EXACTO. Solo si los campos no fallaron por red: con
        //    DevOps caído, intentarlo otra vez solo alarga la espera.
        if (errorTransitorio == null && DevOpsTimeReport.Comenta(modo))
        {
            try
            {
                var texto = $"⏱ Tiempo registrado desde la aplicación: +{WorkSessionService.Format(deltaSegundos)} " +
                            $"(total dedicado: {WorkSessionService.Format(totalSegundos)}).";

                await devops.PublicarComentarioAsync(credenciales, numero, WebUtility.HtmlEncode(texto), ct);
                comentarioOk = true;

                // El comentario cubre el delta exacto, sin redondeo que arrastrar.
                if (!camposOk) await MarcarReportadoAsync(requerimientoId, totalSegundos, ct);
                partes.Add("comentario publicado");
            }
            catch (ErrorDeAzureDevOps ex) { errorTransitorio = ex.Message; }
        }

        bool algoOk = camposOk || comentarioOk;
        if (algoOk)
            await bitacora.RecordAsync(AuditAction.Update, "DevOpsTicket", numero.ToString(),
                $"Tiempo reportado a DevOps (+{WorkSessionService.Format(deltaSegundos)})", ct);

        if (errorTransitorio != null)
            return (true, algoOk, algoOk
                ? $"{string.Join("; ", partes)} (el resto falló: {errorTransitorio})"
                : errorTransitorio);

        return (true, algoOk, string.Join("; ", partes));
    }

    /// <summary>
    /// Avanza la marca de agua de segundos ya reportados con una actualización directa, FUERA del
    /// seguimiento de entidades.
    ///
    /// Así no la bloquea un choque de concurrencia sobre el requerimiento —que lleva sello— ni queda
    /// pendiente de otras entidades del contexto. Se reduce al mínimo la ventana en la que DevOps
    /// podría ir por delante de lo que aquí consta como reportado, que es la ventana en la que se
    /// duplican horas.
    /// </summary>
    private Task MarcarReportadoAsync(int requerimientoId, int segundos, CancellationToken ct) =>
        db.Requirements
            .Where(r => r.Id == requerimientoId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.DevOpsReportedSeconds, segundos), ct);

    // ── Credenciales ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Con qué se habla con DevOps en nombre de quien hace la petición.
    ///
    /// <para>El token es PERSONAL y manda el propio: así los comentarios y los cambios quedan firmados
    /// en DevOps por quien los hizo, que es justo lo que un compromiso de servicio necesita poder
    /// probar. Solo si no lo hay se cae al de la instalación, igual que en el escritorio.</para>
    ///
    /// <para>Con <paramref name="exigirPropio"/> no se cae al de la instalación. Lo exigen las
    /// operaciones que se resuelven contra el DUEÑO del token (la macro <c>@Me</c>): con el compartido
    /// traerían los tickets de otra cuenta sin avisar de nada.</para>
    ///
    /// <para>Devuelve <c>null</c> y un motivo entendible en vez de lanzar: sin token no hay defecto
    /// que reportar, hay algo que la persona tiene que hacer.</para>
    /// </summary>
    /// <summary>
    /// Organización, proyecto y el token de la INSTALACIÓN, sin pasar por la tabla de secretos.
    ///
    /// <para>Existe para lo que corre sin sesión. <see cref="CredencialesAsync"/> mira primero el
    /// token personal de quien pide, y esa consulta lleva su propia guarda de «hay que estar
    /// dentro»: desde un trabajo de fondo lanza antes de llegar a ninguna parte.</para>
    /// </summary>
    private Task<(CredencialesDevOps? credenciales, string problema)> CredencialesDeLaInstalacionAsync(
        CancellationToken ct) => CredencialesDeLaInstalacion.ObtenerAsync(configuracion, ct);

    /// <summary>
    /// Las credenciales de QUIEN PREGUNTA: su token personal si lo tiene guardado, y el de la
    /// instalación si no. Nulo cuando no hay ninguno de los dos o falta la organización.
    ///
    /// <para><b>Existe para que el aviso de inicio del cronómetro firme con la misma cuenta que el
    /// reporte de tiempo al detenerlo.</b> Antes no lo hacía: al arrancar comentaba
    /// <c>AvisoDeInicioEnDevOpsService</c>, que resuelve las credenciales de la INSTALACIÓN porque a
    /// él lo llama también un barrido de fondo sin sesión, y al detener comentaba
    /// <c>ReportarTiempoAsync</c>, que va por el token personal. El resultado, visto en el ticket:
    /// «empezó a trabajar» firmado por la cuenta compartida y «tiempo registrado» firmado por la
    /// persona. Dos cuentas para las dos puntas del mismo cronómetro.</para>
    ///
    /// <para>La solución no es quitarle el barrido al aviso —sin él se perderían los cronómetros que
    /// arrancan en el escritorio— sino <b>invertir la dependencia</b>: el aviso RECIBE las
    /// credenciales, el endpoint le pasa éstas y el barrido le pasa nulo y cae a las de la
    /// instalación, que es el único caso en que no hay alternativa.</para>
    ///
    /// <para>No lleva guarda de rol: no la lleva ninguna de las lecturas de credenciales de esta
    /// clase, y la que sí importa la aplica <c>ObtenerMioEnClaroAsync</c>, que exige sesión para
    /// leer un secreto — así que sin sesión esto no puede devolver el token de nadie.</para>
    /// </summary>
    public async Task<CredencialesDevOps?> CredencialesDeQuienPreguntaAsync(CancellationToken ct = default) =>
        (await CredencialesAsync(exigirPropio: false, ct: ct)).credenciales;

    private async Task<(CredencialesDevOps? credenciales, string problema)> CredencialesAsync(
        bool exigirPropio, string? patCandidato = null, CancellationToken ct = default)
    {
        var organizacion = (await configuracion.ObtenerAsync(SettingsService.Claves.AzureDevOpsOrgUrl, ct))?.TrimEnd('/');
        var proyecto = await configuracion.ObtenerAsync(SettingsService.Claves.AzureDevOpsProject, ct);

        if (string.IsNullOrEmpty(organizacion) || string.IsNullOrEmpty(proyecto))
            return (null, "Falta la URL de organización o el proyecto de Azure DevOps. Pídeselo al líder.");

        // El candidato solo existe cuando se está probando un token recién escrito: no se guarda.
        var pat = string.IsNullOrWhiteSpace(patCandidato)
            ? await secretos.ObtenerMioEnClaroAsync(PropositosDeSecreto.PatDevOps, ct)
            : patCandidato.Trim();

        if (string.IsNullOrEmpty(pat) && !exigirPropio)
            pat = await configuracion.ObtenerAsync(SettingsService.Claves.AzureDevOpsPat, ct);

        if (string.IsNullOrEmpty(pat))
            return (null, exigirPropio
                ? "Necesitas tu propio token de Azure DevOps para traer lo tuyo: la consulta se resuelve " +
                  "contra la cuenta del token. Captúralo en «Mi token de DevOps»."
                : "No hay ningún token de Azure DevOps configurado. Captura el tuyo en «Mi token de DevOps».");

        return (new CredencialesDevOps(organizacion, proyecto, pat), "");
    }

    // ── Ayudas ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// El ticket sobre el que se va a operar, si quien lo pide puede hacerlo.
    ///
    /// El líder opera sobre cualquiera; el desarrollador, solo sobre lo que está a su nombre en
    /// DevOps. En el escritorio esa restricción la daba la interfaz —su pantalla solo listaba lo
    /// suyo—, y eso aquí no basta: a la API se la puede llamar sin pasar por la pantalla.
    /// </summary>
    private async Task<(DevOpsTicket? ticket, string motivo)> TicketOperableAsync(
        int numero, CancellationToken ct)
    {
        var ticket = await db.DevOpsTickets.FirstOrDefaultAsync(t => t.ExternalId == numero, ct);
        if (ticket == null) return (null, NoEstaSincronizado(numero));

        if (usuario.IsAdmin) return (ticket, "");

        if (usuario.DeveloperId is not int developerId)
            return (null, "Tu cuenta no tiene ficha de desarrollador ligada, así que no se puede saber " +
                          "qué tickets son tuyos. Pídeselo al líder.");

        var dev = await db.Developers.AsNoTracking().FirstOrDefaultAsync(d => d.Id == developerId, ct);
        if (dev == null) return (null, "No se encontró tu ficha de desarrollador.");

        return DevOpsIdentityMatcher.Corresponde(ticket.AssignedTo, ticket.AssignedToUniqueName, dev)
            ? (ticket, "")
            : (null, $"El ticket #{numero} no está a tu nombre en Azure DevOps.");
    }

    private static string NoEstaSincronizado(int numero) =>
        $"El ticket #{numero} no está sincronizado aquí. Sincroniza antes de operar sobre él.";

    private static ResultadoDeSincronizacionDto Fallo(string mensaje) =>
        new(false, mensaje, 0, 0, []);

    private static string? Vacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Quita el marcado de un comentario de DevOps. La limpieza vive en
    /// <see cref="TextoDeDevOps"/>, compartida con el pool: es la misma regla sobre el mismo HTML
    /// ajeno, y tres copias serían tres oportunidades de que una se quedara atrás.</summary>
    private static string ATextoPlano(string? html) => TextoDeDevOps.ATextoPlano(html);
}
