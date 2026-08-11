using System.Text.Json;
using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Personas;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// La pantalla de USUARIOS y el segundo factor: lo que el líder ve de cada cuenta y el reinicio.
///
/// <para>Estas pruebas se quedan en la costura entre las dos piezas, que es donde nadie más mira:
/// <c>SegundoFactorServiceTests</c> comprueba que el alta, el rescate y el reinicio hacen lo que
/// dicen; esto comprueba que <b>la lista de cuentas cuenta la verdad sobre ellos</b>. Un reinicio
/// impecable que la rejilla siga pintando como «activo» manda al líder a reiniciar otra vez, o —peor—
/// le hace creer que once personas ya se dieron de alta cuando no lo ha hecho ninguna.</para>
///
/// <para><b>Y la otra mitad es lo que la lista NO puede llevar.</b> Es una respuesta que se pide para
/// pintar una rejilla, o sea la que más veces viaja y la que menos se mira: si un día el secreto del
/// segundo factor o el hash de un código de rescate se colaran ahí, no lo notaría nadie.</para>
/// </summary>
public class UsuariosYSegundoFactorTests
{
    /// <summary>
    /// Un protector que marca en vez de cifrar, para poder buscar después el valor por su marca.
    /// Cifrar de verdad exigiría la protección de datos de ASP.NET, que vive en la API.
    /// </summary>
    private sealed class ProtectorDeMentira : IProtectorDeSecretos
    {
        public const string Marca = "cifrado:";
        public string Proteger(string valorEnClaro) => Marca + valorEnClaro;
        public string? Desproteger(string cifrado) =>
            cifrado.StartsWith(Marca) ? cifrado[Marca.Length..] : null;
    }

    /// <summary>Un dibujante que no dibuja: a estas pruebas el PNG les da igual.</summary>
    private sealed class DibujanteDeMentira : IDibujanteDeCodigoQr
    {
        public byte[] DibujarPng(string contenido) => [0];
    }

    private static PersonasQueryService Pantalla(AppDbContext db, ICurrentUser usuario)
    {
        var origen = new OrigenDePrueba();
        var bitacora = new AuditService(db, usuario, origen);
        return new PersonasQueryService(
            db, usuario, bitacora,
            new AuthService(db, usuario, bitacora),
            new PresenceService(db, usuario, origen),
            new AttendanceService(db, usuario, bitacora, origen),
            new DeveloperProfileService(db, usuario, bitacora),
            new AnnouncementService(db, usuario, bitacora));
    }

    private static SegundoFactorService SegundoFactor(AppDbContext db, ICurrentUser usuario) =>
        new(db, usuario, new ProtectorDeMentira(), new DibujanteDeMentira(),
            new AuditService(db, usuario, new OrigenDePrueba()));

    private static AppDbContext ConDosCuentas()
    {
        var db = TestDb.New();
        db.Users.Add(new User
        {
            Id = 1, Username = "lider", FullName = "Quien manda", Role = UserRole.Admin,
            IsActive = true, PasswordHash = "x"
        });
        db.Users.Add(new User
        {
            Id = 2, Username = "ana", FullName = "Ana", Role = UserRole.Desarrollador,
            IsActive = true, PasswordHash = "x"
        });
        db.SaveChanges();
        return db;
    }

