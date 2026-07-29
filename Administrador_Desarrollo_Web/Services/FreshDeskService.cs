using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Resultado de un sync de Freshdesk (para la UI). <see cref="Aviso"/> != null si el filtro
/// no se pudo aplicar del todo.</summary>
public readonly record struct FreshDeskSyncResult(int Added, int Updated, int Removed, string? Aviso);

/// <summary>Filtro de sincronización de Freshdesk: por agente (asignados a mí) y/o por grupo. Está
/// activo si al menos uno de los dos criterios está puesto.</summary>
public readonly record struct FiltroFreshDesk(bool PorAgente, string Grupo)
{
    public bool Activo => PorAgente || !string.IsNullOrWhiteSpace(Grupo);
}

public class FreshDeskService
{
    private readonly SettingsService _settings;
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    /// <summary>Para abrir un contexto propio en la sincronización de segundo plano; el inyectado es
    /// singleton y lo comparte la UI (no es seguro tocarlo desde otro hilo).</summary>
    private readonly DbContextOptions<AppDbContext> _dbOptions;

    /// <summary>Serializa el sync MANUAL (UI, _db compartido) y el de SEGUNDO PLANO (contexto propio):
    /// nunca escriben en la BD a la vez, para no chocar (colisión de índice único / «database is locked»).</summary>
    private static readonly SemaphoreSlim _syncLock = new(1, 1);

    public FreshDeskService(SettingsService settings, AppDbContext db, AuditService audit,
        DbContextOptions<AppDbContext> dbOptions)
    {
        _settings = settings;
        _db = db;
        _audit = audit;
        _dbOptions = dbOptions;
    }

    public bool IsEnabled =>
        _settings.Get(SettingsService.Keys.FreshDeskEnabled) == "true"
        && !string.IsNullOrEmpty(_settings.Get(SettingsService.Keys.FreshDeskApiKey))
        && !string.IsNullOrEmpty(_settings.Get(SettingsService.Keys.FreshDeskDomain));

    // ── Sync completo a BD local (manual, desde el botón «Sincronizar») ────────────
    public async Task<FreshDeskSyncResult> SyncToLocalAsync(CancellationToken ct = default)
    {
        await _syncLock.WaitAsync(ct);
        try
        {
            var (domain, baseUrl, client) = BuildClient();
            using var _ = client;

            var filtro = LeerFiltro();
            long? miId = filtro.PorAgente ? await ObtenerMiAgenteIdAsync(baseUrl, client, ct) : null;
            var r = await SyncCoreAsync(_db, domain, baseUrl, client, filtro, miId, ct);

            _audit.Record(AuditAction.Create, "FreshDeskTicket", null,
                $"Sync Freshdesk: {r.Added} nuevos, {r.Updated} actualizados, {r.Removed} quitados");
            return r;
        }
        finally { _syncLock.Release(); }
    }

    /// <summary>
    /// Sincroniza y avisa al usuario de los tickets recién ASIGNADOS a él en Freshdesk. «Yo» se
    /// resuelve con /agents/me a partir de la API key configurada. La primera vez (por agente) fija
    /// una línea base SIN avisar (evita el aluvión del backlog). Devuelve cuántos avisos se crearon.
    ///
    /// La configuración se lee AQUÍ (en el hilo llamador, que comparte el _db singleton) y todo lo
    /// pesado —red + escritura con un contexto PROPIO— se ejecuta en un hilo de fondo con Task.Run,
    /// para no congelar la UI ni tocar el _db compartido desde otro hilo.
    /// </summary>
    public async Task<int> SincronizarYDetectarAsignadosAsync(int userId, CancellationToken ct = default)
    {
        if (!IsEnabled) return 0;
        var domain = _settings.Get(SettingsService.Keys.FreshDeskDomain)!.TrimEnd('/');
        var apiKey = _settings.Get(SettingsService.Keys.FreshDeskApiKey)!;
        var filtro = LeerFiltro();

        return await Task.Run(() => EjecutarEnSegundoPlanoAsync(userId, domain, apiKey, filtro, ct), ct);
    }

