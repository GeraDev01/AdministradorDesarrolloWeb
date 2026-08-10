using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Auth;
using AdminWeb.Shared.Dtos.Desempeno;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Los rankings de desempeño y la cola de revisión del líder.
///
/// Dos rutas de consulta y dos políticas distintas, y esa separación es la que sostiene la
/// confidencialidad: el ranking completo —con el desglose de premios, penalizaciones y número de
/// entradas de cada persona— solo lo sirve <c>/ranking</c>, que exige administrador. Un desarrollador
/// entra por <c>/mi-panel</c>, que devuelve otro DTO donde ese detalle sencillamente no existe.
/// Recortarlo en la pantalla no serviría: el cliente corre en la máquina de cada quien.
///
/// <para>A eso se suma ahora la ESCRITURA del líder: aprobar, rechazar y ajustar el puntaje de las
/// autocalificaciones pendientes. Todas exigen <c>SoloAdmin</c> y todas vuelven a exigirlo dentro de
/// <see cref="RevisionDePuntosService"/>: dos barreras, no una.</para>
///
/// <para>Los bytes de la captura que respalda una entrada NO salen por aquí, sino por
/// <c>/api/autocalificacion/entradas/{id}/captura</c>, que ya sirve esa imagen y ya comprueba que
/// quien pregunta es el dueño o un administrador. Una segunda ruta para lo mismo sería una segunda
/// comprobación que mantener al día.</para>
/// </summary>
public static class DesempenoEndpoints
{
    public static void MapDesempenoEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/desempeno").WithTags("Desempeño");

        grupo.MapGet("/ranking", async (
            int? anio, int? mes, bool? incluirNivelLead,
            DesempenoQueryService consultas, CancellationToken ct) =>
        {
            var (periodo, error) = NormalizarPeriodo(anio, mes);
            if (error != null) return Results.BadRequest(new ResultadoDto(false, error));

            var reporte = await consultas.RankingAdminAsync(
                periodo.anio, periodo.mes, incluirNivelLead ?? false, ct);
            return Results.Ok(reporte);
        })
        .RequireAuthorization("SoloAdmin")
        .WithSummary("Ranking individual y por equipo de un mes (administrador)");

        // Sin id en la ruta a propósito: el panel es el de quien pregunta, y lo resuelve el servicio
        // desde la identidad de la petición. Un id por parámetro convertiría esto en «el panel de
        // cualquiera» en cuanto alguien probara otro número.
        grupo.MapGet("/mi-panel", async (
            int? anio, int? mes, DesempenoQueryService consultas, CancellationToken ct) =>
        {
            var (periodo, error) = NormalizarPeriodo(anio, mes);
            if (error != null) return Results.BadRequest(new ResultadoDto(false, error));

            return Results.Ok(await consultas.MiPanelAsync(periodo.anio, periodo.mes, ct));
        })
        .RequireAuthorization("AdminUDesarrollador")
        .WithSummary("KPIs y rankings recortados del desarrollador que consulta");

        // ── Cola de revisión del líder ───────────────────────────────────────────
        //
        // No llevan período en la ruta y no es un descuido: lo pendiente es pendiente venga del mes
        // que venga, y filtrarlo por el mes que se está mirando en el ranking escondería justo lo que
        // más tiempo lleva esperando respuesta. Es lo que hacía la pestaña del escritorio.

        grupo.MapGet("/pendientes", async (RevisionDePuntosService revision, CancellationToken ct) =>
            Results.Ok(await revision.PendientesAsync(ct)))
        .RequireAuthorization("SoloAdmin")
        .WithSummary("Autocalificaciones esperando aprobación, con su evidencia");

        grupo.MapPost("/pendientes/aprobar", async (
            AprobarPuntosRequest cuerpo, RevisionDePuntosService revision, CancellationToken ct) =>
        {
            var (ok, mensaje) = await revision.AprobarAsync(cuerpo.Ids, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization("SoloAdmin")
        .WithSummary("Aprueba una tanda de autocalificaciones; sus puntos cuentan de inmediato");

        grupo.MapPost("/pendientes/rechazar", async (
            RechazarPuntosRequest cuerpo, RevisionDePuntosService revision, CancellationToken ct) =>
        {
            // El motivo vacío llega hasta el servicio a propósito: es él quien explica por qué hace
            // falta —«es lo que la persona va a leer para corregirlo»—, y ese texto es el que se ve.
            var (ok, mensaje) = await revision.RechazarAsync(cuerpo.Ids, cuerpo.Motivo, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization("SoloAdmin")
        .WithSummary("Rechaza una tanda de autocalificaciones con un motivo obligatorio");

        grupo.MapPost("/pendientes/{id:int}/puntos", async (
            int id, AjustarPuntosRequest cuerpo, RevisionDePuntosService revision, CancellationToken ct) =>
        {
            var (ok, mensaje) = await revision.AjustarPuntosAsync(id, cuerpo.Puntos, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization("SoloAdmin")
        .WithSummary("Corrige el puntaje de una entrada que sigue pendiente (no la aprueba)");
    }

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto —«alguien las revisó antes que tú»—; eso es lo que se
    /// enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));

    /// <summary>
    /// Período por omisión: el mes en curso, que es con el que se abre la pantalla. Se valida aquí y
    /// no en el servicio porque es un dato de la petición: un mes 13 es una petición mal formada,
    /// no una situación de negocio.
    /// </summary>
    private static ((int anio, int mes) periodo, string? error) NormalizarPeriodo(int? anio, int? mes)
    {
        var hoy = DateTime.UtcNow;
        int a = anio ?? hoy.Year;
        int m = mes ?? hoy.Month;

        if (m is < 1 or > 12) return ((0, 0), "El mes debe estar entre 1 y 12.");
        if (a is < 2000 or > 2100) return ((0, 0), "El año está fuera de rango.");

        return ((a, m), null);
    }
}
