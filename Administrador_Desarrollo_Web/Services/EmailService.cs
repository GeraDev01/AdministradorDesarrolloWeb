using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Logging;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Services;

public record EmailMessageInfo(uint Uid, string Subject, string From, DateTime Date, string Preview, string Body);

/// <summary>
/// Envío (SMTP) y lectura (IMAP) de correo con MailKit. Configurado con
/// dirección + contraseña de aplicación en Configuración. Permite enviar correos
/// y reportes, listar/organizar carpetas e ingerir requerimientos desde una carpeta.
/// </summary>
public class EmailService
{
    private readonly SettingsService _settings;
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly ILogger<EmailService> _logger;

    public EmailService(SettingsService settings, AppDbContext db, AuditService audit, ILogger<EmailService> logger)
    {
        _settings = settings; _db = db; _audit = audit; _logger = logger;
    }

    private sealed class Cfg
    {
        public string Address = "", DisplayName = "", Password = "", SmtpHost = "", ImapHost = "";
        public int SmtpPort = 587, ImapPort = 993;
    }

    private Cfg Load()
    {
        var c = new Cfg
        {
            Address     = _settings.Get(SettingsService.Keys.EmailAddress) ?? "",
            DisplayName = _settings.Get(SettingsService.Keys.EmailDisplayName) ?? "",
            Password    = _settings.Get(SettingsService.Keys.EmailPassword) ?? "",
            SmtpHost    = _settings.Get(SettingsService.Keys.EmailSmtpHost) ?? "",
            ImapHost    = _settings.Get(SettingsService.Keys.EmailImapHost) ?? "",
        };
        if (int.TryParse(_settings.Get(SettingsService.Keys.EmailSmtpPort), out var sp)) c.SmtpPort = sp;
        if (int.TryParse(_settings.Get(SettingsService.Keys.EmailImapPort), out var ip)) c.ImapPort = ip;
        if (string.IsNullOrWhiteSpace(c.DisplayName)) c.DisplayName = c.Address;
        return c;
    }

    public bool IsConfigured(out string? diagnostic)
    {
        if (_settings.Get(SettingsService.Keys.EmailEnabled) != "true")
        { diagnostic = "El correo no está habilitado. Actívalo en Configuración."; return false; }
        var c = Load();
        if (string.IsNullOrWhiteSpace(c.Address) || string.IsNullOrWhiteSpace(c.Password) || string.IsNullOrWhiteSpace(c.SmtpHost))
        { diagnostic = "Faltan datos de correo (dirección, contraseña o servidor SMTP) en Configuración."; return false; }
        diagnostic = null; return true;
    }

    public string RequirementsFolder => _settings.Get(SettingsService.Keys.EmailRequirementsFolder) ?? "INBOX";

    // ── Envío ────────────────────────────────────────────────────
    public async Task SendAsync(IEnumerable<string> to, string subject, string body, string? attachmentPath = null, CancellationToken ct = default)
    {
        var c = Load();
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(c.DisplayName, c.Address));
        foreach (var t in to.Where(x => !string.IsNullOrWhiteSpace(x)))
            msg.To.Add(MailboxAddress.Parse(t.Trim()));
        if (msg.To.Count == 0) throw new InvalidOperationException("No hay destinatarios válidos.");
        msg.Subject = subject;

        var builder = new BodyBuilder { TextBody = body };
        if (!string.IsNullOrWhiteSpace(attachmentPath) && File.Exists(attachmentPath))
            builder.Attachments.Add(attachmentPath);
        msg.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        var opt = c.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(c.SmtpHost, c.SmtpPort, opt, ct);
        await client.AuthenticateAsync(c.Address, c.Password, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);

