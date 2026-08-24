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
///
/// <para><b>Lo que cambió con la puerta única.</b> REGISTRAR se apagó —el trabajo se propone al pool
/// y el líder le pone valor antes de hacerlo—, pero CORREGIR y REPLICAR siguen vivos: hay entradas
/// pendientes y rechazadas ahí fuera, y cerrarlas dejaría conversaciones a medias y gente con puntos
/// en el limbo. Así que este archivo sigue entero, con dos ajustes: la entrada de partida se siembra
/// a mano en vez de registrarla, y las pruebas de VALIDACIÓN —que miraban el registro— miran ahora
/// la corrección, que es el otro llamador de la misma pieza de código. Borrarlas habría dejado esa
/// validación sin nadie que la vigile justo el día en que perdió a la mitad de sus llamadores.</para>
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

    /// <summary>
    /// UNA ENTRADA PENDIENTE, PUESTA A MANO. Es lo que hacía <c>RegistrarAutocalificacionAsync</c>
    /// antes de apagarse, y lo que sigue habiendo en la cola de revisión de cualquier instalación.
    ///
    /// <para>Siembra los CUATRO CAMPOS QUE IMPORTAN con los mismos valores que ponía el registro
    /// —puntos releídos del criterio, estado <c>Pendiente</c>, <c>SubmittedByDeveloperId</c> puesto y
    /// <c>AssignedByUserId</c> nulo— y no por completitud: son exactamente los que mira corregir para
    /// decidir si la entrada es del desarrollador o se la asignó el líder. Sembrada de cualquier otra
    /// forma, estas pruebas estarían probando otra cosa.</para>
    ///
    /// <para>Deliberadamente NO valida. La validación es lo que las pruebas de más abajo ejercitan
    /// contra la corrección; repetirla aquí haría que un fallo en ella rompiera el montaje de todas
    /// las demás y costara el doble de leer.</para>
    ///
    /// <para>Es <c>async</c> aunque no lo necesite, para que los sitios que la llaman se lean igual
    /// que cuando llamaban al registro.</para>
    /// </summary>
    private static async Task<(bool ok, string mensaje, PointEntry? entrada)> SembrarPendiente(
        AppDbContext db, PointEntry borrador)
    {
        var criterio = await db.ScoringCriteria.AsNoTracking()
            .SingleAsync(c => c.Id == borrador.CriterionId);

        borrador.Points = criterio.DefaultPoints;
        borrador.ApprovalStatus = PointApprovalStatus.Pendiente;
        borrador.SubmittedByDeveloperId = borrador.DeveloperId;
        borrador.AssignedByUserId = null;
        borrador.Date = DateTime.UtcNow;

        db.PointEntries.Add(borrador);
        await db.SaveChangesAsync();

        return (true, "", borrador);
    }

    // ── Registrar: apagado ───────────────────────────────────────────────────────

    /// <summary>
    /// Registrar puntos por cuenta propia se retiró, y el rechazo lo da EL SERVICIO.
    ///
    /// <para>Que esté aquí y no solo en las pruebas del endpoint es el punto entero: la ruta sigue
    /// publicada —a propósito, para contestar con un motivo en vez de un 404 mudo— así que si la
    /// guarda viviera en ella, cualquier otra llamada al método seguiría pagando. Se comprueba
    /// además que no deje nada a medias: ni una fila, ni una entrada devuelta.</para>
    /// </summary>
    [Fact]
    public async Task Registrar_SeRetiro_YDiceADondeIr()
    {
        var (db, dev, crit, yo) = Entorno();

        var (ok, mensaje, entrada) = await Svc(db, yo).RegistrarAutocalificacionAsync(
            Borrador(dev, crit, minutos: 150, enlace: "https://dev.azure.com/org/_git/x/pullrequest/42"));

        Assert.False(ok);
        Assert.Null(entrada);
        Assert.Contains("pool", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.PointEntries);
    }

    /// <summary>
    /// Y se rechaza ANTES de mirar nada: con un borrador que además es inválido —criterio de equipo,
    /// que la validación habría rechazado por su cuenta— el motivo que llega sigue siendo el del
    /// apagado. Sin esto, la prueba de arriba pasaría igual con la guarda puesta al final del método,
    /// que es donde no sirve de nada.
    /// </summary>
    [Fact]
    public async Task Registrar_SeRetiro_AntesDeValidarNada()
    {
        var (db, dev, _, yo) = Entorno();
        var deEquipo = new ScoringCriterion
        {
            Name = "Sprint entregado", DefaultPoints = 10, IsActive = true, Scope = CriterionScope.Equipo
        };
        db.ScoringCriteria.Add(deEquipo); db.SaveChanges();

        var (ok, mensaje, _) = await Svc(db, yo).RegistrarAutocalificacionAsync(Borrador(dev, deEquipo));

        Assert.False(ok);
        Assert.Contains("pool", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("criterio de equipo", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    // ── La validación, que sigue viva porque corregir sigue vivo ─────────────────
    //
    // Estas tres miraban el REGISTRO. Miran ahora la CORRECCIÓN, que es el otro llamador de
    // ValidarBorradorAsync: la regla es literalmente la misma pieza de código, y lo que protege —el
    // enlace tiene que ser http, el tiempo tiene que ser posible, «cero minutos» es «no lo capturé»—
    // sigue aplicándose a todo lo que queda en la cola de revisión.

    [Fact]
    public async Task Corregir_SinTiempoNiEnlace_LosDejaVacios()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = await SembrarPendiente(db,
            Borrador(dev, crit, minutos: 30, enlace: "https://dev.azure.com/ok"));

        var (ok, _) = await Svc(db, yo).EditarAutocalificacionAsync(entrada!.Id, Borrador(dev, crit));

        Assert.True(ok);
        var g = db.PointEntries.AsNoTracking().Single();
        Assert.Null(g.MinutesSpent);
        Assert.Null(g.EvidenceUrl);
    }

    /// <summary>Cero minutos y «no lo capturé» son lo mismo: guardarlo como 0 haría que un total
    /// de «0h» pareciera un dato medido en lugar de un campo vacío.</summary>
    [Fact]
    public async Task Corregir_CeroMinutos_SeGuardaComoNulo()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = await SembrarPendiente(db, Borrador(dev, crit, minutos: 30));

        await Svc(db, yo).EditarAutocalificacionAsync(entrada!.Id, Borrador(dev, crit, minutos: 0));

        Assert.Null(db.PointEntries.AsNoTracking().Single().MinutesSpent);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]   // el jefe lo abre con el navegador
    [InlineData("javascript:alert(1)")]
    [InlineData("dev.azure.com/sin/esquema")]
    [InlineData("no es un enlace")]
    public async Task Corregir_RechazaEnlacesQueNoSonHttp(string enlace)
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = await SembrarPendiente(db, Borrador(dev, crit));

        var (ok, mensaje) = await Svc(db, yo).EditarAutocalificacionAsync(
            entrada!.Id, Borrador(dev, crit, enlace: enlace));

        Assert.False(ok);
        Assert.Contains("http", mensaje);
        Assert.Null(db.PointEntries.AsNoTracking().Single().EvidenceUrl);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(PerformanceScoringService.MaxMinutosDeclarados + 1)]
    public async Task Corregir_RechazaTiemposImposibles(int minutos)
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = await SembrarPendiente(db, Borrador(dev, crit, minutos: 30));

        var (ok, _) = await Svc(db, yo).EditarAutocalificacionAsync(
            entrada!.Id, Borrador(dev, crit, minutos: minutos));

        Assert.False(ok);
        Assert.Equal(30, db.PointEntries.AsNoTracking().Single().MinutesSpent);   // intacta
    }

    // ── Corregir mientras siga pendiente ─────────────────────────────────────────

    [Fact]
    public async Task Editar_ActualizaEvidenciaMientrasEstaPendiente()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = await SembrarPendiente(db, Borrador(dev, crit, minutos: 30));

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

        var (_, _, entrada) = await SembrarPendiente(db, Borrador(dev, crit));
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
        var (_, _, entrada) = await SembrarPendiente(db, Borrador(dev, crit, minutos: 30));

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
    ///
    /// <para>Ese «obligar a registrarla de nuevo» ya ni siquiera es una alternativa: registrar se
    /// apagó. Corregir es ahora la ÚNICA forma de arreglar una entrada rechazada, lo que sube esta
    /// prueba de conveniente a imprescindible.</para>
    /// </summary>
    [Fact]
    public async Task Editar_SePuedeAunqueEsteRechazada()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = await SembrarPendiente(db, Borrador(dev, crit, minutos: 30));

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
        var (_, _, entrada) = await SembrarPendiente(db, Borrador(dev, crit));

        var intruso = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id + 99, userId: 77);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            Svc(db, intruso).EditarAutocalificacionAsync(entrada!.Id, Borrador(dev, crit, minutos: 60)));
    }

    [Fact]
    public async Task Editar_RechazaEnlaceInvalidoSinTocarLaEntrada()
    {
        var (db, dev, crit, yo) = Entorno();
        var (_, _, entrada) = await SembrarPendiente(db,
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
