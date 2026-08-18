using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Pool;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Trae al pool, como actividades SIN CLASIFICAR, los work items de Azure DevOps que todavía no lo
/// están: los del líder y los que allá no tienen dueño.
///
/// <para><b>Para qué existe.</b> Para que el líder deje de tener que acordarse. Antes, que un ticket
/// acabara siendo una actividad del pool exigía seis gestos y el primero era enterarse de que el
/// ticket existía. Aquí se entera solo, y al líder le queda la única parte que es de verdad una
/// decisión: qué clase de trabajo es, cuánto tiempo lleva y de qué equipo es.</para>
///
/// <para><b>Lee de la BASE, no de la red.</b> Los work items ya los trajo la sincronización a
/// <c>DevOpsTickets</c>, con su título, su descripción, su fecha de creación y su asignado. Salir
/// otra vez a DevOps para lo mismo sería un viaje de red por nada y otra credencial que resolver;
/// además así el alta funciona igual venga de un botón, de un trabajo de fondo o de nada, y se puede
/// probar sin tocar la red.</para>
///
/// <para><b>La deduplicación se lee del POOL, no se marca en el ticket.</b> Se descarta todo work
/// item que ya tenga actividad, <b>en cualquier estado</b>. Una marca en <c>DevOpsTickets</c> habría
/// sido más cómoda y habría durado hasta la primera limpieza de datos: <c>DataCleanupService</c>
/// borra esa tabla entera, y a la pasada siguiente el pool se habría llenado con una actividad por
/// cada ticket ya tratado, sin que nadie relacionara las dos cosas. Es el mismo destrozo que explica
/// por qué <c>PoolActivity</c> no tiene clave ajena hacia allá.</para>
///
/// <para><b>Consecuencia asumida:</b> un work item cuya actividad se retiró o ya se aceptó no vuelve
/// a entrar solo NUNCA MÁS. Un bug reabierto sigue siendo un alta manual del líder, que es lo que el
/// modelo dice que debe ser: una decisión, no un automatismo.</para>
///
/// <para><b>Dos gemelos, y solo uno lleva guarda.</b> Es el patrón de la casa para lo que también
/// llama un trabajo de fondo: allí no hay sesión, así que la versión programada no puede exigir rol
/// —lo que la autoriza es la configuración del servidor— y la que se puede pedir desde fuera sí lo
/// exige.</para>
/// </summary>
public class PoolDesdeDevOpsService(
    AppDbContext db,
    ICurrentUser usuario,
    SettingsService configuracion,
    AuditService bitacora,
    NotificationService avisos)
{
    /// <summary>
    /// Cuántas actividades se dan de alta como mucho en una pasada.
    ///
    /// <para>No es una optimización: es lo que evita que el primer día —o el día que alguien amplíe
    /// la ventana— entren cien actividades de golpe en una bandeja que hay que revisar a mano. Lo que
    /// queda fuera no se pierde: entra en la siguiente pasada, y mientras tanto la bitácora dice
    /// cuántas eran.</para>
    /// </summary>
    public const int MaxAltasPorPasada = 20;

    /// <summary>Cuántos días atrás se miran los work items cuando nadie lo ha configurado.</summary>
    public const int DiasPorOmision = 7;

    /// <summary>El alta que pide una persona desde la pantalla del líder.</summary>
    public async Task<ResultadoDeAltaEnPoolDto> LlevarAlPoolAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(usuario);
        return await TraerAsync(ct);
    }

    /// <summary>
    /// El alta que dispara el trabajo periódico.
    ///
    /// <para><b>Sin guarda de rol a propósito</b>: no hay sesión detrás. Lo que autoriza esta
    /// ejecución es la configuración del servidor —el interruptor del trabajo y el intervalo—, igual
    /// que en la ingesta de correo y en el resumen diario. La versión que se puede pedir desde fuera
    /// es <see cref="LlevarAlPoolAsync"/>, y ésa sí la exige.</para>
    /// </summary>
    public Task<ResultadoDeAltaEnPoolDto> LlevarAlPoolProgramadoAsync(CancellationToken ct = default) =>
        TraerAsync(ct);

    private async Task<ResultadoDeAltaEnPoolDto> TraerAsync(CancellationToken ct)
    {
        if (!await configuracion.ObtenerBooleanoAsync(SettingsService.Claves.AzureDevOpsEnabled, ct))
            return new ResultadoDeAltaEnPoolDto(false, "La integración con Azure DevOps está apagada.", 0, 0, []);

        int dias = await configuracion.ObtenerEnteroAsync(
            SettingsService.Claves.PoolDevOpsDiasDeAlta, DiasPorOmision, ct);
        if (dias <= 0) dias = DiasPorOmision;

        var correos = await CorreosDelLiderAsync(ct);
        var desde = DateTime.UtcNow.AddDays(-dias);

        // Los work items que YA tienen actividad, en cualquier estado. Es la deduplicación entera.
        var yaEnElPool = (await db.PoolActivities.AsNoTracking()
                .Where(a => a.DevOpsWorkItemId != null)
                .Select(a => a.DevOpsWorkItemId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        // Se traen los candidatos con la criba que SÍ sabe hacer la base y el resto se decide en
        // memoria: quién es el asignado se resuelve con el mismo comparador de identidades que usa el
        // resto de la aplicación, y eso no se puede traducir a SQL.
        var candidatos = await db.DevOpsTickets.AsNoTracking()
            .Where(t => t.CreatedAtExternal != null && t.CreatedAtExternal >= desde)
            .ToListAsync(ct);

        var elegibles = candidatos
            .Where(t => !yaEnElPool.Contains(t.ExternalId))
            // Cerrado no entra: traer al pool trabajo que ya terminó es dar de alta algo que nadie va
            // a tomar. El mapeo es el mismo que usa la importación a requerimientos.
            .Where(t => !DevOpsService.EsCerrado(t.State))
            .Where(t => EsDelLiderOSinDuenno(t, correos))
            // Del más ANTIGUO al más nuevo, y esto importa por el tope: cortando por el más reciente,
            // los de siempre ganarían cada pasada y los que quedaron fuera no entrarían jamás.
            .OrderBy(t => t.CreatedAtExternal)
            .ThenBy(t => t.ExternalId)
            .ToList();

        int fuera = Math.Max(0, elegibles.Count - MaxAltasPorPasada);
        var aCrear = elegibles.Take(MaxAltasPorPasada).ToList();

        if (aCrear.Count == 0)
            return new ResultadoDeAltaEnPoolDto(
                true, "No hay work items nuevos que traer al pool.", 0, 0, []);

        var titulos = new List<string>(aCrear.Count);
        foreach (var ticket in aCrear)
        {
            db.PoolActivities.Add(DesdeElTicket(ticket));
            titulos.Add($"#{ticket.ExternalId} {ticket.Title}");
        }

        await db.SaveChangesAsync(ct);

        // El aviso va DESPUÉS de guardar, como el de la importación: avisar de una bandeja que no se
        // llegó a escribir mandaría a mirar una pantalla vacía. Y con clave de deduplicación, para
        // que dos pasadas seguidas no acaben en dos avisos idénticos.
        await AvisarAlLiderAsync(aCrear.Count, ct);

        await bitacora.RecordAsync(AuditAction.Create, "PoolActivity", null,
            $"Alta automática desde Azure DevOps: {aCrear.Count} actividad(es) por clasificar" +
            (fuera > 0 ? $"; {fuera} quedaron fuera por el tope de {MaxAltasPorPasada} y entrarán en la siguiente pasada" : ""),
            ct);

        var mensaje = $"{aCrear.Count} actividad(es) traída(s) al pool, pendientes de clasificar.";
        if (fuera > 0)
            mensaje += $" Otras {fuera} quedaron fuera por el tope de {MaxAltasPorPasada} por pasada; " +
                       "entrarán en la siguiente.";

        return new ResultadoDeAltaEnPoolDto(true, mensaje, aCrear.Count, fuera, titulos);
    }

    /// <summary>
    /// La actividad tal como nace: con lo que se puede copiar del ticket y nada más.
    ///
    /// <para><b>Sin puntos y sin tomar.</b> El estado se escribe A MANO porque <c>Disponible</c> es
    /// el valor 0 del enumerado: una fila que no lo fijara nacería tomable, y alguien podría llevarse
    /// esa misma tarde una actividad con los puntos de un Bug de complejidad Baja que nadie eligió.</para>
    ///
    /// <para><b>La prioridad se siembra del ticket</b> en vez de dejarla en «Media». No es un detalle:
    /// «Media» se escribe en DevOps como 3 y allá el valor por omisión es 2, así que publicar una
    /// bandeja entera con la prioridad sin tocar le BAJARÍA la prioridad a cien tickets de golpe.</para>
    /// </summary>
    private static PoolActivity DesdeElTicket(DevOpsTicket t) => new()
    {
        // Recortado a 200, que es lo que admite la columna. El alta no pasa por la validación del
        // borrador —es ella la que exige el tipo y las horas que aquí todavía no hay— así que este
        // recorte es el único que hay entre un título largo de DevOps y un INSERT que revienta.
        Title       = Recortar(t.Title, 200),
        // La descripción llega en HTML y la escribe gente de fuera del equipo: se limpia en el
        // servidor, con la misma regla que los comentarios.
        Description = Vacio(Recortar(TextoDeDevOps.ATextoPlano(t.Description), 4000)),
        Priority    = PrioridadDelPoolEnDevOps.DesdeDevOps(PrioridadDelTicket(t.Priority)),
        Status      = PoolActivityStatus.PorClasificar,
        Points      = 0,
        ExternalUrl = string.IsNullOrWhiteSpace(t.Url) ? null : t.Url,
        DevOpsWorkItemId = t.ExternalId,
        CreatedAt   = DateTime.UtcNow
    };

    /// <summary>La prioridad de DevOps viene como texto y puede venir vacía; fuera de 1..4 se lee
    /// como «sin decidir», que es lo que significa allá.</summary>
    private static int PrioridadDelTicket(string? prioridad) =>
        int.TryParse(prioridad, out var n) && n is >= 1 and <= 4 ? n : 3;

    /// <summary>
    /// El ticket es del líder o no es de nadie.
    ///
    /// <para>Se empata con <see cref="DevOpsIdentityMatcher"/> y no comparando cadenas: es el mismo
    /// comparador con el que toda la aplicación decide de quién es un ticket, y existe precisamente
    /// porque el nombre para mostrar de DevOps trae y omite segundos nombres y acentos.</para>
    ///
    /// <para>Sin correos configurados solo entran los que no tienen dueño. Es deliberado: adivinar
    /// cuáles son «los míos» a partir de cualquier otra cosa llenaría el pool de trabajo ajeno.</para>
    /// </summary>
    private static bool EsDelLiderOSinDuenno(DevOpsTicket t, IReadOnlyList<Developer> correos)
    {
        bool sinDuenno = string.IsNullOrWhiteSpace(t.AssignedTo)
                      && string.IsNullOrWhiteSpace(t.AssignedToUniqueName);

        return sinDuenno
            || DevOpsIdentityMatcher.Buscar(t.AssignedTo, t.AssignedToUniqueName, correos) != null;
    }

    /// <summary>
    /// A nombre de quién son «los míos».
    ///
    /// <para>Sale de un ajuste y no de la sesión, porque el trabajo de fondo no tiene ninguna. Y no
    /// se usa la macro <c>@Me</c> de DevOps: ésa se resuelve contra el dueño del token, que aquí es
    /// el de la INSTALACIÓN, así que traería los tickets de una cuenta compartida en vez de los del
    /// líder.</para>
    ///
    /// <para>Se devuelve como fichas de desarrollador de mentira —solo con el correo— porque es lo
    /// que come el comparador de identidades; no hace falta que existan en la tabla.</para>
    /// </summary>
    private async Task<IReadOnlyList<Developer>> CorreosDelLiderAsync(CancellationToken ct)
    {
        var crudo = await configuracion.ObtenerAsync(SettingsService.Claves.PoolDevOpsCorreosDeAlta, ct);
        if (string.IsNullOrWhiteSpace(crudo)) return [];

        return [.. crudo
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => new Developer { Email = c, FullName = c })];
    }

    /// <summary>
    /// Avisa a quien tiene que clasificarlas. Sin esto, la bandeja es una pantalla que nadie sabe que
    /// tiene algo dentro, y el automatismo entero no serviría de nada.
    /// </summary>
    private async Task AvisarAlLiderAsync(int cuantas, CancellationToken ct)
    {
        try
        {
            var administradores = await db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.Role == UserRole.Admin)
                .Select(u => u.Id)
                .ToListAsync(ct);

            foreach (var userId in administradores)
                await avisos.NotifyAsync(userId, NotificationKind.General,
                    "Actividades del pool por clasificar",
                    $"{cuantas} llegaron desde Azure DevOps y esperan tipo, tiempo y equipo.",
                    "pool", dedupeKey: $"pool-por-clasificar:{DateTime.UtcNow:yyyyMMddHH}", ct: ct);
        }
        catch { /* el aviso es cortesía: las actividades ya están guardadas */ }
    }

    private static string Recortar(string? texto, int max)
    {
        texto = (texto ?? "").Trim();
        return texto.Length <= max ? texto : texto[..max];
    }

    /// <summary>Un texto en blanco se guarda como NULO y no como cadena vacía: son lo mismo para
    /// quien lee y distintos para quien consulta.</summary>
    private static string? Vacio(string s) => s.Length == 0 ? null : s;
}
