using System.Net;
using System.Text.RegularExpressions;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Dtos.DevOps;
using AdminWeb.Shared.Dtos.Pool;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// El puente entre una actividad del pool y su work item de Azure DevOps: ligarlos, empujar hacia
/// allá el esfuerzo y la prioridad, y comentar en el ticket desde aquí.
///
/// <para><b>La regla que gobierna esta clase entera: lo local primero, y pase lo que pase.</b> Azure
/// DevOps está al otro lado de la red y puede tardar, rechazar o no estar. Publicar una actividad,
/// editarla o tomarla son operaciones que terminan en la base de aquí y no pueden depender de que
/// un servidor ajeno conteste. Por eso ningún método de este servicio se llama ANTES de un
/// <c>SaveChanges</c>, ninguno lanza hacia quien lo invoca y todos van acotados por un reloj propio,
/// mucho más corto que el del cliente HTTP.</para>
///
/// <para><b>Y la contraria, que es la que hace que esto sirva de algo: no puede fallar en
/// silencio.</b> Un empuje que no llegó deja tres rastros —el aviso en el mensaje que ve quien hizo
/// el cambio, el motivo guardado en la propia actividad y una entrada en la bitácora—, y la
/// actividad se queda marcada como pendiente hasta que de verdad llegue. Que el pool diga 8 h y
/// DevOps siga en blanco es aceptable durante un rato; que nadie pueda saberlo, no.</para>
///
/// <para><b>Lo pendiente se DERIVA, no se marca.</b> Cada actividad guarda lo ÚLTIMO que DevOps
/// confirmó (<c>DevOpsEsfuerzoEnviado</c>, <c>DevOpsPrioridadEnviada</c>) y «pendiente» es
/// simplemente que eso no coincida con lo que dice la actividad hoy. Así una edición posterior
/// vuelve a levantar la bandera sola, sin que ningún camino de código tenga que acordarse de nada, y
/// un reintento manda solo lo que de verdad falta. Es lo que hace representable el EMPUJE PARCIAL,
/// que es el caso real: la estimación entra y la prioridad no.</para>
///
/// <para><b>Lo que entra en DevOps también cuenta AQUÍ.</b> Cuando la prioridad llega al work item,
/// el ticket local se refleja y —si de verdad cambió— su compromiso de SLA se reajusta, igual que si
/// el cambio se hubiera hecho desde la pantalla de tickets y con el mismo código
/// (<see cref="ReconciliacionDePrioridadDeDevOps"/>). Antes esto no pasaba y el desfase era
/// silencioso: la fila del ticket se corregía sola en la siguiente sincronización, pero el plazo
/// seguía siendo el de la prioridad vieja. La salvedad del «de verdad cambió» importa porque lo que
/// dispara el empuje es la MARCA DE AGUA, no un cambio: mandar la prioridad que el work item ya
/// tenía es aquí el caso corriente, y no puede reprogramar nada.</para>
///
/// <para><b>Por qué no hay una cola de salida.</b> Lo que se manda no es una secuencia de sucesos
/// sino un ESTADO DESEADO: DevOps tiene que acabar con lo que la actividad dice AHORA. Con una cola,
/// tres ediciones hechas mientras DevOps estaba caído se reproducirían las tres, en orden, para
/// dejar exactamente el mismo resultado que la última; y bastaría con que dos se reprodujeran al
/// revés para escribir un número viejo encima del bueno. Con la marca de agua, el reintento manda lo
/// que dice la fila y no hay orden que respetar.</para>
/// </summary>
public partial class PoolDevOpsService(
    AppDbContext db,
    ICurrentUser usuario,
    SettingsService configuracion,
    UserSecretsService secretos,
    AuditService bitacora,
    IClienteAzureDevOps devops)
{
    /// <summary>Ámbito para el mensaje de las guardas, para que diga de qué se habla.</summary>
    private const string Ambito = "del vínculo del pool con Azure DevOps";

    /// <summary>
    /// Cuánto se espera a DevOps durante un empuje, como mucho.
    ///
    /// <para>El cliente HTTP está configurado a 100 s, que es razonable para una sincronización que
    /// alguien lanzó a propósito y está mirando. Aquí no: este empuje va COLGADO de guardar una
    /// actividad, y hacer que publicar tarde minuto y medio porque DevOps no contesta convertiría la
    /// integración en un estorbo. Pasado el plazo se corta, queda pendiente y se avisa — que es
    /// exactamente lo que hay que hacer con un servidor que no responde.</para>
    /// </summary>
    public static readonly TimeSpan Paciencia = TimeSpan.FromSeconds(15);

    /// <summary>Tope de un comentario. Da para explicar un avance, no para pegar un volcado.</summary>
    public const int MaxComentario = 4000;

    /// <summary>Los estados en los que una actividad todavía manda sobre su work item.</summary>
    private static bool SigueEnJuego(PoolActivityStatus estado) =>
        estado is PoolActivityStatus.Disponible or PoolActivityStatus.Tomada
               or PoolActivityStatus.EnRevision or PoolActivityStatus.Devuelta;

    // ── Resolver el vínculo ──────────────────────────────────────────────────────

    /// <summary>
    /// De lo que capturó la persona al NÚMERO del work item.
    ///
    /// <para>Es estática y pura para poder probarla sin base ni red, y para que
    /// <see cref="PoolActivityService"/> la use al validar un borrador aunque la integración no esté
    /// montada: ligar es una decisión que se toma en el formulario, y rechazarla ahí es lo que evita
    /// guardar un vínculo que después nadie entiende.</para>
    ///
    /// <para><b>Acepta el número o la dirección.</b> Lo que la gente tiene a mano es la barra del
    /// navegador, no el número suelto, y obligar a extraerlo a mano es obligar a equivocarse. Cuando
    /// vienen los dos y NO coinciden se rechaza en vez de elegir: un formulario que dice «#1234»
    /// sobre un enlace que apunta a «#5678» está mal de una de las dos maneras, y quedarse con
    /// cualquiera de ellas escribiría el esfuerzo en un ticket ajeno.</para>
    /// </summary>
    public static (bool ok, string error, int? numero) ResolverWorkItem(int? numero, string? enlace)
    {
        var delEnlace = NumeroEnElEnlace(enlace);

        if (numero is not int escrito) return (true, "", delEnlace);

        if (escrito <= 0)
            return (false, "El número del work item tiene que ser mayor que cero. Déjalo vacío si esta " +
                           "actividad no viene de un ticket de Azure DevOps.", null);

        if (delEnlace is int enElEnlace && enElEnlace != escrito)
            return (false, $"El número que escribiste (#{escrito}) no es el del enlace (#{enElEnlace}). " +
                           "Corrige uno de los dos: si se guardara cualquiera de ellos, el esfuerzo y la " +
                           "prioridad acabarían en el ticket equivocado.", null);

        return (true, "", escrito);
    }

    /// <summary>
    /// El número de work item que lleva dentro una dirección de Azure DevOps, si lo lleva.
    ///
    /// Se reconocen las dos formas que produce el propio DevOps: la ruta
    /// (<c>…/_workitems/edit/1234</c>) y la consulta (<c>…/_workitems?id=1234</c>). Cualquier otra
    /// dirección devuelve nulo y no es un error: el enlace de una actividad puede apuntar a un
    /// ticket de Freshdesk o a un correo, y eso simplemente no liga con nada.
    /// </summary>
    private static int? NumeroEnElEnlace(string? enlace)
    {
        if (string.IsNullOrWhiteSpace(enlace)) return null;

        var coincidencia = NumeroEnLaRuta().Match(enlace);
        if (!coincidencia.Success) coincidencia = NumeroEnLaConsulta().Match(enlace);

        return coincidencia.Success && int.TryParse(coincidencia.Groups[1].Value, out var n) && n > 0
            ? n
            : null;
    }

    // El tiempo de espera acota lo que puede costar un enlace rebuscado: el texto lo escribe una
    // persona y llega por la red, así que no puede decidir cuánto trabaja el servidor.
    [GeneratedRegex(@"_workitems/edit/(\d+)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NumeroEnLaRuta();

    [GeneratedRegex(@"_workitems[^?]*\?(?:.*&)?id=(\d+)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NumeroEnLaConsulta();

    // ── Ligar y desligar ─────────────────────────────────────────────────────────

    /// <summary>
    /// Liga la actividad con un work item —o la desliga, si no viene ninguno— y empuja de inmediato
    /// el esfuerzo y la prioridad, que es lo que pidió el equipo: que DevOps refleje lo que dice el
    /// pool desde el momento del vínculo y no desde la siguiente edición.
    /// </summary>
    public async Task<(bool ok, string mensaje)> LigarAsync(
        int poolActivityId, int? workItem, string? enlace, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuario);

        var (resuelto, error, numero) = ResolverWorkItem(workItem, enlace);
        if (!resuelto) return (false, error);

        var actividad = await db.PoolActivities.FirstOrDefaultAsync(a => a.Id == poolActivityId, ct);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");

        // ── Desligar ─────────────────────────────────────────────────────────────
        if (numero is null)
        {
            // Una dirección de la que no se pudo sacar ningún número NO es una orden de desligar.
            // Quien la pegó estaba LIGANDO —pulsó «Ligar»— y lo que trajo fue un ticket de Freshdesk,
            // el correo del cliente o una página de DevOps que no apunta a un work item. Tratarlo
            // como «no me pidieron nada» haría exactamente lo contrario de lo que se pidió: soltar el
            // vínculo que ya había. Desligar es destructivo y no puede ser el resultado por omisión
            // de no haber entendido lo que se escribió; solo se hace cuando no se manda NADA.
            if (!string.IsNullOrWhiteSpace(enlace))
                return (false, "Esa dirección no lleva dentro ningún work item de Azure DevOps. Se " +
                               "reconocen las dos que produce DevOps —«…/_workitems/edit/1234» y " +
                               "«…/_workitems?id=1234»—; si tienes otra, escribe el número a mano. " +
                               "Para quitarle el vínculo a esta actividad, usa «Desligar».");

            if (!actividad.LigadaADevOps)
                return (true, "Esa actividad no estaba ligada a ningún work item.");

            int anterior = actividad.DevOpsWorkItemId!.Value;
            OlvidarVinculo(actividad);
            await db.SaveChangesAsync(ct);

            await Anotar(poolActivityId, $"Desligada del work item #{anterior} de Azure DevOps", ct);
            return (true, $"Desligada del work item #{anterior}. A partir de ahora no se le manda nada; " +
                          $"lo que ya se escribió allá se queda como está.");
        }

        // ── Ligar ────────────────────────────────────────────────────────────────
        if (actividad.DevOpsWorkItemId != numero)
        {
            var (libre, ocupado) = await NadieMasLoTieneAsync(db, numero.Value, poolActivityId, ct);
            if (!libre) return (false, ocupado);

            actividad.DevOpsWorkItemId = numero;

            // La marca de agua se BORRA al cambiar de ticket: lo que se envió, se envió al work item
            // ANTERIOR. Conservarla haría que la actividad se creyera al día en un ticket al que
            // nunca se le mandó nada.
            actividad.DevOpsEsfuerzoEnviado  = null;
            actividad.DevOpsPrioridadEnviada = null;
            actividad.DevOpsUltimoError      = null;
        }

        // El enlace se rellena solo cuando está vacío. Pisar el que capturó el líder sería quitarle
        // una dirección que quizá apunta a otra cosa a propósito (el PR, el correo del cliente).
        if (string.IsNullOrWhiteSpace(actividad.ExternalUrl))
            actividad.ExternalUrl = await UrlDelWorkItemAsync(numero.Value, ct);

        await db.SaveChangesAsync(ct);
        await Anotar(poolActivityId, $"Ligada al work item #{numero} de Azure DevOps", ct);

        var sincronizado = await db.DevOpsTickets.AsNoTracking()
            .AnyAsync(t => t.ExternalId == numero, ct);

        // El empuje va DESPUÉS de guardar el vínculo, y su fallo no lo deshace: el vínculo es una
        // decisión del líder y sigue valiendo aunque DevOps esté caído en este segundo.
        var (_, aviso) = await EmpujarAsync(poolActivityId, ct);

        var nota = sincronizado
            ? ""
            : $" El ticket #{numero} no está sincronizado aquí todavía, así que no se puede enseñar su " +
              "título; el vínculo sirve igual, porque se escribe en DevOps por número.";

        return (true, $"Actividad ligada al work item #{numero}.{nota} {aviso}".Trim());
    }

    /// <summary>
    /// Que ninguna OTRA actividad viva esté ya ligada a ese work item.
    ///
    /// <para>Dos actividades vivas sobre el mismo ticket se pisarían el esfuerzo y la prioridad la
    /// una a la otra, y ninguna de las dos se enteraría: las dos se creerían al día, porque su marca
    /// de agua diría que mandaron lo suyo y era verdad. El desfase solo se vería en DevOps.</para>
    ///
    /// <para>Se comprueba aquí y no con un índice único porque la regla depende del ESTADO de la
    /// otra: un work item cuya actividad ya se aceptó o se retiró puede volver a necesitar otra —un
    /// bug que se reabre—, y un índice único lo prohibiría para siempre. Se asume la carrera: dos
    /// altas simultáneas sobre el mismo número podrían colarse. El daño es visible y reparable
    /// (desligar una), a diferencia de la prohibición permanente, que no lo es.</para>
    ///
    /// <para>Es <b>estática y recibe el contexto</b> para que la comparta el alta de una actividad
    /// —donde el vínculo llega en el mismo formulario— sin obligar a <c>PoolActivityService</c> a
    /// tener este servicio: allí la integración es opcional y esta comprobación no es opcional.</para>
    /// </summary>
    public static async Task<(bool ok, string error)> NadieMasLoTieneAsync(
        AppDbContext db, int numero, int poolActivityId, CancellationToken ct)
    {
        var otras = await db.PoolActivities.AsNoTracking()
            .Where(a => a.DevOpsWorkItemId == numero && a.Id != poolActivityId)
            .Select(a => new { a.Id, a.Title, a.Status })
            .ToListAsync(ct);

        var viva = otras.FirstOrDefault(o => SigueEnJuego(o.Status));

        return viva is null
            ? (true, "")
            : (false, $"El work item #{numero} ya está ligado a la actividad «{viva.Title}» (#{viva.Id}), " +
                      "que sigue en juego. Dos actividades sobre el mismo ticket se pisarían el esfuerzo y " +
                      "la prioridad la una a la otra sin que ninguna lo notara. Desliga aquélla primero.");
    }

    /// <summary>Deja la actividad como si nunca hubiera estado ligada. La marca de agua se va con el
    /// vínculo: sin ticket no hay nada contra qué comparar.</summary>
    private static void OlvidarVinculo(PoolActivity actividad)
    {
        actividad.DevOpsWorkItemId       = null;
        actividad.DevOpsEsfuerzoEnviado  = null;
        actividad.DevOpsPrioridadEnviada = null;
        actividad.DevOpsEmpujadoEnUtc    = null;
        actividad.DevOpsUltimoError      = null;
    }

    // ── El empuje ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Manda a DevOps lo que le falte de esta actividad —el esfuerzo, la prioridad o los dos— y, si
    /// la prioridad entra, refleja con ella el ticket local y reajusta su compromiso de SLA cuando de
    /// verdad cambió.
    ///
    /// <para><b>No lleva guarda de autorización</b>, y es a propósito: se invoca justo DETRÁS de una
    /// operación del pool que ya la pasó (publicar, editar, tomar, ligar). Ponerle otra aquí
    /// obligaría a que el desarrollador que toma un bug tuviera permiso sobre la integración para
    /// que su estimación llegara. La ruta que expone el reintento a la gente es
    /// <see cref="ReintentarAsync"/>, y ésa sí comprueba de quién es la actividad.</para>
    ///
    /// <para><b>Nunca lanza.</b> Quien llama ya guardó lo suyo; lo peor que puede devolver es un
    /// aviso que añadir al mensaje.</para>
    /// </summary>
    /// <returns>
    /// <c>todoLlego</c> dice si DevOps quedó con todo lo que decía el pool —también es cierto cuando
    /// no había nada que mandar—; <c>aviso</c> es el texto para la persona, vacío cuando no hay nada
    /// que contar.
    /// </returns>
    public async Task<(bool todoLlego, string aviso)> EmpujarAsync(
        int poolActivityId, CancellationToken ct = default)
    {
        try
        {
            return await IntentarEmpujeAsync(poolActivityId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Se fue quien esperaba la respuesta. Lo local ya está guardado y la actividad sigue
            // marcada como pendiente, así que no hay nada que decir ni a quién decírselo.
            return (false, "");
        }
        catch (Exception ex)
        {
            // Cualquier otra cosa —un defecto de aquí, la base que no responde— NO puede tumbar la
            // operación que ya terminó. Se cuenta, que es lo único que queda por hacer.
            var motivo = ex.GetBaseException().Message;

            // Y se DEJA ESCRITO en la actividad. Por esta rama sale lo que no previó ninguna de las
            // de dentro, así que sin esto el único rastro de un empuje reventado sería el mensaje que
            // alguien leyó una vez: la actividad seguiría saliendo como pendiente —eso lo da la marca
            // de agua— pero sin decir de qué murió, que es justo lo que hace falta para arreglarlo.
            await AnotarResultadoAsync(poolActivityId, null, null, motivo);

            return (false, $"No se pudo actualizar Azure DevOps: {motivo} " +
                           "La actividad quedó guardada aquí y pendiente de enviar.");
        }
    }

    private async Task<(bool todoLlego, string aviso)> IntentarEmpujeAsync(
        int poolActivityId, CancellationToken ct)
    {
        var actividad = await db.PoolActivities.AsNoTracking()
            .Where(a => a.Id == poolActivityId)
            .Select(a => new
            {
                a.DevOpsWorkItemId, a.HorasEstimadas, a.Priority,
                a.DevOpsEsfuerzoEnviado, a.DevOpsPrioridadEnviada
            })
            .FirstOrDefaultAsync(ct);

        if (actividad?.DevOpsWorkItemId is not int numero || numero <= 0) return (true, "");

        int prioridadDestino = PrioridadDelPoolEnDevOps.ADevOps(actividad.Priority);
        bool faltaEsfuerzo   = actividad.HorasEstimadas is decimal h && actividad.DevOpsEsfuerzoEnviado != h;
        bool faltaPrioridad  = actividad.DevOpsPrioridadEnviada != prioridadDestino;

        if (!faltaEsfuerzo && !faltaPrioridad) return (true, "");

        var (credenciales, problema) = await CredencialesAsync(exigirPropio: false, ct);
        if (credenciales is null)
        {
            await AnotarResultadoAsync(poolActivityId, null, null, problema);
            return (false, $"El ticket #{numero} de Azure DevOps NO se actualizó: {problema} " +
                           "La actividad quedó guardada aquí y pendiente de enviar.");
        }

        // El reloj propio es lo que impide que guardar una actividad se cuelgue detrás de un
        // servidor que no contesta. Enlazado con el del llamador para que cancelar la petición
        // cancele también esto.
        using var reloj = CancellationTokenSource.CreateLinkedTokenSource(ct);
        reloj.CancelAfter(Paciencia);
        var espera = reloj.Token;

        decimal? esfuerzoLlego = null;
        int? prioridadLlego = null;
        var problemas = new List<string>();
        bool devopsMudo = false;

        if (faltaEsfuerzo)
        {
            try
            {
                // Va al campo Effort (Microsoft.VSTS.Scheduling.Effort), que es lo que escribe
                // EscribirEstimacionAsync y EXACTAMENTE el mismo campo que ya usa la pantalla de
                // tickets al estimar. Que sean el mismo importa más que cuál sea: con dos campos
                // distintos, la misma persona estimando desde dos pantallas dejaría dos números en
                // el work item y ninguno sería el bueno.
                var (escrito, aviso) = await devops.EscribirEstimacionAsync(
                    credenciales, numero, (double)actividad.HorasEstimadas!.Value, espera);

                if (escrito) esfuerzoLlego = actividad.HorasEstimadas;
                else problemas.Add(string.IsNullOrWhiteSpace(aviso)
                    ? "Azure DevOps no aceptó el campo Effort en este tipo de work item."
                    : aviso);
            }
            catch (ErrorDeAzureDevOps ex) { problemas.Add(ex.Message); devopsMudo = true; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                problemas.Add($"Azure DevOps no contestó en {Paciencia.TotalSeconds:0} segundos.");
                devopsMudo = true;
            }
        }

        if (faltaPrioridad)
        {
            // Con DevOps mudo no se vuelve a intentar: sería esperar otros quince segundos para
            // recibir el mismo silencio, y quien está guardando lo paga entero. Un rechazo del campo
            // Effort (que llega rápido y solo habla de ESE campo) no cuenta como silencio: la
            // prioridad es otro campo y merece su intento.
            if (devopsMudo)
            {
                problemas.Add("La prioridad no se llegó a intentar, porque Azure DevOps ya no había " +
                              "contestado al escribir el esfuerzo.");
            }
            else
            {
                try
                {
                    await devops.CambiarPrioridadAsync(credenciales, numero, prioridadDestino, espera);
                    prioridadLlego = prioridadDestino;
                }
                catch (ErrorDeAzureDevOps ex) { problemas.Add(ex.Message); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    problemas.Add($"Azure DevOps no contestó en {Paciencia.TotalSeconds:0} segundos " +
                                  "al cambiar la prioridad.");
                }

                // La prioridad entró: se refleja aquí con el mismo código que la pantalla de tickets.
                //
                // SOLO si entró, y por eso va después y no dentro del try: reajustar el plazo por una
                // prioridad que el work item nunca llegó a tener pondría a correr un compromiso que
                // allá no se sostiene, y aquellos «catch» hablan de fallos de DevOps —meter en ellos
                // un fallo de la base de aquí sería mentir en el motivo que se guarda—. Si esto
                // revienta sale por el manejador de EmpujarAsync, que deja escrito el porqué y
                // conserva la actividad pendiente para reintentarlo entero.
                if (prioridadLlego is int aceptada)
                    await ReconciliacionDePrioridadDeDevOps.ReconciliarAsync(
                        db, configuracion, usuario.UserId, numero, aceptada, ct);
            }
        }

        string? error = problemas.Count == 0 ? null : string.Join(" ", problemas);
        await AnotarResultadoAsync(poolActivityId, esfuerzoLlego, prioridadLlego, error);

        bool todoLlego = (!faltaEsfuerzo || esfuerzoLlego != null)
                      && (!faltaPrioridad || prioridadLlego != null);

        await Anotar(poolActivityId, todoLlego
            ? $"Empujado a DevOps en el work item #{numero}: {Resumen(esfuerzoLlego, prioridadLlego)}"
            : $"Empuje a DevOps INCOMPLETO en el work item #{numero}: {error}", ct);

        if (todoLlego)
            return (true, $"Azure DevOps actualizado en el ticket #{numero} ({Resumen(esfuerzoLlego, prioridadLlego)}).");

        // El empuje PARCIAL se nombra como tal. Decir solo «falló» cuando la estimación sí entró
        // haría que quien lo lea suponga que allá no hay nada, y volvería a mandarlo todo a mano.
        var loQueSiLlego = Resumen(esfuerzoLlego, prioridadLlego);
        var cabecera = loQueSiLlego.Length == 0
            ? $"La actividad quedó guardada aquí, pero el ticket #{numero} de Azure DevOps NO se actualizó:"
            : $"El ticket #{numero} de Azure DevOps quedó A MEDIAS —sí entró {loQueSiLlego}—:";

        return (false, $"{cabecera} {error} Queda pendiente de enviar: puedes reintentarlo desde la " +
                       "actividad, sin volver a capturar nada.");
    }

    /// <summary>Lo que sí llegó, dicho en corto. Cadena vacía si no llegó nada.</summary>
    private static string Resumen(decimal? esfuerzo, int? prioridad)
    {
        var partes = new List<string>(2);
        if (esfuerzo is decimal h) partes.Add($"esfuerzo {h:0.##} h");
        if (prioridad is int p) partes.Add($"prioridad {p}");
        return string.Join(" y ", partes);
    }

    /// <summary>
    /// Guarda la marca de agua y el motivo del fallo.
    ///
    /// <para>Con una actualización DIRECTA y fuera del seguimiento de entidades, por lo mismo que
    /// <c>DevOpsService.MarcarReportadoAsync</c>: <c>PoolActivity</c> lleva sello de concurrencia, y
    /// esto no es una edición de negocio que deba perder una carrera contra el líder que está
    /// revisando la misma actividad en otra pestaña. Además así no arrastra al guardado nada que
    /// quedara pendiente en el contexto de quien llamó.</para>
    ///
    /// <para>Sin token de cancelación a propósito: esto ES la constancia. Si se cancela la petición
    /// justo aquí, lo que se perdería es precisamente el rastro de que el empuje falló.</para>
    /// </summary>
    private async Task AnotarResultadoAsync(int poolActivityId, decimal? esfuerzo, int? prioridad, string? error)
    {
        var ahora = DateTime.UtcNow;
        var recorte = error is null ? null : error.Length <= 1000 ? error : error[..1000];

        try
        {
            await db.PoolActivities
                .Where(a => a.Id == poolActivityId)
                .ExecuteUpdateAsync(s => s
                    // COALESCE y no asignación directa: lo que no llegó tiene que conservar la marca
                    // que ya tenía. Escribir null borraría la prueba de un envío anterior que sí
                    // entró, y la actividad volvería a mandar algo que allá ya está.
                    .SetProperty(a => a.DevOpsEsfuerzoEnviado, a => esfuerzo ?? a.DevOpsEsfuerzoEnviado)
                    .SetProperty(a => a.DevOpsPrioridadEnviada, a => prioridad ?? a.DevOpsPrioridadEnviada)
                    .SetProperty(a => a.DevOpsEmpujadoEnUtc, ahora)
                    .SetProperty(a => a.DevOpsUltimoError, recorte), CancellationToken.None);
        }
        catch { /* si ni esto se puede guardar, el aviso del mensaje es lo que queda */ }
    }

    /// <summary>
    /// El reintento que pide una persona. A diferencia de <see cref="EmpujarAsync"/>, éste comprueba
    /// quién lo pide: el líder sobre cualquiera, y quien tenga la actividad tomada sobre la suya
    /// —que es quien vio el aviso cuando su estimación no llegó—.
    /// </summary>
    public async Task<(bool ok, string mensaje)> ReintentarAsync(
        int poolActivityId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(usuario, Ambito);

        var actividad = await db.PoolActivities.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == poolActivityId, ct);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");

        if (!usuario.IsAdmin && actividad.ClaimedByDeveloperId != usuario.DeveloperId)
            return (false, "Esa actividad no es tuya. Pídeselo al líder.");

        if (!actividad.LigadaADevOps)
            return (false, "Esa actividad no está ligada a ningún work item de Azure DevOps, así que no " +
                           "hay nada que mandar.");

        if (!actividad.PendienteDeEnviarADevOps)
            return (true, $"No hay nada pendiente: el ticket #{actividad.DevOpsWorkItemId} ya tiene lo " +
                          "que dice el pool.");

        var (todoLlego, aviso) = await EmpujarAsync(poolActivityId, ct);
        return (todoLlego, aviso.Length == 0 ? "No había nada que mandar." : aviso);
    }

    // ── Qué quedó sin llegar ─────────────────────────────────────────────────────

    /// <summary>
    /// Las actividades ligadas cuyo esfuerzo o prioridad no están en DevOps.
    ///
    /// <para>Es la otra mitad de «no puede fallar en silencio»: el aviso del momento lo vio una
    /// persona y lo cerró. Sin esta lista, un empuje perdido no sería consultable por nadie y el
    /// desfase entre el pool y DevOps solo se descubriría en una reunión.</para>
    ///
    /// <para>Se resuelve en DOS consultas y no con un <c>Join</c>: el vínculo es por número, no por
    /// clave ajena, y hay actividades ligadas a tickets que no están sincronizados aquí. Un join
    /// dejaría fuera precisamente las que más importa ver.</para>
    /// </summary>
    public async Task<PendientesDeDevOpsDto> PendientesAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuario);

        // El filtro fino se hace en memoria: «pendiente» compara decimales entre sí y, en SQLite, EF
        // los guarda como TEXTO —comparar allí sería comparar cadenas, con «9.0» mayor que «40.0»—.
        // La consulta acota por lo que sí se puede traducir: que esté ligada.
        var ligadas = await db.PoolActivities.AsNoTracking()
            .Where(a => a.DevOpsWorkItemId != null)
            .OrderByDescending(a => a.DevOpsEmpujadoEnUtc)
            .ToListAsync(ct);

        var pendientes = ligadas.Where(a => a.PendienteDeEnviarADevOps).ToList();
        if (pendientes.Count == 0) return new PendientesDeDevOpsDto([]);

        var numeros = pendientes.Select(a => a.DevOpsWorkItemId!.Value).Distinct().ToList();
        var tickets = await db.DevOpsTickets.AsNoTracking()
            .Where(t => numeros.Contains(t.ExternalId))
            .Select(t => new { t.ExternalId, t.Title, t.State })
            .ToListAsync(ct);

        var porNumero = tickets.ToDictionary(t => t.ExternalId);

        return new PendientesDeDevOpsDto(pendientes.Select(a =>
        {
            porNumero.TryGetValue(a.DevOpsWorkItemId!.Value, out var ticket);
            return AVista(a, ticket?.Title, ticket?.State);
        }).ToList());
    }

    /// <summary>Cómo está el vínculo de UNA actividad. Requiere sesión y nada más: no dice nada que
    /// quien trabaja la actividad no deba ver.</summary>
    public async Task<(bool ok, string mensaje, VinculoDevOpsDto? vinculo)> VinculoAsync(
        int poolActivityId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(usuario);

        var actividad = await db.PoolActivities.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == poolActivityId, ct);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.", null);

        string? titulo = null, estado = null;
        if (actividad.DevOpsWorkItemId is int numero)
        {
            var ticket = await db.DevOpsTickets.AsNoTracking()
                .Where(t => t.ExternalId == numero)
                .Select(t => new { t.Title, t.State })
                .FirstOrDefaultAsync(ct);
            titulo = ticket?.Title;
            estado = ticket?.State;
        }

        return (true, "", AVista(actividad, titulo, estado));
    }

    private static VinculoDevOpsDto AVista(PoolActivity a, string? tituloDelTicket, string? estadoDelTicket) =>
        new(a.Id,
            a.Title,
            a.DevOpsWorkItemId,
            a.ExternalUrl,
            tituloDelTicket != null,
            tituloDelTicket,
            estadoDelTicket,
            a.HorasEstimadas,
            a.DevOpsEsfuerzoEnviado,
            a.Priority,
            PrioridadDelPoolEnDevOps.ADevOps(a.Priority),
            a.DevOpsPrioridadEnviada,
            a.EsfuerzoPendienteDeEnviar,
            a.PrioridadPendienteDeEnviar,
            a.DevOpsEmpujadoEnUtc,
            a.DevOpsUltimoError);

    // ── Comentarios ──────────────────────────────────────────────────────────────

    /// <summary>
    /// El hilo del work item ligado, en texto plano y del más antiguo al más reciente.
    ///
    /// <para><b>Leerlo exige la MISMA pertenencia que escribir en él</b> —el líder sobre cualquiera,
    /// y quien la tenga tomada sobre la suya— y no es simetría gratuita: la lectura sale a DevOps con
    /// el token de la INSTALACIÓN cuando quien pide no tiene el suyo, así que sin esta comprobación
    /// cualquiera con sesión de desarrollador leería la conversación de cualquier work item ligado
    /// —incluida la de tickets a los que su propia cuenta de DevOps no llega— usando el token
    /// compartido como puerta trasera. La pantalla de tickets ya exige que el ticket esté a su nombre
    /// para enseñar sus comentarios; aquí faltaba lo mismo.</para>
    ///
    /// <para>No recorta nada de lo que las pantallas enseñan: la tarjeta del vínculo vive dentro de
    /// «mi actividad» en la pantalla del desarrollador y dentro de la del líder, que puede sobre
    /// todas.</para>
    ///
    /// <para>Con el token de la instalación se LEE cuando no hay uno propio, porque leer no deja
    /// rastro en el historial de nadie y no hay nada que firmar. La respuesta dice aparte si quien
    /// mira puede además escribir, para que la pantalla no ofrezca un cuadro de texto que va a
    /// rechazarse al pulsar «Enviar».</para>
    /// </summary>
    public async Task<(bool ok, string mensaje, HiloDeDevOpsDto? hilo)> HiloAsync(
        int poolActivityId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(usuario, Ambito);

        var (actividad, motivo) = await ActividadLigadaAsync(poolActivityId, ct);
        if (actividad is null) return (false, motivo, null);
        if (!EsSuya(actividad)) return (false, NoEsTuya, null);

        var (credenciales, problema) = await CredencialesAsync(exigirPropio: false, ct);
        if (credenciales is null) return (false, problema, null);

        // Lo que decide si además se puede ESCRIBIR se calcula AQUÍ y viaja resuelto: es una consulta
        // a la tabla de secretos, y hacerla desde la pantalla obligaría a un segundo viaje para
        // saber si el cuadro de texto sirve de algo. Llegados aquí, lo único que puede faltar es el
        // token propio: la pertenencia ya se comprobó arriba.
        var (propias, faltaElPropio) = await CredencialesAsync(exigirPropio: true, ct);

        try
        {
            var comentarios = await devops.ObtenerComentariosAsync(
                credenciales, actividad.DevOpsWorkItemId!.Value, ct);

            return (true, "", new HiloDeDevOpsDto(
                poolActivityId,
                actividad.DevOpsWorkItemId!.Value,
                actividad.Title,
                comentarios
                    .OrderBy(c => c.CreadoUtc)
                    .Select(c => new ComentarioDevOpsDto(ATextoPlano(c.Texto), c.Autor, c.CreadoUtc))
                    .ToList(),
                propias is not null,
                propias is null ? faltaElPropio : null));
        }
        catch (ErrorDeAzureDevOps ex)
        {
            return (false, ex.Message, null);
        }
    }

    /// <summary>
    /// Publica un comentario en el work item ligado, FIRMADO con el token de quien lo escribe.
    ///
    /// <para><b>Aquí no hay caída al token de la instalación</b>, y es la decisión que da sentido a
    /// todo esto: un comentario es una afirmación de una persona, y con una cuenta compartida el
    /// historial del ticket dejaría de decir quién dijo qué —que es justo lo que hay que poder
    /// probar—. Escribir a nombre de otro es peor que no escribir.</para>
    ///
    /// <para><b>Hoy no lo tiene capturado nadie</b>: la tabla de secretos está vacía, así que «no
    /// tengo token» no es el caso raro, es el de todo el mundo el primer día. Por eso el mensaje
    /// explica para qué hace falta y a dónde ir, en vez de contestar un «no autorizado» que nadie
    /// sabría cómo resolver.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> ComentarAsync(
        int poolActivityId, string? texto, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(usuario, Ambito);

        texto = (texto ?? "").Trim();
        if (texto.Length == 0) return (false, "Escribe el comentario antes de enviarlo.");
        if (texto.Length > MaxComentario)
            return (false, $"El comentario no puede pasar de {MaxComentario} caracteres.");

        var (actividad, motivo) = await ActividadLigadaAsync(poolActivityId, ct);
        if (actividad is null) return (false, motivo);
        if (!EsSuya(actividad)) return (false, NoEsTuya);

        var (credenciales, problema) = await CredencialesAsync(exigirPropio: true, ct);
        if (credenciales is null) return (false, problema);

        int numero = actividad.DevOpsWorkItemId!.Value;

        // El texto lo escribe una persona y acaba dentro de un documento HTML que leen otras: se
        // escapa entero antes de armar nada, igual que en la pantalla de tickets. La cabecera dice
        // de dónde sale el comentario, porque quien lo lee en DevOps no tiene por qué saber que
        // existe un pool de actividades.
        var html = $"<b>Actividad del pool #{poolActivityId}: " +
                   $"{WebUtility.HtmlEncode(actividad.Title)}</b><br>" +
                   WebUtility.HtmlEncode(texto).Replace("\n", "<br>");

        try
        {
            await devops.PublicarComentarioAsync(credenciales, numero, html, ct);
        }
        catch (ErrorDeAzureDevOps ex)
        {
            return (false, ex.Message);
        }

        // El contador local se sube a mano, igual que al comentar desde la pantalla de tickets, y
        // por un motivo concreto además de refrescar la rejilla: quien VIGILA ese ticket recibe un
        // aviso cuando la siguiente sincronización encuentra más comentarios que los que constaban.
        // Sin subirlo, nuestro propio comentario le llegaría como «cambió algo que vigilas».
        var ticket = await db.DevOpsTickets.FirstOrDefaultAsync(t => t.ExternalId == numero, ct);
        if (ticket != null)
        {
            ticket.CommentCount++;
            await db.SaveChangesAsync(ct);
        }

        await Anotar(poolActivityId, $"Comentario publicado en DevOps en el work item #{numero}", ct);

        return (true, $"Comentario publicado en el ticket #{numero} de Azure DevOps, a tu nombre.");
    }

    /// <summary>
    /// La actividad la gobierna quien pregunta: el líder sobre cualquiera y quien la tenga tomada
    /// sobre la suya.
    ///
    /// <para>Es lo que abre las DOS puertas del ticket —leer su hilo y escribir en él—, no solo la de
    /// escribir. Asomarse a la conversación de un work item ajeno desde una actividad que no se
    /// trabaja no es de nadie, y aquí importa el doble porque esa lectura puede ir con el token de la
    /// instalación: sin la comprobación, el token compartido sería una puerta trasera a tickets que
    /// la cuenta de quien mira no alcanza.</para>
    /// </summary>
    private bool EsSuya(PoolActivity actividad) =>
        usuario.IsAdmin || (usuario.DeveloperId is int dev && actividad.ClaimedByDeveloperId == dev);

    private const string NoEsTuya =
        "Esa actividad no la tienes tomada, así que no puedes ver su ticket de Azure DevOps ni " +
        "comentar en él a tu nombre. Pídeselo al líder.";

    // ── Interno ──────────────────────────────────────────────────────────────────

    private async Task<(PoolActivity? actividad, string motivo)> ActividadLigadaAsync(
        int poolActivityId, CancellationToken ct)
    {
        var actividad = await db.PoolActivities.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == poolActivityId, ct);

        if (actividad == null) return (null, "Esa actividad ya no existe. Actualiza la lista.");

        return actividad.LigadaADevOps
            ? (actividad, "")
            : (null, "Esa actividad no está ligada a ningún work item de Azure DevOps. Lígala primero " +
                     "y entonces se podrá comentar en su ticket desde aquí.");
    }

    /// <summary>
    /// Con qué se habla con DevOps.
    ///
    /// <para>Es la misma resolución que hace <c>DevOpsService</c> y no se comparte con él a
    /// propósito: allí es un método privado, y exponerlo obligaría a que este servicio dependiera de
    /// aquél entero —con su sincronización, su importación y sus avisos— para leer dos ajustes y un
    /// secreto. Lo que sí se comparte es lo que importa: la misma clave de configuración y el mismo
    /// propósito de secreto, así que un token capturado sirve para las dos cosas.</para>
    /// </summary>
    private async Task<(CredencialesDevOps? credenciales, string problema)> CredencialesAsync(
        bool exigirPropio, CancellationToken ct)
    {
        var organizacion = (await configuracion.ObtenerAsync(SettingsService.Claves.AzureDevOpsOrgUrl, ct))?.TrimEnd('/');
        var proyecto = await configuracion.ObtenerAsync(SettingsService.Claves.AzureDevOpsProject, ct);

        if (string.IsNullOrEmpty(organizacion) || string.IsNullOrEmpty(proyecto))
            return (null, "Falta la URL de organización o el proyecto de Azure DevOps. Pídeselo al líder.");

        var pat = await secretos.ObtenerMioEnClaroAsync(PropositosDeSecreto.PatDevOps, ct);

        if (string.IsNullOrEmpty(pat) && !exigirPropio)
            pat = await configuracion.ObtenerAsync(SettingsService.Claves.AzureDevOpsPat, ct);

        if (string.IsNullOrEmpty(pat))
            return (null, exigirPropio
                ? "Todavía no has capturado TU token de Azure DevOps, y un comentario tiene que ir " +
                  "firmado por quien lo escribe: con el token de la instalación aparecería a nombre de " +
                  "una cuenta compartida y el historial del ticket dejaría de decir quién dijo qué. " +
                  "Captúralo en «Mi token de DevOps», el botón de la pantalla «Mis tickets»; se guarda " +
                  "cifrado y no vuelve a mostrarse."
                : "No hay ningún token de Azure DevOps configurado, ni tuyo ni de la instalación. " +
                  "Captura el tuyo en «Mi token de DevOps», en la pantalla «Mis tickets».");

        return (new CredencialesDevOps(organizacion, proyecto, pat), "");
    }

    /// <summary>
    /// La dirección del work item, armada igual que la que guarda la sincronización. Se compone en
    /// vez de pedírsela a DevOps porque es determinista y pedirla costaría un viaje de red dentro de
    /// una operación que ya tiene el suyo.
    /// </summary>
    private async Task<string?> UrlDelWorkItemAsync(int numero, CancellationToken ct)
    {
        var organizacion = (await configuracion.ObtenerAsync(SettingsService.Claves.AzureDevOpsOrgUrl, ct))?.TrimEnd('/');
        var proyecto = await configuracion.ObtenerAsync(SettingsService.Claves.AzureDevOpsProject, ct);

        return string.IsNullOrEmpty(organizacion) || string.IsNullOrEmpty(proyecto)
            ? null
            : $"{organizacion}/{Uri.EscapeDataString(proyecto)}/_workitems/edit/{numero}";
    }

    private async Task Anotar(int poolActivityId, string detalle, CancellationToken ct)
    {
        try
        {
            await bitacora.RecordAsync(AuditAction.Update, "PoolActivity",
                poolActivityId.ToString(), detalle, ct);
        }
        catch { /* la bitácora es constancia, no una condición para que la operación valga */ }
    }

    /// <summary>
    /// Quita el marcado de un comentario de DevOps. Mismo criterio que en <c>DevOpsService</c>: los
    /// comentarios los escribe gente de FUERA del equipo, vienen en HTML y se limpian en el servidor
    /// para que del lado del navegador no exista siquiera un texto con marcado que alguien pueda
    /// acabar pintando. La decodificación va DESPUÉS de quitar las etiquetas: al revés, un
    /// «&amp;lt;script&amp;gt;» se convertiría en una etiqueta de verdad.
    /// </summary>
    private static string ATextoPlano(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        var sinEtiquetas = EtiquetasHtml().Replace(html, " ");
        var texto = WebUtility.HtmlDecode(sinEtiquetas);
        return string.Join(' ', texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex("<[^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex EtiquetasHtml();
}
