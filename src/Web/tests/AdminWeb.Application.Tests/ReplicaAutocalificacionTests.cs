using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Réplica del desarrollador a una autocalificación rechazada.
///
/// Antes un rechazo era el final del camino y el desacuerdo se iba a un chat donde no queda
/// constancia. Lo que se protege aquí es que la discusión quede COMPLETA en la propia entrada: al
/// replicar, el motivo del rechazo tiene que pasar al historial antes de limpiarse, o la vuelta
/// siguiente se leería sin la mitad que la explica.
///
/// <para>REPLICAR SIGUIÓ VIVO cuando la autocalificación se apagó, y no por inercia: apagar el
/// registro no borra la cola: hay entradas rechazadas ahí fuera cuyo dueño todavía no ha podido
/// contestar, y cerrarles la puerta sería dejar la última palabra en manos de quien rechazó. Lo
/// único que hubo que cambiar aquí es de dónde sale la entrada de partida.</para>
/// </summary>
public class ReplicaAutocalificacionTests
{
    private static PerformanceScoringService Svc(AppDbContext db, ICurrentUser quien) => new(db, quien);

    private static (AppDbContext db, Developer dev, ScoringCriterion crit, UsuarioDePrueba yo) Entorno()
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

        return (db, dev, crit,
            UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id, userId: 10));
    }

    /// <summary>
    /// Una autocalificación rechazada, como la habría dejado el jefe.
    ///
    /// <para>La entrada se SIEMBRA A MANO. Hasta el corte de la puerta única la registraba
    /// <c>RegistrarAutocalificacionAsync</c>, que ya no paga; lo que hay aquí abajo son los mismos
    /// campos que ponía —puntos releídos del criterio, <c>SubmittedByDeveloperId</c> puesto y
    /// <c>AssignedByUserId</c> nulo—, que son justo los que replicar mira para decidir si la entrada
    /// es del desarrollador. Y describe además lo que de verdad hay en la base de cualquier
    /// instalación el día del corte: filas nacidas del camino viejo, esperando respuesta.</para>
    /// </summary>
    private static async Task<PointEntry> RechazadaAsync(AppDbContext db, Developer dev, ScoringCriterion crit,
                                                         UsuarioDePrueba yo, string motivo = "Eso ya estaba pedido.")
    {
        _ = yo;   // se conserva en la firma: quien lee la llamada tiene que ver de quién es la entrada

        var fila = new PointEntry
        {
            DeveloperId = dev.Id, CriterionId = crit.Id, Year = 2026, Month = 8,
            Comment = "Automaticé el reporte mensual",
            Points = crit.DefaultPoints,
            Date = DateTime.UtcNow,
            SubmittedByDeveloperId = dev.Id,
            AssignedByUserId = null,
            ApprovalStatus = PointApprovalStatus.Rechazado,
            ReviewComment = motivo,
            ReviewedByUserId = 900,
            ReviewedAt = DateTime.UtcNow
        };
        db.PointEntries.Add(fila);
        await db.SaveChangesAsync();
        return fila;
    }

    // ── Camino feliz ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Replicar_DevuelveLaEntradaARevision()
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = await RechazadaAsync(db, dev, crit, yo);

        var (ok, mensaje) = await Svc(db, yo).ReplicarAsync(entrada.Id, "Sí se pidió: está en el correo del 3 de agosto.");

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
    public async Task Replicar_GuardaElMotivoEnElHistorialYLimpiaLaResolucionAnterior()
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = await RechazadaAsync(db, dev, crit, yo, "Eso ya estaba pedido.");

        await Svc(db, yo).ReplicarAsync(entrada.Id, "Está en el correo del 3 de agosto.");

        var g = db.PointEntries.AsNoTracking().Single();
        Assert.Null(g.ReviewComment);
        Assert.Null(g.ReviewedByUserId);
        Assert.Null(g.ReviewedAt);

        Assert.Contains("Eso ya estaba pedido.", g.ReviewHistory);
        Assert.Contains("Está en el correo del 3 de agosto.", g.ReviewHistory);
    }

    [Fact]
    public async Task Replicar_ElHistorialConservaElOrdenDeLasVueltas()
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = await RechazadaAsync(db, dev, crit, yo, "Primer rechazo.");

        await Svc(db, yo).ReplicarAsync(entrada.Id, "Primera réplica.");

        // El jefe la rechaza otra vez.
        var fila = db.PointEntries.Single(p => p.Id == entrada.Id);
        fila.ApprovalStatus = PointApprovalStatus.Rechazado;
        fila.ReviewComment = "Segundo rechazo.";
        db.SaveChanges();

        await Svc(db, yo).ReplicarAsync(entrada.Id, "Segunda réplica.");

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
    public async Task Replicar_NoCambiaLosPuntos()
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = await RechazadaAsync(db, dev, crit, yo);
        int antes = entrada.Points;

        await Svc(db, yo).ReplicarAsync(entrada.Id, "Aquí va mi argumento.");

        Assert.Equal(antes, db.PointEntries.AsNoTracking().Single().Points);
    }

    // ── Lo que no se permite ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public async Task Replicar_ExigeArgumento(string? argumento)
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = await RechazadaAsync(db, dev, crit, yo);

        var (ok, mensaje) = await Svc(db, yo).ReplicarAsync(entrada.Id, argumento);

        Assert.False(ok);
        Assert.Contains("por qué", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PointApprovalStatus.Rechazado, db.PointEntries.AsNoTracking().Single().ApprovalStatus);
    }

    [Fact]
    public async Task Replicar_RechazaArgumentosDesmedidos()
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = await RechazadaAsync(db, dev, crit, yo);

        var (ok, _) = await Svc(db, yo).ReplicarAsync(entrada.Id, new string('x', PerformanceScoringService.MaxArgumento + 1));

        Assert.False(ok);
        Assert.Equal(PointApprovalStatus.Rechazado, db.PointEntries.AsNoTracking().Single().ApprovalStatus);
    }

    [Theory]
    [InlineData(PointApprovalStatus.Aprobado)]
    [InlineData(PointApprovalStatus.Pendiente)]
    public async Task Replicar_SoloSobreLoRechazado(PointApprovalStatus estado)
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = await RechazadaAsync(db, dev, crit, yo);

        var fila = db.PointEntries.Single(p => p.Id == entrada.Id);
        fila.ApprovalStatus = estado;
        db.SaveChanges();

        var (ok, _) = await Svc(db, yo).ReplicarAsync(entrada.Id, "Quiero insistir.");

        Assert.False(ok);
        Assert.Equal(0, db.PointEntries.AsNoTracking().Single().ReviewRound);
    }

    [Fact]
    public async Task Replicar_NoAplicaALoQueAsignoElAdministrador()
    {
        var (db, dev, crit, yo) = Entorno();
        var asignada = new PointEntry
        {
            DeveloperId = dev.Id, CriterionId = crit.Id, Points = -5, Year = 2026, Month = 8,
            Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Rechazado,
            SubmittedByDeveloperId = null, AssignedByUserId = 900
        };
        db.PointEntries.Add(asignada); db.SaveChanges();

        var (ok, mensaje) = await Svc(db, yo).ReplicarAsync(asignada.Id, "No estoy de acuerdo.");

        Assert.False(ok);
        Assert.Contains("líder", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Replicar_OtroDesarrolladorNoPuedeReplicarLoAjeno()
    {
        var (db, dev, crit, yo) = Entorno();
        var entrada = await RechazadaAsync(db, dev, crit, yo);
        var intruso = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id + 99, userId: 77);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, intruso).ReplicarAsync(entrada.Id, "Yo opino que sí."));
    }

    [Fact]
    public async Task Replicar_EntradaInexistente_NoRevienta()
    {
        var (db, _, _, yo) = Entorno();
        var (ok, mensaje) = await Svc(db, yo).ReplicarAsync(4242, "Argumento.");

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
