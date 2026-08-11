using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Una persona en el tablero de asistencia de un día: lo que marcó a mano y lo que vio la
/// telemetría, cruzados. Los dos lados pueden faltar y eso también es información: sin marca pero
/// con telemetría es «no marcó»; con marca y sin telemetría es «trabajó sin abrir la aplicación».
/// </summary>
public record AsistenciaDelDiaFila(
    int UserId,
    string Nombre,
    int? RegistroId,
    DateTime? EntradaOficialUtc,
    DateTime? SalidaOficialUtc,
    AttendanceCloseKind? Cierre,
    bool CorreccionSolicitada,
    string? NotaCorreccion,
    DateTime? PrimeraSenalAutoUtc,
    DateTime? UltimaSenalAutoUtc,
    TimeSpan? DeltaEntrada,
    TimeSpan? DeltaSalida)
{
    /// <summary>Tuvo actividad en la aplicación pero no marcó nada.</summary>
    public bool SinMarcar => RegistroId == null && PrimeraSenalAutoUtc != null;

    /// <summary>Alguno de los dos lados se separa más de lo tolerable de lo que vio la máquina.</summary>
    public bool HayDiscrepancia =>
        (DeltaEntrada is TimeSpan de && de.Duration() > AttendanceService.ToleranciaDiscrepancia) ||
        (DeltaSalida  is TimeSpan ds && ds.Duration() > AttendanceService.ToleranciaDiscrepancia);
}

/// <summary>
/// El registro OFICIAL de asistencia: entrada y salida que cada quien marca a mano.
///
/// Existe aparte de <see cref="PresenceService"/> a propósito. Esa mide lo que la aplicación puede
/// ver sola (el latido) y sirve para «quién está ahora»; pero una aplicación abierta en un equipo
/// encendido no prueba que alguien esté trabajando, ni el trabajo hecho sin abrirla deja de contar.
/// La asistencia la declara la persona; la telemetría solo la CONTRASTA. Ninguna de las dos se
/// deduce de la otra, que es lo que hace que el cruce sirva para detectar olvidos.
///
/// Este servicio NO escribe en <see cref="WorkPresence"/>: solo la lee.
///
/// Del port solo cambia de dónde sale el «equipo» de cada marca: en el escritorio era
/// <c>MÁQUINA\usuario</c> y aquí sería siempre el servidor, así que lo aporta
/// <see cref="IRequestOrigin"/> —la IP y el navegador del cliente—, igual que en la bitácora.
///
/// <para><b>Quién marca y quién mira.</b> Lo PROPIO —marcar, leer los registros de uno, pedir una
/// corrección— es del líder y del desarrollador: Operaciones no registra jornada porque su alcance
/// son los despliegues. Lo comprueban las guardas de aquí, además de la política del grupo
/// <c>/api/jornada</c>; con solo aquélla, un endpoint nuevo que llamara a estos métodos nacería
/// abierto y quien lo escribiera no tendría por qué enterarse. El TABLERO del líder sigue siendo
/// <c>RequireAdmin</c> a secas y no se toca: los días viejos de una cuenta de Operaciones existen,
/// no se borran, y el líder tiene que poder seguir mirándolos y corrigiéndolos.</para>
/// </summary>
public class AttendanceService(AppDbContext db, ICurrentUser currentUser, AuditService audit, IRequestOrigin origin)
{
    /// <summary>
    /// Ámbito con el que hablan las guardas de lo PROPIO, para que el 403 diga de qué se le está
    /// echando. Es el mismo texto que usa <see cref="PresenceService.MisJornadasAsync"/>: las dos
    /// mitades del módulo —lo que la persona marca y lo que la aplicación ve— tienen que negar con
    /// la misma frase, o parecerán dos reglas distintas.
    /// </summary>
    private const string Ambito = "de la jornada propia";

