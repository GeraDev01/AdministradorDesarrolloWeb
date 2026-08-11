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
/// EL CALLEJÓN SIN SALIDA del bloqueo de la firma, y las dos puertas que lo abren.
///
/// <para><b>Cuál era el callejón.</b> Archivar el documento definitivo se prohíbe cuando la firma del
/// colaborador dejó de valer —lo que <c>BloqueoPorFirmaDelColaboradorTests</c> ya cubre—, y la única
/// salida que había, pedirle que volviera a firmar, exigía que la solicitud siguiera PENDIENTE.
/// Puestas juntas, las dos reglas se anulaban: sobre una solicitud ya resuelta el bloqueo se disparaba
/// y el botón que lo resolvía estaba apagado por definición. Y no era un rincón teórico — se llega por
/// un camino que ni siquiera toca la solicitud: basta con que alguien borre en «Firmas» el trazo de esa
/// persona, porque la clave foránea es SetNull y el enlace sobrevive sin imagen.</para>
///
/// <para><b>Qué se prueba aquí.</b> Las dos salidas, contra el SERVICIO y no contra la pantalla: qué
/// botón se pinte es una cortesía —esa pantalla corre en la máquina de cada quien y a la operación se
/// llega también sin navegador—, así que una prueba que mirara el botón daría por buena una regla que
/// no existe. Y se prueba además lo que NO cambia: sin usar ninguna de las dos salidas, archivar con
/// la firma caída se sigue rechazando igual.</para>
/// </summary>
public class SalidasDelBloqueoDeFirmaDeVacacionesTests
{
    private const int AnaId = 61;
    private const int UsuarioDeAna = 161;

    // ── Andamiaje ────────────────────────────────────────────────────────────────

    /// <summary>Un PNG de 1×1 válido, para no depender de ningún archivo.</summary>
    private static byte[] Png() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static UsuarioDePrueba Ana => UsuarioDePrueba.Como(UserRole.Desarrollador, AnaId, userId: UsuarioDeAna);
    private static UsuarioDePrueba Lider => UsuarioDePrueba.Como(UserRole.Admin, developerId: null, userId: 98);

