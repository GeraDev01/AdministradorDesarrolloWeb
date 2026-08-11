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
/// El segundo factor de punta a punta: dar de alta, entrar, rescatar y reiniciar.
///
/// <para><b>Lo que estas pruebas cuidan por encima de todo es EL ORDEN DEL ALTA.</b> Enseñar el
/// código QR y dar la cuenta por protegida en ese momento es el error que deja a alguien fuera de su
/// propia cuenta sin que nadie se entere: si el escaneo salió mal, la aplicación del teléfono no
/// avisa de nada, sigue mostrando códigos de seis dígitos que simplemente no son los buenos. Varias
/// de las pruebas de aquí abajo no comprueban que algo funcione, sino que algo <b>no pueda
/// ocurrir</b>.</para>
/// </summary>
public class SegundoFactorServiceTests
{
    /// <summary>
    /// Un protector que marca en vez de cifrar. Deja comprobar que el secreto se guarda protegido y
    /// se recupera, sin arrastrar la protección de datos de ASP.NET, que vive en la API.
    /// </summary>
    private sealed class ProtectorFalso : IProtectorDeSecretos
    {
        private const string Marca = "cifrado:";
        public string Proteger(string valorEnClaro) => Marca + valorEnClaro;
        public string? Desproteger(string cifrado) =>
            cifrado.StartsWith(Marca) ? cifrado[Marca.Length..] : null;
    }

    /// <summary>
    /// Un protector que NO puede descifrar nada. Reproduce el día en que se pierde el llavero:
    /// la fila del secreto sigue en la base y ya no dice nada.
    /// </summary>
    private sealed class ProtectorIlegible : IProtectorDeSecretos
    {
        public string Proteger(string valorEnClaro) => "ilegible";
        public string? Desproteger(string cifrado) => null;
    }

    /// <summary>Un dibujante que no dibuja: al servicio le da igual, y por eso hay una interfaz.</summary>
    private sealed class DibujanteFalso : IDibujanteDeCodigoQr
    {
        public byte[] DibujarPng(string contenido) => [1, 2, 3];
    }

    private static SegundoFactorService Svc(AppDbContext db, ICurrentUser usuario, IProtectorDeSecretos? protector = null) =>
        new(db, usuario, protector ?? new ProtectorFalso(), new DibujanteFalso(),
            new AuditService(db, usuario, new OrigenDePrueba()));

    private static User CrearUsuario(AppDbContext db, int id = 1, string nombre = "ana", UserRole rol = UserRole.Desarrollador)
    {
        var u = new User { Id = id, Username = nombre, FullName = nombre, PasswordHash = "x", IsActive = true, Role = rol };
        db.Users.Add(u);
        db.SaveChanges();
        return u;
    }

    private static UsuarioDePrueba Yo(int userId = 1, UserRole rol = UserRole.Desarrollador) =>
        UsuarioDePrueba.Como(rol, userId: userId);

    /// <summary>El código que muestra el teléfono ahora mismo, o el de N ventanas más allá.</summary>
    private static string CodigoDe(string secretoEnBase32, long desplazamientoDeVentanas = 0) =>
        Totp.Calcular(Base32.Decodificar(secretoEnBase32)!,
                      Totp.VentanaDe(DateTimeOffset.UtcNow) + desplazamientoDeVentanas);

    /// <summary>
    /// Un código de seis dígitos que con seguridad NO es válido para este secreto.
    ///
    /// Se busca en vez de escribir «000000» a mano porque uno de cada 333 000 intentos ese literal
    /// sería el código bueno, y una prueba que falla una vez cada tanto sin motivo aparente cuesta
    /// más que estas cuatro líneas.
    /// </summary>
    private static string CodigoInvalidoPara(string secretoEnBase32)
    {
        var validos = new[] { CodigoDe(secretoEnBase32, -1), CodigoDe(secretoEnBase32), CodigoDe(secretoEnBase32, 1) };
        for (int i = 0; ; i++)
        {
            var candidato = i.ToString("D6");
            if (!validos.Contains(candidato)) return candidato;
        }
    }