    /// <summary>Cuerpo de la sincronización de segundo plano: NO toca SettingsService ni el _db
    /// compartido; usa solo lo recibido y un <see cref="AppDbContext"/> propio. Corre en un hilo del
    /// pool (invocado con Task.Run).</summary>
    private async Task<int> EjecutarEnSegundoPlanoAsync(int userId, string domain, string apiKey, FiltroFreshDesk filtro, CancellationToken ct)
    {
        await _syncLock.WaitAsync(ct);
        try
        {
            var baseUrl = $"https://{domain}.freshdesk.com/api/v2";
            using var client = CrearHttpClient(apiKey);

            await using var db = new AppDbContext(_dbOptions);
            var miId = await ObtenerMiAgenteIdAsync(baseUrl, client, ct);
            await SyncCoreAsync(db, domain, baseUrl, client, filtro, miId, ct);

            if (miId is null) return 0;   // sin poder identificar «mi» agente no se sabe qué es «mío»
            return DetectarAsignadosNuevos(db, userId, miId.Value);
        }
        finally { _syncLock.Release(); }
    }

    /// <summary>Lee el filtro guardado (en el hilo llamador, no en el de fondo). Público para que la
    /// pantalla de Freshdesk muestre y edite el filtro sin depender de SettingsService.</summary>
    public FiltroFreshDesk LeerFiltro() => new(
        _settings.Get(SettingsService.Keys.FreshDeskFilterMine) == "true",
        _settings.Get(SettingsService.Keys.FreshDeskGroup) ?? "");

    /// <summary>Guarda el filtro (por agente / por grupo) desde la pantalla de Freshdesk.</summary>
    public void GuardarFiltro(bool porAgente, string? grupo)
    {
        _settings.Set(SettingsService.Keys.FreshDeskFilterMine, porAgente ? "true" : "false", false, "Freshdesk filtro: asignados a mí");
        _settings.Set(SettingsService.Keys.FreshDeskGroup, (grupo ?? "").Trim(), false, "Freshdesk filtro: grupo");
    }

