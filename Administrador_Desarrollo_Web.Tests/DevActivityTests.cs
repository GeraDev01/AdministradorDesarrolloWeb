using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Actividades libres (trabajo fuera de los requerimientos asignados) y su cronómetro,
/// incluida la migración del esquema de WorkSessions que las hizo posibles.
/// </summary>
public class DevActivityTests
{
    private const int MiDevId = 3;
    private const int OtroDevId = 4;

    private sealed record Entorno(
        AppDbContext Db, DevActivityService Activities, WorkSessionService Work, CurrentUserContext User);

    private static Entorno Nuevo(UserRole rol = UserRole.Desarrollador, int? devId = MiDevId)
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = MiDevId, FullName = "Yo", IsActive = true });
        db.Developers.Add(new Developer { Id = OtroDevId, FullName = "Otro", IsActive = true });
        db.SaveChanges();

        var user = Ctx.As(rol, devId);
        var audit = new AuditService(db, user);
        var work = new WorkSessionService(db, audit, user);
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
    public void CrearActividadLibre()
    {
        var e = Nuevo();
        var (ok, _, a) = e.Activities.Crear(MiDevId, "Soporte a usuario", "Llamada de 40 min");

        Assert.True(ok);
        Assert.Equal(DevActivityStatus.Abierta, a!.Status);
        Assert.Equal(MiDevId, a.DeveloperId);
        Assert.Single(e.Db.DevActivities);
    }

    [Fact]
    public void NoSePuedeCrearSinTitulo()
    {
        var e = Nuevo();
        var (ok, _, _) = e.Activities.Crear(MiDevId, "   ", null);

        Assert.False(ok);
        Assert.Empty(e.Db.DevActivities);
    }

    [Fact]
    public void NoSePuedeCrearActividadANombreDeOtro()
    {
        var e = Nuevo();
        Assert.Throws<AuthorizationException>(() => e.Activities.Crear(OtroDevId, "Ajena", null));
        Assert.Empty(e.Db.DevActivities);
    }

    [Fact]
    public void SoloSeVenLasActividadesPropias()
    {
        var e = Nuevo();
        e.Activities.Crear(MiDevId, "Mía", null);
        e.Db.DevActivities.Add(new DevActivity { DeveloperId = OtroDevId, Title = "Ajena", CreatedAt = DateTime.UtcNow });
        e.Db.SaveChanges();

        var mias = e.Activities.DeDesarrollador(MiDevId);

        Assert.Single(mias);
        Assert.Equal("Mía", mias[0].Title);
    }

    // ── Cronómetro ──────────────────────────────────────────────────────────────

    [Fact]
    public void CronometrarUnaActividadAcumulaTiempo()
    {
        var e = Nuevo();
        var (_, _, a) = e.Activities.Crear(MiDevId, "Junta de planeación", null);
        var objetivo = WorkTarget.Actividad(a!.Id);

        var s = e.Work.StartOrResume(MiDevId, objetivo);
        Assert.Equal(WorkSessionStatus.Activa, s.Status);
        Assert.Null(s.RequirementId);
        Assert.Equal(a.Id, s.ActivityId);

        // Se simula tiempo transcurrido moviendo el inicio del tramo hacia atrás.
        s.LastResumedAt = DateTime.UtcNow.AddSeconds(-90);
        e.Db.SaveChanges();

        e.Work.Stop(MiDevId, objetivo);

        Assert.InRange(e.Work.GetTotalSecondsByActivity(a.Id), 85, 100);
        Assert.Equal(WorkSessionStatus.Detenida, e.Db.WorkSessions.Single().Status);
    }

    [Fact]
    public void IniciarUnaActividadPausaElCronometroDeUnRequerimiento()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);
        var (_, _, a) = e.Activities.Crear(MiDevId, "Interrupción: soporte", null);

        e.Work.StartOrResume(MiDevId, reqId);
        Assert.Equal(WorkSessionStatus.Activa, e.Work.GetOpenSession(MiDevId, reqId)!.Status);

        e.Work.StartOrResume(MiDevId, WorkTarget.Actividad(a!.Id));

        // El requerimiento queda pausado: el tiempo no se cuenta dos veces.
        Assert.Equal(WorkSessionStatus.Pausada, e.Work.GetOpenSession(MiDevId, reqId)!.Status);
        Assert.Equal(WorkSessionStatus.Activa, e.Work.GetOpenSession(MiDevId, WorkTarget.Actividad(a.Id))!.Status);
        Assert.Single(e.Db.WorkSessions.Where(w => w.Status == WorkSessionStatus.Activa));
    }

    [Fact]
    public void IniciarUnRequerimientoPausaElCronometroDeUnaActividad()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);
        var (_, _, a) = e.Activities.Crear(MiDevId, "Investigación", null);

        e.Work.StartOrResume(MiDevId, WorkTarget.Actividad(a!.Id));
        e.Work.StartOrResume(MiDevId, reqId);

        Assert.Equal(WorkSessionStatus.Pausada, e.Work.GetOpenSession(MiDevId, WorkTarget.Actividad(a.Id))!.Status);
        Assert.Single(e.Db.WorkSessions.Where(w => w.Status == WorkSessionStatus.Activa));
    }

    [Fact]
    public void ElTiempoDeActividadesCuentaEnElTotalDelDesarrollador()
    {
        var e = Nuevo();
        var (_, _, a) = e.Activities.Crear(MiDevId, "Apoyo a otro equipo", null);
        var objetivo = WorkTarget.Actividad(a!.Id);

        var s = e.Work.StartOrResume(MiDevId, objetivo);
        s.LastResumedAt = DateTime.UtcNow.AddSeconds(-120);
        e.Db.SaveChanges();
        e.Work.Stop(MiDevId, objetivo);

        Assert.InRange(e.Work.GetTotalSecondsByDeveloper(MiDevId), 115, 130);
    }

    [Fact]
    public void CerrarLaActividadDetieneElCronometroYConservaElTiempo()
    {
        var e = Nuevo();
        var (_, _, a) = e.Activities.Crear(MiDevId, "Capacitación", null);
        var s = e.Work.StartOrResume(MiDevId, WorkTarget.Actividad(a!.Id));
        s.LastResumedAt = DateTime.UtcNow.AddSeconds(-60);
        e.Db.SaveChanges();

        var (ok, _) = e.Activities.Cerrar(a.Id);

        Assert.True(ok);
        Assert.Equal(DevActivityStatus.Cerrada, e.Db.DevActivities.Find(a.Id)!.Status);
        Assert.Equal(WorkSessionStatus.Detenida, e.Db.WorkSessions.Single().Status);
        Assert.InRange(e.Work.GetTotalSecondsByActivity(a.Id), 55, 70);
    }

    // ── Eliminación ─────────────────────────────────────────────────────────────

    [Fact]
    public void SePuedeEliminarUnaActividadSinTiempoRegistrado()
    {
        var e = Nuevo();
        var (_, _, a) = e.Activities.Crear(MiDevId, "Creada por error", null);

        var (ok, _) = e.Activities.Eliminar(a!.Id);

        Assert.True(ok);
        Assert.Empty(e.Db.DevActivities);
    }

    [Fact]
    public void NoSePuedeEliminarUnaActividadConTiempoRegistrado()
    {
        var e = Nuevo();
        var (_, _, a) = e.Activities.Crear(MiDevId, "Trabajo real", null);
        var objetivo = WorkTarget.Actividad(a!.Id);
        var s = e.Work.StartOrResume(MiDevId, objetivo);
        s.LastResumedAt = DateTime.UtcNow.AddSeconds(-30);
        e.Db.SaveChanges();
        e.Work.Stop(MiDevId, objetivo);

        var (ok, mensaje) = e.Activities.Eliminar(a.Id);

        Assert.False(ok);
        Assert.NotNull(e.Db.DevActivities.Find(a.Id));
        Assert.Contains("no se puede eliminar", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoSePuedeTocarLaActividadDeOtroDesarrollador()
    {
        var e = Nuevo();
        var ajena = new DevActivity { DeveloperId = OtroDevId, Title = "Ajena", CreatedAt = DateTime.UtcNow };
        e.Db.DevActivities.Add(ajena);
        e.Db.SaveChanges();

        Assert.Throws<AuthorizationException>(() => e.Activities.Cerrar(ajena.Id));
        Assert.Throws<AuthorizationException>(() => e.Activities.Eliminar(ajena.Id));
    }

    // ── Validación del objetivo ─────────────────────────────────────────────────

    [Fact]
    public void UnaSesionNoPuedeApuntarADosObjetivosNiANinguno()
    {
        var e = Nuevo();
        Assert.Throws<ArgumentException>(() => e.Work.StartOrResume(MiDevId, new WorkTarget(1, 1)));
        Assert.Throws<ArgumentException>(() => e.Work.StartOrResume(MiDevId, new WorkTarget(null, null)));
    }

    // ── Migración del esquema ───────────────────────────────────────────────────

    [Fact]
    public void LaMigracionDeWorkSessionsConservaLasSesionesExistentes()
    {
        // Se parte de una BD ya migrada y se le REGRESA la forma antigua de WorkSessions
        // (RequirementId NOT NULL, sin ActivityId) para ejercitar el rebuild de verdad.
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = MiDevId, FullName = "Yo", IsActive = true });
        db.SaveChanges();
        int reqId = NuevoRequerimiento(db);

        db.Database.ExecuteSqlRaw(@"DROP TABLE ""WorkSessions"";");
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE ""WorkSessions"" (
                ""Id""                 INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                ""RequirementId""      INTEGER NOT NULL,
                ""DeveloperId""        INTEGER NOT NULL,
                ""StartedAt""          TEXT    NOT NULL,
                ""EndedAt""            TEXT,
                ""AccumulatedSeconds"" INTEGER NOT NULL DEFAULT 0,
                ""LastResumedAt""      TEXT,
                ""Status""             INTEGER NOT NULL DEFAULT 0,
                ""Note""               TEXT,
                ""CreatedAt""          TEXT    NOT NULL
            );");
        db.Database.ExecuteSqlRaw($@"
            INSERT INTO ""WorkSessions""
                (""RequirementId"",""DeveloperId"",""StartedAt"",""AccumulatedSeconds"",""Status"",""Note"",""CreatedAt"")
            VALUES ({reqId}, {MiDevId}, '2026-07-01 10:00:00', 3600, 2, 'sesión histórica', '2026-07-01 10:00:00');");

        // Acto: la migración debe reconstruir la tabla sin perder nada.
        DatabaseMigrator.EnsureUpToDate(db);

        var sesiones = db.WorkSessions.AsNoTracking().ToList();
        Assert.Single(sesiones);
        Assert.Equal(3600, sesiones[0].AccumulatedSeconds);
        Assert.Equal(reqId, sesiones[0].RequirementId);
        Assert.Equal("sesión histórica", sesiones[0].Note);
        Assert.Null(sesiones[0].ActivityId);

        // Y ahora RequirementId sí admite NULL: se puede cronometrar una actividad.
        var user = Ctx.As(UserRole.Desarrollador, MiDevId);
        var audit = new AuditService(db, user);
        var work = new WorkSessionService(db, audit, user);
        var actividad = new DevActivity { DeveloperId = MiDevId, Title = "Post-migración", CreatedAt = DateTime.UtcNow };
        db.DevActivities.Add(actividad);
        db.SaveChanges();

        var s = work.StartOrResume(MiDevId, WorkTarget.Actividad(actividad.Id));
        Assert.Null(s.RequirementId);
        Assert.Equal(2, db.WorkSessions.Count());
    }

    [Fact]
    public void LaMigracionEsIdempotente()
    {
        var db = TestDb.New();
        DatabaseMigrator.EnsureUpToDate(db);
        DatabaseMigrator.EnsureUpToDate(db);   // no debe lanzar ni duplicar nada
        Assert.Empty(db.WorkSessions);
    }
}
