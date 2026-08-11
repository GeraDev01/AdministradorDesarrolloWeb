using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.AusenciasLider;

/// <summary>
/// Una opción de desplegable con su valor TIPADO.
///
/// Las etiquetas las escriben los servicios y viajan con la pantalla; repetirlas en el cliente sería
/// una segunda lista que se desincroniza el día que alguien añada un estado, y el desplegable
/// enseñaría un nombre que ya no es el que se guarda.
///
/// El valor va NULABLE porque estos desplegables ofrecen «todos», y así la opción tiene exactamente
/// el tipo del filtro que rellena — sin conversiones a medio camino entre el desplegable y la
/// petición. Es el mismo criterio de <c>OpcionDelPoolDto</c>.
/// </summary>
public record OpcionDeFiltroDto<T>(T? Valor, string Texto) where T : struct;

// ── Vacaciones ──────────────────────────────────────────────────────────────────

/// <summary>
/// Todo lo que enseña la pantalla de vacaciones del líder: las solicitudes del equipo, los
/// desplegables y las firmas con las que puede firmar el documento.
///
/// Va en una sola respuesta por lo mismo que «Mis vacaciones»: encadenar tres peticiones desde el
/// navegador deja la pantalla pintándose a trozos justo al abrirla, que es cuando más se nota.
/// </summary>
/// <param name="Pendientes">Cuántas esperan respuesta, contadas sobre TODAS y no sobre lo filtrado:
/// es el número que dice si queda trabajo, y esconderlo al filtrar por «Aprobadas» haría creer que
/// no hay nada que resolver.</param>
public record VacacionesDelLiderDto(
    IReadOnlyList<SolicitudDeVacacionesDelLiderDto> Solicitudes,
    IReadOnlyList<OpcionDeFiltroDto<int>> Desarrolladores,
    IReadOnlyList<OpcionDeFiltroDto<VacationStatus>> Estados,
    IReadOnlyList<FirmaDelLiderDto> Firmas,
    int Pendientes);

/// <summary>
/// Una solicitud de vacaciones vista por quien la resuelve.
///
/// <b>No lleva los bytes del respaldo ni los del documento</b>, solo si existen: son hasta 15 MB por
/// fila que nadie mira hasta que los pide. Es la proyección que el escritorio ya hacía en su rejilla
/// (<c>VacRow</c>) y aquí pesa más, porque esos bytes cruzarían la red.
/// </summary>
/// <param name="EtiquetaEstado">El texto del estado, con el emoji del escritorio. El color lo pone
/// la pantalla: un servicio no debe saber del tema visual.</param>
/// <param name="SePuedeResolver">Lo decide el servidor con el estado, no la pantalla: así el botón
/// que se ve y la operación que se permite no pueden discrepar. Cubre aprobar y rechazar.</param>
/// <param name="SePuedeCancelar">Va aparte porque alcanza más lejos: unas vacaciones ya APROBADAS
/// que al final no se toman se cancelan, no se rechazan.</param>
/// <param name="DocumentoFirmado">Ya hay un PDF firmado guardado para esta solicitud. Se dice para
/// que la pantalla ofrezca «descargar el firmado» en vez de volver a generarlo.</param>
/// <param name="FirmaDelColaborador">En qué situación está el trazo que puso quien pidió los días.
/// No confundir con <paramref name="DocumentoFirmado"/>: aquél es el papel que el líder archivó, éste
/// es la firma que ese papel lleva dentro.</param>
/// <param name="SePuedeArchivar">Lo decide el servidor, como <paramref name="SePuedeResolver"/>: el
/// documento definitivo NO sale mientras haya una firma del colaborador que dejó de valer. La regla
/// vive en <c>DocumentoDeVacacionesService</c> y viaja ya resuelta para que el botón que se ve y la
/// operación que se permite no puedan discrepar — pero <b>la barrera de verdad es la del servidor</b>,
/// porque esta pantalla corre en la máquina de cada quien y un botón deshabilitado no es una regla.
/// </param>
/// <param name="SePuedeArchivarSinLaFirma">La otra salida del mismo bloqueo, y por eso viaja al lado:
/// donde <paramref name="SePuedeArchivar"/> dice que no, éste dice por dónde. Sale del servidor por lo
/// mismo que el anterior, y viene aparte en vez de deducirse de <c>DejoDeValer</c> para que el día que
/// la regla cambie —por ejemplo, si se exigiera un permiso especial para renunciar a una firma— el
/// botón se apague solo en lugar de quedarse encendido por una copia olvidada en la pantalla.</param>
public record SolicitudDeVacacionesDelLiderDto(
    int Id,
    int DesarrolladorId,
    string Desarrollador,
    DateTime Inicio,
    DateTime Fin,
    int Dias,
    VacationStatus Estado,
    string EtiquetaEstado,
    string? Comentario,
    string? RespuestaDelLider,
    bool TieneRespaldo,
    bool SePuedeResolver,
    bool SePuedeCancelar,
    bool DocumentoFirmado,
    DateTime? FirmadoUtc,
    FirmaDelColaboradorDto FirmaDelColaborador,
    bool SePuedeArchivar,
    bool SePuedeArchivarSinLaFirma);

