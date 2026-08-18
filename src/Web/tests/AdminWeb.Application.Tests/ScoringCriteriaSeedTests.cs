using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Catálogo de actividades puntuables y su cambio de nomenclatura.
///
/// El riesgo real de renombrar un catálogo cuyo sembrado es idempotente POR NOMBRE es acabar con
/// cada criterio dos veces: el viejo que ya estaba y el nuevo que el sembrado cree que falta. Eso
/// es lo que más se prueba aquí, junto con no pisar las descripciones que el administrador haya
/// escrito a su manera.
/// </summary>
public class ScoringCriteriaSeedTests
{
    /// <summary>
    /// Siembra una base como estaba ANTES del cambio de nomenclatura.
    ///
    /// Los criterios NUEVOS —los que nunca tuvieron otro nombre— quedan fuera, que es lo que de
    /// verdad había en aquella base: sembrarlos aquí con su nombre de hoy fingiría que la migración
    /// tiene algo que hacer con ellos y escondería si el sembrado posterior los da de alta.
    /// </summary>
    private static AppDbContext BaseHeredada()
    {
        var db = TestDb.New();
        foreach (var c in Renombrados)
            db.ScoringCriteria.Add(new ScoringCriterion
            {
                Name = c.NombreAnterior, Description = c.DescripcionAnterior, DefaultPoints = c.Puntos,
                IsActive = true, Scope = CriterionScope.Individual, CreatedAt = DateTime.UtcNow
            });
        foreach (var (nombre, descripcion, puntos) in ScoringCriteriaSeed.DeEquipo)
            db.ScoringCriteria.Add(new ScoringCriterion
            {
                Name = nombre, Description = descripcion, DefaultPoints = puntos,
                IsActive = true, Scope = CriterionScope.Equipo, CreatedAt = DateTime.UtcNow
            });
        db.SaveChanges();
        return db;
    }

    private static int TotalEsperado =>
        ScoringCriteriaSeed.Individuales.Length + ScoringCriteriaSeed.DeEquipo.Length;

    /// <summary>Los que traen nombre de la versión anterior, o sea los que la migración tiene que
    /// tocar. Los nuevos no tienen fila vieja que renombrar y por eso no cuentan en nada de esto.</summary>
    private static ScoringCriteriaSeed.Criterio[] Renombrados =>
        [.. ScoringCriteriaSeed.Individuales.Where(c => c.VieneDeOtroNombre)];

    // ── Integridad del catálogo ──────────────────────────────────────────────

