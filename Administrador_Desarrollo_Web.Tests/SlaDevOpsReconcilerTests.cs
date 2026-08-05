using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Cierre automático de los SLA cuyo ticket ya terminó en Azure DevOps.
///
/// El defecto que cubren estas pruebas: un work item movido a <i>Done</i> no cerraba su compromiso.
/// El desarrollador seguía viendo en «Mis SLA» trabajo ya entregado, le seguían llegando
/// recordatorios para comentar un ticket cerrado, y al pasar el plazo se marcaba vencido y se
/// escalaba al jefe como incumplimiento algo que se había entregado a tiempo.
/// </summary>
public class SlaDevOpsReconcilerTests
{
    private const int MiDevId = 21;

    private sealed record Entorno(AppDbContext Db, SlaService Sla);

    private static Entorno Nuevo(UserRole rol = UserRole.Desarrollador, int? devId = MiDevId)
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = MiDevId, FullName = "Yo", IsActive = true, Email = "yo@empresa.com" });
        db.SaveChanges();

        var user = Ctx.As(rol, devId);
        var audit = new AuditService(db, user);
        var settings = new SettingsService(db, audit);
        var devops = new AzureDevOpsService(settings, db, audit, new NotificationService(db));
        return new Entorno(db, new SlaService(db, user, audit, devops));
    }

    /// <summary>Un SLA activo sobre un requerimiento, ligado al ticket dado.</summary>
    private static int NuevoSla(AppDbContext db, int ticket, DateTime dueUtc, int devId = MiDevId)
    {
        var req = new Requirement { Title = "Req", Status = RequirementStatus.EnDesarrollo, CreatedAt = DateTime.UtcNow };
        db.Requirements.Add(req);
        db.SaveChanges();

        var sla = new SlaCommitment
        {
            RequirementId = req.Id, DeveloperId = devId,
            DevOpsTicketExternalId = ticket, DueAtUtc = dueUtc,
            ReminderEveryHours = 24, NextReminderAtUtc = dueUtc.AddHours(-1),
            Status = SlaStatus.Activo, CreatedAt = DateTime.UtcNow
        };
        db.SlaCommitments.Add(sla);
        db.SaveChanges();
        return sla.Id;
    }

    private static void Ticket(AppDbContext db, int externalId, string estado, DateTime? cambiado = null)
    {
        db.DevOpsTickets.Add(new DevOpsTicket
        {
            ExternalId = externalId, Title = "Item", State = estado,
            UpdatedAtExternal = cambiado, SyncedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private static SlaCommitment Leer(AppDbContext db, int slaId) =>
        db.SlaCommitments.AsNoTracking().Single(s => s.Id == slaId);

    // ── La decisión, aislada ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Active")]
    [InlineData("New")]
    [InlineData("Resolved")]     // resuelto no es cerrado: aún puede volver
    [InlineData("In Progress")]
    [InlineData("")]             // sin estado no se supone nada
    public void UnTicketVivo_NoCierraNada(string estado)
    {
        var ahora = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        Assert.Null(SlaDevOpsReconciler.Decidir(estado, ahora.AddDays(1), null, ahora));
    }

    [Theory]
    [InlineData("Done")]
    [InlineData("Closed")]
    [InlineData("Completed")]
    public void CerradoDentroDelPlazo_EsCumplido(string estado)
    {
        var ahora = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(SlaStatus.Cumplido,
            SlaDevOpsReconciler.Decidir(estado, ahora.AddDays(1), ahora.AddHours(-2), ahora));
    }

    [Fact]
    public void CerradoDespuesDelPlazo_EsVencido()
    {
        // Dar por cumplido todo lo que aparece cerrado inflaría el cumplimiento con trabajo tardío.
        var ahora = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(SlaStatus.Vencido,
            SlaDevOpsReconciler.Decidir("Done", ahora.AddDays(-2), ahora.AddHours(-1), ahora));
    }

    [Fact]
    public void SeMideElCierreReal_NoElMomentoEnQueNosEnteramos()
    {
        // Cerrado el día 1, con plazo hasta el 2, pero nadie abre la aplicación hasta el 5. Usar el
        // reloj de ahora convertiría en incumplimiento algo entregado con un día de sobra.
        var cerrado = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var vence   = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var ahora   = new DateTime(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc);

        Assert.Equal(SlaStatus.Cumplido, SlaDevOpsReconciler.Decidir("Done", vence, cerrado, ahora));
    }

    [Fact]
    public void SinFechaDeCierre_SeUsaElAhora()
    {
        var ahora = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(SlaStatus.Cumplido, SlaDevOpsReconciler.Decidir("Done", ahora.AddHours(1), null, ahora));
        Assert.Equal(SlaStatus.Vencido,  SlaDevOpsReconciler.Decidir("Done", ahora.AddHours(-1), null, ahora));
    }

    [Theory]
    [InlineData("Removed")]
    [InlineData("Cancelled")]
    public void DescartadoEnDevOps_EsCancelado_NoIncumplimiento(string estado)
    {
        // Se descartó el trabajo; nadie tenía ya que atenderlo. El reporte de cumplimiento deja los
        // cancelados fuera del porcentaje, que es justo lo correcto.
        var ahora = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(SlaStatus.Cancelado,
            SlaDevOpsReconciler.Decidir(estado, ahora.AddDays(-5), ahora.AddDays(-4), ahora));
    }

    // ── Sobre la base ────────────────────────────────────────────────────────────

    [Fact]
    public void ElTicketEnDone_DejaDeSalirEnMisSla()
    {
        // El síntoma reportado: la pantalla del desarrollador mostraba compromisos de tickets que en
        // DevOps ya estaban en Done.
        var (db, sla) = Nuevo();
        int id = NuevoSla(db, 4821, DateTime.UtcNow.AddDays(2));
        Ticket(db, 4821, "Done", DateTime.UtcNow.AddHours(-3));

        var mios = sla.DeDesarrollador(MiDevId);

        Assert.Empty(mios);
        Assert.Equal(SlaStatus.Cumplido, Leer(db, id).Status);
    }

    [Fact]
    public void AlCerrarse_DejaDePedirComentarios()
    {
        var (db, sla) = Nuevo();
        int id = NuevoSla(db, 4821, DateTime.UtcNow.AddDays(2));
        Ticket(db, 4821, "Closed", DateTime.UtcNow.AddHours(-1));

        sla.DeDesarrollador(MiDevId);

        Assert.Null(Leer(db, id).NextReminderAtUtc);
        Assert.Empty(sla.PendientesDeAviso(MiDevId));   // ni correo ni aviso en la app
    }

    [Fact]
    public void UnTicketEntregadoATiempo_NoSeEscalaComoIncumplimiento()
    {
        // El correo más caro que manda la aplicación: acusa al jefe de que alguien incumplió. Si el
        // ticket se cerró dentro del plazo, ese correo no puede salir.
        var (db, sla) = Nuevo(UserRole.Admin, null);
        var vence   = DateTime.UtcNow.AddHours(-2);          // el plazo ya pasó
        var cerrado = DateTime.UtcNow.AddHours(-6);          // pero se cerró antes de que pasara
        int id = NuevoSla(db, 4821, vence);
        Ticket(db, 4821, "Done", cerrado);

        var aEscalar = sla.RevisarVencimientos();

        Assert.Empty(aEscalar);
        Assert.Equal(SlaStatus.Cumplido, Leer(db, id).Status);
    }

    [Fact]
    public void UnTicketQueSiguioAbierto_SeSigueVenciendo()
    {
        // La otra cara: el arreglo no puede tapar los incumplimientos de verdad.
        var (db, sla) = Nuevo(UserRole.Admin, null);
        int id = NuevoSla(db, 4821, DateTime.UtcNow.AddHours(-2));
        Ticket(db, 4821, "Active", DateTime.UtcNow.AddHours(-1));

        Assert.Single(sla.RevisarVencimientos());
        Assert.Equal(SlaStatus.Vencido, Leer(db, id).Status);
    }

    [Fact]
    public void UnSlaYaVencido_NoSeResucita()
    {
        // Ya se escaló al administrador y hay constancia de ello. Cambiarlo en silencio a cumplido
        // borraría un incumplimiento real; para eso está «Marcar cumplido», que sí deja rastro.
        var (db, sla) = Nuevo(UserRole.Admin, null);
        int id = NuevoSla(db, 4821, DateTime.UtcNow.AddDays(-3));
        db.SlaCommitments.Find(id)!.Status = SlaStatus.Vencido;
        db.SaveChanges();
        Ticket(db, 4821, "Done", DateTime.UtcNow);

        sla.Todos();

        Assert.Equal(SlaStatus.Vencido, Leer(db, id).Status);
    }

    [Fact]
    public void SinTicketSincronizado_NoSeSuponeNada()
    {
        // Puede ser un SLA con el número escrito a mano, o un proyecto que aún no se ha traído.
        // Cerrarlo «por si acaso» sería inventarse que el trabajo terminó.
        var (db, sla) = Nuevo();
        int id = NuevoSla(db, 9999, DateTime.UtcNow.AddDays(2));

        Assert.Single(sla.DeDesarrollador(MiDevId));
        Assert.Equal(SlaStatus.Activo, Leer(db, id).Status);
    }

    [Fact]
    public void UnSlaSinTicketLigado_NiSeMira()
    {
        var (db, sla) = Nuevo();
        var act = new DevActivity { DeveloperId = MiDevId, Title = "Soporte", CreatedAt = DateTime.UtcNow };
        db.DevActivities.Add(act); db.SaveChanges();
        db.SlaCommitments.Add(new SlaCommitment
        {
            ActivityId = act.Id, DeveloperId = MiDevId, DueAtUtc = DateTime.UtcNow.AddDays(1),
            Status = SlaStatus.Activo, CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        Assert.Single(sla.DeDesarrollador(MiDevId));
    }

    [Fact]
    public void RefrescarMisSla_NoTocaLosCompromisosDeOtro()
    {
        // La pantalla de un desarrollador no puede reescribir en silencio los del resto del equipo.
        var (db, sla) = Nuevo();
        db.Developers.Add(new Developer { Id = 22, FullName = "Otro", IsActive = true });
        db.SaveChanges();
        int ajeno = NuevoSla(db, 4821, DateTime.UtcNow.AddDays(2), devId: 22);
        Ticket(db, 4821, "Done", DateTime.UtcNow.AddHours(-1));

        sla.DeDesarrollador(MiDevId);

        Assert.Equal(SlaStatus.Activo, Leer(db, ajeno).Status);
    }

    [Fact]
    public void ElCierreAutomatico_QuedaEnLaBitacoraYEnLasNotas()
    {
        // Un compromiso que cambia de estado sin que nadie lo toque tiene que poder explicarse.
        var (db, sla) = Nuevo();
        int id = NuevoSla(db, 4821, DateTime.UtcNow.AddDays(2));
        Ticket(db, 4821, "Done", DateTime.UtcNow.AddHours(-1));

        sla.DeDesarrollador(MiDevId);

        Assert.Contains("Done", Leer(db, id).Notes);
        Assert.Contains(db.AuditLogs.AsNoTracking().ToList(),
            a => a.EntityType == "SlaCommitment" && (a.Details ?? "").Contains("automáticamente"));
    }

    [Fact]
    public void ReconciliarDosVeces_NoCambiaNadaLaSegunda()
    {
        var (db, sla) = Nuevo(UserRole.Admin, null);
        NuevoSla(db, 4821, DateTime.UtcNow.AddDays(2));
        Ticket(db, 4821, "Done", DateTime.UtcNow.AddHours(-1));

        Assert.Equal(1, sla.ReconciliarConDevOps());
        Assert.Equal(0, sla.ReconciliarConDevOps());
    }
}
