using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Auth;

/// <summary>Lo que se manda al iniciar sesión. Es el PRIMER tramo: usuario y contraseña, nada más.</summary>
public record LoginRequest(string Usuario, string Contrasena);

/// <summary>
/// Quién está dentro. Es lo ÚNICO que el navegador sabe de la cuenta: ni el hash, ni el sello de
/// sesión, ni los contadores de bloqueo salen de aquí. El menú se pinta con esto.
/// </summary>
/// <param name="DebeActivarSegundoFactor">
/// La cuenta todavía no tiene segundo factor y el servidor le está cortando todo lo demás.
///
/// <para>Va aquí junto a <c>DebeCambiarContrasena</c> y por el mismo motivo: el cliente necesita
/// saberlo para llevar a la pantalla que corresponde SIN tener que provocar antes un rechazo. Lo que
/// decide de verdad es el servidor —el middleware corta la petición mire el cliente lo que mire—;
/// esto solo evita el rebote.</para>
/// </param>
public record UsuarioSesionDto(
    int Id,
    string Usuario,
    string NombreCompleto,
    UserRole Rol,
    int? DeveloperId,
    bool DebeCambiarContrasena,
    bool DebeActivarSegundoFactor);

/// <summary>Cambio de contraseña propio (el obligatorio del primer ingreso usa el mismo).</summary>
public record CambioContrasenaRequest(string NuevaContrasena, string Confirmacion);

// ══════════════════════════════════════════════════════════════════════════════════════════════
//  EL ACCESO EN DOS TRAMOS
// ══════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Cómo terminó el primer tramo del acceso.
///
/// <para><b>Son dos finales y no uno con datos de más.</b> O la sesión quedó abierta —y entonces
/// viene el usuario y no viene el tramo—, o falta el código —y entonces viene el tramo y no viene el
/// usuario—. No existe un estado intermedio en el que el navegador sepa quién es la persona sin
/// haber terminado de entrar: eso sería exactamente la media sesión que este diseño evita.</para>
/// </summary>
/// <param name="SegundoFactorRequerido">
/// Cierto cuando falta el código del teléfono. El cliente enseña el segundo paso.
/// </param>
/// <param name="Tramo">
/// El identificador del tramo: un texto firmado y cifrado por el servidor que dice a quién pertenece
/// este acceso a medias y caduca en minutos. <b>No es una sesión y no sirve para nada más</b>: sin el
/// código correcto no abre ninguna puerta, y no viaja en ninguna cookie, así que muere con la
/// pestaña.
/// </param>
/// <param name="Usuario">La sesión, cuando ya no falta nada.</param>
/// <param name="Mensaje">Lo que hay que leer en pantalla. Nunca dice si la cuenta existe.</param>
public record RespuestaDeAccesoDto(
    bool SegundoFactorRequerido,
    string? Tramo,
    UsuarioSesionDto? Usuario,
    string? Mensaje);

/// <summary>El segundo tramo: el código, con el tramo que dice de quién es.</summary>
/// <param name="Tramo">Lo que devolvió el primer tramo. Caduca en minutos.</param>
/// <param name="Codigo">
/// El de seis dígitos del teléfono <b>o</b> uno de los de rescate. El mismo campo acepta los dos: el
/// servidor los distingue por su forma, que no se solapa.
/// </param>
/// <param name="RecordarEquipo">
/// Si se marca, a este navegador no se le vuelve a pedir el código durante treinta días.
/// </param>
public record SegundoFactorLoginRequest(string Tramo, string Codigo, bool RecordarEquipo);

// ══════════════════════════════════════════════════════════════════════════════════════════════
//  ALTA Y ADMINISTRACIÓN DEL SEGUNDO FACTOR
// ══════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Lo que hace falta para dar de alta el teléfono. <b>Se entrega una sola vez</b>: si la pantalla se
/// recarga, el alta empieza otra vez con un secreto distinto y este deja de valer.
/// </summary>
/// <param name="SecretoEnBase32">La clave en texto, para quien no pueda escanear el código.</param>
/// <param name="SecretoAgrupado">
/// La misma clave partida en grupos de cuatro. Es lo que se enseña en pantalla: una tira de treinta y
/// dos caracteres seguidos se teclea mal, y el error se manifiesta como «mi código nunca es válido».
/// </param>
/// <param name="Uri">El <c>otpauth://</c> completo, por si alguien prefiere abrirlo desde el propio teléfono.</param>
/// <param name="CodigoQrPngBase64">
/// El PNG del código QR ya incrustado (<c>data:</c>). Va dentro de la respuesta y no como una URL a
/// otro endpoint <b>a propósito</b>: una imagen con dirección propia queda en el historial del
/// navegador, en los registros del servidor y en cualquier intermediario por el que pase, y esa
/// imagen ES el secreto.
/// </param>
public record InicioDeAltaDto(
    string SecretoEnBase32,
    string SecretoAgrupado,
    string Uri,
    string CodigoQrPngBase64);

/// <summary>El código con el que se confirma que el teléfono quedó bien dado de alta.</summary>
public record ConfirmarAltaRequest(string Codigo);

/// <summary>
/// El alta quedó hecha, con los ocho códigos de rescate EN CLARO.
///
/// <para>Es la única vez que existen: en la base solo queda su hash y no hay ningún camino para
/// volver a verlos. Quien los recibe los enseña y los olvida.</para>
/// </summary>
public record AltaConfirmadaDto(string Mensaje, IReadOnlyList<string> CodigosDeRescate);

/// <summary>El código del teléfono que hace falta para emitir ocho códigos de rescate nuevos.</summary>
public record RegenerarCodigosRequest(string Codigo);

/// <summary>Cómo está el segundo factor de quien pregunta. Nunca incluye nada del secreto.</summary>
/// <param name="DiasQueSeRecuerdaElEquipo">
/// Lo dice el SERVIDOR y no la pantalla. Escrito a mano en la interfaz, el día que se cambiara la
/// regla la pantalla seguiría prometiendo treinta días sin que nadie lo notara.
/// </param>
public record EstadoDeSegundoFactorDto(
    bool Activo,
    DateTime? DesdeUtc,
    int CodigosDeRescateRestantes,
    int EquiposRecordados,
    int DiasQueSeRecuerdaElEquipo);

/// <summary>
/// Un navegador que ya no pide el código.
///
/// <para><b>La descripción es una PISTA, no una identificación.</b> Sale de lo que el navegador dice
/// de sí mismo, y eso lo puede decir cualquiera. No se usa para decidir nada: solo para que quien
/// mire la lista reconozca cuál es cuál antes de olvidarlos todos.</para>
/// </summary>
public record EquipoRecordadoDto(
    string Descripcion,
    DateTime CreadoUtc,
    DateTime ExpiraUtc,
    DateTime? UltimoUsoUtc);
