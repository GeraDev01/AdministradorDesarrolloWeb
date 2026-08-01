namespace Administrador_Desarrollo_Web.Data;

/// <summary>De dónde salió la conexión que la aplicación acabó usando.</summary>
public enum DbConnectionSource
{
    /// <summary>Configurada en este equipo desde Configuración → Base de datos.</summary>
    ConfiguracionLocal,
    /// <summary>Venía incrustada en el ejecutable al publicarlo.</summary>
    Incrustada,
    /// <summary>No hay ninguna conexión de SQL Server que usar. La aplicación no arranca así.</summary>
    SinConexion
}

public sealed record DbConnectionChoice(
    DbConnectionSource Source,
    string? SqlServerConnection,
    string? Warning)
{
    public bool UsaSqlServer => Source != DbConnectionSource.SinConexion && !string.IsNullOrWhiteSpace(SqlServerConnection);

    public string Descripcion => Source switch
    {
        DbConnectionSource.ConfiguracionLocal => "configuración de este equipo",
        DbConnectionSource.Incrustada         => "conexión incrustada en la aplicación",
        _                                     => "sin conexión a la base del equipo"
    };
}

/// <summary>
/// Decide con qué base trabaja la aplicación. Está aparte de Program para poder probarlo:
/// la regla de precedencia es la que provoca los "¿por qué sigo viendo los datos viejos?".
///
/// Orden: configuración local (SQL Server) &gt; conexión incrustada &gt; NADA.
/// La local gana para que el administrador pueda apuntar a otra base, o usar credenciales con más
/// permisos, sin depender de que le generen un ejecutable nuevo.
///
/// SQLite NO es un destino. Antes, cuando no había conexión de SQL Server, la aplicación abría una
/// base local vacía y seguía trabajando: en esa base ningún usuario del equipo existe, así que el
/// login solo respondía «Usuario o contraseña incorrectos» y era imposible distinguirlo de una
/// contraseña mal escrita. Ahora se devuelve <see cref="DbConnectionSource.SinConexion"/> y la
/// aplicación lo dice en la pantalla de inicio de sesión en vez de crear una base propia.
/// </summary>
public static class DbConnectionResolver
{
    public static DbConnectionChoice Resolve(DbProviderConfig local, string? embedded)
    {
        // Un error al leer la configuración local NO se traga: se propaga como aviso para que el
        // usuario sepa por qué no se está usando lo que él capturó en este equipo.
        var aviso = local.LoadError;

        if (local.Provider == DbProvider.SqlServer && !string.IsNullOrWhiteSpace(local.SqlServerConnection))
            return new DbConnectionChoice(DbConnectionSource.ConfiguracionLocal, local.SqlServerConnection, aviso);

        // Tener SQLite elegido en dbprovider.json ya no aparta a la incrustada: elegir SQLite era
        // una forma silenciosa de dejar de ver los datos del equipo para siempre.
        if (!string.IsNullOrWhiteSpace(embedded))
            return new DbConnectionChoice(DbConnectionSource.Incrustada, embedded, aviso);

        return new DbConnectionChoice(DbConnectionSource.SinConexion, null, aviso);
    }
}
