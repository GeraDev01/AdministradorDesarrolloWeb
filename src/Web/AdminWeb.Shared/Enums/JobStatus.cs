namespace AdminWeb.Shared.Enums;

public enum JobStatus
{
    Pendiente = 0, EnCurso = 1, Completado = 2, Fallido = 3, Cancelado = 4,
    /// <summary>Unos servidores sí y otros no. Antes esto se reportaba como «Completado».</summary>
    Parcial = 5
}
