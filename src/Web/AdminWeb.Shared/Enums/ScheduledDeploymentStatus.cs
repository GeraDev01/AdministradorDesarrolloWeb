namespace AdminWeb.Shared.Enums;

public enum ScheduledDeploymentStatus
{
    /// <summary>Esperando su hora.</summary>
    Programado = 0,
    /// <summary>Alguien lo tomó y lo está ejecutando.</summary>
    EnEjecucion = 1,
    Completado = 2,
    Fallido = 3,
    Cancelado = 4,
    /// <summary>Pasó su hora sin que ninguna aplicación estuviera abierta para ejecutarlo.</summary>
    Perdido = 5
}
