using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

public record DevOpsWorkItem(int Id, string Title, string Type, string State, string? Description, string Url, string AreaPath = "", string Tags = "", string AssignedTo = "", string AssignedToEmail = "");
public record DevOpsComment(string Text, string Author, DateTime CreatedAt);
public record DevOpsSyncResult(int Added, int Updated, IReadOnlyList<(DevOpsTicket Ticket, string Reason)> WatchedChanges);
public record DevOpsImportResult(int Added, int Updated, IReadOnlyList<(string Title, string? AssignedTo)> NewItems);

/// <summary>Filtro de sincronización selectiva: tipos de work item, estados y/o personas asignadas
/// (correos o nombres). Vacío = sin filtro. Sirve para no traer miles de items y que sea rápida.</summary>
public record DevOpsSyncFilter(
    IReadOnlyList<string> Types,
    IReadOnlyList<string> Assignees,
    IReadOnlyList<string> States,
    /// <summary>
    /// Trae solo lo asignado al DUEÑO DEL PAT con el que se sincroniza, usando la macro <c>@Me</c> de
    /// DevOps. La resuelve el servidor, así que no depende de que el correo de la ficha coincida con
    /// el de la cuenta de DevOps — que es justo lo que hacía fallar el empate por identidad.
    /// </summary>
    bool SoloMisAsignados = false,
    /// <summary>Solo lo movido en los últimos N días. Null = sin límite (todo el historial).</summary>
    int? CambiadosEnDias = null)
{
    public DevOpsSyncFilter(IReadOnlyList<string> types, IReadOnlyList<string> assignees, IReadOnlyList<string> states)
        : this(types, assignees, states, false, null) { }

    public bool IsEmpty =>
        Types.Count == 0 && Assignees.Count == 0 && States.Count == 0
        && !SoloMisAsignados && CambiadosEnDias == null;
}

public class AzureDevOpsService
{
    private readonly SettingsService _settings;
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly NotificationService _notifications;

    public AzureDevOpsService(SettingsService settings, AppDbContext db, AuditService audit, NotificationService notifications)
    {
        _settings = settings;
        _db = db;
        _audit = audit;
        _notifications = notifications;
    }

    public bool IsEnabled =>
        _settings.Get(SettingsService.Keys.AzureDevOpsEnabled) == "true"
        && !string.IsNullOrEmpty(_settings.Get(SettingsService.Keys.AzureDevOpsPat));

    /// <summary>
    /// El desarrollador puede traer SUS tickets por su cuenta: basta con que tenga su PAT personal
    /// capturado y que la organización y el proyecto estén configurados.
    ///
    /// A propósito NO exige el PAT de la instalación ni la bandera <c>AzureDevOpsEnabled</c>, que son
    /// del administrador: si dependiera de ellos, nadie podría actualizar su propia lista hasta que
    /// el administrador sincronizara, que era exactamente el problema. Escribir los tickets sí está
    /// a su alcance — el login restringido del ejecutable repartido tiene <c>db_datawriter</c>; lo
    /// que no puede es tocar el esquema ni <c>AppSettings</c>.
    /// </summary>
    public bool PuedeSincronizarMisTickets =>
        LocalDevOpsConfig.Load().TienePat
        && !string.IsNullOrEmpty(_settings.Get(SettingsService.Keys.AzureDevOpsOrgUrl))
        && !string.IsNullOrEmpty(_settings.Get(SettingsService.Keys.AzureDevOpsProject));

    /// <summary>
    /// Trae de DevOps solo los work items asignados a quien ejecuta (macro <c>@Me</c> sobre su PAT
    /// personal) y movidos en los últimos <paramref name="dias"/> días. Es la sincronización del
    /// DESARROLLADOR: acotada a lo suyo y a una ventana de tiempo, para que no arrastre años de
    /// historial ni toque los tickets de nadie más.
    /// </summary>
    public Task<DevOpsSyncResult> SincronizarMisTicketsAsync(int dias = 90, CancellationToken ct = default) =>
        SyncToLocalAsync(
            watchedLocalIds: [],
            filter: new DevOpsSyncFilter([], [], [], SoloMisAsignados: true, CambiadosEnDias: dias),
            ct: ct);