    /// <summary>Núcleo del sync sobre un contexto dado y un dominio ya resuelto. Con el filtro activo,
    /// solo persiste los tickets asignados a mí (<paramref name="miAgenteId"/>) o al grupo configurado,
    /// y quita de la BD los que ya no aplican (salvo los vinculados a DevOps).</summary>
    private async Task<FreshDeskSyncResult> SyncCoreAsync(AppDbContext db, string domain, string baseUrl,
        HttpClient client, FiltroFreshDesk filtro, long? miAgenteId, CancellationToken ct)
    {
        int added = 0, updated = 0;
        int page = 1;
        var now  = DateTime.UtcNow;

        // Los tickets traen «responder_id»/«group_id» (ids), no nombres: éstos se resuelven contra
        // /agents y /groups, que requieren API key de ADMIN (con una de agente dan 403 y el nombre queda
        // vacío; el sync no se rompe). Los ids sí se guardan/usan siempre — es lo que necesita el filtro.
        var agentes = await CargarAgentesAsync(baseUrl, client, ct);
        var grupos  = await CargarGruposAsync(baseUrl, client, ct);

        // Grupo del filtro: acepta ID numérico (no necesita /groups) o nombre (sí lo necesita).
        bool grupoConfigurado = !string.IsNullOrWhiteSpace(filtro.Grupo);
        long? grupoFiltroId = grupoConfigurado ? ResolverGrupoId(filtro.Grupo, grupos) : null;
        // «Mi» agente cuenta para el filtro SOLO si el criterio «por agente» está activo (el id igual se
        // resuelve para las notificaciones de asignación en el sync de fondo, pero eso es aparte).
        long? miParaFiltro = filtro.PorAgente ? miAgenteId : null;
        // IDs vistos en ESTA corrida que NO aplican (candidatos a quitar). NUNCA se borra por ausencia.
        var noCoinciden = new HashSet<long>();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Freshdesk devuelve máximo 100 por página. El «include» solo admite estos valores:
            // requester, stats, company, description. «assignee» NO es válido y Freshdesk respondía
            // HTTP 400 («Validation failed»), por lo que la sincronización nunca conectaba. Se pide
            // «description» porque de ahí sale el «description_text» que se guarda más abajo.
            var url  = $"{baseUrl}/tickets?page={page}&per_page=100&include=requester,stats,description";
            var resp = await client.GetAsync(url, ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("API Key de Freshdesk inválida o sin permisos.");
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;

            if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
                break;

            foreach (var item in arr.EnumerateArray())
            {
                var externalId = item.GetProperty("id").GetInt64();
                var subject    = GetString(item, "subject");
                var status     = GetInt(item, "status");
                var priority   = GetInt(item, "priority");
                var type       = GetStringOrNull(item, "type");
                var source     = GetInt(item, "source");
                var tags       = string.Join(", ", item.TryGetProperty("tags", out var tagArr)
                    ? tagArr.EnumerateArray().Select(t => t.GetString() ?? "")
                    : []);
                var description = GetStringOrNull(item, "description_text");

                var agentName     = "";
                var groupName     = "";
                var requesterName = "";
                var requesterEmail = "";

                // Id del agente asignado: SIEMPRE se guarda; su nombre solo si /agents dio permiso.
                long? responderId = null;
                if (item.TryGetProperty("responder_id", out var rid) && rid.ValueKind == JsonValueKind.Number)
                {
                    responderId = rid.GetInt64();
                    if (agentes.TryGetValue(responderId.Value, out var an)) agentName = an;
                }

                long? ticketGroupId = null;
                if (item.TryGetProperty("group_id", out var gid) && gid.ValueKind == JsonValueKind.Number)
                {
                    ticketGroupId = gid.GetInt64();
                    if (grupos.TryGetValue(ticketGroupId.Value, out var gn)) groupName = gn;
                }

                // Filtro: con el filtro activo solo se persisten los míos (si «por agente») o los del
                // grupo; los demás VISTOS aquí quedan como candidatos a quitar (nunca se borra por ausencia).
                if (filtro.Activo && !EsMio(responderId, ticketGroupId, miParaFiltro, grupoFiltroId))
                {
                    noCoinciden.Add(externalId);
                    continue;
                }

                if (item.TryGetProperty("requester", out var req) && req.ValueKind == JsonValueKind.Object)
                {
                    requesterName  = GetString(req, "name");
                    requesterEmail = GetString(req, "email");
                }

                var createdAt = GetDate(item, "created_at");
                var updatedAt = GetDate(item, "updated_at");
                var ticketUrl = $"https://{domain}.freshdesk.com/helpdesk/tickets/{externalId}";

                var existing = db.FreshDeskTickets.FirstOrDefault(t => t.ExternalId == externalId);
                if (existing == null)
                {
                    db.FreshDeskTickets.Add(new FreshDeskTicket
                    {
                        ExternalId        = externalId,
                        Subject           = subject,
                        Status            = status,
                        Priority          = priority,
                        Type              = type,
                        Source            = source,
                        ResponderId       = responderId,
                        AgentName         = agentName,
                        GroupName         = groupName,
                        RequesterName     = requesterName,
                        RequesterEmail    = requesterEmail,
                        Tags              = tags,
                        Description       = description,
                        CreatedAtExternal = createdAt,
                        UpdatedAtExternal = updatedAt,
                        SyncedAt          = now,
                        Url               = ticketUrl
                    });
                    added++;
                }
                else
                {
                    existing.Subject           = subject;
                    existing.Status            = status;
                    existing.Priority          = priority;
                    existing.Type              = type;
                    existing.Source            = source;
                    existing.ResponderId       = responderId;
                    existing.AgentName         = agentName;
                    existing.GroupName         = groupName;
                    existing.RequesterName     = requesterName;
                    existing.RequesterEmail    = requesterEmail;
                    existing.Tags              = tags;
                    existing.Description       = description;
                    existing.UpdatedAtExternal = updatedAt;
                    existing.SyncedAt          = now;
                    existing.Url               = ticketUrl;
                    updated++;
                }
            }
            db.SaveChanges();

            // Si devolvió menos de 100, no hay más páginas
            if (arr.GetArrayLength() < 100) break;
            page++;
        }

        // Reconciliación: con el filtro COMPLETAMENTE resuelto (mi agente + el grupo pedido, si hay), se
        // quitan de la BD los tickets que ya no aplican, salvo los vinculados a DevOps (no se pierde el
        // enlace manual). Si algo no se pudo resolver, NO se borra nada, para no quitar de más.
        // Solo se quitan los tickets que SÍ vimos en esta corrida y confirmamos que ya no aplican; nunca
        // por ausencia (el endpoint /tickets solo trae ~30 días por defecto, y un ticket viejo que sigue
        // siendo mío no debe borrarse). El borrado se limita a cuando el filtro se resolvió por completo:
        // el criterio por agente (si está) pudo identificar mi agente, y el grupo (si está) se resolvió.
        bool filtroCompleto = filtro.Activo
            && (!filtro.PorAgente || miAgenteId != null)
            && (!grupoConfigurado || grupoFiltroId != null);
        int removed = (filtroCompleto && noCoinciden.Count > 0) ? Reconciliar(db, noCoinciden) : 0;

        return new FreshDeskSyncResult(added, updated, removed, Aviso(filtro, miAgenteId, grupoConfigurado, grupoFiltroId));
    }

