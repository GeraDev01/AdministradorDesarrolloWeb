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
            using var ctrl = new DashboardControl(db, Ctx.As(rol, rol == UserRole.Desarrollador ? 1 : null), new PerformanceScoringService(db));
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

    /// <summary>
    /// Base con dos requerimientos: uno asignado a Ana y otro a Beto, cada uno en un estado
    /// distinto, más una nota de cada quien.
    /// </summary>
    private static (AppDbContext db, int anaId) ConDosDesarrolladores()
    {
        var db = TestDb.New();
        var ana  = new Developer { FullName = "Ana",  IsActive = true };
        var beto = new Developer { FullName = "Beto", IsActive = true };
        db.Developers.AddRange(ana, beto);
        db.SaveChanges();

        var mio   = new Requirement { Title = "Mío",   Status = RequirementStatus.EnDesarrollo };
        var ajeno = new Requirement { Title = "Ajeno", Status = RequirementStatus.EnDesarrollo };
        db.Requirements.AddRange(mio, ajeno);
        db.SaveChanges();

        db.Assignments.Add(new Assignment { RequirementId = mio.Id,   DeveloperId = ana.Id });
        db.Assignments.Add(new Assignment { RequirementId = ajeno.Id, DeveloperId = beto.Id });
        db.Notes.Add(new Note { Title = "De Ana",  DeveloperId = ana.Id, IsCompleted = false, CreatedAt = DateTime.UtcNow });
        db.Notes.Add(new Note { Title = "De nadie", IsCompleted = false, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
        return (db, ana.Id);
    }

    /// <summary>Los números de las tarjetas superiores, en orden.</summary>
    private static List<string> ValoresDeTarjetas(UserRole rol, int? developerId)
    {
        var valores = new List<string>();
        var hilo = new Thread(() =>
        {
            var (db, anaId) = ConDosDesarrolladores();
            using (db)
            using (var ctrl = new DashboardControl(db, Ctx.As(rol, developerId ?? anaId), new PerformanceScoringService(db)))
            {
                // Las tarjetas son Panels con dos Labels: el valor arriba y la etiqueta abajo.
                foreach (Control c in RecorrerTodo(ctrl))
                    if (c is Label l && l.Font.Size > 12f) valores.Add(l.Text);
            }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        return valores;
    }

    private static IEnumerable<Control> RecorrerTodo(Control raiz)
    {
        foreach (Control hijo in raiz.Controls)
        {
            yield return hijo;
            foreach (var nieto in RecorrerTodo(hijo)) yield return nieto;
        }
    }

    [Fact]
    public void ElDesarrolladorSoloCuentaSusRequerimientos()
    {
        // Hay dos requerimientos «En desarrollo», pero solo uno es de Ana. Antes la tarjeta decía 2
        // y ninguno de los dos tenía por qué ser suyo.
        var deAna   = ValoresDeTarjetas(UserRole.Desarrollador, null);
        var deAdmin = ValoresDeTarjetas(UserRole.Admin, null);

        Assert.Equal("1", deAna[1]);     // En desarrollo
        Assert.Equal("2", deAdmin[1]);   // el administrador sigue viendo el total del área
    }

    [Fact]
    public void ElDesarrolladorSoloCuentaSusRecordatorios()
    {
        // Dos notas sin completar; solo una lleva el nombre de Ana.
        var deAna   = ValoresDeTarjetas(UserRole.Desarrollador, null);
        var deAdmin = ValoresDeTarjetas(UserRole.Admin, null);

        Assert.Equal("1", deAna[^1]);    // Pendientes
        Assert.Equal("2", deAdmin[^1]);
    }
}
