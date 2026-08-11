using System.Net;
using System.Net.Http.Json;
using AdminWeb.Shared.Dtos;

namespace AdminWeb.Client.Paginas.Conocimiento;

/// <summary>
/// Las dos llamadas de esta pantalla que no son ni una lectura ni un alta: cambiar un artículo (PUT)
/// y borrarlo (DELETE).
///
/// <para><b>Por qué existen aquí.</b> El cliente común de la aplicación cubre lo que hacen casi todas
/// las pantallas —leer, mandar un formulario, subir archivos— y ninguna otra necesitaba estos dos
/// verbos. Editar un artículo es sustituir el documento entero por su versión nueva, y borrar un
/// borrador es borrarlo: son exactamente lo que PUT y DELETE significan, y torcerlos en dos rutas
/// «/editar» y «/eliminar» por POST habría dejado la API describiendo mal lo que hace.</para>
///
/// <para><b>La respuesta se lee IGUAL salga bien o mal</b>, y eso no es un atajo. Estas rutas
/// contestan siempre el mismo cuerpo —un resultado con su mensaje—: en el éxito explica qué pasó
/// («guardado; como ya estaba publicado, vuelve a la cola») y en el rechazo explica por qué no
/// («solo puedes editar lo que tú escribiste»). Ese texto lo escribió quien conoce la regla, y es lo
/// que hay que enseñar tal cual; sustituirlo por un «no se pudo» genérico convertiría una explicación
/// en un misterio.</para>
/// </summary>
internal static class LlamadasDeConocimiento
{
    public static async Task<ResultadoDto> CambiarAsync<TCuerpo>(
        this HttpClient http, string ruta, TCuerpo cuerpo, CancellationToken ct = default)
    {
        try
        {
            return await InterpretarAsync(await http.PutAsJsonAsync(ruta, cuerpo, ct), ct);
        }
        catch (Exception ex)
        {
            return new ResultadoDto(false, $"No se pudo guardar: {ex.Message}");
        }
    }

    public static async Task<ResultadoDto> BorrarAsync(
        this HttpClient http, string ruta, CancellationToken ct = default)
    {
        try
        {
            return await InterpretarAsync(await http.DeleteAsync(ruta, ct), ct);
        }
        catch (Exception ex)
        {
            return new ResultadoDto(false, $"No se pudo borrar: {ex.Message}");
        }
    }

    /// <summary>
    /// Saca el mensaje de la respuesta.
    ///
    /// <para>El 401 se devuelve CALLADO —sin texto— porque no es un fallo de lo que se estaba
    /// haciendo: la sesión caducó, y de eso ya se encarga el manejador de respuestas llevando a la
    /// pantalla de acceso. Un aviso rojo encima solo añadiría ruido a algo que ya se está
    /// resolviendo.</para>
    /// </summary>
    private static async Task<ResultadoDto> InterpretarAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        if (respuesta.StatusCode == HttpStatusCode.Unauthorized) return new ResultadoDto(false, "");

        try
        {
            if (await respuesta.Content.ReadFromJsonAsync<ResultadoDto>(ct) is { } r &&
                !string.IsNullOrWhiteSpace(r.Mensaje))
                return r with { Ok = respuesta.IsSuccessStatusCode && r.Ok };
        }
        catch { /* pudo no traer cuerpo, o no ser el resultado que se esperaba */ }

        return new ResultadoDto(respuesta.IsSuccessStatusCode, respuesta.StatusCode switch
        {
            HttpStatusCode.Forbidden => "No tienes permiso para hacer eso.",
            HttpStatusCode.NotFound  => "Ese artículo ya no existe. Actualiza la lista.",
            HttpStatusCode.Conflict  => "Alguien más lo modificó mientras tanto. Recarga antes de guardar.",
            _ when respuesta.IsSuccessStatusCode => "Listo.",
            _                        => "No se pudo completar la operación."
        });
    }
}
