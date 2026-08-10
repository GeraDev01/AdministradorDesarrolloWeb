using AdminWeb.Domain.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Api.Filtros;

/// <summary>
/// Traduce las excepciones del dominio al código HTTP que les corresponde, una sola vez y para
/// todos los endpoints.
///
/// Es lo que permite que los servicios sigan lanzando <see cref="AuthorizationException"/> tal como
/// los copiamos —el escritorio ya había anotado que «en la web se traduce a 403»— sin que cada
/// endpoint tenga que acordarse de capturarla. Lo mismo con el choque de concurrencia: en el
/// escritorio solo un servicio y dos pantallas lo manejaban, así que portarlo endpoint por endpoint
/// habría dejado la mayoría de los casos sin respuesta útil.
/// </summary>
public class MapeoExcepciones(ILogger<MapeoExcepciones> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (status, titulo, detalle) = ex switch
        {
            AuthorizationException auth => (
                StatusCodes.Status403Forbidden,
                "Sin permiso",
                auth.Message),

            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "El registro cambió mientras lo editabas",
                "Otra persona modificó este registro antes que tú. Vuelve a cargarlo para no pisar su cambio."),

            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "No encontrado",
                "Ese registro ya no existe. Actualiza la lista."),

            // La petición venía mal formada: sin su testigo antifalsificación, con un cuerpo que no
            // se puede leer, o pasada de tamaño. Sin este caso todas salen como 500, que es lo peor
            // de los dos mundos: al usuario no le dice nada, y a quien vigila el servidor le parece
            // un fallo de la aplicación cuando el fallo está en lo que llegó —y en el caso del
            // testigo puede ser justo lo contrario, esta protección haciendo su trabajo.
            //
            // El código lo trae la propia excepción (400 casi siempre, 413 si no cabía): usarlo es
            // más honesto que fijar uno y acertar solo a veces.
            BadHttpRequestException mala => (
                mala.StatusCode,
                "Petición rechazada",
                EsPorElTestigo(mala)
                    ? "La página lleva demasiado tiempo abierta o la petición no vino de aquí. " +
                      "Recarga e inténtalo otra vez."
                    : "La petición no se pudo leer. Si estabas subiendo un archivo, revisa que no sea enorme."),

            _ => (0, "", "")
        };

        // Lo que no reconocemos se deja pasar al manejador por omisión: convertir cualquier
        // excepción en una respuesta bonita esconde los defectos de verdad.
        if (status == 0) return false;

        if (status == StatusCodes.Status403Forbidden)
            log.LogWarning("Acceso denegado en {Ruta}: {Mensaje}", ctx.Request.Path, ex.Message);

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = titulo,
            Detail = detalle,
            Instance = ctx.Request.Path
        }, ct);

        return true;
    }

    /// <summary>
    /// ¿La petición se rechazó por el testigo antifalsificación?
    ///
    /// Se mira la excepción INTERNA porque el framework envuelve la de antifalsificación en una
    /// <see cref="BadHttpRequestException"/> genérica, y a quien recibe el mensaje le sirve saber que
    /// basta con recargar. Distinguirlo por el texto del mensaje sería atarse a una cadena que
    /// Microsoft puede reescribir en cualquier versión.
    /// </summary>
    private static bool EsPorElTestigo(Exception ex) =>
        ex.InnerException is AntiforgeryValidationException;
}
