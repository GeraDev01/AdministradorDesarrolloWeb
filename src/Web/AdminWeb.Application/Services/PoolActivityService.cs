using System.Linq.Expressions;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Equipos;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared;
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
    /// Tope del DETALLE de una propuesta: lo que admite la columna.
    ///
    /// <para>Aquí se RECHAZA en vez de recortar, al revés que en el alta automática desde un work
    /// item. Allá el texto viene de fuera y recortarlo es lo único que se puede hacer con él; aquí
    /// lo escribe una persona que está mirando la pantalla, y guardarle a medias lo que acaba de
    /// redactar sería perderle trabajo sin decírselo.</para>
    /// </summary>
    public const int MaxDetalle = 4000;

    /// <summary>
    /// Tope de la JUSTIFICACIÓN de un criterio extra: lo que admite la columna. Más largo que un
    /// motivo porque decir qué práctica se aplicó y dónde pide más espacio que decir si algo vale.
    /// </summary>
    public const int MaxJustificacion = 2000;

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

    // ── Quién ve qué ─────────────────────────────────────────────────────────────

    /// <summary>
    /// LOS EQUIPOS CUYAS ACTIVIDADES PUEDE VER quien tiene la sesión, o <c>null</c> si las ve todas.
    ///
    /// <para>Esta es LA regla de visibilidad del pool y vive en un solo sitio a propósito: la usan la
    /// lista de lo disponible y el UPDATE que gana el reclamo. Escrita dos veces, una de las dos
    /// acabaría diciendo otra cosa — y la que importa es la del UPDATE, porque la otra solo decide
    /// qué se dibuja.</para>
    ///
    /// <para><b>Devuelve nulo para administración</b>, que ve el pool entero: es quien publica, quien
    /// rescata lo atascado y quien reparte. Segmentarle la lista dejaría actividades sin nadie que
    /// pudiera sacarlas de donde estén, y además nadie puede segmentar algo que después no vería.</para>
    ///
    /// <para><b>Y lo que devuelve es el SUBÁRBOL hacia arriba</b>: los equipos desde cuya altura se ve
    /// lo mío. Si estoy en «Soporte», que cuelga de «Desarrollo Web», veo lo publicado a Soporte y lo
    /// publicado a Desarrollo Web — se publica al nivel al que se quiere que se vea. En un equipo sin
    /// subequipos eso es exactamente «solo mi equipo», que es lo pedido.</para>
    ///
    /// <para><b>Quien no tiene equipo se queda solo con lo no segmentado.</b> Es el estado en que
    /// queda alguien cuando se borra su equipo, y también el de una cuenta sin ficha. Lo contrario
    /// convertiría quedarse sin equipo en un privilegio.</para>
    /// </summary>
    private async Task<HashSet<int>?> EquiposQueVeoAsync(CancellationToken ct)
    {
        if (currentUser.Role == UserRole.Admin) return null;

        if (currentUser.DeveloperId is not int devId) return [];

        var miEquipo = await db.Developers.AsNoTracking()
            .Where(d => d.Id == devId).Select(d => d.TeamId).FirstOrDefaultAsync(ct);
        if (miEquipo is not int equipoId) return [];

        // Hacia ARRIBA: mi equipo y todos sus antepasados. Lo publicado a un antepasado alcanza a
        // toda su rama, y yo estoy dentro.
        var equipos = await db.Teams.AsNoTracking()
            .Select(t => new { t.Id, t.EquipoPadreId }).ToListAsync(ct);
        var jerarquia = JerarquiaDeEquipos.De(equipos.Select(t => (t.Id, t.EquipoPadreId)));

        return [equipoId, .. jerarquia.Ancestros(equipoId)];
    }

    /// <summary>
    /// El filtro de visibilidad como PREDICADO, para meterlo tal cual en un <c>Where</c>.
    ///
    /// <para>Nulo en <paramref name="equiposQueVeo"/> es «lo ve todo». Lo no segmentado lo ve
    /// cualquiera: es el pool de siempre y es lo que hay en todas las filas que ya existían.</para>
    /// </summary>
    private static Expression<Func<PoolActivity, bool>> Visibles(HashSet<int>? equiposQueVeo) =>
        equiposQueVeo is null
            ? _ => true
            : a => a.EquipoId == null || equiposQueVeo.Contains(a.EquipoId.Value);

    // ── Consultas ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lo que hay libre en el pool ahora mismo, de lo más valioso a lo menos.
    ///
    /// <para>Ya filtrado por lo que puede ver quien mira: lo no segmentado y lo publicado a su equipo
    /// o a alguno de sus antepasados. El filtro va en el SERVIDOR y no en la pantalla, que es lo
    /// único que esconde algo de verdad.</para>
    /// </summary>
    public async Task<List<PoolActivity>> DisponiblesAsync(
        PoolWorkType? tipo = null, PoolComplexity? complejidad = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        // Con los criterios extra: quien mira el pool tiene que poder ver qué se le va a pedir de
        // más ANTES de tomar la actividad. Sin el Include llegarían vacíos y en silencio.
        var q = db.PoolActivities.AsNoTracking()
            .Include(a => a.ExtraCriteria)
            .Where(a => a.Status == PoolActivityStatus.Disponible)
            .Where(Visibles(await EquiposQueVeoAsync(ct)));
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
        //
        // El orden ENTRE estados no es el del enumerado sino el de PoolSeed.OrdenDeEstado, y por eso
        // se ordena en memoria: «Por clasificar» tuvo que declararse con el valor 6 —los números
        // están escritos en una base en producción y renumerar convertiría cada actividad aceptada en
        // otra cosa—, así que ordenando por el número, lo ÚNICO que reclama una decisión del líder
        // saldría al fondo, detrás de todo lo que ya está muerto. La lista es de decenas de filas y
        // ya viene materializada; ordenarla aquí no cuesta nada.
        var filas = await q.ToListAsync(ct);

        return [.. filas
            .OrderBy(a => PoolSeed.OrdenDeEstado(a.Status))
            .ThenByDescending(a => a.Priority)
            .ThenByDescending(a => a.CreatedAt)];
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
        var esfuerzo = borrador.WorkType.ComoBug() ? null : Redondear(borrador.HorasEstimadas);

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
            EquipoId            = borrador.EquipoId,
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
    ///
    /// <para><b>Y es donde se CLASIFICA, con dos desenlaces según de dónde venga la actividad.</b>
    /// Una que entró sola desde un work item no tiene dueño y sale <c>Disponible</c>, al pool, para
    /// que la tome quien quiera. Una PROPUESTA sí lo tiene —nació con el reclamo de quien la
    /// propuso— y sale <c>Tomada</c>, directamente a sus manos.</para>
    ///
    /// <para>La diferencia no es una comodidad: mandar una propuesta al pool común dejaría que se la
    /// llevara otro, que es exactamente lo contrario de lo que pidió quien la propuso —y encima
    /// después de que el líder decidiera cuánto vale, o sea, sabiendo ya lo que paga—. El reclamo es
    /// el único dato que distingue los dos casos, y por eso una propuesta nace con él puesto.</para>
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

        // También se edita lo que todavía no se ha clasificado: para una actividad que entró sola
        // desde un work item, editarla ES clasificarla, y al final de este método pasa a Disponible.
        if (actividad.Status is not (PoolActivityStatus.Disponible or PoolActivityStatus.PorClasificar))
            return (false, "Ya la tomó alguien: no se puede cambiar lo que vale ni lo que pide.");

        bool clasificando = actividad.Status == PoolActivityStatus.PorClasificar;

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
            actividad.WorkType.ComoBug() != cambios.WorkType.ComoBug();
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

        // EL EQUIPO. Hasta ahora esta línea no existía: el contrato lo traía, el endpoint lo metía en
        // el borrador y aquí se descartaba en silencio, así que segmentar una actividad ya publicada
        // era imposible y nadie lo notaba porque toda actividad nacía con su equipo puesto.
        //
        // Y la regla de «ausente» es LA CONTRARIA que la del work item de abajo: allí, sin número, el
        // vínculo se deja como está, porque nulo significa «no me lo mandaron». Aquí NO se puede
        // hacer eso, porque en el equipo el nulo es un valor legítimo —«la ve toda la casa»— y es
        // además el único con el que se puede DESsegmentar. Si se interpretara como «no lo toques»,
        // una actividad publicada para un equipo no podría volver a abrirse a todos nunca.
        actividad.EquipoId    = cambios.EquipoId;

        // ── EL VÍNCULO, AL CAMBIAR DE TICKET ─────────────────────────────────────
        //
        // <b>Sin número, el vínculo se queda como está.</b> No se desliga, y eso es deliberado: este
        // campo es NUEVO en la petición, así que cualquier pantalla que edite una actividad sin
        // saber de él mandaría un nulo, y «ausente» tiene que significar «no lo toques» y no
        // «bórralo». Con la otra lectura, editar el título de una actividad ligada la desligaría en
        // silencio y el ticket dejaría de recibir nada sin que nadie lo hubiera pedido. Desligar es
        // destructivo y tiene su propia ruta, que además lo dice en su respuesta.
        //
        // Al apuntar a OTRO ticket se limpian TODAS las marcas de agua: cada una dice «DevOps ya
        // tiene esto» y esa afirmación es sobre un work item concreto. Conservar cualquiera haría que
        // la actividad se creyera al día en un ticket al que nunca se le mandó nada, y no volvería a
        // mandarse hasta la siguiente edición. Borrarlas la deja pendiente, y el empuje de abajo lo
        // resuelve dentro de la misma operación.
        //
        // LAS CUATRO, Y LA DE LA ASIGNACIÓN ES LA QUE MÁS IMPORTA. Es la única que sobrevive a soltar
        // el reclamo —SoltarReclamo la conserva a propósito, porque sin dueño no hay nada pendiente—,
        // así que es la única que puede llegar viva hasta aquí: una actividad que alguien tomó, se
        // devolvió al pool y el líder repunta a otro ticket. Sin este borrado, cuando la misma
        // persona la vuelva a tomar, «pendiente» compara su identificador contra el que confirmó el
        // ticket ANTERIOR, sale que no falta nada, y el work item nuevo se queda «en progreso» y SIN
        // DUEÑO: exactamente el estado que esta función existe para evitar, y encima en silencio,
        // porque la actividad no aparece en la lista de pendientes del líder.
        if (workItem is not null && actividad.DevOpsWorkItemId != workItem)
        {
            actividad.DevOpsWorkItemId            = workItem;
            actividad.DevOpsEsfuerzoEnviado       = null;
            actividad.DevOpsPrioridadEnviada      = null;
            actividad.DevOpsAsignadoADeveloperId  = null;
            actividad.DevOpsEstadoEnviado         = null;
            actividad.DevOpsUltimoError           = null;
        }

        // El esfuerzo del líder se reescribe solo cuando le toca ponerlo. En un bug la validación ya
        // garantizó que no viene ninguno, y lo que quede es lo que escribió quien lo tomó (o el nulo
        // que acaba de dejar la limpieza de arriba): pisarlo con null aquí borraría, en cada edición
        // de un bug, la estimación de otra persona.
        if (!cambios.WorkType.ComoBug())
        {
            actividad.HorasEstimadas      = Redondear(cambios.HorasEstimadas);
            actividad.HorasEstimadasEnUtc = DateTime.UtcNow;
        }

        // Los criterios extra se REEMPLAZAN por completo. Se puede porque aquí la actividad sigue
        // Disponible —nadie la ha tomado— así que nadie los ha visto todavía para decidir si la
        // tomaba; en cuanto alguien la toma, esta ruta ya está cerrada por la guarda de arriba.
        actividad.ExtraCriteria.Clear();
        foreach (var extra in extras) actividad.ExtraCriteria.Add(extra);

        // ── CLASIFICAR ES PUBLICAR… O ENTREGARLA A QUIEN LA PROPUSO ──────────────
        //
        // Una actividad que no vale puntos ni se puede tomar mientras no tenga tipo, complejidad y
        // horas; en cuanto los tiene —y la validación de arriba es la que garantiza que los tiene—
        // ya es una actividad del pool como cualquiera. No hace falta un segundo gesto ni una ruta
        // aparte: pasar por aquí ES la decisión, y separarlos solo daría ocasión de dejarla
        // clasificada pero sin publicar.
        //
        // A DÓNDE VA LO DECIDE EL RECLAMO, que es el único dato que separa las dos procedencias:
        //  · sin dueño → entró sola desde un work item → al pool, Disponible, para quien la quiera;
        //  · con dueño → es una propuesta → Tomada, a las manos de quien la propuso.
        //
        // Y «Tomada» tiene que significar lo mismo por los dos caminos, o el segundo entregaría algo
        // a medio armar: se le calcula el PLAZO desde ahora, se le copia el CHECKLIST vigente y se le
        // crea la PERCHA del cronómetro, que es lo mismo que hace tomar y por eso está extraído.
        //
        // EL PLAZO CUENTA DESDE AQUÍ y no desde que se propuso, por lo mismo que al tomar: hasta que
        // el líder no dice de qué clase es, no hay plazo que consumir — no se sabía ni de cuántas
        // horas era.
        //
        // LO QUE ESTE CAMINO NO PUEDE DAR, dicho aquí para que no parezca un olvido: si la propuesta
        // se clasifica como BUG o RETRABAJO, se queda SIN ESFUERZO ESTIMADO. En un bug lo escribe
        // quien lo toma en el momento de tomarlo, y aquí ese momento no existe: cuando el líder
        // clasifica, la actividad ya es suya. Preguntárselo después sería preguntarle cuando ya sabe
        // lo que le costó, que es justo lo que ese número no puede ser. Se acepta a sabiendas: esos
        // bugs se comparan contra el cronómetro con la mitad de los datos, y el mensaje lo dice.
        bool aSuDuenno = clasificando && actividad.ClaimedByDeveloperId is not null;

        if (clasificando)
        {
            if (aSuDuenno)
            {
                var ahora = DateTime.UtcNow;
                var (_, limite) = PlazoDesdeAhora(actividad, celda, ahora);

                actividad.Status          = PoolActivityStatus.Tomada;
                actividad.ClaimedAt       = ahora;
                actividad.ClaimDeadlineAt = limite;

                AnotarEnHistorial(actividad,
                    $"Clasificada por {NombreDelUsuario()} como {PoolSeed.Etiqueta(actividad.WorkType)} / " +
                    $"{PoolSeed.Etiqueta(actividad.Complexity)}: {actividad.Points} pts. Queda tomada por " +
                    "quien la propuso.");
            }
            else actividad.Status = PoolActivityStatus.Disponible;
        }

        await db.SaveChangesAsync(ct);

        // El checklist y la percha van DESPUÉS del guardado que fijó el estado, no antes: los dos
        // cuelgan del identificador de la actividad y el segundo escribe una fila en otra tabla.
        // Puestos antes, una validación que fallara más arriba dejaría un cronómetro huérfano.
        if (aSuDuenno && actividad.ClaimedByDeveloperId is int duenno)
            await ArrancarElTrabajoAsync(actividad, duenno, ct);
        await audit.RecordAsync(AuditAction.Update, "PoolActivity", actividad.Id.ToString(),
            clasificando
                ? $"Clasificada como {PoolSeed.Etiqueta(actividad.WorkType)} / " +
                  $"{PoolSeed.Etiqueta(actividad.Complexity)}, {actividad.Points} pts; " +
                  (aSuDuenno ? "queda tomada por quien la propuso" : "publicada en el pool")
                : $"Actividad del pool actualizada: {actividad.Points} pts, prioridad " +
                  $"{EtiquetasDeCatalogo.PrioridadDelPool(actividad.Priority)}, " +
                  $"{extras.Count} criterio(s) extra", ct);

        // A quien la propuso se le avisa: para él, esto es que su propuesta fue aceptada Y que ya
        // tiene trabajo asignado con plazo corriendo. Enterarse al entrar a la pantalla, cuando el
        // plazo lleva dos días consumiéndose, sería la peor forma de descubrirlo.
        if (aSuDuenno && actividad.ClaimedByDeveloperId is int avisado)
            await AvisarDeLaClasificacionAsync(actividad, avisado, ct);

        // El empuje va aquí y no solo al ligar porque la prioridad y el esfuerzo se editan: mandar
        // solo la primera vez dejaría DevOps con el número del día que se publicó, que es peor que
        // no mandar nada — parecería al día y no lo estaría.
        // El empuje se hace igual en los dos casos, pero al CLASIFICAR es la primera vez que sale
        // algo hacia el work item: hasta este momento la actividad no tenía nada que afirmar y sus
        // pendientes estaban cerrados a propósito (ver PoolActivity.YaPublicada).
        var mensaje = clasificando
            ? MensajeDeAlta(actividad.Points, extras)
            : MensajeDeAlta(actividad.Points, extras).Replace("publicada", "actualizada");

        if (aSuDuenno)
        {
            mensaje = mensaje.Replace("publicada en el pool", "clasificada")
                    + $" Queda tomada por {await NombreDeDesarrolladorAsync(actividad.ClaimedByDeveloperId, ct)}, " +
                      "que fue quien la propuso, con su plazo corriendo desde ahora.";

            // El aviso del esfuerzo que falta va SOLO cuando falta: en una tarea o un requerimiento
            // el líder acaba de escribirlo y decirle que no hay estimación sería mentirle.
            if (actividad.WorkType.ComoBug())
                mensaje += " Ojo: al clasificarla ya era suya, así que no hay estimación de esfuerzo " +
                           "con la que contrastar su cronómetro.";
        }

        return (true, ConEmpuje(mensaje, await EmpujarADevOpsAsync(actividad.Id, ct)));
    }

    /// <summary>
    /// QUIEN HACE EL TRABAJO DICE QUÉ HIZO, antes de entregar: la justificación de un criterio extra
    /// y, cuando el criterio lo pide, el artículo de la base de conocimiento que aplicó.
    ///
    /// <para><b>Por qué existe.</b> Un extra como «agregaste pruebas» se verifica mirando la entrega.
    /// «Aplicaste una práctica documentada» no: el líder no puede adivinar QUÉ práctica ni DÓNDE, así
    /// que sin que se lo digan, evaluarlo sería un acto de fe — y un criterio que se da por bueno sin
    /// mirar es un aumento de puntos disfrazado, que es exactamente lo que la matriz existe para
    /// evitar.</para>
    ///
    /// <para><b>Por qué ANTES de entregar y no al verificar.</b> Pedírsela al verificar sería
    /// pedírsela a alguien que ya está esperando su respuesta, y el líder tendría que devolver la
    /// entrega solo para reclamar una frase. Se captura mientras se marca el checklist, que es
    /// cuando la persona todavía está trabajando y se acuerda.</para>
    ///
    /// <para><b>El título del artículo se CONGELA</b>, como todo lo demás de esta fila. Si el autor
    /// le cambia el nombre o el artículo se retira, la fila tiene que seguir diciendo qué se aplicó:
    /// sin la copia, un extra cobrado hace seis meses aparecería como «(artículo 47)».</para>
    ///
    /// <para>Se puede reescribir mientras la actividad siga EN CURSO, y también DEVUELTA: si el
    /// líder la devolvió porque la justificación no explicaba nada, corregirla es justo lo que hay
    /// que poder hacer. Se cierra al entregar y al aceptar, que es cuando ya la está mirando alguien
    /// o cuando los puntos ya se pagaron.</para>
    /// </summary>
    /// <param name="articuloId">
    /// El artículo aplicado. Solo se acepta <c>Publicado</c>: uno en borrador o rechazado no es una
    /// práctica documentada, es un texto que alguien está escribiendo.
    ///
    /// <para>Se admite el artículo PROPIO a propósito, y no es doble pago: escribirlo se cobró una
    /// vez y para siempre; aplicarlo se cobra cada vez, que es lo que se quiere premiar. Lo que sí
    /// hace el panel del líder es DECIRLO, para que la decisión se tome sabiéndolo.</para>
    /// </param>
    public async Task<(bool ok, string mensaje)> JustificarCriterioExtraAsync(
        int criterioId, int developerId, string? justificacion, int? articuloId,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, developerId);

        var criterio = await db.PoolActivityExtraCriteria
            .Include(c => c.Activity)
            .FirstOrDefaultAsync(c => c.Id == criterioId, ct);
        if (criterio == null) return (false, "Ese criterio ya no existe. Actualiza la lista.");

        if (criterio.Activity.ClaimedByDeveloperId != developerId)
            return (false, "Esa actividad no es tuya.");

        if (!criterio.Activity.EnCurso)
            return (false, criterio.Activity.Status == PoolActivityStatus.EnRevision
                ? "Ya la entregaste: el líder la está mirando y esto ya no se cambia."
                : "Esa actividad ya no está en curso.");

        var texto = Limpiar(justificacion);
        if (texto is { Length: > MaxJustificacion })
            return (false, $"La justificación no puede pasar de {MaxJustificacion} caracteres.");

        // EL ARTÍCULO SE RESUELVE CONTRA LA BASE y no se cree lo que llegó: el identificador viene de
        // un desplegable, y un desplegable es una lista que se cargó hace un rato. Y solo Publicado —
        // un borrador propio no es una práctica documentada, es un texto que alguien está escribiendo.
        string? tituloDelArticulo = null;
        if (articuloId is int id)
        {
            var articulo = await db.KnowledgeArticles.AsNoTracking()
                .Where(a => a.Id == id)
                .Select(a => new { a.Title, a.Status })
                .FirstOrDefaultAsync(ct);

            if (articulo == null)
                return (false, "Ese artículo ya no existe. Actualiza la lista.");
            if (articulo.Status != KnowledgeStatus.Publicado)
                return (false, "Solo se puede citar un artículo PUBLICADO: uno en borrador o " +
                               "rechazado todavía no es una práctica documentada.");

            tituloDelArticulo = Recortar(articulo.Title, 200);
        }

        criterio.Justificacion         = texto;
        criterio.KnowledgeArticleId    = articuloId;
        criterio.KnowledgeArticleTitle = tituloDelArticulo;
        await db.SaveChangesAsync(ct);

        return (true, tituloDelArticulo is null
            ? "Justificación guardada."
            : $"Justificación guardada, citando «{tituloDelArticulo}».");
    }

    /// <summary>
    /// SI ESTE CRITERIO EXIGE CITAR UN ARTÍCULO. Se pregunta por el NOMBRE congelado en la fila y no
    /// por el identificador del catálogo: aquél no es estable entre instalaciones y éste es lo que
    /// sobrevive a que el catálogo se depure. Escrito una sola vez porque lo miran la guarda de
    /// entregar y la pantalla, y tienen que decir lo mismo.
    /// </summary>
    public static bool ExigeArticulo(string nombreDelCriterio) =>
        string.Equals(nombreDelCriterio, PoolSeed.CriterioDePracticaDocumentada, StringComparison.Ordinal);

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

    /// <summary>
    /// Quita del pool una actividad que ya no aplica. Solo si nadie la tomó.
    ///
    /// <para><b>Y es también cómo se rechaza una PROPUESTA</b>, que es la razón de que el motivo
    /// aparezca aquí. Retirar algo que nadie propuso no le quita nada a nadie: la actividad entró
    /// sola desde un work item y se descarta sin más. Retirar una propuesta es decirle que no a una
    /// persona, y una propuesta rechazada EN SILENCIO mata la función en una semana — nadie vuelve a
    /// proponer si la vez anterior su trabajo desapareció sin una palabra.</para>
    ///
    /// <para>Por eso el motivo es OBLIGATORIO cuando hay dueño y opcional cuando no: la regla no es
    /// «escribe siempre» —que se acabaría contestando con un punto— sino «escribe cuando alguien lo
    /// va a leer».</para>
    /// </summary>
    /// <param name="motivo">Por qué se retira. Se le manda a quien la propuso, tal cual.</param>
    public async Task<(bool ok, string mensaje)> RetirarAsync(
        int id, string? motivo = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var actividad = await db.PoolActivities.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");

        // Lo que todavía no se ha clasificado también se retira, y es la ÚNICA forma de descartarlo:
        // liberar exige que esté en curso, así que sin esto una actividad que entró sola y no
        // interesa se quedaría en la bandeja del líder para siempre, sin ninguna salida.
        if (actividad.Status is not (PoolActivityStatus.Disponible or PoolActivityStatus.PorClasificar))
            return (false, "Solo se retira lo que sigue libre en el pool. Si alguien la tomó, usa «Liberar».");

        bool sinClasificar = actividad.Status == PoolActivityStatus.PorClasificar;
        int? quienLaPropuso = actividad.ClaimedByDeveloperId;

        motivo = Limpiar(motivo);
        if (quienLaPropuso is not null && motivo is null)
            return (false, "Esta actividad la propuso alguien: escribe por qué la descartas. Se le " +
                           "manda tal cual, y es lo único que va a poder leer para entenderlo.");
        if (motivo is { Length: > MaxMotivo })
            return (false, $"El motivo no puede pasar de {MaxMotivo} caracteres.");

        actividad.Status = PoolActivityStatus.Retirada;
        if (motivo != null)
            AnotarEnHistorial(actividad, $"Descartada por {NombreDelUsuario()}: {motivo}");

        // EL RECLAMO NO SE SUELTA, aunque la propuesta se rechace. Es lo que deja escrito de quién
        // era: sin él, una propuesta descartada sería indistinguible de un work item que nadie quiso,
        // y quien la propuso no la volvería a encontrar ni en su propia lista. Y no estorba: el tope
        // cuenta solo lo vivo, y «Retirada» no lo está.
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "PoolActivity", actividad.Id.ToString(),
            (sinClasificar ? "Descartada sin clasificar" : "Retirada del pool") +
            (motivo != null ? $": {motivo}" : ""), ct);

        if (quienLaPropuso is int dev)
        {
            try
            {
                await notifications.NotifyDeveloperAsync(dev, NotificationKind.General,
                    "Tu propuesta no siguió adelante",
                    $"«{actividad.Title}» quedó descartada. Motivo: {motivo}",
                    dedupeKey: $"pool-descartada-{actividad.Id}", ct: ct);
            }
            catch { /* el descarte ya está hecho; el aviso es cortesía */ }
        }

        return (true, quienLaPropuso is not null
            ? "Propuesta descartada. Se le avisó con tu motivo."
            : sinClasificar
                ? "Actividad descartada. No volverá a entrar sola desde su work item."
                : "Actividad retirada del pool.");
    }

    /// <summary>
    /// El líder dice que ESA ACTIVIDAD LA HIZO ÉL. Se cierra en el acto: no se queda esperando a que
    /// alguien la tome, no pasa por entregar ni por verificar y <b>no abona puntos a nadie</b>.
    ///
    /// <para><b>Por qué hace falta un camino propio y no basta «Retirar».</b> Retirar dice «esto ya
    /// no aplica»; esto dice «esto ya está hecho». Son dos historias distintas de la misma fila y la
    /// diferencia se nota justo donde se mira: lo retirado es trabajo que se descartó y lo propio es
    /// trabajo que se entregó, solo que sin pasar por el pool. Mezclarlos dejaría el histórico
    /// contando como descartado todo lo que el líder resolvió él mismo.</para>
    ///
    /// <para><b>Por qué no da puntos, y por qué eso no se puede olvidar.</b> Los puntos del pool son
    /// el reparto de un trabajo que el líder publica y otra persona toma; el líder no se los abona a
    /// sí mismo. Este método no toca <c>PointEntries</c> ni <c>PointEntryId</c> —la guarda que usa
    /// <see cref="AceptarAsync"/> para no abonar dos veces—, así que una actividad propia no cruza
    /// nunca por ahí. Y además pone los puntos congelados de la fila a CERO: son los que pinta la
    /// rejilla, y dejar un «12» al lado de un estado que no abonó nada se lee como un abono perdido.
    /// Cuánto valía no se pierde: queda escrito en el historial.</para>
    ///
    /// <para>Solo se marca lo que sigue LIBRE —disponible o sin clasificar—. Si alguien ya la tomó,
    /// esto le quitaría de las manos un trabajo que está haciendo, y encima sin avisarle: para eso
    /// está <see cref="LiberarAsync"/>, que sí le avisa.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> MarcarComoPropiaAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var actividad = await db.PoolActivities.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");

        if (actividad.Status is not (PoolActivityStatus.Disponible or PoolActivityStatus.PorClasificar))
            return (false, "Solo se marca como propia lo que sigue libre en el pool. " +
                           "Si alguien ya la tomó, usa «Liberar» primero: así se le avisa.");

        var quien = NombreDelUsuario();
        int valia = actividad.Points;
        var ahora = DateTime.UtcNow;

        AnotarEnHistorial(actividad,
            $"La hizo {quien}: cerrada sin pasar por el pool. Valía {valia} pts y no se abonaron a nadie.");

        actividad.Status           = PoolActivityStatus.Propia;
        actividad.Points           = 0;
        actividad.ReviewedByUserId = currentUser.UserId;
        actividad.ReviewedAt       = ahora;
        actividad.ReviewComment    = null;

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "PoolActivity", actividad.Id.ToString(),
            $"Marcada como hecha por el líder ({quien}): valía {valia} pts y no se abonaron", ct);

        return (true, "Marcada como hecha por ti. Ya no está en el pool y no abonó puntos a nadie.");
    }

    /// <summary>
    /// BORRA la actividad. No es «Retirar»: retirar la deja en la rejilla con su historia, y esto no
    /// deja nada.
    ///
    /// <para><b>Para qué existe.</b> Para lo que nunca debió estar: el duplicado, la prueba, la que
    /// se publicó al equipo equivocado y se volvió a publicar bien. Retirarlas las deja para siempre
    /// en una lista que el líder lee entera, y una bandeja llena de basura deja de leerse.</para>
    ///
    /// <para><b>Lo que no se borra jamás, y por qué.</b> Nada que haya abonado puntos —ni aceptada ni
    /// con <c>PointEntryId</c>—: la entrada de puntos la nombra en su comentario y sin la fila esa
    /// referencia apunta al vacío, justo en la tabla con la que se explica un ranking. Y nada que
    /// alguien tenga en las manos: eso se libera antes, que avisa a quien la estaba trabajando en vez
    /// de hacérsela desaparecer de la lista.</para>
    ///
    /// <para><b>La consecuencia que hay que decir en pantalla.</b> Si venía de un work item de Azure
    /// DevOps, VOLVERÁ A ENTRAR sola en la siguiente pasada: la deduplicación de
    /// <c>PoolDesdeDevOpsService</c> se lee del propio pool —descarta el work item que ya tiene
    /// actividad, en cualquier estado— y al borrar la fila desaparece esa marca. Para descartar un
    /// ticket de una vez sigue estando «Retirar», que lo deja anotado. Por eso el mensaje de vuelta
    /// lo dice cuando toca, en vez de quedarse escrito solo aquí.</para>
    ///
    /// <para><b>Cómo se borra.</b> Los hijos primero y el padre después, dentro de una transacción que
    /// se deshace si el padre no llega a borrarse. El esquema declara <c>ON DELETE CASCADE</c> en las
    /// dos tablas hijas, así que en una base al día bastaría con el padre; se borran igual a mano
    /// porque <c>ExecuteDelete</c> no pasa por el seguimiento de EF —el que aplicaría la cascada del
    /// modelo—, y entonces lo único que sostiene la limpieza es una restricción de la base, que es
    /// justo la que puede faltar en una base vieja. Y el DELETE del padre lleva LA MISMA condición que
    /// se acaba de comprobar: entre la lectura y el borrado caben varios viajes a la base, y esta
    /// clase ya se topó con esa carrera al aceptar.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> EliminarAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var actividad = await db.PoolActivities.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new { a.Title, a.Status, a.PointEntryId, a.DevOpsWorkItemId })
            .FirstOrDefaultAsync(ct);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");

        if (actividad.Status == PoolActivityStatus.Aceptada || actividad.PointEntryId != null)
            return (false, "Esa actividad ya abonó puntos y no se borra: la entrada de puntos la " +
                           "nombra, y sin ella el ranking dejaría de poder explicarse.");

        if (actividad.Status is PoolActivityStatus.Tomada or PoolActivityStatus.EnRevision
                             or PoolActivityStatus.Devuelta)
            return (false, "Alguien la tiene tomada. Usa «Liberar» primero: así se le avisa a quien " +
                           "la estaba trabajando en vez de que se le desaparezca de la lista.");

        using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.PoolActivityChecklistItems.Where(c => c.PoolActivityId == id).ExecuteDeleteAsync(ct);
        await db.PoolActivityExtraCriteria.Where(c => c.PoolActivityId == id).ExecuteDeleteAsync(ct);

        // Y LA PERCHA SE DESMARCA, o se queda apuntando a una fila que ya no existe.
        //
        // Parece imposible —una actividad tomada no se borra, lo impide la guarda de arriba— pero el
        // camino existe y es de todos los días: alguien la toma, y con eso se le crea la percha con
        // su PoolActivityId escrito; la devuelve al pool, y SoltarReclamo limpia LinkedDevActivityId
        // pero la marca de la percha NO se limpia nunca, a propósito; la actividad vuelve a
        // Disponible… y desde ahí sí se borra.
        //
        // Sin esta línea esa DevActivity se queda marcada para siempre: EsPerchaDelPool sigue
        // diciendo que sí, así que su dueño no puede cerrarla, reabrirla, renombrarla ni eliminarla,
        // y tampoco la ve porque la lista la esconde. Un cronómetro con horas medidas dentro,
        // bloqueado y escondido, por una actividad del pool que ya no existe.
        //
        // Va DENTRO de la misma transacción y ANTES de borrar el padre: si el ExecuteDelete de abajo
        // no afecta ninguna fila —alguien cambió la actividad mientras se miraba la lista— esto se
        // deshace con el rollback y la marca se queda como estaba.
        await db.DevActivities
            .Where(a => a.PoolActivityId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PoolActivityId, (int?)null), ct);

        int borradas = await db.PoolActivities
            .Where(a => a.Id == id
                     && a.PointEntryId == null
                     && (a.Status == PoolActivityStatus.Disponible
                      || a.Status == PoolActivityStatus.PorClasificar
                      || a.Status == PoolActivityStatus.Retirada
                      || a.Status == PoolActivityStatus.Propia))
            .ExecuteDeleteAsync(ct);

        if (borradas == 0)
        {
            await tx.RollbackAsync(ct);
            return (false, "Esa actividad cambió mientras mirabas la lista. Recárgala y vuelve a mirar.");
        }

        await tx.CommitAsync(ct);

        await audit.RecordAsync(AuditAction.Delete, "PoolActivity", id.ToString(),
            $"Eliminada del pool: «{Recortar(actividad.Title, 120)}»", ct);

        return (true, actividad.DevOpsWorkItemId is int workItem
            ? $"Actividad eliminada. Ojo: venía del work item #{workItem}, así que volverá a entrar " +
              "sola en la siguiente pasada. Para descartarlo de una vez, «Retirar» en vez de eliminar."
            : "Actividad eliminada.");
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
    /// EL DESARROLLADOR PROPONE TRABAJO AL POOL. Es la puerta que sustituyó a la autocalificación.
    ///
    /// <para><b>Qué se pide y qué no.</b> Título, detalle, un enlace y —si lo hay— el work item. Ni
    /// tipo, ni complejidad, ni horas, ni puntos, ni a nombre de quién. No es una omisión por
    /// comodidad: es LA regla del pool. Quien hace el trabajo no pone su precio; el precio sale de
    /// la matriz una vez que el líder dice de qué clase es y cuánto pesa. Aceptar aquí cualquiera de
    /// esos cuatro campos reabriría por la puerta de atrás exactamente lo que se acaba de cerrar.</para>
    ///
    /// <para><b>Nace CON DUEÑO</b>, y eso es lo que la hace barata. Una propuesta es una actividad
    /// «Por clasificar» con <c>ClaimedByDeveloperId</c> puesto, y con eso sola:
    /// <list type="bullet">
    /// <item>aparece en «lo mío» sin tocar esa consulta, que ya filtra por el reclamo;</item>
    /// <item>NO aparece en lo disponible, que filtra por <c>Disponible</c>;</item>
    /// <item>no se la puede llevar otro, porque tomar exige <c>Disponible</c>;</item>
    /// <item>no sale hacia DevOps, porque <c>YaPublicada</c> la deja callada mientras no tenga
    ///       tipo ni puntos que afirmar;</item>
    /// <item>y ya se ve en la pantalla del líder, con «Clasificar» y «Descartar» al lado.</item>
    /// </list>
    /// Cinco comportamientos que no hubo que escribir. Un estado nuevo los habría pedido todos.</para>
    ///
    /// <para><b>Cuenta dentro del tope de tomadas.</b> Una propuesta es trabajo que esa persona ya
    /// tiene entre manos —de hecho, en cuanto el líder la clasifique se la encuentra tomada— así que
    /// dejarla fuera del tope permitiría llegar al doble de trabajo vivo proponiendo en vez de
    /// tomando, y el tope diría tres mientras la persona lleva seis.</para>
    ///
    /// <para>No se propone a nombre de otro: la ruta no lleva identificador y aquí se exige la
    /// propiedad. Proponer por alguien sería ponerle trabajo a su nombre sin que se entere.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje, PoolActivity? actividad)> ProponerAsync(
        int developerId, string? titulo, string? detalle, string? enlace, int? workItem,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(currentUser, developerId);

        var limpio = (titulo ?? "").Trim();
        if (limpio.Length == 0)
            return (false, "Escribe de qué trabajo se trata: es lo único que el líder va a leer para " +
                           "decidir cuánto vale.", null);
        if (limpio.Length > 200)
            return (false, "El título no puede pasar de 200 caracteres. Lo largo va en el detalle.", null);

        var cuerpo = Limpiar(detalle);
        if (cuerpo is { Length: > MaxDetalle })
            return (false, $"El detalle no puede pasar de {MaxDetalle} caracteres.", null);

        // El mismo validador de enlaces que en todo lo demás: solo http y https, porque el líder lo
        // abre con el navegador al clasificar.
        var (enlaceOk, enlaceError, url) = PerformanceScoringService.NormalizarEnlace(enlace);
        if (!enlaceOk) return (false, enlaceError, null);

        var (vinculoOk, vinculoError, numero) = PoolDevOpsService.ResolverWorkItem(workItem, url);
        if (!vinculoOk) return (false, vinculoError, null);

        // Y que no haya ya otra actividad viva sobre ese mismo work item. Se comprueba con id 0
        // —«todavía no existe»— igual que al publicar: ninguna fila puede ser ella misma.
        if (numero is int wi)
        {
            var (libre, ocupado) = await PoolDevOpsService.NadieMasLoTieneAsync(db, wi, 0, ct);
            if (!libre) return (false, ocupado, null);
        }

        int tope = await TopeDeTomadasAsync(ct);
        int vivas = await CuantasVivasAsync(developerId, ct);
        if (vivas >= tope)
            return (false, $"Ya tienes {vivas} actividad(es) del pool sin entregar —contando las que " +
                           $"están esperando que el líder las clasifique— y el tope es {tope}. " +
                           "Termina o devuelve alguna antes de proponer otra.", null);

        // ClaimedAt se queda VACÍO a propósito, aunque haya dueño. Esa columna no dice «de quién es»
        // sino «cuándo empezó a correr el reloj», y el reloj de una propuesta no ha empezado: lo
        // arranca el líder al clasificarla, que es cuando se sabe de cuántas horas es el plazo.
        // Escribirlo aquí dejaría actividades vencidas antes de que nadie supiera lo que valían.
        var actividad = new PoolActivity
        {
            Title                 = limpio,
            Description           = cuerpo,
            Status                = PoolActivityStatus.PorClasificar,
            Points                = 0,
            ClaimedByDeveloperId  = developerId,
            Priority              = PoolPriority.Media,
            ExternalUrl           = url,
            DevOpsWorkItemId      = numero,
            CreatedByUserId       = currentUser.UserId,
            CreatedAt             = DateTime.UtcNow
        };
        AnotarEnHistorial(actividad,
            $"Propuesta por {await NombreDeDesarrolladorAsync(developerId, ct)}, a la espera de que " +
            "el líder le ponga valor.");

        db.PoolActivities.Add(actividad);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Create, "PoolActivity", actividad.Id.ToString(),
            $"Propuesta al pool: «{actividad.Title}»" +
            (numero is int n ? $", ligada al work item #{n}" : ""), ct);

        await AvisarDeLaPropuestaAsync(actividad, ct);

        // NO se empuja a DevOps, y no hace falta guarda: sin tipo ni puntos, YaPublicada la deja
        // callada. Se dice aquí porque la ausencia de la llamada, en un método que se parece tanto a
        // CrearAsync, se lee como un olvido.
        return (true, "Propuesta enviada. El líder le pondrá tipo y complejidad, y de ahí saldrán sus " +
                      "puntos; cuando lo haga te la encontrarás tomada, con su plazo y su checklist.",
                actividad);
    }

    /// <summary>
    /// Cuántas actividades del pool tiene alguien VIVAS: tomadas, devueltas y propuestas suyas que
    /// todavía esperan clasificación.
    ///
    /// <para>Escrita una sola vez porque la miran proponer y tomar, y las dos tienen que contar lo
    /// mismo: si tomar no contara las propuestas, alguien con el tope lleno de propuestas podría
    /// además tomar tres más.</para>
    /// </summary>
    private Task<int> CuantasVivasAsync(int developerId, CancellationToken ct) =>
        db.PoolActivities.AsNoTracking()
            .CountAsync(a => a.ClaimedByDeveloperId == developerId
                          && (a.Status == PoolActivityStatus.Tomada
                           || a.Status == PoolActivityStatus.Devuelta
                           || a.Status == PoolActivityStatus.PorClasificar), ct);

    /// <summary>
    /// Avisa a quien propuso una actividad de que ya está clasificada y a su nombre.
    ///
    /// <para>Lleva los PUNTOS dentro, que es lo que estaba esperando saber: la propuesta se mandó
    /// sin valor a propósito, y este aviso es el momento en que se entera de cuánto vale. Y lleva el
    /// plazo, porque a partir de ahora corre.</para>
    /// </summary>
    private async Task AvisarDeLaClasificacionAsync(PoolActivity actividad, int developerId, CancellationToken ct)
    {
        try
        {
            var plazo = actividad.ClaimDeadlineAt is DateTime f
                ? $" Entrega esperada: {f.ToLocalTime():dd/MM/yyyy HH:mm}."
                : "";

            await notifications.NotifyDeveloperAsync(developerId, NotificationKind.General,
                "Tu propuesta ya tiene valor, y es tuya",
                $"«{actividad.Title}» quedó como {PoolSeed.Etiqueta(actividad.WorkType)} / " +
                $"{PoolSeed.Etiqueta(actividad.Complexity)}: {actividad.Points} puntos al aceptarse." +
                $"{plazo} Ya está a tu nombre, con su checklist.",
                dedupeKey: $"pool-clasificada-{actividad.Id}", ct: ct);
        }
        catch { /* la clasificación ya está hecha; el aviso es cortesía */ }
    }

    /// <summary>
    /// Avisa a los líderes de que hay una propuesta esperando. Es cortesía y va en try/catch, como
    /// todos los avisos: la propuesta ya está guardada cuando esto corre.
    ///
    /// <para>Importa más de lo que parece. Una propuesta que nadie mira es peor que no poder
    /// proponer: la persona se queda con una actividad ocupándole sitio en el tope y sin saber si
    /// alguien la vio. La cuenta de «Por clasificar» es además la medida de si el cuello de botella
    /// se trasladó al líder, que es el riesgo declarado de todo este cambio.</para>
    /// </summary>
    private async Task AvisarDeLaPropuestaAsync(PoolActivity actividad, CancellationToken ct)
    {
        try
        {
            var lideres = await db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.Role == UserRole.Admin)
                .Select(u => u.Id).ToListAsync(ct);

            var deQuien = await NombreDeDesarrolladorAsync(actividad.ClaimedByDeveloperId, ct);
            foreach (var userId in lideres)
                await notifications.NotifyAsync(userId, NotificationKind.General,
                    "Hay trabajo propuesto por clasificar",
                    $"«{actividad.Title}» — la propuso {deQuien} y espera que le pongas valor.",
                    dedupeKey: $"pool-propuesta-{actividad.Id}", ct: ct);
        }
        catch { /* el aviso es cortesía; la propuesta ya quedó registrada */ }
    }

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

        // ── LA SEGMENTACIÓN, COMPROBADA AQUÍ Y OTRA VEZ EN EL UPDATE ─────────────
        //
        // Esta primera comprobación es solo para poder dar un mensaje que se entienda. La que de
        // verdad manda es la del UPDATE de abajo: a esta dirección se la puede llamar a mano con
        // cualquier identificador, y un filtro que solo viviera en la consulta de la lista escondería
        // la actividad de la pantalla sin impedir que se tomara. Eso no es segmentar, es decorar.
        var equiposQueVeo = await EquiposQueVeoAsync(ct);
        if (equiposQueVeo is not null
            && actividad.EquipoId is int suEquipo && !equiposQueVeo.Contains(suEquipo))
            return (false, "Esa actividad está publicada para otro equipo.");

        // El tope cuenta también LAS PROPUESTAS sin clasificar. Sin eso, quien tenga el tope lleno de
        // propuestas podría además tomar tres actividades más y acabar con el doble de trabajo vivo
        // que el que el tope dice permitir — y encima descubriéndolo el día que el líder clasifique
        // las propuestas y se las encuentre todas tomadas de golpe.
        int tope = await TopeDeTomadasAsync(ct);
        int tomadas = await CuantasVivasAsync(developerId, ct);
        if (tomadas >= tope)
            return (false, $"Ya tienes {tomadas} actividad(es) del pool sin entregar —contando las que " +
                           $"propusiste y siguen sin clasificar— y el tope es {tope}. " +
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
        if (actividad.WorkType.ComoBug())
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

        var (horas, limite) = PlazoDesdeAhora(actividad, celda, ahora);

        // El sello viaja junto al número y con la misma forma nula, para que el UPDATE de abajo pueda
        // dejar los dos como estaban con un COALESCE y no con un condicional que EF tendría que
        // traducir sobre un parámetro.
        DateTime? selloDeLaEstimacion = estimacion is null ? null : ahora;

        // La visibilidad entra en el MISMO Where que el estado y SIN sustituirlo: las dos condiciones
        // tienen que cumplirse para ganar el reclamo. Puesta aparte —o solo en la consulta de la
        // lista— «tomar» seguiría aceptando cualquier identificador.
        int ganadas = await db.PoolActivities
            .Where(a => a.Id == id && a.Status == PoolActivityStatus.Disponible)
            .Where(Visibles(equiposQueVeo))
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
        // Cero filas cambiadas puede ser DOS cosas distintas y hay que distinguirlas, o el mensaje
        // miente en una de ellas: que alguien se adelantara, o que la actividad se segmentara a otro
        // equipo entre que se dibujó la lista y se pulsó el botón. Se relee el estado para saber cuál.
        if (ganadas == 0)
        {
            var ahoraEsta = await db.PoolActivities.AsNoTracking()
                .Where(a => a.Id == id).Select(a => new { a.Status, a.EquipoId }).FirstOrDefaultAsync(ct);

            if (ahoraEsta == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");
            if (equiposQueVeo is not null && ahoraEsta.EquipoId is int ahoraDe
                && !equiposQueVeo.Contains(ahoraDe))
                return (false, "Esa actividad acaba de publicarse para otro equipo.");

            return (false, "Alguien más la tomó primero. Actualiza la lista.");
        }

        // El reclamo ya es firme; a partir de aquí se trabaja sobre la entidad rastreada. Se lee de
        // la base y no de la copia AsNoTracking de arriba porque el UPDATE condicional se ejecutó
        // directamente contra la base y esa copia todavía diría «Disponible».
        var reclamada = await db.PoolActivities.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (reclamada != null) await ArrancarElTrabajoAsync(reclamada, developerId, ct);

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

        // ── EL EXTRA QUE HAY QUE EXPLICAR ────────────────────────────────────────
        //
        // Misma forma que la guarda de la evidencia que hay justo encima, y por el mismo motivo: se
        // pide ANTES de entregar porque después ya hay alguien esperando. Un «aplicaste una práctica
        // documentada» sin decir cuál obligaría al líder a devolver la entrega solo para reclamar una
        // frase, y esa vuelta cuesta más que la frase.
        //
        // Se pide EL ARTÍCULO ADEMÁS DE LA JUSTIFICACIÓN, no en su lugar. El texto explica qué se
        // hizo; el artículo es lo que hace la afirmación comprobable —y lo que, sumado sobre todas las
        // entregas, contesta por primera vez qué artículos se aplican de verdad y cuáles llevan un año
        // publicados sin que nadie los use.
        //
        // Solo se exige a los que lo piden: obligar a justificar TODOS los extras convertiría en
        // trámite unos campos que existen para que uno concreto se pueda verificar.
        var sinJustificar = await db.PoolActivityExtraCriteria.AsNoTracking()
            .Where(c => c.PoolActivityId == actividad.Id)
            .Select(c => new { c.Name, c.Justificacion, c.KnowledgeArticleId })
            .ToListAsync(ct);

        var faltaExplicar = sinJustificar
            .Where(c => ExigeArticulo(c.Name)
                     && (string.IsNullOrWhiteSpace(c.Justificacion) || c.KnowledgeArticleId is null))
            .ToList();

        if (faltaExplicar.Count > 0)
            return (false, "Te comprometiste a «" + faltaExplicar[0].Name + "»: antes de entregar " +
                           "tienes que decir QUÉ artículo de la base de conocimiento aplicaste y " +
                           "CÓMO. Sin eso, el líder no tiene nada que verificar.");

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

        // EL SIGNO SE ESCRIBE, no se da por hecho. Desde que existe el RETRABAJO —un bug sobre algo
        // ya entregado— los puntos de una actividad pueden ser NEGATIVOS, y un «+» pegado delante de
        // un número que resta produce «+-8», que además de feo se lee mal justo en el aviso que le
        // llega a la persona. El formato «+#;-#;0» pone el signo que toque y deja el cero sin signo.
        var desglose = cumplidos.Count > 0
            ? $" ({actividad.Points:+#;-#;0} base, {puntosExtra:+#;-#;0} por {string.Join(", ", cumplidos.Select(c => c.Name))})"
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

        var historial = HistorialCon(actividad, $"Aceptada por {NombreDelUsuario()}: {puntosTotal:+#;-#;0} pts{desglose}.");

        // EL ABONO LO HACE AbonarAsync, que es el único sitio de la aplicación que convierte trabajo
        // en puntos: la transacción, la reserva condicional contra el doble pago y la traza de vuelta
        // viven ahí, y las comparte con el descuento del líder. Aquí se queda lo que es de aceptar una
        // entrega —el desglose de los criterios extra, el aviso, cerrar el cronómetro— y nada más.
        if (!await AbonarAsync(actividad, entrada, PoolActivityStatus.EnRevision,
                               PoolActivityStatus.Aceptada, historial, revisadoUtc, ct))
            return (false, "Esa actividad ya estaba aceptada; sus puntos ya se abonaron.");

        await audit.RecordAsync(AuditAction.Update, "PoolActivity", id.ToString(),
            $"Aceptada: {puntosTotal:+#;-#;0} pts{desglose} a {await NombreDeDesarrolladorAsync(developerId, ct)}", ct);

        try
        {
            await notifications.NotifyDeveloperAsync(developerId, NotificationKind.General,
                // El titular cambia con el signo. «Fue aceptada» a secas encima de un «-8» se lee
                // como una buena noticia hasta que se llega al número, y esta es de las que conviene
                // que se entiendan desde el asunto.
                puntosTotal < 0 ? "Tu actividad del pool se aceptó, y descuenta puntos"
                                : "Tu actividad del pool fue aceptada",
                $"«{actividad.Title}»: {puntosTotal:+#;-#;0} puntos{desglose}, ya cuentan en el ranking de {ahora:MMMM}.",
                dedupeKey: $"pool-aceptada-{id}", ct: ct);
        }
        catch { /* los puntos ya están abonados: un aviso fallido no puede tumbar la operación */ }

        // Con el Id de la ACTIVIDAD LIBRE, no con el de la del pool. Son dos tablas distintas y sus
        // Id no tienen nada que ver: pasar el equivocado cerraba la actividad libre de otra persona
        // —la que por casualidad tuviera ese número— y dejaba abierto el cronómetro de esta.
        await CerrarActividadEnlazadaAsync(actividad.LinkedDevActivityId ?? 0, ct);

        var quien = await NombreDeDesarrolladorAsync(developerId, ct);
        return (true, puntosTotal < 0
            ? $"Aceptada. Se le DESCONTARON {-puntosTotal} puntos a {quien} en {ahora:MM/yyyy}."
            : $"Aceptada. Se abonaron {puntosTotal} puntos a {quien} en {ahora:MM/yyyy}.");
    }

    /// <summary>
    /// EL ÚNICO SITIO DE ESTA APLICACIÓN QUE CONVIERTE TRABAJO EN PUNTOS.
    ///
    /// <para>No es una abstracción por elegancia: es el encargo. Antes había cuatro caminos por los
    /// que entraban puntos y cada uno traía sus propias reglas sobre quién decide, qué criterios valen
    /// y en qué estado nace la entrada. Con el pool como unidad de trabajo quedan dos —aceptar una
    /// entrega y aplicar un descuento— y los dos pasan por aquí, así que «un solo camino» es algo que
    /// se puede comprobar leyendo una función en vez de una frase de un documento. La lista cerrada de
    /// quién puede insertar una entrada la vigila <c>ProductoresDePuntosTests</c>.</para>
    ///
    /// <para><b>Qué garantiza.</b> Que el abono sea atómico —o queda la actividad pagada y su entrada,
    /// o no queda ninguna de las dos— y que <b>no se pueda pagar dos veces</b>. Lo segundo lo sostiene
    /// el <c>ExecuteUpdate</c> condicional sobre <c>PointEntryId == null</c>, que es lo que hace que
    /// dos líderes revisando la misma cola no abonen el doble: quien pierda la carrera no afecta
    /// ninguna fila y se va con un mensaje. Comprobar y luego escribir no bastaba — entre las dos
    /// cosas caben varios viajes a la base.</para>
    ///
    /// <para><b>Los dos modos, y por qué es una sola función y no dos.</b> Una actividad que ya existe
    /// se RESERVA con el update condicional; una que nace pagada —el descuento— se inserta, y ahí no
    /// hay carrera posible porque nadie más conoce todavía esa fila. Lo que comparten es todo lo
    /// demás: la transacción, la entrada, la traza de vuelta y el commit. Partirlo en dos dejaría dos
    /// sitios donde escribir puntos, que es exactamente lo que se vino a cerrar.</para>
    /// </summary>
    /// <param name="actividad">La actividad. Con <c>Id == 0</c> se inserta; con Id se reserva.</param>
    /// <param name="estadoEsperado">En qué estado tiene que estar para poder pagarla, cuando ya
    /// existe. Es la mitad de la guarda: sin él, un identificador mandado a mano podría cobrar algo
    /// que no está esperando verificación.</param>
    private async Task<bool> AbonarAsync(
        PoolActivity actividad, PointEntry entrada,
        PoolActivityStatus? estadoEsperado, PoolActivityStatus estadoFinal,
        string historial, DateTime revisadoUtc, CancellationToken ct)
    {
        int revisor = currentUser.UserId ?? 0;

        using var tx = await db.Database.BeginTransactionAsync(ct);

        if (actividad.Id == 0)
        {
            // Nace pagada. El estado y la traza se escriben en la entidad y no por UPDATE, porque
            // todavía no hay fila que actualizar.
            actividad.Status           = estadoFinal;
            actividad.ReviewedByUserId = revisor;
            actividad.ReviewedAt       = revisadoUtc;
            actividad.ReviewHistory    = historial;
            db.PoolActivities.Add(actividad);
            await db.SaveChangesAsync(ct);
        }
        else
        {
            int ganadas = await db.PoolActivities
                .Where(a => a.Id == actividad.Id
                         && a.PointEntryId == null
                         && (estadoEsperado == null || a.Status == estadoEsperado))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, estadoFinal)
                    .SetProperty(a => a.ReviewedByUserId, revisor)
                    .SetProperty(a => a.ReviewedAt, revisadoUtc)
                    .SetProperty(a => a.ReviewComment, (string?)null)
                    .SetProperty(a => a.ReviewHistory, historial), ct);

            if (ganadas == 0)
            {
                await tx.RollbackAsync(ct);
                return false;
            }
        }

        db.PointEntries.Add(entrada);
        await db.SaveChangesAsync(ct);

        // La traza a la entrada va en la MISMA transacción: si algo falla, no queda una actividad
        // pagada sin sus puntos ni unos puntos sin actividad que los justifique. Si algo revienta
        // antes del commit, el «using» deshace la transacción al salir.
        await db.PoolActivities.Where(a => a.Id == actividad.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PointEntryId, entrada.Id), ct);

        await tx.CommitAsync(ct);
        return true;
    }

    // ── El descuento ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Suelo de un descuento. El número no se escribe aquí: vive en
    /// <see cref="LimitesDeLosPuntos.MinDescuento"/>, en Shared, porque la casilla del navegador
    /// tiene que poder acotarse con el MISMO tope que impone el servidor. Este alias se queda para
    /// que dentro de este archivo se lea como lo que es.
    /// </summary>
    public const int MinPuntosDeDescuento = LimitesDeLosPuntos.MinDescuento;

    /// <summary>
    /// El líder aplica un DESCUENTO a una persona: una actividad del pool que nace ya pagada, cerrada
    /// y con puntos negativos.
    ///
    /// <para><b>Para qué existe.</b> El catálogo tiene cuarenta y un criterios negativos y, desde que
    /// el pool es la unidad de trabajo, ninguna puerta por la que aplicarlos: calificar una actividad
    /// libre era la única y se apagó. Sin esto, el líder se queda sin forma de anotar nada que salió
    /// mal, y cuarenta y un criterios quedan escritos sin poder usarse.</para>
    ///
    /// <para><b>No es un <c>Retrabajo</c>.</b> El retrabajo se publica, alguien lo toma, lo cronometra
    /// y lo entrega: hay trabajo real, aunque no debería haber hecho falta, y su precio sale de la
    /// MATRIZ. Un descuento no lo toma nadie y su precio sale de un CRITERIO que nombra el hecho. La
    /// regla, en una línea: <b>si hay algo que hacer, es retrabajo; si no hay nada que hacer, es un
    /// descuento.</b></para>
    ///
    /// <para><b>El motivo es obligatorio, y es la única escritura del pool que lo exige para PAGAR.</b>
    /// En todo lo demás la justificación es el checklist y los criterios extra: hay algo que mirar.
    /// Aquí no hay entrega, así que lo único que la persona puede leer para entender qué le pasó a sus
    /// puntos es la frase que escribió el líder. Es el mismo argumento del motivo obligatorio de
    /// <see cref="RechazarAsync"/>.</para>
    ///
    /// <para>No comparte NADA con <see cref="CrearAsync"/> a propósito: no consulta la matriz, no copia
    /// checklist, no crea percha y no empuja a DevOps. Así esas cuatro exclusiones dejan de ser
    /// condiciones que alguien puede olvidar y pasan a ser código que no existe.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> PublicarDescuentoAsync(
        int developerId, int criterionId, int puntos, string titulo, string motivo,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        titulo = (titulo ?? "").Trim();
        motivo = (motivo ?? "").Trim();

        if (titulo.Length == 0) return (false, "Escribe de qué es el descuento.");
        if (titulo.Length > 200) return (false, "El título no puede pasar de 200 caracteres.");
        if (motivo.Length == 0)
            return (false, "Escribe el motivo: es lo único que esa persona va a poder leer para " +
                           "entender por qué le bajaron los puntos.");
        if (motivo.Length > MaxMotivo) return (false, $"El motivo no puede pasar de {MaxMotivo} caracteres.");

        var quien = await db.Developers.AsNoTracking()
            .Where(d => d.Id == developerId)
            .Select(d => new { d.Id, d.FullName, d.IsActive })
            .FirstOrDefaultAsync(ct);
        if (quien == null) return (false, "Esa persona ya no existe. Actualiza la lista.");
        if (!quien.IsActive) return (false, $"{quien.FullName} está dada de baja: no tiene ranking al que descontarle.");

        var criterio = await db.ScoringCriteria.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == criterionId, ct);
        if (criterio == null) return (false, "Ese criterio ya no existe. Actualiza la lista.");
        if (!criterio.IsActive) return (false, $"«{criterio.Name}» está retirado del catálogo.");
        if (criterio.Scope == CriterionScope.Equipo)
            return (false, $"«{criterio.Name}» es un criterio de equipo y esto lo paga una sola " +
                           "persona. Elige uno individual.");
        if (criterio.Name.StartsWith(PoolSeed.PrefijoCriterio, StringComparison.Ordinal))
            return (false, $"«{criterio.Name}» es el criterio con el que el pool abona sus actividades. " +
                           "Un descuento lleva un criterio que nombre lo que pasó.");
        // Que el criterio sea NEGATIVO en el catálogo es lo que conserva vivos los cuarenta y un
        // criterios de castigo: son exactamente los que se pueden elegir aquí y en ningún otro sitio.
        if (criterio.DefaultPoints >= 0)
            return (false, $"«{criterio.Name}» no es un criterio de descuento: en el catálogo vale " +
                           $"{criterio.DefaultPoints:+#;-#;0}. Elige uno que reste.");

        if (puntos >= 0)
            return (false, "Un descuento resta: los puntos tienen que ser negativos. Si no quieres " +
                           "quitarle nada, no publiques el descuento.");
        if (puntos < MinPuntosDeDescuento)
            return (false, $"El descuento no puede pasar de {-MinPuntosDeDescuento} puntos. Si de verdad " +
                           "hace falta más, hazlo en dos y que cada uno diga su motivo.");

        var ahora = DateTime.Now;          // local: el período se imputa al mes del calendario de la gente
        var revisadoUtc = DateTime.UtcNow;

        var entrada = new PointEntry
        {
            DeveloperId      = developerId,
            CriterionId      = criterio.Id,
            Points           = puntos,
            Year             = ahora.Year,
            Month            = ahora.Month,
            Comment          = $"Descuento: {titulo} — {motivo}",
            AssignedByUserId = currentUser.UserId,
            ReviewedByUserId = currentUser.UserId,
            ReviewedAt       = revisadoUtc,
            ApprovalStatus   = PointApprovalStatus.Aprobado,
            Date             = revisadoUtc
        };

        // El TIPO es Retrabajo y la COMPLEJIDAD la más baja, y ninguno de los dos significa nada aquí:
        // el precio de un descuento sale del criterio y no de la matriz. Se ponen porque las columnas
        // no son nulables, y queda escrito para que nadie los lea como si dijeran algo — la regla vive
        // en la tabla de «lo que el esquema no expresa» de MODELO-DE-DATOS.md.
        var actividad = new PoolActivity
        {
            Title                = Recortar(titulo, 200),
            Description          = motivo,
            WorkType             = PoolWorkType.Retrabajo,
            Complexity           = PoolComplexity.Baja,
            Points               = puntos,
            Priority             = PoolPriority.Baja,
            ClaimedByDeveloperId = developerId,
            ClaimedAt            = revisadoUtc,
            DeliveredAt          = revisadoUtc,
            CreatedByUserId      = currentUser.UserId,
            CreatedAt            = revisadoUtc
        };

        var historial = $"[{ahora:dd/MM/yyyy HH:mm}] Descuento aplicado por {NombreDelUsuario()} " +
                        $"bajo «{criterio.Name}»: {puntos} pts. Motivo: {motivo}";

        if (!await AbonarAsync(actividad, entrada, estadoEsperado: null,
                               PoolActivityStatus.Descuento, historial, revisadoUtc, ct))
            return (false, "No se pudo aplicar el descuento. Vuelve a intentarlo.");

        await audit.RecordAsync(AuditAction.Update, "PoolActivity", actividad.Id.ToString(),
            $"Descuento de {puntos} pts a {quien.FullName} bajo «{criterio.Name}»: {motivo}", ct);

        try
        {
            // Un descuento silencioso es la peor versión posible de esto: alguien vería su ranking
            // bajar a fin de mes sin nada que leer. El motivo viaja DENTRO del aviso.
            await notifications.NotifyDeveloperAsync(developerId, NotificationKind.General,
                "Se te aplicó un descuento de puntos",
                $"«{titulo}»: {puntos} puntos bajo «{criterio.Name}». Motivo: {motivo}",
                dedupeKey: $"pool-descuento-{actividad.Id}", ct: ct);
        }
        catch { /* el descuento ya está aplicado: un aviso fallido no puede tumbar la operación */ }

        return (true, $"Descuento aplicado: {puntos} puntos a {quien.FullName} en {ahora:MM/yyyy}.");
    }

    /// <summary>
    /// Deshace un descuento. Es la única escritura del pool que puede pagar sin que nadie la revise,
    /// así que tiene que poder deshacerse.
    ///
    /// <para><b>No borra la entrada original.</b> Aquí nada que haya pagado se borra, ni siquiera para
    /// deshacerlo: se escribe una entrada COMPENSATORIA con el mismo criterio y el signo contrario, y
    /// el neto queda en cero con las dos visibles. Un descuento anulado tiene que poder contarse igual
    /// que uno vigente, porque la conversación que lo produjo existió y el histórico se lee para
    /// explicarla.</para>
    ///
    /// <para>La compensatoria se imputa al mes de HOY y no al del descuento, a propósito: los meses
    /// cerrados no se reescriben. Si el descuento fue en marzo y se anula en mayo, marzo siguió siendo
    /// como fue y mayo lo devuelve.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> AnularDescuentoAsync(
        int id, string motivo, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        motivo = (motivo ?? "").Trim();
        if (motivo.Length == 0) return (false, "Escribe por qué se anula: va al historial y al aviso.");
        if (motivo.Length > MaxMotivo) return (false, $"El motivo no puede pasar de {MaxMotivo} caracteres.");

        var actividad = await db.PoolActivities.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (actividad == null) return (false, "Ese descuento ya no existe. Actualiza la lista.");
        if (actividad.Status != PoolActivityStatus.Descuento)
            return (false, "Solo se anulan descuentos.");
        if (actividad.AnulacionPointEntryId != null)
            return (false, "Ese descuento ya estaba anulado.");
        if (actividad.PointEntryId is not int entradaOriginalId)
            return (false, "Ese descuento no llegó a abonarse, así que no hay nada que anular.");
        if (actividad.ClaimedByDeveloperId is not int developerId)
            return (false, "Ese descuento no tiene a quién devolverle los puntos.");

        var original = await db.PointEntries.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == entradaOriginalId, ct);
        if (original == null)
            return (false, "La entrada de puntos del descuento ya no existe, así que no se puede " +
                           "compensar. Ajusta el puntaje a mano si hace falta.");

        var ahora = DateTime.Now;
        var revisadoUtc = DateTime.UtcNow;

        var compensatoria = new PointEntry
        {
            DeveloperId      = developerId,
            CriterionId      = original.CriterionId,
            Points           = -original.Points,
            Year             = ahora.Year,
            Month            = ahora.Month,
            Comment          = $"Anulación del descuento #{actividad.Id}: {motivo}",
            AssignedByUserId = currentUser.UserId,
            ReviewedByUserId = currentUser.UserId,
            ReviewedAt       = revisadoUtc,
            ApprovalStatus   = PointApprovalStatus.Aprobado,
            Date             = revisadoUtc
        };

        var historial = HistorialCon(actividad,
            $"Anulado por {NombreDelUsuario()}: se devolvieron {-original.Points} pts. Motivo: {motivo}");

        using var tx = await db.Database.BeginTransactionAsync(ct);

        // El UPDATE condicional sobre la anulación es la guarda contra devolver los puntos dos veces,
        // igual que PointEntryId lo es contra cobrarlos dos veces. Se reserva ANTES de escribir la
        // entrada, por el mismo motivo.
        int ganadas = await db.PoolActivities
            .Where(a => a.Id == id && a.AnulacionPointEntryId == null
                     && a.Status == PoolActivityStatus.Descuento)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ReviewHistory, historial), ct);
        if (ganadas == 0)
        {
            await tx.RollbackAsync(ct);
            return (false, "Ese descuento ya estaba anulado.");
        }

        db.PointEntries.Add(compensatoria);
        await db.SaveChangesAsync(ct);

        await db.PoolActivities.Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AnulacionPointEntryId, compensatoria.Id), ct);

        await tx.CommitAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "PoolActivity", id.ToString(),
            $"Descuento anulado: se devolvieron {-original.Points} pts. {motivo}", ct);

        try
        {
            await notifications.NotifyDeveloperAsync(developerId, NotificationKind.General,
                "Se anuló el descuento de puntos",
                $"«{actividad.Title}»: se te devolvieron {-original.Points} puntos. Motivo: {motivo}",
                dedupeKey: $"pool-descuento-anulado-{id}", ct: ct);
        }
        catch { /* la anulación ya está hecha */ }

        return (true, $"Descuento anulado: se devolvieron {-original.Points} puntos en {ahora:MM/yyyy}.");
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
            // El signo lo manda el TIPO, no quien teclea. Un retrabajo que se guardara en positivo
            // pagaría por volver a abrir algo ya entregado, que es justo lo contrario de para lo que
            // existe; y un bug en negativo castigaría el trabajo normal sin que nadie lo hubiera
            // decidido. El cero queda fuera en los dos casos: una actividad que no vale nada no es
            // una regla, es una celda a medio llenar.
            if (f.WorkType.Resta() && f.Points >= 0)
                return (false, $"{PoolSeed.Etiqueta(f.WorkType)} / {PoolSeed.Etiqueta(f.Complexity)}: " +
                               "el retrabajo RESTA, así que sus puntos tienen que ser negativos.");
            if (!f.WorkType.Resta() && f.Points <= 0)
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
        // El CERO es lo que no vale, no el signo. Esta guarda existe para atajar la celda a medio
        // configurar —publicar algo que no vale nada—, y desde que existe el RETRABAJO hay un tipo
        // cuyas celdas son negativas a propósito: preguntando por «<= 0» se rechazaban todas, y el
        // mensaje decía «vale 0 puntos» de una celda que valía -12.
        if (celda.Points == 0 || celda.Points < 0 != b.WorkType.Resta())
            return (false, $"{PoolSeed.Etiqueta(b.WorkType)} / {PoolSeed.Etiqueta(b.Complexity)} está " +
                           $"en {celda.Points} puntos, y {(b.WorkType.Resta() ? "un retrabajo tiene que restar" : "eso no suma")}: " +
                           "corrige la matriz antes de publicar.", null, null, null);

        // ── PLAZO ────────────────────────────────────────────────────────────────
        //
        // El plazo SÍ se ajusta por actividad; los puntos no. La asimetría es deliberada: aflojar el
        // plazo no vale puntos, y el plazo real depende del trabajo concreto —un bug medio con un
        // cliente esperando no admite las mismas horas que uno cualquiera—.
        //
        // En un BUG el plazo es OBLIGATORIO y lo pone el líder: es el trato del modelo. Si se dejara
        // caer a la matriz cuando viene vacío, «cuando sea Bug, el plazo se lo pongo yo» dejaría de
        // ser cierto sin que nadie lo notara.
        if (b.WorkType.ComoBug() && b.HorasLimite is null)
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
        if (b.WorkType.ComoBug())
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

    /// <summary>
    /// El PLAZO de una actividad que empieza AHORA: el suyo propio manda sobre el de la matriz.
    ///
    /// <para>Cuenta desde ahora y no desde que se publicó, para que una actividad que esperó dos
    /// semanas en el pool no llegue con el plazo ya consumido. Y se suman HORAS, para que lo que se
    /// promete y lo que mide el cronómetro sean el mismo número. Son horas de reloj —incluyen noches
    /// y fines de semana—, que es lo coherente con medir contra un cronómetro; contar solo jornadas
    /// hábiles exigiría un calendario laboral entero.</para>
    ///
    /// <para>Está extraído porque hay DOS momentos en que una actividad empieza a correr: cuando
    /// alguien la toma del pool, y cuando el líder clasifica una propuesta que ya tenía dueño. Los
    /// dos tienen que calcular el mismo plazo o el segundo entregaría actividades sin fecha.</para>
    /// </summary>
    private static (decimal horas, DateTime? limite) PlazoDesdeAhora(
        PoolActivity actividad, PoolPointsMatrixEntry? celda, DateTime ahora)
    {
        decimal horas = actividad.HorasLimite ?? celda?.HorasLimite ?? 0m;
        return (horas, horas > 0 ? ahora.AddHours((double)horas) : null);
    }

    /// <summary>
    /// LO QUE HACE QUE UNA ACTIVIDAD ESTÉ DE VERDAD EN MARCHA: el checklist congelado y la percha
    /// del cronómetro.
    ///
    /// <para>Extraído por lo mismo que el plazo: tomar del pool y clasificar una propuesta con dueño
    /// desembocan los dos en «Tomada», y las dos cosas de aquí son las que la palabra promete. Una
    /// actividad tomada sin checklist no se puede entregar —entregar exige cumplirlo— y una sin
    /// percha no se puede cronometrar, así que olvidar cualquiera de las dos en el camino nuevo
    /// dejaría trabajo asignado que no se puede ni medir ni terminar.</para>
    ///
    /// <para>El enlace de vuelta se guarda en un SEGUNDO <c>SaveChanges</c> porque el identificador
    /// del cronómetro solo existe después de insertarlo.</para>
    /// </summary>
    private async Task ArrancarElTrabajoAsync(PoolActivity actividad, int developerId, CancellationToken ct)
    {
        await CopiarChecklistAsync(actividad, ct);
        var cronometro = CrearActividadEnlazada(actividad, developerId);
        await db.SaveChangesAsync(ct);

        actividad.LinkedDevActivityId = cronometro.Id;
        await db.SaveChangesAsync(ct);
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
            CreatedAt = DateTime.UtcNow,

            // LA MARCA DE PERCHA, y es lo único de aquí que sobrevive a soltar el reclamo. El
            // vínculo de vuelta (LinkedDevActivityId) lo borra SoltarReclamo, y sin esta marca la
            // percha de una actividad devuelta quedaba indistinguible de una actividad libre
            // cualquiera: cerrada, con tiempo medido y sin pagar. El líder podía calificarla por
            // puntos y el pool volvía a pagar cuando otro terminaba el trabajo.
            PoolActivityId = actividad.Id
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

        // El estado que se mandó a DevOps es DE ESTE RECLAMO, así que se va con él: quien tome la
        // actividad después tiene que volver a poner su work item en curso, aunque ya lo estuviera.
        // Reafirmar un estado que ya está allá no cuesta nada; no reafirmarlo dejaría el ticket
        // parado en la columna donde lo dejó el anterior, que es el problema que esto vino a
        // resolver.
        //
        // A nombre de quién quedó allá NO se toca, y son dos casos distintos a propósito: la
        // asignación se DEDUCE comparando la marca con quien la tiene tomada, así que sin dueño no
        // hay nada pendiente y el work item se queda a nombre del que lo trabajó. Desasignarlo sería
        // vaciar un campo que quizá puso otra persona para reflejar que aquí dejó de haber dueño.
        actividad.DevOpsEstadoEnviado  = null;

        // La estimación de un BUG es de quien lo tenía tomado, así que se va con él. Dejarla puesta
        // haría dos daños: quien lo tome después heredaría el número de otro, y la comprobación de
        // «un bug no se toma sin estimarlo» quedaría satisfecha por algo que esa persona no escribió.
        //
        // En tarea y requerimiento NO se toca: ahí la estimación es del líder y sigue siendo válida
        // con la actividad de vuelta en el pool. Es la segunda de las dos invariantes que permiten
        // derivar el autor de HorasEstimadas del tipo (la otra está en EditarAsync).
        if (actividad.WorkType.ComoBug())
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
