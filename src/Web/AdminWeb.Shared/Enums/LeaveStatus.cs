namespace AdminWeb.Shared.Enums;

/// <summary>
/// Estado de una solicitud de permiso. Mismo vocabulario que <see cref="VacationStatus"/> a
/// propósito: para quien la usa, pedir un permiso y pedir vacaciones son el mismo trámite.
/// </summary>
public enum LeaveStatus { Pendiente = 0, Aprobada = 1, Rechazada = 2, Cancelada = 3 }
