using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Auth;
using AdminWeb.Shared.Dtos.Sla;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Reporte de cumplimiento de SLA, en solo lectura.
///
/// SoloAdmin, igual que en el escritorio: es la medida con la que se juzga al equipo y a cada
/// persona («cuántos de tus compromisos se vencieron»), y ponerla al alcance de cualquiera que haya
/// entrado convertiría un reporte de gestión en un tablero de comparaciones entre compañeros.
/// </summary>
public static class CumplimientoSlaEndpoints
{
    public static void MapCumplimientoSlaEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/cumplimiento-sla").WithTags("Cumplimiento SLA");

        // Ruta con nombre («/reporte») y no la raíz del grupo: la raíz obliga a acertar con la barra
        // final al llamarla y no gana nada a cambio.
        grupo.MapGet("/reporte", async (
            DateOnly? desde, DateOnly? hasta, AgrupacionSla? agrupacion,
            SlaCumplimientoQueryService consultas, CancellationToken ct) =>
        {
            // Por omisión, el mes en curso: es como abre la pantalla del escritorio.
            var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
            var d = desde ?? new DateOnly(hoy.Year, hoy.Month, 1);
            var h = hasta ?? hoy;

            var (ok, error, reporte) = await consultas.CalcularAsync(
                d, h, agrupacion ?? AgrupacionSla.Desarrollador, ct);

            // 400 y no una lista vacía: un período al revés es un dato equivocado que hay que
            // corregir, y devolver «no hay nada» lo haría pasar por un período sin compromisos.
            return ok ? Results.Ok(reporte) : Results.BadRequest(new ResultadoDto(false, error));
        })
        .RequireAuthorization("SoloAdmin")
        .WithSummary("Cumplimiento de SLA del período, agrupado");
    }
}
