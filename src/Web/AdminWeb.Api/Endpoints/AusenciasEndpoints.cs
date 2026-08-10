using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Ausencias;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Las ausencias PROPIAS: pedir, consultar, cancelar y retirar vacaciones y permisos, más el
/// justificante de un permiso.
///
/// <b>Ninguna ruta lleva identificador de persona</b>, igual que en jornada: todas actúan sobre quien
/// tiene la sesión. Con un <c>/{developerId}</c> habría que comprobar en cada endpoint que ese
/// identificador es el propio, y el día que a uno se le olvidara sería «pide vacaciones a nombre de
/// otro». Los identificadores que sí viajan son los de la solicitud, y de esos se encarga la guarda
/// de pertenencia que ya traen los servicios.
///
/// <para>Lo que resuelve el líder —aprobar, rechazar, el documento firmado— no está aquí y no es un
/// descuido: es otra pantalla y otra fase.</para>
/// </summary>
public static class AusenciasEndpoints
{
    public static void MapAusenciasEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/ausencias")
            .WithTags("Ausencias")
            .RequireAuthorization("AdminUDesarrollador");

        // ── Vacaciones ───────────────────────────────────────────────────────────

        // El estado de las FIRMAS se pega aquí, sobre lo que arma AusenciasService. Es composición y
        // no un endpoint aparte a propósito: la pantalla pinta la lista y el estado de la firma en el
        // mismo renglón, y en dos peticiones habría un instante enseñando como «sin firmar» algo que
        // sí lo está — justo el dato que esta pantalla existe para dejar claro.
        grupo.MapGet("/vacaciones/mias", async (
            AusenciasService ausencias, VacationRequestService vacaciones, CancellationToken ct) =>
        {
            var datos = await ausencias.MisVacacionesAsync(ct);
            var papeles = await vacaciones.PapelesDeAsync(datos.Solicitudes.Select(s => s.Id), ct);

            var firmas = datos.Solicitudes
                .Select(s =>
                {
                    var p = papeles[s.Id];
                    return new FirmaDeSolicitudDto(
                        s.Id,
                        Firmada: p.SigueValiendo,
                        DejoDeValer: p.Firmada && !p.SigueValiendo,
                        FirmadaUtc: p.FirmadaUtc,
                        SePuedeFirmar: VacationRequestService.PuedeFirmar(s.Estado),
                        DocumentoArchivado: p.DocumentoDelLiderArchivado);
                })
                .ToList();

            // Y se corrige el conteo de documentos con el que sí descuenta la fila de la firma: el
            // enlace vive en la misma tabla que los documentos generados, y sin esto la confirmación
            // de borrado avisaría de un papel que nadie generó.
            var solicitudes = datos.Solicitudes
                .Select(s => s with { DocumentosGenerados = papeles[s.Id].DocumentosGenerados })
                .ToList();

            return Results.Ok(datos with { Solicitudes = solicitudes, Firmas = firmas });
        })
        .WithSummary("Saldo del año, solicitudes de vacaciones propias y el estado de sus firmas");

        grupo.MapPost("/vacaciones", async (
            NuevaSolicitudDeVacacionesRequest cuerpo, AusenciasService ausencias, CancellationToken ct) =>
        {
            var (ok, mensaje, id) = await ausencias.SolicitarVacacionesAsync(
                cuerpo.Inicio, cuerpo.Fin, cuerpo.Comentario, ct);
            return ok ? Results.Ok(new SolicitudCreadaDto(true, mensaje, id)) : Rechazo(mensaje);
        })
        .WithSummary("Pide vacaciones para uno mismo; la solicitud nace pendiente");

