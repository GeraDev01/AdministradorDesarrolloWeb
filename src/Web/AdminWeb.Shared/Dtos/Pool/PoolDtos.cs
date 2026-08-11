using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Pool;

// ── Pantalla del desarrollador ───────────────────────────────────────────────────

/// <summary>
/// Todo lo que enseña «Mi pool» de una vez: lo que hay libre y lo que ya se tomó.
///
/// Va junto porque las dos listas se leen a la vez —se elige qué tomar mirando cuánto se tiene ya
/// en curso— y porque separarlas obligaría al navegador a encadenar dos peticiones para pintar una
/// sola pantalla.
/// </summary>
/// <param name="TieneFicha">Falso cuando la cuenta no está ligada a una ficha de desarrollador. Esa
/// cuenta puede mirar el pool pero no tomar nada: no hay a quién abonarle los puntos.</param>
/// <param name="Tipos">Las opciones del filtro por tipo, ya con su texto.</param>
public record MiPoolDto(
    bool TieneFicha,
    IReadOnlyList<OpcionDelPoolDto<PoolWorkType>> Tipos,
    IReadOnlyList<ActividadLibreDto> Disponibles,
    IReadOnlyList<MiActividadDelPoolDto> Mias);

/// <summary>
/// Una opción de un desplegable del pool: el valor del enum y cómo se lee.
///
/// Las etiquetas las manda el SERVIDOR y no las escribe la pantalla. Viven en <c>PoolSeed</c>,
/// portado del escritorio, y el cliente no puede referenciar la capa de aplicación —es una frontera
/// dura de este proyecto—; escribirlas otra vez aquí dejaría dos copias de los mismos textos, y la
/// segunda se desincronizaría el día que alguien renombre un tipo. Es el mismo criterio con el que
/// la pantalla de plantillas pide sus tipos al servidor.
///
/// El valor va NULABLE porque estos desplegables ofrecen «todos», y así la opción tiene exactamente
/// el tipo del filtro que rellena.
/// </summary>
public record OpcionDelPoolDto<T>(T? Valor, string Texto) where T : struct;

/// <summary>
/// Una actividad libre en el pool, tal como se ve ANTES de tomarla.
///
/// Los puntos viajan siempre y se enseñan junto al título: es la diferencia con la autocalificación
/// libre —nadie trabaja sin saber cuánto vale lo que va a hacer—. Vienen congelados de la matriz al
/// crearse la actividad, así que la pantalla los pinta y no los calcula.
/// </summary>
/// <param name="Horas">El PLAZO propio de esta actividad, en horas, tal como se capturó: nulo
/// significa «el de la matriz» y 0 «sin fecha límite». Es el dato crudo, para editar; para ENSEÑAR
/// se usa <paramref name="HorasEfectivas"/>.</param>
/// <param name="HorasEfectivas">El plazo YA RESUELTO contra la matriz: lo que de verdad se va a
/// conceder si se toma ahora. Viaja resuelto desde el servidor porque quien mira el pool tiene que
/// poder ver que un bug da cuatro horas ANTES de tomarlo —es con lo que decide—, y el navegador no
/// tiene la matriz para resolverlo por su cuenta. Nulo o 0 = sin fecha límite.</param>
/// <param name="PuntosMaximos">Base más TODOS los criterios extra. Es lo que está en juego si se
/// hace todo lo que se pide.</param>
/// <param name="CriteriosExtra">Lo que además se va a mirar. Viaja antes de tomarla a propósito:
/// enterarse después convertiría el extra en una trampa.</param>
public record ActividadLibreDto(
    int Id,
    string Titulo,
    string? Detalle,
    PoolWorkType Tipo,
    string TipoTexto,
    PoolComplexity Complejidad,
    string ComplejidadTexto,
    int Puntos,
    string? Enlace,
    PoolPriority Prioridad,
    string PrioridadTexto,
    decimal? Horas,
    decimal? HorasEfectivas,
    int PuntosMaximos,
    IReadOnlyList<CriterioExtraDto> CriteriosExtra);