    /// <summary>
    /// Ana con CUENTA de usuario: los avisos se entregan por cuenta y no por ficha, y la primera
    /// salida es justamente un aviso.
    /// </summary>
    private static AppDbContext BaseConAna()
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = AnaId, FullName = "Ana", IsActive = true, VacationDaysLeft = 12 });
        db.Users.Add(new User
        {
            Id = UsuarioDeAna, Username = "ana", FullName = "Ana", PasswordHash = "x",
            Role = UserRole.Desarrollador, DeveloperId = AnaId, IsActive = true
        });
        db.SaveChanges();
        return db;
    }

    private static VacationRequest Solicitud(AppDbContext db)
    {
        var v = new VacationRequest
        {
            DeveloperId = AnaId,
            StartDate = new DateTime(2026, 9, 1),
            EndDate = new DateTime(2026, 9, 5),
            Status = VacationStatus.Pendiente,
            CreatedAt = DateTime.UtcNow
        };
        db.VacationRequests.Add(v);
        db.SaveChanges();
        return v;
    }

    /// <summary>
    /// Un generador que no maqueta nada y se queda con los datos que le pasaron.
    ///
    /// <para><b>Es la única forma honesta de comprobar lo que el papel DICE.</b> El PDF de verdad sale
    /// comprimido, así que buscarle una frase dentro sería buscarla en bytes que no la contienen tal
    /// cual; y afirmar «se archivó, luego lo dice» no probaría nada. Aquí se mira exactamente lo que el
    /// servicio manda imprimir, que es donde vive la decisión.</para>
    ///
    /// <para>Los otros dos documentos lanzan en vez de devolver algo inventado: si una prueba acabara
    /// pidiéndolos, conviene que se entere en el momento y no que pase en verde creyendo que probó
    /// algo.</para>
    /// </summary>
    private sealed class GeneradorEspia : IGeneradorDeDocumentos
    {
        public DatosDeVacaciones? Ultimo { get; private set; }

        public byte[] SolicitudDeVacaciones(DatosDeVacaciones datos)
        {
            Ultimo = datos;
            // No vacío a propósito: el servicio guarda estos bytes como el PDF archivado y hay
            // pruebas que comprueban que el documento quedó guardado.
            return [1, 2, 3];
        }

        public byte[] FichaDeDesarrollador(DatosDeFicha datos) =>
            throw new NotSupportedException("Esta prueba no debería pedir la ficha de un desarrollador.");

        public byte[] OrganizacionDeEquipos(DatosDeEquipos datos) =>
            throw new NotSupportedException("Esta prueba no debería pedir la organización de equipos.");
    }

    /// <summary>
    /// El servicio del líder CON el envío de avisos puesto —la primera salida es un aviso— y, si se
    /// pide, con el generador espía en lugar del de verdad.
    /// </summary>
    private static DocumentoDeVacacionesService DelLider(
        AppDbContext db, ICurrentUser quien, IGeneradorDeDocumentos? generador = null)
    {
        var auditoria = new AuditService(db, quien, new OrigenDePrueba());
        return new DocumentoDeVacacionesService(
            db, quien, new SettingsService(db, quien, auditoria),
            new SignatureService(db, quien, auditoria),
            generador ?? new GeneradorEspia(), new PlantillaDeVacacionesOpenXml(), auditoria,
            Fabrica.Vacaciones(db, quien), new NotificationService(db));
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
    /// Cambia la petición por debajo de la firma: es LA forma de invalidarla, porque la huella se
    /// calcula sobre las fechas y el motivo.
    /// </summary>
    private static void MoverElUltimoDia(AppDbContext db, int solicitudId)
    {
        db.VacationRequests.Find(solicitudId)!.EndDate = new DateTime(2026, 9, 12);
        db.SaveChanges();
    }

    private static async Task<SolicitudDeVacacionesLeida> Leer(AppDbContext db, int solicitudId) =>
        (await DelLider(db, Lider).SolicitudesAsync()).Single(s => s.Id == solicitudId);

    /// <summary>
    /// El caso completo, montado tal como ocurre: Ana firma, el líder resuelve y DESPUÉS la firma se
    /// cae. Devuelve la solicitud ya bloqueada.
    /// </summary>
    private static async Task<VacationRequest> ResueltaConLaFirmaCaida(AppDbContext db)
    {
        var v = Solicitud(db);
        await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);
        await DelLider(db, Lider).ResolverAsync(v.Id, VacationStatus.Aprobada, "Aprobadas.");
        MoverElUltimoDia(db, v.Id);
        return v;
    }

    // ── Lo que NO cambia: el bloqueo sigue en pie ────────────────────────────────

    [Fact]
    public async Task SinUsarNingunaDeLasDosSalidas_ArchivarConLaFirmaCAIDASigueRechazandose()
    {
        // Esto es lo primero que hay que asegurar al abrir un callejón: que no se abrió el muro. Las
        // salidas son dos puertas con nombre, no un permiso general para archivar papeles cojos.
        var db = BaseConAna();
        var v = await ResueltaConLaFirmaCaida(db);

        var lider = DelLider(db, Lider);
        var (ok, mensaje) = await lider.FirmarAsync(v.Id, await FirmaDelJefe(db));

        Assert.False(ok);
        Assert.Contains("dejó de valer", mensaje);

        // Y el mensaje ENSEÑA LAS DOS: es lo único que el líder va a leer, y de ahí sale lo que haga
        // después. Antes terminaba mandándolo a una salida que en este caso estaba apagada.
        Assert.Contains("vuelva a firmar", mensaje);
        Assert.Contains("sin su firma", mensaje);

        // Nada archivado: una negativa no puede dejar un documento a medias.
        Assert.False((await Leer(db, v.Id)).DocumentoFirmado);
        Assert.Empty((await lider.DocumentoFirmadoAsync(v.Id)).pdf);
    }

    // ── Salida A: que vuelva a firmar, aunque la solicitud esté resuelta ─────────

    [Fact]
    public async Task ConLaSolicitudRESUELTA_ElLiderYAPuedePedirLaFirmaYLlegaElAviso()
    {
        // Aquí estaba el nudo: PuedeVolverAFirmar salía de «la solicitud sigue pendiente», y el
        // bloqueo solo se dispara cuando la firma se cayó, cosa que pasa igual de bien después de
        // resolver. El líder se quedaba con el papel imposible y el botón gris.
        var db = BaseConAna();
        var v = await ResueltaConLaFirmaCaida(db);

        var (ok, mensaje) = await DelLider(db, Lider)
            .PedirQueVuelvaAFirmarAsync(v.Id, "Te moví el regreso al 14.");

        Assert.True(ok, mensaje);

        var aviso = db.Notifications.AsNoTracking().Single();
        Assert.Equal(UsuarioDeAna, aviso.ForUserId);
        Assert.Contains("dejó de valer", aviso.Title);
        // El recado DICE que la solicitud ya está resuelta. Sin esa frase, quien lo reciba abriría una
        // solicitud con respuesta y pensaría que el aviso llegó tarde o que se equivocaron.
        Assert.Contains("Aprobada", aviso.Message);
        Assert.Contains("Te moví el regreso al 14.", aviso.Message);
        Assert.Equal("mis-vacaciones", aviso.Url);
    }

    [Fact]
    public async Task AlVolverAFirmarUnaSolicitudRESUELTA_LaFirmaNUEVAVALEYElPapelSaleCompleto()
    {
        // La mitad que de verdad importa de la salida A. Un aviso que llevara a una firma que nace
        // inválida sería peor que no tener salida: la persona firmaría, la pantalla le seguiría
        // diciendo que no vale y nadie entendería por qué. La huella se recalcula sobre los datos de
        // AHORA, que es lo que hace válida a la firma nueva.
        var db = BaseConAna();
        var v = await ResueltaConLaFirmaCaida(db);
        var deAna = Fabrica.Vacaciones(db, Ana);

        var (firmo, mensajeFirma) = await deAna.FirmarAsync(v.Id, Png(), 150, 50);
        Assert.True(firmo, mensajeFirma);

        var papeles = await deAna.PapelesDeAsync([v.Id]);
        Assert.True(papeles[v.Id].Firmada);
        Assert.True(papeles[v.Id].SigueValiendo);
        Assert.NotNull(papeles[v.Id].FirmaId);

        // Y con eso el documento sale por el camino limpio, CON las dos firmas: el bloqueo se
        // convirtió en un trámite, que era el objetivo.
        var lider = DelLider(db, Lider);
        var (ok, mensaje) = await lider.FirmarAsync(v.Id, await FirmaDelJefe(db));

        Assert.True(ok, mensaje);
        Assert.True((await Leer(db, v.Id)).DocumentoFirmado);
    }

    [Fact]
    public async Task ElCallejonDeVerdad_BorrarElTrazoEnFirmasSobreUnaSolicitudYARESUELTA()
    {
        // El camino por el que se llegaba aquí sin tocar la solicitud, que es lo que hacía el callejón
        // difícil de creer y fácil de alcanzar: la firma de vacaciones se guarda como un
        // SignatureProfile de la persona y aparece en el gestor de «Firmas». Al borrarla ahí, el enlace
        // se queda sin imagen —SetNull— y se lee, con toda la razón, como una firma que dejó de valer.
        var db = BaseConAna();
        var v = Solicitud(db);
        await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);

        var lider = DelLider(db, Lider);
        await lider.ResolverAsync(v.Id, VacationStatus.Aprobada, "Aprobadas.");

        var trazoDeAna = db.SignatureProfiles.AsNoTracking().Single(s => s.OwnerDeveloperId == AnaId).Id;
        var auditoria = new AuditService(db, Lider, new OrigenDePrueba());
        Assert.True((await new SignatureService(db, Lider, auditoria).EliminarAsync(trazoDeAna)).ok);

        var firma = (await Leer(db, v.Id)).FirmaDelColaborador;

        // El bloqueo se disparó, y la solicitud no se tocó en ningún momento.
        Assert.True(firma.DejoDeValer);
        Assert.False(DocumentoDeVacacionesService.SePuedeArchivar(firma));

        // Y LAS DOS SALIDAS están encendidas. Antes, la única que había venía apagada de fábrica: ese
        // documento no se podía archivar nunca más.
        Assert.True(firma.PuedeVolverAFirmar);
        Assert.True(DocumentoDeVacacionesService.SePuedeArchivarSinLaFirma(firma));
    }

    [Theory]
    [InlineData(VacationStatus.Aprobada)]
    [InlineData(VacationStatus.Rechazada)]
    [InlineData(VacationStatus.Cancelada)]
    public async Task LaPuertaSEABRESOLOParaReponer_NoParaFirmarPorPrimeraVezAlgoResuelto(VacationStatus estado)
    {
        // El límite de la salida A, y es donde se ve que no se abrió el muro entero. Sin una firma que
        // reponer no hay nada que arreglar: el papel no está bloqueado —sale como toda la vida, con el
        // hueco para firmar a mano— y firmar una decisión ya tomada seguiría sin significar nada.
        var db = BaseConAna();
        var v = Solicitud(db);
        await DelLider(db, Lider).ResolverAsync(v.Id, estado, "Resuelta.");

        var (ok, mensaje) = await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);

        Assert.False(ok);
        Assert.Contains(estado.ToString(), mensaje);
        Assert.Contains("reponer", mensaje);

        // Y el líder tampoco puede pedirle un recado que ella no podría atender.
        var (pedido, mensajePedido) = await DelLider(db, Lider).PedirQueVuelvaAFirmarAsync(v.Id);
        Assert.False(pedido);
        Assert.Contains("nunca la firmó", mensajePedido);
        Assert.Empty(db.Notifications);
    }

    // ── Salida B: archivar sin ella, diciéndolo ─────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n ")]
    public async Task ArchivarSinLaFirmaEXIGEMotivoYNoLoDaPorBuenoEnBlanco(string? motivo)
    {
        // El motivo es lo único que explicará el hueco a quien abra el expediente dentro de un año. Los
        // espacios cuentan como vacío a propósito: un campo obligatorio que se satisface con la barra
        // espaciadora no es un campo obligatorio.
        var db = BaseConAna();
        var v = await ResueltaConLaFirmaCaida(db);

        var lider = DelLider(db, Lider);
        var (ok, mensaje) = await lider.ArchivarSinLaFirmaAsync(v.Id, await FirmaDelJefe(db), motivo);

        Assert.False(ok);
        Assert.Contains("Escribe por qué", mensaje);

        // Y no se archivó nada: rechazar tiene que dejar la solicitud como estaba.
        Assert.False((await Leer(db, v.Id)).DocumentoFirmado);
        Assert.Empty((await lider.DocumentoFirmadoAsync(v.Id)).pdf);
    }

    [Fact]
    public async Task ArchivarSinLaFirmaGuardaElPapelYDejaAsientoConQuienYPorQue()
    {
        var db = BaseConAna();
        var v = await ResueltaConLaFirmaCaida(db);

        var lider = DelLider(db, Lider);
        var (ok, mensaje) = await lider.ArchivarSinLaFirmaAsync(
            v.Id, await FirmaDelJefe(db), "  Ana ya no trabaja aquí y el papel se necesita hoy.  ");

        Assert.True(ok, mensaje);
        Assert.True((await Leer(db, v.Id)).DocumentoFirmado);
        Assert.NotEmpty((await lider.DocumentoFirmadoAsync(v.Id)).pdf);

        // EL ASIENTO. La pregunta de dentro de un año no es «¿quién archivó esto?» sino «¿quién decidió
        // archivarlo sin su firma, y por qué?»: el quién lo pone la sesión, el porqué solo puede venir
        // del motivo que se escribió.
        var asiento = db.AuditLogs.AsNoTracking().ToList()
            .Last(a => a.EntityType == "VacationDocument");

        Assert.Contains("SIN la firma", asiento.Details!);
        Assert.Contains("Ana", asiento.Details!);
        Assert.Contains("Ana ya no trabaja aquí y el papel se necesita hoy.", asiento.Details!);
        Assert.Equal(Lider.UserId, asiento.UserId);
    }

    [Fact]
    public async Task ElDocumentoArchivadoSinLaFirmaLODICE_ConElMotivoYConQuienLoDecidio()
    {
        // LO QUE DE VERDAD SEPARA ESTA SALIDA de lo que la aplicación hacía antes. Antes el papel salía
        // con el hueco en blanco y callado, y uno así no se descubre hasta que hay un problema: quien
        // lo lee no sabe si la persona se negó, si nadie se lo pidió o si el sistema falló.
        var db = BaseConAna();
        var v = await ResueltaConLaFirmaCaida(db);

        var espia = new GeneradorEspia();
        var lider = DelLider(db, Lider, espia);

        var (ok, mensaje) = await lider.ArchivarSinLaFirmaAsync(
            v.Id, await FirmaDelJefe(db), "Ana ya no trabaja aquí.");
        Assert.True(ok, mensaje);

        var datos = espia.Ultimo;
        Assert.NotNull(datos);

        // Las tres cosas que contestan «¿y esta firma?»: que la ausencia fue una DECISIÓN, de quién y
        // por qué.
        Assert.Contains("ARCHIVADO SIN LA FIRMA DE ANA", datos!.Observaciones);
        Assert.Contains("Ana ya no trabaja aquí.", datos.Observaciones);
        Assert.Contains("Admin", datos.Observaciones);   // el FullName de la sesión del líder

        // Y el papel sale de verdad sin la firma de ella y con la del jefe: si la firma caída se
        // colara igual, la advertencia impresa sería falsa.
        Assert.Null(datos.FirmaDelColaborador);
        Assert.NotNull(datos.FirmaDelJefe);
    }

    [Fact]
    public async Task LaAdvertenciaSEANTEPONEYNoSeLLEVAPORDELANTELaRespuestaDelLider()
    {
        // Dos cosas en una: que lo que el líder escribió al resolver sigue en el papel —sustituirlo
        // habría hecho que archivar sin la firma borrara la respuesta que se le dio a la persona— y que
        // la advertencia va DELANTE, porque una advertencia debajo de tres renglones no se lee.
        var db = BaseConAna();
        var v = await ResueltaConLaFirmaCaida(db);

        var espia = new GeneradorEspia();
        await DelLider(db, Lider, espia)
            .ArchivarSinLaFirmaAsync(v.Id, await FirmaDelJefe(db), "No hay forma de localizarla.");

        var observaciones = espia.Ultimo!.Observaciones;

        Assert.Contains("Aprobadas.", observaciones);
        Assert.StartsWith("ARCHIVADO SIN LA FIRMA", observaciones);
        Assert.True(observaciones.IndexOf("ARCHIVADO SIN LA FIRMA", StringComparison.Ordinal)
                  < observaciones.IndexOf("Aprobadas.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ArchivarSinLaFirmaNOEsUnAtajoCuandoLaFirmaSIVALE()
    {
        // Si esta salida estuviera disponible siempre, acabaría siendo el botón que se pulsa por
        // costumbre y el documento cargaría una confesión que además sería mentira: la firma estaba.
        var db = BaseConAna();
        var v = Solicitud(db);
        await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);

        var lider = DelLider(db, Lider);
        await lider.ResolverAsync(v.Id, VacationStatus.Aprobada, "Aprobadas.");

        var (ok, mensaje) = await lider.ArchivarSinLaFirmaAsync(
            v.Id, await FirmaDelJefe(db), "Por si acaso.");

        Assert.False(ok);
        Assert.Contains("camino normal", mensaje);
        Assert.False((await Leer(db, v.Id)).DocumentoFirmado);
    }

    [Fact]
    public async Task ArchivarSinLaFirmaTampocoValeCuandoNUNCAHuboFirma()
    {
        // Ahí no hay ninguna firma caída que descartar y el camino normal YA archiva: el escritorio ni
        // siquiera guardaba la firma del colaborador. Añadirle al papel una confesión sería mentir
        // sobre lo que pasó.
        var db = BaseConAna();
        var v = Solicitud(db);

        var lider = DelLider(db, Lider);
        await lider.ResolverAsync(v.Id, VacationStatus.Aprobada, "Aprobadas.");

        var (ok, mensaje) = await lider.ArchivarSinLaFirmaAsync(
            v.Id, await FirmaDelJefe(db), "Nunca firmó.");

        Assert.False(ok);
        Assert.Contains("nunca firmó", mensaje);

        // Y por el camino normal sale sin problema, que es justo el argumento.
        Assert.True((await lider.FirmarAsync(v.Id, await FirmaDelJefe(db))).ok);
    }

    [Fact]
    public async Task ArchivarSinLaFirmaEsDelLIDERYNoDeQuienNoLaPusoNunca()
    {
        // La guarda va delante de la validación del motivo: a quien no le toca esta operación no se le
        // contesta primero cómo usarla bien.
        var db = BaseConAna();
        var v = await ResueltaConLaFirmaCaida(db);
        var firmaDelJefe = await FirmaDelJefe(db);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => DelLider(db, Ana).ArchivarSinLaFirmaAsync(v.Id, firmaDelJefe, "Me lo archivo yo."));

        await Assert.ThrowsAsync<AuthorizationException>(
            () => DelLider(db, Ana).ArchivarSinLaFirmaAsync(v.Id, firmaDelJefe, motivo: null));
    }

    // ── Las dos salidas conviven ─────────────────────────────────────────────────

    [Fact]
    public async Task ArchivarSinLaFirmaNoCierraLaOtraPuerta_SiLuegoFirmaElPapelSeRehaceCompleto()
    {
        // Renunciar a la firma para hoy no puede significar renunciar a ella para siempre: el papel
        // urgente sale, y si la persona aparece después, el documento bueno lo sustituye. Si esta
        // salida dejara la solicitud marcada como cerrada, el líder habría cambiado un callejón por
        // otro.
        var db = BaseConAna();
        var v = await ResueltaConLaFirmaCaida(db);

        var espia = new GeneradorEspia();
        var lider = DelLider(db, Lider, espia);
        var firmaDelJefe = await FirmaDelJefe(db);

        Assert.True((await lider.ArchivarSinLaFirmaAsync(v.Id, firmaDelJefe, "Urge.")).ok);
        Assert.Contains("ARCHIVADO SIN LA FIRMA", espia.Ultimo!.Observaciones);

        // Ana aparece y vuelve a firmar.
        Assert.True((await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50)).ok);
        Assert.True((await Leer(db, v.Id)).FirmaDelColaborador.Vigente);

        // Y el documento se rehace por el camino limpio: sin la advertencia y con su firma dentro.
        var (ok, mensaje) = await lider.FirmarAsync(v.Id, firmaDelJefe);
        Assert.True(ok, mensaje);
        Assert.DoesNotContain("ARCHIVADO SIN LA FIRMA", espia.Ultimo!.Observaciones);
        Assert.NotNull(espia.Ultimo.FirmaDelColaborador);

        // Y sigue habiendo UN solo documento archivado: el que vale es el último.
        Assert.Equal(1, (await Fabrica.Vacaciones(db, Ana).PapelesDeAsync([v.Id]))[v.Id].DocumentosGenerados);
    }

    [Fact]
    public async Task NingunaDeLasDosSalidasSirveMientrasLaSolicitudSiguePENDIENTE()
    {
        // El documento definitivo imprime la casilla de autorizada o rechazada: sin decisión saldrían
        // las dos en blanco. Esa regla es anterior a todo esto y no se toca — y conviene comprobarlo,
        // porque una salida nueva mal puesta es exactamente por donde se cuela el papel sin resolver.
        var db = BaseConAna();
        var v = Solicitud(db);
        await Fabrica.Vacaciones(db, Ana).FirmarAsync(v.Id, Png(), 150, 50);
        MoverElUltimoDia(db, v.Id);

        var lider = DelLider(db, Lider);
        var firmaDelJefe = await FirmaDelJefe(db);

        Assert.False((await lider.FirmarAsync(v.Id, firmaDelJefe)).ok);

        var (ok, mensaje) = await lider.ArchivarSinLaFirmaAsync(v.Id, firmaDelJefe, "Urge.");
        Assert.False(ok);
        Assert.Contains("Resuelve la solicitud", mensaje);
        Assert.False((await Leer(db, v.Id)).DocumentoFirmado);
    }
}