/// <summary>
/// La firma de quien pidió las vacaciones, vista por el líder.
///
/// <para>Son <b>tres situaciones y no dos</b>, y la tercera es la que motiva todo esto: <b>firmó y su
/// firma dejó de valer</b>, porque la solicitud cambió después. Enseñarla como «sin firmar» a secas
/// escondería que hubo una firma y que se cayó, que es justamente lo que el líder necesita saber para
/// pedir otra en vez de archivar un papel al que le falta.</para>
///
/// <para>Es la misma división que el colaborador ve en «Mis vacaciones» (<c>FirmaDeSolicitudDto</c>),
/// y a propósito: si las dos pantallas contaran la misma firma de dos maneras, la conversación entre
/// el líder y la persona empezaría con los dos mirando datos distintos.</para>
/// </summary>
/// <param name="Firmada">Firmó y su firma <b>sigue valiendo hoy</b>: es la que el documento estampa.
/// </param>
/// <param name="DejoDeValer">Firmó, y lo que firmó ya no es lo que la solicitud dice ahora.</param>
/// <param name="FirmadaUtc">Cuándo puso el trazo. Se manda aunque la firma haya dejado de valer: la
/// fecha es la que permite entender qué pasó primero.</param>
/// <param name="PuedeVolverAFirmar">Si hoy podría firmar. Lo decide el servicio, y hace falta aquí
/// porque pedirle una firma que no puede dar sería mandarlo a una pantalla sin botón.
///
/// <para>La regla ya no es solo «mientras espera respuesta»: <b>una firma que dejó de valer se puede
/// reponer aunque la solicitud esté resuelta</b>. Sin eso, este campo venía apagado exactamente en el
/// caso que bloquea el archivado —la firma se cae, el líder no puede emitir el papel y el único botón
/// de salida está gris—, que era un callejón sin fondo. Lo único que sigue en falso es pedir una
/// PRIMERA firma sobre algo ya resuelto: ahí no hay nada que reponer.</para></param>
public record FirmaDelColaboradorDto(
    bool Firmada,
    bool DejoDeValer,
    DateTime? FirmadaUtc,
    bool PuedeVolverAFirmar);

/// <summary>
/// Resolver una solicitud de vacaciones: aprobarla, rechazarla o cancelarla.
///
/// El comentario es obligatorio al rechazar y lo exige el SERVICIO, no la pantalla: un rechazo sin
/// motivo deja a quien lo pidió sin nada que hacer con la respuesta, y esa regla tiene que aplicarse
/// también a quien llame a la API sin pasar por el navegador.
/// </summary>
public record ResolucionDeVacacionesRequest(VacationStatus Estado, string? Comentario);

/// <summary>
/// Firmar el documento de una solicitud con una firma guardada y dejarlo archivado.
///
/// La firma se identifica por su ficha y NO viajan sus bytes: aceptar una imagen del cliente sería
/// permitir firmar con cualquier cosa que alguien mande.
/// </summary>
public record FirmarDocumentoRequest(int FirmaId);

