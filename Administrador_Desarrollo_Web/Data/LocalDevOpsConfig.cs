using System.Text.Json;
using Administrador_Desarrollo_Web.Security;

namespace Administrador_Desarrollo_Web.Data;

/// <summary>
/// Credenciales personales de Azure DevOps, guardadas EN EL EQUIPO de cada persona y cifradas con
/// DPAPI (ámbito del usuario de Windows).
///
/// Por qué no van en la base compartida:
///  · <b>Atribución</b>: cuando la aplicación publica un comentario en un work item, DevOps lo
///    firma con el dueño del PAT. Con un PAT compartido todos los comentarios aparecerían a nombre
///    de la misma cuenta y el SLA dejaría de probar quién atendió.
///  · <b>Permisos</b>: el login restringido con el que corren los desarrolladores no puede escribir
///    en AppSettings, así que ahí no tendrían dónde guardarlo.
///  · <b>Aislamiento</b>: un PAT es una credencial personal; nadie más debería poder leerlo, y
///    DPAPI de usuario garantiza que ni siquiera otra cuenta del mismo equipo lo descifre.
///
/// La URL de organización y el proyecto SÍ salen de la configuración compartida (no son secretos);
/// aquí solo se guardan si alguien necesita apuntar a otra organización.
/// </summary>
public class LocalDevOpsConfig
{
    public string? Pat { get; set; }
    public string? OrgUrlOverride { get; set; }
    public string? ProjectOverride { get; set; }

    public bool TienePat => !string.IsNullOrWhiteSpace(Pat);

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AdministradorDesarrolloWeb", "devops-personal.json");

    public static LocalDevOpsConfig Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new LocalDevOpsConfig();
            var raw = JsonSerializer.Deserialize<Raw>(File.ReadAllText(FilePath));
            if (raw == null) return new LocalDevOpsConfig();

            var cfg = new LocalDevOpsConfig
            {
                OrgUrlOverride = string.IsNullOrWhiteSpace(raw.OrgUrl) ? null : raw.OrgUrl,
                ProjectOverride = string.IsNullOrWhiteSpace(raw.Project) ? null : raw.Project
            };
            if (!string.IsNullOrEmpty(raw.Pat) && SecretProtector.TryUnprotect(raw.Pat, out var plain))
                cfg.Pat = plain;
            return cfg;
        }
        catch
        {
            // Archivo corrupto o de otra cuenta: se ignora y se pide capturar el PAT de nuevo.
            return new LocalDevOpsConfig();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);

        var raw = new Raw
        {
            Pat = string.IsNullOrWhiteSpace(Pat) ? "" : SecretProtector.Protect(Pat.Trim()),
            OrgUrl = OrgUrlOverride,
            Project = ProjectOverride
        };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Clear()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
    }

    private sealed class Raw
    {
        public string Pat { get; set; } = "";
        public string? OrgUrl { get; set; }
        public string? Project { get; set; }
    }
}
