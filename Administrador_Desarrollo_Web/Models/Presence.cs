namespace Administrador_Desarrollo_Web.Models;

/// <summary>
/// En qué está quien tiene la aplicación abierta. Lo elige cada quien; nadie se lo pone.
/// </summary>
public enum PresenceState
{
    Disponible = 0,
    Ocupado = 1,
    EnReunion = 2,
    Comiendo = 3,
    Ausente = 4,
    /// <summary>Pausa corta. Existe como estado propio porque es la que más se usa y la que más
    /// incomoda tener que explicar; con un botón se marca y se quita.</summary>
    Descanso = 5
}

/// <summary>
/// Una jornada: desde que alguien inició sesión hasta que la cerró. Es el registro de asistencia.
///
/// El <see cref="LastSeenUtc"/> es un LATIDO: la aplicación lo refresca cada pocos minutos mientras
/// está abierta. Sin él, un cuelgue o un apagón dejarían la sesión abierta para siempre y el
/// indicador diría «conectado» durante días. Con él, quien deja de latir se da por desconectado
/// solo, y la jornada se cierra con la hora del último latido — no con la de ahora, que sería
/// regalarle horas que nadie trabajó.
///
/// El ESTADO (en el baño, comiendo…) se guarda aquí porque es información del momento, no histórico:
/// se sobrescribe cada vez que la persona lo cambia y no queda rastro de cuánto tiempo estuvo en
/// cada uno. Fue una decisión deliberada — un registro minutado de las pausas de alguien es
/// vigilancia, no asistencia.
/// </summary>
public class WorkPresence
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>Ficha del desarrollador si la cuenta la tiene ligada. Null para cuentas sin ficha.</summary>
    public int? DeveloperId { get; set; }

    /// <summary>Nombre con el que mostrarlo aunque después se borre el usuario o la ficha.</summary>
    public string DisplayName { get; set; } = "";

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Null mientras la jornada sigue abierta.</summary>
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>Último latido. Es lo que distingue «sigue ahí» de «se le murió la aplicación».</summary>
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    public PresenceState State { get; set; } = PresenceState.Disponible;

    /// <summary>Nota corta y opcional junto al estado («vuelvo 15:30»).</summary>
    public string? StateNote { get; set; }

    /// <summary>Cómo terminó la jornada. Sirve para no contar como trabajadas las que se cayeron.</summary>
    public PresenceEnd? EndReason { get; set; }

    /// <summary>Equipo desde el que se inició la sesión, para distinguir dos jornadas del mismo día.</summary>
    public string? Origin { get; set; }

    public bool Abierta => EndedAtUtc == null;

    /// <summary>Duración de la jornada; para una abierta, lo que va del último latido.</summary>
    public TimeSpan Duracion => (EndedAtUtc ?? LastSeenUtc) - StartedAtUtc;
}

/// <summary>Cómo terminó una jornada.</summary>
public enum PresenceEnd
{
    /// <summary>Cerró sesión o salió de la aplicación.</summary>
    CierreNormal = 0,
    /// <summary>Dejó de latir: se colgó, se apagó el equipo o se fue la red.</summary>
    SinLatido = 1
}
