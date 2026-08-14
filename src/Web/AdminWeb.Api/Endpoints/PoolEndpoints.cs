using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Pool;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// El pool de actividades: lo que el desarrollador toma, trabaja y entrega, y lo que el líder
/// publica, verifica y configura.
///
/// <b>Las rutas del desarrollador no llevan identificador suyo</b>, igual que las de la jornada.
/// Todas actúan sobre la ficha de quien tiene la sesión, leída de la cookie. Con un <c>/{devId}</c>
/// en la ruta habría que comprobar en cada endpoint que es el propio, y el día que a uno se le
/// olvidara sería «entrega la actividad de otra persona». El servicio conserva además su
/// comprobación de propiedad: dos barreras, no una.
///
/// Los puntos NO viajan en ninguna petición de escritura. Se leen de la matriz al publicar y quedan
/// congelados en la actividad; aceptar los que mandara el cliente sería regalar el sistema entero.
///
/// <b>Tampoco viaja el esfuerzo desde quien TOMA una tarea o un requerimiento</b>, ni desde el líder
/// cuando publica un bug: cada uno de esos números tiene un solo autor válido y el servicio rechaza
/// al otro en voz alta. Todo el pool se maneja en HORAS —plazo y esfuerzo— para que lo prometido y
/// lo que miden los cronómetros sean la misma unidad y se puedan restar.
/// </summary>
public static class PoolEndpoints
{
    /// <summary>Quien trabaja el pool: el desarrollador y el líder, que también puede tener ficha.</summary>
    private const string PoliticaDelPool = "AdminUDesarrollador";

    /// <summary>Publicar, verificar y configurar es del líder.</summary>
    private const string PoliticaDelLider = "SoloAdmin";

    public static void MapPoolEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/pool").WithTags("Pool de actividades");

        // ── Pantalla del desarrollador ───────────────────────────────────────────

        grupo.MapGet("/mio", async (
            PoolWorkType? tipo, PoolQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.MiPoolAsync(tipo, ct)))
        .RequireAuthorization(PoliticaDelPool)
        .WithSummary("Lo libre en el pool y lo que ya está a mi nombre, de una vez");

        // Con la política del pool y no con la del líder: lo consultan los dos, y son los mismos
        // puntos que la plantilla del tipo más los enlaces que capturó el propio interesado.
        grupo.MapGet("/{id:int}/checklist", async (
            int id, PoolQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.ChecklistAsync(id, ct)))
        .RequireAuthorization(PoliticaDelPool)
        .WithSummary("El checklist de una actividad, con su evidencia");

