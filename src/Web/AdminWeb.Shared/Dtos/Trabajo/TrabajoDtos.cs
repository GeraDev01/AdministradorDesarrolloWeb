using AdminWeb.Shared.Dtos.Jornada;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Trabajo;

/// <summary>
/// Los textos y los colores con los que se presenta el trabajo (requerimientos y sprint).
///
/// Viven en Shared —y no en el servidor— por el mismo motivo que
/// <see cref="EtiquetasDeCatalogo"/>: los necesitan los dos lados. La API para llenar los campos
/// «…Texto» de los DTO, y el cliente para armar los desplegables de filtro sin pedirle al servidor
/// una lista de opciones que no cambia nunca.
///
/// Los textos se copian AL PIE DE LA LETRA de las tres pantallas del escritorio
/// (<c>RequirementsControl.StatusLabel</c>/<c>PriorityLabel</c>, <c>SprintControl.EtiquetaEstado</c>),
/// emojis incluidos: mientras las dos aplicaciones convivan, quien mire una y otra tiene que leer
/// lo mismo.
/// </summary>
public static class EtiquetasDeTrabajo
{
    public static string Estado(RequirementStatus s) => s switch
    {
        RequirementStatus.PorEstimar   => "Por estimar",
        RequirementStatus.Estimado     => "Estimado",
        RequirementStatus.EnDesarrollo => "En desarrollo",
        RequirementStatus.EnPruebas    => "En pruebas",
        RequirementStatus.PorEntregar  => "Por entregar",
        RequirementStatus.Entregado    => "Entregado",
        RequirementStatus.Cancelado    => "Cancelado",
        _                              => s.ToString()
    };

    public static string Prioridad(RequirementPriority p) => p switch
    {
        RequirementPriority.Baja    => "🔵 Baja",
        RequirementPriority.Media   => "🟡 Media",
        RequirementPriority.Alta    => "🟠 Alta",
        RequirementPriority.Critica => "🔴 Crítica",
        _                           => p.ToString()
    };

    /// <summary>De dónde salió el requerimiento, con los mismos tres textos del escritorio.</summary>
    public static string Origen(RequirementSource o) => o switch
    {
        RequirementSource.AzureDevOps => "DevOps",
        RequirementSource.Email       => "Correo",
        _                             => "Manual"
    };

    /// <summary>
    /// Qué documento es de los que cuelgan de un requerimiento. Son los tres textos de
    /// <c>RequirementAttachmentService.KindLabel</c> del escritorio, al pie de la letra: mientras las
    /// dos aplicaciones convivan, el mismo archivo tiene que llamarse igual en las dos.
    /// </summary>
    public static string TipoDeAdjunto(RequirementAttachmentKind k) => k switch
    {
        RequirementAttachmentKind.Requerimiento => "Requerimiento",
        RequirementAttachmentKind.Estimacion    => "Estimación",
        _                                       => "Otro"
    };

    /// <summary>
    /// Color del estado, en hex para la web.
    ///
    /// Es la MISMA familia de tonos que pintaba <c>AppTheme.StatusColor</c> —ámbar para «en
    /// desarrollo», verde para «entregado», rojo para «cancelado»—, porque esa asociación ya está
    /// aprendida y perderla en la mudanza costaría más que conservarla. Lo que cambia es el escalón:
    /// aquí se usa el oscuro de cada familia y no el claro, porque en la web esto es texto sobre una
    /// tarjeta blanca y los tonos del escritorio no llegan al contraste mínimo (el ámbar claro sobre
    /// blanco queda en 1.8:1, ilegible). No es una paleta nueva: es el mismo criterio que el propio
    /// escritorio ya aplicaba en <c>SprintControl</c>, donde el veredicto se pinta con los tonos
    /// oscuros de esas mismas familias.
    /// </summary>
    public static string ColorDeEstado(RequirementStatus s) => s switch
    {
        RequirementStatus.PorEstimar   => "#64748B",
        RequirementStatus.Estimado     => "#2563EB",
        RequirementStatus.EnDesarrollo => "#B45309",
        RequirementStatus.EnPruebas    => "#7C3AED",
        RequirementStatus.PorEntregar  => "#C2410C",
        RequirementStatus.Entregado    => "#15803D",
        RequirementStatus.Cancelado    => "#DC2626",
        _                              => "#64748B"
    };

