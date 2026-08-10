namespace AdminWeb.Domain.Entities;

/// <summary>
/// Un secreto que pertenece a UNA persona, guardado cifrado del lado del servidor.
///
/// Existe para sustituir algo que en la web no tiene equivalente. En el escritorio, el token
/// personal de Azure DevOps de cada quien vivía en un archivo de su propia máquina cifrado con
/// DPAPI, y ese diseño estaba justificado: los comentarios en DevOps quedan firmados por el dueño
/// del token, así que un token compartido haría que todos los comentarios aparecieran a nombre de
/// la misma cuenta y el SLA dejaría de probar quién atendió.
///
/// En un navegador no hay DPAPI ni dónde guardar eso a salvo — cualquier XSS leería el
/// almacenamiento local. La solución es que el secreto <b>nunca baje al navegador</b>: se guarda
/// aquí cifrado con la protección de datos del servidor, el cliente solo pide «comenta este ticket»
/// y la API firma con el token del usuario autenticado. De paso se arregla un defecto del modelo
/// anterior: hoy el token se pierde al cambiar de computadora, y así sigue a la persona.
/// </summary>
public class UserSecret
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>
    /// Para qué es el secreto (por ejemplo el token de Azure DevOps). Junto con el usuario forma la
    /// llave única: una persona tiene un secreto por propósito, no una colección suelta.
    /// </summary>
    public string Proposito { get; set; } = "";

    /// <summary>
    /// El valor ya cifrado. Nunca sale de la API en claro ni cifrado: las pantallas solo muestran si
    /// está configurado o no, igual que hacía el diálogo del escritorio.
    /// </summary>
    public string CipherText { get; set; } = "";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}

/// <summary>Propósitos conocidos. Constantes para que quien escribe y quien lee no discrepen.</summary>
public static class PropositosDeSecreto
{
    /// <summary>Token personal de Azure DevOps.</summary>
    public const string PatDevOps = "devops.pat";
}
