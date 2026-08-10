using AdminWeb.Application.Services;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Un buzón de mentira.
///
/// <para><b>Aquí no se toca la red, y esa es la razón de que exista <c>IClienteDeCorreo</c>.</b> Una
/// prueba que dependiera de un servidor IMAP de verdad fallaría los días que se cayera y pasaría los
/// días que alguien hubiera dejado el buzón limpio — o sea, no probaría nada.</para>
///
/// <para>Imita lo único del buzón de lo que dependen las reglas: <b>un mensaje deja de estar «sin
/// procesar» cuando se marca</b>, exactamente como la bandera de leído en IMAP. Y sabe fallar a
/// propósito, porque la mitad de lo que hay que comprobar es qué pasa cuando algo se rompe entre
/// guardar el requerimiento y marcar el correo.</para>
/// </summary>
internal sealed class CorreoDeMentira : IClienteDeCorreo
{
    private readonly List<MensajeDeCorreo> _buzon = [];
    private readonly HashSet<uint> _marcados = [];

    /// <summary>Lo que se ha enviado, para poder mirarlo. El adjunto es null cuando no llevaba.</summary>
    public List<(IReadOnlyList<string> Destinatarios, string Asunto, string Cuerpo, AdjuntoDeCorreo? Adjunto)>
        Enviados { get; } = [];

    public bool FallaElEnvio { get; set; }
    public bool FallaElMarcado { get; set; }

    /// <summary>Qué UID han quedado marcados como procesados.</summary>
    public IReadOnlySet<uint> Marcados => _marcados;

    /// <summary>Mete un correo en el buzón.</summary>
    public void Recibir(uint uid, string identidad, string asunto = "Necesito un reporte nuevo",
        string de = "cliente@fuera.com", string cuerpo = "Buenas tardes, ¿podrían agregar un reporte?")
    {
        _buzon.Add(new MensajeDeCorreo(uid, identidad, asunto, de,
            new DateTime(2026, 8, 5, 16, 30, 0, DateTimeKind.Utc),
            cuerpo.Length <= 120 ? cuerpo : cuerpo[..120], cuerpo));
    }

    public Task EnviarAsync(ConfiguracionDeCorreo configuracion, IEnumerable<string> destinatarios,
        string asunto, string cuerpo, AdjuntoDeCorreo? adjunto = null, CancellationToken ct = default)
    {
        if (FallaElEnvio) throw new ErrorDeCorreo("El servidor SMTP rechazó el envío.");

        Enviados.Add((destinatarios.ToList(), asunto, cuerpo, adjunto));
        return Task.CompletedTask;
    }

    public Task<string> ProbarConexionAsync(ConfiguracionDeCorreo configuracion, CancellationToken ct = default) =>
        Task.FromResult("Conexión exitosa (SMTP + IMAP).");

