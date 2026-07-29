using System.Text;
using Microsoft.Data.SqlClient;

namespace Administrador_Desarrollo_Web.Data;

/// <summary>
/// Normaliza connection strings de SQL Server escritas "a mano". Acepta formatos laxos como
/// <c>server = mi-srv.database.windows.net; uid = usuario; pwd = clave; database = MI_BD</c>
/// (espacios alrededor del "=", sinónimos cortos, mayúsculas/minúsculas indistintas) y los
/// convierte a una cadena canónica que Microsoft.Data.SqlClient entiende, agregando los valores
/// que Azure SQL necesita (cifrado en tránsito, timeout razonable).
/// </summary>
public static class SqlConnectionStringHelper
{
    /// <summary>Sinónimos aceptados → palabra clave canónica de SqlConnectionStringBuilder.</summary>
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        // Servidor
        ["server"] = "Data Source",
        ["servidor"] = "Data Source",
        ["host"] = "Data Source",
        ["address"] = "Data Source",
        ["addr"] = "Data Source",
        ["datasource"] = "Data Source",
        ["networkaddress"] = "Data Source",
        // Base de datos
        ["database"] = "Initial Catalog",
        ["basededatos"] = "Initial Catalog",
        ["db"] = "Initial Catalog",
        ["catalog"] = "Initial Catalog",
        ["initialcatalog"] = "Initial Catalog",
        // Usuario
        ["uid"] = "User ID",
        ["user"] = "User ID",
        ["userid"] = "User ID",
        ["username"] = "User ID",
        ["usuario"] = "User ID",
        ["login"] = "User ID",
        // Contraseña
        ["pwd"] = "Password",
        ["pass"] = "Password",
        ["password"] = "Password",
        ["contrasena"] = "Password",
        ["contraseña"] = "Password",
        ["clave"] = "Password",
        // Seguridad / red
        ["trustedconnection"] = "Integrated Security",
        ["integratedsecurity"] = "Integrated Security",
        ["encrypt"] = "Encrypt",
        ["trustservercertificate"] = "TrustServerCertificate",
        ["trustcert"] = "TrustServerCertificate",
        ["timeout"] = "Connect Timeout",
        ["connecttimeout"] = "Connect Timeout",
        ["connectiontimeout"] = "Connect Timeout",
        ["appname"] = "Application Name",
        ["applicationname"] = "Application Name",
        ["mars"] = "MultipleActiveResultSets",
        ["multipleactiveresultsets"] = "MultipleActiveResultSets",
        ["authentication"] = "Authentication",
        ["persistsecurityinfo"] = "Persist Security Info",
        ["pooling"] = "Pooling",
        ["maxpoolsize"] = "Max Pool Size",
        ["minpoolsize"] = "Min Pool Size",
        ["commandtimeout"] = "Command Timeout",
    };

    /// <summary>
    /// Convierte <paramref name="raw"/> en una connection string canónica.
    /// </summary>
    /// <param name="normalized">Cadena lista para usar (vacía si hubo error).</param>
    /// <param name="error">Mensaje explicativo si no se pudo interpretar.</param>
    public static bool TryNormalize(string? raw, out string normalized, out string error)
    {
        normalized = "";
        error = "";

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "La connection string está vacía.";
            return false;
        }

        List<(string Key, string Value)> pairs;
        try
        {
            pairs = SplitPairs(raw).ToList();
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }

        var builder = new SqlConnectionStringBuilder();
        // SqlConnectionStringBuilder.Keys enumera TODAS las palabras clave conocidas, no solo las
        // asignadas, por eso llevamos nuestro propio registro de lo que el usuario escribió.
        var provided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in pairs)
        {
            var canonical = Canonical(key);
            try
            {
                builder[canonical] = value;
                provided.Add(canonical);
            }
            catch (Exception ex)
            {
                error = $"No se reconoce el parámetro «{key}»: {ex.Message}";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            error = "Falta el servidor. Ejemplo: server = mi-servidor.database.windows.net";
            return false;
        }
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
        {
            error = "Falta la base de datos. Ejemplo: database = MI_BASE";
            return false;
        }

        bool hasUser = !string.IsNullOrWhiteSpace(builder.UserID);

        // Sin usuario ni contraseña asumimos autenticación integrada de Windows.
        if (!hasUser && !provided.Contains("Integrated Security") && !provided.Contains("Authentication"))
            builder.IntegratedSecurity = true;

        if (hasUser && string.IsNullOrEmpty(builder.Password) && !provided.Contains("Authentication"))
        {
            error = $"Se indicó el usuario «{builder.UserID}» pero falta la contraseña (pwd = ...).";
            return false;
        }

        if (IsAzureSql(builder.DataSource))
        {
            // Azure SQL siempre exige TLS con un certificado válido emitido por Microsoft.
            if (!provided.Contains("Encrypt")) builder.Encrypt = true;
            if (!provided.Contains("TrustServerCertificate")) builder.TrustServerCertificate = false;
            // El nivel serverless puede tardar en despertar; los 15 s de fábrica se quedan cortos.
            if (!provided.Contains("Connect Timeout")) builder.ConnectTimeout = 60;
        }
        else
        {
            // SQL local/on-premise suele usar certificado autofirmado: sin esto el driver moderno falla.
            if (!provided.Contains("Encrypt") && !provided.Contains("TrustServerCertificate"))
                builder.TrustServerCertificate = true;
            if (!provided.Contains("Connect Timeout")) builder.ConnectTimeout = 30;
        }

        if (!provided.Contains("Application Name"))
            builder.ApplicationName = "Administrador de Desarrollo Web";

        normalized = builder.ConnectionString;
        return true;
    }

    /// <summary>Igual que <see cref="TryNormalize"/> pero devolviendo la original si no se pudo interpretar.</summary>
    public static string NormalizeOrOriginal(string? raw) =>
        TryNormalize(raw, out var normalized, out _) ? normalized : (raw ?? "").Trim();

    public static bool IsAzureSql(string? dataSource) =>
        !string.IsNullOrWhiteSpace(dataSource) &&
        dataSource.Contains(".database.windows.net", StringComparison.OrdinalIgnoreCase);

    /// <summary>Versión para mostrar en logs/mensajes: oculta únicamente la contraseña.</summary>
    public static string Mask(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return "";
        try
        {
            var b = new SqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrEmpty(b.Password)) b.Password = "********";
            return b.ConnectionString;
        }
        catch { return "(cadena no interpretable)"; }
    }

    /// <summary>
    /// Divide la cadena en pares clave/valor. A diferencia de un simple Split(';'), respeta valores
    /// entrecomillados — necesario cuando la contraseña contiene «;» o «=».
    /// </summary>
    private static IEnumerable<(string Key, string Value)> SplitPairs(string raw)
    {
        var sb = new StringBuilder();
        var segments = new List<string>();
        char quote = '\0';

        foreach (var c in raw)
        {
            if (quote != '\0')
            {
                sb.Append(c);
                if (c == quote) quote = '\0';
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                sb.Append(c);
            }
            else if (c == ';')
            {
                segments.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(c);
        }
        segments.Add(sb.ToString());

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment)) continue;

            var eq = segment.IndexOf('=');
            if (eq < 0)
                throw new FormatException($"El fragmento «{segment.Trim()}» no tiene el formato clave=valor.");

            var key = segment[..eq].Trim();
            var value = segment[(eq + 1)..].Trim();

            // Un «=» duplicado (estilo ODBC) o un valor entrecomillado: se limpia para el builder,
            // que volverá a entrecomillar por su cuenta si el valor lo requiere.
            if (value.StartsWith('=')) value = value[1..].Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];

            if (key.Length == 0) continue;
            yield return (key, value);
        }
    }

    private static string Canonical(string key)
    {
        var compact = key.Replace(" ", "").Replace("_", "").Replace("-", "");
        return Synonyms.TryGetValue(compact, out var canonical) ? canonical : key;
    }

    /// <summary>Traduce fallos típicos de SQL Server a una explicación accionable en español.</summary>
    public static string Explain(Exception ex)
    {
        switch (ex)
        {
            case AggregateException agg when agg.InnerException is not null:
                return Explain(agg.InnerException);

            case SqlException sql:
                var hint = sql.Number switch
                {
                    18456 => "Usuario o contraseña incorrectos, o ese usuario no tiene acceso a la base de datos indicada.",
                    4060 or 911 => "El servidor respondió, pero la base de datos no existe o el usuario no tiene permiso sobre ella.",
                    40615 or 40532 => "El firewall de Azure SQL está bloqueando tu IP. Agrégala en «Networking» del servidor, en el portal de Azure.",
                    40613 => "La base de datos está despertando o no está disponible ahora mismo. Reintenta en unos segundos.",
                    53 or 10060 or 10061 => "No se alcanzó el servidor. Revisa el nombre, que el puerto 1433 esté abierto y que tu red no lo bloquee.",
                    -2 => "Se agotó el tiempo de espera al conectar con el servidor.",
                    _ => null
                };

                var msg = hint is null ? sql.Message : $"{hint}\n\nDetalle (error {sql.Number}): {sql.Message}";
                if (sql.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase))
                    msg += "\n\nSugerencia: agrega «TrustServerCertificate=True;» a la connection string.";
                return msg;

            default:
                return ex.Message;
        }
    }
}
