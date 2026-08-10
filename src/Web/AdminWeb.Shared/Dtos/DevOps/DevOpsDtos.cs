using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.DevOps;

// ── La regla que gobierna este archivo ───────────────────────────────────────────
//
// AQUÍ NO HAY NINGÚN PAT, y no es un descuido: es la razón de ser de todo el vertical. En el
// escritorio el token vivía en la máquina de cada quien cifrado con DPAPI y el proceso lo leía
// directamente; en la web el navegador solo puede saber SI hay token configurado, nunca cuál es.
// Un contrato que llevara el valor —aunque la pantalla no lo pintara— lo dejaría a la vista de
// cualquiera que abriese la consola del navegador, y un PAT con permiso «Work Items → Read & write»
// alcanza para reescribir el tablero entero de la organización.
//
// Por eso el único sentido en que un PAT viaja es HACIA ARRIBA: cuando la persona lo captura o
// pide probarlo (ver GuardarMiPatRequest y ProbarPatRequest). De vuelta jamás.

// ── Estado de la integración ─────────────────────────────────────────────────────

/// <summary>
/// Si se puede hablar con Azure DevOps y con qué. Sustituye a lo que en el escritorio se resolvía
/// mirando archivos locales (<c>LocalDevOpsConfig</c>) desde la propia pantalla.
/// </summary>
/// <param name="Organizacion">La URL de la organización. No es secreta y sirve para que quien mira
/// la pantalla confirme que apunta a donde cree.</param>
/// <param name="Habilitada">El interruptor <c>AzureDevOpsEnabled</c> de la configuración: es del
/// administrador y gobierna las sincronizaciones completas.</param>
/// <param name="TengoPatPropio">Si la persona de la sesión tiene su token configurado. Es lo ÚNICO
/// que se puede contestar sobre un secreto.</param>
/// <param name="HayPatDeOrganizacion">Si existe el token compartido de la instalación, con el que se
/// puede leer aunque nadie tenga el suyo.</param>
/// <param name="Explicacion">Qué falta, dicho para que se entienda. Es el texto que la pantalla
/// enseña tal cual en lugar de inventarse uno.</param>
/// <param name="UltimaSincronizacionUtc">Cuándo terminó bien la última sincronización, o null si
/// nunca se ha hecho. La sincronización es MANUAL —no hay trabajo de fondo que la dispare—, así que
/// esta fecha es lo único que dice si lo que se está viendo es de hoy o de la semana pasada.</param>
public record EstadoDevOpsDto(
    bool Configurada,
    string? Organizacion,
    string? Proyecto,
    bool Habilitada,
    bool TengoPatPropio,
    bool HayPatDeOrganizacion,
    string Explicacion,
    DateTime? UltimaSincronizacionUtc = null)
{
    /// <summary>Hay organización, proyecto y algún token con el que preguntar.</summary>
    public bool SePuedeUsar => Configurada && (TengoPatPropio || HayPatDeOrganizacion);
}

// ── Un ticket ────────────────────────────────────────────────────────────────────

/// <summary>
/// Un work item ya sincronizado, tal como lo pintan las tres pantallas.
///
/// Es un contrato propio y no la entidad de EF: <c>DevOpsTicket</c> arrastra el correo del asignado,
/// el identificador de quien confirmó la prioridad y su colección de vínculos con Freshdesk, y nada
/// de eso pinta una rejilla.
/// </summary>
/// <param name="Id">El identificador LOCAL (la fila). Es el que usan vigilar y los vínculos.</param>
/// <param name="Numero">El número del work item en Azure DevOps, que es el que la gente dice en voz
/// alta y el que se usa contra la API.</param>
/// <param name="SinPrioridadDefinida">Nadie ha fijado la prioridad DESDE la aplicación. No se puede
/// deducir de <paramref name="Prioridad"/>: DevOps le pone 2 por omisión a todo lo que se crea.</param>
/// <param name="SinEstimar">Está asignado y quien lo tiene todavía no dijo cuánto le va a llevar.</param>
public record TicketDevOpsDto(
    int Id,
    int Numero,
    string Titulo,
    string Tipo,
    string Estado,
    string Prioridad,
    string AsignadoA,
    string Iteracion,
    string Area,
    string Etiquetas,
    double? Puntos,
    double? HorasEstimadas,
    int Comentarios,
    DateTime? ActualizadoUtc,
    DateTime SincronizadoUtc,
    string Url,
    bool Vigilado,
    bool SinPrioridadDefinida,
    bool SinEstimar,
    bool Cerrado);

