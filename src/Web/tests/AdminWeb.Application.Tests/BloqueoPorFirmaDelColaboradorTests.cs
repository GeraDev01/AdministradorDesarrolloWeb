using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Documentos;
using AdminWeb.Shared.Enums;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El hueco que quedaba en el lado del LÍDER: que la firma del colaborador se invalidara sola no
/// servía de nada mientras la pantalla que emite el documento no lo dijera.
///
/// <para>Lo que se prueba aquí no es la invalidación —eso es de <c>FirmaDeVacacionesTests</c>—, sino
/// las tres consecuencias que faltaban: que el servidor <b>se niegue a archivar</b> el documento
/// definitivo cuando la firma dejó de valer, que <b>lo siga emitiendo</b> cuando vale, y que las
/// <b>tres situaciones</b> lleguen distinguidas a quien tiene que decidir.</para>
///
/// <para>La barrera se prueba contra el SERVICIO y no contra la pantalla a propósito: el botón
/// apagado es una cortesía —esa pantalla corre en la máquina de cada quien y a la operación se llega
/// también sin navegador—, así que una prueba que solo mirara el botón daría por buena una regla que
/// no existe.</para>
/// </summary>
public class BloqueoPorFirmaDelColaboradorTests
{
    private const int AnaId = 41;
    private const int BetoId = 42;
    private const int UsuarioDeAna = 141;

    // ── Andamiaje ────────────────────────────────────────────────────────────────

    /// <summary>Un PNG de 1×1 válido, para no depender de ningún archivo.</summary>
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static UsuarioDePrueba Ana => UsuarioDePrueba.Como(UserRole.Desarrollador, AnaId, userId: UsuarioDeAna);
    private static UsuarioDePrueba Lider => UsuarioDePrueba.Como(UserRole.Admin, developerId: null, userId: 99);

