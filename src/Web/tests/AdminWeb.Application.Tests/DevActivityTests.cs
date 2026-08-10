using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Actividades libres (trabajo fuera de los requerimientos asignados) y su cronómetro.
///
/// Las pruebas de la MIGRACIÓN del esquema de WorkSessions que acompañaban a estas en el escritorio
/// no se portan aquí: el andamiaje web crea el esquema con el modelo de EF, y el migrador —que es
/// quien reconstruye la tabla vieja— se porta con sus propias pruebas en su propio paso.
/// </summary>
public class DevActivityTests
{
    private const int MiDevId = 3;
    private const int OtroDevId = 4;

    private sealed record Entorno(
        AppDbContext Db, DevActivityService Activities, WorkSessionService Work, ICurrentUser User);

    private static Entorno Nuevo(UserRole rol = UserRole.Desarrollador, int? devId = MiDevId)
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = MiDevId, FullName = "Yo", IsActive = true });
        db.Developers.Add(new Developer { Id = OtroDevId, FullName = "Otro", IsActive = true });
        db.SaveChanges();

        var user = UsuarioDePrueba.Como(rol, devId);
        var audit = new AuditService(db, user, new OrigenDePrueba());
        var work = new WorkSessionService(db, user, audit);
        return new Entorno(db, new DevActivityService(db, user, audit, work), work, user);
    }

    private static int NuevoRequerimiento(AppDbContext db, string titulo = "Req de prueba")
    {
        var r = new Requirement { Title = titulo, Status = RequirementStatus.EnDesarrollo, CreatedAt = DateTime.UtcNow };
        db.Requirements.Add(r);
        db.Assignments.Add(new Assignment { Requirement = r, DeveloperId = MiDevId });
        db.SaveChanges();
        return r.Id;
    }

    // ── Alta y reglas ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CrearActividadLibre()
    {
        var e = Nuevo();
        var (ok, _, a) = await e.Activities.CrearAsync(MiDevId, "Soporte a usuario", "Llamada de 40 min");

        Assert.True(ok);
        Assert.Equal(DevActivityStatus.Abierta, a!.Status);
        Assert.Equal(MiDevId, a.DeveloperId);
        Assert.Single(e.Db.DevActivities);
    }

    [Fact]
    public async Task NoSePuedeCrearSinTitulo()
    {
        var e = Nuevo();
        var (ok, _, _) = await e.Activities.CrearAsync(MiDevId, "   ", null);

        Assert.False(ok);
        Assert.Empty(e.Db.DevActivities);
    }

    [Fact]
    public async Task NoSePuedeCrearActividadANombreDeOtro()
    {
        var e = Nuevo();
        await Assert.ThrowsAsync<AuthorizationException>(() => e.Activities.CrearAsync(OtroDevId, "Ajena", null));
        Assert.Empty(e.Db.DevActivities);
    }

    [Fact]
    public async Task SoloSeVenLasActividadesPropias()
    {
        var e = Nuevo();
        await e.Activities.CrearAsync(MiDevId, "Mía", null);
        e.Db.DevActivities.Add(new DevActivity { DeveloperId = OtroDevId, Title = "Ajena", CreatedAt = DateTime.UtcNow });
        e.Db.SaveChanges();

        var mias = await e.Activities.DeDesarrolladorAsync(MiDevId);

        Assert.Single(mias);
        Assert.Equal("Mía", mias[0].Title);
    }

    // ── Cronómetro ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CronometrarUnaActividadAcumulaTiempo()
    {
        var e = Nuevo();
        var (_, _, a) = await e.Activities.CrearAsync(MiDevId, "Junta de planeación", null);
        var objetivo = WorkTarget.Actividad(a!.Id);

        var s = await e.Work.StartOrResumeAsync(MiDevId, objetivo);
        Assert.Equal(WorkSessionStatus.Activa, s.Status);
        Assert.Null(s.RequirementId);
        Assert.Equal(a.Id, s.ActivityId);

        // Se simula tiempo transcurrido moviendo el inicio del tramo hacia atrás.
        s.LastResumedAt = DateTime.UtcNow.AddSeconds(-90);
        await e.Db.SaveChangesAsync();

        await e.Work.StopAsync(MiDevId, objetivo);

        Assert.InRange(await e.Work.GetTotalSecondsByActivityAsync(a.Id), 85, 100);
        Assert.Equal(WorkSessionStatus.Detenida, e.Db.WorkSessions.Single().Status);
    }

    [Fact]
    public async Task IniciarUnaActividadPausaElCronometroDeUnRequerimiento()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);
        var (_, _, a) = await e.Activities.CrearAsync(MiDevId, "Interrupción: soporte", null);

        await e.Work.StartOrResumeAsync(MiDevId, reqId);
        Assert.Equal(WorkSessionStatus.Activa, (await e.Work.GetOpenSessionAsync(MiDevId, reqId))!.Status);

        await e.Work.StartOrResumeAsync(MiDevId, WorkTarget.Actividad(a!.Id));

        // El requerimiento queda pausado: el tiempo no se cuenta dos veces.
        Assert.Equal(WorkSessionStatus.Pausada, (await e.Work.GetOpenSessionAsync(MiDevId, reqId))!.Status);
        Assert.Equal(WorkSessionStatus.Activa, (await e.Work.GetOpenSessionAsync(MiDevId, WorkTarget.Actividad(a.Id)))!.Status);
        Assert.Single(e.Db.WorkSessions.Where(w => w.Status == WorkSessionStatus.Activa));
    }

    [Fact]
    public async Task IniciarUnRequerimientoPausaElCronometroDeUnaActividad()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);
        var (_, _, a) = await e.Activities.CrearAsync(MiDevId, "Investigación", null);

        await e.Work.StartOrResumeAsync(MiDevId, WorkTarget.Actividad(a!.Id));
        await e.Work.StartOrResumeAsync(MiDevId, reqId);

        Assert.Equal(WorkSessionStatus.Pausada, (await e.Work.GetOpenSessionAsync(MiDevId, WorkTarget.Actividad(a.Id)))!.Status);
        Assert.Single(e.Db.WorkSessions.Where(w => w.Status == WorkSessionStatus.Activa));
    }

    [Fact]
    public async Task ElTiempoDeActividadesCuentaEnElTotalDelDesarrollador()
    {
        var e = Nuevo();
        var (_, _, a) = await e.Activities.CrearAsync(MiDevId, "Apoyo a otro equipo", null);
        var objetivo = WorkTarget.Actividad(a!.Id);

        var s = await e.Work.StartOrResumeAsync(MiDevId, objetivo);
        s.LastResumedAt = DateTime.UtcNow.AddSeconds(-120);
        await e.Db.SaveChangesAsync();
        await e.Work.StopAsync(MiDevId, objetivo);

        Assert.InRange(await e.Work.GetTotalSecondsByDeveloperAsync(MiDevId), 115, 130);
    }

    [Fact]
    public async Task CerrarLaActividadDetieneElCronometroYConservaElTiempo()
    {
        var e = Nuevo();
        var (_, _, a) = await e.Activities.CrearAsync(MiDevId, "Capacitación", null);
        var s = await e.Work.StartOrResumeAsync(MiDevId, WorkTarget.Actividad(a!.Id));
        s.LastResumedAt = DateTime.UtcNow.AddSeconds(-60);
        await e.Db.SaveChangesAsync();

        var (ok, _) = await e.Activities.CerrarAsync(a.Id);

        Assert.True(ok);
        Assert.Equal(DevActivityStatus.Cerrada, e.Db.DevActivities.Find(a.Id)!.Status);
        Assert.Equal(WorkSessionStatus.Detenida, e.Db.WorkSessions.Single().Status);
        Assert.InRange(await e.Work.GetTotalSecondsByActivityAsync(a.Id), 55, 70);
    }

    // ── Eliminación ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SePuedeEliminarUnaActividadSinTiempoRegistrado()
    {
        var e = Nuevo();
        var (_, _, a) = await e.Activities.CrearAsync(MiDevId, "Creada por error", null);

        var (ok, _) = await e.Activities.EliminarAsync(a!.Id);

        Assert.True(ok);
        Assert.Empty(e.Db.DevActivities);
    }

    [Fact]
    public async Task NoSePuedeEliminarUnaActividadConTiempoRegistrado()
    {
        var e = Nuevo();
        var (_, _, a) = await e.Activities.CrearAsync(MiDevId, "Trabajo real", null);
        var objetivo = WorkTarget.Actividad(a!.Id);
        var s = await e.Work.StartOrResumeAsync(MiDevId, objetivo);
        s.LastResumedAt = DateTime.UtcNow.AddSeconds(-30);
        await e.Db.SaveChangesAsync();
        await e.Work.StopAsync(MiDevId, objetivo);

        var (ok, mensaje) = await e.Activities.EliminarAsync(a.Id);

        Assert.False(ok);
        Assert.NotNull(e.Db.DevActivities.Find(a.Id));
        Assert.Contains("no se puede eliminar", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoSePuedeTocarLaActividadDeOtroDesarrollador()
    {
        var e = Nuevo();
        var ajena = new DevActivity { DeveloperId = OtroDevId, Title = "Ajena", CreatedAt = DateTime.UtcNow };
        e.Db.DevActivities.Add(ajena);
        e.Db.SaveChanges();

        await Assert.ThrowsAsync<AuthorizationException>(() => e.Activities.CerrarAsync(ajena.Id));
        await Assert.ThrowsAsync<AuthorizationException>(() => e.Activities.EliminarAsync(ajena.Id));
    }

    // ── Validación del objetivo ─────────────────────────────────────────────────

    [Fact]
    public async Task UnaSesionNoPuedeApuntarADosObjetivosNiANinguno()
    {
        var e = Nuevo();
        await Assert.ThrowsAsync<ArgumentException>(() => e.Work.StartOrResumeAsync(MiDevId, new WorkTarget(1, 1)));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Work.StartOrResumeAsync(MiDevId, new WorkTarget(null, null)));
    }
}