    /// <summary>Quita de la BD los tickets cuyo ExternalId ESTÁ en <paramref name="aQuitar"/> (los que se
    /// confirmó que ya no aplican), salvo los vinculados a un ticket de DevOps (para no romper el enlace
    /// manual). Devuelve cuántos se quitaron. Interno para poder probarlo sin red.</summary>
    internal static int Reconciliar(AppDbContext db, ISet<long> aQuitar)
    {
        if (aQuitar.Count == 0) return 0;
        var vinculados = db.TicketLinks.Select(l => l.FreshDeskTicketId).ToHashSet();
        var sobran = db.FreshDeskTickets
            .Where(t => aQuitar.Contains(t.ExternalId))
            .ToList()
            .Where(t => !vinculados.Contains(t.Id))
            .ToList();
        if (sobran.Count > 0)
        {
            db.FreshDeskTickets.RemoveRange(sobran);
            db.SaveChanges();
        }
        return sobran.Count;
    }

    /// <summary>Un ticket «es mío» si está asignado a mi agente o pertenece al grupo del filtro.</summary>
    internal static bool EsMio(long? responderId, long? groupId, long? miAgenteId, long? grupoFiltroId) =>
        (miAgenteId != null && responderId == miAgenteId) ||
        (grupoFiltroId != null && groupId == grupoFiltroId);

    /// <summary>
    /// Resuelve el grupo del filtro a un ID. Primero intenta por NOMBRE (así un grupo llamado "2024"
    /// se resuelve bien y no se confunde con un ID). Si no, lo interpreta como ID numérico: cuando se
    /// pudo listar /groups, VALIDA que el ID exista (si no, devuelve null para no filtrar/borrar por un
    /// ID fantasma); si /groups dio 403 (mapa vacío), confía en el ID que capturó el usuario.
    /// </summary>
    internal static long? ResolverGrupoId(string grupoCfg, IReadOnlyDictionary<long, string> grupos)
    {
        var g = (grupoCfg ?? "").Trim();
        if (g.Length == 0) return null;

        foreach (var kv in grupos)                                   // 1) por nombre (tiene prioridad)
            if (string.Equals(kv.Value, g, StringComparison.OrdinalIgnoreCase)) return kv.Key;

        if (long.TryParse(g, out var id))                            // 2) por ID numérico
        {
            if (grupos.Count > 0 && !grupos.ContainsKey(id)) return null;   // se puede validar y no existe
            return id;                                                       // mapa vacío (403): se confía
        }
        return null;
    }

