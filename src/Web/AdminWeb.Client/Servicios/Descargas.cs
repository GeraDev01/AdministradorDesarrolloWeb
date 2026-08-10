using Microsoft.JSInterop;

namespace AdminWeb.Client.Servicios;

/// <summary>
/// Descarga de archivos generados por la API (Excel, PDF, adjuntos).
///
/// Sustituye a los diálogos «guardar como» del escritorio y, de paso, a algo peor: allí, para
/// enseñar un adjunto guardado en la base, se volcaba a una carpeta temporal y se abría con el
/// programa asociado — y ninguno de esos ficheros temporales se borraba nunca. Aquí el navegador se
/// encarga: lo previsualiza si sabe (PDF, imágenes) o lo descarga, y no deja rastro.
/// </summary>
public class Descargas(IJSRuntime js)
{
    /// <summary>
    /// Descarga lo que devuelva una ruta de la API. El nombre y el tipo los pone la propia respuesta
    /// con su cabecera <c>Content-Disposition</c>, así que el cliente no tiene que adivinarlos.
    /// </summary>
    public ValueTask DesdeApiAsync(string ruta) =>
        js.InvokeVoidAsync("adminweb.descargar", ruta);

    /// <summary>Abre en otra pestaña (previsualizar un PDF, ir a un ticket externo).</summary>
    public ValueTask AbrirEnOtraPestanaAsync(string url) =>
        js.InvokeVoidAsync("adminweb.abrirEnOtraPestana", url);

    /// <summary>
    /// Copia texto al portapapeles. Devuelve false si el navegador lo impide (exige HTTPS y, en
    /// algunos, un gesto del usuario), para que la pantalla pueda decirlo en vez de fingir que
    /// funcionó.
    /// </summary>
    public ValueTask<bool> CopiarAsync(string texto) =>
        js.InvokeAsync<bool>("adminweb.copiar", texto);
}
