namespace AdminWeb.Shared.Enums;

/// <summary>Cómo terminó una jornada.</summary>
public enum PresenceEnd
{
    /// <summary>Cerró sesión o salió de la aplicación.</summary>
    CierreNormal = 0,
    /// <summary>Dejó de latir: se colgó, se apagó el equipo o se fue la red.</summary>
    SinLatido = 1
}
