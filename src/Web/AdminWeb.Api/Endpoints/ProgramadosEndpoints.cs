using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Programados;
using Microsoft.AspNetCore.Mvc;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Los despliegues programados, el almacenamiento en Blob y el estado de los servidores.
///
/// <para><b>Dos políticas y no una.</b> La agenda y el estado son del área de despliegues
/// (<c>AdminUOperaciones</c>): agendar y mirar qué versión tiene cada servidor es exactamente su
/// trabajo. El almacenamiento es <c>SoloAdmin</c>, igual que en el escritorio, porque desde ahí se
/// borran versiones y respaldos directamente del contenedor — eso no es desplegar, es administrar.
/// Los servicios conservan además su <c>AuthorizationGuard</c>: dos barreras, no una.</para>
///
/// <para><b>La cadena de conexión de Blob no aparece en ninguna de estas rutas</b>, ni para leerla ni
/// para probarla. Se captura en la pantalla de configuración, se guarda cifrada y desde ahí solo se
/// puede saber si está puesta. Aceptar una por parámetro —como hacía el escritorio para poder probar
/// antes de guardar— convertiría este endpoint en un probador de credenciales de Azure ajenas.</para>
/// </summary>
public static class ProgramadosEndpoints
{
    /// <summary>Agendar y consultar el estado es del área de despliegues.</summary>
    private const string PoliticaDeDespliegues = "AdminUOperaciones";

    /// <summary>Tocar el contenedor directamente es del líder.</summary>
    private const string PoliticaDelLider = "SoloAdmin";

    /// <summary>
    /// Tope de una subida al contenedor.
    ///
    /// <para>Muy por encima del de los adjuntos (15 MB) porque aquí lo que se sube son paquetes de
    /// versión, que pesan lo que pesan. Sigue habiendo tope: sin uno, una sola petición puede llenar
    /// el disco temporal del servidor. Es también el techo de lo que un navegador puede sostener
    /// mientras el archivo viaja, así que subirlo más no daría más capacidad real.</para>
    /// </summary>
    public const long MaxBytesDeSubida = 256L * 1024 * 1024;

    public static void MapProgramadosEndpoints(this IEndpointRouteBuilder app)
    {
        MapAgenda(app);
        MapAlmacenamiento(app);
    }

    // ── Agenda y estado ─────────────────────────────────────────────────────────

