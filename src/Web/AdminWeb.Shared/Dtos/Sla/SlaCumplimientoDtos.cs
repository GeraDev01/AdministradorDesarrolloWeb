namespace AdminWeb.Shared.Dtos.Sla;

/// <summary>
/// Por qué se desglosa el reporte de cumplimiento. Son las mismas cuatro opciones del escritorio.
/// Va en el contrato (y no como texto libre) para que el cliente no pueda pedir una agrupación que
/// el servidor no sabe calcular.
/// </summary>
public enum AgrupacionSla
{
    Desarrollador = 0,
    Prioridad = 1,
    /// <summary>Cliente, que en Azure DevOps se identifica con los tags del ticket.</summary>
    Tag = 2,
    Mes = 3
}

/// <summary>
/// Una fila del reporte: un grupo (un desarrollador, una prioridad, un cliente, un mes) y cómo
/// terminaron sus compromisos.
/// </summary>
/// <param name="Resueltos">Cumplidos + vencidos. Es el denominador del porcentaje.</param>
/// <param name="PorcentajeCumplimiento">
/// NULL cuando todavía no hay nada resuelto, y ese null es deliberado: un 0 se leería como «cumplió
/// el 0%», que es justo lo contrario de «aún no se puede decir». La pantalla lo pinta como «—».
/// </param>
public record SlaCumplimientoFilaDto(
    string Grupo,
    int Cumplidos,
    int Vencidos,
    int EnCurso,
    int Cancelados,
    int Total,
    int Resueltos,
    double? PorcentajeCumplimiento);

/// <summary>
/// El reporte completo del período: el resumen global (las tarjetas) y el desglose por la
/// agrupación elegida.
/// </summary>
/// <param name="Compromisos">Cuántos compromisos vencen dentro del período consultado.</param>
/// <param name="Mensaje">
/// La línea de estado que el escritorio muestra bajo las tarjetas. Se resuelve en el servidor porque
/// depende de cómo se contó, no de cómo se pinta.
/// </param>
public record SlaCumplimientoDto(
    DateOnly Desde,
    DateOnly Hasta,
    AgrupacionSla Agrupacion,
    int Compromisos,
    SlaCumplimientoFilaDto Resumen,
    IReadOnlyList<SlaCumplimientoFilaDto> Filas,
    string Mensaje);
