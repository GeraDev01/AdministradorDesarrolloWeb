using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Notas;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Notas y pendientes del líder.
///
/// <b>Todo el grupo es del líder</b>: una nota puede recoger lo que alguien pidió en privado o algo
/// que hay que revisar de una persona, así que no hay aquí una sola ruta que tenga sentido abrir a
/// otro rol. La política del grupo es la primera barrera; la segunda es
/// <c>AuthorizationGuard.RequireAdmin</c> dentro del servicio, porque a esta API se la puede llamar
/// sin pasar por el navegador.
///
/// <para>Van en su propio archivo y no dentro de <see cref="AdministracionEndpoints"/> porque aquel
/// grupo es la administración del ÁREA —configuración, limpieza y minutas—, y esto es un módulo con
/// su propia entidad y su propio ciclo de vida. Meterlo ahí obligaría a reescribir el resumen de
/// aquella clase para que dejara de ser cierto.</para>
/// </summary>
public static class NotasEndpoints
{
    public static void MapNotasEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/notas")
            .WithTags("Notas")
            .RequireAuthorization("SoloAdmin");

        grupo.MapGet("/", async (bool? soloPendientes, NotasService notas, CancellationToken ct) =>
            Results.Ok(await notas.ListarAsync(soloPendientes ?? false, ct)))
        .WithSummary("Las notas y pendientes, con las prioridades y los desarrolladores a quien atribuirlas");

        grupo.MapPost("/", async (GuardarNotaRequest cuerpo, NotasService notas, CancellationToken ct) =>
        {
            var (ok, mensaje, _) = await notas.GuardarAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Alta o edición de una nota");

        grupo.MapPost("/{id:int}/completar", async (
            int id, CompletarNotaRequest cuerpo, NotasService notas, CancellationToken ct) =>
        {
            var (ok, mensaje) = await notas.CompletarAsync(id, cuerpo.Completada, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Cierra o reabre una nota sin abrir el formulario");

        grupo.MapPost("/{id:int}/eliminar", async (int id, NotasService notas, CancellationToken ct) =>
        {
            var (ok, mensaje) = await notas.EliminarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Elimina una nota");

        grupo.MapGet("/excel", async (bool? soloPendientes, NotasService notas, CancellationToken ct) =>
            ResultadosDeArchivo.Excel(await notas.ExcelAsync(soloPendientes ?? false, ct), "Notas"))
        .WithSummary("Las notas del filtro, en una hoja de cálculo");
    }

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto; eso es lo que se enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));
}
