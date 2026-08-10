using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Dtos.Freshdesk;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// La sincronización de Freshdesk y la pantalla que la enseña.
///
/// <para><b>Por qué está aquí y no en la integración.</b> El cliente de Freshdesk
/// (<see cref="IApiDeFreshdesk"/>) vive en Infrastructure y solo habla HTTP. Todo lo que decide qué
/// se guarda necesita tres cosas que Infrastructure no puede alcanzar —la configuración
/// (<see cref="SettingsService"/>), la bitácora y los avisos—, porque Application depende de
/// Infrastructure y no al revés. Partirlo así deja además la parte que importa —la que decide qué se
/// borra— probable sin tocar la red.</para>
///
/// <para><b>La clave de API no sale de aquí.</b> Se lee de la configuración, se le pasa al cliente y
/// no aparece en ningún DTO, en ningún mensaje ni en la bitácora. Lo que el navegador puede saber es
/// si la integración está utilizable y, si no, qué falta.</para>
/// </summary>
public class TicketsDeFreshdeskService(
    AppDbContext db,
    ICurrentUser quien,
    AuditService bitacora,
    SettingsService configuracion,
    IApiDeFreshdesk api,
    // Opcional a propósito: las pruebas comprueban que el aviso se CREA, y montar el envío push
    // solo para eso las volvería frágiles sin probar nada más. En la aplicación siempre viene.
    NotificationService? avisos = null)
{
    /// <summary>
    /// Cuántas filas viajan como mucho a la pantalla. La rejilla se lee de a veinte y el histórico
    /// crece sin tope; mandar la tabla entera con la descripción de cada ticket serían megabytes por
    /// recarga. Cuando se recorta, la pantalla lo dice y la búsqueda —que ocurre en el SERVIDOR—
    /// sigue mirando todos los tickets, no solo los que se enviaron la vez anterior.
    /// </summary>
    private const int TopeDeFilas = 300;

    /// <summary>
    /// Cuántos identificadores caben en una condición «IN (…)». SQL Server no admite más de ~2100
    /// parámetros por consulta, y la reconciliación puede traer miles de números de ticket.
    /// </summary>
    private const int TamanoDeLote = 500;

    // ── Pantalla ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lo que enseña la pantalla de Freshdesk: el estado de la integración, el filtro con el que se
    /// sincroniza, el resumen por estado y los tickets que cumplen la búsqueda.
    ///
    /// <para>La búsqueda se resuelve en el SERVIDOR, al contrario que en el escritorio, donde la
    /// rejilla filtraba en memoria sobre la tabla completa ya cargada. Aquí esa tabla tendría que
    /// viajar entera al navegador para poder filtrarla allí, y eso es exactamente lo que no se puede
    /// permitir. A cambio, buscar cuesta un viaje de red.</para>
    /// </summary>
    /// <param name="buscar">Texto libre: asunto, agente, solicitante, correo, tipo o etiquetas. Si es
    /// un número, se entiende como el número del ticket.</param>
    public async Task<PantallaDeFreshdeskDto> PantallaAsync(
        string? buscar = null, int? estado = null, int? prioridad = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var (opciones, motivo) = await OpcionesAsync(ct);
        var filtro = await LeerFiltroAsync(ct);
        var resumen = await ResumenAsync(ct);
        // El SELLO manda sobre el cálculo por tickets. Deducirlo de MAX(SyncedAt) mentía en dos
        // casos reales: una sincronización que no trajo ningún ticket dejaba la fecha vieja, y
        // borrar los tickets del filtro la borraba entera. El cálculo se conserva de respaldo para
        // las bases que ya sincronizaban antes de que existiera el sello.
        var ultima = await configuracion.UltimaSincronizacionAsync(
                         SettingsService.Claves.UltimaSincronizacionFreshdesk, ct)
                  ?? await db.FreshDeskTickets.AsNoTracking()
                         .OrderByDescending(t => t.SyncedAt)
                         .Select(t => (DateTime?)t.SyncedAt)
                         .FirstOrDefaultAsync(ct);

        var consulta = Filtrar(db.FreshDeskTickets.AsNoTracking(), buscar, estado, prioridad);
        var coincidencias = await consulta.CountAsync(ct);

        var filas = await consulta
            .OrderByDescending(t => t.UpdatedAtExternal)
            .Take(TopeDeFilas)
            .ToListAsync(ct);

        var vinculos = await VinculosPorTicketAsync(filas.Select(t => t.Id).ToList(), ct);

        return new PantallaDeFreshdeskDto(
            Habilitada: opciones != null,
            MotivoDeNoDisponible: motivo,
            filtro,
            resumen,
            ultima,
            coincidencias,
            Truncada: coincidencias > filas.Count,
            filas.Select(t => AVista(t, vinculos.GetValueOrDefault(t.Id))).ToList());
    }

    /// <summary>El filtro de sincronización guardado. Vacío es «sin grupo», no «grupo llamado ""».</summary>
    public async Task<FiltroDeSincronizacionDto> LeerFiltroAsync(CancellationToken ct = default) => new(
        await configuracion.ObtenerBooleanoAsync(SettingsService.Claves.FreshDeskFilterMine, ct),
        (await configuracion.ObtenerAsync(SettingsService.Claves.FreshDeskGroup, ct) ?? "").Trim());

    /// <summary>
    /// Guarda el filtro de sincronización. Se persiste en la configuración compartida —y no en una
    /// preferencia de quien mira la pantalla— porque decide qué tickets se GUARDAN, no cómo se ven:
    /// es una decisión de la integración, la misma para todos.
    /// </summary>
    public async Task<(bool ok, string mensaje)> GuardarFiltroAsync(
        bool porAgente, string? grupo, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var (ok, mensaje) = await configuracion.GuardarAsync(
            SettingsService.Claves.FreshDeskFilterMine, porAgente ? "true" : "false", ct);
        if (!ok) return (false, mensaje);

        (ok, mensaje) = await configuracion.GuardarAsync(
            SettingsService.Claves.FreshDeskGroup, (grupo ?? "").Trim(), ct);
        if (!ok) return (false, mensaje);

        return (true, "Filtro de sincronización guardado.");
    }

    // ── Sincronización ───────────────────────────────────────────────────────────

    /// <summary>
    /// Trae los tickets de Freshdesk y los deja al día en esta base.
    ///
    /// <para>Un fallo de la integración —clave rechazada, sin salida a internet, Freshdesk caído— se
    /// devuelve como <c>(false, mensaje)</c> y no como excepción: el endpoint lo convierte en un 400
    /// con ese texto, que explica qué pasó y qué hacer. Un 500 aquí solo diría «algo salió mal».</para>
    /// </summary>
    public async Task<(bool ok, string mensaje, ResultadoDeSincronizacionDto? resultado)> SincronizarAsync(
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(quien);

        var (opciones, motivo) = await OpcionesAsync(ct);
        if (opciones is null) return (false, motivo!, null);

        var filtro = await LeerFiltroAsync(ct);

        try
        {
            // «Mi» agente se resuelve SIEMPRE, aunque el filtro por agente esté apagado: también es lo
            // que permite avisar de lo que me acaban de asignar. En el escritorio pasaba lo mismo, con
            // la diferencia de que allí una de las dos cosas la hacía el temporizador de fondo.
            var miAgenteId = await api.MiAgenteIdAsync(opciones, ct);

            // Los tickets traen ids de agente y de grupo, no nombres. Los nombres salen de estos dos
            // catálogos, que con una clave de AGENTE responden 403 (ver IApiDeFreshdesk): en ese caso
            // llegan vacíos y se sincroniza igual, con los nombres en blanco. Los ids —que es lo que
            // el filtro necesita— se guardan siempre.
            var agentes = await api.AgentesAsync(opciones, ct);
            var grupos = await api.GruposAsync(opciones, ct);

            bool grupoConfigurado = !string.IsNullOrWhiteSpace(filtro.Grupo);
            long? grupoDelFiltro = grupoConfigurado ? ResolverGrupoId(filtro.Grupo, grupos) : null;
            // Mi agente cuenta para el filtro SOLO si el criterio «asignados a mí» está encendido.
            long? miParaElFiltro = filtro.PorAgente ? miAgenteId : null;

            // Números VISTOS en esta corrida que NO cumplen el filtro: los únicos candidatos a quitar.
            var noCoinciden = new HashSet<long>();
            int nuevos = 0, actualizados = 0;
            var ahora = DateTime.UtcNow;

            for (int pagina = 1; ; pagina++)
            {
                ct.ThrowIfCancellationRequested();

                var lote = await api.TicketsAsync(opciones, pagina, ct);
                if (lote.Tickets.Count == 0) break;

                var aplicables = new List<TicketDeFreshdeskCrudo>();
                foreach (var ticket in lote.Tickets)
                {
                    if (filtro.Activo && !EsMio(ticket.AgenteId, ticket.GrupoId, miParaElFiltro, grupoDelFiltro))
                    {
                        noCoinciden.Add(ticket.Numero);
                        continue;
                    }
                    aplicables.Add(ticket);
                }

                if (aplicables.Count > 0)
                {
                    // Una consulta por PÁGINA y no una por ticket. En el escritorio era un
                    // FirstOrDefault por ticket contra una base local, que salía gratis; contra un SQL
                    // Server remoto serían cien viajes de red por página.
                    var numeros = aplicables.Select(t => t.Numero).ToList();
                    var existentes = await db.FreshDeskTickets
                        .Where(t => numeros.Contains(t.ExternalId))
                        .ToDictionaryAsync(t => t.ExternalId, ct);

                    foreach (var ticket in aplicables)
                    {
                        if (existentes.TryGetValue(ticket.Numero, out var fila))
                        {
                            actualizados++;
                        }
                        else
                        {
                            fila = new FreshDeskTicket
                            {
                                ExternalId = ticket.Numero,
                                CreatedAtExternal = ticket.CreadoUtc
                            };
                            db.FreshDeskTickets.Add(fila);
                            nuevos++;
                        }

                        Volcar(fila, ticket, agentes, grupos, opciones, ahora);
                    }

                    await db.SaveChangesAsync(ct);
                }

                if (!lote.HayMas) break;
            }

            // ⚠ NUNCA se borra por ausencia. El endpoint /tickets solo devuelve los de los últimos
            // ~30 días: un ticket viejo que sigue siendo mío simplemente no viene en la respuesta, y
            // tomar eso por «lo borraron» se llevaría por delante todo el histórico anterior al mes en
            // curso. Solo se quitan los que SÍ se vieron en esta corrida y se confirmó que ya no
            // cumplen el filtro, y únicamente cuando el filtro se pudo resolver ENTERO: si no se supo
            // quién soy o no se resolvió el grupo, no se quita nada, porque «no cumple» sería una
            // conclusión sacada de datos incompletos.
            bool filtroCompleto = filtro.Activo
                && (!filtro.PorAgente || miAgenteId != null)
                && (!grupoConfigurado || grupoDelFiltro != null);
            int quitados = filtroCompleto ? await ReconciliarAsync(noCoinciden, ct) : 0;

            await bitacora.RecordAsync(AuditAction.Create, "FreshDeskTicket", null,
                $"Sync Freshdesk: {nuevos} nuevos, {actualizados} actualizados, {quitados} quitados", ct);

            // Los avisos de «te asignaron este ticket» los disparaba en el escritorio el temporizador
            // de fondo. Aquí los trabajos de fondo siguen apagados hasta el corte, así que los cuelga
            // la propia sincronización: es el mismo cálculo y la misma línea base —la primera vez para
            // cada (usuario, agente) no avisa de nada— que evitaba el aluvión del backlog.
            if (quien.UserId is int userId && miAgenteId is long agente)
                await DetectarAsignadosNuevosAsync(userId, agente, ct);

            // El sello se escribe SOLO al terminar bien: marcarlo al empezar dejaría la pantalla
            // diciendo que los datos están al día después de una sincronización que reventó.
            await configuracion.MarcarSincronizacionAsync(
                SettingsService.Claves.UltimaSincronizacionFreshdesk, DateTime.UtcNow, ct);

            var mensaje = $"Sincronización completada: {nuevos} nuevos, {actualizados} actualizados" +
                          (quitados > 0 ? $", {quitados} quitados (ya no cumplen el filtro)" : "") + ".";

            return (true, mensaje, new ResultadoDeSincronizacionDto(
                nuevos, actualizados, quitados, mensaje,
                Aviso(filtro, miAgenteId, grupoConfigurado, grupoDelFiltro)));
        }
        catch (ErrorDeFreshdeskException ex)
        {
            // El mensaje lo escribió la integración y explica el motivo concreto; se enseña tal cual.
            return (false, ex.Message, null);
        }
    }

    /// <summary>
    /// Quita los tickets cuyo número está en <paramref name="aQuitar"/> —los que se CONFIRMÓ que ya
    /// no cumplen el filtro—, salvo los que tengan un vínculo con DevOps: ese enlace lo hizo una
    /// persona a mano y borrarlo sería tirar trabajo suyo. Devuelve cuántos se quitaron.
    /// </summary>
    private async Task<int> ReconciliarAsync(ISet<long> aQuitar, CancellationToken ct = default)
    {
        if (aQuitar.Count == 0) return 0;

        var vinculados = (await db.TicketLinks.AsNoTracking()
            .Select(l => l.FreshDeskTicketId)
            .Distinct()
            .ToListAsync(ct)).ToHashSet();

        var sobran = new List<FreshDeskTicket>();
        foreach (var lote in aQuitar.Chunk(TamanoDeLote))
        {
            var numeros = lote.ToList();
            sobran.AddRange(await db.FreshDeskTickets
                .Where(t => numeros.Contains(t.ExternalId))
                .ToListAsync(ct));
        }

        var borrables = sobran.Where(t => !vinculados.Contains(t.Id)).ToList();
        if (borrables.Count > 0)
        {
            db.FreshDeskTickets.RemoveRange(borrables);
            await db.SaveChangesAsync(ct);
        }
        return borrables.Count;
    }

    // ── Las tres decisiones del filtro ───────────────────────────────────────────
    //
    // Estáticas y públicas por el mismo motivo que en el escritorio: son cálculo puro, deciden qué se
    // guarda y qué se quita, y así se pueden probar una por una sin tocar la red ni la base.

    /// <summary>Un ticket «es mío» si está asignado a mi agente o pertenece al grupo del filtro.</summary>
    public static bool EsMio(long? agenteId, long? grupoId, long? miAgenteId, long? grupoDelFiltro) =>
        (miAgenteId != null && agenteId == miAgenteId) ||
        (grupoDelFiltro != null && grupoId == grupoDelFiltro);

    /// <summary>
    /// Resuelve el grupo del filtro a un ID. Primero por NOMBRE (así un grupo llamado «2024» se
    /// resuelve bien y no se confunde con un ID). Si no, se interpreta como ID numérico: cuando se
    /// pudo listar /groups, se VALIDA que exista —si no, devuelve null para no filtrar contra un ID
    /// fantasma—; cuando /groups respondió 403 y el mapa llegó vacío, se confía en lo que capturó el
    /// líder, que es su única salida en ese caso.
    /// </summary>
    public static long? ResolverGrupoId(string grupoCfg, IReadOnlyDictionary<long, string> grupos)
    {
        var texto = (grupoCfg ?? "").Trim();
        if (texto.Length == 0) return null;

        foreach (var par in grupos)                                   // 1) por nombre (tiene prioridad)
            if (string.Equals(par.Value, texto, StringComparison.OrdinalIgnoreCase)) return par.Key;

        if (long.TryParse(texto, out var id))                         // 2) por ID numérico
        {
            if (grupos.Count > 0 && !grupos.ContainsKey(id)) return null;   // se pudo validar y no existe
            return id;                                                      // mapa vacío (403): se confía
        }
        return null;
    }

    /// <summary>
    /// El aviso de cuando el filtro no se pudo aplicar del todo. No es un error —la sincronización
    /// ocurrió— pero cambia lo que se está viendo, y callarlo dejaría al líder creyendo que su filtro
    /// se respetó.
    /// </summary>
    public static string? Aviso(
        FiltroDeSincronizacionDto filtro, long? miAgenteId, bool grupoConfigurado, long? grupoDelFiltro)
    {
        if (!filtro.Activo) return null;

        bool fallaElAgente = filtro.PorAgente && miAgenteId == null;
        bool fallaElGrupo = grupoConfigurado && grupoDelFiltro == null;

        if (fallaElAgente && fallaElGrupo)
            return "No se pudo aplicar el filtro: no se identificó tu agente (/agents/me) ni el grupo. " +
                   "Revisa la clave de API, o escribe el ID numérico del grupo.";
        if (fallaElGrupo)
            return $"No se encontró el grupo «{filtro.Grupo}». Si tu clave de API no puede listar grupos, " +
                   "escribe su ID numérico." +
                   (filtro.PorAgente ? " Por ahora se filtró solo por tus asignados." : "");
        if (fallaElAgente)
            return "No se pudo identificar tu agente (/agents/me)" +
                   (grupoDelFiltro != null ? "; se filtró solo por el grupo." : ".");
        return null;
    }

    /// <summary>
    /// Crea un aviso por cada ticket ACTIVO (abierto o pendiente) que esté asignado a
    /// <paramref name="miAgenteId"/> y no se hubiera visto antes.
    ///
    /// <para>La línea base y el conjunto «visto» son POR (usuario, agente): la primera vez para ese
    /// agente se registra lo actual SIN avisar. Así se evita el aluvión del backlog en el primer
    /// arranque y también al rotar la clave de API a otro agente, que empieza con su propia base. Las
    /// filas de tickets que dejaron de estar asignados-y-activos se BORRAN, para que una reasignación
    /// posterior vuelva a avisar.</para>
    ///
    /// <para>Es público para que el trabajo de fondo pueda llamarlo cuando se encienda, y para poder
    /// probarlo sin tocar la red.</para>
    /// </summary>
    /// <returns>Cuántos avisos se crearon.</returns>
    public async Task<int> DetectarAsignadosNuevosAsync(
        int userId, long miAgenteId, CancellationToken ct = default)
    {
        var mios = await db.FreshDeskTickets.AsNoTracking()
            .Where(t => t.ResponderId == miAgenteId && (t.Status == 2 || t.Status == 3))
            .Select(t => new { t.ExternalId, t.Subject, t.Url })
            .ToListAsync(ct);
        var miosIds = mios.Select(t => t.ExternalId).ToHashSet();

        var vistos = await db.FreshDeskAssignmentsSeen
            .Where(s => s.UserId == userId && s.AgentId == miAgenteId)
            .ToListAsync(ct);

        // Línea base: la PRIMERA vez para este (usuario, agente) se registra todo lo actual sin avisar.
        // El centinela con ExternalId = 0 marca «base establecida» aunque no haya nada asignado; sin él,
        // la siguiente corrida volvería a creer que es la primera y no avisaría nunca.
        if (vistos.Count == 0)
        {
            db.FreshDeskAssignmentsSeen.Add(new FreshDeskAssignmentSeen
            {
                UserId = userId, AgentId = miAgenteId, ExternalId = 0
            });
            foreach (var numero in miosIds)
                db.FreshDeskAssignmentsSeen.Add(new FreshDeskAssignmentSeen
                {
                    UserId = userId, AgentId = miAgenteId, ExternalId = numero
                });

            await db.SaveChangesAsync(ct);
            return 0;
        }

        // Poda: lo que ya NO está asignado-y-activo deja de estar «visto» (menos el centinela), para
        // que si me lo reasignan más tarde vuelva a avisar.
        var aQuitar = vistos.Where(s => s.ExternalId != 0 && !miosIds.Contains(s.ExternalId)).ToList();
        if (aQuitar.Count > 0) db.FreshDeskAssignmentsSeen.RemoveRange(aQuitar);

        var yaVistos = vistos
            .Where(s => s.ExternalId != 0 && miosIds.Contains(s.ExternalId))
            .Select(s => s.ExternalId)
            .ToHashSet();
        var pendientes = mios.Where(t => !yaVistos.Contains(t.ExternalId)).ToList();

        // El aviso y su marca de «ya lo vi» se guardan JUNTOS a propósito: si se partieran, un fallo
        // a media lista dejaría tickets marcados como avisados sin haber avisado, y ese aviso ya no
        // volvería a salir nunca. Por eso esto no pasa por NotifyAsync, que guarda uno a uno.
        var nuevosAvisos = new List<Notification>();
        foreach (var ticket in pendientes)
        {
            db.FreshDeskAssignmentsSeen.Add(new FreshDeskAssignmentSeen
            {
                UserId = userId, AgentId = miAgenteId, ExternalId = ticket.ExternalId
            });
            var aviso = new Notification
            {
                ForUserId = userId,
                Kind = NotificationKind.FreshDeskAssigned,
                Title = $"Te asignaron el ticket de Freshdesk #{ticket.ExternalId}",
                Message = string.IsNullOrWhiteSpace(ticket.Subject) ? "(sin asunto)" : ticket.Subject,
                Url = ticket.Url,
                CreatedAt = DateTime.UtcNow
            };
            db.Notifications.Add(aviso);
            nuevosAvisos.Add(aviso);
        }

        if (pendientes.Count > 0 || aQuitar.Count > 0) await db.SaveChangesAsync(ct);

        // Y una vez a salvo en la base, el empujón al navegador.
        if (nuevosAvisos.Count > 0 && avisos != null)
            await avisos.EmpujarGuardadosAsync(nuevosAvisos, ct);

        return pendientes.Count;
    }

    // ── Interno ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Qué hace falta para hablar con Freshdesk, o por qué todavía no se puede.
    ///
    /// El motivo se escribe en palabras y nombrando la clave de configuración que falta. Nunca lleva
    /// el valor de ninguna: decir «la clave configurada es abc123» sería publicarla.
    /// </summary>
    private async Task<(OpcionesDeFreshdesk? opciones, string? motivo)> OpcionesAsync(CancellationToken ct)
    {
        if (!await configuracion.ObtenerBooleanoAsync(SettingsService.Claves.FreshDeskEnabled, ct))
            return (null, "La integración con Freshdesk está apagada. " +
                          "Enciéndela en Configuración (FreshDeskEnabled) cuando quieras usarla.");

        var dominio = await configuracion.ObtenerAsync(SettingsService.Claves.FreshDeskDomain, ct);
        if (string.IsNullOrWhiteSpace(dominio))
            return (null, "Falta el dominio de Freshdesk en Configuración (FreshDeskDomain): " +
                          "es el nombre de la cuenta, el trozo de «miempresa.freshdesk.com».");

        var clave = await configuracion.ObtenerAsync(SettingsService.Claves.FreshDeskApiKey, ct);
        if (string.IsNullOrWhiteSpace(clave))
            return (null, "Falta la clave de API de Freshdesk en Configuración (FreshDeskApiKey). " +
                          "Si ya la habías capturado desde el escritorio, vuelve a capturarla aquí: " +
                          "el cifrado viejo de Windows no se puede leer desde el servidor.");

        return (new OpcionesDeFreshdesk(dominio.Trim(), clave), null);
    }

    /// <summary>Deja la fila local igual a lo que acaba de devolver Freshdesk.</summary>
    private static void Volcar(
        FreshDeskTicket fila, TicketDeFreshdeskCrudo ticket,
        IReadOnlyDictionary<long, string> agentes, IReadOnlyDictionary<long, string> grupos,
        OpcionesDeFreshdesk opciones, DateTime ahora)
    {
        fila.Subject = ticket.Asunto;
        fila.Status = ticket.Estado;
        fila.Priority = ticket.Prioridad;
        fila.Type = ticket.Tipo;
        fila.Source = ticket.Fuente;
        fila.ResponderId = ticket.AgenteId;
        // El nombre solo se puede poner si el catálogo estuvo disponible; el ID va siempre, que es lo
        // que necesitan el filtro y los avisos de asignación.
        fila.AgentName = ticket.AgenteId is long agente && agentes.TryGetValue(agente, out var nombreAgente)
            ? nombreAgente : "";
        fila.GroupName = ticket.GrupoId is long grupo && grupos.TryGetValue(grupo, out var nombreGrupo)
            ? nombreGrupo : "";
        fila.RequesterName = ticket.Solicitante;
        fila.RequesterEmail = ticket.CorreoDelSolicitante;
        fila.Tags = ticket.Etiquetas;
        fila.Description = ticket.Descripcion;
        fila.UpdatedAtExternal = ticket.ActualizadoUtc;
        fila.SyncedAt = ahora;
        fila.Url = opciones.UrlDelTicket(ticket.Numero);
    }

    /// <summary>La búsqueda de la pantalla, traducida a la base.</summary>
    private static IQueryable<FreshDeskTicket> Filtrar(
        IQueryable<FreshDeskTicket> consulta, string? buscar, int? estado, int? prioridad)
    {
        if (estado is int e) consulta = consulta.Where(t => t.Status == e);
        if (prioridad is int p) consulta = consulta.Where(t => t.Priority == p);

        var texto = (buscar ?? "").Trim();
        if (texto.Length == 0) return consulta;

        // Un texto que es un número se entiende como el número del ticket: es como se piden entre
        // personas («¿viste el 4821?»). El escritorio además dejaba buscar trozos del número desde su
        // selector de campo; aquí se compara completo, porque convertir la columna a texto en cada
        // fila impediría a la base usar su índice y no valía ese precio.
        long? numero = long.TryParse(texto, out var n) ? n : null;

        return consulta.Where(t =>
            (numero != null && t.ExternalId == numero)
            || t.Subject.Contains(texto)
            || t.AgentName.Contains(texto)
            || t.RequesterName.Contains(texto)
            || t.RequesterEmail.Contains(texto)
            || t.Tags.Contains(texto)
            || (t.Type != null && t.Type.Contains(texto)));
    }

    /// <summary>Las tarjetas de arriba. Cuentan SIEMPRE sobre todos los tickets, no sobre lo filtrado:
    /// son el estado de la cola, y una que se moviera con cada búsqueda no serviría de referencia.</summary>
    private async Task<ResumenDeFreshdeskDto> ResumenAsync(CancellationToken ct)
    {
        var porEstado = await db.FreshDeskTickets.AsNoTracking()
            .GroupBy(t => t.Status)
            .Select(g => new { Estado = g.Key, Cuantos = g.Count() })
            .ToListAsync(ct);
        var urgentes = await db.FreshDeskTickets.AsNoTracking().CountAsync(t => t.Priority == 4, ct);

        int De(int estado) => porEstado.FirstOrDefault(x => x.Estado == estado)?.Cuantos ?? 0;

        return new ResumenDeFreshdeskDto(
            porEstado.Sum(x => x.Cuantos), De(2), De(3), De(4), De(5), urgentes);
    }

    /// <summary>Cuántos vínculos tiene cada ticket de la página, de una sola consulta agrupada.</summary>
    private async Task<Dictionary<int, int>> VinculosPorTicketAsync(
        IReadOnlyList<int> ticketIds, CancellationToken ct)
    {
        if (ticketIds.Count == 0) return [];

        var filas = await db.TicketLinks.AsNoTracking()
            .Where(l => ticketIds.Contains(l.FreshDeskTicketId))
            .GroupBy(l => l.FreshDeskTicketId)
            .Select(g => new { Ticket = g.Key, Cuantos = g.Count() })
            .ToListAsync(ct);

        return filas.ToDictionary(f => f.Ticket, f => f.Cuantos);
    }

    private static TicketDeFreshdeskDto AVista(FreshDeskTicket t, int vinculos) => new(
        t.Id,
        t.ExternalId,
        t.Subject,
        t.Status, FreshDeskTicket.StatusLabel(t.Status),
        t.Priority, FreshDeskTicket.PriorityLabel(t.Priority),
        t.Type,
        FreshDeskTicket.SourceLabel(t.Source),
        t.AgentName,
        t.GroupName,
        t.RequesterName,
        t.RequesterEmail,
        t.Tags,
        t.Description,
        t.CreatedAtExternal,
        t.UpdatedAtExternal,
        t.SyncedAt,
        t.Url,
        vinculos);
}
