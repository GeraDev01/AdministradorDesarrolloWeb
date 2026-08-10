using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Reglas del autoservicio del desarrollador:
///  · puede cancelar o eliminar SUS solicitudes de vacaciones, con límites por estado;
///  · el puntaje de una autocalificación lo fija el criterio, nunca el desarrollador.
/// </summary>
public class DeveloperSelfServiceTests
{
    private const int MiDevId = 7;
    private const int OtroDevId = 8;

    private static (AppDbContext db, VacationRequestService svc) NuevoEntornoVacaciones(
        UserRole rol = UserRole.Desarrollador, int? devId = MiDevId)
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = MiDevId, FullName = "Yo", IsActive = true });
        db.Developers.Add(new Developer { Id = OtroDevId, FullName = "Alguien más", IsActive = true });
        db.SaveChanges();

        var user = UsuarioDePrueba.Como(rol, devId);
        return (db, new VacationRequestService(db, user, new AuditService(db, user, new OrigenDePrueba())));
    }

    private static int CrearSolicitud(AppDbContext db, VacationStatus estado, int devId = MiDevId)
    {
        var v = new VacationRequest
        {
            DeveloperId = devId,
            StartDate = new DateTime(2026, 8, 3),
            EndDate = new DateTime(2026, 8, 7),
            Status = estado,
            CreatedAt = DateTime.UtcNow
        };
        db.VacationRequests.Add(v);
        db.SaveChanges();
        return v.Id;
    }

    // ── Cancelar ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(VacationStatus.Pendiente)]
    [InlineData(VacationStatus.Aprobada)]
    public async Task Cancelar_PermitidoEnEstadosVivos(VacationStatus estado)
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var id = CrearSolicitud(db, estado);

        var (ok, _) = await svc.CancelarAsync(id);

        Assert.True(ok);
        Assert.Equal(VacationStatus.Cancelada, db.VacationRequests.Find(id)!.Status);
    }

    [Theory]
    [InlineData(VacationStatus.Rechazada)]
    [InlineData(VacationStatus.Cancelada)]
    public async Task Cancelar_RechazadoEnEstadosCerrados(VacationStatus estado)
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var id = CrearSolicitud(db, estado);

        var (ok, _) = await svc.CancelarAsync(id);

        Assert.False(ok);
        Assert.Equal(estado, db.VacationRequests.Find(id)!.Status);
    }

    [Fact]
    public async Task Cancelar_NoTocaLosCamposDeRevisionDelAdministrador()
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var id = CrearSolicitud(db, VacationStatus.Pendiente);

        await svc.CancelarAsync(id, "cambio de planes");

        var v = db.VacationRequests.Find(id)!;
        Assert.Null(v.ReviewedById);   // no debe hacerse pasar por una revisión del jefe
        Assert.Null(v.ReviewedAt);
        Assert.Contains("cambio de planes", v.ReviewComment);
    }

    // ── Eliminar ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(VacationStatus.Pendiente)]
    [InlineData(VacationStatus.Cancelada)]
    public async Task Eliminar_PermitidoSoloSinDecisionDelAdministrador(VacationStatus estado)
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var id = CrearSolicitud(db, estado);

        var (ok, _) = await svc.EliminarAsync(id);

        Assert.True(ok);
        Assert.Null(db.VacationRequests.Find(id));
    }

    [Theory]
    [InlineData(VacationStatus.Aprobada)]
    [InlineData(VacationStatus.Rechazada)]
    public async Task Eliminar_ProhibidoSobreHistorial(VacationStatus estado)
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var id = CrearSolicitud(db, estado);

        var (ok, mensaje) = await svc.EliminarAsync(id);

        Assert.False(ok);
        Assert.NotNull(db.VacationRequests.Find(id));
        Assert.Contains("cancélala", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Eliminar_ArrastraLosDocumentosGenerados()
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var id = CrearSolicitud(db, VacationStatus.Pendiente);
        db.VacationDocuments.Add(new VacationDocument
        {
            VacationRequestId = id,
            FileName = "solicitud.docx",
            DocxBytes = [1, 2, 3],
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();
        Assert.Equal(1, await svc.DocumentosAsociadosAsync(id));

        var (ok, _) = await svc.EliminarAsync(id);

        Assert.True(ok);
        Assert.Null(db.VacationRequests.Find(id));
        // La cascada tiene que funcionar de verdad: si la FK no estuviera en cascada,
        // el DELETE habría fallado o habría dejado documentos huérfanos.
        Assert.Empty(db.VacationDocuments.Where(d => d.VacationRequestId == id));
    }

    [Fact]
    public async Task Cancelar_ConservaElComentarioPrevioDelAdministrador()
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var id = CrearSolicitud(db, VacationStatus.Aprobada);
        var v = db.VacationRequests.Find(id)!;
        v.ReviewComment = "Aprobada por el líder.";
        db.SaveChanges();

        await svc.CancelarAsync(id);

        Assert.Contains("Aprobada por el líder.", db.VacationRequests.Find(id)!.ReviewComment);
    }

    // ── Pertenencia ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoSePuedeCancelarLaSolicitudDeOtroDesarrollador()
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var ajena = CrearSolicitud(db, VacationStatus.Pendiente, OtroDevId);

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.CancelarAsync(ajena));
        Assert.Equal(VacationStatus.Pendiente, db.VacationRequests.Find(ajena)!.Status);
    }

    [Fact]
    public async Task NoSePuedeEliminarLaSolicitudDeOtroDesarrollador()
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var ajena = CrearSolicitud(db, VacationStatus.Pendiente, OtroDevId);

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.EliminarAsync(ajena));
        Assert.NotNull(db.VacationRequests.Find(ajena));
    }

    [Fact]
    public async Task ElAdministradorSiPuedeSobreSolicitudesAjenas()
    {
        var (db, svc) = NuevoEntornoVacaciones(UserRole.Admin, devId: null);
        var ajena = CrearSolicitud(db, VacationStatus.Pendiente, OtroDevId);

        var (ok, _) = await svc.CancelarAsync(ajena);

        Assert.True(ok);
    }

    // ── Puntaje fijado por el criterio ──────────────────────────────────────────

    private static (AppDbContext db, PerformanceScoringService svc, int criterioId)
        NuevoEntornoPuntos(int puntosDelCriterio = 5, bool activo = true, CriterionScope alcance = CriterionScope.Individual)
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = MiDevId, FullName = "Yo", IsActive = true });
        var c = new ScoringCriterion
        {
            Name = "Documentó el módulo",
            DefaultPoints = puntosDelCriterio,
            IsActive = activo,
            Scope = alcance,
            CreatedAt = DateTime.UtcNow
        };
        db.ScoringCriteria.Add(c);
        db.SaveChanges();
        return (db, new PerformanceScoringService(db, UsuarioDePrueba.Como(UserRole.Desarrollador, MiDevId)), c.Id);
    }

    private static PointEntry Borrador(int criterioId, int puntosInventados) => new()
    {
        DeveloperId = MiDevId,
        CriterionId = criterioId,
        Points = puntosInventados,
        Year = 2026,
        Month = 7,
        Comment = "hecho"
    };

    [Fact]
    public async Task ElPuntajeSeTomaDelCriterio_NoDeLoQueMandeElDesarrollador()
    {
        var (db, svc, criterioId) = NuevoEntornoPuntos(puntosDelCriterio: 5);

        // El desarrollador intenta colarse 999 puntos.
        var (ok, _, entrada) = await svc.RegistrarAutocalificacionAsync(Borrador(criterioId, 999));

        Assert.True(ok);
        Assert.Equal(5, entrada!.Points);
        Assert.Equal(5, db.PointEntries.Find(entrada.Id)!.Points);
    }

    [Fact]
    public async Task UnPuntajeNegativoInyectadoTampocoSeRespeta()
    {
        var (_, svc, criterioId) = NuevoEntornoPuntos(puntosDelCriterio: 3);

        var (ok, _, entrada) = await svc.RegistrarAutocalificacionAsync(Borrador(criterioId, -50));

        Assert.True(ok);
        Assert.Equal(3, entrada!.Points);
    }

    [Fact]
    public async Task LaEntradaNacePendienteYAtribuidaAlDesarrollador()
    {
        var (_, svc, criterioId) = NuevoEntornoPuntos();

        var borrador = Borrador(criterioId, 1);
        borrador.ApprovalStatus = PointApprovalStatus.Aprobado;   // intento de auto-aprobarse
        borrador.AssignedByUserId = 1;

        var (ok, _, entrada) = await svc.RegistrarAutocalificacionAsync(borrador);

        Assert.True(ok);
        Assert.Equal(PointApprovalStatus.Pendiente, entrada!.ApprovalStatus);
        Assert.Equal(MiDevId, entrada.SubmittedByDeveloperId);
        Assert.Null(entrada.AssignedByUserId);
    }

    [Fact]
    public async Task NoSePuedeAutocalificarConUnCriterioDesactivado()
    {
        var (db, svc, criterioId) = NuevoEntornoPuntos(activo: false);

        var (ok, _, _) = await svc.RegistrarAutocalificacionAsync(Borrador(criterioId, 1));

        Assert.False(ok);
        Assert.Empty(db.PointEntries);
    }

    [Fact]
    public async Task NoSePuedeAutocalificarConUnCriterioDeEquipo()
    {
        var (db, svc, criterioId) = NuevoEntornoPuntos(alcance: CriterionScope.Equipo);

        var (ok, _, _) = await svc.RegistrarAutocalificacionAsync(Borrador(criterioId, 1));

        Assert.False(ok);
        Assert.Empty(db.PointEntries);
    }

    [Fact]
    public async Task NoSePuedeRegistrarAActividadDeOtroDesarrollador()
    {
        var (db, svc, criterioId) = NuevoEntornoPuntos();

        var borrador = Borrador(criterioId, 1);
        borrador.DeveloperId = OtroDevId;

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.RegistrarAutocalificacionAsync(borrador));
        Assert.Empty(db.PointEntries);
    }
}
