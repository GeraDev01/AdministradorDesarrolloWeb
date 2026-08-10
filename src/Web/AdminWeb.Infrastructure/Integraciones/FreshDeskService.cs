using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AdminWeb.Infrastructure.Integraciones;

/// <summary>
/// Lo mínimo para hablar con Freshdesk: el subdominio de la cuenta y la clave de API.
///
/// <para><b>Se pasa por parámetro y no se inyecta.</b> Las dos cosas viven en la tabla de
/// configuración y el líder puede cambiarlas sin reiniciar nada, así que atarlas al arranque
/// dejaría al servidor hablando con la cuenta anterior hasta el siguiente despliegue.</para>
/// </summary>
public sealed record OpcionesDeFreshdesk(string Dominio, string ClaveApi)
{
    private string Cuenta => $"https://{Dominio.Trim().TrimEnd('/')}.freshdesk.com";

    /// <summary>Raíz de la API v2 de la cuenta.</summary>
    public string BaseUrl => $"{Cuenta}/api/v2";

    /// <summary>La dirección con la que se abre un ticket en el navegador.</summary>
    public string UrlDelTicket(long numero) => $"{Cuenta}/helpdesk/tickets/{numero}";

    /// <summary>
    /// <b>ToString propio, y no es cosmético.</b> El que genera un record imprime todas sus
    /// propiedades, así que la clave de API acabaría en cualquier traza, mensaje de excepción o
    /// entrada de bitácora que interpolara estas opciones. Aquí solo sale el dominio.
    /// </summary>
    public override string ToString() => $"Freshdesk({Dominio})";
}

/// <summary>
/// Freshdesk no respondió lo que se esperaba: la clave no vale, no hay permiso, o no se pudo llegar.
///
/// Existe para que quien orquesta la sincronización pueda convertirlo en un mensaje que se entienda
/// —y en un 400 con ese texto— en vez de dejar salir un 500. <b>Su mensaje nunca lleva la clave de
/// API</b>: se enseña tal cual en pantalla.
/// </summary>
public sealed class ErrorDeFreshdeskException(string mensaje) : Exception(mensaje);

/// <summary>
/// Un ticket tal como lo devuelve la API, ya interpretado y sin nada de JSON encima.
///
/// El agente y el grupo viajan como IDENTIFICADORES y no como nombres, porque es lo único que trae
/// el ticket: los nombres hay que resolverlos aparte contra /agents y /groups, que pueden no estar
/// disponibles (ver <see cref="IApiDeFreshdesk.AgentesAsync"/>). El filtro trabaja con los ids, así
/// que sigue funcionando aunque los nombres queden en blanco.
/// </summary>
public record TicketDeFreshdeskCrudo(
    long Numero,
    string Asunto,
    int Estado,
    int Prioridad,
    string? Tipo,
    int Fuente,
    long? AgenteId,
    long? GrupoId,
    string Solicitante,
    string CorreoDelSolicitante,
    string Etiquetas,
    string? Descripcion,
    DateTime? CreadoUtc,
    DateTime? ActualizadoUtc);

/// <summary>
/// Una página de tickets. <paramref name="HayMas"/> lo decide quien conoce el tamaño de página —el
/// cliente de la API— para que quien recorre no tenga que saber que Freshdesk pagina de cien en cien.
/// </summary>
public record PaginaDeTicketsDeFreshdesk(IReadOnlyList<TicketDeFreshdeskCrudo> Tickets, bool HayMas);

