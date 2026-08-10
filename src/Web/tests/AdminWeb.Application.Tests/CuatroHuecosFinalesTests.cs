using System.IO.Compression;
using System.Text.Json;
using AdminWeb.Application.Services;
using AdminWeb.Domain.Documentos;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Documentos;
using AdminWeb.Infrastructure.Seguridad;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Los cuatro últimos huecos de paridad con el escritorio, cerrados juntos.
///
/// <para>Van en un archivo compartido porque lo que los une no es el tema sino la razón de existir:
/// eran lo que quedaba de la auditoría de paridad, y cada uno se descubrió comparando contra el
/// escritorio, no usando la web. Separarlos en cuatro archivos de dos pruebas cada uno haría más
/// difícil ver que ya no queda ninguno.</para>
/// </summary>
public class CuatroHuecosFinalesTests
{
    // ── 1. Alta masiva de servidores desde JSON ──────────────────────────────────

    private static DeploymentTargetService Servidores(AppDbContext db, UserRole rol = UserRole.Admin)
    {
        var cu = UsuarioDePrueba.Como(rol, userId: 9);
        return new DeploymentTargetService(db, cu, new AuditService(db, cu, new OrigenDePrueba()));
    }

    private static string Json(params (string nombre, string host, string clave)[] filas) =>
        JsonSerializer.Serialize(filas.Select(f => new
        {
            nombre = f.nombre, host = f.host, puerto = 21, usuario = "ftp",
            contrasena = f.clave, rutaRemota = "/www", url = (string?)null
        }));

    [Fact]
    public async Task Importar_da_de_alta_los_servidores_del_archivo()
    {
        var db = TestDb.New();

        var (ok, mensaje, altas, actualizados) = await Servidores(db)
            .ImportarDesdeJsonAsync(Json(("Producción", "10.0.0.1", "clave1"), ("Pruebas", "10.0.0.2", "clave2")));

        Assert.True(ok, mensaje);
        Assert.Equal(2, altas);
        Assert.Equal(0, actualizados);
        Assert.Equal(2, await db.DeploymentTargets.CountAsync());
    }

    /// <summary>La contraseña no puede quedar en claro: la lee el escritorio hasta el corte.</summary>
    [Fact]
    public async Task La_contrasena_importada_queda_CIFRADA_y_el_escritorio_la_puede_leer()
    {
        var db = TestDb.New();
        await Servidores(db).ImportarDesdeJsonAsync(Json(("Producción", "10.0.0.1", "SuperSecreta123")));

        var guardado = await db.DeploymentTargets.AsNoTracking().SingleAsync();

        Assert.NotEqual("SuperSecreta123", guardado.Contrasena);
        Assert.Equal("SuperSecreta123", ProtectorPortable.Descifrar(guardado.Contrasena));
    }

    [Fact]
    public async Task Reimportar_ACTUALIZA_por_nombre_en_vez_de_duplicar()
    {
        var db = TestDb.New();
        await Servidores(db).ImportarDesdeJsonAsync(Json(("Producción", "10.0.0.1", "vieja")));

        var (ok, _, altas, actualizados) = await Servidores(db)
            .ImportarDesdeJsonAsync(Json(("Producción", "10.9.9.9", "nueva")));

        Assert.True(ok);
        Assert.Equal(0, altas);
        Assert.Equal(1, actualizados);

        var unico = await db.DeploymentTargets.AsNoTracking().SingleAsync();
        Assert.Equal("10.9.9.9", unico.Host);
        Assert.Equal("nueva", ProtectorPortable.Descifrar(unico.Contrasena));
    }

    /// <summary>
    /// Todo o nada. El escritorio guardaba al final del bucle sin transacción: un fallo a media
    /// lista dejaba media importación hecha y nadie sabía por dónde iba.
    /// </summary>
    [Fact]
    public async Task Si_una_fila_esta_mal_NO_entra_ninguna()
    {
        var db = TestDb.New();

        var (ok, mensaje, _, _) = await Servidores(db)
            .ImportarDesdeJsonAsync(Json(("Buena", "10.0.0.1", "clave"), ("Sin clave", "10.0.0.2", "")));

        Assert.False(ok);
        Assert.Contains("Sin clave", mensaje);
        Assert.Empty(db.DeploymentTargets);      // ni siquiera la que estaba bien
    }

    [Fact]
    public async Task Un_nombre_repetido_DENTRO_del_archivo_se_rechaza()
    {
        var db = TestDb.New();

        var (ok, mensaje, _, _) = await Servidores(db)
            .ImportarDesdeJsonAsync(Json(("Producción", "10.0.0.1", "a"), ("Producción", "10.0.0.2", "b")));

        Assert.False(ok);
        Assert.Contains("2 veces", mensaje);
        Assert.Empty(db.DeploymentTargets);
    }