    // ── Fetch ligero (solo para import a Requerimientos) ──────────
    public async Task<List<DevOpsWorkItem>> QueryWorkItemsAsync(string? wiql = null, CancellationToken ct = default)
    {
        var (orgUrl, project, pat) = GetConfig();
        using var client = BuildClient(pat);

        var ids = await GetIdsAsync(client, orgUrl, project, wiql, ct);
        if (ids.Count == 0) return [];

        var fields = "System.Id,System.Title,System.WorkItemType,System.State,System.Description,System.AreaPath,System.Tags,System.AssignedTo";
        var result = new List<DevOpsWorkItem>();

        foreach (var batch in Batch(ids, 200))
        {
            ct.ThrowIfCancellationRequested();
            var idsParam = string.Join(",", batch);
            var resp = await client.GetAsync($"{orgUrl}/_apis/wit/workitems?ids={idsParam}&fields={fields}&api-version=7.0", ct);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var f   = item.GetProperty("fields");
                var id  = item.GetProperty("id").GetInt32();
                var (assignedTo, assignedEmail) = ReadAssignedTo(f);
                result.Add(new DevOpsWorkItem(
                    id,
                    GetString(f, "System.Title"),
                    GetString(f, "System.WorkItemType"),
                    GetString(f, "System.State"),
                    GetStringOrNull(f, "System.Description"),
                    $"{orgUrl}/{Uri.EscapeDataString(project)}/_workitems/edit/{id}",
                    GetString(f, "System.AreaPath"),
                    GetString(f, "System.Tags"),
                    assignedTo,
                    assignedEmail));
            }
        }
        return result;
    }

    // ── Sync completo a BD local ──────────────────────────────────
    public async Task<DevOpsSyncResult> SyncToLocalAsync(
        IReadOnlyCollection<int> watchedLocalIds,
        DevOpsSyncFilter? filter = null,
        CancellationToken ct = default)
    {
        var (orgUrl, project, pat) = GetConfig();
        using var client = BuildClient(pat);

        var ids = await GetIdsAsync(client, orgUrl, project, BuildSyncWiql(project, filter), ct);
        if (ids.Count == 0) return new DevOpsSyncResult(0, 0, []);

        // Snapshot pre-sync de tickets vigilados para detectar cambios
        var preSnap = watchedLocalIds.Count > 0
            ? _db.DevOpsTickets
                .Where(t => watchedLocalIds.Contains(t.Id))
                .Select(t => new { t.Id, t.UpdatedAtExternal, t.CommentCount })
                .ToDictionary(t => t.Id)
            : new Dictionary<int, object>() as object;

        var snapDict = watchedLocalIds.Count > 0
            ? _db.DevOpsTickets
                .Where(t => watchedLocalIds.Contains(t.Id))
                .Select(t => new { t.Id, t.UpdatedAtExternal, t.CommentCount })
                .ToDictionary(t => t.Id)
            : [];

        var fields = string.Join(",", new[]
        {
            "System.Id", "System.Title", "System.WorkItemType", "System.State",
            "System.Description", "System.AssignedTo", "System.AreaPath",
            "System.IterationPath", "System.Tags", "System.CreatedDate",
            "System.ChangedDate", "Microsoft.VSTS.Common.Priority",
            "Microsoft.VSTS.Scheduling.StoryPoints", "System.CommentCount"
        });

        int added = 0, updated = 0;
        var now = DateTime.UtcNow;

        foreach (var batch in Batch(ids, 200))
        {
            ct.ThrowIfCancellationRequested();
            var idsParam = string.Join(",", batch);
            var resp = await client.GetAsync($"{orgUrl}/_apis/wit/workitems?ids={idsParam}&fields={fields}&api-version=7.0", ct);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var f          = item.GetProperty("fields");
                var externalId = item.GetProperty("id").GetInt32();
                var url        = $"{orgUrl}/{Uri.EscapeDataString(project)}/_workitems/edit/{externalId}";

                var (assignedTo, assignedEmail) = ReadAssignedTo(f);

                int commentCount = GetInt(f, "System.CommentCount");

                var existing = _db.DevOpsTickets.FirstOrDefault(t => t.ExternalId == externalId);
                if (existing == null)
                {
                    _db.DevOpsTickets.Add(new DevOpsTicket
                    {
                        ExternalId        = externalId,
                        Title             = GetString(f, "System.Title"),
                        WorkItemType      = GetString(f, "System.WorkItemType"),
                        State             = GetString(f, "System.State"),
                        Priority          = GetString(f, "Microsoft.VSTS.Common.Priority"),
                        AssignedTo        = assignedTo,
                        AssignedToUniqueName = assignedEmail,
                        AreaPath          = GetString(f, "System.AreaPath"),
                        IterationPath     = GetString(f, "System.IterationPath"),
                        Tags              = GetString(f, "System.Tags"),
                        Description       = GetStringOrNull(f, "System.Description"),
                        StoryPoints       = GetDouble(f, "Microsoft.VSTS.Scheduling.StoryPoints"),
                        CreatedAtExternal = GetDate(f, "System.CreatedDate"),
                        UpdatedAtExternal = GetDate(f, "System.ChangedDate"),
                        SyncedAt          = now,
                        Url               = url,
                        CommentCount      = commentCount
                    });
                    added++;
                }
                else
                {
                    existing.Title             = GetString(f, "System.Title");
                    existing.WorkItemType      = GetString(f, "System.WorkItemType");
                    existing.State             = GetString(f, "System.State");
                    existing.Priority          = GetString(f, "Microsoft.VSTS.Common.Priority");
                    existing.AssignedTo        = assignedTo;
                    existing.AssignedToUniqueName = assignedEmail;
                    existing.AreaPath          = GetString(f, "System.AreaPath");
                    existing.IterationPath     = GetString(f, "System.IterationPath");
                    existing.Tags              = GetString(f, "System.Tags");
                    existing.Description       = GetStringOrNull(f, "System.Description");
                    existing.StoryPoints       = GetDouble(f, "Microsoft.VSTS.Scheduling.StoryPoints");
                    existing.UpdatedAtExternal = GetDate(f, "System.ChangedDate");
                    existing.SyncedAt          = now;
                    existing.Url               = url;
                    existing.CommentCount      = commentCount;
                    updated++;
                }
            }
            _db.SaveChanges();
        }

        // Detectar cambios en tickets vigilados
        var watchedChanges = new List<(DevOpsTicket, string)>();
        if (watchedLocalIds.Count > 0 && snapDict.Count > 0)
        {
            var postTickets = _db.DevOpsTickets
                .Where(t => watchedLocalIds.Contains(t.Id))
                .ToList();

            foreach (var ticket in postTickets)
            {
                if (!snapDict.TryGetValue(ticket.Id, out var snap)) continue;

                var reasons = new List<string>();
                if (ticket.UpdatedAtExternal != snap.UpdatedAtExternal)
                    reasons.Add("estado/campos actualizados");
                if (ticket.CommentCount > snap.CommentCount)
                    reasons.Add($"{ticket.CommentCount - snap.CommentCount} comentario(s) nuevo(s)");

                if (reasons.Count > 0)
                    watchedChanges.Add((ticket, string.Join(", ", reasons)));
            }
        }

        _audit.Record(AuditAction.Create, "DevOpsTicket", null,
            $"Sync Azure DevOps: {added} nuevos, {updated} actualizados");

        return new DevOpsSyncResult(added, updated, watchedChanges);
    }

    // ── Comentarios ───────────────────────────────────────────────
    public async Task<List<DevOpsComment>> GetCommentsAsync(int externalId, CancellationToken ct = default)
    {
        var (orgUrl, project, pat) = GetConfig();
        using var client = BuildClient(pat);

        var url  = $"{orgUrl}/{Uri.EscapeDataString(project)}/_apis/wit/workItems/{externalId}/comments?api-version=7.1-preview.3";
        var resp = await client.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return [];

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var result = new List<DevOpsComment>();

        if (!doc.RootElement.TryGetProperty("comments", out var comments)) return result;

        foreach (var c in comments.EnumerateArray())
        {
            var text   = c.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            var author = "";
            if (c.TryGetProperty("createdBy", out var by))
                author = by.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
            DateTime createdAt = DateTime.UtcNow;
            if (c.TryGetProperty("createdDate", out var cd) && DateTime.TryParse(cd.GetString(), out var dt))
                createdAt = dt.ToUniversalTime();

            result.Add(new DevOpsComment(text, author, createdAt));
        }

        return result;
    }

    public async Task PostCommentAsync(int externalId, string text, CancellationToken ct = default)
    {
        var (orgUrl, project, pat) = GetConfig();
        using var client = BuildClient(pat);

        var url  = $"{orgUrl}/{Uri.EscapeDataString(project)}/_apis/wit/workItems/{externalId}/comments?api-version=7.1-preview.3";
        var body = JsonSerializer.Serialize(new { text });
        var resp = await client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();

        // Actualizar CommentCount en BD local
        var ticket = _db.DevOpsTickets.FirstOrDefault(t => t.ExternalId == externalId);
        if (ticket != null) { ticket.CommentCount++; _db.SaveChanges(); }
    }

    /// <summary>
    /// Sube un archivo (p. ej. una captura de pantalla) como ADJUNTO a DevOps y devuelve su URL,
    /// que luego se embebe en un comentario. Usa el PAT de quien lo ejecuta.
    /// </summary>
    public async Task<string> UploadAttachmentAsync(byte[] bytes, string fileName, CancellationToken ct = default)
    {
        var (orgUrl, project, pat) = GetConfig();
        using var client = BuildClient(pat);

        var url = $"{orgUrl}/{Uri.EscapeDataString(project)}/_apis/wit/attachments?fileName={Uri.EscapeDataString(fileName)}&api-version=7.0";
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var resp = await client.PostAsync(url, content, ct);
        var payload = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"DevOps rechazó el adjunto ({(int)resp.StatusCode}): {Recortar(payload)}");
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("DevOps no devolvió la URL del adjunto.");
    }

    /// <summary>
    /// Publica un comentario en el ticket con EVIDENCIAS (capturas) embebidas: sube cada imagen como
    /// adjunto y arma el comentario en HTML con las imágenes. Si no hay evidencias, es un comentario
    /// de solo texto.
    /// </summary>
    public async Task PostCommentWithEvidenceAsync(int externalId, string? texto,
        IReadOnlyList<(byte[] bytes, string fileName)> evidencias, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.Append(System.Net.WebUtility.HtmlEncode(texto ?? "").Replace("\n", "<br>"));
        foreach (var (bytes, name) in evidencias)
        {
            var url = await UploadAttachmentAsync(bytes, name, ct);
            sb.Append($"<br><br>📎 <b>{System.Net.WebUtility.HtmlEncode(name)}</b><br><img src=\"{url}\" width=\"640\" />");
        }
        await PostCommentAsync(externalId, sb.ToString(), ct);
    }

    // ── Import a Requerimientos + auto-asignación por reglas ──────
    public DevOpsImportResult ImportAsRequirements(List<DevOpsWorkItem> items)
    {
        var rules = _db.DevOpsAssignmentRules.Where(r => r.IsActive)
            .OrderBy(r => r.Order).ThenBy(r => r.Id)
            .Select(r => new { r.Match, r.MatchValue, r.DeveloperId, DevName = r.Developer.FullName })
            .ToList();

        // Para el respaldo por identidad: si ninguna regla coincide, se intenta empatar el asignado
        // del work item (correo o nombre) con un desarrollador activo, de modo que importar de DevOps
        // deje el item en «Mis Asignaciones» de su dueño sin necesidad de una regla por persona.
        var devs = _db.Developers.Where(d => d.IsActive).ToList();

        int added = 0, updated = 0;
        var now = DateTime.UtcNow;
        var newItems = new List<(string Title, string? AssignedTo)>();

        foreach (var item in items)
        {
            var externalId = item.Id.ToString();
            var existing = _db.Requirements.FirstOrDefault(r => r.ExternalId == externalId && r.Source == RequirementSource.AzureDevOps);
            if (existing != null)
            {
                existing.Title       = item.Title;
                if (!string.IsNullOrEmpty(item.Description))
                    existing.Description = item.Description;
                existing.Status      = MapStatus(item.State);
                existing.ExternalUrl = item.Url;
                updated++;
            }
            else
            {
                // No se DA DE ALTA nada que ya esté cerrado (Done/Closed/Completed/Removed/Cancelado):
                // el objetivo es dar seguimiento a trabajo vivo. Los que ya existían sí se actualizan
                // arriba (para reflejar que pasaron a Entregado/Cancelado), pero uno nuevo cerrado no
                // aporta nada que cronometrar.
                if (EsCerrado(item.State)) continue;

                var req = new Requirement
                {
                    Title       = item.Title,
                    Description = item.Description,
                    Status      = MapStatus(item.State),
                    Priority    = RequirementPriority.Media,
                    Source      = RequirementSource.AzureDevOps,
                    ExternalId  = externalId,
                    ExternalUrl = item.Url,
                    CreatedAt   = now,
                    StatusChangedAt = now
                };
                _db.Requirements.Add(req);

                // Auto-asignación: primera regla activa que coincida
                string? assignedName = null;
                int? assignedDevId = null;
                foreach (var rule in rules)
                {
                    if (!RuleMatches(rule.Match, rule.MatchValue, item)) continue;
                    _db.Assignments.Add(new Assignment { Requirement = req, DeveloperId = rule.DeveloperId, AssignedAt = now });
                    assignedName = rule.DevName;
                    assignedDevId = rule.DeveloperId;
                    break;
                }

                // Respaldo por identidad cuando ninguna regla coincidió: correo de DevOps ↔ correo
                // del desarrollador (o nombre normalizado). Es lo que hace que el item aparezca en
                // «Mis Asignaciones» del dueño real sin configurar una regla por cada persona.
                if (assignedName == null)
                {
                    var dev = DevOpsIdentityMatcher.Find(item.AssignedTo, item.AssignedToEmail, devs);
                    if (dev != null)
                    {
                        _db.Assignments.Add(new Assignment { Requirement = req, DeveloperId = dev.Id, AssignedAt = now });
                        assignedName = dev.FullName;
                        assignedDevId = dev.Id;
                    }
                }

                // Aviso al desarrollador: le acaban de asignar trabajo. Dedup por (dev, item) para
                // no repetirlo si se vuelve a importar el mismo work item.
                if (assignedDevId != null)
                    _notifications.NotifyDeveloper(assignedDevId.Value, NotificationKind.RequirementAssigned,
                        "Nuevo requerimiento asignado",
                        $"#{externalId} — {item.Title}", item.Url,
                        dedupeKey: $"req-assign:{assignedDevId}:{externalId}");

                newItems.Add((item.Title, assignedName));
                added++;
            }
        }
        _db.SaveChanges();
        _audit.Record(AuditAction.Create, "Requirement", null,
            $"Importación Azure DevOps: {added} nuevos, {updated} actualizados, {newItems.Count(x => x.AssignedTo != null)} auto-asignados");
        return new DevOpsImportResult(added, updated, newItems);
    }

    private static bool RuleMatches(DevOpsRuleMatch match, string value, DevOpsWorkItem item)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        return match switch
        {
            DevOpsRuleMatch.AreaPathContiene   => item.AreaPath.Contains(v, StringComparison.OrdinalIgnoreCase),
            DevOpsRuleMatch.TipoEsIgual        => item.Type.Equals(v, StringComparison.OrdinalIgnoreCase),
            DevOpsRuleMatch.TituloContiene     => item.Title.Contains(v, StringComparison.OrdinalIgnoreCase),
            DevOpsRuleMatch.TagContiene        => item.Tags.Contains(v, StringComparison.OrdinalIgnoreCase),
            DevOpsRuleMatch.AsignadoAContiene  => item.AssignedTo.Contains(v, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    /// <summary>
    /// Materializa como REQUERIMIENTOS los tickets de DevOps ABIERTOS asignados a un desarrollador
    /// (de los ya sincronizados, empatados por identidad), y se los asigna. Así aparecen en «Mis
    /// Asignaciones» con su cronómetro, sin que el administrador importe a mano. Es idempotente: no
    /// duplica (empata por ExternalId) y NO toca el estado/avance local de los que ya existen, para
    /// no pisar lo que el desarrollador lleva medido. Devuelve (nuevos, ya existentes actualizados).
    /// </summary>
    public (int nuevos, int actualizados) MaterializarAsignados(int developerId)
    {
        var dev = _db.Developers.Find(developerId);
        if (dev == null) return (0, 0);

        var tickets = DevOpsTicketQuery.ForDeveloper(_db, dev).Where(t => !EsCerrado(t.State)).ToList();
        if (tickets.Count == 0) return (0, 0);

        // Políticas de SLA por prioridad de DevOps (configurables por el administrador). Si ninguna
        // está activa, no se crea ningún SLA automático.
        var politicasSla = SlaPolicyStore.Parse(_settings.Get(SlaPolicyStore.SettingKey));
        bool hayPoliticaActiva = politicasSla.Any(p => p.Enabled);
        // Todas las parejas requerimiento↔ticket materializadas en esta pasada (nuevas y existentes):
        // así el SLA por prioridad se aplica a TODAS las asignaciones, no solo a las recién creadas.
        var materializados = new List<(Requirement req, DevOpsTicket ticket)>();

        int nuevos = 0, actualizados = 0;
        var now = DateTime.UtcNow;
        foreach (var t in tickets)
        {
            var extId = t.ExternalId.ToString();
            var req = _db.Requirements.FirstOrDefault(r => r.ExternalId == extId && r.Source == RequirementSource.AzureDevOps);
            if (req == null)
            {
                req = new Requirement
                {
                    Title       = t.Title,
                    Description = t.Description,
                    Status      = MapStatus(t.State),
                    Priority    = RequirementPriority.Media,
                    Source      = RequirementSource.AzureDevOps,
                    ExternalId  = extId,
                    ExternalUrl = t.Url,
                    CreatedAt   = now,
                    StatusChangedAt = now
                };
                _db.Requirements.Add(req);
                _db.Assignments.Add(new Assignment { Requirement = req, DeveloperId = developerId, AssignedAt = now });
                nuevos++;
            }
            else
            {
                // Solo se refresca el título/enlace y se asegura que esté asignado a este dev. El
                // ESTADO y el avance NO se tocan: reflejan lo que el desarrollador lleva con su cronómetro.
                req.Title = t.Title;
                req.ExternalUrl = t.Url;
                if (!_db.Assignments.Any(a => a.RequirementId == req.Id && a.DeveloperId == developerId))
                    _db.Assignments.Add(new Assignment { RequirementId = req.Id, DeveloperId = developerId, AssignedAt = now });
                actualizados++;
            }
            materializados.Add((req, t));
        }
        _db.SaveChanges();

        // SLA automático por prioridad para TODAS las asignaciones: a cada requerimiento SIN ningún SLA
        // (ni siquiera cancelado) se le crea uno según la prioridad de su ticket en DevOps. Así se
        // rellenan también las asignaciones antiguas, respetando los SLA que se cerraron o cancelaron a
        // propósito (esos ya tienen registro, así que no se resucitan). Los req nuevos ya tienen Id real.
        if (hayPoliticaActiva)
        {
            int slasCreados = 0;
            foreach (var (req, ticket) in materializados)
            {
                if (SlaPolicyStore.Resolver(politicasSla, ticket.Priority) is not SlaPolicy pol) continue;
                if (_db.SlaCommitments.Any(s => s.RequirementId == req.Id)) continue;   // ya tuvo SLA: no se recrea
                AgregarSlaAutomatico(req, ticket, pol, developerId, now);
                slasCreados++;
            }
            if (slasCreados > 0) _db.SaveChanges();
        }

        return (nuevos, actualizados);
    }

    /// <summary>
    /// Añade (sin guardar) un SLA automático para un requerimiento según una política de prioridad.
    /// Reutiliza la misma regla de recordatorio que los SLA que asigna el administrador a mano.
    /// </summary>
    private void AgregarSlaAutomatico(Requirement req, DevOpsTicket ticket, SlaPolicy pol, int developerId, DateTime now)
    {
        var dueUtc = now.AddHours(pol.Hours);
        _db.SlaCommitments.Add(new SlaCommitment
        {
            RequirementId          = req.Id,
            DeveloperId            = developerId,
            DevOpsTicketExternalId = ticket.ExternalId,
            DevOpsTicketUrl        = string.IsNullOrWhiteSpace(ticket.Url) ? null : ticket.Url,
            DueAtUtc               = dueUtc,
            ReminderEveryHours     = pol.ReminderEveryHours,
            NextReminderAtUtc      = SlaService.PrimerRecordatorio(now, dueUtc, pol.ReminderEveryHours),
            Status                 = SlaStatus.Activo,
            Notes                  = $"SLA automático por prioridad {pol.Priority} ({SlaPolicyStore.NombrePrioridad(pol.Priority)}) del ticket #{ticket.ExternalId}.",
            CreatedAt              = now
        });
    }

    /// <summary>
    /// Ajusta el SLA de un requerimiento cuando cambia la prioridad de su ticket (acción explícita):
    /// si hay un SLA vigente lo REPROGRAMA al plazo de la nueva prioridad; si no hay ninguno (ni
    /// cancelado) y hay a quién asignárselo, crea uno. No toca nada si la nueva prioridad no tiene
    /// política activa. No guarda: lo hace el llamador.
    /// </summary>
    private void ReconciliarSlaPorPrioridad(Requirement req, DevOpsTicket ticket, int? developerIdParaCrear, DateTime now)
    {
        var politicas = SlaPolicyStore.Parse(_settings.Get(SlaPolicyStore.SettingKey));
        if (SlaPolicyStore.Resolver(politicas, ticket.Priority) is not SlaPolicy pol) return;

        var activo = _db.SlaCommitments.FirstOrDefault(s => s.RequirementId == req.Id && s.Status == SlaStatus.Activo);
        if (activo != null)
        {
            var dueUtc = now.AddHours(pol.Hours);
            activo.DueAtUtc           = dueUtc;
            activo.ReminderEveryHours = pol.ReminderEveryHours;
            activo.NextReminderAtUtc  = SlaService.PrimerRecordatorio(now, dueUtc, pol.ReminderEveryHours);
            activo.Notes = $"SLA ajustado por cambio a prioridad {pol.Priority} ({SlaPolicyStore.NombrePrioridad(pol.Priority)}) del ticket #{ticket.ExternalId}.";
        }
        else if (developerIdParaCrear is int devId && !_db.SlaCommitments.Any(s => s.RequirementId == req.Id))
        {
            AgregarSlaAutomatico(req, ticket, pol, devId, now);
        }
    }

    /// <summary>
    /// true si el estado del work item cuenta como CERRADO (Done/Closed/Completed → Entregado,
    /// Removed/Cancelled → Cancelado). Se apoya en <see cref="MapStatus"/> para tolerar los distintos
    /// nombres de estado según la plantilla de proceso (Agile, Scrum, Basic).
    /// </summary>
    public static bool EsCerrado(string state)
    {
        var st = MapStatus(state);
        return st is RequirementStatus.Entregado or RequirementStatus.Cancelado;
    }

    /// <summary>
    /// Trae los work items ASIGNADOS a alguien y que NO están cerrados: justo los candidatos a
    /// convertirse en trabajo con seguimiento. La WIQL descarta lo obvio en el servidor y luego
    /// <see cref="EsCerrado"/> hace el filtro autoritativo (por si la plantilla usa otros nombres).
    /// </summary>
    public async Task<List<DevOpsWorkItem>> QueryAssignedOpenAsync(CancellationToken ct = default)
    {
        var (_, project, _) = GetConfig();
        var wiql =
            $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{project}' " +
            "AND [System.AssignedTo] <> '' " +
            "AND [System.State] NOT IN ('Removed','Done','Closed','Completed') " +
            "ORDER BY [System.ChangedDate] DESC";
        var items = await QueryWorkItemsAsync(wiql, ct);
        return items.Where(i => !EsCerrado(i.State) && !string.IsNullOrWhiteSpace(i.AssignedTo)).ToList();
    }

    /// <summary>
    /// Reasigna el work item en DevOps (PATCH System.AssignedTo). Con <paramref name="uniqueNameOrEmpty"/>
    /// vacío lo DESASIGNA. Escribe con el PAT de quien lo ejecuta, así el cambio queda a su nombre en
    /// el historial de DevOps. Actualiza también el ticket local y devuelve el nuevo (nombre, correo).
    /// </summary>
    public async Task<(string display, string email)> AssignWorkItemAsync(int externalId, string? uniqueNameOrEmpty, CancellationToken ct = default)
    {
        var (orgUrl, project, pat) = GetConfig();
        using var client = BuildClient(pat);

        object patch = string.IsNullOrWhiteSpace(uniqueNameOrEmpty)
            ? new[] { new { op = "remove", path = "/fields/System.AssignedTo" } }
            : new object[] { new { op = "add", path = "/fields/System.AssignedTo", value = uniqueNameOrEmpty.Trim() } };

        var url  = $"{orgUrl}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/{externalId}?api-version=7.0";
        using var msg = new HttpRequestMessage(new HttpMethod("PATCH"), url)
        {
            Content = new StringContent(JsonSerializer.Serialize(patch), Encoding.UTF8, "application/json-patch+json")
        };
        var resp = await client.SendAsync(msg, ct);
        var payload = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"DevOps rechazó la reasignación ({(int)resp.StatusCode}): {Recortar(payload)}");

        string display = "", email = "";
        using (var doc = JsonDocument.Parse(payload))
            if (doc.RootElement.TryGetProperty("fields", out var f))
                (display, email) = ReadAssignedTo(f);

        var ticket = _db.DevOpsTickets.FirstOrDefault(t => t.ExternalId == externalId);
        if (ticket != null)
        {
            ticket.AssignedTo = display;
            ticket.AssignedToUniqueName = string.IsNullOrWhiteSpace(email) ? null : email;
            _db.SaveChanges();
        }
        return (display, email);
    }

    /// <summary>
    /// Cambia el estado del work item en DevOps (PATCH System.State) — equivale a «moverlo de columna»
    /// en un tablero. DevOps valida la transición según la plantilla; si la rechaza, se propaga el error.
    /// Actualiza el ticket local y devuelve el estado resultante.
    /// </summary>
    public async Task<string> ChangeStateAsync(int externalId, string newState, CancellationToken ct = default)
    {
        var (orgUrl, project, pat) = GetConfig();
        using var client = BuildClient(pat);

        var patch = new object[] { new { op = "add", path = "/fields/System.State", value = newState } };
        var url   = $"{orgUrl}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/{externalId}?api-version=7.0";
        using var msg = new HttpRequestMessage(new HttpMethod("PATCH"), url)
        {
            Content = new StringContent(JsonSerializer.Serialize(patch), Encoding.UTF8, "application/json-patch+json")
        };
        var resp = await client.SendAsync(msg, ct);
        var payload = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"DevOps rechazó el cambio de estado ({(int)resp.StatusCode}): {Recortar(payload)}");

        string state = newState;
        using (var doc = JsonDocument.Parse(payload))
            if (doc.RootElement.TryGetProperty("fields", out var f) && f.TryGetProperty("System.State", out var st))
                state = st.GetString() ?? newState;

        var ticket = _db.DevOpsTickets.FirstOrDefault(t => t.ExternalId == externalId);
        if (ticket != null) { ticket.State = state; _db.SaveChanges(); }
        return state;
    }

    /// <summary>
    /// Cambia la PRIORIDAD del work item en DevOps (PATCH Microsoft.VSTS.Common.Priority, valor 1..4,
    /// donde 1 es la más alta). Escribe con el PAT de quien lo ejecuta, actualiza el ticket y el
    /// requerimiento locales, y ajusta el SLA automático de esa asignación según la nueva prioridad.
    /// Devuelve la prioridad aplicada.
    /// </summary>
    public async Task<int> ChangePriorityAsync(int externalId, int newPriority, CancellationToken ct = default)
    {
        if (newPriority is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(newPriority), "La prioridad de DevOps debe estar entre 1 (muy alta) y 4 (baja).");

        var (orgUrl, project, pat) = GetConfig();
        using var client = BuildClient(pat);

        var patch = new object[] { new { op = "add", path = "/fields/Microsoft.VSTS.Common.Priority", value = newPriority } };
        var url   = $"{orgUrl}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/{externalId}?api-version=7.0";
        using var msg = new HttpRequestMessage(new HttpMethod("PATCH"), url)
        {
            Content = new StringContent(JsonSerializer.Serialize(patch), Encoding.UTF8, "application/json-patch+json")
        };
        var resp = await client.SendAsync(msg, ct);
        var payload = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"DevOps rechazó el cambio de prioridad ({(int)resp.StatusCode}): {Recortar(payload)}");

        // DevOps aceptó el valor; la respuesta trae la prioridad como número (GetString no lo lee bien),
        // así que el valor autoritativo es el que enviamos.
        var ticket = _db.DevOpsTickets.FirstOrDefault(t => t.ExternalId == externalId);
        if (ticket != null)
        {
            ticket.Priority = newPriority.ToString();

            // Reflejar la prioridad en el requerimiento local (mapeo inverso: DevOps 1=muy alta ↔ Crítica).
            var extId = externalId.ToString();
            var req = _db.Requirements.FirstOrDefault(r => r.ExternalId == extId && r.Source == RequirementSource.AzureDevOps);
            if (req != null)
            {
                req.Priority = (RequirementPriority)(4 - newPriority);
                // El SLA es para quien tiene el ticket AHORA: se empata por identidad con el asignado
                // actual del ticket, no por una fila de asignación vieja que pudo quedar de un dueño
                // anterior. Como respaldo, la asignación local más reciente.
                int? devId = DevOpsIdentityMatcher.Find(ticket.AssignedTo, ticket.AssignedToUniqueName, _db.Developers.ToList())?.Id;
                devId ??= _db.Assignments.Where(a => a.RequirementId == req.Id)
                                         .OrderByDescending(a => a.AssignedAt).ThenByDescending(a => a.Id)
                                         .Select(a => (int?)a.DeveloperId).FirstOrDefault();
                ReconciliarSlaPorPrioridad(req, ticket, devId, DateTime.UtcNow);
            }
            _db.SaveChanges();
        }
        return newPriority;
    }

    // ── Reporte de tiempo cronometrado → DevOps ──────────────────────────────────

    /// <summary>
    /// Registra en el ticket de DevOps el tiempo cronometrado para un requerimiento. Envía SOLO el
    /// delta aún no reportado (respecto de <see cref="Requirement.DevOpsReportedSeconds"/>), así que
    /// repetir la operación no duplica horas. Según la configuración, actualiza los campos de trabajo
    /// (Completed/Remaining Work) y/o publica un comentario, con el PAT personal de quien lo ejecuta.
    /// Devuelve (intentado: hubo algo que reportar y estaba activado; ok: se registró; mensaje).
    /// </summary>
    public async Task<(bool intentado, bool ok, string mensaje)> ReportarTiempoDevOpsAsync(
        int requirementId, int totalSegundos, CancellationToken ct = default)
    {
        if (_settings.Get(DevOpsTimeReport.KeyEnabled) != "true") return (false, false, "");

        // Se lee SIN rastrear: con un DbContext singleton compartido, la entidad rastreada puede traer
        // un DevOpsReportedSeconds viejo; el delta debe calcularse contra el valor REAL en base.
        var info = _db.Requirements.AsNoTracking()
            .Where(r => r.Id == requirementId)
            .Select(r => new { r.Source, r.ExternalId, r.DevOpsReportedSeconds })
            .FirstOrDefault();
        if (info == null) return (false, false, "");
        if (!DevOpsTimeReport.AplicaA(info.Source, info.ExternalId, out var workItemId)) return (false, false, "");

        int reportado = info.DevOpsReportedSeconds;
        int deltaSeg = DevOpsTimeReport.DeltaSegundos(totalSegundos, reportado);
        double horasDelta = DevOpsTimeReport.SegundosAHoras(deltaSeg);
        if (deltaSeg <= 0 || horasDelta <= 0) return (false, false, "");   // nada nuevo digno de reportar

        var modo     = DevOpsTimeReport.ParseMode(_settings.Get(DevOpsTimeReport.KeyMode));
        bool reducir = _settings.Get(DevOpsTimeReport.KeyReduceRemaining) == "true";

        bool camposOk = false, comentarioOk = false;
        var partes = new List<string>();
        string? errorTransitorio = null;

        // 1) Campos de trabajo (ADITIVO, no idempotente). En cuanto la PATCH tiene éxito se avanza el
        //    watermark de inmediato —y SOLO por las horas realmente enviadas (redondeadas), arrastrando
        //    el resto— para que ni un fallo posterior ni el redondeo hagan que se re-sumen las mismas horas.
        if (DevOpsTimeReport.ActualizaCampos(modo))
        {
            try
            {
                camposOk = await TryActualizarCamposTrabajoAsync(workItemId, horasDelta, reducir, ct);
                if (camposOk)
                {
                    PersistirReportado(requirementId, reportado + DevOpsTimeReport.HorasASegundos(horasDelta));
                    partes.Add($"Completed Work +{horasDelta:0.##} h");
                }
                else
                    partes.Add("este tipo de work item no admite horas de trabajo");
            }
            catch (Exception ex) { errorTransitorio = ex.Message; }
        }

        // 2) Comentario (reporta el delta EXACTO). Solo si los campos no fallaron por red.
        if (errorTransitorio == null && DevOpsTimeReport.Comenta(modo))
        {
            try
            {
                var texto = $"⏱ Tiempo registrado desde la app: +{WorkSessionService.Format(deltaSeg)} " +
                            $"(total dedicado: {WorkSessionService.Format(totalSegundos)}).";
                await PostCommentAsync(workItemId, texto, ct);
                comentarioOk = true;
                if (!camposOk) PersistirReportado(requirementId, totalSegundos);   // el comentario cubre el delta exacto
                partes.Add("comentario publicado");
            }
            catch (Exception ex) { errorTransitorio = ex.Message; }
        }

        bool algoOk = camposOk || comentarioOk;
        if (algoOk)
            _audit.Record(AuditAction.Update, "DevOpsTicket", workItemId.ToString(),
                $"Tiempo reportado a DevOps (+{WorkSessionService.Format(deltaSeg)})");

        if (errorTransitorio != null)
            return (true, algoOk, algoOk ? $"{string.Join("; ", partes)} (el resto falló: {errorTransitorio})" : errorTransitorio);
        return (true, algoOk, string.Join("; ", partes));
    }

    /// <summary>
    /// Persiste los segundos ya reportados con un UPDATE directo, FUERA del change tracker: así no lo
    /// bloquea un conflicto de concurrencia (RowVersion) ni otras entidades pendientes del DbContext
    /// singleton, y no queda desincronizado (el próximo reporte relee con AsNoTracking). Reduce al
    /// mínimo la ventana en que DevOps podría quedar por delante del watermark local.
    /// </summary>
    private void PersistirReportado(int requirementId, int segundos) =>
        _db.Database.ExecuteSqlRaw("UPDATE Requirements SET DevOpsReportedSeconds = {0} WHERE Id = {1}", segundos, requirementId);

    /// <summary>
    /// Suma <paramref name="horasDelta"/> al Completed Work del work item (leyendo el actual) y, si se
    /// pide, baja el Remaining Work. Devuelve false si el tipo de work item no tiene esos campos
    /// (respuesta 4xx): no es un error fatal. Propaga las fallas transitorias (5xx/red) para reintentar.
    /// </summary>
    private async Task<bool> TryActualizarCamposTrabajoAsync(int workItemId, double horasDelta, bool reducirRestante, CancellationToken ct)
    {
        const string F_COMPLETED = "Microsoft.VSTS.Scheduling.CompletedWork";
        const string F_REMAINING = "Microsoft.VSTS.Scheduling.RemainingWork";

        var (orgUrl, project, pat) = GetConfig();
        using var client = BuildClient(pat);
        var wiUrl = $"{orgUrl}/{Uri.EscapeDataString(project)}/_apis/wit/workitems/{workItemId}?api-version=7.0";

        double completed = 0, remaining = -1;
        var get = await client.GetAsync($"{wiUrl}&fields={F_COMPLETED},{F_REMAINING}", ct);
        get.EnsureSuccessStatusCode();   // 404/red → excepción (transitorio o ticket inexistente)
        using (var doc = JsonDocument.Parse(await get.Content.ReadAsStringAsync(ct)))
            if (doc.RootElement.TryGetProperty("fields", out var f))
            {
                if (f.TryGetProperty(F_COMPLETED, out var cw) && cw.ValueKind == JsonValueKind.Number) completed = cw.GetDouble();
                if (f.TryGetProperty(F_REMAINING, out var rw) && rw.ValueKind == JsonValueKind.Number) remaining = rw.GetDouble();
            }

        var patch = new List<object>
        {
            new { op = "add", path = $"/fields/{F_COMPLETED}", value = Math.Round(completed + horasDelta, 2) }
        };
        if (reducirRestante && remaining >= 0)
            patch.Add(new { op = "add", path = $"/fields/{F_REMAINING}", value = Math.Round(Math.Max(0, remaining - horasDelta), 2) });

        using var msg = new HttpRequestMessage(new HttpMethod("PATCH"), wiUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(patch), Encoding.UTF8, "application/json-patch+json")
        };
        var resp = await client.SendAsync(msg, ct);
        if (resp.IsSuccessStatusCode) return true;
        if ((int)resp.StatusCode >= 500) resp.EnsureSuccessStatusCode();   // transitorio → reintentar
        return false;   // 4xx: el tipo de work item no admite estos campos
    }

    /// <summary>
    /// Detecta los work items ABIERTOS recién asignados a un usuario (que no se conocían) y crea un
    /// aviso por cada uno. La PRIMERA vez para ese usuario fija una línea base SIN avisar, para no
    /// soltar un aluvión con todo su backlog. Devuelve los tickets nuevos (para el globo de la bandeja).
    /// </summary>
    public List<DevOpsTicket> DetectarAsignadosNuevos(int userId, Developer dev)
    {
        var mias = DevOpsTicketQuery.ForDeveloper(_db, dev).Where(t => !EsCerrado(t.State)).ToList();
        var vistos = _db.DevOpsAssignmentsSeen.Where(s => s.UserId == userId).Select(s => s.ExternalId).ToHashSet();

        // Línea base: la PRIMERA vez para el usuario se registra todo lo actual SIN avisar (evita el
        // aluvión del backlog). Se usa un centinela ExternalId = 0 para marcar «base establecida»
        // aunque no tenga ningún ticket todavía. No toca AppSettings a propósito: el login restringido
        // de los desarrolladores no puede escribir ahí, pero sí en esta tabla.
        if (vistos.Count == 0)
        {
            _db.DevOpsAssignmentsSeen.Add(new DevOpsAssignmentSeen { UserId = userId, ExternalId = 0 });
            foreach (var t in mias)
                _db.DevOpsAssignmentsSeen.Add(new DevOpsAssignmentSeen { UserId = userId, ExternalId = t.ExternalId });
            _db.SaveChanges();
            return [];
        }

        var nuevos = mias.Where(t => !vistos.Contains(t.ExternalId)).ToList();
        foreach (var t in nuevos)
        {
            _db.DevOpsAssignmentsSeen.Add(new DevOpsAssignmentSeen { UserId = userId, ExternalId = t.ExternalId });
            _notifications.Notify(userId, NotificationKind.DevOpsAssigned,
                $"Te asignaron el ticket #{t.ExternalId}",
                $"{t.WorkItemType}: {t.Title}", t.Url, dedupeKey: $"devops-assign:{t.ExternalId}");
        }
        if (nuevos.Count > 0) _db.SaveChanges();
        return nuevos;
    }

    /// <summary>WIQL de sincronización: base (no removidos) + filtros opcionales por tipo, estado y persona.</summary>
    public static string BuildSyncWiql(string project, DevOpsSyncFilter? filter)
    {
        static string Esc(string s) => s.Replace("'", "''");
        var sb = new StringBuilder();
        sb.Append("SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '").Append(Esc(project)).Append('\'');
        sb.Append(" AND [System.State] <> 'Removed'");
        if (filter is { IsEmpty: false })
        {
            if (filter.Types.Count > 0)
                sb.Append(" AND [System.WorkItemType] IN (")
                  .Append(string.Join(",", filter.Types.Select(t => $"'{Esc(t)}'"))).Append(')');
            if (filter.States.Count > 0)
                sb.Append(" AND [System.State] IN (")
                  .Append(string.Join(",", filter.States.Select(st => $"'{Esc(st)}'"))).Append(')');
            if (filter.Assignees.Count > 0)
                sb.Append(" AND [System.AssignedTo] IN (")
                  .Append(string.Join(",", filter.Assignees.Select(a => $"'{Esc(a)}'"))).Append(')');
            // @Me lo resuelve DevOps contra el dueño del PAT: no hay nombre ni correo que entrecomillar,
            // y por eso mismo no falla cuando el correo de la ficha no coincide con el de la cuenta.
            if (filter.SoloMisAsignados)
                sb.Append(" AND [System.AssignedTo] = @Me");
            // @Today - N es aritmética de fechas de WIQL; el número va sin comillas.
            if (filter.CambiadosEnDias is int dias && dias > 0)
                sb.Append(" AND [System.ChangedDate] >= @Today - ").Append(dias);
        }
        sb.Append(" ORDER BY [System.ChangedDate] DESC");
        return sb.ToString();
    }

    private static string Recortar(string s) => string.IsNullOrEmpty(s) ? "(sin detalle)" : (s.Length <= 300 ? s : s[..300]);

    private static RequirementStatus MapStatus(string state) => state.ToLowerInvariant() switch
    {
        "new" or "to do" or "proposed"                              => RequirementStatus.PorEstimar,
        "approved" or "committed" or "backlog"                      => RequirementStatus.Estimado,
        "active" or "doing" or "in progress" or "in development"    => RequirementStatus.EnDesarrollo,
        "testing" or "in testing" or "ready for test"               => RequirementStatus.EnPruebas,
        "resolved" or "ready for release"                           => RequirementStatus.PorEntregar,
        "closed" or "done" or "completed"                           => RequirementStatus.Entregado,
        "removed" or "cancelled" or "canceled"                      => RequirementStatus.Cancelado,
        _                                                           => RequirementStatus.PorEstimar
    };

    /// <summary>
    /// Resuelve con qué credenciales se habla con DevOps.
    ///
    /// El PAT es PERSONAL: se toma primero el del equipo de quien está usando la aplicación
    /// (<see cref="LocalDevOpsConfig"/>, cifrado con DPAPI) y solo si no hay se cae al configurado
    /// en la instalación. Así los comentarios que publica la aplicación quedan firmados en DevOps
    /// por la persona que los escribió y no por una cuenta compartida — que es justo lo que un SLA
    /// necesita poder probar.
    ///
    /// La organización y el proyecto no son secretos y salen de la configuración compartida, salvo
    /// que la persona los sobrescriba localmente.
    /// </summary>
    private (string orgUrl, string project, string pat) GetConfig()
    {
        var local = LocalDevOpsConfig.Load();

        var orgUrl  = (local.OrgUrlOverride ?? _settings.Get(SettingsService.Keys.AzureDevOpsOrgUrl))?.TrimEnd('/');
        var project = local.ProjectOverride ?? _settings.Get(SettingsService.Keys.AzureDevOpsProject);
        var pat     = local.Pat ?? _settings.Get(SettingsService.Keys.AzureDevOpsPat);

        if (string.IsNullOrEmpty(orgUrl) || string.IsNullOrEmpty(project))
            throw new InvalidOperationException(
                "Falta la URL de organización o el proyecto de Azure DevOps. Pídelo al administrador.");

        if (string.IsNullOrEmpty(pat))
            throw new InvalidOperationException(
                "No tienes un PAT de Azure DevOps configurado en este equipo. " +
                "Captúralo en «Mi PAT de DevOps» para que los comentarios queden a tu nombre.");

        return (orgUrl, project, pat);
    }

    /// <summary>true si esta persona ya tiene su propio PAT capturado en este equipo.</summary>
    public static bool TienePatPersonal => LocalDevOpsConfig.Load().TienePat;

    /// <summary>
    /// Comprueba que el PAT sirva, pidiendo el work item indicado. Devuelve el nombre con el que
    /// DevOps identifica a quien lo usa, para que la persona confirme que es su propia cuenta.
    /// </summary>
    public async Task<(bool ok, string mensaje)> ProbarCredencialesAsync(CancellationToken ct = default)
    {
        try
        {
            var (orgUrl, project, pat) = GetConfig();
            using var client = BuildClient(pat);

            // Se valida pidiendo el PROYECTO configurado, NO «/_apis/connectionData». Ese endpoint
            // devuelve HTTP 400 en api-version 7.0 con los PAT de formato nuevo (respaldados por
            // Entra, del estilo «...JQQJ99...AZDO...»), y hacía que «Probar conexión» fallara con
            // tokens perfectamente válidos —que sí sirven para leer work items y publicar
            // comentarios—. Pedir el proyecto confirma de una vez las tres cosas que la app
            // necesita: que el PAT sirve, que la organización responde y que ese proyecto es
            // visible para el PAT; y permite distinguir «PAT inválido» de «proyecto equivocado».
            var resp = await client.GetAsync(
                $"{orgUrl}/_apis/projects/{Uri.EscapeDataString(project)}?api-version=7.0", ct);

            // Azure DevOps responde 401 o, en algunos casos, 203 (página de inicio de sesión)
            // cuando el token no autentica.
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized
                || resp.StatusCode == System.Net.HttpStatusCode.NonAuthoritativeInformation)
                return (false, "El PAT es inválido, expiró o no tiene permiso de lectura. " +
                               "Genera uno nuevo en Azure DevOps con «Work Items → Read & write».");

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (false, $"La organización responde, pero no se encontró el proyecto «{project}» " +
                               "(o tu PAT no tiene acceso a él). Revísalo con el administrador.");

            if (!resp.IsSuccessStatusCode)
                return (false, $"DevOps respondió {(int)resp.StatusCode} {resp.ReasonPhrase}. Revisa el PAT y sus permisos.");

            // DevOps identifica al dueño del token en este encabezado, con formato «{id}:{cuenta}»,
            // para que la persona confirme que es SU propia cuenta y no la de otro.
            string? cuenta = null;
            if (resp.Headers.TryGetValues("X-VSS-UserData", out var vals))
            {
                var raw = vals.FirstOrDefault();
                var idx = raw?.IndexOf(':') ?? -1;
                if (idx >= 0 && idx < raw!.Length - 1) cuenta = raw[(idx + 1)..];
            }

            return (true, string.IsNullOrWhiteSpace(cuenta)
                ? $"Conexión correcta con el proyecto {project}."
                : $"Conectado como «{cuenta}» en el proyecto {project}.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static async Task<List<int>> GetIdsAsync(HttpClient client, string orgUrl, string project, string? wiql, CancellationToken ct)
    {
        var query = wiql ?? $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{project}' AND [System.State] <> 'Removed' ORDER BY [System.ChangedDate] DESC";
        var body  = JsonSerializer.Serialize(new { query });
        var resp  = await client.PostAsync(
            $"{orgUrl}/{Uri.EscapeDataString(project)}/_apis/wit/wiql?api-version=7.0",
            new StringContent(body, Encoding.UTF8, "application/json"), ct);

        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("PAT inválido o sin permisos.");
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("Organización o proyecto no encontrado.");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("workItems")
            .EnumerateArray()
            .Select(wi => wi.GetProperty("id").GetInt32())
            .ToList();
    }

    private static HttpClient BuildClient(string pat)
    {
        var client  = new HttpClient();
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static IEnumerable<IEnumerable<T>> Batch<T>(IEnumerable<T> src, int size)
    {
        var list = src.ToList();
        for (int i = 0; i < list.Count; i += size)
            yield return list.Skip(i).Take(size);
    }

    /// <summary>
    /// Lee «System.AssignedTo» y devuelve (nombreParaMostrar, correo). En la API el campo puede
    /// venir como objeto {displayName, uniqueName, ...}, como texto suelto o ausente.
    /// </summary>
    private static (string display, string email) ReadAssignedTo(JsonElement fields)
    {
        if (!fields.TryGetProperty("System.AssignedTo", out var at)) return ("", "");
        if (at.ValueKind == JsonValueKind.Object)
        {
            var display = at.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";
            var email   = at.TryGetProperty("uniqueName",  out var un) ? un.GetString() ?? "" : "";
            return (display, email);
        }
        if (at.ValueKind == JsonValueKind.String) return (at.GetString() ?? "", "");
        return ("", "");
    }

    private static string GetString(JsonElement fields, string key)
    {
        if (fields.TryGetProperty(key, out var v))
        {
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
            if (v.ValueKind == JsonValueKind.Number) return v.GetRawText();
        }
        return "";
    }

    private static string? GetStringOrNull(JsonElement fields, string key)
    {
        if (fields.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }

    private static double? GetDouble(JsonElement fields, string key)
    {
        if (fields.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetDouble();
        return null;
    }

    private static int GetInt(JsonElement fields, string key)
    {
        if (fields.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetInt32();
        return 0;
    }

    private static DateTime? GetDate(JsonElement fields, string key)
    {
        if (fields.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(v.GetString(), out var dt))
                return dt.ToUniversalTime();
        }
        return null;
    }
}
