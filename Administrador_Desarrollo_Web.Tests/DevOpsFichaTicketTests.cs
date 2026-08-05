using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Ficha de un ticket de DevOps: regresiones (bugs hijos) y devoluciones al desarrollador.
///
/// El conteo de devoluciones es lo que hay que cuidar. No es «cuántas veces se lo asignaron»: la
/// primera no es una devolución, y que vuelva a alguien que ya lo tenía sin que nadie más lo
/// trabajara en medio tampoco. Lo que cuenta —y lo que duele— es que regrese después de haber
/// pasado por otras manos.
/// </summary>
public class DevOpsFichaTicketTests
{
    private static DevOpsCambioAsignacion A(int dia, string? de, string? a, string? email = null) =>
        new(new DateTime(2026, 8, dia), de, a, email);

    private static DevOpsFichaTicket Ficha(params DevOpsCambioAsignacion[] cambios) =>
        new(4821, [], cambios);

    // ── Devoluciones ─────────────────────────────────────────────────────────

    [Fact]
    public void PrimeraAsignacion_NoEsUnaDevolucion()
    {
        var f = Ficha(A(1, null, "Ana"));
        Assert.Equal(0, f.DevolucionesA("Ana", null));
    }

    [Fact]
    public void VolverDespuesDeOtro_CuentaComoDevolucion()
    {
        var f = Ficha(
            A(1, null,  "Ana"),
            A(2, "Ana", "Beto"),
            A(3, "Beto", "Ana"));

        Assert.Equal(1, f.DevolucionesA("Ana", null));
        Assert.Equal(0, f.DevolucionesA("Beto", null));   // a Beto nunca se lo devolvieron
    }

    [Fact]
    public void VariasVueltas_SeCuentanTodas()
    {
        var f = Ficha(
            A(1, null,   "Ana"),
            A(2, "Ana",  "Beto"),
            A(3, "Beto", "Ana"),
            A(4, "Ana",  "Caro"),
            A(5, "Caro", "Ana"));

        Assert.Equal(2, f.DevolucionesA("Ana", null));
    }

    /// <summary>
    /// Que quede sin asignar no es «lo tuvo alguien más»: nadie lo trabajó en medio, así que
    /// recuperarlo no es una devolución.
    /// </summary>
    [Fact]
    public void PasarPorSinAsignar_NoCuenta()
    {
        var f = Ficha(
            A(1, null,  "Ana"),
            A(2, "Ana", null),
            A(3, null,  "Ana"));

        Assert.Equal(0, f.DevolucionesA("Ana", null));
    }

    [Fact]
    public void ElOrdenLoDaLaFecha_NoElOrdenDeLaLista()
    {
        // Llegan desordenados a propósito.
        var f = Ficha(
            A(3, "Beto", "Ana"),
            A(1, null,   "Ana"),
            A(2, "Ana",  "Beto"));

        Assert.Equal(1, f.DevolucionesA("Ana", null));
    }

    /// <summary>
    /// El correo manda: el nombre para mostrar de DevOps trae o quita acentos y segundos nombres
    /// según cómo se capturó, y empatar solo por él produce falsos negativos.
    /// </summary>
    [Fact]
    public void ElCorreoDesempata_AunqueElNombreNoCoincida()
    {
        var f = Ficha(
            A(1, null, "Ana Pérez", "ana@empresa.com"),
            A(2, "Ana Pérez", "Beto", "beto@empresa.com"),
            A(3, "Beto", "ANA PEREZ", "ana@empresa.com"));   // sin acento y en mayúsculas

        Assert.Equal(1, f.DevolucionesA("Ana Pérez", "ana@empresa.com"));
    }

    [Fact]
    public void SinPersonaQueBuscar_NoCuentaNada()
    {
        var f = Ficha(A(1, null, "Ana"), A(2, "Ana", "Beto"), A(3, "Beto", "Ana"));
        Assert.Equal(0, f.DevolucionesA(null, null));
        Assert.Equal(0, f.DevolucionesA("  ", ""));
    }

    [Fact]
    public void HistorialVacio_NoRevienta()
    {
        var f = Ficha();
        Assert.Equal(0, f.DevolucionesA("Ana", null));
        Assert.Equal(0, f.Regresiones);
    }

    // ── Regresiones ──────────────────────────────────────────────────────────

    /// <summary>
    /// «Resolved» NO cuenta como cerrado: en DevOps significa «arreglado, falta verificar», y un
    /// bug que nadie ha verificado todavía puede volver. Solo Closed/Done/Completed —y los
    /// cancelados— dejan de pesar.
    /// </summary>
    [Fact]
    public void Regresiones_CuentaLosBugsHijosYSeparaLosQueSiguenVivos()
    {
        var f = new DevOpsFichaTicket(4821,
        [
            new DevOpsBugHijo(1, "Se rompió el alta",  "Active",   "u1"),
            new DevOpsBugHijo(2, "Error al exportar",  "Closed",   "u2"),
            new DevOpsBugHijo(3, "Cálculo equivocado", "Resolved", "u3"),
            new DevOpsBugHijo(4, "Duplicado",          "Removed",  "u4"),
        ], []);

        Assert.Equal(4, f.Regresiones);           // haberlas habido, las hubo
        Assert.Equal(2, f.RegresionesAbiertas);   // Active y Resolved siguen vivos
    }

    // ── Lo que exige el proceso ──────────────────────────────────────────────

    /// <summary>
    /// El campo Priority no sirve para saber si alguien la definió: DevOps le pone 2 por omisión a
    /// todo lo que se crea. Lo que decide es que alguien la haya fijado desde la aplicación.
    /// </summary>
    [Fact]
    public void SinPrioridadDefinida_NoDependeDelValorDePriority()
    {
        var reciénLlegado = new DevOpsTicket { ExternalId = 1, Priority = "2" };
        Assert.True(reciénLlegado.SinPrioridadDefinida);

        reciénLlegado.PriorityConfirmedAt = DateTime.UtcNow;
        Assert.False(reciénLlegado.SinPrioridadDefinida);
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
