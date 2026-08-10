using AdminWeb.Application.Services;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.DevOps;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// La integración con Azure DevOps: el tablero del líder, el tablero por etiqueta y los tickets de
/// cada desarrollador.
///
/// <b>LA REGLA QUE GOBIERNA ESTE ARCHIVO: ningún endpoint devuelve un PAT.</b> Ni entero, ni
/// recortado, ni «para comprobar que quedó bien guardado». De un token solo se puede preguntar si
/// está configurado (<c>/estado</c>) y se puede escribir uno nuevo (<c>/mi-token</c>). En el
/// escritorio el token vivía en la máquina de cada quien y el proceso lo leía directamente; aquí las
/// llamadas a DevOps las hace la API EN NOMBRE de la persona, leyendo su token del lado del servidor.
/// Un endpoint que lo devolviera lo dejaría a la vista de cualquiera que abra la consola del
/// navegador, y un token con «Work Items → Read & write» alcanza para reescribir el tablero entero.
///
/// <b>Dos alcances y por eso dos políticas</b>, que es la traducción de lo que el escritorio decidía
/// escondiendo pantallas del menú:
///
///  · El TABLERO y el tablero POR ETIQUETA son del líder (<c>SoloAdmin</c>), igual que reasignar,
///    mover de columna, sincronizar la instalación entera, las reglas, los filtros guardados y la
///    importación a requerimientos. Repartir trabajo y decidir el alcance es suyo.
///  · MIS TICKETS es de quien tiene la sesión (<c>AdminUDesarrollador</c>), y <b>su ruta no lleva
///    identificador</b>, igual que las de la jornada y el pool: con un <c>/{devId}</c> habría que
///    comprobar en cada endpoint que es el propio.
///
/// <b>Lo que un desarrollador puede tocar de un ticket se comprueba contra la FILA</b>, no contra la
/// pantalla. En el escritorio bastaba con que su pantalla solo listara lo suyo; aquí a la API se la
/// puede llamar sin pasar por el cliente, así que comentar, estimar, cambiar prioridad y ver la ficha
/// exigen que el ticket esté a su nombre en DevOps. Esa comprobación vive en
/// <see cref="DevOpsService"/> — dos barreras, no una.
///
/// <b>Un fallo de la integración se contesta con un mensaje, no con un 500.</b> Sin token, con uno
/// caducado o con DevOps caído, el servicio devuelve <c>(false, mensaje)</c> y aquí sale como 400 con
/// ese texto. Lo escribió quien sabe qué pasó y se enseña TAL CUAL.
/// </summary>
public static class DevOpsEndpoints
{
    /// <summary>El tablero, la sincronización completa y todo lo que reparte trabajo: del líder.</summary>
    private const string PoliticaDelLider = "SoloAdmin";

    /// <summary>Lo propio de cada quien: del líder y del desarrollador.</summary>
    private const string PoliticaDelEquipo = "AdminUDesarrollador";

    public static void MapDevOpsEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/devops").WithTags("Azure DevOps");

        // ── Estado de la integración y token personal ────────────────────────────

        grupo.MapGet("/estado", async (DevOpsService devops, CancellationToken ct) =>
            Results.Ok(await devops.EstadoAsync(ct)))
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Si se puede hablar con DevOps y si hay token configurado (nunca cuál)");

