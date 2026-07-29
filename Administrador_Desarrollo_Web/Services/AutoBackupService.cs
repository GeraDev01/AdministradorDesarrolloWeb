using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Respaldo AUTOMÁTICO de la base a Blob Storage, según la configuración (activado + cada N días). Solo
/// aplica cuando la base es el SQLite local (un respaldo de archivo no tiene sentido con Azure SQL).
/// Se dispara desde el latido de fondo; es idempotente por período y no reintenta a cada rato si falla.
/// </summary>
public class AutoBackupService
{
    public const string KeyEnabled   = "AutoBackupEnabled";
    public const string KeyFrequency = "AutoBackupFrequencyDays";
    public const string KeyLastRun   = "AutoBackupLastRunUtc";

    private readonly SettingsService _settings;
    private readonly BackupService _backup;
    private readonly DbConnectionChoice _dbChoice;
    private readonly BlobStorageService _blob;
    private readonly AuditService _audit;

    private DateTime _ultimoIntento = DateTime.MinValue;

    public AutoBackupService(SettingsService settings, BackupService backup, DbConnectionChoice dbChoice, BlobStorageService blob, AuditService audit)
    {
        _settings = settings; _backup = backup; _dbChoice = dbChoice; _blob = blob; _audit = audit;
    }

    public bool Habilitado => _settings.Get(KeyEnabled) == "true";

    /// <summary>Solo aplica para SQLite local con Blob configurado (para poder informarlo en la UI).</summary>
    public bool Aplicable => Habilitado && !_dbChoice.UsaSqlServer && _blob.IsConfigured;

    /// <summary>
    /// Respalda si toca (activado, SQLite, Blob configurado y ya pasó el período). Devuelve true si
    /// respaldó ahora. Marca la última corrida SOLO tras el éxito; entre intentos deja pasar al menos
    /// una hora para no golpear cada latido si algo falla.
    /// </summary>
    public async Task<bool> RevisarYRespaldarAsync(CancellationToken ct = default)
    {
        if (!Aplicable) return false;

        int freq = int.TryParse(_settings.Get(KeyFrequency), out var f) && f > 0 ? f : 1;
        var last = LeerFecha(_settings.Get(KeyLastRun));
        if ((DateTime.UtcNow - last).TotalDays < freq) return false;             // ya se respaldó este período
        if ((DateTime.UtcNow - _ultimoIntento).TotalMinutes < 60) return false;  // no reintentar tan seguido

        _ultimoIntento = DateTime.UtcNow;
        try
        {
            await _backup.BackupToAzureAsync(new Progress<string>(_ => { }), ct); // audita AuditAction.Backup al terminar
        }
        catch (Exception ex)
        {
            // La UI se limita a registrar el fallo en Debug (invisible en Release): dejamos constancia
            // en la BITÁCORA para que un respaldo que lleva días fallando no pase inadvertido.
            _audit.RecordDetailed(AuditAction.Backup, "AppDb", null,
                $"Respaldo automático FALLÓ: {ex.Message}", AuditOutcome.Fallo);
            throw;
        }
        _settings.Set(KeyLastRun, DateTime.UtcNow.ToString("o"));                 // solo tras éxito
        return true;
    }

    private static DateTime LeerFecha(string? s) =>
        DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue;
}
