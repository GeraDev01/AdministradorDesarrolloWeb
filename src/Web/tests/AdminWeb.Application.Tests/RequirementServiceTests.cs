using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Requerimientos. Es lógica NUEVA como servicio: en el escritorio vivía dentro de
/// <c>RequirementsControl</c>, mezclada con el código que armaba la rejilla, y por eso no tenía
/// pruebas. Al sacarla aquí, lo que hay que dejar amarrado es lo que aquel control decidía y nadie
/// vigilaba:
///
///  · <b>Cancelar no borra.</b> Es la decisión más fácil de perder en un port —el botón se llamaba
///    «Eliminar»— y la más cara: un requerimiento borrado se lleva su tiempo cronometrado, sus
///    asignaciones y su historia en el sprint.
///  · <b>Al asignar se avisa solo a quien no estaba antes.</b> Sin la resta, cada retoque de la
///    lista repetiría el aviso a todo el equipo y los avisos dejarían de leerse.
///  · <b>La pantalla es del líder</b>, y la guarda del servicio es la barrera que sigue en pie
///    cuando alguien llama a la API sin pasar por el navegador.
/// </summary>
public class RequirementServiceTests
{
    private static RequirementService Svc(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, new AuditService(db, cu, new OrigenDePrueba()), new NotificationService(db));

    private static RequirementService Admin(AppDbContext db) =>
        Svc(db, UsuarioDePrueba.Como(UserRole.Admin, userId: 99));

    private static RequirementService.DatosDeRequerimiento Datos(
        string titulo = "Pantalla de pagos",
        RequirementStatus estado = RequirementStatus.PorEstimar,
        RequirementPriority prioridad = RequirementPriority.Media,
        decimal? horas = null,
        DateTime? solicitud = null,
        DateTime? compromiso = null,
        DateTime? entrega = null,
        int avance = 0,
        string? detalle = null) =>
        new(titulo, detalle, estado, prioridad, horas, solicitud, compromiso, entrega, avance);

    /// <summary>Un desarrollador activo con su cuenta, que es lo que hace falta para que el aviso llegue.</summary>
    private static Developer DevConCuenta(AppDbContext db, string nombre, bool activo = true)
    {
        var dev = new Developer { FullName = nombre, IsActive = activo };
        db.Developers.Add(dev);
        db.SaveChanges();
        db.Users.Add(new User
        {
            Username = nombre.ToLowerInvariant(), PasswordHash = "x", FullName = nombre,
            Role = UserRole.Desarrollador, DeveloperId = dev.Id, IsActive = true
        });
        db.SaveChanges();
        return dev;
    }

