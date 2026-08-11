using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Lo que le pasa a un equipo recordado cuando cambia algo importante en la cuenta.
///
/// <para><b>Un equipo recordado se salta el segundo factor entero</b>: en ese navegador, saber la
/// contraseña vuelve a ser suficiente durante treinta días. Eso es aceptable mientras la contraseña
/// sea secreta, y deja de serlo en el momento exacto en que alguien la cambia porque cree que dejó de
/// serlo. Estas pruebas cubren ese momento.</para>
///
/// <para>Casi todas comprueban que algo <b>desaparece</b>. Es a propósito: el fallo que persiguen no
/// se ve nunca desde fuera —la aplicación sigue funcionando igual de bien con la puerta de atrás
/// abierta— y solo se manifiesta el día que alguien intenta cerrar su cuenta y no se cierra.</para>
/// </summary>
public class EquiposRecordadosYSelloTests
{
    /// <summary>Un protector que marca en vez de cifrar: aquí no se prueba la protección de datos.</summary>
    private sealed class ProtectorDeJuguete : IProtectorDeSecretos
    {
        private const string Marca = "cifrado:";
        public string Proteger(string valorEnClaro) => Marca + valorEnClaro;
        public string? Desproteger(string cifrado) => cifrado.StartsWith(Marca) ? cifrado[Marca.Length..] : null;
    }

    /// <summary>Un dibujante que no dibuja: ninguna de estas pruebas mira un código QR.</summary>
    private sealed class DibujanteQueNoDibuja : IDibujanteDeCodigoQr
    {
        public byte[] DibujarPng(string contenido) => [0];
    }

    private static SegundoFactorService Segundo(AppDbContext db, ICurrentUser usuario) =>
        new(db, usuario, new ProtectorDeJuguete(), new DibujanteQueNoDibuja(),
            new AuditService(db, usuario, new OrigenDePrueba()));

    private static User CrearUsuario(AppDbContext db, int id = 7, string nombre = "ana")
    {
        var u = new User
        {
            Id = id,
            Username = nombre,
            FullName = nombre,
            PasswordHash = PasswordHasher.Hash("LaDeSiempre123"),
            IsActive = true,
            Role = UserRole.Desarrollador,
            SecurityStamp = "sello-inicial"
        };
        db.Users.Add(u);
        db.SaveChanges();
        return u;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  LO QUE SE LLEVA POR DELANTE UN CAMBIO DE CONTRASEÑA
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// El caso que da sentido a todo lo demás: «me robaron la cuenta, cambié la contraseña».
    ///
    /// <para>Si el equipo recordado del ladrón sobreviviera al cambio, la víctima habría cambiado la
    /// cerradura de la puerta de delante dejando la de atrás como estaba: el ladrón ya no sabe la
    /// contraseña nueva, cierto, pero el día que la averigüe entrará sin que se le pida el código.
    /// Cambiar la contraseña tiene que cerrar las dos.</para>
    /// </summary>
    [Fact]
    public async Task AlCambiarLaContrasena_LosEquiposRecordadosDejanDeValer()
    {
        using var db = TestDb.New();
        var user = CrearUsuario(db);
        var yo = UsuarioDePrueba.Como(UserRole.Desarrollador, userId: user.Id);

        var segundo = Segundo(db, yo);
        var testigo = await segundo.RecordarEsteEquipoAsync(user.Id, "Chrome en Windows");
        Assert.True(await segundo.EsEquipoRecordadoAsync(user.Id, testigo));

        await Fabrica.Auth(db, yo).ChangePasswordAsync(user.Id, "UnaClaveNueva123");

        Assert.False(await segundo.EsEquipoRecordadoAsync(user.Id, testigo));
        Assert.Empty(await db.UserTrustedDevices.Where(d => d.UserId == user.Id).ToListAsync());
    }

    /// <summary>
    /// Y también cuando la contraseña la restablece el líder, que es el otro camino por el que se
    /// llega aquí: «no puedo entrar, ponme una nueva».
    ///
    /// <para>Va en su propia prueba y no en la de arriba porque son dos métodos distintos del
    /// servicio: uno solo probaría la mitad, y la mitad que se dejara suelta sería una puerta de atrás
    /// abierta justo en el camino que se usa cuando algo raro está pasando con una cuenta.</para>
    /// </summary>
    [Fact]
    public async Task AlRestablecerLaContrasenaElLider_LosEquiposRecordadosDejanDeValer()
    {
        using var db = TestDb.New();
        var user = CrearUsuario(db);

        var testigo = await Segundo(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: user.Id))
            .RecordarEsteEquipoAsync(user.Id, "Firefox en Linux");

        var lider = UsuarioDePrueba.Como(UserRole.Admin, userId: 1);
        var (ok, _, temporal) = await Fabrica.Auth(db, lider).ResetPasswordAsync(user.Id);

        Assert.True(ok);
        Assert.False(string.IsNullOrWhiteSpace(temporal));
        Assert.False(await Segundo(db, lider).EsEquipoRecordadoAsync(user.Id, testigo));
    }

