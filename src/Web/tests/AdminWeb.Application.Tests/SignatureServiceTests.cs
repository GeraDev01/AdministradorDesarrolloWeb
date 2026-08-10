using AdminWeb.Application.Services;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Las firmas reutilizables, ahora que tienen gestor propio.
///
/// <para>Mientras solo se llegaba a ellas desde la pantalla de vacaciones, sus reglas viajaban de
/// hecho pegadas a esa pantalla. Con una pantalla propia en Administración conviene fijarlas donde
/// de verdad viven: <b>crear o borrar la firma COMPARTIDA es del líder</b> —si cualquiera pudiera,
/// cualquiera firmaría por él— y <b>los bytes del PNG no salen en ninguna lista</b>, solo por la
/// ruta de la imagen.</para>
///
/// <para>La imagen no se cifra, igual que en el escritorio: no es un secreto, y cifrarla con el
/// esquema por usuario rompería el compartir en cuanto la base es de todos.</para>
/// </summary>
public class SignatureServiceTests
{
    private const int AnaDevId = 31;
    private const int BetoDevId = 32;

    /// <summary>Un PNG de mentira: aquí solo importa que sean bytes y cuántos.</summary>
    private static readonly byte[] Trazo = [137, 80, 78, 71, 1, 2, 3];

    private static SignatureService Svc(AppDbContext db, ICurrentUser quien) =>
        new(db, quien, new AuditService(db, quien, new OrigenDePrueba()));

    /// <summary>
    /// Base con las dos personas dadas de alta: una firma con dueño lleva su clave foránea, así que
    /// sin ellas lo que falla es el esquema y no la regla que se quiere comprobar.
    /// </summary>
    private static AppDbContext Base()
    {
        var db = TestDb.New();
        db.Developers.Add(new Domain.Entities.Developer { Id = AnaDevId, FullName = "Ana", IsActive = true });
        db.Developers.Add(new Domain.Entities.Developer { Id = BetoDevId, FullName = "Beto", IsActive = true });
        db.SaveChanges();
        return db;
    }

    private static ICurrentUser Jefa => UsuarioDePrueba.Como(UserRole.Admin, userId: 900);
    private static ICurrentUser Ana => UsuarioDePrueba.Como(UserRole.Desarrollador, AnaDevId, userId: 31);
    private static ICurrentUser Beto => UsuarioDePrueba.Como(UserRole.Desarrollador, BetoDevId, userId: 32);

    // ── La firma compartida es del líder ────────────────────────────────────────

    [Fact]
    public async Task CrearLaCompartida_EsDelLider()
    {
        using var db = TestDb.New();

        // Dueño nulo = la firma del jefe, la que se estampa en lo que él autoriza.
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            Svc(db, Ana).CrearAsync("Jefe directo", Trazo, 300, 120, null, false));

