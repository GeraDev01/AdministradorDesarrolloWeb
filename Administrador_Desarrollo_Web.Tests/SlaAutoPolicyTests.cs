using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// SLA automático por prioridad de DevOps: la política pura (parseo/saneo/resolución) y su aplicación
/// al materializar tickets — se crea el compromiso solo para tickets nuevos cuya prioridad tenga una
/// política ACTIVA, con el plazo y el recordatorio configurados.
/// </summary>
public class SlaAutoPolicyTests
{
    // ── Política pura (sin base ni red) ──────────────────────────────────────────

    [Fact]
    public void Parse_SinJson_DevuelveCuatroPoliticasDesactivadas()
    {
        var pols = SlaPolicyStore.Parse(null);
        Assert.Equal(4, pols.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, pols.Select(p => p.Priority));
        Assert.All(pols, p => Assert.False(p.Enabled));
    }

    [Fact]
    public void SerializeParse_HaceRoundTrip()
    {
        var origen = new[]
        {
            new SlaPolicy(1, true, 4, 2),
            new SlaPolicy(2, false, 8, 24),
            new SlaPolicy(3, true, 24, 0),
            new SlaPolicy(4, false, 72, 48),
        };
        var vuelta = SlaPolicyStore.Parse(SlaPolicyStore.Serialize(origen));
        Assert.Equal(origen.Length, vuelta.Count);
        foreach (var o in origen)
        {
            var v = vuelta.Single(x => x.Priority == o.Priority);
            Assert.Equal(o.Enabled, v.Enabled);
            Assert.Equal(o.Hours, v.Hours);
            Assert.Equal(o.ReminderEveryHours, v.ReminderEveryHours);
        }
    }

    [Fact]
    public void Parse_JsonCorrupto_CaeADefaults()
    {
        var pols = SlaPolicyStore.Parse("{ esto no es válido ]");
        Assert.Equal(4, pols.Count);
        Assert.All(pols, p => Assert.False(p.Enabled));
    }

    [Fact]
    public void Parse_RellenaPrioridadesFaltantes()
    {
        // Solo se guardó la P1; las demás deben aparecer con sus valores por defecto.
        var json = SlaPolicyStore.Serialize(new[] { new SlaPolicy(1, true, 5, 1) });
        var pols = SlaPolicyStore.Parse(json);
        Assert.Equal(4, pols.Count);
        Assert.True(pols.Single(p => p.Priority == 1).Enabled);
        Assert.Contains(pols, p => p.Priority == 4);
    }

    [Fact]
    public void Sanear_AcotaHorasYRecordatorio()
    {
        var json = SlaPolicyStore.Serialize(new[] { new SlaPolicy(1, true, 0, 5000) });
        var p1 = SlaPolicyStore.Parse(json).Single(p => p.Priority == 1);
        Assert.True(p1.Hours >= 1);          // 0 → mínimo válido
        Assert.True(p1.ReminderEveryHours <= 720);
    }

    [Fact]
    public void Resolver_DevuelvePoliticaActivaSegunPrioridadTexto()
    {
        var pols = new[] { new SlaPolicy(1, true, 4, 2), new SlaPolicy(2, false, 8, 24) };
        Assert.NotNull(SlaPolicyStore.Resolver(pols, "1"));
        Assert.Equal(4, SlaPolicyStore.Resolver(pols, "1")!.Value.Hours);
        Assert.Null(SlaPolicyStore.Resolver(pols, "2"));    // desactivada
        Assert.Null(SlaPolicyStore.Resolver(pols, "9"));    // sin política
        Assert.Null(SlaPolicyStore.Resolver(pols, ""));     // no numérica
        Assert.Null(SlaPolicyStore.Resolver(pols, null));
    }

    // ── Aplicación al materializar tickets ───────────────────────────────────────

    private static (AzureDevOpsService svc, AppDbContext db, SettingsService settings) Nuevo()
    {
        var db = TestDb.New();
        var user = Ctx.As(UserRole.Admin);
        var settings = new SettingsService(db, new AuditService(db, user));
        var svc = new AzureDevOpsService(settings, db, new AuditService(db, user), new NotificationService(db));
        return (svc, db, settings);
    }