/// <summary>
/// La frontera con Freshdesk: lo único de esta vertical que sale a la red.
///
/// Está detrás de una interfaz para que la sincronización —que es donde viven las reglas que
/// importan, entre ellas la de no borrar nada por ausencia— se pueda probar entera sin tocar
/// internet ni depender de una cuenta real.
/// </summary>
public interface IApiDeFreshdesk
{
    /// <summary>
    /// Una página de tickets.
    ///
    /// <para><b>⚠ /tickets SOLO devuelve los de los últimos ~30 días.</b> Está comprobado contra la
    /// API real. Que un ticket no venga en la respuesta NO significa que se haya borrado en
    /// Freshdesk: significa que quedó fuera de esa ventana. Por eso quien consuma esto <b>nunca</b>
    /// debe borrar tickets locales porque no aparecieran — se perdería todo el histórico anterior al
    /// mes en curso.</para>
    /// </summary>
    Task<PaginaDeTicketsDeFreshdesk> TicketsAsync(
        OpcionesDeFreshdesk opciones, int pagina, CancellationToken ct = default);

    /// <summary>
    /// El id del agente dueño de la clave configurada (GET /agents/me), o null si no se pudo
    /// averiguar. Es lo que convierte «asignados a mí» en algo comprobable.
    /// </summary>
    Task<long?> MiAgenteIdAsync(OpcionesDeFreshdesk opciones, CancellationToken ct = default);

    /// <summary>
    /// Mapa id → nombre de los agentes, para poder poner el nombre de quien tiene cada ticket.
    ///
    /// <para><b>⚠ /agents responde 403 con una clave de AGENTE</b> (solo funciona con clave de
    /// administrador). Ese 403 significa «no disponible», <b>no</b> «la clave está mal»: devolver un
    /// error de configuración aquí acabaría con el líder cambiando una clave que estaba perfecta.
    /// Por eso se devuelve un mapa vacío y la sincronización sigue, con los nombres en blanco.</para>
    /// </summary>
    Task<IReadOnlyDictionary<long, string>> AgentesAsync(
        OpcionesDeFreshdesk opciones, CancellationToken ct = default);

    /// <summary>
    /// Mapa id → nombre de los grupos. Mismo criterio tolerante que <see cref="AgentesAsync"/>: con
    /// una clave de agente, /groups responde 403 y eso es «no disponible», no un error. La
    /// consecuencia visible es que un grupo escrito por NOMBRE no se puede resolver y hay que
    /// escribir su ID numérico.
    /// </summary>
    Task<IReadOnlyDictionary<long, string>> GruposAsync(
        OpcionesDeFreshdesk opciones, CancellationToken ct = default);
}

/// <summary>
/// El cliente de Freshdesk, portado del escritorio y reducido a lo que de verdad es: hablar HTTP y
/// entender JSON. <b>No toca la base de datos, ni la configuración, ni la bitácora</b>; eso vive en
/// la capa de aplicación, que es quien puede alcanzarlas.
///
/// <para>Se habla con <c>HttpClient</c> y <c>System.Text.Json</c>, sin SDK, igual que el escritorio:
/// el cliente oficial arrastra media plataforma para los cuatro endpoints que se usan de él.</para>
/// </summary>
public class FreshDeskService(HttpClient http) : IApiDeFreshdesk
{
    /// <summary>Lo máximo que admite Freshdesk por página. Pedir más devuelve un error de validación.</summary>
    private const int PorPagina = 100;

    public async Task<PaginaDeTicketsDeFreshdesk> TicketsAsync(
        OpcionesDeFreshdesk opciones, int pagina, CancellationToken ct = default)
    {
        // El «include» solo admite requester, stats, company y description. «assignee» NO es válido:
        // Freshdesk respondía 400 («Validation failed») y la sincronización no conectaba nunca. Se
        // pide «description» porque de ahí sale el description_text que se guarda.
        var url = $"{opciones.BaseUrl}/tickets?page={pagina}&per_page={PorPagina}&include=requester,stats,description";

        using var respuesta = await PedirAsync(opciones, url, ct);
        ExigirRespuestaUtil(respuesta, "los tickets");

        using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return new PaginaDeTicketsDeFreshdesk([], HayMas: false);

        var tickets = doc.RootElement.EnumerateArray().Select(Interpretar).ToList();
        return new PaginaDeTicketsDeFreshdesk(tickets, HayMas: tickets.Count >= PorPagina);
    }

