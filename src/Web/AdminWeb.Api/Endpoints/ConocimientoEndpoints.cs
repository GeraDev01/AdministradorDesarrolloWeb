using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Conocimiento;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// La base de conocimiento: escribirla, revisarla y buscar en ella.
///
/// <para><b>El grupo NO lleva política de rol, y es deliberado.</b> Es el único módulo del sistema
/// donde eso es correcto: leer un artículo PUBLICADO es de cualquiera con sesión —Operaciones
/// incluida—, porque esto es documentación de trabajo y no la conversación del equipo de desarrollo.
/// Dejar fuera a quien despliega los sistemas sería quedarse sin lo que más falta hace escribir. La
/// política de sesión iniciada sigue puesta: la trae el <c>SetFallbackPolicy</c> de la aplicación,
/// así que aquí no hay nada abierto, solo nada MÁS restringido.</para>
///
/// <para>Lo que sí lleva política es cada endpoint que hace algo más que leer lo público:
/// <c>AdminUDesarrollador</c> para escribir y <c>SoloAdmin</c> para revisar. Y los dos filtros están
/// además <b>dentro de <see cref="ConocimientoService"/></b>, que es donde está el dato: a un
/// servicio se le puede llamar desde otro endpoint que nazca abierto sin que quien lo escriba se dé
/// cuenta.</para>
///
/// <para><b>Quién ve qué NO se decide aquí.</b> Que un borrador ajeno no se lea, que lo devuelto sea
/// cosa de su autor y del líder, y que un artículo pague puntos una sola vez, lo comprueba el
/// servicio sobre la fila de verdad. Repetirlo en el endpoint solo crearía un segundo sitio donde
/// equivocarse; lo que sí hace el endpoint es traducir el rechazo a un 400 con el mensaje que
/// escribió el servicio, que explica el motivo concreto.</para>
///
/// <para><b>El testigo antifalsificación hace falta en UNA sola ruta</b>, la de subir una imagen, y
/// es la única multipart de todo el grupo. El resto va en JSON, y un cuerpo JSON no se puede
/// provocar desde otra página —obliga al navegador a preguntar antes y no hay política CORS que lo
/// permita—; un formulario multipart sí llegaría aquí con la cookie de sesión puesta. La cookie es
/// SameSite=Strict y eso ya lo frena, pero el testigo es la barrera que no depende de que un
/// navegador implemente bien SameSite. Lo exige <c>UseAntiforgery</c> por el solo hecho de que la
/// ruta lea un formulario, y lo adjunta <c>ClienteApi.SubirAsync</c> sin que la pantalla tenga que
/// acordarse. <b>No le pongas <c>DisableAntiforgery</c> para que «funcione» desde una herramienta de
/// pruebas.</b></para>
/// </summary>
public static class ConocimientoEndpoints
{
    public static void MapConocimientoEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/conocimiento").WithTags("Conocimiento");

        // ── Leer y buscar ────────────────────────────────────────────────────────

        grupo.MapGet("/", async (
            ConocimientoService conocimiento,
            CancellationToken ct,
            string? texto = null, string? etiqueta = null, int? estado = null,
            bool soloMios = false,
            int pagina = 1, int tamano = ConocimientoService.TamanoPaginaPorOmision) =>
        {
            var filtro = new ConocimientoFiltro(texto, etiqueta, AEstado(estado), soloMios);
            return Results.Ok(await conocimiento.BuscarAsync(filtro, pagina, tamano, ct));
        })
        .WithSummary("Busca entre la documentación y el glosario, paginado");

        grupo.MapGet("/etiquetas", async (ConocimientoService conocimiento, CancellationToken ct) =>
            Results.Ok(await conocimiento.EtiquetasAsync(ct)))
        .WithSummary("Las etiquetas de lo publicado con cuántos artículos lleva cada una");