    /// <summary>
    /// A partir de esta diferencia entre lo marcado y lo que vio la máquina, la fila se resalta.
    /// No es una falta: es el umbral a partir del cual vale la pena mirarlo.
    /// </summary>
    public static readonly TimeSpan ToleranciaDiscrepancia = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Duración máxima de una jornada que todavía se considera «en curso».
    ///
    /// Es lo que decide si una entrada sin salida es un turno largo o un olvido, y se mide por horas
    /// transcurridas, NO por si cambió la fecha del calendario. Con el criterio de la fecha, quien
    /// entra a las 22:00 y sale a la 1:00 nunca podría marcar su salida: al pasar la medianoche su
    /// entrada sería «de otro día» y se cerraría como olvido, tirando la hora real que la persona
    /// estaba a punto de registrar.
    ///
    /// Dieciséis horas deja lugar de sobra a cualquier jornada real, incluidas las de un despliegue
    /// nocturno, y sigue atrapando la entrada de anteayer que quedó abierta.
    /// </summary>
    public static readonly TimeSpan MaxJornadaAbierta = TimeSpan.FromHours(16);

    /// <summary>Tope de las notas y motivos, para que quepan en las columnas y no sean un ensayo.</summary>
    public const int MaxNota = 300;
    public const int MaxMotivo = 500;

    // ── Lo propio (nunca reciben userId) ─────────────────────────────────────────

    /// <summary>
    /// El registro abierto de quien tiene la sesión, o null. Sin parámetro de usuario a propósito,
    /// igual que <see cref="PresenceService.MisJornadasAsync"/>: aquí solo hay un usuario posible y
    /// por eso esto no puede convertirse nunca en una fuga de datos de otra persona.
    /// </summary>
    public Task<AttendanceRecord?> MiRegistroAbiertoAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(currentUser, Ambito);
        if (currentUser.UserId is not int userId) return Task.FromResult<AttendanceRecord?>(null);

        return db.AttendanceRecords.AsNoTracking()
            .Where(a => a.UserId == userId && a.CheckOutUtc == null)
            .OrderByDescending(a => a.CheckInUtc)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Marca la entrada del día. La hora es SIEMPRE la de ahora: el método no recibe fecha ni la
    /// recibirá — en cuanto alguien pueda escribir su propia hora de llegada, el registro deja de
    /// probar nada. Lo retroactivo lo corrige el líder, con motivo y rastro.
    /// </summary>
    public async Task<(bool ok, string mensaje)> MarcarEntradaAsync(string? nota = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(currentUser, Ambito);
        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.");

        // Primero los olvidos: si quedó una entrada abierta de ayer, se sella con lo que se sepa de
        // ayer antes de abrir la de hoy. Al revés, esa persona tendría dos registros abiertos y el
        // de ayer acabaría cobrando la noche entera.
        int olvidos = await CerrarOlvidosAsync(userId, ct);

        var abierto = await AbiertoRastreadoAsync(userId, ct);
        if (abierto != null)
            return (false, $"Ya marcaste tu entrada hoy a las {abierto.CheckInUtc.ToLocalTime():HH:mm}.");

        var (desde, hasta) = RangoUtcDelDiaLocal(DateTime.Today);
        var yaCerradoHoy = await db.AttendanceRecords.AsNoTracking()
            .Where(a => a.UserId == userId && a.CheckInUtc >= desde && a.CheckInUtc < hasta)
            .OrderByDescending(a => a.CheckInUtc)
            .FirstOrDefaultAsync(ct);
        if (yaCerradoHoy != null)
            return (false, $"Tu salida de hoy ya quedó marcada a las {yaCerradoHoy.CheckOutUtc?.ToLocalTime():HH:mm}. " +
                           "Si fue un error, pide una corrección al líder.");

        var registro = new AttendanceRecord
        {
            UserId        = userId,
            DeveloperId   = currentUser.DeveloperId,
            DisplayName   = NombreParaMostrar(userId),
            CheckInUtc    = DateTime.UtcNow,
            CheckInOrigin = Equipo(),
            CheckInNote   = Recortar(nota, MaxNota)
        };
        db.AttendanceRecords.Add(registro);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Create, "Asistencia", registro.Id.ToString(),
            $"Entrada marcada {registro.CheckInUtc.ToLocalTime():dd/MM/yyyy HH:mm}", ct);

