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
/// La distinción que importa: lo que solo informa se muestra como aviso pasajero (no interrumpe), y
/// lo que pide una decisión se muestra como diálogo (sí interrumpe). En el escritorio ambos casos
/// eran el mismo MessageBox y por eso confirmar «¿guardar?» costaba lo mismo que decir «guardado».
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
}
