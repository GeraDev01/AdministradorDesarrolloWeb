using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Auto-materializar los tickets de DevOps asignados como requerimientos (para medirles el tiempo
/// en «Mis Asignaciones»): crea + asigna, es idempotente, excluye cerrados y NO pisa el avance local.
/// </summary>
public class DevOpsMaterializeTests
{
    private static (AzureDevOpsService svc, AppDbContext db) Nuevo()
    {
        var db = TestDb.New();
        var user = Ctx.As(UserRole.Admin);
        var settings = new SettingsService(db, new AuditService(db, user));
        return (new AzureDevOpsService(settings, db, new AuditService(db, user), new NotificationService(db)), db);
    }

    private static Developer SeedDev(AppDbContext db, string email)
    {
        var d = new Developer { FullName = "Angel Palma", Email = email, IsActive = true };
        db.Developers.Add(d); db.SaveChanges();
        return d;
    }

    private static void SeedTicket(AppDbContext db, int ext, string state, string email) =>
        db.DevOpsTickets.Add(new DevOpsTicket
        {
            ExternalId = ext, Title = $"WI {ext}", WorkItemType = "Task",
            State = state, AssignedTo = "Angel Palma", AssignedToUniqueName = email, Url = $"http://d/{ext}"
        });

    [Fact]
    public void Materializa_TicketAbierto_CreaRequerimientoYAsignacion()
    {
        var (svc, db) = Nuevo();
        var dev = SeedDev(db, "a@x.com");
        SeedTicket(db, 100, "Active", "a@x.com");
        SeedTicket(db, 101, "Done", "a@x.com");   // cerrado: no se materializa
        db.SaveChanges();

        var (nuevos, _) = svc.MaterializarAsignados(dev.Id);

        Assert.Equal(1, nuevos);
        var req = db.Requirements.Include(r => r.Assignments).Single();
        Assert.Equal("100", req.ExternalId);
        Assert.Equal(RequirementSource.AzureDevOps, req.Source);
        Assert.Contains(req.Assignments, a => a.DeveloperId == dev.Id);
    }

    [Fact]
    public void Materializa_EsIdempotente_NoDuplica()
    {
        var (svc, db) = Nuevo();
        var dev = SeedDev(db, "a@x.com");
        SeedTicket(db, 100, "Active", "a@x.com");
        db.SaveChanges();

        svc.MaterializarAsignados(dev.Id);
        var (nuevos, _) = svc.MaterializarAsignados(dev.Id);

        Assert.Equal(0, nuevos);
        Assert.Single(db.Requirements);
        Assert.Single(db.Assignments);
    }

    [Fact]
    public void Materializa_NoPisaElEstadoLocal()
    {
        var (svc, db) = Nuevo();
        var dev = SeedDev(db, "a@x.com");
        SeedTicket(db, 100, "New", "a@x.com");
        db.SaveChanges();

        svc.MaterializarAsignados(dev.Id);
        var req = db.Requirements.Single();
        req.Status = RequirementStatus.EnDesarrollo;   // el desarrollador arrancó su cronómetro
        db.SaveChanges();

        svc.MaterializarAsignados(dev.Id);             // re-entrar a Mis Asignaciones
        Assert.Equal(RequirementStatus.EnDesarrollo, db.Requirements.Single().Status);
    }

    [Fact]
    public void Materializa_SinTicketsAsignados_NoHaceNada()
    {
        var (svc, db) = Nuevo();
        var dev = SeedDev(db, "a@x.com");
        SeedTicket(db, 100, "Active", "otro@x.com");   // de otra persona
        db.SaveChanges();

        Assert.Equal((0, 0), svc.MaterializarAsignados(dev.Id));
        Assert.Empty(db.Requirements);
    }
}