    /// <summary>Deja una cuenta con el segundo factor ya activo y devuelve su secreto y sus códigos.</summary>
    private static async Task<(string secreto, IReadOnlyList<string> rescate)> DarDeAltaAsync(
        AppDbContext db, int userId = 1)
    {
        var svc = Svc(db, Yo(userId));
        var (_, _, alta) = await svc.ComenzarAltaAsync();
        var (ok, _, codigos) = await svc.ConfirmarAltaAsync(CodigoDe(alta!.SecretoEnBase32));

        Assert.True(ok);
        return (alta.SecretoEnBase32, codigos!);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  EL ORDEN DEL ALTA — lo que no puede ocurrir
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ComenzarElAlta_NoDejaLaCuentaProtegidaNiAMedias()
    {
        // Enseñar el código QR no activa nada. Si lo activara y el escaneo hubiera salido mal, esta
        // persona se quedaría fuera de su cuenta sin que ella ni nadie lo supiera.
        var db = TestDb.New();
        CrearUsuario(db);

        var (ok, _, alta) = await Svc(db, Yo()).ComenzarAltaAsync();

        Assert.True(ok);
        Assert.NotNull(alta);

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == 1);
        Assert.False(user.SegundoFactorActivo);
        Assert.Null(user.SegundoFactorDesdeUtc);

        // Y el secreto definitivo —el único que se mira al entrar— todavía no existe.
        Assert.False(await db.UserSecrets.AnyAsync(s => s.Proposito == PropositosDeSecreto.SegundoFactor));
        Assert.True(await db.UserSecrets.AnyAsync(s => s.Proposito == PropositosDeSecreto.SegundoFactorPendiente));
    }

