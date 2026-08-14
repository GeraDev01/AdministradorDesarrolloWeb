using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Ausencias;

// ── Vacaciones ──────────────────────────────────────────────────────────────────

/// <summary>
/// Todo lo que enseña «Mis vacaciones»: el saldo del año y las solicitudes propias.
///
/// Va en una sola respuesta porque el saldo se calcula a partir de las MISMAS solicitudes que se
/// listan. Pedirlos por separado sería leer dos veces lo mismo y, si entre una lectura y la otra el
/// líder aprobara algo, la pantalla se contradiría a sí misma.
/// </summary>
/// <param name="MaxRespaldoBytes">El tope del documento de respaldo, para que el formulario avise
/// antes de empujar 20 MB por la red y que se los rechacen. El que cuenta lo aplica el servidor.</param>
/// <param name="Firmas">Qué le pasa a la firma de cada solicitud. <b>Lo añade el endpoint</b>, no el
/// servicio que arma el resto: la firma la lleva <c>VacationRequestService</c>, que es quien decide
/// si todavía vale. Viaja en la MISMA respuesta —y no en una segunda petición— porque la pantalla lo
/// pinta a la vez que la lista, y en dos viajes habría un instante enseñando una solicitud firmada
/// como si no lo estuviera. Puede venir nula: solo significa que quien respondió no la calculó.</param>
/// <param name="SaldoAcumulado">El saldo DE VERDAD: el que sale de la antigüedad, con lo acumulado
/// del año anterior, lo que ya caducó y el ajuste que capturó el líder. Lo añade el endpoint, por lo
/// mismo que <paramref name="Firmas"/>: lo calcula <c>SaldoDeVacacionesService</c>, que es quien
/// conoce la tabla de la ley y la ventana de caducidad configurada.
///
/// <para>Convive con <paramref name="Saldo"/> en vez de sustituirlo porque son dos cosas distintas:
/// aquél enseña el número que RH tecleó en la ficha, que es lo que sale impreso en el documento de
/// vacaciones y lo que el escritorio escribía; éste es el cálculo. Se añadió como opcional para que
/// ninguna pantalla dejara de compilar de golpe; puede venir nulo, y entonces solo significa que
/// quien respondió no lo calculó.</para></param>
public record MisVacacionesDto(
    SaldoDeVacacionesDto Saldo,
    IReadOnlyList<SolicitudDeVacacionesDto> Solicitudes,
    long MaxRespaldoBytes,
    IReadOnlyList<FirmaDeSolicitudDto>? Firmas = null,
    SaldoAcumuladoDto? SaldoAcumulado = null);

/// <summary>
/// El estado de la firma de una solicitud, tal como la pantalla tiene que contarlo.
///
/// <para>Son tres situaciones y no dos, y la tercera es la que importa: <b>firmada pero ya sin
/// valer</b>, porque la solicitud cambió después de firmarse. Enseñarla como «sin firmar» a secas
/// escondería que hubo una firma y que dejó de servir, que es justo lo que la persona necesita saber
/// para volver a firmarla.</para>
/// </summary>
/// <param name="DejoDeValer">Firmó, y lo que firmó ya no es lo que dice la solicitud.</param>
/// <param name="SePuedeFirmar">Lo decide el servicio: solo se firma lo que sigue esperando respuesta.</param>
/// <param name="DocumentoArchivado">El líder ya resolvió y archivó el documento definitivo. Es el que
/// lleva las dos firmas, así que hasta que existe no hay nada archivado que ofrecer.</param>
public record FirmaDeSolicitudDto(
    int SolicitudId,
    bool Firmada,
    bool DejoDeValer,
    DateTime? FirmadaUtc,
    bool SePuedeFirmar,
    bool DocumentoArchivado);

