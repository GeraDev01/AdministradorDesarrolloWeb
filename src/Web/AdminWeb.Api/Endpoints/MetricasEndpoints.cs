using AdminWeb.Application.Services;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Las dos pantallas de planeación del líder: «Métricas» (antigüedad y ciclo de vida de los
/// requerimientos, carga y atrasos por persona) y «Estimación y capacidad» (precisión de las
/// estimaciones y a quién se le puede asignar lo siguiente).
///
/// <b>Las dos son del líder y las dos son de SOLO LECTURA.</b> No hay aquí ninguna ruta de escritura
/// porque no hay nada que capturar: todo se deriva de fechas y tiempos que ya existen. El grupo exige
/// <c>SoloAdmin</c> y <see cref="MetricasQueryService"/> vuelve a exigirlo — son datos de evaluación
/// de terceros («quién va atrasado», «quién está sobrecargado») y a la API se llega sin pasar por el
/// cliente.
/// </summary>
public static class MetricasEndpoints
{
    public static void MapMetricasEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/metricas")
            .WithTags("Métricas y planeación")
            .RequireAuthorization("SoloAdmin");

        grupo.MapGet("/", async (MetricasQueryService metricas, CancellationToken ct) =>
            Results.Ok(await metricas.MetricasAsync(ct)))
        .WithSummary("Indicadores, ciclo de vida por requerimiento y carga por desarrollador");

        // El número de días no se valida aquí sino que el servicio lo ACOTA: a diferencia de un mes
        // 13, un 5000 no es una petición mal formada, es una ventana absurda. Recortarla y responder
        // diciendo sobre cuántos días se contó (DiasDeVentana) es más útil que un 400.
        grupo.MapGet("/estimacion", async (
            int? dias, MetricasQueryService metricas, CancellationToken ct) =>
            Results.Ok(await metricas.EstimacionYCapacidadAsync(
                dias ?? MetricasQueryService.DiasDeVentanaPorDefecto, ct)))
        .WithSummary("Precisión de las estimaciones y capacidad del equipo");
    }
}
