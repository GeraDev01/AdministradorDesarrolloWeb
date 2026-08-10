using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Autocalificacion;

/// <summary>
/// Todo lo que necesita «Mis actividades» para pintarse: las autocalificaciones del desarrollador,
/// sus actividades libres y los catálogos con los que se llenan los dos formularios.
///
/// Viaja de una vez y no en cinco peticiones por el mismo motivo que <c>MiJornadaDto</c>: una
/// pantalla que se pinta en tandas parpadea, y aquí además el desplegable de criterios hace falta en
/// cuanto se pulsa «Registrar» — pedirlo entonces metería una espera justo donde el escritorio no la
/// tenía.
/// </summary>
/// <param name="TieneFicha">Falso cuando la cuenta no está ligada a un desarrollador. La pantalla se
/// enseña igual, explicando por qué no hay nada: es mejor que un vacío sin motivo.</param>
/// <param name="Nivel">Nivel de la ficha, tal como está capturado. Se enseña porque varios criterios
/// son «(Junior)», «(Mid)» o «(Senior)» y sin el nivel a la vista no se sabe cuáles tocan.</param>
public record MisActividadesDto(
    bool TieneFicha,
    string? Nivel,
    IReadOnlyList<AutocalificacionDto> Autocalificaciones,
    IReadOnlyList<ActividadLibreDto> ActividadesLibres,
    IReadOnlyList<CriterioDto> Criterios,
    IReadOnlyList<OpcionDto> Requerimientos,
    LimitesDeCapturaDto Limites);

/// <summary>
/// Los topes que aplica el servidor, para que los controles de la pantalla se construyan con ellos.
///
/// Es una decisión del escritorio que conviene no perder: allí el control de horas tenía su propio
/// número escrito a mano y dejaba capturar 59 minutos MÁS de lo que el servicio aceptaba, así que el
/// registro se rechazaba al guardar sin que el campo hubiera avisado de nada. Con los topes viajando
/// desde donde se aplican, eso no puede volver a pasar.
/// </summary>
public record LimitesDeCapturaDto(
    int MaxMinutosDeclarados,
    int MaxArgumento,
    int MaxEvidenciasPorActividad,
    long MaxEvidenciaBytes);

/// <summary>
/// Una entrada de puntos del desarrollador: la que él mismo registró o la que le asignó el líder.
///
/// Las dos se enseñan juntas —igual que en el escritorio— porque el desarrollador quiere ver de una
/// vez todo lo que suma o resta en su mes; lo que las distingue es <paramref name="EsAutocalificacion"/>,
/// que decide qué se puede hacer con ellas.
///
/// La captura NO viaja aquí. Se sabe si la hay y se pide aparte al abrirla: con una imagen por
/// entrada, mandarlas todas convertiría una lista en varios megabytes.
/// </summary>
public record AutocalificacionDto(
    int Id,
    DateTime FechaUtc,
    int Anio,
    int Mes,
    int CriterioId,
    string Criterio,
    string? DescripcionCriterio,
    int Puntos,
    int? MinutosDeclarados,
    string? Enlace,
    string? Comentario,
    int? RequerimientoId,
    string? Requerimiento,
    bool TieneCaptura,
    PointApprovalStatus Estado,
    bool EsAutocalificacion,
    int Vueltas,
    string? ComentarioDeRevision,
    string? HistorialDeRevision)
{
    /// <summary>
    /// Las mismas condiciones que comprueba <c>PerformanceScoringService.EditarAutocalificacionAsync</c>.
    /// Están aquí, en el contrato, para que la pantalla no ofrezca un botón que el servidor va a
    /// rechazar y para que las dos partes no puedan discrepar sobre cuándo se puede corregir.
    /// </summary>
    public bool SePuedeCorregir => EsAutocalificacion && Estado != PointApprovalStatus.Aprobado;

    /// <summary>Mismo criterio, con la regla de <c>ReplicarAsync</c>: solo se replica un rechazo propio.</summary>
    public bool SePuedeReplicar => EsAutocalificacion && Estado == PointApprovalStatus.Rechazado;
}

