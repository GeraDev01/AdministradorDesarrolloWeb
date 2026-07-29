namespace Administrador_Desarrollo_Web.Models;

/// <summary>Sobre qué es la sugerencia: el producto (la app), el departamento, u otra cosa.</summary>
public enum SuggestionCategory { Producto = 0, Departamento = 1, Otro = 2 }

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

    /// <summary>Respuesta del administrador al atenderla.</summary>
    public string? AdminResponse { get; set; }
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Developer? Developer { get; set; }
}
