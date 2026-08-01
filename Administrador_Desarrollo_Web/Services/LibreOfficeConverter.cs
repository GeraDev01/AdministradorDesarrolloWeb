using System.Diagnostics;
using Administrador_Desarrollo_Web.Data;
using Microsoft.Extensions.Logging;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Convierte DOCX→PDF invocando LibreOffice en modo headless
/// (soffice --headless --convert-to pdf). Gratis (MPL), sin Office.
///
/// La ruta de soffice.exe se resuelve en este orden, y se re-resuelve en CADA llamada para que un
/// cambio de configuración aplique sin reiniciar:
///  1. la ruta PERSONAL de esta máquina (<see cref="LibreOfficeLocalConfig"/>) — cada quien lo
///     instaló donde pudo, y el login restringido no puede escribir la configuración compartida;
///  2. la ruta COMPARTIDA de AppSettings (LibreOfficePath) — el valor por omisión del equipo;
///  3. autodetección en las rutas de instalación típicas de Windows.
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

    /// <summary>
    /// La precedencia, pura y sin tocar disco ni base: así se prueba con archivos falsos en un
    /// directorio temporal (el patrón de DbConnectionResolver). Devuelve también DE DÓNDE salió la
    /// ruta: cuando algo falla, «no se encontró» a secas obliga a soportar a ciegas.
    /// </summary>
    public static (string? ruta, string origen) ResolverRuta(
        string? local, string? global, IEnumerable<string> conocidas, Func<string, bool> existe)
    {
        // La local rota NO gana: si alguien movió su LibreOffice, caer a la global o a la
        // autodetección deja a la persona trabajando en vez de atorada en su error de captura.
        if (!string.IsNullOrWhiteSpace(local) && existe(local!))
            return (local, "configurada en esta computadora");
        if (!string.IsNullOrWhiteSpace(global) && existe(global!))
            return (global, "configuración compartida del equipo");
        foreach (var p in conocidas)
            if (existe(p)) return (p, "autodetectada");
        return (null, "ninguna");
    }

    public string? ResolveSofficePath()
    {
        var (ruta, _) = ResolverRuta(
            LibreOfficeLocalConfig.Cargar().SofficePath,
            _settings.Get(SettingsService.Keys.LibreOfficePath),
            KnownPaths, File.Exists);
        return ruta;
    }

    public bool IsAvailable(out string? diagnostic)
    {
        var local = LibreOfficeLocalConfig.Cargar().SofficePath;
        var global = _settings.Get(SettingsService.Keys.LibreOfficePath);
        var (path, _) = ResolverRuta(local, global, KnownPaths, File.Exists);
        if (path == null)
        {
            // Decir qué se intentó: sin esto, una ruta capturada con error y una instalación
            // ausente se reportan igual y el soporte es a ciegas.
            var intentos = new List<string>();
            if (!string.IsNullOrWhiteSpace(local))  intentos.Add($"la ruta de esta computadora ({local})");
            if (!string.IsNullOrWhiteSpace(global)) intentos.Add($"la compartida ({global})");
            intentos.Add("las rutas típicas de instalación");

            diagnostic = $"No se encontró LibreOffice; se intentó: {string.Join(", ", intentos)}. " +
                         "Instálalo (https://es.libreoffice.org/descarga/) o indica dónde está soffice.exe.";
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
