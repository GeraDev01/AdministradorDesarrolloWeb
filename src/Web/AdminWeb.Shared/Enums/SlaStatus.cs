namespace AdminWeb.Shared.Enums;

public enum SlaStatus
{
    /// <summary>Vigente: aún no vence, o venció pero sigue exigiéndose.</summary>
    Activo = 0,
    /// <summary>El administrador lo dio por atendido.</summary>
    Cumplido = 1,
    /// <summary>Pasó la fecha límite sin cerrarse.</summary>
    Vencido = 2,
    /// <summary>Se dejó sin efecto.</summary>
    Cancelado = 3
}
