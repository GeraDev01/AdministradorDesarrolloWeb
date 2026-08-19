using System.Globalization;
using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LA HORA QUE ESCRIBE EL SERVIDOR.
///
/// <para><b>De dónde sale esto.</b> El App Service corre en UTC y nadie le puso
/// <c>WEBSITE_TIME_ZONE</c>, así que un <c>ToLocalTime()</c> calculado allá no convierte nada: un
/// tramo empezado a las 15:30 se publicaría como las 21:30. En las pantallas no pasa —Blazor
/// WebAssembly corre en el navegador, con el reloj de quien mira— pero el comentario que va a Azure
/// DevOps se compone en el servidor.</para>
///
/// <para>Lo que estas pruebas fijan, además de la conversión, es que esto <b>no puede lanzar</b>: el
/// identificador lo teclea una persona en una pantalla, y un comentario que no sale porque sobró un
/// espacio sería peor que una hora dicha en la zona por omisión.</para>
/// </summary>
public class HoraDeLaOrganizacionTests
{
    /// <summary>Las 21:30 UTC del 18 de agosto de 2026: en Ciudad de México son las 15:30.</summary>
    private static readonly DateTime Instante = new(2026, 8, 18, 21, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void SinConfigurar_esCiudadDeMexico()
    {
        Assert.Equal("18/08/2026 15:30", HoraDeLaOrganizacion.Texto(Instante, HoraDeLaOrganizacion.Zona(null)));
    }

    /// <summary>Los dos catálogos valen: producción es Linux (IANA) y el desarrollo es Windows.</summary>
    [Theory]
    [InlineData("America/Mexico_City")]
    [InlineData("Central Standard Time (Mexico)")]
    [InlineData("  America/Mexico_City  ")]
    public void LosDosEstilosDeIdentificadorResuelvenALoMismo(string identificador)
    {
        Assert.Equal("18/08/2026 15:30", HoraDeLaOrganizacion.Texto(Instante, HoraDeLaOrganizacion.Zona(identificador)));
    }

    /// <summary>Otra zona de verdad, para que la prueba anterior no pase por no convertir nada.</summary>
    [Fact]
    public void OtraZonaDaOtraHora()
    {
        Assert.Equal("18/08/2026 21:30", HoraDeLaOrganizacion.Texto(Instante, HoraDeLaOrganizacion.Zona("UTC")));
    }

    /// <summary>
    /// LA TRADUCCIÓN ENTRE CATÁLOGOS, probada donde sí puede fallar.
    ///
    /// <para>Que «America/Mexico_City» y «Central Standard Time (Mexico)» den los dos las 15:30 no
    /// demuestra nada por sí solo: es también lo que saldría si NINGUNO de los dos se resolviera y
    /// los dos cayeran en la zona por omisión, que es Ciudad de México. Con una zona distinta de la
    /// de omisión, el respaldo daría otra hora y la prueba se pondría roja —que es lo que se le
    /// pide a una prueba—.</para>
    ///
    /// <para>Importa porque producción es Linux y el desarrollo es Windows: si la traducción no
    /// funcionara en uno de los dos, la hora saldría mal justo en el sitio donde nadie la mira.</para>
    /// </summary>
    [Theory]
    [InlineData("Europe/Madrid")]              // IANA
    [InlineData("Romance Standard Time")]      // el mismo huso, catalogo de Windows
    public void LosDosCatalogosTraducenAUnaZonaQueNoEsLaDeOmision(string identificador)
    {
        Assert.Equal("18/08/2026 23:30", HoraDeLaOrganizacion.Texto(Instante, HoraDeLaOrganizacion.Zona(identificador)));
    }

    /// <summary>
    /// El requisito principal: un identificador imposible NO tumba nada. Se cae en la zona por
    /// omisión, que es una hora discutible pero un comentario publicado.
    /// </summary>
    [Theory]
    [InlineData("Zona/Inventada")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void UnIdentificadorImposibleNoLanza_caeEnLaZonaPorOmision(string? identificador)
    {
        Assert.Equal("18/08/2026 15:30", HoraDeLaOrganizacion.Texto(Instante, HoraDeLaOrganizacion.Zona(identificador)));
    }

    /// <summary>
    /// El huso va dicho en claro porque el comentario lo firma una cuenta compartida y lo puede leer
    /// alguien que no esté en esta zona. Y sale del INSTANTE, no de la zona: en agosto Ciudad de
    /// México está a −06:00.
    /// </summary>
    [Fact]
    public void ElHusoSeDiceEnClaro()
    {
        Assert.Equal("18/08/2026 15:30 (UTC-06:00)",
                     HoraDeLaOrganizacion.TextoConHuso(Instante, HoraDeLaOrganizacion.Zona(null)));
    }

    [Fact]
    public void ElHusoPositivoLlevaSuSigno()
    {
        Assert.EndsWith("(UTC+02:00)",
                        HoraDeLaOrganizacion.TextoConHuso(Instante, HoraDeLaOrganizacion.Zona("Europe/Madrid")));
    }

    /// <summary>
    /// Un <c>DateTime</c> con <c>Kind == Local</c> no puede tumbar la publicación:
    /// <c>ConvertTimeFromUtc</c> lanza con ése, y basta con que alguien meta un <c>DateTime.Now</c>
    /// por el camino. Lo que llega se trata SIEMPRE como UTC, que es lo que es en esta aplicación.
    /// </summary>
    [Fact]
    public void UnInstanteMarcadoComoLocalNoLanza()
    {
        var comoLocal = DateTime.SpecifyKind(Instante, DateTimeKind.Local);
        var sinMarcar = DateTime.SpecifyKind(Instante, DateTimeKind.Unspecified);

        Assert.Equal("18/08/2026 15:30", HoraDeLaOrganizacion.Texto(comoLocal, HoraDeLaOrganizacion.Zona(null)));
        Assert.Equal("18/08/2026 15:30", HoraDeLaOrganizacion.Texto(sinMarcar, HoraDeLaOrganizacion.Zona(null)));
    }

    /// <summary>
    /// La cultura del proceso no la fija nadie: sale del sistema operativo. Sin cultura invariante,
    /// la barra de «dd/MM/yyyy» la sustituye el separador de esa cultura y el mismo código escribe
    /// una cosa aquí y otra en el servidor.
    /// </summary>
    [Fact]
    public void LaCulturaDelProcesoNoCambiaLoQueSeEscribe()
    {
        var antes = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("18/08/2026 15:30", HoraDeLaOrganizacion.Texto(Instante, HoraDeLaOrganizacion.Zona(null)));
        }
        finally { CultureInfo.CurrentCulture = antes; }
    }

    /// <summary>Y de punta a punta, leyendo el ajuste de la base como lo hará quien comente.</summary>
    [Fact]
    public async Task LaZonaSaleDelAjusteGuardado()
    {
        using var db = TestDb.New();
        var cu = UsuarioDePrueba.Como(UserRole.Admin, userId: 1);
        var ajustes = new SettingsService(db, cu, new AuditService(db, cu, new OrigenDePrueba()));

        Assert.Equal("18/08/2026 15:30",
                     HoraDeLaOrganizacion.Texto(Instante, await ajustes.ObtenerZonaHorariaAsync()));

        db.AppSettings.Add(new AppSetting { Key = SettingsService.Claves.ZonaHoraria, Value = "UTC" });
        await db.SaveChangesAsync();

        Assert.Equal("18/08/2026 21:30",
                     HoraDeLaOrganizacion.Texto(Instante, await ajustes.ObtenerZonaHorariaAsync()));
    }
}
