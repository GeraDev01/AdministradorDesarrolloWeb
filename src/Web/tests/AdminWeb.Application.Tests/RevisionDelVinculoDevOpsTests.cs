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
/// Lo que la revisión del vínculo del pool con Azure DevOps encontró y lo que arregla, más los
/// caminos que las pruebas de la primera hornada no recorrían.
///
/// <para><b>Por qué son un archivo aparte y no más casos dentro de los de allá:</b> aquí cada
/// operación se monta como una PETICIÓN de verdad —un <c>AppDbContext</c> nuevo que comparten los
/// dos servicios, que es exactamente como los registra <c>Program.cs</c>: ámbito por petición—, y
/// aquellas pruebas le dan a cada servicio el suyo. La diferencia importa: el empuje escribe la
/// marca de agua con una actualización DIRECTA que no pasa por el seguimiento de entidades, así que
/// lo que se lea después dentro de la MISMA petición tiene que contar lo que de verdad pasó y no la
/// copia de antes. Con un contexto por servicio ese riesgo no existe y por tanto no se prueba.</para>
/// </summary>
public class RevisionDelVinculoDevOpsTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var ctx in _contextos) ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Andamiaje ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un cliente de DevOps de mentira, propio de este archivo. Anota lo que se le escribió y con qué
    /// token, y sabe tardar: hace falta para comprobar que un servidor que no contesta NO cuelga el
    /// guardado más allá de la paciencia declarada.
    /// </summary>
    private sealed class DevOpsQueSePuedeGobernar : IClienteAzureDevOps
    {
        /// <summary>Lo que tarda cada llamada. Con algo mayor que la paciencia, equivale a un servidor
        /// que se quedó mudo.</summary>
        public TimeSpan Tardanza { get; set; } = TimeSpan.Zero;

        public List<double> EsfuerzosEscritos { get; } = [];
        public List<int> PrioridadesEscritas { get; } = [];
        public List<string> ComentariosPublicados { get; } = [];
        public List<string> TokensUsados { get; } = [];
        public List<ComentarioDevOps> Comentarios { get; set; } = [];

        private async Task EsperarAsync(CredencialesDevOps credenciales, CancellationToken ct)
        {
            TokensUsados.Add(credenciales.Pat);
            if (Tardanza > TimeSpan.Zero) await Task.Delay(Tardanza, ct);
        }

        public async Task<(bool escrito, string aviso)> EscribirEstimacionAsync(
            CredencialesDevOps credenciales, int numero, double horas, CancellationToken ct = default)
        {
            await EsperarAsync(credenciales, ct);
            EsfuerzosEscritos.Add(horas);
            return (true, "");
        }

        public async Task CambiarPrioridadAsync(
            CredencialesDevOps credenciales, int numero, int prioridad, CancellationToken ct = default)
        {
            await EsperarAsync(credenciales, ct);
            PrioridadesEscritas.Add(prioridad);
        }

        public async Task PublicarComentarioAsync(
            CredencialesDevOps credenciales, int numero, string textoHtml, CancellationToken ct = default)
        {
            await EsperarAsync(credenciales, ct);
            ComentariosPublicados.Add(textoHtml);
        }

        public async Task<IReadOnlyList<ComentarioDevOps>> ObtenerComentariosAsync(
            CredencialesDevOps credenciales, int numero, CancellationToken ct = default)
        {
            await EsperarAsync(credenciales, ct);
            return Comentarios;
        }

        private static T No<T>() => throw new InvalidOperationException(
            "El vínculo del pool con DevOps no usa esta operación.");

        public Task<IReadOnlyList<int>> ConsultarIdsAsync(CredencialesDevOps c, string? w, CancellationToken ct = default) => No<Task<IReadOnlyList<int>>>();
        public Task<IReadOnlyList<WorkItemDevOps>> ObtenerWorkItemsAsync(CredencialesDevOps c, IReadOnlyCollection<int> i, CancellationToken ct = default) => No<Task<IReadOnlyList<WorkItemDevOps>>>();
        public Task<string> SubirAdjuntoAsync(CredencialesDevOps c, byte[] b, string n, CancellationToken ct = default) => No<Task<string>>();
        public Task<(string nombre, string correo)> ReasignarAsync(CredencialesDevOps c, int n, string? correo, CancellationToken ct = default) => No<Task<(string, string)>>();
        public Task<string> CambiarEstadoAsync(CredencialesDevOps c, int n, string e, CancellationToken ct = default) => No<Task<string>>();
        public Task<ColumnaDeTablero> CambiarColumnaAsync(CredencialesDevOps c, int n, string col, bool m, CancellationToken ct = default) => No<Task<ColumnaDeTablero>>();
        public Task<ColumnaDeTablero> LeerColumnaAsync(CredencialesDevOps c, int n, CancellationToken ct = default) => No<Task<ColumnaDeTablero>>();
        public Task<bool> SumarTrabajoCompletadoAsync(CredencialesDevOps c, int n, double h, bool r, CancellationToken ct = default) => No<Task<bool>>();
        public Task<IReadOnlyList<BugHijoDevOps>> ObtenerBugsHijosAsync(CredencialesDevOps c, int n, CancellationToken ct = default) => No<Task<IReadOnlyList<BugHijoDevOps>>>();
        public Task<IReadOnlyList<CambioDeAsignacionDevOps>> ObtenerHistorialDeAsignacionAsync(CredencialesDevOps c, int n, CancellationToken ct = default) => No<Task<IReadOnlyList<CambioDeAsignacionDevOps>>>();
        public Task<(bool ok, string mensaje)> ProbarCredencialesAsync(CredencialesDevOps c, CancellationToken ct = default) => No<Task<(bool, string)>>();
    }

    private sealed class ProtectorLlano : IProtectorDeSecretos
    {
        public string Proteger(string valorEnClaro) => "claro:" + valorEnClaro;
        public string? Desproteger(string cifrado) =>
            cifrado.StartsWith("claro:", StringComparison.Ordinal) ? cifrado["claro:".Length..] : null;
    }

    private const string PatDeLaInstalacion = "pat-de-la-instalacion";
    private const string OrgUrl = "https://dev.azure.com/zorroDesierto";
    private const string Proyecto = "Webpro";

    private AppDbContext Registrar(AppDbContext ctx)
    {
        _contextos.Add(ctx);
        return ctx;
    }

    /// <summary>
    /// UNA PETICIÓN, con el cableado tal cual lo arma <c>Program.cs</c>: un contexto nuevo —distinto
    /// del de la petición anterior, porque el ámbito muere con ella— y los dos servicios colgados de
    /// ÉSE, que es lo que de verdad comparten mientras dura.
    ///
    /// <para>Llamarla dos veces en una prueba es hacer dos peticiones. Reutilizar el resultado para
    /// dos operaciones que en la aplicación son dos clics distintos probaría algo que no pasa.</para>
    /// </summary>
    private (PoolActivityService pool, PoolDevOpsService puente) Peticion(
        AppDbContext db, ICurrentUser quien, DevOpsQueSePuedeGobernar cliente)
    {
        var ctx = Registrar(new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Database.GetConnectionString()).Options));

        var bitacora = new AuditService(ctx, quien, new OrigenDePrueba());
        var ajustes = new SettingsService(ctx, quien, bitacora);
        var secretos = new UserSecretsService(ctx, quien, new ProtectorLlano(), bitacora);

        var puente = new PoolDevOpsService(ctx, quien, ajustes, secretos, bitacora, cliente);
        var pool = new PoolActivityService(ctx, quien, bitacora, new NotificationService(ctx), ajustes, puente);
        return (pool, puente);
    }

    private async Task<AppDbContext> BaseListaAsync()
    {
        var db = Registrar(TestDb.New());
        await PoolSeed.SembrarAsync(db);

        db.AppSettings.Add(new AppSetting { Key = SettingsService.Claves.AzureDevOpsOrgUrl, Value = OrgUrl });
        db.AppSettings.Add(new AppSetting { Key = SettingsService.Claves.AzureDevOpsProject, Value = Proyecto });
        db.AppSettings.Add(new AppSetting { Key = SettingsService.Claves.AzureDevOpsPat, Value = PatDeLaInstalacion });
        await db.SaveChangesAsync();
        return db;
    }

    private static UsuarioDePrueba Admin(int userId = 9) => UsuarioDePrueba.Como(UserRole.Admin, userId: userId);
    private static UsuarioDePrueba Dev(int developerId, int userId = 1) =>
        UsuarioDePrueba.Como(UserRole.Desarrollador, developerId, userId);

    private static int NuevoDesarrollador(AppDbContext db, string nombre)
    {
        var d = new Developer { FullName = nombre, IsActive = true };
        db.Developers.Add(d);
        db.SaveChanges();
        return d.Id;
    }

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

    private static async Task ConTokenPropioAsync(AppDbContext db, ICurrentUser quien, string pat)
    {
        CuentaDe(db, quien);
        var bitacora = new AuditService(db, quien, new OrigenDePrueba());
        var (ok, mensaje) = await new UserSecretsService(db, quien, new ProtectorLlano(), bitacora)
            .GuardarMioAsync(PropositosDeSecreto.PatDevOps, pat);
        Assert.True(ok, mensaje);
    }

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
            HorasLimite      = tipo == PoolWorkType.Bug ? 16m : null,
            HorasEstimadas   = tipo == PoolWorkType.Bug ? null : 6m,
            DevOpsWorkItemId = workItem,
            ExternalUrl      = enlace
        };

    private static Task<PoolActivity?> LeerAsync(AppDbContext db, int id) =>
        db.PoolActivities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);

    // ── 1. Ligar con una dirección que no lleva work item ────────────────────────

    /// <summary>
    /// UNA DIRECCIÓN QUE NO LLEVA WORK ITEM DENTRO NO DESLIGA.
    ///
    /// <para>Es el defecto que encontró esta revisión. La ruta de ligar entiende «sin número y sin
    /// enlace» como DESLIGAR, y eso está bien; el problema era que una dirección de la que no se
    /// puede sacar ningún número —un ticket de Freshdesk, el correo del cliente, una URL de DevOps
    /// que no apunta a un work item— caía en la misma rama. Quien pegaba esa dirección y pulsaba
    /// «Ligar» conseguía exactamente lo contrario de lo que pidió: la actividad se DESLIGABA del
    /// ticket con el que estaba, y a partir de ahí dejaba de mandarle el esfuerzo y la prioridad.</para>
    ///
    /// <para>Desligar es destructivo y no puede ser el resultado por omisión de no haber entendido lo
    /// que se escribió.</para>
    /// </summary>
    [Fact]
    public async Task Ligar_conUnaDireccionSinWorkItem_noDesliga_yLoExplica()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsQueSePuedeGobernar();

        var (creada, mensajeDeAlta, actividad) = await Peticion(db, Admin(), cliente).pool
            .CrearAsync(Borrador(workItem: 4821));
        Assert.True(creada, mensajeDeAlta);

        var (ok, mensaje) = await Peticion(db, Admin(), cliente).puente.LigarAsync(
            actividad!.Id, null, "https://soporte.example.com/a/tickets/993");

        Assert.False(ok, "Una dirección que no lleva work item no es una orden de desligar: " +
                         "quien la pegó estaba ligando.");
        Assert.Contains("work item", mensaje, StringComparison.OrdinalIgnoreCase);

        var enBase = await LeerAsync(db, actividad.Id);
        Assert.Equal(4821, enBase!.DevOpsWorkItemId);
    }

    /// <summary>Y desligar de verdad —sin número y sin dirección— sigue funcionando: es la salida
    /// cuando el ticket resultó no ser el que era.</summary>
    [Fact]
    public async Task Desligar_sinNumeroNiDireccion_sigueDesligando()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsQueSePuedeGobernar();

        var (_, _, actividad) = await Peticion(db, Admin(), cliente).pool
            .CrearAsync(Borrador(workItem: 4821));

        var (ok, mensaje) = await Peticion(db, Admin(), cliente).puente
            .LigarAsync(actividad!.Id, null, null);

        Assert.True(ok, mensaje);
        Assert.Contains("4821", mensaje);

        var enBase = await LeerAsync(db, actividad.Id);
        Assert.Null(enBase!.DevOpsWorkItemId);
        Assert.Null(enBase.DevOpsEsfuerzoEnviado);
        Assert.Null(enBase.DevOpsPrioridadEnviada);
    }

    /// <summary>Una dirección en blanco —espacios— es «no mandé nada», no «no te entendí».</summary>
    [Fact]
    public async Task Desligar_conUnaDireccionEnBlanco_sigueDesligando()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsQueSePuedeGobernar();

        var (_, _, actividad) = await Peticion(db, Admin(), cliente).pool
            .CrearAsync(Borrador(workItem: 4821));

        var (ok, _) = await Peticion(db, Admin(), cliente).puente
            .LigarAsync(actividad!.Id, null, "   ");

        Assert.True(ok);
        Assert.Null((await LeerAsync(db, actividad.Id))!.DevOpsWorkItemId);
    }

    // ── 2. El cableado de producción: un solo contexto ───────────────────────────

    /// <summary>
    /// DENTRO DE UNA MISMA PETICIÓN, LO QUE SE EMPUJÓ NO SE LEE COMO PENDIENTE.
    ///
    /// <para>El empuje escribe la marca de agua con una actualización DIRECTA contra la base, que no
    /// pasa por el seguimiento de entidades; y publicar deja además la actividad RASTREADA en el
    /// contexto de esa petición. Si la ficha del vínculo o la lista de lo pendiente se resolvieran
    /// con la copia rastreada, dirían que falta por mandar algo que ya está allá — y el líder vería
    /// un aviso permanente sobre actividades que están al día, que es la forma más rápida de que se
    /// deje de mirar el aviso.</para>
    /// </summary>
    [Fact]
    public async Task EnLaMismaPeticion_loQueSeEmpujoNoSeLeeComoPendiente()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsQueSePuedeGobernar();
        var (pool, puente) = Peticion(db, Admin(), cliente);

        var (ok, mensaje, actividad) = await pool.CrearAsync(
            Borrador(prioridad: PoolPriority.Critica, workItem: 4821));
        Assert.True(ok, mensaje);

        Assert.Equal([6d], cliente.EsfuerzosEscritos);
        Assert.Equal([1], cliente.PrioridadesEscritas);

        var (leido, motivo, vinculo) = await puente.VinculoAsync(actividad!.Id);
        Assert.True(leido, motivo);
        Assert.False(vinculo!.Pendiente,
            "El empuje entró entero: quedar «pendiente» sería contar la copia de antes de mandarlo.");
        Assert.Equal(6m, vinculo.EsfuerzoEnviado);
        Assert.Equal(1, vinculo.PrioridadEnviada);

        Assert.Empty((await puente.PendientesAsync()).Pendientes);
    }

    // ── 3. La paciencia acota lo que cuesta guardar ──────────────────────────────

    /// <summary>
    /// UN DEVOPS QUE NO CONTESTA NO CUELGA EL GUARDADO NI PIERDE LO LOCAL.
    ///
    /// <para>Es la promesa de fondo de todo esto. Con el cliente tardando más que la paciencia
    /// declarada, publicar tiene que terminar —con su aviso— dentro de ese plazo y con la actividad
    /// ya en la base. Se comprueba contra la paciencia del servicio y no contra un número escrito
    /// aquí, para que subirla algún día no deje esta prueba mintiendo.</para>
    /// </summary>
    [Fact]
    public async Task DevOpsQueNoContesta_noCuelgaElGuardado_yDejaLaActividadEnLaBase()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsQueSePuedeGobernar
        {
            Tardanza = PoolDevOpsService.Paciencia + TimeSpan.FromSeconds(30)
        };
        var (pool, _) = Peticion(db, Admin(), cliente);

        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var (ok, mensaje, actividad) = await pool.CrearAsync(Borrador(workItem: 4821));
        reloj.Stop();

        Assert.True(ok, mensaje);
        Assert.NotNull(await LeerAsync(db, actividad!.Id));

        Assert.True(reloj.Elapsed < PoolDevOpsService.Paciencia + TimeSpan.FromSeconds(10),
            $"Publicar tardó {reloj.Elapsed.TotalSeconds:0} s: el empuje tiene que cortarse a los " +
            $"{PoolDevOpsService.Paciencia.TotalSeconds:0} s pase lo que pase al otro lado.");

        Assert.Contains("pendiente", mensaje, StringComparison.OrdinalIgnoreCase);

        // Y queda constancia consultable, no solo el mensaje que alguien vio una vez.
        var enBase = await LeerAsync(db, actividad.Id);
        Assert.True(enBase!.PendienteDeEnviarADevOps);
        Assert.False(string.IsNullOrWhiteSpace(enBase.DevOpsUltimoError));
        Assert.NotNull(enBase.DevOpsEmpujadoEnUtc);
    }

    // ── 4. El reintento ──────────────────────────────────────────────────────────

    /// <summary>
    /// EL REINTENTO MANDA SOLO LO QUE FALTA Y NO PIDE RECAPTURAR NADA.
    ///
    /// <para>Es el camino que sigue quien vio el aviso: DevOps estaba caído al publicar y vuelve a
    /// estar en pie un rato después. No lo cubría ninguna prueba.</para>
    /// </summary>
    [Fact]
    public async Task Reintentar_mandaLoQueFalta_yDejaDeEstarPendiente()
    {
        using var db = await BaseListaAsync();
        // Se publica con DevOps mudo: el empuje se corta y la actividad queda pendiente.
        var cliente = new DevOpsQueSePuedeGobernar
        {
            Tardanza = PoolDevOpsService.Paciencia + TimeSpan.FromSeconds(30)
        };
        var (_, _, actividad) = await Peticion(db, Admin(), cliente).pool
            .CrearAsync(Borrador(workItem: 4821));
        Assert.True((await LeerAsync(db, actividad!.Id))!.PendienteDeEnviarADevOps);

        // DevOps vuelve, y quien vio el aviso pulsa «Reintentar»: otra petición.
        cliente.Tardanza = TimeSpan.Zero;
        var (ok, mensaje) = await Peticion(db, Admin(), cliente).puente.ReintentarAsync(actividad.Id);

        Assert.True(ok, mensaje);
        Assert.Equal([6d], cliente.EsfuerzosEscritos);
        Assert.Equal([2], cliente.PrioridadesEscritas);

        var enBase = await LeerAsync(db, actividad.Id);
        Assert.False(enBase!.PendienteDeEnviarADevOps);
        Assert.Null(enBase.DevOpsUltimoError);
    }

    /// <summary>El reintento de una actividad ajena se rechaza: quien no la tiene tomada no decide
    /// qué se escribe en su ticket.</summary>
    [Fact]
    public async Task Reintentar_unaActividadAjena_seNiega()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsQueSePuedeGobernar();

        var (_, _, actividad) = await Peticion(db, Admin(), cliente).pool
            .CrearAsync(Borrador(workItem: 4821));

        int ajeno = NuevoDesarrollador(db, "Quien pasaba por ahí");
        var (ok, mensaje) = await Peticion(db, Dev(ajeno, userId: 4), cliente).puente
            .ReintentarAsync(actividad!.Id);

        Assert.False(ok);
        Assert.Contains("no es tuya", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    // ── 5. El comentario, firmado y escapado ─────────────────────────────────────

    /// <summary>
    /// EL COMENTARIO VA CON EL TOKEN DE QUIEN LO ESCRIBE Y CON SU TEXTO ESCAPADO.
    ///
    /// <para>Las dos cosas en la misma prueba porque las dos hablan de lo mismo: lo que se publica en
    /// un ticket que lee gente de fuera. El token, para que el historial diga quién dijo qué; el
    /// escapado, porque el cuerpo del comentario se manda como HTML y el texto lo escribe una
    /// persona.</para>
    /// </summary>
    [Fact]
    public async Task Comentar_vaConElTokenPropio_yConElTextoEscapado()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsQueSePuedeGobernar();

        int desarrollador = NuevoDesarrollador(db, "Quien la toma");
        var quien = Dev(desarrollador, userId: 3);

        var (_, _, actividad) = await Peticion(db, Admin(), cliente).pool
            .CrearAsync(Borrador(workItem: 4821));

        await ConTokenPropioAsync(db, quien, "pat-de-quien-comenta");
        Assert.True((await Peticion(db, quien, cliente).pool
            .TomarAsync(actividad!.Id, desarrollador)).ok);

        cliente.TokensUsados.Clear();
        var (ok, mensaje) = await Peticion(db, quien, cliente).puente.ComentarAsync(
            actividad.Id, "Ya quedó <script>alert(1)</script> & listo");

        Assert.True(ok, mensaje);
        Assert.Equal(["pat-de-quien-comenta"], cliente.TokensUsados);

        var publicado = Assert.Single(cliente.ComentariosPublicados);
        Assert.DoesNotContain("<script>", publicado, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", publicado, StringComparison.Ordinal);
        Assert.Contains("&amp;", publicado, StringComparison.Ordinal);
    }

    // ── 6. Quién puede LEER el hilo del ticket ───────────────────────────────────

    /// <summary>
    /// EL HILO DE UN TICKET AJENO NO SE LEE DESDE UNA ACTIVIDAD QUE NO SE TRABAJA.
    ///
    /// <para>Es la segunda cosa que encontró esta revisión. Leer los comentarios sale a DevOps con el
    /// token de la INSTALACIÓN cuando quien pide no tiene el suyo, así que sin esta comprobación
    /// cualquiera con sesión de desarrollador podía leer la conversación de cualquier work item
    /// ligado —incluida la de tickets a los que su propia cuenta de DevOps no tiene acceso— usando el
    /// token compartido como puerta trasera. La pantalla de tickets ya exigía que el ticket estuviera
    /// a su nombre para enseñar sus comentarios; aquí faltaba la misma regla.</para>
    ///
    /// <para>La regla es la misma que para comentar: el líder sobre cualquiera, y quien la tenga
    /// tomada sobre la suya. No recorta nada de lo que las pantallas enseñan —la tarjeta del vínculo
    /// vive dentro de «mi actividad» y dentro de la pantalla del líder— y cierra el hueco.</para>
    /// </summary>
    [Fact]
    public async Task Hilo_deUnaActividadAjena_noSeLee()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsQueSePuedeGobernar
        {
            Comentarios = [new ComentarioDevOps("<p>Algo confidencial</p>", "Alguien", DateTime.UtcNow)]
        };

        int dueño = NuevoDesarrollador(db, "Quien la tiene");
        var (_, _, actividad) = await Peticion(db, Admin(), cliente).pool
            .CrearAsync(Borrador(workItem: 4821));

        Assert.True((await Peticion(db, Dev(dueño, userId: 3), cliente).pool
            .TomarAsync(actividad!.Id, dueño)).ok);

        int curioso = NuevoDesarrollador(db, "Quien pasaba por ahí");
        var (ok, mensaje, hilo) = await Peticion(db, Dev(curioso, userId: 4), cliente).puente
            .HiloAsync(actividad.Id);

        Assert.False(ok, "Leer el hilo de un ticket ajeno con el token compartido es una puerta trasera.");
        Assert.Null(hilo);
        Assert.Contains("no la tienes tomada", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Y quien SÍ la trabaja lo lee, ya sin marcado: los comentarios de DevOps son HTML y los
    /// escribe gente de fuera del equipo.</summary>
    [Fact]
    public async Task Hilo_deLaPropia_seLee_yLlegaSinMarcado()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsQueSePuedeGobernar
        {
            Comentarios = [new ComentarioDevOps(
                "<p>Falta el <b>reporte</b> &amp; la firma</p>", "Alguien de fuera", DateTime.UtcNow)]
        };

        int dueño = NuevoDesarrollador(db, "Quien la tiene");
        var (_, _, actividad) = await Peticion(db, Admin(), cliente).pool
            .CrearAsync(Borrador(workItem: 4821));

        var quien = Dev(dueño, userId: 3);
        Assert.True((await Peticion(db, quien, cliente).pool.TomarAsync(actividad!.Id, dueño)).ok);

        var (ok, mensaje, hilo) = await Peticion(db, quien, cliente).puente.HiloAsync(actividad.Id);

        Assert.True(ok, mensaje);
        var comentario = Assert.Single(hilo!.Comentarios);
        Assert.Equal("Falta el reporte & la firma", comentario.Texto);

        // Sin token propio no se puede escribir, y el motivo lo dice el servidor.
        Assert.False(hilo.PuedoComentar);
        Assert.Contains("token", hilo.PorQueNoPuedoComentar ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>El líder lee el hilo de cualquiera: verificar lo entregado es su trabajo.</summary>
    [Fact]
    public async Task Hilo_elLiderLoLeeDeCualquiera()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsQueSePuedeGobernar
        {
            Comentarios = [new ComentarioDevOps("Ya está", "Alguien", DateTime.UtcNow)]
        };

        int dueño = NuevoDesarrollador(db, "Quien la tiene");
        var (_, _, actividad) = await Peticion(db, Admin(), cliente).pool
            .CrearAsync(Borrador(workItem: 4821));

        Assert.True((await Peticion(db, Dev(dueño, userId: 3), cliente).pool
            .TomarAsync(actividad!.Id, dueño)).ok);

        var (ok, mensaje, hilo) = await Peticion(db, Admin(), cliente).puente.HiloAsync(actividad.Id);

        Assert.True(ok, mensaje);
        Assert.Single(hilo!.Comentarios);
    }
}