/// <summary>
/// El saldo del año en curso: los días que RH dejó en la ficha menos los ya tomados (aprobados) de
/// este año. Los pendientes van aparte y no descuentan, porque todavía pueden rechazarse.
/// </summary>
/// <param name="TieneFicha">Falso cuando la cuenta no está ligada a una ficha de desarrollador: no
/// hay saldo que enseñar ni nada que pedir.</param>
/// <param name="PendientesDeAprobacion">Días pendientes de TODOS los años, no solo del actual: una
/// solicitud de enero que sigue sin respuesta también es tiempo comprometido.</param>
public record SaldoDeVacacionesDto(
    bool TieneFicha,
    int Asignados,
    int TomadosEsteAnio,
    int PendientesDeAprobacion,
    DateTime? FechaDeIngreso)
{
    /// <summary>
    /// Lo que queda. Puede salir negativo si el líder aprobó por encima de lo asignado, y se enseña
    /// así en vez de recortarlo a cero: un saldo en rojo es información, un cero es un dato falso.
    /// </summary>
    public int Disponibles => Asignados - TomadosEsteAnio;
}

/// <summary>
/// El saldo de vacaciones CALCULADO: lo que la ley fue generando por antigüedad, menos lo gozado,
/// menos lo que caducó sin gozarse, más la corrección que el líder capturó a mano.
///
/// <para><b>Nada de esto se guarda como número.</b> Se recalcula en cada consulta a partir de la
/// fecha de ingreso y de las solicitudes, y el único dato almacenado es
/// <paramref name="AjusteManual"/>, que es lo que un humano escribe. Un saldo guardado se
/// desincroniza el primer día que alguien cancele unas vacaciones por otro camino.</para>
///
/// <para><b>La cuenta se puede comprobar a mano</b>, y eso es deliberado: quien reciba esta
/// respuesta tiene que poder sumar los renglones y llegar al mismo número, o el saldo no se puede
/// defender delante de quien reclama sus días.
/// <code>
///   DiasVigentes = DiasGenerados − DiasTomados − DiasCaducados
///   Disponible   = DiasVigentes  − DiasComprometidos + AjusteManual
/// </code></para>
/// </summary>
/// <param name="TieneFicha">Falso cuando la cuenta no está ligada a una ficha: no hay antigüedad
/// que contar ni, por tanto, saldo del que hablar.</param>
/// <param name="FechaDeIngreso">Sin ella no hay nada que calcular, y el mensaje lo dice: es el
/// único caso en el que un cero significa «falta un dato» y no «no te toca nada».</param>
/// <param name="AniosCumplidos">Aniversarios de ingreso completos. Es la entrada de la tabla de la
/// ley: 1 → 12 días, 2 → 14, y así.</param>
/// <param name="ProximoAniversario">Cuándo vuelve a subir el saldo. Va acompañado de
/// <paramref name="DiasDelProximoPeriodo"/> porque un cero sin fecha parece un error del programa y
/// no una regla — y en el primer año el saldo es cero de verdad.</param>
/// <param name="VentanaDeCaducidadMeses">Cuántos meses se arrastran los días no gozados desde el
/// cierre de su periodo. Viaja con la respuesta para que la pantalla pueda explicar POR QUÉ caducó
/// algo sin tener que saberse la configuración de la empresa.</param>
/// <param name="DiasGenerados">Todo lo que la ley ha ido dando desde el ingreso, periodo a periodo.
/// Sin descontar nada: es el total histórico, no lo disponible.</param>
/// <param name="DiasTomados">Días de solicitudes APROBADAS. Incluye los que se gozaron por encima
/// de lo que había, que es lo que puede dejar el saldo en rojo.</param>
/// <param name="DiasCaducados">Se generaron, nadie los gozó y se pasó la ventana. Se enseñan en vez
/// de desaparecer callando: perder días es justo lo que la persona necesita ver a tiempo.</param>
/// <param name="DiasVigentes">Lo generado que sigue vivo hoy.</param>
/// <param name="DiasComprometidos">Días de solicitudes PENDIENTES de respuesta. No están gozados
/// todavía, pero están apartados: sin restarlos, alguien podría pedir tres veces los mismos días y
/// las tres solicitudes parecerían caber.</param>
/// <param name="AjusteManual">La corrección del líder. Positiva o negativa.</param>
/// <param name="NotaDelAjuste">Por qué. Sin nota no se guarda ajuste.</param>
/// <param name="Periodos">El desglose año por año. Es lo que convierte el saldo en algo revisable:
/// sin él, el número final hay que creérselo.</param>
public record SaldoAcumuladoDto(
    bool TieneFicha,
    DateTime? FechaDeIngreso,
    int AniosCumplidos,
    DateTime? ProximoAniversario,
    int DiasDelProximoPeriodo,
    int VentanaDeCaducidadMeses,
    int DiasGenerados,
    int DiasTomados,
    int DiasCaducados,
    int DiasVigentes,
    int DiasComprometidos,
    int AjusteManual,
    string? NotaDelAjuste,
    string? AutorDelAjuste,
    DateTime? FechaDelAjusteUtc,
    string Mensaje,
    IReadOnlyList<PeriodoDeVacacionesDto> Periodos)
{
    /// <summary>
    /// Los días que hoy se pueden pedir. Puede salir NEGATIVO —se gozó de más, o el líder restó con
    /// un ajuste— y se enseña tal cual: un cero de mentira esconde justo lo que hay que corregir.
    ///
    /// <para><b>Lo COMPROMETIDO ya no resta.</b> Antes se descontaba lo pedido y aún sin responder, y
    /// se quitó por decisión del dueño: el número enseña lo que hay hasta que el líder contesta.
    /// <c>DiasComprometidos</c> sigue viajando y la pantalla lo dice aparte, para que nadie crea que
    /// su solicitud se perdió — pero no toca este total.</para>
    ///
    /// <para>De paso se va un defecto que tenía: lo pendiente restaba aquí y NO se apuntaba contra
    /// ningún periodo, así que tampoco frenaba la caducidad; mientras el líder no respondiera, esos
    /// días podían acabar restados dos veces, una como comprometidos y otra como caducados.</para>
    /// </summary>
    public int Disponible => DiasVigentes + AjusteManual;
}

