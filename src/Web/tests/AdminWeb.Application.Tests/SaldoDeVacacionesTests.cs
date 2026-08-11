using AdminWeb.Application.Services;
using AdminWeb.Domain.Calculo;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El saldo de vacaciones: la tabla de la ley, la acumulación con caducidad, qué cuenta como
/// consumido, qué pasa al cancelar y el ajuste manual del líder.
///
/// <para>Las fechas van todas ESCRITAS y el «hoy» se pasa por parámetro. Es lo único que hace
/// probable un cálculo de antigüedad: con <c>DateTime.Today</c> por dentro, la mitad de estas
/// pruebas cambiaría de resultado el día que se cruce un aniversario y nadie sabría por qué se
/// rompieron solas un martes.</para>
/// </summary>
public class SaldoDeVacacionesTests
{
    private const int AnaId = 7;
    private const int BetoId = 8;

    /// <summary>Ingreso de Ana en casi todas las pruebas: el 5 de enero, como los tres que entraron
    /// ese día en la base real. Con corte el 05/01/2026 son exactamente cinco años cumplidos.</summary>
    private static readonly DateTime Ingreso = new(2021, 1, 5);

    private static readonly DateTime CincoAnios = new(2026, 1, 5);

    private static (AppDbContext db, SaldoDeVacacionesService svc, ICurrentUser usuario) Nuevo(
        DateTime? ingreso = null, UserRole rol = UserRole.Admin, int? devId = AnaId)
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = AnaId, FullName = "Ana", IsActive = true, HireDate = ingreso, VacationDaysLeft = 15 });
        db.Developers.Add(new Developer { Id = BetoId, FullName = "Beto", IsActive = true, HireDate = ingreso });
        db.SaveChanges();

        var usuario = UsuarioDePrueba.Como(rol, devId);
        var auditoria = new AuditService(db, usuario, new OrigenDePrueba());
        var svc = new SaldoDeVacacionesService(
            db, usuario, auditoria, new SettingsService(db, usuario, auditoria));
        return (db, svc, usuario);
    }

    private static VacationRequest Vacacion(
        AppDbContext db, DateTime inicio, int dias, VacationStatus estado, int devId = AnaId)
    {
        var v = new VacationRequest
        {
            DeveloperId = devId,
            StartDate = inicio.Date,
            EndDate = inicio.Date.AddDays(dias - 1),
            Status = estado,
            CreatedAt = DateTime.UtcNow
        };
        db.VacationRequests.Add(v);
        db.SaveChanges();
        return v;
    }

    private static void Configurar(AppDbContext db, string valor)
    {
        db.AppSettings.Add(new AppSetting
        {
            Key = SaldoDeVacacionesService.ClaveCaducidadMeses,
            Value = valor
        });
        db.SaveChanges();
    }

    // ── La tabla de la ley ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 12)]
    [InlineData(2, 14)]
    [InlineData(3, 16)]
    [InlineData(4, 18)]
    [InlineData(5, 20)]
    [InlineData(6, 22)]
    [InlineData(10, 22)]
    [InlineData(11, 24)]
    [InlineData(15, 24)]
    [InlineData(16, 26)]
    [InlineData(20, 26)]
    [InlineData(21, 28)]
    [InlineData(25, 28)]
    [InlineData(26, 30)]
    public void LaTablaSigueLaLeyRenglonPorRenglon(int anios, int esperado)
        => Assert.Equal(esperado, TablaDeVacacionesLft.DiasDelPeriodo(anios));

    [Fact]
    public void ElPrimerAnioNoGeneraNada_SinProporcional()
    {
        // Es la regla 4 vista desde la tabla: cero significa «todavía no», no «faltó un dato».
        Assert.Equal(0, TablaDeVacacionesLft.DiasDelPeriodo(0));
        Assert.Equal(0, TablaDeVacacionesLft.DiasDelPeriodo(-3));
    }

    [Fact]
    public void MasAllaDelUltimoRenglonSigueSubiendoDosPorQuinquenio()
    {
        // No es un caso real —serían más de 50 años en la misma empresa— pero un tope silencioso
        // pagaría de menos a quien llegara, y eso no debe poder pasar sin que nadie se entere.
        Assert.Equal(38, TablaDeVacacionesLft.DiasDelPeriodo(50));
        Assert.Equal(40, TablaDeVacacionesLft.DiasDelPeriodo(51));
        Assert.Equal(40, TablaDeVacacionesLft.DiasDelPeriodo(55));
        Assert.Equal(42, TablaDeVacacionesLft.DiasDelPeriodo(56));
    }

    [Fact]
    public void LaTablaDiceLoMISMOQueLaFormulaDelEscritorio()
    {
        // ESTA es la prueba que justifica que haya dos copias de la regla. La tabla se escribió para
        // poder compararla con el artículo 76 renglón por renglón; LftVacaciones es la fórmula que se
        // portó del escritorio sin tocar y que alimenta el botón «Calcular (LFT)» de la ficha. Si
        // alguien toca una de las dos, esta prueba lo dice en el acto en vez de dejar dos números
        // distintos conviviendo en pantallas distintas.
        for (int anios = 1; anios <= 50; anios++)
            Assert.Equal(LftVacaciones.DiasPorAnios(anios), TablaDeVacacionesLft.DiasDelPeriodo(anios));
    }

    [Theory]
    [InlineData("2020-03-01", "2026-07-28", 6)]
    [InlineData("2020-03-01", "2026-02-15", 5)]
    [InlineData("2020-03-01", "2026-03-01", 6)]   // el propio aniversario ya cuenta
    [InlineData("2026-01-05", "2026-08-11", 0)]
    public void LosAniosSeCuentanPorAniversarioYNoPorAnioCalendario(
        string ingreso, string corte, int esperado)
    {
        var a = DateTime.Parse(ingreso);
        var b = DateTime.Parse(corte);

        Assert.Equal(esperado, TablaDeVacacionesLft.AniosCumplidos(a, b));
        // Y coincide con la cuenta del escritorio, por lo mismo que la tabla.
        Assert.Equal(LftVacaciones.AniosCumplidos(a, b), TablaDeVacacionesLft.AniosCumplidos(a, b));
    }

    [Fact]
    public void QuienEntroUn29DeFebreroCumpleElUltimoDiaDeFebrero()
    {
        var ingreso = new DateTime(2020, 2, 29);

        Assert.Equal(new DateTime(2025, 2, 28), TablaDeVacacionesLft.Aniversario(ingreso, 5));
        Assert.Equal(5, TablaDeVacacionesLft.AniosCumplidos(ingreso, new DateTime(2025, 2, 28)));
    }

    // ── El primer año ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ElPrimerAnioEsCeroYElMensajeDiceEnQueFechaDejaDeSerlo()
    {
        // Los tres que entraron el 05/01/2026 en la base real. Un cero a secas parecería un error del
        // programa: el mensaje tiene que decir cuándo cambia y cuántos días serán.
        var (_, svc, _) = Nuevo(new DateTime(2026, 1, 5));

        var saldo = await svc.CalcularAsync(AnaId, new DateTime(2026, 8, 11));

        Assert.Equal(0, saldo.AniosCumplidos);
        Assert.Equal(0, saldo.DiasGenerados);
        Assert.Equal(0, saldo.DiasVigentes);
        Assert.Equal(0, saldo.Disponible);
        Assert.Empty(saldo.Periodos);

        Assert.Equal(new DateTime(2027, 1, 5), saldo.ProximoAniversario);
        Assert.Equal(12, saldo.DiasDelProximoPeriodo);
        Assert.Contains("05/01/2027", saldo.Mensaje);
        Assert.Contains("12 días", saldo.Mensaje);
    }

    [Fact]
    public async Task SinFechaDeIngresoElMensajeDiceQueFaltaCapturarla()
    {
        var (_, svc, _) = Nuevo(ingreso: null);

        var saldo = await svc.CalcularAsync(AnaId, CincoAnios);

        Assert.True(saldo.TieneFicha);
        Assert.Null(saldo.FechaDeIngreso);
        Assert.Contains("fecha de ingreso", saldo.Mensaje);
    }

    // ── Acumulación y caducidad ─────────────────────────────────────────────────

    [Fact]
    public async Task LoNoGozadoSeAcumulaDeUnPeriodoAOtro_PeroCaduca()
    {
        // Cinco años cumplidos sin una sola solicitud registrada: el caso real del arranque.
        // La ley le fue dando 12+14+16+18+20 = 80 días. Con la ventana de 18 meses, los tres
        // primeros periodos ya caducaron y quedan vivos los dos últimos: 18+20 = 38.
        var (_, svc, _) = Nuevo(Ingreso);

        var saldo = await svc.CalcularAsync(AnaId, CincoAnios);

        Assert.Equal(5, saldo.AniosCumplidos);
        Assert.Equal(80, saldo.DiasGenerados);
        Assert.Equal(0, saldo.DiasTomados);
        Assert.Equal(42, saldo.DiasCaducados);     // 12 + 14 + 16
        Assert.Equal(38, saldo.DiasVigentes);      // 18 + 20
        Assert.Equal(38, saldo.Disponible);

        // La identidad que cualquiera puede comprobar sumando los renglones de la respuesta.
        Assert.Equal(saldo.DiasGenerados - saldo.DiasTomados - saldo.DiasCaducados, saldo.DiasVigentes);

        // Y el desglose lo explica año por año, que es lo que permite defender el número.
        Assert.Equal(5, saldo.Periodos.Count);
        Assert.Equal([12, 14, 16, 18, 20], saldo.Periodos.Select(p => p.Dias));
        Assert.Equal([true, true, true, false, false], saldo.Periodos.Select(p => p.Caducado));
        Assert.Equal(new DateTime(2022, 1, 5), saldo.Periodos[0].Cierre);
        Assert.Equal(new DateTime(2023, 7, 5), saldo.Periodos[0].Caduca);
    }

    [Fact]
    public async Task LaVentanaDeCaducidadEsConfigurable()
    {
        var (db, svc, _) = Nuevo(Ingreso);
        Configurar(db, "120");   // diez años: en la práctica, que no caduque nada

        var saldo = await svc.CalcularAsync(AnaId, CincoAnios);

        Assert.Equal(120, saldo.VentanaDeCaducidadMeses);
        Assert.Equal(0, saldo.DiasCaducados);
        Assert.Equal(80, saldo.DiasVigentes);
    }

    [Theory]
    [InlineData("ocho")]      // basura tecleada
    [InlineData("0")]         // fuera de rango por abajo
    [InlineData("99999")]     // fuera de rango por arriba
    public async Task UnaVentanaImposibleCaeAlValorPorOmision(string valor)
    {
        // Aquí un número absurdo NO da error: da un saldo enorme y creíble, que es el peor fallo
        // posible en un cálculo que alguien va a firmar. Por eso se acota en vez de propagarse.
        var (db, svc, _) = Nuevo(Ingreso);
        Configurar(db, valor);

        var saldo = await svc.CalcularAsync(AnaId, CincoAnios);

        Assert.Equal(SaldoDeVacacionesService.CaducidadMesesPorOmision, saldo.VentanaDeCaducidadMeses);
        Assert.Equal(38, saldo.DiasVigentes);
    }

    [Fact]
    public async Task ElPeriodoEsElAniversarioDeCadaQuien_NoElAnioCalendario()
    {
        // Dos personas con el mismo «hoy» y dos fechas de corte distintas: si el periodo fuera el año
        // calendario, las dos tendrían exactamente lo mismo generado.
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = AnaId, FullName = "Ana", IsActive = true, HireDate = new DateTime(2024, 1, 10) });
        db.Developers.Add(new Developer { Id = BetoId, FullName = "Beto", IsActive = true, HireDate = new DateTime(2024, 12, 20) });
        db.SaveChanges();

        var usuario = UsuarioDePrueba.Como(UserRole.Admin);
        var auditoria = new AuditService(db, usuario, new OrigenDePrueba());
        var svc = new SaldoDeVacacionesService(db, usuario, auditoria, new SettingsService(db, usuario, auditoria));

        var hoy = new DateTime(2026, 6, 1);
        var ana = await svc.CalcularAsync(AnaId, hoy);
        var beto = await svc.CalcularAsync(BetoId, hoy);

        Assert.Equal(2, ana.AniosCumplidos);      // 10/01/2024 → ya pasó el 10/01/2026
        Assert.Equal(26, ana.DiasGenerados);      // 12 + 14
        Assert.Equal(1, beto.AniosCumplidos);     // 20/12/2024 → el 20/12/2026 aún no llega
        Assert.Equal(12, beto.DiasGenerados);
    }

    // ── Qué cuenta como consumido ───────────────────────────────────────────────

    [Fact]
    public async Task LoAprobadoSeDescuentaDeLosDiasMasVIEJOSPrimero()
    {
        // Diez días tomados en febrero de 2025. Ese día, los periodos 1 y 2 ya habían caducado, así
        // que el más viejo que seguía vivo era el tercero: de ahí salen. Cargarlos contra el periodo
        // más reciente dejaría los viejos quietos hasta caducar y la persona perdería días teniendo
        // saldo de sobra.
        var (db, svc, _) = Nuevo(Ingreso);
        Vacacion(db, new DateTime(2025, 2, 1), 10, VacationStatus.Aprobada);

        var saldo = await svc.CalcularAsync(AnaId, CincoAnios);

        Assert.Equal(10, saldo.Periodos[2].Usados);
        Assert.Equal(6, saldo.Periodos[2].Restantes);
        Assert.Equal(0, saldo.Periodos[3].Usados);

        Assert.Equal(10, saldo.DiasTomados);
        Assert.Equal(32, saldo.DiasCaducados);    // 12 + 14 + los 6 que sobraron del tercero
        Assert.Equal(38, saldo.DiasVigentes);
    }

    [Fact]
    public async Task LasPENDIENTESNoSeHanGozadoPeroApartanElDisponible()
    {
        // Si no restaran, alguien podría pedir tres veces los mismos días y las tres solicitudes
        // parecerían caber; el líder aprobaría la primera creyendo que quedan días y las otras dos ya
        // estarían de más. Van en su propio renglón para que se entienda por qué bajó el disponible
        // sin que nadie haya aprobado nada.
        var (db, svc, _) = Nuevo(Ingreso);
        Vacacion(db, new DateTime(2026, 2, 1), 5, VacationStatus.Pendiente);

        var saldo = await svc.CalcularAsync(AnaId, CincoAnios);

        Assert.Equal(0, saldo.DiasTomados);
        Assert.Equal(38, saldo.DiasVigentes);
        Assert.Equal(5, saldo.DiasComprometidos);
        Assert.Equal(33, saldo.Disponible);
    }

    [Fact]
    public async Task LoRechazadoYLoCanceladoNoCuentaNiUnDia()
    {
        var (db, svc, _) = Nuevo(Ingreso);
        Vacacion(db, new DateTime(2025, 8, 1), 7, VacationStatus.Rechazada);
        Vacacion(db, new DateTime(2025, 9, 1), 4, VacationStatus.Cancelada);

        var saldo = await svc.CalcularAsync(AnaId, CincoAnios);

        Assert.Equal(0, saldo.DiasTomados);
        Assert.Equal(0, saldo.DiasComprometidos);
        Assert.Equal(38, saldo.Disponible);
    }

    [Fact]
    public async Task LosPermisosNoDescuentanVacaciones()
    {
        // Una incapacidad o una cita médica no son vacaciones. Descontarlas de aquí le cobraría a la
        // persona días que la ley no permite cobrarle.
        var (db, svc, _) = Nuevo(Ingreso);
        db.LeaveRequests.Add(new LeaveRequest
        {
            DeveloperId = AnaId,
            Type = LeaveType.Incapacidad,
            Date = new DateTime(2025, 11, 3),
            DaysCount = 9,
            Status = LeaveStatus.Aprobada,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var saldo = await svc.CalcularAsync(AnaId, CincoAnios);

        Assert.Equal(0, saldo.DiasTomados);
        Assert.Equal(38, saldo.Disponible);
    }

    [Fact]
    public async Task GozarDiasQueTodaviaNoSeHanGeneradoDejaElSaldoEnRojo()
    {
        // Primer año, sin proporcional: los cinco días que se tomó no salían de ningún periodo. El
        // saldo se enseña en negativo en vez de recortarse a cero, que es justo lo que el líder tiene
        // que ver para corregirlo con un ajuste.
        var (db, svc, _) = Nuevo(new DateTime(2026, 1, 5));
        Vacacion(db, new DateTime(2026, 3, 1), 5, VacationStatus.Aprobada);

        var saldo = await svc.CalcularAsync(AnaId, new DateTime(2026, 8, 11));

        Assert.Equal(0, saldo.DiasGenerados);
        Assert.Equal(5, saldo.DiasTomados);
        Assert.Equal(-5, saldo.DiasVigentes);
        Assert.Equal(-5, saldo.Disponible);
    }

    // ── Qué pasa al cancelar ────────────────────────────────────────────────────

    [Fact]
    public async Task CancelarUnasVacacionesYaAPROBADASDevuelveLosDiasSOLO()
    {
        // Ésta es la prueba que paga la decisión de NO guardar el saldo. Nadie programa la
        // devolución: la consulta siguiente sencillamente ya no encuentra esa solicitud entre las
        // aprobadas. Con un saldo almacenado habría que acordarse de sumar los días de vuelta en
        // CancelarAsync, en EliminarAsync y en cualquier camino futuro que cambie un estado — y el
        // día que a alguien se le olvidara, el saldo mentiría sin avisar.
        var (db, svc, usuario) = Nuevo(Ingreso, UserRole.Desarrollador);
        var v = Vacacion(db, new DateTime(2025, 12, 1), 5, VacationStatus.Aprobada);

        var antes = await svc.CalcularAsync(AnaId, CincoAnios);
        Assert.Equal(5, antes.DiasTomados);
        Assert.Equal(33, antes.Disponible);

        var (ok, _) = await Fabrica.Vacaciones(db, usuario).CancelarAsync(v.Id);
        Assert.True(ok);

        var despues = await svc.CalcularAsync(AnaId, CincoAnios);
        Assert.Equal(0, despues.DiasTomados);
        Assert.Equal(38, despues.Disponible);
    }

    [Fact]
    public async Task ElSaldoNoSeGUARDAEnLaFicha()
    {
        // El campo «Días de vacaciones» de la ficha es otra cosa —el número que RH teclea y que sale
        // impreso en el documento— y este cálculo no lo toca. Si algún día alguien lo «sincronizara»
        // desde aquí, tendríamos otra vez un número que mantener a mano.
        var (db, svc, _) = Nuevo(Ingreso);

        await svc.CalcularAsync(AnaId, CincoAnios);
        await svc.RegistrarAjusteAsync(AnaId, -30, "Días gozados antes de la web.");

        var ficha = db.Developers.AsNoTracking().Single(d => d.Id == AnaId);
        Assert.Equal(15, ficha.VacationDaysLeft);
    }

    // ── El ajuste manual ────────────────────────────────────────────────────────

    [Fact]
    public async Task ElAjusteCorrigeElSaldoYExigeUnaNota()
    {
        var (_, svc, _) = Nuevo(Ingreso);

        // Sin motivo no se guarda: un saldo corregido sin explicación escrita no se puede defender
        // dentro de un año delante de quien reclame sus días.
        Assert.False((await svc.RegistrarAjusteAsync(AnaId, -30, null)).ok);
        Assert.False((await svc.RegistrarAjusteAsync(AnaId, -30, "   ")).ok);

        var (ok, mensaje) = await svc.RegistrarAjusteAsync(
            AnaId, -30, "  Gozó 30 días entre 2023 y 2025; se capturaban en papel.  ");

        Assert.True(ok);
        Assert.Contains("-30", mensaje);

        var saldo = await svc.CalcularAsync(AnaId, CincoAnios);
        Assert.Equal(-30, saldo.AjusteManual);
        Assert.Equal("Gozó 30 días entre 2023 y 2025; se capturaban en papel.", saldo.NotaDelAjuste);
        Assert.Equal("admin", saldo.AutorDelAjuste);
        Assert.NotNull(saldo.FechaDelAjusteUtc);

        // El ajuste mueve el disponible, no lo generado: el cálculo sigue diciendo lo que dice.
        Assert.Equal(38, saldo.DiasVigentes);
        Assert.Equal(8, saldo.Disponible);
    }

    [Fact]
    public async Task ElAjusteNuevoREEMPLAZAAlAnterior()
    {
        var (_, svc, _) = Nuevo(Ingreso);

        await svc.RegistrarAjusteAsync(AnaId, -30, "Primera corrección.");
        await svc.RegistrarAjusteAsync(AnaId, -12, "Se revisó el papel: eran 12, no 30.");

        var saldo = await svc.CalcularAsync(AnaId, CincoAnios);

        Assert.Equal(-12, saldo.AjusteManual);
        Assert.Equal("Se revisó el papel: eran 12, no 30.", saldo.NotaDelAjuste);
    }

    [Theory]
    [InlineData(366)]
    [InlineData(-366)]
    public async Task UnAjusteDisparatadoSeRechaza(int dias)
    {
        // No es una regla de negocio: es la red contra el dedazo. Un 3650 tecleado por accidente
        // pasaría por saldo bueno.
        var (_, svc, _) = Nuevo(Ingreso);

        Assert.False((await svc.RegistrarAjusteAsync(AnaId, dias, "Motivo.")).ok);
    }

    [Fact]
    public async Task ElAjusteQuedaEnLaBitacoraConLoQueHabiaAntes()
    {
        var (db, svc, _) = Nuevo(Ingreso);

        await svc.RegistrarAjusteAsync(AnaId, -30, "Días gozados antes de la web.");
        await svc.RegistrarAjusteAsync(AnaId, -12, "Se revisó el papel.");

        var lineas = db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "Developer" && a.EntityId == AnaId.ToString())
            .OrderBy(a => a.Id)
            .ToList();

        Assert.Equal(2, lineas.Count);
        Assert.All(lineas, l => Assert.Equal(AuditAction.Update, l.Action));
        Assert.Contains("Ana", lineas[0].Details);
        Assert.Contains("Días gozados antes de la web.", lineas[0].Details);

        // La segunda línea conserva el número que había antes: de un ajuste de saldo hay que poder
        // reconstruir de dónde venía, no solo que alguien lo tocó.
        Assert.Contains("-30", lineas[1].OldValues);
        Assert.Contains("-12", lineas[1].NewValues);
    }

    [Fact]
    public async Task LaBitacoraDelAjusteNoArrastraElRestoDeLaFicha()
    {
        // La bitácora la lee más gente que la que puede abrir una ficha. Se serializan SOLO los
        // cuatro campos del ajuste; volcar la entidad entera metería ahí datos de la persona que no
        // tienen nada que ver con sus vacaciones.
        var (db, svc, _) = Nuevo(Ingreso);

        await svc.RegistrarAjusteAsync(AnaId, -30, "Días gozados antes de la web.");

        var linea = db.AuditLogs.AsNoTracking()
            .Single(a => a.EntityType == "Developer" && a.EntityId == AnaId.ToString());

        Assert.DoesNotContain("FullName", linea.NewValues);
        Assert.DoesNotContain("Email", linea.NewValues);
        Assert.DoesNotContain("Address", linea.NewValues);
        Assert.DoesNotContain("HireDate", linea.NewValues);
    }

    // ── Quién puede qué ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UnDesarrolladorNoVeElSaldoDeOtro()
    {
        var (_, svc, _) = Nuevo(Ingreso, UserRole.Desarrollador, devId: AnaId);

        await svc.CalcularAsync(AnaId, CincoAnios);   // el suyo, sin problema

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.CalcularAsync(BetoId, CincoAnios));
    }

    [Fact]
    public async Task SoloElLiderAjustaElSaldo()
    {
        // Ni siquiera el propio: el ajuste es la decisión de quien concede los días, y dejar que cada
        // quien se corrija el suyo convertiría el número en una declaración.
        var (_, svc, _) = Nuevo(Ingreso, UserRole.Desarrollador, devId: AnaId);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.RegistrarAjusteAsync(AnaId, 5, "Me deben días."));
    }

    [Fact]
    public async Task UnaCuentaSinFichaNoTieneSaldoPeroTampocoFalla()
    {
        // A las pantallas de ausencias también llega el administrador, cuya cuenta no siempre tiene
        // ficha. Responder vacío es lo correcto; reventar dejaría la pantalla en blanco.
        var (_, svc, _) = Nuevo(Ingreso, UserRole.Admin, devId: null);

        var saldo = await svc.MioAsync(CincoAnios);

        Assert.False(saldo.TieneFicha);
        Assert.Equal(0, saldo.Disponible);
        Assert.Empty(saldo.Periodos);
    }

    // ── La migración ────────────────────────────────────────────────────────────

    [Fact]
    public void ElMigradorAnadeLasColumnasDelAjusteAUnaBaseQueYaExistia()
    {
        // La base real tiene datos y las columnas nuevas no están: lo que corre ahí es el ALTER, no
        // el CREATE. Se simula quitándole a la base de hoy las cuatro columnas, que es exactamente la
        // forma que tiene una base que solo ha visto el escritorio.
        using var db = TestDb.New();

        // Las cuatro sentencias van ESCRITAS UNA A UNA y no armadas en un bucle con interpolación,
        // por lo mismo que en MigracionDePlazosAHorasTests: ExecuteSqlRaw con una cadena interpolada
        // levanta el aviso EF1002 —el que avisa de inyección de SQL— y esta rama compila con cero
        // avisos a propósito. Un nombre de columna no puede ir como parámetro, así que la única
        // forma de no interpolar es no tener nada que interpolar.
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" DROP COLUMN ""VacationAdjustmentDays""");
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" DROP COLUMN ""VacationAdjustmentNote""");
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" DROP COLUMN ""VacationAdjustmentBy""");
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Developers"" DROP COLUMN ""VacationAdjustmentAtUtc""");

        Assert.False(TieneColumna(db, "Developers", "VacationAdjustmentDays"));

        DatabaseMigrator.EnsureUpToDate(db);

        Assert.True(TieneColumna(db, "Developers", "VacationAdjustmentDays"));
        Assert.True(TieneColumna(db, "Developers", "VacationAdjustmentNote"));
        Assert.True(TieneColumna(db, "Developers", "VacationAdjustmentBy"));
        Assert.True(TieneColumna(db, "Developers", "VacationAdjustmentAtUtc"));

        // Y es idempotente: la API arranca muchas veces —cada despliegue, cada instancia— y el
        // segundo paso no puede tropezar con lo que dejó el primero.
        DatabaseMigrator.EnsureUpToDate(db);
        Assert.True(TieneColumna(db, "Developers", "VacationAdjustmentDays"));
    }

    [Fact]
    public void ElMigradorSiembraLaVentanaDeCaducidadSinPisarLoQueElLiderHayaPuesto()
    {
        using var db = TestDb.New();
        db.Database.ExecuteSqlRaw(@"DELETE FROM ""AppSettings"" WHERE ""Key"" = {0}",
            SaldoDeVacacionesService.ClaveCaducidadMeses);

        DatabaseMigrator.EnsureUpToDate(db);

        var fila = db.AppSettings.AsNoTracking()
            .Single(s => s.Key == SaldoDeVacacionesService.ClaveCaducidadMeses);
        Assert.Equal("18", fila.Value);
        Assert.False(string.IsNullOrWhiteSpace(fila.Description));

        // El líder lo cambia a 12 y arranca la API otra vez. Sin el «solo si falta», cada reinicio le
        // devolvería el 18 y el cambio parecería borrarse solo.
        db.AppSettings.Single(s => s.Key == SaldoDeVacacionesService.ClaveCaducidadMeses).Value = "12";
        db.SaveChanges();

        DatabaseMigrator.EnsureUpToDate(db);

        Assert.Equal("12", db.AppSettings.AsNoTracking()
            .Single(s => s.Key == SaldoDeVacacionesService.ClaveCaducidadMeses).Value);
    }

    private static bool TieneColumna(AppDbContext db, string tabla, string columna)
    {
        var conn = db.Database.GetDbConnection();
        bool abrir = conn.State != System.Data.ConnectionState.Open;
        if (abrir) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info('{tabla}')";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (string.Equals(r.GetString(1), columna, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        finally { if (abrir) conn.Close(); }
    }

    // ── El conteo de documentos generados ───────────────────────────────────────

    [Fact]
    public async Task ElConteoDeDocumentosNoIncluyeLaFilaDeLaFIRMA()
    {
        // La firma del colaborador se guarda como una fila de VacationDocuments, pero no es un
        // documento que nadie haya generado. Contándola, la confirmación de borrado avisaba de un
        // papel de más en cuanto la persona firmaba su solicitud, y el endpoint lo tapaba después
        // reescribiendo el número. Ahora lo cuenta bien el servicio y el parche ya no existe.
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = AnaId, FullName = "Ana", IsActive = true });
        db.SaveChanges();

        var v = Vacacion(db, new DateTime(2026, 3, 1), 3, VacationStatus.Pendiente);

        db.VacationDocuments.Add(new VacationDocument
        {
            VacationRequestId = v.Id,
            FileName = "solicitud.docx",
            DocxBytes = [1],
            CreatedAtUtc = DateTime.UtcNow
        });
        db.VacationDocuments.Add(new VacationDocument
        {
            VacationRequestId = v.Id,
            FileName = VacationRequestService.MarcaDeLaFirmaDelColaborador,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();

        var usuario = UsuarioDePrueba.Como(UserRole.Desarrollador, AnaId);
        var auditoria = new AuditService(db, usuario, new OrigenDePrueba());
        var ausencias = new AusenciasService(
            db, usuario, new LeaveRequestService(db, usuario, auditoria), auditoria);

        var mias = await ausencias.MisVacacionesAsync();

        Assert.Equal(1, mias.Solicitudes.Single().DocumentosGenerados);
    }
}