    public Task<IReadOnlyList<string>> ListarCarpetasAsync(ConfiguracionDeCorreo configuracion,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(["INBOX", "Requerimientos"]);

    public Task<IReadOnlyList<MensajeDeCorreo>> LeerRecientesAsync(ConfiguracionDeCorreo configuracion,
        string carpeta, int maximo, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MensajeDeCorreo>>(_buzon.Take(maximo).ToList());

    public Task<IReadOnlyList<MensajeDeCorreo>> LeerSinProcesarAsync(ConfiguracionDeCorreo configuracion,
        string carpeta, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MensajeDeCorreo>>(
            _buzon.Where(m => !_marcados.Contains(m.Identificador)).ToList());

    public Task<MensajeDeCorreo?> LeerUnoAsync(ConfiguracionDeCorreo configuracion, string carpeta,
        uint identificador, CancellationToken ct = default) =>
        Task.FromResult(_buzon.FirstOrDefault(m => m.Identificador == identificador));

    public Task MarcarComoProcesadosAsync(ConfiguracionDeCorreo configuracion, string carpeta,
        IReadOnlyCollection<uint> identificadores, CancellationToken ct = default)
    {
        if (FallaElMarcado) throw new ErrorDeCorreo("El servidor IMAP rechazó la operación.");

        foreach (var id in identificadores) _marcados.Add(id);
        return Task.CompletedTask;
    }
}

/// <summary>
/// La ingesta de requerimientos desde el correo.
///
/// <para><b>Lo que se cuida aquí es que no se dupliquen.</b> La ingesta corre sola cada pocos
/// minutos: una regla que falle una vez no produce un error visible, produce dos requerimientos
/// iguales, y luego tres, y para cuando alguien se da cuenta hay que limpiar el backlog a mano.</para>
///
/// <para>La regla que se hereda del escritorio es que <b>un correo está pendiente mientras siga sin
/// leer</b>. Lo que se añade —y es lo que más pruebas tiene abajo— es que la marca de leído no puede
/// ser la ÚNICA defensa: entre guardar el requerimiento y marcar el correo hay una ventana, y en un
/// trabajo que corre solo esa ventana se acaba abriendo.</para>
/// </summary>
public class IngestaDeCorreoServiceTests
{
    private const string Carpeta = "Requerimientos";

    private static async Task<SettingsService> AjustesAsync(AppDbContext db, bool habilitado = true)
    {
        var admin = UsuarioDePrueba.Como(UserRole.Admin);
        var ajustes = new SettingsService(db, admin, new AuditService(db, admin, new OrigenDePrueba()));

        await ajustes.GuardarAsync(SettingsService.Claves.EmailEnabled, habilitado ? "true" : "false");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailAddress, "aplicacion@empresa.com");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailPassword, "contrasena-de-aplicacion");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailSmtpHost, "smtp.empresa.com");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailImapHost, "imap.empresa.com");
        await ajustes.GuardarAsync(SettingsService.Claves.EmailRequirementsFolder, Carpeta);