/// <summary>
/// Un periodo anual de vacaciones: el año de servicio que va de un aniversario al siguiente.
///
/// <para>Los días se generan AL CERRARSE el periodo, no durante: hasta que no se cumple el año de
/// servicio no hay derecho a esos días. Por eso <paramref name="Cierre"/> es también la fecha desde
/// la que se pueden gozar, y desde la que empieza a correr la caducidad.</para>
/// </summary>
/// <param name="Anio">Año de servicio, 1 en adelante. Es la fila de la tabla de la ley.</param>
/// <param name="Usados">Días de este periodo ya gozados. El reparto es por orden de antigüedad: lo
/// que se toma se descuenta primero de los días más viejos, que son los que están a punto de
/// caducar. Al revés se perderían días teniendo saldo de sobra.</param>
/// <param name="Caducado">Ya pasó su ventana: lo que quede en <paramref name="Restantes"/> está
/// perdido y no cuenta para el saldo.</param>
public record PeriodoDeVacacionesDto(
    int Anio,
    DateTime Inicio,
    DateTime Cierre,
    DateTime Caduca,
    int Dias,
    int Usados,
    int Restantes,
    bool Caducado);

/// <summary>
/// La corrección del saldo que captura el líder.
///
/// <para>La NOTA es obligatoria y el servicio la exige: el ajuste existe porque el cálculo, sin
/// histórico, sale muy alto, y un número corregido sin motivo escrito no se puede defender delante
/// de quien reclama sus días. Queda en la bitácora junto con quién lo escribió.</para>
///
/// <para>El ajuste REEMPLAZA al anterior, no se suma: es «la corrección vigente de esta persona»,
/// una sola. La historia de las correcciones es la bitácora.</para>
/// </summary>
public record AjusteDeSaldoRequest(int Dias, string? Nota);