    public async Task<long?> MiAgenteIdAsync(OpcionesDeFreshdesk opciones, CancellationToken ct = default)
    {
        try
        {
            using var respuesta = await PedirAsync(opciones, $"{opciones.BaseUrl}/agents/me", ct);
            if (!respuesta.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                return id.GetInt64();
        }
        catch (ErrorDeFreshdeskException)
        {
            // Ni siquiera aquí se rompe la sincronización: sin saber quién soy, el criterio «solo los
            // míos» no se puede aplicar, y eso se avisa aparte en vez de dejar al líder sin traer nada.
        }
        return null;
    }

    public Task<IReadOnlyDictionary<long, string>> AgentesAsync(
        OpcionesDeFreshdesk opciones, CancellationToken ct = default) =>
        // Paginado porque una cuenta puede pasar de cien agentes; el nombre del agente vive dentro de
        // su «contact» y se cae al correo cuando no tiene nombre puesto.
        MapaTolerante(opciones, pagina => $"{opciones.BaseUrl}/agents?page={pagina}&per_page={PorPagina}",
            elemento => elemento.TryGetProperty("contact", out var contacto) && contacto.ValueKind == JsonValueKind.Object
                ? TextoONulo(contacto, "name") ?? TextoONulo(contacto, "email")
                : null,
            paginado: true, ct);

    public Task<IReadOnlyDictionary<long, string>> GruposAsync(
        OpcionesDeFreshdesk opciones, CancellationToken ct = default) =>
        MapaTolerante(opciones, _ => $"{opciones.BaseUrl}/groups?per_page={PorPagina}",
            elemento => TextoONulo(elemento, "name"),
            paginado: false, ct);

    // ── Interno ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mapa id → nombre de un catálogo (agentes, grupos) que <b>puede no estar disponible</b>.
    ///
    /// Cualquier respuesta que no sea 2xx —y muy en particular el 403 que devuelven /agents y /groups
    /// con una clave de agente— se trata como «no disponible»: se devuelve lo que se haya podido
    /// juntar y la sincronización continúa sin nombres. Tratarlo como error dejaría la integración
    /// inutilizable con una clave que funciona perfectamente para lo que de verdad importa, que son
    /// los tickets.
    /// </summary>
    private async Task<IReadOnlyDictionary<long, string>> MapaTolerante(
        OpcionesDeFreshdesk opciones, Func<int, string> url, Func<JsonElement, string?> nombre,
        bool paginado, CancellationToken ct)
    {
        var mapa = new Dictionary<long, string>();
        try
        {
            for (int pagina = 1; ; pagina++)
            {
                using var respuesta = await PedirAsync(opciones, url(pagina), ct);
                if (!respuesta.IsSuccessStatusCode) break;   // 403 sin permiso, o cualquier otro tropiezo

                using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) break;

                foreach (var elemento in doc.RootElement.EnumerateArray())
                {
                    if (!elemento.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number) continue;
                    var texto = nombre(elemento);
                    if (!string.IsNullOrWhiteSpace(texto)) mapa[id.GetInt64()] = texto!;
                }

                if (!paginado || doc.RootElement.GetArrayLength() < PorPagina) break;
            }
        }
        catch (ErrorDeFreshdeskException)
        {
            // Sin permiso o problema pasajero: se sincroniza sin nombres, que es peor que tenerlos
            // pero muchísimo mejor que no sincronizar.
        }
        return mapa;
    }

