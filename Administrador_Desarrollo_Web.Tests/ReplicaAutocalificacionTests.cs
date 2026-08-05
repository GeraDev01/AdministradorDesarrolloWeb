using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Réplica del desarrollador a una autocalificación rechazada.
///
/// Antes un rechazo era el final del camino y el desacuerdo se iba a un chat donde no queda
/// constancia. Lo que se protege aquí es que la discusión quede COMPLETA en la propia entrada: al
/// replicar, el motivo del rechazo tiene que pasar al historial antes de limpiarse, o la vuelta
/// siguiente se leería sin la mitad que la explica.
/// </summary>
public class ReplicaAutocalificacionTests
{
    private static PerformanceScoringService Svc(AppDbContext db) => new(db);

    private static (AppDbContext db, Developer dev, ScoringCriterion crit, CurrentUserContext yo) Entorno()
    {
        var db = TestDb.New();
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();

        var crit = new ScoringCriterion
        {
            Name = "Propusiste una mejora por tu cuenta", Description = "…",
            DefaultPoints = 15, IsActive = true, Scope = CriterionScope.Individual
        };
        db.ScoringCriteria.Add(crit); db.SaveChanges();

        return (db, dev, crit, Ctx.As(UserRole.Desarrollador, developerId: dev.Id, userId: 10));
    }

    /// <summary>Registra una autocalificación y la deja rechazada, como la habría dejado el jefe.</summary>
    private static PointEntry Rechazada(AppDbContext db, Developer dev, ScoringCriterion crit,
                                        CurrentUserContext yo, string motivo = "Eso ya estaba pedido.")
    {
        var (_, _, entrada) = Svc(db).RegistrarAutocalificacion(new PointEntry
        {
            DeveloperId = dev.Id, CriterionId = crit.Id, Year = 2026, Month = 8,
            Comment = "Automaticé el reporte mensual"
        }, yo);

        var fila = db.PointEntries.Single(p => p.Id == entrada!.Id);
        fila.ApprovalStatus = PointApprovalStatus.Rechazado;
        fila.ReviewComment = motivo;
        fila.ReviewedByUserId = 900;
        fila.ReviewedAt = DateTime.UtcNow;
        db.SaveChanges();
        return fila;
    }

    // ── Camino feliz ─────────────────────────────────────────────────────────

    [Fact]
    public void Replicar_DevuelveLaEntradaARevision()
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = Rechazada(db, dev, crit, yo);

        var (ok, mensaje) = Svc(db).Replicar(entrada.Id, "Sí se pidió: está en el correo del 3 de agosto.", yo);

        Assert.True(ok);
        var g = db.PointEntries.AsNoTracking().Single();
        Assert.Equal(PointApprovalStatus.Pendiente, g.ApprovalStatus);
        Assert.Equal(1, g.ReviewRound);
        Assert.Contains("revisión", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Dejar el comentario de rechazo pegado a una entrada que ya volvió a «pendiente» haría creer
    /// que alguien la respondió otra vez. Se limpia, pero solo después de pasarlo al historial.
    /// </summary>
    [Fact]
    public void Replicar_GuardaElMotivoEnElHistorialYLimpiaLaResolucionAnterior()
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = Rechazada(db, dev, crit, yo, "Eso ya estaba pedido.");

        Svc(db).Replicar(entrada.Id, "Está en el correo del 3 de agosto.", yo);

        var g = db.PointEntries.AsNoTracking().Single();
        Assert.Null(g.ReviewComment);
        Assert.Null(g.ReviewedByUserId);
        Assert.Null(g.ReviewedAt);

        Assert.Contains("Eso ya estaba pedido.", g.ReviewHistory);
        Assert.Contains("Está en el correo del 3 de agosto.", g.ReviewHistory);
    }

    [Fact]
    public void Replicar_ElHistorialConservaElOrdenDeLasVueltas()
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = Rechazada(db, dev, crit, yo, "Primer rechazo.");

        Svc(db).Replicar(entrada.Id, "Primera réplica.", yo);

        // El jefe la rechaza otra vez.
        var fila = db.PointEntries.Single(p => p.Id == entrada.Id);
        fila.ApprovalStatus = PointApprovalStatus.Rechazado;
        fila.ReviewComment = "Segundo rechazo.";
        db.SaveChanges();

        Svc(db).Replicar(entrada.Id, "Segunda réplica.", yo);

        var historial = db.PointEntries.AsNoTracking().Single().ReviewHistory!;
        Assert.True(historial.IndexOf("Primer rechazo.", StringComparison.Ordinal)
                  < historial.IndexOf("Primera réplica.", StringComparison.Ordinal));
        Assert.True(historial.IndexOf("Primera réplica.", StringComparison.Ordinal)
                  < historial.IndexOf("Segundo rechazo.", StringComparison.Ordinal));
        Assert.True(historial.IndexOf("Segundo rechazo.", StringComparison.Ordinal)
                  < historial.IndexOf("Segunda réplica.", StringComparison.Ordinal));

