using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Services;

public class BackupService
{
    private readonly BlobStorageService _blob;
    private readonly AuditService _audit;

    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AdministradorDesarrolloWeb", "app.db");

    public BackupService(BlobStorageService blob, AuditService audit)
    {
        _blob = blob; _audit = audit;
    }

    public async Task<string> BackupToAzureAsync(
        IProgress<string> progress, CancellationToken ct = default)
    {
        if (!_blob.IsConfigured)
            throw new InvalidOperationException("Azure Blob Storage no configurado. Ve a Configuración.");

        if (!File.Exists(DbPath))
            throw new FileNotFoundException($"Base de datos no encontrada: {DbPath}");

        progress.Report("📦  Iniciando respaldo de la base de datos...");
        var url = await _blob.BackupDatabaseAsync(DbPath, progress, ct);
        _audit.RecordSystem(AuditAction.Backup, $"BD respaldada en Azure Blob: {url}");
        progress.Report("✅  Respaldo completado.");
        return url;
    }

    public void BackupLocalCopy(string destinationFolder)
    {
        Directory.CreateDirectory(destinationFolder);
        var dest = Path.Combine(destinationFolder, $"app_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        File.Copy(DbPath, dest, overwrite: false);
        _audit.RecordSystem(AuditAction.Backup, $"Copia local: {dest}");
    }
}
