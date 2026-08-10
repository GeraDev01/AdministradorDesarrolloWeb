using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Personas;

// ── Presencia: quién está ────────────────────────────────────────────────────────

/// <summary>
/// Una persona en el tablero de «quién está».
///
/// Es una fotografía del MOMENTO y no hay ninguna versión histórica de esto, ni la habrá: el estado
/// (comiendo, en un descanso…) se sobrescribe y no se guarda minutado. Es una decisión ética que
/// viene del escritorio — un registro de las pausas de cada quien es vigilancia, no asistencia.
/// </summary>
/// <param name="Estado">Ausente cuando la persona no está conectada; lo pone el servidor, nadie lo elige.</param>
/// <param name="DesdeUtc">Desde cuándo está conectada. Null si no lo está.</param>
/// <param name="UltimoLatidoUtc">La última vez que dio señales. Null si nunca ha entrado.</param>
public record PresenteDto(
    int UserId,
    string Nombre,
    bool Conectado,
    PresenceState Estado,
    string EstadoTexto,
    string Icono,
    string? Nota,
    DateTime? DesdeUtc,
    DateTime? UltimoLatidoUtc);

/// <summary>
/// El tablero completo. Los contadores y la tolerancia vienen resueltos del servidor porque son la
/// línea de estado que el escritorio pintaba bajo la rejilla, y esa frase explica por qué alguien
/// aparece desconectado.
/// </summary>
public record TableroDePresenciaDto(
    IReadOnlyList<PresenteDto> Personas,
    int Conectados,
    int Total,
    int ToleranciaMinutos);

/// <summary>
/// Una jornada del registro automático: lo que la aplicación vio sola, no lo que la persona declaró.
/// </summary>
/// <param name="SinSenales">La jornada se cerró sola por dejar de latir; la salida es la última señal
/// y NO una hora real de salida.</param>
public record JornadaDelDiaDto(
    int Id,
    string Persona,
    DateTime InicioUtc,
    DateTime? FinUtc,
    string Duracion,
    bool Abierta,
    bool SinSenales,
    string CierreTexto,
    string? Equipo);

/// <summary>El registro de jornadas de un día, con el pie de resumen que el escritorio ya pintaba.</summary>
public record RegistroDeJornadasDto(
    IReadOnlyList<JornadaDelDiaDto> Jornadas,
    int Personas,
    string DuracionTotal);

/// <summary>
/// Una fila del tablero de asistencia OFICIAL: lo que la persona marcó a mano, cruzado con lo que la
/// aplicación vio sola. Las dos mitades pueden faltar y esa ausencia también es el dato.
/// </summary>
/// <param name="DeltaEntradaMinutos">Diferencia con la primera señal, con signo: positivo = marcó después.</param>
/// <param name="Discrepancia">Alguno de los dos deltas pasa la tolerancia; es el resalte ámbar del escritorio.</param>
public record AsistenciaDelDiaDto(
    int UserId,
    string Nombre,
    int? RegistroId,
    DateTime? EntradaUtc,
    DateTime? SalidaUtc,
    string Horas,
    int? DeltaEntradaMinutos,
    int? DeltaSalidaMinutos,
    DateTime? PrimeraSenalUtc,
    DateTime? UltimaSenalUtc,
    string EstadoTexto,
    bool SinMarcar,
    bool Olvido,
    bool Discrepancia,
    bool CorreccionSolicitada,
    string? NotaCorreccion);

/// <summary>La asistencia de un día con sus contadores, que son la línea de estado del escritorio.</summary>
public record AsistenciaDelDiaResumenDto(
    IReadOnlyList<AsistenciaDelDiaDto> Filas,
    int Marcaron,
    int SinMarcar,
    int Olvidos,
    int Solicitudes,
    int ToleranciaMinutos);

/// <summary>
/// Corrección de un registro de asistencia por el líder.
///
/// Las horas viajan como fecha SIN zona y el servidor las interpreta en la suya, igual que hacía el
/// escritorio (donde el «local» era el de la máquina de quien corregía). El motivo es obligatorio y
/// lo exige el servicio: una corrección sin explicación es indistinguible de una manipulación.
/// </summary>
public record CorregirAsistenciaRequest(DateTime EntradaLocal, DateTime? SalidaLocal, string Motivo);

/// <summary>Alta a mano de un día que nadie marcó. Mismas reglas que la corrección.</summary>
public record AltaDeAsistenciaRequest(int UserId, DateTime EntradaLocal, DateTime? SalidaLocal, string Motivo);

// ── Perfil y desarrollo ──────────────────────────────────────────────────────────

/// <summary>
/// Una persona en la lista de la izquierda de «Perfil y desarrollo».
///
/// <b>Aquí NO hay salario, y eso no es un olvido.</b> Este DTO alimenta una lista, y una lista se
/// pinta para todo el mundo que abra la pantalla; el dato confidencial viaja solo en
/// <see cref="FichaDeDesarrolloDto"/>, que se pide de una en una y por una ruta propia. Lo que no
/// sale del servidor no se puede filtrar en el navegador — que es manipulable.
/// </summary>
public record PersonaConFichaDto(
    int DeveloperId,
    string Nombre,
    bool TieneFicha,
    DateTime? ActualizadaUtc);

/// <summary>
/// La ficha de desarrollo de una persona.
///
/// <b>Lleva SALARIO: es el único DTO de todo el vertical que lo hace.</b> Solo lo devuelve la ruta
/// de la ficha, que es SoloAdmin en el endpoint y vuelve a comprobarlo en el servicio. No debe
/// añadirse a ninguna lista ni a ningún resumen: en cuanto un dato confidencial viaja «de paso» en
/// una respuesta que se pide para otra cosa, deja de haber forma de saber quién lo ha visto.
/// </summary>
public record FichaDeDesarrolloDto(
    int DeveloperId,
    string Nombre,
    string? Fortalezas,
    string? Debilidades,
    string? Stack,
    decimal? Salario,
    string? Moneda,
    string? Crecimiento,
    string? Notas,
    DateTime? ActualizadaUtc);