/// <summary>
/// Una solicitud de vacaciones propia, ya lista para pintar.
///
/// <b>No lleva los bytes del documento de respaldo</b>, solo si lo tiene: son hasta 15 MB por fila
/// que nadie mira hasta que los pide. Es la misma decisión que el escritorio tomó al proyectar su
/// rejilla sin los BLOB.
/// </summary>
/// <param name="EtiquetaEstado">El texto del estado. El color lo pone la pantalla.</param>
/// <param name="DocumentosGenerados">Cuántos documentos generados caerían en cascada al eliminarla.
/// Viaja con la lista y no se consulta al confirmar porque el aviso tiene que estar delante de la
/// persona ANTES de que decida, no después.</param>
/// <param name="SePuedeCancelar">Lo decide el servicio, no la pantalla: así el botón que se ve y la
/// operación que se permite no pueden discrepar.</param>
/// <param name="SePuedeAdjuntar">El respaldo solo se cuelga mientras la solicitud siga pendiente:
/// después ya es la decisión del líder sobre lo que había.</param>
public record SolicitudDeVacacionesDto(
    int Id,
    DateTime Inicio,
    DateTime Fin,
    int Dias,
    VacationStatus Estado,
    string EtiquetaEstado,
    string? Comentario,
    string? RespuestaDelLider,
    bool TieneRespaldo,
    int DocumentosGenerados,
    bool SePuedeCancelar,
    bool SePuedeAdjuntar,
    bool SePuedeEliminar);

/// <summary>
/// Alta de una solicitud de vacaciones.
///
/// No lleva desarrollador: es siempre de quien tiene la sesión. Mismo criterio que en jornada — una
/// ruta sin identificador ajeno no tiene nada que comprobar ni, por tanto, nada que olvidar.
/// </summary>
public record NuevaSolicitudDeVacacionesRequest(DateTime Inicio, DateTime Fin, string? Comentario);

// ── Permisos ────────────────────────────────────────────────────────────────────

/// <summary>
/// Todo lo que enseña «Mis permisos»: el resumen del año, las solicitudes propias y los catálogos
/// que necesita el formulario de alta.
/// </summary>
/// <param name="TiposDePermiso">Las etiquetas de tipo las escribe <c>LeaveRequestService</c> y viajan
/// con la pantalla. Repetirlas en el cliente sería una segunda lista que se desincroniza el día que
/// alguien añada un tipo, y el desplegable enseñaría un nombre que ya no es el que se guarda.</param>
/// <param name="MaxDias">El tope que aplica el servicio, para que el formulario no deje escribir un
/// número que la API va a rechazar.</param>
/// <param name="MaxJustificanteBytes">El tope del archivo, por el mismo motivo: avisar antes de
/// empujar 20 MB por la red para que los rechacen. El que cuenta lo aplica el servidor.</param>
/// <param name="HorasDeLaJornada">Lo más largo que puede ser un permiso POR HORAS, por lo mismo que
/// <paramref name="MaxDias"/>: para que el formulario avise antes de mandar un tramo que la API va a
/// rechazar. Más que una jornada ya es el día entero y se pide como día completo.</param>
public record MisPermisosDto(
    bool TieneFicha,
    ResumenDePermisosDto Resumen,
    IReadOnlyList<SolicitudDePermisoDto> Solicitudes,
    IReadOnlyList<OpcionDto> TiposDePermiso,
    int MaxDias,
    long MaxJustificanteBytes,
    decimal HorasDeLaJornada);

/// <summary>
/// Los indicadores de la cabecera: los tres del escritorio y el de las horas, que es nuevo porque
/// antes no había permisos por horas que contar.
/// </summary>
/// <param name="DiasAprobadosEsteAnio">Días de los permisos aprobados <b>de día completo</b>. Los de
/// horas NO se convierten a fracciones de día y ésa es la decisión de fondo: media jornada no son
/// «0,5 días» para todo el mundo, y repartir un tramo de dos horas entre los días haría que este
/// número dejara de poderse comparar con el de los años anteriores. Las horas se cuentan al lado, en
/// <paramref name="HorasAprobadasEsteAnio"/>, y quien mire los dos ve la ausencia entera sin que
/// ninguno de los dos mienta.</param>
/// <param name="HorasAprobadasEsteAnio">Horas de los permisos aprobados por horas. Sale decimal
/// porque un tramo puede ser de hora y media.</param>
public record ResumenDePermisosDto(
    int EsperandoRespuesta,
    int AprobadosEsteAnio,
    int DiasAprobadosEsteAnio,
    decimal HorasAprobadasEsteAnio);

