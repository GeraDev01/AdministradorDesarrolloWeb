using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Cómo va el sprint, en números comparables: el avance real de sus requerimientos contra el
/// tiempo de calendario consumido. Todo lo que la pantalla pinta sale de aquí.
/// </summary>
/// <param name="TotalRequerimientos">Los del sprint, sin contar cancelados.</param>
/// <param name="Entregados">Con estado Entregado.</param>
/// <param name="EnCurso">En desarrollo, en pruebas o por entregar.</param>
/// <param name="SinEmpezar">Por estimar o estimados.</param>
/// <param name="Cancelados">Se muestran aparte; no cuentan en el avance.</param>
/// <param name="AvanceRealPct">Promedio del % de avance (Entregado cuenta como 100).</param>
/// <param name="TiempoPct">% de días naturales del sprint ya consumidos.</param>
/// <param name="DiasTotales">Días naturales del sprint, extremos inclusive.</param>
/// <param name="DiasTranscurridos">Consumidos hasta hoy (0 antes de empezar; el total al acabar).</param>
/// <param name="DiasRestantes">Los que quedan DESPUÉS de hoy (hoy ya cuenta como transcurrido; 0 el último día).</param>
/// <param name="Veredicto">La lectura de un vistazo: adelantado, al día, atrasado…</param>
public sealed record SprintAvance(
    int TotalRequerimientos, int Entregados, int EnCurso, int SinEmpezar, int Cancelados,
    int AvanceRealPct, int TiempoPct,
    int DiasTotales, int DiasTranscurridos, int DiasRestantes,
    string Veredicto);

/// <summary>Cómo terminó (o va) un sprint, para la vista de histórico y velocidad.</summary>
/// <param name="Total">Requerimientos del sprint sin contar cancelados.</param>
/// <param name="CompletadoPct">Entregados sobre el total; 0 si el sprint no tiene nada.</param>
/// <param name="Cerrado">Su última fecha ya pasó. Solo los cerrados cuentan para la velocidad.</param>
public sealed record SprintResumen(
    int SprintId, string Name, DateTime StartDate, DateTime EndDate,
    int Total, int Entregados, int Cancelados, int CompletadoPct, int DiasTotales, bool Cerrado);

