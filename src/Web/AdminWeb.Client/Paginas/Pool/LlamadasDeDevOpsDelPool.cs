using System.Net;
using System.Net.Http.Json;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Busqueda;
using AdminWeb.Shared.Dtos.Pool;

namespace AdminWeb.Client.Paginas.Pool;

/// <summary>
/// Las lecturas del vínculo del pool con Azure DevOps, hechas contra el <c>HttpClient</c> y no contra
/// el cliente común de la aplicación.
///
/// <para><b>Por qué no se usa <c>ClienteApi</c> aquí.</b> Aquél enseña un aviso ROJO de «no se pudo»
/// con el mensaje del servidor y devuelve nulo. Para casi todo está bien, pero estas dos lecturas
/// fallan justo por el motivo que hay que tratar con cuidado: <b>que quien mira todavía no ha
/// capturado su token</b>. Ese texto no es un error de la aplicación —es una instrucción, y el primer
/// día es la de todo el mundo—, así que tiene que salir DENTRO del panel, junto al botón que lleva a
/// capturarlo, y no como una notificación roja que se desvanece a los ocho segundos dejando el panel
/// en blanco. Por eso estas llamadas devuelven el motivo en vez de gritarlo.</para>
///
/// <para><b>Es pública y no interna</b> —a diferencia de las llamadas sueltas de otras pantallas—
/// para que <c>BuscarTicketsAsync</c> se pueda probar contra la API de verdad. Ese método interpreta
/// lo que contesta el buscador global, y esa dependencia es la única de todo el vínculo que se
/// rompería en silencio si alguien cambiara aquel formato; una prueba que la ejerza con la respuesta
/// AUTÉNTICA es lo que la convierte en un fallo visible. En un ensamblado que el navegador descarga
/// entero, «interno» no protegía de nada.</para>
/// </summary>
public static class LlamadasDeDevOpsDelPool
{
    /// <summary>
    /// Con qué work item está ligada una actividad y qué le falta por mandarle. Sin red hacia
    /// DevOps: se resuelve con lo que hay guardado aquí, así que es barato y se puede pedir cada vez
    /// que alguien selecciona una actividad.
    /// </summary>
    public static Task<(VinculoDevOpsDto? datos, string motivo)> LeerVinculoAsync(
        this HttpClient http, int actividadId, CancellationToken ct = default) =>
        LeerAsync<VinculoDevOpsDto>(http, $"api/pool/{actividadId}/devops", ct);

    /// <summary>
    /// El hilo de comentarios del work item ligado. <b>Esto sí sale a la red</b> —a Azure DevOps— así
    /// que se pide cuando alguien lo pide, nunca al abrir un panel.
    /// </summary>
    public static Task<(HiloDeDevOpsDto? datos, string motivo)> LeerHiloAsync(
        this HttpClient http, int actividadId, CancellationToken ct = default) =>
        LeerAsync<HiloDeDevOpsDto>(http, $"api/pool/{actividadId}/devops/comentarios", ct);

    // ── Buscar el ticket que se va a ligar ───────────────────────────────────────

    /// <summary>Un ticket tal como sale del buscador, ya con su número separado del título.</summary>
    public sealed record TicketEncontrado(int Numero, string Titulo, string Detalle);

    /// <summary>
    /// Cómo nombra la búsqueda global a un ticket de Azure DevOps. Se compara con esto para no
    /// ofrecer como work item un requerimiento o un artículo, que llegan por la misma lista.
    /// </summary>
    private const string TipoDeTicket = "Ticket de DevOps";

