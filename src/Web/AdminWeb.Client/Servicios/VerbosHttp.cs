using System.Net;
using System.Net.Http.Json;
using AdminWeb.Shared.Dtos;

namespace AdminWeb.Client.Servicios;

/// <summary>
/// Los dos verbos que el cliente común no cubre: cambiar un documento entero (PUT) y borrarlo
/// (DELETE).
///
/// <para><b>Por qué existen aparte de <see cref="ClienteApi"/>.</b> Aquél cubre lo que hacen casi
/// todas las pantallas —leer, mandar un formulario, subir archivos— y devuelve una tupla
/// <c>(ok, datos)</c> avisando él mismo del error. Estos dos hacen falta porque hay rutas que son
/// literalmente un PUT y un DELETE: sustituir un artículo por su versión nueva, y borrar una fila.
/// Torcerlos en dos rutas «/editar» y «/eliminar» por POST habría dejado la API describiendo mal lo
/// que hace.</para>
///
/// <para><b>Por qué están en Servicios y no colgando de una pantalla.</b> Nacieron dentro de
/// «Conocimiento», que fue quien primero necesitó los dos verbos, y ahí se quedaron mientras fue la
/// única. Al aparecer la segunda —el pool, que borra actividades— la alternativa era copiarlos o que
/// una pantalla llamara a los ayudantes de otra: las dos acaban igual, con dos versiones del mismo
/// código separándose. Aquí no hay que importar nada, porque <c>_Imports.razor</c> ya trae este
/// espacio de nombres.</para>
///
/// <para><b>La respuesta se lee IGUAL salga bien o mal</b>, y eso no es un atajo. Estas rutas
/// contestan siempre el mismo cuerpo —un resultado con su mensaje—: en el éxito explica qué pasó
/// («guardado; como ya estaba publicado, vuelve a la cola») y en el rechazo explica por qué no
/// («solo puedes editar lo que tú escribiste»). Ese texto lo escribió quien conoce la regla, y es lo
/// que hay que enseñar tal cual; sustituirlo por un «no se pudo» genérico convertiría una explicación
/// en un misterio.</para>
/// </summary>
internal static class VerbosHttp
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

        // Los respaldos son GENÉRICOS a propósito. Antes decían «Ese artículo ya no existe» porque
        // esto vivía en Conocimiento; ahora lo usa también el pool, y un 404 al borrar una actividad
        // no puede contestar hablando de artículos. Solo se llega aquí cuando la ruta no mandó su
        // mensaje, que es justo cuando no se sabe de qué se estaba hablando.
        return new ResultadoDto(respuesta.IsSuccessStatusCode, respuesta.StatusCode switch
        {
            HttpStatusCode.Forbidden => "No tienes permiso para hacer eso.",
            HttpStatusCode.NotFound  => "Eso ya no existe. Actualiza la lista.",
            HttpStatusCode.Conflict  => "Alguien más lo modificó mientras tanto. Recarga antes de guardar.",
            _ when respuesta.IsSuccessStatusCode => "Listo.",
            _                        => "No se pudo completar la operación."
        });
    }
}
