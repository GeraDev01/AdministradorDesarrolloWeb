using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Administracion;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// El centro de reportes. Solo el líder. Casi todo es GET —un reporte no cambia nada, se pide— con
/// una excepción: mandarlo por correo, que sí tiene efecto fuera y por eso va por POST.
///
/// <b>El período viaja como dos días sueltos</b> (<c>DateOnly</c>) y no como instantes con huso. Es
/// deliberado: lo que se elige en la pantalla son dos fechas de calendario, exactamente como los dos
/// selectores del escritorio, y los reportes las interpretan igual que él. Mandar un instante daría
/// la falsa impresión de una precisión que el cálculo no tiene.
///
/// <para>Los desarrolladores se filtran con una lista de identificadores separados por coma. Lista
/// vacía significa «todos», que es lo que hacía el «(Todos)» de aquel desplegable.</para>
/// </summary>
public static class ReportesEndpoints
{
    public static void MapReportesEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/reportes")
            .WithTags("Reportes")
            .RequireAuthorization("SoloAdmin");

        grupo.MapGet("/", async (ReportesService reportes, CancellationToken ct) =>
            Results.Ok(await reportes.CatalogoAsync(ct)))
        .WithSummary("Qué reportes hay y a quién se puede filtrar");

        grupo.MapGet("/{clave}", async (
            string clave, DateOnly? desde, DateOnly? hasta, string? devs,
            ReportesService reportes, CancellationToken ct) =>
        {
            var (d, h) = Periodo(desde, hasta);
            var reporte = await reportes.GenerarAsync(clave, d, h, Identificadores(devs), ct);

            return reporte is null
                ? Results.NotFound(new ResultadoDto(false, "Ese reporte no existe."))
                : Results.Ok(reporte);
        })
        .WithSummary("Genera un reporte: tabla, cifras y barras");

        grupo.MapGet("/{clave}/excel", async (
            string clave, DateOnly? desde, DateOnly? hasta, string? devs,
            ReportesService reportes, CancellationToken ct) =>
        {
            var (d, h) = Periodo(desde, hasta);
            var archivo = await reportes.ExcelAsync(clave, d, h, Identificadores(devs), ct);

            return archivo is not { } a
                ? Results.NotFound(new ResultadoDto(false, "Ese reporte no existe."))
                : ResultadosDeArchivo.Excel(a.contenido, a.nombre);
        })
        .WithSummary("El mismo reporte, en una hoja de cálculo");

        // El único POST del grupo, y no rompe la regla de arriba: lo que cambia no es el reporte
        // —que se sigue calculando igual— sino el mundo de fuera, porque sale un correo. Un GET que
        // manda correos es un GET que un precargador del navegador puede disparar solo.
        grupo.MapPost("/{clave}/correo", async (
            string clave, DateOnly? desde, DateOnly? hasta, string? devs,
            EnviarReportePorCorreoRequest cuerpo, ReportesService reportes, CancellationToken ct) =>
        {
            var (d, h) = Periodo(desde, hasta);
            var (ok, mensaje) = await reportes.EnviarPorCorreoAsync(
                clave, d, h, Identificadores(devs), cuerpo.Destinatarios, cuerpo.Nota, ct);

            // Sin correo configurado, sin destinatarios o con el SMTP rechazando el envío sale un 400
            // con el texto del servicio, que dice cuál de los tres fue. Un 500 obligaría a mirar el
            // registro del servidor para averiguar algo que se arregla en Configuración.
            return ok ? Results.Ok(new ResultadoDto(true, mensaje))
                      : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .WithSummary("Manda el reporte por correo con la hoja de cálculo adjunta");
    }

    /// <summary>
    /// El período, con el mismo valor por omisión que el escritorio: del primer día del mes en curso
    /// a hoy. Sin él, una llamada sin fechas recorrería el histórico completo.
    /// </summary>
    private static (DateTime desde, DateTime hasta) Periodo(DateOnly? desde, DateOnly? hasta)
    {
        var hoy = DateTime.Today;
        return (desde?.ToDateTime(TimeOnly.MinValue) ?? new DateTime(hoy.Year, hoy.Month, 1),
                hasta?.ToDateTime(TimeOnly.MinValue) ?? hoy);
    }

    /// <summary>
    /// Los identificadores de la lista separada por coma. Lo que no sea un número se ignora en vez de
    /// tumbar la petición: el filtro es una comodidad, y un reporte que responde 400 porque sobró una
    /// coma no le sirve a nadie.
    /// </summary>
    private static List<int> Identificadores(string? lista) =>
        string.IsNullOrWhiteSpace(lista)
            ? []
            : lista.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Select(t => int.TryParse(t, out var n) ? n : 0)
                   .Where(n => n > 0)
                   .Distinct()
                   .ToList();
}
