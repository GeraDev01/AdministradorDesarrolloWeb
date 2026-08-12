using AdminWeb.Client.Componentes;
using Microsoft.JSInterop;
using Radzen;

namespace AdminWeb.Client.Servicios;

/// <summary>
/// Los avisos y las confirmaciones de la interfaz.
///
/// El escritorio tenía 571 <c>MessageBox</c> repartidos, y ese es el trabajo mecánico más numeroso
/// del port. En el navegador no existe el cuadro modal que detiene el hilo: todo diálogo es
/// asíncrono y devuelve una tarea. Esta clase es la traducción de aquel patrón, resuelta una vez
/// para las ~80 ventanas que vendrán detrás.
///
/// La distinción que importa: lo que solo informa DE PASO se muestra como aviso pasajero (no
/// interrumpe), y lo que pide una decisión se muestra como diálogo (sí interrumpe). En el escritorio
/// ambos casos eran el mismo MessageBox y por eso confirmar «¿guardar?» costaba lo mismo que decir
/// «guardado».
///
/// Y hay un tercer caso que no es ninguno de los dos, así que se nombra en vez de colarlo en el
/// segundo: lo que hay que LEER —el detalle completo de una fila de rejilla, que en la celda no
/// cabe—. Interrumpe como un diálogo, pero no pregunta nada ni devuelve respuesta: se abre, se lee y
/// se cierra.
/// </summary>
public class AvisosDeInterfaz(NotificationService notificaciones, DialogService dialogos, IJSRuntime js)
{
    // ── Informar (no interrumpe) ─────────────────────────────────────────────

    public void Exito(string mensaje, string titulo = "Listo") =>
        Mostrar(NotificationSeverity.Success, titulo, mensaje);

    public void Error(string mensaje, string titulo = "No se pudo") =>
        Mostrar(NotificationSeverity.Error, titulo, mensaje, duracionMs: 8000);

    public void Aviso(string mensaje, string titulo = "Atención") =>
        Mostrar(NotificationSeverity.Warning, titulo, mensaje, duracionMs: 6000);

    public void Info(string mensaje, string titulo = "") =>
        Mostrar(NotificationSeverity.Info, titulo, mensaje);

    /// <summary>Muestra el resultado de un servicio, que siempre viene como (ok, mensaje).</summary>
    public void Resultado(bool ok, string mensaje)
    {
        if (ok) Exito(mensaje); else Error(mensaje);
    }

    private void Mostrar(NotificationSeverity severidad, string titulo, string mensaje, int duracionMs = 4000) =>
        notificaciones.Notify(new NotificationMessage
        {
            Severity = severidad,
            Summary = titulo,
            Detail = mensaje,
            Duration = duracionMs
        });

    // ── Preguntar (interrumpe) ───────────────────────────────────────────────

    /// <summary>
    /// Confirmación de sí/no. Sustituye a los <c>MessageBox.Show(..., YesNo)</c> del escritorio.
    /// </summary>
    public async Task<bool> ConfirmarAsync(string mensaje, string titulo = "Confirmar",
        string textoSi = "Sí", string textoNo = "Cancelar") =>
        await dialogos.Confirm(mensaje, titulo,
            new ConfirmOptions { OkButtonText = textoSi, CancelButtonText = textoNo }) == true;

    /// <summary>
    /// Confirmación de algo que no tiene vuelta atrás. Además de preguntar, exige escribir una
    /// palabra exacta.
    ///
    /// Es la traducción de una decisión del escritorio que conviene no perder: la pantalla de
    /// limpieza de datos pedía teclear una confirmación porque un clic de más ahí no se deshace. En
    /// la web hace todavía más falta, porque a una pantalla se llega por una dirección que alguien
    /// pudo compartir.
    /// </summary>
    public async Task<bool> ConfirmarEscribiendoAsync(string mensaje, string palabra, string titulo = "Confirmar")
    {
        var escrito = await PedirTextoAsync($"{mensaje}\n\nEscribe «{palabra}» para confirmar.", titulo);
        return string.Equals(escrito?.Trim(), palabra, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pide una frase (un motivo, un nombre). Devuelve null si se cancela — distinto de la cadena
    /// vacía, que es «aceptó sin escribir nada».
    /// </summary>
    public async Task<string?> PedirTextoAsync(string mensaje, string titulo = "")
    {
        // Se usa el prompt del navegador mientras no exista el componente propio: cambiarlo después
        // es tocar solo este método, no las pantallas que lo llaman.
        var respuesta = await js.InvokeAsync<string?>("prompt", mensaje, "");
        return respuesta;
    }

    // ── Enseñar (interrumpe, pero no pide nada) ──────────────────────────────

    /// <summary>
    /// El detalle completo de una fila de rejilla. Lo abre el doble clic sobre la fila y el botón
    /// del ojo, que son el mismo camino por dos puertas (ver <c>BotonDeDetalle.razor</c>).
    ///
    /// <para>Se puede cerrar de TRES formas y las tres van declaradas aquí:</para>
    /// <list type="bullet">
    ///   <item>Escape. Radzen ya lo hace por omisión; se escribe igualmente porque es un requisito
    ///   del cuadro y no un valor de fábrica del que se pueda depender en silencio.</item>
    ///   <item>Pinchando fuera. Eso NO es lo de fábrica —Radzen lo trae apagado— y hay que pedirlo.
    ///   Es lo que se espera de algo que solo se lee: nadie busca un botón para dejar de mirar.</item>
    ///   <item>El aspa de la barra de título, para quien navega con el teclado.</item>
    /// </list>
    ///
    /// <para>Ni arrastrable ni redimensionable: son gestos de ventana que solo estorban en algo que
    /// se abre para leer diez renglones y se cierra. El ancho se queda en <c>min()</c> porque el
    /// texto largo necesita medida cómoda de lectura, pero un cuadro de 46 rem en un teléfono se
    /// sale de la pantalla.</para>
    ///
    /// <para>Devuelve una tarea que termina al cerrarse, y nadie la mira: no hay respuesta que
    /// recoger. Se espera igualmente para que una excepción al abrirlo no se pierda.</para>
    /// </summary>
    public async Task VerDetalleAsync(DetalleDeFila detalle) =>
        await dialogos.OpenAsync<CuadroDeDetalle>(
            detalle.Encabezado,
            new Dictionary<string, object> { [nameof(CuadroDeDetalle.Detalle)] = detalle },
            new DialogOptions
            {
                Width = "min(46rem, 92vw)",
                CloseDialogOnEsc = true,
                CloseDialogOnOverlayClick = true,
                ShowClose = true,
                Draggable = false,
                Resizable = false
            });
}
