using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Plantillas;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// La biblioteca de plantillas. Es LA MISMA pantalla para dos roles, igual que en el escritorio: el
/// líder la ve completa y el desarrollador en solo consulta.
///
/// La política del grupo es «AdminUDesarrollador», que es quién puede ENTRAR. QUÉ ve cada uno lo
/// decide <see cref="TemplateService"/> en su consulta <c>Legibles()</c> —hoy, al desarrollador
/// solo los comentarios de Azure DevOps y nada archivado— y aquí no se repite ni se ajusta: si el
/// filtro viviera también en el endpoint, el día que cambie habría dos sitios que corregir y uno se
/// quedaría atrás.
///
/// Fase 1 es solo lectura, así que tampoco existe aquí el contador de usos que el escritorio subía
/// al copiar: es una escritura, y su sitio es la fase en la que la pantalla escriba.
/// </summary>
public static class PlantillasEndpoints
{
    public static void MapPlantillasEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/plantillas")
            .WithTags("Plantillas")
            .RequireAuthorization("AdminUDesarrollador");

        grupo.MapGet("/", async (
            TemplateKind? tipo, string? texto, bool? archivadas,
            TemplateService plantillas, CancellationToken ct) =>
        {
            var filas = await plantillas.ListarResumenAsync(tipo, texto, archivadas ?? false, ct);
            return Results.Ok(filas.Select(ADtoDeLista).ToList());
        })
        .WithSummary("Plantillas legibles por esta sesión, las más usadas primero");

        grupo.MapGet("/{id:int}", async (int id, TemplateService plantillas, CancellationToken ct) =>
        {
            var t = await plantillas.ObtenerAsync(id, ct);
            // 404 y no 403 cuando la plantilla existe pero esta sesión no puede leerla: distinguir
            // los dos casos le confirmaría al desarrollador que hay un script SQL en el id 47.
            return t == null ? Results.NotFound() : Results.Ok(ADtoDeDetalle(t));
        })
        .WithSummary("Una plantilla con su contenido y sus marcadores");

        grupo.MapGet("/conteos", async (TemplateService plantillas, CancellationToken ct) =>
        {
            var conteos = await plantillas.ConteoPorTipoAsync(ct);
            return Results.Ok(conteos
                .Select(kv => new ConteoDePlantillasDto(
                    kv.Key, TemplateService.EtiquetaTipo(kv.Key), TemplateService.IconoTipo(kv.Key), kv.Value))
                .OrderByDescending(c => c.Cuantas)
                .ToList());
        })
        .WithSummary("Cuántas plantillas activas hay de cada tipo");

        // Los tipos del desplegable de filtro los decide el SERVIDOR y no el cliente: al
        // desarrollador no se le ofrecen tipos que la consulta jamás le devolverá, porque elegir
        // «Script SQL» y ver la lista vacía se lee como un error de la aplicación. Es la misma
        // decisión que el escritorio tomó al construir su combo.
        grupo.MapGet("/tipos", (ICurrentUser actual) =>
        {
            var tipos = actual.IsAdmin ? Enum.GetValues<TemplateKind>() : TemplateService.TiposDelEquipo;
            return Results.Ok(tipos
                .Select(k => new OpcionDto(
                    (int)k, $"{TemplateService.IconoTipo(k)}  {TemplateService.EtiquetaTipo(k)}"))
                .ToList());
        })
        .WithSummary("Los tipos de plantilla que esta sesión puede filtrar");

        // Es un POST porque los valores capturados no caben en una dirección, pero NO ESCRIBE NADA:
        // lee la plantilla y devuelve el texto con los huecos rellenos. La fase 1 sigue siendo de
        // solo lectura — en la base no cambia ni una fila.
        grupo.MapPost("/{id:int}/rellenar", async (
            int id, RellenoDePlantillaDto cuerpo, TemplateService plantillas, CancellationToken ct) =>
        {
            var t = await plantillas.ObtenerAsync(id, ct);
            if (t == null) return Results.NotFound();

            var texto = plantillas.Rellenar(t.Body, cuerpo.Valores ?? []);
            return Results.Ok(new TextoDePlantillaDto(texto));
        })
        .WithSummary("Devuelve el cuerpo con sus marcadores rellenos (no modifica nada)");

        // El binario NUNCA viaja dentro del DTO de la lista: se descarga por su propia ruta, y solo
        // cuando alguien lo pide. Sirve el adjunto si lo hay y, si no, el cuerpo convertido a
        // archivo — que es el «💾 Guardar como…» del escritorio.
        grupo.MapGet("/{id:int}/archivo", async (
            int id, TemplateService plantillas, CancellationToken ct) =>
        {
            var t = await plantillas.ObtenerAsync(id, ct);
            if (t == null) return Results.NotFound();

            if (t.FileBytes is { Length: > 0 })
                return Results.File(t.FileBytes, "application/octet-stream",
                    TemplateService.NombreArchivoSugerido(t));

            // La codificación la decide el tipo: un .ps1 sin BOM lo lee Windows PowerShell 5.1 como
            // ANSI y cualquier acento sale corrupto al ejecutarlo. Es la misma regla que el
            // escritorio aplicaba al escribir a disco; aquí se aplica al armar la respuesta.
            var bytes = TemplateService.CodificacionArchivo(t.Kind).GetBytes(t.Body);
            return Results.File(bytes, "application/octet-stream",
                TemplateService.NombreArchivoSugerido(t));
        })
        .WithSummary("Descarga el adjunto de la plantilla o, si no lo tiene, su cuerpo como archivo");
    }

    private static PlantillaListaDto ADtoDeLista(TemplateResumen t) => new(
        t.Id, t.Kind, TemplateService.EtiquetaTipo(t.Kind), TemplateService.IconoTipo(t.Kind),
        t.Title, t.Description, t.Tags, t.UsageCount, t.LastUsedAt, t.Actualizada,
        t.IsArchived, t.TieneArchivo, t.FileName);

    private static PlantillaDetalleDto ADtoDeDetalle(Template t) => new(
        t.Id, t.Kind, TemplateService.EtiquetaTipo(t.Kind), TemplateService.IconoTipo(t.Kind),
        t.Title, t.Description, t.Tags, t.Body,
        TemplateService.Marcadores(t.Body),
        TemplateService.EsScript(t.Kind),
        t.UsageCount, t.LastUsedAt, t.UpdatedAt ?? t.CreatedAt, t.IsArchived,
        TieneArchivo: t.FileBytes is { Length: > 0 },
        NombreArchivo: t.FileName,
        NombreArchivoSugerido: TemplateService.NombreArchivoSugerido(t));
}
