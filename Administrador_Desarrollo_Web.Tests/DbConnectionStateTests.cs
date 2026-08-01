using Administrador_Desarrollo_Web.Data;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// El estado de la conexión es de la APLICACIÓN, no de la ventana de inicio de sesión.
///
/// Estas pruebas fijan lo que se rompió: al cerrar sesión se construye otra pantalla de inicio de
/// sesión, y cuando el estado lo guardaba el propio formulario esa segunda pantalla nacía sin saber
/// nada y pintaba el ● en gris — que se lee como «me desconecté» justo después de haber estado
/// trabajando sin problema.
/// </summary>
public class DbConnectionStateTests
{
    [Fact]
    public void ElEstadoSobreviveAQuienLoPregunte()
    {
        var estado = new DbConnectionState();
        estado.Configurar(DbConnectionStatus.Ok(), null);

        // Da igual cuántas pantallas lo consulten: todas ven lo mismo.
        Assert.True(estado.Estado.Conectado);
        Assert.True(estado.Estado.Conectado);
        Assert.Contains("Conectado", estado.Estado.Titulo);
    }

    [Fact]
    public void SinConfigurar_QuedaEnComprobando_NoEnConectado()
    {
        // Antes de que el arranque diga nada, el indicador no debe afirmar que hay conexión.
        var estado = new DbConnectionState();

        Assert.False(estado.Estado.Conectado);
        Assert.Equal(DbConnectionStatus.Comprobando, estado.Estado);
    }

    [Fact]
    public void SinCadenaQueProbar_NoSeOfreceReintentar()
    {
        var estado = new DbConnectionState();
        estado.Configurar(DbConnectionStatus.SinConexion(), reintento: null);

        Assert.False(estado.SePuedeReintentar);
    }

    [Fact]
    public void ConCadenaQueProbar_SeOfreceReintentar()
    {
        var estado = new DbConnectionState();
        estado.Configurar(DbConnectionStatus.Fallo(new Exception("sin red")),
            () => Task.FromResult(DbConnectionStatus.Ok()));

        Assert.True(estado.SePuedeReintentar);
    }

    [Fact]
    public async Task UnReintentoConExito_SeGuarda()
    {
        // Levantan la VPN, el usuario reintenta y entra. Al cerrar sesión, la siguiente pantalla
        // tiene que partir del estado BUENO, no del fallo del arranque.
        var estado = new DbConnectionState();
        estado.Configurar(DbConnectionStatus.Fallo(new Exception("Revisa tu red o VPN.")),
            () => Task.FromResult(DbConnectionStatus.Ok()));

        Assert.False(estado.Estado.Conectado);

        var resultado = await estado.ReintentarAsync();

        Assert.True(resultado.Conectado);
        Assert.True(estado.Estado.Conectado);
    }

    [Fact]
    public async Task UnReintentoFallido_TambienSeGuarda()
    {
        var estado = new DbConnectionState();
        estado.Configurar(DbConnectionStatus.Ok(),
            () => Task.FromResult(DbConnectionStatus.Fallo(new Exception("Revisa tu red o VPN."))));

        await estado.ReintentarAsync();

        Assert.False(estado.Estado.Conectado);
        Assert.Contains("VPN", estado.Estado.Detalle);
    }

    [Fact]
    public async Task ReintentarSinNadaQueProbar_DevuelveElEstadoActualSinTocarlo()
    {
        var estado = new DbConnectionState();
        estado.Configurar(DbConnectionStatus.SinConexion(), reintento: null);

        var resultado = await estado.ReintentarAsync();

        Assert.False(resultado.Conectado);
        Assert.Equal(DbConnectionStatus.SinConexion(), estado.Estado);
    }
}