    /// <summary>
    /// Color del veredicto del sprint. Son los mismos hex de <c>SprintControl.ColorVeredicto</c>, y
    /// se compara contra el texto que devuelve el servicio porque el veredicto ES ese texto: no hay
    /// un enum detrás que se pueda usar en su lugar.
    /// </summary>
    public static string ColorDeVeredicto(string veredicto) => veredicto switch
    {
        "Adelantado" or "Terminado ✓" => "#15803D",
        "Al día"                      => "#2563EB",
        "Atrasado"                    => "#DC2626",
        "Terminó incompleto"          => "#B45309",
        _                             => "#64748B"
    };

    /// <summary>
    /// Color del cumplimiento de un sprint cerrado, con los mismos cortes del escritorio
    /// (<c>SprintControl.ColorCumplimiento</c>): 90 y 70.
    /// </summary>
    public static string ColorDeCumplimiento(int pct) => pct switch
    {
        >= 90 => "#15803D",
        >= 70 => "#B45309",
        _     => "#DC2626"
    };
}

// ── Requerimientos (pantalla del líder) ─────────────────────────────────────────

/// <summary>
/// Lo que enseña la pantalla de requerimientos de una vez: las filas que cumplen el filtro y los
/// desarrolladores con los que se filtra y se asigna.
///
/// Las dos cosas viajan juntas porque la pantalla no puede pintarse sin ambas, y separarlas
/// obligaría al navegador a encadenar dos peticiones para dibujar una sola tabla.
/// </summary>
/// <param name="Desarrolladores">Solo los ACTIVOS, igual que en el escritorio: asignar trabajo
/// nuevo a alguien dado de baja no es un caso que haya que facilitar.</param>
/// <param name="MaxAdjuntoBytes">El tope de un documento adjunto, para que la pantalla avise antes de
/// empujar por la red algo que va a rebotar. El que cuenta lo aplica el servidor.</param>
public record RequerimientosDto(
    IReadOnlyList<RequerimientoDto> Filas,
    IReadOnlyList<OpcionDto> Desarrolladores,
    long MaxAdjuntoBytes);

/// <summary>
/// Un requerimiento tal como lo ve el líder.
/// </summary>
/// <param name="Vencido">Su fecha de compromiso ya pasó y no está entregado ni cancelado. Lo calcula
/// el servidor con SU fecha, no el navegador con la suya: el «hoy» de un equipo con el reloj
/// corrido pintaría de rojo lo que no lo está.</param>
/// <param name="Sello">El sello de concurrencia en base64, o nulo si la base no lo lleva (SQLite).
/// Viaja de ida y vuelta para que dos personas editando la misma ficha no se pisen: el segundo en
/// guardar recibe un 409 en vez de sobrescribir en silencio lo que escribió el primero.</param>
/// <param name="Adjuntos">Cuántos documentos cuelgan del requerimiento. Es el indicador de la rejilla
/// del escritorio: solo el NÚMERO, porque los bytes se piden por su ruta al abrirlos.</param>
public record RequerimientoDto(
    int Id,
    string Titulo,
    string? Detalle,
    RequirementStatus Estado,
    string EstadoTexto,
    RequirementPriority Prioridad,
    string PrioridadTexto,
    IReadOnlyList<int> DesarrolladoresIds,
    string DesarrolladoresTexto,
    decimal? HorasEstimadas,
    DateTime? FechaSolicitud,
    DateTime? FechaCompromiso,
    DateTime? FechaEntrega,
    int AvancePct,
    string OrigenTexto,
    bool EsDeDevOps,
    string? Enlace,
    bool Vencido,
    string? Sello,
    int Adjuntos);