    [Fact]
    public void Catalogo_NoTieneNombresRepetidos()
    {
        var nuevos = ScoringCriteriaSeed.Individuales.Select(c => c.Nombre)
                     .Concat(ScoringCriteriaSeed.DeEquipo.Select(c => c.Nombre)).ToList();
        Assert.Equal(nuevos.Count, nuevos.Distinct(StringComparer.Ordinal).Count());

        // Y tampoco entre los anteriores: dos entradas migrando desde el mismo nombre viejo harían
        // que la segunda se saltara en silencio.
        var viejos = ScoringCriteriaSeed.Individuales.Select(c => c.NombreAnterior).ToList();
        Assert.Equal(viejos.Count, viejos.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Catalogo_TodoTieneDescripcionYPuntosDistintosDeCero()
    {
        foreach (var c in ScoringCriteriaSeed.Individuales)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Descripcion), $"«{c.Nombre}» sin descripción");
            Assert.False(string.IsNullOrWhiteSpace(c.DescripcionAnterior), $"«{c.Nombre}» sin descripción anterior");
            Assert.NotEqual(0, c.Puntos);
        }
    }

    /// <summary>
    /// Los puntos NO cambian con el renombrado: tocarlos alteraría hacia atrás la comparación entre
    /// meses del histórico, y el ranking de un mes cerrado dejaría de significar lo mismo.
    /// </summary>
    [Fact]
    public async Task Migrar_NoTocaLosPuntos()
    {
        using var db = BaseHeredada();
        var antes = db.ScoringCriteria.AsNoTracking().ToDictionary(c => c.Name, c => c.DefaultPoints);

        await ScoringCriteriaSeed.MigrarNomenclaturaAsync(db);

        foreach (var c in Renombrados)
            Assert.Equal(antes[c.NombreAnterior], db.ScoringCriteria.AsNoTracking().Single(x => x.Name == c.Nombre).DefaultPoints);
    }

    // ── Migración ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Migrar_RenombraYReescribeLaDescripcion()
    {
        using var db = BaseHeredada();
        var muestra = Renombrados[0];

        int cambiados = await ScoringCriteriaSeed.MigrarNomenclaturaAsync(db);

        Assert.Equal(Renombrados.Length, cambiados);
        var g = db.ScoringCriteria.AsNoTracking().Single(c => c.Name == muestra.Nombre);
        Assert.Equal(muestra.Descripcion, g.Description);
        Assert.Empty(db.ScoringCriteria.AsNoTracking().Where(c => c.Name == muestra.NombreAnterior));
    }

    /// <summary>
    /// Si el administrador reescribió la descripción, ese texto es una decisión suya. Se renombra
    /// el criterio pero su redacción se respeta.
    /// </summary>
    [Fact]
    public async Task Migrar_RespetaLaDescripcionQueEscribioElAdministrador()
    {
        using var db = BaseHeredada();
        var muestra = Renombrados[0];

        var fila = db.ScoringCriteria.Single(c => c.Name == muestra.NombreAnterior);
        fila.Description = "Ojo: aquí contamos también las entregas parciales.";
        db.SaveChanges();

        await ScoringCriteriaSeed.MigrarNomenclaturaAsync(db);

        var g = db.ScoringCriteria.AsNoTracking().Single(c => c.Name == muestra.Nombre);
        Assert.Equal("Ojo: aquí contamos también las entregas parciales.", g.Description);
    }

    /// <summary>
    /// El motivo de existir de todo esto: migrar y sembrar sobre una base heredada tiene que dejar
    /// UNA fila por criterio, no dos.
    /// </summary>
    [Fact]
    public async Task MigrarYSembrar_NoDuplicaNada()
    {
        using var db = BaseHeredada();

        await ScoringCriteriaSeed.MigrarNomenclaturaAsync(db);
        await ScoringCriteriaSeed.SembrarAsync(db);

        var nombres = db.ScoringCriteria.AsNoTracking().Select(c => c.Name).ToList();
        Assert.Equal(TotalEsperado, nombres.Count);
        Assert.Equal(nombres.Count, nombres.Distinct(StringComparer.Ordinal).Count());

        // Y ni rastro de la nomenclatura vieja.
        foreach (var c in Renombrados)
            Assert.DoesNotContain(c.NombreAnterior, nombres);
    }

    [Fact]
    public async Task MigrarYSembrar_EsIdempotente()
    {
        using var db = BaseHeredada();

        await ScoringCriteriaSeed.MigrarNomenclaturaAsync(db);
        await ScoringCriteriaSeed.SembrarAsync(db);
        int trasLaPrimera = db.ScoringCriteria.Count();

        Assert.Equal(0, await ScoringCriteriaSeed.MigrarNomenclaturaAsync(db));   // ya no queda nada que renombrar
        await ScoringCriteriaSeed.SembrarAsync(db);

        Assert.Equal(trasLaPrimera, db.ScoringCriteria.Count());
    }

    /// <summary>Base nueva: el sembrado solo, sin migración previa, deja el catálogo completo.</summary>
    [Fact]
    public async Task Sembrar_EnBaseVaciaDejaElCatalogoCompleto()
    {
        using var db = TestDb.New();

        await ScoringCriteriaSeed.SembrarAsync(db);

        Assert.Equal(TotalEsperado, db.ScoringCriteria.Count());
        Assert.Equal(ScoringCriteriaSeed.DeEquipo.Length,
            db.ScoringCriteria.Count(c => c.Scope == CriterionScope.Equipo));
    }

    /// <summary>
    /// Renombrar y no borrar-e-insertar es lo que mantiene vivo el histórico: los puntos que ya se
    /// otorgaron siguen colgando del mismo criterio, con el mismo Id.
    /// </summary>
    [Fact]
    public async Task Migrar_NoRompeLosPuntosYaOtorgados()
    {
        using var db = BaseHeredada();
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();

        var muestra = Renombrados[0];
        int criterioId = db.ScoringCriteria.Single(c => c.Name == muestra.NombreAnterior).Id;
        db.PointEntries.Add(new PointEntry
        {
            DeveloperId = dev.Id, CriterionId = criterioId, Points = muestra.Puntos,
            Year = 2026, Month = 7, Date = DateTime.UtcNow, ApprovalStatus = PointApprovalStatus.Aprobado
        });
        db.SaveChanges();

        await ScoringCriteriaSeed.MigrarNomenclaturaAsync(db);

        var entrada = db.PointEntries.Include(p => p.Criterion).AsNoTracking().Single();
        Assert.Equal(criterioId, entrada.CriterionId);          // mismo Id: nada que reapuntar
        Assert.Equal(muestra.Nombre, entrada.Criterion.Name);   // y ya con el nombre nuevo
    }

    /// <summary>
    /// Si alguien creó a mano un criterio con el nombre nuevo, renombrar dejaría dos homónimos.
    /// En ese caso se prefiere no tocar nada.
    /// </summary>
    [Fact]
    public async Task Migrar_NoRenombraSiElNombreNuevoYaEstaOcupado()
    {
        using var db = BaseHeredada();
        var muestra = Renombrados[0];
        db.ScoringCriteria.Add(new ScoringCriterion
        {
            Name = muestra.Nombre, Description = "Lo creé yo antes de la actualización.",
            DefaultPoints = 1, IsActive = true, Scope = CriterionScope.Individual, CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        await ScoringCriteriaSeed.MigrarNomenclaturaAsync(db);

        Assert.Single(db.ScoringCriteria.AsNoTracking().Where(c => c.Name == muestra.Nombre));
        Assert.Single(db.ScoringCriteria.AsNoTracking().Where(c => c.Name == muestra.NombreAnterior));
    }

    [Fact]
    public async Task Migrar_EnBaseVaciaNoHaceNada()
    {
        using var db = TestDb.New();
        Assert.Equal(0, await ScoringCriteriaSeed.MigrarNomenclaturaAsync(db));
        Assert.Empty(db.ScoringCriteria);
    }
}