/// <summary>
/// Lo que se guarda de la ficha. <paramref name="Salario"/> nulo o cero es «sin registrar», igual
/// que el 0 del control del escritorio.
/// </summary>
public record GuardarFichaRequest(
    int DeveloperId,
    string? Fortalezas,
    string? Debilidades,
    string? Stack,
    decimal? Salario,
    string? Moneda,
    string? Crecimiento,
    string? Notas);

// ── Comunicados ──────────────────────────────────────────────────────────────────

/// <summary>
/// Un posible destinatario de un comunicado. Se listan TODOS los desarrolladores activos y no solo
/// los que pueden recibirlo, igual que en el escritorio: el líder tiene que ver al equipo completo y
/// enterarse de quién se queda fuera por no tener cuenta.
/// </summary>
public record DestinatarioDeComunicadoDto(int DeveloperId, string Nombre, bool TieneCuenta);

/// <summary>Un comunicado: se entrega como aviso en la bandeja de cada destinatario con cuenta.</summary>
public record EnviarComunicadoRequest(string Titulo, string Cuerpo, IReadOnlyList<int> DeveloperIds);

// ── Equipos (edición) ────────────────────────────────────────────────────────────

/// <summary>Alta o edición de un equipo. <paramref name="Id"/> en 0 es un equipo nuevo.</summary>
public record GuardarEquipoRequest(int Id, string Nombre, string? Descripcion, string? ColorHex);

/// <summary>
/// Mueve personas a un equipo (o las deja sin equipo, con <paramref name="EquipoId"/> nulo).
///
/// Acepta VARIAS a la vez porque así funcionaba la rotación del escritorio: se elegía un grupo y se
/// mandaba junto con un mismo motivo, que es como se reorganiza de verdad un equipo.
/// </summary>
public record MoverIntegrantesRequest(IReadOnlyList<int> DeveloperIds, int? EquipoId, string? Nota);

/// <summary>Asigna el rol de alguien dentro de su equipo. Líder es excluyente: solo puede haber uno.</summary>
public record AsignarRolRequest(int DeveloperId, TeamRole Rol);

/// <summary>Una rotación registrada: de qué equipo a cuál, cuándo y por qué.</summary>
public record RotacionDto(
    int Id,
    string Desarrollador,
    string DeEquipo,
    string AEquipo,
    string? Nota,
    DateTime FechaUtc);

// ── Usuarios ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Una cuenta de acceso, tal como se ve en la pantalla de usuarios.
///
/// <b>Lo que no está es deliberado</b>: ni el hash de la contraseña, ni el sello de sesión. El hash
/// es un secreto que en una web queda a un «ver código fuente» de distancia, y el sello permitiría
/// deducir cuándo se invalidó una sesión. Nada de eso hace falta para pintar la lista.
/// </summary>
/// <param name="Bloqueado">Bloqueada AHORA por intentos fallidos. Se resuelve en el servidor para no
/// depender del reloj del navegador.</param>
/// <param name="IntentosFallidos">Intentos fallidos acumulados. Es lo que avisa antes del bloqueo.</param>
public record UsuarioDto(
    int Id,
    string Usuario,
    string Nombre,
    UserRole Rol,
    string RolTexto,
    int? DeveloperId,
    string? Desarrollador,
    bool Activo,
    bool Bloqueado,
    DateTime? BloqueadoHastaUtc,
    int IntentosFallidos,
    bool DebeCambiarContrasena,
    DateTime AltaUtc);

/// <summary>
/// Lo que hace falta para pintar la pantalla de usuarios de una vez: las cuentas, las fichas a las
/// que se pueden ligar y las reglas de bloqueo, que la pantalla explica al usuario y que tenerlas
/// escritas a mano en la interfaz garantizaba que un día dejaran de coincidir con el servidor.
/// </summary>
public record PantallaDeUsuariosDto(
    IReadOnlyList<UsuarioDto> Usuarios,
    IReadOnlyList<OpcionDto> Desarrolladores,
    int IntentosParaBloquear,
    int MinutosDeBloqueo,
    int LargoMinimoDeContrasena);

/// <summary>
/// Alta de una cuenta.
///
/// <b>No lleva contraseña, y es un cambio respecto al escritorio</b>: allí el líder la tecleaba en el
/// diálogo de alta. Aquí la genera el servidor y se enseña UNA sola vez, igual que ya hacía el
/// restablecimiento portado. Una contraseña escrita a mano por otra persona viaja por chat, se repite
/// entre cuentas y sobrevive al primer inicio de sesión; la temporal obliga a cambiarla al entrar.
/// </summary>
public record CrearUsuarioRequest(string Usuario, string Nombre, UserRole Rol, int? DeveloperId);

/// <summary>Edición de una cuenta. La contraseña no se toca por aquí: tiene su propia acción.</summary>
public record ActualizarUsuarioRequest(
    int Id, string Usuario, string Nombre, UserRole Rol, int? DeveloperId, bool Activo);

/// <summary>
/// El resultado de un alta o un restablecimiento. La contraseña temporal se devuelve UNA vez y no se
/// guarda en ningún sitio en claro: si se pierde, se vuelve a restablecer.
/// </summary>
public record ContrasenaTemporalDto(bool Ok, string Mensaje, string? Temporal);
