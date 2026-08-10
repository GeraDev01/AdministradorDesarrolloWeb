using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Que el esquema que crea el migrador coincida con el que espera EF, columna por columna.
///
/// Es la prueba que atrapa el error que solo aparece en producción: una propiedad del modelo cuya
/// columna el DDL no crea compila, pasa las pruebas que no la tocan, y revienta la primera vez que
/// alguien abre la pantalla. Consultar cada tabla nueva obliga a EF a materializar TODAS sus
/// columnas contra la base real.
/// </summary>
public class EsquemaNuevoTests
{
    [Fact]
    public void ElMigradorCreaTodasLasColumnasQueElModeloEspera()
    {
        var db = TestDb.New();

        // Si al DDL le falta una columna, estas consultas lanzan «no such column».
        Assert.Empty(db.AttendanceRecords.AsNoTracking().ToList());
        Assert.Empty(db.PoolActivities.AsNoTracking().ToList());
        Assert.Empty(db.PoolActivityChecklistItems.AsNoTracking().ToList());
        Assert.Empty(db.PoolChecklistTemplateItems.AsNoTracking().ToList());
        Assert.Empty(db.PoolPointsMatrix.AsNoTracking().ToList());
    }

    [Fact]
    public void SeGuardaYSeReleeCadaCampoDeLasTablasNuevas()
    {
        var db = TestDb.New();
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev);
        db.SaveChanges();

        db.AttendanceRecords.Add(new AttendanceRecord
        {
            UserId = 1, DeveloperId = dev.Id, DisplayName = "Ana",
            CheckInUtc = DateTime.UtcNow.AddHours(-8), CheckOutUtc = DateTime.UtcNow,
            CheckInOrigin = "PC1\\ana", CheckOutOrigin = "PC2\\ana",
            CheckInNote = "entré", CheckOutNote = "salí",
            CloseKind = AttendanceCloseKind.Admin,
            CorrectionRequestNote = "revisar", CorrectionRequestedAtUtc = DateTime.UtcNow,
            CorrectedByUserId = 9, CorrectedByName = "Jefe",
            CorrectedAtUtc = DateTime.UtcNow, CorrectionReason = "llegó antes"
        });

        var actividad = new PoolActivity
        {
            Title = "Corregir el cálculo", Description = "detalle",
            WorkType = PoolWorkType.Bug, Complexity = PoolComplexity.Alta, Points = 12,
            Status = PoolActivityStatus.EnRevision, ExternalUrl = "https://dev.azure.com/x/1",
            CreatedByUserId = 9, ClaimedByDeveloperId = dev.Id,
            ClaimedAt = DateTime.UtcNow.AddDays(-2), ClaimDeadlineAt = DateTime.UtcNow.AddDays(6),
            ReturnedCount = 1, DeliveredAt = DateTime.UtcNow,
            ReviewedByUserId = 9, ReviewedAt = DateTime.UtcNow,
            ReviewComment = "falta la prueba", ReviewRound = 2, ReviewHistory = "línea 1\nlínea 2",
            PointEntryId = 77, LinkedDevActivityId = 88
        };
        db.PoolActivities.Add(actividad);
        db.SaveChanges();

        db.PoolActivityChecklistItems.Add(new PoolActivityChecklistItem
        {
            PoolActivityId = actividad.Id, Text = "PR enlazado", Orden = 10,
            RequiereEvidencia = true, IsDone = true, DoneAtUtc = DateTime.UtcNow,
            EvidenceUrl = "https://dev.azure.com/pr/1"
        });
        db.SaveChanges();

        var releida = db.PoolActivities.AsNoTracking().Single();
        Assert.Equal(12, releida.Points);
        Assert.Equal(PoolActivityStatus.EnRevision, releida.Status);
        Assert.Equal(2, releida.ReviewRound);
        Assert.Equal(77, releida.PointEntryId);
        Assert.Contains("línea 2", releida.ReviewHistory);

        var asistencia = db.AttendanceRecords.AsNoTracking().Single();
        Assert.Equal(AttendanceCloseKind.Admin, asistencia.CloseKind);
        Assert.Equal("Jefe", asistencia.CorrectedByName);
        Assert.Equal("PC2\\ana", asistencia.CheckOutOrigin);

        var item = db.PoolActivityChecklistItems.AsNoTracking().Single();
        Assert.True(item.IsDone);
        Assert.Equal("https://dev.azure.com/pr/1", item.EvidenceUrl);
    }

    [Fact]
    public void BorrarUnaActividadSeLlevaSuChecklist()
    {
        var db = TestDb.New();
        PoolSeed.Sembrar(db);

        var actividad = new PoolActivity { Title = "X", WorkType = PoolWorkType.Tarea, Points = 3 };
        db.PoolActivities.Add(actividad);
        db.SaveChanges();
        db.PoolActivityChecklistItems.Add(new PoolActivityChecklistItem
        {
            PoolActivityId = actividad.Id, Text = "algo", Orden = 10
        });
        db.SaveChanges();

        db.PoolActivities.Remove(actividad);
        db.SaveChanges();

        Assert.Empty(db.PoolActivityChecklistItems.AsNoTracking().ToList());
    }

    [Fact]
    public void LaMatrizNoAdmiteDosCeldasParaElMismoPar()
    {
        var db = TestDb.New();
        PoolSeed.Sembrar(db);

        db.PoolPointsMatrix.Add(new PoolPointsMatrixEntry
        {
            WorkType = PoolWorkType.Bug, Complexity = PoolComplexity.Alta, Points = 99, DiasLimite = 1
        });

        // Sin el índice único, el valor de una actividad dependería de cuál celda se leyera primero.
        Assert.ThrowsAny<Exception>(() => db.SaveChanges());
    }
}
