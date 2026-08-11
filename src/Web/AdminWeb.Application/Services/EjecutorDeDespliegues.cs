using System.Collections.Concurrent;
using System.IO.Compression;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Infrastructure.Seguridad;
using AdminWeb.Shared.Dtos.Despliegues;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AdminWeb.Application.Services;

/// <summary>
/// Por dónde sale el avance de un despliegue hacia quien lo esté mirando.
///
/// Se declara aquí y lo implementa la API porque el canal en vivo (SignalR) vive en la capa web y
/// esta no puede verla. Es el mismo arreglo que <c>IRequestOrigin</c>: el servicio dice QUÉ hay que
/// contar, la API sabe POR DÓNDE.
/// </summary>
public interface IAvisosDeDespliegue
{
    Task AvanceAsync(int jobId, AvanceDeDespliegueDto avance);
    Task RegistroAsync(int jobId, RenglonDeBitacoraDto renglon);
    Task FinAsync(int jobId, FinDeDespliegueDto fin);
}

/// <summary>
/// La identidad de quien lanzó el despliegue, congelada al lanzarlo.
///
/// <para><b>Hace falta porque el despliegue sobrevive a la petición.</b> La identidad normal se lee
/// de los claims de la cookie, y cuando el trabajo sigue corriendo diez minutos después ya no hay
/// petición de la que leerlos: la bitácora acabaría firmada por «sistema» y el historial no podría
/// contestar quién desplegó. Congelarla al lanzar es además lo correcto en sí: el despliegue lo
/// lanzó quien lo lanzó, aunque después cierre sesión o le cambien el rol.</para>
/// </summary>
public sealed record IdentidadDelDespliegue(
    int? UserId, string? Username, string? FullName, UserRole? Role, int? DeveloperId) : ICurrentUser
{
    public bool IsLoggedIn => UserId != null;
    public bool IsAdmin => Role == UserRole.Admin;
    public bool IsDesarrollador => Role == UserRole.Desarrollador;
    public bool IsOperaciones => Role == UserRole.Operaciones;

    public static IdentidadDelDespliegue De(ICurrentUser quien) =>
        new(quien.UserId, quien.Username, quien.FullName, quien.Role, quien.DeveloperId);

    /// <summary>Cómo se firma en la bitácora y en la evidencia.</summary>
    public string ParaMostrar() =>
        !string.IsNullOrWhiteSpace(FullName) ? FullName!
        : !string.IsNullOrWhiteSpace(Username) ? Username!
        : "(sin nombre)";
}

/// <summary>Origen de la bitácora para lo que corre fuera de una petición.</summary>
public sealed class OrigenDelServidor : IRequestOrigin
{
    public string Describir() => "(despliegue en el servidor)";
}

/// <summary>
/// Todo lo que hace falta para correr un despliegue, resuelto y autorizado ANTES de soltarlo al
/// fondo. Deliberadamente <b>sin contraseñas</b>: las lee y descifra el motor desde su propio
/// contexto, para que no queden dando vueltas en memoria en la cola de nadie.
/// </summary>
public sealed record OrdenDeDespliegue(
    int JobId,
    int VersionId,
    string Sistema,
    string Version,
    string Destino,
    IReadOnlyList<int> ServidorIds,
    IReadOnlySet<int> RespaldarIds,
    IdentidadDelDespliegue Quien);

/// <summary>Lo que la configuración decide sobre un despliegue concreto.</summary>
/// <param name="RespaldoHabilitado">
/// El interruptor global <c>DeployBackupEnabled</c>. Ojo con su valor por omisión: en el escritorio
/// es «respaldar salvo que diga expresamente false», y así se conserva.
/// </param>
/// <param name="CarpetaDeDespliegue">Dónde vive el paquete y dónde se guardan los respaldos.</param>
public sealed record OpcionesDeEjecucion(bool RespaldoHabilitado, string? CarpetaDeDespliegue);

/// <summary>Nombres legibles de los estados de un despliegue, en un solo sitio.</summary>
public static class TextosDeDespliegue
{
    public static string Estado(JobStatus estado) => estado switch
    {
        JobStatus.Pendiente => "Pendiente",
        JobStatus.EnCurso => "En curso",
        JobStatus.Completado => "Completado",
        JobStatus.Fallido => "Fallido",
        JobStatus.Cancelado => "Cancelado",
        JobStatus.Parcial => "Parcial",
        _ => estado.ToString()
    };

    public static string Duracion(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s"
        : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds}s"
        : $"{t.TotalSeconds:0.0}s";
}

// ─────────────────────────────────────────────────────────────────────────────────
//  El trabajo vivo y el registro que los guarda
// ─────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Un despliegue que está corriendo ahora mismo en el servidor, con lo necesario para que cualquiera
/// que abra la pantalla se ponga al día: el avance, la bitácora acumulada y, si ya acabó, cómo
/// terminó.
///
/// <para><b>Esto es lo que sustituye al proceso del escritorio.</b> Allí el despliegue era el
/// <c>.exe</c> de quien pulsó el botón: cerrarlo a media subida lo mataba, y el estado vivía en un
/// <c>RichTextBox</c> que se vaciaba al salir. Aquí el estado vive en el servidor, así que cerrar la
/// pestaña no interrumpe nada y volver a abrirla devuelve la misma vista.</para>
/// </summary>
public sealed class TrabajoVivo
{
    private readonly object _candado = new();
    private readonly List<RenglonDeBitacoraDto> _bitacora = [];

    // Turno único para los envíos al canal en vivo. La bitácora fina se reporta desde dentro de la
    // subida y sin poder esperar (IProgress es síncrono); sin este turno, dos renglones seguidos
    // podrían llegar al navegador al revés y la consola contaría la historia desordenada.
    private readonly SemaphoreSlim _turno = new(1, 1);

    private AvanceDeDespliegueDto _avance;
    private FinDeDespliegueDto? _fin;

    /// <summary>Cuántos renglones se conservan en memoria. El log completo está en la base.</summary>
    public const int MaxRenglones = 400;

    internal TrabajoVivo(OrdenDeDespliegue orden, DateTime inicioUtc)
    {
        Orden = orden;
        InicioUtc = inicioUtc;
        _avance = new AvanceDeDespliegueDto(orden.JobId, 0, "", "preparando…");
    }

    public OrdenDeDespliegue Orden { get; }
    public DateTime InicioUtc { get; }
    public CancellationTokenSource Cancelacion { get; } = new();

    /// <summary>Cuándo terminó, para poder olvidarlo un rato después. Null mientras corre.</summary>
    public DateTime? FinUtc { get; private set; }

    public bool Terminado => FinUtc != null;

    public void Anotar(RenglonDeBitacoraDto renglon)
    {
        lock (_candado)
        {
            _bitacora.Add(renglon);
            // Un despliegue a veinte servidores con mil archivos escupe miles de renglones. Se
            // conservan los últimos: es lo que alguien que acaba de abrir la pantalla quiere ver, y
            // el expediente completo sigue estando en la base.
            if (_bitacora.Count > MaxRenglones) _bitacora.RemoveRange(0, _bitacora.Count - MaxRenglones);
        }
    }

    public void Avanzar(AvanceDeDespliegueDto avance)
    {
        lock (_candado) _avance = avance;
    }

    public void Terminar(FinDeDespliegueDto fin, DateTime ahoraUtc)
    {
        lock (_candado)
        {
            _fin = fin;
            FinUtc = ahoraUtc;
        }
    }

