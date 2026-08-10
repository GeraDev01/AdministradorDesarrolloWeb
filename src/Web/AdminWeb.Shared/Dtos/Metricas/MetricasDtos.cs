namespace AdminWeb.Shared.Dtos.Metricas;

/// <summary>
/// Con qué intención se pinta un número. Es un tono, no un color: el cliente lo traduce a la variable
/// de tema que corresponda y así el mismo dato se ve igual en las cuatro pantallas que lo enseñan.
///
/// Viaja resuelto desde el servidor —y no se deduce en la pantalla— porque el umbral que decide si
/// un ratio de 1.3 es «malo» es una regla de negocio (<c>EstimationStats</c>, <c>CapacityStats</c>),
/// y calcularla otra vez en el navegador es garantizar que un día las dos discrepen.
/// </summary>
public enum TonoDeIndicador
{
    Neutro,
    Exito,
    Aviso,
    Peligro
}

/// <summary>
/// Una tarjeta de indicador, ya resuelta: qué dice, qué número enseña y con qué tono.
///
/// Es la traducción de las tarjetas que <c>MetricsControl</c> y <c>EstimationCapacityControl</c>
/// armaban a mano en cada carga. El valor viaja como TEXTO porque algunos no son un número —«—»
/// cuando no hay nada que medir, «1.30×», «12 d»— y convertirlos en pantalla obligaría a repartir
/// entre cliente y servidor una decisión que ya está tomada.
/// </summary>
/// <param name="Explicacion">Qué significa el número, para el tooltip. Null si no hace falta.</param>
public record IndicadorDto(string Titulo, string Valor, TonoDeIndicador Tono, string? Explicacion = null);

// ── Métricas de ciclo de vida ───────────────────────────────────────────────────

/// <summary>
/// El tiempo de vida de un requerimiento: cuánto lleva vivo, cuánto lleva en su estado actual y qué
/// tan cerca está de su compromiso.
/// </summary>
/// <param name="DiasVivo">Días desde que se creó hasta que se entregó (o hasta hoy si sigue abierto).</param>
/// <param name="DiasEnEstado">Días desde el último cambio de estado. Es lo que delata a los atorados.</param>
/// <param name="DiasParaCompromiso">
/// Positivo = días que faltan; negativo = días de atraso. Null cuando no hay fecha comprometida o
/// cuando el requerimiento ya está cerrado: contarle días a algo entregado no dice nada.
/// </param>
public record MetricaDeRequerimientoDto(
    int Id,
    string Titulo,
    string Estado,
    int DiasVivo,
    int DiasEnEstado,
    string Compromiso,
    int? DiasParaCompromiso,
    bool Atrasado,
    string Desarrolladores,
    int Avance)
{
    /// <summary>
    /// «faltan 3» / «⚠ atraso 5» / «—». La frase se arma aquí, en el contrato, para que la rejilla y
    /// una futura exportación no la escriban distinto.
    /// </summary>
    public string PlazoTexto => DiasParaCompromiso is not int d ? "—"
        : d >= 0 ? $"faltan {d}"
        : $"⚠ atraso {-d}";
}

/// <summary>La carga abierta de un desarrollador y la antigüedad de lo que arrastra.</summary>
public record MetricaPorDesarrolladorDto(
    string Desarrollador,
    int Activos,
    int Atrasados,
    int EdadPromedio,
    int MasAntiguo);

/// <summary>Todo lo que necesita la pantalla de métricas, en una sola respuesta.</summary>
public record MetricasDto(
    IReadOnlyList<IndicadorDto> Indicadores,
    IReadOnlyList<MetricaDeRequerimientoDto> Requerimientos,
    IReadOnlyList<MetricaPorDesarrolladorDto> PorDesarrollador);

// ── Estimación y capacidad ──────────────────────────────────────────────────────

/// <summary>
/// Un requerimiento con estimación, frente al tiempo que de verdad costó.
/// </summary>
/// <param name="Ratio">Real ÷ estimado. Null mientras no haya tiempo cronometrado: dividir entre
/// cero daría un número, y ese número mentiría.</param>
/// <param name="Clasificacion">«✓ Preciso», «▲ Subestimado (tomó más)»… tal como la escribe
/// <c>EstimationStats.EtiquetaClase</c>.</param>
public record FilaDeEstimacionDto(
    int RequerimientoId,
    string Titulo,
    string Estado,
    double HorasEstimadas,
    double HorasReales,
    double Delta,
    double? Ratio,
    string Clasificacion,
    TonoDeIndicador Tono);

/// <summary>
/// La carga de un desarrollador para decidir a quién asignarle lo siguiente.
/// </summary>
/// <param name="HorasPendientes">Suma de horas estimadas de sus requerimientos abiertos.</param>
/// <param name="HorasRegistradas">Lo que lleva cronometrado en total. Es contexto, no carga futura.</param>
/// <param name="DiasDeVacaciones">Días de vacación aprobada dentro de la ventana consultada.</param>
public record FilaDeCapacidadDto(
    string Desarrollador,
    int Abiertos,
    double HorasPendientes,
    double HorasRegistradas,
    int DiasDeVacaciones,
    string Disponibilidad,
    TonoDeIndicador Tono);

/// <summary>
/// Las dos mitades del reporte de planeación —precisión de estimaciones y capacidad del equipo— en
/// una sola respuesta, igual que las dos pestañas del control del escritorio.
/// </summary>
/// <param name="DiasDeVentana">Sobre cuántos días se contaron las vacaciones (hoy incluido).</param>
/// <param name="CapacidadHoras">Umbral de sobrecarga que se aplicó, para poder decirlo en pantalla.</param>
/// <param name="ResumenDeEstimacion">La línea de estado del escritorio: cuántos se midieron y cuántos ya tienen tiempo.</param>
public record EstimacionYCapacidadDto(
    int DiasDeVentana,
    double CapacidadHoras,
    IReadOnlyList<IndicadorDto> IndicadoresDeEstimacion,
    IReadOnlyList<FilaDeEstimacionDto> Estimacion,
    string ResumenDeEstimacion,
    IReadOnlyList<IndicadorDto> IndicadoresDeCapacidad,
    IReadOnlyList<FilaDeCapacidadDto> Capacidad,
    string ResumenDeCapacidad);