        return ajustes;
    }

    private static IngestaDeCorreoService Svc(AppDbContext db, SettingsService ajustes,
        IClienteDeCorreo correo, ICurrentUser? usuario = null)
    {
        var actual = usuario ?? UsuarioDePrueba.Como(UserRole.Admin);
        var bitacora = new AuditService(db, actual, new OrigenDePrueba());

        return new IngestaDeCorreoService(db, ajustes, correo,
            new DigestService(db, ajustes, correo, bitacora, actual), bitacora, actual);
    }

    // ── Lo básico ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ingerir_CreaUnRequerimientoPorCadaCorreoSinProcesar()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);
        var buzon = new CorreoDeMentira();
        buzon.Recibir(1, "<uno@fuera.com>", asunto: "Reporte de ventas");
        buzon.Recibir(2, "<dos@fuera.com>", asunto: "Alta de usuario");

        var (ok, _, creados) = await Svc(db, ajustes, buzon).IngerirAsync(null);

        Assert.True(ok);
        Assert.Equal(2, creados);
        Assert.Equal(2, await db.Requirements.CountAsync());
    }

    [Fact]
    public async Task Ingerir_ElRequerimientoConservaRemitenteAsuntoYOrigen()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);
        var buzon = new CorreoDeMentira();
        buzon.Recibir(7, "<siete@fuera.com>", asunto: "Necesito el corte del mes",
            de: "contabilidad@cliente.com", cuerpo: "Buenas, ¿me pasan el corte?");

        await Svc(db, ajustes, buzon).IngerirAsync(null);

        var creado = await db.Requirements.SingleAsync();
        Assert.Equal("Necesito el corte del mes", creado.Title);
        Assert.Equal(RequirementSource.Email, creado.Source);
        Assert.Equal(RequirementStatus.PorEstimar, creado.Status);
        Assert.Equal("<siete@fuera.com>", creado.ExternalId);
        Assert.Contains("contabilidad@cliente.com", creado.Description);
        Assert.Contains("¿me pasan el corte?", creado.Description);
    }

    [Fact]
    public async Task Ingerir_MarcaLosCorreosQueYaProceso()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);
        var buzon = new CorreoDeMentira();
        buzon.Recibir(1, "<uno@fuera.com>");
        buzon.Recibir(2, "<dos@fuera.com>");

        await Svc(db, ajustes, buzon).IngerirAsync(null);

        Assert.Equal(new uint[] { 1, 2 }, buzon.Marcados.OrderBy(x => x));
    }

    // ── No duplicar: el corazón de la ingesta ────────────────────────────────────

    [Fact]
    public async Task Ingerir_DosVeces_NoVuelveACrearLoMismo()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);
        var buzon = new CorreoDeMentira();
        buzon.Recibir(1, "<uno@fuera.com>");

        var servicio = Svc(db, ajustes, buzon);
        await servicio.IngerirAsync(null);
        var (_, _, segunda) = await servicio.IngerirAsync(null);

        Assert.Equal(0, segunda);
        Assert.Equal(1, await db.Requirements.CountAsync());
    }

    /// <summary>
    /// El caso que el escritorio no cubría y que en un trabajo de fondo llega solo: el requerimiento
    /// se guardó y el buzón se cayó ANTES de poder marcar el correo. En la siguiente pasada el
    /// mensaje sigue sin leer, así que se vuelve a leer entero — y aun así no se duplica, porque su
    /// Message-Id ya está guardado.
    /// </summary>
    [Fact]
    public async Task Ingerir_SiElMarcadoFalla_LaSiguientePasadaNoDuplica()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);
        var buzon = new CorreoDeMentira { FallaElMarcado = true };
        buzon.Recibir(1, "<uno@fuera.com>");

        var servicio = Svc(db, ajustes, buzon);

        var (primeraOk, _, primera) = await servicio.IngerirAsync(null);
        var (_, _, segunda) = await servicio.IngerirAsync(null);

        // La primera pasada se reporta como buena aunque el marcado fallara: el requerimiento existe,
        // y decir lo contrario invitaría a repetirla.
        Assert.True(primeraOk);
        Assert.Equal(1, primera);
        Assert.Equal(0, segunda);
        Assert.Empty(buzon.Marcados);
        Assert.Equal(1, await db.Requirements.CountAsync());
    }

    /// <summary>
    /// El mismo correo puede aparecer dos veces en una carpeta (un reenvío, una regla del buzón que
    /// lo copió). Dentro de una misma tanda tampoco puede convertirse en dos requerimientos.
    /// </summary>
    [Fact]
    public async Task Ingerir_ElMismoMensajeRepetidoEnLaTanda_SoloCreaUno()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);
        var buzon = new CorreoDeMentira();
        buzon.Recibir(1, "<repetido@fuera.com>");
        buzon.Recibir(2, "<repetido@fuera.com>");

        var (_, _, creados) = await Svc(db, ajustes, buzon).IngerirAsync(null);

        Assert.Equal(1, creados);
        Assert.Equal(1, await db.Requirements.CountAsync());

        // Los DOS quedan marcados: si el descartado siguiera sin leer, cada pasada volvería a
        // leerlo y a descartarlo para siempre.
        Assert.Equal(new uint[] { 1, 2 }, buzon.Marcados.OrderBy(x => x));
    }

    [Fact]
    public async Task Ingerir_UnCorreoYaLeido_NiSiquieraSeMira()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);
        var buzon = new CorreoDeMentira();
        buzon.Recibir(1, "<uno@fuera.com>");
        await buzon.MarcarComoProcesadosAsync(null!, Carpeta, [1u]);

        var (ok, _, creados) = await Svc(db, ajustes, buzon).IngerirAsync(null);

        Assert.True(ok);
        Assert.Equal(0, creados);
        Assert.Equal(0, await db.Requirements.CountAsync());
    }

    // ── Convertir uno a mano ─────────────────────────────────────────────────────

    [Fact]
    public async Task Convertir_ElMismoCorreoDosVeces_LoRechazaLaSegunda()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);
        var buzon = new CorreoDeMentira();
        buzon.Recibir(9, "<nueve@fuera.com>");

        var servicio = Svc(db, ajustes, buzon);

        var (primeraOk, _) = await servicio.ConvertirEnRequerimientoAsync(Carpeta, 9);

        // Un mensaje leído sigue estando en la carpeta, así que nada impide volver a pulsar el
        // botón: lo que lo impide es que su Message-Id ya está guardado.
        var (segundaOk, segundoMensaje) = await servicio.ConvertirEnRequerimientoAsync(Carpeta, 9);

        Assert.True(primeraOk);

        Assert.False(segundaOk);
        Assert.Contains("ya se había convertido", segundoMensaje);
        Assert.Equal(1, await db.Requirements.CountAsync());
    }

    [Fact]
    public async Task Convertir_UnMensajeQueYaNoEsta_LoDiceSinReventar()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);

        var (ok, mensaje) = await Svc(db, ajustes, new CorreoDeMentira())
            .ConvertirEnRequerimientoAsync(Carpeta, 404);

        Assert.False(ok);
        Assert.Contains("ya no está", mensaje);
    }

    // ── Permisos y configuración ─────────────────────────────────────────────────

    [Fact]
    public async Task Ingerir_LoExigeElLider()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);
        var servicio = Svc(db, ajustes, new CorreoDeMentira(),
            UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 2));

        await Assert.ThrowsAsync<AuthorizationException>(() => servicio.IngerirAsync(null));
    }

    [Fact]
    public async Task Bandeja_LaExigeElLider()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);
        var servicio = Svc(db, ajustes, new CorreoDeMentira(),
            UsuarioDePrueba.Como(UserRole.Operaciones, userId: 3));

        await Assert.ThrowsAsync<AuthorizationException>(() => servicio.BandejaAsync(null));
    }

    /// <summary>
    /// El correo apagado no es una excepción: es el estado normal hasta que alguien lo configure, y
    /// lo que se responde es el texto del escritorio, que dice exactamente qué hacer.
    /// </summary>
    [Fact]
    public async Task Ingerir_SinCorreoHabilitado_LoExplicaEnVezDeReventar()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db, habilitado: false);

        var (ok, mensaje, creados) = await Svc(db, ajustes, new CorreoDeMentira()).IngerirAsync(null);

        Assert.False(ok);
        Assert.Equal(0, creados);
        Assert.Contains("no está habilitado", mensaje);
    }

    [Fact]
    public async Task IngestaProgramada_SinCorreoHabilitado_NoHaceNada()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db, habilitado: false);
        var buzon = new CorreoDeMentira();
        buzon.Recibir(1, "<uno@fuera.com>");

        int creados = await Svc(db, ajustes, buzon).EjecutarIngestaProgramadaAsync();

        Assert.Equal(0, creados);
        Assert.Equal(0, await db.Requirements.CountAsync());
    }

    /// <summary>
    /// Sin carpeta, el trabajo de fondo y el botón de la pantalla ingieren exactamente la misma: la
    /// configurada. Si divergieran, importar a mano y dejarlo correr darían resultados distintos.
    /// </summary>
    [Fact]
    public async Task IngestaProgramada_HaceLoMismoQueElBoton()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);
        var buzon = new CorreoDeMentira();
        buzon.Recibir(1, "<uno@fuera.com>");

        int creados = await Svc(db, ajustes, buzon).EjecutarIngestaProgramadaAsync();

        Assert.Equal(1, creados);
        Assert.Equal(RequirementSource.Email, (await db.Requirements.SingleAsync()).Source);
    }

    // ── Envío ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Enviar_SinDestinatariosValidos_LoRechaza()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);
        var buzon = new CorreoDeMentira();

        var (ok, mensaje) = await Svc(db, ajustes, buzon).EnviarAsync("no-es-un-correo", "Hola", "Qué tal");

        Assert.False(ok);
        Assert.Contains("destinatario", mensaje);
        Assert.Empty(buzon.Enviados);
    }

    [Fact]
    public async Task Enviar_UnFalloDelServidor_SaleComoMensajeYNoComoExcepcion()
    {
        var db = TestDb.New();
        var ajustes = await AjustesAsync(db);
        var buzon = new CorreoDeMentira { FallaElEnvio = true };

        var (ok, mensaje) = await Svc(db, ajustes, buzon).EnviarAsync("jefe@empresa.com", "Hola", "Qué tal");

        Assert.False(ok);
        Assert.Contains("SMTP", mensaje);
    }
}