    private static Developer SeedDev(AppDbContext db, string email)
    {
        var d = new Developer { FullName = "Angel Palma", Email = email, IsActive = true };
        db.Developers.Add(d); db.SaveChanges();
        return d;
    }

    private static void SeedTicket(AppDbContext db, int ext, string state, string email, string priority) =>
        db.DevOpsTickets.Add(new DevOpsTicket
        {
            ExternalId = ext, Title = $"WI {ext}", WorkItemType = "Task",
            State = state, Priority = priority, AssignedTo = "Angel Palma",
            AssignedToUniqueName = email, Url = $"http://d/{ext}"
        });

    private static void GuardarPoliticas(SettingsService settings, params SlaPolicy[] pols) =>
        settings.Set(SlaPolicyStore.SettingKey, SlaPolicyStore.Serialize(pols));

    [Fact]
    public void AutoSla_TicketNuevoConPrioridadYPoliticaActiva_CreaCompromiso()
    {
        var (svc, db, settings) = Nuevo();
        var dev = SeedDev(db, "a@x.com");
        SeedTicket(db, 100, "Active", "a@x.com", "1");
        db.SaveChanges();
        GuardarPoliticas(settings, new SlaPolicy(1, true, 4, 2));

        svc.MaterializarAsignados(dev.Id);

        var sla = Assert.Single(db.SlaCommitments);
        var req = db.Requirements.Single();
        Assert.Equal(req.Id, sla.RequirementId);
        Assert.Null(sla.ActivityId);
        Assert.Equal(dev.Id, sla.DeveloperId);
        Assert.Equal(100, sla.DevOpsTicketExternalId);
        Assert.Equal(SlaStatus.Activo, sla.Status);
        Assert.Equal(2, sla.ReminderEveryHours);
        Assert.Equal(4, (sla.DueAtUtc - sla.CreatedAt).TotalHours, 3);        // plazo = 4 h
        Assert.Equal(2, (sla.NextReminderAtUtc!.Value - sla.CreatedAt).TotalHours, 3); // recordar en 2 h
        Assert.Contains("automático", sla.Notes!);
    }

    [Fact]
    public void AutoSla_PoliticaDesactivada_NoCreaNada()
    {
        var (svc, db, settings) = Nuevo();
        var dev = SeedDev(db, "a@x.com");
        SeedTicket(db, 100, "Active", "a@x.com", "1");
        db.SaveChanges();
        GuardarPoliticas(settings, new SlaPolicy(1, false, 4, 2));

        svc.MaterializarAsignados(dev.Id);

        Assert.Empty(db.SlaCommitments);
        Assert.Single(db.Requirements);   // el requerimiento sí se crea igual
    }

    [Fact]
    public void AutoSla_PrioridadSinPolitica_NoCreaNada()
    {
        var (svc, db, settings) = Nuevo();
        var dev = SeedDev(db, "a@x.com");
        SeedTicket(db, 100, "Active", "a@x.com", "3");   // prioridad 3
        db.SaveChanges();
        GuardarPoliticas(settings, new SlaPolicy(1, true, 4, 2));   // solo P1 activa

        svc.MaterializarAsignados(dev.Id);

        Assert.Empty(db.SlaCommitments);
    }

    [Fact]
    public void AutoSla_PrioridadVacia_NoCreaNada()
    {
        var (svc, db, settings) = Nuevo();
        var dev = SeedDev(db, "a@x.com");
        SeedTicket(db, 100, "Active", "a@x.com", "");    // sin prioridad numérica
        db.SaveChanges();
        GuardarPoliticas(settings,
            new SlaPolicy(1, true, 4, 2), new SlaPolicy(2, true, 8, 4),
            new SlaPolicy(3, true, 24, 8), new SlaPolicy(4, true, 72, 24));

        svc.MaterializarAsignados(dev.Id);

        Assert.Empty(db.SlaCommitments);
    }

