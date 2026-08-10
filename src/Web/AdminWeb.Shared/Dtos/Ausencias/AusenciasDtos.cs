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
public record MisVacacionesDto(
    SaldoDeVacacionesDto Saldo,
    IReadOnlyList<SolicitudDeVacacionesDto> Solicitudes,
    long MaxRespaldoBytes,
    IReadOnlyList<FirmaDeSolicitudDto>? Firmas = null);

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
public record MisPermisosDto(
    bool TieneFicha,
    ResumenDePermisosDto Resumen,
    IReadOnlyList<SolicitudDePermisoDto> Solicitudes,
    IReadOnlyList<OpcionDto> TiposDePermiso,
    int MaxDias,
    long MaxJustificanteBytes);

/// <summary>Los tres indicadores de la cabecera, los mismos que enseñaba el escritorio.</summary>
public record ResumenDePermisosDto(int EsperandoRespuesta, int AprobadosEsteAnio, int DiasAprobadosEsteAnio);

/// <summary>
/// Una solicitud de permiso propia.
///
/// Como en vacaciones, el justificante <b>no viaja aquí</b>: se pide por
/// <c>/api/adjuntos/permiso/{id}</c> cuando alguien pulsa.
/// </summary>
/// <param name="Hasta">Último día cubierto, ya calculado. Con «5 días desde una fecha» hay que
/// contar a mano para saber hasta cuándo llega, y ahí es donde se cuela el error de un día.</param>
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

/// <summary>Alta de una solicitud de permiso. Como la de vacaciones, es siempre para uno mismo.</summary>
public record NuevaSolicitudDePermisoRequest(
    LeaveType Tipo,
    DateTime Desde,
    int Dias,
    string? Motivo,
    string? Notas);

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