    /// <summary>La foto para una pantalla que acaba de abrirse (o de reconectar).</summary>
    public TrabajoDeDespliegueDto Foto(int? userId)
    {
        lock (_candado)
            return new TrabajoDeDespliegueDto(
                Orden.JobId, Orden.Sistema, Orden.Version, Orden.Destino,
                Orden.Quien.ParaMostrar(), InicioUtc,
                Mio: userId != null && userId == Orden.Quien.UserId,
                _avance, [.. _bitacora], _fin);
    }

    /// <summary>
    /// Manda algo por el canal en vivo respetando el turno. No se espera al envío: si un navegador
    /// va lento, el despliegue no tiene por qué ir a su ritmo. Un aviso perdido tampoco es grave —
    /// la foto completa se puede volver a pedir.
    /// </summary>
    public void Emitir(Func<Task> envio) => _ = EmitirAsync(envio);

    private async Task EmitirAsync(Func<Task> envio)
    {
        await _turno.WaitAsync().ConfigureAwait(false);
        try { await envio().ConfigureAwait(false); }
        catch { /* el canal en vivo es una comodidad: que falle no puede tumbar el despliegue */ }
        finally { _turno.Release(); }
    }
}

/// <summary>
/// Los despliegues que el servidor tiene entre manos.
///
/// Se guardan un rato DESPUÉS de terminar (<see cref="Retencion"/>) a propósito: quien cerró la
/// pestaña justo cuando acababa vuelve, la abre y quiere ver cómo salió, no una pantalla en blanco
/// que le obligue a buscarlo en el historial.
/// </summary>
public sealed class RegistroDeDespliegues
{
    public static readonly TimeSpan Retencion = TimeSpan.FromMinutes(20);

    private readonly ConcurrentDictionary<int, TrabajoVivo> _trabajos = new();

    public TrabajoVivo Registrar(OrdenDeDespliegue orden, DateTime ahoraUtc)
    {
        var trabajo = new TrabajoVivo(orden, ahoraUtc);
        _trabajos[orden.JobId] = trabajo;
        return trabajo;
    }

    public TrabajoVivo? Buscar(int jobId) => _trabajos.TryGetValue(jobId, out var t) ? t : null;

    public IReadOnlyList<TrabajoVivo> Todos() => [.. _trabajos.Values];

    /// <summary>
    /// ¿Hay un despliegue EN MARCHA tocando este servidor?
    ///
    /// Regla nueva de la web, y no un capricho: en el escritorio solo podía haber un despliegue a la
    /// vez porque lo corría una persona en su máquina. Aquí lo corre el servidor y dos personas
    /// pueden pulsar el botón a la vez; dos subidas simultáneas a la misma carpeta remota dejan una
    /// mezcla de las dos versiones, que es un estado que nadie pidió y que además no se nota.
    /// </summary>
    public bool AlgunoUsa(int servidorId) =>
        _trabajos.Values.Any(t => !t.Terminado && t.Orden.ServidorIds.Contains(servidorId));

    /// <summary>Olvida los que ya terminaron hace rato. Se llama de pasada, sin temporizador propio.</summary>
    public void Limpiar(DateTime ahoraUtc)
    {
        foreach (var (id, trabajo) in _trabajos)
            if (trabajo.FinUtc is { } fin && ahoraUtc - fin > Retencion)
                _trabajos.TryRemove(id, out _);
    }
}

// ─────────────────────────────────────────────────────────────────────────────────
//  La señal de vida: lo único que una instancia ve de lo que está corriendo otra
// ─────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// La prueba, EN LA BASE, de que un despliegue lo está corriendo alguien ahora mismo: una fila que
/// el servidor que lo ejecuta refresca cada <see cref="Latido"/> mientras dura.
///
/// <para><b>Por qué no basta el registro en memoria.</b> <see cref="RegistroDeDespliegues"/> es
/// memoria de UN proceso. Con la API en dos instancias —o durante los segundos en que conviven la
/// vieja y la nueva de un intercambio de ranura— cada una solo ve sus propios despliegues, así que
/// la guarda que impide dos subidas simultáneas a la misma carpeta remota deja de guardar nada justo
/// cuando más falta hace: dos personas desplegando a la vez desde instancias distintas dejan una
/// mezcla de dos versiones en el servidor, y sigue sin notarse. Lo único que ven todas las
/// instancias es la base, así que ahí tiene que estar la prueba.</para>
///
/// <para><b>Por qué una señal que CADUCA y no una marca de «está corriendo».</b> Una marca fija
/// mentiría en cuanto el proceso muriera: el trabajo se quedaría ocupando su servidor para siempre y
/// nadie podría desplegar ahí sin ir a tocar la base a mano — es decir, arreglar un agujero abriendo
/// otro peor. Una señal que hay que renovar dice la verdad sola: si nadie la renueva, es que no hay
/// nadie corriéndolo. Es además el dato con el que <see cref="ReconciliacionDeDespliegues"/>
/// distingue, al arrancar, un trabajo huérfano de uno que otra instancia está ejecutando.</para>
///
/// <para><b>Por qué vive en el log del propio despliegue.</b> No hay tabla nueva —el esquema no se
/// toca desde aquí— y AppSettings tampoco servía: esa tabla se pinta ENTERA en la pantalla de
/// Configuración y habría llenado de apuntes internos lo que un líder teclea. Aquí el dato es del
/// trabajo y se borra en cascada con él. Al terminar se retira; y si el servidor murió a media faena,
/// esta misma fila se REESCRIBE con lo que pasó de verdad, que es lo que impide que el historial
/// mienta.</para>
///
/// <para><b>Y no se enseña.</b> <c>DespliegueQueryService.ExpedienteAsync</c> la filtra por
/// <see cref="Marca"/>, porque esto no es un renglón de bitácora sino una reserva, y ese método
/// alimenta también la evidencia descargable. Es la contrapartida de vivir en una tabla prestada: si
/// algún día la marca cambia, hay que cambiarla también allí o el apunte interno se asoma al
/// expediente. La fila REESCRITA por la reconciliación ya no coincide con la marca y sí sale, que es
/// lo que se pretende.</para>
/// </summary>
public static class SenalDeVidaDelDespliegue
{
    /// <summary>
    /// El texto EXACTO de la fila. No es decorativo: es por lo que se la reconoce. Cambiarlo deja
    /// huérfanas las señales ya escritas —durante un despliegue escalonado, las de la versión
    /// anterior— y esas se darán por muertas en cuanto caduquen, cerrando en el historial despliegues
    /// que seguían vivos.
    /// </summary>
    public const string Marca = "⏱ Señal de vida: el servidor que está corriendo este despliegue sigue en pie.";

