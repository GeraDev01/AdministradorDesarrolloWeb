namespace AdminWeb.Shared.Enums;

/// <summary>
/// En qué punto del camino está un artículo de la base de conocimiento.
///
/// <para>Son CUATRO y no tres, y el cuarto es el que más se piensa. Lo natural sería que un rechazo
/// devolviera el artículo a <see cref="Borrador"/> con el motivo escrito en un campo, pero eso
/// borra la diferencia entre dos hechos que no son el mismo: «esto nunca se mandó» y «esto se mandó
/// y se devolvió». En la lista del autor los dos saldrían igual —como trabajo a medias— y el motivo
/// viviría en un texto que nadie está obligado a abrir.</para>
///
/// <para>Y hay una razón de peso: lo devuelto tiene que poder CONTARSE. Una cola de revisión muere
/// cuando lo que espera no se ve, y eso vale para los dos lados —el líder tiene que ver lo que le
/// llegó y el autor lo que le devolvieron—. Con estado propio, «te devolvieron dos artículos» es
/// una consulta; disuelto en los borradores, no lo es.</para>
///
/// <para>No es un callejón sin salida: de <see cref="Rechazado"/> se sale editando y volviendo a
/// enviar, y cada vuelta queda numerada y con su motivo, igual que en la autocalificación de puntos
/// y en el pool de actividades.</para>
/// </summary>
public enum KnowledgeStatus
{
    /// <summary>Se está escribiendo. Solo lo ve su autor — ni siquiera el líder.</summary>
    Borrador = 0,

    /// <summary>Enviado. Lo ven su autor y el líder, y es lo que forma la cola de revisión.</summary>
    PorRevisar = 1,

    /// <summary>Aprobado y a la vista de cualquiera con sesión.</summary>
    Publicado = 2,

    /// <summary>
    /// El líder lo devolvió con un motivo. Lo ven su autor y el líder. Sirve también para RETIRAR
    /// algo ya publicado que dejó de ser cierto: deja de verse y queda dicho por qué.
    /// </summary>
    Rechazado = 3
}
