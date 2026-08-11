namespace AdminWeb.Client.Paginas.Conocimiento;

/// <summary>
/// El aviso de «esto acaba de cambiar, vuelve a contar» entre las pantallas de la base de
/// conocimiento y la marca del menú.
///
/// <para><b>Por qué hace falta.</b> El número que lleva la entrada del menú es lo único que sostiene
/// la cola de revisión: una cola que hay que acordarse de abrir no se abre, y en cuanto dos artículos
/// se quedan esperando, quien los escribió deja de escribir. Pero un número que se calcula una vez al
/// entrar empieza a mentir en cuanto alguien hace algo: el líder aprueba tres artículos, la cola se
/// vacía y el menú sigue anunciando tres. Una marca que miente es PEOR que ninguna, porque enseña
/// trabajo pendiente que ya está hecho y a la tercera vez nadie vuelve a hacerle caso.</para>
///
/// <para><b>Por qué es un evento y no una consulta periódica.</b> Preguntar cada tantos segundos
/// sería tráfico constante para un número que cambia dos veces al día. Aquí se vuelve a contar
/// exactamente cuando pudo cambiar —se manda algo a revisar, se aprueba, se devuelve, se borra— y en
/// ningún otro momento.</para>
///
/// <para><b>Es estático a propósito</b>, y por eso quien se suscriba tiene que darse de baja: el
/// menú vive en el armazón de la aplicación y las pantallas que avisan van y vienen, así que no hay
/// un objeto común que las dos puedan recibir sin arrastrar una dependencia entre ellas. La
/// contrapartida es que un suscriptor olvidado no se recoge nunca; el armazón se da de baja en su
/// <c>Dispose</c>.</para>
/// </summary>
public static class PendientesDeConocimiento
{
    /// <summary>Alguien tocó algo que puede cambiar los contadores.</summary>
    public static event Action? Cambio;

    /// <summary>
    /// Avisa de que hay que volver a contar. Se llama DESPUÉS de que el servidor haya confirmado la
    /// operación, nunca antes: adelantarlo pintaría un número que todavía no es verdad, y si la
    /// operación fallara se quedaría así.
    /// </summary>
    public static void Avisar() => Cambio?.Invoke();
}
