using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Cuándo se vuelve a alertar de un SLA. El equilibrio importa: repetir cada ciclo convierte el
/// aviso en ruido que se cierra sin leer; callarse deja pasar vencimientos.
/// </summary>
public class SlaAlertTrackerTests
{
    private static readonly DateTime Ahora = new(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);

    private static SlaCommitment Sla(int id, DateTime? venceUtc = null) => new()
    {
        Id = id,
        DeveloperId = 1,
        Status = SlaStatus.Activo,
        DueAtUtc = venceUtc ?? Ahora.AddDays(1),
        NextReminderAtUtc = Ahora.AddMinutes(-1)
    };

    [Fact]
    public void LaPrimeraVezAvisaDeTodo()
    {
        var t = new SlaAlertTracker();
        var nuevos = t.Nuevos([Sla(1), Sla(2)], Ahora);
        Assert.Equal(2, nuevos.Count);
    }

    [Fact]
    public void NoRepiteElMismoAvisoEnElSiguienteCiclo()
    {
        var t = new SlaAlertTracker();
        var lista = new[] { Sla(1), Sla(2) };

        t.Nuevos(lista, Ahora);
        var segunda = t.Nuevos(lista, Ahora.AddMinutes(5));

        Assert.Empty(segunda);
    }

    [Fact]
    public void AvisaEnCuantoAparecUnoNuevo()
    {
        var t = new SlaAlertTracker();
        t.Nuevos([Sla(1)], Ahora);

        var nuevos = t.Nuevos([Sla(1), Sla(2)], Ahora.AddMinutes(5));

        Assert.Single(nuevos);
        Assert.Equal(2, nuevos[0].Id);
    }

    [Fact]
    public void VuelveAAvisarCuandoUnoPasaAEstarFueraDePlazo()
    {
        var t = new SlaAlertTracker();
        var enPlazo = Sla(1, venceUtc: Ahora.AddHours(1));
        t.Nuevos([enPlazo], Ahora);

        // Mismo compromiso, pero ahora ya venció: es información nueva y más grave.
        var vencido = Sla(1, venceUtc: Ahora.AddHours(1));
        var nuevos = t.Nuevos([vencido], Ahora.AddHours(2));

        Assert.Single(nuevos);
        Assert.Equal(1, nuevos[0].Id);
    }

    [Fact]
    public void NoAvisaDosVecesDelMismoVencimiento()
    {
        var t = new SlaAlertTracker();
        var sla = Sla(1, venceUtc: Ahora.AddHours(1));
        t.Nuevos([sla], Ahora);
        t.Nuevos([sla], Ahora.AddHours(2));        // primer aviso de vencido

        var tercera = t.Nuevos([sla], Ahora.AddHours(3));

        Assert.Empty(tercera);
    }

    [Fact]
    public void UnoAtendidoSeOlvida_YSuSiguienteRecordatorioVuelveAAvisar()
    {
        var t = new SlaAlertTracker();
        t.Nuevos([Sla(1)], Ahora);

        // Se comentó el ticket: deja de estar pendiente.
        t.Nuevos([], Ahora.AddMinutes(5));

        // Horas después toca el siguiente recordatorio del MISMO compromiso.
        var nuevos = t.Nuevos([Sla(1)], Ahora.AddHours(6));

        Assert.Single(nuevos);
    }

    [Fact]
    public void ReiniciarOlvidaTodo()
    {
        var t = new SlaAlertTracker();
        t.Nuevos([Sla(1)], Ahora);
        t.Reiniciar();

        Assert.Single(t.Nuevos([Sla(1)], Ahora.AddMinutes(1)));
    }

    [Fact]
    public void SinPendientesNoHayAvisos()
    {
        var t = new SlaAlertTracker();
        Assert.Empty(t.Nuevos([], Ahora));
    }
}
