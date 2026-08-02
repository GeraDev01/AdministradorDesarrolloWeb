using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// El aviso de compromiso próximo a vencer. Lo que se prueba es la decisión de CUÁNDO avisar: un
/// aviso diario durante dos semanas se deja de leer, y uno que llega el día después de vencer no
/// sirve para nada. Se avisa al cruzar tres umbrales y no más.
/// </summary>
public class CommitmentAlertTests
{
    private static readonly DateTime Hoy = new(2026, 8, 10);

    private static (Requirement, int) Req(int diasParaVencer, RequirementStatus estado = RequirementStatus.EnDesarrollo, int devId = 7)
        => (new Requirement
        {
            Id = 100 + diasParaVencer + (int)estado * 1000,
            Title = "Portal de pagos",
            Status = estado,
            CommittedDeliveryDate = Hoy.AddDays(diasParaVencer)
        }, devId);

    private static List<AvisoCompromiso> Calcular(params (Requirement, int)[] reqs)
        => CommitmentAlertService.Calcular(reqs, Hoy);

    [Theory]
    [InlineData(3,  "vence en 3 días")]
    [InlineData(1,  "vence mañana")]
    [InlineData(0,  "vence HOY")]
    [InlineData(-4, "venció hace 4 día(s)")]
    public void AvisaAlCruzarUnUmbral_ConTextoQueSeEntiende(int dias, string esperado)
    {
        var a = Assert.Single(Calcular(Req(dias)));
        Assert.Equal(esperado, a.Etiqueta);
        Assert.Equal(dias, a.DiasRestantes);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(4)]
    public void LejosDeVencer_NoAvisa(int dias) => Assert.Empty(Calcular(Req(dias)));

    [Fact]
    public void LosDiasIntermedios_ReutilizanLaClave_ParaQueElDedupeLosCalle()
    {
        // Con 3 y con 2 días el aviso es el MISMO (mismo umbral cruzado): la clave se repite y el
        // dedupe del servicio de avisos evita el recordatorio diario.
        var a3 = Assert.Single(Calcular(Req(3)));
        var otro = CommitmentAlertService.Calcular(
            [(new Requirement { Id = a3.RequirementId, Title = "Portal de pagos", Status = RequirementStatus.EnDesarrollo,
                                CommittedDeliveryDate = Hoy.AddDays(3) }, 7)],
            Hoy.AddDays(1));   // un día después: quedan 2

        Assert.Equal(a3.DedupeKey, Assert.Single(otro).DedupeKey);
    }

    [Fact]
    public void AlCruzarElSiguienteUmbral_LaClaveCambia_YVuelveAAvisar()
    {
        var tres = Assert.Single(Calcular(Req(3)));
        var manana = Assert.Single(Calcular(Req(1)));
        Assert.NotEqual(tres.DedupeKey, manana.DedupeKey);
    }

    [Fact]
    public void SiElAdministradorMueveLaFecha_VuelveAAvisar()
    {
        // La fecha va en la clave: mover el compromiso es información nueva y merece un aviso,
        // aunque el umbral sea el mismo.
        var original = new Requirement { Id = 55, Title = "X", Status = RequirementStatus.EnDesarrollo, CommittedDeliveryDate = Hoy };
        var movido   = new Requirement { Id = 55, Title = "X", Status = RequirementStatus.EnDesarrollo, CommittedDeliveryDate = Hoy.AddDays(-1) };

        Assert.NotEqual(
            CommitmentAlertService.Calcular([(original, 7)], Hoy).Single().DedupeKey,
            CommitmentAlertService.Calcular([(movido, 7)], Hoy).Single().DedupeKey);
    }

    [Theory]
    [InlineData(RequirementStatus.Entregado)]
    [InlineData(RequirementStatus.Cancelado)]
    public void LoEntregadoOCancelado_NoAvisa_PorVencidoQueEste(RequirementStatus estado)
    {
        // Ya no hay nada que apurar; insistir sería ruido puro.
        Assert.Empty(Calcular(Req(-30, estado)));
    }

    [Fact]
    public void SinFechaComprometida_NoAvisa()
    {
        var sinFecha = new Requirement { Id = 1, Title = "X", Status = RequirementStatus.EnDesarrollo };
        Assert.Empty(CommitmentAlertService.Calcular([(sinFecha, 7)], Hoy));
    }

    [Fact]
    public void UnRequerimientoConDosAsignados_AvisaACadaUno()
    {
        var r = new Requirement { Id = 9, Title = "X", Status = RequirementStatus.EnDesarrollo, CommittedDeliveryDate = Hoy };

        var avisos = CommitmentAlertService.Calcular([(r, 7), (r, 8)], Hoy);

        Assert.Equal(2, avisos.Count);
        Assert.Equal([7, 8], avisos.Select(a => a.DeveloperId).Order());
        // Misma clave para ambos: el dedupe es POR usuario, así que no se pisan entre sí.
        Assert.Single(avisos.Select(a => a.DedupeKey).Distinct());
    }
}
