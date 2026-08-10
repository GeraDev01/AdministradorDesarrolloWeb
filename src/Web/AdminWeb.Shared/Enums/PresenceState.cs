namespace AdminWeb.Shared.Enums;

/// <summary>
/// En qué está quien tiene la aplicación abierta. Lo elige cada quien; nadie se lo pone.
/// </summary>
public enum PresenceState
{
    Disponible = 0,
    Ocupado = 1,
    EnReunion = 2,
    Comiendo = 3,
    Ausente = 4,
    /// <summary>Pausa corta. Existe como estado propio porque es la que más se usa y la que más
    /// incomoda tener que explicar; con un botón se marca y se quita.</summary>
    Descanso = 5
}