/// <summary>
/// Un documento colgado de un requerimiento, tal como lo pinta la lista: <b>sin su contenido</b>. Los
/// bytes se piden a <c>/api/adjuntos/requerimiento/{id}</c> cuando alguien pulsa.
/// </summary>
/// <param name="TipoTexto">La etiqueta del tipo. El color lo pone la pantalla, como en todo lo demás.</param>
/// <param name="Bytes">Tamaño del archivo, para poder decidir si conviene abrirlo ahora.</param>
public record AdjuntoDeRequerimientoDto(
    int Id,
    RequirementAttachmentKind Tipo,
    string TipoTexto,
    string Nombre,
    long Bytes,
    DateTime SubidoUtc);

/// <summary>
/// Alta y edición de un requerimiento. Es el mismo formulario del escritorio
/// (<c>RequirementDetailForm</c>), campo por campo.
///
/// El <see cref="Sello"/> va nulo al dar de alta —no hay nada con qué chocar todavía— y con el valor
/// que trajo la ficha al editar.
/// </summary>
public record GuardarRequerimientoRequest(
    string Titulo,
    string? Detalle,
    RequirementStatus Estado,
    RequirementPriority Prioridad,
    decimal? HorasEstimadas,
    DateTime? FechaSolicitud,
    DateTime? FechaCompromiso,
    DateTime? FechaEntrega,
    int AvancePct,
    string? Sello);

/// <summary>
/// Quiénes quedan asignados al requerimiento. Es la semántica del diálogo de casillas del
/// escritorio: <b>lo que viene aquí es lo que queda</b>, y quien no venga deja de estar asignado.
/// </summary>
public record AsignarDesarrolladoresRequest(IReadOnlyList<int> DesarrolladoresIds);

/// <summary>Cancelar un requerimiento. Lleva el sello por el mismo motivo que guardarlo.</summary>
public record CancelarRequerimientoRequest(string? Sello);

// ── Sprint ──────────────────────────────────────────────────────────────────────

/// <summary>Un sprint en el desplegable de la pantalla de seguimiento.</summary>
public record SprintDto(
    int Id, string Nombre, string? Objetivo, DateTime Inicio, DateTime Fin, bool EnCurso);

/// <summary>
/// El seguimiento de un sprint: la cabecera, el avance y lo comprometido.
///
/// Todo lo que la línea de tiempo dibuja sale de aquí (las fechas del sprint, los días consumidos y
/// las marcas de cada requerimiento), así que la pantalla no calcula nada: solo pinta.
/// </summary>
public record SeguimientoDeSprintDto(
    SprintDto Sprint,
    AvanceDeSprintDto Avance,
    IReadOnlyList<RequerimientoDeSprintDto> Requerimientos,
    int Mios);

/// <summary>
/// El avance del sprint, calculado SIEMPRE sobre todo el sprint. Es la copia literal de
/// <c>SprintAvance</c>, que no puede cruzar al navegador porque vive en la capa de aplicación.
///
/// Que se calcule sobre el sprint entero —y no sobre lo que el filtro de la pantalla deja ver— es
/// una decisión del escritorio que se conserva: dos porcentajes distintos para el mismo sprint
/// serían dos verdades.
/// </summary>
public record AvanceDeSprintDto(
    int TotalRequerimientos, int Entregados, int EnCurso, int SinEmpezar, int Cancelados,
    int AvanceRealPct, int TiempoPct,
    int DiasTotales, int DiasTranscurridos, int DiasRestantes,
    string Veredicto);

/// <summary>
/// Un requerimiento comprometido en el sprint.
/// </summary>
/// <param name="Mio">Está asignado a quien consulta. Es el 👤 del escritorio: una AYUDA visual, no
/// un filtro de seguridad — el desarrollador ve el sprint completo.</param>
/// <param name="CompromisoVencido">Compromiso pasado y sin entregar. Es lo que el líder vino a ver,
/// y por eso se resuelve en el servidor contra su fecha.</param>
public record RequerimientoDeSprintDto(
    int Id,
    string Titulo,
    RequirementStatus Estado,
    string EstadoTexto,
    int AvancePct,
    DateTime? Compromiso,
    DateTime? Entregado,
    decimal? HorasEstimadas,
    bool Mio,
    bool CompromisoVencido);