        Assert.Equal(2, db.PointEntries.AsNoTracking().Single().ReviewRound);
    }

    /// <summary>La réplica no toca los puntos: eso sigue siendo cosa del criterio y del jefe.</summary>
    [Fact]
    public void Replicar_NoCambiaLosPuntos()
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = Rechazada(db, dev, crit, yo);
        int antes = entrada.Points;

        Svc(db).Replicar(entrada.Id, "Aquí va mi argumento.", yo);

        Assert.Equal(antes, db.PointEntries.AsNoTracking().Single().Points);
    }

    // ── Lo que no se permite ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Replicar_ExigeArgumento(string? argumento)
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = Rechazada(db, dev, crit, yo);

        var (ok, mensaje) = Svc(db).Replicar(entrada.Id, argumento, yo);

        Assert.False(ok);
        Assert.Contains("por qué", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PointApprovalStatus.Rechazado, db.PointEntries.AsNoTracking().Single().ApprovalStatus);
    }

    [Fact]
    public void Replicar_RechazaArgumentosDesmedidos()
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = Rechazada(db, dev, crit, yo);

        var (ok, _) = Svc(db).Replicar(entrada.Id, new string('x', PerformanceScoringService.MaxArgumento + 1), yo);

        Assert.False(ok);
        Assert.Equal(PointApprovalStatus.Rechazado, db.PointEntries.AsNoTracking().Single().ApprovalStatus);
    }

    [Theory]
    [InlineData(PointApprovalStatus.Aprobado)]
    [InlineData(PointApprovalStatus.Pendiente)]
    public void Replicar_SoloSobreLoRechazado(PointApprovalStatus estado)
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = Rechazada(db, dev, crit, yo);

        var fila = db.PointEntries.Single(p => p.Id == entrada.Id);
        fila.ApprovalStatus = estado;
        db.SaveChanges();

        var (ok, _) = Svc(db).Replicar(entrada.Id, "Quiero insistir.", yo);

        Assert.False(ok);
        Assert.Equal(0, db.PointEntries.AsNoTracking().Single().ReviewRound);
    }

    [Fact]
    public void Replicar_NoAplicaALoQueAsignoElAdministrador()
    {
        var (db, dev, crit, yo) = Entorno();
        var asignada = new PointEntry
        {
            DeveloperId = dev.Id, CriterionId = crit.Id, Points = -5, Year = 2026, Month = 8,
            Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Rechazado,
            SubmittedByDeveloperId = null, AssignedByUserId = 900
        };
        db.PointEntries.Add(asignada); db.SaveChanges();

        var (ok, mensaje) = Svc(db).Replicar(asignada.Id, "No estoy de acuerdo.", yo);

        Assert.False(ok);
        Assert.Contains("líder", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Replicar_OtroDesarrolladorNoPuedeReplicarLoAjeno()
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = Rechazada(db, dev, crit, yo);
        var intruso = Ctx.As(UserRole.Desarrollador, developerId: dev.Id + 99, userId: 77);

        Assert.Throws<AuthorizationException>(() => Svc(db).Replicar(entrada.Id, "Yo opino que sí.", intruso));
    }

    [Fact]
    public void Replicar_EntradaInexistente_NoRevienta()
    {
        var (db, _, _, yo) = Entorno();
        var (ok, mensaje) = Svc(db).Replicar(4242, "Argumento.", yo);

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    // ── Historial ────────────────────────────────────────────────────────────

    [Fact]
    public void AnotarEnHistorial_AcumulaSinPisarLoAnterior()
    {
        var e = new PointEntry();

        PerformanceScoringService.AnotarEnHistorial(e, "Primera línea.");
        PerformanceScoringService.AnotarEnHistorial(e, "Segunda línea.");

        Assert.Contains("Primera línea.", e.ReviewHistory);
        Assert.Contains("Segunda línea.", e.ReviewHistory);
        Assert.Equal(2, e.ReviewHistory!.Split('\n').Length);
    }

    /// <summary>
    /// Un ida y vuelta muy largo se recorta por el PRINCIPIO: lo último que se dijo es lo que hace
    /// falta para decidir.
    /// </summary>
    [Fact]
    public void AnotarEnHistorial_SeRecortaConservandoLoMasReciente()
    {
        var e = new PointEntry();
        for (int i = 0; i < 400; i++)
            PerformanceScoringService.AnotarEnHistorial(e, new string('x', 60) + $" n{i}");

        Assert.True(e.ReviewHistory!.Length < 9000, "el historial debería estar acotado");
        Assert.Contains("n399", e.ReviewHistory);       // lo último sigue ahí
        Assert.DoesNotContain(" n0 ", e.ReviewHistory); // lo más viejo se fue
    }
}
