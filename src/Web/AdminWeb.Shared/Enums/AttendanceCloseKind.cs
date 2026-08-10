namespace AdminWeb.Shared.Enums;

/// <summary>Cómo quedó cerrado un registro oficial de asistencia.</summary>
public enum AttendanceCloseKind
{
    /// <summary>La persona marcó su salida.</summary>
    Manual = 0,
    /// <summary>Olvidó marcarla; se cerró sola al marcar la entrada del día siguiente, estimando
    /// la hora con la última señal de la telemetría de ese día.</summary>
    Olvido = 1,
    /// <summary>La cerró o la corrigió el líder, con motivo y rastro.</summary>
    Admin = 2
}