    /// <summary>Cada cuánto se renueva. Corto se puede: es actualizar una fila de una tabla.</summary>
    public static readonly TimeSpan Latido = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Cuánto se aguanta sin señal antes de dar el despliegue por muerto. Diez latidos, holgado a
    /// propósito: pasarse de prudente solo retrasa unos minutos el diagnóstico, mientras que pasarse
    /// de impaciente declara libre un servidor al que todavía se le están subiendo archivos y
    /// autoriza justo el despliegue simultáneo que todo esto existe para impedir.
    /// </summary>
    public static readonly TimeSpan Tolerancia = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Deja constancia de que este despliegue sigue vivo AHORA. Crea la fila la primera vez y la
    /// actualiza después: es UNA por trabajo y no un rastro de latidos, que serían miles por
    /// despliegue y dejarían el expediente ilegible.
    /// </summary>
    public static async Task RefrescarAsync(
        AppDbContext db, int jobId, DateTime ahoraUtc, CancellationToken ct = default)
    {
        var fila = await db.DeploymentLogEntries
            .Where(l => l.JobId == jobId && l.Message == Marca)
            .OrderByDescending(l => l.Timestamp)
            .FirstOrDefaultAsync(ct);

        if (fila == null)
            db.DeploymentLogEntries.Add(new DeploymentLogEntry
            {
                JobId = jobId,
                Message = Marca,
                Level = DeployLogLevel.Info,
                Timestamp = ahoraUtc
            });
        else
            fila.Timestamp = ahoraUtc;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Retira la señal. Se llama al terminar el despliegue pase lo que pase: una señal que se queda
    /// puesta después de acabar deja el servidor ocupado a ojos de las demás instancias hasta que
    /// caduca, y son minutos en los que nadie puede desplegar ahí sin ningún motivo.
    /// </summary>
    public static Task<int> RetirarAsync(AppDbContext db, int jobId, CancellationToken ct = default) =>
        db.DeploymentLogEntries.Where(l => l.JobId == jobId && l.Message == Marca).ExecuteDeleteAsync(ct);

    /// <summary>
    /// Cuáles de estos servidores están recibiendo un despliegue ahora mismo, lo lance quien lo
    /// lance y desde la instancia que sea.
    ///
    /// <para>El destino de un trabajo se lee de su PERFIL, que es donde queda escrito: la selección
    /// directa congela uno propio al lanzar (<c>IsAdHoc</c>) justo para que el historial —y esta
    /// consulta— puedan contestar a dónde iba.</para>
    ///
    /// <para>Un trabajo «En curso» cuya señal ya caducó NO cuenta como ocupación, y eso es
    /// deliberado: si el proceso que lo corría murió, el servidor está libre de verdad y bloquearlo
    /// hasta que alguien reinicie la aplicación sería peor que el problema. La fila mentirosa la
    /// arregla la reconciliación; el servidor se libera solo.</para>
    /// </summary>
    public static async Task<HashSet<int>> ServidoresOcupadosAsync(
        AppDbContext db, IReadOnlyCollection<int> candidatos, DateTime ahoraUtc, CancellationToken ct = default)
    {
        if (candidatos.Count == 0) return [];

        var limite = ahoraUtc - Tolerancia;
        var ids = candidatos.ToList();

        var ocupados = await db.DeploymentProfileTargets.AsNoTracking()
            .Where(pt => ids.Contains(pt.TargetId))
            .Where(pt => db.DeploymentJobs.Any(j =>
                j.DeploymentProfileId == pt.ProfileId
                && j.Status == JobStatus.EnCurso
                && j.LogEntries.Any(l => l.Message == Marca && l.Timestamp > limite)))
            .Select(pt => pt.TargetId)
            .Distinct()
            .ToListAsync(ct);

        return [.. ocupados];
    }
}

/// <summary>
/// Cierra los despliegues que se quedaron en <see cref="JobStatus.EnCurso"/> sin nadie que los esté
/// ejecutando. Se llama al arrancar la API.
///
/// <para><b>Qué arregla.</b> Un reinicio a media faena mata el proceso que estaba desplegando. El
/// servidor remoto queda libre —correcto: nadie le está subiendo nada— pero la fila del trabajo se
/// queda «En curso» para siempre, y el historial acaba con despliegues eternamente en marcha que
/// nadie puede explicar meses después.</para>
///
/// <para><b>Lo que hace difícil esto.</b> Con varias instancias, un arranque NO puede cerrar sin más
/// todo lo que encuentre en curso: lo más probable es que se esté reiniciando una sola instancia
/// mientras otra sigue desplegando tan tranquila, y cerrarle el trabajo por debajo dejaría su
/// historial mintiendo en la otra dirección —y, peor, liberaría su servidor para que un tercero
/// desplegara encima—. Por eso el criterio no es «está en curso» sino «está en curso y hace rato que
/// nadie da señales de estarlo corriendo»: ver <see cref="SenalDeVidaDelDespliegue"/>.</para>
/// </summary>
public static class ReconciliacionDeDespliegues
{
    /// <summary>
    /// Cierra los interrumpidos y devuelve cuántos eran. Es idempotente: volver a llamarla no
    /// encuentra nada, porque al cerrar el trabajo deja de estar «En curso» y su señal deja de serlo.
    /// </summary>
    public static async Task<int> CerrarInterrumpidosAsync(
        AppDbContext db, DateTime ahoraUtc, CancellationToken ct = default)
    {
        var enCurso = await db.DeploymentJobs.Where(j => j.Status == JobStatus.EnCurso).ToListAsync(ct);
        if (enCurso.Count == 0) return 0;

        var ids = enCurso.Select(j => j.Id).ToList();

        // Las señales y los hitos por servidor, en dos consultas y no en dos por trabajo: esto corre
        // en el arranque, y un arranque que hace treinta viajes a una base remota se nota.
        var senales = (await db.DeploymentLogEntries
                .Where(l => ids.Contains(l.JobId) && l.Message == SenalDeVidaDelDespliegue.Marca)
                .ToListAsync(ct))
            .GroupBy(l => l.JobId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.Timestamp).First());

        var hitos = await db.DeploymentLogEntries.AsNoTracking()
            .Where(l => ids.Contains(l.JobId) && l.TargetId != null
                        && (l.Level == DeployLogLevel.Exito || l.Level == DeployLogLevel.Error))
            .Select(l => new { l.JobId, TargetId = l.TargetId!.Value, l.Level })
            .Distinct()
            .ToListAsync(ct);

        var limite = ahoraUtc - SenalDeVidaDelDespliegue.Tolerancia;
        int cerrados = 0;