/// <summary>
/// Evidencia de las actividades libres.
///
/// Hay dos cosas que cuidar: que el listado NO arrastre los archivos (la base es compartida y se lee
/// por red, así que traer los BLOB en cada refresco lo paga todo el equipo) y que no se pueda colar
/// un ejecutable — la evidencia acaba abierta con el programa asociado en la máquina de quien la
/// descarga.
/// </summary>
public class DevActivityEvidenceTests
{
    private static readonly byte[] PngFalso = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static DevActivityService Svc(AppDbContext db, ICurrentUser cu)
    {
        var audit = new AuditService(db, cu, new OrigenDePrueba());
        return new DevActivityService(db, cu, audit, new WorkSessionService(db, cu, audit));
    }

    private static async Task<(AppDbContext db, Developer dev, DevActivity act, ICurrentUser yo, ICurrentUser jefa)> EntornoAsync()
    {
        var db = TestDb.New();
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();

        var yo = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id, userId: 10);
        var (_, _, act) = await Svc(db, yo).CrearAsync(dev.Id, "Apoyo a soporte", "Ayudé con la migración del cliente X.");

        return (db, dev, act!, yo, UsuarioDePrueba.Como(UserRole.Admin, developerId: null, userId: 900));
    }

    // ── Adjuntar ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Agregar_GuardaLaEvidenciaYDetectaElTipoPorLosBytes()
    {
        var (db, _, act, yo, _) = await EntornoAsync();

        var (ok, _, ev) = await Svc(db, yo).AgregarEvidenciaAsync(act.Id, "captura.png", PngFalso, "Pantalla del error");

        Assert.True(ok);
        Assert.NotNull(ev);
        var g = db.DevActivityAttachments.AsNoTracking().Single();
        Assert.Equal("image/png", g.ContentType);
        Assert.Equal(PngFalso.Length, g.SizeBytes);
        Assert.Equal("Pantalla del error", g.Description);
        Assert.True(g.EsImagen);
    }

    /// <summary>
    /// El tipo se decide por el CONTENIDO, no por el nombre: el nombre lo pone quien sube el
    /// archivo y llamarle «.pdf» a un PNG no lo convierte en PDF.
    /// </summary>
    [Fact]
    public async Task Agregar_ElNombreNoDecideElTipo()
    {
        var (db, _, act, yo, _) = await EntornoAsync();
        await Svc(db, yo).AgregarEvidenciaAsync(act.Id, "documento.pdf", PngFalso);

        Assert.Equal("image/png", db.DevActivityAttachments.AsNoTracking().Single().ContentType);
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("script.ps1")]
    [InlineData("atajo.lnk")]
    [InlineData("macro.VBS")]
    public async Task Agregar_RechazaEjecutables(string nombre)
    {
        var (db, _, act, yo, _) = await EntornoAsync();

        var (ok, mensaje, _) = await Svc(db, yo).AgregarEvidenciaAsync(act.Id, nombre, [1, 2, 3]);

        Assert.False(ok);
        Assert.Contains("No se admiten", mensaje);
        Assert.Empty(db.DevActivityAttachments);
    }

    [Fact]
    public async Task Agregar_RechazaLoVacioYLoDemasiadoGrande()
    {
        var (db, _, act, yo, _) = await EntornoAsync();
        var svc = Svc(db, yo);

        Assert.False((await svc.AgregarEvidenciaAsync(act.Id, "vacio.png", [])).ok);
        Assert.False((await svc.AgregarEvidenciaAsync(act.Id, "enorme.png", new byte[DevActivityService.MaxEvidenciaBytes + 1])).ok);
        Assert.Empty(db.DevActivityAttachments);
    }

    [Fact]
    public async Task Agregar_TopeDeArchivosPorActividad()
    {
        var (db, _, act, yo, _) = await EntornoAsync();
        var svc = Svc(db, yo);

        for (int i = 0; i < DevActivityService.MaxEvidenciasPorActividad; i++)
            Assert.True((await svc.AgregarEvidenciaAsync(act.Id, $"captura{i}.png", PngFalso)).ok);

        var (ok, mensaje, _) = await svc.AgregarEvidenciaAsync(act.Id, "una-mas.png", PngFalso);
        Assert.False(ok);
        Assert.Contains("máximo", mensaje);
        Assert.Equal(DevActivityService.MaxEvidenciasPorActividad, db.DevActivityAttachments.Count());
    }

    /// <summary>Cerrada es evidencia consolidada: mismo criterio que Renombrar.</summary>
    [Fact]
    public async Task Agregar_SeNiegaSiLaActividadEstaCerrada()
    {
        var (db, _, act, yo, _) = await EntornoAsync();
        await Svc(db, yo).CerrarAsync(act.Id);

        var (ok, mensaje, _) = await Svc(db, yo).AgregarEvidenciaAsync(act.Id, "tarde.png", PngFalso);

        Assert.False(ok);
        Assert.Contains("Reábrela", mensaje);
        Assert.Empty(db.DevActivityAttachments);
    }

    [Fact]
    public async Task Agregar_NoSeLeCuelgaEvidenciaAUnaActividadAjena()
    {
        var (db, _, act, _, _) = await EntornoAsync();
        var intruso = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 999, userId: 77);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, intruso).AgregarEvidenciaAsync(act.Id, "x.png", PngFalso));
    }

    // ── Listar y leer ────────────────────────────────────────────────────────

    /// <summary>
    /// El listado NO trae los bytes: es lo que evita mover megas por la red en cada refresco de la
    /// rejilla. El contenido solo viaja por BytesDeEvidenciaAsync.
    /// </summary>
    [Fact]
    public async Task EvidenciasDe_NoArrastraElContenido()
    {
        var (db, _, act, yo, _) = await EntornoAsync();
        await Svc(db, yo).AgregarEvidenciaAsync(act.Id, "captura.png", PngFalso);

        var lista = await Svc(db, yo).EvidenciasDeAsync(act.Id);

        Assert.Single(lista);
        Assert.Empty(lista[0].Bytes);
        Assert.Equal(PngFalso.Length, lista[0].SizeBytes);   // el tamaño sí, para poder mostrarlo

        var (bytes, nombre, tipo) = await Svc(db, yo).BytesDeEvidenciaAsync(lista[0].Id);
        Assert.Equal(PngFalso, bytes);
        Assert.Equal("captura.png", nombre);
        Assert.Equal("image/png", tipo);
    }

    [Fact]
    public async Task ElAdministradorPuedeVerLaEvidenciaDeCualquiera()
    {
        var (db, _, act, yo, jefa) = await EntornoAsync();
        await Svc(db, yo).AgregarEvidenciaAsync(act.Id, "captura.png", PngFalso);

        var lista = await Svc(db, jefa).EvidenciasDeAsync(act.Id);
        Assert.Single(lista);
        Assert.Equal(PngFalso, (await Svc(db, jefa).BytesDeEvidenciaAsync(lista[0].Id)).bytes);
    }

    [Fact]
    public async Task OtroDesarrolladorNoVeLaEvidenciaAjena()
    {
        var (db, _, act, yo, _) = await EntornoAsync();
        await Svc(db, yo).AgregarEvidenciaAsync(act.Id, "captura.png", PngFalso);
        var intruso = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 999, userId: 77);

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, intruso).EvidenciasDeAsync(act.Id));
    }

    [Fact]
    public async Task ConteoEvidencias_AgrupaSinTraerArchivos()
    {
        var (db, dev, act, yo, _) = await EntornoAsync();
        var svc = Svc(db, yo);
        await svc.AgregarEvidenciaAsync(act.Id, "a.png", PngFalso);
        await svc.AgregarEvidenciaAsync(act.Id, "b.png", PngFalso);

        var (_, _, otra) = await svc.CrearAsync(dev.Id, "Otra actividad", null);

        var conteo = await svc.ConteoEvidenciasAsync([act.Id, otra!.Id]);

        Assert.Equal(2, conteo[act.Id]);
        Assert.False(conteo.ContainsKey(otra.Id));   // sin evidencia no aparece
        Assert.Empty(await svc.ConteoEvidenciasAsync([]));
    }

    // ── Quitar ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Eliminar_QuitaLaEvidencia()
    {
        var (db, _, act, yo, _) = await EntornoAsync();
        var (_, _, ev) = await Svc(db, yo).AgregarEvidenciaAsync(act.Id, "captura.png", PngFalso);

        Assert.True((await Svc(db, yo).EliminarEvidenciaAsync(ev!.Id)).ok);
        Assert.Empty(db.DevActivityAttachments);
    }

    [Fact]
    public async Task Eliminar_SeNiegaSiLaActividadEstaCerrada()
    {
        var (db, _, act, yo, _) = await EntornoAsync();
        var (_, _, ev) = await Svc(db, yo).AgregarEvidenciaAsync(act.Id, "captura.png", PngFalso);
        await Svc(db, yo).CerrarAsync(act.Id);

        var (ok, _) = await Svc(db, yo).EliminarEvidenciaAsync(ev!.Id);

        Assert.False(ok);
        Assert.Single(db.DevActivityAttachments);
    }

    /// <summary>Borrar la actividad (solo si no tiene tiempo) se lleva su evidencia por delante.</summary>
    [Fact]
    public async Task EliminarLaActividad_ArrastraSuEvidencia()
    {
        var (db, _, act, yo, _) = await EntornoAsync();
        await Svc(db, yo).AgregarEvidenciaAsync(act.Id, "captura.png", PngFalso);

        Assert.True((await Svc(db, yo).EliminarAsync(act.Id)).ok);
        Assert.Empty(db.DevActivityAttachments);
    }

    [Fact]
    public void NombreSeguro_LimpiaRutasYCaracteresProhibidos()
    {
        Assert.Equal("captura.png", DevActivityService.NombreSeguro(@"C:\Users\ana\captura.png"));
        Assert.Equal("evidencia", DevActivityService.NombreSeguro("   "));
        Assert.DoesNotContain("..", DevActivityService.NombreSeguro(@"..\..\etc\passwd"));
    }
}
