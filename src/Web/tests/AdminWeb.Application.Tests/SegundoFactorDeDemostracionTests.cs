using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AdminWeb.Application.Demo;
using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Seguridad;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Las cuentas de demostración nacen con el segundo factor ya dado de alta y con un secreto FIJO que
/// el arranque anuncia en el registro.
///
/// <para><b>Lo que estas pruebas cuidan no es que la comodidad funcione, sino que no pueda salirse de
/// la demostración.</b> Un secreto de segundo factor fijo y publicado es, por definición, un segundo
/// factor que no protege nada: en una base inventada da igual, y en la de verdad sería de los
/// agujeros graves —todas las cuentas con un código que puede calcular cualquiera que lea el
/// repositorio—. Por eso la mitad de lo que hay aquí abajo no comprueba que algo funcione, sino que
/// algo <b>no pueda ocurrir</b>: que fuera de una base virgen y de un entorno que no es Production no
/// se escriba ni una fila, y que no exista ninguna puerta pública por la que sembrar sin haberlo
/// preguntado antes.</para>
/// </summary>
public class SegundoFactorDeDemostracionTests
{
    /// <summary>Las seis cuentas que siembra la demostración, para no repetir la lista en cada prueba.</summary>
    private static readonly string[] CuentasDeDemostracion = ["lider", "ops", "ana", "beto", "caro", "dani"];