/// <summary>
/// Archivar el documento definitivo <b>reconociendo que va sin la firma del colaborador</b>. Es la
/// SEGUNDA salida del bloqueo, la que no depende de que la persona esté y conteste.
///
/// <para><b>El motivo es obligatorio y lo exige el SERVICIO</b>, no la pantalla: acaba impreso en el
/// papel que se archiva —con el nombre de quien lo decidió y la fecha— y es lo único que le explicará
/// el hueco a quien abra ese expediente dentro de un año. Sin él, esta ruta sería otra vez lo que la
/// aplicación hacía antes: emitir un documento al que le falta una firma sin decirlo.</para>
///
/// <para>La <see cref="FirmaId"/> es la del JEFE, la misma que en <see cref="FirmarDocumentoRequest"/>:
/// lo que se está renunciando a esperar es la del colaborador. Un documento sin ninguna de las dos no
/// sería un documento resuelto.</para>
/// </summary>
public record ArchivarSinLaFirmaRequest(int FirmaId, string? Motivo);

/// <summary>
/// Pedirle al colaborador que firme —o que vuelva a firmar— su solicitud.
///
/// <para><b>Es la PRIMERA salida del bloqueo</b>, y la buena: acaba con un papel que lleva las dos
/// firmas. Sin ella, descubrir que la firma no vale dejaría al líder con un documento que no puede
/// archivar y sin nada que hacer desde donde está, y un bloqueo sin salida molesta más de lo que
/// protege. La otra —<see cref="ArchivarSinLaFirmaRequest"/>— renuncia a la firma y lo declara; ésta
/// la consigue.</para>
/// </summary>
/// <param name="Nota">Lo que el líder quiera añadir al aviso («te cambié las fechas a la semana
/// siguiente»). Es opcional porque el aviso ya explica solo lo que pasó; existe porque el motivo real
/// del cambio lo sabe él, y sin un renglón donde escribirlo la persona recibe un «vuelve a firmar»
/// sin contexto.</param>
public record RecordatorioDeFirmaRequest(string? Nota);

// ── Firmas reutilizables ────────────────────────────────────────────────────────

/// <summary>
/// Una firma guardada, tal como se elige en un desplegable.
///
/// <b>Sin los bytes del PNG</b>: se piden por <c>/api/ausencias-lider/firmas/{id}/imagen</c> cuando
/// hay que enseñarla. Mandarlas todas en la lista sería cargar cada imagen en cada apertura de la
/// pantalla para acabar dibujando una sola.
/// </summary>
public record FirmaDelLiderDto(
    int Id,
    string Nombre,
    bool Predeterminada,
    int Ancho,
    int Alto,
    DateTime CreadaUtc);

/// <summary>Cambiar el nombre de una firma guardada.</summary>
public record RenombrarFirmaRequest(string Nombre);

// ── Permisos ────────────────────────────────────────────────────────────────────

/// <summary>
/// Lo que enseña la pantalla de permisos del líder: las solicitudes del equipo, los desplegables y
/// los topes que aplica el servicio.
/// </summary>
/// <param name="MaxDias">El tope que aplica <c>LeaveRequestService</c>, para que el formulario de
/// registro no deje escribir un número que la API va a rechazar.</param>
public record PermisosDelLiderDto(
    IReadOnlyList<PermisoDelLiderDto> Permisos,
    IReadOnlyList<OpcionDeFiltroDto<int>> Desarrolladores,
    IReadOnlyList<OpcionDeFiltroDto<LeaveType>> Tipos,
    IReadOnlyList<OpcionDeFiltroDto<LeaveStatus>> Estados,
    int Pendientes,
    int MaxDias);