    private static void MapAgenda(IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/programados").WithTags("Despliegues programados");

        grupo.MapGet("/", async (
            bool? soloPendientes, ProgramadosService agenda, IConfiguration configuracion,
            CancellationToken ct) =>
        {
            // El interruptor del trabajo de fondo lo sabe el alojamiento, no el servicio. Viaja hasta
            // la pantalla porque, apagado, agendar no sirve de nada y hay que decirlo antes.
            bool trabajoActivo = configuracion.GetValue("AdminWeb:TrabajosDeFondoActivos", false);
            return Results.Ok(await agenda.PantallaAsync(soloPendientes ?? true, trabajoActivo, ct));
        })
        .RequireAuthorization(PoliticaDeDespliegues)
        .WithSummary("La agenda de despliegues con lo necesario para agendar uno nuevo");

        grupo.MapPost("/agendar", async (
            ProgramarDespliegueRequest cuerpo, ProgramadosService agenda, CancellationToken ct) =>
        {
            var (ok, mensaje) = await agenda.ProgramarAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDeDespliegues)
        .WithSummary("Agenda un despliegue a un perfil o a una selección directa de servidores");

        grupo.MapPost("/{id:int}/cancelar", async (
            int id, ProgramadosService agenda, CancellationToken ct) =>
        {
            var (ok, mensaje) = await agenda.CancelarAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDeDespliegues)
        .WithSummary("Cancela una programación que todavía no se disparó");

        grupo.MapGet("/estado-de-servidores", async (
            bool? incluirDadosDeBaja, EstadoDeServidoresService estado, CancellationToken ct) =>
            Results.Ok(await estado.ObtenerAsync(incluirDadosDeBaja ?? false, ct)))
        .RequireAuthorization(PoliticaDeDespliegues)
        .WithSummary("Qué versión tiene hoy cada servidor, quién se la puso y cuándo");

        // Los DOS interruptores de la pantalla viajan, incluido «solo atrasados» —que allí filtra en
        // el navegador—: se baja LO QUE EL FILTRO ESTÁ ENSEÑANDO, la misma decisión de minutas. Uno de
        // los dos que no llegara aquí devolvería una hoja distinta de la tabla que se tiene delante.
        grupo.MapGet("/estado-de-servidores/excel", async (
            bool? incluirDadosDeBaja, bool? soloAtrasados, EstadoDeServidoresService estado,
            CancellationToken ct) =>
            ResultadosDeArchivo.Excel(
                await estado.ExcelAsync(incluirDadosDeBaja ?? false, soloAtrasados ?? false, ct),
                "EstadoDeServidores"))
        .RequireAuthorization(PoliticaDeDespliegues)
        .WithSummary("El estado de los servidores que el filtro enseña, en una hoja de cálculo");
    }

    // ── Almacenamiento ──────────────────────────────────────────────────────────

    private static void MapAlmacenamiento(IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/almacenamiento")
            .WithTags("Almacenamiento en Blob")
            .RequireAuthorization(PoliticaDelLider);

        grupo.MapGet("/", async (AlmacenamientoService almacen, CancellationToken ct) =>
            Results.Ok(await almacen.EstadoAsync(ct)))
        .WithSummary("Si el almacenamiento está configurado y qué carpetas tiene");

        // El nombre del blob va en la cadena de consulta y no en la ruta porque LLEVA BARRAS
        // («releases/QA/paquete.zip»): en la ruta cada barra sería un segmento más y el enrutador no
        // encontraría el endpoint.
        grupo.MapGet("/archivos", async (
            string? carpeta, AlmacenamientoService almacen, CancellationToken ct) =>
            Results.Ok(await almacen.ListarAsync(carpeta, ct)))
        .WithSummary("El contenido de una carpeta del contenedor");

        grupo.MapGet("/subcarpetas-de-versiones", async (
            AlmacenamientoService almacen, CancellationToken ct) =>
            Results.Ok(await almacen.SubcarpetasDeVersionesAsync(ct)))
        .WithSummary("Las subcarpetas de destino que se ofrecen para las versiones");

        grupo.MapGet("/metadatos", async (
            string blob, AlmacenamientoService almacen, CancellationToken ct) =>
            Results.Ok(await almacen.MetadatosAsync(blob, ct)))
        .WithSummary("Los metadatos actuales de un archivo, releídos del contenedor");

        grupo.MapGet("/descargar", async (
            string blob, HttpContext ctx, AlmacenamientoService almacen, CancellationToken ct) =>
        {
            var (contenido, nombre) = await almacen.AbrirParaDescargaAsync(blob, ct);

            // Siempre como descarga y con nosniff: el contenedor guarda paquetes y respaldos, no hay
            // nada que previsualizar, y dejar que el navegador adivine el tipo de un archivo que
            // alguien subió es justo cómo un HTML acaba ejecutándose en el dominio de la aplicación.
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(contenido, "application/octet-stream", nombre);
        })
        .WithSummary("Descarga un archivo del contenedor a través de la API");

        grupo.MapPost("/probar", async (AlmacenamientoService almacen, CancellationToken ct) =>
        {
            var (ok, mensaje) = await almacen.ProbarConexionAsync(ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Comprueba la conexión con la cuenta ya configurada");

        grupo.MapPost("/carpetas", async (
            CrearCarpetaEnBlobRequest cuerpo, AlmacenamientoService almacen, CancellationToken ct) =>
        {
            var (ok, mensaje) = await almacen.CrearCarpetaAsync(cuerpo.Ruta, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Crea una carpeta en el contenedor");

        grupo.MapPost("/carpetas/alcance", async (
            CarpetaRequest cuerpo, AlmacenamientoService almacen, CancellationToken ct) =>
            Results.Ok(await almacen.AlcanceDeBorradoAsync(cuerpo.Carpeta, ct)))
        .WithSummary("Cuántos elementos se llevaría por delante borrar una carpeta");

        grupo.MapPost("/carpetas/eliminar", async (
            CarpetaRequest cuerpo, AlmacenamientoService almacen, CancellationToken ct) =>
        {
            var (ok, mensaje) = await almacen.EliminarCarpetaAsync(cuerpo.Carpeta, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Elimina una carpeta y todo lo que cuelga de ella");

        grupo.MapPost("/eliminar", async (
            BlobRequest cuerpo, AlmacenamientoService almacen, CancellationToken ct) =>
        {
            var (ok, mensaje) = await almacen.EliminarAsync(cuerpo.Blob, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Elimina un archivo del contenedor");

        grupo.MapPost("/metadatos", async (
            GuardarMetadatosRequest cuerpo, AlmacenamientoService almacen, CancellationToken ct) =>
        {
            var (ok, mensaje) = await almacen.GuardarMetadatosAsync(cuerpo.Blob, cuerpo.Metadatos, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Reemplaza los metadatos de un archivo");

        grupo.MapPost("/enlace", async (
            EnlaceDeDescargaRequest cuerpo, AlmacenamientoService almacen, CancellationToken ct) =>
        {
            var (ok, mensaje, enlace) = await almacen.EnlaceDeDescargaAsync(cuerpo.Blob, cuerpo.Horas, ct);
            return ok && enlace is not null
                ? Results.Ok(enlace)
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .WithSummary("Firma un enlace temporal de descarga para un archivo");

        // Declara IFormFile a propósito: eso es lo que marca el endpoint como formulario y hace que
        // el middleware de antiforgery le exija su testigo. Un cuerpo JSON no lo necesita —el
        // navegador pregunta antes y sin política CORS no pasa—, pero un formulario multipart desde
        // otro sitio sí llegaría con la cookie de sesión puesta.
        grupo.MapPost("/subir", async (
            IFormFile archivo, [FromForm] string? carpeta, [FromForm] bool? sobrescribir,
            AlmacenamientoService almacen, CancellationToken ct) =>
        {
            if (archivo.Length == 0)
                return Results.BadRequest(new ResultadoDto(false, "No llegó ningún archivo."));

            // En FLUJO hacia Azure, sin materializarlo en memoria: un paquete de versión de cientos
            // de megas cargado entero en el montón tumbaría el servidor cada vez que alguien sube uno.
            await using var contenido = archivo.OpenReadStream();

            var (ok, mensaje) = await almacen.SubirAsync(
                carpeta, archivo.FileName, contenido, sobrescribir ?? false, ct);

            return Resultado(ok, mensaje);
        })
        .WithMetadata(new RequestSizeLimitAttribute(MaxBytesDeSubida))
        .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxBytesDeSubida })
        .WithSummary("Sube un archivo a una carpeta del contenedor");
    }

    /// <summary>
    /// Un rechazo de negocio sale como 400 con SU mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo concreto —«la hora programada debe ser al menos un minuto en el
    /// futuro»—; eso es lo que se enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));
}