        _audit.Record(AuditAction.Update, "Email", "", $"Correo enviado a {msg.To.Count} destinatario(s): {subject}");
    }

    public async Task<string> TestConnectionAsync(CancellationToken ct = default)
    {
        var c = Load();
        using (var smtp = new SmtpClient())
        {
            var opt = c.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await smtp.ConnectAsync(c.SmtpHost, c.SmtpPort, opt, ct);
            await smtp.AuthenticateAsync(c.Address, c.Password, ct);
            await smtp.DisconnectAsync(true, ct);
        }
        if (!string.IsNullOrWhiteSpace(c.ImapHost))
        {
            using var imap = new ImapClient();
            await imap.ConnectAsync(c.ImapHost, c.ImapPort, SecureSocketOptions.SslOnConnect, ct);
            await imap.AuthenticateAsync(c.Address, c.Password, ct);
            await imap.DisconnectAsync(true, ct);
        }
        return "Conexión exitosa (SMTP" + (string.IsNullOrWhiteSpace(c.ImapHost) ? "" : " + IMAP") + ").";
    }

    // ── Lectura (IMAP) ───────────────────────────────────────────
    public async Task<List<string>> ListFoldersAsync(CancellationToken ct = default)
    {
        var c = Load();
        using var client = new ImapClient();
        await client.ConnectAsync(c.ImapHost, c.ImapPort, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(c.Address, c.Password, ct);
        var names = new List<string> { "INBOX" };
        foreach (var ns in client.PersonalNamespaces)
            foreach (var f in await client.GetFoldersAsync(ns, cancellationToken: ct))
                if (!f.Attributes.HasFlag(FolderAttributes.NonExistent)) names.Add(f.FullName);
        await client.DisconnectAsync(true, ct);
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<EmailMessageInfo>> FetchRecentAsync(string folderName, int max = 40, CancellationToken ct = default)
    {
        var c = Load();
        using var client = new ImapClient();
        await client.ConnectAsync(c.ImapHost, c.ImapPort, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(c.Address, c.Password, ct);

        var folder = await OpenFolderAsync(client, folderName, FolderAccess.ReadOnly, ct);
        var result = new List<EmailMessageInfo>();
        int count = folder.Count;
        for (int i = count - 1; i >= 0 && result.Count < max; i--)
        {
            ct.ThrowIfCancellationRequested();
            var m = await folder.GetMessageAsync(i, ct);
            var uid = (await folder.FetchAsync(new[] { i }, MessageSummaryItems.UniqueId, ct)).FirstOrDefault()?.UniqueId.Id ?? 0;
            string bodyText = m.TextBody ?? StripHtml(m.HtmlBody) ?? "";
            result.Add(new EmailMessageInfo(uid, m.Subject ?? "(sin asunto)", m.From.ToString(), m.Date.LocalDateTime,
                Truncate(bodyText, 120), bodyText));
        }
        await client.DisconnectAsync(true, ct);
        return result;
    }

    /// <summary>Crea un requerimiento por cada correo no leído de la carpeta y lo marca como leído.</summary>
    public async Task<int> IngestRequirementsAsync(string folderName, CancellationToken ct = default)
    {
        var c = Load();
        using var client = new ImapClient();
        await client.ConnectAsync(c.ImapHost, c.ImapPort, SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(c.Address, c.Password, ct);

        var folder = await OpenFolderAsync(client, folderName, FolderAccess.ReadWrite, ct);
        var uids = await folder.SearchAsync(SearchQuery.NotSeen, ct);
        int created = 0;
        foreach (var uid in uids)
        {
            ct.ThrowIfCancellationRequested();
            var m = await folder.GetMessageAsync(uid, ct);
            string bodyText = m.TextBody ?? StripHtml(m.HtmlBody) ?? "";
            _db.Requirements.Add(new Requirement
            {
                Title = Truncate(string.IsNullOrWhiteSpace(m.Subject) ? "(sin asunto)" : m.Subject.Trim(), 200),
                Description = $"De: {m.From}\nFecha: {m.Date.LocalDateTime:dd/MM/yyyy HH:mm}\n\n{bodyText}".Trim(),
                Source = RequirementSource.Email,
                Status = RequirementStatus.PorEstimar,
                CreatedAt = DateTime.UtcNow,
                StatusChangedAt = DateTime.UtcNow
            });
            await folder.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
            created++;
        }
        if (created > 0)
        {
            _db.SaveChanges();
            _audit.Record(AuditAction.Create, "Requirement", "", $"{created} requerimiento(s) importado(s) desde correo (carpeta {folderName})");
        }
        await client.DisconnectAsync(true, ct);
        return created;
    }

    private static async Task<IMailFolder> OpenFolderAsync(ImapClient client, string folderName, FolderAccess access, CancellationToken ct)
    {
        IMailFolder folder;
        if (string.IsNullOrWhiteSpace(folderName) || folderName.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
            folder = client.Inbox;
        else
        {
            try { folder = await client.GetFolderAsync(folderName, ct); }
            catch { folder = client.Inbox; }
        }
        await folder.OpenAsync(access, ct);
        return folder;
    }

    private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }
}
