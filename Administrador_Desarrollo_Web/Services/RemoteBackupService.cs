using System.IO.Compression;
using Administrador_Desarrollo_Web.Models;
using FluentFTP;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Respalda la carpeta remota de un servidor ANTES de desplegar y sube el ZIP al mismo Azure Blob
/// Storage ya configurado, bajo el prefijo <c>respaldos-despliegue/</c>.
///
/// Es lo que permite revertir: hasta ahora el despliegue sobrescribía el destino sin conservar
/// copia de lo que había, así que un despliegue malo no tenía vuelta atrás.
///
/// Regla deliberada: si el respaldo falla, <b>no se despliega</b>. Un respaldo que "se intentó" no
/// sirve de nada el día que hay que revertir, y desplegar creyendo que hay red de seguridad es peor
/// que desplegar sabiendo que no la hay.
/// </summary>
public class RemoteBackupService
{
    private readonly BlobStorageService _blob;
    private readonly SettingsService _settings;

    public RemoteBackupService(BlobStorageService blob, SettingsService settings)
    {
        _blob = blob; _settings = settings;
    }

    /// <summary>Permite al administrador desactivar el respaldo previo si asume el riesgo.</summary>
    public bool Habilitado => _settings.Get(SettingsService.Keys.DeployBackupEnabled) != "false";

    public async Task<string?> RespaldarAsync(DeploymentTarget target, string host, bool explicitTls,
        string password, int jobId, IProgress<string> progress, CancellationToken ct)
    {
        if (!Habilitado)
        {
            progress.Report("  ⚠  Respaldo previo DESACTIVADO en configuración: se despliega sin red de seguridad.");
            return null;
        }
        if (!_blob.IsConfigured)
            throw new InvalidOperationException(
                "No se puede respaldar antes de desplegar: falta configurar Azure Blob Storage. " +
                "Configúralo, o desactiva el respaldo previo si asumes el riesgo.");

        progress.Report($"  💾  Respaldando {target.RutaRemota} antes de sobrescribir…");

        var tempDir = Path.Combine(Path.GetTempPath(), $"bkp_{jobId}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var zipPath = Path.Combine(Path.GetTempPath(), $"{Sanitizar(target.Nombre)}_{DateTime.UtcNow:yyyy-MM-dd_HHmmss}.zip");

        try
        {
            int archivos = await FtpRetryPolicy.ConReintentosAsync(async intento =>
            {
                if (intento > 0) progress.Report($"      ↻ Reintentando la descarga del respaldo…");

                await using var client = new AsyncFtpClient(host, target.Usuario, password, target.Puerto);
                FtpRetryPolicy.Configure(client.Config, explicitTls);
                await client.AutoConnect(ct);

                // Carpeta remota vacía o inexistente: no hay nada que respaldar y no es un error
                // (es el primer despliegue a ese servidor).
                if (!await client.DirectoryExists(target.RutaRemota, ct))
                {
                    progress.Report("      · La carpeta remota no existe todavía: nada que respaldar.");
                    return 0;
                }

                var resultados = await client.DownloadDirectory(tempDir, target.RutaRemota,
                    FtpFolderSyncMode.Update, FtpLocalExists.Overwrite, FtpVerify.None, null, null, ct);

                var fallidos = resultados.Where(r => !r.IsSuccess).ToList();
                if (fallidos.Count > 0)
                    throw new IOException($"No se pudieron descargar {fallidos.Count} archivo(s) del respaldo (p.ej. {fallidos[0].Name}).");

                await client.Disconnect(ct);
                return resultados.Count(r => r.IsSuccess);
            }, FtpRetryPolicy.MaxSiteAttempts, progress, $"respaldo de {target.Nombre}", ct);

            if (archivos == 0) return null;

            await Task.Run(() => ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, false), ct);
            var tamano = new FileInfo(zipPath).Length;

            var blobName = $"{_blob.PrefijoRespaldosDespliegue}/{Sanitizar(target.Nombre)}/job{jobId}_{DateTime.UtcNow:yyyy-MM-dd_HHmmss}.zip";
            // Metadatos para poder identificar el respaldo sin abrirlo el día que haya que revertir.
            var meta = new Dictionary<string, string>
            {
                ["servidor"] = Sanitizar(target.Nombre),
                ["ruta_remota"] = target.RutaRemota,
                ["job"] = jobId.ToString(),
                ["archivos"] = archivos.ToString(),
                ["respaldado_utc"] = DateTime.UtcNow.ToString("O")
            };
            var url = await _blob.UploadFileAsync(zipPath, blobName, null, ct, meta);

            progress.Report($"  ✓  Respaldo subido: {archivos} archivos, {tamano / 1024d / 1024d:0.0} MB → {blobName}");
            return url;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
        }
    }

    private static string Sanitizar(string nombre)
    {
        var limpio = new string(nombre.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return limpio.Length == 0 ? "servidor" : limpio;
    }
}
