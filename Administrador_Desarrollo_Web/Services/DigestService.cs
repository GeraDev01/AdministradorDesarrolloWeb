using System.Text;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Cifras del resumen que se envía por correo.</summary>
public record DigestData(int SlaVencidos, int SlaPorVencer24, int SlaActivos, int SugerenciasNuevas,
    int DesplieguesUlt24, int TicketsSyncUlt24, int ReqPorEntregar7d);

/// <summary>
/// Resumen por correo (digest) diario/semanal al jefe: SLA por vencer/vencidos, sugerencias sin
/// atender, despliegues y tickets del día, y entregas próximas. Se dispara desde el latido de fondo,
/// idempotente por período. Composición y destinatarios son puros/testeables; el envío usa EmailService.
/// </summary>
public class DigestService
{
    public const string KeyEnabled    = "DigestEnabled";
    public const string KeyFrequency  = "DigestFrequencyDays";
    public const string KeyRecipients = "DigestRecipients";
    public const string KeyLastRun    = "DigestLastRunUtc";

    private readonly SettingsService _settings;
    private readonly EmailService _email;
    private readonly DbContextOptions<AppDbContext> _dbOptions;
    private readonly AuditService _audit;

    private DateTime _ultimoIntento = DateTime.MinValue;

    public DigestService(SettingsService settings, EmailService email, DbContextOptions<AppDbContext> dbOptions, AuditService audit)
    {
        _settings = settings; _email = email; _dbOptions = dbOptions; _audit = audit;
    }

    public bool Habilitado => _settings.Get(KeyEnabled) == "true"
                           && _settings.Get(SettingsService.Keys.EmailEnabled) == "true";

    /// <summary>Envía el resumen si toca. Devuelve true si se envió. Marca el período solo tras el éxito.</summary>
    public async Task<bool> RevisarYEnviarAsync(CancellationToken ct = default)
    {
        if (!Habilitado) return false;

        var destinos = Destinatarios(
            _settings.Get(KeyRecipients),
            _settings.Get(SettingsService.Keys.SlaEscalationEmail),
            _settings.Get(SettingsService.Keys.EmailAddress));
        if (destinos.Count == 0) return false;

        int freq = int.TryParse(_settings.Get(KeyFrequency), out var f) && f > 0 ? f : 1;
        var last = LeerFecha(_settings.Get(KeyLastRun));
        if ((DateTime.UtcNow - last).TotalDays < freq) return false;
        if ((DateTime.UtcNow - _ultimoIntento).TotalMinutes < 60) return false;

        _ultimoIntento = DateTime.UtcNow;

        // Contexto FRESCO: no se reusa el singleton de la UI para leer desde un temporizador.
        DigestData data;
        await using (var db = new AppDbContext(_dbOptions))
            data = Recopilar(db, DateTime.UtcNow);

        // Si no hay absolutamente nada que reportar, no se envía un correo vacío; se marca el período
        // para no volver a revisar hasta el siguiente.
        if (!HayAlgoQueReportar(data))
        {
            _settings.Set(KeyLastRun, DateTime.UtcNow.ToString("o"));
            return false;
        }

        var cuerpo = Componer(data, DateTime.Now, freq);
        try
        {
            await _email.SendAsync(destinos, $"Resumen del equipo — {DateTime.Now:dd/MM/yyyy}", cuerpo, ct: ct);
        }
        catch (Exception ex)
        {
            _audit.RecordDetailed(AuditAction.Update, "Digest", null,
                $"Envío del resumen por correo FALLÓ: {ex.Message}", AuditOutcome.Fallo);
            throw;
        }
        _settings.Set(KeyLastRun, DateTime.UtcNow.ToString("o"));
        return true;
    }

    /// <summary>true si alguna cifra es distinta de cero (algo que valga la pena enviar).</summary>
    public static bool HayAlgoQueReportar(DigestData d) =>
        d.SlaVencidos + d.SlaPorVencer24 + d.SlaActivos + d.SugerenciasNuevas
        + d.DesplieguesUlt24 + d.TicketsSyncUlt24 + d.ReqPorEntregar7d > 0;