        foreach (var job in enCurso)
        {
            senales.TryGetValue(job.Id, out var senal);

            // Cuándo se supo por última vez de este trabajo. Sin señal ninguna vale su hora de
            // arranque, y esa equivalencia importa: un trabajo recién lanzado que todavía no tiene
            // señal puede estar corriéndolo una instancia con la versión ANTERIOR de la aplicación
            // —los minutos de un despliegue escalonado, cuando conviven las dos—, y cerrárselo sería
            // el mismo error que cerrárselo a una instancia viva. Uno de hace horas sin señal, en
            // cambio, no engaña a nadie: o murió antes de dar la primera, o es anterior a que esto
            // existiera.
            var cuando = senal?.Timestamp ?? job.StartedAt ?? job.CreatedAt;

            // Reciente = alguien lo está corriendo, aquí o en otra instancia. No se toca.
            if (cuando > limite) continue;

            // Lo que SÍ se sabe se lee de la bitácora, que se persiste servidor por servidor según
            // van cayendo: es la diferencia entre «no se sabe nada» y «se sabe esto y falta el
            // resto», y es lo que convierte el expediente en algo utilizable.
            int ok = hitos.Count(h => h.JobId == job.Id && h.Level == DeployLogLevel.Exito);
            int fallidos = hitos.Count(h => h.JobId == job.Id && h.Level == DeployLogLevel.Error);

            // «Fallido» y no otra cosa, a falta de un estado propio. No es «Cancelado»: nadie lo
            // pidió. No es «Parcial»: eso es el RESULTADO de un despliegue que terminó, y este no
            // terminó. Y desde luego no se queda «En curso», que es la mentira que estamos quitando.
            // La verdad completa —que se interrumpió y que hay servidores de los que no se sabe— va
            // en el renglón de abajo, que sale en el expediente y en la evidencia descargable.
            job.Status = JobStatus.Fallido;
            job.TargetsOk = ok;
            job.TargetsFailed = fallidos;

            // Terminó cuando se le perdió la pista, no cuando lo notamos: fechar el cierre «ahora»
            // le regalaría al despliegue las horas que la aplicación estuvo caída.
            job.CompletedAt = cuando;

            // Sin el triángulo que llevaba delante, por lo mismo que las etiquetas de estado: este
            // renglón acaba en la EVIDENCIA descargable, un .txt que se abre en un equipo del que no
            // sabemos nada y donde un emoji sin fuente sale como un cuadro vacío. La palabra
            // «INTERRUMPIDO» en mayúsculas ya grita bastante, y el renglón va además en nivel Error.
            var explicacion =
                $"INTERRUMPIDO. El servidor de la aplicación que estaba corriendo este despliegue se " +
                $"detuvo a media faena (última señal de vida: {cuando:dd/MM/yyyy HH:mm} UTC) y al volver ya no " +
                $"había nada que retomar. Confirmados en la bitácora: {ok} de {job.TargetsTotal} servidor(es)" +
                (fallidos > 0 ? $", con {fallidos} fallido(s)" : "") + ". " +
                "Del resto NO se sabe: el que estuviera recibiendo archivos en ese momento pudo quedarse con " +
                "una mezcla de dos versiones, así que compruébalo antes de volver a desplegar. Se cierra como " +
                "«Fallido» porque no terminó, que no es lo mismo que haber fallado al publicar.";

            // La señal se REESCRIBE en vez de borrarse y escribir otra fila: así el renglón conserva
            // la hora de la última señal —que es cuando de verdad se paró todo— y deja de ser una
            // señal, de modo que una segunda pasada ya no lo confunde con un despliegue vivo.
            if (senal != null)
            {
                senal.Message = explicacion;
                senal.Level = DeployLogLevel.Error;
            }
            else
            {
                db.DeploymentLogEntries.Add(new DeploymentLogEntry
                {
                    JobId = job.Id,
                    Timestamp = cuando,
                    Level = DeployLogLevel.Error,
                    Message = explicacion
                });
            }

            cerrados++;
        }

        if (cerrados > 0) await db.SaveChangesAsync(ct);
        return cerrados;
    }
}

// ─────────────────────────────────────────────────────────────────────────────────
//  El motor
// ─────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// El despliegue en sí: leer el paquete, respaldar, publicar servidor por servidor y cerrar el
/// trabajo. Es el cuerpo del <c>DeployAsync</c> del escritorio, con sus decisiones intactas:
///
///  · <b>Contexto de datos propio</b>. En el escritorio hacía falta porque el <c>AppDbContext</c>
///    era Singleton y un despliegue largo chocaba con cualquier otra pantalla. Aquí el contexto ya
///    es por petición, pero el despliegue no TIENE petición: corre en su propio ámbito.
///  · <b>Bitácora persistida paso a paso</b>: acumularla hasta el final hacía que una cancelación se
///    llevara el registro entero y dejara el trabajo eternamente «En curso».
///  · <b>Respaldo previo</b> de la carpeta remota, opcional y por servidor. Si el respaldo falla, no
///    se despliega: desplegar creyendo que hay red de seguridad es peor que saber que no la hay.
///  · <b>Un servidor desactivado no recibe despliegues</b>, aunque siga colgando del perfil.
///  · <b>«Parcial»</b> cuando unos servidores sí y otros no; antes eso se reportaba «Completado».
///  · <b>La cancelación no se propaga</b>: cierra el trabajo ordenadamente en vez de dejarlo colgado.
///
/// No tiene dependencias inyectadas y todo entra por parámetro. Eso es lo que permite probarlo
/// entero —incluidas la cancelación y la caída de un servidor— sin tocar la red.
/// </summary>
public sealed class MotorDeDespliegue
{
    public async Task<DeploymentJob> EjecutarAsync(
        AppDbContext db,
        IPublicacionDeDespliegue publicacion,
        AuditService bitacoraDeAuditoria,
        IAvisosDeDespliegue avisos,
        OpcionesDeEjecucion opciones,
        RespaldoPrevioService respaldo,
        AlmacenamientoService almacenamiento,
        TrabajoVivo trabajo,
        CancellationToken ct)
    {
        var orden = trabajo.Orden;
        var correlacion = AuditService.NuevaCorrelacion();

        var job = await db.DeploymentJobs.FirstAsync(j => j.Id == orden.JobId, CancellationToken.None);

        // El orden lo fija la selección, no la base: es el que la persona vio al elegir, y en un
        // despliegue escalonado (primero un nodo, luego el resto) ese orden es la mitad del plan.
        var posicion = orden.ServidorIds
            .Select((id, indice) => (id, indice))
            .ToDictionary(p => p.id, p => p.indice);
        var servidores = await db.DeploymentTargets
            .Where(t => orden.ServidorIds.Contains(t.Id) && t.IsActive)
            .ToListAsync(CancellationToken.None);
        servidores = [.. servidores.OrderBy(t => posicion[t.Id])];

        var relojTotal = System.Diagnostics.Stopwatch.StartNew();
        int ok = 0, fallidos = 0, procesados = 0;
        bool cancelado = false;

        var fina = new BitacoraFina(trabajo, avisos);

        int conRespaldo = servidores.Count(t => opciones.RespaldoHabilitado && orden.RespaldarIds.Contains(t.Id));
        var respaldoResumen = !opciones.RespaldoHabilitado ? "desactivado en la configuración"
                            : conRespaldo == servidores.Count ? "todos"
                            : conRespaldo == 0 ? "ninguno"
                            : $"{conRespaldo} de {servidores.Count}";

        fina.Report($"🚀  Iniciando despliegue de {orden.Sistema} v{orden.Version}");
        fina.Report($"    Destino: {orden.Destino}");
        fina.Report($"    Bitácora: {correlacion}   ·   Respaldo previo: {respaldoResumen}");

        await AnotarAsync(db, trabajo, avisos, null, null,
            $"Despliegue iniciado por {orden.Quien.ParaMostrar()} (correlación {correlacion}). " +
            $"Respaldo previo: {respaldoResumen}.", DeployLogLevel.Info);

        await bitacoraDeAuditoria.RecordDetailedAsync(AuditAction.Deploy, "DeploymentJob", job.Id.ToString(),
            $"Inicio: {orden.Sistema} v{orden.Version} → {orden.Destino} ({servidores.Count} servidor(es))",
            AuditOutcome.Exito, correlationId: correlacion, ct: CancellationToken.None);

        try
        {
            // El paquete se lee UNA vez y se publica a todos los servidores desde memoria, igual que
            // en el escritorio: ni se extrae a una carpeta temporal ni se vuelve a leer por servidor.
            // La integridad de cada archivo la valida el CRC del propio ZIP al abrirlo.
            fina.Report("📂  Leyendo el paquete…");
            var (rutaDelPaquete, paqueteTemporal) =
                await RutaDelPaqueteAsync(db, almacenamiento, orden.VersionId, fina, ct);

            IReadOnlyList<ArchivoDelPaquete> archivos;
            try
            {
                archivos = await LeerPaqueteAsync(rutaDelPaquete, ct);
            }
            finally
            {
                // El temporal se borra en cuanto el paquete está en memoria, y no al final: un
                // despliegue a veinte servidores puede durar media hora, y dejar ahí un .zip de
                // cientos de megas todo ese rato llena el disco del servidor sin motivo.
                if (paqueteTemporal)
                    try { File.Delete(rutaDelPaquete); } catch { /* temporal: no vale la pena */ }
            }

            fina.Report($"  ✓  {archivos.Count} archivos listos para publicar.");
            await AnotarAsync(db, trabajo, avisos, null, null,
                $"Paquete leído: {archivos.Count} archivo(s).", DeployLogLevel.Info);

            for (int i = 0; i < servidores.Count; i++)
            {
                var servidor = servidores[i];
                if (ct.IsCancellationRequested) { cancelado = true; break; }

                fina.Report($"🌐  [{servidor.Nombre}]");
                var reloj = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    await ADesplegarAsync(db, publicacion, trabajo, avisos, opciones, respaldo, orden,
                        servidor, archivos, i, servidores.Count, fina, ct);
                    reloj.Stop();

                    // Lo que responde «¿qué tiene este servidor, quién se lo puso y cuándo?» sin
                    // reconstruirlo leyendo la bitácora entera. Se guarda EN CUANTO termina: si el
                    // despliegue falla en el siguiente, lo ya publicado queda igual de registrado.
                    servidor.LastDeployedAt = DateTime.UtcNow;
                    servidor.LastReleaseId = orden.VersionId;
                    servidor.LastDeployedById = orden.Quien.UserId;
                    servidor.LastDeploymentJobId = job.Id;
                    await db.SaveChangesAsync(CancellationToken.None);

                    await AnotarAsync(db, trabajo, avisos, servidor.Id, servidor.Nombre,
                        $"Despliegue completado en {reloj.Elapsed.TotalSeconds:0.0}s.", DeployLogLevel.Exito);
                    ok++;
                }
                catch (OperationCanceledException)
                {
                    // La cancelación NO se propaga: se cierra el trabajo ordenadamente para no perder
                    // la bitácora ni dejarlo colgado en «En curso».
                    cancelado = true;
                    await AnotarAsync(db, trabajo, avisos, servidor.Id, servidor.Nombre,
                        "Cancelado durante este servidor.", DeployLogLevel.Advertencia);
                    break;
                }
                catch (Exception ex)
                {
                    reloj.Stop();
                    await AnotarAsync(db, trabajo, avisos, servidor.Id, servidor.Nombre,
                        $"Error tras {reloj.Elapsed.TotalSeconds:0.0}s: {ex.Message}", DeployLogLevel.Error);
                    fallidos++;
                }

                procesados++;
                Avanzar(trabajo, avisos, (int)Math.Round((double)procesados / servidores.Count * 100),
                    servidor.Nombre, $"servidor {procesados} de {servidores.Count} listo");
            }
        }
        catch (OperationCanceledException) { cancelado = true; }
        catch (Exception ex)
        {
            // Un fallo ANTES de tocar ningún servidor (el paquete no está, no se puede leer): se
            // registra como error del trabajo entero en vez de dejarlo «En curso» para siempre.
            await AnotarAsync(db, trabajo, avisos, null, null, ex.Message, DeployLogLevel.Error);
            fallidos = servidores.Count;
        }

