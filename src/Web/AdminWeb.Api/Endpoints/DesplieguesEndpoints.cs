using System.Text;
using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Despliegues;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Los despliegues: el catálogo de lo que se publica, los servidores a los que va, los perfiles que
/// los agrupan, el despliegue en sí y su historial.
///
/// <para><b>Dos alcances y por eso dos políticas.</b> Desplegar, consultar y dar de alta un servidor
/// son de <c>AdminUOperaciones</c>: Operaciones es quien despliega y quien conoce el destino nuevo.
/// Administrar el catálogo —sistemas, versiones, perfiles— y CORREGIR o dar de baja un servidor son
/// de <c>SoloAdmin</c>: un servidor mal editado rompe despliegues de todo el equipo, y un perfil
/// habilitado de más le regala a Operaciones un destino que nadie revisó. Es el mismo reparto que el
/// escritorio resolvía escondiendo botones, puesto ahora donde de verdad cuenta.</para>
///
/// <para><b>Lanzar y mirar están separados a propósito.</b> <c>POST /lanzar</c> devuelve en cuanto el
/// trabajo arranca; lo que pasa después llega por el canal en vivo, y <c>GET /trabajo/{id}</c> existe
/// para ponerse al día al abrir la pantalla o al volver de una reconexión. Cerrar la pestaña no
/// cancela nada: para eso está <c>POST /trabajo/{id}/cancelar</c>, que hay que pedir a propósito.</para>
///
/// <para><b>Ninguna respuesta lleva la contraseña de un servidor</b>, ni cifrada. Se puede saber si
/// está puesta y se puede reemplazar; leerla, no.</para>
/// </summary>
public static class DesplieguesEndpoints
{
    /// <summary>Desplegar y consultar: del líder y de Operaciones.</summary>
    private const string PoliticaDelDespliegue = "AdminUOperaciones";

    /// <summary>El catálogo y la corrección de servidores: del líder.</summary>
    private const string PoliticaDelLider = "SoloAdmin";

    public static void MapDesplieguesEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/despliegues").WithTags("Despliegues");

        // ── Al abrir la pantalla ─────────────────────────────────────────────────

        grupo.MapGet("/opciones", async (DespliegueQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.OpcionesAsync(ct)))
        .RequireAuthorization(PoliticaDelDespliegue)
        .WithSummary("El checklist obligatorio, las reglas y lo que puede hacer quien mira");

        // ── Sistemas y versiones ─────────────────────────────────────────────────

        grupo.MapGet("/sistemas", async (DespliegueQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.SistemasAsync(ct)))
        .RequireAuthorization(PoliticaDelDespliegue)
        .WithSummary("Los sistemas y cuántas versiones tiene cada uno");

