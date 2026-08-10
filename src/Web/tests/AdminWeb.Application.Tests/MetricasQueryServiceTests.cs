using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Metricas;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Las dos pantallas de planeación del líder, de punta a punta: de las filas de la base a los
/// indicadores que se pintan.
///
/// Lo que se protege aquí no es la aritmética —eso lo cubre <see cref="EstimacionYCapacidadTests"/>—
/// sino que se reúna lo correcto: qué cuenta como atrasado, qué entra en el porcentaje de
/// cumplimiento y que estas pantallas no las pueda abrir nadie más que el líder.
/// </summary>
public class MetricasQueryServiceTests
{
    private static MetricasQueryService Svc(AppDbContext db, ICurrentUser quien) => new(db, quien);

    private static UsuarioDePrueba Lider() => UsuarioDePrueba.Como(UserRole.Admin, userId: 900);

    private static Requirement Req(AppDbContext db, string titulo, RequirementStatus estado,
        DateTime creado, DateTime? compromiso = null, DateTime? entrega = null, decimal? estimadas = null)
    {
        var r = new Requirement
        {
            Title = titulo, Status = estado, CreatedAt = creado, StatusChangedAt = creado,
            CommittedDeliveryDate = compromiso, ActualDeliveryDate = entrega, EstimateHours = estimadas
        };
        db.Requirements.Add(r);
        db.SaveChanges();
        return r;
    }

    private static Developer Dev(AppDbContext db, string nombre)
    {
        var d = new Developer { FullName = nombre, IsActive = true };
        db.Developers.Add(d);
        db.SaveChanges();
        return d;
    }

    private static void Asignar(AppDbContext db, Developer dev, Requirement req)
    {
        db.Assignments.Add(new Assignment { DeveloperId = dev.Id, RequirementId = req.Id });
        db.SaveChanges();
    }

    private static string Valor(IReadOnlyList<IndicadorDto> indicadores, string titulo) =>
        indicadores.Single(i => i.Titulo == titulo).Valor;

