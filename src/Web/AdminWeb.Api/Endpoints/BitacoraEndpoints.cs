using AdminWeb.Application.Services;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// La bitácora. SOLO el líder, y solo lectura: las entradas las escribe <c>AuditService</c> desde
/// las operaciones que registra, nunca un endpoint. Una bitácora con un POST público deja de ser
/// evidencia de nada.
///
/// Va PAGINADA sin excepción. Es la tabla que más crece del sistema y no hay ninguna ruta que
/// devuelva «todo»: si la hubiera, alguien la llamaría el día que la tabla tenga dos millones de
/// filas.
/// </summary>
public static class BitacoraEndpoints
{
    public static void MapBitacoraEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/bitacora")
            .WithTags("Bitácora")
            .RequireAuthorization("SoloAdmin");

        grupo.MapGet("/", async (
            DateTimeOffset? desde, DateTimeOffset? hasta, string? usuario, AuditAction? accion, string? texto,
            int? pagina, int? tamano,
            BitacoraQueryService bitacora, CancellationToken ct) =>
        {
            // Se reciben como DateTimeOffset y no como DateTime: un DateTime que llega por la
            // dirección pierde el huso según cómo lo interprete quien lo parsea, y una bitácora
            // desplazada seis horas es peor que una vacía, porque parece correcta. Con el desfase
            // explícito en la cadena, la conversión a UTC no depende de nada más.
            var pag = await bitacora.BuscarAsync(
                desde?.UtcDateTime, hasta?.UtcDateTime, usuario, accion, texto,
                pagina ?? 1,
                tamano ?? BitacoraQueryService.TamanoPaginaPorOmision,
                ct);

            return Results.Ok(pag);
        })
        .WithSummary("Una página de la bitácora, con filtros por fecha, usuario, acción y texto");

        grupo.MapGet("/opciones", async (BitacoraQueryService bitacora, CancellationToken ct) =>
            Results.Ok(await bitacora.OpcionesAsync(ct)))
        .WithSummary("Los usuarios que aparecen en la bitácora, para el filtro");
    }
}
