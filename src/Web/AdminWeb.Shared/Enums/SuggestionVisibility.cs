namespace AdminWeb.Shared.Enums;

/// <summary>Quién puede ver la sugerencia además de su autor.</summary>
public enum SuggestionVisibility
{
    /// <summary>Todo el equipo la ve en el tablero de propuestas.</summary>
    Publica = 0,
    /// <summary>Solo el administrador (y su autor). Para lo que no se quiere plantear en público.</summary>
    SoloAdministrador = 1
}
