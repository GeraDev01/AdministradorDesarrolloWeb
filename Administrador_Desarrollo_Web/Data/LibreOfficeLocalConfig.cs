using System.Text.Json;

namespace Administrador_Desarrollo_Web.Data;

/// <summary>
/// Dónde está LibreOffice EN ESTA computadora. Se guarda por usuario de Windows, en
/// %APPDATA%\AdministradorDesarrolloWeb\libreoffice.json, igual que <see cref="LocalDevOpsConfig"/>
/// y <see cref="GridLayoutConfig"/>.
///
/// Por qué no va en la base compartida: la ruta de soffice.exe es un dato POR MÁQUINA — cada quien
/// lo instaló donde pudo — y además el login restringido con el que corren los desarrolladores
/// tiene DENY de escritura sobre AppSettings, así que ahí no tendrían dónde guardarla. El valor de
/// AppSettings se conserva como ruta POR OMISIÓN del equipo; esta, si existe, gana en esta máquina.
///
/// Sin DPAPI a propósito: una ruta de archivo no es un secreto.
/// </summary>
public class LibreOfficeLocalConfig
{
    /// <summary>Ruta completa de soffice.exe elegida por la persona, o null si nunca configuró.</summary>
    public string? SofficePath { get; set; }

    private readonly string _ruta;

    public static string RutaPredeterminada =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AdministradorDesarrolloWeb", "libreoffice.json");

    /// <summary>La ruta se inyecta para poder probar con un archivo temporal.</summary>
    public LibreOfficeLocalConfig(string? ruta = null) => _ruta = ruta ?? RutaPredeterminada;

    public static LibreOfficeLocalConfig Cargar(string? ruta = null)
    {
        var cfg = new LibreOfficeLocalConfig(ruta);
        try
        {
            if (!File.Exists(cfg._ruta)) return cfg;
            var raw = JsonSerializer.Deserialize<Raw>(File.ReadAllText(cfg._ruta));
            cfg.SofficePath = string.IsNullOrWhiteSpace(raw?.SofficePath) ? null : raw!.SofficePath.Trim();
        }
        catch
        {
            // Archivo corrupto: se ignora y la persona vuelve a configurar. Jamás tirar la app
            // por un JSON de preferencias.
            cfg.SofficePath = null;
        }
        return cfg;
    }

    public void Guardar()
    {
        var dir = Path.GetDirectoryName(_ruta)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(_ruta, JsonSerializer.Serialize(
            new Raw { SofficePath = SofficePath }, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Si la ruta apunta a un soffice de verdad: existe y se llama soffice.exe o soffice.com.
    /// Validar el nombre evita el error clásico de elegir cualquier .exe en el diálogo de abrir;
    /// que además funcione lo confirma el botón «Probar» (esto no lanza ningún proceso).
    /// </summary>
    public static bool EsSofficeValido(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta) || !File.Exists(ruta)) return false;
        var nombre = Path.GetFileName(ruta);
        return nombre.Equals("soffice.exe", StringComparison.OrdinalIgnoreCase)
            || nombre.Equals("soffice.com", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Raw
    {
        public string? SofficePath { get; set; }
    }
}
