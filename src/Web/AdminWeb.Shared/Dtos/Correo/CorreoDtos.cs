namespace AdminWeb.Shared.Dtos.Correo;

/// <summary>
/// Cómo está el correo, para que la pantalla sepa qué puede ofrecer.
///
/// <para><b>Aquí no viaja la contraseña de aplicación, ni cifrada.</b> La cuenta y los servidores se
/// configuran en la pantalla de Configuración y el navegador solo necesita saber si el correo quedó
/// utilizable y, si no, por qué. En el escritorio la distinción no existía porque el valor nunca
/// salía del proceso; aquí cualquier campo de este DTO se lee abriendo la consola del navegador.</para>
/// </summary>
/// <param name="Diagnostico">
/// Vacío cuando todo está en orden. Cuando no, el mismo texto del escritorio: dice si falta activar
/// el correo o si faltan datos, que son dos arreglos distintos.
/// </param>
public record EstadoDeCorreoDto(
    bool Configurado,
    string Diagnostico,
    string Direccion,
    string CarpetaDeIngesta,
    bool ResumenActivo,
    int ResumenCadaDias,
    IReadOnlyList<string> DestinatariosDelResumen,
    DateTime? UltimoResumenUtc);

/// <summary>
/// Un mensaje del buzón.
///
/// <para><c>Cuerpo</c> es TEXTO PLANO: si el correo venía en HTML, el servidor ya le quitó las
/// etiquetas. Es la misma decisión del escritorio —allí se pintaba en un TextBox— y en la web pesa
/// todavía más: el cuerpo lo escribe gente de fuera de la organización, así que renderizarlo como
/// marcado sería un XSS almacenado servido por nuestra propia página.</para>
/// </summary>
/// <param name="Identificador">
/// El UID del mensaje dentro de su carpeta IMAP. Es lo que la pantalla devuelve para convertir uno
/// en requerimiento, para que el servidor vuelva a leerlo del buzón en vez de fiarse del texto que
/// le mande el navegador.
/// </param>
public record MensajeDeCorreoDto(
    uint Identificador,
    string Asunto,
    string De,
    DateTime FechaUtc,
    string Vista,
    string Cuerpo);

/// <summary>La bandeja: qué carpeta se está viendo, cuáles hay y qué mensajes trae.</summary>
public record BandejaDeCorreoDto(
    string Carpeta,
    IReadOnlyList<string> Carpetas,
    IReadOnlyList<MensajeDeCorreoDto> Mensajes);

/// <summary>
/// Un correo por enviar. <c>Destinatarios</c> viene tal como se escribió, separado por comas o
/// puntos y coma: partirlo es cosa del servidor, que es quien tiene que rechazar lo que no sea una
/// dirección.
/// </summary>
public record EnviarCorreoRequest(string? Destinatarios, string? Asunto, string? Cuerpo);

/// <summary>
/// Ingesta a petición. <c>Carpeta</c> vacía significa «la configurada»: así el botón de la pantalla
/// y el trabajo de fondo hacen exactamente lo mismo.
/// </summary>
public record IngerirCorreoRequest(string? Carpeta);

/// <summary>
/// Convertir un mensaje concreto en requerimiento. Van la carpeta y el UID, no el texto: el servidor
/// relee el mensaje del buzón, porque un título y un cuerpo que llegan del navegador son texto que
/// escribió quien hizo la petición, no lo que decía el correo.
/// </summary>
public record ConvertirEnRequerimientoRequest(string? Carpeta, uint Identificador);

/// <summary>Resultado de una ingesta. El mensaje viene del servicio y se enseña tal cual.</summary>
public record ResultadoDeIngestaDto(string Mensaje, int Creados);

/// <summary>
/// El resumen tal como saldría ahora mismo, para poder mirarlo antes de mandarlo.
///
/// <para><c>HayAlgoQueReportar</c> es la regla del escritorio: cuando todas las cifras están en
/// cero no se manda un correo vacío. Enseñarla evita la duda de «¿por qué no llegó nada?».</para>
/// </summary>
public record VistaPreviaDelResumenDto(
    bool HayAlgoQueReportar,
    string Asunto,
    string Cuerpo,
    IReadOnlyList<string> Destinatarios);