/// <summary>
/// Un permiso visto por quien lo resuelve. Como en vacaciones, el justificante <b>no viaja aquí</b>:
/// se pide por <c>/api/adjuntos/permiso/{id}</c>, que ya sirve todos los adjuntos con el mismo trato.
/// </summary>
/// <param name="Hasta">Último día cubierto, ya calculado. Con «5 días desde una fecha» hay que
/// contar a mano para saber hasta cuándo llega, y ahí es donde se cuela el error de un día.</param>
/// <param name="LoRegistroElLider">La capturó el líder (el trámite ocurrió fuera de la aplicación) en
/// vez de pedirla el desarrollador. Nace aprobada, así que nunca aparece en la cola de pendientes.
/// </param>
/// <param name="Resolvio">Quién la resolvió, como texto heredado del registro manual del escritorio.
/// </param>
/// <param name="SePuedeCorregir">
/// Se pueden arreglar sus datos capturados. Va aparte de <c>SePuedeResolver</c> aunque hoy dependan
/// del mismo estado: son dos operaciones distintas —una decide, la otra enmienda— y atarlas a un solo
/// campo haría que cambiar la regla de una moviera en silencio el botón de la otra. Lo decide el
/// servidor, como el resto: así el botón que se ve y la operación que se permite no discrepan.
/// </param>
public record PermisoDelLiderDto(
    int Id,
    int DesarrolladorId,
    string Desarrollador,
    LeaveType Tipo,
    string EtiquetaTipo,
    DateTime Desde,
    DateTime Hasta,
    int Dias,
    LeaveStatus Estado,
    string EtiquetaEstado,
    string? Motivo,
    string? Notas,
    string? RespuestaDelLider,
    bool TieneJustificante,
    bool LoRegistroElLider,
    string? Resolvio,
    bool SePuedeResolver,
    bool SePuedeCorregir);

/// <summary>
/// Aprobar o rechazar un permiso. Al rechazar, el motivo lo exige <c>LeaveRequestService</c> con su
/// propio mensaje; se manda vacío a propósito para que ese texto sea el que se lea.
/// </summary>
public record ResolucionDePermisoRequest(string? Comentario);

/// <summary>
/// El permiso que el líder captura porque se acordó fuera de la aplicación (una llamada, un
/// pasillo). Nace <b>aprobado</b>: registrarlo ES concederlo, y dejarlo pendiente le crearía a él
/// mismo un trámite que ya resolvió. Es la decisión que el escritorio dejó escrita en
/// <c>LeaveRequestsControl</c> y que sin este alta dejaría fuera a la mitad de los permisos reales.
/// </summary>
public record RegistroDePermisoRequest(
    int DesarrolladorId,
    LeaveType Tipo,
    DateTime Desde,
    int Dias,
    string? Motivo,
    string? Notas);

/// <summary>
/// Corregir los datos de un permiso que sigue pendiente.
///
/// <para><b>Aquí no hay adjunto, y esa es la pieza que importa.</b> El justificante lo subió quien
/// pidió el permiso y no es del líder: corregirle una fecha o una falta de ortografía al motivo no
/// puede llevárselo por delante. Al no viajar en esta petición, no hay forma de borrarlo sin
/// querer — ni desde la pantalla ni llamando a la API a mano.</para>
///
/// <para>Tampoco viaja el desarrollador: una solicitud no cambia de dueño. Lo fija el servicio a
/// partir de la fila que ya existe.</para>
/// </summary>
public record CorreccionDePermisoRequest(
    LeaveType Tipo,
    DateTime Desde,
    int Dias,
    string? Motivo,
    string? Notas);

// ── Actividades libres del equipo ───────────────────────────────────────────────

/// <summary>
/// Las actividades libres del equipo con sus indicadores. Contesta la pregunta para la que existía
/// la pantalla del escritorio: «¿en qué se fue el tiempo que no aparece en ningún requerimiento?».
/// </summary>
/// <param name="TiempoTotal">La suma de lo cronometrado, ya formateada por <c>WorkSessionService</c>:
/// que el formato lo ponga el servidor evita que la web y el escritorio muestren el mismo total con
/// dos caras distintas.</param>
public record ActividadesDelEquipoDto(
    IReadOnlyList<ActividadDelEquipoDto> Actividades,
    IReadOnlyList<OpcionDeFiltroDto<int>> Desarrolladores,
    IReadOnlyList<OpcionDeFiltroDto<DevActivityStatus>> Estados,
    int Total,
    int Abiertas,
    string TiempoTotal);

/// <summary>
/// Una actividad libre en la lista del líder. <b>Solo el NÚMERO de evidencias</b>, sin un byte: la
/// lista únicamente necesita saber que las hay, y el archivo se pide al abrir el detalle. Es la
/// decisión que el escritorio ya había tomado y que aquí se paga en red.
/// </summary>
public record ActividadDelEquipoDto(
    int Id,
    string Desarrollador,
    string Titulo,
    string? Descripcion,
    DevActivityStatus Estado,
    string EtiquetaEstado,
    DateTime CreadaUtc,
    DateTime? CerradaUtc,
    int Segundos,
    string Tiempo,
    int Evidencias);

