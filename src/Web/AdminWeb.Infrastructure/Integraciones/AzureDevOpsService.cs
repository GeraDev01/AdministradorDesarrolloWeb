using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace AdminWeb.Infrastructure.Integraciones;

// ── Nota sobre los nombres ───────────────────────────────────────────────────────
//
// La clase conserva el nombre del escritorio (AzureDevOpsService) para que cotejarla con el original
// durante el corte sea inmediato. Lo que cambia de fondo es su ALCANCE: allí el servicio hacía las
// llamadas HTTP, escribía en la base, apuntaba en la bitácora y avisaba a la gente, todo junto. Aquí
// se queda ÚNICAMENTE con hablar con Azure DevOps, porque esta capa no puede referenciar a la de
// aplicación —donde viven la configuración, los secretos y la bitácora— y porque un cliente sin base
// de datos es lo que permite falsearlo en las pruebas sin tocar la red.

/// <summary>
/// Con qué credenciales se habla con Azure DevOps en una llamada concreta.
///
/// Van por PARÁMETRO y no por configuración del cliente a propósito: el token es PERSONAL. Cada
/// petición se firma con el de quien la provocó, para que en el historial de DevOps los comentarios
/// y los cambios queden a su nombre y no a los de una cuenta compartida — que es justo lo que un SLA
/// necesita poder probar. Un <c>HttpClient</c> con el token en sus cabeceras por omisión obligaría a
/// tener un cliente por persona.
/// </summary>
public sealed record CredencialesDevOps(string OrgUrl, string Proyecto, string Pat)
{
    /// <summary>
    /// El <c>ToString</c> que genera un record imprime TODOS sus miembros, así que el que viene de
    /// serie escupiría el token entero en cuanto alguien interpolara estas credenciales en un
    /// mensaje de error o en una traza. Se sobrescribe por eso, no por estética.
    /// </summary>
    public override string ToString() =>
        $"CredencialesDevOps {{ OrgUrl = {OrgUrl}, Proyecto = {Proyecto}, Pat = (oculto) }}";
}

/// <summary>Un work item tal como lo devuelve la API, ya desenredado del JSON.</summary>
public sealed record WorkItemDevOps(
    int Id,
    string Titulo,
    string Tipo,
    string Estado,
    string Prioridad,
    string? Descripcion,
    string Url,
    string Area,
    string Iteracion,
    string Etiquetas,
    string AsignadoA,
    string CorreoAsignado,
    double? Puntos,
    double? HorasEstimadas,
    int Comentarios,
    DateTime? CreadoUtc,
    DateTime? ActualizadoUtc);

/// <summary>Un comentario del work item. <c>Texto</c> viene en HTML, tal como lo guarda DevOps.</summary>
public sealed record ComentarioDevOps(string Texto, string Autor, DateTime CreadoUtc);

/// <summary>Un bug colgado como hijo de un work item.</summary>
public sealed record BugHijoDevOps(int Id, string Titulo, string Estado, string Url);

/// <summary>
/// En qué columna del tablero está un work item, y si está en la mitad derecha de una columna
/// partida.
///
/// <para>Columna y estado NO son lo mismo, aunque casi siempre vayan juntos: el estado es del work
/// item y la columna es del TABLERO de un equipo. Un tablero puede tener dos columnas sobre el mismo
/// estado —«Análisis» y «En desarrollo», las dos sobre «Approved»— y entonces cambiar el estado no
/// basta para decidir en cuál cae la tarjeta.</para>
/// </summary>
public sealed record ColumnaDeTablero(string Columna, bool MitadHecha);

/// <summary>Un cambio de dueño, según el historial de revisiones del work item.</summary>
public sealed record CambioDeAsignacionDevOps(DateTime Fecha, string? De, string? A, string? CorreoDeA);

/// <summary>
/// Filtro de sincronización selectiva: tipos, estados y/o personas. Vacío = sin filtro. Sirve para no
/// traer miles de items y que la sincronización sea rápida.
/// </summary>
/// <param name="SoloMisAsignados">
/// Trae solo lo asignado al DUEÑO DEL TOKEN con el que se sincroniza, usando la macro <c>@Me</c> de
/// DevOps. La resuelve el servidor, así que no depende de que el correo de la ficha coincida con el
/// de la cuenta de DevOps — que es justo lo que hacía fallar el empate por identidad.
/// </param>
/// <param name="CambiadosEnDias">Solo lo movido en los últimos N días. Nulo = todo el historial.</param>
/// <param name="CreadosEnDias">
/// Solo lo CREADO en los últimos N días. Nulo = sin acotar.
///
/// <para>Es distinto de <paramref name="CambiadosEnDias"/> y no lo sustituye: un work item de hace
/// dos años que alguien tocó ayer entra por «cambiados» y no por «creados». Para traer al pool lo
/// que acaba de aparecer, la fecha que importa es la de creación —si no, cada comentario en un
/// ticket viejo lo volvería a proponer como trabajo nuevo—.</para>
/// </param>
public sealed record FiltroDeSincronizacion(
    IReadOnlyList<string> Tipos,
    IReadOnlyList<string> Asignados,
    IReadOnlyList<string> Estados,
    bool SoloMisAsignados = false,
    int? CambiadosEnDias = null,
    int? CreadosEnDias = null)
{
    public static FiltroDeSincronizacion Vacio { get; } = new([], [], []);

    /// <summary>
    /// No hay nada que acotar, así que la consulta sale sin filtros.
    ///
    /// <para><b>Todo campo nuevo tiene que entrar aquí</b>, y no es una formalidad: un filtro cuyos
    /// campos no se contaran daría «vacío», el bloque de cláusulas no se emitiría ENTERO y la
    /// sincronización se traería el proyecto completo. No rompe nada y no se ve; solo trae diez mil
    /// work items.</para>
    /// </summary>
    public bool EstaVacio =>
        Tipos.Count == 0 && Asignados.Count == 0 && Estados.Count == 0
        && !SoloMisAsignados && CambiadosEnDias == null && CreadosEnDias == null;
}