/// <summary>Un desarrollador al que se le puede reasignar un work item.</summary>
/// <param name="Correo">El <c>uniqueName</c> con el que DevOps identifica a la persona. Sin él no se
/// puede reasignar: la API de DevOps empata por correo, no por nombre.</param>
public record DesarrolladorDevOpsDto(int Id, string Texto, string? Correo);

// ── Pantalla del líder: el tablero ───────────────────────────────────────────────

/// <summary>
/// Todo lo que enseña «Azure DevOps» de una vez.
///
/// Va junto porque la pantalla no se puede pintar a trozos: las estadísticas, los desplegables de
/// «mover a estado» y los desarrolladores a los que reasignar salen de los mismos tickets que la
/// rejilla, y pedirlos por separado obligaría al navegador a encadenar cinco peticiones.
/// </summary>
/// <param name="EstadosConocidos">Los estados vistos en la organización. Son los que se ofrecen al
/// mover un ticket de columna: DevOps valida la transición, así que ofrecer los que existen es lo
/// más lejos que se puede llegar sin preguntarle la plantilla de proceso.</param>
/// <param name="SinPrioridad">Cuántos ABIERTOS no tienen prioridad definida. Se cuenta sobre el
/// total y no sobre lo filtrado: es trabajo pendiente del líder y no debe cambiar porque alguien
/// esté mirando otra cosa.</param>
public record TableroDevOpsDto(
    EstadoDevOpsDto Integracion,
    IReadOnlyList<TicketDevOpsDto> Filas,
    IReadOnlyList<FiltroGuardadoDto> FiltrosGuardados,
    IReadOnlyList<ReglaDeAsignacionDto> Reglas,
    IReadOnlyList<DesarrolladorDevOpsDto> Desarrolladores,
    IReadOnlyList<string> EstadosConocidos,
    IReadOnlyList<string> TiposConocidos,
    DateTime? UltimaSincronizacionUtc,
    int SinPrioridad,
    int Vigilados);

// ── Pantalla del líder: tablero por etiqueta ─────────────────────────────────────

/// <summary>Una fila del tablero por etiqueta: cuánto hay de cada tipo bajo esa etiqueta.</summary>
public record FilaPorEtiquetaDto(
    string Etiqueta, int Total, int Abiertos, int Bugs, int Tareas, int UserStories, int Otros);

/// <summary>
/// El tablero por etiqueta: qué cliente o categoría acumula más bugs, tareas y solicitudes.
/// </summary>
/// <param name="Etiquetas">Todas las etiquetas presentes, para el desplegable de «entrar a».</param>
/// <param name="DentroDe">La etiqueta en la que se entró, o nulo. Esa etiqueta no aparece como fila:
/// dentro de «Bepensa» todos los tickets son de Bepensa y contarlo no diría nada.</param>
/// <param name="Resumen">El pie de la tabla, escrito por el servidor porque distingue dos casos que
/// se leen distinto («N etiquetas sobre M tickets» / «dentro de X, N etiquetas que coexisten»).</param>
public record TableroPorEtiquetaDto(
    IReadOnlyList<string> Etiquetas,
    IReadOnlyList<FilaPorEtiquetaDto> Filas,
    string? DentroDe,
    int TicketsDelSubconjunto,
    int Bugs,
    int Tareas,
    int UserStories,
    string Resumen);

// ── Pantalla del desarrollador: mis tickets ──────────────────────────────────────

