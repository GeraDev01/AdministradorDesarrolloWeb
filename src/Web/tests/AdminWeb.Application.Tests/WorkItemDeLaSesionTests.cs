using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// EN QUÉ TICKET SE ESCRIBE — que es la pregunta cara de toda esta función.
///
/// <para>Equivocarse aquí no produce un texto feo: produce un comentario en el ticket de otro
/// cliente, firmado por la cuenta compartida y sin forma de retirarlo. Por eso la resolución vive
/// aparte de quien publica y se prueba sin red.</para>
/// </summary>
public class WorkItemDeLaSesionTests : IDisposable
{
    private readonly List<AppDbContext> _contextos = [];

    public void Dispose()
    {
        foreach (var c in _contextos) c.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext Base()
    {
        var db = TestDb.New();
        _contextos.Add(db);
        return db;
    }

    private static int NuevoDev(AppDbContext db, string nombre = "Ana")
    {
        var d = new Developer { FullName = nombre, IsActive = true };
        db.Developers.Add(d);
        db.SaveChanges();
        return d.Id;
    }

    private static int NuevaSesion(AppDbContext db, int devId, int? reqId = null, int? actividadId = null)
    {
        var s = new WorkSession
        {
            DeveloperId = devId,
            RequirementId = reqId,
            ActivityId = actividadId,
            StartedAt = DateTime.UtcNow,
            LastResumedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            Status = WorkSessionStatus.Activa
        };
        db.WorkSessions.Add(s);
        db.SaveChanges();
        return s.Id;
    }

    // ── Requerimientos ───────────────────────────────────────────────────────────

    private static int NuevoRequerimiento(
        AppDbContext db, RequirementSource origen = RequirementSource.AzureDevOps, string? externo = "4321")
    {
        var r = new Requirement
        {
            Title = "Corregir el cálculo",
            Status = RequirementStatus.Estimado,
            CreatedAt = DateTime.UtcNow,
            Source = origen,
            ExternalId = externo
        };
        db.Requirements.Add(r);
        db.SaveChanges();
        return r.Id;
    }

    private static void Asignar(AppDbContext db, int reqId, int devId)
    {
        db.Assignments.Add(new Assignment { RequirementId = reqId, DeveloperId = devId });
        db.SaveChanges();
    }

    [Fact]
    public async Task UnRequerimientoDeDevOpsAsignadoAMi_daSuWorkItem()
    {
        using var db = Base();
        int dev = NuevoDev(db);
        int req = NuevoRequerimiento(db);
        Asignar(db, req, dev);

        Assert.Equal(4321, await WorkItemDeLaSesion.ResolverAsync(db, NuevaSesion(db, dev, reqId: req)));
    }

    /// <summary>
    /// EL AGUJERO, cortado aquí. El cronómetro deja arrancar sobre un requerimiento que no es tuyo
    /// —comportamiento viejo con pantallas encima— pero ese trabajo no sale de la organización.
    /// Medir de más es contabilidad interna; escribir de más es del cliente.
    /// </summary>
    [Fact]
    public async Task UnRequerimientoQueNoEsMio_noSeComenta()
    {
        using var db = Base();
        int mio = NuevoDev(db, "Ana");
        int otro = NuevoDev(db, "Beto");
        int req = NuevoRequerimiento(db);
        Asignar(db, req, otro);

        Assert.Null(await WorkItemDeLaSesion.ResolverAsync(db, NuevaSesion(db, mio, reqId: req)));
    }

    [Fact]
    public async Task UnRequerimientoSinAsignarANadie_noSeComenta()
    {
        using var db = Base();
        int dev = NuevoDev(db);
        int req = NuevoRequerimiento(db);

        Assert.Null(await WorkItemDeLaSesion.ResolverAsync(db, NuevaSesion(db, dev, reqId: req)));
    }

    /// <summary>La misma regla que el reporte de tiempo al detener, y no otra parecida: si un día
    /// divergen, el aviso y el tiempo acabarían en tickets distintos.</summary>
    [Theory]
    [InlineData(RequirementSource.Manual, "4321")]
    [InlineData(RequirementSource.AzureDevOps, "no-es-un-numero")]
    [InlineData(RequirementSource.AzureDevOps, "0")]
    [InlineData(RequirementSource.AzureDevOps, null)]
    public async Task UnRequerimientoQueNoApuntaAUnWorkItem_noSeComenta(RequirementSource origen, string? externo)
    {
        using var db = Base();
        int dev = NuevoDev(db);
        int req = NuevoRequerimiento(db, origen, externo);
        Asignar(db, req, dev);

        Assert.Null(await WorkItemDeLaSesion.ResolverAsync(db, NuevaSesion(db, dev, reqId: req)));
    }

    // ── Actividades del pool, por su percha ──────────────────────────────────────

    private static int NuevaPercha(AppDbContext db, int devId)
    {
        var a = new DevActivity { DeveloperId = devId, Title = "Pool #1: algo", Status = DevActivityStatus.Abierta };
        db.DevActivities.Add(a);
        db.SaveChanges();
        return a.Id;
    }

    private static void LigarAlPool(AppDbContext db, int perchaId, int? workItem)
    {
        db.PoolActivities.Add(new PoolActivity
        {
            Title = "Corregir el cálculo",
            WorkType = PoolWorkType.Tarea,
            Complexity = PoolComplexity.Alta,
            Priority = PoolPriority.Alta,
            LinkedDevActivityId = perchaId,
            DevOpsWorkItemId = workItem
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task UnaActividadDelPoolLigada_daSuWorkItem()
    {
        using var db = Base();
        int dev = NuevoDev(db);
        int percha = NuevaPercha(db, dev);
        LigarAlPool(db, percha, 777);

        Assert.Equal(777, await WorkItemDeLaSesion.ResolverAsync(db, NuevaSesion(db, dev, actividadId: percha)));
    }

    /// <summary>La percha de otro no se comenta. El cronómetro ya lo impide al arrancar, pero el
    /// barrido recorre también sesiones que arrancaron en la aplicación de escritorio, que no pasa
    /// por esa guarda.</summary>
    [Fact]
    public async Task LaPerchaDeOtro_noSeComenta()
    {
        using var db = Base();
        int mio = NuevoDev(db, "Ana");
        int otro = NuevoDev(db, "Beto");
        int perchaAjena = NuevaPercha(db, otro);
        LigarAlPool(db, perchaAjena, 777);

        Assert.Null(await WorkItemDeLaSesion.ResolverAsync(db, NuevaSesion(db, mio, actividadId: perchaAjena)));
    }

    /// <summary>Una actividad libre de verdad —soporte, una junta— no tiene ticket, y eso no es un
    /// fallo del que haya que dejar constancia.</summary>
    [Fact]
    public async Task UnaActividadLibreDeVerdad_noTieneATodoQuienAvisar()
    {
        using var db = Base();
        int dev = NuevoDev(db);
        int actividad = NuevaPercha(db, dev);

        Assert.Null(await WorkItemDeLaSesion.ResolverAsync(db, NuevaSesion(db, dev, actividadId: actividad)));
    }

    [Fact]
    public async Task UnaActividadDelPoolSinWorkItem_noSeComenta()
    {
        using var db = Base();
        int dev = NuevoDev(db);
        int percha = NuevaPercha(db, dev);
        LigarAlPool(db, percha, null);

        Assert.Null(await WorkItemDeLaSesion.ResolverAsync(db, NuevaSesion(db, dev, actividadId: percha)));
    }

    /// <summary>
    /// Sin clave ajena nada impide que dos filas del pool apunten a la misma percha: el enlace es un
    /// número suelto y la limpieza de datos borra actividades enteras, así que un identificador se
    /// puede reutilizar. En ese caso manda la ÚLTIMA, no la que salga primero.
    /// </summary>
    [Fact]
    public async Task SiDosActividadesDelPoolApuntanALaMismaPercha_mandaLaUltima()
    {
        using var db = Base();
        int dev = NuevoDev(db);
        int percha = NuevaPercha(db, dev);
        LigarAlPool(db, percha, 111);
        LigarAlPool(db, percha, 222);

        Assert.Equal(222, await WorkItemDeLaSesion.ResolverAsync(db, NuevaSesion(db, dev, actividadId: percha)));
    }

    [Fact]
    public async Task UnaSesionQueNoExiste_noResuelveNada()
    {
        using var db = Base();
        Assert.Null(await WorkItemDeLaSesion.ResolverAsync(db, 99999));
    }
}