/// <summary>
/// Azure DevOps rechazó algo o no contestó, con un motivo que se puede enseñar.
///
/// Existe para que la capa de aplicación pueda distinguir «falló la integración» de cualquier otro
/// defecto y contestar con un mensaje entendible en vez de un 500. El mensaje NUNCA lleva el token:
/// se compone del código HTTP y del cuerpo que devolvió DevOps, y el token solo viaja en la cabecera
/// <c>Authorization</c>, que no se toca aquí.
/// </summary>
public class ErrorDeAzureDevOps(string mensaje) : Exception(mensaje);

/// <summary>
/// Lo que se le puede pedir a Azure DevOps.
///
/// Existe la interfaz por una razón concreta: <b>las pruebas no tocan la red</b>. Todo lo que hay
/// detrás de aquí es HTTP, y todo lo que hay delante —resolver el token, decidir de quién es un
/// ticket, calcular estadísticas— se prueba contra una implementación falsa.
/// </summary>
public interface IClienteAzureDevOps
{
    /// <summary>Los identificadores que devuelve una consulta WIQL. Nulo = la consulta por omisión.</summary>
    Task<IReadOnlyList<int>> ConsultarIdsAsync(
        CredencialesDevOps credenciales, string? wiql, CancellationToken ct = default);

    /// <summary>Los work items completos, en lotes de 200 (el tope que admite la API).</summary>
    Task<IReadOnlyList<WorkItemDevOps>> ObtenerWorkItemsAsync(
        CredencialesDevOps credenciales, IReadOnlyCollection<int> ids, CancellationToken ct = default);

    Task<IReadOnlyList<ComentarioDevOps>> ObtenerComentariosAsync(
        CredencialesDevOps credenciales, int numero, CancellationToken ct = default);

    Task PublicarComentarioAsync(
        CredencialesDevOps credenciales, int numero, string textoHtml, CancellationToken ct = default);

    /// <summary>Sube un archivo como adjunto y devuelve su URL, para embeberla en un comentario.</summary>
    Task<string> SubirAdjuntoAsync(
        CredencialesDevOps credenciales, byte[] contenido, string nombre, CancellationToken ct = default);

    /// <summary>Reasigna el work item. Con el correo vacío lo DESASIGNA. Devuelve el asignado resultante.</summary>
    Task<(string nombre, string correo)> ReasignarAsync(
        CredencialesDevOps credenciales, int numero, string? correoOVacio, CancellationToken ct = default);

    /// <summary>Cambia System.State. Devuelve el estado que quedó.</summary>
    Task<string> CambiarEstadoAsync(
        CredencialesDevOps credenciales, int numero, string nuevoEstado, CancellationToken ct = default);

    /// <summary>
    /// Mueve el work item a una COLUMNA del tablero, que no es lo mismo que cambiarle el estado.
    ///
    /// <para>Hace falta cuando el estado no determina la columna: tableros con dos columnas sobre el
    /// mismo estado, o con columnas partidas. Descubre el campo del tablero leyendo el propio work
    /// item, porque su nombre lleva dentro el identificador del tablero y ése cambia por equipo.</para>
    /// </summary>
    Task<ColumnaDeTablero> CambiarColumnaAsync(
        CredencialesDevOps credenciales, int numero, string columna, bool mitadHecha,
        CancellationToken ct = default);

    /// <summary>En qué columna está ahora. Para poder enseñarlo sin cambiar nada.</summary>
    Task<ColumnaDeTablero> LeerColumnaAsync(
        CredencialesDevOps credenciales, int numero, CancellationToken ct = default);

    Task CambiarPrioridadAsync(
        CredencialesDevOps credenciales, int numero, int prioridad, CancellationToken ct = default);

    /// <summary>
    /// Escribe la estimación en el campo Effort. Devuelve si se pudo y, si no, el aviso: hay
    /// plantillas de proceso donde ese campo no existe en ese tipo de work item.
    /// </summary>
    Task<(bool escrito, string aviso)> EscribirEstimacionAsync(
        CredencialesDevOps credenciales, int numero, double horas, CancellationToken ct = default);

    /// <summary>
    /// Suma horas al Completed Work y, si se pide, baja el Remaining Work. Devuelve false si el tipo
    /// de work item no tiene esos campos (4xx), que no es un error fatal; los fallos transitorios
    /// (5xx y red) se propagan para poder reintentar.
    /// </summary>
    Task<bool> SumarTrabajoCompletadoAsync(
        CredencialesDevOps credenciales, int numero, double horasDelta, bool reducirRestante,
        CancellationToken ct = default);

    Task<IReadOnlyList<BugHijoDevOps>> ObtenerBugsHijosAsync(
        CredencialesDevOps credenciales, int numero, CancellationToken ct = default);

    Task<IReadOnlyList<CambioDeAsignacionDevOps>> ObtenerHistorialDeAsignacionAsync(
        CredencialesDevOps credenciales, int numero, CancellationToken ct = default);

    /// <summary>Comprueba que el token sirva y devuelve con qué cuenta lo identifica DevOps.</summary>
    Task<(bool ok, string mensaje)> ProbarCredencialesAsync(
        CredencialesDevOps credenciales, CancellationToken ct = default);
}

/// <summary>
/// El cliente de Azure DevOps: <c>HttpClient</c> y JSON, sin SDK.
///
/// Se habla con la API REST directamente, igual que en el escritorio y por el mismo motivo: el
/// cliente oficial arrastra media plataforma para las pocas operaciones que se usan de él. La
/// versión de API (7.0, y 7.1-preview.3 para comentarios) es la MISMA que el escritorio: mientras
/// las dos aplicaciones convivan no tiene sentido que una hable un dialecto distinto.
///
/// El <c>HttpClient</c> lo entrega <see cref="IHttpClientFactory"/>. En el escritorio cada llamada
/// hacía <c>new HttpClient()</c> y se salía con la suya porque eran unas pocas al día desde una sola
/// máquina; en un servidor eso agota los sockets, porque cada instancia deja su conexión en
/// TIME_WAIT durante minutos.
/// </summary>
public partial class AzureDevOpsService(HttpClient http) : IClienteAzureDevOps
{
    /// <summary>Campo de DevOps donde vive la estimación. Es el que pidió el equipo.</summary>
    public const string CampoEsfuerzo = "Microsoft.VSTS.Scheduling.Effort";