        int sinIntentar = Math.Max(0, servidores.Count - ok - fallidos);
        job.Status = cancelado ? JobStatus.Cancelado
                   : fallidos == 0 && ok > 0 ? JobStatus.Completado
                   : ok == 0 ? JobStatus.Fallido
                   : JobStatus.Parcial;
        job.CompletedAt = DateTime.UtcNow;
        job.TargetsOk = ok;
        job.TargetsFailed = fallidos;
        // CancellationToken.None: el cierre del trabajo debe guardarse aunque se haya cancelado.
        await db.SaveChangesAsync(CancellationToken.None);

        relojTotal.Stop();
        var resumen = $"{orden.Sistema} v{orden.Version} → {orden.Destino}: {ok} OK, {fallidos} fallidos"
                    + (sinIntentar > 0 ? $", {sinIntentar} sin intentar" : "")
                    + $" en {TextosDeDespliegue.Duracion(relojTotal.Elapsed)}";

        await AnotarAsync(db, trabajo, avisos, null, null, $"Fin ({TextosDeDespliegue.Estado(job.Status)}). {resumen}",
            job.Status == JobStatus.Completado ? DeployLogLevel.Exito : DeployLogLevel.Advertencia);

        await bitacoraDeAuditoria.RecordDetailedAsync(AuditAction.Deploy, "DeploymentJob", job.Id.ToString(),
            resumen, job.Status is JobStatus.Completado ? AuditOutcome.Exito : AuditOutcome.Fallo,
            newValues: new { job.Status, job.TargetsOk, job.TargetsFailed, SinIntentar = sinIntentar },
            correlationId: correlacion, ct: CancellationToken.None);

        var fin = new FinDeDespliegueDto(job.Id, job.Status, TextosDeDespliegue.Estado(job.Status),
            ok, fallidos, sinIntentar, resumen);
        Avanzar(trabajo, avisos, 100, "", $"{TextosDeDespliegue.Estado(job.Status)} en {TextosDeDespliegue.Duracion(relojTotal.Elapsed)}");
        trabajo.Terminar(fin, DateTime.UtcNow);
        trabajo.Emitir(() => avisos.FinAsync(job.Id, fin));

