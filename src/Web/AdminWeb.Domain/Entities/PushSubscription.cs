namespace AdminWeb.Domain.Entities;

/// <summary>
/// Un navegador que aceptó recibir avisos aunque la pestaña esté cerrada.
///
/// <para><b>Es lo que sustituye a los globos de la bandeja del sistema.</b> En el escritorio, la
/// aplicación seguía viva escondida en la bandeja y podía avisar en cualquier momento; una página
/// cerrada no puede. La única forma de que un aviso llegue con la pestaña cerrada es que el navegador
/// lo entregue por su cuenta, y para eso hace falta guardar a quién entregárselo.</para>
///
/// <para>Es una tabla NUEVA de la web: el escritorio no la conoce ni la necesita, así que añadirla no
/// le afecta. Hay una fila por NAVEGADOR, no por persona: quien use el portátil y el teléfono tiene
/// dos, y las dos deben recibir el aviso.</para>
/// </summary>
public class PushSubscription
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>
    /// La dirección que da el navegador para entregarle avisos. Es única por navegador y por
    /// aplicación, y es la clave de verdad de esta tabla: el mismo navegador que vuelve a
    /// suscribirse trae la misma, y hay que actualizar en vez de duplicar.
    /// </summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Clave pública del navegador. Sin ella el aviso no se puede cifrar para él.</summary>
    public string P256dh { get; set; } = "";

    /// <summary>Secreto de autenticación que acompaña al cifrado del aviso.</summary>
    public string Auth { get; set; } = "";

    /// <summary>
    /// Qué navegador es, para que alguien pueda reconocer y retirar el permiso de un equipo que ya
    /// no usa. No se guarda el agente completo: no hace falta y es huella de seguimiento.
    /// </summary>
    public string? Descripcion { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Última vez que el servicio de entrega la aceptó. Una suscripción caduca sin avisar —el
    /// navegador se desinstala, se limpia el sitio— y el servidor solo se entera al intentar
    /// entregar: entonces responde 404 o 410 y la fila se borra.
    /// </summary>
    public DateTime? LastOkUtc { get; set; }

    public User? User { get; set; }
}
