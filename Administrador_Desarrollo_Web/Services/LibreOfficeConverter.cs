using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Convierte DOCX→PDF invocando LibreOffice en modo headless
/// (soffice --headless --convert-to pdf). Gratis (MPL), sin Office.
/// La ruta de soffice.exe se toma de la configuración (LibreOfficePath) o se
/// autodetecta en las rutas de instalación típicas de Windows.
/// </summary>
public class LibreOfficeConverter : IDocxToPdfConverter
{
    private readonly SettingsService _settings;
    private readonly ILogger<LibreOfficeConverter> _logger;
    // soffice usa una sola instancia por perfil: serializamos las conversiones.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly string[] KnownPaths =
    {
        @"C:\Program Files\LibreOffice\program\soffice.exe",
        @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
    };

    public LibreOfficeConverter(SettingsService settings, ILogger<LibreOfficeConverter> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public string? ResolveSofficePath()
    {
        var configured = _settings.Get(SettingsService.Keys.LibreOfficePath);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        foreach (var p in KnownPaths)
            if (File.Exists(p)) return p;

        return null;
    }

    public bool IsAvailable(out string? diagnostic)
    {
        var path = ResolveSofficePath();
        if (path == null)
        {
            diagnostic = "No se encontró LibreOffice. Instálalo (https://es.libreoffice.org/descarga/) " +
                         "o indica la ruta de soffice.exe en Configuración.";
            return false;
        }
        diagnostic = null;
        return true;
    }

    public async Task<byte[]> ConvertAsync(byte[] docxBytes, CancellationToken ct = default)
    {
        var soffice = ResolveSofficePath()
            ?? throw new InvalidOperationException(
                "No se encontró LibreOffice (soffice.exe). Instálalo o configura su ruta en Configuración.");

        var work = Path.Combine(Path.GetTempPath(), "advweb_docx_" + Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(work, "profile");
        Directory.CreateDirectory(work);
        Directory.CreateDirectory(profile);
        var inputPath = Path.Combine(work, "solicitud.docx");
        var outputPath = Path.Combine(work, "solicitud.pdf");

        await File.WriteAllBytesAsync(inputPath, docxBytes, ct);

        await _gate.WaitAsync(ct);
        try
        {
            // Perfil de usuario aislado: no choca con una instancia de LibreOffice ya abierta.
            var profileUri = new Uri(profile).AbsoluteUri;
            var psi = new ProcessStartInfo
            {
                FileName = soffice,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            psi.ArgumentList.Add($"-env:UserInstallation={profileUri}");
            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add("--norestore");
            psi.ArgumentList.Add("--nologo");
            psi.ArgumentList.Add("--convert-to");
            psi.ArgumentList.Add("pdf:writer_pdf_Export");
            psi.ArgumentList.Add("--outdir");
            psi.ArgumentList.Add(work);
            psi.ArgumentList.Add(inputPath);

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            string stderr = await proc.StandardError.ReadToEndAsync(timeout.Token);
            await proc.WaitForExitAsync(timeout.Token);

            if (!File.Exists(outputPath))
                throw new InvalidOperationException(
                    $"LibreOffice no generó el PDF (código {proc.ExitCode}). {stderr}".Trim());

            var pdf = await File.ReadAllBytesAsync(outputPath, ct);
            if (pdf.Length == 0)
                throw new InvalidOperationException("LibreOffice generó un PDF vacío.");
            return pdf;
        }
        finally
        {
            _gate.Release();
            TryCleanup(work);
        }
    }

    private void TryCleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { _logger.LogDebug(ex, "No se pudo limpiar {dir}", dir); }
    }
}
