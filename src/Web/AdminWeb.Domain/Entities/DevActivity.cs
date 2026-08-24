using AdminWeb.Shared.Enums;

namespace AdminWeb.Domain.Entities;

/// <summary>
/// Actividad libre de un desarrollador: trabajo real que no corresponde a ninguno de sus
/// requerimientos asignados (soporte, juntas, investigación, apoyo a otro equipo…). Existe para
/// que ese tiempo se pueda cronometrar y quede registrado, en lugar de perderse o colgarse de un
/// requerimiento que no le corresponde.
///
/// La medición vive en <see cref="WorkSession"/>, igual que la de los requerimientos: una
/// actividad puede acumular varias sesiones a lo largo de días.
/// </summary>
public class DevActivity
{
    public int Id { get; set; }
    public int DeveloperId { get; set; }

    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public DevActivityStatus Status { get; set; } = DevActivityStatus.Abierta;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    /// <summary>
    /// Sello de concurrencia optimista. Es nuevo de la web: la actividad puede estar abierta en dos
    /// navegadores a la vez —el dueño editando la descripción, el líder cerrándola—, algo que en el
    /// escritorio no ocurría. Con el sello, el segundo en guardar recibe un error de concurrencia en
    /// vez de pisar en silencio lo que escribió el primero.
    /// Solo se mapea contra SQL Server; en SQLite se ignora.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// Entrada de puntos que generó al calificarla el líder. Nulo = todavía no se ha calificado.
    ///
    /// <para><b>Es la guarda contra pagar dos veces</b>, además de la traza de dónde salieron esos
    /// puntos. Es el mismo mecanismo que ya usan la actividad del pool y el artículo de conocimiento:
    /// una vez que hay entrada, no se vuelve a calificar.</para>
    ///
    /// <para><b>Sin clave ajena</b>, igual que sus dos hermanas: con ella habría dos rutas de borrado
    /// en cascada desde <c>Developers</c> —por <c>PointEntries</c> y por <c>DevActivities</c>— y SQL
    /// Server rechaza crear esas restricciones. El precio asumido: si la limpieza de datos purga las
    /// entradas de puntos, este número queda colgando; es solo una traza.</para>
    /// </summary>
    public int? PointEntryId { get; set; }

    /// <summary>
    /// LA ACTIVIDAD DEL POOL PARA LA QUE ESTA FILA ES EL CRONÓMETRO. Nulo = es una actividad libre
    /// de verdad, de las que alguien abre para medir trabajo que no cuelga de ningún requerimiento.
    ///
    /// <para><b>Es una MARCA, no un vínculo, y por eso no se limpia nunca.</b> El vínculo vivo es
    /// <c>PoolActivity.LinkedDevActivityId</c>, que apunta al revés y que <c>SoltarReclamo</c> borra
    /// al devolver o liberar la actividad. Eso dejaba una percha cerrada, con tiempo medido y sin
    /// pagar, a la que ya no apuntaba ninguna actividad del pool — y la guarda que impedía
    /// calificarla preguntaba justo por ese vínculo, así que dejaba de reconocerla. El líder podía
    /// cobrarla por puntos y, cuando otro terminara el trabajo, el pool pagaba otra vez.</para>
    ///
    /// <para>Con la marca escrita al crear la percha y nunca borrada, «esto fue el cronómetro de una
    /// actividad del pool» sigue siendo cierto para siempre, que es lo que la pregunta necesita.
    /// El valor <b>-1</b> significa «fue percha, no sé de cuál»: es lo que puede recuperar el
    /// migrador de las perchas que ya habían perdido su vínculo antes de que esta columna existiera,
    /// y basta para lo único que la marca tiene que hacer.</para>
    ///
    /// <para><b>Sin clave ajena</b>, por lo mismo que <see cref="PointEntryId"/>: sería una segunda
    /// ruta de borrado en cascada desde <c>Developers</c> —por <c>PoolActivities</c> y por
    /// <c>DevActivities</c>— y SQL Server rechaza crear esas restricciones.</para>
    /// </summary>
    public int? PoolActivityId { get; set; }

    /// <summary>Si esta fila es la percha del cronómetro de una actividad del pool.</summary>
    public bool EsPerchaDelPool => PoolActivityId != null;

    public Developer Developer { get; set; } = null!;
}