/// <summary>
/// Una actividad del pool que ya está a nombre de quien mira la pantalla.
/// </summary>
/// <param name="LimiteUtc">Cuándo se espera entregada, ya como INSTANTE. Nulo si no se fijó plazo ni
/// en la actividad ni en su celda de la matriz. Se calculó al tomarla sumando horas.</param>
/// <param name="HorasEstimadas">El ESFUERZO estimado, en horas: lo que se dijo que costaría. No es
/// el plazo —eso es <paramref name="LimiteUtc"/>—, es la carga de trabajo, y es el número contra el
/// que se va a contrastar el cronómetro. En un bug lo escribió quien la tomó, al tomarla; en una
/// tarea o un requerimiento lo escribió el líder al publicarla.</param>
/// <param name="ChecklistHechos">Puntos del checklist ya cumplidos, y <paramref name="ChecklistTotal"/>
/// cuántos son. Los cuenta el servidor de una sola consulta: pedir el checklist de cada fila para
/// pintar «3/5» sería un viaje de red por actividad.</param>
/// <param name="MotivoDeDevolucion">Lo que escribió el líder al devolverla. Es lo primero que hay que
/// leer cuando el estado es «Devuelta»: sin eso, se vuelve a entregar igual.</param>
public record MiActividadDelPoolDto(
    int Id,
    string Titulo,
    string? Detalle,
    string TipoTexto,
    int Puntos,
    PoolActivityStatus Estado,
    string EstadoTexto,
    DateTime? LimiteUtc,
    bool Vencida,
    bool EnCurso,
    decimal? HorasEstimadas,
    int ChecklistHechos,
    int ChecklistTotal,
    string? MotivoDeDevolucion,
    string? Enlace)
{
    /// <summary>El avance del checklist, listo para la rejilla («3/5»).</summary>
    public string Avance => ChecklistTotal == 0 ? "—" : $"{ChecklistHechos}/{ChecklistTotal}";
}

// ── Pantalla del líder ───────────────────────────────────────────────────────────

/// <summary>
/// Lo que enseña la pantalla del líder: el pool completo y la cola de verificación.
///
/// Las dos listas viajan juntas aunque una sea subconjunto de la otra, porque los filtros de estado
/// y tipo son del pool y no de la cola: con el pool filtrado por «Disponible», mirar lo que espera
/// verificación seguiría siendo necesario y ya no estaría en la lista.
/// </summary>
public record PoolDelLiderDto(
    IReadOnlyList<ActividadDelPoolDto> Actividades,
    IReadOnlyList<EntregaPorVerificarDto> Pendientes);

/// <summary>
/// Una actividad del pool vista por el líder. Lleva el tipo y la complejidad crudos, además de sus
/// etiquetas, porque son lo que rellena el formulario al editarla.
/// </summary>
/// <param name="QuienLaTiene">Nombre de quien la tomó, o nulo si sigue libre.</param>
/// <param name="Devoluciones">Cuántas veces volvió al pool. Devolver no se castiga, pero el número
/// dice algo: una actividad devuelta cinco veces es un problema de la actividad o de quien la toma.</param>
/// <param name="Horas">El PLAZO propio de la actividad, en horas: cuánto se concede desde que
/// alguien la toma. Nulo = el de la matriz (solo en tarea y requerimiento; un bug siempre lo trae).
/// 0 = sin fecha límite. Es el campo que rellena el formulario al editarla.</param>
/// <param name="HorasEstimadas">El ESFUERZO estimado, en horas. No lo confundas con
/// <paramref name="Horas"/>: aquél es cuándo hay que entregarlo y éste cuánto trabajo se cree que
/// cuesta. <b>Quién lo escribió depende del tipo</b>, y se sabe mirando <paramref name="Tipo"/>: en
/// un Bug es de quien la tomó y se capturó al tomarla; en una Tarea o un Requerimiento es del líder
/// y se capturó al publicarla. Nulo en un bug que todavía nadie ha tomado.</param>
public record ActividadDelPoolDto(
    int Id,
    string Titulo,
    string? Detalle,
    PoolWorkType Tipo,
    string TipoTexto,
    PoolComplexity Complejidad,
    string ComplejidadTexto,
    int Puntos,
    PoolActivityStatus Estado,
    string EstadoTexto,
    string? QuienLaTiene,
    DateTime? LimiteUtc,
    bool Vencida,
    int Devoluciones,
    string? Enlace,
    PoolPriority Prioridad,
    string PrioridadTexto,
    decimal? Horas,
    decimal? HorasEstimadas,
    int PuntosMaximos,
    int CuantosCriteriosExtra);