        // Los contadores de lo pendiente. Los pide el menú en cada carga, así que va antes de la
        // ruta con {id} — si no, «pendientes» entraría por ahí y respondería 400 por no ser un
        // número. (El enrutamiento de ASP.NET prefiere el literal, pero el orden lo deja explícito.)
        grupo.MapGet("/pendientes", async (ConocimientoService conocimiento, CancellationToken ct) =>
            Results.Ok(await conocimiento.PendientesAsync(ct)))
        .WithSummary("Cuántos artículos esperan revisión y cuántos me devolvieron a mí");

        grupo.MapGet("/cola", async (ConocimientoService conocimiento, CancellationToken ct) =>
            Results.Ok(await conocimiento.ColaDeRevisionAsync(ct)))
        .RequireAuthorization("SoloAdmin")
        .WithSummary("La cola de revisión, con lo que lleva más tiempo esperando arriba");

        grupo.MapGet("/criterios", async (ConocimientoService conocimiento, CancellationToken ct) =>
            Results.Ok(await conocimiento.CriteriosParaOtorgarAsync(ct)))
        .RequireAuthorization("SoloAdmin")
        .WithSummary("Criterios del catálogo con los que otorgar puntos al aprobar");

        grupo.MapGet("/{id:int}", async (int id, ConocimientoService conocimiento, CancellationToken ct) =>
        {
            var articulo = await conocimiento.LeerAsync(id, ct);
            // 404 y no una respuesta vacía: a un artículo se llega por una dirección que alguien
            // pudo compartir, y hay que poder distinguir «no existe» de «existe y está vacío».
            //
            // El mismo 404 para «no existe» y para «no te toca verlo», a propósito: un 403 sobre un
            // borrador ajeno confirmaría que ese artículo existe, que es justo lo que un borrador no
            // debe revelar.
            return articulo == null
                ? Results.NotFound(new { Detail = "Ese artículo no existe o no está disponible para ti." })
                : Results.Ok(articulo);
        })
        .WithSummary("Un artículo completo, con el cuerpo ya analizado en bloques");

        // ── Escribir ─────────────────────────────────────────────────────────────