/// <summary>
/// Una actividad registrable, con lo que vale y lo que significa.
/// </summary>
/// <param name="Disponible">Falso cuando ya no se puede elegir —se desactivó, es de equipo o no da
/// puntos positivos— pero alguna entrada del desarrollador la usa. Se conserva en la lista por la
/// misma razón que en el escritorio: si desapareciera, corregir esa entrada la guardaría con OTRO
/// criterio sin avisar.</param>
public record CriterioDto(int Id, string Nombre, string? Descripcion, int Puntos, bool Disponible)
{
    /// <summary>
    /// Cómo se lee en el desplegable. Va en el contrato —igual que en <see cref="OpcionDto"/>— para
    /// que el nombre, lo que vale y el aviso de que ya no se puede elegir se escriban una sola vez:
    /// repartidos por pantalla acabarían diciendo cosas distintas de la misma actividad.
    /// </summary>
    public string Texto => $"{Nombre}  (+{Puntos} pts)" + (Disponible ? "" : "  — ya no disponible");
}

/// <summary>
/// Una actividad libre del desarrollador: el trabajo que no cae en ninguno de sus requerimientos.
/// </summary>
/// <param name="SegundosCronometrados">Lo que ha medido el cronómetro. Es lo que impide borrarla:
/// ese tiempo es evidencia de trabajo hecho.</param>
/// <param name="Evidencias">Cuántos archivos la respaldan. Solo el número: los archivos se piden al
/// abrir la actividad.</param>
public record ActividadLibreDto(
    int Id,
    string Titulo,
    string? Descripcion,
    DevActivityStatus Estado,
    DateTime CreadaUtc,
    DateTime? CerradaUtc,
    int SegundosCronometrados,
    int Evidencias)
{
    /// <summary>Cerrada es evidencia consolidada: el servicio no admite cambios hasta reabrirla.</summary>
    public bool EstaAbierta => Estado == DevActivityStatus.Abierta;
}

/// <summary>
/// Un archivo que respalda una actividad libre, SIN su contenido. Los bytes se piden a
/// <c>/api/adjuntos/actividad/{id}</c>, que es por donde salen todos los adjuntos de la aplicación.
/// </summary>
public record EvidenciaDto(
    int Id,
    string NombreArchivo,
    string TipoDeContenido,
    long Bytes,
    string? Nota,
    DateTime FechaUtc)
{
    public bool EsImagen => TipoDeContenido.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Registrar o corregir una autocalificación.
///
/// No trae puntos ni estado, y no es un olvido: el puntaje lo relee el servicio del criterio y el
/// estado siempre nace «Pendiente». El desarrollador elige QUÉ registra, nunca CUÁNTO vale.
/// </summary>
/// <param name="Captura">La imagen nueva, si se adjuntó una. Va en el cuerpo y no como archivo
/// suelto porque es un CAMPO del formulario: se guarda con él o no se guarda, lo que evita la
/// captura huérfana de quien se arrepiente a medias.</param>
/// <param name="QuitarCaptura">Solo al corregir, y solo cuando no viene una nueva: dice que la que
/// había se retira. Sin este tercer estado, «no mandé imagen» significaría a la vez «déjala como
/// está» y «bórrala», y una de las dos lecturas perdería la captura en silencio.</param>
public record AutocalificacionRequest(
    int CriterioId,
    int Anio,
    int Mes,
    string? Comentario,
    int? RequerimientoId,
    int? MinutosDeclarados,
    string? Enlace,
    byte[]? Captura = null,
    string? NombreDeCaptura = null,
    bool QuitarCaptura = false);

/// <summary>
/// La réplica a un rechazo. El argumento es obligatorio y lo comprueba el servicio: una réplica
/// vacía le devuelve al líder un trabajo que ya hizo sin darle nada nuevo que valorar.
/// </summary>
public record ReplicaRequest(string Argumento);

/// <summary>Alta o edición de una actividad libre.</summary>
public record ActividadLibreRequest(string Titulo, string? Descripcion);
