using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Resolución del buzón que recibe los escalamientos de SLA vencido. Se prueba a través de la
/// misma lógica de separación/validación que usa SlaNotificationService.
/// </summary>
public class SlaEscalationRecipientTests
{
    private static SettingsService NuevoSettings(out AuditService audit)
    {
        var db = TestDb.New();
        var user = Ctx.As(Administrador_Desarrollo_Web.Models.UserRole.Admin);
        audit = new AuditService(db, user);
        return new SettingsService(db, audit);
    }

    /// <summary>Réplica de la regla de SlaNotificationService.BuzonesEscalamiento (que es privada).</summary>
    private static List<string> Resolver(SettingsService s)
    {
        var configurado = s.Get(SettingsService.Keys.SlaEscalationEmail);
        var destinos = (configurado ?? "")
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(d => d.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (destinos.Count > 0) return destinos;
        var propio = s.Get(SettingsService.Keys.EmailAddress);
        return string.IsNullOrWhiteSpace(propio) ? [] : [propio];
    }

    [Fact]
    public void SeUsaElCorreoDelJefeCuandoEstaConfigurado()
    {
        var s = NuevoSettings(out _);
        s.Set(SettingsService.Keys.EmailAddress, "app@empresa.com");
        s.Set(SettingsService.Keys.SlaEscalationEmail, "líder@empresa.com");

        Assert.Equal(["líder@empresa.com"], Resolver(s));
    }

    [Fact]
    public void AdmiteVariosCorreosSeparadosPorPuntoYComaOComa()
    {
        var s = NuevoSettings(out _);
        s.Set(SettingsService.Keys.SlaEscalationEmail, " líder@empresa.com ; gerente@empresa.com , líder@empresa.com ");

        var destinos = Resolver(s);

        Assert.Equal(2, destinos.Count);                  // el duplicado se descarta
        Assert.Contains("líder@empresa.com", destinos);
        Assert.Contains("gerente@empresa.com", destinos);
    }

    [Fact]
    public void SinConfigurarSeCaeALaCuentaDeLaAplicacion()
    {
        var s = NuevoSettings(out _);
        s.Set(SettingsService.Keys.EmailAddress, "app@empresa.com");

        Assert.Equal(["app@empresa.com"], Resolver(s));
    }

    [Fact]
    public void LasEntradasSinArrobaSeIgnoran()
    {
        var s = NuevoSettings(out _);
        s.Set(SettingsService.Keys.EmailAddress, "app@empresa.com");
        s.Set(SettingsService.Keys.SlaEscalationEmail, "esto-no-es-correo; líder@empresa.com");

        Assert.Equal(["líder@empresa.com"], Resolver(s));
    }

    [Fact]
    public void SinNadaConfiguradoNoHayDestinatarios()
    {
        var s = NuevoSettings(out _);
        Assert.Empty(Resolver(s));
    }
}