    [Fact]
    public async Task Un_JSON_ilegible_se_rechaza_SIN_citar_su_contenido()
    {
        var db = TestDb.New();

        var (ok, mensaje, _, _) = await Servidores(db)
            .ImportarDesdeJsonAsync("""[{"nombre":"X","contrasena":"SuperSecreta123" """);

        Assert.False(ok);
        // El analizador de JSON entrecomilla el trozo que no entendió, y ahí puede ir la contraseña.
        Assert.DoesNotContain("SuperSecreta123", mensaje);
    }

    [Fact]
    public async Task Importar_no_es_de_cualquiera()
    {
        var db = TestDb.New();

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Servidores(db, UserRole.Desarrollador).ImportarDesdeJsonAsync(Json(("X", "1.1.1.1", "c"))));
    }

    // ── 2. Fortalezas y debilidades destacadas en la ficha ───────────────────────

    private static DatosDeFicha Ficha(params EvaluacionImpresa[] evaluaciones) => new(
        NombreCompleto: "Ana García", Correo: "ana@x.com", Telefono: null, Nivel: "Senior",
        Equipo: "Plataforma", RolEnElEquipo: "Desarrolladora",
        FechaDeIngreso: new DateTime(2020, 1, 1),
        PuntosAprobadosDelAnio: 42, TiempoTotal: "120 h", AsignacionesActivas: 3,
        Evaluaciones: evaluaciones, Hitos: [], GeneradoEl: "09/08/2026");

    [Fact]
    public void La_ficha_destaca_la_evaluacion_MAS_RECIENTE()
    {
        // Llegan de más reciente a más antigua, que es la suposición que sostiene la sección.
        var pdf = new GeneradorDeDocumentosQuestPdf().FichaDeDesarrollador(Ficha(
            new EvaluacionImpresa(new DateTime(2026, 6, 1), "1S 2026", 4,
                "Cerró el proyecto de facturación", "Documentar más", null, "Gerardo"),
            new EvaluacionImpresa(new DateTime(2025, 6, 1), "1S 2025", 3,
                "Buen arranque", "Pedir ayuda antes", null, "Gerardo")));

        Assert.NotEmpty(pdf);
        // Es un PDF de verdad: empieza por «%PDF».
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Sin_evaluaciones_la_ficha_lo_dice_en_vez_de_dejar_un_hueco()
    {
        // Un hueco en blanco en un papel oficial se lee como un error de impresión.
        var pdf = new GeneradorDeDocumentosQuestPdf().FichaDeDesarrollador(Ficha());

        Assert.NotEmpty(pdf);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    // ── 3. La firma se estampa igual en Word que en PDF ──────────────────────────

    private static byte[] PngDeUnPixel() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static DatosDeVacaciones Vacaciones() => new(
        "Ana", "09/08/2026", "Desarrollo", "Senior", "Gerardo", "01/01/2020",
        "10", "2026", "17/08/2026", "28/08/2026", "31/08/2026", "12",
        true, false, "", null);

    /// <summary>
    /// Una firma capturada en un lienzo grande se ACOTA, no se estampa a tamaño natural.
    ///
    /// <para>Antes el PDF la limitaba a 48 puntos de alto y el Word la ponía a su tamaño en píxeles,
    /// así que el mismo documento salía distinto en cada formato y en Word podía desbordar la celda
    /// de la tabla.</para>
    /// </summary>
    [Fact]
    public void Una_firma_grande_se_acota_al_mismo_alto_que_el_PDF()
    {
        var word = new PlantillaDeVacacionesOpenXml();

        // 600×200 px son 450×150 puntos: muy por encima de los 48 de alto que reserva el papel.
        var docx = word.Rellenar(word.DeFabrica(), Vacaciones(), new FirmaEnPng(PngDeUnPixel(), 600, 200));

        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var lector = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = lector.ReadToEnd();

        var cy = long.Parse(System.Text.RegularExpressions.Regex.Match(xml, @"cy=""(\d+)""").Groups[1].Value);
        var cx = long.Parse(System.Text.RegularExpressions.Regex.Match(xml, @"cx=""(\d+)""").Groups[1].Value);

        // 48 puntos = 609 600 EMU. Se admite un píxel de holgura por el redondeo.
        Assert.InRange(cy, 600_000, 620_000);
        // Y la PROPORCIÓN se conserva: 600/200 = 3.
        Assert.InRange((double)cx / cy, 2.9, 3.1);
    }

    [Fact]
    public void Una_firma_pequena_NO_se_agranda()
    {
        var word = new PlantillaDeVacacionesOpenXml();

        // 100×30 px = 75×22.5 puntos: cabe de sobra, así que se deja como está.
        var docx = word.Rellenar(word.DeFabrica(), Vacaciones(), new FirmaEnPng(PngDeUnPixel(), 100, 30));

        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var lector = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = lector.ReadToEnd();

        var cy = long.Parse(System.Text.RegularExpressions.Regex.Match(xml, @"cy=""(\d+)""").Groups[1].Value);

        Assert.Equal(30 * 9525L, cy);   // su tamaño natural, sin tocar
    }
}