        return job;
    }

    // ── Un servidor ──────────────────────────────────────────────────────────────

    private static async Task ADesplegarAsync(
        AppDbContext db, IPublicacionDeDespliegue publicacion, TrabajoVivo trabajo,
        IAvisosDeDespliegue avisos, OpcionesDeEjecucion opciones, RespaldoPrevioService respaldo,
        OrdenDeDespliegue orden,
        DeploymentTarget servidor, IReadOnlyList<ArchivoDelPaquete> archivos,
        int indice, int total, IProgress<string> fina, CancellationToken ct)
    {
        // Sin respaldo al valor crudo si no se puede descifrar. Antes, cuando fallaba, se mandaba el
        // texto CIFRADO como contraseña: el servidor contestaba «530 User cannot log in» y el
        // problema parecía de las credenciales del FTP y no de esta aplicación.
        var contrasena = ProtectorPortable.Descifrar(servidor.Contrasena);
        if (contrasena == null)
            throw new InvalidOperationException(
                $"No se pudo descifrar la contraseña del servidor «{servidor.Nombre}». " +
                (ProtectorPortable.EsHeredadoDeWindows(servidor.Contrasena)
                    ? "Se guardó desde una PC con el cifrado antiguo de Windows, que el servidor no puede leer. " +
                      "Un líder debe volver a capturarla en Despliegues → Servidores (una sola vez)."
                    : "Vuelve a capturarla en Despliegues → Servidores."));

        var destino = new DestinoDeDespliegue(
            servidor.Nombre, servidor.Host, servidor.Puerto, servidor.Usuario, contrasena, servidor.RutaRemota);

        void Estado(string detalle, int hechos)
        {
            // Avance global = (servidores ya terminados + fracción de archivos del actual) / total.
            double fraccion = archivos.Count <= 0 ? 0 : (double)hechos / archivos.Count;
            int pct = (int)Math.Round((indice + fraccion) / Math.Max(1, total) * 100);
            Avanzar(trabajo, avisos, pct, servidor.Nombre, detalle);
        }

        // 1. Respaldo de la carpeta remota, ANTES de tocar nada. Si no se puede respaldar, no se
        //    despliega: es la regla que traía el escritorio y la que da sentido al respaldo.
        if (opciones.RespaldoHabilitado && orden.RespaldarIds.Contains(servidor.Id))
        {
            Estado("respaldando la carpeta remota…", 0);

            // El ZIP sube a Azure Blob, que es donde lo dejaba el escritorio y donde tiene sentido:
            // guardarlo en la carpeta de despliegue del propio servidor lo pondría en el mismo disco
            // que se está a punto de sobrescribir, y un respaldo que vive junto a lo que protege no
            // protege de gran cosa. Si el respaldo falla, la excepción SUBE y este servidor no recibe
            // el despliegue: es la regla del escritorio y no se relaja.
            var blob = await respaldo.RespaldarAsync(servidor, contrasena, orden.JobId, fina, ct);

            await AnotarAsync(db, trabajo, avisos, servidor.Id, servidor.Nombre,
                blob == null
                    ? "Sin respaldo: la carpeta remota estaba vacía (primer despliegue a este servidor)."
                    : $"Respaldo guardado como «{blob}».",
                DeployLogLevel.Info);
        }
        else
        {
            await AnotarAsync(db, trabajo, avisos, servidor.Id, servidor.Nombre,
                opciones.RespaldoHabilitado
                    ? "Sin respaldo previo para este servidor (elección de quien desplegó)."
                    : "Sin respaldo previo: está desactivado en la configuración.",
                DeployLogLevel.Advertencia);
        }

        // 2. Publicación.
        Estado("conectando…", 0);
        int subidos = await publicacion.PublicarAsync(destino, archivos, fina,
            (hechos, cual) => Estado($"subiendo {cual}  ({hechos + 1}/{archivos.Count})", hechos), ct);

        fina.Report($"  ✓  {subidos} archivos publicados → {servidor.RutaRemota}");
        await AnotarAsync(db, trabajo, avisos, servidor.Id, servidor.Nombre,
            $"{subidos} archivo(s) publicados a {servidor.RutaRemota}.", DeployLogLevel.Info);
    }

    /// <summary>
    // El respaldo previo lo hace RespaldoPrevioService, que sube el ZIP a Azure Blob igual que el
    // escritorio. Aquí hubo un rato una versión propia que lo dejaba en la carpeta de despliegue del
    // servidor, porque el servicio de Blob se estaba portando en paralelo y todavía no existía; ya
    // existe, y guardar el respaldo en el mismo disco que se va a sobrescribir no protegía de nada.

    // ── Paquete ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dónde está el .zip de la versión, dejándolo alcanzable para el motor.
    ///
    /// <para>Dos orígenes, y el orden importa: primero la carpeta de despliegue del servidor, que no
    /// cuesta nada; si no está ahí pero sí en Azure Blob, se BAJA a un temporal. Eso último faltaba
    /// —el servicio de Blob se portó en paralelo y nadie los unió—, y su ausencia dejaba inservible
    /// una versión perfectamente registrada: el despliegue moría con un «solo está en Blob».</para>
    ///
    /// <para>El temporal lo borra quien llama (ver <see cref="LeerPaqueteAsync"/>, que lo lee entero
    /// a memoria antes de volver).</para>
    /// </summary>
    private static async Task<(string ruta, bool esTemporal)> RutaDelPaqueteAsync(
        AppDbContext db, AlmacenamientoService almacenamiento, int versionId, IProgress<string> fina,
        CancellationToken ct)
    {
        var version = await db.AppReleases.AsNoTracking().FirstOrDefaultAsync(r => r.Id == versionId, ct)
            ?? throw new InvalidOperationException("La versión ya no existe.");

        if (!string.IsNullOrWhiteSpace(version.ZipLocalPath) && File.Exists(version.ZipLocalPath))
            return (version.ZipLocalPath!, false);

        if (string.IsNullOrWhiteSpace(version.ZipBlobUrl))
            throw new FileNotFoundException(
                "El servidor no encuentra el paquete de esta versión en la carpeta de despliegue. " +
                "Vuelve a dejarlo ahí y regístralo, o elige otra versión.");

        if (!await almacenamiento.EstaConfiguradoAsync(ct))
            throw new InvalidOperationException(
                "El paquete de esta versión está en Azure Blob Storage, pero Blob no está configurado " +
                "en este servidor. Configúralo, o deja el .zip en la carpeta de despliegue.");

        fina.Report($"  ⬇  Bajando el paquete desde el almacenamiento…");

        var temporal = Path.Combine(Path.GetTempPath(), $"paquete_{versionId}_{Guid.NewGuid():N}.zip");

        // Por la vía SIN guarda de sesión, no por la del explorador de almacenamiento. Aquí no hay
        // nadie conectado —el programado usa una identidad ficticia y el manual corre en un Task.Run
        // sin HttpContext—, así que el RequireAdmin de la otra saltaba siempre y el despliegue moría
        // con «requiere permisos de líder». Además admite el ZipBlobUrl en sus dos formas: nombre de
        // blob (lo que escribe la web) y URL absoluta (lo que dejó escrito el escritorio).
        var contenido = await almacenamiento.AbrirPaqueteDeDespliegueAsync(version.ZipBlobUrl!, ct);
        await using (contenido)
        await using (var destino = File.Create(temporal))
            await contenido.CopyToAsync(destino, ct);

        fina.Report($"  ✓  Paquete descargado ({new FileInfo(temporal).Length / 1024d / 1024d:0.0} MB).");
        return (temporal, true);
    }

    /// <summary>
    /// Lee las entradas del ZIP a memoria. Al leer cada entrada completa se valida su CRC, así que
    /// la comprobación de integridad va incluida y no hace falta un checksum aparte por archivo.
    /// </summary>
    public static async Task<List<ArchivoDelPaquete>> LeerPaqueteAsync(string rutaZip, CancellationToken ct)
    {
        var lista = new List<ArchivoDelPaquete>();
        await using var flujo = File.OpenRead(rutaZip);
        using var zip = new ZipArchive(flujo, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entrada in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            // Entradas de carpeta: nombre vacío o el nombre completo termina en «/».
            if (string.IsNullOrEmpty(entrada.Name) || entrada.FullName.EndsWith('/')) continue;

            await using var lectura = entrada.Open();
            using var memoria = new MemoryStream();
            await lectura.CopyToAsync(memoria, ct);
            lista.Add(new ArchivoDelPaquete(entrada.FullName.Replace('\\', '/'), memoria.ToArray()));
        }

        if (lista.Count == 0)
            throw new InvalidOperationException("El paquete de esta versión no contiene archivos.");

        return lista;
    }

    // ── Bitácora y avance ────────────────────────────────────────────────────────

    /// <summary>
    /// Escribe un hito en <c>DeploymentLogEntry</c> y lo cuenta en vivo. Se PERSISTE de inmediato:
    /// acumular hasta el final hacía que una cancelación o una caída se llevaran el registro entero.
    /// </summary>
    private static async Task AnotarAsync(
        AppDbContext db, TrabajoVivo trabajo, IAvisosDeDespliegue avisos,
        int? servidorId, string? servidor, string mensaje, DeployLogLevel nivel)
    {
        var ahora = DateTime.UtcNow;
        db.DeploymentLogEntries.Add(new DeploymentLogEntry
        {
            JobId = trabajo.Orden.JobId,
            TargetId = servidorId,
            TargetName = servidor,
            Message = mensaje,
            Level = nivel,
            Timestamp = ahora
        });
        await db.SaveChangesAsync(CancellationToken.None);

        var renglon = new RenglonDeBitacoraDto(ahora, servidor, mensaje, nivel);
        trabajo.Anotar(renglon);
        trabajo.Emitir(() => avisos.RegistroAsync(trabajo.Orden.JobId, renglon));
    }

    private static void Avanzar(TrabajoVivo trabajo, IAvisosDeDespliegue avisos, int pct, string servidor, string detalle)
    {
        var avance = new AvanceDeDespliegueDto(trabajo.Orden.JobId, Math.Clamp(pct, 0, 100), servidor, detalle);
        trabajo.Avanzar(avance);
        trabajo.Emitir(() => avisos.AvanceAsync(trabajo.Orden.JobId, avance));
    }

    /// <summary>
    /// La bitácora fina —qué archivo va, qué reintento— que la capa de FTP reporta mientras trabaja.
    /// Va al canal en vivo y a la memoria, pero NO a la base: son miles de renglones por despliegue y
    /// el escritorio ya tomaba esta misma decisión (la consola los enseñaba, el log guardaba hitos).
    /// </summary>
    private sealed class BitacoraFina(TrabajoVivo trabajo, IAvisosDeDespliegue avisos) : IProgress<string>
    {
        public void Report(string valor)
        {
            var renglon = new RenglonDeBitacoraDto(DateTime.UtcNow, null, valor, DeployLogLevel.Info);
            trabajo.Anotar(renglon);
            trabajo.Emitir(() => avisos.RegistroAsync(trabajo.Orden.JobId, renglon));
        }
    }

}

