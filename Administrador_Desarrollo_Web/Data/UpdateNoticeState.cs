using System.Text.Json;

namespace Administrador_Desarrollo_Web.Data;

/// <summary>
/// De qué versión ya se avisó EN ESTE EQUIPO, en
/// %APPDATA%\AdministradorDesarrolloWeb\actualizacion.json.
///
/// Va en disco y no en la base porque la pregunta es «¿esta máquina ya vio el aviso?», no «¿esta
/// persona?»: quien entra desde dos equipos debe enterarse en los dos, y el login restringido no
/// podría escribirlo en la base de todos modos. Mismo patrón que
/// <see cref="LibreOfficeLocalConfig"/> y <see cref="GridLayoutConfig"/>.
/// </summary>
public class UpdateNoticeState
{
    /// <summary>Texto de la última versión avisada, o null si nunca se avisó.</summary>
    public string? UltimaVersionAvisada { get; set; }

    private readonly string _ruta;

    public static string RutaPredeterminada =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AdministradorDesarrolloWeb", "actualizacion.json");

    /// <summary>La ruta se inyecta para poder probar con un archivo temporal.</summary>
    public UpdateNoticeState(string? ruta = null) => _ruta = ruta ?? RutaPredeterminada;

    public static UpdateNoticeState Cargar(string? ruta = null)
    {
        var estado = new UpdateNoticeState(ruta);
        try
        {
            if (!File.Exists(estado._ruta)) return estado;
            var raw = JsonSerializer.Deserialize<Raw>(File.ReadAllText(estado._ruta));
            estado.UltimaVersionAvisada = string.IsNullOrWhiteSpace(raw?.UltimaVersionAvisada)
                ? null : raw!.UltimaVersionAvisada!.Trim();
        }
        catch
        {
            // Archivo corrupto: en el peor caso se vuelve a avisar una vez. Jamás tirar el arranque
            // de la aplicación por un JSON de preferencias.
            estado.UltimaVersionAvisada = null;
        }
        return estado;
    }

    public void Guardar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_ruta)!);
        File.WriteAllText(_ruta, JsonSerializer.Serialize(
            new Raw { UltimaVersionAvisada = UltimaVersionAvisada },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class Raw
    {
        public string? UltimaVersionAvisada { get; set; }
    }
}
