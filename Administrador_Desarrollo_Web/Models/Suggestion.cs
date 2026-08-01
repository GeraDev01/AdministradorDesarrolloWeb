namespace Administrador_Desarrollo_Web.Models;

/// <summary>Sobre qué es la sugerencia: el producto (la app), el departamento, u otra cosa.</summary>
public enum SuggestionCategory { Producto = 0, Departamento = 1, Otro = 2 }

/// <summary>Quién puede ver la sugerencia además de su autor.</summary>
public enum SuggestionVisibility
{
    /// <summary>Todo el equipo la ve en el tablero de propuestas.</summary>
    Publica = 0,
    /// <summary>Solo el administrador (y su autor). Para lo que no se quiere plantear en público.</summary>
    SoloAdministrador = 1
}

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

/// <summary>
/// Propuesta o sugerencia que un desarrollador envía para mejorar el producto o el departamento. El
/// administrador la revisa, le cambia el estado y puede responderla. El autor puede pedir que su
/// nombre no se muestre al administrador (<see cref="Anonymous"/>): la ficha sigue ligada para que él
/// mismo dé seguimiento, pero el panel de administración oculta quién la mandó.
/// </summary>
public class Suggestion
{
    public int Id { get; set; }

    /// <summary>Ficha del desarrollador que la propone. Null si su usuario no está vinculado a una.</summary>
    public int? DeveloperId { get; set; }

    /// <summary>Usuario que la envió (sirve para «mis sugerencias» aunque no haya ficha ligada).</summary>
    public int CreatedByUserId { get; set; }

    public string Title { get; set; } = "";
    public string Body { get; set; } = "";

    public SuggestionCategory Category { get; set; } = SuggestionCategory.Producto;
    public SuggestionStatus Status { get; set; } = SuggestionStatus.Nueva;

    /// <summary>El autor pidió no mostrar su nombre al administrador.</summary>
    public bool Anonymous { get; set; }

    /// <summary>
    /// Si la ve todo el equipo o solo el administrador. Antes TODAS eran públicas sin preguntar, así
    /// que quien quería plantear algo delicado —el ambiente del área, una queja— no tenía dónde.
    /// </summary>
    public SuggestionVisibility Visibility { get; set; } = SuggestionVisibility.Publica;

    /// <summary>
    /// El autor abre su propuesta a los votos del equipo. Apagado, la sugerencia se lee pero no se
    /// vota: hay cosas que no son un concurso de popularidad y que aun así conviene plantear.
    /// Una sugerencia que solo ve el administrador nunca se puede votar, esté como esté esta bandera.
    /// </summary>
    public bool OpenToVoting { get; set; } = true;

    /// <summary>Solo tiene sentido votar lo que el equipo ve y su autor abrió a votación.</summary>
    public bool SePuedeVotar => Visibility == SuggestionVisibility.Publica && OpenToVoting;

    /// <summary>Respuesta del administrador al atenderla.</summary>
    public string? AdminResponse { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Developer? Developer { get; set; }
}