/// <summary>
/// «Mis tickets DevOps»: los work items asignados a quien tiene la sesión.
/// </summary>
/// <param name="TieneFicha">Falso si la cuenta no está ligada a una ficha de desarrollador. Sin
/// ficha no hay con qué empatar el asignado del ticket, así que no hay lista que enseñar.</param>
/// <param name="PuedoSincronizar">Si se puede pedir a DevOps lo propio. Exige token personal,
/// organización y proyecto; NO exige el interruptor del administrador, igual que en el escritorio:
/// si dependiera de él, nadie podría actualizar su lista hasta que el líder sincronizara.</param>
/// <param name="Aviso">El texto que se enseña cuando no hay filas. Distingue «todavía no hay nada
/// sincronizado» de «hay tickets pero ninguno a tu nombre», que llevan a acciones distintas.</param>
/// <param name="Abiertos">Los sin cerrar sobre el TOTAL, no sobre lo filtrado: es el trabajo
/// pendiente real y no debe cambiar porque se acote la vista.</param>
public record MisTicketsDevOpsDto(
    bool TieneFicha,
    bool TengoPatPropio,
    bool PuedoSincronizar,
    string? Aviso,
    IReadOnlyList<TicketDevOpsDto> Filas,
    IReadOnlyList<string> Estados,
    IReadOnlyList<string> Tipos,
    IReadOnlyList<string> Iteraciones,
    int Total,
    int Abiertos,
    int SinEstimar,
    DateTime? UltimaSincronizacionUtc,
    string Resumen);

// ── Comentarios ──────────────────────────────────────────────────────────────────

/// <summary>
/// Un comentario del ticket.
/// </summary>
/// <param name="Texto">Texto PLANO. DevOps guarda los comentarios en HTML y los escribe gente de
/// fuera del equipo; el marcado se quita en el servidor para que la pantalla no tenga siquiera la
/// tentación de pintarlo como HTML.</param>
public record ComentarioDevOpsDto(string Texto, string Autor, DateTime CreadoUtc);

/// <summary>Los comentarios de un ticket, con lo justo para titular el panel.</summary>
public record ComentariosDevOpsDto(
    int Numero, string Titulo, IReadOnlyList<ComentarioDevOpsDto> Comentarios);

// ── Ficha: regresiones y devoluciones ────────────────────────────────────────────

/// <summary>Un bug colgado como hijo del ticket. Cada uno es una regresión que provocó.</summary>
public record RegresionDto(int Id, string Titulo, string Estado, string Url, bool Cerrado);

/// <summary>Un cambio de dueño del ticket. Sin correos: para leer el historial basta el nombre.</summary>
public record CambioDeAsignacionDto(DateTime FechaUtc, string? De, string? A);

/// <summary>
/// Lo que no cabe en los campos del ticket: qué regresiones colgó y por cuántas manos pasó.
/// </summary>
/// <param name="Devoluciones">Cuántas veces le devolvieron el ticket al asignado actual DESPUÉS de
/// que lo tuviera alguien más. La primera asignación no cuenta, ni que se lo reasignen a uno mismo
/// dos veces seguidas: lo que duele es que vuelva tras haber pasado por otras manos.</param>
/// <param name="Manos">Cuántas personas distintas lo han tenido.</param>
public record FichaDeTicketDto(
    int Numero,
    string Titulo,
    string AsignadoA,
    IReadOnlyList<RegresionDto> Regresiones,
    int RegresionesAbiertas,
    IReadOnlyList<CambioDeAsignacionDto> Asignaciones,
    int Devoluciones,
    int Manos);

// ── Filtros guardados ────────────────────────────────────────────────────────────

/// <summary>
/// Un filtro con nombre de la rejilla grande.
/// </summary>
/// <param name="AjustesDeRejilla">El estado de la rejilla serializado (filtros por columna, orden,
/// anchos y columnas visibles). En el escritorio esta columna guardaba solo los valores permitidos
/// por columna, porque el filtro estilo Excel estaba escrito a mano; aquí lo trae la rejilla de
/// serie y su estado incluye además el orden y las columnas, que es lo que de verdad se quiere
/// recuperar al volver a una vista guardada.</param>
public record FiltroGuardadoDto(
    int Id, string Nombre, string? Busqueda, string? TituloContiene, string? AjustesDeRejilla);

