using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Los vínculos entre work items y tickets: lo único de esta vertical que escribe una persona a
/// mano, y por eso lo que hay que proteger de que se pierda por accidente.
/// </summary>
public class VinculosDeTicketsServiceTests
{
    private static VinculosDeTicketsService Servicio(AppDbContext db, UserRole rol = UserRole.Admin)
    {
        var usuario = UsuarioDePrueba.Como(rol);
        return new VinculosDeTicketsService(db, usuario, new AuditService(db, usuario, new OrigenDePrueba()));
    }

    private static async Task<(DevOpsTicket workItem, FreshDeskTicket ticket)> Poblar(AppDbContext db)
    {
        var workItem = new DevOpsTicket
        {
            ExternalId = 4321, Title = "Corregir el redondeo del timbrado",
            WorkItemType = "Bug", State = "Active", AssignedTo = "Ana López",
            Description = new string('x', 5000), UpdatedAtExternal = DateTime.UtcNow
        };
        var ticket = new FreshDeskTicket
        {
            ExternalId = 987, Subject = "No puedo timbrar", Status = 2, Priority = 4,
            AgentName = "Ana López", Description = new string('y', 5000), UpdatedAtExternal = DateTime.UtcNow
        };
        db.DevOpsTickets.Add(workItem);
        db.FreshDeskTickets.Add(ticket);
        await db.SaveChangesAsync();
        return (workItem, ticket);
    }

    [Fact]
    public async Task Pantalla_TraeLasDosListasConSusOpcionesYSuResumen()
    {
        using var db = TestDb.New();
        await Poblar(db);

        var pantalla = await Servicio(db).PantallaAsync(new FiltroDeWorkItems(), new FiltroDeTickets());

        Assert.Equal(1, pantalla.Resumen.WorkItems);
        Assert.Equal(1, pantalla.Resumen.Tickets);
        Assert.Equal(0, pantalla.Resumen.Vinculos);
        Assert.Single(pantalla.DevOps.Filas);
        Assert.Single(pantalla.Freshdesk.Filas);
        Assert.Equal(["Bug"], pantalla.DevOps.Tipos);
        Assert.Equal(["Urgente"], pantalla.Freshdesk.Prioridades);
        Assert.Contains("sin vincular", pantalla.DevOps.Resumen);
        Assert.False(pantalla.DevOps.Truncada);
    }

    [Fact]
    public async Task Pantalla_AplicaElFiltroDeCadaLadoPorSeparado()
    {
        using var db = TestDb.New();
        await Poblar(db);

        var pantalla = await Servicio(db).PantallaAsync(
            new FiltroDeWorkItems(Texto: "no aparece por ningún lado"),
            new FiltroDeTickets(Estado: "Abierto"));

        Assert.Empty(pantalla.DevOps.Filas);
        Assert.Single(pantalla.Freshdesk.Filas);
    }

    [Fact]
    public async Task Vincular_DejaConstanciaDeQuienLoAfirmo()
    {
        using var db = TestDb.New();
        var (workItem, ticket) = await Poblar(db);

        var (ok, mensaje) = await Servicio(db).VincularAsync(workItem.Id, ticket.Id, "  Mismo defecto  ");

        Assert.True(ok, mensaje);
        var enlace = await db.TicketLinks.SingleAsync();
        Assert.Equal("Mismo defecto", enlace.Notes);
        Assert.Equal("Admin", enlace.LinkedByUser);
        Assert.Contains("#4321", mensaje);
        Assert.Contains("#987", mensaje);
    }

    [Fact]
    public async Task Vincular_DosVecesLoMismo_LoExplicaEnVezDeReventar()
    {
        // El índice único de la base lo impediría igual, pero con un choque de clave que nadie sabe
        // leer. El mensaje dice qué pasó.
        using var db = TestDb.New();
        var (workItem, ticket) = await Poblar(db);
        await Servicio(db).VincularAsync(workItem.Id, ticket.Id, null);

        var (ok, mensaje) = await Servicio(db).VincularAsync(workItem.Id, ticket.Id, null);

        Assert.False(ok);
        Assert.Contains("ya estaban vinculados", mensaje);
        Assert.Single(await db.TicketLinks.ToListAsync());
    }

    [Fact]
    public async Task Vincular_ConAlgoQueYaNoEstaEnLaLista_LoDice()
    {
        using var db = TestDb.New();
        var (workItem, _) = await Poblar(db);

        var (ok, mensaje) = await Servicio(db).VincularAsync(workItem.Id, 9999, null);

        Assert.False(ok);
        Assert.Contains("Freshdesk", mensaje);
    }

    [Fact]
    public async Task Quitar_DeshaceElVinculoYDejaLosDosTicketsEnSuSitio()
    {
        using var db = TestDb.New();
        var (workItem, ticket) = await Poblar(db);
        await Servicio(db).VincularAsync(workItem.Id, ticket.Id, null);
        var enlace = await db.TicketLinks.SingleAsync();

        var (ok, mensaje) = await Servicio(db).QuitarAsync(enlace.Id);

        Assert.True(ok, mensaje);
        Assert.Empty(await db.TicketLinks.ToListAsync());
        Assert.NotNull(await db.DevOpsTickets.FindAsync(workItem.Id));
        Assert.NotNull(await db.FreshDeskTickets.FindAsync(ticket.Id));
    }

    [Fact]
    public async Task Quitar_LoQueYaNoExiste_LoDiceSinRomperNada()
    {
        using var db = TestDb.New();

        var (ok, mensaje) = await Servicio(db).QuitarAsync(4242);

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje);
    }

    [Fact]
    public async Task VincularEsDelLider()
    {
        using var db = TestDb.New();
        var (workItem, ticket) = await Poblar(db);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            Servicio(db, UserRole.Desarrollador).VincularAsync(workItem.Id, ticket.Id, null));
    }
}