        var mensaje = $"Entrada marcada a las {registro.CheckInUtc.ToLocalTime():HH:mm}.";
        if (olvidos > 0)
            mensaje += $" Se cerró {(olvidos == 1 ? "una jornada anterior" : $"{olvidos} jornadas anteriores")} " +
                       "que quedó sin salida; revísala en «Mi jornada».";
        return (true, mensaje);
    }

    /// <summary>Marca la salida. La hora también es la de ahora, por lo mismo que la entrada.</summary>
    public async Task<(bool ok, string mensaje)> MarcarSalidaAsync(string? nota = null, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(currentUser, Ambito);
        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.");

        // Si otra pestaña de la misma persona ya marcó la salida, esta consulta —hecha en el
        // contexto de ESTA petición— sencillamente no la encuentra abierta y el mensaje es el de
        // «no tienes una entrada abierta», que es la verdad.
        var abierto = await AbiertoRastreadoAsync(userId, ct);
        if (abierto == null)
            return (false, "No tienes una entrada abierta: marca primero tu entrada.");

        // Una entrada olvidada hace días no se cierra «ahora»: eso le regalaría todas esas horas.
        // Se mide por tiempo transcurrido y no por fecha del calendario, para no romperle la salida
        // a quien trabaja de noche y cruza la medianoche (ver MaxJornadaAbierta).
        if (DateTime.UtcNow - abierto.CheckInUtc > MaxJornadaAbierta)
        {
            await CerrarComoOlvidoAsync(abierto, ct);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(AuditAction.Update, "Asistencia", abierto.Id.ToString(),
                $"Salida no marcada del {abierto.CheckInUtc.ToLocalTime():dd/MM/yyyy}; cerrada con la última señal", ct);
            return (false, $"Tu entrada abierta era del {abierto.CheckInUtc.ToLocalTime():dd/MM/yyyy} y llevaba " +
                           "demasiado tiempo sin cerrar: se cerró como olvido con la última señal de ese día. " +
                           "Marca ahora tu entrada de hoy.");
        }

        abierto.CheckOutUtc    = DateTime.UtcNow;
        abierto.CheckOutOrigin = Equipo();
        abierto.CheckOutNote   = Recortar(nota, MaxNota);
        abierto.CloseKind      = AttendanceCloseKind.Manual;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "Asistencia", abierto.Id.ToString(),
            $"Salida marcada {abierto.CheckOutUtc?.ToLocalTime():dd/MM/yyyy HH:mm} " +
            $"({PresenceService.Duracion(abierto.Duracion ?? TimeSpan.Zero)})", ct);

        return (true, $"Salida marcada a las {abierto.CheckOutUtc?.ToLocalTime():HH:mm}. " +
                      $"Jornada de {PresenceService.Duracion(abierto.Duracion ?? TimeSpan.Zero)}.");
    }

    /// <summary>Los registros PROPIOS de un rango de días locales, para «Mi jornada».</summary>
    public async Task<List<AttendanceRecord>> MisRegistrosAsync(DateTime desdeLocal, DateTime hastaLocal,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(currentUser, Ambito);
        if (currentUser.UserId is not int userId) return [];

        var (desde, hasta) = RangoUtcDelDiaLocal(desdeLocal, hastaLocal);
        return await db.AttendanceRecords.AsNoTracking()
            .Where(a => a.UserId == userId && a.CheckInUtc >= desde && a.CheckInUtc < hasta)
            .OrderBy(a => a.CheckInUtc)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Pide al líder que corrija un registro propio. El desarrollador no edita sus horas —si
    /// pudiera, el registro no probaría nada—, pero sin esto un error suyo no tendría forma de
    /// arreglarse dentro de la aplicación y la conversación acabaría en un chat, sin rastro.
    /// </summary>
    public async Task<(bool ok, string mensaje)> SolicitarCorreccionAsync(int registroId, string motivo,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(currentUser, Ambito);
        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.");

        motivo = (motivo ?? "").Trim();
        if (motivo.Length == 0)
            return (false, "Escribe qué habría que corregir: es lo que el líder va a leer.");
        if (motivo.Length > MaxMotivo)
            return (false, $"El motivo no puede pasar de {MaxMotivo} caracteres.");

        var registro = await db.AttendanceRecords.FirstOrDefaultAsync(a => a.Id == registroId, ct);
        if (registro == null) return (false, "Ese registro ya no existe. Actualiza la lista.");

        // Por UserId y no por la guarda de desarrollador: la asistencia es de la CUENTA, y hay
        // cuentas sin ficha de desarrollador que también marcan.
        if (registro.UserId != userId && !currentUser.IsAdmin)
            throw new AuthorizationException("No puedes pedir correcciones sobre la asistencia de otra persona.");

        registro.CorrectionRequestNote    = motivo;
        registro.CorrectionRequestedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "Asistencia", registro.Id.ToString(),
            $"Corrección solicitada: {motivo}", ct);
        return (true, "Solicitud enviada. El líder la verá en el tablero de asistencia.");
    }

    // ── Tablero del administrador ────────────────────────────────────────────────

    /// <summary>
    /// La asistencia de un día: todas las cuentas activas, con lo marcado y lo que vio la máquina.
    /// Incluye a quien no marcó nada, porque esa ausencia es justamente el dato.
    /// </summary>
    public async Task<List<AsistenciaDelDiaFila>> AsistenciaDelDiaAsync(DateTime diaLocal, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var (desde, hasta) = RangoUtcDelDiaLocal(diaLocal);

        var usuarios = await db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new { u.Id, u.FullName, u.Username })
            .ToListAsync(ct);

        // Un registro por persona y día es la regla, pero el líder puede haber dado de alta alguno a
        // mano. Si hay varios, se muestra el que tenga una corrección pendiente —para que ninguna
        // petición quede fuera de alcance— y, si ninguno la tiene, el primero del día, que es cuando
        // esa persona empezó de verdad.
        var registros = (await db.AttendanceRecords.AsNoTracking()
            .Where(a => a.CheckInUtc >= desde && a.CheckInUtc < hasta)
            .ToListAsync(ct))
            .GroupBy(a => a.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.CorrectionRequestedAtUtc != null)
                      .ThenBy(a => a.CheckInUtc)
                      .First());

        // La telemetría del mismo día, agregada por persona. Se consulta aquí y no a través de
        // PresenceService porque aquel barre jornadas caídas (escribe), y un tablero de consulta no
        // debería tener efectos secundarios sobre filas ajenas.
        var telemetria = (await db.WorkPresences.AsNoTracking()
            .Where(p => p.StartedAtUtc >= desde && p.StartedAtUtc < hasta)
            .Select(p => new { p.UserId, p.StartedAtUtc, p.EndedAtUtc, p.LastSeenUtc })
            .ToListAsync(ct))
            .GroupBy(p => p.UserId)
            .ToDictionary(
                g => g.Key,
                g => (Primera: g.Min(p => p.StartedAtUtc),
                      Ultima:  g.Max(p => p.EndedAtUtc ?? p.LastSeenUtc)));

        return usuarios
            .Select(u =>
            {
                registros.TryGetValue(u.Id, out var reg);
                bool hayAuto = telemetria.TryGetValue(u.Id, out var auto);

                TimeSpan? deltaEntrada = reg != null && hayAuto ? reg.CheckInUtc - auto.Primera : null;
                TimeSpan? deltaSalida  = reg?.CheckOutUtc is DateTime salida && hayAuto ? salida - auto.Ultima : null;

                return new AsistenciaDelDiaFila(
                    u.Id,
                    string.IsNullOrWhiteSpace(u.FullName) ? u.Username : u.FullName,
                    reg?.Id,
                    reg?.CheckInUtc,
                    reg?.CheckOutUtc,
                    reg?.CloseKind,
                    reg?.CorrectionRequestedAtUtc != null,
                    reg?.CorrectionRequestNote,
                    hayAuto ? auto.Primera : null,
                    hayAuto ? auto.Ultima : null,
                    deltaEntrada,
                    deltaSalida);
            })
            .OrderBy(f => f.EntradaOficialUtc == null)      // primero quienes sí marcaron
            .ThenBy(f => f.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Cuántas solicitudes de corrección esperan respuesta. Para el aviso del líder.</summary>
    public Task<int> CorreccionesPendientesAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);
        return db.AttendanceRecords.AsNoTracking().CountAsync(a => a.CorrectionRequestedAtUtc != null, ct);
    }

    /// <summary>
    /// El líder corrige un registro. El motivo es obligatorio: una corrección sin explicación es
    /// indistinguible de una manipulación, y este dato acaba pesando en una nómina.
    /// Los valores anteriores van a la bitácora, no se pierden.
    /// </summary>
    public async Task<(bool ok, string mensaje)> CorregirRegistroAsync(
        int registroId, DateTime entradaLocal, DateTime? salidaLocal, string motivo, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        motivo = (motivo ?? "").Trim();
        if (motivo.Length == 0) return (false, "Escribe el motivo de la corrección.");
        if (motivo.Length > MaxMotivo) return (false, $"El motivo no puede pasar de {MaxMotivo} caracteres.");

        var registro = await db.AttendanceRecords.FirstOrDefaultAsync(a => a.Id == registroId, ct);
        if (registro == null) return (false, "Ese registro ya no existe. Actualiza la lista.");

        var (valido, error, entradaUtc, salidaUtc) =
            await ValidarHorarioAsync(registro.UserId, entradaLocal, salidaLocal, registro.Id, ct);
        if (!valido) return (false, error);

        var antes = new
        {
            Entrada = registro.CheckInUtc,
            Salida  = registro.CheckOutUtc,
            Cierre  = registro.CloseKind?.ToString()
        };

        registro.CheckInUtc  = entradaUtc;
        registro.CheckOutUtc = salidaUtc;
        registro.CloseKind   = salidaUtc == null ? null : AttendanceCloseKind.Admin;
        registro.CorrectedByUserId = currentUser.UserId;
        registro.CorrectedByName   = currentUser.FullName ?? currentUser.Username;
        registro.CorrectedAtUtc    = DateTime.UtcNow;
        registro.CorrectionReason  = motivo;
        // La solicitud queda atendida: dejarla puesta haría que el tablero siguiera pidiendo algo
        // que ya se hizo.
        registro.CorrectionRequestNote    = null;
        registro.CorrectionRequestedAtUtc = null;

        await db.SaveChangesAsync(ct);

        await audit.RecordDetailedAsync(AuditAction.Update, "Asistencia", registro.Id.ToString(),
            $"Asistencia corregida de {registro.DisplayName}: {motivo}",
            AuditOutcome.Exito,
            oldValues: antes,
            newValues: new { Entrada = registro.CheckInUtc, Salida = registro.CheckOutUtc, Cierre = registro.CloseKind?.ToString() },
            ct: ct);

        return (true, $"Registro corregido: {entradaUtc.ToLocalTime():dd/MM/yyyy HH:mm}" +
                      (salidaUtc is DateTime s ? $" – {s.ToLocalTime():HH:mm}." : " (sin salida)."));
    }

    /// <summary>
    /// Alta manual del líder para un día que nadie marcó (alguien trabajó sin la aplicación, o se le
    /// pasó por completo). Lleva motivo y rastro, igual que la corrección.
    /// </summary>
    public async Task<(bool ok, string mensaje)> CrearRegistroManualAsync(
        int userId, DateTime entradaLocal, DateTime? salidaLocal, string motivo, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        motivo = (motivo ?? "").Trim();
        if (motivo.Length == 0) return (false, "Escribe el motivo del alta manual.");
        if (motivo.Length > MaxMotivo) return (false, $"El motivo no puede pasar de {MaxMotivo} caracteres.");

        var usuario = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (usuario == null) return (false, "Esa cuenta ya no existe.");

        var (valido, error, entradaUtc, salidaUtc) = await ValidarHorarioAsync(userId, entradaLocal, salidaLocal, null, ct);
        if (!valido) return (false, error);

        var registro = new AttendanceRecord
        {
            UserId      = userId,
            DeveloperId = usuario.DeveloperId,
            DisplayName = string.IsNullOrWhiteSpace(usuario.FullName) ? usuario.Username : usuario.FullName,
            CheckInUtc  = entradaUtc,
            CheckOutUtc = salidaUtc,
            CloseKind   = salidaUtc == null ? null : AttendanceCloseKind.Admin,
            CorrectedByUserId = currentUser.UserId,
            CorrectedByName   = currentUser.FullName ?? currentUser.Username,
            CorrectedAtUtc    = DateTime.UtcNow,
            CorrectionReason  = motivo
        };
        db.AttendanceRecords.Add(registro);
        await db.SaveChangesAsync(ct);

        await audit.RecordDetailedAsync(AuditAction.Create, "Asistencia", registro.Id.ToString(),
            $"Asistencia dada de alta a mano para {registro.DisplayName}: {motivo}",
            AuditOutcome.Exito,
            newValues: new { Entrada = registro.CheckInUtc, Salida = registro.CheckOutUtc },
            ct: ct);

        return (true, $"Registro creado para {registro.DisplayName}.");
    }

    // ── Interno ──────────────────────────────────────────────────────────────────

    /// <summary>El registro abierto del usuario, RASTREADO (se va a mutar).</summary>
    private Task<AttendanceRecord?> AbiertoRastreadoAsync(int userId, CancellationToken ct) =>
        db.AttendanceRecords
            .Where(a => a.UserId == userId && a.CheckOutUtc == null)
            .OrderByDescending(a => a.CheckInUtc)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Cierra las entradas que llevan demasiado tiempo abiertas. Nunca deja un registro abierto para
    /// siempre y, sobre todo, nunca inventa horas: sella con la última señal que la telemetría vio
    /// ESE día y, si no hubo ninguna, con la propia hora de entrada (duración cero, imposible de
    /// confundir con una jornada real). Queda marcado como olvido para que el líder lo corrija.
    ///
    /// El corte va por HORAS transcurridas, no por fecha del calendario: con la fecha, una jornada
    /// nocturna en curso se sellaría en cuanto dieran las doce (ver <see cref="MaxJornadaAbierta"/>).
    /// </summary>
    private async Task<int> CerrarOlvidosAsync(int userId, CancellationToken ct)
    {
        var corte = DateTime.UtcNow - MaxJornadaAbierta;
        var abiertas = await db.AttendanceRecords
            .Where(a => a.UserId == userId && a.CheckOutUtc == null && a.CheckInUtc < corte)
            .ToListAsync(ct);
        if (abiertas.Count == 0) return 0;

        foreach (var a in abiertas) await CerrarComoOlvidoAsync(a, ct);
        await db.SaveChangesAsync(ct);
        return abiertas.Count;
    }

    private async Task CerrarComoOlvidoAsync(AttendanceRecord registro, CancellationToken ct)
    {
        var senal = await UltimaSenalAutomaticaAsync(registro.UserId, registro.CheckInUtc.ToLocalTime().Date, ct);

        // La señal solo sirve si cae DESPUÉS de la entrada. Puede no caer: si alguien tuvo la
        // aplicación abierta por la mañana y marcó su entrada por la tarde, la última señal del día
        // es anterior a la marca, y usarla daría una jornada de duración negativa que ninguna
        // pantalla sabría mostrar. En ese caso vale más una duración cero, que se lee como
        // «esto hay que corregirlo».
        registro.CheckOutUtc = senal is DateTime s && s > registro.CheckInUtc ? s : registro.CheckInUtc;
        registro.CloseKind   = AttendanceCloseKind.Olvido;
    }

    /// <summary>
    /// La última señal de vida que la aplicación registró de esa persona ese día local. Es la mejor
    /// estimación disponible de cuándo se fue: no es la verdad, pero es evidencia, y es siempre
    /// mejor que la hora de ahora (que le regalaría la noche entera).
    /// </summary>
    private async Task<DateTime?> UltimaSenalAutomaticaAsync(int userId, DateTime diaLocal, CancellationToken ct)
    {
        var (desde, hasta) = RangoUtcDelDiaLocal(diaLocal);

        // Se toman las jornadas que TOCAN el día, no solo las que empiezan en él: una que arrancó a
        // las 23:50 de la víspera y siguió hasta las 3:00 es precisamente la señal que interesa para
        // estimar a qué hora se fue quien olvidó marcar su salida.
        var señales = await db.WorkPresences.AsNoTracking()
            .Where(p => p.UserId == userId
                     && p.StartedAtUtc < hasta
                     && (p.EndedAtUtc ?? p.LastSeenUtc) >= desde)
            .Select(p => new { p.EndedAtUtc, p.LastSeenUtc })
            .ToListAsync(ct);
        if (señales.Count == 0) return null;

        // Y la señal se acota al día: una jornada que siguió hasta el mediodía siguiente no puede
        // fijar la salida de ayer a esa hora.
        var ultima = señales.Max(p => p.EndedAtUtc ?? p.LastSeenUtc);
        return ultima < hasta ? ultima : hasta.AddSeconds(-1);
    }

    /// <summary>
    /// Reglas comunes al corregir y al dar de alta a mano: la salida va después de la entrada, no se
    /// marca el futuro, y no se solapa con otro registro de la misma persona.
    /// </summary>
    private async Task<(bool ok, string error, DateTime entradaUtc, DateTime? salidaUtc)> ValidarHorarioAsync(
        int userId, DateTime entradaLocal, DateTime? salidaLocal, int? registroIdExcluido, CancellationToken ct)
    {
        var entradaUtc = entradaLocal.ToUniversalTime();
        DateTime? salidaUtc = salidaLocal?.ToUniversalTime();

        if (salidaUtc is DateTime s && s <= entradaUtc)
            return (false, "La salida tiene que ser posterior a la entrada.", default, null);

        var margen = DateTime.UtcNow.AddMinutes(5);   // el reloj del equipo puede ir unos minutos adelantado
        if (entradaUtc > margen || salidaUtc > margen)
            return (false, "No se puede registrar asistencia en el futuro.", default, null);

        // Un registro sin salida NO es un instante: ocupa desde su entrada hasta que se cierre. Vale
        // para los dos lados de la comparación —el que se está capturando y los que ya existen—, y
        // una jornada abierta no puede pasar de MaxJornadaAbierta.
        var finComparacion = salidaUtc ?? entradaUtc + MaxJornadaAbierta;

        // El descarte grueso va en la base; el fino, en memoria, porque esa extensión no se puede
        // expresar en SQL con este proveedor. La ventana acota los candidatos a un puñado de filas,
        // no al histórico de la persona.
        var candidatos = await db.AttendanceRecords.AsNoTracking()
            .Where(a => a.UserId == userId
                     && (registroIdExcluido == null || a.Id != registroIdExcluido)
                     && a.CheckInUtc < finComparacion
                     && a.CheckInUtc > entradaUtc - MaxJornadaAbierta)
            .Select(a => new { a.CheckInUtc, a.CheckOutUtc })
            .ToListAsync(ct);

        // Las comparaciones son ESTRICTAS: dos registros que solo se tocan en el extremo (uno acaba
        // a las 14:00 y el otro empieza a las 14:00) no se enciman, y rechazarlos obligaría a
        // inventar un minuto de hueco entre turnos.
        bool solapa = candidatos.Any(a => (a.CheckOutUtc ?? a.CheckInUtc + MaxJornadaAbierta) > entradaUtc);
        if (solapa)
            return (false, "Ese horario se encima con otro registro de la misma persona.", default, null);

        return (true, "", entradaUtc, salidaUtc);
    }

    private string NombreParaMostrar(int userId) =>
        currentUser.FullName
        ?? currentUser.Username
        ?? $"Usuario #{userId}";

    private static string? Recortar(string? texto, int tope)
    {
        texto = (texto ?? "").Trim();
        if (texto.Length == 0) return null;
        return texto.Length > tope ? texto[..tope] : texto;
    }

    /// <summary>Rango UTC de un día local completo.</summary>
    private static (DateTime desde, DateTime hasta) RangoUtcDelDiaLocal(DateTime diaLocal) =>
        RangoUtcDelDiaLocal(diaLocal, diaLocal);

    private static (DateTime desde, DateTime hasta) RangoUtcDelDiaLocal(DateTime desdeLocal, DateTime hastaLocal) =>
        (desdeLocal.Date.ToUniversalTime(), hastaLocal.Date.AddDays(1).ToUniversalTime());

    /// <summary>
    /// Desde dónde se marcó. En el escritorio era el equipo y el usuario de Windows; aquí lo
    /// identifica el cliente (IP y navegador), porque el servidor es siempre el mismo. Se guarda una
    /// por marca, igual que antes: entrar desde un equipo y salir desde otro es normal y ese dato
    /// no debe perderse.
    /// </summary>
    private string Equipo() => origin.Describir();

    // ── Etiquetas ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cómo se lee un cierre en pantalla, desde el lado de quien marcó.
    ///
    /// <para><b>Sin pictogramas delante, y no es un olvido.</b> «Olvido» llevaba un triángulo y
    /// «Corregida», un lápiz. Los dibuja el SISTEMA OPERATIVO: se ven distintos en un Windows, en un
    /// Mac y en un móvil, no heredan el color del texto —en el tema oscuro siguen con los suyos— y
    /// donde no hay fuente de emoji instalada salen como un CUADRO VACÍO, que es peor que no poner
    /// nada. Y no se sustituyen por un icono de la fuente porque esto es una CADENA que las rejillas
    /// pintan tal cual (no hay dónde colgar un <c>&lt;RadzenIcon&gt;</c>); el aviso que hacía falta
    /// ya lo da la pantalla, que resalta en ámbar la fila que reclama algo.</para>
    ///
    /// <para>Este texto se CONCATENA con el de <c>PersonasQueryService.EstadoDeAsistencia</c>
    /// («Pide corrección · Olvido (estimada)»), así que los dos se limpiaron a la vez: dejar uno con
    /// marca y el otro sin ella daba media etiqueta decorada.</para>
    /// </summary>
    public static string EtiquetaCierre(AttendanceCloseKind? cierre) => cierre switch
    {
        AttendanceCloseKind.Manual => "Marcada",
        AttendanceCloseKind.Olvido => "Olvido (estimada)",
        AttendanceCloseKind.Admin  => "Corregida por el líder",
        _                          => "— abierta"
    };
}