/// <summary>
/// El histórico y la velocidad del equipo. Solo del líder.
/// </summary>
/// <param name="Sprints">Del más viejo al más nuevo, para que la gráfica se lea de izquierda a
/// derecha en el tiempo. La tabla los invierte al pintarlos, igual que el escritorio.</param>
/// <param name="Velocidad">Promedio de entregados por sprint CERRADO y con trabajo.</param>
/// <param name="SprintsContados">Cuántos entraron en ese promedio. En cero, la pantalla pone «—» en
/// vez de un 0 que se leería como «el equipo no entrega nada».</param>
/// <param name="CumplimientoPromedio">Promedio del % cumplido de los cerrados con trabajo, o nulo si
/// todavía no hay ninguno.</param>
public record HistoricoDeSprintsDto(
    IReadOnlyList<SprintResumenDto> Sprints,
    double Velocidad,
    int SprintsContados,
    int? CumplimientoPromedio);

/// <summary>Cómo salió (o va) un sprint. Copia de <c>SprintResumen</c> para el navegador.</summary>
public record SprintResumenDto(
    int Id, string Nombre, DateTime Inicio, DateTime Fin,
    int Total, int Entregados, int Cancelados, int CompletadoPct, int DiasTotales, bool Cerrado);

/// <summary>
/// Un requerimiento que se puede colgar del sprint.
///
/// La lista trae los que están SIN sprint más los de ESTE sprint, y ni los cancelados ni los de
/// otros sprints: moverlos debe ser una decisión tomada desde el otro sprint, no el accidente de
/// una casilla.
/// </summary>
public record CandidatoDeSprintDto(
    int Id, string Titulo, string EstadoTexto, DateTime? Compromiso, bool EnElSprint);

/// <summary>Alta y edición de un sprint. Las fechas son DÍAS: la hora se descarta en el servicio.</summary>
public record GuardarSprintRequest(string? Nombre, string? Objetivo, DateTime Inicio, DateTime Fin);

/// <summary>
/// Los requerimientos que quedan en el sprint. Lo marcado es lo que queda: lo que no venga vuelve al
/// backlog (salvo los cancelados, que el servicio nunca desliga por esta vía).
/// </summary>
public record FijarRequerimientosRequest(IReadOnlyList<int> RequerimientoIds);

// ── Mis asignaciones ────────────────────────────────────────────────────────────

/// <summary>En qué estado está el cronómetro de una fila, para saber qué botones ofrecer.</summary>
public enum EstadoDelCronometro
{
    /// <summary>Nunca se arrancó, o ya se detuvo y consolidó.</summary>
    SinSesion = 0,
    Activo = 1,
    Pausado = 2
}

/// <summary>
/// Lo que enseña «Mis asignaciones» de una vez: mis requerimientos con el tiempo que llevo en cada
/// uno, y el cronómetro que esté corriendo.
/// </summary>
/// <param name="TieneFicha">Falso cuando la cuenta no está ligada a una ficha de desarrollador. Esa
/// cuenta no tiene asignaciones ni puede cronometrar: el tiempo se registra contra una ficha.</param>
/// <param name="Cronometro">El cronómetro en marcha de esta persona, sea de esta pantalla o de otra.
/// Es el MISMO contrato que usa «Mi jornada» a propósito: hay un solo cronómetro por persona, y dos
/// formas de describirlo acabarían discrepando.</param>
public record MisAsignacionesDto(
    bool TieneFicha,
    IReadOnlyList<MiAsignacionDto> Filas,
    CronometroDto? Cronometro);

/// <summary>
/// Un requerimiento asignado a quien mira la pantalla.
/// </summary>
/// <param name="TiempoTexto">El tiempo ya formateado («2h 05m 30s»). Lo formatea el servidor porque
/// el formato vive en <c>WorkSessionService.Format</c>, en la capa de aplicación, y el navegador no
/// la puede referenciar: escribirlo otra vez aquí dejaría dos formatos que se desincronizarían.</param>
public record MiAsignacionDto(
    int Id,
    string Titulo,
    RequirementStatus Estado,
    string EstadoTexto,
    string PrioridadTexto,
    decimal? HorasEstimadas,
    DateTime? FechaCompromiso,
    int AvancePct,
    bool Vencido,
    int SegundosDedicados,
    string TiempoTexto,
    EstadoDelCronometro Cronometro);
