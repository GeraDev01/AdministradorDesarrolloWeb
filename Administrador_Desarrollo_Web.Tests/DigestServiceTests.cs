using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>Resumen por correo: composición del texto, selección de destinatarios y recopilación de cifras.</summary>
public class DigestServiceTests
{
    [Fact]
    public void Componer_incluyeLasCifras()
    {
        var d = new DigestData(SlaVencidos: 2, SlaPorVencer24: 1, SlaActivos: 5, SugerenciasNuevas: 3,
            DesplieguesUlt24: 4, TicketsSyncUlt24: 7, ReqPorEntregar7d: 6);
        var texto = DigestService.Componer(d, new DateTime(2026, 7, 27, 9, 0, 0), frecuenciaDias: 1);

        Assert.Contains("2 vencido", texto);
        Assert.Contains("1 por vencer", texto);
        Assert.Contains("5 activo", texto);
        Assert.Contains("3 sin atender", texto);
        Assert.Contains("4 despliegue", texto);
        Assert.Contains("7 ticket", texto);
        Assert.Contains("6 requerimiento", texto);
        Assert.Contains("Resumen del equipo", texto);
    }

    [Fact]
    public void Componer_semanal_cambiaElTitulo()
        => Assert.Contains("Resumen semanal", DigestService.Componer(
            new DigestData(0, 0, 0, 0, 0, 0, 0), DateTime.Now, frecuenciaDias: 7));

    [Fact]
    public void Destinatarios_precedencia()
    {
        // 1) los configurados ganan
        Assert.Equal(new[] { "a@x.com", "b@x.com" },
            DigestService.Destinatarios("a@x.com; b@x.com", "esc@x.com", "propio@x.com"));
        // 2) si no hay, el de escalamiento
        Assert.Equal(new[] { "esc@x.com" }, DigestService.Destinatarios(" ", "esc@x.com", "propio@x.com"));
        // 3) si no, la propia cuenta
        Assert.Equal(new[] { "propio@x.com" }, DigestService.Destinatarios(null, null, "propio@x.com"));
        // 4) nada válido → vacío
        Assert.Empty(DigestService.Destinatarios(null, null, "sin-arroba"));
        // separa por ',' y ';' y descarta lo que no tenga '@'
        Assert.Equal(new[] { "a@x.com", "b@x.com" }, DigestService.Destinatarios("a@x.com, basura ; b@x.com", null, null));
    }

    [Fact]
    public void HayAlgoQueReportar_soloCuandoAlgoNoEsCero()
    {
        Assert.False(DigestService.HayAlgoQueReportar(new DigestData(0, 0, 0, 0, 0, 0, 0)));
        Assert.True(DigestService.HayAlgoQueReportar(new DigestData(0, 0, 1, 0, 0, 0, 0)));
        Assert.True(DigestService.HayAlgoQueReportar(new DigestData(0, 0, 0, 0, 0, 0, 3)));
    }

    [Fact]
    public void Recopilar_cuentaSlaYSugerencias()
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = 1, FullName = "Dev", IsActive = true });
        db.SaveChanges();
        var now = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);

        db.SlaCommitments.AddRange(
            new SlaCommitment { DeveloperId = 1, Status = SlaStatus.Activo, DueAtUtc = now.AddHours(-1), CreatedAt = now },  // vencido (activo pasado)
            new SlaCommitment { DeveloperId = 1, Status = SlaStatus.Activo, DueAtUtc = now.AddHours(12), CreatedAt = now },  // por vencer
            new SlaCommitment { DeveloperId = 1, Status = SlaStatus.Activo, DueAtUtc = now.AddHours(48), CreatedAt = now },  // activo lejano
            new SlaCommitment { DeveloperId = 1, Status = SlaStatus.Vencido, DueAtUtc = now.AddHours(-5), CreatedAt = now }); // vencido
        db.Suggestions.Add(new Suggestion { Title = "s", Body = "b", CreatedByUserId = 1, Status = SuggestionStatus.Nueva, CreatedAt = now });
        db.SaveChanges();

        var data = DigestService.Recopilar(db, now);
        Assert.Equal(3, data.SlaActivos);
        Assert.Equal(2, data.SlaVencidos);      // 1 activo pasado + 1 con estado Vencido
        Assert.Equal(1, data.SlaPorVencer24);
        Assert.Equal(1, data.SugerenciasNuevas);
    }
}