    [Fact]
    public void AutoSla_SinPoliticasConfiguradas_NoCreaNada()
    {
        var (svc, db, _) = Nuevo();
        var dev = SeedDev(db, "a@x.com");
        SeedTicket(db, 100, "Active", "a@x.com", "1");
        db.SaveChanges();
        // No se guarda ninguna política (todo por defecto = desactivado).

        svc.MaterializarAsignados(dev.Id);

        Assert.Empty(db.SlaCommitments);
    }

    [Fact]
    public void AutoSla_EsIdempotente_NoDuplicaAlReSincronizar()
    {
        var (svc, db, settings) = Nuevo();
        var dev = SeedDev(db, "a@x.com");
        SeedTicket(db, 100, "Active", "a@x.com", "1");
        db.SaveChanges();
        GuardarPoliticas(settings, new SlaPolicy(1, true, 4, 2));

        svc.MaterializarAsignados(dev.Id);
        svc.MaterializarAsignados(dev.Id);   // re-entrar a Mis Asignaciones

        Assert.Single(db.SlaCommitments);
    }

    [Fact]
    public void AutoSla_Backfill_AsignacionExistenteSinSla_RecibeSlaAlActivarPolitica()
    {
        var (svc, db, settings) = Nuevo();
        var dev = SeedDev(db, "a@x.com");
        SeedTicket(db, 100, "Active", "a@x.com", "1");
        db.SaveChanges();

        // Primera pasada SIN política activa: se materializa el requerimiento pero no se crea SLA.
        svc.MaterializarAsignados(dev.Id);
        Assert.Empty(db.SlaCommitments);
        Assert.Single(db.Requirements);

        // El administrador activa la política y se vuelve a sincronizar: el backfill cubre la
        // asignación YA existente (no solo las nuevas).
        GuardarPoliticas(settings, new SlaPolicy(1, true, 4, 2));
        svc.MaterializarAsignados(dev.Id);

        var sla = Assert.Single(db.SlaCommitments);
        Assert.Equal(db.Requirements.Single().Id, sla.RequirementId);
        Assert.Equal(100, sla.DevOpsTicketExternalId);
        Assert.Equal(SlaStatus.Activo, sla.Status);
    }

    [Fact]
    public void AutoSla_Backfill_VariasAsignaciones_SoloLasDePrioridadConPolitica()
    {
        var (svc, db, settings) = Nuevo();
        var dev = SeedDev(db, "a@x.com");
        SeedTicket(db, 100, "Active", "a@x.com", "1");   // política activa
        SeedTicket(db, 101, "Active", "a@x.com", "3");   // sin política
        SeedTicket(db, 102, "Active", "a@x.com", "");    // sin prioridad
        db.SaveChanges();
        GuardarPoliticas(settings, new SlaPolicy(1, true, 4, 2));

        svc.MaterializarAsignados(dev.Id);

        var sla = Assert.Single(db.SlaCommitments);
        Assert.Equal(100, sla.DevOpsTicketExternalId);
    }

    [Fact]
    public void AutoSla_TicketPreexistente_NoResucitaSlaCancelado()
    {
        var (svc, db, settings) = Nuevo();
        var dev = SeedDev(db, "a@x.com");
        SeedTicket(db, 100, "Active", "a@x.com", "1");
        db.SaveChanges();
        GuardarPoliticas(settings, new SlaPolicy(1, true, 4, 2));

        svc.MaterializarAsignados(dev.Id);
        // El administrador cancela el SLA a propósito.
        var sla = db.SlaCommitments.Single();
        sla.Status = SlaStatus.Cancelado;
        db.SaveChanges();

        svc.MaterializarAsignados(dev.Id);   // otra sincronización

        // No debe crear uno nuevo: el requerimiento ya existía (no es «nuevo»).
        Assert.Single(db.SlaCommitments);
        Assert.Equal(SlaStatus.Cancelado, db.SlaCommitments.Single().Status);
    }
}