    /// <summary>
    /// Ana con CUENTA de usuario, y ahí está la gracia: los avisos se entregan por cuenta, no por
    /// ficha. Beto se queda sin ella para poder probar el caso en que el recado no llega a nadie.
    /// </summary>
    private static AppDbContext BaseConEquipo()
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = AnaId, FullName = "Ana", IsActive = true, VacationDaysLeft = 12 });
        db.Developers.Add(new Developer { Id = BetoId, FullName = "Beto", IsActive = true });
        db.Users.Add(new User
        {
            Id = UsuarioDeAna, Username = "ana", FullName = "Ana", PasswordHash = "x",
            Role = UserRole.Desarrollador, DeveloperId = AnaId, IsActive = true
        });
        db.SaveChanges();
        return db;
    }

    private static VacationRequest Solicitud(AppDbContext db, int devId = AnaId, string? comentario = null)
    {
        var v = new VacationRequest
        {
            DeveloperId = devId,
            StartDate = new DateTime(2026, 9, 1),
            EndDate = new DateTime(2026, 9, 5),
            Status = VacationStatus.Pendiente,
            Comment = comentario,
            CreatedAt = DateTime.UtcNow
        };
        db.VacationRequests.Add(v);
        db.SaveChanges();
        return v;
    }

    /// <summary>
    /// El servicio del líder CON el envío de avisos puesto. <c>Fabrica.DocumentoDeVacaciones</c> lo
    /// deja fuera —le basta con el documento—, y aquí hace falta porque la salida del bloqueo es
    /// justamente un aviso: sin él se probaría el bloqueo y no la salida.
    /// </summary>
    private static DocumentoDeVacacionesService DelLider(AppDbContext db, ICurrentUser quien)
    {
        var auditoria = new AuditService(db, quien, new OrigenDePrueba());
        return new DocumentoDeVacacionesService(
            db, quien, new SettingsService(db, quien, auditoria),
            new SignatureService(db, quien, auditoria),
            new GeneradorDeDocumentosQuestPdf(), new PlantillaDeVacacionesOpenXml(), auditoria,
            Fabrica.Vacaciones(db, quien), Fabrica.Saldo(db, quien), new NotificationService(db));
    }

    /// <summary>La firma compartida del jefe, creada como la crea su gestor.</summary>
    private static async Task<int> FirmaDelJefe(AppDbContext db)
    {
        var auditoria = new AuditService(db, Lider, new OrigenDePrueba());
        var (ok, mensaje, id) = await new SignatureService(db, Lider, auditoria)
            .CrearAsync("Jefe directo", Png(), 190, 60, duenoDeveloperId: null, predeterminada: true);
        Assert.True(ok, mensaje);
        return id;
    }

    /// <summary>
    /// Cambia la petición por debajo de la firma. Es LA forma de invalidarla: la huella se calcula
    /// sobre las fechas y el motivo, así que mover el último día es exactamente lo que la protección
    /// existe para detectar.
    /// </summary>
    private static void MoverElUltimoDia(AppDbContext db, int solicitudId)
    {
        db.VacationRequests.Find(solicitudId)!.EndDate = new DateTime(2026, 9, 12);
        db.SaveChanges();
    }

    private static async Task<SolicitudDeVacacionesLeida> Leer(AppDbContext db, int solicitudId) =>
        (await DelLider(db, Lider).SolicitudesAsync()).Single(s => s.Id == solicitudId);

    // ── La barrera ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConLaFirmaCaida_ElServidorSeNiegaAArchivarElDocumento()
    {
        // El caso entero, en orden: Ana firma, alguien mueve las fechas, el líder resuelve y va a
        // archivar. Hasta ahora salía un PDF con el hueco de la firma en blanco y nadie se enteraba.
        var db = BaseConEquipo();
        var v = Solicitud(db);
        await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);

        var lider = DelLider(db, Lider);
        await lider.ResolverAsync(v.Id, VacationStatus.Aprobada, "Aprobadas.");
        MoverElUltimoDia(db, v.Id);

        var (ok, mensaje) = await lider.FirmarAsync(v.Id, await FirmaDelJefe(db));

        Assert.False(ok);
        // El mensaje tiene que decir QUÉ pasó y no «firma inválida»: es lo único que el líder va a
        // leer, y de ahí sale lo que haga después.
        Assert.Contains("dejó de valer", mensaje);
        Assert.Contains("Ana", mensaje);

        // Y no quedó nada archivado: la negativa no puede dejar un documento a medias.
        Assert.False((await Leer(db, v.Id)).DocumentoFirmado);
        var (pdf, _) = await lider.DocumentoFirmadoAsync(v.Id);
        Assert.Empty(pdf);
    }

    [Fact]
    public async Task ConLaFirmaVIGENTE_ElDocumentoSeArchivaComoSiempre()
    {
        // La otra mitad, y la que se rompería con una barrera demasiado ansiosa: sin esta prueba, un
        // bloqueo mal escrito dejaría la pantalla sin poder emitir NADA y nadie sabría por qué.
        var db = BaseConEquipo();
        var v = Solicitud(db);
        await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);

        var lider = DelLider(db, Lider);
        await lider.ResolverAsync(v.Id, VacationStatus.Aprobada, "Aprobadas.");

        var (ok, mensaje) = await lider.FirmarAsync(v.Id, await FirmaDelJefe(db));

        Assert.True(ok, mensaje);
        var (pdf, _) = await lider.DocumentoFirmadoAsync(v.Id);
        Assert.NotEmpty(pdf);
    }

    [Fact]
    public async Task SinFirmaDelColaborador_ElDocumentoSIGUEArchivandose()
    {
        // El bloqueo alcanza a la firma que SE CAYÓ, no a la que nunca hubo. El escritorio ni
        // guardaba la firma del colaborador, así que exigirla dejaría sin poder archivar el
        // histórico entero — y a cambio de nada, porque ahí no hay ninguna firma que se perdiera.
        var db = BaseConEquipo();
        var v = Solicitud(db);

        var lider = DelLider(db, Lider);
        await lider.ResolverAsync(v.Id, VacationStatus.Aprobada, "Aprobadas.");

        var (ok, mensaje) = await lider.FirmarAsync(v.Id, await FirmaDelJefe(db));

        Assert.True(ok, mensaje);
    }

    [Fact]
    public async Task LaBarreraNoDependeDeQuePASEPORLaPantalla()
    {
        // Dicho de otro modo: no hay una segunda comprobación en el endpoint que el servicio no
        // tenga. Se llama al servicio a pelo, como lo haría quien hable con la API sin navegador, y
        // la negativa es la misma.
        var db = BaseConEquipo();
        var v = Solicitud(db, comentario: "Viaje familiar");
        await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);

        var lider = DelLider(db, Lider);
        await lider.ResolverAsync(v.Id, VacationStatus.Aprobada, "Aprobadas.");

        // Esta vez cambia el MOTIVO, no las fechas: la huella cubre las dos cosas porque las dos son
        // la petición, y el papel las imprime.
        db.VacationRequests.Find(v.Id)!.Comment = "Ya no es un viaje";
        db.SaveChanges();

        Assert.False((await lider.FirmarAsync(v.Id, await FirmaDelJefe(db))).ok);
    }

    // ── Las tres situaciones que llegan a la pantalla ────────────────────────────

    [Fact]
    public async Task LaPantallaDistingueLasTRESSituacionesDeLaFirma()
    {
        // «Firmada» y «sin firmar» dejaban la tercera escondida dentro de la segunda, que es la
        // lectura más tranquilizadora posible de un problema: parece que nunca hubo firma, cuando lo
        // que pasó es que la solicitud cambió por debajo de una que sí existió.
        var db = BaseConEquipo();
        var sinFirmar = Solicitud(db);
        var firmada = Solicitud(db);
        var caida = Solicitud(db);

        var deAna = Fabrica.Vacaciones(db, Ana);
        await deAna.FirmarAsync(firmada.Id, Png(), 150, 50);
        await deAna.FirmarAsync(caida.Id, Png(), 150, 50);
        MoverElUltimoDia(db, caida.Id);

        var lista = await DelLider(db, Lider).SolicitudesAsync();
        var porId = lista.ToDictionary(s => s.Id, s => s.FirmaDelColaborador);

        Assert.False(porId[sinFirmar.Id].Vigente);
        Assert.False(porId[sinFirmar.Id].DejoDeValer);
        Assert.Null(porId[sinFirmar.Id].FirmadaUtc);

        Assert.True(porId[firmada.Id].Vigente);
        Assert.False(porId[firmada.Id].DejoDeValer);
        Assert.NotNull(porId[firmada.Id].FirmadaUtc);

        Assert.False(porId[caida.Id].Vigente);
        Assert.True(porId[caida.Id].DejoDeValer);
        // La FECHA sigue viajando aunque la firma ya no valga: es la que permite entender qué pasó
        // primero, y sin ella el aviso solo dice que algo se rompió.
        Assert.NotNull(porId[caida.Id].FirmadaUtc);

        // Y lo que la pantalla usa para apagar el botón sale del MISMO sitio que la barrera.
        Assert.True(DocumentoDeVacacionesService.SePuedeArchivar(porId[sinFirmar.Id]));
        Assert.True(DocumentoDeVacacionesService.SePuedeArchivar(porId[firmada.Id]));
        Assert.False(DocumentoDeVacacionesService.SePuedeArchivar(porId[caida.Id]));
    }

    [Fact]
    public async Task ResolverLaSolicitudApagaElPuedeVolverAFirmar()
    {
        // Se firma la PETICIÓN mientras espera respuesta. La pantalla necesita saberlo para no
        // ofrecer pedir una firma que la persona ya no puede dar.
        var db = BaseConEquipo();
        var v = Solicitud(db);
        await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);

        Assert.True((await Leer(db, v.Id)).FirmaDelColaborador.PuedeVolverAFirmar);

        await DelLider(db, Lider).ResolverAsync(v.Id, VacationStatus.Aprobada, "Aprobadas.");

        Assert.False((await Leer(db, v.Id)).FirmaDelColaborador.PuedeVolverAFirmar);
        // Resolver NO tumba la firma: eso ya lo cubre FirmaDeVacacionesTests, y se comprueba aquí
        // otra vez porque una barrera que se disparara al aprobar haría imposible el documento con
        // las dos firmas — que es justo el que se archiva.
        Assert.True((await Leer(db, v.Id)).FirmaDelColaborador.Vigente);
    }

    [Fact]
    public async Task LaHojaDeCalculoDiceLoMismoQueLaPantalla()
    {
        // La exportación baja lo que el filtro enseña, así que también tiene que decir lo que la
        // pantalla dice: una hoja que llamara «sin firmar» a una firma caída sería la versión
        // imprimible del problema.
        var db = BaseConEquipo();
        var v = Solicitud(db);
        await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);
        MoverElUltimoDia(db, v.Id);

        var libro = await DelLider(db, Lider).ExcelAsync(developerId: AnaId);

        using var memoria = new MemoryStream(libro);
        using var archivo = new XLWorkbook(memoria);
        var hoja = archivo.Worksheet(1);

        var encabezados = hoja.Row(1).CellsUsed().Select(c => c.GetString()).ToArray();
        var columna = Array.IndexOf(encabezados, "Firma del colaborador");
        Assert.True(columna >= 0, "La hoja no trae la columna de la firma del colaborador.");

        Assert.Equal("Dejó de valer", hoja.Row(2).Cell(columna + 1).GetString());

        // Y va AL FINAL: quien tenga fórmulas o filtros montados sobre esta exportación los tiene
        // atados a la posición de cada columna, y meterla en medio se los correría todos.
        Assert.Equal(encabezados.Length - 1, columna);
    }

    // ── La salida del bloqueo ────────────────────────────────────────────────────

    [Fact]
    public async Task ElLiderPidePorLaFirmaYALaPersonaLeLlegaElAvisoConElMotivo()
    {
        // Sin esto el bloqueo sería un callejón: el líder descubre que no puede archivar y no tiene
        // nada que hacer desde donde está. El aviso va por cuenta de usuario y lleva a «Mis
        // vacaciones», que es donde está el botón de firmar.
        var db = BaseConEquipo();
        var v = Solicitud(db);
        await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);
        MoverElUltimoDia(db, v.Id);

        var (ok, mensaje) = await DelLider(db, Lider)
            .PedirQueVuelvaAFirmarAsync(v.Id, "Te moví el regreso al 14.");

        Assert.True(ok, mensaje);

        var aviso = db.Notifications.AsNoTracking().Single();
        Assert.Equal(UsuarioDeAna, aviso.ForUserId);
        Assert.Contains("dejó de valer", aviso.Title);
        Assert.Contains("cambió después de que la firmaras", aviso.Message);
        // La nota del líder viaja: el aviso explica QUÉ pasó, pero el motivo solo lo sabe él.
        Assert.Contains("Te moví el regreso al 14.", aviso.Message);
        Assert.Equal("mis-vacaciones", aviso.Url);
    }

    [Fact]
    public async Task ElRecadoTambienSirveCuandoLaPersonaNoHaFirmadoTODAVIA()
    {
        var db = BaseConEquipo();
        var v = Solicitud(db);

        var (ok, mensaje) = await DelLider(db, Lider).PedirQueVuelvaAFirmarAsync(v.Id);

        Assert.True(ok, mensaje);
        var aviso = db.Notifications.AsNoTracking().Single();
        Assert.Contains("Falta tu firma", aviso.Title);
    }

    [Fact]
    public async Task DespuesDeVolverAFirmar_ElDocumentoYaSeArchiva()
    {
        // El circuito completo, que es lo que convierte el bloqueo en un trámite y no en un muro:
        // se cae la firma, el líder la pide, Ana vuelve a firmar y el papel sale.
        var db = BaseConEquipo();
        var v = Solicitud(db);
        var deAna = Fabrica.Vacaciones(db, Ana);
        await deAna.FirmarAsync(v.Id, Png(), 150, 50);
        MoverElUltimoDia(db, v.Id);

        var lider = DelLider(db, Lider);
        Assert.True((await lider.PedirQueVuelvaAFirmarAsync(v.Id)).ok);

        // Ana firma de nuevo: ahora la huella coincide con la solicitud tal como está.
        Assert.True((await deAna.FirmarAsync(v.Id, Png(), 150, 50)).ok);
        Assert.True((await Leer(db, v.Id)).FirmaDelColaborador.Vigente);

        await lider.ResolverAsync(v.Id, VacationStatus.Aprobada, "Aprobadas.");
        var (ok, mensaje) = await lider.FirmarAsync(v.Id, await FirmaDelJefe(db));

        Assert.True(ok, mensaje);
    }

    [Fact]
    public async Task NoSePideUnaFirmaQueYAVALE()
    {
        var db = BaseConEquipo();
        var v = Solicitud(db);
        await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);

        var (ok, mensaje) = await DelLider(db, Lider).PedirQueVuelvaAFirmarAsync(v.Id);

        Assert.False(ok);
        Assert.Contains("no hay nada que pedirle", mensaje);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task ConLaSolicitudYARESUELTA_SeExplicaEnVezDeMandarUnRecadoImposible()
    {
        // Avisar ahí sería mandarla a una pantalla donde no hay botón: se firma la petición mientras
        // espera respuesta. Se prefiere decírselo al líder a dejar a los dos esperando al otro.
        var db = BaseConEquipo();
        var v = Solicitud(db);

        var lider = DelLider(db, Lider);
        await lider.ResolverAsync(v.Id, VacationStatus.Aprobada, "Aprobadas.");

        var (ok, mensaje) = await lider.PedirQueVuelvaAFirmarAsync(v.Id);

        Assert.False(ok);
        Assert.Contains("Aprobada", mensaje);
        Assert.Empty(db.Notifications);
    }

    [Fact]
    public async Task SinCuentaActivaNoSeMIENTE_ElRecadoSeDaPorNoEntregado()
    {
        // Beto no tiene cuenta de usuario y los avisos se entregan por cuenta. Devolver «listo»
        // dejaría al líder esperando una firma que nadie le pidió, y con un asiento en la bitácora
        // diciendo que sí — que es la peor combinación posible.
        var db = BaseConEquipo();
        var v = Solicitud(db, devId: BetoId);

        var (ok, mensaje) = await DelLider(db, Lider).PedirQueVuelvaAFirmarAsync(v.Id);

        Assert.False(ok);
        Assert.Contains("no tiene cuenta activa", mensaje);
        Assert.Empty(db.Notifications);
        Assert.DoesNotContain(db.AuditLogs.AsNoTracking().ToList(),
            a => a.Details != null && a.Details.Contains("que firme"));
    }

    [Fact]
    public async Task PedirLaFirmaEsDelLIDER()
    {
        var db = BaseConEquipo();
        var v = Solicitud(db);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => DelLider(db, Ana).PedirQueVuelvaAFirmarAsync(v.Id));
    }

    // ── Las etiquetas, sin emoji ─────────────────────────────────────────────────

    [Theory]
    [InlineData(VacationStatus.Pendiente, "Pendiente")]
    [InlineData(VacationStatus.Aprobada, "Aprobada")]
    [InlineData(VacationStatus.Rechazada, "Rechazada")]
    [InlineData(VacationStatus.Cancelada, "Cancelada")]
    public void LaEtiquetaEsLaPALABRASOLA(VacationStatus estado, string esperada)
    {
        // Los dibujaba el sistema operativo: salían distintos en cada equipo, no heredaban el color
        // del texto y donde no hay fuente de emoji instalada salían como un cuadro vacío. La PALABRA
        // no cambia, así que los asientos viejos de la bitácora se siguen leyendo y encontrando igual.
        Assert.Equal(esperada, DocumentoDeVacacionesService.Etiqueta(estado));
    }
}
