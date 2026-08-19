using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// EL AVISO EN AZURE DEVOPS DE QUE ALGUIEN EMPEZÓ, con su hora.
///
/// <para>Lo que estas pruebas defienden, por orden de lo que costaría equivocarse: que no se comente
/// en el ticket de otro, que no se comente dos veces lo mismo, que no se llene el ticket de un
/// cliente de avisos repetidos, y que un Azure DevOps caído no tumbe el cronómetro ni pierda el
/// aviso.</para>
/// </summary>
public class AvisoDeInicioEnDevOpsTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var c in _contextos) c.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Andamiaje ────────────────────────────────────────────────────────────────

    /// <summary>
    /// El cliente de DevOps de mentira. Solo implementa de verdad lo que este camino usa; lo demás
    /// lanza, para que una prueba que acabe llamando a otra cosa lo diga en voz alta.
    /// </summary>
    private sealed class DevOpsDeMentira : IClienteAzureDevOps
    {
        public List<(int numero, string html)> Publicados { get; } = [];
        public Exception? Fallo { get; set; }

        /// <summary>Se ejecuta DENTRO de la publicación. Es lo que permite mirar en qué estado está
        /// la base en el instante exacto en que se está hablando con DevOps.</summary>
        public Func<Task>? AlPublicar { get; set; }

        public async Task PublicarComentarioAsync(
            CredencialesDevOps c, int numero, string textoHtml, CancellationToken ct = default)
        {
            if (AlPublicar != null) await AlPublicar();
            if (Fallo != null) throw Fallo;
            Publicados.Add((numero, textoHtml));
        }

        private static T No<T>() => throw new InvalidOperationException(
            "El aviso de inicio no usa esta operación.");

        public Task<IReadOnlyList<int>> ConsultarIdsAsync(CredencialesDevOps c, string? w, CancellationToken ct = default) => No<Task<IReadOnlyList<int>>>();
        public Task<IReadOnlyList<WorkItemDevOps>> ObtenerWorkItemsAsync(CredencialesDevOps c, IReadOnlyCollection<int> i, CancellationToken ct = default) => No<Task<IReadOnlyList<WorkItemDevOps>>>();
        public Task<IReadOnlyList<ComentarioDevOps>> ObtenerComentariosAsync(CredencialesDevOps c, int n, CancellationToken ct = default) => No<Task<IReadOnlyList<ComentarioDevOps>>>();
        public Task<string> SubirAdjuntoAsync(CredencialesDevOps c, byte[] b, string n, CancellationToken ct = default) => No<Task<string>>();
        public Task<(string nombre, string correo)> ReasignarAsync(CredencialesDevOps c, int n, string? correo, CancellationToken ct = default) => No<Task<(string, string)>>();
        public Task<string> CambiarEstadoAsync(CredencialesDevOps c, int n, string e, CancellationToken ct = default) => No<Task<string>>();
        public Task<ColumnaDeTablero> CambiarColumnaAsync(CredencialesDevOps c, int n, string col, bool m, CancellationToken ct = default) => No<Task<ColumnaDeTablero>>();
        public Task<ColumnaDeTablero> LeerColumnaAsync(CredencialesDevOps c, int n, CancellationToken ct = default) => No<Task<ColumnaDeTablero>>();
        public Task CambiarPrioridadAsync(CredencialesDevOps c, int n, int p, CancellationToken ct = default) => No<Task>();
        public Task<(bool escrito, string aviso)> EscribirEstimacionAsync(CredencialesDevOps c, int n, double h, CancellationToken ct = default) => No<Task<(bool, string)>>();
        public Task<bool> SumarTrabajoCompletadoAsync(CredencialesDevOps c, int n, double h, bool r, CancellationToken ct = default) => No<Task<bool>>();
        public Task<IReadOnlyList<BugHijoDevOps>> ObtenerBugsHijosAsync(CredencialesDevOps c, int n, CancellationToken ct = default) => No<Task<IReadOnlyList<BugHijoDevOps>>>();
        public Task<IReadOnlyList<CambioDeAsignacionDevOps>> ObtenerHistorialDeAsignacionAsync(CredencialesDevOps c, int n, CancellationToken ct = default) => No<Task<IReadOnlyList<CambioDeAsignacionDevOps>>>();
        public Task<(bool ok, string mensaje)> ProbarCredencialesAsync(CredencialesDevOps c, CancellationToken ct = default) => No<Task<(bool, string)>>();
    }

    /// <summary>Una base con la integración encendida y credenciales de instalación puestas.</summary>
    private AppDbContext Base(bool encendido = true, bool conCredenciales = true)
    {
        var db = TestDb.New();
        _contextos.Add(db);

        void Ajuste(string clave, string valor) => db.AppSettings.Add(new AppSetting { Key = clave, Value = valor });

        Ajuste(SettingsService.Claves.AzureDevOpsEnabled, "true");
        Ajuste(SettingsService.Claves.CronometroAvisoDeInicio, encendido ? "true" : "false");

        if (conCredenciales)
        {
            Ajuste(SettingsService.Claves.AzureDevOpsOrgUrl, "https://dev.azure.com/zorroDesierto");
            Ajuste(SettingsService.Claves.AzureDevOpsProject, "Webpro");
            Ajuste(SettingsService.Claves.AzureDevOpsPat, "pat-de-la-instalacion");
        }

        db.SaveChanges();
        return db;
    }

    /// <summary>Sin sesión: es como lo llama el barrido, y es donde revientan las guardas.</summary>
    private static AvisoDeInicioEnDevOpsService Avisos(AppDbContext db, IClienteAzureDevOps cliente)
    {
        var cu = UsuarioDePrueba.Anonimo();
        return new AvisoDeInicioEnDevOpsService(
            db, new SettingsService(db, cu, new AuditService(db, cu, new OrigenDePrueba())),
            new AuditService(db, cu, new OrigenDePrueba()), cliente);
    }

    private static int NuevoDev(AppDbContext db, string nombre = "Ana Pérez")
    {
        var d = new Developer { FullName = nombre, IsActive = true };
        db.Developers.Add(d);
        db.SaveChanges();
        return d.Id;
    }

    /// <summary>Una actividad del pool tomada y ligada a un work item, con su percha y su sesión.</summary>
    /// <summary>
    /// El arranque nace RECIENTE porque el aviso tiene límite de antigüedad: una sesión vieja ya no
    /// se anuncia, ni por el barrido ni por el camino inmediato.
    /// </summary>
    private static (int sesionId, int devId) Escenario(
        AppDbContext db, int workItem = 4321, DateTime? inicio = null)
    {
        int devId = NuevoDev(db);

        var percha = new DevActivity
        {
            DeveloperId = devId, Title = "Pool #1: corregir el cálculo", Status = DevActivityStatus.Abierta
        };
        db.DevActivities.Add(percha);
        db.SaveChanges();

        db.PoolActivities.Add(new PoolActivity
        {
            Title = "Corregir el cálculo",
            WorkType = PoolWorkType.Tarea,
            Complexity = PoolComplexity.Alta,
            Priority = PoolPriority.Alta,
            LinkedDevActivityId = percha.Id,
            DevOpsWorkItemId = workItem
        });

        var sesion = new WorkSession
        {
            DeveloperId = devId,
            ActivityId = percha.Id,
            StartedAt = inicio ?? DateTime.UtcNow.AddMinutes(-5),
            LastResumedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Status = WorkSessionStatus.Activa
        };
        db.WorkSessions.Add(sesion);
        db.SaveChanges();

        return (sesion.Id, devId);
    }

    private static DateTime? MarcaDe(AppDbContext db, int sesionId)
    {
        db.ChangeTracker.Clear();
        return db.WorkSessions.AsNoTracking().Single(w => w.Id == sesionId).InicioComentadoEnUtc;
    }

    // ── Lo feliz ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// El aviso sale con el nombre dentro y con LA HORA A LA QUE SE EMPEZÓ, no la hora a la que se
    /// publica. La diferencia importa: el barrido puede publicar un rato después, y un comentario
    /// que dijera «empezó ahora» estaría mintiendo sobre lo único que viene a contar.
    /// </summary>
    [Fact]
    public async Task ElAvisoLlevaElNombreYLaHoraEnQueSeEmpezo()
    {
        using var db = Base();
        var haceRato = DateTime.UtcNow.AddMinutes(-90);
        var (sesionId, _) = Escenario(db, inicio: haceRato);
        var cliente = new DevOpsDeMentira();

        var texto = await Avisos(db, cliente).AvisarInicioAsync(sesionId);

        var (numero, html) = Assert.Single(cliente.Publicados);
        Assert.Equal(4321, numero);

        // El nombre va DENTRO porque el comentario lo firma la cuenta compartida y el autor que se
        // ve en DevOps no dice nada. Escapado —el acento sale como entidad— porque el nombre lo
        // escribió una persona y esto acaba dentro de un documento HTML ajeno; DevOps lo pinta bien.
        Assert.Contains("Ana P&#233;rez", html);

        // La hora es la del arranque y no la de ahora, dicha en la zona de la organización. El
        // formato en sí lo prueba HoraDeLaOrganizacionTests; lo que se afirma aquí es CUÁL instante
        // se cuenta.
        var zona = HoraDeLaOrganizacion.Zona(null);
        Assert.Contains(HoraDeLaOrganizacion.TextoConHuso(haceRato, zona), html);
        Assert.DoesNotContain(HoraDeLaOrganizacion.TextoConHuso(DateTime.UtcNow, zona), html);

        Assert.Contains("4321", texto);
        Assert.NotNull(MarcaDe(db, sesionId));
    }

    /// <summary>
    /// Un arranque demasiado viejo ya no se anuncia, tampoco por el camino inmediato.
    ///
    /// <para>Hace falta ahí tanto como en el barrido: una sesión que se quedó sin marca —porque el
    /// silencio la calló— sigue abierta, y al REANUDARLA por la tarde se publicaría «empezó a
    /// trabajar a las 10:00» a las cuatro. Es falso y además no le sirve a nadie.</para>
    /// </summary>
    [Fact]
    public async Task UnArranqueViejoNoSeAnunciaNiPorElCaminoInmediato()
    {
        using var db = Base();
        var (sesionId, _) = Escenario(
            db, inicio: DateTime.UtcNow - AvisoDeInicioEnDevOpsService.Ventana.Add(TimeSpan.FromMinutes(5)));
        var cliente = new DevOpsDeMentira();

        await Avisos(db, cliente).AvisarInicioAsync(sesionId);

        Assert.Empty(cliente.Publicados);
        Assert.Null(MarcaDe(db, sesionId));
    }

    // ── Que no se repita ─────────────────────────────────────────────────────────

    /// <summary>Llamarlo dos veces sobre la misma sesión publica UNA. La marca decide, no quien llama.</summary>
    [Fact]
    public async Task DosLlamadasSobreLaMismaSesion_publicanUnaSolaVez()
    {
        using var db = Base();
        var (sesionId, _) = Escenario(db);
        var cliente = new DevOpsDeMentira();
        var avisos = Avisos(db, cliente);

        await avisos.AvisarInicioAsync(sesionId);
        var segundo = await avisos.AvisarInicioAsync(sesionId);

        Assert.Single(cliente.Publicados);
        Assert.Equal("", segundo);
    }

    /// <summary>
    /// EL FRENO QUE «UNA VEZ POR SESIÓN» NO PUEDE DAR. Quien para a comer, para a la reunión y para
    /// por un café crea varias sesiones el mismo día sobre el mismo trabajo. Sin silencio, el ticket
    /// del cliente recibiría un «empezó a trabajar» por cada una.
    /// </summary>
    [Fact]
    public async Task VariasSesionesSeguidasSobreLoMismo_soloAvisanLaPrimera()
    {
        using var db = Base();
        var (primera, devId) = Escenario(db);
        var cliente = new DevOpsDeMentira();
        var avisos = Avisos(db, cliente);

        await avisos.AvisarInicioAsync(primera);
        Assert.Single(cliente.Publicados);

        // La misma persona detiene y vuelve a arrancar sobre la misma percha: sesión nueva.
        int perchaId = db.WorkSessions.AsNoTracking().Single(w => w.Id == primera).ActivityId!.Value;
        var otra = new WorkSession
        {
            DeveloperId = devId,
            ActivityId = perchaId,
            StartedAt = DateTime.UtcNow,
            LastResumedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Status = WorkSessionStatus.Activa
        };
        db.WorkSessions.Add(otra);
        await db.SaveChangesAsync();

        await avisos.AvisarInicioAsync(otra.Id);

        Assert.Single(cliente.Publicados);          // sigue habiendo uno solo
        Assert.Null(MarcaDe(db, otra.Id));          // y la segunda no se dio por avisada
    }

    /// <summary>
    /// Pero el silencio es de la PERSONA, no del ticket. Un requerimiento puede estar asignado a dos
    /// —quien desarrolla y quien prueba—, y que empiece la segunda es información nueva: callarla
    /// porque la primera empezó hace una hora es perder justo el aviso que aporta algo.
    /// </summary>
    [Fact]
    public async Task ElSilencioEsDeLaPersona_noDelTicket()
    {
        using var db = Base();
        var (deAna, _) = Escenario(db);
        var cliente = new DevOpsDeMentira();
        var avisos = Avisos(db, cliente);

        await avisos.AvisarInicioAsync(deAna);
        Assert.Single(cliente.Publicados);

        // Beto arranca sobre LA MISMA percha —que es suya también en este escenario de prueba—
        // dentro del silencio de Ana.
        int perchaId = db.WorkSessions.AsNoTracking().Single(w => w.Id == deAna).ActivityId!.Value;
        int beto = NuevoDev(db, "Beto Ruiz");
        db.DevActivities.Find(perchaId)!.DeveloperId = beto;
        await db.SaveChangesAsync();

        var suya = new WorkSession
        {
            DeveloperId = beto, ActivityId = perchaId,
            StartedAt = DateTime.UtcNow, LastResumedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            Status = WorkSessionStatus.Activa
        };
        db.WorkSessions.Add(suya);
        await db.SaveChangesAsync();

        await avisos.AvisarInicioAsync(suya.Id);

        Assert.Equal(2, cliente.Publicados.Count);
        Assert.Contains(cliente.Publicados, p => p.html.Contains("Beto"));
    }

    /// <summary>Pero pasado el silencio sí vuelve a avisar: al día siguiente es otro día de trabajo.</summary>
    [Fact]
    public async Task PasadoElSilencio_vuelveAAvisar()
    {
        using var db = Base();
        var (primera, devId) = Escenario(db);
        var cliente = new DevOpsDeMentira();
        var avisos = Avisos(db, cliente);

        await avisos.AvisarInicioAsync(primera);

        // Se envejece la marca de la primera más allá del silencio.
        db.ChangeTracker.Clear();
        var vieja = db.WorkSessions.Single(w => w.Id == primera);
        vieja.InicioComentadoEnUtc = DateTime.UtcNow - AvisoDeInicioEnDevOpsService.Silencio.Add(TimeSpan.FromMinutes(1));
        await db.SaveChangesAsync();

        int perchaId = vieja.ActivityId!.Value;
        var otra = new WorkSession
        {
            DeveloperId = devId, ActivityId = perchaId,
            StartedAt = DateTime.UtcNow, LastResumedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            Status = WorkSessionStatus.Activa
        };
        db.WorkSessions.Add(otra);
        await db.SaveChangesAsync();

        await avisos.AvisarInicioAsync(otra.Id);

        Assert.Equal(2, cliente.Publicados.Count);
    }

    // ── Que no se comente donde no toca ──────────────────────────────────────────

    /// <summary>Apagado no habla con DevOps, ni para preguntar. Es opt-in porque escribe fuera.</summary>
    [Fact]
    public async Task ApagadoNoPublicaNada()
    {
        using var db = Base(encendido: false);
        var (sesionId, _) = Escenario(db);
        var cliente = new DevOpsDeMentira();

        await Avisos(db, cliente).AvisarInicioAsync(sesionId);

        Assert.Empty(cliente.Publicados);
        Assert.Null(MarcaDe(db, sesionId));
    }

    [Fact]
    public async Task SinWorkItemNoHayNadaQueAvisar()
    {
        using var db = Base();
        int devId = NuevoDev(db);
        var libre = new DevActivity { DeveloperId = devId, Title = "Junta", Status = DevActivityStatus.Abierta };
        db.DevActivities.Add(libre);
        db.SaveChanges();

        var sesion = new WorkSession
        {
            DeveloperId = devId, ActivityId = libre.Id,
            StartedAt = DateTime.UtcNow, LastResumedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            Status = WorkSessionStatus.Activa
        };
        db.WorkSessions.Add(sesion);
        db.SaveChanges();

        var cliente = new DevOpsDeMentira();
        await Avisos(db, cliente).AvisarInicioAsync(sesion.Id);

        Assert.Empty(cliente.Publicados);
        Assert.Null(MarcaDe(db, sesion.Id));   // y no queda marcada: no es que ya se avisara
    }

    // ── Que un DevOps caído no cueste nada ───────────────────────────────────────

    /// <summary>
    /// Si DEVOPS CONTESTÓ QUE NO, la marca se suelta y se reintenta. Es el único caso en que se sabe
    /// con certeza que allá no se escribió nada, y por eso es el único en que reintentar es seguro.
    /// </summary>
    [Fact]
    public async Task SiDevOpsRechaza_laSesionQuedaListaParaReintentar()
    {
        using var db = Base();
        var (sesionId, _) = Escenario(db);
        var cliente = new DevOpsDeMentira
        {
            Fallo = new ErrorDeAzureDevOps("El work item #4321 no existe.")
        };

        var texto = await Avisos(db, cliente).AvisarInicioAsync(sesionId);

        Assert.Null(MarcaDe(db, sesionId));
        Assert.Contains("No se pudo avisar", texto);

        // Y el reintento, cuando DevOps vuelve, sí publica.
        cliente.Fallo = null;
        await Avisos(db, cliente).AvisarInicioAsync(sesionId);
        Assert.Single(cliente.Publicados);
    }

    /// <summary>
    /// PERO SI NO SE SABE CÓMO ACABÓ, la marca SE QUEDA.
    ///
    /// <para>Es la decisión menos obvia de todo esto y la que más costaría equivocar. Cuando se agota
    /// la paciencia —o se corta la red— el comentario puede estar publicado perfectamente: DevOps lo
    /// creó y tardó en contestar. Soltar la marca ahí hacía que el barrido lo publicara otra vez dos
    /// minutos después, y un comentario duplicado en el ticket de un cliente no se puede retirar.</para>
    ///
    /// <para>Así que se elige perder un aviso antes que duplicarlo, y queda dicho en la bitácora que
    /// no se sabe cómo acabó.</para>
    /// </summary>
    [Fact]
    public async Task SiNoSeSabeSiLlego_noSeVuelveAIntentar()
    {
        using var db = Base();
        var (sesionId, _) = Escenario(db);
        var cliente = new DevOpsDeMentira
        {
            Fallo = new TaskCanceledException("Se agotó el tiempo de espera.")
        };

        var texto = await Avisos(db, cliente).AvisarInicioAsync(sesionId);

        Assert.NotNull(MarcaDe(db, sesionId));               // la marca se queda
        Assert.Contains("No se sabe", texto);

        // Y el barrido no lo republica, que es justo lo que se estaba evitando.
        cliente.Fallo = null;
        await Avisos(db, cliente).AvisarIniciosPendientesAsync();
        Assert.Empty(cliente.Publicados);
    }

    /// <summary>Sin token de la instalación no se puede publicar, y tampoco se pierde el aviso.</summary>
    [Fact]
    public async Task SinCredencialesDeLaInstalacion_noSePierdeElAviso()
    {
        using var db = Base(conCredenciales: false);
        var (sesionId, _) = Escenario(db);
        var cliente = new DevOpsDeMentira();

        await Avisos(db, cliente).AvisarInicioAsync(sesionId);

        Assert.Empty(cliente.Publicados);
        Assert.Null(MarcaDe(db, sesionId));
    }

    /// <summary>
    /// Y todo lo anterior corriendo SIN SESIÓN, que es como lo llama el barrido. Si alguna consulta
    /// del camino llevara guarda, aquí lanzaría en vez de publicar.
    /// </summary>
    [Fact]
    public async Task FuncionaSinSesionHttp()
    {
        using var db = Base();
        var (sesionId, _) = Escenario(db);
        var cliente = new DevOpsDeMentira();

        await Avisos(db, cliente).AvisarInicioAsync(sesionId);

        Assert.Single(cliente.Publicados);
    }
    // ── El barrido, que es lo que cubre el cronómetro del ESCRITORIO ───────────

    /// <summary>
    /// EL CASO QUE JUSTIFICA EL BARRIDO. La aplicación de escritorio sigue en producción, comparte
    /// esta base y tiene su propio botón de arrancar el cronómetro: lo que se arranca allá no pasa
    /// por ningún endpoint nuestro. Aquí se simula tal cual —una sesión que aparece en la tabla sin
    /// que nadie haya llamado a nada— y el barrido la encuentra.
    /// </summary>
    [Fact]
    public async Task ElBarridoRecogeLoQueArrancoElEscritorio()
    {
        using var db = Base();
        var (sesionId, _) = Escenario(db, inicio: DateTime.UtcNow.AddMinutes(-5));
        var cliente = new DevOpsDeMentira();

        int publicados = await Avisos(db, cliente).AvisarIniciosPendientesAsync();

        Assert.Equal(1, publicados);
        Assert.Single(cliente.Publicados);
        Assert.NotNull(MarcaDe(db, sesionId));
    }

    /// <summary>
    /// Un arranque recién hecho NO se recoge todavía: puede que el aviso inmediato vaya camino de
    /// DevOps en este mismo instante, y el barrido no tiene por qué competir con él.
    /// </summary>
    [Fact]
    public async Task UnArranqueDeHaceUnSegundo_seLeDejaSuTurnoAlAvisoInmediato()
    {
        using var db = Base();
        Escenario(db, inicio: DateTime.UtcNow);
        var cliente = new DevOpsDeMentira();

        Assert.Equal(0, await Avisos(db, cliente).AvisarIniciosPendientesAsync());
        Assert.Empty(cliente.Publicados);
    }

    /// <summary>
    /// Y uno viejo tampoco. La ventana corta hace dos trabajos: un «empezó a las 09:12» publicado a
    /// las siete de la tarde no le sirve a nadie, y es lo que impide que una sesión cuyo work item se
    /// borró en DevOps se reintente para siempre.
    /// </summary>
    [Fact]
    public async Task UnArranqueViejoYaNoSeAvisa()
    {
        using var db = Base();
        Escenario(db, inicio: DateTime.UtcNow - AvisoDeInicioEnDevOpsService.Ventana.Add(TimeSpan.FromMinutes(10)));
        var cliente = new DevOpsDeMentira();

        Assert.Equal(0, await Avisos(db, cliente).AvisarIniciosPendientesAsync());
        Assert.Empty(cliente.Publicados);
    }

    /// <summary>
    /// LA AVALANCHA, por el otro lado. Las actividades libres —juntas, soporte— son la mayoría de
    /// las sesiones y no tienen ticket ninguno: si entraran en la consulta se llevarían el cupo de
    /// cada pasada y las que sí hay que avisar no llegarían nunca. Se descartan EN la consulta.
    /// </summary>
    [Fact]
    public async Task LasActividadesLibresNoSeComenElCupoDeLaPasada()
    {
        using var db = Base();
        int devId = NuevoDev(db);

        // Muchas más libres que el cupo de una pasada, y todas más nuevas que la que sí importa.
        for (int i = 0; i < AvisoDeInicioEnDevOpsService.MaximoPorPasada + 5; i++)
        {
            var libre = new DevActivity
            {
                DeveloperId = devId, Title = $"Junta {i}", Status = DevActivityStatus.Abierta
            };
            db.DevActivities.Add(libre);
            db.SaveChanges();

            db.WorkSessions.Add(new WorkSession
            {
                DeveloperId = devId, ActivityId = libre.Id,
                StartedAt = DateTime.UtcNow.AddMinutes(-2),
                LastResumedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
                Status = WorkSessionStatus.Activa
            });
        }
        db.SaveChanges();

        var (ligada, _) = Escenario(db, inicio: DateTime.UtcNow.AddMinutes(-30));
        var cliente = new DevOpsDeMentira();

        Assert.Equal(1, await Avisos(db, cliente).AvisarIniciosPendientesAsync());
        Assert.NotNull(MarcaDe(db, ligada));
    }

    /// <summary>
    /// Se atiende lo MÁS NUEVO primero. Al revés, un puñado de sesiones que fallan siempre —work
    /// items borrados en DevOps— serían las más viejas de cada pasada, se llevarían el cupo entero y
    /// las recién arrancadas no se publicarían nunca.
    /// </summary>
    [Fact]
    public async Task SeAtiendeLoMasNuevoPrimero()
    {
        using var db = Base();

        // Cada escenario trae su propio desarrollador y su propia percha, así que el silencio por
        // objetivo no interfiere: son objetivos distintos.
        for (int i = 0; i < AvisoDeInicioEnDevOpsService.MaximoPorPasada; i++)
            Escenario(db, workItem: 1000 + i, inicio: DateTime.UtcNow.AddMinutes(-90));

        var (reciente, _) = Escenario(db, workItem: 9999, inicio: DateTime.UtcNow.AddMinutes(-2));
        var cliente = new DevOpsDeMentira();

        await Avisos(db, cliente).AvisarIniciosPendientesAsync();

        Assert.Contains(cliente.Publicados, p => p.numero == 9999);
        Assert.NotNull(MarcaDe(db, reciente));
    }

    [Fact]
    public async Task ElBarridoRespetaElCupoDeLaPasada()
    {
        using var db = Base();
        for (int i = 0; i < AvisoDeInicioEnDevOpsService.MaximoPorPasada + 4; i++)
            Escenario(db, workItem: 2000 + i, inicio: DateTime.UtcNow.AddMinutes(-5));

        var cliente = new DevOpsDeMentira();

        Assert.Equal(AvisoDeInicioEnDevOpsService.MaximoPorPasada,
                     await Avisos(db, cliente).AvisarIniciosPendientesAsync());
    }

    /// <summary>Con la función apagada el barrido no consulta ni publica nada.</summary>
    [Fact]
    public async Task ElBarridoApagadoNoHaceNada()
    {
        using var db = Base(encendido: false);
        Escenario(db, inicio: DateTime.UtcNow.AddMinutes(-5));
        var cliente = new DevOpsDeMentira();

        Assert.Equal(0, await Avisos(db, cliente).AvisarIniciosPendientesAsync());
        Assert.Empty(cliente.Publicados);
    }

    /// <summary>
    /// El barrido cuenta lo PUBLICADO, no lo intentado. Es la única señal por la que alguien podría
    /// enterarse de que el token caducó: un registro que dijera «10 arranques publicados» la mañana
    /// en que no salió ninguno es peor que no tener registro.
    /// </summary>
    [Fact]
    public async Task ElBarridoNoCuentaComoPublicadoLoQueFallo()
    {
        using var db = Base();
        Escenario(db, workItem: 5001, inicio: DateTime.UtcNow.AddMinutes(-5));
        Escenario(db, workItem: 5002, inicio: DateTime.UtcNow.AddMinutes(-6));

        var cliente = new DevOpsDeMentira
        {
            Fallo = new ErrorDeAzureDevOps("El token no vale.")
        };

        Assert.Equal(0, await Avisos(db, cliente).AvisarIniciosPendientesAsync());
    }

    /// <summary>
    /// LA INANICIÓN. Una sesión que no puede resolver ticket —un requerimiento interno, uno sin
    /// asignar— sale antes de reservar, así que su marca se queda en nulo y vuelve a salir en cada
    /// pasada. Con diez de ésas más nuevas que un arranque bueno, el cupo se lo llevaban siempre
    /// ellas y el arranque bueno caducaba sin publicarse. Por eso el filtro de la consulta tiene que
    /// descartar exactamente lo mismo que descarta quien resuelve el ticket.
    /// </summary>
    [Fact]
    public async Task LosRequerimientosSinTicketNoMatanDeHambreALosBuenos()
    {
        using var db = Base();
        int dev = NuevoDev(db, "Quien trabaja en lo interno");

        // El arranque BUENO, más viejo que la multitud.
        var (bueno, _) = Escenario(db, workItem: 4321, inicio: DateTime.UtcNow.AddMinutes(-30));

        // Y una multitud de sesiones sobre requerimientos que nunca podrán avisar: internos, sin
        // origen de DevOps. Más nuevas que la buena, y más que el cupo de una pasada.
        for (int i = 0; i < AvisoDeInicioEnDevOpsService.MaximoPorPasada + 3; i++)
        {
            var req = new Requirement
            {
                Title = $"Interno {i}", Status = RequirementStatus.EnDesarrollo,
                CreatedAt = DateTime.UtcNow, Source = RequirementSource.Manual
            };
            db.Requirements.Add(req);
            db.SaveChanges();

            db.Assignments.Add(new Assignment { RequirementId = req.Id, DeveloperId = dev });
            db.WorkSessions.Add(new WorkSession
            {
                DeveloperId = dev, RequirementId = req.Id,
                StartedAt = DateTime.UtcNow.AddMinutes(-2),
                LastResumedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
                Status = WorkSessionStatus.Activa
            });
            db.SaveChanges();
        }

        var cliente = new DevOpsDeMentira();

        Assert.Equal(1, await Avisos(db, cliente).AvisarIniciosPendientesAsync());
        Assert.NotNull(MarcaDe(db, bueno));
    }

    /// <summary>
    /// LA RESERVA SE ESCRIBE ANTES DE PUBLICAR, y en eso descansa todo lo demás.
    ///
    /// <para>Es lo que hace imposible el comentario doble cuando el endpoint y el barrido caen sobre
    /// la misma sesión: quien llega segundo se encuentra la marca puesta y se retira. Si el orden se
    /// invirtiera —publicar y luego marcar— habría una ventana, del tamaño de una llamada de red, en
    /// la que los dos verían la marca vacía y los dos publicarían.</para>
    ///
    /// <para>Se comprueba mirando la base DESDE DENTRO de la publicación, que es el único momento en
    /// que la diferencia entre los dos órdenes es observable.</para>
    /// </summary>
    [Fact]
    public async Task LaMarcaYaEstaPuestaCuandoSeHablaConDevOps()
    {
        using var db = Base();
        var (sesionId, _) = Escenario(db);

        DateTime? marcaDurante = null;
        var cliente = new DevOpsDeMentira();
        // Se lee sin seguimiento: la reserva se escribe con una actualización directa que no pasa
        // por el rastreador, así que esto ve lo que hay en la base, no lo que el contexto recuerda.
        cliente.AlPublicar = async () =>
            marcaDurante = (await db.WorkSessions.AsNoTracking()
                .FirstAsync(w => w.Id == sesionId)).InicioComentadoEnUtc;

        await Avisos(db, cliente).AvisarInicioAsync(sesionId);

        Assert.Single(cliente.Publicados);
        Assert.NotNull(marcaDurante);
    }

    /// <summary>
    /// EL CRUCE. El endpoint y el barrido pueden caer sobre la misma sesión a la vez; la reserva
    /// condicional hace que solo uno publique. Aquí se ejerce en secuencia, que es lo que una prueba
    /// puede afirmar de verdad: el segundo se encuentra la marca puesta y se retira.
    /// </summary>
    [Fact]
    public async Task ElEndpointYElBarridoNoSePisan()
    {
        using var db = Base();
        var (sesionId, _) = Escenario(db, inicio: DateTime.UtcNow.AddMinutes(-5));
        var cliente = new DevOpsDeMentira();
        var avisos = Avisos(db, cliente);

        await avisos.AvisarInicioAsync(sesionId);          // el endpoint
        await avisos.AvisarIniciosPendientesAsync();       // el barrido, justo después

        Assert.Single(cliente.Publicados);
    }
}
