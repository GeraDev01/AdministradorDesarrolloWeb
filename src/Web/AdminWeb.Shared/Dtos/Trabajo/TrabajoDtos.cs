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
/// Los textos van SIN EMOJI. Los pinta el sistema operativo, no heredan el color del texto y donde no
/// hay fuente de emoji salen como un cuadro vacío —que es exactamente lo que se veía en la columna
/// Prioridad de esta pantalla, un cuadro delante de cada palabra—. La paridad con el escritorio
/// (<c>RequirementsControl.StatusLabel</c>/<c>PriorityLabel</c>, <c>SprintControl.EtiquetaEstado</c>),
/// que era la razón de conservarlos, se acabó con el escritorio. No los repongas.
///
/// La urgencia de la prioridad no se pierde: la pinta la rejilla con un punto de color a partir del
/// enum <c>RequirementPriority</c>, que viaja en el DTO junto al texto; ver <see cref="ColorDePrioridad"/>
/// más abajo. El razonamiento largo está en <see cref="EtiquetasDeCatalogo"/>.
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
        RequirementPriority.Baja    => "Baja",
        RequirementPriority.Media   => "Media",
        RequirementPriority.Alta    => "Alta",
        RequirementPriority.Critica => "Crítica",
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

    // ── ATENCIÓN: LOS CUATRO MÉTODOS DE ABAJO DEVUELVEN CSS, NO COLORES ──────────────────────────
    //
    // Devuelven la cadena «var(--…)» literal, y eso solo significa algo dentro de un atributo style
    // de una página: quien la resuelve es el NAVEGADOR, no .NET. Es lo que permite que el mismo
    // estado se lea bien en tema claro y en oscuro sin que este archivo sepa cuál está puesto.
    //
    // Este archivo vive en el proyecto COMPARTIDO con el servidor. Hoy solo lo consumen las pantallas
    // (Sprint, Requerimientos y Mis asignaciones) y por eso es seguro. Si mañana alguien lo usa para
    // generar un Word, un PDF o un correo, var() no resuelve ahí y el texto saldrá SIN COLOR y sin
    // ningún error que lo delate: en ese caso hace falta una tabla aparte con valores reales.

    /// <summary>
    /// Color del estado de un requerimiento.
    ///
    /// Ya no son los tonos de <c>AppTheme.StatusColor</c> sino los CINCO cajones semánticos de la
    /// aplicación —éxito, aviso, peligro, en curso y neutro—, que es todo el vocabulario de color que
    /// hay. Siete estados en cinco cajones significa que algunos comparten color a propósito: el
    /// nombre del estado va SIEMPRE escrito al lado, así que el color agrupa («esto reclama», «esto
    /// ya terminó») y es el texto el que identifica. Inventar dos tonos más para no repetir sería
    /// volver a tener una paleta que nadie sabe leer.
    /// </summary>
    public static string ColorDeEstado(RequirementStatus s) => s switch
    {
        RequirementStatus.PorEstimar   => "var(--rz-text-secondary-color)",  // sin empezar
        RequirementStatus.Estimado     => "var(--rz-info)",                  // listo para arrancar
        RequirementStatus.EnDesarrollo => "var(--rz-warning)",               // en manos de alguien
        RequirementStatus.EnPruebas    => "var(--rz-info)",                  // en curso
        RequirementStatus.PorEntregar  => "var(--rz-warning)",               // espera a alguien
        RequirementStatus.Entregado    => "var(--rz-success)",
        RequirementStatus.Cancelado    => "var(--rz-danger)",
        _                              => "var(--rz-text-secondary-color)"
    };

    /// <summary>
    /// Color de la prioridad de un requerimiento.
    ///
    /// Existe porque la etiqueta de <see cref="Prioridad"/> dejó de llevar su círculo de emoji, y ahí
    /// el color SÍ hacía un trabajo que la palabra no hace: en una rejilla de trece filas la urgencia
    /// se veía sin leer nada. Las pantallas lo reponen con un punto pintado con estas variables, que
    /// obedecen al tema en vez de al sistema operativo. La palabra va SIEMPRE al lado: un punto solo
    /// no lo distingue quien no separa el rojo del verde.
    ///
    /// <para>Éstas cuatro sí son el SEMÁFORO, al revés que el color de presencia: una prioridad es una
    /// escala de urgencia de verdad —crítica reclama y baja no— y ése es justo el vocabulario que
    /// rojo/ámbar/azul/gris ya tiene. «Baja» va al neutro y no al verde porque no es un logro.</para>
    /// </summary>
    public static string ColorDePrioridad(RequirementPriority p) => p switch
    {
        RequirementPriority.Critica => "var(--rz-danger)",
        RequirementPriority.Alta    => "var(--rz-warning)",
        RequirementPriority.Media   => "var(--rz-info)",
        _                           => "var(--rz-text-secondary-color)"   // Baja
    };

    /// <summary>
    /// Color del veredicto del sprint. Se compara contra el TEXTO que devuelve el servicio porque el
    /// veredicto ES ese texto: no hay un enum detrás que se pueda usar en su lugar.
    ///
    /// «Al día» va al color informativo y no al acento: ir al día es un estado, no la acción
    /// principal de la pantalla, y el acento está reservado para lo que hay que mirar o pulsar.
    ///
    /// <para>EL ✓ DE «Terminado ✓» SE QUEDA, Y NO ES UN DESCUIDO DE LA LIMPIEZA DE EMOJI. Dos motivos.
    /// El primero es que no comparte el defecto: U+2713 sale de la fuente de TEXTO, hereda el color y
    /// no depende de que haya fuente de emoji, así que no pinta ningún cuadro vacío. El segundo es el
    /// que importa: esto de aquí no es una etiqueta, es una COMPARACIÓN POR IGUALDAD, y la cadena la
    /// escribe <c>SprintService.CalcularAvance</c>. Quitarle el ✓ a un solo lado hace que el switch
    /// deje de casar, el veredicto caiga al caso por defecto y pierda el verde —sin error de
    /// compilación, sin excepción y sin que ninguna prueba lo note, porque cada lado se comprueba por
    /// separado—. Si algún día se toca, se tocan A LA VEZ este literal, el de <c>SprintService</c> y
    /// los asserts de <c>SprintServiceTests</c>; nunca uno suelto.</para>
    /// </summary>
    public static string ColorDeVeredicto(string veredicto) => veredicto switch
    {
        "Adelantado" or "Terminado ✓" => "var(--rz-success)",
        "Al día"                      => "var(--rz-info)",
        "Atrasado"                    => "var(--rz-danger)",
        "Terminó incompleto"          => "var(--rz-warning)",
        _                             => "var(--rz-text-secondary-color)"
    };

    /// <summary>
    /// Color del cumplimiento de un sprint cerrado, con los mismos cortes del escritorio
    /// (<c>SprintControl.ColorCumplimiento</c>): 90 y 70.
    /// </summary>
    public static string ColorDeCumplimiento(int pct) => pct switch
    {
        >= 90 => "var(--rz-success)",
        >= 70 => "var(--rz-warning)",
        _     => "var(--rz-danger)"
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
/// <param name="Prioridad">El enum, además de su texto, y por el mismo motivo que en
/// <see cref="RequerimientoDto"/>: la rejilla pinta el punto de urgencia con
/// <see cref="EtiquetasDeTrabajo.ColorDePrioridad"/> y el color tiene que salir del VALOR. Sin este
/// campo la pantalla tendría que volver a convertir la palabra en enum para saber de qué color va,
/// que es exactamente lo que se rompe el día que alguien cambia una etiqueta.</param>
/// <param name="TiempoTexto">El tiempo ya formateado («2h 05m 30s»). Lo formatea el servidor porque
/// el formato vive en <c>WorkSessionService.Format</c>, en la capa de aplicación, y el navegador no
/// la puede referenciar: escribirlo otra vez aquí dejaría dos formatos que se desincronizarían.</param>
public record MiAsignacionDto(
    int Id,
    string Titulo,
    RequirementStatus Estado,
    string EstadoTexto,
    RequirementPriority Prioridad,
    string PrioridadTexto,
    decimal? HorasEstimadas,
    DateTime? FechaCompromiso,
    int AvancePct,
    bool Vencido,
    int SegundosDedicados,
    string TiempoTexto,
    EstadoDelCronometro Cronometro);
