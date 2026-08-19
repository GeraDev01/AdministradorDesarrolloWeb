using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Equipos;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// DE DÓNDE SALEN LOS PUNTOS DE UNA FILA DEL RANKING.
///
/// <para>Son dos cosas que el ranking no decía y que estas pruebas fijan. La primera: cuánto del
/// total salió del POOL —trabajo publicado, tomado y verificado— y cuánto del resto —autocalificarse
/// y lo que el líder otorga a mano—. Un mismo total de cuarenta no significa lo mismo según de dónde
/// venga, y hasta ahora no había forma de distinguirlo sin abrir las entradas una por una.</para>
///
/// <para>La segunda: la lista de lo que hay detrás, que es lo que convierte un número que se acepta
/// o no se acepta en algo que se puede revisar y explicar.</para>
/// </summary>
public class DetalleDeDesempenoTests
{
    private const int Anio = 2026;
    private const int Mes = 8;

    private static DesempenoQueryService Svc(AppDbContext db, ICurrentUser cu) =>
        new(db, new PerformanceScoringService(db, cu), cu);

    private static ICurrentUser Lider() => UsuarioDePrueba.Como(UserRole.Admin, userId: 9);

    private static Developer Dev(AppDbContext db, string nombre, int? equipoId = null)
    {
        var d = new Developer { FullName = nombre, IsActive = true, TeamId = equipoId };
        db.Developers.Add(d);
        db.SaveChanges();
        return d;
    }

    private static ScoringCriterion Criterio(AppDbContext db, string nombre)
    {
        var c = new ScoringCriterion
        {
            Name = nombre, DefaultPoints = 0, IsActive = true, Scope = CriterionScope.Individual
        };
        db.ScoringCriteria.Add(c);
        db.SaveChanges();
        return c;
    }

    private static void Punto(AppDbContext db, int devId, int criterioId, int puntos, string? comentario = null)
    {
        db.PointEntries.Add(new PointEntry
        {
            DeveloperId = devId, CriterionId = criterioId, Points = puntos,
            Year = Anio, Month = Mes, Date = new DateTime(Anio, Mes, 10, 12, 0, 0, DateTimeKind.Utc),
            Comment = comentario, ApprovalStatus = PointApprovalStatus.Aprobado
        });
        db.SaveChanges();
    }

    /// <summary>Una base con Ana: 10 puntos de autocalificación y 12 de una actividad del pool.</summary>
    private static (AppDbContext db, Developer ana) BaseConAna()
    {
        var db = TestDb.New();
        var ana = Dev(db, "Ana");

        var suyo = Criterio(db, "Ayudaste a alguien");
        var delPool = Criterio(db, PoolSeed.NombreCriterio(PoolWorkType.Bug));

        Punto(db, ana.Id, suyo.Id, 10, "Le eché una mano a Beto");
        Punto(db, ana.Id, delPool.Id, 12, "Pool #1: Corregir el cálculo");

        return (db, ana);
    }

    // ── La columna «Del pool» del ranking ────────────────────────────────────────

    [Fact]
    public async Task El_ranking_separa_lo_que_salio_del_pool()
    {
        var (db, ana) = BaseConAna();

        var fila = (await Svc(db, Lider()).RankingAdminAsync(Anio, Mes, false))
            .Individual.Single(f => f.DeveloperId == ana.Id);

        Assert.Equal(22, fila.Total);
        Assert.Equal(12, fila.DelPool);      // solo lo del pool
        Assert.Equal(0, fila.Retrabajos);
    }

    /// <summary>
    /// El retrabajo se cuenta aparte y RESTA. La cuenta va separada de los puntos a propósito: los
    /// puntos ya están dentro de «Del pool» y lo que aporta el número es cuántas veces hubo que
    /// volver sobre algo ya entregado.
    /// </summary>
    [Fact]
    public async Task El_ranking_cuenta_los_retrabajos_y_los_resta()
    {
        var (db, ana) = BaseConAna();
        var retrabajo = Criterio(db, PoolSeed.NombreCriterio(PoolWorkType.Retrabajo));
        Punto(db, ana.Id, retrabajo.Id, -8, "Pool #2: se rompió lo que ya se había entregado");

        var fila = (await Svc(db, Lider()).RankingAdminAsync(Anio, Mes, false))
            .Individual.Single(f => f.DeveloperId == ana.Id);

        Assert.Equal(14, fila.Total);        // 10 + 12 − 8
        Assert.Equal(4, fila.DelPool);       // 12 − 8: lo del pool, neto
        Assert.Equal(1, fila.Retrabajos);
    }

    [Fact]
    public async Task El_ranking_de_equipos_suma_lo_del_pool_de_sus_integrantes()
    {
        using var db = TestDb.New();
        var equipo = new Team { Name = "Alfa" };
        db.Teams.Add(equipo);
        db.SaveChanges();

        var ana = Dev(db, "Ana", equipo.Id);
        var beto = Dev(db, "Beto", equipo.Id);
        var delPool = Criterio(db, PoolSeed.NombreCriterio(PoolWorkType.Tarea));
        var suyo = Criterio(db, "Ayudaste a alguien");

        Punto(db, ana.Id, delPool.Id, 6);
        Punto(db, beto.Id, delPool.Id, 4);
        Punto(db, beto.Id, suyo.Id, 20);

        var fila = (await Svc(db, Lider()).RankingAdminAsync(Anio, Mes, false))
            .Equipos.Single(f => f.TeamId == equipo.Id);

        Assert.Equal(30, fila.Total);
        Assert.Equal(10, fila.DelPool);
    }

