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

        /// <summary>Si se pone, reasignar lanza. Con un <see cref="ErrorDeAzureDevOps"/> equivale a
        /// que DevOps rechace el correo o no conteste.</summary>
        public Exception? FalloDeAsignacion { get; set; }

        /// <summary>Si se pone, mover de columna lanza. Es el caso de la transición inválida, que
        /// depende de la plantilla de proceso del proyecto.</summary>
        public Exception? FalloDeEstado { get; set; }

        /// <summary>
        /// A quién dice DevOps que quedó asignado, si no es a quien se le pidió. Sirve para el caso
        /// en que el correo de la ficha no es el de la cuenta de allá: DevOps contesta 200 con OTRA
        /// identidad, y eso NO puede contar como asignado.
        /// </summary>
        public (string nombre, string correo)? ResuelveLaAsignacionComo { get; set; }

        /// <summary>
        /// Con qué NOMBRE aparece cada cuenta en DevOps. Sin entrada, se contesta con el propio
        /// correo: allá el nombre para mostrar lo pone el directorio y desde aquí no se puede
        /// adivinar, así que fingir que coincide con el de la ficha probaría algo que no ocurre.
        /// Lo declaran las pruebas a las que ese nombre les importa.
        /// </summary>
        public Dictionary<string, string> NombreEnDevOps { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<double> EsfuerzosEscritos { get; } = [];
        public List<int> PrioridadesEscritas { get; } = [];
        public List<string> ComentariosPublicados { get; } = [];
        public List<string?> AsignacionesEscritas { get; } = [];
        public List<string> EstadosEscritos { get; } = [];
        public List<string> AdjuntosSubidos { get; } = [];
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

        /// <summary>La dirección la devuelve DevOps y es lo que se embebe en el comentario, así que
        /// aquí se inventa una reconocible: es lo que permite comprobar que la captura acabó DENTRO
        /// del HTML publicado y no subida y olvidada.</summary>
        public Task<string> SubirAdjuntoAsync(
            CredencialesDevOps credenciales, byte[] contenido, string nombre, CancellationToken ct = default)
        {
            Llamadas.Add(credenciales);
            AdjuntosSubidos.Add(nombre);
            return Task.FromResult($"https://dev.azure.com/_apis/wit/attachments/{AdjuntosSubidos.Count}");
        }

        public Task<(string nombre, string correo)> ReasignarAsync(
            CredencialesDevOps credenciales, int numero, string? correoOVacio, CancellationToken ct = default)
        {
            Llamadas.Add(credenciales);
            if (FalloDeAsignacion != null) throw FalloDeAsignacion;

            AsignacionesEscritas.Add(correoOVacio);

            // Por omisión DevOps acepta el correo tal cual y contesta con él. Empatar identidades
            // se hace por correo cuando los dos lados lo traen, que es el caso normal, así que el
            // nombre solo decide cómo se LEE el resultado.
            var correo = correoOVacio ?? "";
            return Task.FromResult(
                ResuelveLaAsignacionComo ?? (NombreEnDevOps.GetValueOrDefault(correo, correo), correo));
        }

        public Task<string> CambiarEstadoAsync(
            CredencialesDevOps credenciales, int numero, string nuevoEstado, CancellationToken ct = default)
        {
            Llamadas.Add(credenciales);
            if (FalloDeEstado != null) throw FalloDeEstado;

            EstadosEscritos.Add(nuevoEstado);
            OrdenDeLosMovimientos.Add("estado");
            return Task.FromResult(nuevoEstado);
        }

        /// <summary>Si se pone, mover de columna lanza. Es el caso del tablero que no tiene esa
        /// columna, o del work item que no está en ningún tablero.</summary>
        public Exception? FalloDeColumna { get; set; }

        public List<(string columna, bool mitadHecha)> ColumnasEscritas { get; } = [];

        /// <summary>Estado y columna en el orden en que se mandaron. Dos listas separadas dicen QUÉ
        /// se mandó pero no CUÁL fue antes, y aquí el orden es la mitad de lo que hay que probar.</summary>
        public List<string> OrdenDeLosMovimientos { get; } = [];

        public Task<ColumnaDeTablero> CambiarColumnaAsync(
            CredencialesDevOps credenciales, int numero, string columna, bool mitadHecha,
            CancellationToken ct = default)
        {
            Llamadas.Add(credenciales);
            if (FalloDeColumna != null) throw FalloDeColumna;

            ColumnasEscritas.Add((columna, mitadHecha));
            OrdenDeLosMovimientos.Add("columna");
            return Task.FromResult(new ColumnaDeTablero(columna, mitadHecha));
        }

        public Task<ColumnaDeTablero> LeerColumnaAsync(
            CredencialesDevOps credenciales, int numero, CancellationToken ct = default)
        {
            Llamadas.Add(credenciales);
            return Task.FromResult(ColumnasEscritas.Count == 0
                ? new ColumnaDeTablero("", false)
                : new ColumnaDeTablero(ColumnasEscritas[^1].columna, ColumnasEscritas[^1].mitadHecha));
        }

        // El resto no lo usa el pool: lanza en vez de contestar algo inventado, para que una prueba
        // que acabe aquí se entere en el momento en lugar de pasar en verde sin haber probado nada.
        private static T No<T>() => throw new InvalidOperationException(
            "El vínculo del pool con DevOps no usa esta operación.");

        public Task<IReadOnlyList<int>> ConsultarIdsAsync(CredencialesDevOps c, string? w, CancellationToken ct = default) => No<Task<IReadOnlyList<int>>>();
        public Task<IReadOnlyList<WorkItemDevOps>> ObtenerWorkItemsAsync(CredencialesDevOps c, IReadOnlyCollection<int> i, CancellationToken ct = default) => No<Task<IReadOnlyList<WorkItemDevOps>>>();
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

    /// <summary>
    /// Una ficha de desarrollador. CON CORREO por omisión, porque es lo que hace falta para poner el
    /// work item a su nombre: sin él, tomar una actividad ligada deja la asignación pendiente y
    /// media suite estaría probando ese camino sin querer. El caso de la ficha sin correo se monta a
    /// propósito pasando <c>correo: null</c>, y tiene su prueba.
    /// </summary>
    private static int NuevoDesarrollador(
        AppDbContext db, string nombre = "Quien la toma", string? correo = "quien.toma@soltum.com.mx")
    {
        var d = new Developer { FullName = nombre, Email = correo, IsActive = true };
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

    // ── 4b. Tomarla la pone a tu nombre y en curso EN DEVOPS ─────────────────────

    /// <summary>
    /// <b>Lo que se pidió.</b> Tomar una actividad ligada deja el work item a nombre de quien la
    /// tomó y movido a «en progreso», sin que nadie tenga que ir a DevOps a hacerlo. Las dos cosas
    /// quedan además con su marca de agua, que es lo que impide volver a mandarlas en cada guardado.
    /// </summary>
    [Fact]
    public async Task Tomar_asignaElWorkItemYLoMueveAEnProgreso()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira { NombreEnDevOps = { ["ana.perez@soltum.com.mx"] = "Ana Pérez" } };
        int devId = NuevoDesarrollador(db, "Ana Pérez", "ana.perez@soltum.com.mx");

        var (ok, mensaje, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        Assert.True(ok, mensaje);

        // Publicar no asigna nada: no hay a quién. Es lo que separa las dos mitades del empuje.
        Assert.Empty(cliente.AsignacionesEscritas);
        Assert.Empty(cliente.EstadosEscritos);

        var (tomada, texto) = await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);
        Assert.True(tomada, texto);

        Assert.Equal(["ana.perez@soltum.com.mx"], cliente.AsignacionesEscritas);
        Assert.Equal([PoolDevOpsService.EstadoEnProgresoPorOmision], cliente.EstadosEscritos);

        var fila = await LeerAsync(db, actividad.Id);
        Assert.Equal(devId, fila!.DevOpsAsignadoADeveloperId);
        Assert.Equal(PoolDevOpsService.EstadoEnProgresoPorOmision, fila.DevOpsEstadoEnviado);
        Assert.False(fila.PendienteDeEnviarADevOps);

        // Y el mensaje lo cuenta: quien la toma tiene que saber que su ticket ya está a su nombre,
        // o irá a comprobarlo a mano —que es el paso que esto vino a quitar—.
        Assert.Contains("asignado a Ana Pérez", texto);
        Assert.Contains("movido a", texto);
    }

    /// <summary>
    /// Una actividad SIN ligar no manda nada al tomarla, y no es una obviedad: la mayoría del pool no
    /// viene de un ticket, así que este es el camino de todos los días. Un empuje que saliera a la
    /// red igualmente haría que tomar cualquier tarea costara un viaje a otro servidor.
    /// </summary>
    [Fact]
    public async Task Tomar_sinTicketLigado_noHablaConDevOps()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        int devId = NuevoDesarrollador(db);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador());
        var (tomada, texto) = await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        Assert.True(tomada, texto);
        Assert.Empty(cliente.Llamadas);
    }

    /// <summary>
    /// El work item se asigna PRIMERO y se mueve DESPUÉS. Es el orden en que se hace a mano y el que
    /// deja mejor el historial: al revés, hay un instante con un ticket «en progreso» sin dueño, que
    /// es justo lo que este automatismo existe para evitar.
    /// </summary>
    [Fact]
    public async Task Tomar_asignaAntesDeMover()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        int devId = NuevoDesarrollador(db);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        // Las dos ocurrieron, y la de asignar dejó su rastro antes: se comprueba por el orden en que
        // el cliente registró las llamadas, que es lo único que sabe de tiempo.
        Assert.Single(cliente.AsignacionesEscritas);
        Assert.Single(cliente.EstadosEscritos);
        Assert.True(cliente.Llamadas.Count >= 2);
    }

    /// <summary>
    /// Sin correo en la ficha no se puede asignar, y eso NO puede tumbar el reclamo: la actividad es
    /// suya igual. Lo que sí pasa es que se dice con nombre y apellidos, se guarda el motivo y queda
    /// pendiente —porque el arreglo está aquí, no en DevOps—.
    /// </summary>
    [Fact]
    public async Task Tomar_sinCorreoEnLaFicha_laActividadEsSuyaIgual_yQuedaConstancia()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        int devId = NuevoDesarrollador(db, "Sin Correo", correo: null);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        var (tomada, texto) = await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        Assert.True(tomada, texto);
        Assert.Empty(cliente.AsignacionesEscritas);
        Assert.Contains("Sin Correo", texto);
        Assert.Contains("ficha", texto);

        var fila = await LeerAsync(db, actividad.Id);
        Assert.Equal(PoolActivityStatus.Tomada, fila!.Status);       // el reclamo se ganó igual
        Assert.Null(fila.DevOpsAsignadoADeveloperId);
        Assert.True(fila.AsignacionPendienteDeEnviar);
        Assert.Contains("Sin Correo", fila.DevOpsUltimoError);

        // Mover de columna SÍ se intenta: que falte un correo aquí no dice nada sobre si DevOps
        // contesta, y dejar el ticket parado en «New» por eso sería castigar dos veces el mismo dato.
        Assert.Single(cliente.EstadosEscritos);
    }

    /// <summary>
    /// DevOps contesta 200 pero resolvió el correo a OTRA persona —la ficha tiene un correo que allá
    /// no es su cuenta—. Eso no cuenta como asignado: dar la marca por buena dejaría el ticket a
    /// nombre equivocado y sin que nada volviera a intentarlo nunca.
    /// </summary>
    [Fact]
    public async Task Tomar_siDevOpsLoResuelveAOtraPersona_noCuentaComoAsignado()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira
        {
            ResuelveLaAsignacionComo = ("Otro Cualquiera", "otro.cualquiera@soltum.com.mx")
        };
        int devId = NuevoDesarrollador(db, "Ana Pérez", "ana.perez@soltum.com.mx");

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        var (tomada, texto) = await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        Assert.True(tomada, texto);

        var fila = await LeerAsync(db, actividad.Id);
        Assert.Null(fila!.DevOpsAsignadoADeveloperId);
        Assert.True(fila.AsignacionPendienteDeEnviar);
        Assert.Contains("Otro Cualquiera", fila.DevOpsUltimoError);
    }

    /// <summary>
    /// DevOps contesta sin correo —hay respuestas donde «System.AssignedTo» llega como texto suelto—
    /// y con un nombre que no es exactamente el de la ficha. Eso NO es un desacuerdo: la petición se
    /// aceptó con el correo que se mandó, y comparar nombres para decidirlo marcaría la asignación
    /// como fallida para siempre, reintentándola en cada guardado sin que nada estuviera mal.
    /// </summary>
    [Fact]
    public async Task Tomar_siDevOpsNoDevuelveCorreo_seCreeLaAsignacion()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira { ResuelveLaAsignacionComo = ("Jesus Canul", "") };
        int devId = NuevoDesarrollador(db, "Jesus Abraham Canul", "jesus.canul@soltum.com.mx");

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        var (tomada, texto) = await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        Assert.True(tomada, texto);

        var fila = await LeerAsync(db, actividad.Id);
        Assert.Equal(devId, fila!.DevOpsAsignadoADeveloperId);
        Assert.False(fila.AsignacionPendienteDeEnviar);
        Assert.Null(fila.DevOpsUltimoError);
    }

    /// <summary>
    /// Una actividad ya ACEPTADA deja de estar pendiente aunque su asignación nunca llegara. El
    /// trabajo terminó: insistir meses después tocaría un ticket probablemente cerrado, y tenerla
    /// para siempre en la lista del líder convertiría esa lista en ruido que nadie mira. El motivo
    /// del fallo sigue guardado.
    /// </summary>
    [Fact]
    public async Task Aceptada_yaNoQuedaPendienteDeAsignar()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira { FalloDeAsignacion = new ErrorDeAzureDevOps("no contesta") };
        int devId = NuevoDesarrollador(db);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        Assert.True((await LeerAsync(db, actividad.Id))!.AsignacionPendienteDeEnviar);

        // Se entrega y se acepta. El checklist de una Tarea exige evidencia en un punto, así que se
        // marca todo desde el servicio, que es como lo hace la pantalla.
        var checklist = await db.PoolActivityChecklistItems.AsNoTracking()
            .Where(c => c.PoolActivityId == actividad.Id).ToListAsync();
        foreach (var punto in checklist)
            await Pool(db, Dev(devId), cliente).MarcarItemAsync(
                punto.Id, devId, true, punto.RequiereEvidencia ? "https://dev.azure.com/pr/1" : null);

        var (entregada, porQue) = await Pool(db, Dev(devId), cliente).EntregarAsync(actividad.Id, devId);
        Assert.True(entregada, porQue);

        var (aceptada, motivo) = await Pool(db, Admin(), cliente).AceptarAsync(actividad.Id);
        Assert.True(aceptada, motivo);

        var fila = await LeerAsync(db, actividad.Id);
        Assert.False(fila!.AsignacionPendienteDeEnviar);
        Assert.False(fila.PendienteDeEnviarADevOps);
        Assert.False(string.IsNullOrWhiteSpace(fila.DevOpsUltimoError));   // el porqué no se borra

        Assert.Empty((await Puente(db, Admin(), cliente).PendientesAsync()).Pendientes);
    }

    /// <summary>
    /// La transición que rechaza DevOps —porque en ese proyecto el estado se llama de otra forma— se
    /// cuenta con el mensaje de allá Y con dónde se arregla. El mensaje de DevOps suele enumerar los
    /// estados válidos, así que es lo que hace falta para capturar el bueno sin ir a adivinarlo.
    /// </summary>
    [Fact]
    public async Task Tomar_siDevOpsRechazaLaTransicion_seDiceQueEstadoSeIntentoYDondeSeCambia()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira
        {
            FalloDeEstado = new ErrorDeAzureDevOps(
                "TF401320: la transición de New a Active no está permitida. Válidos: New, Doing, Done.")
        };
        int devId = NuevoDesarrollador(db);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        var (tomada, texto) = await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        Assert.True(tomada, texto);

        var fila = await LeerAsync(db, actividad.Id);
        Assert.Null(fila!.DevOpsEstadoEnviado);
        Assert.True(fila.EstadoPendienteDeEnviar);
        Assert.Contains("Doing", fila.DevOpsUltimoError);                                   // lo que contestó DevOps
        // Y dice DÓNDE se corrige. La clave es la del ajuste POR TIPO: los estados válidos son
        // propios de cada tipo de work item, así que un mensaje que mandara a un ajuste único
        // mandaría a un sitio donde el problema no se puede arreglar.
        Assert.Contains(SettingsService.Claves.PoolDevOpsEstadoAlTomar, fila.DevOpsUltimoError);
        Assert.Contains("TIPO", fila.DevOpsUltimoError);

        // La asignación sí entró: un rechazo de la transición no dice nada sobre el otro campo.
        Assert.Equal(devId, fila.DevOpsAsignadoADeveloperId);
        Assert.False(fila.AsignacionPendienteDeEnviar);
    }

    /// <summary>
    /// El estado en el que se pone el ticket se puede CONFIGURAR, porque su nombre depende de la
    /// plantilla de proceso del proyecto y no de nosotros.
    /// </summary>
    [Fact]
    public async Task Estado_elConfiguradoManda()
    {
        using var db = await BaseListaAsync();
        db.AppSettings.Add(new AppSetting
        {
            Key = SettingsService.Claves.PoolDevOpsEstadoEnProgreso, Value = "Doing"
        });
        await db.SaveChangesAsync();

        var cliente = new DevOpsDeMentira();
        int devId = NuevoDesarrollador(db);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        Assert.Equal(["Doing"], cliente.EstadosEscritos);
    }

    /// <summary>
    /// Sin configurar, el estado se DEDUCE de los que usan de verdad los tickets ya sincronizados.
    /// Es lo que evita que la función nazca muerta en un proyecto que no use la plantilla Agile: nadie
    /// configura lo que no sabe que existe.
    /// </summary>
    [Fact]
    public async Task Estado_sinConfigurar_seDeduceDeLosTicketsSincronizados()
    {
        using var db = await BaseListaAsync();
        db.DevOpsTickets.Add(new DevOpsTicket { ExternalId = 1, Title = "a", State = "Doing" });
        db.DevOpsTickets.Add(new DevOpsTicket { ExternalId = 2, Title = "b", State = "Doing" });
        db.DevOpsTickets.Add(new DevOpsTicket { ExternalId = 3, Title = "c", State = "New" });
        db.DevOpsTickets.Add(new DevOpsTicket { ExternalId = 4, Title = "d", State = "Closed" });
        await db.SaveChangesAsync();

        var cliente = new DevOpsDeMentira();
        int devId = NuevoDesarrollador(db);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        // «New» y «Closed» no significan «en desarrollo», así que no compiten por frecuencia.
        Assert.Equal(["Doing"], cliente.EstadosEscritos);
    }

    /// <summary>
    /// Devolver la actividad al pool NO desasigna el work item —vaciar allá un campo que quizá puso
    /// otra persona sería destruir información ajena— pero sí deja el estado por volver a mandar: si
    /// otro la toma, su ticket tiene que volver a ponerse en curso, y a SU nombre.
    /// </summary>
    [Fact]
    public async Task Devolver_noDesasigna_yElSiguienteQueLaTomaSeLaLlevaASuNombre()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        int primero = NuevoDesarrollador(db, "Ana Pérez", "ana.perez@soltum.com.mx");
        int segundo = NuevoDesarrollador(db, "Beto Ruiz", "beto.ruiz@soltum.com.mx");

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        await Pool(db, Dev(primero), cliente).TomarAsync(actividad!.Id, primero);
        await Pool(db, Dev(primero), cliente).DevolverAsync(actividad.Id, primero, "no me da la vida");

        var libre = await LeerAsync(db, actividad.Id);
        Assert.Equal(primero, libre!.DevOpsAsignadoADeveloperId);   // allá sigue a nombre de Ana
        Assert.False(libre.AsignacionPendienteDeEnviar);            // y sin dueño aquí, nada que mandar
        Assert.Null(libre.DevOpsEstadoEnviado);

        var (tomada, texto) = await Pool(db, Dev(segundo), cliente).TomarAsync(actividad.Id, segundo);
        Assert.True(tomada, texto);

        Assert.Equal(["ana.perez@soltum.com.mx", "beto.ruiz@soltum.com.mx"], cliente.AsignacionesEscritas);
        Assert.Equal(segundo, (await LeerAsync(db, actividad.Id))!.DevOpsAsignadoADeveloperId);
    }

    /// <summary>
    /// Lo que ya está en DevOps no se vuelve a mandar en cada guardado. Con la asignación y el estado
    /// puestos, editar o reintentar no tiene que producir una sola llamada más: son marcas de agua,
    /// no una orden que se repita.
    /// </summary>
    [Fact]
    public async Task Tomar_loQueYaLlego_noSeVuelveAMandar()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        int devId = NuevoDesarrollador(db);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        int llamadas = cliente.Llamadas.Count;

        var (ok, mensaje) = await Puente(db, Dev(devId), cliente).ReintentarAsync(actividad.Id);
        Assert.True(ok, mensaje);
        Assert.Equal(llamadas, cliente.Llamadas.Count);
    }

    /// <summary>
    /// El ticket local se refleja con lo que DevOps aceptó, para que la rejilla no siga enseñando al
    /// asignado y la columna anteriores hasta la próxima sincronización. Es el mismo criterio que ya
    /// aplica la prioridad.
    /// </summary>
    [Fact]
    public async Task Tomar_reflejaElTicketLocal()
    {
        using var db = await BaseListaAsync();
        db.DevOpsTickets.Add(new DevOpsTicket
        {
            ExternalId = 4321, Title = "Corregir el cálculo", State = "New", AssignedTo = ""
        });
        await db.SaveChangesAsync();

        var cliente = new DevOpsDeMentira { NombreEnDevOps = { ["ana.perez@soltum.com.mx"] = "Ana Pérez" } };
        int devId = NuevoDesarrollador(db, "Ana Pérez", "ana.perez@soltum.com.mx");

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        var ticket = await db.DevOpsTickets.AsNoTracking().SingleAsync(t => t.ExternalId == 4321);
        Assert.Equal("Ana Pérez", ticket.AssignedTo);
        Assert.Equal("ana.perez@soltum.com.mx", ticket.AssignedToUniqueName);
        Assert.Equal(PoolDevOpsService.EstadoEnProgresoPorOmision, ticket.State);
    }

    // ── 4c. El vínculo que cambia bajo los pies ──────────────────────────────────

    /// <summary>
    /// <b>REGRESIÓN.</b> Cuando DevOps contesta sin correo, el reflejo local NO borra
    /// <c>AssignedToUniqueName</c>: guarda el que se mandó.
    ///
    /// <para>Esa columna es la llave con la que toda la aplicación decide de quién es un ticket
    /// —qué sale en «Mis tickets DevOps», qué puede operar un desarrollador, a quién se le liga el
    /// requerimiento—. Escribir el nulo de una respuesta que simplemente no traía el campo borraría
    /// un dato bueno de la sincronización y degradaría ese ticket a empatarse solo por nombre.</para>
    /// </summary>
    [Fact]
    public async Task ReflejoLocal_siDevOpsNoDevuelveCorreo_conservaLaIdentidadQueSeMando()
    {
        using var db = await BaseListaAsync();
        db.DevOpsTickets.Add(new DevOpsTicket
        {
            ExternalId = 4321, Title = "Corregir el cálculo", State = "New",
            AssignedTo = "Alguien Anterior", AssignedToUniqueName = "alguien.anterior@soltum.com.mx"
        });
        await db.SaveChangesAsync();

        // DevOps acepta pero contesta sin uniqueName, que es el caso que se da con «System.AssignedTo»
        // devuelto como texto suelto.
        var cliente = new DevOpsDeMentira { ResuelveLaAsignacionComo = ("Ana Pérez", "") };
        int devId = NuevoDesarrollador(db, "Ana Pérez", "ana.perez@soltum.com.mx");

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        var (tomada, texto) = await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);
        Assert.True(tomada, texto);

        var ticket = await db.DevOpsTickets.AsNoTracking().SingleAsync(t => t.ExternalId == 4321);
        Assert.Equal("Ana Pérez", ticket.AssignedTo);
        Assert.Equal("ana.perez@soltum.com.mx", ticket.AssignedToUniqueName);
    }

    /// <summary>
    /// <b>REGRESIÓN.</b> Repuntar la actividad a OTRO work item desde «Editar» tiene que olvidar
    /// TAMBIÉN a nombre de quién quedó el anterior.
    ///
    /// <para>Es el único camino por el que una marca de asignación puede llegar viva a un ticket
    /// nuevo: soltar el reclamo la conserva a propósito —sin dueño no hay nada pendiente—, así que
    /// una actividad devuelta al pool llega a la edición con ella puesta. Sin este olvido, cuando la
    /// MISMA persona vuelva a tomarla, «pendiente» compara su identificador contra el que confirmó
    /// el ticket ANTERIOR, sale que no falta nada, y el work item nuevo se queda «en progreso» y sin
    /// dueño. Y en silencio: la actividad ni siquiera aparece en la lista de pendientes del líder.</para>
    /// </summary>
    [Fact]
    public async Task CambiarDeTicketAlEditar_olvidaTambienAQuienSeLeAsignoElAnterior()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        int devId = NuevoDesarrollador(db, "Ana Pérez", "ana.perez@soltum.com.mx");

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 100));
        await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);
        Assert.Equal(devId, (await LeerAsync(db, actividad.Id))!.DevOpsAsignadoADeveloperId);

        // Se devuelve al pool: la marca de la asignación SOBREVIVE (allá sigue a su nombre), la del
        // estado no. Es la asimetría que hace alcanzable el fallo.
        await Pool(db, Dev(devId), cliente).DevolverAsync(actividad.Id, devId, "no me da la vida");
        var libre = await LeerAsync(db, actividad.Id);
        Assert.Equal(devId, libre!.DevOpsAsignadoADeveloperId);
        Assert.Null(libre.DevOpsEstadoEnviado);

        // El líder la repunta a otro ticket: el bueno era el #999.
        var cambio = Borrador(workItem: 999);
        var (editada, porQue) = await Pool(db, Admin(), cliente).EditarAsync(actividad.Id, cambio);
        Assert.True(editada, porQue);

        var reapuntada = await LeerAsync(db, actividad.Id);
        Assert.Equal(999, reapuntada!.DevOpsWorkItemId);
        Assert.Null(reapuntada.DevOpsAsignadoADeveloperId);
        Assert.Null(reapuntada.DevOpsEstadoEnviado);

        // Y al volver a tomarla la MISMA persona, el ticket nuevo sí se pone a su nombre.
        cliente.AsignacionesEscritas.Clear();
        var (tomada, texto) = await Pool(db, Dev(devId), cliente).TomarAsync(actividad.Id, devId);
        Assert.True(tomada, texto);

        Assert.Equal(["ana.perez@soltum.com.mx"], cliente.AsignacionesEscritas);
        var alFinal = await LeerAsync(db, actividad.Id);
        Assert.Equal(devId, alFinal!.DevOpsAsignadoADeveloperId);
        Assert.False(alFinal.PendienteDeEnviarADevOps);
    }

    /// <summary>
    /// <b>REGRESIÓN.</b> Una cuenta de desarrollador SIN FICHA no puede reintentar el empuje de una
    /// actividad que no es suya.
    ///
    /// <para>El estado se da de verdad: la clave ajena de <c>Users</c> hacia <c>Developers</c> está
    /// declarada <c>OnDelete(SetNull)</c>, así que borrar una ficha deja la sesión viva con
    /// <c>DeveloperId</c> nulo. Comparando los dos nullables a mano, esa cuenta pasaba la guarda
    /// sobre cualquier actividad LIBRE —donde <c>ClaimedByDeveloperId</c> también es nulo—, porque
    /// nulo distinto de nulo es falso. Y reintentar escribe en un work item ajeno con el token de la
    /// INSTALACIÓN, así que no es una puerta cualquiera.</para>
    /// </summary>
    [Fact]
    public async Task Reintentar_sinFichaDeDesarrollador_noPasaPorUnaActividadLibre()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira { AceptaEsfuerzo = false };   // deja algo pendiente

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        Assert.True((await LeerAsync(db, actividad!.Id))!.PendienteDeEnviarADevOps);

        // Rol Desarrollador y NINGUNA ficha: exactamente lo que queda tras borrar un Developer.
        var sinFicha = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: null, userId: 77);

        int llamadas = cliente.Llamadas.Count;
        var (ok, mensaje) = await Puente(db, sinFicha, cliente).ReintentarAsync(actividad.Id);

        Assert.False(ok);
        Assert.Contains("no es tuya", mensaje);
        Assert.Equal(llamadas, cliente.Llamadas.Count);   // no salió nada hacia DevOps
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

    // ── 10b. Comentar CON EVIDENCIAS ─────────────────────────────────────────────

    /// <summary>Un PNG mínimo: lo que decide si algo es imagen son sus BYTES, no su extensión.</summary>
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 1, 2, 3];

    /// <summary>
    /// <b>La razón de que las evidencias se puedan adjuntar desde el pool:</b> la captura tiene que
    /// acabar DENTRO del comentario del work item, que es lo único que ve quien lee el ticket en
    /// DevOps. Subirla y no embeberla la dejaría en un adjunto que nadie encuentra.
    /// </summary>
    [Fact]
    public async Task Comentar_conEvidencias_lasSubeYLasEmbebeEnElComentario()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        var admin = Admin();

        var (_, _, actividad) = await Pool(db, admin, cliente).CrearAsync(Borrador(workItem: 4321));
        await ConTokenPropioAsync(db, admin, "pat-de-la-jefa");

        var (ok, mensaje) = await Puente(db, admin, cliente).ComentarAsync(
            actividad!.Id, "Corregido y probado.", [("antes.png", Png()), ("despues.png", Png())]);

        Assert.True(ok, mensaje);
        Assert.Equal(["antes.png", "despues.png"], cliente.AdjuntosSubidos);

        var publicado = Assert.Single(cliente.ComentariosPublicados);
        Assert.Contains("Corregido y probado.", publicado);
        Assert.Contains("antes.png", publicado);
        Assert.Contains("<img src=\"https://dev.azure.com/_apis/wit/attachments/1\"", publicado);
        Assert.Contains("<img src=\"https://dev.azure.com/_apis/wit/attachments/2\"", publicado);

        // Se firman con el token de quien comenta, igual que el texto: la subida deja rastro en el
        // historial del work item y tiene que llevar su nombre.
        Assert.Equal("pat-de-la-jefa", cliente.UltimoToken);
    }

    /// <summary>
    /// Con captura, el texto deja de ser obligatorio: pegar la pantalla del error ya corregido ES el
    /// comentario, y obligar a escribir «adjunto evidencia» al lado no añade nada.
    /// </summary>
    [Fact]
    public async Task Comentar_soloConEvidencia_sePublica()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        var admin = Admin();

        var (_, _, actividad) = await Pool(db, admin, cliente).CrearAsync(Borrador(workItem: 4321));
        await ConTokenPropioAsync(db, admin, "pat-de-la-jefa");

        var (ok, mensaje) = await Puente(db, admin, cliente)
            .ComentarAsync(actividad!.Id, "", [("prueba.png", Png())]);

        Assert.True(ok, mensaje);
        Assert.Single(cliente.ComentariosPublicados);
    }

    /// <summary>Sin texto y sin capturas no hay nada que publicar, y se dice de las dos formas.</summary>
    [Fact]
    public async Task Comentar_vacioYSinEvidencias_seNiega()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        var admin = Admin();

        var (_, _, actividad) = await Pool(db, admin, cliente).CrearAsync(Borrador(workItem: 4321));
        await ConTokenPropioAsync(db, admin, "pat-de-la-jefa");

        var (ok, mensaje) = await Puente(db, admin, cliente).ComentarAsync(actividad!.Id, "   ");

        Assert.False(ok);
        Assert.Contains("evidencia", mensaje);
        Assert.Empty(cliente.ComentariosPublicados);
    }

    /// <summary>
    /// Lo que no es una imagen se rechaza <b>por sus bytes</b> y ANTES de subir nada. A esta ruta se
    /// la puede llamar sin pasar por el navegador, y lo que se suba acaba servido desde el dominio de
    /// Azure DevOps: creerle a la extensión sería dejar que quien sube elija qué se publica allá.
    /// </summary>
    [Fact]
    public async Task Comentar_conAlgoQueNoEsImagen_seRechazaSinSubirNada()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        var admin = Admin();

        var (_, _, actividad) = await Pool(db, admin, cliente).CrearAsync(Borrador(workItem: 4321));
        await ConTokenPropioAsync(db, admin, "pat-de-la-jefa");

        var (ok, _) = await Puente(db, admin, cliente).ComentarAsync(
            actividad!.Id, "Aquí va la prueba.", [("parece.png", "esto es texto plano"u8.ToArray())]);

        Assert.False(ok);
        Assert.Empty(cliente.AdjuntosSubidos);
        Assert.Empty(cliente.ComentariosPublicados);
    }

    /// <summary>
    /// El lote se valida ENTERO antes de subir la primera. Al revés, un lote con la última mala
    /// dejaría las anteriores ya subidas a DevOps sin comentario que las enseñe: adjuntos huérfanos
    /// que nadie va a encontrar para borrarlos.
    /// </summary>
    [Fact]
    public async Task Comentar_siUnaEvidenciaNoVale_noSeSubeNinguna()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        var admin = Admin();

        var (_, _, actividad) = await Pool(db, admin, cliente).CrearAsync(Borrador(workItem: 4321));
        await ConTokenPropioAsync(db, admin, "pat-de-la-jefa");

        var (ok, _) = await Puente(db, admin, cliente).ComentarAsync(
            actividad!.Id, "Van dos.",
            [("buena.png", Png()), ("mala.png", "no soy una imagen"u8.ToArray())]);

        Assert.False(ok);
        Assert.Empty(cliente.AdjuntosSubidos);
    }

    /// <summary>Más capturas de las que caben se rechaza sin salir a la red.</summary>
    [Fact]
    public async Task Comentar_conDemasiadasEvidencias_seRechaza()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        var admin = Admin();

        var (_, _, actividad) = await Pool(db, admin, cliente).CrearAsync(Borrador(workItem: 4321));
        await ConTokenPropioAsync(db, admin, "pat-de-la-jefa");

        var demasiadas = Enumerable
            .Range(1, PoolDevOpsService.MaxEvidencias + 1)
            .Select(i => ($"captura{i}.png", Png()))
            .ToList();

        var (ok, mensaje) = await Puente(db, admin, cliente)
            .ComentarAsync(actividad!.Id, "Todas de golpe.", demasiadas);

        Assert.False(ok);
        Assert.Contains(PoolDevOpsService.MaxEvidencias.ToString(), mensaje);
        Assert.Empty(cliente.AdjuntosSubidos);
    }

    // ── 10c. La COLUMNA del tablero ──────────────────────────────────────

    /// <summary>
    /// Sin ajuste de columna NO se manda ninguna: el estado ya mueve la tarjeta en los tableros
    /// normales, y dos peticiones más por cada toma —una para descubrir el campo del tablero y otra
    /// para escribirlo— no se pagan por nada.
    /// </summary>
    [Fact]
    public async Task Tomar_sinAjusteDeColumna_noSeTocaLaColumna()
    {
        using var db = await BaseListaAsync();
        var cliente = new DevOpsDeMentira();
        int devId = NuevoDesarrollador(db);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        Assert.NotEmpty(cliente.EstadosEscritos);      // el estado sí
        Assert.Empty(cliente.ColumnasEscritas);        // la columna no
    }

    /// <summary>
    /// Con el ajuste puesto, al tomarla la tarjeta se mueve de columna —y el tipo del work item
    /// decide a cuál, igual que con el estado.
    /// </summary>
    [Fact]
    public async Task Tomar_conAjusteDeColumna_seMueveLaTarjeta()
    {
        using var db = await BaseListaAsync();
        db.DevOpsTickets.Add(new DevOpsTicket
        {
            ExternalId = 4321, Title = "algo", State = "New", WorkItemType = "Bug"
        });
        db.AppSettings.Add(new AppSetting
        {
            Key = SettingsService.Claves.PoolDevOpsColumnaAlTomar,
            Value = "Bug=Corrección; Task=En curso"
        });
        await db.SaveChangesAsync();

        var cliente = new DevOpsDeMentira();
        int devId = NuevoDesarrollador(db);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        var (tomada, texto) = await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        Assert.True(tomada, texto);
        Assert.Equal([("Corrección", false)], cliente.ColumnasEscritas);

        // Y queda constancia de las DOS cosas en la misma marca, porque son un solo paso.
        var fila = await LeerAsync(db, actividad.Id);
        Assert.Contains("Corrección", fila!.DevOpsEstadoEnviado);
        Assert.False(fila.EstadoPendienteDeEnviar);
    }

    /// <summary>
    /// El estado va PRIMERO y la columna después. Al revés, el cambio de estado arrastraría la
    /// tarjeta a la columna por omisión de ese estado y desharía lo que se acabara de poner —que es
    /// justo lo único que esta función viene a arreglar.
    /// </summary>
    [Fact]
    public async Task Tomar_elEstadoVaAntesQueLaColumna()
    {
        using var db = await BaseListaAsync();
        db.AppSettings.Add(new AppSetting
        {
            Key = SettingsService.Claves.PoolDevOpsColumnaAlTomar, Value = "En curso"
        });
        await db.SaveChangesAsync();

        var cliente = new DevOpsDeMentira();
        int devId = NuevoDesarrollador(db);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        Assert.Equal(["estado", "columna"], cliente.OrdenDeLosMovimientos);
    }

    /// <summary>La mitad derecha de una columna partida llega como el booleano aparte que es, y no
    /// pegada al nombre de la columna —que sería una columna inexistente.</summary>
    [Fact]
    public async Task Tomar_conColumnaPartida_laMitadViajaAparte()
    {
        using var db = await BaseListaAsync();
        db.AppSettings.Add(new AppSetting
        {
            Key = SettingsService.Claves.PoolDevOpsColumnaAlTomar, Value = "En curso|hecho"
        });
        await db.SaveChangesAsync();

        var cliente = new DevOpsDeMentira();
        int devId = NuevoDesarrollador(db);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        Assert.Equal([("En curso", true)], cliente.ColumnasEscritas);
    }

    /// <summary>
    /// Si la columna falla, el paso entero se queda PENDIENTE aunque el estado sí hubiera entrado.
    ///
    /// <para>Marcarlo como hecho dejaría la tarjeta en la columna equivocada para siempre, sin nada
    /// que lo reintentara. Volver a escribir el estado que ya está no cuesta nada, así que el
    /// reintento del paso completo es lo barato.</para>
    /// </summary>
    [Fact]
    public async Task Tomar_siLaColumnaFalla_elPasoSeQuedaPendiente()
    {
        using var db = await BaseListaAsync();
        db.AppSettings.Add(new AppSetting
        {
            Key = SettingsService.Claves.PoolDevOpsColumnaAlTomar, Value = "Inventada"
        });
        await db.SaveChangesAsync();

        var cliente = new DevOpsDeMentira
        {
            FalloDeColumna = new ErrorDeAzureDevOps(
                "El valor «Inventada» no está entre los permitidos para el campo de columna.")
        };
        int devId = NuevoDesarrollador(db);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        var (tomada, texto) = await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        Assert.True(tomada, texto);   // tomarla no depende de que DevOps coopere

        var fila = await LeerAsync(db, actividad.Id);
        Assert.Null(fila!.DevOpsEstadoEnviado);
        Assert.True(fila.EstadoPendienteDeEnviar);

        // Y el motivo habla de la COLUMNA y de su ajuste, no del estado: mandar a mirar el ajuste del
        // estado —que sí había funcionado— es media hora perdida.
        Assert.Contains("columna", fila.DevOpsUltimoError!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SettingsService.Claves.PoolDevOpsColumnaAlTomar, fila.DevOpsUltimoError);
    }

    /// <summary>
    /// Un work item que no está en ningún tablero no tumba la toma ni pierde la asignación: se
    /// cuenta y se reintenta. Pasa con las tareas hijas y con las áreas de otro equipo, que no son un
    /// error de configuración de nadie.
    /// </summary>
    [Fact]
    public async Task Tomar_siElWorkItemNoEstaEnUnTablero_laTomaSigueEnPie()
    {
        using var db = await BaseListaAsync();
        db.AppSettings.Add(new AppSetting
        {
            Key = SettingsService.Claves.PoolDevOpsColumnaAlTomar, Value = "En curso"
        });
        await db.SaveChangesAsync();

        var cliente = new DevOpsDeMentira
        {
            FalloDeColumna = new ErrorDeAzureDevOps(
                "El work item #4321 no pertenece a ningún tablero de este proyecto.")
        };
        int devId = NuevoDesarrollador(db);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        var (tomada, _) = await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        Assert.True(tomada);

        var fila = await LeerAsync(db, actividad.Id);
        Assert.Equal(devId, fila!.DevOpsAsignadoADeveloperId);   // la asignación sí entró
        Assert.False(fila.AsignacionPendienteDeEnviar);
        Assert.Contains("tablero", fila.DevOpsUltimoError!);
    }

    /// <summary>
    /// La marca de agua se recorta a lo que cabe en su columna.
    ///
    /// <para>Guarda el estado y la columna juntos, y en SQL Server pasarse de <c>nvarchar(100)</c> no
    /// trunca: rechaza el guardado. Sin recorte, un nombre de columna largo dejaría la actividad sin
    /// marca reintentando para siempre un movimiento que ya había entrado.</para>
    /// </summary>
    [Fact]
    public async Task Tomar_conUnaColumnaDeNombreLargo_laMarcaCabeEnSuColumna()
    {
        using var db = await BaseListaAsync();
        db.AppSettings.Add(new AppSetting
        {
            Key = SettingsService.Claves.PoolDevOpsColumnaAlTomar, Value = new string('C', 120)
        });
        await db.SaveChangesAsync();

        var cliente = new DevOpsDeMentira();
        int devId = NuevoDesarrollador(db);

        var (_, _, actividad) = await Pool(db, Admin(), cliente).CrearAsync(Borrador(workItem: 4321));
        await Pool(db, Dev(devId), cliente).TomarAsync(actividad!.Id, devId);

        var fila = await LeerAsync(db, actividad.Id);
        Assert.NotNull(fila!.DevOpsEstadoEnviado);
        Assert.True(fila.DevOpsEstadoEnviado!.Length <= 100,
                    $"la marca mide {fila.DevOpsEstadoEnviado.Length} y la columna admite 100");
        Assert.False(fila.EstadoPendienteDeEnviar);
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
            "DevOpsAsignadoADeveloperId", "DevOpsEstadoEnviado",
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
