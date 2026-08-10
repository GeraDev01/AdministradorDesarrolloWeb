using System.IO.Compression;
using System.Text.RegularExpressions;
using AdminWeb.Application.Services;
using AdminWeb.Domain.Documentos;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Documentos;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// La firma del colaborador en su solicitud de vacaciones: que salga en el documento, que la de una
/// sola firma siga saliendo como siempre, y —lo que de verdad importa— que <b>una solicitud que
/// cambia después de firmarse deje de valer</b>.
///
/// <para>Ese último caso es el que justifica todo lo demás. Una firma pegada a unas fechas que ya no
/// son las que se firmaron es un documento falso, y de los peores: no revienta, no aparece en ningún
/// registro y nadie lo mira hasta que hay un problema. Por eso lo que se prueba aquí no es que
/// alguna ruta de edición se acuerde de invalidarla —eso dependería de quien escriba la siguiente—,
/// sino que la firma <b>deja de valer sola</b> en cuanto la solicitud no coincide con lo firmado.</para>
/// </summary>
public class FirmaDeVacacionesTests
{
    private const int AnaId = 7;
    private const int BetoId = 8;

    // ── Andamiaje ────────────────────────────────────────────────────────────────

    /// <summary>Un PNG de 1×1 válido, para no depender de ningún archivo.</summary>
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static AppDbContext BaseConDos()
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = AnaId, FullName = "Ana", IsActive = true, VacationDaysLeft = 12 });
        db.Developers.Add(new Developer { Id = BetoId, FullName = "Beto", IsActive = true });
        db.SaveChanges();
        return db;
    }

    private static UsuarioDePrueba Ana => UsuarioDePrueba.Como(UserRole.Desarrollador, AnaId, userId: AnaId);
    private static UsuarioDePrueba Beto => UsuarioDePrueba.Como(UserRole.Desarrollador, BetoId, userId: BetoId);
    private static UsuarioDePrueba Lider => UsuarioDePrueba.Como(UserRole.Admin, developerId: null, userId: 99);

    private static VacationRequest Solicitud(AppDbContext db,
        VacationStatus estado = VacationStatus.Pendiente, int devId = AnaId, string? comentario = null)
    {
        var v = new VacationRequest
        {
            DeveloperId = devId,
            StartDate = new DateTime(2026, 9, 1),
            EndDate = new DateTime(2026, 9, 5),
            Status = estado,
            Comment = comentario,
            CreatedAt = DateTime.UtcNow
        };
        db.VacationRequests.Add(v);
        db.SaveChanges();
        return v;
    }

    /// <summary>
    /// Las firmas estampadas en un .docx, por su nombre.
    ///
    /// Se cuentan por el NOMBRE del dibujo y no por las imágenes del paquete: la plantilla de fábrica
    /// ya trae la suya —el membrete—, así que contar los .png del zip daría siempre uno de más y la
    /// prueba pasaría o fallaría por el logotipo del área.
    /// </summary>
    private static List<string> FirmasEn(byte[] docx) =>
        Regex.Matches(XmlDe(docx), @"<wp:docPr[^>]*\sname=""(Firma[^""]*)""")
             .Select(m => m.Groups[1].Value)
             .ToList();

    private static string XmlDe(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var lector = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        return lector.ReadToEnd();
    }

    /// <summary>Todo el texto del .docx sin etiquetas: lo que se leería impreso.</summary>
    private static string TextoDe(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var sb = new System.Text.StringBuilder();
        foreach (var entrada in zip.Entries.Where(e => e.FullName.EndsWith(".xml")))
        {
            using var lector = new StreamReader(entrada.Open());
            sb.Append(Regex.Replace(lector.ReadToEnd(), "<[^>]+>", ""));
        }
        return sb.ToString();
    }

    private static DatosDeVacaciones Datos(byte[]? delJefe = null, byte[]? delColaborador = null) => new(
        Nombre: "Ana García", FechaSolicitud: "09/08/2026", Departamento: "Desarrollo",
        Puesto: "Senior", JefeDirecto: "Gerardo", FechaIngreso: "01/01/2020",
        TotalDias: "5", Periodo: "2026", FechaInicio: "01/09/2026", FechaFin: "05/09/2026",
        FechaRegreso: "07/09/2026", DiasPendientes: "12",
        Autorizada: true, Rechazada: false, Observaciones: "Cubre Beto.",
        FirmaDelJefe: delJefe, FirmaDelColaborador: delColaborador);

    // ── El documento con las dos firmas ──────────────────────────────────────────

    [Fact]
    public void ElWordSaleConLasDOSFirmasCuandoHayDos()
    {
        var word = new PlantillaDeVacacionesOpenXml();

        var docx = word.Rellenar(word.DeFabrica(), Datos(Png(), Png()),
            new FirmaEnPng(Png(), 190, 60), new FirmaEnPng(Png(), 150, 50));

        Assert.Equal(["Firma del colaborador", "Firma del jefe"], FirmasEn(docx));

        // Y ningún marcador impreso: es el fallo que no revienta y acaba en un papel oficial.
        var texto = TextoDe(docx);
        Assert.DoesNotContain("FIRMA_GERENTE", texto);
        Assert.DoesNotContain("FIRMA_COLABORADOR", texto);
        Assert.DoesNotContain("{{", texto);
    }

    /// <summary>
    /// Dos dibujos en el mismo documento no pueden compartir identificador. Word lo abre igual, pero
    /// su validador lo marca y hay visores que dejan de pintar el segundo — o sea, la firma que falta
    /// es justo la que nadie va a echar de menos hasta imprimir.
    /// </summary>
    [Fact]
    public void LasDosFirmasNoCompartenIdentificadorDeDibujo()
    {
        var word = new PlantillaDeVacacionesOpenXml();

        var xml = XmlDe(word.Rellenar(word.DeFabrica(), Datos(Png(), Png()),
            new FirmaEnPng(Png(), 190, 60), new FirmaEnPng(Png(), 150, 50)));

        var ids = Regex.Matches(xml, @"<wp:docPr\s+id=""(\d+)""\s+name=""Firma")
            .Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public void ConUnaSOLAFirmaElDocumentoSaleComoSiempre()
    {
        // Es la mitad del trabajo que nadie pide y que rompería a todo el mundo: la solicitud que
        // nadie firmó desde la web —todas las de antes— tiene que salir exactamente igual.
        var word = new PlantillaDeVacacionesOpenXml();

        var soloJefe = word.Rellenar(word.DeFabrica(), Datos(delJefe: Png()), new FirmaEnPng(Png(), 190, 60));
        var soloColaborador = word.Rellenar(word.DeFabrica(), Datos(delColaborador: Png()),
            null, new FirmaEnPng(Png(), 150, 50));
        var ninguna = word.Rellenar(word.DeFabrica(), Datos(), null);

        Assert.Equal(["Firma del jefe"], FirmasEn(soloJefe));
        Assert.Equal(["Firma del colaborador"], FirmasEn(soloColaborador));
        Assert.Empty(FirmasEn(ninguna));

        // El ancla que se queda sin firma se BORRA, nunca se imprime.
        foreach (var docx in new[] { soloJefe, soloColaborador, ninguna })
        {
            var texto = TextoDe(docx);
            Assert.DoesNotContain("FIRMA_GERENTE", texto);
            Assert.DoesNotContain("FIRMA_COLABORADOR", texto);
        }
    }

    // ── Firmar la propia solicitud ───────────────────────────────────────────────

    [Fact]
    public async Task AlFirmarSeGuardaLaFirmaComoSuyaYQuedaLigadaALaSolicitud()
    {
        var db = BaseConDos();
        var v = Solicitud(db);

        var (ok, mensaje) = await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);

        Assert.True(ok, mensaje);

        // La imagen va donde van todas las firmas, y CON DUEÑO: una firma sin dueño es la compartida
        // del jefe, y guardarla ahí la ofrecería para estampar documentos ajenos.
        var firma = db.SignatureProfiles.AsNoTracking().Single();
        Assert.Equal(AnaId, firma.OwnerDeveloperId);
        Assert.False(firma.IsDefault);
        Assert.Equal(150, firma.WidthPx);

        var papeles = await Fabrica.Vacaciones(db, Ana).PapelesDeAsync([v.Id]);
        Assert.True(papeles[v.Id].Firmada);
        Assert.True(papeles[v.Id].SigueValiendo);
        Assert.Equal(firma.Id, papeles[v.Id].FirmaId);
    }

    [Fact]
    public async Task NadieFirmaPorOtro_NiSiquieraElLider()
    {
        // La única operación de este servicio donde ser líder NO basta. Un líder que pudiera firmar
        // por alguien produciría exactamente el documento que todo esto existe para evitar.
        var db = BaseConDos();
        var v = Solicitud(db);

        var (ok, mensaje) = await Fabrica.Vacaciones(db, Lider).FirmarAsync(v.Id, Png(), 150, 50);

        Assert.False(ok);
        Assert.Contains("quien la pidió", mensaje);
        Assert.Empty(db.SignatureProfiles);
    }

    [Fact]
    public async Task NoSeFirmaLaSolicitudDeOtroDesarrollador()
    {
        var db = BaseConDos();
        var ajena = Solicitud(db, devId: BetoId);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Fabrica.Vacaciones(db, Ana).FirmarAsync(ajena.Id, Png(), 150, 50));
    }

    [Theory]
    [InlineData(VacationStatus.Aprobada)]
    [InlineData(VacationStatus.Rechazada)]
    [InlineData(VacationStatus.Cancelada)]
    public async Task NoSeFirmaLoQueYaSeResolvio(VacationStatus estado)
    {
        // Se firma la PETICIÓN mientras espera respuesta. Lo ya resuelto lo firma quien lo resolvió.
        var db = BaseConDos();
        var v = Solicitud(db, estado);

        var (ok, mensaje) = await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);

        Assert.False(ok);
        Assert.Contains(estado.ToString(), mensaje);
    }

    [Fact]
    public async Task VolverAFirmarReemplazaLaAnteriorSinDejarleLaImagenSuelta()
    {
        var db = BaseConDos();
        var v = Solicitud(db);
        var svc = Fabrica.Vacaciones(db, Ana);

        await svc.FirmarAsync(v.Id, Png(), 150, 50);
        var primera = db.SignatureProfiles.AsNoTracking().Single().Id;

        await svc.FirmarAsync(v.Id, Png(), 200, 70);

        // Una sola firma guardada, y es la nueva: acumularlas dejaría imágenes que ya no responden
        // por nada y que nadie volvería a ver para borrarlas.
        var quedan = db.SignatureProfiles.AsNoTracking().ToList();
        Assert.Single(quedan);
        Assert.NotEqual(primera, quedan[0].Id);
        Assert.Equal(200, quedan[0].WidthPx);

        // Y un solo enlace, no dos.
        Assert.Single(db.VacationDocuments.AsNoTracking()
            .Where(d => d.FileName == VacationRequestService.MarcaDeLaFirmaDelColaborador));
    }

    [Fact]
    public async Task AlEliminarLaSolicitudSeVaTambienLaImagenDeLaFirma()
    {
        // El enlace cae en cascada, pero la imagen cuelga del desarrollador: sin borrarla a mano
        // quedaría una firma suelta, invisible en toda la aplicación y guardada para siempre.
        var db = BaseConDos();
        var v = Solicitud(db);
        var svc = Fabrica.Vacaciones(db, Ana);

        await svc.FirmarAsync(v.Id, Png(), 150, 50);
        var (ok, _) = await svc.EliminarAsync(v.Id);

        Assert.True(ok);
        Assert.Empty(db.SignatureProfiles);
        Assert.Empty(db.VacationDocuments);
    }

    [Fact]
    public async Task LaFirmaNoCuentaComoDocumentoGenerado()
    {
        // Comparten tabla, pero la confirmación de borrado avisa de «documentos generados»: contarla
        // haría creer que se pierde un papel que nadie generó.
        var db = BaseConDos();
        var v = Solicitud(db);
        var svc = Fabrica.Vacaciones(db, Ana);

        await svc.FirmarAsync(v.Id, Png(), 150, 50);

        Assert.Equal(0, await svc.DocumentosAsociadosAsync(v.Id));
        Assert.Equal(0, (await svc.PapelesDeAsync([v.Id]))[v.Id].DocumentosGenerados);
    }

    // ── LO DELICADO: la solicitud cambia después de firmarse ─────────────────────

    [Fact]
    public async Task SiCambianLASFECHASDespuesDeFirmar_LaFirmaDejaDeValer()
    {
        var db = BaseConDos();
        var v = Solicitud(db);
        var svc = Fabrica.Vacaciones(db, Ana);
        await svc.FirmarAsync(v.Id, Png(), 150, 50);

        // Se cambia la solicitud POR DEBAJO, sin pasar por ninguna ruta que sepa de firmas: es
        // exactamente el caso que hay que cubrir, porque la protección no puede depender de que quien
        // escriba mañana un «corregir solicitud» se acuerde de invalidarla.
        var guardada = db.VacationRequests.Find(v.Id)!;
        guardada.EndDate = new DateTime(2026, 9, 12);
        db.SaveChanges();

        var papeles = await svc.PapelesDeAsync([v.Id]);

        Assert.True(papeles[v.Id].Firmada);            // firmó: eso no se borra ni se esconde
        Assert.False(papeles[v.Id].SigueValiendo);     // pero lo que firmó ya no es esto
        Assert.Null(papeles[v.Id].FirmaId);            // y no hay firma que estampar
        Assert.Null(await svc.FirmaVigenteAsync(v.Id));
    }

    [Fact]
    public async Task SiCambiaELMOTIVODespuesDeFirmar_LaFirmaDejaDeValer()
    {
        // El motivo se imprime en el papel como observación mientras nadie haya respondido nada, así
        // que cambiarlo cambia lo que la firma respalda.
        var db = BaseConDos();
        var v = Solicitud(db, comentario: "Viaje familiar");
        var svc = Fabrica.Vacaciones(db, Ana);
        await svc.FirmarAsync(v.Id, Png(), 150, 50);

        db.VacationRequests.Find(v.Id)!.Comment = "Curso en otra ciudad";
        db.SaveChanges();

        Assert.False((await svc.PapelesDeAsync([v.Id]))[v.Id].SigueValiendo);
    }

    [Fact]
    public async Task UnaFirmaQueDejoDeValerNOSaleEnElDocumento()
    {
        // La comprobación no puede quedarse en la pantalla: lo que no puede pasar es que el PAPEL
        // salga firmado, porque es el papel el que se archiva y el que alguien lee dentro de un año.
        var db = BaseConDos();
        var v = Solicitud(db);
        await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);

        var documentos = Fabrica.DocumentoDeVacaciones(db, Ana);
        var (ok, _, conFirma, _) = await documentos.GenerarWordAsync(v.Id, firmaId: null);
        Assert.True(ok);
        Assert.Equal(["Firma del colaborador"], FirmasEn(conFirma));

        db.VacationRequests.Find(v.Id)!.EndDate = new DateTime(2026, 9, 12);
        db.SaveChanges();

        var (ok2, _, sinFirma, _) = await Fabrica.DocumentoDeVacaciones(db, Ana)
            .GenerarWordAsync(v.Id, firmaId: null);

        Assert.True(ok2);
        Assert.Empty(FirmasEn(sinFirma));
        Assert.DoesNotContain("FIRMA_COLABORADOR", TextoDe(sinFirma));
    }

    [Fact]
    public async Task ResolverLaSolicitudNOInvalidaLaFirma()
    {
        // La otra mitad de la regla, y la que se rompería con una invalidación demasiado ansiosa: el
        // líder aprueba y escribe su observación, y eso NO es un cambio en la petición. Si invalidara,
        // el documento con las dos firmas sería imposible — que es justo el que se archiva.
        var db = BaseConDos();
        var v = Solicitud(db, comentario: "Viaje familiar");
        await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);

        var (ok, _) = await Fabrica.DocumentoDeVacaciones(db, Lider)
            .ResolverAsync(v.Id, VacationStatus.Aprobada, "Buen viaje.");

        Assert.True(ok);
        Assert.True((await Fabrica.Vacaciones(db, Ana).PapelesDeAsync([v.Id]))[v.Id].SigueValiendo);
    }

    // ── El documento definitivo, con las dos ─────────────────────────────────────

    [Fact]
    public async Task AlFirmarElLider_ElDocumentoArchivadoConservaLaFirmaDelColaborador()
    {
        var db = BaseConDos();
        var v = Solicitud(db);
        await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);

        var lider = Fabrica.DocumentoDeVacaciones(db, Lider);
        await lider.ResolverAsync(v.Id, VacationStatus.Aprobada, "Aprobadas.");

        // La firma compartida del jefe, creada como la crea su gestor.
        var auditoria = new AuditService(db, Lider, new OrigenDePrueba());
        var (creada, _, firmaDelJefe) = await new SignatureService(db, Lider, auditoria)
            .CrearAsync("Jefe directo", Png(), 190, 60, duenoDeveloperId: null, predeterminada: true);
        Assert.True(creada);

        var (ok, mensaje) = await lider.FirmarAsync(v.Id, firmaDelJefe);
        Assert.True(ok, mensaje);

        // El enlace de la firma del colaborador SIGUE ahí: sin excluirlo al archivar, el documento
        // firmado lo habría sobrescrito y se habría llevado por delante la firma de quien pidió las
        // vacaciones justo en el momento de archivar el papel.
        var papeles = await Fabrica.Vacaciones(db, Ana).PapelesDeAsync([v.Id]);
        Assert.True(papeles[v.Id].SigueValiendo);
        Assert.True(papeles[v.Id].DocumentoDelLiderArchivado);
        Assert.Equal(1, papeles[v.Id].DocumentosGenerados);

        // Y el Word definitivo lleva las DOS.
        var (okWord, _, docx, _) = await lider.GenerarWordAsync(v.Id, firmaDelJefe);
        Assert.True(okWord);
        Assert.Equal(["Firma del colaborador", "Firma del jefe"], FirmasEn(docx));
    }

    // ── Quién puede ver el documento ─────────────────────────────────────────────

    [Fact]
    public async Task ElDuenoVeSuDocumentoPeroNoPuedeEstamparLaFirmaDelJefe()
    {
        // Ver el propio papel es lo que hace que firmarlo signifique algo. Lo que no puede es elegir
        // firma: con eso cualquiera se emitiría un documento «autorizado» que nadie autorizó.
        var db = BaseConDos();
        var v = Solicitud(db);

        var auditoria = new AuditService(db, Lider, new OrigenDePrueba());
        var (_, _, firmaDelJefe) = await new SignatureService(db, Lider, auditoria)
            .CrearAsync("Jefe directo", Png(), 190, 60, duenoDeveloperId: null, predeterminada: true);

        var (ok, _, docx, nombre) = await Fabrica.DocumentoDeVacaciones(db, Ana)
            .GenerarWordAsync(v.Id, firmaDelJefe);

        Assert.True(ok);
        Assert.EndsWith(".docx", nombre);
        Assert.Empty(FirmasEn(docx));   // el firmaId que pidió se ignoró por no ser el líder
    }

    [Fact]
    public async Task NadieVeElDocumentoDeOtroDesarrollador()
    {
        var db = BaseConDos();
        var ajena = Solicitud(db, devId: BetoId);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Fabrica.DocumentoDeVacaciones(db, Ana).GenerarWordAsync(ajena.Id, firmaId: null));
    }

    [Fact]
    public async Task ElDuenoBajaSuDocumentoArchivadoYUnAjenoNo()
    {
        var db = BaseConDos();
        var v = Solicitud(db);

        var lider = Fabrica.DocumentoDeVacaciones(db, Lider);
        await lider.ResolverAsync(v.Id, VacationStatus.Aprobada, "Aprobadas.");

        var auditoria = new AuditService(db, Lider, new OrigenDePrueba());
        var (_, _, firmaDelJefe) = await new SignatureService(db, Lider, auditoria)
            .CrearAsync("Jefe directo", Png(), 190, 60, duenoDeveloperId: null, predeterminada: true);
        await lider.FirmarAsync(v.Id, firmaDelJefe);

        var (pdf, nombre) = await Fabrica.DocumentoDeVacaciones(db, Ana).DocumentoFirmadoAsync(v.Id);
        Assert.NotEmpty(pdf);
        Assert.NotEmpty(nombre);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Fabrica.DocumentoDeVacaciones(db, Beto).DocumentoFirmadoAsync(v.Id));
    }

    // ── La huella ────────────────────────────────────────────────────────────────

    [Fact]
    public void LaHuellaCambiaConLaPETICIONYNoConLaRESPUESTA()
    {
        var v = new VacationRequest
        {
            DeveloperId = AnaId,
            StartDate = new DateTime(2026, 9, 1),
            EndDate = new DateTime(2026, 9, 5),
            Comment = "Viaje familiar"
        };
        var original = VacationRequestService.HuellaDeLaPeticion(v);

        // Lo que la persona firmó: cambia.
        v.EndDate = new DateTime(2026, 9, 6);
        Assert.NotEqual(original, VacationRequestService.HuellaDeLaPeticion(v));
        v.EndDate = new DateTime(2026, 9, 5);

        v.Comment = "Otro motivo";
        Assert.NotEqual(original, VacationRequestService.HuellaDeLaPeticion(v));
        v.Comment = "Viaje familiar";

        // Lo que el trámite añade después: NO cambia. Estado, respuesta del líder y respaldo son
        // pasos posteriores a la firma, y meterlos aquí la invalidaría en cuanto el jefe respondiera.
        v.Status = VacationStatus.Aprobada;
        v.ReviewComment = "Buen viaje.";
        v.ReviewedAt = DateTime.UtcNow;
        v.AttachmentFileName = "constancia.pdf";
        v.AttachmentBytes = [1, 2, 3];

        Assert.Equal(original, VacationRequestService.HuellaDeLaPeticion(v));
    }
}
