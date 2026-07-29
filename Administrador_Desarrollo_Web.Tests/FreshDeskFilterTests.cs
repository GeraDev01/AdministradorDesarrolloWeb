using System.Collections.Generic;
using System.Linq;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Filtro «solo mis tickets o los del grupo»: el predicado de pertenencia, la resolución del grupo
/// (nombre o ID, con validación) y la reconciliación, que SOLO quita lo confirmado como no-mío en la
/// corrida y nunca por ausencia, conservando los vinculados a DevOps.
/// </summary>
public class FreshDeskFilterTests
{
    // ── EsMio ────────────────────────────────────────────────────────────────────
    [Fact] public void EsMio_PorAgenteAsignado() => Assert.True(FreshDeskService.EsMio(100, 5, miAgenteId: 100, grupoFiltroId: 9));
    [Fact] public void EsMio_PorGrupo()          => Assert.True(FreshDeskService.EsMio(999, 9, miAgenteId: 100, grupoFiltroId: 9));
    [Fact] public void EsMio_NingunoCoincide()   => Assert.False(FreshDeskService.EsMio(999, 5, miAgenteId: 100, grupoFiltroId: 9));
    [Fact] public void EsMio_SinCriterios_Falso()=> Assert.False(FreshDeskService.EsMio(100, 9, miAgenteId: null, grupoFiltroId: null));

    [Fact]
    public void EsMio_TicketSinAgenteNiGrupo_NoRevienta()
        => Assert.False(FreshDeskService.EsMio(responderId: null, groupId: null, miAgenteId: 100, grupoFiltroId: 9));

    // ── ResolverGrupoId ──────────────────────────────────────────────────────────
    private static readonly IReadOnlyDictionary<long, string> Grupos =
        new Dictionary<long, string> { [50] = "Desarrollo Web", [51] = "Soporte" };
    private static readonly IReadOnlyDictionary<long, string> SinGrupos = new Dictionary<long, string>();   // /groups dio 403

    [Fact] public void ResolverGrupo_PorNombreInsensibleAMayus() => Assert.Equal(50, FreshDeskService.ResolverGrupoId("  desarrollo web ", Grupos));
    [Fact] public void ResolverGrupo_IdNumericoConocido()        => Assert.Equal(51, FreshDeskService.ResolverGrupoId("51", Grupos));
    [Fact] public void ResolverGrupo_NombreDesconocido_EsNull()  => Assert.Null(FreshDeskService.ResolverGrupoId("Marketing", Grupos));
    [Fact] public void ResolverGrupo_Vacio_EsNull()              => Assert.Null(FreshDeskService.ResolverGrupoId("   ", Grupos));

    [Fact] // ID equivocado/renumerado que SÍ se puede validar → null (no filtra ni borra por un ID fantasma)
    public void ResolverGrupo_IdNumericoDesconocido_EsNull() => Assert.Null(FreshDeskService.ResolverGrupoId("999", Grupos));

    [Fact] // sin permiso para listar grupos (403): se confía en el ID que puso el usuario
    public void ResolverGrupo_IdNumerico_SinPoderValidar_SeConfia() => Assert.Equal(999, FreshDeskService.ResolverGrupoId("999", SinGrupos));

    [Fact] // un grupo cuyo NOMBRE parece número: gana el nombre, no se confunde con un ID
    public void ResolverGrupo_NombreNumerico_GanaAlId()
    {
        var g = new Dictionary<long, string> { [88] = "2024" };
        Assert.Equal(88, FreshDeskService.ResolverGrupoId("2024", g));
    }

    [Fact]
    public void ResolverGrupo_PorNombre_SinPermisoDeGrupos_EsNull()
        => Assert.Null(FreshDeskService.ResolverGrupoId("Desarrollo Web", SinGrupos));

    // ── Reconciliar (quita SOLO lo confirmado como no-mío, conserva vinculados) ────
    private static FreshDeskTicket T(long ext) => new()
    {
        ExternalId = ext, Subject = $"t{ext}", Status = 2, Url = $"http://f/{ext}"
    };

    [Fact]
    public void Reconciliar_QuitaLosConfirmados_PeroConservaLosVinculados()
    {
        var db = TestDb.New();
        db.FreshDeskTickets.AddRange(T(10), T(20), T(30));      // 10 no se toca; 20 se quita; 30 se quitaría pero vinculado
        db.DevOpsTickets.Add(new DevOpsTicket { ExternalId = 1, Title = "WI", State = "Active" });
        db.SaveChanges();

        var fd30 = db.FreshDeskTickets.Single(t => t.ExternalId == 30);
        var doTk = db.DevOpsTickets.Single();
        db.TicketLinks.Add(new TicketLink { DevOpsTicketId = doTk.Id, FreshDeskTicketId = fd30.Id, LinkedAt = System.DateTime.UtcNow });
        db.SaveChanges();

        int quitados = FreshDeskService.Reconciliar(db, new HashSet<long> { 20, 30 });

        Assert.Equal(1, quitados);                                                  // solo el 20
        Assert.Contains(db.FreshDeskTickets, t => t.ExternalId == 10);              // no estaba en la lista → queda
        Assert.DoesNotContain(db.FreshDeskTickets, t => t.ExternalId == 20);        // confirmado no-mío → fuera
        Assert.Contains(db.FreshDeskTickets, t => t.ExternalId == 30);              // vinculado → se conserva
    }

    [Fact]
    public void Reconciliar_ListaVacia_NoQuitaNada()
    {
        var db = TestDb.New();
        db.FreshDeskTickets.AddRange(T(10), T(11));
        db.SaveChanges();

        Assert.Equal(0, FreshDeskService.Reconciliar(db, new HashSet<long>()));
        Assert.Equal(2, db.FreshDeskTickets.Count());
    }

    [Fact] // un ticket viejo aún mío que NO se vio esta corrida (fuera de la ventana de 30 días) NO se toca
    public void Reconciliar_NoBorraLoQueNoSeVio()
    {
        var db = TestDb.New();
        db.FreshDeskTickets.AddRange(T(10), T(99));   // 99 = viejo, aún mío, no vino en la respuesta
        db.SaveChanges();

        // La lista a quitar solo trae lo confirmado como no-mío esta corrida; el 99 no está.
        FreshDeskService.Reconciliar(db, new HashSet<long> { 10 });

        Assert.Contains(db.FreshDeskTickets, t => t.ExternalId == 99);   // sobrevive
    }
}
