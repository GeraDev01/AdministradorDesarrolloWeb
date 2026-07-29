using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Compromisos de atención (SLA) con recordatorios para comentar el ticket de Azure DevOps.
/// No se prueba la llamada a DevOps (es red), sí toda la lógica de plazos, permisos y estados.
/// </summary>
public class SlaTests
{
    private const int MiDevId = 11;
    private const int OtroDevId = 12;

    private sealed record Entorno(AppDbContext Db, SlaService Sla, CurrentUserContext User);

    private static Entorno Nuevo(UserRole rol = UserRole.Admin, int? devId = null)
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = MiDevId, FullName = "Yo", IsActive = true, Email = "yo@empresa.com" });
        db.Developers.Add(new Developer { Id = OtroDevId, FullName = "Otro", IsActive = true });
        db.SaveChanges();

        var user = Ctx.As(rol, devId);
        var audit = new AuditService(db, user);
        var settings = new SettingsService(db, audit);
        var devops = new AzureDevOpsService(settings, db, audit, new NotificationService(db));
        return new Entorno(db, new SlaService(db, user, audit, devops), user);
    }

    private static int NuevaActividad(AppDbContext db, int devId = MiDevId)
    {
        var a = new DevActivity { DeveloperId = devId, Title = "Soporte", CreatedAt = DateTime.UtcNow };
        db.DevActivities.Add(a);
        db.SaveChanges();
        return a.Id;
    }

    private static int NuevoRequerimiento(AppDbContext db)
    {
        var r = new Requirement { Title = "Req", Status = RequirementStatus.EnDesarrollo, CreatedAt = DateTime.UtcNow };
        db.Requirements.Add(r);
        db.SaveChanges();
        return r.Id;
    }

    // ── Asignación ──────────────────────────────────────────────────────────────

    [Fact]
    public void ElAdministradorAsignaSlaSobreUnRequerimiento()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);

        var (ok, _, sla) = e.Sla.Asignar(SlaTarget.Requerimiento(reqId), MiDevId,
            DateTime.Now.AddDays(2), 24, devOpsTicketId: 12551, url: null, notas: null);

        Assert.True(ok);
        Assert.Equal(reqId, sla!.RequirementId);
        Assert.Null(sla.ActivityId);
        Assert.Equal(SlaStatus.Activo, sla.Status);
        Assert.Equal(12551, sla.DevOpsTicketExternalId);
    }

    [Fact]
    public void ElAdministradorAsignaSlaSobreUnaActividadLibre()
    {
        var e = Nuevo();
        int actId = NuevaActividad(e.Db);

        var (ok, _, sla) = e.Sla.Asignar(SlaTarget.Actividad(actId), MiDevId,
            DateTime.Now.AddHours(8), 4, null, null, null);

        Assert.True(ok);
        Assert.Equal(actId, sla!.ActivityId);
        Assert.Null(sla.RequirementId);
    }

    [Fact]
    public void UnDesarrolladorNoPuedeAsignarSla()
    {
        var e = Nuevo(UserRole.Desarrollador, MiDevId);
        int reqId = NuevoRequerimiento(e.Db);

        Assert.Throws<AuthorizationException>(() =>
            e.Sla.Asignar(SlaTarget.Requerimiento(reqId), MiDevId, DateTime.Now.AddDays(1), 24, null, null, null));
        Assert.Empty(e.Db.SlaCommitments);
    }

    [Fact]
    public void NoSeAceptaUnaFechaLimiteEnElPasado()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);

        var (ok, mensaje, _) = e.Sla.Asignar(SlaTarget.Requerimiento(reqId), MiDevId,
            DateTime.Now.AddHours(-1), 24, null, null, null);

        Assert.False(ok);
        Assert.Contains("ya pasó", mensaje);
        Assert.Empty(e.Db.SlaCommitments);
    }

    [Fact]
    public void NoSeAceptanDosSlaActivosSobreElMismoObjetivo()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);
        e.Sla.Asignar(SlaTarget.Requerimiento(reqId), MiDevId, DateTime.Now.AddDays(1), 24, null, null, null);

        var (ok, mensaje, _) = e.Sla.Asignar(SlaTarget.Requerimiento(reqId), OtroDevId, DateTime.Now.AddDays(3), 24, null, null, null);

        Assert.False(ok);
        Assert.Contains("ya tiene un SLA activo", mensaje);
        Assert.Single(e.Db.SlaCommitments);
    }

    [Fact]
    public void UnObjetivoInvalidoSeRechaza()
    {
        var e = Nuevo();
        var (ok, _, _) = e.Sla.Asignar(new SlaTarget(1, 1), MiDevId, DateTime.Now.AddDays(1), 24, null, null, null);
        Assert.False(ok);
    }

    // ── Programación del recordatorio ───────────────────────────────────────────

    [Fact]
    public void ElPrimerRecordatorioNuncaSeProgramaDespuesDelVencimiento()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);

        // Vence en 2 horas pero se pidió recordar cada 24: debe recordar al vencer, no nunca.
        var (_, _, sla) = e.Sla.Asignar(SlaTarget.Requerimiento(reqId), MiDevId,
            DateTime.Now.AddHours(2), 24, null, null, null);

        Assert.NotNull(sla!.NextReminderAtUtc);
        Assert.True(sla.NextReminderAtUtc <= sla.DueAtUtc);
    }

    [Fact]
    public void SinCadenciaElRecordatorioEsAlVencer()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);

        var (_, _, sla) = e.Sla.Asignar(SlaTarget.Requerimiento(reqId), MiDevId,
            DateTime.Now.AddDays(3), 0, null, null, null);

        Assert.Equal(sla!.DueAtUtc, sla.NextReminderAtUtc);
    }

    // ── Avisos ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ElDesarrolladorVeSoloSusPendientesQueYaTocan()
    {
        var e = Nuevo();
        int r1 = NuevoRequerimiento(e.Db), r2 = NuevoRequerimiento(e.Db);
        var (_, _, aTiempo) = e.Sla.Asignar(SlaTarget.Requerimiento(r1), MiDevId, DateTime.Now.AddDays(5), 24, null, null, null);
        var (_, _, urgente) = e.Sla.Asignar(SlaTarget.Requerimiento(r2), MiDevId, DateTime.Now.AddDays(5), 24, null, null, null);

        // Se fuerza que a uno ya le toque recordatorio.
        e.Db.SlaCommitments.Find(urgente!.Id)!.NextReminderAtUtc = DateTime.UtcNow.AddMinutes(-5);
        e.Db.SaveChanges();

        var pendientes = e.Sla.PendientesDeAviso(MiDevId);

        Assert.Single(pendientes);
        Assert.Equal(urgente.Id, pendientes[0].Id);
        Assert.DoesNotContain(pendientes, p => p.Id == aTiempo!.Id);
    }

    [Fact]
    public void UnDesarrolladorNoVeLosSlaDeOtro()
    {
        var e = Nuevo(UserRole.Desarrollador, MiDevId);
        Assert.Throws<AuthorizationException>(() => e.Sla.DeDesarrollador(OtroDevId));
        Assert.Throws<AuthorizationException>(() => e.Sla.PendientesDeAviso(OtroDevId));
    }

    // ── Vencimiento ─────────────────────────────────────────────────────────────

    [Fact]
    public void RevisarVencimientosMarcaYDevuelveLoNoNotificado()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);
        var (_, _, sla) = e.Sla.Asignar(SlaTarget.Requerimiento(reqId), MiDevId, DateTime.Now.AddHours(1), 24, null, null, null);

        // Se adelanta el vencimiento.
        e.Db.SlaCommitments.Find(sla!.Id)!.DueAtUtc = DateTime.UtcNow.AddMinutes(-10);
        e.Db.SaveChanges();

        var vencidos = e.Sla.RevisarVencimientos();

        Assert.Single(vencidos);
        var guardado = e.Db.SlaCommitments.Find(sla.Id)!;
        Assert.Equal(SlaStatus.Vencido, guardado.Status);
        Assert.Null(guardado.NextReminderAtUtc);   // deja de recordar
    }

    [Fact]
    public void UnIncumplimientoYaNotificadoNoSeVuelveAReportar()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);
        var (_, _, sla) = e.Sla.Asignar(SlaTarget.Requerimiento(reqId), MiDevId, DateTime.Now.AddHours(1), 24, null, null, null);
        e.Db.SlaCommitments.Find(sla!.Id)!.DueAtUtc = DateTime.UtcNow.AddMinutes(-10);
        e.Db.SaveChanges();

        var primera = e.Sla.RevisarVencimientos();
        e.Sla.MarcarIncumplimientoNotificado(primera.Select(v => v.Id));
        var segunda = e.Sla.RevisarVencimientos();

        Assert.Single(primera);
        Assert.Empty(segunda);   // ya está marcado y ya no está Activo
    }

    // ── Posponer ────────────────────────────────────────────────────────────────

    [Fact]
    public void PosponerNoPuedeRebasarLaFechaLimite()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);
        // Vence en 2 horas; se intenta posponer 48.
        var (_, _, sla) = e.Sla.Asignar(SlaTarget.Requerimiento(reqId), MiDevId, DateTime.Now.AddHours(2), 1, null, null, null);

        var (ok, _) = e.Sla.Posponer(sla!.Id, 48);

        Assert.True(ok);
        var guardado = e.Db.SlaCommitments.Find(sla.Id)!;
        Assert.Equal(guardado.DueAtUtc, guardado.NextReminderAtUtc);
    }

    [Fact]
    public void NoSePuedePosponerElSlaDeOtroDesarrollador()
    {
        var admin = Nuevo();
        int reqId = NuevoRequerimiento(admin.Db);
        var (_, _, sla) = admin.Sla.Asignar(SlaTarget.Requerimiento(reqId), OtroDevId, DateTime.Now.AddDays(1), 4, null, null, null);

        // Ahora la misma base, pero la sesión es de otro desarrollador.
        var user = Ctx.As(UserRole.Desarrollador, MiDevId);
        var audit = new AuditService(admin.Db, user);
        var settings = new SettingsService(admin.Db, audit);
        var svc = new SlaService(admin.Db, user, audit,
            new AzureDevOpsService(settings, admin.Db, audit, new NotificationService(admin.Db)));

        Assert.Throws<AuthorizationException>(() => svc.Posponer(sla!.Id, 4));
    }

    // ── Cierre ──────────────────────────────────────────────────────────────────

    [Fact]
    public void MarcarCumplidoDetieneLosRecordatorios()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);
        var (_, _, sla) = e.Sla.Asignar(SlaTarget.Requerimiento(reqId), MiDevId, DateTime.Now.AddDays(1), 4, null, null, null);

        var (ok, _) = e.Sla.MarcarCumplido(sla!.Id, "atendido en la junta");

        Assert.True(ok);
        var guardado = e.Db.SlaCommitments.Find(sla.Id)!;
        Assert.Equal(SlaStatus.Cumplido, guardado.Status);
        Assert.Null(guardado.NextReminderAtUtc);
        Assert.Contains("atendido en la junta", guardado.Notes);
    }

    [Fact]
    public void UnSlaCumplidoNoApareceEnPendientes()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);
        var (_, _, sla) = e.Sla.Asignar(SlaTarget.Requerimiento(reqId), MiDevId, DateTime.Now.AddDays(1), 4, null, null, null);
        e.Db.SlaCommitments.Find(sla!.Id)!.NextReminderAtUtc = DateTime.UtcNow.AddMinutes(-1);
        e.Db.SaveChanges();
        e.Sla.MarcarCumplido(sla.Id);

        Assert.Empty(e.Sla.PendientesDeAviso(MiDevId));
    }

    [Fact]
    public void ComentarRequiereTicketLigado()
    {
        var e = Nuevo();
        int reqId = NuevoRequerimiento(e.Db);
        var (_, _, sla) = e.Sla.Asignar(SlaTarget.Requerimiento(reqId), MiDevId, DateTime.Now.AddDays(1), 24, null, null, null);

        var (ok, mensaje) = e.Sla.ComentarTicketAsync(sla!.Id, "avance").GetAwaiter().GetResult();

        Assert.False(ok);
        Assert.Contains("no tiene un ticket", mensaje);
        Assert.Equal(0, e.Db.SlaCommitments.Find(sla.Id)!.CommentCount);
    }
}
