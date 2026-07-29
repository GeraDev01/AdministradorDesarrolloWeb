using Administrador_Desarrollo_Web.Models;
using Microsoft.Extensions.Logging;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Envío de los avisos de SLA por correo. Está separado de <see cref="SlaService"/> porque las
/// reglas del compromiso deben poder probarse sin tocar SMTP, y porque un fallo de correo no debe
/// impedir que el SLA se marque como vencido.
///
/// Nada de esto revienta la aplicación: si el correo está mal configurado se registra y se sigue.
/// El aviso dentro de la app es el canal que siempre funciona.
/// </summary>
public class SlaNotificationService
{
    private readonly SlaService _sla;
    private readonly EmailService _email;
    private readonly SettingsService _settings;
    private readonly ILogger<SlaNotificationService> _logger;

    public SlaNotificationService(SlaService sla, EmailService email, SettingsService settings,
        ILogger<SlaNotificationService> logger)
    {
        _sla = sla; _email = email; _settings = settings; _logger = logger;
    }

    private bool CorreoHabilitado => _settings.Get(SettingsService.Keys.EmailEnabled) == "true";

    /// <summary>
    /// Buzones que reciben los escalamientos: el/los del jefe configurados en Configuración → Correo,
    /// separados por «;» o «,». Si no se configuró ninguno, se cae a la propia cuenta de la
    /// aplicación para que el aviso no se pierda.
    /// </summary>
    private List<string> BuzonesEscalamiento()
    {
        var configurado = _settings.Get(SettingsService.Keys.SlaEscalationEmail);
        var destinos = (configurado ?? "")
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(d => d.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (destinos.Count > 0) return destinos;

        var propio = _settings.Get(SettingsService.Keys.EmailAddress);
        return string.IsNullOrWhiteSpace(propio) ? [] : [propio];
    }

    /// <summary>
    /// Avisa por correo al desarrollador de los SLA que necesitan que comente el ticket.
    /// Devuelve cuántos correos se enviaron.
    /// </summary>
    public async Task<int> AvisarDesarrolladorAsync(int developerId, CancellationToken ct = default)
    {
        if (!CorreoHabilitado) return 0;

        var pendientes = _sla.PendientesDeAviso(developerId);
        if (pendientes.Count == 0) return 0;

        var correo = pendientes[0].Developer?.Email;
        if (string.IsNullOrWhiteSpace(correo))
        {
            _logger.LogInformation("El desarrollador {id} no tiene correo; solo se le avisa dentro de la app.", developerId);
            return 0;
        }

        var ahora = DateTime.UtcNow;
        var lineas = pendientes.Select(s =>
        {
            var vencido = s.EstaVencido(ahora);
            var ticket = s.DevOpsTicketExternalId is int t ? $"  ·  ticket #{t}" : "";
            return $"  {(vencido ? "[VENCIDO]" : "[por vencer]")} {SlaService.DescribirObjetivo(s)}\n" +
                   $"      límite: {s.DueAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}{ticket}";
        });

        var cuerpo =
            $"Hola {pendientes[0].Developer?.FullName}:\n\n" +
            "Tienes compromisos que requieren que dejes un comentario de avance en su ticket de Azure DevOps:\n\n" +
            string.Join("\n\n", lineas) +
            "\n\nPuedes comentarlos desde la aplicación, en «Mis SLA».\n\n— Administrador de Desarrollo";

        try
        {
            await _email.SendAsync([correo!], $"SLA: {pendientes.Count} pendiente(s) por comentar", cuerpo, ct: ct);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo enviar el aviso de SLA a {correo}", correo);
            return 0;
        }
    }

    /// <summary>
    /// Marca vencimientos y escala por correo a administración. Solo confirma el aviso si el
    /// correo salió: si falla, el SLA se reintentará en la siguiente revisión.
    /// </summary>
    public async Task<int> EscalarIncumplimientosAsync(CancellationToken ct = default)
    {
        var vencidos = _sla.RevisarVencimientos();
        if (vencidos.Count == 0) return 0;

        var destinos = BuzonesEscalamiento();
        if (!CorreoHabilitado || destinos.Count == 0)
        {
            _logger.LogInformation("{n} SLA vencido(s); sin correo configurado, solo quedan en la aplicación.", vencidos.Count);
            return vencidos.Count;
        }

        var lineas = vencidos.Select(s =>
            $"  · {s.Developer?.FullName ?? "?"} — {SlaService.DescribirObjetivo(s)}\n" +
            $"      venció: {s.DueAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}" +
            (s.LastCommentAtUtc is DateTime c
                ? $"  ·  último comentario: {c.ToLocalTime():dd/MM/yyyy HH:mm}"
                : "  ·  sin comentarios en el ticket"));

        var cuerpo =
            $"Se vencieron {vencidos.Count} compromiso(s) de atención:\n\n" +
            string.Join("\n\n", lineas) +
            "\n\n— Administrador de Desarrollo";

        try
        {
            await _email.SendAsync(destinos, $"SLA vencido: {vencidos.Count} compromiso(s)", cuerpo, ct: ct);
            _sla.MarcarIncumplimientoNotificado(vencidos.Select(v => v.Id));
        }
        catch (Exception ex)
        {
            // Sin marcar como notificado: el próximo ciclo lo reintenta.
            _logger.LogWarning(ex, "No se pudo escalar el incumplimiento de SLA a {destinos}", string.Join(";", destinos));
        }
        return vencidos.Count;
    }
}
