using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

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
    private static PerformanceScoringService Svc(AppDbContext db) => new(db);

    private static (AppDbContext db, Developer dev, ScoringCriterion crit, CurrentUserContext yo) Entorno(int puntos = 5)
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

        return (db, dev, crit, Ctx.As(UserRole.Desarrollador, developerId: dev.Id, userId: 10));
    }

    private static PointEntry Borrador(Developer dev, ScoringCriterion crit,
                                       int? minutos = null, string? enlace = null) => new()
    {
        DeveloperId = dev.Id, CriterionId = crit.Id, Year = 2026, Month = 8,
        Comment = "Automaticé el reporte", MinutesSpent = minutos, EvidenceUrl = enlace
    };

    // ── Registrar con evidencia ──────────────────────────────────────────────────

    [Fact]
    public void Registrar_GuardaTiempoDedicadoYEnlace()
    {
        var (db, dev, crit, yo) = Entorno();

        var (ok, _, entrada) = Svc(db).RegistrarAutocalificacion(
            Borrador(dev, crit, minutos: 150, enlace: "https://dev.azure.com/zorroDesierto/Webpro/_git/x/pullrequest/42"), yo);

        Assert.True(ok);
        var g = db.PointEntries.AsNoTracking().Single();
        Assert.Equal(150, g.MinutesSpent);
        Assert.Equal("https://dev.azure.com/zorroDesierto/Webpro/_git/x/pullrequest/42", g.EvidenceUrl);
        Assert.Equal(PointApprovalStatus.Pendiente, g.ApprovalStatus);
        Assert.Equal(dev.Id, g.SubmittedByDeveloperId);
        Assert.NotNull(entrada);
    }

    [Fact]
    public void Registrar_SinTiempoNiEnlace_LosDejaVacios()
    {
        var (db, dev, crit, yo) = Entorno();
        var (ok, _, _) = Svc(db).RegistrarAutocalificacion(Borrador(dev, crit), yo);

        Assert.True(ok);
        var g = db.PointEntries.AsNoTracking().Single();
        Assert.Null(g.MinutesSpent);
        Assert.Null(g.EvidenceUrl);
    }

    /// <summary>Cero minutos y «no lo capturé» son lo mismo: guardarlo como 0 haría que un total
    /// de «0h» pareciera un dato medido en lugar de un campo vacío.</summary>
    [Fact]
    public void Registrar_CeroMinutos_SeGuardaComoNulo()
    {
        var (db, dev, crit, yo) = Entorno();
        Svc(db).RegistrarAutocalificacion(Borrador(dev, crit, minutos: 0), yo);

        Assert.Null(db.PointEntries.AsNoTracking().Single().MinutesSpent);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]   // el jefe lo abre con UseShellExecute
    [InlineData("javascript:alert(1)")]
    [InlineData("dev.azure.com/sin/esquema")]
    [InlineData("no es un enlace")]
    public void Registrar_RechazaEnlacesQueNoSonHttp(string enlace)
    {
        var (db, dev, crit, yo) = Entorno();
        var (ok, mensaje, _) = Svc(db).RegistrarAutocalificacion(Borrador(dev, crit, enlace: enlace), yo);

        Assert.False(ok);
        Assert.Contains("http", mensaje);
        Assert.Empty(db.PointEntries);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(PerformanceScoringService.MaxMinutosDeclarados + 1)]
    public void Registrar_RechazaTiemposImposibles(int minutos)
    {
        var (db, dev, crit, yo) = Entorno();
        var (ok, _, _) = Svc(db).RegistrarAutocalificacion(Borrador(dev, crit, minutos: minutos), yo);

        Assert.False(ok);
        Assert.Empty(db.PointEntries);
    }

    // ── Corregir mientras siga pendiente ─────────────────────────────────────────

    [Fact]
    public void Editar_ActualizaEvidenciaMientrasEstaPendiente()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = Svc(db).RegistrarAutocalificacion(Borrador(dev, crit, minutos: 30), yo);

        var cambios = Borrador(dev, crit, minutos: 90, enlace: "https://dev.azure.com/org/proj/_workitems/edit/7");
        cambios.Comment = "Corrijo: también migré la vista";

        var (ok, _) = Svc(db).EditarAutocalificacion(entrada!.Id, cambios, yo);

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
    public void Editar_ReevaluaLosPuntosDesdeElCriterioNuevo()
    {
        var (db, dev, crit, yo) = Entorno(puntos: 5);
        var otro = new ScoringCriterion { Name = "Cero bugs", DefaultPoints = 12, IsActive = true, Scope = CriterionScope.Individual };
        db.ScoringCriteria.Add(otro); db.SaveChanges();

        var (_, _, entrada) = Svc(db).RegistrarAutocalificacion(Borrador(dev, crit), yo);
        Assert.Equal(5, entrada!.Points);

        var cambios = Borrador(dev, otro);
        Svc(db).EditarAutocalificacion(entrada.Id, cambios, yo);

        Assert.Equal(12, db.PointEntries.AsNoTracking().Single().Points);
    }

    /// <summary>
    /// Aprobada se cierra: cambiar después del visto bueno aquello sobre lo que se dio el visto
    /// bueno vaciaría de sentido la aprobación.
    /// </summary>
    [Fact]
    public void Editar_SeNiegaUnaVezAprobada()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = Svc(db).RegistrarAutocalificacion(Borrador(dev, crit, minutos: 30), yo);

        var fila = db.PointEntries.Single(p => p.Id == entrada!.Id);
        fila.ApprovalStatus = PointApprovalStatus.Aprobado;
        db.SaveChanges();

        var (ok, mensaje) = Svc(db).EditarAutocalificacion(entrada!.Id, Borrador(dev, crit, minutos: 999), yo);

        Assert.False(ok);
        Assert.Contains("aprobada", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(30, db.PointEntries.AsNoTracking().Single().MinutesSpent);   // intacta
    }

    /// <summary>
    /// Una rechazada SÍ se corrige: suele estarlo por algo que se puede arreglar, y obligar a
    /// registrarla de nuevo hacía perder la captura, el comentario y el hilo de la conversación.
    /// </summary>
    [Fact]
    public void Editar_SePuedeAunqueEsteRechazada()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = Svc(db).RegistrarAutocalificacion(Borrador(dev, crit, minutos: 30), yo);

        var fila = db.PointEntries.Single(p => p.Id == entrada!.Id);
        fila.ApprovalStatus = PointApprovalStatus.Rechazado;
        fila.ReviewComment = "Falta el enlace al PR.";
        db.SaveChanges();

        var cambios = Borrador(dev, crit, minutos: 45, enlace: "https://dev.azure.com/org/_git/x/pullrequest/9");
        var (ok, mensaje) = Svc(db).EditarAutocalificacion(entrada!.Id, cambios, yo);

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
    public void Editar_SeNiegaSiLaAsignoElAdministrador()
    {
        var (db, dev, crit, yo) = Entorno();
        var asignada = new PointEntry
        {
            DeveloperId = dev.Id, CriterionId = crit.Id, Points = 5, Year = 2026, Month = 8,
            Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Pendiente,
            SubmittedByDeveloperId = null, AssignedByUserId = 1
        };
        db.PointEntries.Add(asignada); db.SaveChanges();

        var (ok, _) = Svc(db).EditarAutocalificacion(asignada.Id, Borrador(dev, crit, minutos: 60), yo);

        Assert.False(ok);
        Assert.Null(db.PointEntries.AsNoTracking().Single().MinutesSpent);
    }

    [Fact]
    public void Editar_OtroDesarrolladorNoPuedeTocarLaAjena()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = Svc(db).RegistrarAutocalificacion(Borrador(dev, crit), yo);

        var intruso = Ctx.As(UserRole.Desarrollador, developerId: dev.Id + 99, userId: 77);

        Assert.Throws<AuthorizationException>(() =>
            Svc(db).EditarAutocalificacion(entrada!.Id, Borrador(dev, crit, minutos: 60), intruso));
    }

    [Fact]
    public void Editar_RechazaEnlaceInvalidoSinTocarLaEntrada()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = Svc(db).RegistrarAutocalificacion(
            Borrador(dev, crit, minutos: 30, enlace: "https://dev.azure.com/ok"), yo);

        var (ok, _) = Svc(db).EditarAutocalificacion(
            entrada!.Id, Borrador(dev, crit, minutos: 45, enlace: "file:///etc/passwd"), yo);

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
    public void Totales_AcumulanMinutosPorEstadoYExcluyenLoRechazado()
    {
        var (db, dev, crit, _) = Entorno();

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

        var t = Svc(db).DevMonthlyTotals(dev.Id, 2026, 8);

        Assert.Equal(180, t.MinutesApproved);
        Assert.Equal(45, t.MinutesPending);
        Assert.Equal(225, Svc(db).MinutosDeclarados(dev.Id, 2026, 8));
    }

    [Fact]
    public void Totales_NoMezclanPeriodos()
    {
        var (db, dev, crit, _) = Entorno();
        db.PointEntries.AddRange(
            new PointEntry { DeveloperId = dev.Id, CriterionId = crit.Id, Points = 5, Year = 2026, Month = 8, Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Aprobado, MinutesSpent = 100 },
            new PointEntry { DeveloperId = dev.Id, CriterionId = crit.Id, Points = 5, Year = 2026, Month = 7, Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Aprobado, MinutesSpent = 999 });
        db.SaveChanges();

        Assert.Equal(100, Svc(db).MinutosDeclarados(dev.Id, 2026, 8));
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
