using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// La cola de revisión del líder: aprobar, rechazar y ajustar el puntaje de una autocalificación.
///
/// Lo que se protege aquí son las tres reglas que el escritorio ganó a base de incidentes:
///
///  · Una entrada YA APROBADA no se vuelve a tocar. Sus puntos están contados en un ranking que la
///    gente vio.
///  · Rechazar EXIGE motivo. Sin él, quien recibe la devolución no sabe qué corregir.
///  · Ajustar el puntaje NO aprueba nada: la entrada sigue esperando decisión.
///
/// Y una que es de la web y no existía allí: los ids llegan por HTTP, así que una entrada que ya no
/// esté pendiente tiene que quedar fuera aunque alguien mande su id a mano.
/// </summary>
public class RevisionDePuntosServiceTests
{
    private static RevisionDePuntosService Svc(AppDbContext db, ICurrentUser quien) =>
        new(db, quien, new AuditService(db, quien, new OrigenDePrueba()), new NotificationService(db));

    private static (AppDbContext db, Developer dev, ScoringCriterion crit, UsuarioDePrueba lider) Entorno()
    {
        var db = TestDb.New();

        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev);
        db.SaveChanges();

        var crit = new ScoringCriterion
        {
            Name = "Propusiste una mejora por tu cuenta",
            DefaultPoints = 15, IsActive = true, Scope = CriterionScope.Individual
        };
        db.ScoringCriteria.Add(crit);
        db.SaveChanges();

