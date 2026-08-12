using Microsoft.JSInterop;

namespace AdminWeb.Client.Recorridos;

/// <summary>
/// El puente con el navegador: entrega el guion al envoltorio de Driver.js y le pregunta qué pasó.
///
/// <para>Son métodos estáticos que reciben el <c>IJSRuntime</c> en vez de un servicio registrado en
/// el contenedor. Es a propósito: así lanzar un recorrido no obliga a tocar <c>Program.cs</c> ni a
/// que quien escriba una pantalla se acuerde de inyectar nada. No hay estado que guardar de este
/// lado — el recorrido en curso lo lleva el navegador.</para>
/// </summary>
public static class LanzadorDeRecorridos
{
    /// <summary>
    /// Lanza el recorrido y devuelve CUÁNTOS pasos se enseñaron de verdad.
    ///
    /// <para>El número importa porque puede ser menor que los pasos escritos, y eso es normal: los
    /// pasos cuyo control no está en pantalla —porque el rol no lo ve, porque no hay datos, porque
    /// el panel está plegado— se saltan en silencio y el recorrido sigue. La prueba es la que grita
    /// cuando una marca desaparece del marcado; a quien está usando la aplicación no se le
    /// interrumpe por eso.</para>
    /// </summary>
    public static async ValueTask<int> LanzarAsync(IJSRuntime js, Recorrido recorrido)
    {
        var guion = recorrido.Pasos.Select(p => new PasoParaElNavegador(
            p.Marca,
            p.Titulo,
            p.Texto,
            p.Lado.ToString().ToLowerInvariant()));

        return await js.InvokeAsync<int>("adminweb.recorridos.iniciar", recorrido.Id, guion);
    }

    /// <summary>
    /// Cierra el recorrido en curso, si lo hay. Se llama al cambiar de pantalla y NO es opcional:
    /// mientras un recorrido está abierto, la hoja de Driver.js deja toda la aplicación sin recibir
    /// clics salvo el control señalado. Un recorrido que sobreviviera a una navegación dejaría la
    /// pantalla siguiente congelada, y el aspecto sería el de una aplicación colgada.
    ///
    /// <para>Cerrar así NO lo cuenta como visto: quien navega a otra parte no ha visto el
    /// recorrido, y marcarlo escondería el aviso de que hay uno sin estrenar.</para>
    /// </summary>
    public static async ValueTask CerrarAsync(IJSRuntime js)
    {
        try
        {
            await js.InvokeVoidAsync("adminweb.recorridos.cerrar");
        }
        catch (JSDisconnectedException)
        {
            // La página se está yendo. No hay nada que cerrar ni nadie a quien contárselo.
        }
    }

    /// <summary>¿Ya se vio este recorrido en este navegador?</summary>
    public static async ValueTask<bool> YaVistoAsync(IJSRuntime js, Recorrido recorrido)
    {
        try
        {
            return await js.InvokeAsync<bool>("adminweb.recorridos.visto", recorrido.Id);
        }
        catch (JSDisconnectedException)
        {
            return true;   // sin poder preguntar, no se enseña el punto: mejor callado que insistente
        }
    }

    /// <summary>
    /// El paso tal como lo espera el envoltorio de JavaScript. Los nombres viajan en minúscula
    /// inicial porque es lo que hace la serialización de Blazor por omisión.
    /// </summary>
    private sealed record PasoParaElNavegador(string Marca, string Titulo, string Texto, string Lado);
}
