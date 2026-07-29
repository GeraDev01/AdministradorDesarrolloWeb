using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms.Controls;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// El Dashboard comprueba el rol por su cuenta: las secciones de datos del equipo (carga por
/// desarrollador, recordatorios internos y ranking de desempeño) no deben ni construirse ni
/// consultarse para quien no es administrador.
///
/// Se instancia el control de verdad (requiere STA por WinForms) en lugar de probar solo una
/// bandera: lo que importa es que las consultas no corran.
/// </summary>
public class DashboardRoleTests
{
    private static AppDbContext ConDatos()
    {
        var db = TestDb.New();
        var dev = new Developer { FullName = "Alguien", IsActive = true };
        db.Developers.Add(dev);
        var crit = new ScoringCriterion { Name = "C", DefaultPoints = 5, IsActive = true, CreatedAt = DateTime.UtcNow };
        db.ScoringCriteria.Add(crit);
        db.Notes.Add(new Note { Title = "Pendiente interno", IsCompleted = false, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();

        db.PointEntries.Add(new PointEntry
        {
            DeveloperId = dev.Id, CriterionId = crit.Id, Points = 10,
            Year = DateTime.Today.Year, Month = DateTime.Today.Month,
            ApprovalStatus = PointApprovalStatus.Aprobado, Date = DateTime.UtcNow
        });
        db.SaveChanges();
        return db;
    }

    /// <summary>Cuenta cuántos DataGridView quedaron construidos dentro del control.</summary>
    private static int ContarGrids(Control raiz)
    {
        int n = raiz is DataGridView ? 1 : 0;
        foreach (Control hijo in raiz.Controls) n += ContarGrids(hijo);
        return n;
    }

    private static int GridsPara(UserRole rol)
    {
        int resultado = 0;
        var hilo = new Thread(() =>
        {
            using var db = ConDatos();
            using var ctrl = new DashboardControl(db, Ctx.As(rol, rol == UserRole.Desarrollador ? 1 : null));
            resultado = ContarGrids(ctrl);
        });
        hilo.SetApartmentState(ApartmentState.STA);   // WinForms lo exige
        hilo.Start();
        hilo.Join();
        return resultado;
    }

    [Fact]
    public void ElAdministradorVeLasCuatroSecciones()
    {
        Assert.Equal(4, GridsPara(UserRole.Admin));
    }

    [Theory]
    [InlineData(UserRole.Operaciones)]
    [InlineData(UserRole.Desarrollador)]
    public void LosDemasRolesSoloVenProximasEntregas(UserRole rol)
    {
        // Solo el grid de vencimientos: los de carga, recordatorios y ranking ni se construyen,
        // así que sus consultas tampoco se ejecutan.
        Assert.Equal(1, GridsPara(rol));
    }
}