/// <summary>
/// Una entrega esperando verificación.
/// </summary>
/// <param name="Vuelta">En qué vuelta va (1 la primera entrega). Una segunda o tercera no es lo
/// mismo que una entrega nueva y conviene saberlo antes de abrir el checklist.</param>
/// <param name="UltimoComentario">La última línea del historial del ida y vuelta, para no tener que
/// abrir nada para saber qué se dijo la vez anterior.</param>
public record EntregaPorVerificarDto(
    int Id,
    string Titulo,
    string TipoTexto,
    string ComplejidadTexto,
    int Puntos,
    string QuienLaEntrego,
    DateTime? EntregadaUtc,
    int Vuelta,
    string? UltimoComentario,
    string? Enlace);

// ── Checklist ────────────────────────────────────────────────────────────────────

/// <summary>
/// Un punto del checklist de una actividad concreta, con su evidencia.
///
/// Lo consultan las dos pantallas: quien la trabaja para marcarlo y el líder para verificarlo. Es
/// exactamente lo mismo que se mira en los dos casos, así que es un solo contrato.
/// </summary>
public record PuntoDeChecklistDto(
    int Id,
    string Texto,
    bool Hecho,
    bool RequiereEvidencia,
    string? Evidencia);

// ── Configuración ────────────────────────────────────────────────────────────────

/// <summary>
/// La configuración que define el sistema: cuánto vale cada cosa y qué hay que cumplir por tipo.
///
/// La plantilla viene completa —los tres tipos, activos e inactivos— y no por tipo: son unas pocas
/// filas, y traerlas de golpe evita una petición cada vez que el líder cambia el desplegable.
/// </summary>
public record ConfiguracionDelPoolDto(
    IReadOnlyList<CeldaDeMatrizDto> Matriz,
    IReadOnlyList<PuntoDePlantillaDto> Plantilla,
    IReadOnlyList<OpcionDelPoolDto<PoolWorkType>> Tipos,
    IReadOnlyList<OpcionDelPoolDto<PoolComplexity>> Complejidades,
    IReadOnlyList<OpcionDelPoolDto<PoolActivityStatus>> Estados);

/// <summary>
/// Una celda de la matriz tipo × complejidad.
///
/// <see cref="Puntos"/> y <see cref="HorasLimite"/> son mutables porque el líder los edita en la
/// propia rejilla y el mismo objeto vuelve al servidor al guardar; el resto identifica la celda y no
/// se toca. Cambiar la matriz NO revalúa lo ya publicado: cada actividad lleva sus puntos congelados.
/// </summary>
public record CeldaDeMatrizDto(
    PoolWorkType Tipo,
    string TipoTexto,
    PoolComplexity Complejidad,
    string ComplejidadTexto)
{
    public int Puntos { get; set; }

    /// <summary>
    /// Horas desde que se toma hasta que se espera entregada. 0 = sin fecha límite.
    ///
    /// <para>Son horas de RELOJ, no jornadas: al tomar la actividad se suman sobre el instante de
    /// ahora, así que 40 h vence pasado mañana y no dentro de cinco días laborales. Conviene que la
    /// pantalla lo diga, porque es lo que hace comparable el plazo con lo que mide el cronómetro.</para>
    ///
    /// <para>De aquí sale el plazo de las TAREAS y los REQUERIMIENTOS. En un BUG no manda: ahí lo
    /// fija el líder actividad por actividad.</para>
    /// </summary>
    public decimal HorasLimite { get; set; }
}

/// <summary>
/// Un punto de la plantilla de checklist de un tipo.
/// </summary>
/// <param name="Activo">Los puntos no se borran, se desactivan: las actividades en curso llevan su
/// propia copia, pero borrarlos haría perder qué se pedía cuando se hicieron las anteriores.</param>
public record PuntoDePlantillaDto(
    int Id,
    PoolWorkType Tipo,
    string Texto,
    int Orden,
    bool RequiereEvidencia,
    bool Activo);

// ── Peticiones ───────────────────────────────────────────────────────────────────