/// <summary>
/// La ficha de una actividad ajena: sus sesiones de cronómetro y su evidencia. <b>Solo lectura</b> —
/// la evidencia de una actividad de otro se consulta, no se cambia: eso es de su dueño, tal como lo
/// dejó escrito el escritorio.
///
/// No repite el título ni la descripción: quien abre este detalle ya tiene delante la fila de la
/// lista, y mandarlos otra vez solo abriría la puerta a que las dos copias se contradigan.
/// </summary>
public record DetalleDeActividadDto(
    int Id,
    string TiempoTotal,
    IReadOnlyList<SesionDeActividadDto> Sesiones,
    IReadOnlyList<EvidenciaDeActividadDto> Evidencias);

/// <summary>Un tramo de cronómetro. <paramref name="FinUtc"/> nulo significa «en curso».</summary>
public record SesionDeActividadDto(DateTime InicioUtc, DateTime? FinUtc, int Segundos, string Duracion);

/// <summary>
/// Una evidencia adjunta, sin su contenido: los bytes se piden por
/// <c>/api/adjuntos/actividad/{id}</c>, que ya decide el tipo por los bytes y limpia el nombre.
/// </summary>
public record EvidenciaDeActividadDto(
    int Id,
    string Nombre,
    long Bytes,
    string? Descripcion,
    DateTime CreadaUtc);

// ── Sugerencias ─────────────────────────────────────────────────────────────────

/// <summary>
/// El tablero del líder: las sugerencias del equipo con sus filtros y sus contadores.
/// </summary>
/// <param name="SinAtender">Las que siguen «Nueva». Es lo que el líder viene a hacer aquí.</param>
/// <param name="SoloParaTi">Las marcadas «solo líder»: si él no las atiende no las atiende nadie,
/// porque nadie más las ve.</param>
public record TableroDeSugerenciasDto(
    IReadOnlyList<SugerenciaDelLiderDto> Sugerencias,
    IReadOnlyList<OpcionDeFiltroDto<SuggestionStatus>> Estados,
    IReadOnlyList<OpcionDeFiltroDto<SuggestionCategory>> Categorias,
    IReadOnlyList<OpcionDeFiltroDto<SuggestionVisibility>> Visibilidades,
    int SinAtender,
    int SoloParaTi);

/// <summary>
/// Una sugerencia en el tablero del líder.
///
/// <b>El anonimato lo resuelve el SERVIDOR.</b> Cuando la sugerencia es anónima,
/// <see cref="Autor"/> llega en null porque el servidor no lo pone — no porque la pantalla lo
/// esconda. La entidad trae dentro el autor y su ficha SIEMPRE (los necesita para avisarle cuando la
/// contesten); mandarlos y confiar en que nadie los pinte sería regalarlos, porque cualquiera que
/// mire la respuesta de la API leería el nombre igual. En el escritorio el dato ni salía del
/// proceso; aquí cruza una red.
/// </summary>
public record SugerenciaDelLiderDto(
    int Id,
    string Titulo,
    string Cuerpo,
    SuggestionCategory Categoria,
    string CategoriaEtiqueta,
    SuggestionStatus Estado,
    string EstadoEtiqueta,
    string Alcance,
    string? Autor,
    bool Anonima,
    bool SoloParaElLider,
    bool SePuedeVotar,
    int Votos,
    string? Respuesta,
    DateTime CreadaUtc,
    DateTime? RevisadaUtc);

/// <summary>Atender una sugerencia: cambiarle el estado y contestarla.</summary>
public record RespuestaASugerenciaRequest(SuggestionStatus Estado, string? Respuesta);

/// <summary>
/// De dónde sale la plantilla de Word que se está usando.
/// </summary>
/// <param name="EsDeFabrica">True mientras nadie haya subido una. La de fábrica va incrustada en el
/// ensamblado, así que restablecer siempre es posible: no se puede perder.</param>
public record OrigenDePlantillaDto(bool EsDeFabrica, string? SubidaPor, DateTime? SubidaEnUtc);
