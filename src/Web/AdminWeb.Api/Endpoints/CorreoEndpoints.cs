using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Correo;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// El correo del área: la bandeja del buzón, la ingesta de requerimientos, el envío y el resumen del
/// equipo.
///
/// <para><b>Todo el grupo es del líder.</b> Es la cuenta de correo de la organización: quien pueda
/// llamar a estas rutas puede escribir a nombre de la empresa y leer lo que llega al buzón. La
/// política está aquí y la guarda vuelve a comprobarse dentro de cada servicio, porque cualquiera
/// puede llamar a la API sin pasar por el navegador.</para>
///
/// <para><b>La contraseña de aplicación no aparece en ninguna respuesta de este archivo</b>, ni
/// siquiera en un mensaje de error: se captura en Configuración, se guarda cifrada y se usa dentro
/// del servidor. Lo más que se puede saber desde fuera es si el correo está utilizable.</para>
///
/// <para>Un fallo de la integración —sin configurar, credenciales rechazadas, servidor caído— sale
/// como <b>400 con el texto del servicio</b>, no como 500. Que el servidor de correo de un tercero
/// esté caído no es un defecto de esta aplicación, y quien lo lea necesita saber qué arreglar.</para>
/// </summary>
public static class CorreoEndpoints
{
    public static void MapCorreoEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/correo")
            .WithTags("Correo")
            .RequireAuthorization("SoloAdmin");

        // ── El buzón ─────────────────────────────────────────────────────────────

        grupo.MapGet("/estado", async (IngestaDeCorreoService correo, CancellationToken ct) =>
            Results.Ok(await correo.EstadoAsync(ct)))
        .WithSummary("Si el correo está utilizable y cómo está configurado el resumen");

        grupo.MapGet("/bandeja", async (
            string? carpeta, int? maximo, IngestaDeCorreoService correo, CancellationToken ct) =>
        {
            var (ok, mensaje, bandeja) = await correo.BandejaAsync(
                carpeta, maximo ?? IngestaDeCorreoService.MensajesPorOmision, ct);

            return ok && bandeja is not null
                ? Results.Ok(bandeja)
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .WithSummary("Las carpetas del buzón y los mensajes recientes de una de ellas");

        grupo.MapPost("/enviar", async (
            EnviarCorreoRequest cuerpo, IngestaDeCorreoService correo, CancellationToken ct) =>
        {
            var (ok, mensaje) = await correo.EnviarAsync(
                cuerpo.Destinatarios, cuerpo.Asunto, cuerpo.Cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Envía un correo desde la cuenta de la aplicación");

        grupo.MapPost("/probar", async (IngestaDeCorreoService correo, CancellationToken ct) =>
        {
            var (ok, mensaje) = await correo.ProbarConexionAsync(ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Comprueba la cuenta y los servidores sin enviar nada");

        // ── Ingesta ──────────────────────────────────────────────────────────────

        grupo.MapPost("/ingerir", async (
            IngerirCorreoRequest cuerpo, IngestaDeCorreoService correo, CancellationToken ct) =>
        {
            var (ok, mensaje, creados) = await correo.IngerirAsync(cuerpo.Carpeta, ct);

            return ok
                ? Results.Ok(new ResultadoDeIngestaDto(mensaje, creados))
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .WithSummary("Importa como requerimientos los correos sin procesar de la carpeta");

        grupo.MapPost("/convertir", async (
            ConvertirEnRequerimientoRequest cuerpo, IngestaDeCorreoService correo, CancellationToken ct) =>
        {
            var (ok, mensaje) = await correo.ConvertirEnRequerimientoAsync(
                cuerpo.Carpeta, cuerpo.Identificador, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Convierte un mensaje concreto en requerimiento y lo da por procesado");

        // ── Resumen del equipo ───────────────────────────────────────────────────

        grupo.MapGet("/resumen", async (DigestService resumen, CancellationToken ct) =>
            Results.Ok(await resumen.VistaPreviaAsync(ct)))
        .WithSummary("El resumen tal como saldría ahora, sin enviarlo");

        grupo.MapPost("/resumen/enviar", async (DigestService resumen, CancellationToken ct) =>
        {
            var (ok, mensaje) = await resumen.EnviarAhoraAsync(ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Manda el resumen ahora, sin esperar a que toque");
    }

    /// <summary>
    /// Un rechazo de negocio o un fallo de la integración salen como 400 con el mensaje del servicio.
    /// Ese texto explica el motivo en concreto —«vuelve a capturar la contraseña de aplicación», «esa
    /// carpeta no existe»— y es lo que se enseña tal cual.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));
}
