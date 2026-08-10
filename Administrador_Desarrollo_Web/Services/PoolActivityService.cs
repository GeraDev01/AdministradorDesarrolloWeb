using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

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
/// </summary>
public class PoolActivityService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly AuditService _audit;
    private readonly NotificationService _notifications;
    private readonly SettingsService _settings;

    public PoolActivityService(AppDbContext db, ICurrentUser currentUser, AuditService audit,
        NotificationService notifications, SettingsService settings)
    {
        _db = db; _currentUser = currentUser; _audit = audit;
        _notifications = notifications; _settings = settings;
    }

    /// <summary>Cuántas actividades puede tener alguien tomadas a la vez, si nadie lo configuró.</summary>
    public const int MaxTomadasPorOmision = 3;

    /// <summary>Clave del tope en la configuración, para que el líder lo ajuste sin recompilar.</summary>
    public const string ClaveMaxTomadas = "pool.max-tomadas";

    /// <summary>Tope del motivo de una devolución. Da para explicarse, no para un ensayo.</summary>
    public const int MaxMotivo = 1000;

    // ── Consultas ────────────────────────────────────────────────────────────────

    /// <summary>Lo que hay libre en el pool ahora mismo, de lo más valioso a lo menos.</summary>
    public List<PoolActivity> Disponibles(PoolWorkType? tipo = null, PoolComplexity? complejidad = null)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);

        var q = _db.PoolActivities.AsNoTracking()
            .Where(a => a.Status == PoolActivityStatus.Disponible);
        if (tipo is PoolWorkType t) q = q.Where(a => a.WorkType == t);
        if (complejidad is PoolComplexity c) q = q.Where(a => a.Complexity == c);

        return q.OrderByDescending(a => a.Points).ThenBy(a => a.CreatedAt).ToList();
    }

    /// <summary>Las actividades del pool de un desarrollador: en curso primero, aceptadas al final.</summary>
    public List<PoolActivity> MisDelPool(int developerId)
    {
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, developerId);

        return _db.PoolActivities.AsNoTracking()
            .Where(a => a.ClaimedByDeveloperId == developerId && a.Status != PoolActivityStatus.Disponible)
            .OrderBy(a => a.Status == PoolActivityStatus.Aceptada)   // lo pendiente arriba
            .ThenByDescending(a => a.ClaimedAt)
            .ToList();
    }

    /// <summary>Todas, para la pantalla del líder.</summary>
    public List<PoolActivity> Todas(PoolActivityStatus? estado = null, PoolWorkType? tipo = null)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var q = _db.PoolActivities.AsNoTracking().Include(a => a.ClaimedBy).AsQueryable();
        if (estado is PoolActivityStatus e) q = q.Where(a => a.Status == e);
        if (tipo is PoolWorkType t) q = q.Where(a => a.WorkType == t);

        return q.OrderBy(a => a.Status).ThenByDescending(a => a.CreatedAt).ToList();
    }

    /// <summary>Las que esperan verificación del líder.</summary>
    public List<PoolActivity> PendientesDeVerificar()
    {
        AuthorizationGuard.RequireAdmin(_currentUser);
        return _db.PoolActivities.AsNoTracking().Include(a => a.ClaimedBy)
            .Where(a => a.Status == PoolActivityStatus.EnRevision)
            .OrderBy(a => a.DeliveredAt)
            .ToList();
    }

    /// <summary>Cuántas esperan verificación. Para el aviso del menú; sin guarda porque solo cuenta.</summary>
    public int CuentaPendientesDeVerificar() =>
        _db.PoolActivities.AsNoTracking().Count(a => a.Status == PoolActivityStatus.EnRevision);

    /// <summary>
    /// El checklist de una actividad. Requiere sesión y nada más: lo consultan tanto quien la trabaja
    /// como el líder que la verifica, y no contiene nada privado —son los mismos puntos que la
    /// plantilla del tipo, más los enlaces de evidencia que el propio interesado capturó—.
    /// </summary>
    public List<PoolActivityChecklistItem> ChecklistDe(int poolActivityId)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        return _db.PoolActivityChecklistItems.AsNoTracking()
            .Where(c => c.PoolActivityId == poolActivityId)
            .OrderBy(c => c.Orden).ThenBy(c => c.Id)
            .ToList();
    }

    // ── Administración de la actividad ───────────────────────────────────────────

    /// <summary>
    /// Crea una actividad en el pool. Los puntos NO vienen del borrador: se leen de la matriz y se
    /// congelan aquí. Es la regla central del sistema y por eso vive en el servicio, no en la
    /// pantalla — que nadie pueda escribir el valor es lo que hace comparables las actividades.
    /// </summary>
    public (bool ok, string mensaje, PoolActivity? actividad) Crear(PoolActivity borrador)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var (valido, error, celda, enlace) = ValidarBorrador(borrador);
        if (!valido) return (false, error, null);

        var actividad = new PoolActivity
        {
            Title           = borrador.Title.Trim(),
            Description     = Limpiar(borrador.Description),
            WorkType        = borrador.WorkType,
            Complexity      = borrador.Complexity,
            Points          = celda!.Points,
            Status          = PoolActivityStatus.Disponible,
            ExternalUrl     = enlace,
            CreatedByUserId = _currentUser.UserId,
            CreatedAt       = DateTime.UtcNow
        };
        _db.PoolActivities.Add(actividad);
        GuardarSinEnvenenar(actividad);

        _audit.Record(AuditAction.Create, "PoolActivity", actividad.Id.ToString(),
            $"Actividad del pool «{actividad.Title}» ({PoolSeed.Etiqueta(actividad.WorkType)}/" +
            $"{PoolSeed.Etiqueta(actividad.Complexity)}, {actividad.Points} pts)");

        return (true, $"Actividad publicada en el pool: {actividad.Points} puntos.", actividad);
    }

    /// <summary>
    /// Edita una actividad que sigue en el pool. Solo mientras nadie la haya tomado: cambiarle el
    /// alcance o el valor a alguien que ya la está trabajando sería cambiar el trato a medio camino.
    /// Si cambia el tipo o la complejidad, los puntos se recongelan desde la matriz.
    /// </summary>
    public (bool ok, string mensaje) Editar(int id, PoolActivity cambios)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var actividad = _db.PoolActivities.FirstOrDefault(a => a.Id == id);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");
        _db.Entry(actividad).Reload();

        if (actividad.Status != PoolActivityStatus.Disponible)
            return (false, "Ya la tomó alguien: no se puede cambiar lo que vale ni lo que pide.");

        var (valido, error, celda, enlace) = ValidarBorrador(cambios);
        if (!valido) return (false, error);

        actividad.Title       = cambios.Title.Trim();
        actividad.Description = Limpiar(cambios.Description);
        actividad.WorkType    = cambios.WorkType;
        actividad.Complexity  = cambios.Complexity;
        actividad.Points      = celda!.Points;
        actividad.ExternalUrl = enlace;

        GuardarSinEnvenenar(actividad);
        _audit.Record(AuditAction.Update, "PoolActivity", actividad.Id.ToString(),
            $"Actividad del pool actualizada: {actividad.Points} pts");
        return (true, $"Actividad actualizada: {actividad.Points} puntos.");
    }

    /// <summary>Quita del pool una actividad que ya no aplica. Solo si nadie la tomó.</summary>
    public (bool ok, string mensaje) Retirar(int id)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var actividad = _db.PoolActivities.FirstOrDefault(a => a.Id == id);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");
        _db.Entry(actividad).Reload();

        if (actividad.Status != PoolActivityStatus.Disponible)
            return (false, "Solo se retira lo que sigue libre en el pool. Si alguien la tomó, usa «Liberar».");

        actividad.Status = PoolActivityStatus.Retirada;
        GuardarSinEnvenenar(actividad);
        _audit.Record(AuditAction.Update, "PoolActivity", actividad.Id.ToString(), "Retirada del pool");
        return (true, "Actividad retirada del pool.");
    }

    /// <summary>
    /// Devuelve al pool una actividad que alguien tomó y no avanza (se fue de vacaciones, se venció,
    /// cambió de prioridad). Es del líder porque afecta el trabajo de otra persona: por eso avisa.
    /// </summary>
    public (bool ok, string mensaje) Liberar(int id, string? motivo)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var actividad = _db.PoolActivities.FirstOrDefault(a => a.Id == id);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");
        _db.Entry(actividad).Reload();

        if (!actividad.EnCurso && actividad.Status != PoolActivityStatus.EnRevision)
            return (false, "Esa actividad no la tiene nadie.");

        int? devAnterior = actividad.ClaimedByDeveloperId;
        int devActivityId = actividad.LinkedDevActivityId ?? 0;
        motivo = Limpiar(motivo);

        AnotarEnHistorial(actividad, $"Liberada por {NombreDelUsuario()}" +
                                     (motivo != null ? $": {motivo}" : "") + ". Vuelve al pool.");
        SoltarReclamo(actividad);
        var borrados = BorrarChecklist(actividad.Id);
        GuardarSinEnvenenar(actividad, borrados);

        // Igual que en Devolver: el cronómetro se cierra después de que el estado ya esté firme.
        CerrarActividadEnlazada(devActivityId);

        _audit.Record(AuditAction.Update, "PoolActivity", actividad.Id.ToString(),
            $"Liberada al pool{(motivo != null ? $": {motivo}" : "")}");

        if (devAnterior is int dev)
        {
            try
            {
                _notifications.NotifyDeveloper(dev, NotificationKind.General,
                    "Una actividad del pool volvió al pool",
                    $"«{actividad.Title}» ya no está a tu nombre." + (motivo != null ? $" Motivo: {motivo}" : ""),
                    dedupeKey: $"pool-liberada-{actividad.Id}-{actividad.ReturnedCount}");
            }
            catch { /* la liberación ya está hecha; el aviso es cortesía */ }
        }

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
    public (bool ok, string mensaje) Tomar(int id, int developerId)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, developerId);

        var actividad = _db.PoolActivities.AsNoTracking().FirstOrDefault(a => a.Id == id);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");
        if (actividad.Status != PoolActivityStatus.Disponible)
            return (false, "Alguien más la tomó primero. Actualiza la lista.");

        int tope = TopeDeTomadas();
        int tomadas = _db.PoolActivities.AsNoTracking()
            .Count(a => a.ClaimedByDeveloperId == developerId
                     && (a.Status == PoolActivityStatus.Tomada || a.Status == PoolActivityStatus.Devuelta));
        if (tomadas >= tope)
            return (false, $"Ya tienes {tomadas} actividad(es) del pool sin entregar y el tope es {tope}. " +
                           "Termina o devuelve alguna antes de tomar otra.");

        var celda = CeldaDeMatriz(actividad.WorkType, actividad.Complexity);
        var ahora = DateTime.UtcNow;
        DateTime? limite = celda is { DiasLimite: > 0 } ? ahora.AddDays(celda.DiasLimite) : null;

        int ganadas = _db.PoolActivities
            .Where(a => a.Id == id && a.Status == PoolActivityStatus.Disponible)
            .ExecuteUpdate(s => s
                .SetProperty(a => a.Status, PoolActivityStatus.Tomada)
                .SetProperty(a => a.ClaimedByDeveloperId, developerId)
                .SetProperty(a => a.ClaimedAt, ahora)
                .SetProperty(a => a.ClaimDeadlineAt, limite));
        if (ganadas == 0) return (false, "Alguien más la tomó primero. Actualiza la lista.");

        // El reclamo ya es firme; a partir de aquí se trabaja sobre la entidad rastreada. Reload
        // porque el UPDATE de arriba pasó por encima del rastreador y la copia en memoria del
        // contexto Singleton todavía diría «Disponible».
        var rastreada = _db.PoolActivities.FirstOrDefault(a => a.Id == id);
        if (rastreada != null) _db.Entry(rastreada).Reload();
        rastreada ??= actividad;

        var checklist = CopiarChecklist(rastreada);
        rastreada.LinkedDevActivityId = CrearActividadEnlazada(rastreada, developerId);
        GuardarSinEnvenenar(rastreada, checklist);

        _audit.Record(AuditAction.Update, "PoolActivity", id.ToString(),
            $"Tomada del pool ({actividad.Points} pts)");

        var textoLimite = limite is DateTime f
            ? $" Fecha esperada de entrega: {f.ToLocalTime():dd/MM/yyyy}."
            : "";
        return (true, $"La actividad es tuya: {actividad.Points} puntos al aceptarse.{textoLimite} " +
                      "Completa el checklist para poder entregarla.");
    }

    /// <summary>
    /// El desarrollador la devuelve al pool. No se castiga: penalizarlo haría que nadie se atreviera
    /// con lo difícil, que es justo lo contrario de lo que se busca. Lo que sí queda es la cuenta
    /// (<see cref="PoolActivity.ReturnedCount"/>) y el motivo en el historial.
    /// </summary>
    public (bool ok, string mensaje) Devolver(int id, int developerId, string? motivo)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, developerId);

        var actividad = _db.PoolActivities.FirstOrDefault(a => a.Id == id);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");
        _db.Entry(actividad).Reload();

        if (actividad.ClaimedByDeveloperId != developerId)
            return (false, "Esa actividad no es tuya.");
        if (!actividad.EnCurso)
            return (false, actividad.Status == PoolActivityStatus.EnRevision
                ? "Ya la entregaste: espera la revisión del líder."
                : "Esa actividad ya no está en curso.");

        motivo = Limpiar(motivo);
        AnotarEnHistorial(actividad, $"Devuelta al pool por {NombreDelUsuario()}" +
                                     (motivo != null ? $": {motivo}" : "") + ".");

        // El cronómetro se cierra DESPUÉS de guardar: cerrarlo antes metía un SaveChanges a medio
        // camino que cometía el ReturnedCount++ sin el resto del reset, y si el guardado final
        // fallaba, el reintento volvía a incrementarlo.
        int devActivityId = actividad.LinkedDevActivityId ?? 0;

        actividad.ReturnedCount++;
        SoltarReclamo(actividad);
        var borrados = BorrarChecklist(actividad.Id);
        GuardarSinEnvenenar(actividad, borrados);

        CerrarActividadEnlazada(devActivityId);

        _audit.Record(AuditAction.Update, "PoolActivity", actividad.Id.ToString(),
            $"Devuelta al pool (van {actividad.ReturnedCount})");
        return (true, "Actividad devuelta al pool. Cualquiera puede tomarla.");
    }

    /// <summary>
    /// Marca o desmarca un punto del checklist. Los que exigen evidencia no se pueden marcar sin un
    /// enlace válido: sin eso, marcar una casilla no cuesta nada y la verificación se queda sin
    /// nada que mirar.
    /// </summary>
    public (bool ok, string mensaje) MarcarItem(int itemId, int developerId, bool hecho, string? evidencia)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, developerId);

        var item = _db.PoolActivityChecklistItems.FirstOrDefault(c => c.Id == itemId);
        if (item == null) return (false, "Ese punto ya no existe. Actualiza la lista.");
        _db.Entry(item).Reload();

        var actividad = _db.PoolActivities.AsNoTracking().FirstOrDefault(a => a.Id == item.PoolActivityId);
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

        try { _db.SaveChanges(); }
        catch { _db.Entry(item).State = EntityState.Detached; throw; }

        return (true, hecho ? "Punto marcado." : "Punto desmarcado.");
    }

    /// <summary>
    /// El desarrollador entrega la actividad. El checklist completo es la condición: es lo que
    /// convierte «ya está» en algo comprobable, y lo que hace que la verificación del líder sea un
    /// vistazo a la evidencia y no una discusión sobre si el trabajo alcanza.
    /// </summary>
    public (bool ok, string mensaje) Entregar(int id, int developerId)
    {
        AuthorizationGuard.RequireLoggedIn(_currentUser);
        AuthorizationGuard.RequireOwnershipOrAdmin(_currentUser, developerId);

        var actividad = _db.PoolActivities.FirstOrDefault(a => a.Id == id);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");
        _db.Entry(actividad).Reload();

        if (actividad.ClaimedByDeveloperId != developerId)
            return (false, "Esa actividad no es tuya.");
        if (!actividad.EnCurso)
            return (false, actividad.Status == PoolActivityStatus.EnRevision
                ? "Ya está entregada, esperando revisión."
                : "Esa actividad ya no está en curso.");

        var checklist = ChecklistDe(actividad.Id);
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
        GuardarSinEnvenenar(actividad);

        _audit.Record(AuditAction.Update, "PoolActivity", actividad.Id.ToString(),
            $"Entregada para verificación (vuelta {actividad.ReviewRound + 1})");
        AvisarALosLideres(actividad);

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
    public (bool ok, string mensaje) Aceptar(int id)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var actividad = _db.PoolActivities.AsNoTracking().FirstOrDefault(a => a.Id == id);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");

        if (actividad.Status == PoolActivityStatus.Aceptada || actividad.PointEntryId != null)
            return (false, "Esa actividad ya estaba aceptada; sus puntos ya se abonaron.");
        if (actividad.Status != PoolActivityStatus.EnRevision)
            return (false, "Solo se aceptan actividades entregadas y pendientes de verificar.");
        if (actividad.ClaimedByDeveloperId is not int developerId)
            return (false, "Esa actividad no tiene dueño: no hay a quién abonarle los puntos.");

        var criterio = CriterioDe(actividad.WorkType);
        if (criterio == null)
            return (false, "Falta el criterio del pool en el catálogo. Reinicia la aplicación para que se siembre.");

        var ahora = DateTime.Now;   // local: el período se imputa al mes del calendario de la gente
        var revisadoUtc = DateTime.UtcNow;
        var entrada = new PointEntry
        {
            DeveloperId      = developerId,
            CriterionId      = criterio.Id,
            Points           = actividad.Points,
            Year             = ahora.Year,
            Month            = ahora.Month,
            Comment          = $"Pool #{actividad.Id}: {actividad.Title}",
            AssignedByUserId = _currentUser.UserId,
            ReviewedByUserId = _currentUser.UserId,
            ReviewedAt       = revisadoUtc,
            ApprovalStatus   = PointApprovalStatus.Aprobado,
            Date             = revisadoUtc,
            EvidenceUrl      = PrimeraEvidencia(actividad.Id) ?? actividad.ExternalUrl
        };

        var historial = HistorialCon(actividad, $"Aceptada por {NombreDelUsuario()}: +{actividad.Points} pts.");
        int revisor = _currentUser.UserId ?? 0;

        using var tx = _db.Database.BeginTransaction();
        try
        {
            // El UPDATE condicional ES la guarda: si otra sesión ya la aceptó, esto afecta 0 filas.
            int ganadas = _db.PoolActivities
                .Where(a => a.Id == id
                         && a.Status == PoolActivityStatus.EnRevision
                         && a.PointEntryId == null)
                .ExecuteUpdate(s => s
                    .SetProperty(a => a.Status, PoolActivityStatus.Aceptada)
                    .SetProperty(a => a.ReviewedByUserId, revisor)
                    .SetProperty(a => a.ReviewedAt, revisadoUtc)
                    .SetProperty(a => a.ReviewComment, (string?)null)
                    .SetProperty(a => a.ReviewHistory, historial));
            if (ganadas == 0)
            {
                tx.Rollback();
                return (false, "Esa actividad ya estaba aceptada; sus puntos ya se abonaron.");
            }

            _db.PointEntries.Add(entrada);
            _db.SaveChanges();

            // La traza a la entrada va en la misma transacción: si algo falla, no queda una actividad
            // aceptada sin sus puntos ni unos puntos sin actividad que los justifique.
            _db.PoolActivities.Where(a => a.Id == id)
                .ExecuteUpdate(s => s.SetProperty(a => a.PointEntryId, entrada.Id));

            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { /* la conexión pudo caerse: el rollback lo hará el motor */ }
            _db.Entry(entrada).State = EntityState.Detached;
            throw;
        }

        // La copia rastreada del contexto Singleton quedó vieja: los UPDATE de arriba pasaron por
        // encima del rastreador. Sin esto, cualquier pantalla que la tuviera cargada la seguiría
        // mostrando «Por verificar».
        RefrescarSiEstaRastreada(id);

        // Con el Id de la ACTIVIDAD LIBRE, no con el de la del pool. Son dos tablas distintas y sus
        // Id no tienen nada que ver: pasar el equivocado cerraba la actividad libre de otra persona
        // —la que por casualidad tuviera ese número— y dejaba abierto el cronómetro de esta.
        CerrarActividadEnlazada(actividad.LinkedDevActivityId ?? 0);

        _audit.Record(AuditAction.Update, "PoolActivity", id.ToString(),
            $"Aceptada: +{actividad.Points} pts a {NombreDeDesarrollador(developerId)}");

        try
        {
            _notifications.NotifyDeveloper(developerId, NotificationKind.General,
                "Tu actividad del pool fue aceptada",
                $"«{actividad.Title}»: +{actividad.Points} puntos, ya cuentan en el ranking de {ahora:MMMM}.",
                dedupeKey: $"pool-aceptada-{id}");
        }
        catch { /* los puntos ya están abonados: un aviso fallido no puede tumbar la operación */ }

        return (true, $"Aceptada. Se abonaron {actividad.Points} puntos a " +
                      $"{NombreDeDesarrollador(developerId)} en {ahora:MM/yyyy}.");
    }

    /// <summary>
    /// El líder la devuelve al desarrollador con un motivo. El motivo es obligatorio: sin él, quien
    /// la recibe no sabe qué arreglar y la va a volver a entregar igual.
    ///
    /// La actividad NO vuelve al pool: sigue siendo de quien la tomó, que corrige y la entrega otra
    /// vez. Su checklist se conserva —lo hecho está hecho—; lo que se marca es la vuelta.
    /// </summary>
    public (bool ok, string mensaje) Rechazar(int id, string motivo)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        motivo = (motivo ?? "").Trim();
        if (motivo.Length == 0) return (false, "Escribe qué falta: es lo que la persona va a leer para corregirlo.");
        if (motivo.Length > MaxMotivo) return (false, $"El motivo no puede pasar de {MaxMotivo} caracteres.");

        var actividad = _db.PoolActivities.FirstOrDefault(a => a.Id == id);
        if (actividad == null) return (false, "Esa actividad ya no existe. Actualiza la lista.");
        _db.Entry(actividad).Reload();

        if (actividad.Status != PoolActivityStatus.EnRevision)
            return (false, actividad.Status == PoolActivityStatus.Aceptada
                ? "Esa actividad ya fue aceptada."
                : "Solo se devuelven actividades entregadas y pendientes de verificar.");

        actividad.Status           = PoolActivityStatus.Devuelta;
        actividad.ReviewComment    = motivo;
        actividad.ReviewedByUserId = _currentUser.UserId;
        actividad.ReviewedAt       = DateTime.UtcNow;
        actividad.ReviewRound++;
        AnotarEnHistorial(actividad, $"Devuelta por {NombreDelUsuario()}: {motivo}");
        GuardarSinEnvenenar(actividad);

        _audit.Record(AuditAction.Update, "PoolActivity", actividad.Id.ToString(),
            $"Devuelta para corregir (vuelta {actividad.ReviewRound}): {motivo}");

        if (actividad.ClaimedByDeveloperId is int dev)
            _notifications.NotifyDeveloper(dev, NotificationKind.General,
                "Te devolvieron una actividad del pool",
                $"«{actividad.Title}»: {motivo}",
                dedupeKey: $"pool-devuelta-{actividad.Id}-{actividad.ReviewRound}");

        return (true, "Devuelta al desarrollador con tu comentario.");
    }

    // ── Matriz y plantillas ──────────────────────────────────────────────────────

    public List<PoolPointsMatrixEntry> ObtenerMatriz() =>
        _db.PoolPointsMatrix.AsNoTracking()
            .OrderBy(m => m.WorkType).ThenBy(m => m.Complexity)
            .ToList();

    /// <summary>
    /// Guarda la matriz completa. Cambiarla NO revalúa nada de lo ya creado: las actividades llevan
    /// sus puntos congelados. Solo afecta a las que se creen a partir de ahora.
    /// </summary>
    public (bool ok, string mensaje) GuardarMatriz(IReadOnlyList<PoolPointsMatrixEntry> filas)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        foreach (var f in filas)
        {
            if (f.Points <= 0)
                return (false, $"{PoolSeed.Etiqueta(f.WorkType)} / {PoolSeed.Etiqueta(f.Complexity)}: " +
                               "los puntos tienen que ser mayores que cero.");
            if (f.DiasLimite < 0)
                return (false, $"{PoolSeed.Etiqueta(f.WorkType)} / {PoolSeed.Etiqueta(f.Complexity)}: " +
                               "los días no pueden ser negativos.");
        }

        var actuales = _db.PoolPointsMatrix.ToList();
        var tocadas = new List<PoolPointsMatrixEntry>();
        int cambios = 0;
        foreach (var f in filas)
        {
            var fila = actuales.FirstOrDefault(m => m.WorkType == f.WorkType && m.Complexity == f.Complexity);
            if (fila == null)
            {
                var nueva = new PoolPointsMatrixEntry
                {
                    WorkType = f.WorkType, Complexity = f.Complexity,
                    Points = f.Points, DiasLimite = f.DiasLimite,
                    UpdatedAt = DateTime.UtcNow, UpdatedByUserId = _currentUser.UserId
                };
                _db.PoolPointsMatrix.Add(nueva);
                tocadas.Add(nueva);
                cambios++;
                continue;
            }

            if (fila.Points == f.Points && fila.DiasLimite == f.DiasLimite) continue;
            fila.Points          = f.Points;
            fila.DiasLimite      = f.DiasLimite;
            fila.UpdatedAt       = DateTime.UtcNow;
            fila.UpdatedByUserId = _currentUser.UserId;
            tocadas.Add(fila);
            cambios++;
        }

        if (cambios == 0) return (true, "No hubo cambios que guardar.");

        try { _db.SaveChanges(); }
        catch
        {
            // Contexto compartido: unas celdas a medio cambiar no pueden quedarse pendientes, o el
            // siguiente SaveChanges de otra pantalla las cometería con valores que nadie confirmó.
            foreach (var m in tocadas) _db.Entry(m).State = EntityState.Detached;
            throw;
        }
        _audit.Record(AuditAction.ConfigChange, "PoolPointsMatrix", null,
            $"Matriz de puntos del pool actualizada ({cambios} celda(s))");
        return (true, $"Matriz guardada ({cambios} celda(s)). Las actividades ya creadas conservan sus puntos.");
    }

    public List<PoolChecklistTemplateItem> Plantilla(PoolWorkType tipo, bool incluirInactivos = false)
    {
        var q = _db.PoolChecklistTemplateItems.AsNoTracking().Where(t => t.WorkType == tipo);
        if (!incluirInactivos) q = q.Where(t => t.IsActive);
        return q.OrderBy(t => t.Orden).ThenBy(t => t.Id).ToList();
    }

    /// <summary>Alta o edición de un punto del checklist de un tipo. Id = 0 significa alta.</summary>
    public (bool ok, string mensaje) GuardarPlantillaItem(PoolChecklistTemplateItem item)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var texto = (item.Text ?? "").Trim();
        if (texto.Length == 0) return (false, "Escribe qué hay que cumplir.");
        if (texto.Length > 300) return (false, "El texto no puede pasar de 300 caracteres.");

        if (item.Id == 0)
        {
            int siguiente = _db.PoolChecklistTemplateItems
                .Where(t => t.WorkType == item.WorkType)
                .Select(t => (int?)t.Orden).Max() ?? 0;

            var nuevo = new PoolChecklistTemplateItem
            {
                WorkType = item.WorkType, Text = texto, Orden = siguiente + 10,
                RequiereEvidencia = item.RequiereEvidencia, IsActive = true
            };
            _db.PoolChecklistTemplateItems.Add(nuevo);
            GuardarPlantillaSinEnvenenar(nuevo);
            _audit.Record(AuditAction.Create, "PoolChecklistTemplate", null,
                $"Punto de checklist ({PoolSeed.Etiqueta(item.WorkType)}): {texto}");
            return (true, "Punto agregado. Aplica a las actividades que se tomen a partir de ahora.");
        }

        var existente = _db.PoolChecklistTemplateItems.FirstOrDefault(t => t.Id == item.Id);
        if (existente == null) return (false, "Ese punto ya no existe. Actualiza la lista.");
        _db.Entry(existente).Reload();

        existente.Text              = texto;
        existente.RequiereEvidencia = item.RequiereEvidencia;
        existente.Orden             = item.Orden;
        GuardarPlantillaSinEnvenenar(existente);

        _audit.Record(AuditAction.Update, "PoolChecklistTemplate", existente.Id.ToString(),
            $"Punto de checklist actualizado: {texto}");
        return (true, "Punto actualizado. Las actividades ya tomadas conservan el checklist que recibieron.");
    }

    /// <summary>
    /// Desactiva un punto en lugar de borrarlo: las actividades en curso llevan su propia copia,
    /// pero borrarlo haría perder qué se pedía cuando se hicieron las anteriores.
    /// </summary>
    public (bool ok, string mensaje) DesactivarPlantillaItem(int itemId)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var item = _db.PoolChecklistTemplateItems.FirstOrDefault(t => t.Id == itemId);
        if (item == null) return (false, "Ese punto ya no existe. Actualiza la lista.");
        _db.Entry(item).Reload();

        item.IsActive = !item.IsActive;
        GuardarPlantillaSinEnvenenar(item);

        _audit.Record(AuditAction.Update, "PoolChecklistTemplate", item.Id.ToString(),
            item.IsActive ? $"Punto reactivado: {item.Text}" : $"Punto desactivado: {item.Text}");
        return (true, item.IsActive ? "Punto reactivado." : "Punto desactivado. Deja de pedirse en las actividades nuevas.");
    }

    // ── Interno ──────────────────────────────────────────────────────────────────

    private (bool ok, string error, PoolPointsMatrixEntry? celda, string? enlace) ValidarBorrador(PoolActivity b)
    {
        var titulo = (b.Title ?? "").Trim();
        if (titulo.Length == 0) return (false, "Escribe un título para la actividad.", null, null);
        if (titulo.Length > 200) return (false, "El título no puede pasar de 200 caracteres.", null, null);

        var celda = CeldaDeMatriz(b.WorkType, b.Complexity);
        if (celda == null)
            return (false, $"No hay puntos configurados para {PoolSeed.Etiqueta(b.WorkType)} / " +
                           $"{PoolSeed.Etiqueta(b.Complexity)}. Captúralos en la pestaña de configuración.", null, null);
        if (celda.Points <= 0)
            return (false, $"{PoolSeed.Etiqueta(b.WorkType)} / {PoolSeed.Etiqueta(b.Complexity)} vale " +
                           "0 puntos: corrige la matriz antes de publicar.", null, null);

        // El mismo validador que la autocalificación: solo http/https, porque el líder abre el
        // enlace con el navegador del sistema al verificar.
        var (enlaceOk, enlaceError, enlace) = PerformanceScoringService.NormalizarEnlace(b.ExternalUrl);
        if (!enlaceOk) return (false, enlaceError, null, null);

        return (true, "", celda, enlace);
    }

    private PoolPointsMatrixEntry? CeldaDeMatriz(PoolWorkType tipo, PoolComplexity complejidad) =>
        _db.PoolPointsMatrix.AsNoTracking()
            .FirstOrDefault(m => m.WorkType == tipo && m.Complexity == complejidad);

    private ScoringCriterion? CriterioDe(PoolWorkType tipo)
    {
        var nombre = PoolSeed.NombreCriterio(tipo);
        return _db.ScoringCriteria.AsNoTracking().FirstOrDefault(c => c.Name == nombre);
    }

    private int TopeDeTomadas()
    {
        var valor = _settings.Get(ClaveMaxTomadas);
        return int.TryParse(valor, out int n) && n > 0 ? n : MaxTomadasPorOmision;
    }

    /// <summary>
    /// Copia el checklist vigente del tipo a la actividad (ver el porqué en el modelo). Devuelve los
    /// items que deja pendientes en el contexto, para poder desanclarlos si el guardado falla.
    /// </summary>
    private List<PoolActivityChecklistItem> CopiarChecklist(PoolActivity actividad)
    {
        var tocados = BorrarChecklist(actividad.Id);   // por si vuelve a tomarse tras una devolución

        foreach (var plantilla in Plantilla(actividad.WorkType))
        {
            var item = new PoolActivityChecklistItem
            {
                PoolActivityId    = actividad.Id,
                Text              = plantilla.Text,
                Orden             = plantilla.Orden,
                RequiereEvidencia = plantilla.RequiereEvidencia
            };
            _db.PoolActivityChecklistItems.Add(item);
            tocados.Add(item);
        }
        return tocados;
    }

    /// <summary>Marca el checklist para borrado y devuelve lo que quedó pendiente en el contexto.</summary>
    private List<PoolActivityChecklistItem> BorrarChecklist(int poolActivityId)
    {
        var items = _db.PoolActivityChecklistItems.Where(c => c.PoolActivityId == poolActivityId).ToList();
        if (items.Count > 0) _db.PoolActivityChecklistItems.RemoveRange(items);
        return items;
    }

    /// <summary>
    /// Crea la actividad libre enlazada con la que se cronometra el trabajo. Es lo que permite usar
    /// el cronómetro que ya existe sin añadirle un tercer tipo de objetivo (ver el modelo).
    ///
    /// Este SaveChanges comete también el reclamo de la actividad, porque el contexto es compartido
    /// y guarda todo lo pendiente. Está bien que así sea: el reclamo es lo importante y ya está
    /// decidido a estas alturas; lo que sigue (guardar el enlace) es un retoque.
    /// </summary>
    private int? CrearActividadEnlazada(PoolActivity actividad, int developerId)
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

        try
        {
            _db.DevActivities.Add(libre);
            _db.SaveChanges();
            return libre.Id;
        }
        catch
        {
            // Que falle el cronómetro no debe impedir tomar la actividad: se puede trabajar sin
            // cronometrar, y quedarse sin poder tomar nada sería mucho peor.
            //
            // Pero la fila fallida NO puede quedarse Added en el contexto compartido: el siguiente
            // SaveChanges de cualquier pantalla volvería a intentar insertarla, y arrastraría ese
            // error a una operación que no tiene nada que ver.
            _db.Entry(libre).State = EntityState.Detached;
            return null;
        }
    }

    /// <summary>
    /// Cierra la actividad libre del cronómetro. Se llama SIEMPRE al final, con el estado de la
    /// actividad del pool ya guardado: hacerlo antes metía un SaveChanges a media operación que
    /// cometía cambios sin terminar.
    /// </summary>
    private void CerrarActividadEnlazada(int devActivityId)
    {
        if (devActivityId <= 0) return;

        DevActivity? libre = null;
        try
        {
            libre = _db.DevActivities.FirstOrDefault(a => a.Id == devActivityId);
            if (libre == null || libre.Status == DevActivityStatus.Cerrada) return;
            libre.Status   = DevActivityStatus.Cerrada;
            libre.ClosedAt = DateTime.UtcNow;
            _db.SaveChanges();
        }
        catch
        {
            // Cerrar el cronómetro es cortesía: lo importante (los puntos, o el reset del reclamo)
            // ya está guardado. Pero la fila a medio modificar no puede quedarse en el contexto
            // compartido, o el siguiente SaveChanges de otra pantalla la cometería.
            if (libre != null) { try { _db.Entry(libre).State = EntityState.Detached; } catch { } }
        }
    }

    /// <summary>
    /// Sincroniza la copia rastreada tras un UPDATE que pasó por encima del rastreador. Sin esto, la
    /// pantalla que tuviera esa actividad cargada seguiría mostrando el estado anterior.
    /// </summary>
    private void RefrescarSiEstaRastreada(int id)
    {
        try
        {
            var rastreada = _db.ChangeTracker.Entries<PoolActivity>()
                .FirstOrDefault(e => e.Entity.Id == id);
            rastreada?.Reload();
        }
        catch { /* refrescar una copia en memoria nunca debe tumbar la operación */ }
    }

    private void SoltarReclamo(PoolActivity actividad)
    {
        actividad.Status               = PoolActivityStatus.Disponible;
        actividad.ClaimedByDeveloperId = null;
        actividad.ClaimedAt            = null;
        actividad.ClaimDeadlineAt      = null;
        actividad.DeliveredAt          = null;
        actividad.ReviewComment        = null;
        actividad.LinkedDevActivityId  = null;
    }

    private string? PrimeraEvidencia(int poolActivityId) =>
        _db.PoolActivityChecklistItems.AsNoTracking()
            .Where(c => c.PoolActivityId == poolActivityId && c.EvidenceUrl != null)
            .OrderBy(c => c.Orden)
            .Select(c => c.EvidenceUrl)
            .FirstOrDefault();

    private void AvisarALosLideres(PoolActivity actividad)
    {
        try
        {
            var lideres = _db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.Role == UserRole.Admin)
                .Select(u => u.Id).ToList();

            foreach (var userId in lideres)
                _notifications.Notify(userId, NotificationKind.General,
                    "Hay una actividad del pool por verificar",
                    $"«{actividad.Title}» ({actividad.Points} pts) — la entregó " +
                    $"{NombreDeDesarrollador(actividad.ClaimedByDeveloperId)}.",
                    dedupeKey: $"pool-review-{actividad.Id}-{actividad.ReviewRound}");
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

    /// <summary>
    /// Guarda dejando el contexto limpio si falla. El AppDbContext es Singleton y compartido: lo que
    /// quede Modified/Added/Deleted tras un error lo cometería el siguiente SaveChanges de cualquier
    /// otra pantalla — incluido el de la propia bitácora.
    ///
    /// Hay que desanclar TODO lo que la operación dejó pendiente, no solo la actividad: varias de
    /// ellas mueven también su checklist, y unos items en Deleted que se cometen más tarde dejarían
    /// a alguien sin el checklist —y sin las evidencias que ya había capturado— de una actividad que
    /// sigue siendo suya.
    /// </summary>
    private void GuardarSinEnvenenar(PoolActivity actividad,
        IReadOnlyCollection<PoolActivityChecklistItem>? checklistTocado = null)
    {
        try { _db.SaveChanges(); }
        catch
        {
            _db.Entry(actividad).State = EntityState.Detached;
            if (checklistTocado != null)
                foreach (var item in checklistTocado)
                {
                    try { _db.Entry(item).State = EntityState.Detached; } catch { }
                }
            throw;
        }
    }

    /// <summary>Lo mismo que <see cref="GuardarSinEnvenenar"/>, para los puntos de la plantilla.</summary>
    private void GuardarPlantillaSinEnvenenar(PoolChecklistTemplateItem item)
    {
        try { _db.SaveChanges(); }
        catch
        {
            _db.Entry(item).State = EntityState.Detached;
            throw;
        }
    }

    private string NombreDelUsuario() =>
        _currentUser.User?.FullName ?? _currentUser.Username ?? "el líder";

    private string NombreDeDesarrollador(int? developerId) =>
        developerId is int id
            ? _db.Developers.AsNoTracking().Where(d => d.Id == id).Select(d => d.FullName).FirstOrDefault() ?? "(sin ficha)"
            : "(sin dueño)";

    private static string? Limpiar(string? texto)
    {
        texto = (texto ?? "").Trim();
        return texto.Length == 0 ? null : texto;
    }

    private static string Recortar(string texto, int tope) =>
        texto.Length <= tope ? texto : texto[..tope];
}
