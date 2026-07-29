using System.Linq;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Detección de «me asignaron un ticket de Freshdesk»: línea base sin avisar la primera vez (por
/// agente), aviso solo por lo NUEVO/ACTIVO/mío, reasignación de vuelta vuelve a avisar, y rotar la
/// API key a otro agente NO inunda con su backlog.
/// </summary>
public class FreshDeskAssignmentTests
{
    private const int UserId = 7;
    private const long MiAgente = 1001;

    private static FreshDeskTicket Ticket(long externalId, long? responderId, int status = 2) => new()
    {
        ExternalId = externalId,
        Subject = $"Ticket {externalId}",
        Status = status,
        ResponderId = responderId,
        Url = $"https://x.freshdesk.com/helpdesk/tickets/{externalId}"
    };

    private static void Reasignar(AppDbContext db, long externalId, long? nuevoResponder)
    {
        var t = db.FreshDeskTickets.First(x => x.ExternalId == externalId);
        t.ResponderId = nuevoResponder;
        db.SaveChanges();
    }

    [Fact]
    public void PrimeraVez_FijaLineaBase_SinAvisar()
    {
        var db = TestDb.New();
        db.FreshDeskTickets.Add(Ticket(100, MiAgente));   // ya asignado a mí antes de empezar
        db.FreshDeskTickets.Add(Ticket(200, 999));        // de alguien más
        db.SaveChanges();

        int nuevos = FreshDeskService.DetectarAsignadosNuevos(db, UserId, MiAgente);

        Assert.Equal(0, nuevos);
        Assert.Empty(db.Notifications);                                   // nada de aluvión de backlog
        Assert.Contains(db.FreshDeskAssignmentsSeen, s => s.ExternalId == 0 && s.AgentId == MiAgente);   // centinela
        Assert.Contains(db.FreshDeskAssignmentsSeen, s => s.ExternalId == 100 && s.AgentId == MiAgente); // lo mío en la base
        Assert.DoesNotContain(db.FreshDeskAssignmentsSeen, s => s.ExternalId == 200);
    }

    [Fact]
    public void TrasLaBase_AvisaSoloPorLoNuevoYMio()
    {
        var db = TestDb.New();
        FreshDeskService.DetectarAsignadosNuevos(db, UserId, MiAgente);   // base vacía (solo centinela)

        db.FreshDeskTickets.Add(Ticket(300, MiAgente));   // nuevo y mío → avisa
        db.FreshDeskTickets.Add(Ticket(400, 999));        // nuevo pero de otro → no
        db.SaveChanges();

        int nuevos = FreshDeskService.DetectarAsignadosNuevos(db, UserId, MiAgente);

        Assert.Equal(1, nuevos);
        var aviso = Assert.Single(db.Notifications);
        Assert.Equal(NotificationKind.FreshDeskAssigned, aviso.Kind);
        Assert.Equal(UserId, aviso.ForUserId);
        Assert.Contains("#300", aviso.Title);
    }

    [Fact]
    public void NoAvisa_PorTicketsResueltosOCerrados()
    {
        var db = TestDb.New();
        FreshDeskService.DetectarAsignadosNuevos(db, UserId, MiAgente);   // base

        db.FreshDeskTickets.Add(Ticket(500, MiAgente, status: 4));   // resuelto
        db.FreshDeskTickets.Add(Ticket(501, MiAgente, status: 5));   // cerrado
        db.FreshDeskTickets.Add(Ticket(502, MiAgente, status: 3));   // pendiente → sí cuenta
        db.SaveChanges();

        int nuevos = FreshDeskService.DetectarAsignadosNuevos(db, UserId, MiAgente);

        Assert.Equal(1, nuevos);
        Assert.Contains("#502", Assert.Single(db.Notifications).Title);
    }

    [Fact]
    public void NoRepiteElMismoTicket_MientrasSigaAsignado()
    {
        var db = TestDb.New();
        FreshDeskService.DetectarAsignadosNuevos(db, UserId, MiAgente);   // base

        db.FreshDeskTickets.Add(Ticket(600, MiAgente));
        db.SaveChanges();

        Assert.Equal(1, FreshDeskService.DetectarAsignadosNuevos(db, UserId, MiAgente));
        Assert.Equal(0, FreshDeskService.DetectarAsignadosNuevos(db, UserId, MiAgente));   // sigue mío → no repite
        Assert.Single(db.Notifications);
    }

    [Fact]
    public void ReasignarLejosYDeVuelta_VuelveAvisar()
    {
        var db = TestDb.New();
        FreshDeskService.DetectarAsignadosNuevos(db, UserId, MiAgente);   // base

        db.FreshDeskTickets.Add(Ticket(700, MiAgente));
        db.SaveChanges();
        Assert.Equal(1, FreshDeskService.DetectarAsignadosNuevos(db, UserId, MiAgente));   // aviso 1

        Reasignar(db, 700, 999);                                          // me lo quitan
        Assert.Equal(0, FreshDeskService.DetectarAsignadosNuevos(db, UserId, MiAgente));
        Assert.DoesNotContain(db.FreshDeskAssignmentsSeen, s => s.ExternalId == 700);   // se podó el «visto»

        Reasignar(db, 700, MiAgente);                                     // me lo devuelven
        Assert.Equal(1, FreshDeskService.DetectarAsignadosNuevos(db, UserId, MiAgente));   // aviso 2

        Assert.Equal(2, db.Notifications.Count(n => n.Title.Contains("#700")));
    }

    [Fact]
    public void RotarLaApiKey_NoInundaConElBacklogDelOtroAgente()
    {
        const long otroAgente = 2002;
        var db = TestDb.New();
        db.FreshDeskTickets.Add(Ticket(100, MiAgente));
        db.FreshDeskTickets.Add(Ticket(800, otroAgente));
        db.FreshDeskTickets.Add(Ticket(801, otroAgente));
        db.SaveChanges();

        // Base del primer agente.
        FreshDeskService.DetectarAsignadosNuevos(db, UserId, MiAgente);

        // Se rota la key a otro agente con backlog: al ser (usuario, agente) nuevo, solo fija su base.
        int nuevos = FreshDeskService.DetectarAsignadosNuevos(db, UserId, otroAgente);

        Assert.Equal(0, nuevos);
        Assert.Empty(db.Notifications);
        Assert.Contains(db.FreshDeskAssignmentsSeen, s => s.AgentId == otroAgente && s.ExternalId == 0);
        Assert.Contains(db.FreshDeskAssignmentsSeen, s => s.AgentId == otroAgente && s.ExternalId == 800);
    }
}
