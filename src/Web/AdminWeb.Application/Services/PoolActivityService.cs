using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// El pool de actividades valoradas: crear, tomar, completar contra un checklist y convertir en
/// puntos.
///
/// Lo que resuelve, y por qué existe aparte de <see cref="PerformanceScoringService"/>: en la
/// autocalificación libre el desarrollador elegía qué registrar y el líder juzgaba después si lo
/// valía. Dos decisiones subjetivas sobre trabajo ya hecho, y ninguna de las dos comparable entre
/// personas. Aquí el valor está fijado ANTES —sale de la matriz y queda congelado en la actividad—,
/// de modo que verificar no es negociar cuánto vale, sino comprobar que está hecho.
///
/// Los puntos siguen viviendo en <see cref="PointEntry"/>: al aceptar una actividad se crea una
/// entrada ya APROBADA. Así el ranking, los KPIs, los reportes y el PDF funcionan sin cambiar una
/// línea, y el pool no se convierte en un segundo sistema de puntuación paralelo al que ya existe.
///
/// Del escritorio se conservan intactos los UPDATE CONDICIONALES de <see cref="TomarAsync"/> y
/// <see cref="AceptarAsync"/>: son la garantía —en la base, no en el código— de que dos personas no
/// se lleven la misma actividad ni se abonen dos veces los mismos puntos, y en la web importan más
/// que en el escritorio porque ahora sí hay peticiones concurrentes de verdad. Lo que sí desaparece
/// son las defensas del contexto Singleton (Reload antes de decidir, Detach tras un fallo,
/// sincronizar el ChangeTracker): con un contexto por petición, lo que se lee ya es fresco y lo que
/// quede pendiente tras un error muere con la petición.
/// </summary>
/// <param name="devopsDelPool">
/// El puente con Azure DevOps, para empujar allá el esfuerzo y la prioridad de las actividades
/// LIGADAS a un work item.
///
/// <para><b>Va opcional, y por eso mismo el pool no depende de la integración.</b> Una instalación
/// sin Azure DevOps configurado —o una prueba que solo mira los puntos y el checklist— tiene que
/// poder publicar, tomar y aceptar actividades exactamente igual. Si fuera obligatorio, la mitad del
/// sistema de puntos arrastraría un cliente HTTP para no usarlo nunca.</para>
///
/// <para>El empuje se invoca SIEMPRE después del <c>SaveChanges</c> que guarda el cambio, nunca
/// antes y nunca dentro de una transacción: lo local se guarda pase lo que pase, y lo que devuelve
/// es un aviso que se añade al mensaje, no un fallo que lo tumbe.</para>
/// </param>
public class PoolActivityService(
    AppDbContext db, ICurrentUser currentUser, AuditService audit, NotificationService notifications,
    SettingsService configuracion, PoolDevOpsService? devopsDelPool = null)
{
    /// <summary>Cuántas actividades puede tener alguien tomadas a la vez, si nadie lo configuró.</summary>
    public const int MaxTomadasPorOmision = 3;

    /// <summary>Clave del tope en la configuración, para que el líder lo ajuste sin recompilar.</summary>
    public const string ClaveMaxTomadas = "pool.max-tomadas";

    /// <summary>Tope del motivo de una devolución. Da para explicarse, no para un ensayo.</summary>
    public const int MaxMotivo = 1000;

    /// <summary>
    /// Tope del PLAZO, en horas: 2 920 = los 365 días de antes por una jornada de ocho. Es el mismo
    /// techo que ya había, dicho en la unidad nueva, para no ampliar de tapadillo lo que se podía
    /// prometer.
    /// </summary>
    public const decimal MaxHorasDePlazo = 2920m;

    /// <summary>
    /// Tope del ESFUERZO, en horas. El mismo que <see cref="RequirementService.MaxHoras"/>: mil horas
    /// son medio año de una persona, y a partir de ahí no es una actividad, es un proyecto que hay
    /// que partir. Que los dos topes coincidan importa porque los dos números acaban comparándose
    /// contra las mismas <c>WorkSession</c>.
    /// </summary>
    public const decimal MaxHorasEstimadas = RequirementService.MaxHoras;

    /// <summary>
    /// Piso del ESFUERZO: el cuarto de hora, que es la granularidad con la que ya se estima en la
    /// pantalla de tickets. Con un piso de cero, «0» pasaría por estimación y dejaría la comparación
    /// contra el cronómetro sin nada al otro lado.
    /// </summary>
    public const decimal MinHorasEstimadas = 0.25m;

    // ── Consultas ────────────────────────────────────────────────────────────────

    /// <summary>Lo que hay libre en el pool ahora mismo, de lo más valioso a lo menos.</summary>
    public async Task<List<PoolActivity>> DisponiblesAsync(
        PoolWorkType? tipo = null, PoolComplexity? complejidad = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        // Con los criterios extra: quien mira el pool tiene que poder ver qué se le va a pedir de
        // más ANTES de tomar la actividad. Sin el Include llegarían vacíos y en silencio.
        var q = db.PoolActivities.AsNoTracking()
            .Include(a => a.ExtraCriteria)
            .Where(a => a.Status == PoolActivityStatus.Disponible);
        if (tipo is PoolWorkType t) q = q.Where(a => a.WorkType == t);
        if (complejidad is PoolComplexity c) q = q.Where(a => a.Complexity == c);

        // Lo urgente primero. Antes mandaban los puntos, y eso premiaba tomar lo que más paga en vez
        // de lo que más corre; la prioridad existe justamente para poder decir cuál es cuál.
        return await q
            .OrderByDescending(a => a.Priority)
            .ThenByDescending(a => a.Points)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>Las actividades del pool de un desarrollador: en curso primero, aceptadas al final.</summary>
    public async Task<List<PoolActivity>> MisDelPoolAsync(int developerId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, developerId);

        return await db.PoolActivities.AsNoTracking()
            .Where(a => a.ClaimedByDeveloperId == developerId && a.Status != PoolActivityStatus.Disponible)
            .OrderBy(a => a.Status == PoolActivityStatus.Aceptada)   // lo pendiente arriba
            .ThenByDescending(a => a.ClaimedAt)
            .ToListAsync(ct);
    }

    /// <summary>Todas, para la pantalla del líder.</summary>
    public async Task<List<PoolActivity>> TodasAsync(
        PoolActivityStatus? estado = null, PoolWorkType? tipo = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        // ExtraCriteria va incluido porque la vista del líder calcula con él el máximo alcanzable.
        // Sin el Include no fallaría nada: EF devolvería la colección VACÍA y el máximo saldría
        // igual a la base, en silencio y siempre mal. Un error que no se ve es peor que uno que se ve.
        var q = db.PoolActivities.AsNoTracking()
            .Include(a => a.ClaimedBy)
            .Include(a => a.ExtraCriteria)
            .AsQueryable();
        if (estado is PoolActivityStatus e) q = q.Where(a => a.Status == e);
        if (tipo is PoolWorkType t) q = q.Where(a => a.WorkType == t);

        // La prioridad manda en el orden dentro de cada estado: lo urgente arriba, que es lo que
        // hace útil la columna. Descendente porque Crítica es el valor más alto del enum.
        return await q
            .OrderBy(a => a.Status)
            .ThenByDescending(a => a.Priority)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>Las que esperan verificación del líder.</summary>
    public async Task<List<PoolActivity>> PendientesDeVerificarAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);
        return await db.PoolActivities.AsNoTracking().Include(a => a.ClaimedBy)
            .Where(a => a.Status == PoolActivityStatus.EnRevision)
            .OrderBy(a => a.DeliveredAt)
            .ToListAsync(ct);
    }

    /// <summary>Cuántas esperan verificación. Para el aviso del menú; sin guarda porque solo cuenta.</summary>
    public Task<int> CuentaPendientesDeVerificarAsync(CancellationToken ct = default) =>
        db.PoolActivities.AsNoTracking().CountAsync(a => a.Status == PoolActivityStatus.EnRevision, ct);

    /// <summary>
    /// El checklist de una actividad. Requiere sesión y nada más: lo consultan tanto quien la trabaja
    /// como el líder que la verifica, y no contiene nada privado —son los mismos puntos que la
    /// plantilla del tipo, más los enlaces de evidencia que el propio interesado capturó—.
    /// </summary>
    public async Task<List<PoolActivityChecklistItem>> ChecklistDeAsync(
        int poolActivityId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        return await db.PoolActivityChecklistItems.AsNoTracking()
            .Where(c => c.PoolActivityId == poolActivityId)
            .OrderBy(c => c.Orden).ThenBy(c => c.Id)
            .ToListAsync(ct);
    }

    // ── Administración de la actividad ───────────────────────────────────────────

    /// <summary>
    /// Crea una actividad en el pool. Los puntos NO vienen del borrador: se leen de la matriz y se
    /// congelan aquí. Es la regla central del sistema y por eso vive en el servicio, no en la
    /// pantalla — que nadie pueda escribir el valor es lo que hace comparables las actividades.
    /// </summary>
    public async Task<(bool ok, string mensaje, PoolActivity? actividad)> CrearAsync(
        PoolActivity borrador, IReadOnlyList<int>? criteriosExtra = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        // Id 0: todavía no existe, así que ninguna fila puede ser «ella misma» al comprobar que
        // nadie más tiene ese work item.
        var (valido, error, celda, enlace, workItem) = await ValidarBorradorAsync(borrador, 0, ct);
        if (!valido) return (false, error, null);

        var (extraOk, extraError, extras) = await MaterializarCriteriosExtraAsync(criteriosExtra, ct);
        if (!extraOk) return (false, extraError, null);

        // El esfuerzo solo se escribe cuando NO es un bug: en un bug lo pone quien lo tome, y la
        // validación de arriba ya rechazó que viniera. El sello acompaña siempre al número, para que
        // después se pueda saber cuándo se capturó y no solo cuánto se dijo.
        var esfuerzo = borrador.WorkType == PoolWorkType.Bug ? null : Redondear(borrador.HorasEstimadas);

        // DiasLimite NO se escribe: la web ya no lo usa. Durante la convivencia, el escritorio verá
        // las actividades publicadas desde aquí como «sin plazo propio» y les aplicará los días de su
        // propia matriz. Se asume a sabiendas; escribir además el equivalente en días sería mantener
        // dos verdades sobre lo mismo y que se contradijeran a la primera edición.
        var actividad = new PoolActivity
        {
            Title               = borrador.Title.Trim(),
            Description         = Limpiar(borrador.Description),
            WorkType            = borrador.WorkType,
            Complexity          = borrador.Complexity,
            Points              = celda!.Points,
            Priority            = borrador.Priority,
            HorasLimite         = Redondear(borrador.HorasLimite),
            HorasEstimadas      = esfuerzo,
            HorasEstimadasEnUtc = esfuerzo is null ? null : DateTime.UtcNow,
            Status              = PoolActivityStatus.Disponible,
            ExternalUrl         = enlace,
            DevOpsWorkItemId    = workItem,
            CreatedByUserId     = currentUser.UserId,
            CreatedAt           = DateTime.UtcNow
        };
        foreach (var extra in extras) actividad.ExtraCriteria.Add(extra);

        db.PoolActivities.Add(actividad);
        await db.SaveChangesAsync(ct);

        var extraTexto = extras.Count > 0
            ? $", +{extras.Sum(e => e.Points)} pts en {extras.Count} criterio(s) extra por evaluar"
            : "";

        await audit.RecordAsync(AuditAction.Create, "PoolActivity", actividad.Id.ToString(),
            $"Actividad del pool «{actividad.Title}» ({PoolSeed.Etiqueta(actividad.WorkType)}/" +
            $"{PoolSeed.Etiqueta(actividad.Complexity)}, {actividad.Points} pts, " +
            $"prioridad {EtiquetasDeCatalogo.PrioridadDelPool(actividad.Priority)}{extraTexto}" +
            (workItem is int wi ? $", ligada al work item #{wi}" : "") + ")", ct);

        return (true, ConEmpuje(MensajeDeAlta(actividad.Points, extras),
                                await EmpujarADevOpsAsync(actividad.Id, ct)), actividad);
    }

    /// <summary>
    /// Manda a DevOps lo que le falte de la actividad, si está ligada y la integración está montada.
    ///
    /// <para><b>Va SIEMPRE detrás del <c>SaveChanges</c> que guardó el cambio</b>, nunca dentro de
    /// él ni dentro de una transacción. Es la regla que hace que publicar, editar o tomar una
    /// actividad no puedan fallar ni colgarse porque un servidor ajeno no conteste: lo de aquí ya
    /// está guardado cuando esto empieza, y lo peor que devuelve es un texto que añadir al
    /// mensaje.</para>
    ///
    /// <para>Sin integración configurada devuelve vacío y no dice nada: quien no la use no tiene por
    /// qué leer un aviso sobre ella en cada actividad que publica.</para>
    /// </summary>
    private async Task<string> EmpujarADevOpsAsync(int poolActivityId, CancellationToken ct)
    {
        if (devopsDelPool is null) return "";
        var (_, aviso) = await devopsDelPool.EmpujarAsync(poolActivityId, ct);
        return aviso;
    }

    private static string ConEmpuje(string mensaje, string aviso) =>
        aviso.Length == 0 ? mensaje : $"{mensaje} {aviso}";

    /// <summary>
    /// Lo que se le dice al líder tras publicar. Enseña el máximo alcanzable además de la base,
    /// porque es el número que verá quien mire la actividad en el pool y conviene que el líder lo
    /// haya visto antes de publicarla.
    /// </summary>
    private static string MensajeDeAlta(int puntos, List<PoolActivityExtraCriterion> extras) =>
        extras.Count == 0
            ? $"Actividad publicada en el pool: {puntos} puntos."
            : $"Actividad publicada: {puntos} puntos de base y hasta {puntos + extras.Sum(e => e.Points)} " +
              $"si se cumplen los {extras.Count} criterio(s) extra.";

    /// <summary>
    /// Edita una actividad que sigue en el pool. Solo mientras nadie la haya tomado: cambiarle el
    /// alcance o el valor a alguien que ya la está trabajando sería cambiar el trato a medio camino.
    /// Si cambia el tipo o la complejidad, los puntos se recongelan desde la matriz.
    /// </summary>
    public async Task<(bool ok, string mensaje)> EditarAsync(
        int id, PoolActivity cambios, IReadOnlyList<int>? criteriosExtra = null,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var actividad = await db.PoolActivities
            .Include(a => a.ExtraCriteria)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");

        if (actividad.Status != PoolActivityStatus.Disponible)
            return (false, "Ya la tomó alguien: no se puede cambiar lo que vale ni lo que pide.");

        var (valido, error, celda, enlace, workItem) = await ValidarBorradorAsync(cambios, id, ct);
        if (!valido) return (false, error);

        var (extraOk, extraError, extras) = await MaterializarCriteriosExtraAsync(criteriosExtra, ct);
        if (!extraOk) return (false, extraError);

        // ── LA LIMPIEZA AL CAMBIAR DE TIPO ───────────────────────────────────────
        //
        // Va AQUÍ, y solo aquí, porque éste es el ÚNICO punto de la aplicación donde el tipo de una
        // actividad ya guardada cambia (CrearAsync da de alta, y el resto de los sitios que escriben
        // WorkType construyen objetos en memoria: el borrador de los endpoints, las filas de la
        // matriz, la demostración y las pruebas).
        //
        // Al cruzar la frontera bug / no-bug, la estimación cambia de dueño y el número capturado se
        // quedaría atribuido a quien no lo escribió:
        //  · un bug que pasa a tarea puede llevar la estimación de QUIEN LO TOMÓ, y en una tarea la
        //    estimación es del líder: conservarla sería atribuirle un número que no escribió;
        //  · una tarea que pasa a bug lleva la estimación DEL LÍDER, y en un bug es de quien lo toma:
        //    también se borra, y se le pedirá al tomarlo.
        // Tarea ↔ Requerimiento NO cruza esa frontera y no dispara nada: en los dos el autor es el
        // líder, y borrar ahí le haría recapturar un número que sigue siendo suyo.
        //
        // Es lo que sostiene que el autor de HorasEstimadas se pueda DERIVAR del tipo en vez de
        // guardarse en una columna aparte. Aquí la actividad sigue Disponible —lo exige la guarda de
        // arriba—, así que nadie la tenía tomada y a nadie que esté trabajando se le quita su número.
        bool cambiaElAutorDeLaEstimacion =
            (actividad.WorkType == PoolWorkType.Bug) != (cambios.WorkType == PoolWorkType.Bug);
        if (cambiaElAutorDeLaEstimacion)
        {
            actividad.HorasEstimadas      = null;
            actividad.HorasEstimadasEnUtc = null;
        }

        actividad.Title       = cambios.Title.Trim();
        actividad.Description = Limpiar(cambios.Description);
        actividad.WorkType    = cambios.WorkType;
        actividad.Complexity  = cambios.Complexity;
        actividad.Points      = celda!.Points;
        actividad.Priority    = cambios.Priority;
        actividad.HorasLimite = Redondear(cambios.HorasLimite);
        actividad.ExternalUrl = enlace;

        // ── EL VÍNCULO, AL CAMBIAR DE TICKET ─────────────────────────────────────
        //
        // <b>Sin número, el vínculo se queda como está.</b> No se desliga, y eso es deliberado: este
        // campo es NUEVO en la petición, así que cualquier pantalla que edite una actividad sin
        // saber de él mandaría un nulo, y «ausente» tiene que significar «no lo toques» y no
        // «bórralo». Con la otra lectura, editar el título de una actividad ligada la desligaría en
        // silencio y el ticket dejaría de recibir nada sin que nadie lo hubiera pedido. Desligar es
        // destructivo y tiene su propia ruta, que además lo dice en su respuesta.
        //
        // Al apuntar a OTRO ticket sí se limpia la marca de agua: dice «DevOps ya tiene esto» y esa
        // afirmación es sobre un work item concreto. Conservarla haría que la actividad se creyera
        // al día en un ticket al que nunca se le mandó nada, y no volvería a mandarse hasta la
        // siguiente edición del esfuerzo. Borrarla la deja pendiente, y el empuje de abajo la
        // resuelve dentro de la misma operación.
        if (workItem is not null && actividad.DevOpsWorkItemId != workItem)
        {
            actividad.DevOpsWorkItemId       = workItem;
            actividad.DevOpsEsfuerzoEnviado  = null;
            actividad.DevOpsPrioridadEnviada = null;
            actividad.DevOpsUltimoError      = null;
        }

        // El esfuerzo del líder se reescribe solo cuando le toca ponerlo. En un bug la validación ya
        // garantizó que no viene ninguno, y lo que quede es lo que escribió quien lo tomó (o el nulo
        // que acaba de dejar la limpieza de arriba): pisarlo con null aquí borraría, en cada edición
        // de un bug, la estimación de otra persona.
        if (cambios.WorkType != PoolWorkType.Bug)
        {
            actividad.HorasEstimadas      = Redondear(cambios.HorasEstimadas);
            actividad.HorasEstimadasEnUtc = DateTime.UtcNow;
        }

        // Los criterios extra se REEMPLAZAN por completo. Se puede porque aquí la actividad sigue
        // Disponible —nadie la ha tomado— así que nadie los ha visto todavía para decidir si la
        // tomaba; en cuanto alguien la toma, esta ruta ya está cerrada por la guarda de arriba.
        actividad.ExtraCriteria.Clear();
        foreach (var extra in extras) actividad.ExtraCriteria.Add(extra);

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "PoolActivity", actividad.Id.ToString(),
            $"Actividad del pool actualizada: {actividad.Points} pts, prioridad " +
            $"{EtiquetasDeCatalogo.PrioridadDelPool(actividad.Priority)}, " +
            $"{extras.Count} criterio(s) extra", ct);

        // El empuje va aquí y no solo al ligar porque la prioridad y el esfuerzo se editan: mandar
        // solo la primera vez dejaría DevOps con el número del día que se publicó, que es peor que
        // no mandar nada — parecería al día y no lo estaría.
        return (true, ConEmpuje(MensajeDeAlta(actividad.Points, extras).Replace("publicada", "actualizada"),
                                await EmpujarADevOpsAsync(actividad.Id, ct)));
    }

    /// <summary>
    /// El líder marca si un criterio extra se cumplió. Solo los cumplidos suman al aceptar.
    ///
    /// <para>Se puede evaluar mientras la actividad está entregada o devuelta, no antes: evaluar
    /// algo que todavía no se ha entregado sería puntuar una intención. Y no después de aceptada,
    /// porque en ese momento los puntos ya se otorgaron y cambiar la respuesta dejaría la entrada de
    /// puntos diciendo un total que ya no se corresponde con sus criterios.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> EvaluarCriterioExtraAsync(
        int criterioId, bool cumplido, string? comentario, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var criterio = await db.PoolActivityExtraCriteria
            .Include(c => c.Activity)
            .FirstOrDefaultAsync(c => c.Id == criterioId, ct);
        if (criterio == null) return (false, "Ese criterio ya no existe. Actualiza la lista.");

        var estado = criterio.Activity.Status;
        if (estado is PoolActivityStatus.Aceptada)
            return (false, "La actividad ya se aceptó y sus puntos están otorgados: " +
                           "los criterios ya no se pueden cambiar.");
        if (estado is not (PoolActivityStatus.EnRevision or PoolActivityStatus.Devuelta))
            return (false, "Los criterios extra se evalúan cuando hay una entrega que mirar, " +
                           "no antes.");

        criterio.IsMet          = cumplido;
        criterio.EvaluatedAtUtc = DateTime.UtcNow;
        criterio.Comment        = string.IsNullOrWhiteSpace(comentario) ? null : comentario.Trim();

        await db.SaveChangesAsync(ct);

        return (true, cumplido
            ? $"«{criterio.Name}» dado por cumplido: +{criterio.Points} puntos."
            : $"«{criterio.Name}» marcado como NO cumplido: no suma.");
    }

    /// <summary>Quita del pool una actividad que ya no aplica. Solo si nadie la tomó.</summary>
    public async Task<(bool ok, string mensaje)> RetirarAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var actividad = await db.PoolActivities.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");

        if (actividad.Status != PoolActivityStatus.Disponible)
            return (false, "Solo se retira lo que sigue libre en el pool. Si alguien la tomó, usa «Liberar».");

        actividad.Status = PoolActivityStatus.Retirada;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "PoolActivity", actividad.Id.ToString(), "Retirada del pool", ct);
        return (true, "Actividad retirada del pool.");
    }

    /// <summary>
    /// Devuelve al pool una actividad que alguien tomó y no avanza (se fue de vacaciones, se venció,
    /// cambió de prioridad). Es del líder porque afecta el trabajo de otra persona: por eso avisa.
    /// </summary>
    public async Task<(bool ok, string mensaje)> LiberarAsync(
        int id, string? motivo, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var actividad = await db.PoolActivities.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");

        if (!actividad.EnCurso && actividad.Status != PoolActivityStatus.EnRevision)
            return (false, "Esa actividad no la tiene nadie.");

        int? devAnterior = actividad.ClaimedByDeveloperId;
        int devActivityId = actividad.LinkedDevActivityId ?? 0;
        motivo = Limpiar(motivo);

        AnotarEnHistorial(actividad, $"Liberada por {NombreDelUsuario()}" +
                                     (motivo != null ? $": {motivo}" : "") + ". Vuelve al pool.");
        SoltarReclamo(actividad);
        await BorrarChecklistAsync(actividad.Id, ct);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "PoolActivity", actividad.Id.ToString(),
            $"Liberada al pool{(motivo != null ? $": {motivo}" : "")}", ct);

        if (devAnterior is int dev)
        {
            try
            {
                await notifications.NotifyDeveloperAsync(dev, NotificationKind.General,
                    "Una actividad del pool volvió al pool",
                    $"«{actividad.Title}» ya no está a tu nombre." + (motivo != null ? $" Motivo: {motivo}" : ""),
                    dedupeKey: $"pool-liberada-{actividad.Id}-{actividad.ReturnedCount}", ct: ct);
            }
            catch { /* la liberación ya está hecha; el aviso es cortesía */ }
        }

        // Igual que en Devolver: el cronómetro se cierra al final, con el estado ya firme.
        await CerrarActividadEnlazadaAsync(devActivityId, ct);

        return (true, "Actividad devuelta al pool.");
    }

    // ── Ciclo del desarrollador ──────────────────────────────────────────────────

    /// <summary>
    /// El desarrollador toma una actividad del pool. Aquí pasan tres cosas que importan:
    /// el reclamo se gana de forma ATÓMICA, se COPIA el checklist vigente (congelado: se le exigirá
    /// lo que se le pidió al tomarla, no lo que se agregue después) y se crea una actividad libre
    /// enlazada para que pueda cronometrar el trabajo con el cronómetro de siempre.
    ///
    /// El reclamo se gana con un UPDATE condicional y no leyendo-y-después-escribiendo. Releer no
    /// basta: entre la lectura y el guardado caben varios viajes a la base, y dos personas que
    /// abrieron la lista a la vez leerían «Disponible» las dos, escribirían las dos —el UPDATE de EF
    /// no lleva condición de estado— y acabarían con la actividad a nombre del último y el checklist
    /// duplicado. Con el UPDATE condicional, la base decide quién gana: solo uno afecta una fila.
    /// </summary>
    /// <param name="horasEstimadas">
    /// En cuántas horas cree resolverlo quien la toma. <b>Obligatorio en los BUGS</b> y rechazado en
    /// tareas y requerimientos, donde el esfuerzo lo fijó el líder al publicar.
    ///
    /// <para>Va con valor por omisión y ANTES del token de cancelación para no romper las firmas
    /// existentes: un bug tomado sin él falla en voz alta con un mensaje que explica el porqué, que
    /// es exactamente lo que se busca, en vez de reclamarse en silencio sin estimación.</para>
    /// </param>
    public async Task<(bool ok, string mensaje)> TomarAsync(
        int id, int developerId, decimal? horasEstimadas = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, developerId);

        var actividad = await db.PoolActivities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");
        if (actividad.Status != PoolActivityStatus.Disponible)
            return (false, "Alguien más la tomó primero. Actualiza la lista.");

        int tope = await TopeDeTomadasAsync(ct);
        int tomadas = await db.PoolActivities.AsNoTracking()
            .CountAsync(a => a.ClaimedByDeveloperId == developerId
                          && (a.Status == PoolActivityStatus.Tomada || a.Status == PoolActivityStatus.Devuelta), ct);
        if (tomadas >= tope)
            return (false, $"Ya tienes {tomadas} actividad(es) del pool sin entregar y el tope es {tope}. " +
                           "Termina o devuelve alguna antes de tomar otra.");

        var celda = await CeldaDeMatrizAsync(actividad.WorkType, actividad.Complexity, ct);
        var ahora = DateTime.UtcNow;

        // ── LA ESTIMACIÓN OBLIGATORIA DEL BUG ────────────────────────────────────
        //
        // Se pide AQUÍ y en ningún otro momento. Escrita a mitad del trabajo ya no es una estimación:
        // quien la escribe sabe lo que le costó, y el número deja de servir para contrastarlo con el
        // cronómetro, que es para lo único que existe.
        //
        // Se valida ANTES del UPDATE condicional, así que quien mande una estimación inválida se va
        // sin haber reclamado nada: la actividad sigue Disponible y sin dueño.
        decimal? estimacion = null;
        if (actividad.WorkType == PoolWorkType.Bug)
        {
            if (horasEstimadas is not decimal propuesta)
                return (false, "Antes de tomar un bug tienes que decir en cuántas horas crees " +
                               "resolverlo. Es el único momento en que ese número sirve de algo: " +
                               "después ya sabrás lo que te costó.");
            if (propuesta < MinHorasEstimadas || propuesta > MaxHorasEstimadas)
                return (false, $"La estimación tiene que estar entre {MinHorasEstimadas} y " +
                               $"{MaxHorasEstimadas:0} horas.");
            estimacion = Redondear(propuesta);
        }
        else if (horasEstimadas is not null)
        {
            return (false, "El esfuerzo de una tarea o un requerimiento lo fija el líder al " +
                           "publicarla; al tomarla no se cambia.");
        }

        // El plazo de ESTA actividad manda sobre el de la matriz; si no se fijó, la matriz. Sigue
        // contando desde AHORA y no desde que se publicó, para que una actividad que esperó dos
        // semanas en el pool no llegue con el plazo ya consumido. Lo que cambia es la UNIDAD: se
        // suman HORAS, para que lo que se promete y lo que mide el cronómetro sean el mismo número.
        // Son horas de reloj —incluyen noches y fines de semana—, que es lo coherente con medir
        // contra un cronómetro; contar solo jornadas hábiles exigiría un calendario laboral entero.
        decimal horas = actividad.HorasLimite ?? celda?.HorasLimite ?? 0m;
        DateTime? limite = horas > 0 ? ahora.AddHours((double)horas) : null;

        // El sello viaja junto al número y con la misma forma nula, para que el UPDATE de abajo pueda
        // dejar los dos como estaban con un COALESCE y no con un condicional que EF tendría que
        // traducir sobre un parámetro.
        DateTime? selloDeLaEstimacion = estimacion is null ? null : ahora;

        int ganadas = await db.PoolActivities
            .Where(a => a.Id == id && a.Status == PoolActivityStatus.Disponible)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, PoolActivityStatus.Tomada)
                .SetProperty(a => a.ClaimedByDeveloperId, developerId)
                .SetProperty(a => a.ClaimedAt, ahora)
                .SetProperty(a => a.ClaimDeadlineAt, limite)
                // La estimación entra en el MISMO update que gana el reclamo, no en un guardado
                // posterior. Si fuera un segundo paso, dos personas mandando su estimación a la vez
                // acabarían con la actividad a nombre de una y el número de la otra: quien pierde la
                // carrera no debe poder escribir nada, y así no escribe nada.
                //
                // El COALESCE no es un adorno: en una tarea o un requerimiento «estimacion» es null y
                // la columna YA trae el número del líder desde que se publicó. Escribir null la
                // borraría al tomarla, y quien la tomó se quedaría sin nada contra qué comparar.
                .SetProperty(a => a.HorasEstimadas, a => estimacion ?? a.HorasEstimadas)
                .SetProperty(a => a.HorasEstimadasEnUtc, a => selloDeLaEstimacion ?? a.HorasEstimadasEnUtc), ct);
        if (ganadas == 0) return (false, "Alguien más la tomó primero. Actualiza la lista.");

        // El reclamo ya es firme; a partir de aquí se trabaja sobre la entidad rastreada. Se lee de
        // la base y no de la copia AsNoTracking de arriba porque el UPDATE condicional se ejecutó
        // directamente contra la base y esa copia todavía diría «Disponible».
        var reclamada = await db.PoolActivities.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (reclamada != null)
        {
            await CopiarChecklistAsync(reclamada, ct);
            var cronometro = CrearActividadEnlazada(reclamada, developerId);
            await db.SaveChangesAsync(ct);

            // El identificador del cronómetro solo existe después de insertarlo, así que el enlace
            // se guarda en un segundo paso.
            reclamada.LinkedDevActivityId = cronometro.Id;
            await db.SaveChangesAsync(ct);
        }

        await audit.RecordAsync(AuditAction.Update, "PoolActivity", id.ToString(),
            $"Tomada del pool ({actividad.Points} pts)", ct);

        // Con la HORA y no solo el día: con plazos de cuatro u ocho horas, enseñar la fecha a secas
        // es enseñar un plazo falso —«hoy» no dice si vence a las once o a las siete—.
        var textoLimite = limite is DateTime f
            ? $" Entrega esperada: {f.ToLocalTime():dd/MM/yyyy HH:mm} ({horas:0.##} h)."
            : "";
        var textoEstimacion = estimacion is decimal e
            ? $" Dijiste que te tomaría {e:0.##} h; eso es lo que se comparará con tu cronómetro."
            : "";

        // Tomar un BUG es el momento en que aparece su esfuerzo, así que es también el momento de
        // mandarlo: hasta aquí no había número que llevar a DevOps. En una tarea o un requerimiento
        // el esfuerzo ya se empujó al publicarla y esto no encuentra nada pendiente.
        return (true, ConEmpuje(
            $"La actividad es tuya: {actividad.Points} puntos al aceptarse.{textoLimite}" +
            $"{textoEstimacion} Completa el checklist para poder entregarla.",
            await EmpujarADevOpsAsync(id, ct)));
    }

    /// <summary>
    /// El desarrollador la devuelve al pool. No se castiga: penalizarlo haría que nadie se atreviera
    /// con lo difícil, que es justo lo contrario de lo que se busca. Lo que sí queda es la cuenta
    /// (<see cref="PoolActivity.ReturnedCount"/>) y el motivo en el historial.
    /// </summary>
    public async Task<(bool ok, string mensaje)> DevolverAsync(
        int id, int developerId, string? motivo, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, developerId);

        var actividad = await db.PoolActivities.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");

        if (actividad.ClaimedByDeveloperId != developerId)
            return (false, "Esa actividad no es tuya.");
        if (!actividad.EnCurso)
            return (false, actividad.Status == PoolActivityStatus.EnRevision
                ? "Ya la entregaste: espera la revisión del líder."
                : "Esa actividad ya no está en curso.");

        motivo = Limpiar(motivo);
        AnotarEnHistorial(actividad, $"Devuelta al pool por {NombreDelUsuario()}" +
                                     (motivo != null ? $": {motivo}" : "") + ".");

        // El identificador del cronómetro se guarda ANTES de soltar el reclamo, que es quien lo
        // borra de la actividad. El cierre en sí va al final: cerrarlo antes metía un SaveChanges a
        // medio camino que cometía el ReturnedCount++ sin el resto del reset, y si el guardado final
        // fallaba, el reintento volvía a incrementarlo.
        int devActivityId = actividad.LinkedDevActivityId ?? 0;

        actividad.ReturnedCount++;
        SoltarReclamo(actividad);
        await BorrarChecklistAsync(actividad.Id, ct);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "PoolActivity", actividad.Id.ToString(),
            $"Devuelta al pool (van {actividad.ReturnedCount})", ct);

        await CerrarActividadEnlazadaAsync(devActivityId, ct);
        return (true, "Actividad devuelta al pool. Cualquiera puede tomarla.");
    }

    /// <summary>
    /// Marca o desmarca un punto del checklist. Los que exigen evidencia no se pueden marcar sin un
    /// enlace válido: sin eso, marcar una casilla no cuesta nada y la verificación se queda sin
    /// nada que mirar.
    /// </summary>
    public async Task<(bool ok, string mensaje)> MarcarItemAsync(
        int itemId, int developerId, bool hecho, string? evidencia, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, developerId);

        var item = await db.PoolActivityChecklistItems.FirstOrDefaultAsync(c => c.Id == itemId, ct);
        if (item == null) return (false, "Ese punto ya no existe. Actualiza la lista.");

        var actividad = await db.PoolActivities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == item.PoolActivityId, ct);
        if (actividad == null) return (false, "La actividad ya no existe.");
        if (actividad.ClaimedByDeveloperId != developerId)
            return (false, "Esa actividad no es tuya.");
        if (!actividad.EnCurso)
            return (false, actividad.Status == PoolActivityStatus.EnRevision
                ? "Ya la entregaste: espera la revisión del líder."
                : "Esa actividad ya no está en curso.");

        var (enlaceOk, enlaceError, enlace) = PerformanceScoringService.NormalizarEnlace(evidencia);
        if (!enlaceOk) return (false, enlaceError);

        if (hecho && item.RequiereEvidencia && enlace == null)
            return (false, $"«{item.Text}» necesita un enlace que lo respalde (el PR, el work item o el ticket).");

        item.IsDone      = hecho;
        item.DoneAtUtc   = hecho ? DateTime.UtcNow : null;
        item.EvidenceUrl = enlace;

        await db.SaveChangesAsync(ct);

        return (true, hecho ? "Punto marcado." : "Punto desmarcado.");
    }

    /// <summary>
    /// El desarrollador entrega la actividad. El checklist completo es la condición: es lo que
    /// convierte «ya está» en algo comprobable, y lo que hace que la verificación del líder sea un
    /// vistazo a la evidencia y no una discusión sobre si el trabajo alcanza.
    /// </summary>
    public async Task<(bool ok, string mensaje)> EntregarAsync(
        int id, int developerId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, developerId);

        var actividad = await db.PoolActivities.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");

        if (actividad.ClaimedByDeveloperId != developerId)
            return (false, "Esa actividad no es tuya.");
        if (!actividad.EnCurso)
            return (false, actividad.Status == PoolActivityStatus.EnRevision
                ? "Ya está entregada, esperando revisión."
                : "Esa actividad ya no está en curso.");

        var checklist = await ChecklistDeAsync(actividad.Id, ct);
        if (checklist.Count == 0)
            return (false, "Esta actividad se quedó sin checklist: devuélvela al pool y vuelve a tomarla " +
                           "para que se te copie el que corresponde. Sin checklist no hay nada que verificar.");

        var faltantes = checklist.Where(c => !c.IsDone).ToList();
        if (faltantes.Count > 0)
            return (false, $"Te faltan {faltantes.Count} punto(s) del checklist: " +
                           string.Join("; ", faltantes.Take(3).Select(c => c.Text)) +
                           (faltantes.Count > 3 ? "…" : "") + ".");

        var sinEvidencia = checklist
            .Where(c => c.RequiereEvidencia && string.IsNullOrWhiteSpace(c.EvidenceUrl))
            .ToList();
        if (sinEvidencia.Count > 0)
            return (false, "Falta el enlace de evidencia en: " +
                           string.Join("; ", sinEvidencia.Select(c => c.Text)) + ".");

        actividad.Status      = PoolActivityStatus.EnRevision;
        actividad.DeliveredAt = DateTime.UtcNow;
        AnotarEnHistorial(actividad, $"Entregada por {NombreDelUsuario()} (vuelta {actividad.ReviewRound + 1}).");
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "PoolActivity", actividad.Id.ToString(),
            $"Entregada para verificación (vuelta {actividad.ReviewRound + 1})", ct);
        await AvisarALosLideresAsync(actividad, ct);

        return (true, "Entregada. El líder la va a verificar y entonces se te abonan los puntos.");
    }

    // ── Verificación del líder ───────────────────────────────────────────────────

    /// <summary>
    /// El líder acepta la actividad y se generan los puntos.
    ///
    /// Los puntos son los CONGELADOS en la actividad, no los de la matriz de hoy: quien la tomó lo
    /// hizo sabiendo cuánto valía. La entrada nace ya APROBADA porque la verificación es esto mismo
    /// —volver a mandarla a una cola de aprobación sería revisar dos veces lo mismo— y se imputa al
    /// mes en que se acepta, que es cuando el trabajo quedó reconocido.
    ///
    /// Solo abona los puntos UNA VEZ, y eso lo garantiza la base, no una comprobación previa: el
    /// paso a «Aceptada» se hace con un UPDATE condicional dentro de una transacción que cubre
    /// también la entrada de puntos. Comprobar-y-después-escribir no bastaba: dos líderes revisando
    /// la cola a la vez leerían los dos «EnRevisión», pasarían los dos la comprobación y abonarían
    /// los puntos dos veces. Aquí, quien pierda la carrera no afecta ninguna fila y se va con un
    /// mensaje, no con un segundo abono.
    /// </summary>
    public async Task<(bool ok, string mensaje)> AceptarAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var actividad = await db.PoolActivities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");

        if (actividad.Status == PoolActivityStatus.Aceptada || actividad.PointEntryId != null)
            return (false, "Esa actividad ya estaba aceptada; sus puntos ya se abonaron.");
        if (actividad.Status != PoolActivityStatus.EnRevision)
            return (false, "Solo se aceptan actividades entregadas y pendientes de verificar.");
        if (actividad.ClaimedByDeveloperId is not int developerId)
            return (false, "Esa actividad no tiene dueño: no hay a quién abonarle los puntos.");

        var criterio = await CriterioDeAsync(actividad.WorkType, ct);
        if (criterio == null)
            return (false, "Falta el criterio del pool en el catálogo. Reinicia la aplicación para que se siembre.");

        var extras = await db.PoolActivityExtraCriteria.AsNoTracking()
            .Where(c => c.PoolActivityId == id)
            .Select(c => new { c.Name, c.Points, c.IsMet })
            .ToListAsync(ct);

        // Aceptar con criterios sin evaluar se BLOQUEA en vez de contarlos como no cumplidos.
        // Contarlos en silencio sería la peor de las dos opciones: los puntos se pierden sin que
        // nadie lo decida, y como después de aceptar ya no se pueden tocar, el error no tiene
        // arreglo. Aquí la única salida es que el líder diga sí o no a cada uno, que es justo para
        // lo que se anunciaron al publicar.
        var sinEvaluar = extras.Where(c => c.IsMet is null).ToList();
        if (sinEvaluar.Count > 0)
            return (false, $"Falta evaluar {sinEvaluar.Count} criterio(s) extra antes de aceptar: " +
                           $"{string.Join(", ", sinEvaluar.Select(c => $"«{c.Name}»"))}. " +
                           "Después de aceptar ya no se pueden cambiar.");

        var cumplidos  = extras.Where(c => c.IsMet == true).ToList();
        var puntosExtra = cumplidos.Sum(c => c.Points);
        var puntosTotal = actividad.Points + puntosExtra;

        var desglose = cumplidos.Count > 0
            ? $" (+{actividad.Points} base, +{puntosExtra} por {string.Join(", ", cumplidos.Select(c => c.Name))})"
            : "";

        var ahora = DateTime.Now;   // local: el período se imputa al mes del calendario de la gente
        var revisadoUtc = DateTime.UtcNow;
        var entrada = new PointEntry
        {
            DeveloperId      = developerId,
            CriterionId      = criterio.Id,
            // Una sola entrada con el total, y no una por criterio. Así el abono es atómico —o se
            // otorga todo o nada— y PoolActivity.PointEntryId, que es la guarda contra el doble
            // abono, sigue apuntando a una única fila. El desglose no se pierde: vive en el
            // comentario y, con fecha y comentario propios, en PoolActivityExtraCriteria.
            Points           = puntosTotal,
            Year             = ahora.Year,
            Month            = ahora.Month,
            Comment          = $"Pool #{actividad.Id}: {actividad.Title}{desglose}",
            AssignedByUserId = currentUser.UserId,
            ReviewedByUserId = currentUser.UserId,
            ReviewedAt       = revisadoUtc,
            ApprovalStatus   = PointApprovalStatus.Aprobado,
            Date             = revisadoUtc,
            EvidenceUrl      = await PrimeraEvidenciaAsync(actividad.Id, ct) ?? actividad.ExternalUrl
        };

        var historial = HistorialCon(actividad, $"Aceptada por {NombreDelUsuario()}: +{puntosTotal} pts{desglose}.");
        int revisor = currentUser.UserId ?? 0;

        using var tx = await db.Database.BeginTransactionAsync(ct);

        // El UPDATE condicional ES la guarda: si otra sesión ya la aceptó, esto afecta 0 filas.
        int ganadas = await db.PoolActivities
            .Where(a => a.Id == id
                     && a.Status == PoolActivityStatus.EnRevision
                     && a.PointEntryId == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, PoolActivityStatus.Aceptada)
                .SetProperty(a => a.ReviewedByUserId, revisor)
                .SetProperty(a => a.ReviewedAt, revisadoUtc)
                .SetProperty(a => a.ReviewComment, (string?)null)
                .SetProperty(a => a.ReviewHistory, historial), ct);
        if (ganadas == 0)
        {
            await tx.RollbackAsync(ct);
            return (false, "Esa actividad ya estaba aceptada; sus puntos ya se abonaron.");
        }

        db.PointEntries.Add(entrada);
        await db.SaveChangesAsync(ct);

        // La traza a la entrada va en la misma transacción: si algo falla, no queda una actividad
        // aceptada sin sus puntos ni unos puntos sin actividad que los justifique. Si algo revienta
        // antes del commit, el «using» deshace la transacción al salir.
        await db.PoolActivities.Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PointEntryId, entrada.Id), ct);

        await tx.CommitAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "PoolActivity", id.ToString(),
            $"Aceptada: +{puntosTotal} pts{desglose} a {await NombreDeDesarrolladorAsync(developerId, ct)}", ct);

        try
        {
            await notifications.NotifyDeveloperAsync(developerId, NotificationKind.General,
                "Tu actividad del pool fue aceptada",
                $"«{actividad.Title}»: +{puntosTotal} puntos{desglose}, ya cuentan en el ranking de {ahora:MMMM}.",
                dedupeKey: $"pool-aceptada-{id}", ct: ct);
        }
        catch { /* los puntos ya están abonados: un aviso fallido no puede tumbar la operación */ }

        // Con el Id de la ACTIVIDAD LIBRE, no con el de la del pool. Son dos tablas distintas y sus
        // Id no tienen nada que ver: pasar el equivocado cerraba la actividad libre de otra persona
        // —la que por casualidad tuviera ese número— y dejaba abierto el cronómetro de esta.
        await CerrarActividadEnlazadaAsync(actividad.LinkedDevActivityId ?? 0, ct);

        return (true, $"Aceptada. Se abonaron {puntosTotal} puntos a " +
                      $"{await NombreDeDesarrolladorAsync(developerId, ct)} en {ahora:MM/yyyy}.");
    }

    /// <summary>
    /// El líder la devuelve al desarrollador con un motivo. El motivo es obligatorio: sin él, quien
    /// la recibe no sabe qué arreglar y la va a volver a entregar igual.
    ///
    /// La actividad NO vuelve al pool: sigue siendo de quien la tomó, que corrige y la entrega otra
    /// vez. Su checklist se conserva —lo hecho está hecho—; lo que se marca es la vuelta.
    /// </summary>
    public async Task<(bool ok, string mensaje)> RechazarAsync(
        int id, string motivo, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        motivo = (motivo ?? "").Trim();
        if (motivo.Length == 0) return (false, "Escribe qué falta: es lo que la persona va a leer para corregirlo.");
        if (motivo.Length > MaxMotivo) return (false, $"El motivo no puede pasar de {MaxMotivo} caracteres.");

        var actividad = await db.PoolActivities.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");

        if (actividad.Status != PoolActivityStatus.EnRevision)
            return (false, actividad.Status == PoolActivityStatus.Aceptada
                ? "Esa actividad ya fue aceptada."
                : "Solo se devuelven actividades entregadas y pendientes de verificar.");

        actividad.Status           = PoolActivityStatus.Devuelta;
        actividad.ReviewComment    = motivo;
        actividad.ReviewedByUserId = currentUser.UserId;
        actividad.ReviewedAt       = DateTime.UtcNow;
        actividad.ReviewRound++;
        AnotarEnHistorial(actividad, $"Devuelta por {NombreDelUsuario()}: {motivo}");
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "PoolActivity", actividad.Id.ToString(),
            $"Devuelta para corregir (vuelta {actividad.ReviewRound}): {motivo}", ct);

        if (actividad.ClaimedByDeveloperId is int dev)
            await notifications.NotifyDeveloperAsync(dev, NotificationKind.General,
                "Te devolvieron una actividad del pool",
                $"«{actividad.Title}»: {motivo}",
                dedupeKey: $"pool-devuelta-{actividad.Id}-{actividad.ReviewRound}", ct: ct);

        return (true, "Devuelta al desarrollador con tu comentario.");
    }

    // ── Matriz y plantillas ──────────────────────────────────────────────────────

    public Task<List<PoolPointsMatrixEntry>> ObtenerMatrizAsync(CancellationToken ct = default) =>
        db.PoolPointsMatrix.AsNoTracking()
            .OrderBy(m => m.WorkType).ThenBy(m => m.Complexity)
            .ToListAsync(ct);

    /// <summary>
    /// Guarda la matriz completa. Cambiarla NO revalúa nada de lo ya creado: las actividades llevan
    /// sus puntos congelados. Solo afecta a las que se creen a partir de ahora.
    /// </summary>
    public async Task<(bool ok, string mensaje)> GuardarMatrizAsync(
        IReadOnlyList<PoolPointsMatrixEntry> filas, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        foreach (var f in filas)
        {
            if (f.Points <= 0)
                return (false, $"{PoolSeed.Etiqueta(f.WorkType)} / {PoolSeed.Etiqueta(f.Complexity)}: " +
                               "los puntos tienen que ser mayores que cero.");
            // El plazo de la matriz, en HORAS. Con tope por arriba, que antes no había: sin él una
            // celda podía guardar un número que después no cabe en la columna, y el error saltaba
            // al guardar y no al capturarlo, donde se puede corregir.
            if (f.HorasLimite < 0)
                return (false, $"{PoolSeed.Etiqueta(f.WorkType)} / {PoolSeed.Etiqueta(f.Complexity)}: " +
                               "las horas no pueden ser negativas. 0 = sin fecha límite.");
            if (f.HorasLimite > MaxHorasDePlazo)
                return (false, $"{PoolSeed.Etiqueta(f.WorkType)} / {PoolSeed.Etiqueta(f.Complexity)}: " +
                               $"el plazo no puede pasar de {MaxHorasDePlazo:0} horas.");
        }

        var actuales = await db.PoolPointsMatrix.ToListAsync(ct);
        int cambios = 0;
        foreach (var f in filas)
        {
            var horas = Math.Round(f.HorasLimite, 2, MidpointRounding.AwayFromZero);

            var fila = actuales.FirstOrDefault(m => m.WorkType == f.WorkType && m.Complexity == f.Complexity);
            if (fila == null)
            {
                // Sin DiasLimite: la web ya no lo escribe. Queda en 0, y en la práctica esta rama no
                // corre nunca contra una base con datos, porque las doce combinaciones ya existen.
                db.PoolPointsMatrix.Add(new PoolPointsMatrixEntry
                {
                    WorkType = f.WorkType, Complexity = f.Complexity,
                    Points = f.Points, HorasLimite = horas,
                    UpdatedAt = DateTime.UtcNow, UpdatedByUserId = currentUser.UserId
                });
                cambios++;
                continue;
            }

            if (fila.Points == f.Points && fila.HorasLimite == horas) continue;
            fila.Points          = f.Points;
            fila.HorasLimite     = horas;
            fila.UpdatedAt       = DateTime.UtcNow;
            fila.UpdatedByUserId = currentUser.UserId;
            cambios++;
        }

        if (cambios == 0) return (true, "No hubo cambios que guardar.");

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.ConfigChange, "PoolPointsMatrix", null,
            $"Matriz de puntos del pool actualizada ({cambios} celda(s))", ct);
        return (true, $"Matriz guardada ({cambios} celda(s)). Las actividades ya creadas conservan sus puntos.");
    }

    public Task<List<PoolChecklistTemplateItem>> PlantillaAsync(
        PoolWorkType tipo, bool incluirInactivos = false, CancellationToken ct = default)
    {
        var q = db.PoolChecklistTemplateItems.AsNoTracking().Where(t => t.WorkType == tipo);
        if (!incluirInactivos) q = q.Where(t => t.IsActive);
        return q.OrderBy(t => t.Orden).ThenBy(t => t.Id).ToListAsync(ct);
    }

    /// <summary>Alta o edición de un punto del checklist de un tipo. Id = 0 significa alta.</summary>
    public async Task<(bool ok, string mensaje)> GuardarPlantillaItemAsync(
        PoolChecklistTemplateItem item, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var texto = (item.Text ?? "").Trim();
        if (texto.Length == 0) return (false, "Escribe qué hay que cumplir.");
        if (texto.Length > 300) return (false, "El texto no puede pasar de 300 caracteres.");

        if (item.Id == 0)
        {
            int siguiente = await db.PoolChecklistTemplateItems
                .Where(t => t.WorkType == item.WorkType)
                .Select(t => (int?)t.Orden).MaxAsync(ct) ?? 0;

            db.PoolChecklistTemplateItems.Add(new PoolChecklistTemplateItem
            {
                WorkType = item.WorkType, Text = texto, Orden = siguiente + 10,
                RequiereEvidencia = item.RequiereEvidencia, IsActive = true
            });
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(AuditAction.Create, "PoolChecklistTemplate", null,
                $"Punto de checklist ({PoolSeed.Etiqueta(item.WorkType)}): {texto}", ct);
            return (true, "Punto agregado. Aplica a las actividades que se tomen a partir de ahora.");
        }

        var existente = await db.PoolChecklistTemplateItems.FirstOrDefaultAsync(t => t.Id == item.Id, ct);
        if (existente == null) return (false, "Ese punto ya no existe. Actualiza la lista.");

        existente.Text              = texto;
        existente.RequiereEvidencia = item.RequiereEvidencia;
        existente.Orden             = item.Orden;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "PoolChecklistTemplate", existente.Id.ToString(),
            $"Punto de checklist actualizado: {texto}", ct);
        return (true, "Punto actualizado. Las actividades ya tomadas conservan el checklist que recibieron.");
    }

    /// <summary>
    /// Desactiva un punto en lugar de borrarlo: las actividades en curso llevan su propia copia,
    /// pero borrarlo haría perder qué se pedía cuando se hicieron las anteriores.
    /// </summary>
    public async Task<(bool ok, string mensaje)> DesactivarPlantillaItemAsync(
        int itemId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var item = await db.PoolChecklistTemplateItems.FirstOrDefaultAsync(t => t.Id == itemId, ct);
        if (item == null) return (false, "Ese punto ya no existe. Actualiza la lista.");

        item.IsActive = !item.IsActive;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "PoolChecklistTemplate", item.Id.ToString(),
            item.IsActive ? $"Punto reactivado: {item.Text}" : $"Punto desactivado: {item.Text}", ct);
        return (true, item.IsActive ? "Punto reactivado." : "Punto desactivado. Deja de pedirse en las actividades nuevas.");
    }

    // ── Interno ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Valida el borrador y resuelve de paso el vínculo con DevOps.
    ///
    /// El work item sale de aquí y no del formulario tal cual porque hay dos formas de decirlo —el
    /// número o la dirección pegada— y porque un número que contradice a su enlace tiene que
    /// rechazarse ANTES de guardar nada: guardado, escribiría el esfuerzo en el ticket de otro.
    /// </summary>
    private async Task<(bool ok, string error, PoolPointsMatrixEntry? celda, string? enlace, int? workItem)>
        ValidarBorradorAsync(PoolActivity b, int actividadId, CancellationToken ct)
    {
        var titulo = (b.Title ?? "").Trim();
        if (titulo.Length == 0) return (false, "Escribe un título para la actividad.", null, null, null);
        if (titulo.Length > 200) return (false, "El título no puede pasar de 200 caracteres.", null, null, null);

        var celda = await CeldaDeMatrizAsync(b.WorkType, b.Complexity, ct);
        if (celda == null)
            return (false, $"No hay puntos configurados para {PoolSeed.Etiqueta(b.WorkType)} / " +
                           $"{PoolSeed.Etiqueta(b.Complexity)}. Captúralos en la pestaña de configuración.", null, null, null);
        if (celda.Points <= 0)
            return (false, $"{PoolSeed.Etiqueta(b.WorkType)} / {PoolSeed.Etiqueta(b.Complexity)} vale " +
                           "0 puntos: corrige la matriz antes de publicar.", null, null, null);

        // ── PLAZO ────────────────────────────────────────────────────────────────
        //
        // El plazo SÍ se ajusta por actividad; los puntos no. La asimetría es deliberada: aflojar el
        // plazo no vale puntos, y el plazo real depende del trabajo concreto —un bug medio con un
        // cliente esperando no admite las mismas horas que uno cualquiera—.
        //
        // En un BUG el plazo es OBLIGATORIO y lo pone el líder: es el trato del modelo. Si se dejara
        // caer a la matriz cuando viene vacío, «cuando sea Bug, el plazo se lo pongo yo» dejaría de
        // ser cierto sin que nadie lo notara.
        if (b.WorkType == PoolWorkType.Bug && b.HorasLimite is null)
            return (false, "Un bug lleva el plazo que tú decidas, en horas. Escríbelo: la matriz no lo " +
                           "pone por ti. 0 = sin fecha límite.", null, null, null);

        if (b.HorasLimite is { } plazo && (plazo < 0 || plazo > MaxHorasDePlazo))
            return (false, $"El plazo tiene que estar entre 0 y {MaxHorasDePlazo:0} horas. " +
                           "0 = sin fecha límite.", null, null, null);

        // ── ESFUERZO ─────────────────────────────────────────────────────────────
        //
        // En una TAREA o un REQUERIMIENTO lo estima el líder aquí, y es obligatorio: si fuera
        // opcional, el número del que depende toda la comparación con el cronómetro sería el primero
        // en saltarse el día que alguien tenga prisa.
        //
        // En un BUG se RECHAZA en vez de ignorarse. Si el líder pudiera precargarlo, a quien lo toma
        // no se le preguntaría nunca y el número dejaría de ser suyo — que es lo único que lo hace
        // comparable, porque se escribe antes de saber lo que costó.
        if (b.WorkType == PoolWorkType.Bug)
        {
            if (b.HorasEstimadas is not null)
                return (false, "El esfuerzo de un bug lo estima quien lo toma, en el momento de tomarlo. " +
                               "Tú pones el plazo.", null, null, null);
        }
        else if (b.HorasEstimadas is not { } esfuerzo || esfuerzo < MinHorasEstimadas || esfuerzo > MaxHorasEstimadas)
        {
            return (false, $"Escribe el esfuerzo estimado, entre {MinHorasEstimadas} y {MaxHorasEstimadas:0} " +
                           "horas. Sin ese número no hay nada que contrastar con el cronómetro.", null, null, null);
        }

        // El mismo validador que la autocalificación: solo http/https, porque el líder abre el
        // enlace con el navegador al verificar.
        var (enlaceOk, enlaceError, enlace) = PerformanceScoringService.NormalizarEnlace(b.ExternalUrl);
        if (!enlaceOk) return (false, enlaceError, null, null, null);

        // ── VÍNCULO CON DEVOPS ───────────────────────────────────────────────────
        //
        // El número puede venir escrito o dentro del enlace; que los dos se contradigan se rechaza,
        // porque guardado escribiría el esfuerzo y la prioridad en el ticket de otra persona.
        var (vinculoOk, vinculoError, workItem) =
            PoolDevOpsService.ResolverWorkItem(b.DevOpsWorkItemId, enlace);
        if (!vinculoOk) return (false, vinculoError, null, null, null);

        // Y que no haya OTRA actividad viva sobre el mismo work item: se pisarían el esfuerzo y la
        // prioridad la una a la otra sin que ninguna se enterara. Se comprueba aquí además de en
        // «ligar» porque publicar con el número puesto es el camino corriente y saltarse la
        // comprobación por él la volvería decorativa.
        if (workItem is int numero)
        {
            var (libre, ocupado) = await PoolDevOpsService.NadieMasLoTieneAsync(db, numero, actividadId, ct);
            if (!libre) return (false, ocupado, null, null, null);
        }

        return (true, "", celda, enlace, workItem);
    }

    /// <summary>
    /// Copia del catálogo los criterios extra elegidos, con su nombre y sus puntos CONGELADOS.
    ///
    /// <para>Se congelan por lo mismo que los puntos base: quien toma una actividad viendo «+5 por
    /// pruebas automatizadas» tiene que cobrar 5, aunque el líder baje ese criterio a 2 mientras la
    /// trabaja. Referenciar el catálogo en vivo permitiría revaluar hacia atrás.</para>
    ///
    /// <para>Se ignoran en silencio los identificadores que no existan o estén inactivos, en vez de
    /// fallar: la lista viene de una pantalla que pudo cargarse antes de que el líder depurara el
    /// catálogo, y hacer fallar la publicación entera por eso sería desproporcionado. Lo que NO se
    /// ignora es un criterio de equipo colado aquí — ver abajo.</para>
    /// </summary>
    private async Task<(bool ok, string error, List<PoolActivityExtraCriterion> criterios)>
        MaterializarCriteriosExtraAsync(IReadOnlyList<int>? ids, CancellationToken ct)
    {
        if (ids is null || ids.Count == 0) return (true, "", []);

        var unicos = ids.Distinct().ToList();

        var delCatalogo = await db.ScoringCriteria.AsNoTracking()
            .Where(c => unicos.Contains(c.Id) && c.IsActive)
            .Select(c => new { c.Id, c.Name, c.DefaultPoints, c.Scope })
            .ToListAsync(ct);

        // Un criterio de EQUIPO no puede sumar aquí: una actividad del pool la cobra una sola
        // persona, y colarlo convertiría un reconocimiento colectivo en puntos individuales. Esto sí
        // se rechaza en voz alta en vez de ignorarse, porque es un error de concepto y no un desfase.
        var deEquipo = delCatalogo.Where(c => c.Scope == CriterionScope.Equipo).ToList();
        if (deEquipo.Count > 0)
            return (false, $"«{deEquipo[0].Name}» es un criterio de equipo y no puede sumar en una " +
                           "actividad del pool, que la cobra una sola persona.", []);

        return (true, "", [.. delCatalogo.Select(c => new PoolActivityExtraCriterion
        {
            ScoringCriterionId = c.Id,
            Name               = c.Name,
            Points             = c.DefaultPoints
        })]);
    }

    private Task<PoolPointsMatrixEntry?> CeldaDeMatrizAsync(
        PoolWorkType tipo, PoolComplexity complejidad, CancellationToken ct) =>
        db.PoolPointsMatrix.AsNoTracking()
            .FirstOrDefaultAsync(m => m.WorkType == tipo && m.Complexity == complejidad, ct);

    private Task<ScoringCriterion?> CriterioDeAsync(PoolWorkType tipo, CancellationToken ct)
    {
        var nombre = PoolSeed.NombreCriterio(tipo);
        return db.ScoringCriteria.AsNoTracking().FirstOrDefaultAsync(c => c.Name == nombre, ct);
    }

    /// <summary>
    /// Cuántas actividades puede tener tomadas a la vez quien las toma. El valor vive en la
    /// configuración para que el líder lo ajuste sin recompilar.
    /// </summary>
    private async Task<int> TopeDeTomadasAsync(CancellationToken ct)
    {
        int n = await configuracion.ObtenerEnteroAsync(ClaveMaxTomadas, MaxTomadasPorOmision, ct);
        return n > 0 ? n : MaxTomadasPorOmision;
    }

    /// <summary>Copia el checklist vigente del tipo a la actividad (ver el porqué en el modelo).</summary>
    private async Task CopiarChecklistAsync(PoolActivity actividad, CancellationToken ct)
    {
        await BorrarChecklistAsync(actividad.Id, ct);   // por si vuelve a tomarse tras una devolución

        foreach (var plantilla in await PlantillaAsync(actividad.WorkType, ct: ct))
            db.PoolActivityChecklistItems.Add(new PoolActivityChecklistItem
            {
                PoolActivityId    = actividad.Id,
                Text              = plantilla.Text,
                Orden             = plantilla.Orden,
                RequiereEvidencia = plantilla.RequiereEvidencia
            });
    }

    /// <summary>Marca el checklist para borrado; se comete con el resto de la operación.</summary>
    private async Task BorrarChecklistAsync(int poolActivityId, CancellationToken ct)
    {
        var items = await db.PoolActivityChecklistItems.Where(c => c.PoolActivityId == poolActivityId).ToListAsync(ct);
        if (items.Count > 0) db.PoolActivityChecklistItems.RemoveRange(items);
    }

    /// <summary>
    /// Prepara la actividad libre enlazada con la que se cronometra el trabajo. Es lo que permite
    /// usar el cronómetro que ya existe sin añadirle un tercer tipo de objetivo (ver el modelo).
    ///
    /// Solo la deja pendiente en el contexto: se inserta con el mismo SaveChanges que comete el
    /// checklist, de modo que tomar la actividad sea una sola unidad de trabajo.
    /// </summary>
    private DevActivity CrearActividadEnlazada(PoolActivity actividad, int developerId)
    {
        var libre = new DevActivity
        {
            DeveloperId = developerId,
            Title = $"Pool #{actividad.Id}: {Recortar(actividad.Title, 180)}",
            Description = "Creada automáticamente al tomar la actividad del pool, para poder " +
                          "cronometrar el trabajo. Se cierra cuando la actividad se acepta.",
            Status = DevActivityStatus.Abierta,
            CreatedAt = DateTime.UtcNow
        };
        db.DevActivities.Add(libre);
        return libre;
    }

    /// <summary>
    /// Cierra la actividad libre del cronómetro. Se llama SIEMPRE al FINAL, con el estado de la
    /// actividad del pool ya guardado, y por dos motivos: hacerlo antes metía un SaveChanges a media
    /// operación que cometía cambios sin terminar, y como un fallo aquí se traga —cerrar el
    /// cronómetro es cortesía; lo importante, los puntos o el reset del reclamo, ya está guardado—,
    /// la fila a medio modificar se queda pendiente en el contexto y no debe quedar nada detrás que
    /// la cometa por accidente.
    /// </summary>
    private async Task CerrarActividadEnlazadaAsync(int devActivityId, CancellationToken ct)
    {
        if (devActivityId <= 0) return;

        try
        {
            var libre = await db.DevActivities.FirstOrDefaultAsync(a => a.Id == devActivityId, ct);
            if (libre == null || libre.Status == DevActivityStatus.Cerrada) return;
            libre.Status   = DevActivityStatus.Cerrada;
            libre.ClosedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch { /* ver el resumen: es cortesía y va al final, así que no arrastra nada */ }
    }

    /// <summary>
    /// Devuelve la actividad al pool y la deja como estaba antes de que nadie la tomara. Pasan por
    /// aquí los dos caminos que sueltan un reclamo: <see cref="DevolverAsync"/> (lo suelta quien la
    /// tenía) y <see cref="LiberarAsync"/> (se lo quita el líder), y por eso la limpieza se escribe
    /// una sola vez.
    /// </summary>
    private void SoltarReclamo(PoolActivity actividad)
    {
        actividad.Status               = PoolActivityStatus.Disponible;
        actividad.ClaimedByDeveloperId = null;
        actividad.ClaimedAt            = null;
        actividad.ClaimDeadlineAt      = null;
        actividad.DeliveredAt          = null;
        actividad.ReviewComment        = null;
        actividad.LinkedDevActivityId  = null;

        // La estimación de un BUG es de quien lo tenía tomado, así que se va con él. Dejarla puesta
        // haría dos daños: quien lo tome después heredaría el número de otro, y la comprobación de
        // «un bug no se toma sin estimarlo» quedaría satisfecha por algo que esa persona no escribió.
        //
        // En tarea y requerimiento NO se toca: ahí la estimación es del líder y sigue siendo válida
        // con la actividad de vuelta en el pool. Es la segunda de las dos invariantes que permiten
        // derivar el autor de HorasEstimadas del tipo (la otra está en EditarAsync).
        if (actividad.WorkType == PoolWorkType.Bug)
        {
            actividad.HorasEstimadas      = null;
            actividad.HorasEstimadasEnUtc = null;
        }
    }

    private Task<string?> PrimeraEvidenciaAsync(int poolActivityId, CancellationToken ct) =>
        db.PoolActivityChecklistItems.AsNoTracking()
            .Where(c => c.PoolActivityId == poolActivityId && c.EvidenceUrl != null)
            .OrderBy(c => c.Orden)
            .Select(c => c.EvidenceUrl)
            .FirstOrDefaultAsync(ct);

    private async Task AvisarALosLideresAsync(PoolActivity actividad, CancellationToken ct)
    {
        try
        {
            var lideres = await db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.Role == UserRole.Admin)
                .Select(u => u.Id).ToListAsync(ct);

            var deQuien = await NombreDeDesarrolladorAsync(actividad.ClaimedByDeveloperId, ct);
            foreach (var userId in lideres)
                await notifications.NotifyAsync(userId, NotificationKind.General,
                    "Hay una actividad del pool por verificar",
                    $"«{actividad.Title}» ({actividad.Points} pts) — la entregó {deQuien}.",
                    dedupeKey: $"pool-review-{actividad.Id}-{actividad.ReviewRound}", ct: ct);
        }
        catch { /* el aviso es cortesía; la entrega ya quedó registrada */ }
    }

    /// <summary>Tope del historial. Un ida y vuelta muy largo no debe crecer sin límite.</summary>
    private const int MaxHistorial = 8000;

    /// <summary>
    /// Agrega una línea fechada al historial sin borrar lo anterior. Mismo criterio que el de
    /// <see cref="PointEntry.ReviewHistory"/>: <see cref="PoolActivity.ReviewComment"/> guarda solo
    /// la última decisión, así que sin esto la segunda devolución borraría el motivo de la primera.
    /// </summary>
    public static void AnotarEnHistorial(PoolActivity actividad, string linea)
        => actividad.ReviewHistory = HistorialCon(actividad, linea);

    /// <summary>
    /// El historial que resultaría de anotar esa línea, sin tocar la entidad. Lo necesitan las
    /// transiciones que se escriben con un UPDATE condicional, donde no hay entidad que mutar.
    /// </summary>
    private static string HistorialCon(PoolActivity actividad, string linea)
    {
        var sello = $"[{DateTime.Now:dd/MM/yyyy HH:mm}] {linea.Trim()}";
        var historial = string.IsNullOrWhiteSpace(actividad.ReviewHistory)
            ? sello
            : actividad.ReviewHistory + "\n" + sello;

        // Se recorta por el PRINCIPIO: lo último que se dijo es lo que hace falta para decidir.
        return historial.Length > MaxHistorial ? "(…)\n" + historial[^MaxHistorial..] : historial;
    }

    private string NombreDelUsuario() =>
        currentUser.FullName ?? currentUser.Username ?? "el líder";

    private async Task<string> NombreDeDesarrolladorAsync(int? developerId, CancellationToken ct) =>
        developerId is int id
            ? await db.Developers.AsNoTracking().Where(d => d.Id == id)
                  .Select(d => d.FullName).FirstOrDefaultAsync(ct) ?? "(sin ficha)"
            : "(sin dueño)";

    /// <summary>
    /// Deja unas horas con dos decimales, que es lo que cabe en la columna <c>decimal(6,2)</c>.
    ///
    /// <para>El redondeo se hace AQUÍ, a la vista y con la regla escrita —el medio sube—, y no se le
    /// deja a la base: si lo hiciera la columna, el mismo 1.005 podría guardarse como 1.00 en un
    /// motor y como 1.01 en otro, y nadie sabría de dónde salió la diferencia. Además así la persona
    /// ve al releer exactamente el número que se guardó.</para>
    /// </summary>
    private static decimal? Redondear(decimal? horas) =>
        horas is decimal h ? Math.Round(h, 2, MidpointRounding.AwayFromZero) : null;

    private static string? Limpiar(string? texto)
    {
        texto = (texto ?? "").Trim();
        return texto.Length == 0 ? null : texto;
    }

    private static string Recortar(string texto, int tope) =>
        texto.Length <= tope ? texto : texto[..tope];
}
