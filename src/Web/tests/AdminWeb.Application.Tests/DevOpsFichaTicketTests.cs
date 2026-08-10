using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Integraciones;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Ficha de un ticket de DevOps: regresiones (bugs hijos) y devoluciones al desarrollador.
///
/// El conteo de devoluciones es lo que hay que cuidar. No es «cuántas veces se lo asignaron»: la
/// primera no es una devolución, y que vuelva a alguien que ya lo tenía sin que nadie más lo
/// trabajara en medio tampoco. Lo que cuenta —y lo que duele— es que regrese después de haber pasado
/// por otras manos. Portadas del escritorio.
/// </summary>
public class DevOpsFichaTicketTests
{
    private static CambioDeAsignacionDevOps A(int dia, string? de, string? a, string? correo = null) =>
        new(new DateTime(2026, 8, dia), de, a, correo);

    private static DevOpsTicketFicha Ficha(params CambioDeAsignacionDevOps[] cambios) =>
        new(4821, [], cambios);

    // ── Devoluciones ─────────────────────────────────────────────────────────────

    [Fact]
    public void PrimeraAsignacion_NoEsUnaDevolucion()
    {
        Assert.Equal(0, Ficha(A(1, null, "Ana")).DevolucionesA("Ana", null));
    }

    [Fact]
    public void VolverDespuesDeOtro_CuentaComoDevolucion()
    {
        var ficha = Ficha(
            A(1, null, "Ana"),
            A(2, "Ana", "Beto"),
            A(3, "Beto", "Ana"));

        Assert.Equal(1, ficha.DevolucionesA("Ana", null));
        Assert.Equal(0, ficha.DevolucionesA("Beto", null));   // a Beto nunca se lo devolvieron
    }

    [Fact]
    public void VariasVueltas_SeCuentanTodas()
    {
        var ficha = Ficha(
            A(1, null, "Ana"),
            A(2, "Ana", "Beto"),
            A(3, "Beto", "Ana"),
            A(4, "Ana", "Caro"),
            A(5, "Caro", "Ana"));

        Assert.Equal(2, ficha.DevolucionesA("Ana", null));
    }

    /// <summary>
    /// Que quede sin asignar no es «lo tuvo alguien más»: nadie lo trabajó en medio, así que
    /// recuperarlo no es una devolución.
    /// </summary>
    [Fact]
    public void PasarPorSinAsignar_NoCuenta()
    {
        var ficha = Ficha(
            A(1, null, "Ana"),
            A(2, "Ana", null),
            A(3, null, "Ana"));

        Assert.Equal(0, ficha.DevolucionesA("Ana", null));
    }

    [Fact]
    public void ElOrdenLoDaLaFecha_NoElOrdenDeLaLista()
    {
        // Llegan desordenados a propósito: la API los devuelve por revisión, no por fecha.
        var ficha = Ficha(
            A(3, "Beto", "Ana"),
            A(1, null, "Ana"),
            A(2, "Ana", "Beto"));

        Assert.Equal(1, ficha.DevolucionesA("Ana", null));
    }

    /// <summary>
    /// El correo manda: el nombre para mostrar de DevOps trae o quita acentos y segundos nombres
    /// según cómo se capturó, y empatar solo por él produce falsos negativos.
    /// </summary>
    [Fact]
    public void ElCorreoDesempata_AunqueElNombreNoCoincida()
    {
        var ficha = Ficha(
            A(1, null, "Ana Pérez", "ana@empresa.com"),
            A(2, "Ana Pérez", "Beto", "beto@empresa.com"),
            A(3, "Beto", "ANA PEREZ", "ana@empresa.com"));   // sin acento y en mayúsculas

        Assert.Equal(1, ficha.DevolucionesA("Ana Pérez", "ana@empresa.com"));
    }

    [Fact]
    public void SinPersonaQueBuscar_NoCuentaNada()
    {
        var ficha = Ficha(A(1, null, "Ana"), A(2, "Ana", "Beto"), A(3, "Beto", "Ana"));

        Assert.Equal(0, ficha.DevolucionesA(null, null));
        Assert.Equal(0, ficha.DevolucionesA("  ", ""));
    }

    [Fact]
    public void HistorialVacio_NoRevienta()
    {
        var ficha = Ficha();

        Assert.Equal(0, ficha.DevolucionesA("Ana", null));
        Assert.Equal(0, ficha.Regresiones);
        Assert.Equal(0, ficha.Manos);
    }

    [Fact]
    public void Manos_CuentaPersonasDistintas_NoCambiosDeDueno()
    {
        var ficha = Ficha(
            A(1, null, "Ana"),
            A(2, "Ana", "Beto"),
            A(3, "Beto", "Ana"));

        Assert.Equal(2, ficha.Manos);
    }

    // ── Regresiones ──────────────────────────────────────────────────────────────

    /// <summary>
    /// «Resolved» NO cuenta como cerrado: en DevOps significa «arreglado, falta verificar», y un bug
    /// que nadie ha verificado todavía puede volver. Solo Closed/Done/Completed —y los cancelados—
    /// dejan de pesar.
    /// </summary>
    [Fact]
    public void Regresiones_CuentaLosBugsHijosYSeparaLosQueSiguenVivos()
    {
        var ficha = new DevOpsTicketFicha(4821,
        [
            new BugHijoDevOps(1, "Se rompió el alta", "Active", "u1"),
            new BugHijoDevOps(2, "Error al exportar", "Closed", "u2"),
            new BugHijoDevOps(3, "Cálculo equivocado", "Resolved", "u3"),
            new BugHijoDevOps(4, "Duplicado", "Removed", "u4"),
        ], []);

        Assert.Equal(4, ficha.Regresiones);           // haberlas habido, las hubo
        Assert.Equal(2, ficha.RegresionesAbiertas);   // Active y Resolved siguen vivos
    }

    // ── Lo que el proceso exige de cada ticket ───────────────────────────────────

    /// <summary>
    /// El campo Priority no sirve para saber si alguien la definió: DevOps le pone 2 por omisión a
    /// todo lo que se crea. Lo que decide es que alguien la haya fijado desde la aplicación.
    /// </summary>
    [Fact]
    public void SinPrioridadDefinida_NoDependeDelValorDePriority()
    {
        var recienLlegado = new DevOpsTicket { ExternalId = 1, Priority = "2" };
        Assert.True(recienLlegado.SinPrioridadDefinida);

        recienLlegado.PriorityConfirmedAt = DateTime.UtcNow;
        Assert.False(recienLlegado.SinPrioridadDefinida);
    }

    [Fact]
    public void SinEstimar_SoloAplicaALoQueEstaAsignado()
    {
        var deNadie = new DevOpsTicket { ExternalId = 1, AssignedTo = "" };
        Assert.False(deNadie.SinEstimar);   // sin dueño no hay a quién exigirle

        var deAna = new DevOpsTicket { ExternalId = 2, AssignedTo = "Ana" };
        Assert.True(deAna.SinEstimar);

        deAna.EstimatedHours = 4.5;
        Assert.False(deAna.SinEstimar);
    }
}
