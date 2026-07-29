namespace Administrador_Desarrollo_Web.Services;

/// <summary>Disponibilidad de un desarrollador para asignarle trabajo.</summary>
public enum Disponibilidad
{
    /// <summary>Está de vacaciones hoy.</summary>
    DeVacaciones,
    /// <summary>Sin requerimientos abiertos.</summary>
    Libre,
    /// <summary>Con trabajo, dentro de su capacidad.</summary>
    Ocupado,
    /// <summary>Con más horas estimadas pendientes que su capacidad.</summary>
    Sobrecargado
}

public record CapacityRow(string Developer, int Abiertos, double HorasPendientes, double HorasRegistradas,
    int DiasVacaciones, Disponibilidad Estado);

/// <summary>
/// Planeación de capacidad: cruza la carga (requerimientos abiertos y horas estimadas pendientes) con
/// las vacaciones para ver quién está libre, ocupado, sobrecargado o de vacaciones. Lógica pura.
/// </summary>
public static class CapacityStats
{
    /// <summary>Umbral por defecto (horas estimadas pendientes) a partir del cual se considera sobrecarga.</summary>
    public const double CapacidadPorDefecto = 40;

    public static Disponibilidad Clasificar(bool deVacacionesHoy, int abiertos, double horasPendientes, double capacidadHoras)
    {
        if (deVacacionesHoy) return Disponibilidad.DeVacaciones;
        if (abiertos == 0)   return Disponibilidad.Libre;
        return horasPendientes > capacidadHoras ? Disponibilidad.Sobrecargado : Disponibilidad.Ocupado;
    }

    /// <summary>Días (inclusive) de una vacación aprobada que caen dentro de un rango dado; 0 si no se solapa.</summary>
    public static int DiasVacacionEnRango(DateTime vacInicio, DateTime vacFin, DateTime rangoInicio, DateTime rangoFin)
    {
        var ini = vacInicio.Date > rangoInicio.Date ? vacInicio.Date : rangoInicio.Date;
        var fin = vacFin.Date    < rangoFin.Date    ? vacFin.Date    : rangoFin.Date;
        return fin < ini ? 0 : (fin - ini).Days + 1;
    }

    /// <summary>true si la fecha dada cae dentro de la vacación (inclusive).</summary>
    public static bool EnVacacion(DateTime vacInicio, DateTime vacFin, DateTime dia) =>
        dia.Date >= vacInicio.Date && dia.Date <= vacFin.Date;

    public static string EtiquetaEstado(Disponibilidad d) => d switch
    {
        Disponibilidad.DeVacaciones  => "🏖 De vacaciones",
        Disponibilidad.Libre         => "🟢 Libre",
        Disponibilidad.Ocupado       => "🟡 Ocupado",
        Disponibilidad.Sobrecargado  => "🔴 Sobrecargado",
        _                            => d.ToString()
    };
}
