using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// PROPONER TRABAJO AL POOL: la puerta que sustituyó a la autocalificación.
///
/// <para><b>Qué cambió y por qué.</b> Antes, el trabajo que no estaba publicado —una investigación,
/// un apagafuegos, ayudar a otro equipo— se registraba después de hacerlo y el líder decidía si valía
/// puntos. Eso era ponerle precio a algo ya hecho, que es exactamente lo que el pool existe para
/// evitar. Ahora se PROPONE: la actividad nace a nombre de quien la propuso y SIN VALOR, y el líder
/// le pone tipo y complejidad, de donde salen los puntos. El precio vuelve a fijarse antes.</para>
///
/// <para><b>La pieza que lo hace barato.</b> Una propuesta no es un estado nuevo: es un
/// «Por clasificar» —el que ya usaban las actividades que llegan solas desde Azure DevOps— CON
/// RECLAMO. Ese único dato es el que la separa de las otras, y de él cuelgan cinco comportamientos
/// que no hubo que escribir. Las pruebas de abajo los fijan uno por uno, porque un modelo que se
/// apoya en un solo campo se rompe entero el día que alguien lo limpie «de paso».</para>
///
/// <para><b>Y el desenlace doble de clasificar</b>, que es la línea más delicada de todo el cambio:
/// sin reclamo la actividad sale <c>Disponible</c>, al pool común; con reclamo sale <c>Tomada</c>, a
/// las manos de quien la propuso. La regresión de que lo venido de DevOps SIGUE yendo al pool está
/// aquí abajo y es obligatoria: si esa rama se cayera, cada ticket clasificado quedaría asignado a
/// nadie o —peor— a quien no lo pidió, y nadie se enteraría hasta que faltara trabajo en el pool.</para>
/// </summary>
public class ProponerAlPoolTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var ctx in _contextos) ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext OtroContexto(AppDbContext db)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(db.Database.GetConnectionString()).Options;
        var ctx = new AppDbContext(opts);
        _contextos.Add(ctx);
        return ctx;
    }

    private PoolActivityService Svc(AppDbContext db, ICurrentUser cu)
    {
        var ctx = OtroContexto(db);
        var bitacora = new AuditService(ctx, cu, new OrigenDePrueba());
        return new PoolActivityService(ctx, cu, bitacora, new NotificationService(ctx),
                                       new SettingsService(ctx, cu, bitacora));
    }

    private static async Task<AppDbContext> BaseConPoolAsync()
    {
        var db = TestDb.New();
        await PoolSeed.SembrarAsync(db);
        return db;
    }

    private static int NuevoDesarrollador(AppDbContext db, string nombre)
    {
        var d = new Developer { FullName = nombre, IsActive = true };
        db.Developers.Add(d);
        db.SaveChanges();
        return d.Id;
    }

    private static UsuarioDePrueba Admin(int userId = 9) => UsuarioDePrueba.Como(UserRole.Admin, userId: userId);
    private static UsuarioDePrueba Dev(int developerId, int userId = 1) =>
        UsuarioDePrueba.Como(UserRole.Desarrollador, developerId, userId);

    /// <summary>
    /// Lo que el líder manda al clasificar. Es el MISMO borrador que al publicar —tipo, complejidad y
    /// el número que ese tipo exige— porque clasificar y editar son la misma ruta: es justamente lo
    /// que hace que no haya un segundo formulario que mantener.
    /// </summary>
    private static PoolActivity Clasificacion(string titulo,
        PoolWorkType tipo = PoolWorkType.Tarea, PoolComplexity complejidad = PoolComplexity.Media) =>
        new()
        {
            Title          = titulo,
            WorkType       = tipo,
            Complexity     = complejidad,
            HorasLimite    = tipo.ComoBug() ? 16m : null,
            HorasEstimadas = tipo.ComoBug() ? null : 6m
        };

    /// <summary>
    /// Una actividad como la que trae el alta automática desde un work item: «Por clasificar», sin
    /// puntos y SIN DUEÑO. Se siembra a mano en vez de llamar al servicio de DevOps porque lo que se
    /// prueba aquí no es la ingesta sino qué hace clasificar con lo que ella deja: la forma de la
    /// fila es todo lo que importa, y es exactamente ésta.
    /// </summary>
    private static async Task<PoolActivity> DesdeDevOpsAsync(AppDbContext db, string titulo, int workItem)
    {
        var a = new PoolActivity
        {
            Title = titulo,
            Status = PoolActivityStatus.PorClasificar,
            Points = 0,
            DevOpsWorkItemId = workItem,
            CreatedAt = DateTime.UtcNow
        };
        db.PoolActivities.Add(a);
        await db.SaveChangesAsync();
        return a;
    }

    private static Task<PoolActivity> LeerAsync(AppDbContext db, int id) =>
        db.PoolActivities.AsNoTracking().FirstAsync(a => a.Id == id);

    // ── 1. Cómo nace una propuesta ───────────────────────────────────────────────

    /// <summary>
    /// Los cuatro campos que definen una propuesta, y ninguno es casual: «Por clasificar» la deja
    /// fuera de lo tomable, los CERO puntos dicen que todavía no vale nada, el RECLAMO dice de quién
    /// es, y <c>ClaimedAt</c> VACÍO dice que el reloj no ha empezado.
    ///
    /// <para>Ese último es el que más fácil se colaría mal. <c>ClaimedAt</c> no significa «de quién
    /// es» sino «cuándo empezó a correr el plazo», y una propuesta no tiene plazo todavía —no se sabe
    /// ni de cuántas horas es, porque eso sale del tipo que el líder aún no ha puesto—. Escribirlo al
    /// proponer dejaría actividades vencidas antes de que nadie supiera lo que valían.</para>
    /// </summary>
    [Fact]
    public async Task Proponer_NaceSinValorYANombreDeQuienLaPropuso()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");

        var (ok, mensaje, actividad) = await Svc(db, Dev(dev)).ProponerAsync(
            dev, "Investigar por qué el reporte tarda 40 s", "Pasó tres veces esta semana.",
            "https://dev.azure.com/org/proj/_workitems/edit/91", null);

        Assert.True(ok, mensaje);

        var g = await LeerAsync(db, actividad!.Id);
        Assert.Equal(PoolActivityStatus.PorClasificar, g.Status);
        Assert.Equal(0, g.Points);
        Assert.Equal(dev, g.ClaimedByDeveloperId);
        Assert.Null(g.ClaimedAt);
        Assert.Null(g.ClaimDeadlineAt);
        Assert.Equal("Pasó tres veces esta semana.", g.Description);

        // El work item sale del enlace sin haberlo escrito aparte: es el mismo resolutor que usa
        // publicar, y ahorra tener que pedir el número cuando ya se pegó la dirección.
        Assert.Equal(91, g.DevOpsWorkItemId);
    }

    /// <summary>
    /// NO SALE HACIA DEVOPS mientras no esté clasificada. La guarda no está en proponer sino en
    /// <c>PoolActivity.YaPublicada</c>, que calla a todo lo que no tiene tipo ni puntos que afirmar:
    /// por eso proponer no tiene ni una línea sobre el empuje.
    ///
    /// <para>Importa porque el empuje resuelve credenciales personales, y una propuesta hecha desde
    /// una pantalla sin sesión de DevOps dejaría «no hay sesión» escrito como último error. Y aunque
    /// no fallara, mandaría al ticket una prioridad «Media» que allá se escribe como 3 sobre el 2 que
    /// trae por omisión: le bajaría la prioridad al ticket de otra persona.</para>
    /// </summary>
    [Fact]
    public async Task Proponer_NoLeMandaNadaAlWorkItem()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");

        var (ok, _, actividad) = await Svc(db, Dev(dev)).ProponerAsync(dev, "Algo", null, null, 77);
        Assert.True(ok);

        var g = await LeerAsync(db, actividad!.Id);
        Assert.False(g.YaPublicada);
        Assert.Null(g.DevOpsEstadoEnviado);
        Assert.Null(g.DevOpsAsignadoADeveloperId);
    }

    /// <summary>
    /// Dos de los cinco comportamientos gratis, que son los que hacen que un estado nuevo no hiciera
    /// falta: la propuesta aparece en «lo mío» y NO en lo disponible. Ninguna de las dos consultas se
    /// tocó — una filtra por el reclamo y la otra por <c>Disponible</c>.
    /// </summary>
    [Fact]
    public async Task Proponer_SaleEnLoMio_YNoEnLoDisponible()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");

        await Svc(db, Dev(dev)).ProponerAsync(dev, "Lo mío", null, null, null);

        Assert.Single(await Svc(db, Dev(dev)).MisDelPoolAsync(dev));
        Assert.Empty(await Svc(db, Dev(dev)).DisponiblesAsync());
    }

    /// <summary>El tercero: nadie más se la puede llevar, porque tomar exige <c>Disponible</c>.</summary>
    [Fact]
    public async Task Proponer_NadieMasSeLaPuedeLlevar()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");

        var (_, _, propuesta) = await Svc(db, Dev(ana)).ProponerAsync(ana, "Lo mío", null, null, null);

        var (ok, mensaje) = await Svc(db, Dev(beto, userId: 2)).TomarAsync(propuesta!.Id, beto);

        Assert.False(ok);
        Assert.Contains("tomó primero", mensaje);
        Assert.Equal(ana, (await LeerAsync(db, propuesta.Id)).ClaimedByDeveloperId);
    }

    // ── 2. Lo que no se admite ───────────────────────────────────────────────────

    /// <summary>
    /// No se propone a nombre de otro. Es una EXCEPCIÓN y no un «false», igual que en todo lo demás
    /// que comprueba propiedad: la ruta no lleva identificador, así que llegar aquí con uno ajeno no
    /// es un error del usuario sino una llamada que no debería existir.
    /// </summary>
    [Fact]
    public async Task Proponer_NoSePuedeANombreDeOtro()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int ana = NuevoDesarrollador(db, "Ana");
        int beto = NuevoDesarrollador(db, "Beto");

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, Dev(ana)).ProponerAsync(beto, "A tu nombre", null, null, null));

        Assert.Empty(db.PoolActivities);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData(null)]
    public async Task Proponer_ExigeTitulo(string? titulo)
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");

        var (ok, _, _) = await Svc(db, Dev(dev)).ProponerAsync(dev, titulo, "detalle", null, null);

        Assert.False(ok);
        Assert.Empty(db.PoolActivities);
    }

    /// <summary>
    /// El título se rechaza pasado de 200 y el detalle pasado de 4 000, que es lo que admiten las
    /// columnas. Se RECHAZA en vez de recortarse, al revés que en el alta automática: allá el texto
    /// viene de fuera y recortarlo es lo único que se puede hacer; aquí lo acaba de escribir alguien
    /// que está mirando la pantalla, y guardárselo a medias sería perderle trabajo sin decírselo.
    /// </summary>
    [Fact]
    public async Task Proponer_RechazaLoDesmedidoEnVezDeRecortarlo()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");

        var (okTitulo, _, _) = await Svc(db, Dev(dev))
            .ProponerAsync(dev, new string('t', 201), null, null, null);
        Assert.False(okTitulo);

        var (okDetalle, _, _) = await Svc(db, Dev(dev))
            .ProponerAsync(dev, "Bien", new string('d', PoolActivityService.MaxDetalle + 1), null, null);
        Assert.False(okDetalle);

        Assert.Empty(db.PoolActivities);
    }

    /// <summary>
    /// El mismo validador de enlaces que en todo lo demás: solo http y https. El líder abre ese
    /// enlace con el navegador al clasificar, así que admitir cualquier esquema convertiría un campo
    /// de texto que llena el desarrollador en una forma de hacerle ejecutar algo con un clic.
    /// </summary>
    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("dev.azure.com/sin/esquema")]
    public async Task Proponer_RechazaEnlacesQueNoSonHttp(string enlace)
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");

        var (ok, mensaje, _) = await Svc(db, Dev(dev)).ProponerAsync(dev, "Bien", null, enlace, null);

        Assert.False(ok);
        Assert.Contains("http", mensaje);
        Assert.Empty(db.PoolActivities);
    }

    /// <summary>
    /// Y no se propone algo que ya tiene actividad viva sobre el mismo work item. Sin esta guarda las
    /// dos se pisarían el esfuerzo y la prioridad en el ticket la una a la otra, y ninguna se
    /// enteraría — que es el mismo motivo por el que publicar lo comprueba.
    /// </summary>
    [Fact]
    public async Task Proponer_RechazaUnWorkItemQueYaTieneActividadViva()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");
        await DesdeDevOpsAsync(db, "Ya está en el pool", workItem: 404);

        var (ok, _, _) = await Svc(db, Dev(dev)).ProponerAsync(dev, "Lo mismo otra vez", null, null, 404);

        Assert.False(ok);
        Assert.Equal(1, db.PoolActivities.Count());
    }

    // ── 3. El tope, que es lo que impide llegar al doble ─────────────────────────

    /// <summary>
    /// LAS PROPUESTAS CUENTAN DENTRO DEL TOPE, y lo hacen en los dos sentidos: no se puede proponer
    /// con el tope lleno de tomadas, ni tomar con el tope lleno de propuestas.
    ///
    /// <para>La segunda mitad es la que de verdad importaba. Si tomar no contara las propuestas,
    /// alguien podría llegar al doble de trabajo vivo proponiendo en vez de tomando —y descubrirlo el
    /// día que el líder clasificara las propuestas y se las encontrara todas tomadas de golpe—. Por
    /// eso las dos preguntan por la misma cuenta y no por dos listas de estados escritas aparte.</para>
    /// </summary>
    [Fact]
    public async Task ElTope_CuentaLasPropuestasEnLosDosSentidos()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");

        // Se llena el tope con PROPUESTAS, que es el caso nuevo.
        for (int i = 0; i < PoolActivityService.MaxTomadasPorOmision; i++)
        {
            var (ok, mensaje, _) = await Svc(db, Dev(dev)).ProponerAsync(dev, $"Propuesta {i}", null, null, null);
            Assert.True(ok, mensaje);
        }

        // Ni una más.
        var (okOtra, mensajeOtra, _) = await Svc(db, Dev(dev)).ProponerAsync(dev, "Una más", null, null, null);
        Assert.False(okOtra);
        Assert.Contains("tope", mensajeOtra);

        // Y tampoco se puede TOMAR nada del pool: es la mitad que se habría escapado.
        var publicada = new PoolActivity
        {
            Title = "Libre", WorkType = PoolWorkType.Tarea, Complexity = PoolComplexity.Media,
            Points = 5, Status = PoolActivityStatus.Disponible, HorasEstimadas = 6m, CreatedAt = DateTime.UtcNow
        };
        db.PoolActivities.Add(publicada);
        await db.SaveChangesAsync();

        var (okTomar, mensajeTomar) = await Svc(db, Dev(dev)).TomarAsync(publicada.Id, dev);

        Assert.False(okTomar);
        Assert.Contains("sin clasificar", mensajeTomar);
    }

    // ── 4. Clasificar: los dos desenlaces ────────────────────────────────────────

    /// <summary>
    /// Clasificar una PROPUESTA la deja tomada por quien la propuso, y «tomada» tiene que significar
    /// exactamente lo mismo que por el otro camino: con su plazo corriendo, con el checklist copiado
    /// y con la percha del cronómetro creada.
    ///
    /// <para>Las tres se comprueban porque las tres son lo que la palabra promete. Una actividad
    /// tomada sin checklist NO SE PUEDE ENTREGAR —entregar exige cumplirlo— y una sin percha no se
    /// puede cronometrar: olvidar cualquiera de las dos en este camino dejaría trabajo asignado que
    /// no se puede ni medir ni terminar, y sin ningún mensaje de error que lo delatara.</para>
    ///
    /// <para>El PLAZO cuenta desde la clasificación y no desde la propuesta, por lo mismo que al
    /// tomar: hasta que el líder no dice de qué clase es, no hay plazo que consumir.</para>
    /// </summary>
    [Fact]
    public async Task Clasificar_UnaPropuesta_QuedaTomadaPorQuienLaPropuso()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");

        var (_, _, propuesta) = await Svc(db, Dev(dev))
            .ProponerAsync(dev, "Investigar la caída", "Lleva tres días.", null, null);

        var antes = DateTime.UtcNow;
        var (ok, mensaje) = await Svc(db, Admin())
            .EditarAsync(propuesta!.Id, Clasificacion("Investigar la caída"));
        Assert.True(ok, mensaje);

        var g = await LeerAsync(db, propuesta.Id);
        Assert.Equal(PoolActivityStatus.Tomada, g.Status);
        Assert.Equal(dev, g.ClaimedByDeveloperId);
        Assert.True(g.Points > 0, "clasificar es lo que le pone valor");
        Assert.NotNull(g.ClaimedAt);
        Assert.True(g.ClaimedAt >= antes, "el reloj arranca al clasificar, no al proponer");
        Assert.NotNull(g.ClaimDeadlineAt);

        // El checklist congelado del tipo, que es lo que se le va a exigir para entregar.
        Assert.NotEmpty(await Svc(db, Dev(dev)).ChecklistDeAsync(propuesta.Id));

        // Y la percha del cronómetro, marcada como tal para que no se pueda calificar por fuera.
        Assert.NotNull(g.LinkedDevActivityId);
        var percha = await db.DevActivities.AsNoTracking().FirstAsync(a => a.Id == g.LinkedDevActivityId);
        Assert.Equal(dev, percha.DeveloperId);
        Assert.Equal(propuesta.Id, percha.PoolActivityId);
    }

    /// <summary>
    /// LA REGRESIÓN OBLIGATORIA: lo que entró solo desde un work item SIGUE saliendo al pool común.
    ///
    /// <para>Es la contraparte de la prueba de arriba y la razón de que las dos existan. Clasificar
    /// se volvió bimodal, y el único dato que decide a dónde va la actividad es el reclamo. Si esa
    /// rama se rompiera —o si alguien decidiera que «clasificar siempre asigna»— cada ticket
    /// clasificado quedaría a nombre de quien no lo pidió, el pool se quedaría vacío, y nadie se
    /// enteraría hasta que alguien preguntara por qué no hay nada que tomar.</para>
    ///
    /// <para>Se comprueba también que NO se le crea percha ni checklist: eso es de lo que está en
    /// marcha, y esto todavía no lo está.</para>
    /// </summary>
    [Fact]
    public async Task Clasificar_LoQueVinoDeDevOps_SigueSaliendoAlPoolDisponible()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        var delTicket = await DesdeDevOpsAsync(db, "Corregir el cálculo", workItem: 512);

        var (ok, mensaje) = await Svc(db, Admin())
            .EditarAsync(delTicket.Id, Clasificacion("Corregir el cálculo"));
        Assert.True(ok, mensaje);

        var g = await LeerAsync(db, delTicket.Id);
        Assert.Equal(PoolActivityStatus.Disponible, g.Status);
        Assert.Null(g.ClaimedByDeveloperId);
        Assert.Null(g.ClaimedAt);
        Assert.Null(g.ClaimDeadlineAt);
        Assert.Null(g.LinkedDevActivityId);

        Assert.Empty(await Svc(db, Admin()).ChecklistDeAsync(delTicket.Id));
        Assert.Single(await Svc(db, Admin()).DisponiblesAsync());
    }

    /// <summary>
    /// Y editar una actividad YA publicada no la asigna a nadie: la bimodalidad solo actúa al
    /// clasificar. Sin esto, corregirle el título a una actividad disponible se la llevaría de las
    /// manos a todo el mundo.
    /// </summary>
    [Fact]
    public async Task Editar_LoQueYaEstaPublicado_SigueSiendoDeNadie()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;

        var (_, _, publicada) = await Svc(db, Admin()).CrearAsync(Clasificacion("Una tarea"));

        var (ok, mensaje) = await Svc(db, Admin())
            .EditarAsync(publicada!.Id, Clasificacion("Una tarea, con otro nombre"));
        Assert.True(ok, mensaje);

        var g = await LeerAsync(db, publicada.Id);
        Assert.Equal(PoolActivityStatus.Disponible, g.Status);
        Assert.Null(g.ClaimedByDeveloperId);
    }

    // ── 5. Descartar una propuesta ───────────────────────────────────────────────

    /// <summary>
    /// UNA PROPUESTA NO SE DESCARTA EN SILENCIO: el motivo es obligatorio cuando alguien la propuso.
    ///
    /// <para>La regla no es «escribe siempre» —eso se acaba contestando con un punto— sino «escribe
    /// cuando alguien lo va a leer». Y no es cortesía: una propuesta rechazada sin una palabra mata
    /// la función en una semana, porque nadie vuelve a proponer si la vez anterior su trabajo
    /// desapareció sin explicación.</para>
    /// </summary>
    [Fact]
    public async Task Descartar_UnaPropuesta_ExigeMotivoYLoAvisa()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");
        db.Users.Add(new User { Username = "ana", FullName = "Ana", Role = UserRole.Desarrollador,
                                IsActive = true, DeveloperId = dev, PasswordHash = "x" });
        await db.SaveChangesAsync();

        var (_, _, propuesta) = await Svc(db, Dev(dev)).ProponerAsync(dev, "Reescribir el módulo", null, null, null);

        // Sin motivo, no.
        var (sinMotivo, queja) = await Svc(db, Admin()).RetirarAsync(propuesta!.Id);
        Assert.False(sinMotivo);
        Assert.Contains("por qué", queja);
        Assert.Equal(PoolActivityStatus.PorClasificar, (await LeerAsync(db, propuesta.Id)).Status);

        // Con motivo, sí, y le llega tal cual.
        var (ok, mensaje) = await Svc(db, Admin())
            .RetirarAsync(propuesta.Id, "Ya está planeado para el trimestre que viene.");
        Assert.True(ok, mensaje);

        var g = await LeerAsync(db, propuesta.Id);
        Assert.Equal(PoolActivityStatus.Retirada, g.Status);
        Assert.Contains("Ya está planeado", g.ReviewHistory);

        // EL RECLAMO NO SE SUELTA: es lo que deja escrito de quién era. Sin él, una propuesta
        // descartada sería indistinguible de un work item que nadie quiso.
        Assert.Equal(dev, g.ClaimedByDeveloperId);

        var aviso = Assert.Single(db.Notifications.AsNoTracking().ToList());
        Assert.Contains("Ya está planeado", aviso.Message);
    }

    /// <summary>
    /// Y lo que NADIE propuso se sigue retirando sin motivo: descartar un work item que no interesa
    /// no le quita nada a nadie, y exigir una explicación para hablarle a un nadie sería un trámite.
    /// </summary>
    [Fact]
    public async Task Retirar_LoQueNadiePropuso_NoPideMotivo()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        var delTicket = await DesdeDevOpsAsync(db, "Algo que no interesa", workItem: 700);

        var (ok, mensaje) = await Svc(db, Admin()).RetirarAsync(delTicket.Id);

        Assert.True(ok, mensaje);
        Assert.Equal(PoolActivityStatus.Retirada, (await LeerAsync(db, delTicket.Id)).Status);
        Assert.Empty(db.Notifications);
    }

    /// <summary>
    /// Descartar libera el sitio del tope. Es la salida que impide que una propuesta que el líder no
    /// va a clasificar nunca se quede ocupando uno de los tres huecos para siempre.
    /// </summary>
    [Fact]
    public async Task Descartar_LiberaElSitioDelTope()
    {
        var db = await BaseConPoolAsync();
        using var _ = db;
        int dev = NuevoDesarrollador(db, "Ana");

        for (int i = 0; i < PoolActivityService.MaxTomadasPorOmision; i++)
            await Svc(db, Dev(dev)).ProponerAsync(dev, $"Propuesta {i}", null, null, null);

        var primera = db.PoolActivities.AsNoTracking().OrderBy(a => a.Id).First();
        Assert.True((await Svc(db, Admin()).RetirarAsync(primera.Id, "No aplica.")).ok);

        var (ok, mensaje, _) = await Svc(db, Dev(dev)).ProponerAsync(dev, "Ahora sí cabe", null, null, null);
        Assert.True(ok, mensaje);
    }
}
