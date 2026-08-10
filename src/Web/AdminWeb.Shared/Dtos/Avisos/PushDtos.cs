namespace AdminWeb.Shared.Dtos.Avisos;

/// <summary>
/// Lo que hace falta para ofrecer —o no— los avisos con la pestaña cerrada.
/// </summary>
/// <param name="Disponible">
/// Falso cuando el servidor no tiene llaves VAPID configuradas. La pantalla debe decirlo en vez de
/// ofrecer un interruptor que no haría nada: activar los avisos y no recibir ninguno es peor que no
/// tener la opción.
/// </param>
/// <param name="LlavePublica">
/// La llave con la que el navegador se suscribe. <b>No es secreta</b> —viaja al navegador por
/// diseño—; la privada nunca sale del servidor.
/// </param>
public record ConfiguracionDePushDto(bool Disponible, string? LlavePublica);

/// <summary>
/// La suscripción que entregó el navegador. Las tres primeras las genera él y son opacas para
/// nosotros: son la dirección de entrega y las llaves con las que se cifra el aviso.
/// </summary>
public record SuscripcionPushRequest(
    string Endpoint,
    string P256dh,
    string Auth,
    string? Descripcion);

/// <summary>Retirar el permiso de este navegador.</summary>
public record CancelarPushRequest(string Endpoint);
