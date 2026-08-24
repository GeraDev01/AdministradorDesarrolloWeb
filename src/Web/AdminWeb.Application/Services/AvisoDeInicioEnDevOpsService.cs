using System.Net;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// AVISA EN AZURE DEVOPS DE QUE ALGUIEN EMPEZÓ A TRABAJAR, con la hora.
///
/// <para><b>Un aviso por SESIÓN de cronómetro.</b> La marca de agua vive en
/// <c>WorkSession.InicioComentadoEnUtc</c>, y de ahí sale toda la regla sin necesidad de que nadie
/// más la conozca: detener y volver a empezar crea una sesión nueva, que merece su aviso; reanudar
/// una pausada —tras comer, tras una reunión, tras la auto-pausa de arrancar otra cosa— sigue siendo
/// la misma sesión y no comenta nada.</para>
///
/// <para><b>Sirve a dos amos y no se entera de cuál lo llama.</b> Lo invoca el endpoint cuando el
/// cronómetro arranca desde la web —para que el aviso sea inmediato— y lo invoca el barrido para
/// recoger lo que arrancó en la aplicación de escritorio, que sigue en producción y no pasa por
/// nuestros endpoints. Que los dos puedan cruzarse sobre la misma sesión no es un problema: la marca
/// se RESERVA con una actualización condicional, así que gana quien llegue primero y el otro se
/// encuentra el trabajo hecho.</para>
///
/// <para><b>Sin <c>ICurrentUser</c>, y es deliberado</b>: la mitad de sus llamadas vienen de un
/// ámbito de fondo donde no hay sesión, y cualquier consulta con guarda lanzaría allí.</para>
///
/// <para><b>Pero las credenciales las RECIBE, no las resuelve</b>, y esa inversión es la que
/// arregla algo que se veía en el ticket: al arrancar el cronómetro el comentario salía firmado por
/// la cuenta de la instalación —la compartida— y al detenerlo, por el token personal de quien
/// trabaja, porque el reporte de tiempo sí va por ahí. Dos cuentas para las dos puntas del mismo
/// cronómetro. Ahora el endpoint, que sí tiene sesión, le pasa las de quien pulsó «Iniciar»; el
/// barrido le pasa nulo y entonces —y solo entonces— caen las de la instalación, que es el único
/// caso en que no hay alternativa: un cronómetro arrancado en el escritorio no tiene sesión web de
/// la que sacar un token.</para>
///
/// <para>El nombre de quien empezó va DENTRO del texto de todas formas: el aviso lo lee el cliente
/// en su ticket, y ahí «Ana empezó a trabajar» se entiende sin tener que mirar quién firma.</para>
/// </summary>
public class AvisoDeInicioEnDevOpsService(
    AppDbContext db,
    SettingsService configuracion,
    AuditService bitacora,
    IClienteAzureDevOps devops)
{
    /// <summary>
    /// Lo que se espera a que Azure DevOps conteste. Corto a propósito: al otro lado del endpoint
    /// hay alguien mirando cómo su botón «Iniciar» no responde, y el cronómetro ya arrancó — lo que
    /// falte se cuenta, no se espera.
    /// </summary>
    public static readonly TimeSpan Paciencia = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Cuánto tiene que callar un mismo objetivo entre un aviso y el siguiente.
    ///
    /// <para><b>Existe porque «una vez por sesión» no basta.</b> Quien para a comer, para a la
    /// reunión y para a por un café crea cuatro sesiones en una tarde, y sin este freno el ticket del
    /// cliente recibiría cuatro «empezó a trabajar en esto» del mismo día y de la misma persona. El
    /// aviso vale la primera vez; a partir de ahí es ruido en un sitio que no es nuestro.</para>
    ///
    /// <para>Es una constante y no un ajuste porque el número correcto no depende de la instalación
    /// sino de la forma de la jornada: cuatro horas separan «volví después de comer» de «esto es otro
    /// día de trabajo».</para>
    /// </summary>
    public static readonly TimeSpan Silencio = TimeSpan.FromHours(4);

    /// <summary>Con la función apagada no se toca nada. Es opt-in porque escribe en tickets que
    /// también leen los clientes.</summary>
    public Task<bool> EstaEncendidoAsync(CancellationToken ct = default) =>
        configuracion.ObtenerBooleanoAsync(SettingsService.Claves.CronometroAvisoDeInicio, ct);

    /// <summary>
    /// Publica el aviso de esta sesión, si toca.
    ///
    /// <para>Es idempotente y la marca es quien decide: quien llama no tiene que saber si esto fue un
    /// arranque o una reanudación, ni si alguien se le adelantó. Devuelve el texto que se le puede
    /// enseñar a la persona, o cadena vacía cuando no había nada que contar —que es el caso normal y
    /// no merece ruido.</para>
    /// </summary>
    /// <param name="credenciales">Con qué cuenta se firma el comentario. Nulo = las de la
    /// instalación, que es lo que pasa el barrido de fondo porque allí no hay sesión de la que sacar
    /// un token personal.</param>
    public async Task<string> AvisarInicioAsync(int workSessionId,
        CredencialesDevOps? credenciales = null, CancellationToken ct = default) =>
        (await PublicarAsync(workSessionId, credenciales, ct)).texto;

    /// <summary>
    /// Lo mismo, diciendo además SI se publicó. Lo usa el barrido, que tiene que contar aciertos y
    /// no intentos: el texto de vuelta no sirve para contar porque un fallo también devuelve texto.
    /// </summary>
    private async Task<(bool publicado, string texto)> PublicarAsync(
        int workSessionId, CredencialesDevOps? credencialesDeQuienArranco = null,
        CancellationToken ct = default)
    {
        if (!await EstaEncendidoAsync(ct)) return (false, "");
        if (!await configuracion.ObtenerBooleanoAsync(SettingsService.Claves.AzureDevOpsEnabled, ct))
            return (false, "");

        var sesion = await db.WorkSessions.AsNoTracking()
            .Where(w => w.Id == workSessionId)
            .Select(w => new { w.DeveloperId, w.StartedAt, w.InicioComentadoEnUtc, w.RequirementId, w.ActivityId })
            .FirstOrDefaultAsync(ct);

        if (sesion is null || sesion.InicioComentadoEnUtc is not null) return (false, "");

        // Demasiado viejo para anunciarlo. Vale para los DOS caminos, y hace falta en el inmediato
        // tanto como en el barrido: una sesión que se quedó sin marca —porque el silencio la calló—
        // sigue abierta, y al REANUDARLA por la tarde este mismo método publicaría «empezó a
        // trabajar a las 10:00» a las cuatro, que es falso y además inútil.
        if (sesion.StartedAt < DateTime.UtcNow - Ventana) return (false, "");

        int? numero = await WorkItemDeLaSesion.ResolverAsync(db, workSessionId, ct);
        if (numero is not int workItem) return (false, "");

        if (await CalladoTodaviaAsync(sesion.DeveloperId, sesion.RequirementId, sesion.ActivityId, ct))
            return (false, "");

        // ── La reserva ──────────────────────────────────────────────────────────────
        //
        // Se marca ANTES de publicar y con una actualización condicional, no después. Es lo que hace
        // imposible el comentario doble cuando el endpoint y el barrido caen sobre la misma sesión:
        // el UPDATE solo cambia la fila si sigue en nulo, así que quien devuelva cero filas sabe que
        // el otro va a publicar y se retira sin hacer nada.
        var ahora = DateTime.UtcNow;
        int reservadas = await db.WorkSessions
            .Where(w => w.Id == workSessionId && w.InicioComentadoEnUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.InicioComentadoEnUtc, ahora), ct);

        if (reservadas == 0) return (false, "");

        // Desde aquí TODO va dentro del try. La reserva ya está escrita en la base, así que cualquier
        // salida que no pase por el manejo de abajo dejaría la sesión marcada como avisada sin
        // haberlo hecho —y eso no se recupera nunca—. Antes, leer las credenciales quedaba fuera: si
        // quien pulsa «Iniciar» recargaba la página en ese instante, la cancelación de la petición
        // salía del método con la reserva puesta y el aviso perdido.
        try
        {
            // Las de quien arrancó si las hay; las de la instalación si no. El respaldo NO se quita:
            // es el único camino del barrido, y también el de quien no ha capturado su token todavía
            // — a ése es mejor avisarle firmado por la cuenta compartida que no avisarle.
            var credenciales = credencialesDeQuienArranco
                ?? (await CredencialesDeLaInstalacion.ObtenerAsync(configuracion, ct)).credenciales;
            if (credenciales is null)
            {
                await SoltarLaReservaAsync(workSessionId);
                return (false, "");
            }

            var zona = await configuracion.ObtenerZonaHorariaAsync(ct);
            var quien = await db.Developers.AsNoTracking()
                .Where(d => d.Id == sesion.DeveloperId)
                .Select(d => d.FullName)
                .FirstOrDefaultAsync(ct) ?? "Alguien del equipo";

            using var reloj = CancellationTokenSource.CreateLinkedTokenSource(ct);
            reloj.CancelAfter(Paciencia);

            await devops.PublicarComentarioAsync(
                credenciales, workItem, Texto(quien, sesion.StartedAt, zona), reloj.Token);

            await bitacora.RecordAsync(AuditAction.Update, "WorkSession", workSessionId.ToString(),
                $"Aviso de inicio publicado en el work item #{workItem}", ct);

            return (true, $" Se avisó en el work item #{workItem}.");
        }
        catch (ErrorDeAzureDevOps ex)
        {
            // DEVOPS CONTESTÓ, Y DIJO QUE NO. Esto es lo único que garantiza que allá no se escribió
            // nada, y por eso es el único caso en que la reserva se suelta para reintentar.
            await SoltarLaReservaAsync(workSessionId);
            await AnotarFalloAsync(workSessionId, workItem, ex.Message);
            return (false, $" No se pudo avisar en el work item #{workItem}.");
        }
        catch (Exception ex)
        {
            // NO SE SABE QUÉ PASÓ: se agotó la paciencia, se cortó la red, se canceló la petición. El
            // comentario puede estar publicado perfectamente —DevOps lo creó y tardó en contestar—, y
            // soltar la marca aquí hacía que el barrido lo publicara OTRA VEZ dos minutos después.
            //
            // Así que la marca SE QUEDA. Se elige perder un aviso antes que duplicar un comentario en
            // el ticket de un cliente, porque lo primero no se nota y lo segundo no se puede retirar.
            // Queda en la bitácora que no se sabe cómo acabó.
            await AnotarFalloAsync(workSessionId, workItem,
                $"no se sabe si llegó (no hubo respuesta): {ex.Message}");
            return (false, $" No se sabe si el aviso llegó al work item #{workItem}.");
        }
    }

    private Task AnotarFalloAsync(int workSessionId, int workItem, string motivo) =>
        bitacora.RecordAsync(AuditAction.Update, "WorkSession", workSessionId.ToString(),
            $"Aviso de inicio en el work item #{workItem}: {Recortar(motivo)}");

    /// <summary>
    /// Cuánto hacia atrás mira el barrido.
    ///
    /// <para>Corta a propósito, y hace DOS trabajos. El evidente: un «empezó a las 09:12» publicado
    /// a las siete de la tarde no le sirve a nadie. El que importa: es lo que impide que una sesión
    /// cuyo work item se borró en DevOps se reintente para siempre —falla, suelta la marca, vuelve a
    /// entrar—. Pasada la ventana deja de intentarse sola, sin necesidad de llevar la cuenta de los
    /// intentos de cada sesión.</para>
    /// </summary>
    public static readonly TimeSpan Ventana = TimeSpan.FromHours(2);

    /// <summary>
    /// Lo que se le da de margen a un arranque antes de recogerlo, para no pisarle el turno al aviso
    /// inmediato que ya va camino de DevOps.
    /// </summary>
    public static readonly TimeSpan Gracia = TimeSpan.FromMinutes(1);

    /// <summary>Cuántas se atienden por pasada. Con una pasada cada dos minutos son de sobra: lo que
    /// no entre esta vez entra en la siguiente, y así una racha rara no se convierte en cien
    /// peticiones seguidas contra Azure DevOps con el token compartido.</summary>
    public const int MaximoPorPasada = 10;

    /// <summary>
    /// Recoge los arranques que nadie avisó todavía. Devuelve cuántos se publicaron.
    ///
    /// <para><b>Existe por la aplicación de escritorio</b>, que sigue en producción, comparte esta
    /// base y tiene su propio botón de arrancar el cronómetro. Lo que se arranca allá no pasa por
    /// ningún endpoint nuestro, así que la única forma de enterarse es mirar la tabla. De paso recoge
    /// lo que el aviso inmediato no consiguió publicar.</para>
    ///
    /// <para><b>Se atiende lo MÁS NUEVO primero.</b> Al revés, un puñado de sesiones que fallan
    /// siempre —work items borrados en DevOps— serían las más viejas de cada pasada, se llevarían el
    /// cupo entero y las sesiones recién arrancadas no llegarían nunca a publicarse.</para>
    /// </summary>
    public async Task<int> AvisarIniciosPendientesAsync(CancellationToken ct = default)
    {
        if (!await EstaEncendidoAsync(ct)) return 0;

        var ahora = DateTime.UtcNow;
        var desde = ahora - Ventana;
        var hasta = ahora - Gracia;

        // ── El filtro tiene que cuadrar con quien resuelve el ticket ────────────────
        //
        // Aquí se descarta EN LA CONSULTA todo lo que <see cref="WorkItemDeLaSesion"/> va a descartar
        // después. No es una optimización: una sesión que pasa este filtro y luego no resuelve ticket
        // no llega a marcarse —sale antes de la reserva—, así que vuelve a salir en la pasada
        // siguiente, y en la siguiente. Con diez de ésas más nuevas que un arranque bueno, el cupo se
        // lo llevan siempre ellas y el arranque bueno caduca sin publicarse: el barrido dejaría de
        // hacer justo aquello para lo que existe. Las condiciones son las mismas de allá, no unas
        // parecidas; si un día divergen, esto vuelve a atascarse.
        var candidatas = await db.WorkSessions.AsNoTracking()
            .Where(w => w.InicioComentadoEnUtc == null && w.StartedAt >= desde && w.StartedAt <= hasta)
            .Where(w =>
                // Requerimiento: de Azure DevOps y asignado a quien lo cronometra.
                (w.RequirementId != null
                    && db.Requirements.Any(r => r.Id == w.RequirementId
                                             && r.Source == RequirementSource.AzureDevOps
                                             && r.ExternalId != null && r.ExternalId != "")
                    && db.Assignments.Any(a => a.RequirementId == w.RequirementId
                                            && a.DeveloperId == w.DeveloperId))
                // Percha del pool: suya, y con work item de verdad.
                || (w.ActivityId != null
                    && db.DevActivities.Any(a => a.Id == w.ActivityId && a.DeveloperId == w.DeveloperId)
                    && db.PoolActivities.Any(p => p.LinkedDevActivityId == w.ActivityId
                                               && p.DevOpsWorkItemId != null
                                               && p.DevOpsWorkItemId > 0)))
            .OrderByDescending(w => w.StartedAt)
            .Take(MaximoPorPasada)
            .Select(w => w.Id)
            .ToListAsync(ct);

        int publicados = 0;
        foreach (var id in candidatas)
        {
            ct.ThrowIfCancellationRequested();

            // Se cuentan los PUBLICADOS, no los intentados: el texto de vuelta también lleva
            // contenido cuando el aviso falló, y contarlo haría que el registro dijera «10
            // arranques publicados» la mañana en que el token caducó y no salió ninguno —que es la
            // única señal por la que alguien podría enterarse—.
            // Sin credenciales: aquí NO hay sesión de la que sacar un token personal, así que
            // firma la cuenta de la instalación. Es el único caso en que no hay alternativa, y
            // es justo el que justifica que el respaldo siga existiendo.
            var (publicado, _) = await PublicarAsync(id, credencialesDeQuienArranco: null, ct);
            if (publicado) publicados++;
        }

        return publicados;
    }

    /// <summary>
    /// Si ESTA PERSONA ya avisó hace poco sobre este mismo objetivo.
    ///
    /// <para>Por objetivo y no por work item porque es la misma pregunta —cada objetivo va a un solo
    /// ticket— y así se resuelve sobre la tabla que ya se está leyendo, sin resolver el ticket de
    /// cada sesión vieja.</para>
    ///
    /// <para><b>Y por PERSONA</b>, que no es un detalle: un requerimiento puede estar asignado a dos
    /// —quien desarrolla y quien prueba— y sin esto el aviso del segundo se lo tragaría la marca del
    /// primero. Lo que este freno existe para evitar es que la MISMA persona repita el mismo aviso
    /// tres veces en una tarde; que empiece alguien distinto es información nueva.</para>
    /// </summary>
    private Task<bool> CalladoTodaviaAsync(
        int developerId, int? requerimientoId, int? actividadId, CancellationToken ct)
    {
        var desde = DateTime.UtcNow - Silencio;

        return db.WorkSessions.AsNoTracking()
            .Where(w => w.DeveloperId == developerId
                     && w.RequirementId == requerimientoId && w.ActivityId == actividadId)
            .AnyAsync(w => w.InicioComentadoEnUtc != null && w.InicioComentadoEnUtc > desde, ct);
    }

    /// <summary>
    /// Devuelve la marca a nulo. Sin token de cancelación a propósito: si la petición se cancela
    /// justo aquí, lo que se pierde es la posibilidad de reintentar un aviso que no salió.
    /// </summary>
    private async Task SoltarLaReservaAsync(int workSessionId)
    {
        try
        {
            await db.WorkSessions
                .Where(w => w.Id == workSessionId)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.InicioComentadoEnUtc, (DateTime?)null));
        }
        catch { /* si ni esto se puede escribir, el barrido no lo verá; no hay nada mejor que hacer */ }
    }

    /// <summary>
    /// El texto que aparece en el ticket.
    ///
    /// <para>Lleva el NOMBRE dentro porque el comentario lo firma la cuenta compartida y el autor que
    /// se ve en DevOps no dice nada. Lleva el HUSO porque lo puede leer alguien que no esté en esta
    /// zona horaria. Y dice «empezó a trabajar» y no «tomó la tarea» porque son momentos distintos:
    /// esto se publica cuando el cronómetro arranca, que puede ser días después de haberla tomado.</para>
    /// </summary>
    internal static string Texto(string nombre, DateTime inicioUtc, TimeZoneInfo zona) =>
        $"⏱ <b>{WebUtility.HtmlEncode(nombre)}</b> empezó a trabajar en esto el " +
        $"{HoraDeLaOrganizacion.TextoConHuso(inicioUtc, zona)}.";

    private static string Recortar(string texto) =>
        texto.Length <= 300 ? texto : texto[..300];
}