        Assert.Empty(db.SignatureProfiles);
    }

    [Fact]
    public async Task CrearLaCompartida_GuardaLaImagenYSusMedidas()
    {
        using var db = TestDb.New();

        var (ok, _, id) = await Svc(db, Jefa).CrearAsync("Jefe directo", Trazo, 300, 120, null, true);

        Assert.True(ok);
        var firma = db.SignatureProfiles.AsNoTracking().Single();
        Assert.Equal(id, firma.Id);
        Assert.Equal(Trazo, firma.PngBytes);

        // Las medidas van con la imagen porque el documento las usa para colocarla sin deformarla.
        Assert.Equal(300, firma.WidthPx);
        Assert.Equal(120, firma.HeightPx);
        Assert.Null(firma.OwnerDeveloperId);
        Assert.True(firma.IsDefault);
    }

    [Fact]
    public async Task BorrarLaCompartida_EsDelLider()
    {
        using var db = TestDb.New();
        var (_, _, id) = await Svc(db, Jefa).CrearAsync("Jefe directo", Trazo, 300, 120, null, false);

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, Ana).EliminarAsync(id));
        Assert.Single(db.SignatureProfiles);
    }

    // ── Una firma propia es de su dueño ─────────────────────────────────────────

    [Fact]
    public async Task LaFirmaPropia_NoLaTocaOtro()
    {
        using var db = Base();
        var (_, _, id) = await Svc(db, Ana).CrearAsync("La mía", Trazo, 200, 80, AnaDevId, false);

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, Beto).RenombrarAsync(id, "Secuestrada"));
        Assert.Equal("La mía", db.SignatureProfiles.AsNoTracking().Single().DisplayName);
    }

    [Fact]
    public async Task NoSePuedeCrearUnaFirmaANombreDeOtro()
    {
        using var db = Base();

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            Svc(db, Beto).CrearAsync("De Ana", Trazo, 200, 80, AnaDevId, false));
    }

    // ── Los bytes no viajan en las listas ───────────────────────────────────────

    /// <summary>
    /// Con diez firmas guardadas, una lista que llevara los PNG dentro descargaría las diez para
    /// acabar enseñando diez nombres. El único camino de los bytes es <c>ImagenAsync</c>.
    /// </summary>
    [Fact]
    public async Task LasListas_NoLlevanLaImagen()
    {
        using var db = TestDb.New();
        var jefa = Svc(db, Jefa);
        await jefa.CrearAsync("Jefe directo", Trazo, 300, 120, null, true);
        await jefa.CrearAsync("Jefe suplente", Trazo, 300, 120, null, false);

        Assert.All(await jefa.TodasAsync(), f => Assert.Empty(f.PngBytes));
        Assert.All(await jefa.VisiblesAsync(null), f => Assert.Empty(f.PngBytes));

        // Y el nombre y las medidas sí llegan, que es lo que la lista necesita.
        var todas = await jefa.TodasAsync();
        Assert.Equal(2, todas.Count);
        Assert.All(todas, f => Assert.Equal(300, f.WidthPx));
    }

    [Fact]
    public async Task LaImagen_SaleSoloPorSuPropiaRuta()
    {
        using var db = TestDb.New();
        var (_, _, id) = await Svc(db, Jefa).CrearAsync("Jefe directo", Trazo, 300, 120, null, false);

        var (png, ancho, alto) = await Svc(db, Jefa).ImagenAsync(id);

        Assert.Equal(Trazo, png);
        Assert.Equal(300, ancho);
        Assert.Equal(120, alto);
    }

    /// <summary>La compartida la ve cualquiera con sesión: acompaña a los documentos que el equipo recibe.</summary>
    [Fact]
    public async Task LaImagenDeLaCompartida_LaVeCualquieraConSesion()
    {
        using var db = TestDb.New();
        var (_, _, id) = await Svc(db, Jefa).CrearAsync("Jefe directo", Trazo, 300, 120, null, false);

        var (png, _, _) = await Svc(db, Ana).ImagenAsync(id);

        Assert.Equal(Trazo, png);
    }

    [Fact]
    public async Task LaImagenDeUnaFirmaPropia_NoLaVeOtro()
    {
        using var db = Base();
        var (_, _, id) = await Svc(db, Ana).CrearAsync("La mía", Trazo, 200, 80, AnaDevId, false);

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, Beto).ImagenAsync(id));
    }

    // ── La predeterminada ───────────────────────────────────────────────────────

    /// <summary>
    /// El ámbito de «predeterminada» es el DUEÑO: la compartida y la de cada persona tienen cada una
    /// la suya. Sin esto, marcar la propia apagaría la del jefe y el documento saldría sin firmar.
    /// </summary>
    [Fact]
    public async Task MarcarPredeterminada_ApagaSoloLaDeSuAmbito()
    {
        using var db = Base();
        var jefa = Svc(db, Jefa);
        var (_, _, primera) = await jefa.CrearAsync("Jefe directo", Trazo, 300, 120, null, true);
        var (_, _, segunda) = await jefa.CrearAsync("Jefe suplente", Trazo, 300, 120, null, false);
        var (_, _, mia) = await Svc(db, Ana).CrearAsync("La mía", Trazo, 200, 80, AnaDevId, true);

        var (ok, mensaje) = await jefa.MarcarPredeterminadaAsync(segunda);

        Assert.True(ok);
        Assert.Contains("Jefe suplente", mensaje);

        var guardadas = db.SignatureProfiles.AsNoTracking().ToDictionary(f => f.Id, f => f.IsDefault);
        Assert.False(guardadas[primera]);
        Assert.True(guardadas[segunda]);
        Assert.True(guardadas[mia]);   // otro ámbito: no se toca
    }

    [Fact]
    public async Task VisiblesAsync_TraeLasCompartidasYLasPropiasConLaPredeterminadaPrimero()
    {
        using var db = Base();
        await Svc(db, Jefa).CrearAsync("Jefe directo", Trazo, 300, 120, null, false);
        await Svc(db, Ana).CrearAsync("La mía", Trazo, 200, 80, AnaDevId, true);
        await Svc(db, Beto).CrearAsync("La de Beto", Trazo, 200, 80, BetoDevId, false);

        var visibles = await Svc(db, Ana).VisiblesAsync(AnaDevId);

        Assert.Equal(2, visibles.Count);
        Assert.Equal("La mía", visibles[0].DisplayName);   // la predeterminada, primero
        Assert.DoesNotContain(visibles, f => f.DisplayName == "La de Beto");
    }

    // ── Validaciones y mensajes ─────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SinNombre_NoSeGuarda(string nombre)
    {
        using var db = TestDb.New();

        var (ok, mensaje, _) = await Svc(db, Jefa).CrearAsync(nombre, Trazo, 300, 120, null, false);

        Assert.False(ok);
        Assert.Contains("nombre", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.SignatureProfiles);
    }

    [Fact]
    public async Task SinTrazo_NoSeGuarda()
    {
        using var db = TestDb.New();

        var (ok, mensaje, _) = await Svc(db, Jefa).CrearAsync("Jefe directo", [], 300, 120, null, false);

        Assert.False(ok);
        Assert.Contains("no se dibujó", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sin medidas el documento no sabe colocarla sin deformarla, así que se rechaza antes de
    /// guardarla en vez de descubrirlo al generar el papel.
    /// </summary>
    [Theory]
    [InlineData(0, 120)]
    [InlineData(300, 0)]
    public async Task SinMedidas_NoSeGuarda(int ancho, int alto)
    {
        using var db = TestDb.New();

        var (ok, mensaje, _) = await Svc(db, Jefa).CrearAsync("Jefe directo", Trazo, ancho, alto, null, false);

        Assert.False(ok);
        Assert.Contains("medidas", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Un trazo son unos pocos KB; lo que llegue por encima del tope no es una firma.</summary>
    [Fact]
    public async Task UnaImagenDesmedida_NoEsUnTrazo()
    {
        using var db = TestDb.New();

        var (ok, mensaje, _) = await Svc(db, Jefa)
            .CrearAsync("Jefe directo", new byte[SignatureService.MaxBytes + 1], 300, 120, null, false);

        Assert.False(ok);
        Assert.Contains("KB", mensaje);
        Assert.Empty(db.SignatureProfiles);
    }

    /// <summary>
    /// El escritorio se callaba cuando la firma ya no existía y quien pulsaba no se enteraba de nada.
    /// Aquí el mensaje dice qué hacer, y es el que la pantalla enseña tal cual.
    /// </summary>
    [Fact]
    public async Task UnaFirmaQueYaNoEsta_LoDice()
    {
        using var db = TestDb.New();

        var (ok, mensaje) = await Svc(db, Jefa).EliminarAsync(404);

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje);
    }

    [Fact]
    public async Task Eliminar_DejaConstanciaEnLaBitacora()
    {
        using var db = TestDb.New();
        var (_, _, id) = await Svc(db, Jefa).CrearAsync("Jefe directo", Trazo, 300, 120, null, false);

        Assert.True((await Svc(db, Jefa).EliminarAsync(id)).ok);
        Assert.Empty(db.SignatureProfiles);

        var anotado = await db.AuditLogs.AsNoTracking()
            .AnyAsync(a => a.EntityType == "SignatureProfile" && a.Details!.Contains("Firma eliminada"));
        Assert.True(anotado);
    }
}
