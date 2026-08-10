namespace AdminWeb.Shared.Dtos.Desempeno;

/// <summary>
/// Una fila del ranking individual TAL COMO LA VE EL ADMINISTRADOR: además del total, el desglose
/// entre lo premiado y lo penalizado y cuántas entradas lo componen.
///
/// Este desglose es exactamente lo que NO recibe un desarrollador (ver <see cref="RankingPublicoFilaDto"/>):
/// saber que un compañero acumuló −8 puntos en 3 entradas es información de evaluación, y en el
/// escritorio solo se llegaba a ella desde la pantalla del administrador.
/// </summary>
/// <param name="Posicion">Lugar 1-based entre quienes compiten. 0 en los de nivel Lead: no ocupan lugar.</param>
/// <param name="Medalla">🥇🥈🥉 / «#n», y 👑 para el nivel Lead, que aparece fuera de concurso.</param>
/// <param name="Premio">Suma de los puntos positivos aprobados del período.</param>
/// <param name="Penalizacion">Suma de los puntos negativos aprobados (viene en negativo, como se captura).</param>
public record RankingIndividualFilaDto(
    int Posicion,
    string Medalla,
    int DeveloperId,
    string Desarrollador,
    int Total,
    int Premio,
    int Penalizacion,
    int Entradas,
    bool EsNivelLead);

/// <summary>
/// Una fila del ranking por equipo para el administrador. Se separan los puntos que vienen de los
/// integrantes de los que se le asignaron al equipo como tal, porque son dos cosas que se ganan de
/// forma distinta y sumarlas sin más escondería cuál de las dos está moviendo el marcador.
/// </summary>
public record RankingEquipoFilaDto(
    int Posicion,
    string Medalla,
    int TeamId,
    string Equipo,
    int Total,
    int PuntosIntegrantes,
    int PuntosEquipo,
    int Miembros);

/// <summary>Los dos rankings del período para la pantalla de desempeño del administrador.</summary>
/// <param name="IncluyeNivelLead">Si se pidieron también los de nivel Lead (fuera de concurso).</param>
public record DesempenoAdminDto(
    int Anio,
    int Mes,
    bool IncluyeNivelLead,
    IReadOnlyList<RankingIndividualFilaDto> Individual,
    IReadOnlyList<RankingEquipoFilaDto> Equipos);

/// <summary>
/// Una autocalificación esperando el sí o el no del líder, con TODA la evidencia que la respalda.
///
/// Este contrato es solo del administrador: lleva el comentario, el enlace y el historial de la
/// discusión de una persona concreta. Un desarrollador nunca lo recibe — lo suyo va por
/// «Mis Actividades», y de los demás no ve más que nombre y total (ver <see cref="RankingPublicoFilaDto"/>).
///
/// La CAPTURA no viaja: solo si la hay. Los bytes se piden a
/// <c>/api/autocalificacion/entradas/{id}/captura</c>, que es la ruta que ya sirve esa imagen y que
/// comprueba que quien pregunta es el dueño o un administrador.
/// </summary>
/// <param name="Periodo">«Julio 2026». Es el mes AL QUE SE ATRIBUYE la actividad, que no tiene por
/// qué ser el mes en que se registró.</param>
/// <param name="Vueltas">Cuántas veces volvió a revisión tras un rechazo. Cero es una propuesta
/// nueva; dos es la tercera insistencia sobre lo mismo, y saberlo antes de abrirla cambia cómo se
/// revisa.</param>
/// <param name="TiempoDeclarado">«3h 20m» / «45m» / «—», ya formateado.</param>
/// <param name="HistorialDeRevision">El ida y vuelta completo: cada rechazo con su motivo y cada
/// réplica con su argumento, en orden. Null mientras no haya habido discusión.</param>
public record PuntoPendienteDto(
    int Id,
    int DesarrolladorId,
    string Desarrollador,
    string Criterio,
    string? DescripcionCriterio,
    int Puntos,
    int Anio,
    int Mes,
    string Periodo,
    DateTime FechaUtc,
    int? RequerimientoId,
    string? Requerimiento,
    int Vueltas,
    int? MinutosDeclarados,
    string TiempoDeclarado,
    bool TieneCaptura,
    string? Enlace,
    string? Comentario,
    string? HistorialDeRevision)
{
    /// <summary>Está en discusión: ya se rechazó al menos una vez y el desarrollador replicó.</summary>
    public bool EsReplica => Vueltas > 0;
}

