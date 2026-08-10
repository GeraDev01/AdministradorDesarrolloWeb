namespace AdminWeb.Domain.Entities;

/// <summary>
/// Un TRAMO trabajado: el segmento entre una reanudación y la siguiente pausa/detención, con su
/// fecha. Se registra cada vez que se consolida tiempo, de modo que el total por día sea EXACTO
/// aunque una sesión abarque varios días (con solo pausar y reanudar al otro día).
///
/// No lleva llaves foráneas a propósito: es una bitácora histórica de tiempo; si luego se borra el
/// requerimiento, el tramo se conserva como registro. Se atribuye al día LOCAL en que inició.
/// </summary>
public class WorkInterval
{
    public int Id { get; set; }
    public int DeveloperId { get; set; }
    public int? RequirementId { get; set; }
    public int? ActivityId { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public int Seconds { get; set; }

    /// <summary>Fecha LOCAL (a medianoche) a la que se atribuye el tramo, para agrupar por día.</summary>
    public DateTime LocalDate { get; set; }
}