    private static UsuarioDePrueba ElLider() => UsuarioDePrueba.Como(UserRole.Admin, userId: 1);
    private static UsuarioDePrueba Ana() => UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 2);

    /// <summary>El código que enseñaría el teléfono ahora mismo para ese secreto.</summary>
    private static string CodigoDe(string secretoEnBase32) =>
        Totp.Calcular(Base32.Decodificar(secretoEnBase32)!, Totp.VentanaDe(DateTimeOffset.UtcNow));

    /// <summary>Da de alta el segundo factor de una cuenta de verdad, pasando por las dos fases.</summary>
    private static async Task<IReadOnlyList<string>> DarDeAltaAsync(AppDbContext db, ICurrentUser quien)
    {
        var svc = SegundoFactor(db, quien);
        var (_, _, alta) = await svc.ComenzarAltaAsync();
        var (ok, _, codigos) = await svc.ConfirmarAltaAsync(CodigoDe(alta!.SecretoEnBase32));

        Assert.True(ok);
        return codigos!;
    }

    private static UsuarioDto Fila(PantallaDeUsuariosDto datos, string usuario) =>
        datos.Usuarios.Single(u => u.Usuario == usuario);

    // ── Lo que el líder ve ───────────────────────────────────────────────────────

    /// <summary>
    /// Tras un alta de verdad, la lista dice que esa cuenta lo tiene y desde cuándo — y la otra sigue
    /// saliendo como pendiente.
    ///
    /// <para>Las DOS mitades importan. Sin la primera, el líder no puede saber quién falta; sin la
    /// segunda, una consulta mal escrita —un <c>Any()</c> sin filtrar por cuenta, por ejemplo— daría
    /// todas las cuentas por activas en cuanto una sola lo estuviera, y el error se leería como «ya
    /// están todos» justo el día en que hay que perseguir a quien falta.</para>
    /// </summary>
    [Fact]
    public async Task LaLista_DiceQuienLoTieneActivoYDesdeCuando()
    {
        var db = ConDosCuentas();
        await DarDeAltaAsync(db, Ana());

        var datos = await Pantalla(db, ElLider()).UsuariosAsync();

        var ana = Fila(datos, "ana");
        Assert.True(ana.SegundoFactorActivo);
        Assert.NotNull(ana.SegundoFactorDesdeUtc);
        // Recién dado de alta: la fecha es de ahora y no una heredada de otro campo.
        Assert.True(DateTime.UtcNow - ana.SegundoFactorDesdeUtc!.Value < TimeSpan.FromMinutes(5));

        var lider = Fila(datos, "lider");
        Assert.False(lider.SegundoFactorActivo);
        Assert.Null(lider.SegundoFactorDesdeUtc);
    }

    /// <summary>
    /// El alta entrega tantos códigos de rescate como dice la propia pantalla, y ese número es el que
    /// se usa para escribir «le quedan 2 de 8».
    ///
    /// <para>Es una prueba de coherencia, no de cálculo: el día que el servidor emita otra cantidad,
    /// la frase de la interfaz seguirá siendo cierta sola. Escribir el ocho a mano en la pantalla es
    /// exactamente lo que un día deja de coincidir sin que nada lo avise.</para>
    /// </summary>
    [Fact]
    public async Task LosCodigosQueSeEmiten_SonLosQueLaPantallaDiceQueSeEmiten()
    {
        var db = ConDosCuentas();
        var codigos = await DarDeAltaAsync(db, Ana());

        var datos = await Pantalla(db, ElLider()).UsuariosAsync();

        Assert.Equal(codigos.Count, datos.CodigosDeRescateEmitidos);
        Assert.Equal(codigos.Count, Fila(datos, "ana").CodigosDeRescateRestantes);
    }

    /// <summary>
    /// Los códigos gastados dejan de contar, y los de una cuenta no se le suman a la otra.
    ///
    /// <para>Lo segundo es lo que de verdad se está vigilando aquí: la cuenta de códigos se resuelve
    /// con UNA consulta agrupada para no hacer un viaje por fila, y una agrupación mal escrita es
    /// silenciosa — enseñaría ocho rescates disponibles a quien no le queda ninguno, que es la
    /// persona a la que hay que reiniciarle el segundo factor antes de que se quede fuera.</para>
    /// </summary>
    [Fact]
    public async Task LosCodigosGastados_NoCuentan_YNoSeMezclanEntreCuentas()
    {
        var db = ConDosCuentas();
        var deAna = await DarDeAltaAsync(db, Ana());
        await DarDeAltaAsync(db, ElLider());

        // Ana gasta dos de los suyos entrando con ellos.
        var puerta = SegundoFactor(db, UsuarioDePrueba.Anonimo());
        Assert.True((await puerta.VerificarAsync(2, deAna[0])).Exito);
        Assert.True((await puerta.VerificarAsync(2, deAna[1])).Exito);

        var datos = await Pantalla(db, ElLider()).UsuariosAsync();

        Assert.Equal(deAna.Count - 2, Fila(datos, "ana").CodigosDeRescateRestantes);
        // La del líder no se ha tocado: si la agrupación no filtrara por cuenta, este número bajaría
        // también, o subiría al sumarle los de Ana.
        Assert.Equal(deAna.Count, Fila(datos, "lider").CodigosDeRescateRestantes);
    }

    /// <summary>Quien no lo ha dado de alta sale sin fecha y sin ningún código, no con un cero raro.</summary>
    [Fact]
    public async Task QuienNoLoHaDadoDeAlta_SaleSinFechaYSinCodigos()
    {
        var db = ConDosCuentas();

        var datos = await Pantalla(db, ElLider()).UsuariosAsync();

        foreach (var cuenta in datos.Usuarios)
        {
            Assert.False(cuenta.SegundoFactorActivo);
            Assert.Null(cuenta.SegundoFactorDesdeUtc);
            Assert.Equal(0, cuenta.CodigosDeRescateRestantes);
        }
    }

    // ── El reinicio, visto desde la pantalla ─────────────────────────────────────

    /// <summary>
    /// Después de que el líder reinicie, la rejilla vuelve a decir la verdad: esa cuenta está otra vez
    /// pendiente de darlo de alta y ya no le queda ningún código de rescate.
    ///
    /// <para>Es la costura completa de la operación tal como se usa: se mira la lista, se pulsa el
    /// botón, se vuelve a mirar la lista. Si la segunda lectura enseñara el estado anterior, el líder
    /// reiniciaría dos veces —o daría por hecho que no funcionó— con alguien esperando al teléfono.</para>
    /// </summary>
    [Fact]
    public async Task TrasElReinicioDelLider_LaListaLoRefleja()
    {
        var db = ConDosCuentas();
        await DarDeAltaAsync(db, Ana());

        var (ok, mensaje) = await SegundoFactor(db, ElLider()).ReiniciarAsync(2);
        Assert.True(ok);
        // El mensaje del servidor es el que se enseña tal cual: tiene que nombrar a la cuenta, o el
        // líder no sabe sobre cuál de las once acaba de actuar.
        Assert.Contains("ana", mensaje);

        var ana = Fila(await Pantalla(db, ElLider()).UsuariosAsync(), "ana");
        Assert.False(ana.SegundoFactorActivo);
        Assert.Null(ana.SegundoFactorDesdeUtc);
        Assert.Equal(0, ana.CodigosDeRescateRestantes);
    }

    /// <summary>
    /// El reinicio deja en la bitácora QUIÉN lo hizo, SOBRE QUIÉN y CUÁNDO.
    ///
    /// <para>Es una acción que devuelve el acceso a una cuenta: sin las tres cosas no se puede
    /// reconstruir después si aquello fue lo que pareció. El nombre de quien lo pulsó no lo escribe la
    /// pantalla ni el endpoint —los dos son manipulables desde fuera— sino la bitácora, a partir de la
    /// sesión del servidor, y esta prueba fija esa diferencia.</para>
    /// </summary>
    [Fact]
    public async Task ElReinicio_DejaEnLaBitacoraQuienAQuienYCuando()
    {
        var db = ConDosCuentas();
        await DarDeAltaAsync(db, Ana());
        var antes = DateTime.UtcNow;

        await SegundoFactor(db, ElLider()).ReiniciarAsync(2);

        var asiento = db.AuditLogs
            .Where(a => a.EntityType == "User" && a.EntityId == "2")
            .OrderByDescending(a => a.Id)
            .First();

        Assert.Equal(ElLider().Username, asiento.UserName);   // quién
        Assert.Equal("2", asiento.EntityId);                  // a quién
        Assert.True(asiento.Timestamp >= antes.AddSeconds(-1)); // cuándo
        Assert.Contains("egundo factor", asiento.Details);
    }

    // ── Lo que la lista NO puede llevar ──────────────────────────────────────────

    /// <summary>
    /// Ni el secreto del segundo factor ni los hashes de los códigos de rescate aparecen en lo que se
    /// le manda al navegador.
    ///
    /// <para>Se comprueba sobre el JSON de verdad y no sobre las propiedades del DTO: lo que llega al
    /// navegador es ese texto, y buscar dentro atrapa también el caso de que alguien los meta dentro
    /// de un campo con otro nombre —«Notas», «Detalle»— donde una prueba de nombres no miraría.</para>
    ///
    /// <para>El secreto se busca por su valor EN CLARO y por su forma protegida, porque las dos
    /// serían igual de graves: la primera abre la cuenta, y la segunda la abriría el día que alguien
    /// se hiciera con el llavero.</para>
    /// </summary>
    [Fact]
    public async Task LaListaDeCuentas_NoLlevaElSecretoNiLosCodigosDeRescate()
    {
        var db = ConDosCuentas();
        await DarDeAltaAsync(db, Ana());

        var datos = await Pantalla(db, ElLider()).UsuariosAsync();
        var json = JsonSerializer.Serialize(datos);

        var guardado = db.UserSecrets.Single(s => s.Proposito == PropositosDeSecreto.SegundoFactor);
        var enClaro = guardado.CipherText[ProtectorDeMentira.Marca.Length..];

        Assert.DoesNotContain(guardado.CipherText, json);
        Assert.DoesNotContain(enClaro, json);

        foreach (var codigo in db.UserRecoveryCodes.Select(c => c.CodigoHash))
            Assert.DoesNotContain(codigo, json);
    }
}
