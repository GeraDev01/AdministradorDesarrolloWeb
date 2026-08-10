using AdminWeb.Application.Services;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Cómo se devuelve un archivo desde la API. Un solo sitio, para que todos los adjuntos —evidencias,
/// capturas del foro, documentos de vacaciones, exportaciones a Excel— salgan igual.
/// </summary>
public static class ResultadosDeArchivo
{
    /// <summary>
    /// Sirve un archivo guardado en la base.
    ///
    /// El tipo se deduce de los BYTES y no del nombre —la extensión la escribe quien sube el
    /// archivo y puede mentir—, y se manda <c>nosniff</c> para que el navegador tampoco adivine por
    /// su cuenta: sin eso, un archivo con contenido HTML servido como imagen puede acabar
    /// ejecutándose en el dominio de la aplicación.
    ///
    /// Lo que se puede ver (imágenes, PDF) se muestra dentro de la página; lo demás se descarga.
    /// Con esto desaparecen de golpe los volcados a carpetas temporales del escritorio, que además
    /// nunca se borraban.
    /// </summary>
    public static IResult Adjunto(HttpContext ctx, byte[]? contenido, string? nombre)
    {
        if (contenido is null || contenido.Length == 0)
            return Results.NotFound(new { mensaje = "Ese archivo ya no está." });

        var tipo = ArchivosSubidos.TipoDeContenido(contenido);
        var nombreSeguro = ArchivosSubidos.NombreSeguro(nombre);
        if (nombreSeguro.Length == 0) nombreSeguro = "archivo";

        ctx.Response.Headers.XContentTypeOptions = "nosniff";

        return ArchivosSubidos.SePuedePrevisualizar(tipo)
            ? Results.File(contenido, tipo, enableRangeProcessing: true)
            : Results.File(contenido, tipo, nombreSeguro);
    }

    /// <summary>Una hoja de cálculo recién generada. Siempre se descarga; no hay nada que previsualizar.</summary>
    public static IResult Excel(byte[] contenido, string nombreSinExtension) =>
        Results.File(contenido,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{ArchivosSubidos.NombreSeguro(nombreSinExtension)}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
}