    // ── Permisos ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElDesarrollador_NoToca_LosRequerimientos()
    {
        // La pantalla es del líder: él compromete el alcance y él responde por él. Lo que el
        // desarrollador ve de lo suyo va por «Mis asignaciones», con su propia guarda.
        var db = TestDb.New();
        var (_, _, req) = await Admin(db).CrearAsync(Datos());
        int reqId = req!.Id;
        var dev = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 3));

        await Assert.ThrowsAsync<AuthorizationException>(() => dev.ListarAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => dev.DesarrolladoresAsignablesAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => dev.CrearAsync(Datos()));
        await Assert.ThrowsAsync<AuthorizationException>(() => dev.ActualizarAsync(reqId, Datos(), null));
        await Assert.ThrowsAsync<AuthorizationException>(() => dev.CancelarAsync(reqId, null));
        await Assert.ThrowsAsync<AuthorizationException>(() => dev.AsignarAsync(reqId, [1]));
    }

    [Fact]
    public async Task Operaciones_TampocoEntra()
    {
        // Comprobación positiva por rol: cerrar la puerta al desarrollador no puede dejarla abierta
        // para Operaciones, cuyo alcance son los despliegues.
        var db = TestDb.New();
        var ops = Svc(db, UsuarioDePrueba.Como(UserRole.Operaciones, userId: 4));

        await Assert.ThrowsAsync<AuthorizationException>(() => ops.ListarAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.CrearAsync(Datos()));
    }

    // ── Alta y validación ────────────────────────────────────────────────────────

    [Fact]
    public async Task Crear_NormalizaTitulo_FechasADiaYAvanceAcotado()
    {
        var db = TestDb.New();
        var (ok, _, req) = await Admin(db).CrearAsync(Datos(
            titulo: "  Pantalla de pagos  ",
            solicitud: new DateTime(2026, 8, 3, 14, 30, 0),
            compromiso: new DateTime(2026, 8, 14, 9, 0, 0),
            avance: 150));

        Assert.True(ok);
        Assert.Equal("Pantalla de pagos", req!.Title);
        Assert.Equal(new DateTime(2026, 8, 3), req.RequestDate);     // la hora se descarta: son DÍAS
        Assert.Equal(new DateTime(2026, 8, 14), req.CommittedDeliveryDate);
        Assert.Equal(100, req.ProgressPercent);                      // 150 no existe
        Assert.Equal(req.CreatedAt, req.StatusChangedAt);            // nace con su estado sellado
    }

    [Fact]
    public async Task Crear_SinHoras_DejaLaEstimacionEnNulo()
    {
        // Un 0 capturado por omisión no es «cero horas»: es «todavía sin estimar», y guardarlo como
        // 0 haría que los informes de capacidad contaran trabajo que nadie midió.
        var db = TestDb.New();
        var (_, _, req) = await Admin(db).CrearAsync(Datos(horas: 0));

        Assert.Null(req!.EstimateHours);
    }

    [Theory]
    [InlineData("", "título")]
    [InlineData("   ", "título")]
    public async Task Crear_ExigeTitulo(string titulo, string fragmento)
    {
        var db = TestDb.New();
        var (ok, mensaje, req) = await Admin(db).CrearAsync(Datos(titulo: titulo));

        Assert.False(ok);
        Assert.Null(req);
        Assert.Contains(fragmento, mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Requirements.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Crear_RechazaEstimacionesImposibles()
    {
        var db = TestDb.New();
        var admin = Admin(db);

        Assert.False((await admin.CrearAsync(Datos(horas: -1))).ok);
        Assert.False((await admin.CrearAsync(Datos(horas: 5000))).ok);
        Assert.True((await admin.CrearAsync(Datos(horas: 8))).ok);
    }

    [Fact]
    public async Task Crear_RechazaEntregaAnteriorALaSolicitud()
    {
        // No es una regla inventada: una entrega anterior a la solicitud vuelve negativo cualquier
        // cálculo de tiempo de atención, y esos números se reportan.
        var db = TestDb.New();
        var (ok, mensaje, _) = await Admin(db).CrearAsync(Datos(
            solicitud: new DateTime(2026, 8, 10), entrega: new DateTime(2026, 8, 1)));

        Assert.False(ok);
        Assert.Contains("entrega", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    // ── Edición ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Actualizar_SelloDeEstado_SoloCuandoElEstadoCambia()
    {
        // StatusChangedAt es lo que permite saber cuánto lleva un requerimiento parado donde está.
        // Tocarlo en cada guardado pondría ese contador a cero al corregir una falta de ortografía.
        var db = TestDb.New();
        var admin = Admin(db);
        var (_, _, req) = await admin.CrearAsync(Datos(estado: RequirementStatus.Estimado));
        var sello = req!.StatusChangedAt;

        await admin.ActualizarAsync(req.Id, Datos(titulo: "Otro título", estado: RequirementStatus.Estimado), null);
        Assert.Equal(sello, db.Requirements.AsNoTracking().Single().StatusChangedAt);

        await admin.ActualizarAsync(req.Id, Datos(estado: RequirementStatus.EnDesarrollo), null);
        Assert.NotEqual(sello, db.Requirements.AsNoTracking().Single().StatusChangedAt);
    }

    [Fact]
    public async Task Actualizar_DeAlgoQueYaNoExiste_LoDiceEnVezDeReventar()
    {
        var db = TestDb.New();
        var (ok, mensaje) = await Admin(db).ActualizarAsync(9999, Datos(), null);

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje);
    }

    [Fact]
    public async Task Actualizar_ConSelloIlegible_SeRechazaSinGuardar()
    {
        // El sello llega del navegador: puede venir recortado por un enlace mal copiado. Rechazarlo
        // con un mensaje es mejor que dejar que reviente la petición entera al descifrarlo.
        var db = TestDb.New();
        var admin = Admin(db);
        var (_, _, req) = await admin.CrearAsync(Datos(titulo: "Original"));

        var (ok, mensaje) = await admin.ActualizarAsync(req!.Id, Datos(titulo: "Pisado"), "no-es-base64!!");

        Assert.False(ok);
        Assert.Contains("sello", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Original", db.Requirements.AsNoTracking().Single().Title);
    }

    [Fact]
    public async Task Actualizar_ConSelloValido_GuardaAunqueLaBaseNoLleveSello()
    {
        // En SQLite RowVersion está IGNORADO (ver AppDbContext), así que pedirle a EF el valor
        // original de esa propiedad reventaría. El servicio comprueba antes que exista; esta prueba
        // es la que garantiza que ese cuidado sigue ahí, porque el modo de fallo solo se ve fuera de
        // SQL Server.
        var db = TestDb.New();
        var admin = Admin(db);
        var (_, _, req) = await admin.CrearAsync(Datos(titulo: "Original"));

        var (ok, _) = await admin.ActualizarAsync(req!.Id, Datos(titulo: "Corregido"), "AAAAAAAAB9E=");

        Assert.True(ok);
        Assert.Equal("Corregido", db.Requirements.AsNoTracking().Single().Title);
    }

    // ── Cancelar (el «eliminar» del escritorio) ──────────────────────────────────

    [Fact]
    public async Task Cancelar_NoBorra_DejaElRequerimientoEnCancelado()
    {
        // LA prueba de esta clase. El botón del escritorio decía «Eliminar» y solo hacía esto.
        var db = TestDb.New();
        var admin = Admin(db);
        var (_, _, req) = await admin.CrearAsync(Datos(titulo: "Reporte mensual"));

        var (ok, mensaje) = await admin.CancelarAsync(req!.Id, null);

        Assert.True(ok);
        Assert.Contains("cancelado", mensaje, StringComparison.OrdinalIgnoreCase);
        var vivo = Assert.Single(db.Requirements.AsNoTracking().ToList());
        Assert.Equal(RequirementStatus.Cancelado, vivo.Status);
        Assert.Equal("Reporte mensual", vivo.Title);
    }

    [Fact]
    public async Task Cancelar_QuedaEnLaBitacoraComoBaja()
    {
        // Quien lea la bitácora buscando «quién lo eliminó» tiene que encontrarlo, aunque por dentro
        // haya sido un cambio de estado: para quien lo pidió, eso es lo que pasó.
        var db = TestDb.New();
        var admin = Admin(db);
        var (_, _, req) = await admin.CrearAsync(Datos(titulo: "Reporte mensual"));

        await admin.CancelarAsync(req!.Id, null);

        var apunte = db.AuditLogs.AsNoTracking()
            .Single(a => a.Action == AuditAction.Delete && a.EntityType == "Requirement");
        Assert.Equal(req.Id.ToString(), apunte.EntityId);
        Assert.Contains("Cancelado: Reporte mensual", apunte.Details);
    }

    [Fact]
    public async Task Cancelar_DosVeces_LoDiceEnVezDeFingirQueHizoAlgo()
    {
        var db = TestDb.New();
        var admin = Admin(db);
        var (_, _, req) = await admin.CrearAsync(Datos());
        await admin.CancelarAsync(req!.Id, null);

        var (ok, mensaje) = await admin.CancelarAsync(req.Id, null);

        Assert.False(ok);
        Assert.Contains("ya estaba cancelado", mensaje);
    }

    // ── Asignación ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Asignar_DejaExactamenteLosIndicados()
    {
        // Semántica del diálogo de casillas: lo marcado es lo que queda.
        var db = TestDb.New();
        var admin = Admin(db);
        var ana = DevConCuenta(db, "Ana");
        var beto = DevConCuenta(db, "Beto");
        var (_, _, req) = await admin.CrearAsync(Datos());

        await admin.AsignarAsync(req!.Id, [ana.Id, beto.Id]);
        await admin.AsignarAsync(req.Id, [beto.Id]);   // Ana sale

        var asignados = db.Assignments.AsNoTracking().Where(a => a.RequirementId == req.Id)
            .Select(a => a.DeveloperId).ToList();
        Assert.Equal([beto.Id], asignados);
    }

    [Fact]
    public async Task Asignar_ListaVacia_LoDejaSinNadie_YLoDice()
    {
        var db = TestDb.New();
        var admin = Admin(db);
        var ana = DevConCuenta(db, "Ana");
        var (_, _, req) = await admin.CrearAsync(Datos());
        await admin.AsignarAsync(req!.Id, [ana.Id]);

        var (ok, mensaje) = await admin.AsignarAsync(req.Id, []);

        Assert.True(ok);
        Assert.Contains("sin desarrolladores", mensaje);
        Assert.Empty(db.Assignments.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Asignar_IgnoraFichasDeBajaOInexistentes_SinRomperElResto()
    {
        // La lista pudo armarse en un navegador que lleva media hora abierto: que a alguien lo hayan
        // dado de baja mientras tanto no es motivo para no asignar a los demás.
        var db = TestDb.New();
        var admin = Admin(db);
        var ana = DevConCuenta(db, "Ana");
        var baja = DevConCuenta(db, "Ex", activo: false);
        var (_, _, req) = await admin.CrearAsync(Datos());

        var (ok, _) = await admin.AsignarAsync(req!.Id, [ana.Id, baja.Id, 9999]);

        Assert.True(ok);
        Assert.Equal([ana.Id], db.Assignments.AsNoTracking().Select(a => a.DeveloperId).ToList());
    }

    [Fact]
    public async Task Asignar_AvisaSoloAQuienNoEstabaAntes()
    {
        // Sin esta resta, cada guardado repetiría el aviso a todo el equipo del requerimiento y los
        // avisos dejarían de leerse, que es la forma más segura de que el que importaba pase
        // desapercibido.
        var db = TestDb.New();
        var admin = Admin(db);
        var ana = DevConCuenta(db, "Ana");
        var beto = DevConCuenta(db, "Beto");
        var (_, _, req) = await admin.CrearAsync(Datos(titulo: "Pantalla de pagos"));

        await admin.AsignarAsync(req!.Id, [ana.Id]);
        Assert.Single(db.Notifications.AsNoTracking().ToList());

        // Ana sigue; Beto entra. Solo debe haber UN aviso nuevo, el de Beto.
        await admin.AsignarAsync(req.Id, [ana.Id, beto.Id]);

        var avisos = db.Notifications.AsNoTracking().ToList();
        Assert.Equal(2, avisos.Count);
        Assert.All(avisos, a => Assert.Equal(NotificationKind.RequirementAssigned, a.Kind));
        Assert.Contains(avisos, a => a.Message.Contains("Pantalla de pagos"));
    }

    [Fact]
    public async Task Asignar_LoMismoDosVeces_NoVuelveAAvisar()
    {
        var db = TestDb.New();
        var admin = Admin(db);
        var ana = DevConCuenta(db, "Ana");
        var (_, _, req) = await admin.CrearAsync(Datos());

        await admin.AsignarAsync(req!.Id, [ana.Id]);
        await admin.AsignarAsync(req.Id, [ana.Id]);

        Assert.Single(db.Notifications.AsNoTracking().ToList());
    }

    [Fact]
    public async Task Asignar_QuitarYVolverAPoner_SIAvisaOtraVez()
    {
        // La resta es la deduplicación, y por eso NO se usa una clave fija por requerimiento: con
        // ella, a quien le devuelven algo que le habían quitado no se enteraría nunca.
        var db = TestDb.New();
        var admin = Admin(db);
        var ana = DevConCuenta(db, "Ana");
        var (_, _, req) = await admin.CrearAsync(Datos());

        await admin.AsignarAsync(req!.Id, [ana.Id]);
        await admin.AsignarAsync(req.Id, []);
        await admin.AsignarAsync(req.Id, [ana.Id]);

        Assert.Equal(2, db.Notifications.AsNoTracking().Count());
    }

    // ── Filtros de la lista ──────────────────────────────────────────────────────

    [Fact]
    public async Task Listar_FiltraPorEstado_PorDesarrolladorYPorTexto()
    {
        var db = TestDb.New();
        var admin = Admin(db);
        var ana = DevConCuenta(db, "Ana");
        var beto = DevConCuenta(db, "Beto");

        var (_, _, pagos) = await admin.CrearAsync(Datos(
            titulo: "Pantalla de PAGOS", estado: RequirementStatus.EnDesarrollo));
        var (_, _, reporte) = await admin.CrearAsync(Datos(
            titulo: "Reporte mensual", estado: RequirementStatus.Entregado, detalle: "Incluye pagos por sucursal"));
        await admin.CrearAsync(Datos(titulo: "Otra cosa", estado: RequirementStatus.Estimado));

        await admin.AsignarAsync(pagos!.Id, [ana.Id]);
        await admin.AsignarAsync(reporte!.Id, [beto.Id]);

        Assert.Equal([pagos.Id],
            (await admin.ListarAsync(estado: RequirementStatus.EnDesarrollo)).Select(r => r.Id));
        Assert.Equal([reporte.Id],
            (await admin.ListarAsync(developerId: beto.Id)).Select(r => r.Id));

        // El buscador mira título Y descripción, y no distingue mayúsculas: «pagos» encuentra los
        // dos, uno por el título y otro por el detalle.
        var porTexto = await admin.ListarAsync(busqueda: "pagos");
        Assert.Equal(2, porTexto.Count);
        Assert.Contains(porTexto, r => r.Id == pagos.Id);
        Assert.Contains(porTexto, r => r.Id == reporte.Id);
    }

    [Fact]
    public async Task Listar_LosCancelados_SiguenSaliendo()
    {
        // Cancelar no esconde: el requerimiento sigue en la lista con su estado, que es justo lo que
        // permite responder «¿qué pasó con aquello?».
        var db = TestDb.New();
        var admin = Admin(db);
        var (_, _, req) = await admin.CrearAsync(Datos(titulo: "Reporte mensual"));
        await admin.CancelarAsync(req!.Id, null);

        var todos = await admin.ListarAsync();

        Assert.Equal(RequirementStatus.Cancelado, Assert.Single(todos).Status);
    }

    [Fact]
    public async Task DesarrolladoresAsignables_SoloLosActivos_YPorNombre()
    {
        var db = TestDb.New();
        DevConCuenta(db, "Zoe");
        DevConCuenta(db, "Ana");
        DevConCuenta(db, "Ex", activo: false);

        var asignables = await Admin(db).DesarrolladoresAsignablesAsync();

        Assert.Equal(["Ana", "Zoe"], asignables.Select(d => d.FullName));
    }
}
