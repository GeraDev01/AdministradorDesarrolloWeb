using AdminWeb.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;

namespace AdminWeb.Api.Auth;

/// <summary>
/// Lo único que queda vivo ENTRE los dos tramos del acceso: quién acertó su contraseña y está
/// pendiente de teclear el código.
///
/// <para><b>Por qué no una cookie de sesión con menos permisos.</b> Era la salida fácil: emitir la
/// cookie tras la contraseña y marcarla como «a medias». Se descartó porque el riesgo de esa forma
/// no está en lo que hace bien, sino en lo que pasa el día que alguien se olvida de mirar la marca.
/// Una cookie de sesión la manda el navegador SOLA en todas las peticiones, así que cada endpoint
/// escrito de aquí en adelante tendría que acordarse de que existe una sesión que no vale; el que se
/// olvidara —uno solo, en dos años— convertiría el segundo factor en un adorno, y no fallaría nada
/// que se pudiera ver. Aquí no hay nada que recordar: mientras falte el código no existe ninguna
/// cookie, así que <b>toda</b> la API responde 401 por el camino de siempre.</para>
///
/// <para><b>Qué es entonces.</b> Un texto cifrado y firmado por el servidor con la protección de
/// datos que ya usa el resto del sistema —la del llavero protegido con certificado—, con caducidad
/// dentro del propio texto. Viaja en el cuerpo de la respuesta y el cliente lo devuelve en el cuerpo
/// de la siguiente petición: no es una cookie, así que ningún navegador lo manda solo, ningún sitio
/// ajeno puede provocarlo y muere en cuanto se cierra la pestaña.</para>
///
/// <para><b>Y qué NO es.</b> No es una credencial: quien lo robe entero no consigue nada sin el
/// código de seis dígitos, que es justo lo que este tramo está esperando.</para>
/// </summary>
public sealed class TramoDeAcceso(IDataProtectionProvider proveedor)
{
    /// <summary>
    /// Cuánto vive. Cinco minutos: lo que tarda alguien en desbloquear el teléfono, abrir la
    /// aplicación de códigos y teclear seis dígitos, con margen para que suene el teléfono en medio.
    ///
    /// <para>Más largo empieza a parecerse a lo que se quería evitar: un permiso a medias esperando
    /// en el aire. Más corto obliga a repetir usuario y contraseña a quien simplemente tardó, y el
    /// remedio de la gente ante eso es dejar de usar el segundo factor si puede.</para>
    /// </summary>
    public static readonly TimeSpan Duracion = TimeSpan.FromMinutes(5);

    /// <summary>
    /// El propósito ata este texto a este uso. La protección de datos deriva una llave distinta por
    /// propósito, así que un tramo NO se puede presentar donde se espera otra cosa protegida por el
    /// mismo llavero — ni al revés. El <c>.v1</c> está para poder cambiar el formato algún día
    /// invalidando de golpe los tramos en vuelo, que como mucho serán los de cinco minutos.
    /// </summary>
    private const string Proposito = "AdminWeb.Acceso.SegundoFactorPendiente.v1";

    private readonly ITimeLimitedDataProtector _protector =
        proveedor.CreateProtector(Proposito).ToTimeLimitedDataProtector();

    /// <summary>
    /// Emite el tramo de quien acaba de acertar su contraseña.
    ///
    /// <para>Dentro van el identificador y el SELLO DE SEGURIDAD de la cuenta. El sello es lo que
    /// hace que el tramo se caiga si entre los dos pasos cambia algo importante: si en esos cinco
    /// minutos alguien cambia la contraseña de esa cuenta o el líder la restablece, el sello es otro
    /// y este tramo deja de servir. Sin él, un tramo emitido con la contraseña vieja seguiría
    /// abriendo la puerta después de haberla cambiado, que es el momento exacto en que no debe.</para>
    /// </summary>
    public string Emitir(User usuario) =>
        _protector.Protect($"{usuario.Id}|{usuario.SecurityStamp}", Duracion);

    /// <summary>
    /// Lee un tramo. Devuelve <c>null</c> si caducó, si lo tocaron, si viene de otro propósito o si
    /// no es nada.
    ///
    /// <para>Un solo <c>null</c> para todos los motivos, y a propósito: distinguir «caducado» de
    /// «falsificado» solo le sirve a quien esté probando tramos a ver cuál cuela.</para>
    /// </summary>
    public (int UserId, string Sello)? Leer(string? tramo)
    {
        if (string.IsNullOrWhiteSpace(tramo)) return null;

        string contenido;
        try
        {
            // Unprotect lanza ante cualquier problema —caducado, manipulado, llave que ya no está—.
            // Se traga la excepción a propósito: aquí un texto inválido es una respuesta esperada de
            // la puerta de entrada, no un fallo del sistema, y dejarla subir llenaría el registro de
            // errores con el ruido de cualquiera que pruebe cosas.
            contenido = _protector.Unprotect(tramo);
        }
        catch
        {
            return null;
        }

        var partes = contenido.Split('|', 2);
        if (partes.Length != 2 || !int.TryParse(partes[0], out int userId)) return null;

        return (userId, partes[1]);
    }
}
