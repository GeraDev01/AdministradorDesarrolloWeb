namespace AdminWeb.Shared.Enums;

/// <summary>Ciclo de vida de una sugerencia desde que se envía hasta que el administrador la atiende.</summary>
public enum SuggestionStatus
{
    /// <summary>Recién enviada; nadie la ha revisado.</summary>
    Nueva = 0,
    /// <summary>El administrador la está considerando.</summary>
    EnRevision = 1,
    /// <summary>Se acepta la idea.</summary>
    Aceptada = 2,
    /// <summary>No se llevará a cabo.</summary>
    Rechazada = 3,
    /// <summary>Ya se implementó.</summary>
    Implementada = 4
}