        return (db, dev, crit, UsuarioDePrueba.Como(UserRole.Admin, userId: 900));
    }

    /// <summary>Una autocalificación tal como la deja el desarrollador: pendiente y con dueño.</summary>
    private static PointEntry Pendiente(AppDbContext db, Developer dev, ScoringCriterion crit,
        int puntos = 15, int vueltas = 0)
    {
        var entrada = new PointEntry
        {
            DeveloperId = dev.Id, CriterionId = crit.Id, Points = puntos,
            Year = 2026, Month = 8, Date = DateTime.UtcNow,
            ApprovalStatus = PointApprovalStatus.Pendiente,
            SubmittedByDeveloperId = dev.Id,
            ReviewRound = vueltas
        };
        db.PointEntries.Add(entrada);
        db.SaveChanges();
        return entrada;
    }

    // ── Autorización ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_desarrollador_no_puede_aprobarse_sus_propios_puntos()
    {
        var (db, dev, crit, _) = Entorno();
        using var _db = db;
        var entrada = Pendiente(db, dev, crit);

        var yo = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id, userId: 10);

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, yo).AprobarAsync([entrada.Id]));
        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, yo).RechazarAsync([entrada.Id], "no"));
        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, yo).AjustarPuntosAsync(entrada.Id, 99));
        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, yo).PendientesAsync());
    }

    // ── Aprobar ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Aprobar_deja_la_entrada_aprobada_con_quien_y_cuando()
    {
        var (db, dev, crit, lider) = Entorno();
        using var _db = db;
        var entrada = Pendiente(db, dev, crit);

        var (ok, mensaje) = await Svc(db, lider).AprobarAsync([entrada.Id]);

        Assert.True(ok);
        Assert.Contains("+15", mensaje);

        var fila = db.PointEntries.Single(p => p.Id == entrada.Id);
        Assert.Equal(PointApprovalStatus.Aprobado, fila.ApprovalStatus);
        Assert.Equal(900, fila.ReviewedByUserId);
        Assert.NotNull(fila.ReviewedAt);
    }

    [Fact]
    public async Task Aprobar_una_replica_deja_escrito_como_termino_la_discusion()
    {
        var (db, dev, crit, lider) = Entorno();
        using var _db = db;

        // Sin réplica el historial estaría de más; con ella hace falta que las dos mitades queden
        // juntas y en orden, o la conversación se lee incompleta.
        var normal = Pendiente(db, dev, crit);
        var discutida = Pendiente(db, dev, crit, vueltas: 2);

        await Svc(db, lider).AprobarAsync([normal.Id, discutida.Id]);

        Assert.Null(db.PointEntries.Single(p => p.Id == normal.Id).ReviewHistory);
        Assert.Contains("tras la réplica",
            db.PointEntries.Single(p => p.Id == discutida.Id).ReviewHistory);
    }

    [Fact]
    public async Task Una_entrada_ya_aprobada_no_se_puede_volver_a_resolver()
    {
        var (db, dev, crit, lider) = Entorno();
        using var _db = db;
        var entrada = Pendiente(db, dev, crit);
        await Svc(db, lider).AprobarAsync([entrada.Id]);

        // Aunque alguien mande su id a mano: no se carga, porque el filtro es por ESTADO.
        var (ok, mensaje) = await Svc(db, lider).RechazarAsync([entrada.Id], "me arrepentí");

        Assert.False(ok);
        Assert.Contains("sigue pendiente", mensaje);
        Assert.Equal(PointApprovalStatus.Aprobado,
            db.PointEntries.Single(p => p.Id == entrada.Id).ApprovalStatus);
    }

    [Fact]
    public async Task Una_tanda_resuelve_lo_que_sigue_pendiente_y_dice_cuanto_se_omitio()
    {
        var (db, dev, crit, lider) = Entorno();
        using var _db = db;
        var ya = Pendiente(db, dev, crit);
        var sigue = Pendiente(db, dev, crit);
        await Svc(db, lider).AprobarAsync([ya.Id]);

        // Rechazar la tanda entera porque una cambió de estado mientras se miraba obligaría a
        // repetir un trabajo ya hecho.
        var (ok, mensaje) = await Svc(db, lider).AprobarAsync([ya.Id, sigue.Id]);

        Assert.True(ok);
        Assert.Contains("Se omitieron 1", mensaje);
        Assert.Equal(PointApprovalStatus.Aprobado,
            db.PointEntries.Single(p => p.Id == sigue.Id).ApprovalStatus);
    }

    // ── Rechazar ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rechazar_sin_motivo_no_procede()
    {
        var (db, dev, crit, lider) = Entorno();
        using var _db = db;
        var entrada = Pendiente(db, dev, crit);

        var (ok, mensaje) = await Svc(db, lider).RechazarAsync([entrada.Id], "   ");

        Assert.False(ok);
        Assert.Contains("va a leer", mensaje);
        Assert.Equal(PointApprovalStatus.Pendiente,
            db.PointEntries.Single(p => p.Id == entrada.Id).ApprovalStatus);
    }

    [Fact]
    public async Task Rechazar_guarda_el_motivo_para_que_se_pueda_corregir()
    {
        var (db, dev, crit, lider) = Entorno();
        using var _db = db;
        var entrada = Pendiente(db, dev, crit);

        var (ok, _) = await Svc(db, lider).RechazarAsync([entrada.Id], "  Falta el enlace al PR.  ");

        Assert.True(ok);
        var fila = db.PointEntries.Single(p => p.Id == entrada.Id);
        Assert.Equal(PointApprovalStatus.Rechazado, fila.ApprovalStatus);
        Assert.Equal("Falta el enlace al PR.", fila.ReviewComment);
        Assert.Equal(900, fila.ReviewedByUserId);
    }

    [Fact]
    public async Task Rechazar_de_nuevo_una_replica_se_anota_en_el_historial()
    {
        var (db, dev, crit, lider) = Entorno();
        using var _db = db;
        var discutida = Pendiente(db, dev, crit, vueltas: 1);

        await Svc(db, lider).RechazarAsync([discutida.Id], "Sigue sin evidencia.");

        var fila = db.PointEntries.Single(p => p.Id == discutida.Id);
        Assert.Contains("Rechazada de nuevo", fila.ReviewHistory);
        Assert.Contains("Sigue sin evidencia.", fila.ReviewHistory);
    }

    [Fact]
    public async Task Un_motivo_desmedido_se_rechaza_antes_de_tocar_nada()
    {
        var (db, dev, crit, lider) = Entorno();
        using var _db = db;
        var entrada = Pendiente(db, dev, crit);

        var (ok, _) = await Svc(db, lider).RechazarAsync(
            [entrada.Id], new string('x', RevisionDePuntosService.MaxMotivo + 1));

        Assert.False(ok);
        Assert.Equal(PointApprovalStatus.Pendiente,
            db.PointEntries.Single(p => p.Id == entrada.Id).ApprovalStatus);
    }

    // ── Ajustar puntos ───────────────────────────────────────────────────────

    [Fact]
    public async Task Ajustar_el_puntaje_no_aprueba_la_entrada()
    {
        var (db, dev, crit, lider) = Entorno();
        using var _db = db;
        var entrada = Pendiente(db, dev, crit);

        var (ok, mensaje) = await Svc(db, lider).AjustarPuntosAsync(entrada.Id, 8);

        Assert.True(ok);
        Assert.Contains("sigue pendiente", mensaje);

        var fila = db.PointEntries.Single(p => p.Id == entrada.Id);
        Assert.Equal(8, fila.Points);
        Assert.Equal(PointApprovalStatus.Pendiente, fila.ApprovalStatus);
    }

    [Fact]
    public async Task Ajustar_admite_negativos_porque_al_revisar_puede_resultar_un_descuento()
    {
        var (db, dev, crit, lider) = Entorno();
        using var _db = db;
        var entrada = Pendiente(db, dev, crit);

        var (ok, _) = await Svc(db, lider).AjustarPuntosAsync(entrada.Id, -5);

        Assert.True(ok);
        Assert.Equal(-5, db.PointEntries.Single(p => p.Id == entrada.Id).Points);
    }

    [Fact]
    public async Task El_puntaje_de_una_entrada_aprobada_ya_no_se_toca()
    {
        var (db, dev, crit, lider) = Entorno();
        using var _db = db;
        var entrada = Pendiente(db, dev, crit);
        await Svc(db, lider).AprobarAsync([entrada.Id]);

        var (ok, mensaje) = await Svc(db, lider).AjustarPuntosAsync(entrada.Id, 99);

        Assert.False(ok);
        Assert.Contains("ya fue aprobada", mensaje);
        Assert.Equal(15, db.PointEntries.Single(p => p.Id == entrada.Id).Points);
    }

    // ── Lectura de la cola ───────────────────────────────────────────────────

    [Fact]
    public async Task La_cola_trae_solo_lo_pendiente_y_lo_mas_viejo_primero()
    {
        var (db, dev, crit, lider) = Entorno();
        using var _db = db;

        var vieja = Pendiente(db, dev, crit);
        vieja.Date = DateTime.UtcNow.AddDays(-10);
        var nueva = Pendiente(db, dev, crit);
        var resuelta = Pendiente(db, dev, crit);
        db.SaveChanges();
        await Svc(db, lider).AprobarAsync([resuelta.Id]);

        var cola = await Svc(db, lider).PendientesAsync();

        Assert.Equal(2, cola.Count);
        Assert.Equal(vieja.Id, cola[0].Id);   // quien lleva más tiempo esperando va primero
        Assert.Equal(nueva.Id, cola[1].Id);
        Assert.DoesNotContain(cola, p => p.Id == resuelta.Id);
    }

    [Fact]
    public async Task La_cola_formatea_el_periodo_y_el_tiempo_declarado()
    {
        var (db, dev, crit, lider) = Entorno();
        using var _db = db;

        var entrada = Pendiente(db, dev, crit);
        entrada.MinutesSpent = 200;
        db.SaveChanges();

        var fila = (await Svc(db, lider).PendientesAsync()).Single();

        Assert.Equal("Agosto 2026", fila.Periodo);
        Assert.Equal("3h 20m", fila.TiempoDeclarado);
        Assert.False(fila.TieneCaptura);
    }

    [Fact]
    public async Task Sin_tiempo_declarado_se_enseña_una_raya_y_no_un_cero()
    {
        var (db, dev, crit, lider) = Entorno();
        using var _db = db;
        Pendiente(db, dev, crit);

        // «0 m» y «no lo capturé» son cosas distintas y la segunda no debe parecerse a un dato.
        Assert.Equal("—", (await Svc(db, lider).PendientesAsync()).Single().TiempoDeclarado);
    }

    [Fact]
    public async Task Una_seleccion_vacia_lo_dice_en_vez_de_no_hacer_nada()
    {
        var (db, _, _, lider) = Entorno();
        using var _db = db;

        var (ok, mensaje) = await Svc(db, lider).AprobarAsync([]);

        Assert.False(ok);
        Assert.Contains("al menos una", mensaje);
    }
}
