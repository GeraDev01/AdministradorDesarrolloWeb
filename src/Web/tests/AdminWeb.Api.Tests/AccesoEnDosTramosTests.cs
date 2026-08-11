using System.Net;
using System.Net.Http.Json;
using AdminWeb.Api.Endpoints;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Auth;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// La puerta entera, con la API levantada: los dos tramos del acceso, el equipo recordado y el corte
/// de quien todavía no ha activado su segundo factor.
///
/// <para><b>Lo que solo se puede comprobar aquí es que NO HAY SESIÓN entre los dos tramos.</b> Las
/// pruebas de servicios ven que el código se verifica bien, pero no pueden ver lo único que importa
/// de este diseño: que después de acertar la contraseña, y antes de teclear el código, la API sigue
/// contestando 401 a todo. Ese es el fallo que este archivo existe para atrapar, y es un fallo que
/// no se nota usando la aplicación — todo funcionaría igual de bien con la puerta abierta.</para>
///
/// <para><b>Sobre el reloj.</b> Un código de seis dígitos vale para una ventana de treinta segundos y
/// el servidor apunta la última aceptada para que no se pueda repetir. Por eso cada prueba usa SU
/// PROPIA cuenta: compartirlas haría que la segunda en correr se encontrara la ventana ya gastada y
/// fallara un día sí y otro también, por el motivo correcto y en el sitio equivocado.</para>
/// </summary>
public class AccesoEnDosTramosTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    private const string Contrasena = "ClaveDeLaPuerta123";

    /// <summary>Una ruta protegida cualquiera: sirve para preguntar «¿hay sesión de verdad?».</summary>
    private const string RutaProtegida = "/api/jornada/estado";

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  ENTRE LOS DOS TRAMOS NO HAY SESIÓN
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LA prueba de este archivo: la contraseña sola no abre nada.
    ///
    /// <para>Se comprueban las dos mitades y las dos hacen falta. Que la respuesta ANUNCIE que falta
    /// el código es lo que permite al cliente enseñar el segundo paso; que la misma petición siguiente
    /// reciba un 401 es lo que demuestra que no se emitió ninguna cookie. Sin la segunda, esto pasaría
    /// igual de bien con una sesión a medias emitida por descuido.</para>
    /// </summary>
    [Fact]
    public async Task LaContrasenaSola_AnunciaQueFaltaElCodigo_YNoAbreNingunaSesion()
    {
        var (_, _, _) = await CuentaListaAsync("puerta.uno");
        var navegador = api.NuevoCliente();

        var acceso = await EntrarConLaContrasenaAsync(navegador, "puerta.uno");

        Assert.True(acceso.SegundoFactorRequerido);
        Assert.False(string.IsNullOrWhiteSpace(acceso.Tramo));
        Assert.Null(acceso.Usuario);   // ni siquiera se dice quién es hasta que se termina de entrar

        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.GetAsync(RutaProtegida)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.GetAsync("/api/auth/me")).StatusCode);
    }

    /// <summary>Con el código correcto sí, y la sesión que queda es una de verdad.</summary>
    [Fact]
    public async Task ConElCodigoDelTelefono_LaSesionSeAbre()
    {
        var (_, secreto, _) = await CuentaListaAsync("puerta.dos");
        var navegador = api.NuevoCliente();

        var acceso = await EntrarConLaContrasenaAsync(navegador, "puerta.dos");
        var segundo = await navegador.PostAsJsonAsync("/api/auth/login/segundo-factor",
            new SegundoFactorLoginRequest(acceso.Tramo!, CodigoSiguiente(secreto), RecordarEquipo: false));

        Assert.Equal(HttpStatusCode.OK, segundo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await navegador.GetAsync(RutaProtegida)).StatusCode);
    }

    /// <summary>
    /// Un código equivocado no abre nada, y el rechazo es 401.
    ///
    /// <para>El código de estado importa: con un 400 el cliente lo trataría como un error de forma de
    /// la petición y no como «esa credencial no vale», que es lo que es.</para>
    /// </summary>
    [Fact]
    public async Task ConUnCodigoEquivocado_NoSeAbreNada()
    {
        await CuentaListaAsync("puerta.tres");
        var navegador = api.NuevoCliente();

        var acceso = await EntrarConLaContrasenaAsync(navegador, "puerta.tres");
        var segundo = await navegador.PostAsJsonAsync("/api/auth/login/segundo-factor",
            new SegundoFactorLoginRequest(acceso.Tramo!, "000000", RecordarEquipo: true));

        Assert.Equal(HttpStatusCode.Unauthorized, segundo.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await navegador.GetAsync(RutaProtegida)).StatusCode);

        // Y no se recordó el equipo pese a haberlo pedido: quien no pasa el código no deja huella de
        // confianza. Si se guardara antes de comprobar, bastaría con fallar el código una vez para
        // dejar el navegador marcado, y entonces el segundo factor lo habría desactivado el error.
        using var ambito = api.Services.CreateScope();
        var db = ambito.ServiceProvider.GetRequiredService<AppDbContext>();
        var cuenta = await db.Users.AsNoTracking().FirstAsync(u => u.Username == "puerta.tres");
        Assert.Equal(0, await db.UserTrustedDevices.CountAsync(d => d.UserId == cuenta.Id));
    }

    /// <summary>
    /// Un tramo manipulado no sirve, y tampoco el de otra sesión de acceso.
    ///
    /// <para>Es lo que sostiene todo el diseño: el tramo no es un identificador que se pueda adivinar
    /// ni retocar, sino un texto cifrado y firmado por el servidor. Si esto fallara, cualquiera podría
    /// fabricarse uno con el número de cuenta de otra persona.</para>
    /// </summary>
    [Fact]
    public async Task UnTramoInventadoOManipulado_NoSirve()
    {
        var (_, secreto, _) = await CuentaListaAsync("puerta.cuatro");
        var navegador = api.NuevoCliente();

        var acceso = await EntrarConLaContrasenaAsync(navegador, "puerta.cuatro");

        foreach (var tramo in new[] { "esto-no-es-un-tramo", "", acceso.Tramo! + "x" })
        {
            var r = await navegador.PostAsJsonAsync("/api/auth/login/segundo-factor",
                new SegundoFactorLoginRequest(tramo, CodigoSiguiente(secreto), RecordarEquipo: false));

            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
    }

    /// <summary>
    /// Un código de rescate vale en EL MISMO CAMPO que el del teléfono, y se gasta al usarlo.
    ///
    /// <para>Que sea el mismo campo no es comodidad: es lo que garantiza que los dos caminos pasen por
    /// el mismo bloqueo por intentos fallidos. Dos puertas serían dos sitios donde aplicarlo, y el día
    /// que una se olvidara sería la que se usaría para probar códigos sin límite.</para>
    /// </summary>
    [Fact]
    public async Task UnCodigoDeRescate_ValeEnElMismoCampo_YSoloUnaVez()
    {
        var (_, _, rescate) = await CuentaListaAsync("puerta.cinco");
        var codigo = rescate[0];

        var primero = api.NuevoCliente();
        var acceso = await EntrarConLaContrasenaAsync(primero, "puerta.cinco");
        var r = await primero.PostAsJsonAsync("/api/auth/login/segundo-factor",
            new SegundoFactorLoginRequest(acceso.Tramo!, codigo, RecordarEquipo: false));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await primero.GetAsync(RutaProtegida)).StatusCode);

        // El mismo código, otra vez: ya no vale.
        var segundoNavegador = api.NuevoCliente();
        var otroAcceso = await EntrarConLaContrasenaAsync(segundoNavegador, "puerta.cinco");
        var repetido = await segundoNavegador.PostAsJsonAsync("/api/auth/login/segundo-factor",
            new SegundoFactorLoginRequest(otroAcceso.Tramo!, codigo, RecordarEquipo: false));

        Assert.Equal(HttpStatusCode.Unauthorized, repetido.StatusCode);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  EL EQUIPO RECORDADO
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Con la casilla marcada, ESE navegador ya no pide el código; otro distinto sí.
    ///
    /// <para>Las dos mitades otra vez: sin la segunda, esto pasaría igual de bien si «recordar el
    /// equipo» hubiera apagado el segundo factor de la cuenta entera.</para>
    /// </summary>
    [Fact]
    public async Task SiSeRecuerdaElEquipo_EseNavegadorNoVuelveAPedirCodigo_YOtroSi()
    {
        var (_, secreto, _) = await CuentaListaAsync("puerta.seis");

        var conocido = api.NuevoCliente();
        var acceso = await EntrarConLaContrasenaAsync(conocido, "puerta.seis");
        (await conocido.PostAsJsonAsync("/api/auth/login/segundo-factor",
            new SegundoFactorLoginRequest(acceso.Tramo!, CodigoSiguiente(secreto), RecordarEquipo: true)))
            .EnsureSuccessStatusCode();

        // Se cierra la sesión: lo que queda es solo la cookie del equipo recordado, que a propósito
        // sobrevive a salir — «este equipo es mío» no deja de ser cierto al terminar el día.
        await conocido.PostAsync("/api/auth/logout", null);

        var deVuelta = await EntrarConLaContrasenaAsync(conocido, "puerta.seis");
        Assert.False(deVuelta.SegundoFactorRequerido);
        Assert.Equal("puerta.seis", deVuelta.Usuario!.Usuario);
        Assert.Equal(HttpStatusCode.OK, (await conocido.GetAsync(RutaProtegida)).StatusCode);

        // Y la bitácora DISTINGUE esta entrada de una con el código tecleado. Sin esa distinción,
        // después de un incidente no habría forma de separar los accesos hechos con el teléfono
        // delante de los que se apoyaron en una confianza concedida semanas antes, que es justo lo
        // que hay que revisar.
        using (var ambito = api.Services.CreateScope())
        {
            var db = ambito.ServiceProvider.GetRequiredService<AppDbContext>();
            var cuenta = await db.Users.AsNoTracking().FirstAsync(u => u.Username == "puerta.seis");
            var apuntes = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "User" && a.EntityId == cuenta.Id.ToString())
                .Select(a => a.Details).ToListAsync();

            Assert.Contains(apuntes, d => d != null && d.Contains("desde un equipo recordado"));
        }

        // Un navegador cualquiera sigue pidiendo el código.
        var desconocido = api.NuevoCliente();
        Assert.True((await EntrarConLaContrasenaAsync(desconocido, "puerta.seis")).SegundoFactorRequerido);
    }

    /// <summary>
    /// La cookie del equipo recordado de una cuenta NO se salta el segundo factor de otra.
    ///
    /// <para>Es el error más tentador de esta función: guardar «este navegador es de confianza» sin
    /// atarlo a la cuenta. En un equipo compartido —o en el portátil de alguien que también tiene una
    /// cuenta de prueba— bastaría con que UNA persona marcara la casilla para que todas las demás
    /// entraran con solo su contraseña.</para>
    /// </summary>
    [Fact]
    public async Task LaCookieDeUnEquipoRecordado_NoValeParaOtraCuenta()
    {
        var (_, secreto, _) = await CuentaListaAsync("puerta.siete");
        await CuentaListaAsync("puerta.ocho");

        var navegador = api.NuevoCliente();
        var acceso = await EntrarConLaContrasenaAsync(navegador, "puerta.siete");
        (await navegador.PostAsJsonAsync("/api/auth/login/segundo-factor",
            new SegundoFactorLoginRequest(acceso.Tramo!, CodigoSiguiente(secreto), RecordarEquipo: true)))
            .EnsureSuccessStatusCode();

        // El MISMO navegador, con la misma cookie de equipo recordado, entrando en la otra cuenta.
        var otra = await EntrarConLaContrasenaAsync(navegador, "puerta.ocho");

        Assert.True(otra.SegundoFactorRequerido);
    }

    /// <summary>
    /// Cambiar la contraseña deja de confiar en los equipos recordados. Es el escenario «me robaron
    /// la cuenta»: cerrar la puerta de delante sin cerrar la de atrás no cierra nada.
    /// </summary>
    [Fact]
    public async Task AlCambiarLaContrasena_ElEquipoRecordadoDejaDeSerlo()
    {
        var (_, secreto, _) = await CuentaListaAsync("puerta.nueve");

        var navegador = api.NuevoCliente();
        var acceso = await EntrarConLaContrasenaAsync(navegador, "puerta.nueve");
        (await navegador.PostAsJsonAsync("/api/auth/login/segundo-factor",
            new SegundoFactorLoginRequest(acceso.Tramo!, CodigoSiguiente(secreto), RecordarEquipo: true)))
            .EnsureSuccessStatusCode();

        const string nueva = "LaQueNadieVio456";
        (await navegador.PostAsJsonAsync("/api/auth/change-password",
            new CambioContrasenaRequest(nueva, nueva))).EnsureSuccessStatusCode();

        // Ese mismo navegador, con su cookie de equipo recordado intacta, vuelve a entrar.
        var deVuelta = await EntrarConLaContrasenaAsync(navegador, "puerta.nueve", nueva);

        Assert.True(deVuelta.SegundoFactorRequerido);
    }

    /// <summary>
    /// CUALQUIER rotación del sello de seguridad deja el equipo sin recordar, no solo el cambio de
    /// contraseña.
    ///
    /// <para>El sello se rota desde varios sitios —al desactivar una cuenta, al cambiarle el rol— y
    /// desde donde se rote mañana. Que la cookie lleve dentro con qué sello se emitió es lo que hace
    /// que esos sitios no tengan que acordarse de borrar nada: aquí se rota el sello A MANO, sin pasar
    /// por ninguna de las operaciones que además limpian las filas, que es exactamente la situación
    /// que se quiere cubrir.</para>
    /// </summary>
    [Fact]
    public async Task SiElSelloDeSeguridadCambia_ElEquipoRecordadoDejaDeSerlo()
    {
        var (id, secreto, _) = await CuentaListaAsync("puerta.catorce");

        var navegador = api.NuevoCliente();
        var acceso = await EntrarConLaContrasenaAsync(navegador, "puerta.catorce");
        (await navegador.PostAsJsonAsync("/api/auth/login/segundo-factor",
            new SegundoFactorLoginRequest(acceso.Tramo!, CodigoSiguiente(secreto), RecordarEquipo: true)))
            .EnsureSuccessStatusCode();

        using (var ambito = api.Services.CreateScope())
        {
            var db = ambito.ServiceProvider.GetRequiredService<AppDbContext>();
            var cuenta = await db.Users.FirstAsync(u => u.Id == id);
            cuenta.SecurityStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync();

            // La fila del equipo recordado SIGUE ahí: nadie la borró, que es justo lo que se quiere
            // probar. Sin esta comprobación, la prueba pasaría igual si la rotación hubiera limpiado
            // las filas, y entonces no estaría probando lo que dice probar.
            Assert.Equal(1, await db.UserTrustedDevices.CountAsync(d => d.UserId == id));
        }

        var deVuelta = await EntrarConLaContrasenaAsync(navegador, "puerta.catorce");

        Assert.True(deVuelta.SegundoFactorRequerido);
    }

    /// <summary>
    /// Se pueden ver los equipos recordados y olvidarlos todos, sin pasar por el líder.
    ///
    /// <para>Sin esto, «recuérdame treinta días» sería una decisión que no se puede deshacer hasta que
    /// caduque sola: quien se acordara al día siguiente de haberlo marcado en un equipo prestado no
    /// tendría nada que hacer.</para>
    /// </summary>
    [Fact]
    public async Task LosEquiposRecordados_SeVenYSePuedenOlvidarTodos()
    {
        var (_, secreto, _) = await CuentaListaAsync("puerta.diez");

        var navegador = api.NuevoCliente();
        var acceso = await EntrarConLaContrasenaAsync(navegador, "puerta.diez");
        (await navegador.PostAsJsonAsync("/api/auth/login/segundo-factor",
            new SegundoFactorLoginRequest(acceso.Tramo!, CodigoSiguiente(secreto), RecordarEquipo: true)))
            .EnsureSuccessStatusCode();

        var equipos = await navegador.GetFromJsonAsync<List<EquipoRecordadoDto>>("/api/auth/segundo-factor/equipos");
        Assert.Single(equipos!);

        var estado = await navegador.GetFromJsonAsync<EstadoDeSegundoFactorDto>("/api/auth/segundo-factor/estado");
        Assert.Equal(1, estado!.EquiposRecordados);
        // Los treinta días los dice el servidor: la pantalla los repite en vez de escribirlos a mano.
        Assert.Equal(30, estado.DiasQueSeRecuerdaElEquipo);

        var olvido = await navegador.PostAsync("/api/auth/segundo-factor/equipos/olvidar", null);
        olvido.EnsureSuccessStatusCode();

        Assert.Empty(await navegador.GetFromJsonAsync<List<EquipoRecordadoDto>>("/api/auth/segundo-factor/equipos") ?? []);

        // Y el navegador vuelve a pedir el código de verdad, no solo en la lista.
        await navegador.PostAsync("/api/auth/logout", null);
        Assert.True((await EntrarConLaContrasenaAsync(navegador, "puerta.diez")).SegundoFactorRequerido);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  EL ALTA OBLIGATORIA Y SU ORDEN
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sin segundo factor activo se entra, pero no se llega a ninguna pantalla: todo queda cortado con
    /// su código, y solo se dejan pasar las rutas del alta.
    ///
    /// <para>Que el código viaje como DATO y no dentro del texto del mensaje es lo que permite al
    /// cliente llevar a la pantalla correcta; un mensaje se reescribe cualquier día.</para>
    /// </summary>
    [Fact]
    public async Task SinSegundoFactorActivo_TodoQuedaCortadoSalvoElAlta()
    {
        await SembrarAsync("puerta.once", UserRole.Desarrollador);
        var navegador = api.NuevoCliente();

        var acceso = await EntrarConLaContrasenaAsync(navegador, "puerta.once");
        Assert.False(acceso.SegundoFactorRequerido);        // todavía no lo tiene: se entra
        Assert.True(acceso.Usuario!.DebeActivarSegundoFactor);

        var cortada = await navegador.GetAsync(RutaProtegida);
        Assert.Equal(HttpStatusCode.Forbidden, cortada.StatusCode);
        Assert.Contains(AuthEndpoints.CodigoDebeActivarSegundoFactor, await cortada.Content.ReadAsStringAsync());

        // Y lo imprescindible para salir del atolladero sigue abierto.
        Assert.Equal(HttpStatusCode.OK, (await navegador.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await navegador.GetAsync("/api/auth/segundo-factor/estado")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await navegador.PostAsync("/api/auth/segundo-factor/alta", null)).StatusCode);
    }

    /// <summary>
    /// Con las DOS cosas pendientes manda la contraseña: el alta del segundo factor queda cortada
    /// hasta que la temporal se cambia.
    ///
    /// <para><b>El orden no es un detalle de presentación.</b> Una contraseña temporal se dicta por
    /// chat o en voz alta; mientras siga viva, cualquiera que la haya visto puede entrar. Si esa
    /// persona pudiera dar de alta el segundo factor, daría de alta SU teléfono y se quedaría con la
    /// cuenta para siempre: la dueña ya no podría entrar ni sabiendo su propia contraseña. Al revés no
    /// hay daño equivalente.</para>
    /// </summary>
    [Fact]
    public async Task ConLasDosPendientes_LaContrasenaVaPrimero()
    {
        await SembrarAsync("puerta.doce", UserRole.Desarrollador, conContrasenaTemporal: true);
        var navegador = api.NuevoCliente();

        await EntrarConLaContrasenaAsync(navegador, "puerta.doce");

        var alta = await navegador.PostAsync("/api/auth/segundo-factor/alta", null);

        Assert.Equal(HttpStatusCode.Forbidden, alta.StatusCode);
        var cuerpo = await alta.Content.ReadAsStringAsync();
        Assert.Contains(AuthEndpoints.CodigoDebeCambiarContrasena, cuerpo);
        Assert.DoesNotContain(AuthEndpoints.CodigoDebeActivarSegundoFactor, cuerpo);
    }

    /// <summary>
    /// El alta no activa nada hasta que un código coincide, y al confirmarla la sesión deja de estar
    /// cortada sin tener que volver a entrar.
    ///
    /// <para>Lo segundo es el detalle que se olvida: la cookie sigue llevando el aviso de «te falta el
    /// segundo factor» hasta que se reemite, y sin reemitirla la persona se quedaría atrapada en la
    /// pantalla de alta que acaba de completar.</para>
    /// </summary>
    [Fact]
    public async Task AlConfirmarElAlta_LaSesionDejaDeEstarCortada_YSalenLosCodigosDeRescate()
    {
        await SembrarAsync("puerta.trece", UserRole.Desarrollador);
        var navegador = api.NuevoCliente();
        await EntrarConLaContrasenaAsync(navegador, "puerta.trece");

        var inicio = await navegador.PostAsync("/api/auth/segundo-factor/alta", null);
        var alta = await inicio.Content.ReadFromJsonAsync<InicioDeAltaDto>();

        // El QR viaja incrustado en la respuesta y no como una dirección propia: esa imagen ES el
        // secreto, y una dirección quedaría escrita en el historial y en los registros.
        Assert.StartsWith("data:image/png;base64,", alta!.CodigoQrPngBase64);
        Assert.Contains("otpauth://totp/", alta.Uri);

        // Un código equivocado NO activa nada.
        var fallido = await navegador.PostAsJsonAsync("/api/auth/segundo-factor/confirmar",
            new ConfirmarAltaRequest("000000"));
        Assert.Equal(HttpStatusCode.BadRequest, fallido.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await navegador.GetAsync(RutaProtegida)).StatusCode);

        var bueno = await navegador.PostAsJsonAsync("/api/auth/segundo-factor/confirmar",
            new ConfirmarAltaRequest(CodigoDeAhora(alta.SecretoEnBase32)));
        bueno.EnsureSuccessStatusCode();

        var hecho = await bueno.Content.ReadFromJsonAsync<AltaConfirmadaDto>();
        Assert.Equal(CodigosDeRescate.Cuantos, hecho!.CodigosDeRescate.Count);

        // Y la sesión ya no está cortada, sin haber tenido que volver a entrar.
        Assert.Equal(HttpStatusCode.OK, (await navegador.GetAsync(RutaProtegida)).StatusCode);

        var yo = await navegador.GetFromJsonAsync<UsuarioSesionDto>("/api/auth/me");
        Assert.False(yo!.DebeActivarSegundoFactor);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  Ayudas
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Una cuenta nueva con su segundo factor ya dado de alta, por las rutas de verdad, y el navegador
    /// del alta desechado: lo que devuelve son las credenciales, no una sesión.
    /// </summary>
    private async Task<(int id, string secreto, IReadOnlyList<string> rescate)> CuentaListaAsync(string usuario)
    {
        int id = await SembrarAsync(usuario, UserRole.Desarrollador);

        var cliente = api.NuevoCliente();
        await EntrarConLaContrasenaAsync(cliente, usuario);

        var inicio = await cliente.PostAsync("/api/auth/segundo-factor/alta", null);
        inicio.EnsureSuccessStatusCode();
        var alta = await inicio.Content.ReadFromJsonAsync<InicioDeAltaDto>();

        var confirmacion = await cliente.PostAsJsonAsync("/api/auth/segundo-factor/confirmar",
            new ConfirmarAltaRequest(CodigoDeAhora(alta!.SecretoEnBase32)));
        confirmacion.EnsureSuccessStatusCode();

        var hecho = await confirmacion.Content.ReadFromJsonAsync<AltaConfirmadaDto>();
        return (id, alta.SecretoEnBase32, hecho!.CodigosDeRescate);
    }

    private async Task<int> SembrarAsync(string usuario, UserRole rol, bool conContrasenaTemporal = false)
    {
        using var ambito = api.Services.CreateScope();
        var db = ambito.ServiceProvider.GetRequiredService<AppDbContext>();

        var existente = await db.Users.FirstOrDefaultAsync(u => u.Username == usuario);
        if (existente != null) return existente.Id;

        var nuevo = new User
        {
            Username = usuario,
            FullName = usuario,
            Role = rol,
            IsActive = true,
            MustChangePassword = conContrasenaTemporal,
            PasswordHash = PasswordHasher.Hash(Contrasena)
        };

        db.Users.Add(nuevo);
        await db.SaveChangesAsync();
        return nuevo.Id;
    }

    private static async Task<RespuestaDeAccesoDto> EntrarConLaContrasenaAsync(
        HttpClient cliente, string usuario, string contrasena = Contrasena)
    {
        var r = await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest(usuario, contrasena));
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<RespuestaDeAccesoDto>())!;
    }

    /// <summary>El código que muestra el teléfono ahora mismo.</summary>
    private static string CodigoDeAhora(string secretoEnBase32) =>
        Totp.Calcular(Base32.Decodificar(secretoEnBase32)!, Totp.VentanaDe(DateTimeOffset.UtcNow));

    /// <summary>
    /// El código de la ventana SIGUIENTE, que es el que hay que usar para entrar después de haber
    /// dado el alta de esa misma cuenta.
    ///
    /// <para>No es un truco de las pruebas, es la antirrepetición del servidor haciendo su trabajo: el
    /// alta acaba de aceptar el código de la ventana en curso y ese ya no vuelve a valer. La ventana
    /// siguiente entra dentro de la tolerancia de reloj (±1) y es posterior a la apuntada, así que se
    /// acepta tanto si el reloj ya cambió de ventana como si no.</para>
    /// </summary>
    private static string CodigoSiguiente(string secretoEnBase32) =>
        Totp.Calcular(Base32.Decodificar(secretoEnBase32)!, Totp.VentanaDe(DateTimeOffset.UtcNow) + 1);
}
