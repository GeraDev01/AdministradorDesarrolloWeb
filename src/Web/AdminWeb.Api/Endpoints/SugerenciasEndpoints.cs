using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Sugerencias;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Sugerencias del equipo: enviarlas, darles seguimiento y apoyar las de los demás.
///
/// Todo lo de aquí es de <b>cualquiera con sesión</b> y actúa sobre lo suyo, así que basta con la
/// política de respaldo de la aplicación. Lo del administrador —listar todas y responderlas— no vive
/// en este archivo: es otra pantalla y otra puerta.
///
/// <para><b>Ninguna ruta lleva identificador de usuario</b>, por lo mismo que en la jornada: «mis
/// sugerencias» son las de quien tiene la cookie, leídas por el servicio. Una ruta con
/// <c>/{userId}</c> obligaría a comprobar en cada endpoint que ese identificador es el propio, y el
/// día que se olvidara serían las de otra persona.</para>
///
/// <para><b>EL ANONIMATO SE RESUELVE AQUÍ.</b> Es la razón de que estos DTOs se armen a mano en vez
/// de devolver lo que da el servicio: <see cref="Suggestion"/> trae dentro el autor y su ficha
/// SIEMPRE, también cuando la sugerencia es anónima —los necesita para avisarle cuando la
/// contesten—. Mandar eso al navegador y confiar en que la pantalla no lo pinte sería regalarlo:
/// cualquiera que mire la respuesta de la API leería el nombre igual. En el escritorio el dato ni
/// salía del proceso; aquí cruza una red, y por eso el nombre no se pone cuando
/// <see cref="Suggestion.Anonymous"/> está encendido.</para>
/// </summary>
public static class SugerenciasEndpoints
{
    public static void MapSugerenciasEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/sugerencias").WithTags("Sugerencias");

        grupo.MapGet("/mias", async (
            SuggestionService sugerencias, ICurrentUser quien, CancellationToken ct) =>
        {
            // Las mías y las del equipo van juntas y en una sola respuesta porque son las dos
            // pestañas de la MISMA pantalla: pedirlas por separado la dejaría medio pintada mientras
            // llega la segunda.
            var mias = await sugerencias.MiasAsync(ct);
            var equipo = await sugerencias.EquipoAsync(ct);
            var votos = await sugerencias.ContarVotosAsync(ct);
            int yo = quien.UserId ?? -1;

            return Results.Ok(new SugerenciasDto(
                mias.Select(s => AMia(s, votos.TryGetValue(s.Id, out var n) ? n : 0)).ToList(),
                equipo.Select(e => DelEquipo(e, yo)).ToList(),
                Categorias(),
                Visibilidades()));
        })
        .WithSummary("Las sugerencias propias, el tablero del equipo y los desplegables, de una vez");

        grupo.MapPost("/nueva", async (
            NuevaSugerenciaRequest cuerpo, SuggestionService sugerencias, CancellationToken ct) =>
        {
            var (ok, mensaje, _) = await sugerencias.EnviarAsync(
                cuerpo.Categoria, cuerpo.Titulo, cuerpo.Cuerpo, cuerpo.Anonima,
                cuerpo.Visibilidad, cuerpo.AbiertaAVotacion, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Envía una sugerencia nueva");

        grupo.MapPost("/{id:int}/voto", async (
            int id, SuggestionService sugerencias, CancellationToken ct) =>
        {
            var (ok, votado, total) = await sugerencias.VotarAsync(id, ct);

            // El servicio rechaza sin texto porque el motivo es siempre el mismo: la propuesta ya no
            // está, o su autor no la abrió a votación. El mensaje se escribe aquí para que no salga
            // un «no se pudo» pelado que no le dice a nadie qué pasó.
            return ok
                ? Results.Ok(new VotoDto(votado, total))
                : Results.BadRequest(new ResultadoDto(false,
                    "Esa propuesta ya no admite votos. Actualiza la lista."));
        })
        .WithSummary("Apoya una propuesta del equipo o retira el apoyo");

        // Eliminar va por POST y no por DELETE, que sería lo propio: el único camino del cliente para
        // llamar a la API con efecto —ClienteApi— habla GET y POST. Un DELETE que nadie puede invocar
        // no haría la API más correcta.
        grupo.MapPost("/{id:int}/eliminar", async (
            int id, SuggestionService sugerencias, CancellationToken ct) =>
        {
            var (ok, mensaje) = await sugerencias.EliminarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Elimina una sugerencia propia mientras siga «Nueva»");
    }

    // ── De entidad a DTO ─────────────────────────────────────────────────────────

    /// <summary>
    /// Una sugerencia propia. No lleva autor porque esta lista es la del usuario que pregunta: el
    /// autor es él, y mandárselo de vuelta sería una copia de un dato que ya tiene.
    /// </summary>
    private static MiSugerenciaDto AMia(Suggestion s, int votos) => new(
        s.Id,
        s.Title,
        s.Body,
        s.Category,
        SuggestionService.EtiquetaCategoria(s.Category),
        s.Status,
        SuggestionService.EtiquetaEstado(s.Status),
        SuggestionService.EtiquetaAlcance(s),
        s.Anonymous,
        s.SePuedeVotar,
        votos,
        s.AdminResponse,
        s.CreatedAt,
        s.ReviewedAt,
        SuggestionService.PuedeEliminar(s.Status));

    /// <summary>
    /// Una propuesta del tablero del equipo. El autor solo viaja cuando la sugerencia NO es anónima;
    /// si lo es, el nombre no sale del servidor.
    /// </summary>
    private static PropuestaDelEquipoDto DelEquipo(SuggestionConVotos e, int yo) => new(
        e.Sug.Id,
        e.Sug.Title,
        e.Sug.Body,
        e.Sug.Category,
        SuggestionService.EtiquetaCategoria(e.Sug.Category),
        e.Sug.Status,
        SuggestionService.EtiquetaEstado(e.Sug.Status),
        e.Sug.Anonymous ? null : e.Sug.Developer?.FullName,
        e.Sug.Anonymous,
        e.Sug.CreatedByUserId == yo,
        e.Sug.SePuedeVotar,
        e.Votos,
        e.YoVote,
        // La respuesta del líder sale también en el tablero: es la decisión del escritorio, donde
        // «👁 Ver» abría la misma ficha para una propuesta propia que para una del equipo. Tiene
        // sentido — lo que se contesta sobre una propuesta pública es información del equipo, y sin
        // ella el tablero enseñaría un «Rechazada» sin decir por qué.
        e.Sug.AdminResponse,
        e.Sug.CreatedAt);

    // ── Desplegables ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Las categorías se arman desde el enum y no desde una lista escrita a mano: así, el día que se
    /// añada una, aparece sola en la pantalla en lugar de quedarse fuera sin que nadie se entere.
    /// </summary>
    private static IReadOnlyList<OpcionDto> Categorias() =>
        Enum.GetValues<SuggestionCategory>()
            .Select(c => new OpcionDto((int)c, SuggestionService.EtiquetaCategoria(c)))
            .ToList();

    private static IReadOnlyList<OpcionDto> Visibilidades() =>
        Enum.GetValues<SuggestionVisibility>()
            .Select(v => new OpcionDto((int)v, SuggestionService.EtiquetaVisibilidad(v)))
            .ToList();

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto —«ya no puedes eliminarla: el líder empezó a
    /// atenderla»—; eso es lo que se enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));
}