    /// <summary>
    /// El sello de seguridad y los equipos recordados se mueven JUNTOS, en el mismo guardado.
    ///
    /// <para>Es lo que esta prueba fija por escrito: no basta con que las dos cosas ocurran, tienen
    /// que ocurrir a la vez. Un instante con el sello nuevo y los equipos viejos sería una ventana
    /// —pequeña, pero real— en la que la cuenta está a medio cerrar.</para>
    /// </summary>
    [Fact]
    public async Task ElSelloYLosEquipos_CambianEnElMismoActo()
    {
        using var db = TestDb.New();
        var user = CrearUsuario(db);
        var yo = UsuarioDePrueba.Como(UserRole.Desarrollador, userId: user.Id);

        await Segundo(db, yo).RecordarEsteEquipoAsync(user.Id, "Edge en Windows");
        var selloViejo = user.SecurityStamp;

        await Fabrica.Auth(db, yo).ChangePasswordAsync(user.Id, "OtraClaveMas123");

        var fresco = await db.Users.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        Assert.NotEqual(selloViejo, fresco.SecurityStamp);
        Assert.Equal(0, await db.UserTrustedDevices.CountAsync(d => d.UserId == user.Id));
    }

    /// <summary>
    /// Cambiar la contraseña de una cuenta NO toca los equipos recordados de las demás.
    ///
    /// <para>Parece una obviedad y es justo el tipo de cosa que se rompe al escribir un
    /// <c>RemoveRange</c> sin su filtro: el error no falla, solo echa a todo el mundo de sus equipos y
    /// nadie sabe por qué le vuelven a pedir el código.</para>
    /// </summary>
    [Fact]
    public async Task CambiarLaContrasenaDeUno_NoDesconfiaDeLosEquiposDeOtro()
    {
        using var db = TestDb.New();
        var ana = CrearUsuario(db, id: 7, nombre: "ana");
        var beto = CrearUsuario(db, id: 8, nombre: "beto");

        var segundo = Segundo(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 1));
        var deAna = await segundo.RecordarEsteEquipoAsync(ana.Id, "el de Ana");
        var deBeto = await segundo.RecordarEsteEquipoAsync(beto.Id, "el de Beto");