/// <summary>Alta o reemplazo de un filtro guardado. Se empata por NOMBRE, igual que el escritorio.</summary>
public record GuardarFiltroRequest(
    string Nombre, string? Busqueda, string? TituloContiene, string? AjustesDeRejilla);

// ── Reglas de auto-asignación ────────────────────────────────────────────────────

/// <summary>
/// Una regla de auto-asignación al importar work items como requerimientos. Se evalúan por orden y
/// gana la primera que coincida.
/// </summary>
public record ReglaDeAsignacionDto(
    int Id,
    DevOpsRuleMatch Condicion,
    string CondicionTexto,
    string Valor,
    int DesarrolladorId,
    string Desarrollador,
    int Orden,
    bool Activa);

/// <summary>Alta o edición de una regla. <c>Id = 0</c> significa alta.</summary>
public record GuardarReglaRequest(
    int Id, DevOpsRuleMatch Condicion, string Valor, int DesarrolladorId, int Orden, bool Activa);

// ── Peticiones ───────────────────────────────────────────────────────────────────

/// <summary>
/// Sincronización SELECTIVA del líder. Todo vacío = traer todo lo no removido.
///
/// Los tres filtros se combinan con Y, igual que en el escritorio: sirven para que la sincronización
/// sea rápida, no para descubrir tickets.
/// </summary>
/// <param name="Correos">Correos de las personas cuyas asignaciones se quieren traer. Van por correo
/// y no por nombre porque es lo que entiende la WIQL de DevOps.</param>
/// <param name="CambiadosEnDias">Solo lo movido en los últimos N días. Nulo = todo el historial.</param>
public record SincronizarDevOpsRequest(
    IReadOnlyList<string>? Tipos = null,
    IReadOnlyList<string>? Estados = null,
    IReadOnlyList<string>? Correos = null,
    int? CambiadosEnDias = null);

/// <summary>
/// Sincronización del DESARROLLADOR: lo asignado a la cuenta de SU token, en una ventana de días.
/// </summary>
public record SincronizarMisTicketsRequest(int Dias);

/// <summary>Reasignación en DevOps. Correo vacío o nulo DESASIGNA.</summary>
public record ReasignarTicketRequest(string? Correo);

/// <summary>Mover el ticket de columna (System.State). DevOps valida la transición.</summary>
public record CambiarEstadoRequest(string Estado);

/// <summary>Prioridad de DevOps: 1 (muy alta) a 4 (baja).</summary>
public record CambiarPrioridadRequest(int Prioridad);

/// <summary>Estimación en horas, que va también al campo Effort del work item.</summary>
public record EstimarTicketRequest(double Horas);

/// <summary>
/// Captura del token personal. Es la ÚNICA dirección en que un PAT viaja: hacia el servidor.
/// </summary>
public record GuardarMiPatRequest(string Pat);

/// <summary>
/// Prueba de conexión. Con <paramref name="Pat"/> se prueba el token recién escrito SIN guardarlo
/// —igual que el diálogo del escritorio—; vacío prueba el que ya esté configurado.
/// </summary>
public record ProbarPatRequest(string? Pat);

// ── Resultados ───────────────────────────────────────────────────────────────────

/// <summary>
/// Cómo fue una sincronización.
/// </summary>
/// <param name="CambiosVigilados">Los tickets vigilados que se movieron, ya redactados. Sustituyen a
/// la barra amarilla y al globo del área de notificación del escritorio.</param>
public record ResultadoDeSincronizacionDto(
    bool Ok, string Mensaje, int Nuevos, int Actualizados, IReadOnlyList<string> CambiosVigilados);

/// <summary>Cómo fue una importación a requerimientos, con lo que se dio de alta y a quién le tocó.</summary>
public record ResultadoDeImportacionDto(
    bool Ok, string Mensaje, IReadOnlyList<string> Nuevos);
