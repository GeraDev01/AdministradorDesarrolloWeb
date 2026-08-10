using System.Net.Http.Json;
using AdminWeb.Api.TiempoReal;
using AdminWeb.Application.Services;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Personas;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// La presencia de una cuenta que NO registra jornada, de punta a punta y con la API levantada de
/// verdad.
///
/// <para><b>Por qué esto no se puede probar solo en la capa de aplicación.</b> Allí
/// <c>PresenceService</c> recibe un doble de <see cref="IConexionesEnVivo"/> y todo funciona; el
/// dato real de «quién está dentro» lo tiene la capa web, en <see cref="RegistroDeConexiones"/>, y
/// entre las dos hay una junta que se puede quedar sin atar. Y se queda sin atar EN SILENCIO: el
/// parámetro de <c>PresenceService</c> es opcional —para no obligar a media docena de pruebas a
/// inventarse un doble—, así que sin el registro en el contenedor el servicio se construye igual,
/// sin error y sin advertencia, y todos los operativos salen «Desconectado» para siempre, incluso
/// mientras están desplegando. No lo delata nada más que mirar la pantalla.</para>
///
/// <para>Por eso estas pruebas resuelven el registro DEL MISMO contenedor que usa el hub y luego
/// preguntan por HTTP: si alguien registrara otra instancia, o ninguna, el tablero contestaría
/// «desconectado» y la primera prueba caería.</para>
/// </summary>
public class PresenciaDeOperacionesTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    private const string Usuario = "ops.presencia";

    private async Task<int> IdDelOperativoAsync()
    {
        await api.ClienteComoAsync(UserRole.Operaciones, Usuario);

        using var ambito = api.Services.CreateScope();
        var db = ambito.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.Where(u => u.Username == Usuario).Select(u => u.Id).SingleAsync();
    }

    private static async Task<PresenteDto> FilaAsync(HttpClient admin, int userId)
    {
        var tablero = await admin.GetFromJsonAsync<TableroDePresenciaDto>("/api/personas/presencia");
        Assert.NotNull(tablero);
        return tablero!.Personas.Single(p => p.UserId == userId);
    }

    [Fact]
    public async Task UnOperativoConectado_SaleDisponibleYSinAbrirleJornada()
    {
        var userId = await IdDelOperativoAsync();
        var admin = await api.ClienteAdminAsync();

        // Esto es lo que hace el hub al abrirse un socket. Se hace a mano porque estas pruebas hablan
        // por HTTP y no levantan la conexión de tiempo real.
        var conexiones = api.Services.GetRequiredService<RegistroDeConexiones>();
        conexiones.Entra(userId);

        try
        {
            var fila = await FilaAsync(admin, userId);

            Assert.True(fila.Conectado);
            Assert.Equal(PresenceState.Disponible, fila.Estado);

            // Y la mitad que se olvida: el indicador tiene que LLEGAR a la pantalla. Se calculaba en
            // PresenceService y se perdía al mapear el DTO, así que la columna «Desde» seguía
            // diciendo «nunca ha entrado» de alguien conectado en ese mismo instante.
            Assert.False(fila.RegistraJornada);
        }
        finally
        {
            conexiones.Sale(userId);
        }

        // Estar dentro NO le abre jornada: eso es lo que se quitó, y una fila abierta aquí lo delata.
        using var ambito = api.Services.CreateScope();
        var db = ambito.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.WorkPresences.AnyAsync(w => w.UserId == userId));
    }

    [Fact]
    public async Task UnOperativoDESCONECTADO_NoSalePintadoComoDisponible()
    {
        // Es la otra mitad y la que de verdad importa: un tablero que da por disponible a quien no
        // está deja de servir para lo único que sirve.
        var userId = await IdDelOperativoAsync();
        var admin = await api.ClienteAdminAsync();

        var fila = await FilaAsync(admin, userId);

        Assert.False(fila.Conectado);
        Assert.Equal(PresenceState.Ausente, fila.Estado);
        Assert.False(fila.RegistraJornada);
    }
}