    /// <summary>
    /// Busca tickets por NÚMERO o por TÍTULO, que son las dos formas en que se acuerda uno de un
    /// ticket: o se tiene el número a mano, o se recuerda de qué iba.
    ///
    /// <para><b>Se apoya en la búsqueda global</b> (<c>/api/busqueda</c>), que ya filtra en el
    /// servidor por título y por número exacto y devuelve como mucho una docena de tickets. La
    /// alternativa era traerse el tablero entero —los casi ocho mil tickets sincronizados, con todas
    /// sus columnas— para filtrarlo en el navegador, y eso es varios megabytes por cada vez que
    /// alguien abre el formulario de publicar. Para escoger un ticket entre unos pocos candidatos, la
    /// búsqueda del servidor es la herramienta correcta.</para>
    ///
    /// <para><b>El precio, dicho en voz alta:</b> esa ruta contesta con lo que se ENSEÑA
    /// («#1234  Título»), no con un contrato pensado para esto, así que aquí hay que separar el
    /// número del título. Se hace de la única forma que degrada bien: si el texto no empieza por
    /// almohadilla y dígitos, la fila NO se ofrece. Así, el día que ese formato cambie, lo que pasa
    /// es que el buscador deja de encontrar —visible al instante y sin consecuencias—, y nunca que se
    /// ligue un número inventado, que escribiría el esfuerzo en el ticket de otra persona. El día que
    /// exista una ruta de búsqueda de tickets con contrato propio, esto se cambia por ella y se borra
    /// <see cref="Interpretar"/>.</para>
    /// </summary>
    public static async Task<(IReadOnlyList<TicketEncontrado> tickets, string motivo)> BuscarTicketsAsync(
        this HttpClient http, string? texto, CancellationToken ct = default)
    {
        texto = (texto ?? "").Trim();

        // El mismo mínimo que aplica el servidor. Se dice aquí para contestar «escribe algo más» en
        // vez de un viaje que devuelve una lista vacía, que se lee como «no existe ese ticket».
        if (texto.Length < 2)
            return ([], "Escribe al menos dos caracteres: el número del work item o parte de su título.");

        var (hits, motivo) = await LeerAsync<List<ResultadoDeBusquedaDto>>(
            http, $"api/busqueda?q={Uri.EscapeDataString(texto)}", ct);

        if (hits is null) return ([], motivo);

        var tickets = hits
            .Where(h => h.Tipo == TipoDeTicket)
            .Select(Interpretar)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

        return (tickets, tickets.Count > 0
            ? ""
            : "Ningún ticket sincronizado coincide con eso. Puedes escribir el número a mano: se puede " +
              "ligar un work item que todavía no se ha traído a esta aplicación.");
    }

    /// <summary>
    /// Saca el número y el título de la línea del buscador («#1234  Corregir el reporte»).
    ///
    /// <para>Devuelve nulo en cuanto algo no encaja, y ese nulo es la protección: una fila que no se
    /// entiende no se ofrece. Sin expresiones regulares a propósito —recorrer los dígitos es más
    /// corto de leer que el patrón que haría falta— y sin aceptar un número que no empiece la
    /// cadena: «Bug #12 del #34» no es una línea de este buscador y no se va a interpretar como si lo
    /// fuera.</para>
    /// </summary>
    private static TicketEncontrado? Interpretar(ResultadoDeBusquedaDto hit)
    {
        var texto = hit.Texto ?? "";
        if (texto.Length < 2 || texto[0] != '#') return null;

        int fin = 1;
        while (fin < texto.Length && char.IsAsciiDigit(texto[fin])) fin++;

        return fin > 1 && int.TryParse(texto[1..fin], out var numero) && numero > 0
            ? new TicketEncontrado(numero, texto[fin..].Trim(), hit.Detalle ?? "")
            : null;
    }

    // ── Interno ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Una lectura que devuelve su fallo en vez de anunciarlo.
    ///
    /// <para>El 401 vuelve CALLADO: la sesión caducó y de eso ya se encarga el manejador de
    /// respuestas llevando a la pantalla de acceso; un texto encima solo taparía el sitio donde va a
    /// aparecer algo útil. Y la cancelación tampoco es un fallo: significa que quien esperaba se fue
    /// de la pantalla.</para>
    /// </summary>
    private static async Task<(T? datos, string motivo)> LeerAsync<T>(
        HttpClient http, string ruta, CancellationToken ct)
    {
        try
        {
            var respuesta = await http.GetAsync(ruta, ct);

            if (respuesta.IsSuccessStatusCode)
                return (await respuesta.Content.ReadFromJsonAsync<T>(ct), "");

            if (respuesta.StatusCode == HttpStatusCode.Unauthorized) return (default, "");

            return (default, await MotivoAsync(respuesta, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return (default, "");
        }
        catch (Exception ex)
        {
            return (default, $"No se pudo consultar: {ex.Message}");
        }
    }

    /// <summary>
    /// Por qué el servidor dijo que no. Estas rutas rechazan con un resultado y su mensaje —«todavía
    /// no has capturado TU token», «esa actividad no está ligada a ningún work item»—, escrito por
    /// quien conoce la regla; es ese texto el que hay que enseñar y no un «no se pudo» que dejaría a
    /// quien lo lea sin saber qué hacer.
    /// </summary>
    private static async Task<string> MotivoAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            if (await respuesta.Content.ReadFromJsonAsync<ResultadoDto>(ct) is { } r &&
                !string.IsNullOrWhiteSpace(r.Mensaje))
                return r.Mensaje;
        }
        catch { /* pudo no traer cuerpo, o no ser el resultado que se esperaba */ }

        return respuesta.StatusCode switch
        {
            HttpStatusCode.Forbidden => "No tienes permiso para ver esto.",
            HttpStatusCode.NotFound  => "Esa actividad ya no existe. Actualiza la lista.",
            _                        => "No se pudo consultar el vínculo con Azure DevOps."
        };
    }
}