/// <summary>
/// Sprints: el administrador fija las fechas, cuelga requerimientos y ve el avance en una línea
/// de tiempo.
///
/// LECTURA (Listar, Requerimientos, Avance, MisRequerimientos): también del desarrollador. La
/// mitad del valor de un sprint es que el equipo vea la MISMA verdad; uno que solo ve el jefe
/// genera la pregunta diaria de «¿cómo vamos?» que la pantalla venía a eliminar.
/// ESCRITURA (Crear, Actualizar, Eliminar, FijarRequerimientos, Historico): solo administrador —
/// él compromete el alcance y él responde por él.
/// Operaciones queda fuera de todo: su alcance son los despliegues.
/// </summary>
public class SprintService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly AuditService _audit;

    public SprintService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
    {
        _db = db; _currentUser = currentUser; _audit = audit;
    }

    public const int MaxNombre = 100;
    public const int MaxObjetivo = 1000;

    /// <summary>Tope de duración. Un «sprint» de un año es un roadmap capturado en el lugar equivocado.</summary>
    public const int MaxDias = 120;

    // ── Consultas ────────────────────────────────────────────────────────────────

    /// <summary>Todos los sprints, el más reciente primero.</summary>
    public List<Sprint> Listar()
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(_currentUser, "de consulta del sprint");
        return _db.Sprints.AsNoTracking()
            .OrderByDescending(s => s.StartDate).ThenByDescending(s => s.Id)
            .ToList();
    }

    /// <summary>Los requerimientos del sprint (AsNoTracking: el contexto es Singleton).</summary>
    public List<Requirement> Requerimientos(int sprintId)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(_currentUser, "de consulta del sprint");
        return _db.Requirements.AsNoTracking()
            .Where(r => r.SprintId == sprintId)
            .OrderBy(r => r.Status).ThenBy(r => r.CommittedDeliveryDate ?? DateTime.MaxValue)
            .ToList();
    }

    /// <summary>
    /// Los ids de los requerimientos del sprint asignados a QUIEN CONSULTA, para resaltarlos.
    /// Vacío si la cuenta no está ligada a una ficha de desarrollador (caso real: cuentas de
    /// administración sin ficha) — la pantalla lo dice en vez de resaltar nada.
    /// </summary>
    public HashSet<int> MisRequerimientos(int sprintId)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(_currentUser, "de consulta del sprint");
        if (_currentUser.DeveloperId is not int devId) return [];
        return _db.Requirements.AsNoTracking()
            .Where(r => r.SprintId == sprintId && r.Assignments.Any(a => a.DeveloperId == devId))
            .Select(r => r.Id)
            .ToHashSet();
    }

    /// <summary>
    /// Todos los sprints con su resultado, del más viejo al más nuevo (así la gráfica se lee de
    /// izquierda a derecha en el tiempo). Es la vista de VELOCIDAD: con tres o cuatro sprints
    /// cerrados se sabe cuánto entrega el equipo de verdad, que es la única base honesta para
    /// comprometer el siguiente.
    /// </summary>
    public List<SprintResumen> Historico()
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var sprints = _db.Sprints.AsNoTracking().OrderBy(s => s.StartDate).ThenBy(s => s.Id).ToList();
        if (sprints.Count == 0) return [];

        // Un solo agregado en la base en vez de una consulta por sprint: con veinte sprints, el
        // N+1 se nota en una base remota.
        var conteos = _db.Requirements.AsNoTracking()
            .Where(r => r.SprintId != null)
            .GroupBy(r => new { SprintId = r.SprintId!.Value, r.Status })
            .Select(g => new { g.Key.SprintId, g.Key.Status, Cuantos = g.Count() })
            .ToList();

        var hoy = DateTime.Today;
        return sprints.Select(s =>
        {
            var suyos = conteos.Where(c => c.SprintId == s.Id).ToList();
            int cancelados = suyos.Where(c => c.Status == RequirementStatus.Cancelado).Sum(c => c.Cuantos);
            int total      = suyos.Sum(c => c.Cuantos) - cancelados;
            int entregados = suyos.Where(c => c.Status == RequirementStatus.Entregado).Sum(c => c.Cuantos);

            return new SprintResumen(
                s.Id, s.Name, s.StartDate, s.EndDate,
                Total: total, Entregados: entregados, Cancelados: cancelados,
                CompletadoPct: total == 0 ? 0 : (int)Math.Round(entregados * 100.0 / total, MidpointRounding.AwayFromZero),
                DiasTotales: (s.EndDate.Date - s.StartDate.Date).Days + 1,
                Cerrado: hoy > s.EndDate.Date);
        }).ToList();
    }

    /// <summary>
    /// Promedio de requerimientos entregados por sprint CERRADO Y CON TRABAJO — la velocidad del
    /// equipo. Los sprints en curso quedan fuera a propósito: uno que empezó ayer arrastraría el
    /// promedio hacia abajo y haría creer que el equipo rinde menos de lo que rinde.
    /// </summary>
    public static (double velocidad, int sprintsContados) Velocidad(IReadOnlyList<SprintResumen> historico)
    {
        // Con trabajo comprometido: un sprint cerrado que nunca se pobló —o al que le cancelaron
        // todo— aportaría un cero que no es un fracaso y hundiría el promedio. Uno con Total > 0 y
        // cero entregados SÍ cuenta: ahí el cero es real.
        var cerrados = historico.Where(h => h.Cerrado && h.Total > 0).ToList();
        if (cerrados.Count == 0) return (0, 0);
        return (cerrados.Average(h => h.Entregados), cerrados.Count);
    }

    // ── Alta, edición y baja ─────────────────────────────────────────────────────

    public (bool ok, string mensaje, Sprint? sprint) Crear(string? nombre, string? objetivo, DateTime inicio, DateTime fin)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var (valido, error) = Validar(nombre, objetivo, inicio, fin);
        if (!valido) return (false, error, null);

        var s = new Sprint
        {
            Name = nombre!.Trim(),
            Goal = string.IsNullOrWhiteSpace(objetivo) ? null : objetivo.Trim(),
            StartDate = inicio.Date,
            EndDate = fin.Date
        };
        _db.Sprints.Add(s);
        _db.SaveChanges();
        _audit.Record(AuditAction.Create, "Sprint", s.Id.ToString(), $"Sprint «{s.Name}» ({s.StartDate:dd/MM} – {s.EndDate:dd/MM/yyyy})");
        return (true, $"Sprint «{s.Name}» creado.", s);
    }

    public (bool ok, string mensaje) Actualizar(int id, string? nombre, string? objetivo, DateTime inicio, DateTime fin)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var (valido, error) = Validar(nombre, objetivo, inicio, fin);
        if (!valido) return (false, error);

        var s = Fresco(id);
        if (s == null) return (false, "Ese sprint ya no existe.");

        s.Name = nombre!.Trim();
        s.Goal = string.IsNullOrWhiteSpace(objetivo) ? null : objetivo.Trim();
        s.StartDate = inicio.Date;
        s.EndDate = fin.Date;
        _db.SaveChanges();
        _audit.Record(AuditAction.Update, "Sprint", s.Id.ToString(), $"Sprint «{s.Name}» ({s.StartDate:dd/MM} – {s.EndDate:dd/MM/yyyy})");
        return (true, "Sprint actualizado.");
    }

    public (bool ok, string mensaje) Eliminar(int id)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        var s = Fresco(id);
        if (s == null) return (false, "Ese sprint ya no existe.");

        // Desligar A MANO, no confiar en la FK: en la base real la columna SprintId se agregó por
        // ALTER sin restricción (migración aditiva), así que ahí no hay SetNull que valga.
        DesligarRequirementsRastreados();
        var suyos = _db.Requirements.Where(r => r.SprintId == id).ToList();
        foreach (var r in suyos) r.SprintId = null;

        _db.Sprints.Remove(s);
        if (GuardarSinEnvenenar(suyos, s) is { } conflicto) return (false, conflicto);
        _audit.Record(AuditAction.Delete, "Sprint", id.ToString(),
            $"Sprint «{s.Name}» eliminado; {suyos.Count} requerimiento(s) de vuelta al backlog");
        return (true, $"Sprint eliminado. Sus {suyos.Count} requerimiento(s) quedaron sin sprint.");
    }

    /// <summary>
    /// Deja el sprint EXACTAMENTE con estos requerimientos: agrega los marcados y desliga los que
    /// estaban y ya no vienen. Es la semántica del diálogo de casillas — lo que se ve marcado es
    /// lo que queda. EXCEPCIÓN: los cancelados no se desligan nunca por esta vía — el diálogo no
    /// los lista (no hay nada que seguirles), y «no aparecía en la lista» no puede significar
    /// «bórralo de la historia del sprint».
    /// </summary>
    public (bool ok, string mensaje) FijarRequerimientos(int sprintId, IReadOnlyCollection<int> requirementIds)
    {
        AuthorizationGuard.RequireAdmin(_currentUser);

        if (Fresco(sprintId) == null) return (false, "Ese sprint ya no existe.");

        DesligarRequirementsRastreados();
        var actuales = _db.Requirements.Where(r => r.SprintId == sprintId).ToList();
        foreach (var r in actuales.Where(r => r.Status != RequirementStatus.Cancelado
                                           && !requirementIds.Contains(r.Id)))
            r.SprintId = null;

        var nuevos = _db.Requirements.Where(r => requirementIds.Contains(r.Id)).ToList();
        foreach (var r in nuevos) r.SprintId = sprintId;   // si venía de otro sprint, se muda

        if (GuardarSinEnvenenar(actuales.Concat(nuevos), null) is { } conflicto) return (false, conflicto);
        return (true, $"{nuevos.Count} requerimiento(s) en el sprint.");
    }

    /// <summary>
    /// Desancla del rastreador todo Requirement vivo antes de una consulta que va a MUTAR.
    ///
    /// El AppDbContext es Singleton: si «Requerimientos» quedó abierta, sus entidades siguen
    /// rastreadas y la resolución de identidad haría que nuestra consulta devolviera ESAS
    /// instancias viejas — con un SprintId stale (se desligaría o quedaría huérfano lo que no es)
    /// y con un RowVersion caducado que en SQL Server truena el UPDATE con conflicto de
    /// concurrencia. Desligado primero, la consulta materializa filas frescas de la base.
    /// </summary>
    private void DesligarRequirementsRastreados()
    {
        foreach (var e in _db.ChangeTracker.Entries<Requirement>().ToList())
            e.State = EntityState.Detached;
    }

    /// <summary>
    /// SaveChanges que no deja el contexto Singleton envenenado: si otra máquina modificó un
    /// requerimiento entre la lectura y el guardado (RowVersion), se desancla TODO lo tocado y se
    /// devuelve el mensaje para la pantalla. Sin esto, las entidades quedarían Modified/Deleted y
    /// el siguiente guardado de CUALQUIER pantalla reintentaría esta operación a escondidas.
    /// </summary>
    private string? GuardarSinEnvenenar(IEnumerable<Requirement> tocados, Sprint? sprintTocado)
    {
        try { _db.SaveChanges(); return null; }
        catch (DbUpdateConcurrencyException)
        {
            foreach (var r in tocados) _db.Entry(r).State = EntityState.Detached;
            if (sprintTocado != null) _db.Entry(sprintTocado).State = EntityState.Detached;
            return "Otro usuario modificó los requerimientos al mismo tiempo. Recarga e intenta de nuevo.";
        }
    }

    // ── El cálculo ───────────────────────────────────────────────────────────────

    /// <summary>Avance del sprint contra hoy (fecha local).</summary>
    public SprintAvance Avance(int sprintId)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(_currentUser, "de consulta del sprint");
        var s = _db.Sprints.AsNoTracking().First(x => x.Id == sprintId);
        return CalcularAvance(s, Requerimientos(sprintId), DateTime.Today);
    }

    /// <summary>
    /// Puro y estático: toda la aritmética del seguimiento, comprobable sin base ni pantalla.
    ///
    /// Días NATURALES a propósito, no hábiles: las fechas las fija el administrador y el equipo
    /// entiende «vamos a la mitad del sprint» en calendario; meter días hábiles aquí haría que el
    /// % de tiempo no cuadrara con la línea de tiempo que se dibuja (que sombrea los fines de
    /// semana, pero no los descuenta).
    /// </summary>
    public static SprintAvance CalcularAvance(Sprint sprint, IReadOnlyList<Requirement> reqs, DateTime hoyLocal)
    {
        var hoy = hoyLocal.Date;
        var inicio = sprint.StartDate.Date;
        var fin = sprint.EndDate.Date;

        int diasTotales = (fin - inicio).Days + 1;   // extremos inclusive; Validar garantiza fin >= inicio
        int transcurridos = hoy < inicio ? 0
                          : hoy > fin ? diasTotales
                          : (hoy - inicio).Days + 1;
        int restantes = Math.Max(0, diasTotales - transcurridos);
        int tiempoPct = diasTotales == 0 ? 100 : transcurridos * 100 / diasTotales;

        // Los cancelados no cuentan en el avance: dejar de hacer algo no es avanzar ni atrasarse.
        var activos = reqs.Where(r => r.Status != RequirementStatus.Cancelado).ToList();
        int entregados = activos.Count(r => r.Status == RequirementStatus.Entregado);
        int enCurso = activos.Count(r => r.Status is RequirementStatus.EnDesarrollo
                                                   or RequirementStatus.EnPruebas
                                                   or RequirementStatus.PorEntregar);
        int sinEmpezar = activos.Count - entregados - enCurso;

        // AwayFromZero: el Math.Round por omisión redondea al par (60.5 → 60), y un avance que
        // «baja» medio punto respecto de lo capturado se reporta como error.
        int avanceReal = activos.Count == 0 ? 0
            : (int)Math.Round(activos.Average(r =>
                r.Status == RequirementStatus.Entregado ? 100.0 : Math.Clamp(r.ProgressPercent, 0, 100)),
                MidpointRounding.AwayFromZero);

        string veredicto;
        if (activos.Count == 0)      veredicto = "Sin requerimientos";
        else if (hoy < inicio)       veredicto = "Aún no empieza";
        else if (hoy > fin)          veredicto = entregados == activos.Count ? "Terminado ✓" : "Terminó incompleto";
        // ±10 puntos de tolerancia: sin margen, el veredicto parpadearía entre «al día» y
        // «atrasado» con cada día que pasa, y un semáforo nervioso deja de leerse.
        else if (avanceReal >= tiempoPct + 10) veredicto = "Adelantado";
        else if (avanceReal <= tiempoPct - 10) veredicto = "Atrasado";
        else                                   veredicto = "Al día";

        return new SprintAvance(
            activos.Count, entregados, enCurso, sinEmpezar,
            reqs.Count - activos.Count,
            avanceReal, tiempoPct, diasTotales, transcurridos, restantes, veredicto);
    }

    // ── Interno ──────────────────────────────────────────────────────────────────

    private static (bool ok, string error) Validar(string? nombre, string? objetivo, DateTime inicio, DateTime fin)
    {
        nombre = (nombre ?? "").Trim();
        if (nombre.Length < 3) return (false, "Ponle un nombre al sprint (al menos 3 caracteres).");
        if (nombre.Length > MaxNombre) return (false, $"El nombre no puede pasar de {MaxNombre} caracteres.");
        if ((objetivo ?? "").Length > MaxObjetivo) return (false, $"El objetivo no puede pasar de {MaxObjetivo} caracteres.");
        if (fin.Date < inicio.Date) return (false, "La fecha de fin no puede ser anterior a la de inicio.");
        if (((fin.Date - inicio.Date).Days + 1) > MaxDias)
            return (false, $"Un sprint de más de {MaxDias} días ya no es un sprint. Divide el trabajo.");
        return (true, "");
    }

    /// <summary>Relee del disco, no del rastreador: el AppDbContext es Singleton compartido.</summary>
    private Sprint? Fresco(int id)
    {
        var rastreado = _db.Sprints.Local.FirstOrDefault(s => s.Id == id);
        if (rastreado != null) _db.Entry(rastreado).State = EntityState.Detached;
        return _db.Sprints.FirstOrDefault(s => s.Id == id);
    }
}
