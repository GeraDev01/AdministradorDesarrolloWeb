using Administrador_Desarrollo_Web.Data;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Lo que dice el indicador ● de la pantalla de inicio de sesión. Aquí importa el TEXTO: es lo único
/// que un desarrollador puede leerle al administrador para saber si su aplicación está hablando con
/// la base del equipo, sin tener que abrir el log.
///
/// Regla firme: esa pantalla se ve ANTES de autenticarse, así que el indicador dice si hay conexión
/// o no y nada más. Ni servidor, ni base, ni usuario, ni el mensaje crudo de SQL Server (que trae el
/// host dentro). Todo eso vive en el log.
/// </summary>
public class DbConnectionStatusTests
{
    private const string Servidor = "sql-equipo.contoso.com";
    private const string Base     = "SOLTUM_DEV_WD";

    [Fact]
    public void EnVerdeSoloDiceQueEstaConectado()
    {
        var estado = DbConnectionStatus.Ok();

        Assert.True(estado.Conectado);
        Assert.Contains("Conectado", estado.Titulo);
    }

    [Fact]
    public void NingunEstadoFiltraServidorNiBaseNiContrasena()
    {
        DbConnectionStatus[] estados =
        [
            DbConnectionStatus.Ok(),
            DbConnectionStatus.SinConexion(),
            DbConnectionStatus.Comprobando,
            // Un fallo cuyo mensaje trae justo lo que no debe salir en pantalla.
            DbConnectionStatus.Fallo(new Exception(
                $"Cannot open server '{Servidor}' database '{Base}' for user 'app_dev' password 'secreta'"))
        ];

        foreach (var estado in estados)
        {
            var textoEnPantalla = estado.Titulo + " " + estado.Detalle;
            Assert.DoesNotContain(Servidor, textoEnPantalla);
            Assert.DoesNotContain(Base, textoEnPantalla);
            Assert.DoesNotContain("secreta", textoEnPantalla);
        }
    }

    [Fact]
    public void SinConexionDiceQueNoTrabajaConUnaBaseLocal()
    {
        var estado = DbConnectionStatus.SinConexion();

        Assert.False(estado.Conectado);
        Assert.Contains("No se pudo conectar", estado.Titulo);
        Assert.Contains("base local", estado.Detalle);
        // El desarrollador no puede arreglar esto solo: el mensaje tiene que apuntar al administrador.
        Assert.Contains("build-app.ps1", estado.Detalle);
    }

    [Fact]
    public void UnFalloDaUnMotivoAccionableEnUnaLinea()
    {
        var estado = DbConnectionStatus.Fallo(new InvalidOperationException("Revisa tu red o VPN."));

        Assert.False(estado.Conectado);
        Assert.Contains("No se pudo conectar", estado.Titulo);
        // Sin número de error ni jerga: lo que el desarrollador puede hacer al respecto.
        Assert.Contains("VPN", estado.Detalle);
        Assert.DoesNotContain("\n", estado.Detalle);
    }
}
