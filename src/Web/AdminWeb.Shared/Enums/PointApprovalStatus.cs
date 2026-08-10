namespace AdminWeb.Shared.Enums;

/// <summary>
/// Estado de aprobación de una entrada de puntos. Las que asigna el jefe (Admin)
/// nacen Aprobado; las que el desarrollador registra por autocalificación nacen
/// Pendiente y solo cuentan en el ranking cuando el jefe las aprueba.
/// </summary>
public enum PointApprovalStatus
{
    Pendiente = 0,
    Aprobado = 1,
    Rechazado = 2
}
