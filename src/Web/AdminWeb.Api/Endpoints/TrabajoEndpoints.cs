using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Trabajo;
using AdminWeb.Shared.Enums;
using Microsoft.AspNetCore.Mvc;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// El trabajo comprometido: los requerimientos, el sprint que los agrupa y lo que cada quien tiene
/// asignado.
///
/// <b>Tres alcances distintos y por eso tres políticas distintas</b>, que es la traducción exacta de
/// lo que el escritorio decidía escondiendo botones:
///
///  · Los REQUERIMIENTOS son del líder de punta a punta (<c>SoloAdmin</c>). Él compromete el alcance
///    y él responde por él.
///  · El SPRINT se LEE también desde la ficha del desarrollador (<c>AdminUDesarrollador</c>) y solo
///    lo ESCRIBE el líder. La mitad del valor de un sprint es que el equipo vea la misma verdad; uno
///    que solo ve el jefe genera la pregunta diaria de «¿cómo vamos?» que la pantalla venía a
///    eliminar. El histórico, en cambio, es del líder: es la base con la que se compromete el
///    siguiente sprint.
///  · MIS ASIGNACIONES es de quien tiene la sesión, y <b>su ruta no lleva identificador</b>, igual
///    que las de la jornada y el pool. Con un <c>/{devId}</c> habría que comprobar en cada endpoint
///    que es el propio, y el día que a uno se le olvidara sería «mira el trabajo de otra persona».
///
/// <b>El cronómetro de «Mis asignaciones» NO tiene endpoints aquí</b>, y no es un olvido: son los de
/// <c>/api/jornada/cronometro/*</c>, que ya arrancan, pausan y detienen contra un requerimiento. Hay
/// un solo cronómetro por persona —arrancar uno pausa el anterior—, así que duplicar las rutas por
/// pantalla sería fabricar dos puertas a la misma regla y esperar que nadie las desincronice.
/// </summary>
public static class TrabajoEndpoints
{
    /// <summary>Los requerimientos y el armado del sprint: del líder.</summary>
    private const string PoliticaDelLider = "SoloAdmin";

    /// <summary>Consultar el sprint y lo propio: del líder y del desarrollador.</summary>
    private const string PoliticaDelEquipo = "AdminUDesarrollador";

    public static void MapTrabajoEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/trabajo").WithTags("Trabajo");

        // ── Requerimientos ───────────────────────────────────────────────────────