        grupo.MapPost("/sistemas/nuevo", async (
            GuardarSistemaRequest cuerpo, DespliegueCatalogoService catalogo, CancellationToken ct) =>
            Resultado(await catalogo.CrearSistemaAsync(cuerpo, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Da de alta un sistema");

        grupo.MapPost("/sistemas/{id:int}/editar", async (
            int id, GuardarSistemaRequest cuerpo, DespliegueCatalogoService catalogo, CancellationToken ct) =>
            Resultado(await catalogo.EditarSistemaAsync(id, cuerpo, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Cambia el nombre, la descripción o la carpeta por omisión de un sistema");

        grupo.MapPost("/sistemas/{id:int}/alternar", async (
            int id, DespliegueCatalogoService catalogo, CancellationToken ct) =>
            // «Alternar» y no «eliminar», con toda intención: borrar un sistema arrastraba en cascada
            // sus versiones y con ellas el historial de todo lo que se desplegó desde él.
            Resultado(await catalogo.AlternarSistemaAsync(id, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Da de baja o reactiva un sistema (no lo borra)");

        grupo.MapGet("/versiones", async (
            int? sistemaId, DespliegueQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.VersionesAsync(sistemaId, ct)))
        .RequireAuthorization(PoliticaDelDespliegue)
        .WithSummary("Las versiones, con su changelog y si el servidor alcanza su paquete");

        grupo.MapGet("/paquetes", async (DespliegueQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.PaquetesAsync(ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Los .zip que hay en la carpeta de despliegue y aún no son una versión");

        grupo.MapPost("/versiones/nueva", async (
            CrearVersionRequest cuerpo, DespliegueCatalogoService catalogo, CancellationToken ct) =>
            Resultado(await catalogo.CrearVersionAsync(cuerpo, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Registra como versión un paquete que ya está en la carpeta del servidor");

        // La otra vía de entrada de una versión: un artefacto que la compilación automática ya dejó
        // en el almacén. Faltaba porque el servicio de Blob se portó en paralelo y nadie unió las dos
        // piezas; sin esto, un paquete correctamente subido no había forma de registrarlo.
        grupo.MapPost("/versiones/nueva-del-almacen", async (
            CrearVersionDesdeAlmacenRequest cuerpo, DespliegueCatalogoService catalogo,
            AlmacenamientoService almacenamiento, CancellationToken ct) =>
            Resultado(await catalogo.CrearVersionDesdeAlmacenAsync(almacenamiento, cuerpo, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Registra como versión un paquete que ya está en Azure Blob Storage");

        grupo.MapPost("/versiones/{id:int}/editar", async (
            int id, EditarVersionRequest cuerpo, DespliegueCatalogoService catalogo, CancellationToken ct) =>
            Resultado(await catalogo.EditarVersionAsync(id, cuerpo, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Corrige el changelog, la carpeta destino y —si aún no se desplegó— la etiqueta");

        grupo.MapPost("/versiones/{id:int}/eliminar", async (
            int id, DespliegueCatalogoService catalogo, CancellationToken ct) =>
            Resultado(await catalogo.EliminarVersionAsync(id, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Quita del catálogo una versión que nunca se desplegó");

        // ── Servidores ───────────────────────────────────────────────────────────

        grupo.MapGet("/servidores", async (DespliegueQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.ServidoresAsync(ct)))
        .RequireAuthorization(PoliticaDelDespliegue)
        .WithSummary("Los servidores con lo que tienen desplegado hoy (sin contraseñas)");

        // Alta con la política del despliegue: Operaciones SÍ puede crear servidores, porque es quien
        // conoce el destino nuevo. Corregir uno existente ya no, y por eso va aparte.
        grupo.MapPost("/servidores/nuevo", async (
            GuardarServidorRequest cuerpo, DeploymentTargetService servidores, CancellationToken ct) =>
            Resultado(await servidores.CrearAsync(cuerpo, ct)))
        .RequireAuthorization(PoliticaDelDespliegue)
        .WithSummary("Da de alta un servidor de destino");

        // Alta MASIVA desde un JSON, como en el escritorio. Es del LÍDER y no de Operaciones,
        // aunque el alta de uno en uno sí lo sea: aquí un archivo puede reescribir de golpe el
        // inventario entero —incluidas las contraseñas de servidores que ya estaban— y eso es
        // corregir lo existente, no dar de alta un destino nuevo.
        grupo.MapPost("/servidores/importar", async Task<IResult> (
            IFormFile archivo, DeploymentTargetService servidores, CancellationToken ct) =>
        {
            // Tope pequeño y a propósito: es un JSON de texto con unas decenas de servidores. Un
            // archivo de megabytes aquí es un error, no un inventario.
            const long tope = 1024 * 1024;
            if (archivo.Length is 0 or > tope)
                return Resultado((false,
                    $"El archivo está vacío o pasa de {tope / 1024} KB. Es un JSON de texto."));

            using var lector = new StreamReader(archivo.OpenReadStream());
            var json = await lector.ReadToEndAsync(ct);

            var (ok, mensaje, _, _) = await servidores.ImportarDesdeJsonAsync(json, ct);
            return Resultado((ok, mensaje));
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Da de alta o actualiza varios servidores desde un JSON");

        grupo.MapPost("/servidores/{id:int}/editar", async (
            int id, GuardarServidorRequest cuerpo, DeploymentTargetService servidores, CancellationToken ct) =>
            // Contraseña vacía = conservar la que tiene. No es una comodidad: como nunca viaja de
            // vuelta al navegador, el formulario no puede reenviarla.
            Resultado(await servidores.EditarAsync(id, cuerpo, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Corrige un servidor existente");

        grupo.MapPost("/servidores/{id:int}/baja", async (
            int id, MotivoDespliegueRequest? cuerpo, DeploymentTargetService servidores, CancellationToken ct) =>
            Resultado(await servidores.DesactivarAsync(id, cuerpo?.Motivo, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Da de baja un servidor (conserva su historial)");

        grupo.MapPost("/servidores/{id:int}/alta", async (
            int id, DeploymentTargetService servidores, CancellationToken ct) =>
            Resultado(await servidores.ReactivarAsync(id, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Reactiva un servidor dado de baja");

        // ── Perfiles ─────────────────────────────────────────────────────────────

        grupo.MapGet("/perfiles", async (DespliegueQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.PerfilesAsync(ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Los perfiles de destino guardados");

        grupo.MapPost("/perfiles/nuevo", async (
            GuardarPerfilRequest cuerpo, DespliegueCatalogoService catalogo, CancellationToken ct) =>
            Resultado(await catalogo.GuardarPerfilAsync(null, cuerpo, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Crea un perfil con sus servidores");

        grupo.MapPost("/perfiles/{id:int}/editar", async (
            int id, GuardarPerfilRequest cuerpo, DespliegueCatalogoService catalogo, CancellationToken ct) =>
            Resultado(await catalogo.GuardarPerfilAsync(id, cuerpo, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Deja el perfil exactamente con estos servidores");

        grupo.MapPost("/perfiles/{id:int}/eliminar", async (
            int id, DespliegueCatalogoService catalogo, CancellationToken ct) =>
            Resultado(await catalogo.EliminarPerfilAsync(id, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Elimina un perfil que no aparece en ningún despliegue");

        // ── El despliegue ────────────────────────────────────────────────────────

        grupo.MapPost("/lanzar", async (
            LanzarDespliegueRequest cuerpo, DeploymentService despliegues, CancellationToken ct) =>
        {
            // El token de la PETICIÓN llega hasta aquí y no más allá: lo que valida y registra el
            // trabajo sí depende de que la petición siga viva; el despliegue en sí, no. Ese es todo
            // el cambio de fondo de esta pantalla respecto al escritorio.
            //
            // Lanzar RESERVA los servidores antes de contestar, así que este 400 tiene dos motivos
            // nuevos que el cliente enseña tal cual: que el destino ya esté recibiendo otro
            // despliegue —lo vea esta instancia de la API o cualquier otra— y, muy de vez en cuando,
            // que dos personas hayan pulsado el botón en el mismo instante y a una le toque repetir.
            // Ninguno de los dos es un error del sistema: son la respuesta correcta.
            var (ok, mensaje, jobId) = await despliegues.LanzarAsync(cuerpo, ct);
            return ok
                ? Results.Ok(new DespliegueLanzadoDto(jobId!.Value, mensaje))
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .RequireAuthorization(PoliticaDelDespliegue)
        .WithSummary("Pone un despliegue en marcha en el servidor y devuelve su identificador");

        grupo.MapGet("/en-curso", (DespliegueQueryService consultas) =>
            Results.Ok(consultas.EnCurso()))
        .RequireAuthorization(PoliticaDelDespliegue)
        .WithSummary("Los despliegues que el servidor tiene entre manos ahora mismo");

        grupo.MapGet("/trabajo/{jobId:int}", (int jobId, DespliegueQueryService consultas) =>
            consultas.Trabajo(jobId) is { } trabajo
                ? Results.Ok(trabajo)
                : NoExiste("Ese despliegue ya no está en curso. Búscalo en el historial para ver cómo terminó."))
        .RequireAuthorization(PoliticaDelDespliegue)
        .WithSummary("El avance y la bitácora acumulada de un despliegue, para retomar su vista");

        grupo.MapPost("/trabajo/{jobId:int}/cancelar", (int jobId, DeploymentService despliegues) =>
            Resultado(despliegues.Cancelar(jobId)))
        .RequireAuthorization(PoliticaDelDespliegue)
        .WithSummary("Cancela un despliegue en curso (acción deliberada)");

        // ── Historial ────────────────────────────────────────────────────────────

        grupo.MapGet("/historial", async (DespliegueQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.HistorialAsync(ct)))
        .RequireAuthorization(PoliticaDelDespliegue)
        .WithSummary("Los últimos despliegues: qué, a dónde, quién y cómo terminó");

        grupo.MapGet("/historial/{jobId:int}", async (
            int jobId, DespliegueQueryService consultas, CancellationToken ct) =>
            await consultas.ExpedienteAsync(jobId, ct) is { } expediente
                ? Results.Ok(expediente)
                : NoExiste("Ese despliegue ya no existe. Actualiza la lista."))
        .RequireAuthorization(PoliticaDelDespliegue)
        .WithSummary("El expediente de un despliegue: checklist confirmado y log completo");

        grupo.MapGet("/historial/{jobId:int}/evidencia", async (
            int jobId, DespliegueQueryService consultas, CancellationToken ct) =>
        {
            // Texto plano a propósito: la evidencia se entrega y se lee sin esta aplicación y sin una
            // hoja de cálculo. Es la misma decisión que tomó el escritorio.
            var archivo = await consultas.EvidenciaAsync(jobId, ct);
            return archivo is not { } a
                ? NoExiste("Ese despliegue ya no existe. Actualiza la lista.")
                : Results.File(Encoding.UTF8.GetBytes(a.contenido), "text/plain; charset=utf-8", a.nombre);
        })
        .RequireAuthorization(PoliticaDelDespliegue)
        .WithSummary("La evidencia de un despliegue, en un archivo de texto descargable");
    }

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto —«ese servidor está recibiendo otro despliegue»,
    /// «falta confirmar el checklist previo»—; eso es lo que se enseña.
    /// </summary>
    private static IResult Resultado((bool ok, string mensaje) r) =>
        r.ok ? Results.Ok(new ResultadoDto(true, r.mensaje))
             : Results.BadRequest(new ResultadoDto(false, r.mensaje));

    /// <summary>
    /// Lo que se pidió ya no está. Va con el mismo contrato que los rechazos para que el cliente lo
    /// lea por el mismo camino y enseñe el texto en vez de un «no se pudo» genérico.
    /// </summary>
    private static IResult NoExiste(string mensaje) =>
        Results.NotFound(new ResultadoDto(false, mensaje));
}
