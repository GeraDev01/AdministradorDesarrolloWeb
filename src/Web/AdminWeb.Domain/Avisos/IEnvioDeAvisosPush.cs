namespace AdminWeb.Domain.Avisos;

/// <summary>
/// Entrega un aviso al navegador de alguien aunque tenga la pestaña cerrada.
///
/// <para>Está detrás de una interfaz por lo mismo que los documentos en PDF: la implementación
/// depende de una librería concreta y de unas llaves, y quien manda un aviso no tiene por qué saber
/// nada de eso. Además permite que los servicios se prueben sin salir a la red.</para>
/// </summary>
public interface IEnvioDeAvisosPush
{
    /// <summary>
    /// ¿Están las llaves configuradas? Sin ellas no se puede entregar nada, y conviene decirlo en la
    /// pantalla en vez de dejar que el usuario active los avisos y no le llegue ninguno.
    /// </summary>
    bool Configurado { get; }

    /// <summary>La llave pública, que el navegador necesita para suscribirse. No es secreta.</summary>
    string? LlavePublica { get; }

    /// <summary>
    /// Entrega un aviso. Devuelve <c>false</c> cuando la suscripción ya no vale —el navegador se
    /// desinstaló, se limpió el sitio— y quien llama debe borrarla: seguir intentándolo con una
    /// dirección muerta gasta una petición por aviso y por siempre.
    /// </summary>
    Task<bool> EnviarAsync(AvisoPush aviso, CancellationToken ct = default);
}

/// <summary>
/// Un aviso listo para entregar. El destino son las tres cadenas que dio el navegador al suscribirse.
/// </summary>
/// <param name="Titulo">Lo que se lee en negrita. Corto: el sistema lo recorta.</param>
/// <param name="Cuerpo">Una línea o dos. Igual, se recorta.</param>
/// <param name="Url">A dónde llevar al pulsar el aviso. Relativa a la aplicación.</param>
public record AvisoPush(
    string Endpoint,
    string P256dh,
    string Auth,
    string Titulo,
    string Cuerpo,
    string? Url);
