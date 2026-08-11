namespace AdminWeb.Domain.Security;

/// <summary>
/// El texto que se mete dentro del código QR.
///
/// <para>Es el URI <c>otpauth://</c>, el formato que leen todas las aplicaciones de códigos del
/// teléfono. No es un invento de este sistema ni admite variaciones: si se le cambia la forma, el
/// teléfono escanea el QR y no entiende nada, o —peor— lo da de alta con el secreto mal leído y el
/// fallo no aparece hasta el primer intento de entrar.</para>
/// </summary>
public static class UriDeSegundoFactor
{
    /// <summary>
    /// Cómo aparece esta cuenta en la lista del teléfono.
    ///
    /// <para><b>Se eligió pensando en dónde se va a leer.</b> En la aplicación de códigos, esta
    /// línea sale entre las cuentas personales de cada quien —el banco, el correo, la tienda—, así
    /// que tiene que identificarse sin contexto y a la primera. Empieza por el nombre de la empresa
    /// para que quede junto a cualquier otra entrada de la casa y para que nadie dude de a qué
    /// pertenece ese código a los seis meses.</para>
    ///
    /// <para>Es corto por lo mismo: la lista del teléfono recorta los nombres largos, y un emisor
    /// recortado a media palabra no identifica nada.</para>
    ///
    /// <para><b>Cambiarlo NO reconfigura los teléfonos ya dados de alta</b>: los que ya escanearon
    /// conservan la etiqueta vieja, y solo verán la nueva quienes se den de alta después. Si algún
    /// día se cambia, hay que asumir que durante una temporada conviven las dos.</para>
    /// </summary>
    public const string Emisor = "Soltum Administrador";

    /// <summary>
    /// Arma el URI para una cuenta y un secreto.
    ///
    /// <para>El emisor aparece DOS veces —dentro de la etiqueta y como parámetro <c>issuer</c>— y
    /// no es una redundancia que se pueda podar: las aplicaciones antiguas leen la etiqueta y las
    /// modernas el parámetro. Omitir cualquiera de los dos hace que en algunos teléfonos la cuenta
    /// aparezca sin nombre de emisor.</para>
    ///
    /// <para>Los tres parámetros del final se escriben aunque sean los valores por omisión del
    /// estándar. Es deliberado: dejan constancia legible de con qué se está calculando, y si algún
    /// día se cambiara alguno, el sitio donde se declara ya existe.</para>
    /// </summary>
    public static string Construir(string usuario, string secretoEnBase32)
    {
        // Los dos puntos son el separador entre emisor y cuenta DENTRO de la etiqueta, así que cada
        // parte se escapa por separado: un usuario que llevara dos puntos partiría la etiqueta en
        // tres y el teléfono leería un emisor que no es.
        var etiqueta = $"{Uri.EscapeDataString(Emisor)}:{Uri.EscapeDataString(usuario ?? "")}";

        return $"otpauth://totp/{etiqueta}" +
               $"?secret={Uri.EscapeDataString(secretoEnBase32 ?? "")}" +
               $"&issuer={Uri.EscapeDataString(Emisor)}" +
               $"&algorithm=SHA1" +
               $"&digits={Totp.Digitos}" +
               $"&period={Totp.SegundosPorVentana}";
    }
}

/// <summary>
/// Dibuja un código QR. La implementación con QRCoder vive en Infrastructure.
///
/// <para><b>Por qué hay una interfaz para algo tan pequeño.</b> Para que el servicio del segundo
/// factor —que es donde está la lógica que importa— no dependa de una librería de dibujo. Así se
/// puede probar el alta entera sin generar un solo píxel, y cambiar de librería es escribir otra
/// implementación en vez de tocar la seguridad.</para>
/// </summary>
public interface IDibujanteDeCodigoQr
{
    /// <summary>
    /// El PNG del código QR con el contenido dado. Devuelve los bytes crudos: quien los pinte
    /// decide si los manda como archivo o incrustados en la página.
    /// </summary>
    byte[] DibujarPng(string contenido);
}
