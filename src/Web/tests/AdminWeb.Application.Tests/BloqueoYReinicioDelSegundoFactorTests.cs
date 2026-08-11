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
/// Las tres costuras donde el segundo factor deja de proteger si nadie mira: el bloqueo por intentos
/// al probar códigos, el bloqueo al emitir códigos de rescate nuevos, y lo que el reinicio del líder
/// le hace —o no— a la sesión que ya estaba abierta.
///
/// <para><b>Las tres tienen la misma forma de fallar.</b> Todo compila, todas las pantallas se ven
/// bien y el segundo factor «funciona»: pide el código, lo acepta cuando es bueno y lo rechaza cuando
/// no. Lo que se pierde en silencio es el LÍMITE —cuántas veces se puede probar— y el ALCANCE —a
/// quién alcanza un reinicio—, y ninguna de las dos cosas se nota usando la aplicación. Por eso se
/// prueban aquí y no en una pantalla.</para>
/// </summary>
public class BloqueoYReinicioDelSegundoFactorTests
{
    // ── Andamiaje propio, deliberadamente duplicado ──────────────────────────────
    //
    // Estas piezas también existen en SegundoFactorServiceTests. No se comparten a propósito: son
    // cuatro líneas cada una, y compartirlas obligaría a tocar un archivo del que dependen decenas de
    // pruebas ajenas cada vez que a una de estas le hiciera falta algo distinto.

    private sealed class ProtectorFalso : IProtectorDeSecretos
    {
        private const string Marca = "cifrado:";
        public string Proteger(string valorEnClaro) => Marca + valorEnClaro;
        public string? Desproteger(string cifrado) =>
            cifrado.StartsWith(Marca) ? cifrado[Marca.Length..] : null;
    }

    private sealed class DibujanteFalso : IDibujanteDeCodigoQr
    {
        public byte[] DibujarPng(string contenido) => [1, 2, 3];
    }

    private static SegundoFactorService Svc(AppDbContext db, ICurrentUser usuario) =>
        new(db, usuario, new ProtectorFalso(), new DibujanteFalso(),
            new AuditService(db, usuario, new OrigenDePrueba()));

    private static UsuarioDePrueba Yo(int userId = 1, UserRole rol = UserRole.Desarrollador) =>
        UsuarioDePrueba.Como(rol, userId: userId);

    private const string Contrasena = "Contrasena123";

    private static User CrearUsuario(AppDbContext db, int id = 1, string nombre = "ana",
                                     UserRole rol = UserRole.Desarrollador)
    {
        var u = new User
        {
            Id = id,
            Username = nombre,
            FullName = nombre,
            PasswordHash = PasswordHasher.Hash(Contrasena),
            IsActive = true,
            Role = rol
        };
        db.Users.Add(u);
        db.SaveChanges();
        return u;
    }

    private static string CodigoDe(string secretoEnBase32, long desplazamientoDeVentanas = 0) =>
        Totp.Calcular(Base32.Decodificar(secretoEnBase32)!,
                      Totp.VentanaDe(DateTimeOffset.UtcNow) + desplazamientoDeVentanas);

    /// <summary>Seis dígitos que con seguridad NO valen para este secreto en ninguna de las tres ventanas.</summary>
    private static string CodigoInvalidoPara(string secretoEnBase32)
    {
        var validos = new[] { CodigoDe(secretoEnBase32, -1), CodigoDe(secretoEnBase32), CodigoDe(secretoEnBase32, 1) };
        for (int i = 0; ; i++)
        {
            var candidato = i.ToString("D6");
            if (!validos.Contains(candidato)) return candidato;
        }
    }

    private static async Task<string> DarDeAltaAsync(AppDbContext db, int userId = 1)
    {
        var svc = Svc(db, Yo(userId));
        var (_, _, alta) = await svc.ComenzarAltaAsync();
        var (ok, _, _) = await svc.ConfirmarAltaAsync(CodigoDe(alta!.SecretoEnBase32));
        Assert.True(ok);
        return alta.SecretoEnBase32;
    }

    private static Task<User> ReleerAsync(AppDbContext db, int id = 1) =>
        db.Users.AsNoTracking().FirstAsync(u => u.Id == id);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  EL LÍMITE DE INTENTOS CONTRA EL CÓDIGO
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Acertar la contraseña NO puede poner a cero el contador de códigos fallidos.
    ///
    /// <para><b>Es la prueba que sostiene el segundo factor entero.</b> El atacante contra el que el
    /// segundo factor existe es exactamente quien YA tiene la contraseña; si acertarla le devolviera
    /// el contador a cero, le bastaría con intercalar un acceso cada cuatro intentos para probar los
    /// seis dígitos sin límite: el quinto fallo no llegaría nunca y el bloqueo no saltaría jamás.
    /// Un millón de combinaciones sin freno son una tarde de trabajo, no una barrera.</para>
    ///
    /// <para>Se comprueba con el ciclo entero —acceso, fallos, acceso— y no mirando el contador tras
    /// un solo acceso, porque lo que hay que impedir es el ciclo, no una asignación concreta.</para>
    /// </summary>
    [Fact]
    public async Task AcertarLaContrasena_NoDesarmaElBloqueoDelCodigo()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        var secreto = await DarDeAltaAsync(db);

