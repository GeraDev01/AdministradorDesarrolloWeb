namespace Administrador_Desarrollo_Web.Data;

/// <summary>
/// Último estado conocido de la conexión con la base del equipo, compartido por toda la aplicación.
///
/// Existe porque la pantalla de inicio de sesión es TRANSITORIA: al cerrar sesión se construye otra
/// distinta de la del arranque. Cuando el estado lo guardaba el propio formulario, esa segunda
/// pantalla nacía sin saber nada y el indicador se quedaba en ● gris, que se lee como «me
/// desconecté» justo después de haber estado trabajando sin problema. El estado es de la aplicación,
/// no de una ventana concreta, así que vive aquí.
///
/// También guarda con qué se vuelve a probar la conexión, y —importante— se queda con el resultado
/// de ese reintento: si alguien levanta la VPN y reintenta, la siguiente pantalla de inicio de
/// sesión ya parte del estado bueno y no del viejo.
/// </summary>
public sealed class DbConnectionState
{
    private readonly object _candado = new();
    private DbConnectionStatus _estado = DbConnectionStatus.Comprobando;
    private Func<Task<DbConnectionStatus>>? _reintento;

    public DbConnectionStatus Estado { get { lock (_candado) return _estado; } }

    /// <summary>
    /// Hay una cadena de conexión que volver a probar. Falso cuando el ejecutable no trae conexión
    /// incrustada y este equipo tampoco tiene ninguna: ahí reintentar no puede cambiar nada.
    /// </summary>
    public bool SePuedeReintentar { get { lock (_candado) return _reintento != null; } }

    public void Configurar(DbConnectionStatus estado, Func<Task<DbConnectionStatus>>? reintento)
    {
        lock (_candado) { _estado = estado; _reintento = reintento; }
    }

    /// <summary>
    /// Vuelve a probar la conexión y se queda con el resultado. Si no hay nada que probar, devuelve
    /// el estado actual sin tocar nada.
    /// </summary>
    public async Task<DbConnectionStatus> ReintentarAsync()
    {
        Func<Task<DbConnectionStatus>>? reintento;
        lock (_candado) reintento = _reintento;
        if (reintento == null) return Estado;

        var nuevo = await reintento();
        lock (_candado) _estado = nuevo;
        return nuevo;
    }
}
