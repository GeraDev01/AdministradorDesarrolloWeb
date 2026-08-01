namespace Administrador_Desarrollo_Web.Data;

/// <summary>
/// Estado de la conexión con la base del equipo, tal como lo pinta el indicador (● verde / ● rojo)
/// de la pantalla de inicio de sesión.
///
/// Existe porque el login responde lo MISMO cuando el usuario no existe en la base que cuando la
/// contraseña está mal ("Usuario o contraseña incorrectos"). Sin este indicador, estar apuntando a
/// la base equivocada era indistinguible de escribir mal la contraseña, y averiguarlo obligaba a
/// abrir el log en la PC de cada quien.
///
/// A propósito NO dice contra qué servidor ni contra qué base está: esta pantalla se ve antes de
/// autenticarse, así que solo informa si hay conexión o no. Servidor, base y usuario siguen quedando
/// en el log (<c>BD inicializada [origen]: destino</c>), que es donde el administrador los consulta.
/// </summary>
public sealed record DbConnectionStatus(bool Conectado, string Titulo, string Detalle)
{
    /// <summary>Verde: se abrió la base del equipo y el esquema quedó listo.</summary>
    public static DbConnectionStatus Ok() =>
        new(true, "Conectado a la base de datos del equipo", "");

    /// <summary>
    /// Rojo: no hay ninguna conexión que usar. El ejecutable se publicó sin la conexión incrustada
    /// y este equipo tampoco tiene una capturada.
    /// </summary>
    public static DbConnectionStatus SinConexion() =>
        new(false,
            "No se pudo conectar a la base de datos del equipo",
            "Este ejecutable no trae la conexión del equipo y en este equipo no hay ninguna " +
            "configurada. Pide al administrador el ejecutable publicado con Deploy\\build-app.ps1. " +
            "La aplicación no trabaja con una base local.");

    /// <summary>Rojo: hay conexión configurada, pero el servidor no respondió.</summary>
    public static DbConnectionStatus Fallo(Exception ex) =>
        new(false,
            "No se pudo conectar a la base de datos del equipo",
            SqlConnectionStringHelper.ExplainBrief(ex));

    /// <summary>Estado transitorio mientras se reintenta, para que el indicador no mienta.</summary>
    public static DbConnectionStatus Comprobando { get; } =
        new(false, "Comprobando la conexión...", "");
}