    // ── El detalle que abre el doble clic ────────────────────────────────────────

    [Fact]
    public async Task El_detalle_de_una_persona_trae_cada_entrada_con_su_motivo()
    {
        var (db, ana) = BaseConAna();

        var d = await Svc(db, Lider()).DetalleDeDesarrolladorAsync(ana.Id, Anio, Mes);

        Assert.NotNull(d);
        Assert.Equal("Ana", d!.Titulo);
        Assert.Equal(22, d.Total);
        Assert.Equal(12, d.DelPool);
        Assert.Equal(2, d.Entradas.Count);

        var delPool = d.Entradas.Single(e => e.EsDelPool);
        Assert.Equal(12, delPool.Puntos);
        Assert.Equal("Ana", delPool.Quien);

        var suya = d.Entradas.Single(e => !e.EsDelPool);
        Assert.Equal("Le eché una mano a Beto", suya.Comentario);
    }

    /// <summary>
    /// El detalle es del MES QUE SE PIDE. Es lo que sostiene que el panel se abra desde una fila del
    /// ranking de marzo mientras el reloj dice agosto y siga cuadrando con ella.
    /// </summary>
    [Fact]
    public async Task El_detalle_no_se_lleva_puntos_de_otro_mes()
    {
        var (db, ana) = BaseConAna();
        var suyo = db.ScoringCriteria.Single(c => c.Name == "Ayudaste a alguien");
        db.PointEntries.Add(new PointEntry
        {
            DeveloperId = ana.Id, CriterionId = suyo.Id, Points = 99,
            Year = Anio, Month = Mes - 1, Date = new DateTime(Anio, Mes - 1, 3, 0, 0, 0, DateTimeKind.Utc),
            ApprovalStatus = PointApprovalStatus.Aprobado
        });
        db.SaveChanges();

        var d = await Svc(db, Lider()).DetalleDeDesarrolladorAsync(ana.Id, Anio, Mes);

        Assert.Equal(22, d!.Total);
        Assert.DoesNotContain(d.Entradas, e => e.Puntos == 99);
    }

    [Fact]
    public async Task El_detalle_trae_las_actividades_del_pool_aceptadas_en_el_mes()
    {
        var (db, ana) = BaseConAna();

        db.PoolActivities.Add(new PoolActivity
        {
            Title = "Corregir el cálculo", WorkType = PoolWorkType.Bug, Complexity = PoolComplexity.Alta,
            Points = 12, Status = PoolActivityStatus.Aceptada, ClaimedByDeveloperId = ana.Id,
            ReviewedAt = new DateTime(Anio, Mes, 10, 12, 0, 0, DateTimeKind.Utc),
            ExternalUrl = "https://dev.azure.com/o/p/_workitems/edit/7"
        });
        // La de otro mes no sale: sus puntos están en otro renglón del ranking.
        db.PoolActivities.Add(new PoolActivity
        {
            Title = "De hace dos meses", WorkType = PoolWorkType.Tarea, Complexity = PoolComplexity.Baja,
            Points = 3, Status = PoolActivityStatus.Aceptada, ClaimedByDeveloperId = ana.Id,
            ReviewedAt = new DateTime(Anio, Mes - 2, 10, 12, 0, 0, DateTimeKind.Utc)
        });
        db.SaveChanges();

        var d = await Svc(db, Lider()).DetalleDeDesarrolladorAsync(ana.Id, Anio, Mes);

        var actividad = Assert.Single(d!.ActividadesDelPool);
        Assert.Equal("Corregir el cálculo", actividad.Titulo);
        // Las etiquetas llegan ESCRITAS, no como el nombre del enumerado.
        Assert.Equal("Bug", actividad.TipoTexto);
        Assert.Equal("Alta", actividad.ComplejidadTexto);
        Assert.Equal("Ana", actividad.Quien);
    }

    [Fact]
    public async Task El_detalle_de_un_equipo_dice_de_quien_salio_cada_renglon()
    {
        using var db = TestDb.New();
        var equipo = new Team { Name = "Alfa" };
        db.Teams.Add(equipo);
        db.SaveChanges();

        var ana = Dev(db, "Ana", equipo.Id);
        var beto = Dev(db, "Beto", equipo.Id);
        Dev(db, "De otro equipo");   // no debe salir

        var crit = Criterio(db, PoolSeed.NombreCriterio(PoolWorkType.Tarea));
        Punto(db, ana.Id, crit.Id, 6);
        Punto(db, beto.Id, crit.Id, 4);

        var d = await Svc(db, Lider()).DetalleDeEquipoAsync(equipo.Id, Anio, Mes);

        Assert.Equal("Alfa", d!.Titulo);
        Assert.Equal(10, d.Total);
        Assert.Equal(["Ana", "Beto"], d.Entradas.Select(e => e.Quien).OrderBy(q => q).ToArray());
    }

    [Fact]
    public async Task El_detalle_es_solo_del_lider()
    {
        var (db, ana) = BaseConAna();
        var suyo = UsuarioDePrueba.Como(UserRole.Desarrollador, ana.Id, userId: 3);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, suyo).DetalleDeDesarrolladorAsync(ana.Id, Anio, Mes));
    }

    [Fact]
    public async Task El_detalle_de_alguien_que_ya_no_existe_no_revienta()
    {
        var (db, _) = BaseConAna();

        Assert.Null(await Svc(db, Lider()).DetalleDeDesarrolladorAsync(9999, Anio, Mes));
        Assert.Null(await Svc(db, Lider()).DetalleDeEquipoAsync(9999, Anio, Mes));
    }
}