    /// <summary>
    /// Una petición GET con la autenticación de Freshdesk (Basic <c>clave:X</c>).
    ///
    /// <para>La credencial se pone en CADA petición y no en <c>DefaultRequestHeaders</c>: el cliente
    /// tipado lo administra la fábrica y su instancia se reutiliza, así que una clave pegada a él
    /// seguiría viajando después de que el líder la cambiara —o, peor, hacia un dominio distinto—.</para>
    /// </summary>
    private async Task<HttpResponseMessage> PedirAsync(
        OpcionesDeFreshdesk opciones, string url, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, url);
        peticion.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{opciones.ClaveApi}:X")));
        peticion.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            return await http.SendAsync(peticion, ct);
        }
        catch (HttpRequestException ex)
        {
            // El dominio sí puede decirse (no es secreto y es la mitad de los fallos reales: un
            // subdominio mal escrito). La clave, jamás.
            throw new ErrorDeFreshdeskException(
                $"No se pudo conectar con Freshdesk ({opciones.Dominio}.freshdesk.com). " +
                $"Revisa el dominio en Configuración y la salida a internet del servidor. Detalle: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ErrorDeFreshdeskException(
                "Freshdesk tardó demasiado en responder. Vuelve a intentarlo en un momento.");
        }
    }

    /// <summary>
    /// Convierte una respuesta que no sirve en un mensaje que una persona pueda accionar.
    ///
    /// El 401 y el 403 se distinguen a propósito: el primero es «la clave no vale» y el segundo «la
    /// clave vale pero no alcanza». Confundirlos manda a cambiar la clave a quien tenía que pedir
    /// permisos.
    /// </summary>
    private static void ExigirRespuestaUtil(HttpResponseMessage respuesta, string que)
    {
        if (respuesta.IsSuccessStatusCode) return;

        throw new ErrorDeFreshdeskException(respuesta.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "Freshdesk no aceptó la clave de API. Vuelve a capturarla en Configuración.",
            HttpStatusCode.Forbidden =>
                $"La clave de API de Freshdesk no tiene permiso para leer {que}. " +
                "Hace falta una clave con acceso a los tickets de la cuenta.",
            HttpStatusCode.TooManyRequests =>
                "Freshdesk está limitando las peticiones (demasiadas en poco tiempo). " +
                "Espera un minuto y vuelve a sincronizar.",
            _ => $"Freshdesk respondió {(int)respuesta.StatusCode} al pedir {que}."
        });
    }

    /// <summary>Un ticket del JSON a algo con nombre y tipo.</summary>
    private static TicketDeFreshdeskCrudo Interpretar(JsonElement t)
    {
        var solicitante = "";
        var correo = "";
        if (t.TryGetProperty("requester", out var quien) && quien.ValueKind == JsonValueKind.Object)
        {
            solicitante = Texto(quien, "name");
            correo = Texto(quien, "email");
        }

        return new TicketDeFreshdeskCrudo(
            Numero: t.GetProperty("id").GetInt64(),
            Asunto: Texto(t, "subject"),
            Estado: Entero(t, "status"),
            Prioridad: Entero(t, "priority"),
            Tipo: TextoONulo(t, "type"),
            Fuente: Entero(t, "source"),
            AgenteId: EnteroLargoONulo(t, "responder_id"),
            GrupoId: EnteroLargoONulo(t, "group_id"),
            Solicitante: solicitante,
            CorreoDelSolicitante: correo,
            Etiquetas: t.TryGetProperty("tags", out var etiquetas) && etiquetas.ValueKind == JsonValueKind.Array
                ? string.Join(", ", etiquetas.EnumerateArray().Select(e => e.GetString() ?? ""))
                : "",
            Descripcion: TextoONulo(t, "description_text"),
            CreadoUtc: Fecha(t, "created_at"),
            ActualizadoUtc: Fecha(t, "updated_at"));
    }

    private static string Texto(JsonElement el, string clave) => TextoONulo(el, clave) ?? "";

    private static string? TextoONulo(JsonElement el, string clave) =>
        el.TryGetProperty(clave, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Entero(JsonElement el, string clave) =>
        el.TryGetProperty(clave, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static long? EnteroLargoONulo(JsonElement el, string clave) =>
        el.TryGetProperty(clave, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;

    /// <summary>Las fechas llegan en ISO con zona; se guardan siempre en UTC.</summary>
    private static DateTime? Fecha(JsonElement el, string clave) =>
        el.TryGetProperty(clave, out var v) && v.ValueKind == JsonValueKind.String
        && DateTime.TryParse(v.GetString(), out var fecha)
            ? fecha.ToUniversalTime()
            : null;
}
