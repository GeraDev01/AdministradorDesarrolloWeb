using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LO QUE CAMBIÓ EN EL SALDO: el corte de arranque y los días laborables.
///
/// <para>Va en su propio archivo y no dentro de <c>SaldoDeVacacionesTests</c> a propósito: aquellas
/// pruebas APARTAN las dos cosas —echan el corte muy atrás y no siembran festivos— porque lo que
/// comprueban es la acumulación, la caducidad y el reparto, y con el corte de producción sus
/// escenarios darían cero y seguirían verdes sin comprobar nada. Aquí se comprueban las dos reglas
/// que allí se apartan.</para>
/// </summary>
public class SaldoConCorteYFestivosTests
{
    private const int AnaId = 7;

    /// <summary>
    /// El escenario, con el corte y los festivos TAL COMO RIGEN EN PRODUCCIÓN. Nada apartado.
    /// </summary>
    private static (AppDbContext db, SaldoDeVacacionesService svc) Nuevo(
        DateTime ingreso, bool conFestivos = true)
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = AnaId, FullName = "Ana", IsActive = true, HireDate = ingreso });
        db.SaveChanges();

        var usuario = UsuarioDePrueba.Como(UserRole.Admin, AnaId);
        var auditoria = new AuditService(db, usuario, new OrigenDePrueba());
        var calendario = new CalendarioLaboralService(db);

        if (conFestivos) calendario.SembrarLosDeLeyAsync(2026).GetAwaiter().GetResult();

        return (db, new SaldoDeVacacionesService(
            db, usuario, auditoria, new SettingsService(db, usuario, auditoria), calendario));
    }

    private static void Vacacion(AppDbContext db, DateTime inicio, int diasDeCalendario,
        VacationStatus estado = VacationStatus.Aprobada)
    {
        db.VacationRequests.Add(new VacationRequest
        {
            DeveloperId = AnaId,
            StartDate = inicio.Date,
            EndDate = inicio.Date.AddDays(diasDeCalendario - 1),
            Status = estado,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    // ── El corte ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Lo cerrado antes del 1 de enero de 2026 no genera nada.</b>
    ///
    /// <para>Ana entró en 2019, así que a mediados de 2026 lleva siete aniversarios. Sin corte se le
    /// generarían los siete periodos y, como no hay ni una vacación capturada de aquellos años, el
    /// sistema le prometería todos esos días — días que casi con seguridad ya disfrutó. Con el corte
    /// solo cuenta el que cerró en 2026.</para>
    /// </summary>
    [Fact]
    public async Task LO_CERRADO_ANTES_DEL_CORTE_no_genera_nada()
    {
        var (_, svc) = Nuevo(new DateTime(2019, 3, 10), conFestivos: false);

        var saldo = await svc.CalcularAsync(AnaId, new DateTime(2026, 8, 14));

        // Séptimo aniversario el 10/03/2026: 22 días (art. 76, del sexto al décimo).
        Assert.Equal(7, saldo.AniosCumplidos);
        Assert.Equal(22, saldo.DiasGenerados);
        Assert.Equal(22, saldo.DiasVigentes);
    }

    /// <summary>
    /// Los periodos anteriores SIGUEN saliendo en el desglose, con cero días. Quitarlos de la lista
    /// haría que el detalle empezara en el año siete y se leyera como si faltaran filas.
    /// </summary>
    [Fact]
    public async Task LOS_PERIODOS_VIEJOS_se_siguen_viendo_en_cero()
    {
        var (_, svc) = Nuevo(new DateTime(2019, 3, 10), conFestivos: false);

        var saldo = await svc.CalcularAsync(AnaId, new DateTime(2026, 8, 14));

        Assert.Equal(7, saldo.Periodos.Count);
        Assert.All(saldo.Periodos.Take(6), p => Assert.Equal(0, p.Dias));
        Assert.Equal(22, saldo.Periodos[6].Dias);
    }

    /// <summary>
    /// El corte es un AJUSTE y no una constante: cambiarlo mueve el saldo. Se comprueba porque es la
    /// única forma de que las pruebas de acumulación puedan seguir existiendo, y porque el dueño puede
    /// querer moverlo sin que nadie recompile.
    /// </summary>
    [Fact]
    public async Task EL_CORTE_SE_PUEDE_MOVER()
    {
        var (db, svc) = Nuevo(new DateTime(2019, 3, 10), conFestivos: false);
        db.AppSettings.Add(new AppSetting
        { Key = SaldoDeVacacionesService.ClaveCorte, Value = "2000-01-01" });
        db.SaveChanges();

        var saldo = await svc.CalcularAsync(AnaId, new DateTime(2026, 8, 14));

        // Ahora sí se generan los siete: 12+14+16+18+20+22+22.
        Assert.Equal(124, saldo.DiasGenerados);
    }

    /// <summary>Un corte mal escrito cae al de siempre en vez de tumbar el cálculo: sin saldo no se
    /// pueden pedir vacaciones, y eso es peor que un corte en la fecha de omisión.</summary>
    [Theory]
    [InlineData("mañana")]
    [InlineData("")]
    [InlineData("32/13/2026")]
    public async Task UN_CORTE_MAL_ESCRITO_cae_al_de_siempre(string valor)
    {
        var (db, svc) = Nuevo(new DateTime(2019, 3, 10), conFestivos: false);
        db.AppSettings.Add(new AppSetting
        { Key = SaldoDeVacacionesService.ClaveCorte, Value = valor });
        db.SaveChanges();

        var saldo = await svc.CalcularAsync(AnaId, new DateTime(2026, 8, 14));

        Assert.Equal(22, saldo.DiasGenerados);   // el mismo que con el corte por omisión
    }

    // ── Los días laborables ──────────────────────────────────────────────────────

    /// <summary>
    /// <b>Una semana de lunes a viernes consume CINCO días, no siete.</b> Es el artículo 76: los días
    /// que concede son laborables. Contando naturales se le cobraban de más a quien pedía vacaciones,
    /// todos los años y en silencio.
    /// </summary>
    [Fact]
    public async Task UNA_SEMANA_DE_LUNES_A_VIERNES_son_cinco_dias()
    {
        var (db, svc) = Nuevo(new DateTime(2024, 1, 8));
        // Lunes 6 a viernes 10 de julio de 2026.
        Vacacion(db, new DateTime(2026, 7, 6), 5);

        var saldo = await svc.CalcularAsync(AnaId, new DateTime(2026, 8, 14));

        Assert.Equal(5, saldo.DiasTomados);
    }

    /// <summary>
    /// Y un rango que se lleva un fin de semana por delante consume solo los laborables: del lunes 6
    /// al lunes 13 de julio de 2026 hay ocho días de calendario y SEIS laborables.
    /// </summary>
    [Fact]
    public async Task EL_FIN_DE_SEMANA_no_consume_vacaciones()
    {
        var (db, svc) = Nuevo(new DateTime(2024, 1, 8));
        Vacacion(db, new DateTime(2026, 7, 6), 8);

        var saldo = await svc.CalcularAsync(AnaId, new DateTime(2026, 8, 14));

        Assert.Equal(6, saldo.DiasTomados);
    }

    /// <summary>
    /// <b>Un día de descanso obligatorio tampoco consume.</b> Del jueves 24 al lunes 28 de diciembre
    /// de 2026 hay cinco días de calendario; fuera el sábado, el domingo y el 25 —Navidad, artículo 74
    /// fracción VIII— quedan DOS.
    /// </summary>
    [Fact]
    public async Task UN_FESTIVO_DE_LEY_tampoco_consume()
    {
        var (db, svc) = Nuevo(new DateTime(2024, 1, 8));
        Vacacion(db, new DateTime(2026, 12, 24), 5);

        var saldo = await svc.CalcularAsync(AnaId, new DateTime(2027, 1, 15));

        // 24 jueves (laborable), 25 viernes (Navidad), 26 sábado, 27 domingo, 28 lunes (laborable).
        Assert.Equal(2, saldo.DiasTomados);
    }

    /// <summary>
    /// La misma solicitud SIN calendario sembrado consume un día más. Es la prueba que demuestra que
    /// los festivos hacen algo: sin ella, la de arriba podría estar pasando por los fines de semana.
    /// </summary>
    [Fact]
    public async Task SIN_CALENDARIO_ese_festivo_si_se_cobraria()
    {
        var (db, svc) = Nuevo(new DateTime(2024, 1, 8), conFestivos: false);
        Vacacion(db, new DateTime(2026, 12, 24), 5);

        var saldo = await svc.CalcularAsync(AnaId, new DateTime(2027, 1, 15));

        Assert.Equal(3, saldo.DiasTomados);   // el 25 se cuenta porque nadie dijo que era festivo
    }

    // ── La siembra ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Sembrar dos veces no duplica nada: corre en cada arranque y tiene que ser inocuo. Un festivo
    /// repetido se descontaría dos veces de las vacaciones de quien lo pida.
    /// </summary>
    [Fact]
    public async Task SEMBRAR_DOS_VECES_no_duplica()
    {
        var db = TestDb.New();
        var calendario = new CalendarioLaboralService(db);

        int primera = await calendario.SembrarLosDeLeyAsync(2026);
        int segunda = await calendario.SembrarLosDeLeyAsync(2026);

        Assert.True(primera > 0);
        Assert.Equal(0, segunda);
        Assert.Equal(primera, db.DiasFestivos.Count());
    }

    /// <summary>
    /// Y no toca los días que puso la casa. Es lo que hace que resembrar al añadir años nuevos sea
    /// seguro: repone los de ley que falten y deja en paz lo demás.
    /// </summary>
    [Fact]
    public async Task SEMBRAR_no_toca_los_dias_propios()
    {
        var db = TestDb.New();
        db.DiasFestivos.Add(new DiaFestivo
        {
            Fecha = new DateTime(2026, 8, 20),
            Motivo = "Aniversario de la empresa",
            EsDeLey = false
        });
        db.SaveChanges();

        await new CalendarioLaboralService(db).SembrarLosDeLeyAsync(2026);

        var propio = db.DiasFestivos.Single(f => !f.EsDeLey);
        Assert.Equal("Aniversario de la empresa", propio.Motivo);
    }

    /// <summary>Un día que puso la casa cuenta igual que uno de ley: no se trabaja, no consume.</summary>
    [Fact]
    public async Task UN_DIA_PROPIO_DE_LA_CASA_tampoco_consume()
    {
        var (db, svc) = Nuevo(new DateTime(2024, 1, 8), conFestivos: false);
        db.DiasFestivos.Add(new DiaFestivo
        {
            // Martes 7 de julio de 2026.
            Fecha = new DateTime(2026, 7, 7),
            Motivo = "Aniversario de la empresa",
            EsDeLey = false
        });
        db.SaveChanges();

        Vacacion(db, new DateTime(2026, 7, 6), 5);   // lunes a viernes
        var saldo = await svc.CalcularAsync(AnaId, new DateTime(2026, 8, 14));

        Assert.Equal(4, saldo.DiasTomados);
    }
}