        grupo.MapGet("/requerimientos", async (
            RequirementStatus? estado, int? desarrolladorId, string? buscar,
            TrabajoQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.RequerimientosAsync(estado, desarrolladorId, buscar, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Los requerimientos que cumplen el filtro, con los desarrolladores asignables");

        grupo.MapPost("/requerimientos/nuevo", async (
            GuardarRequerimientoRequest cuerpo, RequirementService servicio, CancellationToken ct) =>
        {
            var (ok, mensaje, _) = await servicio.CrearAsync(ADatos(cuerpo), ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Da de alta un requerimiento");

        grupo.MapPost("/requerimientos/{id:int}/editar", async (
            int id, GuardarRequerimientoRequest cuerpo, RequirementService servicio, CancellationToken ct) =>
        {
            // El sello viaja hasta el servicio y es él quien lo confronta. Si no cuadra, el guardado
            // lanza el choque de concurrencia y el filtro global lo convierte en 409: es la única
            // respuesta que pide algo distinto de reintentar —recargar antes de volver a guardar—.
            var (ok, mensaje) = await servicio.ActualizarAsync(id, ADatos(cuerpo), cuerpo.Sello, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Guarda los cambios de un requerimiento");

        grupo.MapPost("/requerimientos/{id:int}/cancelar", async (
            int id, CancelarRequerimientoRequest? cuerpo, RequirementService servicio, CancellationToken ct) =>
        {
            // «Cancelar» y no «eliminar», con toda intención: es lo único que hacía el botón de
            // borrar del escritorio, y el nombre de la ruta tiene que decir lo que de verdad pasa.
            var (ok, mensaje) = await servicio.CancelarAsync(id, cuerpo?.Sello, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Pasa el requerimiento a Cancelado (no lo borra)");

        grupo.MapPost("/requerimientos/{id:int}/asignar", async (
            int id, AsignarDesarrolladoresRequest cuerpo, RequirementService servicio, CancellationToken ct) =>
        {
            var (ok, mensaje) = await servicio.AsignarAsync(id, cuerpo.DesarrolladoresIds, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Deja el requerimiento exactamente con estos desarrolladores");

        // ── Documentos de un requerimiento ───────────────────────────────────────
        //
        // Los BYTES no salen por aquí: se piden a /api/adjuntos/requerimiento/{id}, que es por donde
        // sale todo lo guardado en la base y donde ya está resuelto lo delicado (tipo por contenido,
        // nosniff y nombre limpio).

        grupo.MapGet("/requerimientos/{id:int}/adjuntos", async (
            int id, RequirementAttachmentService adjuntos, CancellationToken ct) =>
            Results.Ok(await adjuntos.DeRequerimientoAsync(id, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Los documentos que cuelgan del requerimiento, sin su contenido");

        // Declara IFormFile a propósito: eso es lo que marca el endpoint como formulario y hace que el
        // middleware de antiforgery le exija su testigo. Un cuerpo JSON obliga al navegador a
        // preguntar antes y sin política CORS no pasa, pero un formulario multipart desde otro sitio
        // sí llegaría con la cookie puesta. El testigo lo adjunta ClienteApi.SubirAsync.
        //
        // El TIPO viaja como campo del formulario y no en la ruta: es un dato del documento que se
        // sube, igual que sus bytes, y en la ruta obligaría a inventar un segmento por cada valor del
        // enum el día que aparezca uno nuevo.
        grupo.MapPost("/requerimientos/{id:int}/adjuntos", async (
            int id, IFormFile archivo, [FromForm] RequirementAttachmentKind tipo,
            RequirementAttachmentService adjuntos, CancellationToken ct) =>
        {
            // El tamaño se mira ANTES de copiar: validar después ya habría traído el archivo entero a
            // la memoria de la API, que es justo lo que se quiere evitar de una subida desmedida.
            if (archivo.Length > ArchivosSubidos.MaxBytes)
                return Resultado(false, $"El documento pasa de {ArchivosSubidos.MaxBytes / (1024 * 1024)} MB.");

            using var memoria = new MemoryStream();
            await archivo.CopyToAsync(memoria, ct);
            var contenido = memoria.ToArray();

            // La comprobación que cuenta se hace AQUÍ, en el servidor: a esta ruta se puede llamar sin
            // pasar por el navegador. Se admite cualquier documento —un requerimiento suele venir en
            // PDF o .docx y una estimación en hoja de cálculo—; lo peligroso lo descarta
            // ArchivosSubidos con su lista de extensiones.
            var (valido, error, nombreSeguro) = ArchivosSubidos.Validar(archivo.FileName, contenido);
            if (!valido) return Resultado(false, error);

            var (ok, mensaje, _) = await adjuntos.AgregarAsync(id, tipo, nombreSeguro, contenido, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Cuelga del requerimiento un documento de requerimiento o de estimación");

        // Eliminar va por POST y no por DELETE por lo mismo que en el resto de la API: el único camino
        // del cliente para llamar con efecto —ClienteApi— habla GET y POST.
        grupo.MapPost("/adjuntos/{id:int}/eliminacion", async (
            int id, RequirementAttachmentService adjuntos, CancellationToken ct) =>
        {
            var (ok, mensaje) = await adjuntos.EliminarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Quita un documento de un requerimiento");

        // ── Sprint ───────────────────────────────────────────────────────────────

        grupo.MapGet("/sprints", async (TrabajoQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.SprintsAsync(ct)))
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Los sprints, el más reciente primero");

        // Antes que la ruta con identificador no hace falta ordenarlas: la restricción :int impide
        // que «historico» entre por ahí.
        grupo.MapGet("/sprints/historico", async (TrabajoQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.HistoricoAsync(ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Cómo salió cada sprint y la velocidad del equipo");

        grupo.MapGet("/sprints/{id:int}", async (
            int id, TrabajoQueryService consultas, CancellationToken ct) =>
            await consultas.SeguimientoAsync(id, ct) is { } seguimiento
                ? Results.Ok(seguimiento)
                : NoExiste("Ese sprint ya no existe. Actualiza la lista."))
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("El seguimiento del sprint: avance y lo comprometido");

        grupo.MapGet("/sprints/{id:int}/candidatos", async (
            int id, TrabajoQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.CandidatosAsync(id, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Lo que se le puede colgar al sprint (sin sprint o ya suyo)");

        grupo.MapPost("/sprints/nuevo", async (
            GuardarSprintRequest cuerpo, SprintService servicio, CancellationToken ct) =>
        {
            var (ok, mensaje, _) = await servicio.CrearAsync(
                cuerpo.Nombre, cuerpo.Objetivo, cuerpo.Inicio, cuerpo.Fin, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Crea un sprint con sus fechas");

        grupo.MapPost("/sprints/{id:int}/editar", async (
            int id, GuardarSprintRequest cuerpo, SprintService servicio, CancellationToken ct) =>
        {
            var (ok, mensaje) = await servicio.ActualizarAsync(
                id, cuerpo.Nombre, cuerpo.Objetivo, cuerpo.Inicio, cuerpo.Fin, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Cambia el nombre, el objetivo o las fechas del sprint");

        grupo.MapPost("/sprints/{id:int}/eliminar", async (
            int id, SprintService servicio, CancellationToken ct) =>
        {
            // Aquí eliminar sí borra el sprint, pero NO sus requerimientos: el servicio los desliga y
            // vuelven al backlog. Es lo que dice su mensaje, y por eso se enseña tal cual.
            var (ok, mensaje) = await servicio.EliminarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Elimina el sprint y devuelve sus requerimientos al backlog");

        grupo.MapPost("/sprints/{id:int}/requerimientos", async (
            int id, FijarRequerimientosRequest cuerpo, SprintService servicio, CancellationToken ct) =>
        {
            var (ok, mensaje) = await servicio.FijarRequerimientosAsync(id, cuerpo.RequerimientoIds, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Deja el sprint exactamente con estos requerimientos");

        // ── Mis asignaciones ─────────────────────────────────────────────────────

        grupo.MapGet("/mis-asignaciones", async (
            RequirementStatus? estado, TrabajoQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.MisAsignacionesAsync(estado, ct)))
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Mis requerimientos con el tiempo dedicado y el cronómetro en marcha");
    }

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto —«ese requerimiento ya no existe», «ya estaba
    /// cancelado»—; eso es lo que se enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));

    /// <summary>
    /// Lo que se pidió ya no está. Va con el mismo contrato que los rechazos para que el cliente lo
    /// lea por el mismo camino y enseñe el texto en vez de un «no se pudo» genérico.
    /// </summary>
    private static IResult NoExiste(string mensaje) =>
        Results.NotFound(new ResultadoDto(false, mensaje));

    /// <summary>
    /// Lo que el formulario captura, tal como lo espera el servicio.
    ///
    /// Deliberadamente sin origen, sin URL externa y sin los segundos ya reportados a DevOps: esos
    /// los pone la integración y no una persona, y aceptarlos aquí permitiría que un requerimiento
    /// capturado a mano se hiciera pasar por uno importado.
    /// </summary>
    private static RequirementService.DatosDeRequerimiento ADatos(GuardarRequerimientoRequest c) => new(
        c.Titulo, c.Detalle, c.Estado, c.Prioridad, c.HorasEstimadas,
        c.FechaSolicitud, c.FechaCompromiso, c.FechaEntrega, c.AvancePct);
}