    private const string CampoTrabajoHecho = "Microsoft.VSTS.Scheduling.CompletedWork";
    private const string CampoTrabajoRestante = "Microsoft.VSTS.Scheduling.RemainingWork";

    /// <summary>El tope de identificadores por petición que admite <c>/wit/workitems</c>.</summary>
    private const int TamanoDeLote = 200;

    private static readonly string CamposDeSincronizacion = string.Join(",",
    [
        "System.Id", "System.Title", "System.WorkItemType", "System.State",
        "System.Description", "System.AssignedTo", "System.AreaPath",
        "System.IterationPath", "System.Tags", "System.CreatedDate",
        "System.ChangedDate", "Microsoft.VSTS.Common.Priority",
        "Microsoft.VSTS.Scheduling.StoryPoints", "System.CommentCount",
        // La estimación puede venir puesta desde DevOps: si ya está, no hay que volver a pedirla.
        CampoEsfuerzo
    ]);

    // ── Consultas ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> ConsultarIdsAsync(
        CredencialesDevOps credenciales, string? wiql, CancellationToken ct = default)
    {
        var consulta = wiql ?? ConstruirWiqlDeSincronizacion(credenciales.Proyecto, null);
        var cuerpo = JsonSerializer.Serialize(new { query = consulta });

        using var peticion = Nueva(HttpMethod.Post, credenciales,
            $"{credenciales.OrgUrl}/{Uri.EscapeDataString(credenciales.Proyecto)}/_apis/wit/wiql?api-version=7.0");
        peticion.Content = new StringContent(cuerpo, Encoding.UTF8, "application/json");

        using var respuesta = await EnviarAsync(peticion, ct);

        // Los dos fallos que la gente puede corregir por su cuenta se nombran; el resto sale con su
        // código, que es lo único honesto cuando no sabemos qué pasó.
        if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
            throw new ErrorDeAzureDevOps(
                "El token de Azure DevOps es inválido, expiró o no tiene permiso de lectura. " +
                "Genera uno nuevo con «Work Items → Read & write».");

        if (respuesta.StatusCode == HttpStatusCode.NotFound)
            throw new ErrorDeAzureDevOps(
                $"No se encontró la organización o el proyecto «{credenciales.Proyecto}» en Azure DevOps.");

        await ExigirExitoAsync(respuesta, "consultar los work items", ct);

        using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("workItems", out var items)) return [];

        return items.EnumerateArray().Select(wi => wi.GetProperty("id").GetInt32()).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkItemDevOps>> ObtenerWorkItemsAsync(
        CredencialesDevOps credenciales, IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];

        var resultado = new List<WorkItemDevOps>(ids.Count);
        foreach (var lote in EnLotes(ids, TamanoDeLote))
        {
            ct.ThrowIfCancellationRequested();

            using var peticion = Nueva(HttpMethod.Get, credenciales,
                $"{credenciales.OrgUrl}/_apis/wit/workitems" +
                $"?ids={string.Join(",", lote)}&fields={CamposDeSincronizacion}&api-version=7.0");

            using var respuesta = await EnviarAsync(peticion, ct);
            await ExigirExitoAsync(respuesta, "traer los work items", ct);

            using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
                resultado.Add(LeerWorkItem(item, credenciales));
        }
        return resultado;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ComentarioDevOps>> ObtenerComentariosAsync(
        CredencialesDevOps credenciales, int numero, CancellationToken ct = default)
    {
        using var peticion = Nueva(HttpMethod.Get, credenciales,
            $"{credenciales.OrgUrl}/{Uri.EscapeDataString(credenciales.Proyecto)}" +
            $"/_apis/wit/workItems/{numero}/comments?api-version=7.1-preview.3");

        using var respuesta = await EnviarAsync(peticion, ct);
        await ExigirExitoAsync(respuesta, $"leer los comentarios del ticket #{numero}", ct);

        using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("comments", out var comentarios)) return [];

        var resultado = new List<ComentarioDevOps>();
        foreach (var c in comentarios.EnumerateArray())
        {
            var texto = c.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";

            var autor = "";
            if (c.TryGetProperty("createdBy", out var quien))
                autor = quien.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";

            var creado = DateTime.UtcNow;
            if (c.TryGetProperty("createdDate", out var fecha) && DateTime.TryParse(fecha.GetString(), out var f))
                creado = f.ToUniversalTime();

            resultado.Add(new ComentarioDevOps(texto, autor, creado));
        }
        return resultado;
    }

    // ── Escritura ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task PublicarComentarioAsync(
        CredencialesDevOps credenciales, int numero, string textoHtml, CancellationToken ct = default)
    {
        using var peticion = Nueva(HttpMethod.Post, credenciales,
            $"{credenciales.OrgUrl}/{Uri.EscapeDataString(credenciales.Proyecto)}" +
            $"/_apis/wit/workItems/{numero}/comments?api-version=7.1-preview.3");
        peticion.Content = new StringContent(
            JsonSerializer.Serialize(new { text = textoHtml }), Encoding.UTF8, "application/json");

        using var respuesta = await EnviarAsync(peticion, ct);
        await ExigirExitoAsync(respuesta, $"publicar el comentario en el ticket #{numero}", ct);
    }

    /// <inheritdoc />
    public async Task<string> SubirAdjuntoAsync(
        CredencialesDevOps credenciales, byte[] contenido, string nombre, CancellationToken ct = default)
    {
        using var peticion = Nueva(HttpMethod.Post, credenciales,
            $"{credenciales.OrgUrl}/{Uri.EscapeDataString(credenciales.Proyecto)}" +
            $"/_apis/wit/attachments?fileName={Uri.EscapeDataString(nombre)}&api-version=7.0");
        peticion.Content = new ByteArrayContent(contenido);
        peticion.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var respuesta = await EnviarAsync(peticion, ct);
        await ExigirExitoAsync(respuesta, $"subir la evidencia «{nombre}»", ct);

        using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("url", out var url) && url.GetString() is { } valor
            ? valor
            : throw new ErrorDeAzureDevOps("Azure DevOps aceptó la evidencia pero no devolvió su dirección.");
    }

    /// <inheritdoc />
    public async Task<(string nombre, string correo)> ReasignarAsync(
        CredencialesDevOps credenciales, int numero, string? correoOVacio, CancellationToken ct = default)
    {
        object parche = string.IsNullOrWhiteSpace(correoOVacio)
            ? new[] { new { op = "remove", path = "/fields/System.AssignedTo" } }
            : new object[] { new { op = "add", path = "/fields/System.AssignedTo", value = correoOVacio.Trim() } };

        var payload = await ParchearAsync(credenciales, numero, parche, "reasignar el ticket", ct);

        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.TryGetProperty("fields", out var campos)
            ? LeerAsignado(campos)
            : ("", "");
    }

    /// <inheritdoc />
    public async Task<string> CambiarEstadoAsync(
        CredencialesDevOps credenciales, int numero, string nuevoEstado, CancellationToken ct = default)
    {
        var parche = new object[] { new { op = "add", path = "/fields/System.State", value = nuevoEstado } };
        var payload = await ParchearAsync(credenciales, numero, parche, "cambiar el estado del ticket", ct);

        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.TryGetProperty("fields", out var campos)
               && campos.TryGetProperty("System.State", out var estado)
            ? estado.GetString() ?? nuevoEstado
            : nuevoEstado;
    }

    /// <inheritdoc />
    public async Task<ColumnaDeTablero> CambiarColumnaAsync(
        CredencialesDevOps credenciales, int numero, string columna, bool mitadHecha,
        CancellationToken ct = default)
    {
        // ── Por qué hay que LEER antes de escribir ───────────────────────────────
        //
        // La columna del tablero NO se escribe en «System.BoardColumn»: ese campo es calculado y de
        // solo lectura, y DevOps rechaza el parche. La que se escribe es «WEF_{guid}_Kanban.Column»,
        // donde el guid es el del TABLERO —uno por equipo—, así que el nombre del campo no se puede
        // saber de antemano: hay que descubrirlo en el propio work item.
        //
        // Se pide sin filtro de campos a propósito: los WEF_ no salen si se piden por nombre, porque
        // para pedirlos por nombre habría que saberlos ya.
        var campos = await CamposDeTableroAsync(credenciales, numero, ct);

        if (campos.CampoColumna is null)
            throw new ErrorDeAzureDevOps(
                $"El work item #{numero} no pertenece a ningún tablero de este proyecto, así que no " +
                "tiene columna que cambiar. Suele pasar con los tipos que no aparecen en el tablero " +
                "—una tarea hija, por ejemplo— o cuando el área del work item es de otro equipo.");

        var parche = new List<object>
        {
            new { op = "add", path = $"/fields/{campos.CampoColumna}", value = columna }
        };

        // La mitad derecha de una columna partida es un campo aparte y BOOLEANO, no un estado ni un
        // nombre de columna. Solo se manda si el tablero tiene esa columna partida: en un tablero sin
        // partir el campo no existe y mandarlo haría fallar el parche entero.
        if (campos.CampoMitadHecha is not null)
            parche.Add(new { op = "add", path = $"/fields/{campos.CampoMitadHecha}", value = mitadHecha });

        var payload = await ParchearAsync(credenciales, numero, parche.ToArray(),
            $"mover el ticket a la columna «{columna}»", ct);

        using var doc = JsonDocument.Parse(payload);
        return LeerColumna(doc.RootElement);
    }

    /// <inheritdoc />
    public async Task<ColumnaDeTablero> LeerColumnaAsync(
        CredencialesDevOps credenciales, int numero, CancellationToken ct = default)
    {
        using var peticion = Nueva(HttpMethod.Get, credenciales,
            $"{credenciales.OrgUrl}/{Uri.EscapeDataString(credenciales.Proyecto)}" +
            $"/_apis/wit/workitems/{numero}?api-version=7.0");

        using var respuesta = await EnviarAsync(peticion, ct);
        await ExigirExitoAsync(respuesta, $"leer la columna del ticket #{numero}", ct);

        using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
        return LeerColumna(doc.RootElement);
    }

    /// <summary>
    /// Los nombres de los campos de tablero de ESTE work item, descubiertos leyéndolo.
    ///
    /// <para>Se buscan por forma y no por una lista escrita: «WEF_», el identificador del tablero y
    /// el sufijo. Escribir el guid a fuego ataría la aplicación a un tablero concreto, y basta con
    /// que alguien cree un equipo nuevo para que deje de valer.</para>
    /// </summary>
    private async Task<(string? CampoColumna, string? CampoMitadHecha)> CamposDeTableroAsync(
        CredencialesDevOps credenciales, int numero, CancellationToken ct)
    {
        using var peticion = Nueva(HttpMethod.Get, credenciales,
            $"{credenciales.OrgUrl}/{Uri.EscapeDataString(credenciales.Proyecto)}" +
            $"/_apis/wit/workitems/{numero}?api-version=7.0");

        using var respuesta = await EnviarAsync(peticion, ct);
        await ExigirExitoAsync(respuesta, $"leer los campos del ticket #{numero}", ct);

        using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("fields", out var campos)) return (null, null);

        string? columna = null, mitad = null;
        foreach (var campo in campos.EnumerateObject())
        {
            if (columna is null && CampoDeColumna().IsMatch(campo.Name)) columna = campo.Name;
            else if (mitad is null && CampoDeMitadHecha().IsMatch(campo.Name)) mitad = campo.Name;
        }

        return (columna, mitad);
    }

    /// <summary>
    /// En qué columna quedó, leído de los campos calculados que devuelve DevOps.
    ///
    /// <para>Aquí SÍ se lee «System.BoardColumn», que es de solo lectura pero perfectamente legible:
    /// es lo que dice en qué columna acabó de verdad, que puede no ser la que se pidió si el tablero
    /// tiene reglas propias.</para>
    /// </summary>
    private static ColumnaDeTablero LeerColumna(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("fields", out var campos)) return new ColumnaDeTablero("", false);

        var columna = campos.TryGetProperty("System.BoardColumn", out var c) ? c.GetString() ?? "" : "";
        var hecha = campos.TryGetProperty("System.BoardColumnDone", out var d)
                    && d.ValueKind == JsonValueKind.True;

        return new ColumnaDeTablero(columna, hecha);
    }

    // El guid del tablero va en medio del nombre, así que el patrón es lo único estable. Con tiempo
    // de espera acotado por lo mismo que los demás: el texto viene de un servidor ajeno.
    [GeneratedRegex(@"^WEF_[0-9A-Fa-f]{32}_Kanban\.Column$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CampoDeColumna();

    [GeneratedRegex(@"^WEF_[0-9A-Fa-f]{32}_Kanban\.Column\.Done$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CampoDeMitadHecha();

    /// <inheritdoc />
    public Task CambiarPrioridadAsync(
        CredencialesDevOps credenciales, int numero, int prioridad, CancellationToken ct = default)
    {
        var parche = new object[]
        {
            new { op = "add", path = "/fields/Microsoft.VSTS.Common.Priority", value = prioridad }
        };
        return ParchearAsync(credenciales, numero, parche, "cambiar la prioridad del ticket", ct);
    }

    /// <inheritdoc />
    public async Task<(bool escrito, string aviso)> EscribirEstimacionAsync(
        CredencialesDevOps credenciales, int numero, double horas, CancellationToken ct = default)
    {
        var parche = new object[] { new { op = "add", path = $"/fields/{CampoEsfuerzo}", value = horas } };

        using var peticion = NuevoParche(credenciales, numero, parche);
        using var respuesta = await EnviarAsync(peticion, ct);

        if (respuesta.IsSuccessStatusCode) return (true, "");

        // No se lanza: que un tipo de work item no tenga el campo Effort NO puede tirar por la borda
        // la estimación que la persona acaba de capturar. Quien llama la guarda igual en local y
        // enseña este aviso.
        return (false, $"Azure DevOps no aceptó el campo Effort ({(int)respuesta.StatusCode}): " +
                       Recortar(await respuesta.Content.ReadAsStringAsync(ct)));
    }

    /// <inheritdoc />
    public async Task<bool> SumarTrabajoCompletadoAsync(
        CredencialesDevOps credenciales, int numero, double horasDelta, bool reducirRestante,
        CancellationToken ct = default)
    {
        var url = $"{credenciales.OrgUrl}/{Uri.EscapeDataString(credenciales.Proyecto)}" +
                  $"/_apis/wit/workitems/{numero}?api-version=7.0";

        // Se LEE antes de sumar porque los campos de trabajo son acumulativos y la API solo admite
        // fijar el valor total, no incrementarlo.
        double hecho = 0, restante = -1;
        using (var lectura = Nueva(HttpMethod.Get, credenciales,
                   $"{url}&fields={CampoTrabajoHecho},{CampoTrabajoRestante}"))
        using (var respuestaLectura = await EnviarAsync(lectura, ct))
        {
            // 404 y los fallos de red salen como error: son transitorios o el ticket no existe, y en
            // ninguno de los dos casos hay nada que sumar.
            await ExigirExitoAsync(respuestaLectura, $"leer las horas del ticket #{numero}", ct);

            using var doc = JsonDocument.Parse(await respuestaLectura.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("fields", out var campos))
            {
                if (campos.TryGetProperty(CampoTrabajoHecho, out var h) && h.ValueKind == JsonValueKind.Number)
                    hecho = h.GetDouble();
                if (campos.TryGetProperty(CampoTrabajoRestante, out var r) && r.ValueKind == JsonValueKind.Number)
                    restante = r.GetDouble();
            }
        }

        var parche = new List<object>
        {
            new { op = "add", path = $"/fields/{CampoTrabajoHecho}", value = Math.Round(hecho + horasDelta, 2) }
        };
        if (reducirRestante && restante >= 0)
            parche.Add(new
            {
                op = "add",
                path = $"/fields/{CampoTrabajoRestante}",
                value = Math.Round(Math.Max(0, restante - horasDelta), 2)
            });

        using var peticion = NuevoParche(credenciales, numero, parche);
        using var respuesta = await EnviarAsync(peticion, ct);

        if (respuesta.IsSuccessStatusCode) return true;

        // 5xx es transitorio y se propaga para poder reintentar; 4xx significa que ese tipo de work
        // item no admite estos campos, que no es un fallo que reintentar arregle.
        if ((int)respuesta.StatusCode >= 500)
            await ExigirExitoAsync(respuesta, $"registrar las horas del ticket #{numero}", ct);

        return false;
    }

    // ── Ficha del ticket ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<BugHijoDevOps>> ObtenerBugsHijosAsync(
        CredencialesDevOps credenciales, int numero, CancellationToken ct = default)
    {
        using var peticion = Nueva(HttpMethod.Get, credenciales,
            $"{credenciales.OrgUrl}/_apis/wit/workitems/{numero}?$expand=relations&api-version=7.0");

        using var respuesta = await EnviarAsync(peticion, ct);
        await ExigirExitoAsync(respuesta, $"leer las relaciones del ticket #{numero}", ct);

        var hijos = new List<int>();
        using (var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct)))
        {
            if (!doc.RootElement.TryGetProperty("relations", out var relaciones)
                || relaciones.ValueKind != JsonValueKind.Array)
                return [];

            foreach (var r in relaciones.EnumerateArray())
            {
                // Hierarchy-Forward es «hijo»; Hierarchy-Reverse sería el padre.
                if (TextoONulo(r, "rel") != "System.LinkTypes.Hierarchy-Forward") continue;
                if (TextoONulo(r, "url") is not { } url) continue;

                var ultimo = url[(url.LastIndexOf('/') + 1)..];
                if (int.TryParse(ultimo, out int hijo)) hijos.Add(hijo);
            }
        }
        if (hijos.Count == 0) return [];

        // La relación no dice de qué tipo es el hijo, así que hay que traerlos para quedarse solo con
        // los Bug. No hay forma de saberlo antes.
        const string campos = "System.Id,System.Title,System.WorkItemType,System.State";
        var resultado = new List<BugHijoDevOps>();

        foreach (var lote in EnLotes(hijos, TamanoDeLote))
        {
            ct.ThrowIfCancellationRequested();

            using var peticionLote = Nueva(HttpMethod.Get, credenciales,
                $"{credenciales.OrgUrl}/_apis/wit/workitems" +
                $"?ids={string.Join(",", lote)}&fields={campos}&api-version=7.0");

            using var respuestaLote = await EnviarAsync(peticionLote, ct);
            await ExigirExitoAsync(respuestaLote, "leer los bugs hijos", ct);

            using var doc = JsonDocument.Parse(await respuestaLote.Content.ReadAsStringAsync(ct));
            foreach (var item in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var campo = item.GetProperty("fields");
                if (!Texto(campo, "System.WorkItemType").Equals("Bug", StringComparison.OrdinalIgnoreCase))
                    continue;

                int id = item.GetProperty("id").GetInt32();
                resultado.Add(new BugHijoDevOps(
                    id,
                    Texto(campo, "System.Title"),
                    Texto(campo, "System.State"),
                    UrlDelWorkItem(credenciales, id)));
            }
        }
        return resultado.OrderBy(b => b.Id).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CambioDeAsignacionDevOps>> ObtenerHistorialDeAsignacionAsync(
        CredencialesDevOps credenciales, int numero, CancellationToken ct = default)
    {
        using var peticion = Nueva(HttpMethod.Get, credenciales,
            $"{credenciales.OrgUrl}/_apis/wit/workitems/{numero}/updates?api-version=7.0");

        using var respuesta = await EnviarAsync(peticion, ct);
        await ExigirExitoAsync(respuesta, $"leer el historial del ticket #{numero}", ct);

        using var doc = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("value", out var revisiones)) return [];

        var cambios = new List<CambioDeAsignacionDevOps>();
        foreach (var revision in revisiones.EnumerateArray())
        {
            if (!revision.TryGetProperty("fields", out var campos)) continue;

            // DevOps genera una revisión por CADA edición y la mayoría no tocan el asignado: sin este
            // filtro el conteo de devoluciones contaría cambios de título.
            if (!campos.TryGetProperty("System.AssignedTo", out var cambio)) continue;

            var (de, _) = LeerIdentidad(cambio, "oldValue");
            var (a, correoDeA) = LeerIdentidad(cambio, "newValue");

            // Sin fecha no se puede ordenar, y el orden es justo lo que da sentido al conteo.
            DateTime fecha = default;
            if (revision.TryGetProperty("revisedDate", out var revisada)
                && revisada.ValueKind == JsonValueKind.String
                && DateTime.TryParse(revisada.GetString(), out var f1))
                fecha = f1;
            else if (campos.TryGetProperty("System.ChangedDate", out var cambiada)
                     && cambiada.TryGetProperty("newValue", out var nueva)
                     && nueva.ValueKind == JsonValueKind.String
                     && DateTime.TryParse(nueva.GetString(), out var f2))
                fecha = f2;

            cambios.Add(new CambioDeAsignacionDevOps(
                fecha,
                string.IsNullOrWhiteSpace(de) ? null : de,
                string.IsNullOrWhiteSpace(a) ? null : a,
                string.IsNullOrWhiteSpace(correoDeA) ? null : correoDeA));
        }
        return cambios.OrderBy(c => c.Fecha).ToList();
    }

    // ── Prueba de credenciales ───────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<(bool ok, string mensaje)> ProbarCredencialesAsync(
        CredencialesDevOps credenciales, CancellationToken ct = default)
    {
        try
        {
            // Se valida pidiendo el PROYECTO configurado, NO «/_apis/connectionData». Ese endpoint
            // devuelve HTTP 400 en api-version 7.0 con los tokens de formato nuevo (respaldados por
            // Entra, del estilo «...JQQJ99...AZDO...»), y hacía que «Probar conexión» fallara con
            // tokens perfectamente válidos —que sí sirven para leer work items y publicar
            // comentarios—. Pedir el proyecto confirma de una vez las tres cosas que hacen falta:
            // que el token sirve, que la organización responde y que ese proyecto es visible para él;
            // y permite distinguir «token inválido» de «proyecto equivocado».
            using var peticion = Nueva(HttpMethod.Get, credenciales,
                $"{credenciales.OrgUrl}/_apis/projects/{Uri.EscapeDataString(credenciales.Proyecto)}?api-version=7.0");

            using var respuesta = await EnviarAsync(peticion, ct);

            // Azure DevOps responde 401 o, en algunos casos, 203 (la página de inicio de sesión)
            // cuando el token no autentica.
            if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NonAuthoritativeInformation)
                return (false, "El token es inválido, expiró o no tiene permiso de lectura. " +
                               "Genera uno nuevo en Azure DevOps con «Work Items → Read & write».");

            if (respuesta.StatusCode == HttpStatusCode.NotFound)
                return (false, $"La organización responde, pero no se encontró el proyecto «{credenciales.Proyecto}» " +
                               "(o tu token no tiene acceso a él). Revísalo con el líder.");

            if (!respuesta.IsSuccessStatusCode)
                return (false, $"Azure DevOps respondió {(int)respuesta.StatusCode} {respuesta.ReasonPhrase}. " +
                               "Revisa el token y sus permisos.");

            // DevOps identifica al dueño del token en este encabezado, con formato «{id}:{cuenta}»,
            // para que la persona confirme que es SU propia cuenta y no la de otro.
            string? cuenta = null;
            if (respuesta.Headers.TryGetValues("X-VSS-UserData", out var valores))
            {
                var crudo = valores.FirstOrDefault();
                var separador = crudo?.IndexOf(':') ?? -1;
                if (separador >= 0 && separador < crudo!.Length - 1) cuenta = crudo[(separador + 1)..];
            }

            return (true, string.IsNullOrWhiteSpace(cuenta)
                ? $"Conexión correcta con el proyecto {credenciales.Proyecto}."
                : $"Conectado como «{cuenta}» en el proyecto {credenciales.Proyecto}.");
        }
        catch (ErrorDeAzureDevOps ex)
        {
            return (false, ex.Message);
        }
    }

    // ── WIQL ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// La consulta de sincronización: base (no removidos) más los filtros opcionales.
    ///
    /// Es estática y pura para poder probarla: una WIQL mal armada no falla, simplemente devuelve
    /// menos tickets de los que debía, y eso no lo delata nada.
    /// </summary>
    public static string ConstruirWiqlDeSincronizacion(string proyecto, FiltroDeSincronizacion? filtro)
    {
        // WIQL usa comillas simples para las cadenas, así que la única forma de cerrarlas antes de
        // tiempo es una comilla dentro del valor; se duplica, que es como se escapan.
        static string Escapar(string s) => s.Replace("'", "''");

        var sb = new StringBuilder();
        sb.Append("SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '")
          .Append(Escapar(proyecto)).Append('\'');
        sb.Append(" AND [System.State] <> 'Removed'");

        if (filtro is { EstaVacio: false })
        {
            if (filtro.Tipos.Count > 0)
                sb.Append(" AND [System.WorkItemType] IN (")
                  .Append(string.Join(",", filtro.Tipos.Select(t => $"'{Escapar(t)}'"))).Append(')');

            if (filtro.Estados.Count > 0)
                sb.Append(" AND [System.State] IN (")
                  .Append(string.Join(",", filtro.Estados.Select(e => $"'{Escapar(e)}'"))).Append(')');

            if (filtro.Asignados.Count > 0)
                sb.Append(" AND [System.AssignedTo] IN (")
                  .Append(string.Join(",", filtro.Asignados.Select(a => $"'{Escapar(a)}'"))).Append(')');

            // @Me lo resuelve DevOps contra el dueño del token: no hay nombre ni correo que
            // entrecomillar, y por eso mismo no falla cuando el correo de la ficha no coincide con el
            // de la cuenta de DevOps.
            if (filtro.SoloMisAsignados)
                sb.Append(" AND [System.AssignedTo] = @Me");

            // @Today - N es aritmética de fechas de WIQL; el número va sin comillas.
            if (filtro.CambiadosEnDias is int dias && dias > 0)
                sb.Append(" AND [System.ChangedDate] >= @Today - ").Append(dias);

            // La fecha de CREACIÓN, que es otra cosa: acota lo que acaba de aparecer, no lo que
            // alguien movió. Se pueden pedir las dos a la vez y se suman, como cualquier otro par de
            // cláusulas de aquí.
            if (filtro.CreadosEnDias is int nuevos && nuevos > 0)
                sb.Append(" AND [System.CreatedDate] >= @Today - ").Append(nuevos);
        }

        sb.Append(" ORDER BY [System.ChangedDate] DESC");
        return sb.ToString();
    }

    /// <summary>
    /// Los work items ASIGNADOS a alguien y NO cerrados: los candidatos a convertirse en trabajo con
    /// seguimiento. Descarta lo obvio en el servidor; el filtro autoritativo lo hace después quien
    /// llama, por si la plantilla de proceso usa otros nombres de estado.
    /// </summary>
    public static string WiqlDeAsignadosAbiertos(string proyecto) =>
        $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{proyecto.Replace("'", "''")}' " +
        "AND [System.AssignedTo] <> '' " +
        "AND [System.State] NOT IN ('Removed','Done','Closed','Completed') " +
        "ORDER BY [System.ChangedDate] DESC";

    // ── Plomería HTTP ────────────────────────────────────────────────────────────

    /// <summary>
    /// Una petición firmada con el token de quien la provocó.
    ///
    /// La autenticación va por petición y no en <c>DefaultRequestHeaders</c>: el cliente lo comparte
    /// toda la aplicación y escribir ahí la cabecera haría que dos peticiones simultáneas de personas
    /// distintas se pisaran el token — con el resultado de que un comentario acabara firmado por
    /// otro.
    /// </summary>
    private static HttpRequestMessage Nueva(HttpMethod metodo, CredencialesDevOps credenciales, string url)
    {
        var peticion = new HttpRequestMessage(metodo, url);

        // Azure DevOps usa Basic con el usuario vacío y el token como contraseña.
        var codificado = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{credenciales.Pat}"));
        peticion.Headers.Authorization = new AuthenticationHeaderValue("Basic", codificado);
        peticion.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return peticion;
    }

    private static HttpRequestMessage NuevoParche(CredencialesDevOps credenciales, int numero, object parche)
    {
        var peticion = Nueva(HttpMethod.Patch, credenciales,
            $"{credenciales.OrgUrl}/{Uri.EscapeDataString(credenciales.Proyecto)}" +
            $"/_apis/wit/workitems/{numero}?api-version=7.0");

        // El tipo de contenido es json-PATCH y no json a secas: con el otro, DevOps rechaza el cuerpo.
        peticion.Content = new StringContent(
            JsonSerializer.Serialize(parche), Encoding.UTF8, "application/json-patch+json");
        return peticion;
    }

    private async Task<string> ParchearAsync(
        CredencialesDevOps credenciales, int numero, object parche, string queSeIntentaba, CancellationToken ct)
    {
        using var peticion = NuevoParche(credenciales, numero, parche);
        using var respuesta = await EnviarAsync(peticion, ct);
        await ExigirExitoAsync(respuesta, queSeIntentaba, ct);
        return await respuesta.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Envía y convierte los fallos de red en un error que se puede enseñar.
    ///
    /// Sin esto, un servidor caído o un nombre que no resuelve salen como
    /// <c>HttpRequestException</c> y acaban en un 500: para quien lo mira, «la aplicación se rompió»
    /// en vez de «DevOps no contesta», que es lo que de verdad pasó y lo que orienta a quien tiene
    /// que arreglarlo.
    /// </summary>
    private async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage peticion, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(peticion, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ErrorDeAzureDevOps(
                "Azure DevOps tardó demasiado en contestar. Vuelve a intentarlo en un momento.");
        }
        catch (HttpRequestException ex)
        {
            throw new ErrorDeAzureDevOps($"No se pudo contactar con Azure DevOps: {ex.Message}");
        }
    }

    private static async Task ExigirExitoAsync(
        HttpResponseMessage respuesta, string queSeIntentaba, CancellationToken ct)
    {
        if (respuesta.IsSuccessStatusCode) return;

        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        throw new ErrorDeAzureDevOps(
            $"Azure DevOps rechazó {queSeIntentaba} ({(int)respuesta.StatusCode}): {Recortar(cuerpo)}");
    }

    /// <summary>
    /// El cuerpo de un rechazo, acortado. DevOps devuelve páginas enteras de HTML en algunos errores
    /// y volcarlas en un aviso no ayuda a nadie.
    /// </summary>
    private static string Recortar(string s) =>
        string.IsNullOrWhiteSpace(s) ? "(sin detalle)" : s.Length <= 300 ? s : s[..300];

    private static IEnumerable<IEnumerable<int>> EnLotes(IEnumerable<int> origen, int tamano)
    {
        var lista = origen.ToList();
        for (int i = 0; i < lista.Count; i += tamano)
            yield return lista.Skip(i).Take(tamano);
    }

    // ── Lectura del JSON ─────────────────────────────────────────────────────────

    private WorkItemDevOps LeerWorkItem(JsonElement item, CredencialesDevOps credenciales)
    {
        var campos = item.GetProperty("fields");
        var id = item.GetProperty("id").GetInt32();
        var (asignado, correo) = LeerAsignado(campos);

        return new WorkItemDevOps(
            id,
            Texto(campos, "System.Title"),
            Texto(campos, "System.WorkItemType"),
            Texto(campos, "System.State"),
            Texto(campos, "Microsoft.VSTS.Common.Priority"),
            TextoONulo(campos, "System.Description"),
            UrlDelWorkItem(credenciales, id),
            Texto(campos, "System.AreaPath"),
            Texto(campos, "System.IterationPath"),
            Texto(campos, "System.Tags"),
            asignado,
            correo,
            Numero(campos, "Microsoft.VSTS.Scheduling.StoryPoints"),
            Numero(campos, CampoEsfuerzo),
            Entero(campos, "System.CommentCount"),
            Fecha(campos, "System.CreatedDate"),
            Fecha(campos, "System.ChangedDate"));
    }

    private static string UrlDelWorkItem(CredencialesDevOps credenciales, int id) =>
        $"{credenciales.OrgUrl}/{Uri.EscapeDataString(credenciales.Proyecto)}/_workitems/edit/{id}";

    /// <summary>
    /// Lee «System.AssignedTo» y devuelve (nombreParaMostrar, correo). En la API el campo puede venir
    /// como objeto {displayName, uniqueName, …}, como texto suelto o ausente.
    /// </summary>
    private static (string nombre, string correo) LeerAsignado(JsonElement campos)
    {
        if (!campos.TryGetProperty("System.AssignedTo", out var asignado)) return ("", "");

        if (asignado.ValueKind == JsonValueKind.Object)
            return (asignado.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "",
                    asignado.TryGetProperty("uniqueName", out var un) ? un.GetString() ?? "" : "");

        if (asignado.ValueKind == JsonValueKind.String) return (asignado.GetString() ?? "", "");
        return ("", "");
    }

    /// <summary>Lee oldValue/newValue de un cambio de identidad, que puede venir como objeto o texto.</summary>
    private static (string nombre, string correo) LeerIdentidad(JsonElement cambio, string cual)
    {
        if (!cambio.TryGetProperty(cual, out var valor)) return ("", "");

        if (valor.ValueKind == JsonValueKind.Object)
            return (valor.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "",
                    valor.TryGetProperty("uniqueName", out var un) ? un.GetString() ?? "" : "");

        if (valor.ValueKind == JsonValueKind.String) return (valor.GetString() ?? "", "");
        return ("", "");
    }

    private static string Texto(JsonElement campos, string clave)
    {
        if (!campos.TryGetProperty(clave, out var valor)) return "";

        // La prioridad llega como número aunque se guarde y se enseñe como texto.
        return valor.ValueKind switch
        {
            JsonValueKind.String => valor.GetString() ?? "",
            JsonValueKind.Number => valor.GetRawText(),
            _ => ""
        };
    }

    private static string? TextoONulo(JsonElement campos, string clave) =>
        campos.TryGetProperty(clave, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;

    private static double? Numero(JsonElement campos, string clave) =>
        campos.TryGetProperty(clave, out var valor) && valor.ValueKind == JsonValueKind.Number
            ? valor.GetDouble()
            : null;

    private static int Entero(JsonElement campos, string clave) =>
        campos.TryGetProperty(clave, out var valor) && valor.ValueKind == JsonValueKind.Number
            ? valor.GetInt32()
            : 0;

    private static DateTime? Fecha(JsonElement campos, string clave) =>
        campos.TryGetProperty(clave, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && DateTime.TryParse(valor.GetString(), out var fecha)
            ? fecha.ToUniversalTime()
            : null;
}