        var auth = Fabrica.Auth(db);
        var svc = Svc(db, Yo());

        // Cuatro fallos, un acceso correcto por contraseña, y otro fallo. Si el acceso limpiara el
        // contador, este quinto fallo sería «el primero» y la cuenta seguiría abierta.
        for (int i = 0; i < 4; i++)
            Assert.False((await svc.VerificarAsync(1, CodigoInvalidoPara(secreto))).Exito);

        Assert.True((await auth.LoginAsync("ana", Contrasena)).Exito);

        Assert.Equal(4, (await ReleerAsync(db)).FailedLoginCount);

        Assert.False((await svc.VerificarAsync(1, CodigoInvalidoPara(secreto))).Exito);
        Assert.True(AuthService.EstaBloqueado(await ReleerAsync(db)));
    }

    /// <summary>
    /// Y un acceso correcto tampoco LEVANTA un bloqueo que ya saltó.
    ///
    /// <para>Era la otra mitad del mismo agujero: el bloqueo se ponía al quinto código malo y la
    /// siguiente contraseña correcta lo borraba, así que ni siquiera había que contar intentos para
    /// esquivarlo — bastaba con volver a entrar.</para>
    /// </summary>
    [Fact]
    public async Task AcertarLaContrasena_NoLevantaUnBloqueoQueYaSalto()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        var secreto = await DarDeAltaAsync(db);
        var svc = Svc(db, Yo());

        for (int i = 0; i < AuthService.MaxFailedAttempts; i++)
            await svc.VerificarAsync(1, CodigoInvalidoPara(secreto));

        Assert.True(AuthService.EstaBloqueado(await ReleerAsync(db)));

        // Con la cuenta bloqueada, ni siquiera el primer tramo deja pasar.
        var acceso = await Fabrica.Auth(db).LoginAsync("ana", Contrasena);
        Assert.False(acceso.Exito);
        Assert.True(AuthService.EstaBloqueado(await ReleerAsync(db)));
    }

    /// <summary>
    /// A quien NO tiene segundo factor, acertar la contraseña sí le limpia los intentos: para esa
    /// cuenta la contraseña es el acceso completo, y arrastrar los fallos de ayer la bloquearía al
    /// primer despiste de hoy. Es el comportamiento de siempre y no debe cambiar.
    /// </summary>
    [Fact]
    public async Task SinSegundoFactor_ElAccesoCorrectoSigueLimpiandoLosIntentos()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        var auth = Fabrica.Auth(db);

        await auth.LoginAsync("ana", "mal");
        await auth.LoginAsync("ana", "mal");
        Assert.Equal(2, (await ReleerAsync(db)).FailedLoginCount);

        Assert.True((await auth.LoginAsync("ana", Contrasena)).Exito);
        Assert.Equal(0, (await ReleerAsync(db)).FailedLoginCount);
    }

    /// <summary>
    /// Y el camino del equipo recordado —el único acceso completo que no pasa por el servicio del
    /// segundo factor— sí tiene que limpiarlos.
    ///
    /// <para>Sin esto, la corrección de arriba tendría un efecto secundario feo y difícil de
    /// relacionar con su causa: quien entra siempre desde un equipo recordado nunca limpiaría su
    /// contador, y cinco despistes repartidos a lo largo de meses acabarían bloqueándole la cuenta.</para>
    /// </summary>
    [Fact]
    public async Task TrasEntrarPorEquipoRecordado_LosIntentosVuelvenACero()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        var secreto = await DarDeAltaAsync(db);
        var svc = Svc(db, Yo());

        await svc.VerificarAsync(1, CodigoInvalidoPara(secreto));
        Assert.Equal(1, (await ReleerAsync(db)).FailedLoginCount);

        await Fabrica.Auth(db).LimpiarBloqueoTrasAccesoCompletoAsync(1);

        var releido = await ReleerAsync(db);
        Assert.Equal(0, releido.FailedLoginCount);
        Assert.Null(releido.LockoutUntil);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  EL LÍMITE DE INTENTOS AL EMITIR CÓDIGOS DE RESCATE NUEVOS
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Emitir códigos de rescate nuevos exige un código del teléfono, y ese código se puede fallar un
    /// número limitado de veces.
    ///
    /// <para><b>Sin límite, la exigencia era decorativa.</b> Quien se hiciera con una sesión abierta
    /// podía probar el millón de códigos posibles desde esa misma sesión hasta acertar, y llevarse
    /// ocho llaves permanentes de la cuenta sin haber tenido nunca el teléfono delante — que es
    /// exactamente lo que esta puerta dice impedir.</para>
    /// </summary>
    [Fact]
    public async Task EmitirCodigosNuevos_TambienSeQuedaSinIntentos()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        var secreto = await DarDeAltaAsync(db);
        var svc = Svc(db, Yo());

        for (int i = 0; i < AuthService.MaxFailedAttempts; i++)
        {
            var (ok, _, _) = await svc.RegenerarCodigosDeRescateAsync(CodigoInvalidoPara(secreto));
            Assert.False(ok);
        }

        Assert.True(AuthService.EstaBloqueado(await ReleerAsync(db)));

        // Y con la cuenta bloqueada no vale ni el código bueno: si valiera, el bloqueo solo estaría
        // estorbando a quien se equivoca y no frenando a quien prueba.
        var (conElBueno, mensaje, codigos) = await svc.RegenerarCodigosDeRescateAsync(CodigoDe(secreto, 2));
        Assert.False(conElBueno);
        Assert.Null(codigos);
        Assert.Contains("bloqueada", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Y acertar deja el contador a cero, igual que al entrar: quien demuestra tener el teléfono no
    /// arrastra los errores de dedo de antes.
    /// </summary>
    [Fact]
    public async Task EmitirCodigosNuevosConElCodigoBueno_LimpiaLosFallosPrevios()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        var secreto = await DarDeAltaAsync(db);
        var svc = Svc(db, Yo());

        await svc.RegenerarCodigosDeRescateAsync(CodigoInvalidoPara(secreto));
        Assert.Equal(1, (await ReleerAsync(db)).FailedLoginCount);

        // Una ventana más allá: el alta acaba de gastar la de ahora y repetirla se rechazaría por
        // «ya se usó», que no es lo que esta prueba mide.
        var (ok, _, codigos) = await svc.RegenerarCodigosDeRescateAsync(CodigoDe(secreto, 1));

        Assert.True(ok);
        Assert.Equal(CodigosDeRescate.Cuantos, codigos!.Count);
        Assert.Equal(0, (await ReleerAsync(db)).FailedLoginCount);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  EL ALCANCE DEL REINICIO DEL LÍDER
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// El reinicio del líder tiene que ECHAR FUERA a las sesiones abiertas de esa cuenta, y eso es
    /// rotar su sello de seguridad.
    ///
    /// <para><b>Sin rotarlo, el reinicio no reinicia nada de lo que importa</b>, por dos motivos que
    /// se suman. Uno: la cookie de quien ya estaba dentro se emitió cuando su segundo factor estaba
    /// activo, así que no lleva el aviso de «te falta activarlo» y el corte de peticiones no le
    /// exigiría nada — la cuenta seguiría funcionando durante horas SIN ningún segundo factor, que es
    /// justo lo que la regla de «obligatorio para todos» prohíbe. Dos: si el reinicio se pidió porque
    /// alguien se quedó con el teléfono o con la sesión, dejarle la sesión viva lo convierte en un
    /// trámite decorativo.</para>
    /// </summary>
    [Fact]
    public async Task ElReinicioDelLider_EchaFueraALasSesionesAbiertas()
    {
        var db = TestDb.New();
        CrearUsuario(db);                                   // la víctima
        CrearUsuario(db, 2, "lider", UserRole.Admin);
        await DarDeAltaAsync(db);

        var selloAntes = (await ReleerAsync(db)).SecurityStamp;

        var (ok, _) = await Svc(db, Yo(2, UserRole.Admin)).ReiniciarAsync(1);
        Assert.True(ok);

        var despues = await ReleerAsync(db);
        Assert.NotEqual(selloAntes, despues.SecurityStamp);
        Assert.False(despues.SegundoFactorActivo);
    }

    /// <summary>
    /// Y el sello del LÍDER no se toca: reiniciarle el segundo factor a alguien no puede tirar la
    /// sesión de quien lo está haciendo, o el líder se echaría a sí mismo cada vez que ayuda a un
    /// compañero.
    /// </summary>
    [Fact]
    public async Task ElReinicio_NoTocaLaSesionDeQuienLoHace()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        CrearUsuario(db, 2, "lider", UserRole.Admin);
        await DarDeAltaAsync(db);

        var selloDelLider = (await ReleerAsync(db, 2)).SecurityStamp;

        await Svc(db, Yo(2, UserRole.Admin)).ReiniciarAsync(1);

        Assert.Equal(selloDelLider, (await ReleerAsync(db, 2)).SecurityStamp);
    }
}
