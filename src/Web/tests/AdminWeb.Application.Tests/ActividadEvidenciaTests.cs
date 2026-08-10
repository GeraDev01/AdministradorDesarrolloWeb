using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Evidencia de las actividades autocalificadas: tiempo dedicado, enlace al item de DevOps y la
/// corrección de una entrada mientras siga pendiente.
///
/// Lo que se protege aquí es que la evidencia NO se pueda tocar una vez revisada: el momento en que
/// el jefe aprueba es el que convierte una propuesta en un hecho, y editar después cambiaría
/// aquello sobre lo que se dio el visto bueno.
/// </summary>
public class ActividadEvidenciaTests
{
    private static PerformanceScoringService Svc(AppDbContext db, ICurrentUser quien) => new(db, quien);

    private static (AppDbContext db, Developer dev, ScoringCriterion crit, UsuarioDePrueba yo) Entorno(int puntos = 5)
    {
        var db = TestDb.New();
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();

        var crit = new ScoringCriterion
        {
            Name = "Iniciativa", Description = "Propusiste e implementaste una mejora que nadie pidió.",
            DefaultPoints = puntos, IsActive = true, Scope = CriterionScope.Individual
        };
        db.ScoringCriteria.Add(crit); db.SaveChanges();

        return (db, dev, crit,
            UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id, userId: 10));
    }

    private static PointEntry Borrador(Developer dev, ScoringCriterion crit,
                                       int? minutos = null, string? enlace = null) => new()
    {
        DeveloperId = dev.Id, CriterionId = crit.Id, Year = 2026, Month = 8,
        Comment = "Automaticé el reporte", MinutesSpent = minutos, EvidenceUrl = enlace
    };

    // ── Registrar con evidencia ──────────────────────────────────────────────────

    [Fact]
    public async Task Registrar_GuardaTiempoDedicadoYEnlace()
    {
        var (db, dev, crit, yo) = Entorno();

        var (ok, _, entrada) = await Svc(db, yo).RegistrarAutocalificacionAsync(
            Borrador(dev, crit, minutos: 150, enlace: "https://dev.azure.com/zorroDesierto/Webpro/_git/x/pullrequest/42"));

        Assert.True(ok);
        var g = db.PointEntries.AsNoTracking().Single();
        Assert.Equal(150, g.MinutesSpent);
        Assert.Equal("https://dev.azure.com/zorroDesierto/Webpro/_git/x/pullrequest/42", g.EvidenceUrl);
        Assert.Equal(PointApprovalStatus.Pendiente, g.ApprovalStatus);
        Assert.Equal(dev.Id, g.SubmittedByDeveloperId);
        Assert.NotNull(entrada);
    }

    [Fact]
    public async Task Registrar_SinTiempoNiEnlace_LosDejaVacios()
    {
        var (db, dev, crit, yo) = Entorno();
        var (ok, _, _) = await Svc(db, yo).RegistrarAutocalificacionAsync(Borrador(dev, crit));

        Assert.True(ok);
        var g = db.PointEntries.AsNoTracking().Single();
        Assert.Null(g.MinutesSpent);
        Assert.Null(g.EvidenceUrl);
    }

    /// <summary>Cero minutos y «no lo capturé» son lo mismo: guardarlo como 0 haría que un total
    /// de «0h» pareciera un dato medido en lugar de un campo vacío.</summary>
    [Fact]
    public async Task Registrar_CeroMinutos_SeGuardaComoNulo()
    {
        var (db, dev, crit, yo) = Entorno();
        await Svc(db, yo).RegistrarAutocalificacionAsync(Borrador(dev, crit, minutos: 0));

        Assert.Null(db.PointEntries.AsNoTracking().Single().MinutesSpent);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]   // el jefe lo abre con el navegador
    [InlineData("javascript:alert(1)")]
    [InlineData("dev.azure.com/sin/esquema")]
    [InlineData("no es un enlace")]
    public async Task Registrar_RechazaEnlacesQueNoSonHttp(string enlace)
    {
        var (db, dev, crit, yo) = Entorno();
        var (ok, mensaje, _) = await Svc(db, yo).RegistrarAutocalificacionAsync(Borrador(dev, crit, enlace: enlace));

        Assert.False(ok);
        Assert.Contains("http", mensaje);
        Assert.Empty(db.PointEntries);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(PerformanceScoringService.MaxMinutosDeclarados + 1)]
    public async Task Registrar_RechazaTiemposImposibles(int minutos)
    {
        var (db, dev, crit, yo) = Entorno();
        var (ok, _, _) = await Svc(db, yo).RegistrarAutocalificacionAsync(Borrador(dev, crit, minutos: minutos));

        Assert.False(ok);
        Assert.Empty(db.PointEntries);
    }

    // ── Corregir mientras siga pendiente ─────────────────────────────────────────

    [Fact]
    public async Task Editar_ActualizaEvidenciaMientrasEstaPendiente()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = await Svc(db, yo).RegistrarAutocalificacionAsync(Borrador(dev, crit, minutos: 30));

        var cambios = Borrador(dev, crit, minutos: 90, enlace: "https://dev.azure.com/org/proj/_workitems/edit/7");
        cambios.Comment = "Corrijo: también migré la vista";

        var (ok, _) = await Svc(db, yo).EditarAutocalificacionAsync(entrada!.Id, cambios);

        Assert.True(ok);
        var g = db.PointEntries.AsNoTracking().Single();
        Assert.Equal(90, g.MinutesSpent);
        Assert.Equal("https://dev.azure.com/org/proj/_workitems/edit/7", g.EvidenceUrl);
        Assert.Equal("Corrijo: también migré la vista", g.Comment);
        Assert.Equal(PointApprovalStatus.Pendiente, g.ApprovalStatus);   // sigue sin revisar
    }

    /// <summary>Al cambiar de criterio los puntos se releen del nuevo: el desarrollador elige QUÉ
    /// registra, nunca CUÁNTO vale.</summary>
    [Fact]
    public async Task Editar_ReevaluaLosPuntosDesdeElCriterioNuevo()
    {
        var (db, dev, crit, yo) = Entorno(puntos: 5);
        var otro = new ScoringCriterion { Name = "Cero bugs", DefaultPoints = 12, IsActive = true, Scope = CriterionScope.Individual };
        db.ScoringCriteria.Add(otro); db.SaveChanges();

        var (_, _, entrada) = await Svc(db, yo).RegistrarAutocalificacionAsync(Borrador(dev, crit));
        Assert.Equal(5, entrada!.Points);

        var cambios = Borrador(dev, otro);
        await Svc(db, yo).EditarAutocalificacionAsync(entrada.Id, cambios);

        Assert.Equal(12, db.PointEntries.AsNoTracking().Single().Points);
    }

    /// <summary>
    /// Aprobada se cierra: cambiar después del visto bueno aquello sobre lo que se dio el visto
    /// bueno vaciaría de sentido la aprobación.
    /// </summary>
    [Fact]
    public async Task Editar_SeNiegaUnaVezAprobada()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = await Svc(db, yo).RegistrarAutocalificacionAsync(Borrador(dev, crit, minutos: 30));

        var fila = db.PointEntries.Single(p => p.Id == entrada!.Id);
        fila.ApprovalStatus = PointApprovalStatus.Aprobado;
        db.SaveChanges();

        var (ok, mensaje) = await Svc(db, yo).EditarAutocalificacionAsync(entrada!.Id, Borrador(dev, crit, minutos: 999));

        Assert.False(ok);
        Assert.Contains("aprobada", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(30, db.PointEntries.AsNoTracking().Single().MinutesSpent);   // intacta
    }

    /// <summary>
    /// Una rechazada SÍ se corrige: suele estarlo por algo que se puede arreglar, y obligar a
    /// registrarla de nuevo hacía perder la captura, el comentario y el hilo de la conversación.
    /// </summary>
    [Fact]
    public async Task Editar_SePuedeAunqueEsteRechazada()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = await Svc(db, yo).RegistrarAutocalificacionAsync(Borrador(dev, crit, minutos: 30));

        var fila = db.PointEntries.Single(p => p.Id == entrada!.Id);
        fila.ApprovalStatus = PointApprovalStatus.Rechazado;
        fila.ReviewComment = "Falta el enlace al PR.";
        db.SaveChanges();

        var cambios = Borrador(dev, crit, minutos: 45, enlace: "https://dev.azure.com/org/_git/x/pullrequest/9");
        var (ok, mensaje) = await Svc(db, yo).EditarAutocalificacionAsync(entrada!.Id, cambios);

        Assert.True(ok);
        var g = db.PointEntries.AsNoTracking().Single();
        Assert.Equal(45, g.MinutesSpent);
        Assert.Equal("https://dev.azure.com/org/_git/x/pullrequest/9", g.EvidenceUrl);

        // Corregir NO la devuelve a revisión: para eso está Replicar, y el mensaje lo dice.
        Assert.Equal(PointApprovalStatus.Rechazado, g.ApprovalStatus);
        Assert.Contains("Replicar", mensaje);
    }

    /// <summary>Los puntos que reparte el jefe no son una propuesta del desarrollador: no se editan
    /// por esta vía aunque estuvieran pendientes.</summary>
    [Fact]
    public async Task Editar_SeNiegaSiLaAsignoElAdministrador()
    {
        var (db, dev, crit, yo) = Entorno();
        var asignada = new PointEntry
        {
            DeveloperId = dev.Id, CriterionId = crit.Id, Points = 5, Year = 2026, Month = 8,
            Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Pendiente,
            SubmittedByDeveloperId = null, AssignedByUserId = 1
        };
        db.PointEntries.Add(asignada); db.SaveChanges();

        var (ok, _) = await Svc(db, yo).EditarAutocalificacionAsync(asignada.Id, Borrador(dev, crit, minutos: 60));

        Assert.False(ok);
        Assert.Null(db.PointEntries.AsNoTracking().Single().MinutesSpent);
    }

    [Fact]
    public async Task Editar_OtroDesarrolladorNoPuedeTocarLaAjena()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = await Svc(db, yo).RegistrarAutocalificacionAsync(Borrador(dev, crit));

        var intruso = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id + 99, userId: 77);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            Svc(db, intruso).EditarAutocalificacionAsync(entrada!.Id, Borrador(dev, crit, minutos: 60)));
    }

    [Fact]
    public async Task Editar_RechazaEnlaceInvalidoSinTocarLaEntrada()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = await Svc(db, yo).RegistrarAutocalificacionAsync(
            Borrador(dev, crit, minutos: 30, enlace: "https://dev.azure.com/ok"));

        var (ok, _) = await Svc(db, yo).EditarAutocalificacionAsync(
            entrada!.Id, Borrador(dev, crit, minutos: 45, enlace: "file:///etc/passwd"));

        Assert.False(ok);
        var g = db.PointEntries.AsNoTracking().Single();
        Assert.Equal(30, g.MinutesSpent);
        Assert.Equal("https://dev.azure.com/ok", g.EvidenceUrl);
    }

    // ── Contabilización del tiempo ───────────────────────────────────────────────

    /// <summary>
    /// El tiempo se acumula separando aprobado de pendiente, y lo RECHAZADO no suma: si el jefe no
    /// reconoció la actividad, su tiempo tampoco debe engrosar el total del desarrollador.
    /// </summary>
    [Fact]
    public async Task Totales_AcumulanMinutosPorEstadoYExcluyenLoRechazado()
    {
        var (db, dev, crit, yo) = Entorno();

        void Add(int minutos, PointApprovalStatus estado) => db.PointEntries.Add(new PointEntry
        {
            DeveloperId = dev.Id, CriterionId = crit.Id, Points = 5, Year = 2026, Month = 8,
            Date = DateTime.UtcNow, ApprovalStatus = estado, MinutesSpent = minutos
        });
        Add(120, PointApprovalStatus.Aprobado);
        Add(60,  PointApprovalStatus.Aprobado);
        Add(45,  PointApprovalStatus.Pendiente);
        Add(500, PointApprovalStatus.Rechazado);   // NO cuenta
        db.PointEntries.Add(new PointEntry     // sin tiempo capturado: no rompe la suma
        {
            DeveloperId = dev.Id, CriterionId = crit.Id, Points = 5, Year = 2026, Month = 8,
            Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Aprobado, MinutesSpent = null
        });
        db.SaveChanges();

        var t = await Svc(db, yo).DevMonthlyTotalsAsync(dev.Id, 2026, 8);

        Assert.Equal(180, t.MinutesApproved);
        Assert.Equal(45, t.MinutesPending);
        Assert.Equal(225, await Svc(db, yo).MinutosDeclaradosAsync(dev.Id, 2026, 8));
    }

    [Fact]
    public async Task Totales_NoMezclanPeriodos()
    {
        var (db, dev, crit, yo) = Entorno();
        db.PointEntries.AddRange(
            new PointEntry { DeveloperId = dev.Id, CriterionId = crit.Id, Points = 5, Year = 2026, Month = 8, Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Aprobado, MinutesSpent = 100 },
            new PointEntry { DeveloperId = dev.Id, CriterionId = crit.Id, Points = 5, Year = 2026, Month = 7, Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Aprobado, MinutesSpent = 999 });
        db.SaveChanges();

        Assert.Equal(100, await Svc(db, yo).MinutosDeclaradosAsync(dev.Id, 2026, 8));
    }

    // ── Normalización del enlace ─────────────────────────────────────────────────

    [Fact]
    public void NormalizarEnlace_VacioEsValidoYQuedaNulo()
    {
        foreach (var vacio in new[] { null, "", "   " })
        {
            var (ok, _, url) = PerformanceScoringService.NormalizarEnlace(vacio);
            Assert.True(ok);
            Assert.Null(url);
        }
    }

    [Fact]
    public void NormalizarEnlace_RecortaEspaciosDelPegado()
    {
        var (ok, _, url) = PerformanceScoringService.NormalizarEnlace("  https://dev.azure.com/org/_git/repo  ");
        Assert.True(ok);
        Assert.Equal("https://dev.azure.com/org/_git/repo", url);
    }

    [Fact]
    public void NormalizarEnlace_RechazaLoDemasiadoLargo()
    {
        var (ok, _, _) = PerformanceScoringService.NormalizarEnlace("https://x.com/" + new string('a', 600));
        Assert.False(ok);
    }
}