    /// <summary>Reúne las cifras del resumen desde la base.</summary>
    public static DigestData Recopilar(AppDbContext db, DateTime nowUtc)
    {
        var hace24 = nowUtc.AddHours(-24);
        var hoy = DateTime.Today;

        var activos = db.SlaCommitments.AsNoTracking().Where(s => s.Status == SlaStatus.Activo).Select(s => s.DueAtUtc).ToList();
        int vencidos = activos.Count(due => due < nowUtc) + db.SlaCommitments.Count(s => s.Status == SlaStatus.Vencido);
        int porVencer = activos.Count(due => due >= nowUtc && due <= nowUtc.AddHours(24));

        return new DigestData(
            SlaVencidos:       vencidos,
            SlaPorVencer24:    porVencer,
            SlaActivos:        activos.Count,
            SugerenciasNuevas: db.Suggestions.Count(s => s.Status == SuggestionStatus.Nueva),
            DesplieguesUlt24:  db.DeploymentJobs.Count(j => j.StartedAt != null && j.StartedAt >= hace24),
            TicketsSyncUlt24:  db.DevOpsTickets.Count(t => t.SyncedAt >= hace24),
            ReqPorEntregar7d:  db.Requirements.Count(r => r.CommittedDeliveryDate != null
                                    && r.CommittedDeliveryDate >= hoy && r.CommittedDeliveryDate <= hoy.AddDays(7)
                                    && r.Status != RequirementStatus.Entregado && r.Status != RequirementStatus.Cancelado));
    }

    /// <summary>Arma el texto del correo (puro, para poder probarlo).</summary>
    public static string Componer(DigestData d, DateTime fechaLocal, int frecuenciaDias)
    {
        var sb = new StringBuilder();
        sb.AppendLine(frecuenciaDias >= 7 ? "Resumen semanal del equipo" : "Resumen del equipo");
        sb.AppendLine($"{fechaLocal:dddd dd/MM/yyyy HH:mm}");
        sb.AppendLine();
        sb.AppendLine("SLA (compromisos de atención):");
        sb.AppendLine($"  • {d.SlaVencidos} vencido(s)");
        sb.AppendLine($"  • {d.SlaPorVencer24} por vencer en las próximas 24 h");
        sb.AppendLine($"  • {d.SlaActivos} activo(s) en total");
        sb.AppendLine();
        sb.AppendLine("Trabajo:");
        sb.AppendLine($"  • {d.ReqPorEntregar7d} requerimiento(s) por entregar en los próximos 7 días");
        sb.AppendLine($"  • {d.TicketsSyncUlt24} ticket(s) de DevOps sincronizados en las últimas 24 h");
        sb.AppendLine($"  • {d.DesplieguesUlt24} despliegue(s) iniciados en las últimas 24 h");
        sb.AppendLine();
        sb.AppendLine("Sugerencias:");
        sb.AppendLine($"  • {d.SugerenciasNuevas} sin atender");
        sb.AppendLine();
        sb.AppendLine("— Generado automáticamente por Administrador de Desarrollo Web.");
        return sb.ToString();
    }

    /// <summary>Destinatarios: los configurados; si no hay, el buzón de escalamiento de SLA; si no, la propia cuenta.</summary>
    public static List<string> Destinatarios(string? recipients, string? escalation, string? propio)
    {
        var lista = Split(recipients);
        if (lista.Count > 0) return lista;
        lista = Split(escalation);
        if (lista.Count > 0) return lista;
        return string.IsNullOrWhiteSpace(propio) || !propio.Contains('@') ? [] : [propio.Trim()];
    }

    private static List<string> Split(string? s) =>
        (s ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Where(x => x.Contains('@'))
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .ToList();

    private static DateTime LeerFecha(string? s) =>
        DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue;
}