    [Fact]
    public async Task ConfirmarConUnCodigoQueNoCoincide_DejaLaCuentaComoEstaba()
    {
        // Es el caso del escaneo fallido: el teléfono muestra códigos, pero de otro secreto.
        var db = TestDb.New();
        CrearUsuario(db);
        var svc = Svc(db, Yo());
        var (_, _, alta) = await svc.ComenzarAltaAsync();

        var (ok, mensaje, codigos) = await svc.ConfirmarAltaAsync(CodigoInvalidoPara(alta!.SecretoEnBase32));

        Assert.False(ok);
        Assert.Null(codigos);
        Assert.Contains("no es correcto", mensaje);

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == 1);
        Assert.False(user.SegundoFactorActivo);
        Assert.False(await db.UserSecrets.AnyAsync(s => s.Proposito == PropositosDeSecreto.SegundoFactor));
    }

    [Fact]
    public async Task ConfirmarSinHaberPedidoElCodigoQr_NoActivaNada()
    {
        // No hay forma de saltarse el paso 1: sin alta en curso no hay secreto contra el que probar.
        var db = TestDb.New();
        CrearUsuario(db);

        var (ok, mensaje, _) = await Svc(db, Yo()).ConfirmarAltaAsync("123456");

        Assert.False(ok);
        Assert.Contains("Vuelve a pedir el código QR", mensaje);
        Assert.False((await db.Users.AsNoTracking().FirstAsync(u => u.Id == 1)).SegundoFactorActivo);
    }

    [Fact]
    public async Task ConfirmarConElCodigoBueno_ActivaYPromueveElSecreto()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        var svc = Svc(db, Yo());
        var (_, _, alta) = await svc.ComenzarAltaAsync();

        var (ok, _, codigos) = await svc.ConfirmarAltaAsync(CodigoDe(alta!.SecretoEnBase32));

        Assert.True(ok);

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == 1);
        Assert.True(user.SegundoFactorActivo);
        Assert.NotNull(user.SegundoFactorDesdeUtc);
        Assert.NotNull(user.SegundoFactorUltimaVentana);   // la antirrepetición arranca ya armada

        // El pendiente desaparece y el definitivo ocupa su lugar: no quedan dos secretos vivos.
        Assert.False(await db.UserSecrets.AnyAsync(s => s.Proposito == PropositosDeSecreto.SegundoFactorPendiente));
        Assert.True(await db.UserSecrets.AnyAsync(s => s.Proposito == PropositosDeSecreto.SegundoFactor));

        Assert.Equal(CodigosDeRescate.Cuantos, codigos!.Count);
    }

    [Fact]
    public async Task VolverAPedirElCodigoQr_CambiaElSecretoYElViejoDejaDeValer()
    {
        // Quien vuelve a esta pantalla es porque el primer intento no le sirvió. Si el secreto viejo
        // siguiera valiendo, se podría confirmar con uno que ya no es el que está en pantalla.
        var db = TestDb.New();
        CrearUsuario(db);
        var svc = Svc(db, Yo());

        var (_, _, primera) = await svc.ComenzarAltaAsync();
        var (_, _, segunda) = await svc.ComenzarAltaAsync();

        Assert.NotEqual(primera!.SecretoEnBase32, segunda!.SecretoEnBase32);

        var (ok, _, _) = await svc.ConfirmarAltaAsync(CodigoDe(primera.SecretoEnBase32));
        Assert.False(ok);

        // Y solo hay UNA fila pendiente, no una por intento.
        Assert.Equal(1, await db.UserSecrets.CountAsync(s => s.Proposito == PropositosDeSecreto.SegundoFactorPendiente));
    }

    [Fact]
    public async Task ConElSegundoFactorYaActivo_NoSePuedeEmpezarOtraAlta()
    {
        // Si bastara una sesión abierta para dar de alta otro teléfono, quien se hiciera con esa
        // sesión se pondría a sí mismo como segundo factor y dejaría fuera a la dueña de la cuenta.
        var db = TestDb.New();
        CrearUsuario(db);
        await DarDeAltaAsync(db);

        var (ok, mensaje, alta) = await Svc(db, Yo()).ComenzarAltaAsync();

        Assert.False(ok);
        Assert.Null(alta);
        Assert.Contains("reinicie", mensaje);
    }

    [Fact]
    public async Task NiElSecretoNiElUri_LleganNuncaALaBitacora()
    {
        // El URI del código QR lleva el secreto entero dentro. Una bitácora que lo guardara sería un
        // registro permanente de segundos factores en claro, legible por cualquiera con acceso a la
        // pantalla de Bitácora.
        var db = TestDb.New();
        CrearUsuario(db);
        var (secreto, _) = await DarDeAltaAsync(db);

        var apuntes = await db.AuditLogs.AsNoTracking().Select(a => (a.Details ?? "") + (a.NewValues ?? "")).ToListAsync();

        Assert.NotEmpty(apuntes);
        Assert.All(apuntes, texto =>
        {
            Assert.DoesNotContain(secreto, texto);
            Assert.DoesNotContain("otpauth", texto);
        });
    }

    [Fact]
    public async Task LosCodigosDeRescate_NoSeGuardanEnClaro()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        var (_, rescate) = await DarDeAltaAsync(db);

        var guardados = await db.UserRecoveryCodes.AsNoTracking().Select(c => c.CodigoHash).ToListAsync();

        Assert.Equal(CodigosDeRescate.Cuantos, guardados.Count);
        foreach (var codigo in rescate)
        {
            var canonico = CodigosDeRescate.Normalizar(codigo)!;
            Assert.DoesNotContain(guardados, h => h.Contains(canonico, StringComparison.OrdinalIgnoreCase));
            // Y el que sí está es su hash.
            Assert.Contains(CodigosDeRescate.Hashear(canonico), guardados);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  ENTRAR
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ElCodigoConElQueSeDioDeAlta_NoSirveParaEntrar()
    {
        // La antirrepetición cruza el alta y la entrada: el código de confirmación queda gastado.
        // Sin esto, quien viera esa pantalla por encima del hombro tendría minuto y medio.
        var db = TestDb.New();
        CrearUsuario(db);
        var svc = Svc(db, Yo());
        var (_, _, alta) = await svc.ComenzarAltaAsync();
        var codigoDelAlta = CodigoDe(alta!.SecretoEnBase32);
        await svc.ConfirmarAltaAsync(codigoDelAlta);

        var resultado = await svc.VerificarAsync(1, codigoDelAlta);

        Assert.False(resultado.Exito);
        Assert.Contains("ya se usó", resultado.Mensaje);
    }

    [Fact]
    public async Task ConElCodigoSiguiente_SeEntra()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        var (secreto, _) = await DarDeAltaAsync(db);

        var resultado = await Svc(db, Yo()).VerificarAsync(1, CodigoDe(secreto, 1));

        Assert.True(resultado.Exito);
        Assert.False(resultado.FueCodigoDeRescate);
        Assert.Equal(CodigosDeRescate.Cuantos, resultado.CodigosDeRescateRestantes);
    }

    [Fact]
    public async Task ElMismoCodigoDosVeces_LaSegundaSeRechaza()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        var (secreto, _) = await DarDeAltaAsync(db);
        var svc = Svc(db, Yo());
        var codigo = CodigoDe(secreto, 1);

        Assert.True((await svc.VerificarAsync(1, codigo)).Exito);

        var segunda = await svc.VerificarAsync(1, codigo);
        Assert.False(segunda.Exito);
        Assert.Contains("ya se usó", segunda.Mensaje);
    }

    [Fact]
    public async Task LaVentanaAceptada_QuedaAnotadaEnLaCuenta()
    {
        // Es la comprobación de que la antirrepetición se PERSISTE. Sin esta línea en el servicio,
        // todo seguiría funcionando y el mismo código valdría una y otra vez.
        var db = TestDb.New();
        CrearUsuario(db);
        var (secreto, _) = await DarDeAltaAsync(db);
        long ventanaEsperada = Totp.VentanaDe(DateTimeOffset.UtcNow) + 1;

        await Svc(db, Yo()).VerificarAsync(1, CodigoDe(secreto, 1));

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == 1);
        Assert.Equal(ventanaEsperada, user.SegundoFactorUltimaVentana);
    }

    [Fact]
    public async Task UnaCuentaSinSegundoFactor_NoEntraConNingunCodigo()
    {
        var db = TestDb.New();
        CrearUsuario(db);

        var resultado = await Svc(db, Yo()).VerificarAsync(1, "123456");

        Assert.False(resultado.Exito);
        Assert.Contains("todavía no tiene segundo factor", resultado.Mensaje);
    }

    [Fact]
    public async Task ElCodigoDeOtraPersona_NoAbreEstaCuenta()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        CrearUsuario(db, 2, "beto");
        var (secretoDeAna, _) = await DarDeAltaAsync(db, 1);
        await DarDeAltaAsync(db, 2);

        var resultado = await Svc(db, Yo(2)).VerificarAsync(2, CodigoDe(secretoDeAna, 1));

        Assert.False(resultado.Exito);
    }

    [Fact]
    public async Task CincoCodigosMalos_BloqueanLaCuentaComoLaContrasena()
    {
        // Seis dígitos son un millón de posibilidades; sin límite de intentos, quien ya tiene la
        // contraseña los prueba todos. Con el mismo bloqueo que la contraseña, no.
        var db = TestDb.New();
        CrearUsuario(db);
        var (secreto, _) = await DarDeAltaAsync(db);
        var svc = Svc(db, Yo());
        var malo = CodigoInvalidoPara(secreto);

        for (int i = 1; i < AuthService.MaxFailedAttempts; i++)
            Assert.False((await svc.VerificarAsync(1, malo)).Exito);

        var ultimo = await svc.VerificarAsync(1, malo);
        Assert.False(ultimo.Exito);
        Assert.Contains("bloqueada", ultimo.Mensaje);

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == 1);
        Assert.True(AuthService.EstaBloqueado(user));

        // Y con la cuenta bloqueada, ni el código bueno entra.
        Assert.False((await svc.VerificarAsync(1, CodigoDe(secreto, 1))).Exito);
    }

    [Fact]
    public async Task AlEntrarBien_ElContadorDeFallosVuelveACero()
    {
        // Si no, los fallos de la semana pasada se sumarían a un despiste de hoy y la cuenta se
        // bloquearía al primer error.
        var db = TestDb.New();
        CrearUsuario(db);
        var (secreto, _) = await DarDeAltaAsync(db);
        var svc = Svc(db, Yo());

        await svc.VerificarAsync(1, CodigoInvalidoPara(secreto));
        Assert.Equal(1, (await db.Users.AsNoTracking().FirstAsync(u => u.Id == 1)).FailedLoginCount);

        await svc.VerificarAsync(1, CodigoDe(secreto, 1));
        Assert.Equal(0, (await db.Users.AsNoTracking().FirstAsync(u => u.Id == 1)).FailedLoginCount);
    }

    [Fact]
    public async Task SiElSecretoNoSePuedeDescifrar_SeDiceYSeSenalaLaSalida()
    {
        // El día que se pierda el llavero de la protección de datos, la fila del secreto seguirá
        // ahí sin decir nada. Callarlo sería dejar a alguien tecleando códigos correctos sin
        // entender por qué no entra.
        var db = TestDb.New();
        CrearUsuario(db);
        var (secreto, _) = await DarDeAltaAsync(db);

        var resultado = await Svc(db, Yo(), new ProtectorIlegible()).VerificarAsync(1, CodigoDe(secreto, 1));

        Assert.False(resultado.Exito);
        Assert.Contains("reinicie", resultado.Mensaje);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  CÓDIGOS DE RESCATE
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnCodigoDeRescate_EntraUnaSolaVez()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        var (_, rescate) = await DarDeAltaAsync(db);
        var svc = Svc(db, Yo());

        var primera = await svc.VerificarAsync(1, rescate[0]);
        Assert.True(primera.Exito);
        Assert.True(primera.FueCodigoDeRescate);
        Assert.Equal(CodigosDeRescate.Cuantos - 1, primera.CodigosDeRescateRestantes);

        var segunda = await svc.VerificarAsync(1, rescate[0]);
        Assert.False(segunda.Exito);
        Assert.Contains("ya se usó", segunda.Mensaje);
    }

    [Fact]
    public async Task ElCodigoUsado_SeMarcaEnVezDeBorrarse()
    {
        // La fila es la constancia de que alguien entró por aquí y de cuándo, que es exactamente lo
        // que se busca en la bitácora cuando algo resulta no haber sido lo que parecía.
        var db = TestDb.New();
        CrearUsuario(db);
        var (_, rescate) = await DarDeAltaAsync(db);

        await Svc(db, Yo()).VerificarAsync(1, rescate[0]);

        Assert.Equal(CodigosDeRescate.Cuantos, await db.UserRecoveryCodes.CountAsync());
        Assert.Equal(1, await db.UserRecoveryCodes.CountAsync(c => c.UsadoEnUtc != null));

        var apunte = await db.AuditLogs.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Details != null && a.Details.Contains("código de rescate"));
        Assert.NotNull(apunte);
    }

    [Fact]
    public async Task SeAceptaComoLoTecleaUnaPersona()
    {
        // De un papel, en minúsculas y sin los guiones. Rechazarlo por eso sería rechazar un código
        // bueno a alguien que ya está teniendo un mal día.
        var db = TestDb.New();
        CrearUsuario(db);
        var (_, rescate) = await DarDeAltaAsync(db);

        var resultado = await Svc(db, Yo()).VerificarAsync(1, rescate[0].Replace("-", "").ToLowerInvariant());

        Assert.True(resultado.Exito);
    }

    [Fact]
    public async Task ElCodigoDeRescateDeOtraPersona_NoAbreEstaCuenta()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        CrearUsuario(db, 2, "beto");
        var (_, deAna) = await DarDeAltaAsync(db, 1);
        await DarDeAltaAsync(db, 2);

        var resultado = await Svc(db, Yo(2)).VerificarAsync(2, deAna[0]);

        Assert.False(resultado.Exito);
        // Y el de Ana sigue intacto: un intento fallido de otro no le gasta un código.
        Assert.Equal(CodigosDeRescate.Cuantos, await Svc(db, Yo(1)).ContarCodigosDeRescateAsync(1));
    }

    [Fact]
    public async Task CuandoQuedanPocos_ElMensajeLoAvisa()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        var (_, rescate) = await DarDeAltaAsync(db);
        var svc = Svc(db, Yo());

        // Se gastan todos menos el último.
        for (int i = 0; i < CodigosDeRescate.Cuantos - 1; i++)
            Assert.True((await svc.VerificarAsync(1, rescate[i])).Exito);

        var ultimo = await svc.VerificarAsync(1, rescate[^1]);

        Assert.True(ultimo.Exito);
        Assert.Equal(0, ultimo.CodigosDeRescateRestantes);
        Assert.Contains("Era el último", ultimo.Mensaje);
    }

    [Fact]
    public async Task RegenerarLosCodigos_ExigeElCodigoDelTelefono()
    {
        // Sin esa exigencia, quien se hiciera con una sesión abierta se llevaría ocho llaves
        // permanentes de la cuenta sin haber tenido nunca el teléfono delante.
        var db = TestDb.New();
        CrearUsuario(db);
        var (secreto, viejos) = await DarDeAltaAsync(db);
        var svc = Svc(db, Yo());

        var (falla, _, _) = await svc.RegenerarCodigosDeRescateAsync(CodigoInvalidoPara(secreto));
        Assert.False(falla);

        var (ok, _, nuevos) = await svc.RegenerarCodigosDeRescateAsync(CodigoDe(secreto, 1));

        Assert.True(ok);
        Assert.Equal(CodigosDeRescate.Cuantos, nuevos!.Count);
        Assert.Empty(nuevos.Intersect(viejos));

        // Los viejos dejan de valer de verdad, no solo de mostrarse.
        Assert.False((await svc.VerificarAsync(1, viejos[0])).Exito);
        Assert.Equal(CodigosDeRescate.Cuantos, await svc.ContarCodigosDeRescateAsync(1));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  EQUIPOS RECORDADOS
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UnEquipoRecordado_NoVuelveAPedirElCodigoDurante30Dias()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        var svc = Svc(db, Yo());

        var testigo = await svc.RecordarEsteEquipoAsync(1, "Chrome en Windows");

        Assert.True(await svc.EsEquipoRecordadoAsync(1, testigo));

        var fila = await db.UserTrustedDevices.AsNoTracking().FirstAsync();
        Assert.Equal(SegundoFactorService.DiasQueSeRecuerdaElEquipo,
                     (int)Math.Round((fila.ExpiraEnUtc - fila.CreatedAtUtc).TotalDays));
    }

    [Fact]
    public async Task ElTestigoNoSeGuardaEnClaro()
    {
        // Si estuviera en claro, quien leyera esta tabla se fabricaría la cookie de cualquiera y se
        // saltaría el segundo factor de todo el equipo a la vez.
        var db = TestDb.New();
        CrearUsuario(db);

        var testigo = await Svc(db, Yo()).RecordarEsteEquipoAsync(1, null);

        var guardado = await db.UserTrustedDevices.AsNoTracking().Select(d => d.TokenHash).FirstAsync();
        Assert.NotEqual(testigo, guardado);
        Assert.Equal(64, guardado.Length);
    }

    [Fact]
    public async Task ElTestigoDeOtraCuenta_NoVale()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        CrearUsuario(db, 2, "beto");
        var svc = Svc(db, Yo());

        var deAna = await svc.RecordarEsteEquipoAsync(1, "portátil");

        Assert.False(await svc.EsEquipoRecordadoAsync(2, deAna));
    }

    [Fact]
    public async Task UnEquipoVencido_VuelveAPedirElCodigoYSeLimpiaSolo()
    {
        // La caducidad la manda el servidor: la de la cookie la cambia cualquiera desde su navegador.
        var db = TestDb.New();
        CrearUsuario(db);
        var svc = Svc(db, Yo());
        var testigo = await svc.RecordarEsteEquipoAsync(1, "portátil");

        var fila = await db.UserTrustedDevices.FirstAsync();
        fila.ExpiraEnUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        Assert.False(await svc.EsEquipoRecordadoAsync(1, testigo));
        Assert.Empty(await db.UserTrustedDevices.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task UnTestigoInventado_NoVale()
    {
        var db = TestDb.New();
        CrearUsuario(db);
        var svc = Svc(db, Yo());

        Assert.False(await svc.EsEquipoRecordadoAsync(1, "no soy un testigo"));
        Assert.False(await svc.EsEquipoRecordadoAsync(1, null));
        Assert.False(await svc.EsEquipoRecordadoAsync(1, ""));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  REINICIO POR EL LÍDER
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ElLiderReinicia_YLaCuentaVuelveAEmpezarDeCero()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        CrearUsuario(db, 9, "lider", UserRole.Admin);
        var (_, rescate) = await DarDeAltaAsync(db, 1);
        await Svc(db, Yo(1)).RecordarEsteEquipoAsync(1, "portátil de ana");

        var (ok, mensaje) = await Svc(db, Yo(9, UserRole.Admin)).ReiniciarAsync(1);

        Assert.True(ok);
        Assert.Contains("reiniciado", mensaje);

        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == 1);
        Assert.False(user.SegundoFactorActivo);
        Assert.Null(user.SegundoFactorDesdeUtc);
        // La ventana también se limpia: pertenecía a un secreto que ya no existe, y dejarla podría
        // hacer que el primer código del teléfono nuevo se rechazara por «ya usado».
        Assert.Null(user.SegundoFactorUltimaVentana);

        Assert.Empty(await db.UserSecrets.AsNoTracking().Where(s => s.UserId == 1).ToListAsync());
        Assert.Empty(await db.UserRecoveryCodes.AsNoTracking().Where(c => c.UserId == 1).ToListAsync());

        // Los códigos de rescate viejos tampoco sirven ya.
        Assert.False((await Svc(db, Yo(1)).VerificarAsync(1, rescate[0])).Exito);
    }

    [Fact]
    public async Task ElReinicio_TambienDejaDeConfiarEnLosEquiposRecordados()
    {
        // Es la parte que se olvida. Quien pide un reinicio porque le robaron el teléfono no gana
        // nada si el equipo que se llevaron con él sigue entrando sin código.
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        CrearUsuario(db, 9, "lider", UserRole.Admin);
        await DarDeAltaAsync(db, 1);
        var testigo = await Svc(db, Yo(1)).RecordarEsteEquipoAsync(1, "el que se llevaron");

        await Svc(db, Yo(9, UserRole.Admin)).ReiniciarAsync(1);

        Assert.False(await Svc(db, Yo(1)).EsEquipoRecordadoAsync(1, testigo));
        Assert.Empty(await db.UserTrustedDevices.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ElReinicio_DejaAsientoEnLaBitacoraConQuienYAQuien()
    {
        // Es una operación que QUITA una protección: tiene que poder mirarse después.
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        CrearUsuario(db, 9, "lider", UserRole.Admin);
        await DarDeAltaAsync(db, 1);

        await Svc(db, Yo(9, UserRole.Admin)).ReiniciarAsync(1);

        var apunte = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Details != null && a.Details.Contains("REINICIADO"))
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();

        Assert.NotNull(apunte);
        Assert.Contains("ana", apunte!.Details);
        Assert.Equal("admin", apunte.UserName);
    }

    [Fact]
    public async Task ReiniciarElSegundoFactorDeOtro_NoEsCosaDeCualquiera()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        CrearUsuario(db, 2, "beto");
        await DarDeAltaAsync(db, 1);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, Yo(2)).ReiniciarAsync(1));

        Assert.True((await db.Users.AsNoTracking().FirstAsync(u => u.Id == 1)).SegundoFactorActivo);
    }

    [Fact]
    public async Task TrasElReinicio_SePuedeDarDeAltaOtraVez()
    {
        // El reinicio no deja la cuenta sin protección: la deja como el primer día, obligada a
        // volver a darla de alta.
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        CrearUsuario(db, 9, "lider", UserRole.Admin);
        await DarDeAltaAsync(db, 1);
        await Svc(db, Yo(9, UserRole.Admin)).ReiniciarAsync(1);

        var (secreto, _) = await DarDeAltaAsync(db, 1);

        Assert.True((await db.Users.AsNoTracking().FirstAsync(u => u.Id == 1)).SegundoFactorActivo);
        Assert.True((await Svc(db, Yo(1)).VerificarAsync(1, CodigoDe(secreto, 1))).Exito);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  ESTADO
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ElEstado_DiceLoJustoYNadaDelSecreto()
    {
        var db = TestDb.New();
        CrearUsuario(db);

        var antes = await Svc(db, Yo()).MiEstadoAsync();
        Assert.False(antes.Activo);
        Assert.Equal(0, antes.CodigosDeRescateRestantes);

        var (_, rescate) = await DarDeAltaAsync(db);
        await Svc(db, Yo()).VerificarAsync(1, rescate[0]);

        var despues = await Svc(db, Yo()).MiEstadoAsync();
        Assert.True(despues.Activo);
        Assert.NotNull(despues.DesdeUtc);
        Assert.Equal(CodigosDeRescate.Cuantos - 1, despues.CodigosDeRescateRestantes);
    }

    [Fact]
    public async Task SinSesion_NoSePuedeNiEmpezarElAlta()
    {
        var db = TestDb.New();
        CrearUsuario(db);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, UsuarioDePrueba.Anonimo()).ComenzarAltaAsync());
        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, UsuarioDePrueba.Anonimo()).ConfirmarAltaAsync("123456"));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  EL DIBUJO DEL CÓDIGO QR
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ElCodigoQr_SaleComoPngYSinDependerDeWindows()
    {
        // Esta prueba existe por una razón concreta: varias salidas de la librería de códigos QR
        // dependen de System.Drawing, que es una pieza de Windows y no existe en el contenedor
        // Linux donde corre esto. Con la salida equivocada, el fallo no aparecería al compilar sino
        // la primera vez que alguien pidiera su código QR en producción.
        var uri = UriDeSegundoFactor.Construir("ana", Totp.GenerarSecretoEnBase32());

        var png = new DibujanteDeCodigoQrCoder().DibujarPng(uri);

        Assert.NotNull(png);
        Assert.True(png.Length > 100);
        // Los ocho bytes con que empieza todo PNG.
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
    }
}