    // ── Autorización ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Estas_pantallas_son_del_lider()
    {
        using var db = TestDb.New();

        // Son datos de evaluación de terceros: quién va atrasado y quién está sobrecargado.
        foreach (var quien in new ICurrentUser[]
                 {
                     UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 10),
                     UsuarioDePrueba.Como(UserRole.Operaciones, userId: 11),
                     UsuarioDePrueba.Anonimo()
                 })
        {
            await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, quien).MetricasAsync());
            await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, quien).EstimacionYCapacidadAsync());
        }
    }

    // ── Métricas de ciclo de vida ────────────────────────────────────────────

    [Fact]
    public async Task Un_compromiso_vencido_cuenta_como_atraso_aunque_nadie_lo_haya_marcado()
    {
        using var db = TestDb.New();
        var hoy = DateTime.Today;

        var tarde = Req(db, "Va tarde", RequirementStatus.EnDesarrollo, hoy.AddDays(-30), compromiso: hoy.AddDays(-5));
        Req(db, "A tiempo", RequirementStatus.EnDesarrollo, hoy.AddDays(-10), compromiso: hoy.AddDays(5));

        var d = await Svc(db, Lider()).MetricasAsync();

        Assert.Equal("1", Valor(d.Indicadores, "Atrasados"));
        Assert.Equal("2", Valor(d.Indicadores, "Activos"));

        // Se mide el plazo, no la etiqueta: el atrasado va primero en la lista.
        Assert.Equal(tarde.Id, d.Requerimientos[0].Id);
        Assert.True(d.Requerimientos[0].Atrasado);
        Assert.Equal("⚠ atraso 5", d.Requerimientos[0].PlazoTexto);
    }

    [Fact]
    public async Task Lo_cerrado_no_arrastra_atraso_ni_sigue_envejeciendo()
    {
        using var db = TestDb.New();
        var hoy = DateTime.Today;

        // Entregado tarde: su compromiso quedó atrás, pero ya no es un atraso vivo.
        Req(db, "Entregado tarde", RequirementStatus.Entregado, hoy.AddDays(-40),
            compromiso: hoy.AddDays(-20), entrega: hoy.AddDays(-10));

        var d = await Svc(db, Lider()).MetricasAsync();
        var fila = Assert.Single(d.Requerimientos);

        Assert.Equal("0", Valor(d.Indicadores, "Atrasados"));
        Assert.Equal("0", Valor(d.Indicadores, "Activos"));
        Assert.False(fila.Atrasado);
        Assert.Null(fila.DiasParaCompromiso);           // no se le cuentan días a algo ya entregado
        Assert.Equal(30, fila.DiasVivo);                // del alta a la ENTREGA, no hasta hoy
    }

    [Fact]
    public async Task Sin_entregas_comprometidas_el_cumplimiento_se_enseña_como_raya()
    {
        using var db = TestDb.New();
        Req(db, "Abierto", RequirementStatus.EnDesarrollo, DateTime.Today.AddDays(-3));

        // «0 %» se leería como «cumplió el cero por ciento» cuando no hay nada que medir. Es la misma
        // decisión que ya se tomó en la pantalla de Cumplimiento de SLA.
        var d = await Svc(db, Lider()).MetricasAsync();

        Assert.Equal("—", Valor(d.Indicadores, "Entregados a tiempo"));
    }

    [Fact]
    public async Task El_cumplimiento_solo_mira_lo_entregado_que_tenia_fecha_prometida()
    {
        using var db = TestDb.New();
        var hoy = DateTime.Today;

        Req(db, "A tiempo", RequirementStatus.Entregado, hoy.AddDays(-20),
            compromiso: hoy.AddDays(-5), entrega: hoy.AddDays(-6));
        Req(db, "Tarde", RequirementStatus.Entregado, hoy.AddDays(-20),
            compromiso: hoy.AddDays(-5), entrega: hoy.AddDays(-1));
        // Sin compromiso: no se prometió nada, así que no cuenta ni a favor ni en contra.
        Req(db, "Sin promesa", RequirementStatus.Entregado, hoy.AddDays(-20), entrega: hoy.AddDays(-2));

        var d = await Svc(db, Lider()).MetricasAsync();

        Assert.Equal("50%", Valor(d.Indicadores, "Entregados a tiempo"));
    }

    [Fact]
    public async Task La_carga_por_persona_solo_cuenta_lo_que_sigue_abierto()
    {
        using var db = TestDb.New();
        var hoy = DateTime.Today;
        var ana = Dev(db, "Ana");

        Asignar(db, ana, Req(db, "Abierto y tarde", RequirementStatus.EnDesarrollo,
            hoy.AddDays(-20), compromiso: hoy.AddDays(-2)));
        Asignar(db, ana, Req(db, "Abierto", RequirementStatus.EnPruebas, hoy.AddDays(-10)));
        Asignar(db, ana, Req(db, "Ya entregado", RequirementStatus.Entregado,
            hoy.AddDays(-60), entrega: hoy.AddDays(-1)));

        var fila = Assert.Single((await Svc(db, Lider()).MetricasAsync()).PorDesarrollador);

        Assert.Equal("Ana", fila.Desarrollador);
        Assert.Equal(2, fila.Activos);
        Assert.Equal(1, fila.Atrasados);
        Assert.Equal(20, fila.MasAntiguo);
        Assert.Equal(15, fila.EdadPromedio);
    }

    // ── Estimación y capacidad ───────────────────────────────────────────────

    [Fact]
    public async Task La_estimacion_cruza_las_horas_con_el_tiempo_cronometrado()
    {
        using var db = TestDb.New();
        var ana = Dev(db, "Ana");
        var req = Req(db, "Con estimación", RequirementStatus.EnDesarrollo,
            DateTime.Today.AddDays(-5), estimadas: 10m);
        Req(db, "Sin estimación", RequirementStatus.EnDesarrollo, DateTime.Today.AddDays(-5));

        db.WorkSessions.Add(new WorkSession
        {
            DeveloperId = ana.Id, RequirementId = req.Id,
            AccumulatedSeconds = 15 * 3600, Status = WorkSessionStatus.Detenida
        });
        db.SaveChanges();

        var d = await Svc(db, Lider()).EstimacionYCapacidadAsync();

        // Solo el que tiene horas estimadas: sin estimación no hay nada contra qué comparar.
        var fila = Assert.Single(d.Estimacion);
        Assert.Equal(10, fila.HorasEstimadas);
        Assert.Equal(15, fila.HorasReales);
        Assert.Equal(1.5, fila.Ratio);
        Assert.Equal("▲ Subestimado (tomó más)", fila.Clasificacion);
        Assert.Equal(TonoDeIndicador.Peligro, fila.Tono);
    }

    [Fact]
    public async Task La_capacidad_suma_las_horas_de_lo_abierto_y_marca_la_sobrecarga()
    {
        using var db = TestDb.New();
        var hoy = DateTime.Today;
        var ana = Dev(db, "Ana");
        var beto = Dev(db, "Beto");

        Asignar(db, ana, Req(db, "Grande", RequirementStatus.EnDesarrollo, hoy, estimadas: 30m));
        Asignar(db, ana, Req(db, "Otro grande", RequirementStatus.EnDesarrollo, hoy, estimadas: 20m));
        // Lo entregado no ocupa capacidad futura.
        Asignar(db, ana, Req(db, "Ya entregado", RequirementStatus.Entregado, hoy, estimadas: 100m));

        var d = await Svc(db, Lider()).EstimacionYCapacidadAsync();

        var deAna = d.Capacidad.Single(c => c.Desarrollador == "Ana");
        Assert.Equal(2, deAna.Abiertos);
        Assert.Equal(50, deAna.HorasPendientes);
        Assert.Equal("🔴 Sobrecargado", deAna.Disponibilidad);
        Assert.Equal(TonoDeIndicador.Peligro, deAna.Tono);

        var deBeto = d.Capacidad.Single(c => c.Desarrollador == "Beto");
        Assert.Equal("🟢 Libre", deBeto.Disponibilidad);

        // El sobrecargado va primero: es a quien NO hay que asignarle lo siguiente.
        Assert.Equal("Ana", d.Capacidad[0].Desarrollador);
    }

    [Fact]
    public async Task Quien_esta_de_vacaciones_hoy_sale_como_tal_aunque_tenga_carga()
    {
        using var db = TestDb.New();
        var hoy = DateTime.Today;
        var ana = Dev(db, "Ana");
        Asignar(db, ana, Req(db, "Suyo", RequirementStatus.EnDesarrollo, hoy, estimadas: 100m));

        db.VacationRequests.Add(new VacationRequest
        {
            DeveloperId = ana.Id, StartDate = hoy.AddDays(-1), EndDate = hoy.AddDays(3),
            Status = VacationStatus.Aprobada
        });
        db.SaveChanges();

        var deAna = (await Svc(db, Lider()).EstimacionYCapacidadAsync(dias: 30))
            .Capacidad.Single(c => c.Desarrollador == "Ana");

        Assert.Equal("🏖 De vacaciones", deAna.Disponibilidad);
        // La ventana es inclusiva y cuenta hoy: del día de hoy al tercero son cuatro.
        Assert.Equal(4, deAna.DiasDeVacaciones);
    }

    [Fact]
    public async Task Una_vacacion_sin_aprobar_no_bloquea_a_nadie()
    {
        using var db = TestDb.New();
        var hoy = DateTime.Today;
        var ana = Dev(db, "Ana");

        db.VacationRequests.Add(new VacationRequest
        {
            DeveloperId = ana.Id, StartDate = hoy, EndDate = hoy.AddDays(5),
            Status = VacationStatus.Pendiente
        });
        db.SaveChanges();

        var deAna = (await Svc(db, Lider()).EstimacionYCapacidadAsync()).Capacidad.Single();

        Assert.Equal("🟢 Libre", deAna.Disponibilidad);
        Assert.Equal(0, deAna.DiasDeVacaciones);
    }

    [Fact]
    public async Task La_ventana_de_vacaciones_se_acota_en_vez_de_rechazarse()
    {
        using var db = TestDb.New();

        // Un 5000 no es una petición mal formada, es una ventana absurda: recortarla y decir sobre
        // cuántos días se contó es más útil que un error.
        var d = await Svc(db, Lider()).EstimacionYCapacidadAsync(dias: 5000);

        Assert.Equal(MetricasQueryService.MaxDiasDeVentana, d.DiasDeVentana);
        Assert.Contains($"{MetricasQueryService.MaxDiasDeVentana} días", d.ResumenDeCapacidad);
    }
}
