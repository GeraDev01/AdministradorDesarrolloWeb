using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Los secretos personales guardados del lado del servidor.
///
/// Sustituyen al token de Azure DevOps que en el escritorio vivía cifrado con DPAPI en la máquina de
/// cada quien. Lo que estas pruebas cuidan es la promesa del sustituto: que el valor se guarde
/// cifrado, que sea de una sola persona, y que **nunca** aparezca en claro donde no debe — ni
/// siquiera en la bitácora.
/// </summary>
public class UserSecretsServiceTests
{
    /// <summary>
    /// Un protector de mentira: marca el texto en vez de cifrarlo. Sirve para comprobar que el
    /// servicio cifra ANTES de guardar y descifra al leer, sin depender de la protección de datos
    /// de ASP.NET (que vive en la API y necesita su propio arranque).
    /// </summary>
    private sealed class ProtectorDePrueba : IProtectorDeSecretos
    {
        private const string Marca = "cifrado:";
        public string Proteger(string valorEnClaro) => Marca + valorEnClaro;
        public string? Desproteger(string cifrado) =>
            cifrado.StartsWith(Marca) ? cifrado[Marca.Length..] : null;
    }

    private static UserSecretsService Svc(AppDbContext db, ICurrentUser usuario) =>
        new(db, usuario, new ProtectorDePrueba(), new AuditService(db, usuario, new OrigenDePrueba()));

    private static void CrearUsuario(AppDbContext db, int id, string nombre)
    {
        db.Users.Add(new User { Id = id, Username = nombre, FullName = nombre, PasswordHash = "x", IsActive = true });
        db.SaveChanges();
    }

    [Fact]
    public async Task GuardarYRecuperar_DevuelveElValorOriginal()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador));

        var (ok, _) = await svc.GuardarMioAsync(PropositosDeSecreto.PatDevOps, "mi-token-secreto");

        Assert.True(ok);
        Assert.Equal("mi-token-secreto", await svc.ObtenerMioEnClaroAsync(PropositosDeSecreto.PatDevOps));
    }

    [Fact]
    public async Task LoQueSeGuarda_NoEsElValorEnClaro()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");

        await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador))
            .GuardarMioAsync(PropositosDeSecreto.PatDevOps, "mi-token-secreto");

        var fila = db.UserSecrets.AsNoTracking().Single();
        Assert.DoesNotContain("mi-token-secreto", fila.CipherText[..8]);
        Assert.StartsWith("cifrado:", fila.CipherText);
    }

    [Fact]
    public async Task LaBitacora_NuncaGuardaElValor()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");

        await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador))
            .GuardarMioAsync(PropositosDeSecreto.PatDevOps, "mi-token-secreto");

        // A la bitácora va el hecho de que se configuró, jamás el secreto: una bitácora que guarda
        // secretos convierte el registro de auditoría en el sitio más goloso del sistema.
        var registros = db.AuditLogs.AsNoTracking().ToList();
        Assert.NotEmpty(registros);
        Assert.All(registros, r =>
        {
            Assert.DoesNotContain("mi-token-secreto", r.Details ?? "");
            Assert.DoesNotContain("mi-token-secreto", r.NewValues ?? "");
        });
    }

    [Fact]
    public async Task GuardarDosVeces_ReemplazaEnVezDeAcumular()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador));

        await svc.GuardarMioAsync(PropositosDeSecreto.PatDevOps, "viejo");
        await svc.GuardarMioAsync(PropositosDeSecreto.PatDevOps, "nuevo");

        Assert.Single(db.UserSecrets.AsNoTracking());
        Assert.Equal("nuevo", await svc.ObtenerMioEnClaroAsync(PropositosDeSecreto.PatDevOps));
    }

    [Fact]
    public async Task ElSecretoDeCadaQuien_EsSuyo()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        CrearUsuario(db, 2, "beto");

        await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 1))
            .GuardarMioAsync(PropositosDeSecreto.PatDevOps, "el de Ana");

        var deBeto = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 2));

        // Que cada comentario en DevOps quede firmado por su dueño es la razón de que el token sea
        // personal: con uno compartido, todos aparecerían a nombre de la misma cuenta.
        Assert.False(await deBeto.TengoConfiguradoAsync(PropositosDeSecreto.PatDevOps));
        Assert.Null(await deBeto.ObtenerMioEnClaroAsync(PropositosDeSecreto.PatDevOps));
    }

    [Fact]
    public async Task TengoConfigurado_EsLoUnicoQueSeLePuedeDecirALaPantalla()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador));

        Assert.False(await svc.TengoConfiguradoAsync(PropositosDeSecreto.PatDevOps));
        await svc.GuardarMioAsync(PropositosDeSecreto.PatDevOps, "token");
        Assert.True(await svc.TengoConfiguradoAsync(PropositosDeSecreto.PatDevOps));
    }

    [Fact]
    public async Task Borrar_DejaLaCuentaSinSecreto()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador));
        await svc.GuardarMioAsync(PropositosDeSecreto.PatDevOps, "token");

        var (ok, _) = await svc.BorrarMioAsync(PropositosDeSecreto.PatDevOps);

        Assert.True(ok);
        Assert.False(await svc.TengoConfiguradoAsync(PropositosDeSecreto.PatDevOps));
    }

    [Fact]
    public async Task UnSecretoQueYaNoSePuedeDescifrar_SeTrataComoNoConfigurado()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        db.UserSecrets.Add(new UserSecret
        {
            UserId = 1,
            Proposito = PropositosDeSecreto.PatDevOps,
            CipherText = "basura-que-no-descifra"
        });
        await db.SaveChangesAsync();

        // Pasa si se rota la llave o alguien manipula la fila. No debe tumbar la petición: la
        // persona simplemente vuelve a capturarlo.
        var enClaro = await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador))
            .ObtenerMioEnClaroAsync(PropositosDeSecreto.PatDevOps);

        Assert.Null(enClaro);
    }

    [Fact]
    public async Task SinSesion_NoSePuedeGuardarNada()
    {
        var db = TestDb.New();
        var svc = Svc(db, UsuarioDePrueba.Anonimo());

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.GuardarMioAsync(PropositosDeSecreto.PatDevOps, "token"));
    }

    [Fact]
    public async Task BorrarLaCuenta_SeLlevaSusSecretos()
    {
        var db = TestDb.New();
        CrearUsuario(db, 1, "ana");
        await Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador))
            .GuardarMioAsync(PropositosDeSecreto.PatDevOps, "token");

        db.Users.Remove(db.Users.Single(u => u.Id == 1));
        await db.SaveChangesAsync();

        Assert.Empty(db.UserSecrets.AsNoTracking());
    }
}