// ─────────────────────────────────────────────────────────────────────────────────
//  El ejecutor
// ─────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Quien pone a correr los despliegues y los sostiene mientras corren.
///
/// <para><b>Es Singleton, y ahí está todo el asunto de esta vertical.</b> Un despliegue dura minutos
/// y no puede vivir dentro de la petición que lo pidió: en cuanto la respuesta sale, el ámbito de la
/// petición se cierra y con él su contexto de datos. Por eso cada despliegue abre su propio ámbito,
/// exactamente igual que los trabajos periódicos, y por eso cerrar la pestaña no lo interrumpe.</para>
///
/// <para>Cancelar sigue existiendo, pero es una acción deliberada: hay que pedirla.</para>
/// </summary>
public sealed class EjecutorDeDespliegues(
    IServiceScopeFactory ambitos,
    IAvisosDeDespliegue avisos,
    ILogger<EjecutorDeDespliegues> log)
{
    private readonly RegistroDeDespliegues _registro = new();
    private readonly MotorDeDespliegue _motor = new();

    /// <summary>Pone el despliegue en marcha y devuelve de inmediato. El trabajo sigue por su cuenta.</summary>
    public void Lanzar(OrdenDeDespliegue orden)
    {
        _registro.Limpiar(DateTime.UtcNow);
        var trabajo = _registro.Registrar(orden, DateTime.UtcNow);

        // Sin await y sin el token de la petición: es justamente lo que se busca. El token de la
        // petición se cancela al responder, y atarlo aquí mataría el despliegue en cuanto el
        // navegador recibiera el «ya arrancó».
        _ = Task.Run(() => CorrerAsync(trabajo));
    }

    /// <summary>
    /// Igual que <see cref="Lanzar"/>, pero ESPERA a que termine y devuelve cómo acabó.
    ///
    /// <para>La usa el disparo de un despliegue PROGRAMADO. Ahí no hay ninguna pantalla mirando el
    /// avance en vivo: quien llama es un trabajo de fondo que tiene que anotar en la cita si salió
    /// bien o mal, y para eso necesita el final. Con <see cref="Lanzar"/> —que vuelve de inmediato,
    /// como debe ser cuando hay alguien esperando una respuesta en el navegador— la cita quedaría
    /// marcada como completada antes de que el despliegue hubiera empezado siquiera.</para>
    ///
    /// <para>Es la MISMA maquinaria: mismo motor, mismo ámbito por trabajo, mismos avisos en vivo por
    /// si alguien abre la consola mientras corre. Lo único que cambia es que aquí se aguarda.</para>
    /// </summary>
    public async Task<TrabajoVivo> LanzarYEsperarAsync(OrdenDeDespliegue orden)
    {
        _registro.Limpiar(DateTime.UtcNow);
        var trabajo = _registro.Registrar(orden, DateTime.UtcNow);

        await CorrerAsync(trabajo);
        return trabajo;
    }

    private async Task CorrerAsync(TrabajoVivo trabajo)
    {
        // La PRIMERA señal se planta antes de tocar nada, y esperándola. Dejársela al primer latido
        // costaría poco pero abriría un hueco: el trabajo estaría corriendo unos milisegundos sin
        // que ninguna otra instancia pudiera saberlo. Al lanzar desde la pantalla eso da igual
        // —DeploymentService ya la plantó bajo candado, junto con la fila del trabajo—, pero un
        // despliegue PROGRAMADO no pasa por ahí y esta es toda su reserva.
        await RefrescarSenalAsync(trabajo.Orden.JobId);

        // A partir de aquí se renueva sola, con un token PROPIO y no con el de la cancelación del
        // trabajo: cancelar no detiene el despliegue en el acto —se cierra en cuanto termina el
        // archivo que está subiendo— y si la señal se apagara al pedir la cancelación, otra
        // instancia daría el servidor por libre mientras todavía se le está escribiendo encima.
        using var latido = new CancellationTokenSource();
        var senal = MantenerSenalAsync(trabajo.Orden.JobId, latido.Token);

        try
        {
            using var ambito = ambitos.CreateScope();
            var servicios = ambito.ServiceProvider;
            var db = servicios.GetRequiredService<AppDbContext>();
            var publicacion = servicios.GetRequiredService<IPublicacionDeDespliegue>();
            var configuracion = servicios.GetRequiredService<SettingsService>();
            var respaldo = servicios.GetRequiredService<RespaldoPrevioService>();
            var almacenamiento = servicios.GetRequiredService<AlmacenamientoService>();

            // La bitácora de auditoría se arma a mano con la identidad congelada: la del ámbito la
            // leería de una petición que ya no existe y firmaría «sistema» un despliegue que tiene
            // dueño con nombre y apellido.
            var auditoria = new AuditService(db, trabajo.Orden.Quien, new OrigenDelServidor());

            var opciones = new OpcionesDeEjecucion(
                await RespaldoHabilitadoAsync(configuracion),
                await configuracion.ObtenerAsync(SettingsService.Claves.DefaultDeployFolder));

            await _motor.EjecutarAsync(db, publicacion, auditoria, avisos, opciones, respaldo, almacenamiento,
                trabajo, trabajo.Cancelacion.Token);
        }
        catch (Exception ex)
        {
            // Aquí solo llega lo que el motor no pudo manejar (no hay contexto, no hay servicio). El
            // trabajo se cierra igualmente para que la pantalla no se quede esperando un final que
            // no va a llegar.
            log.LogError(ex, "El despliegue {JobId} terminó de forma inesperada.", trabajo.Orden.JobId);
            if (!trabajo.Terminado)
            {
                var fin = new FinDeDespliegueDto(trabajo.Orden.JobId, JobStatus.Fallido,
                    TextosDeDespliegue.Estado(JobStatus.Fallido), 0, 0, 0,
                    "El despliegue no pudo completarse. Revisa el registro del servidor.");
                trabajo.Terminar(fin, DateTime.UtcNow);
                trabajo.Emitir(() => avisos.FinAsync(trabajo.Orden.JobId, fin));
            }

            // La pantalla ya sabe que acabó, pero la FILA seguiría «En curso»: el motor es quien la
            // cierra y aquí no llegó a correr. Sin esto, un fallo al montar el ámbito dejaba un
            // trabajo eternamente en marcha hasta el siguiente arranque.
            await CerrarTrabajoAtascadoAsync(trabajo.Orden.JobId, ex);
        }
        finally
        {
            latido.Cancel();
            // Se espera al latido ANTES de retirar la señal: al revés podría volver a escribirla
            // justo después de borrarla y dejar el servidor ocupado hasta que caducara.
            await senal;
            await RetirarSenalAsync(trabajo.Orden.JobId);

            trabajo.Cancelacion.Dispose();
        }
    }

    /// <summary>
    /// Renueva la señal de vida cada <see cref="SenalDeVidaDelDespliegue.Latido"/> mientras el
    /// despliegue dura. Empieza durmiendo porque la primera ya la plantó quien arrancó el trabajo.
    /// </summary>
    private async Task MantenerSenalAsync(int jobId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(SenalDeVidaDelDespliegue.Latido, ct); }
            catch (OperationCanceledException) { return; }

            await RefrescarSenalAsync(jobId, ct);
        }
    }

    /// <summary>
    /// Escribe la señal de vida. Abre su PROPIO ámbito: el contexto del despliegue lo está usando el
    /// motor y un <c>DbContext</c> no se puede compartir entre dos hilos — el síntoma serían fallos
    /// aleatorios en mitad de una subida, que es de lo peor que se puede diagnosticar.
    ///
    /// <para>Que no se pueda escribir NO tumba el despliegue: se anota y se sigue. La consecuencia
    /// está asumida y es la menos mala — si la base no contesta durante minutos, el despliegue pasa a
    /// parecer muerto desde fuera y su servidor queda declarado libre. Detenerlo por eso sería peor:
    /// se cortaría una subida a medias por un problema que no es suyo.</para>
    /// </summary>
    private async Task RefrescarSenalAsync(int jobId, CancellationToken ct = default)
    {
        try
        {
            using var ambito = ambitos.CreateScope();
            var db = ambito.ServiceProvider.GetRequiredService<AppDbContext>();
            await SenalDeVidaDelDespliegue.RefrescarAsync(db, jobId, DateTime.UtcNow, ct);
        }
        catch (OperationCanceledException) { /* se acabó el trabajo mientras se escribía */ }
        catch (Exception ex)
        {
            log.LogWarning(ex,
                "No se pudo refrescar la señal de vida del despliegue {JobId}. Si no se recupera, " +
                "otra instancia podría dar por libre su servidor.", jobId);
        }
    }

    private async Task RetirarSenalAsync(int jobId)
    {
        try
        {
            using var ambito = ambitos.CreateScope();
            var db = ambito.ServiceProvider.GetRequiredService<AppDbContext>();
            await SenalDeVidaDelDespliegue.RetirarAsync(db, jobId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // No es grave y no vale la pena insistir: la señal caduca sola y, hasta entonces, lo
            // único que pasa es que ese servidor no admite otro despliegue durante unos minutos.
            log.LogWarning(ex, "No se pudo retirar la señal de vida del despliegue {JobId}.", jobId);
        }
    }

    /// <summary>
    /// Cierra en la base un trabajo que ni siquiera llegó a arrancar porque no se pudo montar su
    /// ámbito (falta un servicio, la base no contesta).
    ///
    /// <para>Es el mismo agujero que tapa la reconciliación del arranque, pero visto desde dentro y
    /// con una ventaja: aquí se sabe POR QUÉ, porque hay una excepción en la mano. Es lo mejor que se
    /// puede hacer y no una garantía —si lo que falló fue la propia base, esto también falla— y
    /// entonces sí queda para la reconciliación del próximo arranque.</para>
    /// </summary>
    private async Task CerrarTrabajoAtascadoAsync(int jobId, Exception causa)
    {
        try
        {
            using var ambito = ambitos.CreateScope();
            var db = ambito.ServiceProvider.GetRequiredService<AppDbContext>();

            var job = await db.DeploymentJobs.FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);

            // Solo si sigue abierto: el motor pudo haberlo cerrado ya con su resultado de verdad, y
            // pisarlo con un «Fallido» genérico perdería el único dato bueno que quedaba.
            if (job is null || job.Status != JobStatus.EnCurso) return;

            job.Status = JobStatus.Fallido;
            job.CompletedAt = DateTime.UtcNow;

            db.DeploymentLogEntries.Add(new DeploymentLogEntry
            {
                JobId = jobId,
                Timestamp = DateTime.UtcNow,
                Level = DeployLogLevel.Error,
                Message = "El despliegue no llegó a arrancar en el servidor y se cierra sin haber tocado " +
                          $"ningún destino: {causa.Message}"
            });

            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            log.LogError(ex,
                "Tampoco se pudo cerrar en la base el despliegue {JobId}, que quedó a medias. " +
                "Lo cerrará la reconciliación del próximo arranque.", jobId);
        }
    }

    /// <summary>
    /// El respaldo previo está activo SALVO que la configuración diga expresamente «false».
    ///
    /// Se lee el valor crudo y no con <c>ObtenerBooleanoAsync</c> a propósito: aquel da false a todo
    /// lo que no sea «true», y con una clave sin configurar —que es como está hoy en la base— el
    /// respaldo previo quedaría apagado sin que nadie lo hubiera apagado. Es la misma regla que el
    /// escritorio (<c>Get(...) != "false"</c>) y el sentido de la omisión importa: quien no se
    /// pronuncia obtiene la red de seguridad.
    /// </summary>
    private static async Task<bool> RespaldoHabilitadoAsync(SettingsService configuracion) =>
        !string.Equals(
            await configuracion.ObtenerAsync(SettingsService.Claves.DeployBackupEnabled),
            "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>Los despliegues que el servidor tiene entre manos (y los que acaban de terminar).</summary>
    public IReadOnlyList<TrabajoDeDespliegueDto> EnCurso(int? userId)
    {
        _registro.Limpiar(DateTime.UtcNow);
        return [.. _registro.Todos()
            .OrderByDescending(t => t.InicioUtc)
            .Select(t => t.Foto(userId))];
    }

    /// <summary>La foto de un despliegue concreto, para retomar su vista al volver a la pantalla.</summary>
    public TrabajoDeDespliegueDto? Trabajo(int jobId, int? userId) => _registro.Buscar(jobId)?.Foto(userId);

    /// <summary>¿Hay un despliegue en marcha tocando este servidor?</summary>
    public bool ServidorOcupado(int servidorId) => _registro.AlgunoUsa(servidorId);

    /// <summary>
    /// Cancela un despliegue. Es deliberado: cerrar la pestaña no cancela, hay que pedirlo.
    ///
    /// Lo puede cancelar quien lo lanzó y también un líder: si alguien arranca un despliegue a
    /// producción y se va a comer, dejar la única parada de emergencia en sus manos no es una regla,
    /// es un problema esperando.
    /// </summary>
    public (bool ok, string mensaje) Cancelar(int jobId, ICurrentUser quien)
    {
        var trabajo = _registro.Buscar(jobId);
        if (trabajo == null)
            return (false, "Ese despliegue ya no está en curso. Búscalo en el historial para ver cómo terminó.");
        if (trabajo.Terminado)
            return (false, "Ese despliegue ya terminó.");

        if (!quien.IsAdmin && quien.UserId != trabajo.Orden.Quien.UserId)
            throw new AuthorizationException(
                "Solo puede cancelar este despliegue quien lo lanzó, o un líder.");

        trabajo.Cancelacion.Cancel();
        return (true, "Cancelación pedida. El despliegue se cierra en cuanto termine el archivo que está subiendo.");
    }
}