        grupo.MapPost("/", async (
            ConocimientoEscrituraDto cuerpo, ConocimientoService conocimiento, CancellationToken ct) =>
        {
            var (ok, mensaje, articulo) = await conocimiento.CrearAsync(
                cuerpo.Titulo, cuerpo.Cuerpo, cuerpo.Etiquetas, ct);

            return ok && articulo != null
                ? Results.Ok(new ConocimientoCreadoDto(articulo.Id, mensaje))
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .RequireAuthorization("AdminUDesarrollador")
        .WithSummary("Crea un artículo en borrador");

        grupo.MapPut("/{id:int}", async (
            int id, ConocimientoEscrituraDto cuerpo, ConocimientoService conocimiento, CancellationToken ct) =>
        {
            var (ok, mensaje) = await conocimiento.EditarAsync(id, cuerpo.Titulo, cuerpo.Cuerpo, cuerpo.Etiquetas, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization("AdminUDesarrollador")
        .WithSummary("Cambia el texto de un artículo (si estaba publicado, vuelve a la cola)");

        grupo.MapPost("/{id:int}/enviar", async (
            int id, ConocimientoService conocimiento, CancellationToken ct) =>
        {
            var (ok, mensaje) = await conocimiento.EnviarARevisionAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization("AdminUDesarrollador")
        .WithSummary("Manda el artículo a la cola del líder");

        // ── Imágenes del artículo ────────────────────────────────────────────────
        //
        // En dos tiempos y no en el mismo envío que el texto: para que el cuerpo pueda NOMBRAR una
        // imagen hace falta que ya tenga número. Así el cuerpo sigue sin llevar una sola dirección
        // escrita por una persona, que es de donde salen los agujeros de esta clase de pantalla.
        //
        // Una imagen por llamada, y por eso la parte se llama «imagen» en singular: es lo que hace
        // el editor cada vez que alguien pega una captura, y le devuelve la marca ya armada para
        // meterla en el texto en ese mismo gesto.

        grupo.MapPost("/{id:int}/imagenes", async (
            int id, IFormCollection formulario, ConocimientoService conocimiento, CancellationToken ct) =>
        {
            var archivo = formulario.Files.GetFile("imagen");
            if (archivo == null)
                return Results.BadRequest(new ResultadoDto(false, "No llegó ninguna imagen."));

            // El peso se mira ANTES de leer nada: cargar en memoria lo que el servicio va a rechazar
            // después sería regalar una forma barata de tumbar el servidor. La comprobación que
            // cuenta —que los bytes sean de verdad una imagen— sigue estando en el servicio, que es
            // por donde pasa todo lo que se guarda.
            if (archivo.Length > ConocimientoService.MaxBytesImagen)
                return Results.BadRequest(new ResultadoDto(false,
                    $"«{archivo.FileName}» pesa {ForumMedia.Tamano(archivo.Length)} y el tope por imagen es " +
                    $"{ForumMedia.Tamano(ConocimientoService.MaxBytesImagen)}."));

            using var memoria = new MemoryStream();
            await archivo.CopyToAsync(memoria, ct);

            var (ok, mensaje, imagen) = await conocimiento.GuardarImagenAsync(
                id, archivo.FileName, memoria.ToArray(), ct);

            return ok && imagen != null
                ? Results.Ok(imagen)
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .RequireAuthorization("AdminUDesarrollador")
        .WithSummary("Sube una imagen al artículo y devuelve la marca con la que nombrarla");

        grupo.MapGet("/{id:int}/imagenes", async (
            int id, ConocimientoService conocimiento, CancellationToken ct) =>
            Results.Ok(await conocimiento.ImagenesDeAsync(id, ct)))
        .WithSummary("Las imágenes que ya tiene el artículo, sin los bytes (van por /api/adjuntos)");

        grupo.MapDelete("/{id:int}", async (
            int id, ConocimientoService conocimiento, CancellationToken ct) =>
        {
            var (ok, mensaje) = await conocimiento.EliminarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization("AdminUDesarrollador")
        .WithSummary("Borra un borrador o algo devuelto que nunca llegó a publicarse");

        // ── Revisar ──────────────────────────────────────────────────────────────
        //
        // Aprobar y otorgar puntos son UNA sola llamada, no dos. Separarlas dejaría el segundo paso a
        // merced de que alguien se acuerde, y unos puntos que llegan tres días tarde ya no premian
        // nada. El servicio se encarga de que un artículo no pueda pagar dos veces aunque esta ruta
        // se llame cien.

        grupo.MapPost("/{id:int}/aprobar", async (
            int id, ConocimientoAprobacionDto cuerpo, ConocimientoService conocimiento, CancellationToken ct) =>
        {
            var (ok, mensaje) = await conocimiento.AprobarAsync(id, cuerpo.CriterioId, cuerpo.Puntos, cuerpo.Nota, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization("SoloAdmin")
        .WithSummary("Publica el artículo y, si el líder quiere, otorga los puntos en la misma operación");

        grupo.MapPost("/{id:int}/rechazar", async (
            int id, ConocimientoRechazoDto cuerpo, ConocimientoService conocimiento, CancellationToken ct) =>
        {
            var (ok, mensaje) = await conocimiento.RechazarAsync(id, cuerpo.Motivo, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization("SoloAdmin")
        .WithSummary("Devuelve el artículo con un motivo, o retira uno ya publicado");
    }

    /// <summary>
    /// El estado llega como número y se comprueba contra el enum. Un valor que no existe se trata
    /// como «sin filtrar» en lugar de reventar: un parámetro mal escrito en una dirección compartida
    /// no debe devolver un error, solo la lista completa.
    /// </summary>
    private static KnowledgeStatus? AEstado(int? estado) =>
        estado is int e && Enum.IsDefined(typeof(KnowledgeStatus), e) ? (KnowledgeStatus)e : null;

    /// <summary>
    /// Un rechazo de negocio sale como 400 con SU mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo concreto —«solo su autor puede mandarlo a revisar», «este
    /// artículo ya otorgó puntos»—; eso es lo que se enseña, no un «no se pudo» genérico.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));
}
