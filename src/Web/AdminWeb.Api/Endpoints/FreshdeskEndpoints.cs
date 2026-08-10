using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Freshdesk;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// La mesa de ayuda vista desde aquí: los tickets que se traen de Freshdesk y los vínculos que los
/// atan a los work items de Azure DevOps.
///
/// <b>Todo es del líder (<c>SoloAdmin</c>)</b>, igual que en el escritorio. No es una precaución
/// genérica: sincronizar habla con una cuenta de Freshdesk usando una clave de API que compromete a
/// la empresa entera, y el filtro que decide qué se guarda es compartido —cambiarlo le cambia la
/// pantalla a todos—. Los vínculos son afirmaciones sobre el trabajo del equipo y por eso los firma
/// quien los hace.
///
/// <b>La clave de API no sale por ninguna de estas rutas</b>, ni siquiera enmascarada. Lo que se
/// puede saber es si la integración está utilizable y, cuando no lo está, qué falta configurar.
///
/// <b>Un fallo de la integración sale como 400 con su mensaje, no como 500.</b> «Freshdesk no aceptó
/// la clave» y «no se pudo conectar con la cuenta» son cosas que quien mira la pantalla puede
/// arreglar; un 500 le diría solo que algo se rompió.
/// </summary>
public static class FreshdeskEndpoints
{
    private const string PoliticaDelLider = "SoloAdmin";

    public static void MapFreshdeskEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Freshdesk ────────────────────────────────────────────────────────────
        var freshdesk = app.MapGroup("/api/freshdesk").WithTags("Freshdesk");

        freshdesk.MapGet("/", async (
            string? buscar, int? estado, int? prioridad,
            TicketsDeFreshdeskService tickets, CancellationToken ct) =>
            Results.Ok(await tickets.PantallaAsync(buscar, estado, prioridad, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Los tickets guardados que cumplen la búsqueda, con el resumen y el filtro de sincronización");

        freshdesk.MapPost("/sincronizar", async (
            TicketsDeFreshdeskService tickets, CancellationToken ct) =>
        {
            var (ok, mensaje, resultado) = await tickets.SincronizarAsync(ct);
            // En el éxito se devuelve el detalle (cuántos, y el aviso si el filtro no se pudo aplicar
            // del todo); en el rechazo, el mismo contrato que el resto de la API.
            return ok && resultado is not null
                ? Results.Ok(resultado)
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Trae los tickets de Freshdesk y deja la copia local al día");

        freshdesk.MapPost("/filtro", async (
            GuardarFiltroRequest cuerpo, TicketsDeFreshdeskService tickets, CancellationToken ct) =>
        {
            var (ok, mensaje) = await tickets.GuardarFiltroAsync(cuerpo.PorAgente, cuerpo.Grupo, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Guarda con qué criterio se sincroniza: asignados a mí y/o un grupo");

        // ── Vínculos de tickets ──────────────────────────────────────────────────
        //
        // En su propio grupo y no colgando de /api/freshdesk: un vínculo tiene dos extremos y el otro
        // es Azure DevOps. Meterlo debajo de Freshdesk daría a entender que es cosa suya.
        var vinculos = app.MapGroup("/api/vinculos").WithTags("Vínculos de tickets");

        vinculos.MapGet("/", async (
            string? buscar,
            string? doTexto, string? doEstado, string? doTipo, string? doAsignado, bool? doSinVincular,
            string? fdTexto, string? fdEstado, string? fdPrioridad, string? fdAgente, bool? fdSinVincular,
            VinculosDeTicketsService servicio, CancellationToken ct) =>
            Results.Ok(await servicio.PantallaAsync(
                new FiltroDeWorkItems(doTexto, doEstado, doTipo, doAsignado, doSinVincular ?? false),
                new FiltroDeTickets(fdTexto, fdEstado, fdPrioridad, fdAgente, fdSinVincular ?? false),
                buscar, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Los vínculos existentes y las dos listas desde las que se enlaza");

        vinculos.MapPost("/nuevo", async (
            CrearVinculoRequest cuerpo, VinculosDeTicketsService servicio, CancellationToken ct) =>
        {
            var (ok, mensaje) = await servicio.VincularAsync(cuerpo.WorkItemId, cuerpo.TicketId, cuerpo.Notas, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Ata un work item de DevOps con un ticket de Freshdesk");

        vinculos.MapPost("/{id:int}/quitar", async (
            int id, VinculosDeTicketsService servicio, CancellationToken ct) =>
        {
            // «Quitar» y no «eliminar»: lo que se deshace es la relación que alguien afirmó. Los dos
            // tickets siguen exactamente donde estaban, y el nombre de la ruta tiene que decirlo.
            var (ok, mensaje) = await servicio.QuitarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Deshace un vínculo (no toca ninguno de los dos tickets)");
    }

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto —«ya estaban vinculados», «falta el dominio en
    /// Configuración»—; eso es lo que se enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));
}