        // El cuerpo va OPCIONAL, igual que el motivo de devolver y liberar: quien toma una tarea o un
        // requerimiento no manda nada. En un bug sí hace falta —lleva la estimación de quien lo
        // toma—, y un cuerpo ausente llega hasta el servicio a propósito: es él quien explica por qué
        // un bug no se puede tomar sin estimarlo, y ese texto es el que ve la persona.
        grupo.MapPost("/{id:int}/tomar", async (
            int id, TomarActividadRequest? cuerpo, PoolActivityService pool, ICurrentUser quien,
            CancellationToken ct) =>
        {
            if (quien.DeveloperId is not int developerId) return SinFicha();
            var (ok, mensaje) = await pool.TomarAsync(id, developerId, cuerpo?.HorasEstimadas, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelPool)
        .WithSummary("Toma una actividad libre del pool (un bug, con la estimación de quien lo toma)");

        grupo.MapPost("/{id:int}/devolver", async (
            int id, MotivoRequest? cuerpo, PoolActivityService pool, ICurrentUser quien,
            CancellationToken ct) =>
        {
            if (quien.DeveloperId is not int developerId) return SinFicha();
            var (ok, mensaje) = await pool.DevolverAsync(id, developerId, cuerpo?.Motivo, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelPool)
        .WithSummary("Devuelve mi actividad al pool para que la tome cualquiera");

        grupo.MapPost("/{id:int}/entregar", async (
            int id, PoolActivityService pool, ICurrentUser quien, CancellationToken ct) =>
        {
            if (quien.DeveloperId is not int developerId) return SinFicha();
            var (ok, mensaje) = await pool.EntregarAsync(id, developerId, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelPool)
        .WithSummary("Entrega la actividad para que el líder la verifique");

        grupo.MapPost("/checklist/{itemId:int}/marcar", async (
            int itemId, MarcarPuntoRequest cuerpo, PoolActivityService pool, ICurrentUser quien,
            CancellationToken ct) =>
        {
            if (quien.DeveloperId is not int developerId) return SinFicha();
            var (ok, mensaje) = await pool.MarcarItemAsync(itemId, developerId, cuerpo.Hecho, cuerpo.Evidencia, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelPool)
        .WithSummary("Marca o desmarca un punto del checklist, con su evidencia");

        // ── Pantalla del líder ───────────────────────────────────────────────────

        grupo.MapGet("/lider", async (
            PoolActivityStatus? estado, PoolWorkType? tipo, PoolQueryService consultas,
            CancellationToken ct) =>
            Results.Ok(await consultas.PoolDelLiderAsync(estado, tipo, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("El pool completo y la cola de verificación");

        grupo.MapGet("/configuracion", async (PoolQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.ConfiguracionAsync(ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("La matriz de puntos y las plantillas de checklist");

        grupo.MapPost("/publicar", async (
            PublicarActividadRequest cuerpo, PoolActivityService pool, CancellationToken ct) =>
        {
            var (ok, mensaje, _) = await pool.CrearAsync(ABorrador(cuerpo), cuerpo.CriteriosExtra, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Publica una actividad en el pool con el valor que le da la matriz");

        grupo.MapPost("/{id:int}/editar", async (
            int id, PublicarActividadRequest cuerpo, PoolActivityService pool, CancellationToken ct) =>
        {
            var (ok, mensaje) = await pool.EditarAsync(id, ABorrador(cuerpo), cuerpo.CriteriosExtra, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Edita una actividad que sigue libre en el pool");

        grupo.MapGet("/criterios-disponibles", async (
            PoolQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.CriteriosExtraDisponiblesAsync(ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("El catálogo de criterios que se pueden pedir como extra en una actividad");

        // La política va DECLARADA y no heredada de la de respaldo. Sin ella, esta dirección caía en
        // aquella —la que atrapa lo que a nadie se le asignó— y la leía cualquier sesión: Operaciones
        // incluida, enumerando números. Es la única del grupo que se había quedado sin declararla, y
        // es la misma que su vecina de arriba y que el checklist, que es lo que lee un desarrollador
        // de su propia actividad.
        grupo.MapGet("/{id:int}/criterios", async (
            int id, PoolQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.CriteriosExtraDeAsync(id, ct)))
        .RequireAuthorization(PoliticaDelPool)
        .WithSummary("Los criterios extra de una actividad y en qué quedó cada uno");

        // La ruta cuelga del criterio y no de la actividad porque el identificador del criterio ya
        // es único: pedir los dos permitiría mandar un par que no se corresponde, y habría que
        // comprobarlo para no evaluar el criterio de otra actividad.
        grupo.MapPost("/criterios/{criterioId:int}/evaluar", async (
            int criterioId, EvaluarCriterioRequest cuerpo, PoolActivityService pool,
            CancellationToken ct) =>
        {
            var (ok, mensaje) = await pool.EvaluarCriterioExtraAsync(
                criterioId, cuerpo.Cumplido, cuerpo.Comentario, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Marca si un criterio extra se cumplió; solo los cumplidos suman al aceptar");

        grupo.MapPost("/{id:int}/retirar", async (
            int id, PoolActivityService pool, CancellationToken ct) =>
        {
            var (ok, mensaje) = await pool.RetirarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Quita del pool una actividad que ya no aplica");

        grupo.MapPost("/{id:int}/liberar", async (
            int id, MotivoRequest? cuerpo, PoolActivityService pool, CancellationToken ct) =>
        {
            var (ok, mensaje) = await pool.LiberarAsync(id, cuerpo?.Motivo, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Devuelve al pool una actividad que alguien tomó y no avanza");

        grupo.MapPost("/{id:int}/aceptar", async (
            int id, PoolActivityService pool, CancellationToken ct) =>
        {
            var (ok, mensaje) = await pool.AceptarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Acepta la entrega y abona los puntos congelados de la actividad");

        grupo.MapPost("/{id:int}/rechazar", async (
            int id, MotivoRequest? cuerpo, PoolActivityService pool, CancellationToken ct) =>
        {
            // El motivo vacío llega hasta el servicio a propósito: es él quien explica por qué hace
            // falta («es lo que la persona va a leer para corregirlo»), y ese texto es el que se ve.
            var (ok, mensaje) = await pool.RechazarAsync(id, cuerpo?.Motivo ?? "", ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Devuelve la entrega al desarrollador con un motivo");

        grupo.MapPost("/matriz", async (
            GuardarMatrizRequest cuerpo, PoolActivityService pool, CancellationToken ct) =>
        {
            var filas = cuerpo.Celdas
                .Select(c => new PoolPointsMatrixEntry
                {
                    WorkType = c.Tipo, Complexity = c.Complejidad,
                    Points = c.Puntos, HorasLimite = c.HorasLimite
                })
                .ToList();

            var (ok, mensaje) = await pool.GuardarMatrizAsync(filas, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Guarda la matriz de puntos (no revalúa lo ya publicado)");

        grupo.MapPost("/plantilla", async (
            GuardarPuntoDePlantillaRequest cuerpo, PoolActivityService pool, CancellationToken ct) =>
        {
            var (ok, mensaje) = await pool.GuardarPlantillaItemAsync(new PoolChecklistTemplateItem
            {
                Id = cuerpo.Id,
                WorkType = cuerpo.Tipo,
                Text = cuerpo.Texto,
                Orden = cuerpo.Orden,
                RequiereEvidencia = cuerpo.RequiereEvidencia
            }, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Alta o edición de un punto del checklist de un tipo");

        grupo.MapPost("/plantilla/{itemId:int}/alternar", async (
            int itemId, PoolActivityService pool, CancellationToken ct) =>
        {
            var (ok, mensaje) = await pool.DesactivarPlantillaItemAsync(itemId, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Desactiva o reactiva un punto de la plantilla");

        MapDevOps(grupo);
    }

    /// <summary>
    /// El vínculo con Azure DevOps: ligar, reintentar lo que no llegó y comentar en el ticket.
    ///
    /// <para><b>Ligar es del LÍDER</b> y lo demás es de quien trabaja. No es una asimetría gratuita:
    /// ligar decide sobre qué ticket ajeno se va a escribir el esfuerzo y la prioridad a partir de
    /// ahora, mientras que reintentar solo vuelve a mandar lo que ya se decidió y comentar habla
    /// únicamente a nombre de quien lo escribe.</para>
    ///
    /// <para><b>Ninguna de estas rutas puede tumbar una operación del pool</b>: son todas propias, y
    /// el empuje que va colgado de publicar/editar/tomar vive dentro de esos endpoints y nunca sale
    /// como error — lo local ya se guardó antes de intentarlo.</para>
    /// </summary>
    private static void MapDevOps(RouteGroupBuilder grupo)
    {
        grupo.MapPost("/{id:int}/devops/ligar", async (
            int id, LigarConDevOpsRequest? cuerpo, PoolDevOpsService devops, CancellationToken ct) =>
        {
            // El cuerpo ausente llega hasta el servicio como «sin número ni enlace», que es
            // exactamente lo que significa DESLIGAR. No hace falta una ruta aparte para eso: son la
            // misma decisión —con qué ticket habla esta actividad— y una de las respuestas es
            // «con ninguno».
            var (ok, mensaje) = await devops.LigarAsync(id, cuerpo?.WorkItem, cuerpo?.Enlace, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Liga la actividad con un work item de DevOps (o la desliga) y empuja esfuerzo y prioridad");

        grupo.MapGet("/{id:int}/devops", async (
            int id, PoolDevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje, vinculo) = await devops.VinculoAsync(id, ct);
            return ok ? Results.Ok(vinculo) : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .RequireAuthorization(PoliticaDelPool)
        .WithSummary("Con qué work item está ligada y qué falta por mandarle");

        grupo.MapGet("/devops/pendientes", async (PoolDevOpsService devops, CancellationToken ct) =>
            Results.Ok(await devops.PendientesAsync(ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Las actividades cuyo esfuerzo o prioridad no llegaron a DevOps");

        grupo.MapPost("/{id:int}/devops/reintentar", async (
            int id, PoolDevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje) = await devops.ReintentarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelPool)
        .WithSummary("Vuelve a mandar a DevOps lo que quedó pendiente de esta actividad");

        grupo.MapGet("/{id:int}/devops/comentarios", async (
            int id, PoolDevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje, hilo) = await devops.HiloAsync(id, ct);
            return ok ? Results.Ok(hilo) : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .RequireAuthorization(PoliticaDelPool)
        .WithSummary("El hilo del work item ligado, y si puedo escribir en él");

        grupo.MapPost("/{id:int}/devops/comentar", async (
            int id, ComentarEnDevOpsRequest? cuerpo, PoolDevOpsService devops, CancellationToken ct) =>
        {
            // El texto vacío y la falta de token personal llegan los dos hasta el servicio a
            // propósito: es él quien explica que un comentario va firmado por quien lo escribe y
            // dónde se captura el token, y ese texto es el que la persona necesita leer. Hoy nadie
            // lo tiene capturado, así que ese mensaje es el caso corriente y no la excepción.
            var (ok, mensaje) = await devops.ComentarAsync(id, cuerpo?.Texto, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelPool)
        .WithSummary("Publica un comentario en el work item ligado, con el token de quien comenta");
    }

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto —«alguien más la tomó primero»—; eso es lo que se
    /// enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));

    /// <summary>
    /// La cuenta no está ligada a ninguna ficha de desarrollador. No es un fallo de permisos —la
    /// política ya la dejó pasar— sino de datos: los puntos se abonan a una ficha y esta cuenta no
    /// tiene ninguna.
    /// </summary>
    private static IResult SinFicha() =>
        Results.BadRequest(new ResultadoDto(false,
            "Tu cuenta no tiene ficha de desarrollador ligada, así que no puede trabajar actividades " +
            "del pool. Pídeselo al líder."));

    /// <summary>
    /// El borrador que espera el servicio portado. Deliberadamente sin puntos ni estado: los pone él
    /// desde la matriz, y esa es la regla que hace comparables las actividades entre personas.
    /// </summary>
    /// <summary>
    /// El borrador que se le pasa al servicio. Sigue SIN llevar puntos: los pone la matriz.
    /// Los criterios extra van aparte y no aquí, porque no son un campo de la actividad sino filas
    /// propias que hay que resolver contra el catálogo antes de congelarlas.
    ///
    /// <para>El esfuerzo de un bug sí se copia aquí aunque el líder no deba mandarlo: quien lo
    /// rechaza es el servicio, con un mensaje que explica que ese número lo escribe quien lo toma.
    /// Descartarlo en silencio en este punto dejaría al líder creyendo que se guardó.</para>
    /// </summary>
    /// <remarks>
    /// El work item viaja como un campo más del borrador y lo resuelve el servicio, que es quien
    /// sabe sacarlo también del enlace pegado y quien rechaza que los dos se contradigan. Traducirlo
    /// aquí dejaría esa regla fuera del sitio donde se guarda.
    /// </remarks>
    private static PoolActivity ABorrador(PublicarActividadRequest c) => new()
    {
        Title = c.Titulo,
        Description = c.Detalle,
        WorkType = c.Tipo,
        Complexity = c.Complejidad,
        Priority = c.Prioridad,
        HorasLimite = c.Horas,
        HorasEstimadas = c.HorasEstimadas,
        ExternalUrl = c.Enlace,
        DevOpsWorkItemId = c.WorkItem,
        EquipoId = c.EquipoId
    };
}