        grupo.MapPost("/mi-token", async (
            GuardarMiPatRequest cuerpo, DevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje) = await devops.GuardarMiPatAsync(cuerpo.Pat, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Guarda o reemplaza mi token personal (solo de subida: no hay vuelta)");

        grupo.MapPost("/mi-token/eliminar", async (DevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje) = await devops.BorrarMiPatAsync(ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Quita mi token personal");

        grupo.MapPost("/mi-token/probar", async (
            ProbarPatRequest? cuerpo, DevOpsService devops, CancellationToken ct) =>
        {
            // El token que viene en el cuerpo se prueba SIN guardarlo, como el diálogo del escritorio:
            // así se sabe si sirve antes de reemplazar el que ya funciona. Vacío prueba el configurado.
            var (ok, mensaje) = await devops.ProbarAsync(cuerpo?.Pat, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Prueba la conexión y dice con qué cuenta identifica DevOps al token");

        // ── Tablero del líder ────────────────────────────────────────────────────

        grupo.MapGet("/tablero", async (DevOpsQueryService consultas, CancellationToken ct) =>
            Results.Ok(await consultas.TableroAsync(ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Los tickets sincronizados con sus filtros guardados, reglas y desplegables");

        grupo.MapGet("/exportar", async (DevOpsQueryService consultas, CancellationToken ct) =>
            ResultadosDeArchivo.Excel(await consultas.ExportarTableroAsync(ct), "azure_devops"))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("El tablero completo en una hoja de cálculo");

        grupo.MapPost("/sincronizar", async (
            SincronizarDevOpsRequest? cuerpo, DevOpsService devops, CancellationToken ct) =>
        {
            // Sin cuerpo es la sincronización completa; con él, la selectiva. Es un solo endpoint
            // porque es una sola operación: dos rutas para lo mismo serían dos sitios que mantener.
            var filtro = cuerpo is null
                ? null
                : new FiltroDeSincronizacion(
                    cuerpo.Tipos ?? [], cuerpo.Correos ?? [], cuerpo.Estados ?? [],
                    SoloMisAsignados: false, CambiadosEnDias: cuerpo.CambiadosEnDias);

            var resultado = await devops.SincronizarAsync(filtro, ct);
            return resultado.Ok ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Trae los work items del proyecto (completa o selectiva)");

        grupo.MapPost("/importar", async (DevOpsService devops, CancellationToken ct) =>
        {
            var resultado = await devops.ImportarComoRequerimientosAsync(ct);
            return resultado.Ok ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Da de alta como requerimientos los work items asignados y abiertos");

        // ── Acciones sobre un ticket ─────────────────────────────────────────────

        grupo.MapPost("/{numero:int}/reasignar", async (
            int numero, ReasignarTicketRequest? cuerpo, DevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje) = await devops.ReasignarAsync(numero, cuerpo?.Correo, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Reasigna el work item en DevOps (correo vacío lo desasigna)");

        grupo.MapPost("/{numero:int}/estado", async (
            int numero, CambiarEstadoRequest cuerpo, DevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje) = await devops.CambiarEstadoAsync(numero, cuerpo.Estado, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Mueve el work item de columna; DevOps valida la transición");

        // Del equipo y no solo del líder: en el escritorio el desarrollador cambiaba la prioridad de
        // SUS tickets desde su pantalla. Que sea suyo lo comprueba el servicio contra la fila.
        grupo.MapPost("/{numero:int}/prioridad", async (
            int numero, CambiarPrioridadRequest cuerpo, DevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje) = await devops.CambiarPrioridadAsync(numero, cuerpo.Prioridad, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Cambia la prioridad en DevOps (1 muy alta … 4 baja)");

        grupo.MapPost("/{numero:int}/estimar", async (
            int numero, EstimarTicketRequest cuerpo, DevOpsService devops, CancellationToken ct) =>
        {
            // Se responde 200 aunque DevOps rechace el campo Effort: la estimación SÍ quedó guardada y
            // el mensaje lo explica. Devolver un fallo haría que la persona la volviera a capturar.
            var (ok, mensaje) = await devops.EstimarAsync(numero, cuerpo.Horas, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Guarda la estimación aquí y en el campo Effort del work item");

        grupo.MapPost("/{numero:int}/vigilar", async (
            int numero, DevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje) = await devops.AlternarVigilanciaAsync(numero, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Empieza o deja de vigilar el ticket (la lista es personal)");

        grupo.MapGet("/{numero:int}/ficha", async (
            int numero, DevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje, ficha) = await devops.FichaAsync(numero, ct);
            return ok && ficha is not null
                ? Results.Ok(ficha)
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Regresiones (bugs hijos) y por cuántas manos pasó el ticket");

        // ── Comentarios ──────────────────────────────────────────────────────────

        grupo.MapGet("/{numero:int}/comentarios", async (
            int numero, DevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje, datos) = await devops.ComentariosAsync(numero, ct);
            return ok && datos is not null
                ? Results.Ok(datos)
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Los comentarios del ticket, en texto plano");

        // Multipart SIEMPRE, lleve o no evidencias. Podría ir en JSON cuando no las lleva, pero
        // entonces la misma operación tendría dos formas y habría que acertar con cuál mandar.
        //
        // Declarar IFormCollection es lo que marca el endpoint como formulario y hace que el testigo
        // antifalsificación sea obligatorio, que es justo lo que multipart necesita: una escritura en
        // JSON no puede provocarse desde otro sitio, pero un formulario alojado en cualquier página
        // sí llegaría aquí con la cookie de sesión puesta. Lo adjunta ClienteApi.SubirAsync.
        grupo.MapPost("/{numero:int}/comentarios", async (
            int numero, IFormCollection formulario, DevOpsService devops, CancellationToken ct) =>
        {
            var (evidencias, error) = await LeerEvidenciasAsync(formulario.Files, ct);
            if (error != null) return Resultado(false, error);

            var (ok, mensaje) = await devops.ComentarAsync(numero, formulario["texto"], evidencias, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Publica un comentario en el ticket, con capturas si las hay");

        // ── Filtros guardados de la rejilla ──────────────────────────────────────

        grupo.MapPost("/filtros", async (
            GuardarFiltroRequest cuerpo, DevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje) = await devops.GuardarFiltroAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Guarda (o reemplaza, por nombre) una vista de la rejilla");

        grupo.MapPost("/filtros/{id:int}/eliminar", async (
            int id, DevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje) = await devops.BorrarFiltroAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Elimina un filtro guardado");

        // ── Reglas de auto-asignación ────────────────────────────────────────────

        grupo.MapPost("/reglas", async (
            GuardarReglaRequest cuerpo, DevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje) = await devops.GuardarReglaAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Alta o edición de una regla de auto-asignación");

        grupo.MapPost("/reglas/{id:int}/eliminar", async (
            int id, DevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje) = await devops.BorrarReglaAsync(id, ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Elimina una regla de auto-asignación");

        // ── Tablero por etiqueta ─────────────────────────────────────────────────

        grupo.MapGet("/etiquetas", async (
            DevOpsQueryService consultas, CancellationToken ct,
            string? dentroDe = null, bool soloAbiertos = false) =>
            Results.Ok(await consultas.PorEtiquetaAsync(Vacio(dentroDe), soloAbiertos, ct)))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("Cuántos tickets, bugs y tareas hay por etiqueta");

        grupo.MapGet("/etiquetas/exportar", async (
            DevOpsQueryService consultas, CancellationToken ct,
            string? dentroDe = null, bool soloAbiertos = false) =>
            ResultadosDeArchivo.Excel(
                await consultas.ExportarPorEtiquetaAsync(Vacio(dentroDe), soloAbiertos, ct),
                "devops_por_etiqueta"))
        .RequireAuthorization(PoliticaDelLider)
        .WithSummary("El tablero por etiqueta en una hoja de cálculo");

        // ── Mis tickets ──────────────────────────────────────────────────────────

        grupo.MapGet("/mis-tickets", async (
            DevOpsQueryService consultas, CancellationToken ct,
            string? texto = null, string? estado = null, string? tipo = null, string? iteracion = null,
            bool soloAbiertos = true, bool soloSinEstimar = false,
            int? dias = MyDevOpsTicketFilter.DiasPorOmision) =>
        {
            var filtro = new FiltroDeMisTickets(
                Vacio(texto), Vacio(estado), Vacio(tipo), Vacio(iteracion),
                soloAbiertos, soloSinEstimar, dias);

            return Results.Ok(await consultas.MisTicketsAsync(filtro, ct));
        })
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Mis work items de DevOps, ya filtrados");

        grupo.MapPost("/mis-tickets/sincronizar", async (
            SincronizarMisTicketsRequest? cuerpo, DevOpsService devops, CancellationToken ct) =>
        {
            var dias = cuerpo?.Dias ?? MyDevOpsTicketFilter.DiasPorOmision;
            var resultado = await devops.SincronizarMisTicketsAsync(dias, ct);
            return resultado.Ok ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Trae de DevOps lo asignado a la cuenta de MI token");

        // Del EQUIPO y no del líder, a diferencia de <c>/importar</c>: aquel reparte el trabajo de
        // todos según las reglas, y esto solo baja a las asignaciones propias lo que YA está a nombre
        // de quien lo pide. Sin identificador en la ruta, como el resto de lo propio.
        grupo.MapPost("/mis-tickets/materializar", async (DevOpsService devops, CancellationToken ct) =>
        {
            var (ok, mensaje) = await devops.MaterializarMisAsignadosAsync(ct);
            return Resultado(ok, mensaje);
        })
        .RequireAuthorization(PoliticaDelEquipo)
        .WithSummary("Trae mis tickets abiertos a «Mis asignaciones» para poder cronometrarlos");
    }

    /// <summary>
    /// Las capturas que acompañan a un comentario.
    ///
    /// El número y el tamaño se comprueban aquí para rechazar pronto lo que no cabe; que los bytes
    /// sean de verdad una imagen lo comprueba el servicio, que es por donde pasa todo lo que se sube
    /// a DevOps.
    ///
    /// Lo comparte «registrar avance» de los SLA (<see cref="SlaEndpoints"/>), que acaba publicando
    /// por este mismo camino: dos copias de los límites acabarían diciendo cosas distintas sobre la
    /// misma subida.
    /// </summary>
    internal static async Task<(IReadOnlyList<(string nombre, byte[] contenido)> evidencias, string? error)>
        LeerEvidenciasAsync(IFormFileCollection archivos, CancellationToken ct)
    {
        var imagenes = archivos.GetFiles("imagenes");
        if (imagenes.Count == 0) return ([], null);

        if (imagenes.Count > MaxEvidencias)
            return ([], $"No se pueden adjuntar más de {MaxEvidencias} evidencias en un comentario.");

        var resultado = new List<(string, byte[])>(imagenes.Count);
        foreach (var archivo in imagenes)
        {
            if (archivo.Length > ArchivosSubidos.MaxBytes)
                return ([], $"«{archivo.FileName}» pasa del tamaño máximo permitido.");

            using var memoria = new MemoryStream();
            await archivo.CopyToAsync(memoria, ct);
            resultado.Add((archivo.FileName, memoria.ToArray()));
        }

        return (resultado, null);
    }

    /// <summary>Cuántas capturas caben en un comentario. Cada una es una subida aparte a DevOps.</summary>
    private const int MaxEvidencias = 5;

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto —«tu token expiró», «el ticket #123 no está a tu
    /// nombre»—; eso es lo que se enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));

    /// <summary>Un parámetro de consulta en blanco es «sin filtro», no un filtro por cadena vacía.</summary>
    private static string? Vacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