/// <summary>
/// Alta y edición de una actividad. <b>No lleva puntos, y no es un olvido:</b> los pone la matriz
/// según el tipo y la complejidad. Aceptarlos aquí permitiría publicar una actividad de 25 puntos
/// donde la matriz dice 5, que es justo lo que este sistema vino a impedir.
///
/// <para>Lo que sí se decide por actividad son el PLAZO, el ESFUERZO y la PRIORIDAD, y ninguno de
/// los tres toca los puntos. Es la asimetría a propósito: aflojar el plazo, estimar más horas o
/// subir la urgencia no vale puntos, así que no hay forma de convertirlos en una vía para regalarlos.</para>
///
/// <para>Y los CRITERIOS EXTRA, que sí suman —pero solo si se cumplen, y eso se decide al verificar
/// la entrega, no aquí. Aquí solo se anuncia qué se va a mirar.</para>
/// </summary>
/// <param name="Horas">El PLAZO: horas para entregarla desde que alguien la toma. 0 = sin fecha
/// límite.
/// <para><b>Obligatorio si el tipo es Bug</b>, porque ahí el plazo lo pone el líder y la matriz no
/// lo pone por él. En Tarea y Requerimiento es un ajuste opcional y nulo significa «el de la
/// matriz».</para></param>
/// <param name="HorasEstimadas">El ESFUERZO: cuántas horas de trabajo se cree que cuesta. No es el
/// plazo. <b>Obligatorio en Tarea y Requerimiento</b> —lo estima el líder— y <b>rechazado en Bug</b>,
/// donde lo escribe quien lo tome en el momento de tomarlo: si el líder pudiera precargarlo, a esa
/// persona no se le preguntaría nunca y el número dejaría de ser suyo.</param>
/// <param name="CriteriosExtra">Identificadores del catálogo de criterios. Se copian con su nombre y
/// sus puntos congelados: quien tome la actividad cobra lo que vio, aunque el catálogo cambie.</param>
public record PublicarActividadRequest(
    string Titulo,
    string? Detalle,
    PoolWorkType Tipo,
    PoolComplexity Complejidad,
    string? Enlace,
    PoolPriority Prioridad = PoolPriority.Media,
    decimal? Horas = null,
    decimal? HorasEstimadas = null,
    IReadOnlyList<int>? CriteriosExtra = null);

/// <summary>
/// Tomar una actividad del pool. Lleva un solo dato y solo hace falta para los BUGS.
///
/// <para><b>Por qué la estimación se pide justo aquí y no antes ni después.</b> En un bug, el
/// esfuerzo lo estima quien lo toma, y el momento de tomarlo es el ÚNICO en que ese número es
/// honesto: escrito a mitad del trabajo, quien lo escribe ya sabe lo que le costó, y entonces deja
/// de ser una estimación y de servir para contrastarla con el cronómetro —que es para lo único que
/// existe—. Por eso el servidor no deja tomar un bug sin ella.</para>
///
/// <para>El cuerpo entero es opcional en la ruta, igual que el motivo de devolver o liberar: quien
/// toma una tarea o un requerimiento no manda nada, porque ahí el esfuerzo ya lo fijó el líder al
/// publicarla y mandarlo se rechaza.</para>
/// </summary>
public record TomarActividadRequest(decimal? HorasEstimadas);

/// <summary>El líder dice si un criterio extra se cumplió. Solo los cumplidos suman al aceptar.</summary>
public record EvaluarCriterioRequest(bool Cumplido, string? Comentario);

/// <summary>
/// Un criterio extra tal como se ve en una actividad: lo que se prometió mirar y en qué quedó.
/// </summary>
/// <param name="Cumplido">Nulo mientras nadie lo ha evaluado. Se distingue de «false» a propósito:
/// al devolver una entrega, «se miró y no se cumplió» es información que quien corrige necesita.</param>
public record CriterioExtraDto(
    int Id,
    int? CriterioId,
    string Nombre,
    int Puntos,
    bool? Cumplido,
    string? Comentario);

/// <summary>Una opción del catálogo para elegir criterios extra al publicar.</summary>
public record CriterioDisponibleDto(int Id, string Nombre, string? Descripcion, int Puntos);

/// <summary>
/// El motivo de devolver, liberar o rechazar. Va opcional en el contrato porque quién lo exige
/// depende de la operación —al rechazar es obligatorio, al devolver al pool no— y esa regla vive en
/// el servicio, que además explica en su mensaje por qué hace falta.
/// </summary>
public record MotivoRequest(string? Motivo);

/// <summary>Marcar o desmarcar un punto del checklist, con el enlace que lo respalda si lo exige.</summary>
public record MarcarPuntoRequest(bool Hecho, string? Evidencia);

/// <summary>La matriz completa tal como quedó en la rejilla del líder.</summary>
public record GuardarMatrizRequest(IReadOnlyList<CeldaDeMatrizDto> Celdas);

/// <summary>Alta o edición de un punto de la plantilla. <c>Id = 0</c> significa alta.</summary>
public record GuardarPuntoDePlantillaRequest(
    int Id,
    PoolWorkType Tipo,
    string Texto,
    bool RequiereEvidencia,
    int Orden);
