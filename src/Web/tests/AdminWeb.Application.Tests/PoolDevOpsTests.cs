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
/// EL VÍNCULO DEL POOL CON AZURE DEVOPS: ligar una actividad con un work item, mandar allá su
/// esfuerzo y su prioridad, y comentar en el ticket desde aquí.
///
/// <para><b>Lo que estas pruebas cuidan es una sola promesa, y es la que decide si esto sirve o
/// estorba:</b> que guardar una actividad del pool NUNCA dependa de que Azure DevOps conteste, y que
/// aun así nunca falle en silencio. Todo lo demás —la tabla de prioridades, el número que se saca de
/// un enlace, quién firma un comentario— existe para que eso sea cierto sin mentir sobre lo que hay
/// al otro lado.</para>
///
/// <para>Nada de esto toca la red: se ejercita contra <see cref="DevOpsDeMentira"/>, que además
/// apunta con qué token se firmó cada llamada, porque «los comentarios se firman con el PAT de quien
/// comenta» no se puede comprobar de ninguna otra forma.</para>
/// </summary>
public class PoolDevOpsTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var ctx in _contextos) ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Andamiaje ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un cliente de Azure DevOps de mentira, con un interruptor por OPERACIÓN.
    ///
    /// <para>Es propio y no el de las pruebas de <c>DevOpsService</c> por una razón concreta: aquí
    /// hace falta que la estimación entre y la prioridad NO, que es el empuje parcial y el caso que
    /// más importa. Con un único interruptor de «todo falla» ese escenario no se puede montar.</para>
    /// </summary>
    private sealed class DevOpsDeMentira : IClienteAzureDevOps
    {
        /// <summary>Con false, DevOps rechaza el campo Effort (hay plantillas donde no existe).
        /// Es un rechazo RÁPIDO y de ese campo: no significa que el servidor esté mudo.</summary>
        public bool AceptaEsfuerzo { get; set; } = true;

        /// <summary>Si se pone, escribir la prioridad lanza. Simula un rechazo o una caída a medias.</summary>
        public Exception? FalloDePrioridad { get; set; }

        /// <summary>Si se pone, escribir el esfuerzo lanza. Con un
        /// <see cref="ErrorDeAzureDevOps"/> equivale a «DevOps no contesta».</summary>
        public Exception? FalloDeEsfuerzo { get; set; }

        public Exception? FalloDeComentario { get; set; }

        public List<double> EsfuerzosEscritos { get; } = [];
        public List<int> PrioridadesEscritas { get; } = [];
        public List<string> ComentariosPublicados { get; } = [];
        public List<CredencialesDevOps> Llamadas { get; } = [];
        public List<ComentarioDevOps> Comentarios { get; set; } = [];

        public string? UltimoToken => Llamadas.Count == 0 ? null : Llamadas[^1].Pat;

        public Task<(bool escrito, string aviso)> EscribirEstimacionAsync(
            CredencialesDevOps credenciales, int numero, double horas, CancellationToken ct = default)
        {
            Llamadas.Add(credenciales);
            if (FalloDeEsfuerzo != null) throw FalloDeEsfuerzo;
            if (!AceptaEsfuerzo)
                return Task.FromResult((false, "Azure DevOps no aceptó el campo Effort (400): TF401320."));

            EsfuerzosEscritos.Add(horas);
            return Task.FromResult((true, ""));
        }

        public Task CambiarPrioridadAsync(
            CredencialesDevOps credenciales, int numero, int prioridad, CancellationToken ct = default)
        {
            Llamadas.Add(credenciales);
            if (FalloDePrioridad != null) throw FalloDePrioridad;
            PrioridadesEscritas.Add(prioridad);
            return Task.CompletedTask;
        }

        public Task PublicarComentarioAsync(
            CredencialesDevOps credenciales, int numero, string textoHtml, CancellationToken ct = default)
        {
            Llamadas.Add(credenciales);
            if (FalloDeComentario != null) throw FalloDeComentario;
            ComentariosPublicados.Add(textoHtml);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ComentarioDevOps>> ObtenerComentariosAsync(
            CredencialesDevOps credenciales, int numero, CancellationToken ct = default)
        {
            Llamadas.Add(credenciales);
            return Task.FromResult<IReadOnlyList<ComentarioDevOps>>(Comentarios);
        }

        // El resto no lo usa el pool: lanza en vez de contestar algo inventado, para que una prueba
        // que acabe aquí se entere en el momento en lugar de pasar en verde sin haber probado nada.
        private static T No<T>() => throw new InvalidOperationException(
            "El vínculo del pool con DevOps no usa esta operación.");

        public Task<IReadOnlyList<int>> ConsultarIdsAsync(CredencialesDevOps c, string? w, CancellationToken ct = default) => No<Task<IReadOnlyList<int>>>();
        public Task<IReadOnlyList<WorkItemDevOps>> ObtenerWorkItemsAsync(CredencialesDevOps c, IReadOnlyCollection<int> i, CancellationToken ct = default) => No<Task<IReadOnlyList<WorkItemDevOps>>>();
        public Task<string> SubirAdjuntoAsync(CredencialesDevOps c, byte[] b, string n, CancellationToken ct = default) => No<Task<string>>();
        public Task<(string nombre, string correo)> ReasignarAsync(CredencialesDevOps c, int n, string? correo, CancellationToken ct = default) => No<Task<(string, string)>>();
        public Task<string> CambiarEstadoAsync(CredencialesDevOps c, int n, string e, CancellationToken ct = default) => No<Task<string>>();
        public Task<bool> SumarTrabajoCompletadoAsync(CredencialesDevOps c, int n, double h, bool r, CancellationToken ct = default) => No<Task<bool>>();
        public Task<IReadOnlyList<BugHijoDevOps>> ObtenerBugsHijosAsync(CredencialesDevOps c, int n, CancellationToken ct = default) => No<Task<IReadOnlyList<BugHijoDevOps>>>();
        public Task<IReadOnlyList<CambioDeAsignacionDevOps>> ObtenerHistorialDeAsignacionAsync(CredencialesDevOps c, int n, CancellationToken ct = default) => No<Task<IReadOnlyList<CambioDeAsignacionDevOps>>>();
        public Task<(bool ok, string mensaje)> ProbarCredencialesAsync(CredencialesDevOps c, CancellationToken ct = default) => No<Task<(bool, string)>>();
    }

    /// <summary>Un protector que no cifra: lo que se prueba es de quién es el token, no el cifrado.</summary>
    private sealed class ProtectorDeMentirijillas : IProtectorDeSecretos
    {
        public string Proteger(string valorEnClaro) => "cifrado:" + valorEnClaro;
        public string? Desproteger(string cifrado) =>
            cifrado.StartsWith("cifrado:", StringComparison.Ordinal) ? cifrado["cifrado:".Length..] : null;
    }

    private const string PatDeLaInstalacion = "pat-compartido";
    private const string OrgUrl = "https://dev.azure.com/zorroDesierto";
    private const string Proyecto = "Webpro";

    private AppDbContext OtroContexto(AppDbContext db)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Database.GetConnectionString()).Options;
        var ctx = new AppDbContext(opts);
        _contextos.Add(ctx);
        return ctx;
    }

    private AuditService Bitacora(AppDbContext ctx, ICurrentUser cu) => new(ctx, cu, new OrigenDePrueba());

    private PoolDevOpsService Puente(AppDbContext db, ICurrentUser cu, DevOpsDeMentira cliente)
    {
        var ctx = OtroContexto(db);
        var bitacora = Bitacora(ctx, cu);
        return new PoolDevOpsService(ctx, cu, new SettingsService(ctx, cu, bitacora),
            new UserSecretsService(ctx, cu, new ProtectorDeMentirijillas(), bitacora), bitacora, cliente);
    }

    private PoolActivityService Pool(AppDbContext db, ICurrentUser cu, DevOpsDeMentira? cliente = null)
    {
        var ctx = OtroContexto(db);
        var bitacora = Bitacora(ctx, cu);
        return new PoolActivityService(ctx, cu, bitacora, new NotificationService(ctx),
            new SettingsService(ctx, cu, bitacora),
            cliente is null ? null : Puente(db, cu, cliente));
    }

    /// <summary>Base sembrada y con la integración ya configurada, salvo el token personal.</summary>
    private static async Task<AppDbContext> BaseListaAsync(bool conPatDeLaInstalacion = true)
    {
        var db = TestDb.New();
        await PoolSeed.SembrarAsync(db);

        db.AppSettings.Add(new AppSetting { Key = SettingsService.Claves.AzureDevOpsOrgUrl, Value = OrgUrl });
        db.AppSettings.Add(new AppSetting { Key = SettingsService.Claves.AzureDevOpsProject, Value = Proyecto });
        if (conPatDeLaInstalacion)
            db.AppSettings.Add(new AppSetting { Key = SettingsService.Claves.AzureDevOpsPat, Value = PatDeLaInstalacion });
        await db.SaveChangesAsync();
        return db;
    }

    private static UsuarioDePrueba Admin(int userId = 9) => UsuarioDePrueba.Como(UserRole.Admin, userId: userId);
    private static UsuarioDePrueba Dev(int developerId, int userId = 1) =>
        UsuarioDePrueba.Como(UserRole.Desarrollador, developerId, userId);

    private static int NuevoDesarrollador(AppDbContext db, string nombre = "Quien la toma")
    {
        var d = new Developer { FullName = nombre, IsActive = true };
        db.Developers.Add(d);
        db.SaveChanges();
        return d.Id;
    }

    /// <summary>
    /// La CUENTA de quien va a guardar un token. Hace falta de verdad: <c>UserSecrets</c> cuelga de
    /// <c>Users</c> en cascada —un secreto sin dueño no significa nada— y sin la fila la clave ajena
    /// rechaza el guardado.
    /// </summary>
    private static void CuentaDe(AppDbContext db, ICurrentUser quien)
    {
        int id = quien.UserId!.Value;
        if (db.Users.Any(u => u.Id == id)) return;

        db.Users.Add(new User
        {
            Id = id, Username = quien.Username ?? $"u{id}", FullName = quien.FullName ?? $"u{id}",
            PasswordHash = "x", IsActive = true, Role = quien.Role ?? UserRole.Desarrollador
        });
        db.SaveChanges();
    }

    /// <summary>Guarda el token PERSONAL de alguien, que es lo que hace posible comentar a su nombre.</summary>
    private async Task ConTokenPropioAsync(AppDbContext db, ICurrentUser quien, string pat)
    {
        CuentaDe(db, quien);
        var ctx = OtroContexto(db);
        var (ok, mensaje) = await new UserSecretsService(
                ctx, quien, new ProtectorDeMentirijillas(), Bitacora(ctx, quien))
            .GuardarMioAsync(PropositosDeSecreto.PatDevOps, pat);
        Assert.True(ok, mensaje);
    }

    private const decimal PlazoDelBug = 16m;
    private const decimal EsfuerzoDelLider = 6m;

    private static PoolActivity Borrador(
        PoolWorkType tipo = PoolWorkType.Tarea,
        PoolPriority prioridad = PoolPriority.Alta,
        int? workItem = null,
        string? enlace = null) => new()
        {
            Title            = "Corregir el cálculo de facturación",
            WorkType         = tipo,
            Complexity       = PoolComplexity.Alta,
            Priority         = prioridad,
            HorasLimite      = tipo == PoolWorkType.Bug ? PlazoDelBug : null,
            HorasEstimadas   = tipo == PoolWorkType.Bug ? null : EsfuerzoDelLider,
            DevOpsWorkItemId = workItem,
            ExternalUrl      = enlace
        };

    private static Task<PoolActivity?> LeerAsync(AppDbContext db, int id) =>
        db.PoolActivities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);

    // ── 1. La tabla de prioridades ───────────────────────────────────────────────

    /// <summary>
    /// La correspondencia es la que se acordó, y va escrita al derecho: en el pool lo más urgente es
    /// el valor MÁS ALTO y en DevOps el MÁS BAJO. Un <c>(int)</c> daría Baja→0 y Crítica→3, o sea la
    /// escala del revés y con un valor que DevOps ni siquiera admite.
    /// </summary>
    [Theory]
    [InlineData(PoolPriority.Critica, 1)]
    [InlineData(PoolPriority.Alta, 2)]
    [InlineData(PoolPriority.Media, 3)]
    [InlineData(PoolPriority.Baja, 4)]
    public void Prioridad_seTraduceSegunLaTabla(PoolPriority delPool, int enDevOps)
    {
        Assert.Equal(enDevOps, PrioridadDelPoolEnDevOps.ADevOps(delPool));
        Assert.Equal(delPool, PrioridadDelPoolEnDevOps.DesdeDevOps(enDevOps));
    }

    /// <summary>
    /// TODO valor declarado del enumerado tiene su correspondencia.
    ///
    /// <para>Es la prueba que justifica que la traducción sea una tabla y no un cast. El día que
    /// alguien añada un quinto escalón al pool, un cast seguiría compilando y publicaría un número
    /// inventado en tickets reales; aquí falla la construcción de la suite y hay que decidir a mano
    /// qué significa esa prioridad en DevOps, que es exactamente lo que se busca.</para>
    /// </summary>
    [Fact]
    public void Prioridad_ningunValorDelEnumeradoSeQuedaSinTraduccion()
    {
        foreach (PoolPriority valor in Enum.GetValues<PoolPriority>())
        {
            var enDevOps = PrioridadDelPoolEnDevOps.ADevOps(valor);
            Assert.InRange(enDevOps, 1, 4);
        }
    }

    // ── 2. Resolver el vínculo ───────────────────────────────────────────────────

    [Theory]
    [InlineData("https://dev.azure.com/zorroDesierto/Webpro/_workitems/edit/4321", 4321)]
    [InlineData("https://dev.azure.com/org/proj/_workitems/edit/17?fullScreen=true", 17)]
    [InlineData("https://tfs.local/tfs/col/proj/_workitems?id=908&triage=true", 908)]
    [InlineData("https://freshdesk.example.com/a/tickets/55", null)]
    [InlineData(null, null)]
    public void WorkItem_seSacaDeLaDireccionCuandoNoSeEscribe(string? enlace, int? esperado)
    {
        var (ok, error, numero) = PoolDevOpsService.ResolverWorkItem(null, enlace);
        Assert.True(ok, error);
        Assert.Equal(esperado, numero);
    }

    /// <summary>
    /// El número escrito y el del enlace se contradicen: se rechaza en vez de elegir uno.
    ///
    /// Elegir cualquiera de los dos escribiría el esfuerzo y la prioridad en un ticket ajeno, y eso
    /// no se puede deshacer desde aquí: en DevOps quedaría un número que nadie de ese equipo puso.
    /// </summary>
    [Fact]
    public void WorkItem_siElNumeroYElEnlaceNoCoinciden_seRechaza()
    {
        var (ok, error, numero) = PoolDevOpsService.ResolverWorkItem(
            1234, "https://dev.azure.com/org/proj/_workitems/edit/5678");

        Assert.False(ok);
        Assert.Null(numero);
        Assert.Contains("1234", error);
        Assert.Contains("5678", error);
    }

    [Fact]
    public void WorkItem_noPuedeSerCeroNiNegativo()
    {
        Assert.False(PoolDevOpsService.ResolverWorkItem(0, null).ok);
        Assert.False(PoolDevOpsService.ResolverWorkItem(-3, null).ok);
    }

    // ── 3. Empujar al publicar ───────────────────────────────────────────────────

    /// <summary>
    /// Publicar una actividad ligada manda el esfuerzo y la prioridad de una vez, que es lo que se
    /// pidió: que DevOps refleje lo que dice el pool desde el momento del vínculo.
    /// </summary>
    [Fact]
    public async Task Publicar_conWorkItem_mandaEsfuerzoYPrioridad()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();

        var (ok, mensaje, actividad) = await Pool(db, Admin(), cliente)
            .CrearAsync(Borrador(workItem: 4321, prioridad: PoolPriority.Critica));

        Assert.True(ok, mensaje);
        Assert.Equal(4321, actividad!.DevOpsWorkItemId);
        Assert.Equal([(double)EsfuerzoDelLider], cliente.EsfuerzosEscritos);
        Assert.Equal([1], cliente.PrioridadesEscritas);        // Crítica ↦ 1

        var guardada = await LeerAsync(db, actividad.Id);
        Assert.Equal(EsfuerzoDelLider, guardada!.DevOpsEsfuerzoEnviado);
        Assert.Equal(1, guardada.DevOpsPrioridadEnviada);
        Assert.False(guardada.PendienteDeEnviarADevOps);
        Assert.Null(guardada.DevOpsUltimoError);
    }

    /// <summary>El enlace pegado basta: se saca el número de él y se liga igual.</summary>
    [Fact]
    public async Task Publicar_conSoloElEnlace_ligaConElNumeroQueLlevaDentro()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();

        var (ok, mensaje, actividad) = await Pool(db, Admin(), cliente).CrearAsync(
            Borrador(enlace: $"{OrgUrl}/{Proyecto}/_workitems/edit/777"));

        Assert.True(ok, mensaje);
        Assert.Equal(777, actividad!.DevOpsWorkItemId);
        Assert.Single(cliente.PrioridadesEscritas);
    }

    /// <summary>Sin vínculo no se llama a DevOps ni una vez: el pool sigue siendo el pool.</summary>
    [Fact]
    public async Task Publicar_sinWorkItem_noHablaConDevOps()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();

        var (ok, mensaje, _) = await Pool(db, Admin(), cliente).CrearAsync(Borrador());

        Assert.True(ok, mensaje);
        Assert.Empty(cliente.Llamadas);
    }

    // ── 4. El esfuerzo del bug, que aparece al tomarlo ───────────────────────────

    /// <summary>
    /// En un bug el esfuerzo lo escribe quien lo toma, así que al publicarlo no hay nada que mandar
    /// —solo la prioridad— y es TOMARLO lo que lleva las horas a DevOps. Si el empuje solo ocurriera
    /// al publicar, la estimación de los bugs no llegaría nunca.
    /// </summary>
    [Fact]
    public async Task Bug_laEstimacionLlegaAlTomarlo_noAlPublicarlo()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        int devId = NuevoDesarrollador(db);

        var (ok, mensaje, actividad) = await Pool(db, Admin(), cliente)
            .CrearAsync(Borrador(PoolWorkType.Bug, PoolPriority.Media, workItem: 99));
        Assert.True(ok, mensaje);

        Assert.Empty(cliente.EsfuerzosEscritos);               // todavía nadie lo estimó
        Assert.Equal([3], cliente.PrioridadesEscritas);        // Media ↦ 3

        var (tomado, texto) = await Pool(db, Dev(devId), cliente)
            .TomarAsync(actividad!.Id, devId, horasEstimadas: 4.5m);
        Assert.True(tomado, texto);

        Assert.Equal([4.5d], cliente.EsfuerzosEscritos);
        Assert.Equal(4.5m, (await LeerAsync(db, actividad.Id))!.DevOpsEsfuerzoEnviado);
    }

    // ── 5. DevOps caído: lo local se guarda igual, y se dice ─────────────────────

    /// <summary>
    /// <b>La prueba central.</b> Con DevOps mudo, publicar TERMINA BIEN y la actividad queda
    /// guardada con sus puntos: el pool no depende de un servidor ajeno. Y no se calla: el mensaje
    /// lo dice, el motivo queda escrito en la fila y la actividad se queda pendiente de enviar.
    /// </summary>
    [Fact]
    public async Task DevOpsCaido_laActividadSeGuardaIgual_yQuedaConstancia()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira
        {
            FalloDeEsfuerzo = new ErrorDeAzureDevOps("No se pudo contactar con Azure DevOps: sin ruta al host.")
        };

        var (ok, mensaje, actividad) = await Pool(db, Admin(), cliente)
            .CrearAsync(Borrador(workItem: 4321));

        Assert.True(ok, mensaje);
        Assert.NotNull(actividad);

        var guardada = await LeerAsync(db, actividad!.Id);
        Assert.Equal(10, guardada!.Points);                     // la actividad quedó completa
        Assert.Null(guardada.DevOpsEsfuerzoEnviado);
        Assert.Null(guardada.DevOpsPrioridadEnviada);
        Assert.True(guardada.PendienteDeEnviarADevOps);
        Assert.NotNull(guardada.DevOpsEmpujadoEnUtc);
        Assert.Contains("sin ruta al host", guardada.DevOpsUltimoError);

        // Y quien publicó se entera en el mismo mensaje, sin tener que ir a buscarlo.
        Assert.Contains("4321", mensaje);
        Assert.Contains("pendiente de enviar", mensaje);
    }

    /// <summary>
    /// Con el servidor mudo, la prioridad NO se vuelve a intentar: sería esperar el mismo silencio
    /// otra vez y quien está guardando lo paga entero. Se dice que no se intentó, en vez de callarlo.
    /// </summary>
    [Fact]
    public async Task DevOpsMudo_noGastaUnSegundoIntentoEnLaPrioridad()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira
        {
            FalloDeEsfuerzo = new ErrorDeAzureDevOps("Azure DevOps tardó demasiado en contestar.")
        };

        var (_, mensaje, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));

        Assert.Empty(cliente.PrioridadesEscritas);
        Assert.Contains("no se llegó a intentar", (await LeerAsync(db, actividad!.Id))!.DevOpsUltimoError);
    }

    /// <summary>
    /// Sin ningún token configurado tampoco se rompe nada: se guarda, se explica y queda pendiente.
    /// Es el estado en el que arranca una instalación recién montada.
    /// </summary>
    [Fact]
    public async Task SinNingunToken_seGuardaIgualYSeExplica()
    {
        using var db = await BaseListaAsync(conPatDeLaInstalacion: false);
        var cliente = new DevOpsDeMentira();

        var (ok, mensaje, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 55));

        Assert.True(ok, mensaje);
        Assert.Empty(cliente.Llamadas);
        Assert.Contains("token", mensaje);
        Assert.True((await LeerAsync(db, actividad!.Id))!.PendienteDeEnviarADevOps);
    }

    // ── 6. El empuje PARCIAL ─────────────────────────────────────────────────────

    /// <summary>
    /// La estimación entra y la prioridad falla. Lo que llegó se da por llegado —y no se vuelve a
    /// mandar— y lo que no, sigue pendiente. El mensaje dice que quedó A MEDIAS: llamarlo «falló» a
    /// secas haría suponer que allá no hay nada y que hay que capturarlo todo otra vez.
    /// </summary>
    [Fact]
    public async Task EmpujeParcial_loQueLlegoSeDaPorLlegado_yElRestoSigueVivo()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira
        {
            FalloDePrioridad = new ErrorDeAzureDevOps("Azure DevOps rechazó cambiar la prioridad (403).")
        };

        var (ok, mensaje, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        Assert.True(ok, mensaje);
        Assert.Contains("A MEDIAS", mensaje);

        var guardada = await LeerAsync(db, actividad!.Id);
        Assert.Equal(EsfuerzoDelLider, guardada!.DevOpsEsfuerzoEnviado);
        Assert.False(guardada.EsfuerzoPendienteDeEnviar);
        Assert.True(guardada.PrioridadPendienteDeEnviar);

        // El reintento manda SOLO lo que falta: repetir la estimación la reescribiría en DevOps sin
        // motivo y, si allá alguien la hubiera ajustado, se la pisaría.
        cliente.FalloDePrioridad = null;
        var (reintentado, aviso) = await Puente(db, Admin(), cliente).ReintentarAsync(actividad.Id);

        Assert.True(reintentado, aviso);
        Assert.Single(cliente.EsfuerzosEscritos);
        Assert.Equal([2], cliente.PrioridadesEscritas);
        Assert.False((await LeerAsync(db, actividad.Id))!.PendienteDeEnviarADevOps);
    }

    /// <summary>
    /// Que DevOps no tenga el campo Effort en ese tipo de work item NO es que el servidor esté mudo:
    /// es un rechazo rápido y de ese campo. La prioridad sí se intenta, y entra.
    /// </summary>
    [Fact]
    public async Task SiNoExisteElCampoEffort_laPrioridadSeMandaIgual()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira { AceptaEsfuerzo = false };

        var (ok, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));

        Assert.True(ok);
        Assert.Equal([2], cliente.PrioridadesEscritas);

        var guardada = await LeerAsync(db, actividad!.Id);
        Assert.Equal(2, guardada!.DevOpsPrioridadEnviada);
        Assert.True(guardada.EsfuerzoPendienteDeEnviar);
        Assert.Contains("Effort", guardada.DevOpsUltimoError);
    }

    // ── 7. Cada cambio se vuelve a mandar ────────────────────────────────────────

    /// <summary>
    /// Editar la prioridad la vuelve a mandar. Mandar solo al ligar dejaría DevOps con el número del
    /// día que se publicó, que es peor que no mandar nada: parecería al día sin estarlo.
    /// </summary>
    [Fact]
    public async Task Editar_vuelveAMandarLoQueCambio()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();

        var (_, _, actividad) = await Pool(db, Admin(), cliente)
            .CrearAsync(Borrador(workItem: 4321, prioridad: PoolPriority.Baja));
        Assert.Equal([4], cliente.PrioridadesEscritas);

        var cambios = Borrador(workItem: 4321, prioridad: PoolPriority.Critica);
        cambios.HorasEstimadas = 9m;

        var (ok, mensaje) = await Pool(db, Admin(), cliente).EditarAsync(actividad!.Id, cambios);

        Assert.True(ok, mensaje);
        Assert.Equal([4, 1], cliente.PrioridadesEscritas);
        Assert.Equal([(double)EsfuerzoDelLider, 9d], cliente.EsfuerzosEscritos);
        Assert.False((await LeerAsync(db, actividad.Id))!.PendienteDeEnviarADevOps);
    }

    /// <summary>
    /// EDITAR SIN MANDAR EL NÚMERO NO DESLIGA.
    ///
    /// <para>Es el accidente que esta regla evita: el work item es un campo NUEVO de la petición, y
    /// cualquier pantalla que edite una actividad sin conocerlo manda un nulo. Si «ausente»
    /// significara «bórralo», cambiar el título de una actividad ligada la desligaría en silencio y
    /// el ticket dejaría de recibir nada sin que nadie lo hubiera pedido. Desligar es destructivo y
    /// tiene su propia ruta.</para>
    /// </summary>
    [Fact]
    public async Task Editar_sinMandarElWorkItem_noDesliga()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));

        // Una edición «de las de siempre»: cambia el título y no dice nada del vínculo. Y sin
        // enlace, para que el número no pueda re-deducirse de él.
        var cambios = Borrador();
        cambios.Title = "Otro título";

        var (ok, mensaje) = await Pool(db, Admin(), cliente).EditarAsync(actividad!.Id, cambios);

        Assert.True(ok, mensaje);
        var guardada = await LeerAsync(db, actividad.Id);
        Assert.Equal(4321, guardada!.DevOpsWorkItemId);
        Assert.Equal("Otro título", guardada.Title);
        Assert.False(guardada.PendienteDeEnviarADevOps);
    }

    /// <summary>
    /// Al APUNTAR A OTRO ticket, la marca de agua se borra. Conservarla haría que la actividad se
    /// creyera al día en un work item al que no se le ha mandado nada nunca.
    /// </summary>
    [Fact]
    public async Task CambiarDeTicket_olvidaLoQueSeLeHabiaMandadoAlAnterior()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 100));

        var cambios = Borrador(workItem: 200);
        var (ok, mensaje) = await Pool(db, Admin(), cliente).EditarAsync(actividad!.Id, cambios);

        Assert.True(ok, mensaje);
        var guardada = await LeerAsync(db, actividad.Id);
        Assert.Equal(200, guardada!.DevOpsWorkItemId);
        Assert.False(guardada.PendienteDeEnviarADevOps);          // se volvió a mandar, ahora al 200
        Assert.Equal(2, cliente.EsfuerzosEscritos.Count);         // una vez a cada ticket
    }

    // ── 8. Ligar y desligar ──────────────────────────────────────────────────────

    /// <summary>Se puede ligar después, sin volver a publicar, y empuja en el acto.</summary>
    [Fact]
    public async Task Ligar_despuesDePublicar_empujaEnElActo()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador());
        Assert.Empty(cliente.Llamadas);

        var (ok, mensaje) = await Puente(db, Admin(), cliente).LigarAsync(actividad!.Id, 4321, null);

        Assert.True(ok, mensaje);
        Assert.Equal([(double)EsfuerzoDelLider], cliente.EsfuerzosEscritos);

        // Y el enlace se rellena solo, para que quien mire la actividad pueda abrir el ticket.
        Assert.Equal($"{OrgUrl}/{Proyecto}/_workitems/edit/4321",
            (await LeerAsync(db, actividad.Id))!.ExternalUrl);
    }

    /// <summary>
    /// Desligar deja de mandar y borra la marca de agua. Lo que ya se escribió en DevOps se queda
    /// como está: borrarlo allá sería destruir información que quizá ya no es nuestra.
    /// </summary>
    [Fact]
    public async Task Desligar_dejaDeMandarYNoTocaLoQueYaEstaEnDevOps()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        int llamadasAlLigar = cliente.Llamadas.Count;

        var (ok, mensaje) = await Puente(db, Admin(), cliente).LigarAsync(actividad!.Id, null, null);

        Assert.True(ok, mensaje);
        Assert.Equal(llamadasAlLigar, cliente.Llamadas.Count);

        var guardada = await LeerAsync(db, actividad.Id);
        Assert.Null(guardada!.DevOpsWorkItemId);
        Assert.Null(guardada.DevOpsEsfuerzoEnviado);
        Assert.False(guardada.PendienteDeEnviarADevOps);
    }

    /// <summary>
    /// Dos actividades VIVAS sobre el mismo work item se pisarían el esfuerzo y la prioridad la una
    /// a la otra sin que ninguna se enterara —las dos se creerían al día—, así que la segunda se
    /// rechaza. En cuanto la primera se cierra, el mismo ticket vuelve a poder ligarse: un bug que
    /// se reabre es un caso normal y un índice único lo prohibiría para siempre.
    /// </summary>
    [Fact]
    public async Task DosActividadesVivasSobreElMismoTicket_seRechazan_perounaCerradaNoEstorba()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();

        var (_, _, primera) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));

        var (ok, error, _) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        Assert.False(ok);
        Assert.Contains("4321", error);
        Assert.Contains(primera!.Id.ToString(), error);

        var (retirada, mensaje) = await Pool(db, Admin(), cliente).RetirarAsync(primera.Id);
        Assert.True(retirada, mensaje);

        var (segunda, error2, _) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        Assert.True(segunda, error2);
    }

    // ── 9. Lo que quedó pendiente se puede consultar ─────────────────────────────

    /// <summary>
    /// El aviso lo vio una persona y cerró la pestaña. Sin una lista consultable, «el pool dice una
    /// cosa y DevOps otra» sería un hecho que nadie puede comprobar.
    /// </summary>
    [Fact]
    public async Task LoPendiente_sePuedeConsultarDespues()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira
        {
            FalloDeEsfuerzo = new ErrorDeAzureDevOps("Azure DevOps no contesta.")
        };

        var (_, _, fallida) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));

        cliente.FalloDeEsfuerzo = null;
        var (_, _, buena) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 8888));

        var pendientes = await Puente(db, Admin(), cliente).PendientesAsync();

        var fila = Assert.Single(pendientes.Pendientes);
        Assert.Equal(fallida!.Id, fila.PoolActivityId);
        Assert.Equal(4321, fila.WorkItem);
        Assert.True(fila.Pendiente);
        Assert.Contains("no contesta", fila.UltimoError);
        Assert.DoesNotContain(pendientes.Pendientes, p => p.PoolActivityId == buena!.Id);
    }

    // ── 10. Comentarios: los firma quien comenta ─────────────────────────────────

    /// <summary>
    /// <b>Sin token propio no se comenta</b>, y el mensaje lo explica: hoy la tabla de secretos está
    /// vacía, así que éste es el caso de TODO el mundo el primer día. Un «no autorizado» seco dejaría
    /// a la persona sin saber qué hacer, y caer al token de la instalación firmaría sus palabras con
    /// una cuenta compartida — que es justo lo que este vínculo existe para evitar.
    /// </summary>
    [Fact]
    public async Task Comentar_sinTokenPropio_seNiegaYDiceDondeCapturarlo()
    {
        using var db = await BaseListaAsync();       // hay PAT de la instalación, pero no personal
        var cliente = new DevOpsDeMentira();

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));

        var (ok, mensaje) = await Puente(db, Admin(), cliente).ComentarAsync(actividad!.Id, "Ya está el arreglo.");

        Assert.False(ok);
        Assert.Empty(cliente.ComentariosPublicados);
        Assert.Contains("Mi token de DevOps", mensaje);
        Assert.Contains("firmad", mensaje);
    }

    /// <summary>Con token propio se publica, y se firma CON EL SUYO, no con el de la instalación.</summary>
    [Fact]
    public async Task Comentar_conTokenPropio_seFirmaConEse()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        var admin = Admin();

        var (_, _, actividad) = await Pool(db, admin, cliente).CrearAsync(Borrador(workItem: 4321));

        await ConTokenPropioAsync(db, admin, "pat-de-la-jefa");

        var (ok, mensaje) = await Puente(db, admin, cliente)
            .ComentarAsync(actividad!.Id, "Ya está el arreglo <b>listo</b>.");

        Assert.True(ok, mensaje);
        Assert.Equal("pat-de-la-jefa", cliente.UltimoToken);

        // El texto de la persona va ESCAPADO: lo escribe alguien y acaba dentro de un documento HTML
        // que leen otros. La cabecera dice de dónde sale, porque quien lo lee en DevOps no tiene por
        // qué saber que existe un pool.
        var publicado = Assert.Single(cliente.ComentariosPublicados);
        Assert.Contains("&lt;b&gt;listo&lt;/b&gt;", publicado);
        Assert.Contains($"Actividad del pool #{actividad.Id}", publicado);
    }

    /// <summary>
    /// Quien no tiene la actividad tomada no comenta en su ticket. Comentar es hablar a nombre
    /// propio en un hilo ajeno, y la actividad es lo que da derecho a estar ahí.
    /// </summary>
    [Fact]
    public async Task Comentar_enUnaActividadAjena_seNiega()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        int devId = NuevoDesarrollador(db);
        int otroId = NuevoDesarrollador(db, "El otro");

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        var otro = Dev(otroId, userId: 77);
        await ConTokenPropioAsync(db, otro, "pat-del-otro");

        var (ok, mensaje) = await Puente(db, otro, cliente).ComentarAsync(actividad.Id, "Yo paso por aquí.");

        Assert.False(ok);
        Assert.Empty(cliente.ComentariosPublicados);
        Assert.Contains("no la tienes tomada", mensaje);
    }

    /// <summary>
    /// Comentar sube el contador local de comentarios del ticket sincronizado. No es cosmética: quien
    /// VIGILA ese ticket recibe un aviso cuando la sincronización encuentra más comentarios que los
    /// que constaban, y sin subirlo nuestro propio comentario le llegaría como «cambió algo que
    /// vigilas».
    /// </summary>
    [Fact]
    public async Task Comentar_subeElContadorDelTicketSincronizado()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        var admin = Admin();

        db.DevOpsTickets.Add(new DevOpsTicket { ExternalId = 4321, Title = "El ticket", CommentCount = 2 });
        await db.SaveChangesAsync();

        var (_, _, actividad) = await Pool(db, admin, cliente).CrearAsync(Borrador(workItem: 4321));

        await ConTokenPropioAsync(db, admin, "pat-de-la-jefa");

        var (ok, mensaje) = await Puente(db, admin, cliente).ComentarAsync(actividad!.Id, "Avanzando.");

        Assert.True(ok, mensaje);
        Assert.Equal(3, await db.DevOpsTickets.AsNoTracking()
            .Where(t => t.ExternalId == 4321).Select(t => t.CommentCount).FirstAsync());
    }

    /// <summary>Sin vínculo no hay ticket en el que comentar, y se dice qué hacer antes.</summary>
    [Fact]
    public async Task Comentar_enUnaActividadSinLigar_diceQueHayQueLigarlaPrimero()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador());
        var (ok, mensaje) = await Puente(db, Admin(), cliente).ComentarAsync(actividad!.Id, "Hola.");

        Assert.False(ok);
        Assert.Contains("Lígala primero", mensaje);
    }

    // ── 11. Un ticket sin sincronizar se puede ligar igual ───────────────────────

    /// <summary>
    /// Se liga por NÚMERO, no por clave ajena, así que un work item recién creado en DevOps —que
    /// aquí no existe todavía— se liga sin problema y se le escribe igual. Es el caso corriente:
    /// nadie sincroniza antes de publicar una actividad.
    /// </summary>
    [Fact]
    public async Task UnTicketSinSincronizar_seLigaYSeEscribeIgual()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();

        Assert.Empty(db.DevOpsTickets);

        var (ok, mensaje, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 60001));

        Assert.True(ok, mensaje);
        Assert.Single(cliente.EsfuerzosEscritos);

        var (_, _, vinculo) = await Puente(db, Admin(), cliente).VinculoAsync(actividad!.Id);
        Assert.Equal(60001, vinculo!.WorkItem);
        Assert.False(vinculo.SincronizadoAqui);
        Assert.Null(vinculo.TituloDelTicket);
    }

    /// <summary>
    /// Y si la limpieza de datos borra el ticket sincronizado, el vínculo NO se rompe: sigue siendo
    /// el número, que es de DevOps y no de aquí. Lo único que se pierde es poder enseñar su título.
    /// </summary>
    [Fact]
    public async Task SiSeBorraElTicketSincronizado_elVinculoSigueEnPie()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();

        db.DevOpsTickets.Add(new DevOpsTicket { ExternalId = 4321, Title = "El ticket", State = "Active" });
        await db.SaveChangesAsync();

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));

        db.DevOpsTickets.RemoveRange(db.DevOpsTickets);
        await db.SaveChangesAsync();

        var (ok, mensaje, vinculo) = await Puente(db, Admin(), cliente).VinculoAsync(actividad!.Id);

        Assert.True(ok, mensaje);
        Assert.Equal(4321, vinculo!.WorkItem);
        Assert.False(vinculo.SincronizadoAqui);

        // Y se le puede seguir escribiendo, que es para lo que existe el vínculo.
        var cambios = Borrador(workItem: 4321, prioridad: PoolPriority.Critica);
        Assert.True((await Pool(db, Admin(), cliente).EditarAsync(actividad.Id, cambios)).ok);
        Assert.Contains(1, cliente.PrioridadesEscritas);
    }

    // ── 12. El esquema ───────────────────────────────────────────────────────────

    /// <summary>
    /// EL MIGRADOR CREA LAS COLUMNAS NUEVAS EN UNA BASE QUE NO LAS TIENE.
    ///
    /// <para>Esta prueba existe porque el fallo que caza no lo detecta ninguna otra: en una base de
    /// PRUEBA las columnas las crea <c>EnsureCreated</c> a partir del modelo, así que una columna sin
    /// su parche pasa toda la suite en verde y solo revienta contra la base real, donde
    /// <c>EnsureCreated</c> no altera nada. Se simula una base vieja quitándole las columnas y se
    /// exige que el migrador las devuelva, y que no falle ni una sentencia.</para>
    /// </summary>
    [Fact]
    public void ElMigrador_devuelveLasColumnasDelVinculoAUnaBaseQueNoLasTiene()
    {
        string[] nuevas =
        [
            "DevOpsWorkItemId", "DevOpsEsfuerzoEnviado", "DevOpsPrioridadEnviada",
            "DevOpsEmpujadoEnUtc", "DevOpsUltimoError"
        ];

        using var db = TestDb.New();

        // El índice que declara el modelo se quita primero: SQLite no deja tirar una columna que un
        // índice todavía menciona. El migrador vuelve a crear el suyo, que es parte de lo que se
        // comprueba aquí.
        db.Database.ExecuteSqlRaw(@"DROP INDEX IF EXISTS ""IX_PoolActivities_DevOpsWorkItemId""");

        // La sentencia se compone en una variable y no se interpola en la llamada: el nombre sale del
        // arreglo literal de arriba y no de fuera, y armarla aparte lo deja dicho —además de evitar
        // que el analizador de EF avise de una inyección que aquí no puede existir—.
        foreach (var columna in nuevas)
        {
            var soltarLaColumna = @"ALTER TABLE ""PoolActivities"" DROP COLUMN """ + columna + @"""";
            db.Database.ExecuteSqlRaw(soltarLaColumna);
        }

        foreach (var columna in nuevas)
            Assert.False(TieneColumna(db, columna),
                $"«{columna}» debería haberse podido quitar: si no, esta prueba no estaría probando " +
                "que el migrador la crea.");

        var fallidas = DatabaseMigrator.EnsureUpToDate(db);

        Assert.True(fallidas.Count == 0,
            "El migrador dejó sentencias sin aplicar: " + string.Join(" | ", fallidas));

        foreach (var columna in nuevas)
            Assert.True(TieneColumna(db, columna),
                $"El migrador no creó «{columna}» en PoolActivities. Sin el parche, la columna solo " +
                "existe en las bases que crea EnsureCreated desde el modelo — o sea, en las de prueba.");

        // Y es IDEMPOTENTE: la API arranca en cada despliegue y en cada instancia, así que la
        // segunda pasada tiene que ser tan silenciosa como la primera.
        Assert.Empty(DatabaseMigrator.EnsureUpToDate(db));
    }

    private static bool TieneColumna(AppDbContext db, string columna)
    {
        var conexion = db.Database.GetDbConnection();
        bool abrir = conexion.State != System.Data.ConnectionState.Open;
        if (abrir) conexion.Open();
        try
        {
            using var cmd = conexion.CreateCommand();
            cmd.CommandText = "PRAGMA table_info('PoolActivities')";
            using var lector = cmd.ExecuteReader();
            while (lector.Read())
                if (string.Equals(lector.GetString(1), columna, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        finally { if (abrir) conexion.Close(); }
    }
}