    /// <summary>
    /// Siembra igual que el arranque: primero los catálogos y después la demostración.
    ///
    /// <para>El orden no es decorativo. El pool y el desempeño de la demostración sacan sus puntos de
    /// la matriz que siembra <see cref="PoolSeed"/>, igual que haría la aplicación al publicar cada
    /// actividad; sin los catálogos delante, la siembra no encuentra de dónde copiarlos.</para>
    /// </summary>
    private static async Task<string?> SembrarComoElArranqueAsync(
        AppDbContext db, IProtectorDeSecretos protector, bool pedido = true, bool esProduccion = false)
    {
        await ScoringCriteriaSeed.SembrarAsync(db);
        await TemplateSeed.SembrarAsync(db);
        await PoolSeed.SembrarAsync(db);

        return await DatosDeDemostracion.SembrarSiSePuedeAsync(db, protector, pedido, esProduccion);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  EL CÓDIGO SE CALCULA POR FUERA, Y ESA ES LA MITAD DEL VALOR DE ESTE ARCHIVO
    //
    //  Lo que la demostración promete es que quien teclee el secreto anunciado EN SU APLICACIÓN DE
    //  CÓDIGOS entra. Generar aquí el código con las mismas piezas que después lo validan —Base32
    //  para leer el secreto, Totp para calcularlo— no comprueba eso: comprueba que la aplicación
    //  está de acuerdo consigo misma, que es cierto también cuando las dos están equivocadas. Se
    //  probó: poniendo el contador del HMAC al revés —un error que ningún teléfono del mundo
    //  perdonaría— este archivo entero seguía en verde.
    //
    //  Así que los bits del secreto se escriben a mano desde el RFC 4648 y el código se calcula a
    //  mano desde el RFC 6238, sin tocar Base32 ni Totp. Es lo mismo que ya hace totp-de-humo.ps1 y
    //  por el mismo motivo.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Los veinte bytes del secreto anunciado, deletreados A MANO desde el alfabeto del RFC 4648.
    ///
    /// <para>«DEMO» son los valores 3, 4, 12 y 14, o sea <c>00011 00100 01100 01110</c>: veinte bits.
    /// Reagrupados de ocho en ocho, cada DOS repeticiones cierran cinco bytes exactos —<c>00011001
    /// 00011000 11100001 10010001 10001110</c>—. El secreto son ocho «DEMO», así que es ese patrón
    /// cuatro veces: veinte bytes, los 160 bits que pide HMAC-SHA1.</para>
    /// </summary>
    private static byte[] BitsDelSecretoAnunciado()
    {
        byte[] patron = [0x19, 0x18, 0xE1, 0x91, 0x8E];

        var bits = new byte[Totp.BytesDelSecreto];
        for (int i = 0; i < bits.Length; i++) bits[i] = patron[i % patron.Length];
        return bits;
    }

    /// <summary>La ventana de treinta segundos en la que cae este instante, contada aquí.</summary>
    private static long VentanaDeAhora() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

    /// <summary>
    /// El código de seis dígitos de un secreto y una ventana, calculado desde cero: HMAC-SHA1 del
    /// contador en ocho bytes con el más significativo primero, truncamiento dinámico del RFC 4226 y
    /// los seis dígitos de la derecha. Que esto esté bien lo prueba
    /// <see cref="ElCalculoDeEstasPruebas_CoincideConLosVectoresDelRfc6238"/>: una segunda
    /// implementación sin comprobar no vale más que la primera.
    /// </summary>
    private static string CodigoPorFuera(byte[] secreto, long ventana)
    {
        var contador = BitConverter.GetBytes(ventana);
        if (BitConverter.IsLittleEndian) Array.Reverse(contador);

        var firma = HMACSHA1.HashData(secreto, contador);

        int desde = firma[^1] & 0x0F;
        int numero = ((firma[desde] & 0x7F) << 24)
                   | (firma[desde + 1] << 16)
                   | (firma[desde + 2] << 8)
                   | firma[desde + 3];

        return (numero % 1_000_000).ToString("D6");
    }

    /// <summary>El código que mostraría ahora mismo el teléfono de quien tecleó el secreto anunciado.</summary>
    private static string CodigoDelSecretoAnunciado() =>
        CodigoPorFuera(BitsDelSecretoAnunciado(), VentanaDeAhora());

    /// <summary>
    /// Un código de seis dígitos que con seguridad NO vale para el secreto anunciado.
    ///
    /// <para>Se descartan las TRES ventanas que el servicio acepta —la de ahora y una a cada lado por
    /// la tolerancia de reloj—, no solo la de ahora: mirar una sola dejaría una prueba que falla sola
    /// muy de vez en cuando, que cuesta más que estas cuatro líneas.</para>
    /// </summary>
    private static string CodigoQueNoSaleDeEseSecreto()
    {
        var secreto = BitsDelSecretoAnunciado();
        long ahora = VentanaDeAhora();

        var validos = new[]
        {
            CodigoPorFuera(secreto, ahora - 1), CodigoPorFuera(secreto, ahora), CodigoPorFuera(secreto, ahora + 1)
        };

        for (int i = 0; ; i++)
        {
            var candidato = i.ToString("D6");
            if (!validos.Contains(candidato)) return candidato;
        }
    }

    /// <summary>El servicio de verdad, el mismo que atiende la pantalla de acceso.</summary>
    private static SegundoFactorService Svc(AppDbContext db, IProtectorDeSecretos protector)
    {
        var nadie = UsuarioDePrueba.Anonimo();   // al verificar todavía no hay sesión: así entra la gente
        return new SegundoFactorService(db, nadie, protector, new DibujanteDeCodigoQrCoder(),
                                        new AuditService(db, nadie, new OrigenDePrueba()));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  LA SIEMBRA DEJA EL SEGUNDO FACTOR DADO DE ALTA
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LaSiembra_DejaLasSeisCuentasConElSegundoFactorYaActivo()
    {
        var db = TestDb.New();
        var protector = new ProtectorSimulado();

        var resumen = await SembrarComoElArranqueAsync(db, protector);

        Assert.NotNull(resumen);

        var cuentas = await db.Users.AsNoTracking().ToListAsync();
        Assert.Equal(CuentasDeDemostracion.Length, cuentas.Count);

        foreach (var cuenta in cuentas)
        {
            Assert.Contains(cuenta.Username, CuentasDeDemostracion);
            Assert.True(cuenta.SegundoFactorActivo);
            Assert.NotNull(cuenta.SegundoFactorDesdeUtc);

            // La ventana en nulo es la ANTIRREPETICIÓN sin estrenar. Si la siembra anotara la de
            // ahora, el primer código que teclee quien abra la demostración se rechazaría por «ya
            // se usó», que es la peor forma posible de recibir a alguien.
            Assert.Null(cuenta.SegundoFactorUltimaVentana);
        }
    }

    [Fact]
    public async Task ElSecretoQueSeGuarda_EsElQueElArranqueAnuncia_YVaProtegido()
    {
        // Si lo guardado no fuera lo anunciado, el registro estaría dictando un secreto que no abre
        // nada y la demostración quedaría igual de inservible que antes, pero además mintiendo.
        var db = TestDb.New();
        var protector = new ProtectorSimulado();

        await SembrarComoElArranqueAsync(db, protector);

        var secretos = await db.UserSecrets.AsNoTracking().ToListAsync();
        Assert.Equal(CuentasDeDemostracion.Length, secretos.Count);

        foreach (var fila in secretos)
        {
            // Bajo el propósito definitivo, que es el ÚNICO que se mira al entrar. Sembrarlo como
            // pendiente dejaría las cuentas marcadas como activas y sin nada contra qué comparar.
            Assert.Equal(PropositosDeSecreto.SegundoFactor, fila.Proposito);
            Assert.Equal(DatosDeDemostracion.SecretoDelSegundoFactor, protector.Desproteger(fila.CipherText));
        }

        // Y lo que se escribe es lo que devolvió el protector, no el secreto crudo. Guardarlo tal cual
        // «porque total, es de demostración» dejaría la siembra escribiendo en esa columna algo que el
        // resto del sistema da por cifrado.
        Assert.DoesNotContain(secretos, f => f.CipherText == DatosDeDemostracion.SecretoDelSegundoFactor);
    }

    [Fact]
    public async Task LaSiembra_DejaTambienLosOchoCodigosDeRescateDeCadaCuenta()
    {
        // Como los dejaría un alta de verdad. Sin ellos las pantallas de seguridad avisarían en rojo
        // «no te queda ningún código de rescate» para las seis cuentas, que es una alarma cierta
        // describiendo algo que en esta base no ha pasado.
        var db = TestDb.New();

        await SembrarComoElArranqueAsync(db, new ProtectorSimulado());

        var codigos = await db.UserRecoveryCodes.AsNoTracking().ToListAsync();

        Assert.Equal(CuentasDeDemostracion.Length * CodigosDeRescate.Cuantos, codigos.Count);
        Assert.All(codigos, c => Assert.Null(c.UsadoEnUtc));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  Y EL CÓDIGO DE ESE SECRETO ENTRA DE VERDAD
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    // Los vectores del apéndice B del RFC 6238, recortados a los seis dígitos que muestra el
    // teléfono. Sin esto, el cálculo de aquí abajo sería una segunda implementación tan sin
    // comprobar como la primera, y dos implementaciones equivocadas en verde no valen más que una.
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    public void ElCalculoDeEstasPruebas_CoincideConLosVectoresDelRfc6238(long segundosUnix, string esperado)
    {
        Assert.Equal(esperado, CodigoPorFuera(Encoding.ASCII.GetBytes("12345678901234567890"), segundosUnix / 30));
    }

    [Fact]
    public void ElSecretoAnunciado_SonLosBitsQueLeeraLaAplicacionDelTelefono()
    {
        // Lo que se publica es un TEXTO, y lo que el teléfono usa son los bits que ese texto significa
        // en Base32. Aquí se comparan los bits deletreados a mano con los que lee la aplicación: si el
        // día de mañana alguien retoca el texto del secreto —una letra de más, un carácter que no es
        // del alfabeto— esto se pone en rojo antes de que la guía prometa un secreto que no abre nada.
        Assert.Equal(BitsDelSecretoAnunciado(), Base32.Decodificar(DatosDeDemostracion.SecretoDelSegundoFactor));

        // Y son los 160 bits exactos que espera el cálculo, sin relleno que sobre ni que falte.
        Assert.Equal(Totp.BytesDelSecreto, BitsDelSecretoAnunciado().Length);
    }

    [Fact]
    public async Task ElCodigoDelSecretoAnunciado_LoAceptaElServicioDeSegundoFactor()
    {
        // Es la prueba que de verdad importa de este lado. Comprobar que lo sembrado y lo anunciado
        // coinciden solo demuestra que dos mitades del mismo cambio dicen lo mismo; lo que hace falta
        // saber es si la APLICACIÓN abre la puerta con el código que sacaría una aplicación de códigos
        // cualquiera, que es lo único que va a hacer quien levante la demostración. Por eso el código
        // viene de CodigoPorFuera —RFC a mano, sin Base32 ni Totp— y se le da al servicio de verdad,
        // el mismo que atiende la pantalla de acceso.
        var db = TestDb.New();
        var protector = new ProtectorSimulado();
        await SembrarComoElArranqueAsync(db, protector);

        var servicio = Svc(db, protector);
        var codigo = CodigoDelSecretoAnunciado();

        // El MISMO código sirve para las seis: el secreto es uno solo, y la antirrepetición se lleva
        // por cuenta, así que gastarlo en una no se lo quita a las demás.
        foreach (var cuenta in await db.Users.AsNoTracking().ToListAsync())
        {
            var resultado = await servicio.VerificarAsync(cuenta.Id, codigo);

            Assert.True(resultado.Exito, $"«{cuenta.Username}» no aceptó el código: {resultado.Mensaje}");
            Assert.False(resultado.FueCodigoDeRescate);
        }
    }

    [Fact]
    public async Task UnCodigoQueNoSaleDeEseSecreto_SigueSinEntrar()
    {
        // El acompañamiento de la anterior: sin esto, una prueba que acepta cualquier cosa pasaría
        // igual de verde y no estaría probando nada.
        var db = TestDb.New();
        var protector = new ProtectorSimulado();
        await SembrarComoElArranqueAsync(db, protector);

        var lider = await db.Users.AsNoTracking().FirstAsync(u => u.Username == "lider");

        var resultado = await Svc(db, protector).VerificarAsync(lider.Id, CodigoQueNoSaleDeEseSecreto());

        Assert.False(resultado.Exito);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  IDEMPOTENCIA
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SembrarDosVeces_NoDuplicaNadaNiCambiaElSegundoFactor()
    {
        // La segunda pasada no es un caso raro: es lo que ocurre en CADA arranque a partir del
        // segundo. Tiene que no hacer nada, y sobre todo no volver a escribir secretos ni códigos de
        // rescate encima de los que ya funcionaban.
        var db = TestDb.New();
        var protector = new ProtectorSimulado();

        Assert.NotNull(await SembrarComoElArranqueAsync(db, protector));

        var antes = await ContarTodoAsync(db);
        var desdeAntes = (await db.Users.AsNoTracking().FirstAsync(u => u.Username == "lider")).SegundoFactorDesdeUtc;

        var segunda = await SembrarComoElArranqueAsync(db, protector);

        Assert.Null(segunda);   // ni siquiera empieza: la base ya no está virgen
        Assert.Equal(antes, await ContarTodoAsync(db));

        var lider = await db.Users.AsNoTracking().FirstAsync(u => u.Username == "lider");
        Assert.Equal(desdeAntes, lider.SegundoFactorDesdeUtc);

        // Y lo que ya funcionaba sigue funcionando, que es lo que la cuenta de filas no dice.
        Assert.True((await Svc(db, protector).VerificarAsync(lider.Id, CodigoDelSecretoAnunciado())).Exito);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  LA BARRERA: ESTO NO PUEDE ACTIVARSE CONTRA UNA BASE DE VERDAD
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnProduction_NoSeSiembraNadaYElSecretoNoLlegaANingunaCuenta()
    {
        // La guarda que importa de las tres, ahora que la siembra reparte segundos factores conocidos.
        // Se pide a propósito —pedido: true— porque el caso peligroso no es que nadie lo pida, sino
        // que la configuración se copie por error a donde no debe.
        var db = TestDb.New();
        var protector = new ProtectorSimulado();

        var resumen = await DatosDeDemostracion.SembrarSiSePuedeAsync(
            db, protector, pedido: true, esProduccion: true);

        Assert.Null(resumen);
        await NadaDeLaDemostracionEnLaBaseAsync(db);
    }

    [Fact]
    public async Task SinPedirlo_NoSeSiembraNadaAunqueElEntornoLoPermita()
    {
        var db = TestDb.New();
        var protector = new ProtectorSimulado();

        var resumen = await DatosDeDemostracion.SembrarSiSePuedeAsync(
            db, protector, pedido: false, esProduccion: false);

        Assert.Null(resumen);
        await NadaDeLaDemostracionEnLaBaseAsync(db);
    }

    [Fact]
    public async Task ContraUnaBaseQueYaTieneGente_NoSeSiembraNada()
    {
        // La tercera guarda, y la que de verdad protege: producción tiene desarrolladores desde el
        // primer día, así que aunque las otras dos fallaran, aquí se para.
        var db = TestDb.New();
        var protector = new ProtectorSimulado();

        db.Developers.Add(new Developer { FullName = "Alguien de verdad", Email = "alguien@soltum.mx" });
        await db.SaveChangesAsync();

        var resumen = await DatosDeDemostracion.SembrarSiSePuedeAsync(
            db, protector, pedido: true, esProduccion: false);

        Assert.Null(resumen);
        Assert.Empty(await db.Users.AsNoTracking().ToListAsync());
        Assert.Empty(await db.UserSecrets.AsNoTracking().ToListAsync());
    }

    [Fact]
    public void NoHayNingunaPuertaPublicaParaSembrarSinPreguntarAntes()
    {
        // Esta prueba es la barrera dicha en código, y no en un comentario que promete disciplina: el
        // día que alguien añada otro método público que siembre, se pone en rojo antes de que llegue
        // a ninguna parte.
        //
        // CUIDADO CON LO QUE ESTO GARANTIZA Y LO QUE NO, porque la diferencia es justo la que
        // importa. Garantiza que no se puede sembrar sin pasar por la puerta que pregunta. NO
        // garantiza que la respuesta sea cierta: de las tres guardas, «se pidió» y «no es Production»
        // son valores que ENTREGA quien llama, así que un llamador nuevo que pasara
        // «esProduccion: false» estando en producción las saltaría sin que esta prueba se enterara.
        // La única que no depende de nadie de fuera es la tercera —la base sin un solo
        // desarrollador—, y por eso sigue siendo la que de verdad protege. Hoy hay UN llamador,
        // PreparacionDeLaBase, y saca las dos del entorno real; cualquier segundo llamador tiene que
        // hacer lo mismo, y eso hay que revisarlo a mano al leerlo.
        var puertas = typeof(DatosDeDemostracion)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.Name.StartsWith("Sembrar", StringComparison.Ordinal))
            .Select(m => m.Name)
            .ToList();

        Assert.Equal([nameof(DatosDeDemostracion.SembrarSiSePuedeAsync)], puertas);
    }

    [Fact]
    public async Task LaCuentaAdminDelArranque_NoRecibeElSegundoFactorDeLaDemostracion()
    {
        // El arranque siembra «admin» ANTES de llegar a la demostración, así que convive con estas
        // seis en la misma base. El secreto fijo cuelga del sembrador de demostración y de nada más:
        // si se le pegara a la cuenta que se usa de verdad, la comodidad se habría convertido en el
        // agujero. «admin» sale de aquí como entró, obligada a dar de alta su propio teléfono.
        var db = TestDb.New();
        var protector = new ProtectorSimulado();

        var admin = new User
        {
            Username = "admin", FullName = "Administrador", PasswordHash = "x",
            Role = UserRole.Admin, IsActive = true, MustChangePassword = true
        };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        Assert.NotNull(await SembrarComoElArranqueAsync(db, protector));

        var despues = await db.Users.AsNoTracking().FirstAsync(u => u.Username == "admin");
        Assert.False(despues.SegundoFactorActivo);
        Assert.Null(despues.SegundoFactorDesdeUtc);
        Assert.Empty(await db.UserSecrets.AsNoTracking().Where(s => s.UserId == admin.Id).ToListAsync());
        Assert.Empty(await db.UserRecoveryCodes.AsNoTracking().Where(c => c.UserId == admin.Id).ToListAsync());
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  Piezas de las pruebas
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Lo que tiene que quedar igual entre dos siembras: las cuentas y todo lo que cuelga de ellas.</summary>
    private static async Task<(int usuarios, int desarrolladores, int secretos, int rescates, int requerimientos, int actividades)>
        ContarTodoAsync(AppDbContext db) =>
        (await db.Users.CountAsync(),
         await db.Developers.CountAsync(),
         await db.UserSecrets.CountAsync(),
         await db.UserRecoveryCodes.CountAsync(),
         await db.Requirements.CountAsync(),
         await db.PoolActivities.CountAsync());

    /// <summary>Ni una cuenta, ni un secreto, ni un código de rescate: la siembra no llegó a empezar.</summary>
    private static async Task NadaDeLaDemostracionEnLaBaseAsync(AppDbContext db)
    {
        Assert.Empty(await db.Users.AsNoTracking().ToListAsync());
        Assert.Empty(await db.Developers.AsNoTracking().ToListAsync());
        Assert.Empty(await db.UserSecrets.AsNoTracking().ToListAsync());
        Assert.Empty(await db.UserRecoveryCodes.AsNoTracking().ToListAsync());
    }
}
