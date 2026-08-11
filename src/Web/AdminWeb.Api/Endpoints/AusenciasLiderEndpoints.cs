using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.AusenciasLider;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// El lado del LÍDER en las ausencias y las actividades: resolver vacaciones y emitir su documento
/// firmado, resolver permisos, mirar en qué se fue el tiempo del equipo y atender sus sugerencias.
///
/// <para>Va en un archivo aparte de <see cref="AusenciasEndpoints"/> a propósito: allí ninguna ruta
/// lleva identificador de persona porque todas actúan sobre quien tiene la sesión, y aquí TODAS lo
/// llevan porque el líder opera sobre lo ajeno. Mezclarlas haría que la regla «esta familia de rutas
/// no toca datos de otro» dejara de ser cierta justo donde protege.</para>
///
/// <para><b>Todo el grupo exige la política del líder</b>, y los servicios conservan además su
/// <c>AuthorizationGuard</c>: dos barreras, no una. La de aquí es la que se aplica aunque nadie pase
/// por el navegador.</para>
///
/// <para><b>Los bytes no viajan en las listas.</b> El respaldo de unas vacaciones, el justificante de
/// un permiso, la evidencia de una actividad, la imagen de una firma y el PDF del documento se piden
/// cada uno por su ruta. Una lista que los llevara dentro descargaría megabytes que casi nadie
/// abre.</para>
/// </summary>
public static class AusenciasLiderEndpoints
{
    /// <summary>Resolver ausencias, firmar documentos y atender sugerencias es del líder.</summary>
    private const string PoliticaDelLider = "SoloAdmin";

    public static void MapAusenciasLiderEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/ausencias-lider")
            .WithTags("Ausencias y actividades (líder)")
            .RequireAuthorization(PoliticaDelLider);