        grupo.MapPost("/vacaciones/{id:int}/cancelacion", async (
            int id, CancelacionRequest? cuerpo, VacationRequestService vacaciones, CancellationToken ct) =>
        {
            var (ok, mensaje) = await vacaciones.CancelarAsync(id, cuerpo?.Motivo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Retira una solicitud propia, esté pendiente o ya aprobada");

        // Eliminar va por POST y no por DELETE porque el cliente solo sabe leer y enviar (ver
        // ClienteApi): un verbo que la única aplicación que llama no puede usar sería decorativo.
        grupo.MapPost("/vacaciones/{id:int}/eliminacion", async (
            int id, VacationRequestService vacaciones, CancellationToken ct) =>
        {
            var (ok, mensaje) = await vacaciones.EliminarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Borra una solicitud que nunca llegó a ser una decisión del líder");

        // El respaldo SUBE por aquí y BAJA por /api/adjuntos/vacaciones/{id}, exactamente igual que el
        // justificante de un permiso: allí está resuelto lo delicado (tipo por bytes, nosniff y nombre
        // limpio) y allí lo pide también el líder al resolver la solicitud, sin una segunda ruta.
        grupo.MapPost("/vacaciones/{id:int}/respaldo", async (
            int id, IFormFile archivo, VacationRequestService vacaciones, CancellationToken ct) =>
        {
            // El tamaño se mira ANTES de copiar. Validar también lo comprueba, pero para entonces el
            // archivo ya estaría entero en la memoria de la API, que es justo lo que se quiere evitar
            // de una subida desmedida.
            if (archivo.Length > ArchivosSubidos.MaxBytes)
                return Rechazo($"El respaldo pasa de {ArchivosSubidos.MaxBytes / (1024 * 1024)} MB.");

            using var memoria = new MemoryStream();
            await archivo.CopyToAsync(memoria, ct);
            var contenido = memoria.ToArray();

            // La comprobación que cuenta se hace AQUÍ, en el servidor: a esta ruta se puede llamar sin
            // pasar por el navegador, así que lo que filtre el cliente no es una barrera. Se admite
            // cualquier documento y no solo imágenes —un respaldo suele ser un PDF o un .docx—; el
            // filtro de lo peligroso lo pone ArchivosSubidos con su lista de extensiones.
            var (valido, error, nombreSeguro) = ArchivosSubidos.Validar(archivo.FileName, contenido);
            if (!valido) return Rechazo(error);

            var (ok, mensaje) = await vacaciones.AdjuntarRespaldoAsync(id, contenido, nombreSeguro, ct);
            return Resultado(ok, mensaje);
        })
        // El testigo antifalsificación lo exige el middleware por ser multipart, y lo adjunta el
        // cliente sin que la pantalla tenga que acordarse (ClienteApi.SubirAsync).
        .WithSummary("Sube o reemplaza el documento de respaldo de unas vacaciones propias aún pendientes");

        // ── La firma de la propia solicitud ──────────────────────────────────────
        //
        // Va en ESTE grupo y no en el del líder, y no es un detalle de organización: aquí ninguna ruta
        // lleva identificador de persona porque todas actúan sobre quien tiene la sesión, y firmar es
        // la operación donde eso más importa. El servicio lo vuelve a exigir —firmar por otro no se
        // permite ni siendo líder—, pero que ni siquiera exista un hueco donde escribir «a nombre de»
        // es la primera barrera y la que no se puede olvidar.
        //
        // La imagen sube como ARCHIVO y no como texto en base64, igual que las firmas del jefe: son
        // bytes, y base64 los infla un tercio. Las medidas viajan en la cadena de consulta porque en
        // el cuerpo multipart solo va el archivo, que es lo que el enrutado ata sin ambigüedad; y no
        // son opcionales: sin ellas el documento no sabe a qué tamaño estamparla y la firma sale de un
        // píxel — que es exactamente lo que hacía el escritorio al caer a su valor de respaldo.
        grupo.MapPost("/vacaciones/{id:int}/firma", async (
            int id, IFormFile archivo, int ancho, int alto,
            VacationRequestService vacaciones, CancellationToken ct) =>
        {
            // El tope se mira ANTES de copiar, y es el de las firmas y no el de los adjuntos: un
            // trazo recortado son unos pocos KB, y lo que llegue por encima no es una firma.
            if (archivo.Length > SignatureService.MaxBytes)
                return Rechazo($"La imagen de la firma pasa de {SignatureService.MaxBytes / 1024} KB; " +
                               "eso no es un trazo.");

            using var memoria = new MemoryStream();
            await archivo.CopyToAsync(memoria, ct);
            var contenido = memoria.ToArray();

            // Se exige que sea una imagen DE VERDAD, por los BYTES y no por el nombre: acabará pegada
            // en un documento que alguien archiva. La comprobación que cuenta se hace aquí porque a
            // esta ruta se puede llamar sin pasar por el navegador.
            var (valido, error, _) = ArchivosSubidos.Validar(archivo.FileName, contenido, soloImagenes: true);
            if (!valido) return Rechazo(error);

            var (ok, mensaje) = await vacaciones.FirmarAsync(id, contenido, ancho, alto, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Firma una solicitud de vacaciones propia con el trazo capturado a mano");

        // ── El documento propio ──────────────────────────────────────────────────
        //
        // El desarrollador tiene que poder VER lo que firma. Son las mismas dos salidas que ve el
        // líder y salen del mismo servicio —que ahora deja pasar «al dueño o al líder»—, así que no
        // hay una segunda forma de armar el papel que pudiera decir otra cosa. Lo que el servicio no
        // deja es elegir firma: el parámetro de la firma del jefe se ignora para quien no lo es.
        grupo.MapGet("/vacaciones/{id:int}/documento", async (
            int id, HttpContext ctx, DocumentoDeVacacionesService documentos, CancellationToken ct) =>
        {
            var (ok, mensaje, pdf, nombre) = await documentos.GenerarAsync(id, firmaId: null, ct);
            return ok ? ResultadosDeArchivo.Adjunto(ctx, pdf, nombre) : Rechazo(mensaje);
        })
        .WithSummary("El PDF de la solicitud propia, con la firma de quien la pidió si ya la firmó");

        grupo.MapGet("/vacaciones/{id:int}/documento/word", async (
            int id, HttpContext ctx, DocumentoDeVacacionesService documentos, CancellationToken ct) =>
        {
            var (ok, mensaje, docx, nombre) = await documentos.GenerarWordAsync(id, firmaId: null, ct);
            return ok ? ResultadosDeArchivo.Adjunto(ctx, docx, nombre) : Rechazo(mensaje);
        })
        .WithSummary("La solicitud propia en Word, sobre la plantilla editable y con la firma puesta");

        grupo.MapGet("/vacaciones/{id:int}/documento/firmado", async (
            int id, HttpContext ctx, DocumentoDeVacacionesService documentos, CancellationToken ct) =>
        {
            var (pdf, nombre) = await documentos.DocumentoFirmadoAsync(id, ct);
            // El «todavía no está firmado» se dice aquí y no se deja al 404 genérico: es un estado
            // normal de la solicitud, no un archivo perdido.
            return pdf.Length == 0
                ? Results.NotFound(new { mensaje = "Esa solicitud todavía no tiene documento firmado por el líder." })
                : ResultadosDeArchivo.Adjunto(ctx, pdf, nombre);
        })
        .WithSummary("El PDF ya resuelto y archivado de una solicitud propia, con las dos firmas");

        // ── Permisos ─────────────────────────────────────────────────────────────

        grupo.MapGet("/permisos/mios", async (AusenciasService ausencias, CancellationToken ct) =>
            Results.Ok(await ausencias.MisPermisosAsync(ct)))
        .WithSummary("Indicadores del año, permisos propios y catálogos del formulario");

        grupo.MapPost("/permisos", async (
            NuevaSolicitudDePermisoRequest cuerpo, AusenciasService ausencias, CancellationToken ct) =>
        {
            var (ok, mensaje, id) = await ausencias.SolicitarPermisoAsync(
                cuerpo.Tipo, cuerpo.Desde, cuerpo.Dias, cuerpo.Motivo, cuerpo.Notas, ct);
            return ok ? Results.Ok(new SolicitudCreadaDto(true, mensaje, id)) : Rechazo(mensaje);
        })
        .WithSummary("Solicita un permiso para uno mismo; queda pendiente de que el líder lo resuelva");

        grupo.MapPost("/permisos/{id:int}/cancelacion", async (
            int id, CancelacionRequest? cuerpo, LeaveRequestService permisos, CancellationToken ct) =>
        {
            var (ok, mensaje) = await permisos.CancelarAsync(id, cuerpo?.Motivo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Retira un permiso propio, esté pendiente o ya aprobado");

        grupo.MapPost("/permisos/{id:int}/eliminacion", async (
            int id, LeaveRequestService permisos, CancellationToken ct) =>
        {
            var (ok, mensaje) = await permisos.EliminarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Borra un permiso que nunca llegó a resolverse");

        // El justificante SUBE por aquí y BAJA por /api/adjuntos/permiso/{id}, que ya existe y sirve
        // todos los adjuntos guardados en la base con el mismo trato (tipo por bytes, nosniff, nombre
        // limpio). Duplicar esa bajada aquí sería tener dos sitios donde equivocarse.
        grupo.MapPost("/permisos/{id:int}/justificante", async (
            int id, IFormFile archivo, AusenciasService ausencias, CancellationToken ct) =>
        {
            // El tamaño se mira ANTES de copiar. Validar también lo comprueba, pero para entonces el
            // archivo ya estaría entero en la memoria de la API, que es justo lo que se quiere evitar
            // de una subida desmedida.
            if (archivo.Length > ArchivosSubidos.MaxBytes)
                return Rechazo($"El justificante pasa de {ArchivosSubidos.MaxBytes / (1024 * 1024)} MB.");

            using var memoria = new MemoryStream();
            await archivo.CopyToAsync(memoria, ct);
            var contenido = memoria.ToArray();

            // La comprobación que cuenta se hace AQUÍ, en el servidor: a esta ruta se puede llamar sin
            // pasar por el navegador, así que lo que filtre el cliente no es una barrera. El nombre
            // que vuelve es el ya saneado; el que escribió quien sube el archivo no se guarda.
            var (valido, error, nombreSeguro) = ArchivosSubidos.Validar(archivo.FileName, contenido);
            if (!valido) return Rechazo(error);

            var (ok, mensaje) = await ausencias.AdjuntarJustificanteAsync(id, contenido, nombreSeguro, ct);
            return Resultado(ok, mensaje);
        })
        // El testigo antifalsificación SÍ se exige aquí: es la única familia de peticiones que puede
        // provocarse desde otro sitio, porque un formulario multipart ajeno llegaría con la cookie de
        // sesión puesta. Lo adjunta el cliente sin que la pantalla tenga que acordarse
        // (ClienteApi.SubirAsync).
        .WithSummary("Sube o reemplaza el justificante de un permiso propio aún pendiente");
    }

    /// <summary>
    /// La respuesta de una operación de escritura. El mensaje lo escribió el servicio y se manda tal
    /// cual: explica el motivo en concreto —«No se puede eliminar una solicitud «Aprobada»: es parte
    /// del historial»— y eso es lo que permite saber qué hacer después.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje)) : Rechazo(mensaje);

    /// <summary>
    /// Un rechazo de negocio: 400 con el mensaje del servicio en <c>Detail</c>.
    ///
    /// La FORMA del cuerpo importa. El cliente lee toda respuesta fallida como ProblemDetails (ver
    /// <c>ClienteApi.DescribirAsync</c>) y se queda con <c>Detail</c>; cualquier otra forma acabaría
    /// enseñando un «no se pudo completar la operación» genérico y tirando por el camino justo lo que
    /// el servicio se molestó en explicar.
    /// </summary>
    private static IResult Rechazo(string mensaje) =>
        Results.Problem(detail: mensaje, statusCode: StatusCodes.Status400BadRequest, title: "No se pudo");
}