    /// <summary>Mensaje cuando el filtro no se pudo aplicar del todo (para avisarle al usuario).</summary>
    private static string? Aviso(FiltroFreshDesk filtro, long? miAgenteId, bool grupoConfigurado, long? grupoFiltroId)
    {
        if (!filtro.Activo) return null;
        bool agenteFalla = filtro.PorAgente && miAgenteId == null;   // /agents/me no identificó mi agente
        bool grupoFalla  = grupoConfigurado && grupoFiltroId == null; // grupo no resuelto

        if (agenteFalla && grupoFalla)
            return "No se pudo aplicar el filtro: no se identificó tu agente (/agents/me) ni el grupo. " +
                   "Revisa la API key, o escribe el ID numérico del grupo.";
        if (grupoFalla)
            return $"No se encontró el grupo «{filtro.Grupo}». Si tu API key no puede listar grupos, " +
                   "escribe su ID numérico." + (filtro.PorAgente ? " Por ahora se filtró solo por tus asignados." : "");
        if (agenteFalla)
            return "No se pudo identificar tu agente (/agents/me)" +
                   (grupoFiltroId != null ? "; se filtró solo por el grupo." : ".");
        return null;
    }

    /// <summary>
    /// Mira los tickets ACTIVOS (abierto/pendiente) asignados a «mi» agente (<paramref name="miAgenteId"/>)
    /// y crea un aviso por cada uno que no se hubiera visto antes. Todo sobre <paramref name="db"/>.
    ///
    /// La línea base y el conjunto «visto» son POR (usuario, agente): la primera vez para ese agente se
    /// registra lo actual SIN avisar. Así se evita el aluvión del backlog en el primer arranque Y también
    /// al rotar la API key a otro agente (que empieza con su propia base). Las filas de tickets que
    /// dejaron de estar asignados-y-activos a mí se BORRAN, para que una reasignación posterior vuelva a
    /// avisar. Devuelve cuántos avisos se crearon. Público y estático para poder probarlo sin tocar la red.
    /// </summary>
    public static int DetectarAsignadosNuevos(AppDbContext db, int userId, long miAgenteId)
    {
        var mios = db.FreshDeskTickets
            .Where(t => t.ResponderId == miAgenteId && (t.Status == 2 || t.Status == 3))
            .Select(t => new { t.ExternalId, t.Subject, t.Url })
            .ToList();
        var miosIds = mios.Select(t => t.ExternalId).ToHashSet();

        var seen = db.FreshDeskAssignmentsSeen.Where(s => s.UserId == userId && s.AgentId == miAgenteId).ToList();

        // Línea base: la PRIMERA vez para este (usuario, agente) se registra todo lo actual SIN avisar
        // (centinela ExternalId = 0 para marcar «base establecida» aunque no haya nada asignado).
        if (seen.Count == 0)
        {
            db.FreshDeskAssignmentsSeen.Add(new FreshDeskAssignmentSeen { UserId = userId, AgentId = miAgenteId, ExternalId = 0 });
            foreach (var id in miosIds)
                db.FreshDeskAssignmentsSeen.Add(new FreshDeskAssignmentSeen { UserId = userId, AgentId = miAgenteId, ExternalId = id });
            db.SaveChanges();
            return 0;
        }

        // Poda: lo que ya NO está asignado-y-activo a mí deja de estar «visto» (menos el centinela), para
        // que si me lo reasignan más tarde vuelva a avisar.
        var aQuitar = seen.Where(s => s.ExternalId != 0 && !miosIds.Contains(s.ExternalId)).ToList();
        if (aQuitar.Count > 0) db.FreshDeskAssignmentsSeen.RemoveRange(aQuitar);

        var vistos = seen.Where(s => s.ExternalId != 0 && miosIds.Contains(s.ExternalId)).Select(s => s.ExternalId).ToHashSet();
        var pendientes = mios.Where(t => !vistos.Contains(t.ExternalId)).ToList();

        foreach (var t in pendientes)
        {
            db.FreshDeskAssignmentsSeen.Add(new FreshDeskAssignmentSeen { UserId = userId, AgentId = miAgenteId, ExternalId = t.ExternalId });
            db.Notifications.Add(new Notification
            {
                ForUserId = userId,
                Kind      = NotificationKind.FreshDeskAssigned,
                Title     = $"Te asignaron el ticket de Freshdesk #{t.ExternalId}",
                Message   = string.IsNullOrWhiteSpace(t.Subject) ? "(sin asunto)" : t.Subject,
                Url       = t.Url,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (pendientes.Count > 0 || aQuitar.Count > 0) db.SaveChanges();
        return pendientes.Count;
    }

    /// <summary>Id del agente dueño de la API key configurada (GET /agents/me). Devuelve null si el
    /// endpoint no responde 2xx (p. ej. permisos) o el JSON no trae id.</summary>
    private static async Task<long?> ObtenerMiAgenteIdAsync(string baseUrl, HttpClient client, CancellationToken ct)
    {
        try
        {
            var resp = await client.GetAsync($"{baseUrl}/agents/me", ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                return idEl.GetInt64();
        }
        catch { /* red o permisos: no se puede identificar el agente */ }
        return null;
    }

    private (string domain, string baseUrl, HttpClient client) BuildClient()
    {
        var domain = _settings.Get(SettingsService.Keys.FreshDeskDomain)?.TrimEnd('/');
        var apiKey = _settings.Get(SettingsService.Keys.FreshDeskApiKey);

        if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("Configura el dominio y API Key de Freshdesk en Configuración.");

        return (domain, $"https://{domain}.freshdesk.com/api/v2", CrearHttpClient(apiKey));
    }

    /// <summary>HttpClient con la autenticación Basic de Freshdesk (apiKey:X). No lee configuración,
    /// así que es seguro construirlo desde el hilo de fondo.</summary>
    private static HttpClient CrearHttpClient(string apiKey)
    {
        var client  = new HttpClient();
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:X"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>
    /// Mapa id→nombre de los AGENTES de Freshdesk (para poner el nombre del agente asignado a cada
    /// ticket). Requiere una API key de administrador; si Freshdesk responde 403 u otro error, se
    /// devuelve vacío y los nombres quedan en blanco sin romper la sincronización.
    /// </summary>
    private async Task<Dictionary<long, string>> CargarAgentesAsync(string baseUrl, HttpClient client, CancellationToken ct)
    {
        var mapa = new Dictionary<long, string>();
        try
        {
            int page = 1;
            while (true)
            {
                var resp = await client.GetAsync($"{baseUrl}/agents?page={page}&per_page=100", ct);
                if (!resp.IsSuccessStatusCode) break;   // 403 sin permiso, etc.
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) break;
                foreach (var a in doc.RootElement.EnumerateArray())
                {
                    if (!a.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number) continue;
                    // El nombre del agente vive en su «contact»; se cae al email si no hubiera nombre.
                    var nombre = a.TryGetProperty("contact", out var c) && c.ValueKind == JsonValueKind.Object
                        ? (GetStringOrNull(c, "name") ?? GetStringOrNull(c, "email") ?? "")
                        : "";
                    if (!string.IsNullOrWhiteSpace(nombre)) mapa[idEl.GetInt64()] = nombre;
                }
                if (doc.RootElement.GetArrayLength() < 100) break;
                page++;
            }
        }
        catch { /* sin permiso o error transitorio: se sincroniza sin nombres de agente */ }
        return mapa;
    }

    /// <summary>Mapa id→nombre de los GRUPOS de Freshdesk. Mismo criterio tolerante que los agentes.</summary>
    private async Task<Dictionary<long, string>> CargarGruposAsync(string baseUrl, HttpClient client, CancellationToken ct)
    {
        var mapa = new Dictionary<long, string>();
        try
        {
            var resp = await client.GetAsync($"{baseUrl}/groups?per_page=100", ct);
            if (!resp.IsSuccessStatusCode) return mapa;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var g in doc.RootElement.EnumerateArray())
                    if (g.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                    {
                        var nombre = GetString(g, "name");
                        if (!string.IsNullOrWhiteSpace(nombre)) mapa[idEl.GetInt64()] = nombre;
                    }
        }
        catch { /* sin permiso o error transitorio */ }
        return mapa;
    }

    private static string GetString(JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? "";
        return "";
    }

    private static string? GetStringOrNull(JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }

    private static int GetInt(JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetInt32();
        return 0;
    }

    private static DateTime? GetDate(JsonElement el, string key)
    {
        if (el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(v.GetString(), out var dt))
                return dt.ToUniversalTime();
        }
        return null;
    }
}