        MapVacaciones(grupo);
        MapFirmas(grupo);
        MapPermisos(grupo);
        MapActividades(grupo);
        MapSugerencias(grupo);
    }

    // ── Vacaciones ───────────────────────────────────────────────────────────────

    private static void MapVacaciones(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/vacaciones", async (
            int? developerId, VacationStatus? estado,
            DocumentoDeVacacionesService vacaciones, SignatureService firmas,
            CatalogosQueryService catalogos, CancellationToken ct) =>
        {
            var solicitudes = await vacaciones.SolicitudesAsync(developerId, estado, ct);
            var pendientes = await vacaciones.PendientesCountAsync(ct);
            var guardadas = await firmas.VisiblesAsync(null, ct);

            return Results.Ok(new VacacionesDelLiderDto(
                solicitudes.Select(ASolicitud).ToList(),
                await DesarrolladoresAsync(catalogos, ct),
                Opciones<VacationStatus>(DocumentoDeVacacionesService.Etiqueta),
                guardadas.Select(AFirma).ToList(),
                pendientes));
        })
        .WithSummary("Las solicitudes de vacaciones del equipo, los desplegables y las firmas guardadas");

        grupo.MapPost("/vacaciones/{id:int}/resolucion", async (
            int id, ResolucionDeVacacionesRequest cuerpo, DocumentoDeVacacionesService vacaciones,
            CancellationToken ct) =>
        {
            // El comentario vacío llega hasta el servicio a propósito: es él quien explica por qué
            // hace falta al rechazar, y ese texto es el que se lee.
            var (ok, mensaje) = await vacaciones.ResolverAsync(id, cuerpo.Estado, cuerpo.Comentario, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Aprueba, rechaza o cancela una solicitud (rechazar exige motivo)");

        // La exportación baja LO QUE EL FILTRO ESTÁ ENSEÑANDO, misma decisión que en minutas. Los dos
        // parámetros son los mismos que los del listado de arriba, y no por casualidad: cualquier
        // filtro que no se repitiera aquí sería una fila de más en la hoja que nadie pidió.
        grupo.MapGet("/vacaciones/excel", async (
            int? developerId, VacationStatus? estado, DocumentoDeVacacionesService vacaciones,
            CancellationToken ct) =>
            ResultadosDeArchivo.Excel(await vacaciones.ExcelAsync(developerId, estado, ct), "Vacaciones"))
        .WithSummary("Las solicitudes de vacaciones del filtro, en una hoja de cálculo");

        // El respaldo que adjuntó quien pidió las vacaciones NO tiene ruta aquí: se pide a
        // /api/adjuntos/vacaciones/{id}, que es por donde sale todo lo guardado en la base y que ya
        // deja verlo al dueño y al líder. Una segunda ruta para los mismos bytes sería un segundo
        // sitio donde equivocarse con el permiso.

        // El documento se genera al vuelo y no se guarda: es el BORRADOR, y guardar cada
        // previsualización llenaría la base de PDF que nadie vuelve a mirar. Con firmaId se ve cómo
        // quedará firmado antes de archivarlo.
        grupo.MapGet("/vacaciones/{id:int}/documento", async (
            int id, int? firmaId, HttpContext ctx, DocumentoDeVacacionesService vacaciones,
            CancellationToken ct) =>
        {
            var (ok, mensaje, pdf, nombre) = await vacaciones.GenerarAsync(id, firmaId, ct);
            return ok ? ResultadosDeArchivo.Adjunto(ctx, pdf, nombre) : Rechazo(mensaje);
        })
        .WithSummary("El PDF de la solicitud: borrador sin firmar, o con la firma indicada");

        // El mismo documento en WORD, sobre la plantilla que el área puede editar. Son dos salidas
        // del mismo papel: el PDF lo maqueta el código y siempre sale igual; el Word sale de la
        // plantilla de RH, así que cambiar una palabra del formato no exige recompilar.
        grupo.MapGet("/vacaciones/{id:int}/documento/word", async (
            int id, int? firmaId, HttpContext ctx, DocumentoDeVacacionesService vacaciones,
            CancellationToken ct) =>
        {
            var (ok, mensaje, docx, nombre) = await vacaciones.GenerarWordAsync(id, firmaId, ct);
            return ok ? ResultadosDeArchivo.Adjunto(ctx, docx, nombre) : Rechazo(mensaje);
        })
        .WithSummary("La solicitud en Word, rellenada sobre la plantilla editable");

        // ── La plantilla editable ────────────────────────────────────────────────

        grupo.MapGet("/plantilla-vacaciones", async (
            DocumentoDeVacacionesService vacaciones, CancellationToken ct) =>
        {
            var (esDeFabrica, quien, cuando) = await vacaciones.OrigenDeLaPlantillaAsync(ct);
            return Results.Ok(new OrigenDePlantillaDto(esDeFabrica, quien, cuando));
        })
        .WithSummary("De dónde sale la plantilla que se está usando y desde cuándo");

        grupo.MapGet("/plantilla-vacaciones/descargar", async (
            HttpContext ctx, DocumentoDeVacacionesService vacaciones, CancellationToken ct) =>
            ResultadosDeArchivo.Adjunto(ctx, await vacaciones.PlantillaVigenteAsync(ct),
                                        "Solicitud_Vacaciones_Plantilla.docx"))
        .WithSummary("Descarga la plantilla vigente para editarla en Word");

        grupo.MapPost("/plantilla-vacaciones", async (
            IFormFile archivo, DocumentoDeVacacionesService vacaciones, CancellationToken ct) =>
        {
            // Tope propio y generoso: la de fábrica pesa 124 KB, pero una con logotipos en alta
            // resolución puede irse a varios megabytes sin que eso sea un abuso.
            const long tope = 10 * 1024 * 1024;
            if (archivo.Length is 0 or > tope)
                return Rechazo($"El archivo está vacío o pasa de {tope / 1024 / 1024} MB.");

            using var memoria = new MemoryStream();
            await archivo.CopyToAsync(memoria, ct);

            var (ok, mensaje) = await vacaciones.GuardarPlantillaAsync(
                memoria.ToArray(), archivo.FileName, ct);
            return Resultado(ok, mensaje);
        })
        // SIN DisableAntiforgery, igual que la subida de firmas: es multipart, así que UseAntiforgery
        // le exige el testigo, y ClienteApi.SubirAsync ya lo adjunta. Quitarlo dejaría una ruta que
        // un sitio ajeno podría provocar con la cookie de sesión puesta.
        .WithSummary("Sustituye la plantilla de Word de la solicitud de vacaciones");

        grupo.MapPost("/plantilla-vacaciones/restablecer", async (
            DocumentoDeVacacionesService vacaciones, CancellationToken ct) =>
        {
            var (ok, mensaje) = await vacaciones.RestablecerPlantillaAsync(ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Vuelve a la plantilla que trae la aplicación");

        // La comprobación de que la firma del colaborador sigue valiendo NO se hace aquí: vive en
        // FirmarAsync, junto al resto de las reglas del documento. Repetirla en este endpoint daría
        // dos sitios donde decidir lo mismo, y el día que uno cambiara el otro seguiría dejando pasar
        // exactamente lo que el otro prohíbe.
        grupo.MapPost("/vacaciones/{id:int}/documento/firma", async (
            int id, FirmarDocumentoRequest cuerpo, DocumentoDeVacacionesService vacaciones,
            CancellationToken ct) =>
        {
            var (ok, mensaje) = await vacaciones.FirmarAsync(id, cuerpo.FirmaId, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Firma el documento con una firma guardada y lo archiva en la solicitud");

        // La SALIDA del bloqueo anterior, y por eso vive al lado: si firmar el documento puede
        // negarse porque la firma del colaborador se cayó, tiene que haber a un clic de distancia
        // la forma de conseguir otra. Sin esto, el rechazo de arriba sería un callejón.
        grupo.MapPost("/vacaciones/{id:int}/firma-del-colaborador/recordatorio", async (
            int id, RecordatorioDeFirmaRequest? cuerpo, DocumentoDeVacacionesService vacaciones,
            CancellationToken ct) =>
        {
            var (ok, mensaje) = await vacaciones.PedirQueVuelvaAFirmarAsync(id, cuerpo?.Nota, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Le avisa al colaborador que firme —o que vuelva a firmar— su solicitud");

        grupo.MapGet("/vacaciones/{id:int}/documento/firmado", async (
            int id, HttpContext ctx, DocumentoDeVacacionesService vacaciones, CancellationToken ct) =>
        {
            var (pdf, nombre) = await vacaciones.DocumentoFirmadoAsync(id, ct);
            // El «todavía no está firmado» se dice aquí y no se deja al 404 genérico de Adjunto: es
            // un estado normal de la solicitud, no un archivo perdido.
            return pdf.Length == 0
                ? Results.NotFound(new { mensaje = "Esa solicitud todavía no tiene documento firmado." })
                : ResultadosDeArchivo.Adjunto(ctx, pdf, nombre);
        })
        .WithSummary("El PDF firmado que quedó archivado");
    }

    // ── Firmas reutilizables ─────────────────────────────────────────────────────

    private static void MapFirmas(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/firmas", async (SignatureService firmas, CancellationToken ct) =>
            Results.Ok((await firmas.VisiblesAsync(null, ct)).Select(AFirma).ToList()))
        .WithSummary("Las firmas compartidas guardadas, sin sus imágenes");

        grupo.MapGet("/firmas/{id:int}/imagen", async (
            int id, HttpContext ctx, SignatureService firmas, CancellationToken ct) =>
        {
            var (png, _, _) = await firmas.ImagenAsync(id, ct);
            return ResultadosDeArchivo.Adjunto(ctx, png, "firma.png");
        })
        .WithSummary("El PNG de una firma guardada");

        // La firma SUBE como archivo y no como texto en base64: son bytes de imagen, y base64 los
        // infla un tercio y obliga a materializarlos enteros antes de poder mirarlos. Al ser
        // multipart, el testigo antifalsificación es obligatorio — lo adjunta ClienteApi.SubirAsync.
        //
        // El nombre y las medidas viajan en la CADENA DE CONSULTA y no como campos del formulario:
        // en el cuerpo multipart solo va el archivo, que es lo que este enrutado ata sin ambigüedad,
        // y así el resto se enlaza igual que en cualquier otro endpoint.
        grupo.MapPost("/firmas", async (
            IFormFile archivo, string nombre, int ancho, int alto, bool? predeterminada,
            SignatureService firmas, CancellationToken ct) =>
        {
            // El tamaño se mira ANTES de copiar: validar después ya habría traído el archivo entero a
            // la memoria de la API, que es justo lo que se quiere evitar de una subida desmedida.
            if (archivo.Length > SignatureService.MaxBytes)
                return Rechazo($"La imagen de la firma pasa de {SignatureService.MaxBytes / 1024} KB; eso no es un trazo.");

            using var memoria = new MemoryStream();
            await archivo.CopyToAsync(memoria, ct);
            var contenido = memoria.ToArray();

            // La comprobación que cuenta se hace AQUÍ: a esta ruta se puede llamar sin pasar por el
            // navegador, así que lo que filtre el cliente no es una barrera. Se exige que sea una
            // imagen DE VERDAD —por los bytes, no por el nombre— porque acabará pegada en un
            // documento que alguien archiva.
            var (valido, error, _) = ArchivosSubidos.Validar(archivo.FileName, contenido, soloImagenes: true);
            if (!valido) return Rechazo(error);

            // Dueño nulo: es la firma COMPARTIDA, la del jefe. Es la que el documento de vacaciones
            // usa, y crearla es del líder — el servicio lo vuelve a comprobar.
            var (ok, mensaje, _) = await firmas.CrearAsync(
                nombre, contenido, ancho, alto, duenoDeveloperId: null, predeterminada ?? false, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Guarda una firma trazada a mano como firma compartida del jefe");

        grupo.MapPost("/firmas/{id:int}/nombre", async (
            int id, RenombrarFirmaRequest cuerpo, SignatureService firmas, CancellationToken ct) =>
        {
            var (ok, mensaje) = await firmas.RenombrarAsync(id, cuerpo.Nombre, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Cambia el nombre de una firma guardada");

        grupo.MapPost("/firmas/{id:int}/predeterminada", async (
            int id, SignatureService firmas, CancellationToken ct) =>
        {
            var (ok, mensaje) = await firmas.MarcarPredeterminadaAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Deja esa firma como la que se ofrece por omisión");

        // Eliminar va por POST y no por DELETE porque el único camino del cliente para llamar a la
        // API con efecto —ClienteApi— habla GET y POST. Un DELETE que nadie puede invocar no haría
        // la API más correcta.
        grupo.MapPost("/firmas/{id:int}/eliminacion", async (
            int id, SignatureService firmas, CancellationToken ct) =>
        {
            var (ok, mensaje) = await firmas.EliminarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Borra una firma guardada (los documentos ya firmados no se tocan)");
    }

    // ── Permisos ─────────────────────────────────────────────────────────────────

    private static void MapPermisos(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/permisos", async (
            int? developerId, LeaveStatus? estado, LeaveRequestService permisos,
            CatalogosQueryService catalogos, CancellationToken ct) =>
        {
            // Se reusa el servicio portado y no una proyección propia: es quien trae la guarda y el
            // orden («las pendientes primero»). El coste conocido es que devuelve la entidad entera,
            // con los bytes del justificante dentro; recortarlo exigiría tocar un servicio que en
            // esta fase no se toca, y el justificante sigue sin salir hacia el navegador —solo se
            // dice que existe, y se descarga por /api/adjuntos/permiso/{id}.
            var filas = await permisos.TodasAsync(developerId, estado, ct);
            var pendientes = await permisos.PendientesCountAsync(ct);

            return Results.Ok(new PermisosDelLiderDto(
                filas.Select(APermiso).ToList(),
                await DesarrolladoresAsync(catalogos, ct),
                Opciones<LeaveType>(LeaveRequestService.EtiquetaTipo),
                Opciones<LeaveStatus>(LeaveRequestService.Etiqueta),
                pendientes,
                LeaveRequestService.MaxDias));
        })
        .WithSummary("Los permisos del equipo, con las pendientes primero, y los desplegables");

        grupo.MapPost("/permisos/{id:int}/aprobacion", async (
            int id, ResolucionDePermisoRequest? cuerpo, LeaveRequestService permisos, CancellationToken ct) =>
        {
            var (ok, mensaje) = await permisos.AprobarAsync(id, cuerpo?.Comentario, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Aprueba un permiso pendiente, con un comentario opcional");

        grupo.MapPost("/permisos/{id:int}/rechazo", async (
            int id, ResolucionDePermisoRequest? cuerpo, LeaveRequestService permisos, CancellationToken ct) =>
        {
            // El motivo vacío llega hasta el servicio a propósito: es él quien explica por qué hace
            // falta —«es lo único que el solicitante va a leer»—, y ese texto es el que se ve.
            var (ok, mensaje) = await permisos.RechazarAsync(id, cuerpo?.Comentario, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Rechaza un permiso pendiente; el motivo es obligatorio");

        // Corregir NO pasa por LeaveRequestService.EditarAsync, y esa es la mitad importante de esta
        // ruta: aquel método reemplaza la solicitud entera con lo que llegue —adjunto incluido—, así
        // que desde una pantalla que no sube archivos borraría el justificante de quien pidió el
        // permiso. El cuerpo de esta petición ni siquiera tiene dónde meter un adjunto.
        grupo.MapPost("/permisos/{id:int}/correccion", async (
            int id, CorreccionDePermisoRequest cuerpo, LeaveRequestService permisos, CancellationToken ct) =>
        {
            var (ok, mensaje) = await permisos.CorregirPendienteAsync(
                id, cuerpo.Tipo, cuerpo.Desde, cuerpo.Dias, cuerpo.Motivo, cuerpo.Notas, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Corrige los datos de un permiso pendiente sin tocar su justificante");

        grupo.MapPost("/permisos/registro", async (
            RegistroDePermisoRequest cuerpo, LeaveRequestService permisos, CancellationToken ct) =>
        {
            var (ok, mensaje, _) = await permisos.RegistrarPorAdministradorAsync(ABorrador(cuerpo), ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Registra un permiso ya concedido fuera de la aplicación; nace aprobado");

        grupo.MapPost("/permisos/{id:int}/eliminacion", async (
            int id, LeaveRequestService permisos, CancellationToken ct) =>
        {
            var (ok, mensaje) = await permisos.EliminarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Borra un permiso del historial (el líder mantiene el registro)");
    }

    // ── Actividades libres del equipo ────────────────────────────────────────────

    private static void MapActividades(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/actividades", async (
            int? developerId, DevActivityStatus? estado, DevActivityService actividades,
            WorkSessionService cronometro, CatalogosQueryService catalogos, CancellationToken ct) =>
        {
            var filas = await actividades.TodasParaAdministradorAsync(developerId, estado, ct);
            var evidencias = await actividades.ConteoEvidenciasAsync(filas.Select(a => a.Id), ct);

            // El total se pide actividad por actividad porque es lo único que ofrece el servicio de
            // cronómetro ya portado, y en esta fase no se toca. Es la misma consulta por fila que
            // hacía el escritorio al llenar su rejilla.
            var segundos = new Dictionary<int, int>();
            foreach (var a in filas)
                segundos[a.Id] = await cronometro.GetTotalSecondsByActivityAsync(a.Id, ct);

            var lista = filas
                .Select(a => new ActividadDelEquipoDto(
                    a.Id,
                    a.Developer?.FullName ?? "—",
                    a.Title,
                    a.Description,
                    a.Status,
                    DevActivityService.Etiqueta(a.Status),
                    a.CreatedAt,
                    a.ClosedAt,
                    segundos.GetValueOrDefault(a.Id),
                    WorkSessionService.Format(segundos.GetValueOrDefault(a.Id)),
                    evidencias.GetValueOrDefault(a.Id)))
                .ToList();

            return Results.Ok(new ActividadesDelEquipoDto(
                lista,
                await DesarrolladoresAsync(catalogos, ct),
                Opciones<DevActivityStatus>(DevActivityService.Etiqueta),
                lista.Count,
                lista.Count(a => a.Estado == DevActivityStatus.Abierta),
                WorkSessionService.Format(segundos.Values.Sum())));
        })
        .WithSummary("Las actividades libres del equipo con su tiempo y sus indicadores");

        // Mismos dos filtros que el listado: se baja LO QUE EL FILTRO ESTÁ ENSEÑANDO.
        grupo.MapGet("/actividades/excel", async (
            int? developerId, DevActivityStatus? estado, DevActivityService actividades,
            CancellationToken ct) =>
            ResultadosDeArchivo.Excel(
                await actividades.ExcelDelEquipoAsync(developerId, estado, ct), "Actividades"))
        .WithSummary("Las actividades libres del filtro, en una hoja de cálculo");

        grupo.MapGet("/actividades/{id:int}/detalle", async (
            int id, DevActivityService actividades, WorkSessionService cronometro, CancellationToken ct) =>
        {
            var sesiones = await actividades.SesionesDeAsync(id, ct);
            var evidencias = await actividades.EvidenciasDeAsync(id, ct);
            var total = await cronometro.GetTotalSecondsByActivityAsync(id, ct);
            var ahora = DateTime.UtcNow;

            return Results.Ok(new DetalleDeActividadDto(
                id,
                WorkSessionService.Format(total),
                sesiones.Select(w => new SesionDeActividadDto(
                        w.StartedAt, w.EndedAt, w.LiveSeconds(ahora),
                        WorkSessionService.Format(w.LiveSeconds(ahora))))
                    .ToList(),
                evidencias.Select(e => new EvidenciaDeActividadDto(
                        e.Id, e.FileName, e.SizeBytes, e.Description, e.CreatedAtUtc))
                    .ToList()));
        })
        .WithSummary("Las sesiones de cronómetro y la evidencia de una actividad, en solo lectura");
    }

    // ── Sugerencias ──────────────────────────────────────────────────────────────

    private static void MapSugerencias(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/sugerencias", async (
            SuggestionStatus? estado, SuggestionCategory? categoria, SuggestionVisibility? visibilidad,
            bool? topVotos, SuggestionService sugerencias, CancellationToken ct) =>
        {
            var filas = await sugerencias.TodasAsync(estado, categoria, visibilidad, ct);
            var votos = await sugerencias.ContarVotosAsync(ct);

            int Votos(Suggestion s) => votos.GetValueOrDefault(s.Id);
            if (topVotos == true)
                filas = filas.OrderByDescending(Votos).ThenByDescending(s => s.CreatedAt).ToList();

            return Results.Ok(new TableroDeSugerenciasDto(
                filas.Select(s => ASugerencia(s, Votos(s))).ToList(),
                Opciones<SuggestionStatus>(SuggestionService.EtiquetaEstado),
                Opciones<SuggestionCategory>(SuggestionService.EtiquetaCategoria),
                Opciones<SuggestionVisibility>(SuggestionService.EtiquetaVisibilidad),
                filas.Count(s => s.Status == SuggestionStatus.Nueva),
                filas.Count(s => s.Visibility == SuggestionVisibility.SoloAdministrador)));
        })
        .WithSummary("El tablero de sugerencias del líder, con sus filtros y contadores");

        grupo.MapPost("/sugerencias/{id:int}/respuesta", async (
            int id, RespuestaASugerenciaRequest cuerpo, SuggestionService sugerencias, CancellationToken ct) =>
        {
            var (ok, mensaje) = await sugerencias.ResponderAsync(id, cuerpo.Estado, cuerpo.Respuesta, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Cambia el estado de una sugerencia y la responde; avisa a su autor");

        grupo.MapPost("/sugerencias/{id:int}/eliminacion", async (
            int id, SuggestionService sugerencias, CancellationToken ct) =>
        {
            var (ok, mensaje) = await sugerencias.EliminarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Elimina una sugerencia del tablero");
    }

    // ── De entidad a DTO ─────────────────────────────────────────────────────────

    private static SolicitudDeVacacionesDelLiderDto ASolicitud(SolicitudDeVacacionesLeida v) => new(
        v.Id,
        v.DesarrolladorId,
        v.Desarrollador,
        v.Inicio,
        v.Fin,
        DocumentoDeVacacionesService.Dias(v.Inicio, v.Fin),
        v.Estado,
        DocumentoDeVacacionesService.Etiqueta(v.Estado),
        v.Comentario,
        v.RespuestaDelLider,
        v.TieneRespaldo,
        DocumentoDeVacacionesService.SePuedeResolver(v.Estado),
        DocumentoDeVacacionesService.SePuedeCancelar(v.Estado),
        v.DocumentoFirmado,
        v.FirmadoUtc,
        new FirmaDelColaboradorDto(
            v.FirmaDelColaborador.Vigente,
            v.FirmaDelColaborador.DejoDeValer,
            v.FirmaDelColaborador.FirmadaUtc,
            v.FirmaDelColaborador.PuedeVolverAFirmar),
        // La regla la escribe el servicio, igual que SePuedeResolver: si mañana el bloqueo alcanzara
        // también a las que nadie firmó, el botón se apagaría solo en vez de quedarse encendido por
        // una copia olvidada aquí. Y es el MISMO método que aplica la barrera al archivar.
        DocumentoDeVacacionesService.SePuedeArchivar(v.FirmaDelColaborador));

    private static FirmaDelLiderDto AFirma(SignatureProfile f) => new(
        f.Id, f.DisplayName, f.IsDefault, f.WidthPx, f.HeightPx, f.CreatedAtUtc);

    private static PermisoDelLiderDto APermiso(LeaveRequest l) => new(
        l.Id,
        l.DeveloperId,
        l.Developer?.FullName ?? "—",
        l.Type,
        LeaveRequestService.EtiquetaTipo(l.Type),
        l.Date,
        l.EndDate,
        l.DaysCount,
        l.Status,
        LeaveRequestService.Etiqueta(l.Status),
        l.Reason,
        l.Notes,
        l.ReviewComment,
        // El nombre basta como señal de que hay justificante: el servicio deja los dos campos en
        // nulo cuando no hay bytes, así que uno sin el otro no existe.
        l.AttachmentFileName != null,
        LoRegistroElLider: !l.EsSolicitudDelDesarrollador,
        l.ApprovedBy,
        l.Status == LeaveStatus.Pendiente,
        // La regla la escribe el servicio, no este mapeo: si mañana se pudiera corregir algo más que
        // lo pendiente, el botón aparecería solo en vez de quedarse escondido por una copia olvidada.
        LeaveRequestService.PuedeEditar(l.Status));

    /// <summary>
    /// Una sugerencia para el tablero del líder.
    ///
    /// <b>El autor solo viaja cuando la sugerencia NO es anónima.</b> La entidad lo trae siempre
    /// —hace falta para avisarle cuando la contesten— y mandarlo confiando en que la pantalla no lo
    /// pinte sería regalarlo: quien mire la respuesta de la API leería el nombre igual.
    /// </summary>
    private static SugerenciaDelLiderDto ASugerencia(Suggestion s, int votos) => new(
        s.Id,
        s.Title,
        s.Body,
        s.Category,
        SuggestionService.EtiquetaCategoria(s.Category),
        s.Status,
        SuggestionService.EtiquetaEstado(s.Status),
        SuggestionService.EtiquetaAlcance(s),
        s.Anonymous ? null : s.Developer?.FullName,
        s.Anonymous,
        s.Visibility == SuggestionVisibility.SoloAdministrador,
        s.SePuedeVotar,
        votos,
        s.AdminResponse,
        s.CreatedAt,
        s.ReviewedAt);

    /// <summary>
    /// El borrador que espera el servicio portado. Sin estado ni resolutor: los pone él, porque
    /// registrar un permiso ES concederlo y quien lo concede es quien tiene la sesión.
    /// </summary>
    private static LeaveRequest ABorrador(RegistroDePermisoRequest c) => new()
    {
        DeveloperId = c.DesarrolladorId,
        Type = c.Tipo,
        Date = c.Desde,
        DaysCount = c.Dias,
        Reason = c.Motivo,
        Notes = c.Notas
    };

    // ── Desplegables ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Las opciones de un desplegable, armadas desde el enum y no desde una lista escrita a mano: el
    /// día que se añada un valor aparece solo en la pantalla en lugar de quedarse fuera sin que nadie
    /// se entere.
    /// </summary>
    private static IReadOnlyList<OpcionDeFiltroDto<T>> Opciones<T>(Func<T, string> etiqueta)
        where T : struct, Enum =>
        Enum.GetValues<T>().Select(v => new OpcionDeFiltroDto<T>(v, etiqueta(v))).ToList();

    /// <summary>
    /// Los desarrolladores activos, como opciones de un desplegable. Salen del catálogo del área y
    /// no de las filas que se están enseñando: filtrando por una persona, una lista armada desde los
    /// datos se quedaría con esa sola y ya no habría forma de volver.
    /// </summary>
    private static async Task<IReadOnlyList<OpcionDeFiltroDto<int>>> DesarrolladoresAsync(
        CatalogosQueryService catalogos, CancellationToken ct) =>
        (await catalogos.DesarrolladoresAsync(ct: ct))
            .Select(d => new OpcionDeFiltroDto<int>(d.Id, d.Nombre))
            .ToList();

    // La etiqueta del estado de una actividad vive en DevActivityService.Etiqueta: la escriben la
    // rejilla y la exportación, y con una copia aquí las dos habrían acabado diciendo cosas distintas.

    // ── Respuestas ───────────────────────────────────────────────────────────────

    /// <summary>
    /// La respuesta de una operación de escritura. El mensaje lo escribió el servicio y se manda tal
    /// cual: explica el motivo en concreto —«Esa solicitud ya está «Aprobada»; no hay nada que
    /// resolver»— y eso es lo que permite saber qué hacer después.
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