        await Fabrica.Auth(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: ana.Id))
            .ChangePasswordAsync(ana.Id, "SoloLaDeAna123");

        Assert.False(await segundo.EsEquipoRecordadoAsync(ana.Id, deAna));
        Assert.True(await segundo.EsEquipoRecordadoAsync(beto.Id, deBeto));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  LA LISTA Y EL «OLVIDARLOS TODOS»
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// La lista enseña los vigentes y NO los vencidos.
    ///
    /// <para>La fila de uno vencido sigue en la base hasta que alguien vuelve a usar ese navegador y
    /// se limpia sola. Enseñarla haría creer que se confía en un equipo en el que ya no se confía, y
    /// quien la viera pulsaría «olvidarlos todos» por algo que ya no existía.</para>
    /// </summary>
    [Fact]
    public async Task LaLista_DejaFueraLosVencidos()
    {
        using var db = TestDb.New();
        var user = CrearUsuario(db);

        db.UserTrustedDevices.Add(new UserTrustedDevice
        {
            UserId = user.Id,
            TokenHash = new string('a', 64),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-40),
            ExpiraEnUtc = DateTime.UtcNow.AddDays(-10),
            Descripcion = "el del año pasado"
        });
        db.UserTrustedDevices.Add(new UserTrustedDevice
        {
            UserId = user.Id,
            TokenHash = new string('b', 64),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiraEnUtc = DateTime.UtcNow.AddDays(29),
            Descripcion = "el de esta semana"
        });
        await db.SaveChangesAsync();

        var equipos = await Fabrica.Auth(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: user.Id))
            .EquiposRecordadosAsync(user.Id);

        Assert.Single(equipos);
        Assert.Equal("el de esta semana", equipos[0].Descripcion);
    }

    /// <summary>
    /// «Olvidarlos todos» los borra de verdad y deja asiento en la bitácora.
    ///
    /// <para>El asiento importa tanto como el borrado: quitar una protección —aunque sea la propia—
    /// tiene que poder mirarse después.</para>
    /// </summary>
    [Fact]
    public async Task OlvidarLosEquipos_LosBorraYQuedaAnotado()
    {
        using var db = TestDb.New();
        var user = CrearUsuario(db);
        var yo = UsuarioDePrueba.Como(UserRole.Desarrollador, userId: user.Id);

        var segundo = Segundo(db, yo);
        var uno = await segundo.RecordarEsteEquipoAsync(user.Id, "el de casa");
        var otro = await segundo.RecordarEsteEquipoAsync(user.Id, "el de la oficina");

        int cuantos = await Fabrica.Auth(db, yo).OlvidarEquiposRecordadosAsync(user.Id);

        Assert.Equal(2, cuantos);
        Assert.False(await segundo.EsEquipoRecordadoAsync(user.Id, uno));
        Assert.False(await segundo.EsEquipoRecordadoAsync(user.Id, otro));

        var apuntes = await db.AuditLogs.Where(a => a.EntityType == "User").ToListAsync();
        Assert.Contains(apuntes, a => a.Details != null && a.Details.Contains("dejó de confiar"));
    }

    /// <summary>Sin equipos que olvidar no se inventa ninguno, y devuelve cero sin quejarse.</summary>
    [Fact]
    public async Task OlvidarLosEquipos_SinNinguno_DevuelveCero()
    {
        using var db = TestDb.New();
        var user = CrearUsuario(db);

        int cuantos = await Fabrica.Auth(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: user.Id))
            .OlvidarEquiposRecordadosAsync(user.Id);

        Assert.Equal(0, cuantos);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  LA BITÁCORA DEL ACCESO
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Acertar la contraseña y entrar dejaron de ser lo mismo, y la bitácora tiene que distinguirlos.
    ///
    /// <para>Sin esa distinción, una ráfaga de contraseñas acertadas que nunca llegan a una entrada
    /// completa —la firma de alguien con credenciales robadas estrellándose contra el segundo factor—
    /// aparecería en la bitácora exactamente igual que una mañana normal.</para>
    /// </summary>
    [Fact]
    public async Task ConSegundoFactorActivo_LaContrasenaAcertada_NoSeAnotaComoEntrada()
    {
        using var db = TestDb.New();
        var user = CrearUsuario(db);
        user.SegundoFactorActivo = true;
        await db.SaveChangesAsync();

        var auth = Fabrica.Auth(db, UsuarioDePrueba.Anonimo());
        var resultado = await auth.LoginAsync("ana", "LaDeSiempre123");

        Assert.True(resultado.Exito);

        var apunte = await db.AuditLogs.OrderBy(a => a.Id).LastAsync();
        Assert.Contains("falta el segundo factor", apunte.Details);
        Assert.DoesNotContain("Login exitoso", apunte.Details);
    }

    /// <summary>Y sin segundo factor, la contraseña SÍ es la entrada entera: se anota como tal.</summary>
    [Fact]
    public async Task SinSegundoFactor_LaContrasenaAcertada_SeAnotaComoEntrada()
    {
        using var db = TestDb.New();
        CrearUsuario(db);

        await Fabrica.Auth(db, UsuarioDePrueba.Anonimo()).LoginAsync("ana", "LaDeSiempre123");

        var apunte = await db.AuditLogs.OrderBy(a => a.Id).LastAsync();
        Assert.Contains("Login exitoso", apunte.Details);
    }
}
