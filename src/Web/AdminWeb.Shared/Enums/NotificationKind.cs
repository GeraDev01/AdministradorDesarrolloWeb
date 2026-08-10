namespace AdminWeb.Shared.Enums;

/// <summary>Tipo de aviso, para el ícono y el enrutamiento al tocar la notificación.</summary>
public enum NotificationKind
{
    General = 0, DevOpsAssigned = 1, RequirementAssigned = 2, FreshDeskAssigned = 3, Comunicado = 4,
    /// <summary>La fecha comprometida de un requerimiento se acerca o ya pasó.</summary>
    CompromisoPorVencer = 5
}
