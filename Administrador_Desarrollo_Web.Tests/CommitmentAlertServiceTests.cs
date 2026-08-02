using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// El barrido de compromisos contra la base: que cree el aviso una sola vez, que no lo repita en
/// cada ciclo (corre cada 5 minutos), y que el campo Url quede vacío — ese lo abre el shell y una
/// clave de navegación interna dejaría el doble clic sin hacer nada.
/// </summary>
public class CommitmentAlertServiceTests
{
    private static CommitmentAlertService Svc(AppDbContext db) => new(db, new NotificationService(db));

    /// <summary>Un desarrollador con cuenta activa y un requerimiento suyo comprometido.</summary>
    private static (Developer dev, User user, Requirement req) Escenario(AppDbContext db, int diasParaVencer)
    {
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();

        var user = new User { Username = "ana", FullName = "Ana", Role = UserRole.Desarrollador, IsActive = true, PasswordHash = "x", DeveloperId = dev.Id };
        db.Users.Add(user);

        var req = new Requirement
        {
            Title = "Portal de pagos", Status = RequirementStatus.EnDesarrollo,
            CommittedDeliveryDate = DateTime.Today.AddDays(diasParaVencer)
        };
        db.Requirements.Add(req); db.SaveChanges();
        db.Assignments.Add(new Assignment { RequirementId = req.Id, DeveloperId = dev.Id });
        db.SaveChanges();
        return (dev, user, req);
    }

    [Fact]
    public void CreaElAviso_ParaElDesarrolladorAsignado()
    {
        var db = TestDb.New();
        var (_, user, _) = Escenario(db, diasParaVencer: 1);

        Assert.Equal(1, Svc(db).RevisarYAvisar());

        var n = db.Notifications.AsNoTracking().Single();
        Assert.Equal(user.Id, n.ForUserId);
        Assert.Equal(NotificationKind.CompromisoPorVencer, n.Kind);
        Assert.Contains("vence mañana", n.Title);
    }

    [Fact]
    public void NoDejaUrl_PorqueEsaLaAbreElShell()
    {
        // Con una clave interna en Url, el doble clic intentaba un ShellExecute que fallaba en
        // silencio Y se saltaba el mensaje. Sin Url, la pantalla muestra el detalle completo.
        var db = TestDb.New();
        Escenario(db, diasParaVencer: 0);

        Svc(db).RevisarYAvisar();

        Assert.Null(db.Notifications.AsNoTracking().Single().Url);
    }

    [Fact]
    public void DosCiclosSeguidos_NoDuplicanElAviso()
    {
        // El temporizador corre cada 5 minutos: sin dedupe, la bandeja se llenaría del mismo aviso.
        var db = TestDb.New();
        Escenario(db, diasParaVencer: 1);
        var svc = Svc(db);

        Assert.Equal(1, svc.RevisarYAvisar());
        Assert.Equal(0, svc.RevisarYAvisar());
        Assert.Single(db.Notifications.AsNoTracking().ToList());
    }

    [Fact]
    public void ElIndiceUnico_ImpideElDuplicadoDeOtraInstancia()
    {
        // La comprobación en código es un lee-luego-inserta: con dos apps abiertas, ambas pueden
        // pasarla. El respaldo real es UX_Notif_Dedupe, y esta prueba verifica que existe y muerde.
        var db = TestDb.New();
        var (_, user, _) = Escenario(db, diasParaVencer: 1);
        Svc(db).RevisarYAvisar();
        var clave = db.Notifications.AsNoTracking().Single().DedupeKey;

        db.Notifications.Add(new Notification
        {
            ForUserId = user.Id, Kind = NotificationKind.CompromisoPorVencer,
            Title = "duplicado", Message = "x", DedupeKey = clave
        });

        Assert.ThrowsAny<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void ElIndiceEsFiltrado_LosAvisosSinClaveNoSePisan()
    {
        // Los avisos de asignación no llevan DedupeKey. Sin el filtro «WHERE DedupeKey IS NOT
        // NULL», el índice único dejaría UNO SOLO por usuario y rompería esa función.
        var db = TestDb.New();
        var (_, user, _) = Escenario(db, diasParaVencer: 30);   // lejos: no genera aviso propio
        var notif = new NotificationService(db);

        Assert.NotNull(notif.Notify(user.Id, NotificationKind.RequirementAssigned, "A", "uno"));
        Assert.NotNull(notif.Notify(user.Id, NotificationKind.RequirementAssigned, "B", "dos"));

        Assert.Equal(2, db.Notifications.AsNoTracking().Count(n => n.DedupeKey == null));
    }

    [Fact]
    public void SinCuentaActiva_NoRevienta_SoloNoAvisa()
    {
        var db = TestDb.New();
        var dev = new Developer { FullName = "Sin cuenta", IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();
        var req = new Requirement { Title = "X", Status = RequirementStatus.EnDesarrollo, CommittedDeliveryDate = DateTime.Today };
        db.Requirements.Add(req); db.SaveChanges();
        db.Assignments.Add(new Assignment { RequirementId = req.Id, DeveloperId = dev.Id });
        db.SaveChanges();

        Assert.Equal(0, Svc(db).RevisarYAvisar());
    }

    [Fact]
    public void SinCompromisosCercanos_NoTocaLaBase()
    {
        var db = TestDb.New();
        Escenario(db, diasParaVencer: 30);

        Assert.Equal(0, Svc(db).RevisarYAvisar());
        Assert.Empty(db.Notifications.AsNoTracking().ToList());
    }
}
