using System.Text.Json;
using Administrador_Desarrollo_Web.Security;

namespace Administrador_Desarrollo_Web.Data;

public enum DbProvider { Sqlite, SqlServer }

public class DbProviderConfig
{
    public DbProvider Provider { get; set; } = DbProvider.Sqlite;
    public string SqlServerConnection { get; set; } = "";

    private static string ConfigFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AdministradorDesarrolloWeb", "dbprovider.json");

    /// <summary>
    /// Motivo por el que la configuración no se pudo cargar tal cual estaba guardada. Cuando no es
    /// null, quien llama DEBE avisar al usuario: antes cualquier fallo degradaba a SQLite en
    /// silencio y la aplicación seguía trabajando sobre la base local creyendo estar en SQL Server.
    /// </summary>
    public string? LoadError { get; private set; }

    /// <summary>
    /// true si existe dbprovider.json, es decir, si alguien eligió el proveedor a propósito en
    /// este equipo. Sirve para no imponerle la conexión incrustada a quien escogió SQLite.
    /// </summary>
    public bool ConfiguracionExplicita { get; private set; }

    public static DbProviderConfig Load()
    {
        var path = ConfigFilePath;
        if (!File.Exists(path)) return new DbProviderConfig();

        DbProviderConfigRaw? raw;
        try
        {
            raw = JsonSerializer.Deserialize<DbProviderConfigRaw>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            return new DbProviderConfig { LoadError = $"No se pudo leer {path}: {ex.Message}" };
        }

        if (raw == null)
            return new DbProviderConfig { LoadError = $"{path} está vacío o mal formado." };

        var declarado = Enum.TryParse<DbProvider>(raw.Provider, out var p) ? p : DbProvider.Sqlite;
        var cfg = new DbProviderConfig { Provider = declarado, ConfiguracionExplicita = true };

        if (!string.IsNullOrEmpty(raw.SqlServerConnection))
        {
            if (SecretProtector.TryUnprotect(raw.SqlServerConnection, out var plain))
                cfg.SqlServerConnection = plain;
            else
            {
                // DPAPI está atado al usuario de Windows: el archivo copiado de otro equipo o de
                // otra cuenta no se puede descifrar. Es exactamente el caso que hay que gritar.
                cfg.Provider = DbProvider.Sqlite;
                cfg.LoadError =
                    "La connection string guardada no se pudo descifrar (se cifra con la cuenta de " +
                    "Windows que la guardó). Vuelve a capturarla en Configuración → Base de datos.";
            }
        }
        else if (declarado == DbProvider.SqlServer)
        {
            cfg.Provider = DbProvider.Sqlite;
            cfg.LoadError = "Está configurado SQL Server pero no hay connection string guardada.";
        }

        return cfg;
    }

    public static void Save(DbProviderConfig cfg)
    {
        var dir = Path.GetDirectoryName(ConfigFilePath)!;
        Directory.CreateDirectory(dir);

        var raw = new DbProviderConfigRaw
        {
            Provider = cfg.Provider.ToString(),
            SqlServerConnection = string.IsNullOrEmpty(cfg.SqlServerConnection)
                ? ""
                : SecretProtector.Protect(cfg.SqlServerConnection)
        };
        File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(raw, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class DbProviderConfigRaw
    {
        public string Provider { get; set; } = "Sqlite";
        public string SqlServerConnection { get; set; } = "";
    }
}
