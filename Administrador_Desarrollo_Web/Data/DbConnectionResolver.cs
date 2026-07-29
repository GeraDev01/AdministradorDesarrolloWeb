namespace Administrador_Desarrollo_Web.Data;

/// <summary>De dónde salió la conexión que la aplicación acabó usando.</summary>
public enum DbConnectionSource
{
    /// <summary>Configurada en este equipo desde Configuración → Base de datos.</summary>
    ConfiguracionLocal,
    /// <summary>Venía incrustada en el ejecutable al publicarlo.</summary>
    Incrustada,
    /// <summary>Base local SQLite (no hay ninguna de SQL Server disponible).</summary>
    SqliteLocal
}

public sealed record DbConnectionChoice(
    DbConnectionSource Source,
    string? SqlServerConnection,
    string? Warning)
{
    public bool UsaSqlServer => Source != DbConnectionSource.SqliteLocal && !string.IsNullOrWhiteSpace(SqlServerConnection);

    public string Descripcion => Source switch
    {
        DbConnectionSource.ConfiguracionLocal => "configuración de este equipo",
        DbConnectionSource.Incrustada         => "conexión incrustada en la aplicación",
        _                                     => "base local SQLite"
    };
}

/// <summary>
/// Decide con qué base trabaja la aplicación. Está aparte de Program para poder probarlo:
/// la regla de precedencia es la que provoca los "¿por qué sigo viendo los datos viejos?".
///
/// Orden: configuración local &gt; conexión incrustada &gt; SQLite.
/// La local gana para que el administrador pueda apuntar a otra base, o usar credenciales con más
/// permisos, sin depender de que le generen un ejecutable nuevo.
/// </summary>
public static class DbConnectionResolver
{
    public static DbConnectionChoice Resolve(DbProviderConfig local, string? embedded)
    {
        // Un error al leer la configuración local NO se traga: se propaga como aviso para que el
        // usuario no acabe capturando datos en SQLite creyendo estar en SQL Server.
        var aviso = local.LoadError;

        if (local.Provider == DbProvider.SqlServer && !string.IsNullOrWhiteSpace(local.SqlServerConnection))
            return new DbConnectionChoice(DbConnectionSource.ConfiguracionLocal, local.SqlServerConnection, aviso);

        if (!string.IsNullOrWhiteSpace(embedded))
        {
            // Si el usuario eligió SQLite a propósito, se respeta: no se le impone la incrustada.
            bool eligioSqliteAdrede = local.Provider == DbProvider.Sqlite && local.LoadError == null && local.ConfiguracionExplicita;
            if (!eligioSqliteAdrede)
                return new DbConnectionChoice(DbConnectionSource.Incrustada, embedded, aviso);
        }

        return new DbConnectionChoice(DbConnectionSource.SqliteLocal, null, aviso);
    }
}
