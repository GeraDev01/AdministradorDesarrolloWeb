using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Importar work items de DevOps como Requerimientos: auto-asignación por identidad y exclusión de
/// los que ya están cerrados (Done/Removed), que es justo lo que se pidió para dar seguimiento.
/// </summary>
public class DevOpsImportTests
{
    private static AzureDevOpsService Nuevo(out AppDbContext db)
    {
        db = TestDb.New();
        var user = Ctx.As(UserRole.Admin);
        var settings = new SettingsService(db, new AuditService(db, user));
        return new AzureDevOpsService(settings, db, new AuditService(db, user), new NotificationService(db));
    }

    private static DevOpsWorkItem WI(int id, string title, string state, string assignedDisplay, string assignedEmail)
        => new(id, title, "Task", state, null, $"http://devops/{id}", "", "", assignedDisplay, assignedEmail);

    [Theory]
    [InlineData("Active", false)]
    [InlineData("New", false)]
    [InlineData("To Do", false)]
    [InlineData("Resolved", false)]   // resuelto NO es cerrado: sigue en juego
    [InlineData("Done", true)]
    [InlineData("Closed", true)]
    [InlineData("Completed", true)]
    [InlineData("Removed", true)]
    public void EsCerrado_ClasificaSegunElEstado(string state, bool esperado)
        => Assert.Equal(esperado, AzureDevOpsService.EsCerrado(state));

    [Fact]
    public void Importar_AutoAsignaPorCorreo_YExcluyeCerrados()
    {
        var svc = Nuevo(out var db);
        var dev = new Developer { FullName = "Daniel López", Email = "daniel.lopez@soltum.com.mx", IsActive = true };
        db.Developers.Add(dev);
        db.SaveChanges();

        var items = new List<DevOpsWorkItem>
        {
            WI(101, "Trabajo vivo",  "Active",  "Daniel Lopez", "daniel.lopez@soltum.com.mx"),
            WI(102, "Ya terminado",  "Done",    "Daniel Lopez", "daniel.lopez@soltum.com.mx"),
            WI(103, "Removido",      "Removed", "Daniel Lopez", "daniel.lopez@soltum.com.mx"),
        };

        var result = svc.ImportAsRequirements(items);

        // Solo el activo se dio de alta.
        Assert.Equal(1, result.Added);
        var reqs = db.Requirements.Include(r => r.Assignments).ToList();
        Assert.Single(reqs);
        Assert.Equal("Trabajo vivo", reqs[0].Title);
        Assert.Equal(RequirementSource.AzureDevOps, reqs[0].Source);

        // Y quedó asignado a Daniel por identidad (correo), sin regla alguna.
        Assert.Contains(reqs[0].Assignments, a => a.DeveloperId == dev.Id);
    }

    [Fact]
    public void Importar_SinDesarrolladorQueEmpate_CreaRequerimientoSinAsignar()
    {
        var svc = Nuevo(out var db);
        db.Developers.Add(new Developer { FullName = "Angel Palma", Email = "angel.palma@soltum.com.mx", IsActive = true });
        db.SaveChanges();

        var items = new List<DevOpsWorkItem>
        {
            WI(201, "De alguien ajeno", "Active", "Persona Externa", "externa@otro.com"),
        };

        var result = svc.ImportAsRequirements(items);

        Assert.Equal(1, result.Added);
        var req = db.Requirements.Include(r => r.Assignments).Single();
        Assert.Empty(req.Assignments);                 // sin match ⇒ sin asignación
        Assert.Null(result.NewItems.Single().AssignedTo);
    }

    [Fact]
    public void Importar_ItemExistenteQuePasoACerrado_SeActualizaAEntregado()
    {
        var svc = Nuevo(out var db);
        // Primera importación: activo.
        svc.ImportAsRequirements([WI(301, "Cosa", "Active", "Angel Palma", "angel.palma@soltum.com.mx")]);
        // Luego el mismo item pasó a Done: la actualización SÍ debe reflejarlo (para el seguimiento),
        // aunque uno nuevo cerrado no se crearía.
        var result = svc.ImportAsRequirements([WI(301, "Cosa", "Done", "Angel Palma", "angel.palma@soltum.com.mx")]);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Updated);
        var req = db.Requirements.Single(r => r.ExternalId == "301");
        Assert.Equal(RequirementStatus.Entregado, req.Status);
    }
}
