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

    public Developer Developer { get; set; } = null!;
}
