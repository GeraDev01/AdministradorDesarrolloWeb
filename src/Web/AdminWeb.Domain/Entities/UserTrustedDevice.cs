namespace AdminWeb.Domain.Entities;

/// <summary>
/// Un navegador en el que esta persona ya demostró tener su teléfono, y al que por eso no se le
/// vuelve a pedir el código durante treinta días.
///
/// <para><b>Por qué se guarda en la base y no basta con una cookie firmada.</b> Una cookie firmada
/// vale mientras no haya que retirarla, y aquí hay que poder retirarla: el día que alguien pierde el
/// teléfono, el líder le reinicia el segundo factor — y ese reinicio tiene que dejar de confiar
/// también en los navegadores recordados, o quien tenga el equipo abierto sigue entrando sin código
/// como si nada hubiera pasado. Con una fila por navegador, olvidarlos es borrar filas. Con una
/// cookie autosuficiente, no habría forma de alcanzarla.</para>
///
/// <para><b>Una fila por NAVEGADOR, no por persona</b>: el mismo usuario tiene el portátil de la
/// oficina, el de casa y el teléfono, y confiar en uno no dice nada de los otros.</para>
/// </summary>
public class UserTrustedDevice
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>
    /// SHA-256 —hexadecimal minúsculo, 64 caracteres— del testigo aleatorio que viaja en la cookie
    /// del navegador.
    ///
    /// <para><b>Se guarda el hash y no el testigo</b> por la misma razón que las contraseñas: quien
    /// consiguiera leer esta tabla podría, si estuviera en claro, fabricarse la cookie de cualquiera
    /// y saltarse el segundo factor de todos a la vez. Con el hash, la tabla no sirve para entrar.
    /// El testigo son 256 bits del generador criptográfico: adivinarlo no es una vía.</para>
    /// </summary>
    public string TokenHash { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Cuándo deja de valer la confianza. <b>La fecha manda desde el servidor</b>: la caducidad de la
    /// cookie la puede cambiar cualquiera desde su propio navegador, esta no.
    /// </summary>
    public DateTime ExpiraEnUtc { get; set; }

    /// <summary>Última vez que se entró desde aquí. Es informativo: sirve para reconocer el equipo en una lista.</summary>
    public DateTime? UltimoUsoUtc { get; set; }

    /// <summary>
    /// Cómo describir el equipo a una persona («Chrome en Windows»). Sale del navegador, así que es
    /// una pista y no una identificación: quien quiera puede decir que es otro. No se usa para
    /// decidir nada, solo para que la lista signifique algo.
    /// </summary>
    public string? Descripcion { get; set; }

    public User? User { get; set; }
}
