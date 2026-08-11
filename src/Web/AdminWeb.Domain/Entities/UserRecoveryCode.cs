namespace AdminWeb.Domain.Entities;

/// <summary>
/// Uno de los ocho códigos de rescate de una persona: la salida cuando el teléfono ya no está.
///
/// <para><b>Por qué es una tabla y no una columna con los ocho juntos.</b> Cada código tiene vida
/// propia: se gasta por separado, y hay que poder responder «¿cuántos te quedan?» para avisar antes
/// de que se acaben. Metidos en una sola columna —un JSON, una lista separada por comas— cada uso
/// obligaría a leer, parsear, modificar y volver a escribir el conjunto entero, que es la forma de
/// que dos intentos simultáneos se pisen y uno de los códigos gastados «reviva».</para>
///
/// <para><b>Aquí no hay ningún código legible.</b> Solo su hash: ver
/// <c>CodigosDeRescate.Hashear</c>, que explica por qué el hash es rápido y no lento.</para>
/// </summary>
public class UserRecoveryCode
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>
    /// SHA-256 del código en su forma canónica, en hexadecimal minúsculo: 64 caracteres siempre.
    /// Es también por lo que se BUSCA al entrar —se hashea lo tecleado y se consulta por índice—,
    /// así que probar los ocho no cuesta ocho comprobaciones sino una consulta.
    /// </summary>
    public string CodigoHash { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Cuándo se gastó, o nulo si sigue disponible.
    ///
    /// <para><b>Se marca en vez de borrarse</b> por dos motivos. Uno: la fila borrada no deja
    /// constancia, y «¿cuándo entró alguien con un código de rescate?» es exactamente la pregunta
    /// que se hace cuando algo va mal. Dos: si se borrara, un código usado volvería a estar
    /// «disponible» en el sentido de que ya no habría nada que impidiera volver a darlo de alta.
    /// Marcado, el mismo código nunca sirve dos veces.</para>
    /// </summary>
    public DateTime? UsadoEnUtc { get; set; }

    public User? User { get; set; }
}