/// <summary>
/// Una solicitud de permiso propia.
///
/// Como en vacaciones, el justificante <b>no viaja aquí</b>: se pide por
/// <c>/api/adjuntos/permiso/{id}</c> cuando alguien pulsa.
/// </summary>
/// <param name="Hasta">Último día cubierto, ya calculado. Con «5 días desde una fecha» hay que
/// contar a mano para saber hasta cuándo llega, y ahí es donde se cuela el error de un día. En un
/// permiso por horas es el mismo día que <paramref name="Desde"/>: el tramo cabe en una jornada.</param>
/// <param name="PorHoras">El permiso es de un TRAMO de un día y no de días completos.</param>
/// <param name="HoraInicio">Las horas del tramo, en nulo cuando el permiso es de días completos.</param>
/// <param name="Horas">Lo que dura el tramo. Cero en los de día completo: un día de ausencia no se
/// convierte a horas, ver <see cref="ResumenDePermisosDto"/>.</param>
/// <param name="Duracion">Cuánto dura, ya escrito: «2 día(s)» o «2 h (de 09:00 a 11:00)». Lo redacta
/// el servidor —igual que las etiquetas de tipo y estado— para que esta pantalla y la del líder no
/// puedan contar la misma ausencia de dos maneras.</param>
/// <param name="LoRegistroElLider">Los permisos que capturó el administrador no son solicitudes del
/// desarrollador; se ven igual, pero no se corrigen desde aquí.</param>
/// <param name="SePuedeAdjuntar">Un justificante solo se puede colgar mientras la solicitud siga
/// pendiente: después ya es la respuesta del líder sobre lo que había.</param>
public record SolicitudDePermisoDto(
    int Id,
    LeaveType Tipo,
    string EtiquetaTipo,
    DateTime Desde,
    DateTime Hasta,
    int Dias,
    bool PorHoras,
    TimeOnly? HoraInicio,
    TimeOnly? HoraFin,
    decimal Horas,
    string Duracion,
    LeaveStatus Estado,
    string EtiquetaEstado,
    string? Motivo,
    string? Notas,
    string? RespuestaDelLider,
    bool TieneJustificante,
    bool LoRegistroElLider,
    bool SePuedeCancelar,
    bool SePuedeAdjuntar,
    bool SePuedeEliminar);

/// <summary>
/// Alta de una solicitud de permiso. Como la de vacaciones, es siempre para uno mismo.
/// </summary>
/// <param name="Dias">Días completos. En un permiso por horas va en 1: el tramo es de un solo día.</param>
/// <param name="HoraInicio">El tramo, <b>solo</b> si se pide por horas. Los dos en nulo —que es lo
/// que manda cualquier cliente que no sepa de esto— significan «día completo», que es como se pidieron
/// todos los permisos hasta ahora.</param>
public record NuevaSolicitudDePermisoRequest(
    LeaveType Tipo,
    DateTime Desde,
    int Dias,
    string? Motivo,
    string? Notas,
    TimeOnly? HoraInicio = null,
    TimeOnly? HoraFin = null);

// ── Comunes a las dos pantallas ─────────────────────────────────────────────────

/// <summary>
/// Cancelar una solicitud propia. El motivo es opcional y queda anotado en la respuesta de la
/// solicitud, que es donde el líder lo va a leer.
/// </summary>
public record CancelacionRequest(string? Motivo);

/// <summary>
/// Resultado de un alta: además del mensaje del servicio, el identificador de lo recién creado.
///
/// Hace falta para el justificante, que se sube en una segunda petición (multipart) contra la
/// solicitud ya existente: sin el identificador, la pantalla no tendría a qué colgarlo.
/// </summary>
public record SolicitudCreadaDto(bool Ok, string Mensaje, int Id);