/// <summary>
/// Aprobar una tanda de autocalificaciones. Van varias porque revisar es un trabajo por tandas, igual
/// que en la rejilla de selección múltiple del escritorio.
/// </summary>
public record AprobarPuntosRequest(IReadOnlyList<int> Ids);

/// <summary>
/// Rechazar una tanda, con el motivo que va a leer quien las registró.
///
/// El motivo NO es opcional. El servidor lo exige y el mensaje con el que lo exige es el que se
/// enseña; mandarlo vacío desde la pantalla solo serviría para que conteste lo que ya se sabe aquí.
/// </summary>
public record RechazarPuntosRequest(IReadOnlyList<int> Ids, string Motivo);

/// <summary>
/// Corregir el puntaje de una entrada que sigue pendiente. Admite negativos: hay actividades que al
/// revisarlas resultan ser un descuento.
/// </summary>
public record AjustarPuntosRequest(int Puntos);

/// <summary>
/// Una fila de ranking tal como se le muestra a un DESARROLLADOR: nombre y total, y nada más.
///
/// La restricción vive aquí, en el contrato, y no en la pantalla, porque el cliente corre en la
/// máquina de cada quien y se puede manipular: si el detalle de los demás (entradas, comentarios,
/// evidencias, desglose de premios y castigos) viajara en el JSON, bastaría abrir las herramientas
/// del navegador para leerlo. Lo que no le corresponde a un rol sencillamente no existe en su DTO.
///
/// Tampoco viaja el id del desarrollador: no hace falta para pintar la tabla, y con él se podría
/// intentar consultar por su cuenta otras rutas.
/// </summary>
/// <param name="EsMio">Su propia fila (o la de su equipo), para resaltarla. Lo resuelve el servidor.</param>
public record RankingPublicoFilaDto(int Posicion, string Medalla, string Nombre, int Total, bool EsMio);

/// <summary>
/// El panel del desarrollador: sus indicadores del mes y los dos rankings, ya recortados.
/// </summary>
/// <param name="Nivel">Etiqueta del nivel de su ficha («🌳 Senior»), o el aviso de que no tiene.</param>
/// <param name="ExplicacionNivel">Qué se espera de ese nivel. Null cuando no hay nada que explicar.</param>
/// <param name="Equipo">Nombre del equipo, o «Sin equipo».</param>
/// <param name="RolEnEquipo">«Líder del equipo» / «Integrante». Null si no tiene equipo.</param>
/// <param name="Aprobado">Puntos ya aprobados del período.</param>
/// <param name="EnRevision">Puntos que siguen pendientes de aprobación.</param>
/// <param name="Rechazadas">
/// CUÁNTAS actividades le rechazaron, no cuántos puntos: es el número que el escritorio enseña en la
/// tarjeta, porque lo accionable es «tengo 2 cosas que corregir», no un total de puntos que nunca
/// llegó a contar.
/// </param>
/// <param name="Posicion">Frase ya resuelta: «#3 de 12», «Fuera de ranking (nivel Lead)»…</param>
/// <param name="TieneFicha">Falso si la cuenta no está ligada a una ficha de desarrollador.</param>
public record MiPanelDto(
    int Anio,
    int Mes,
    bool TieneFicha,
    string Nivel,
    string? ExplicacionNivel,
    string Equipo,
    string? RolEnEquipo,
    int Aprobado,
    int EnRevision,
    int Rechazadas,
    string Posicion,
    IReadOnlyList<RankingPublicoFilaDto> Individual,
    IReadOnlyList<RankingPublicoFilaDto> Equipos);
