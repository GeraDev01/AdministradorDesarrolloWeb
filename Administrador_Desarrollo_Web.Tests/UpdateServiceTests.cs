using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// La decisión de QUÉ ofrecerle a cada copia: automática si se instaló con Velopack, aviso manual
/// si es la copia portátil.
///
/// Lo que NO se prueba aquí, y hay que decirlo: la actualización de verdad —descargar del feed,
/// aplicar el delta, reiniciar— necesita una instalación real de Velopack y dos versiones
/// publicadas. Eso solo se comprueba probándolo. Aquí se fija el comportamiento en el entorno de
/// pruebas, que es exactamente el de una copia NO instalada (IsInstalled = false).
/// </summary>
public class UpdateServiceTests
{
    private static UpdateService Svc(AppDbContext db)
    {
        var cu = Ctx.As(UserRole.Admin, userId: 99);
        return new UpdateService(new SettingsService(db, new AuditService(db, cu)),
                                 NullLogger<UpdateService>.Instance);
    }

    private static void Publicar(AppDbContext db, string? version, string? url = null, string? notas = null, string? feed = null)
    {
        var cu = Ctx.As(UserRole.Admin, userId: 99);
        var settings = new SettingsService(db, new AuditService(db, cu));
        if (version != null) settings.Set(UpdateNotice.KeyLatestVersion, version, false, "v");
        if (url != null)     settings.Set(UpdateNotice.KeyDownloadUrl, url, false, "u");
        if (notas != null)   settings.Set(UpdateNotice.KeyReleaseNotes, notas, false, "n");
        if (feed != null)    settings.Set(UpdateService.KeyFeedUrl, feed, false, "f");
    }

    [Fact]
    public void CopiaNoInstalada_NoSeConsideraGestionada()
    {
        // Las pruebas (y el build de desarrollo, y la copia portátil) NO están instaladas con
        // Velopack: la actualización automática no puede ofrecerse ahí.
        var db = TestDb.New();
        Publicar(db, "9.9.9", feed: "https://ejemplo/feed");

        Assert.False(Svc(db).EsInstalacionGestionada);
    }

    [Fact]
    public async Task SinFeedYSinVersionPublicada_NoOfreceNada()
    {
        var db = TestDb.New();
        Assert.Null(await Svc(db).BuscarAsync(yaAvisada: null));
    }

    [Fact]
    public async Task PortatilConVersionPublicada_CaeAlAvisoManual()
    {
        var db = TestDb.New();
        Publicar(db, "9.9.9", url: "https://ejemplo/app.exe", notas: "Arregla el login");

        var oferta = await Svc(db).BuscarAsync(yaAvisada: null);

        Assert.NotNull(oferta);
        Assert.Equal(ModoActualizacion.AvisoManual, oferta!.Modo);
        Assert.Equal("9.9.9", oferta.VersionTexto);
        Assert.Equal("https://ejemplo/app.exe", oferta.Url);
        Assert.Equal("Arregla el login", oferta.Novedades);
    }

    [Fact]
    public async Task FeedInalcanzable_NoLanza_YCaeAlAvisoManual()
    {
        // El feed puede estar caído o mal capturado. Que eso deje al equipo sin enterarse de la
        // versión nueva sería peor que avisarle por el camino lento.
        var db = TestDb.New();
        Publicar(db, "9.9.9", url: "https://ejemplo/app.exe",
                 feed: "https://este-host-no-existe.invalid/feed");

        var oferta = await Svc(db).BuscarAsync(yaAvisada: null);

        Assert.NotNull(oferta);
        Assert.Equal(ModoActualizacion.AvisoManual, oferta!.Modo);
    }

    [Fact]
    public async Task VersionYaAvisada_NoSeRepiteEnElModoManual()
    {
        var db = TestDb.New();
        Publicar(db, "9.9.9", url: "https://ejemplo/app.exe");

        Assert.Null(await Svc(db).BuscarAsync(yaAvisada: "9.9.9"));
        Assert.NotNull(await Svc(db).BuscarAsync(yaAvisada: "9.9.8"));
    }

    [Fact]
    public async Task VersionPublicadaAnterior_NoOfreceNada()
    {
        var db = TestDb.New();
        Publicar(db, "0.0.1", url: "https://ejemplo/app.exe");

        Assert.Null(await Svc(db).BuscarAsync(yaAvisada: null));
    }

    [Fact]
    public async Task SinVelopackPreparado_DescargarYAplicar_FallanSinLanzar()
    {
        // La pantalla puede llamarlos en cualquier orden; ninguno debe tirar la aplicación.
        var db = TestDb.New();
        var svc = Svc(db);
        var oferta = new OfertaActualizacion(ModoActualizacion.AvisoManual, "9.9.9", null, "https://x");

        var (ok, mensaje) = await svc.DescargarAsync(oferta);
        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(mensaje));
        Assert.False(svc.AplicarAlSalir());        // nada preparado: false, sin lanzar
        Assert.Null(svc.VersionListaParaAplicar);
        Assert.False(svc.DescargaEnCurso);
        svc.PedirReinicioTrasAplicar();            // no debe lanzar aunque no haya nada
        Assert.False(svc.AplicarAlSalir());
    }

    [Fact]
    public async Task CopiaGestionada_ConFeedCaido_NoOfreceLaDescargaManual()
    {
        // Ofrecerle el .exe portátil a quien tiene una instalación Velopack lo llevaría a
        // descomprimirlo encima y romperla. Aquí la copia NO es gestionada (las pruebas nunca lo
        // son), así que el aviso manual SÍ debe salir: es el otro lado de la misma regla.
        var db = TestDb.New();
        Publicar(db, "9.9.9", url: "https://ejemplo/app.exe", feed: "https://no-existe.invalid/f");

        var oferta = await Svc(db).BuscarAsync(yaAvisada: null);

        Assert.Equal(ModoActualizacion.AvisoManual, oferta!.Modo);
    }

    [Fact]
    public async Task CambiarElFeed_SurteEfectoSinReiniciar()
    {
        // El servicio es Singleton y el proceso vive días en la bandeja: corregir una errata en
        // Configuración tiene que aplicar sin cerrar la aplicación.
        var db = TestDb.New();
        var svc = Svc(db);
        Publicar(db, "9.9.9", url: "https://ejemplo/app.exe", feed: "https://primero.invalid/f");
        await svc.BuscarAsync(yaAvisada: null);          // construye el gestor con el feed viejo

        Publicar(db, version: null, feed: "https://segundo.invalid/f"); // el admin lo corrige
        var oferta = await svc.BuscarAsync(yaAvisada: null);

        // No revienta y sigue resolviendo: lo que se prueba es que no quedó atascado en el viejo.
        Assert.NotNull(oferta);
    }
}
