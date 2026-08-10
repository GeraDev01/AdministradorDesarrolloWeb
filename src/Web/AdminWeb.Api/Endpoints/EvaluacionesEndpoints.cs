using AdminWeb.Application.Services;
using AdminWeb.Domain.Security;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Evaluaciones;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Las evaluaciones de líder y los hitos de cada desarrollador, y la ficha en PDF que los reúne.
///
/// <b>Dos mitades con dos políticas, y la separación es lo que sostiene la confidencialidad.</b> Todo
/// lo que lleva un identificador de desarrollador en la ruta exige <c>SoloAdmin</c>: son las
/// debilidades que un líder escribió sobre una persona, y con un id en la ruta y sin política
/// cualquiera leería las de cualquiera probando números. Lo que un desarrollador puede pedir va por
/// <c>/mias</c>, SIN identificador: el servidor resuelve de quién es la ficha a partir de la sesión,
/// así que no hay nada que comprobar ni que olvidar.
///
/// <para>El PDF sale por las dos vías, cada una con su alcance: el líder puede pedir la ficha de
/// cualquiera y el desarrollador solo la suya. Además de la política, el servicio comprueba la
/// pertenencia con <c>RequireOwnershipOrAdmin</c>.</para>
/// </summary>
public static class EvaluacionesEndpoints
{
    /// <summary>Escribir evaluaciones e hitos, y leer la ficha de cualquiera, es del líder.</summary>
    private const string PoliticaDelLider = "SoloAdmin";

    /// <summary>Leer lo propio: el desarrollador, y el líder que también puede tener ficha.</summary>
    private const string PoliticaPropia = "AdminUDesarrollador";

    public static void MapEvaluacionesEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/evaluaciones").WithTags("Evaluaciones");

        // ── Lo propio (sin identificador en la ruta) ─────────────────────────────
        //
        // Va ANTES que «/{developerId:int}» por claridad de lectura; el enrutador no las confunde
        // porque aquella exige un entero y esta es una palabra.

        grupo.MapGet("/mias", async (EvaluacionesService evaluaciones, CancellationToken ct) =>
            Results.Ok(await evaluaciones.MisAsync(ct)))
        .RequireAuthorization(PoliticaPropia)
        .WithSummary("Mis evaluaciones e hitos, en solo lectura");

        grupo.MapGet("/mias/ficha", async (
            HttpContext ctx, FichaDeDesarrolladorQueryService fichas, ICurrentUser quien,
            CancellationToken ct) =>
        {
            if (quien.DeveloperId is not int devId) return SinFicha();

            var (pdf, nombre) = await fichas.FichaAsync(devId, ct);
            return pdf is null
                ? Results.NotFound(new ResultadoDto(false, nombre))
                : ResultadosDeArchivo.Adjunto(ctx, pdf, nombre);
        })
        .RequireAuthorization(PoliticaPropia)
        .WithSummary("Mi ficha en PDF");

        // ── Pantalla del líder ───────────────────────────────────────────────────

        grupo.MapGet("/desarrolladores", async (EvaluacionesService evaluaciones, CancellationToken ct) =>
            Results.Ok(await evaluaciones.DesarrolladoresAsync(ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Desarrolladores activos, para el selector de la pantalla");

        grupo.MapGet("/{developerId:int}", async (
            int developerId, EvaluacionesService evaluaciones, CancellationToken ct) =>
        {
            var ficha = await evaluaciones.DeDesarrolladorAsync(developerId, ct);
            return ficha is null
                ? Results.NotFound(new ResultadoDto(false, "El desarrollador no existe."))
                : Results.Ok(ficha);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Evaluaciones e hitos de un desarrollador");

        grupo.MapGet("/{developerId:int}/ficha", async (
            int developerId, HttpContext ctx, FichaDeDesarrolladorQueryService fichas,
            CancellationToken ct) =>
        {
            var (pdf, nombre) = await fichas.FichaAsync(developerId, ct);
            return pdf is null
                ? Results.NotFound(new ResultadoDto(false, nombre))
                : ResultadosDeArchivo.Adjunto(ctx, pdf, nombre);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("La ficha de un desarrollador en PDF");

        grupo.MapPost("/", async (
            GuardarEvaluacionRequest cuerpo, EvaluacionesService evaluaciones, CancellationToken ct) =>
        {
            var (ok, mensaje) = await evaluaciones.GuardarEvaluacionAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Alta o edición de una evaluación");

        grupo.MapPost("/{id:int}/eliminacion", async (
            int id, EvaluacionesService evaluaciones, CancellationToken ct) =>
        {
            var (ok, mensaje) = await evaluaciones.EliminarEvaluacionAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Elimina una evaluación");

        grupo.MapPost("/hitos", async (
            GuardarHitoRequest cuerpo, EvaluacionesService evaluaciones, CancellationToken ct) =>
        {
            var (ok, mensaje) = await evaluaciones.GuardarHitoAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Alta o edición de un hito");

        grupo.MapPost("/hitos/{id:int}/eliminacion", async (
            int id, EvaluacionesService evaluaciones, CancellationToken ct) =>
        {
            var (ok, mensaje) = await evaluaciones.EliminarHitoAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Elimina un hito");
    }

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto —«la calificación va de 1 a 5»—; eso es lo que se
    /// enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));

    /// <summary>
    /// La cuenta no está ligada a ninguna ficha de desarrollador. No es un fallo de permisos —la
    /// política ya la dejó pasar— sino de datos: la ficha se imprime de un desarrollador y esta
    /// cuenta no tiene ninguno.
    /// </summary>
    private static IResult SinFicha() =>
        Results.BadRequest(new ResultadoDto(false,
            "Tu cuenta no tiene ficha de desarrollador ligada, así que no hay ficha que imprimir. " +
            "Pídeselo al líder."));
}
