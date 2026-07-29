using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

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

        var user = Ctx.As(rol, devId);
        return (db, new VacationRequestService(db, user, new AuditService(db, user)));
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
    public void Cancelar_PermitidoEnEstadosVivos(VacationStatus estado)
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var id = CrearSolicitud(db, estado);

        var (ok, _) = svc.Cancelar(id);

        Assert.True(ok);
        Assert.Equal(VacationStatus.Cancelada, db.VacationRequests.Find(id)!.Status);
    }

    [Theory]
    [InlineData(VacationStatus.Rechazada)]
    [InlineData(VacationStatus.Cancelada)]
    public void Cancelar_RechazadoEnEstadosCerrados(VacationStatus estado)
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var id = CrearSolicitud(db, estado);

        var (ok, _) = svc.Cancelar(id);

        Assert.False(ok);
        Assert.Equal(estado, db.VacationRequests.Find(id)!.Status);
    }

    [Fact]
    public void Cancelar_NoTocaLosCamposDeRevisionDelAdministrador()
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var id = CrearSolicitud(db, VacationStatus.Pendiente);

        svc.Cancelar(id, "cambio de planes");

        var v = db.VacationRequests.Find(id)!;
        Assert.Null(v.ReviewedById);   // no debe hacerse pasar por una revisión del jefe
        Assert.Null(v.ReviewedAt);
        Assert.Contains("cambio de planes", v.ReviewComment);
    }

    // ── Eliminar ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(VacationStatus.Pendiente)]
    [InlineData(VacationStatus.Cancelada)]
    public void Eliminar_PermitidoSoloSinDecisionDelAdministrador(VacationStatus estado)
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var id = CrearSolicitud(db, estado);

        var (ok, _) = svc.Eliminar(id);

        Assert.True(ok);
        Assert.Null(db.VacationRequests.Find(id));
    }

    [Theory]
    [InlineData(VacationStatus.Aprobada)]
    [InlineData(VacationStatus.Rechazada)]
    public void Eliminar_ProhibidoSobreHistorial(VacationStatus estado)
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var id = CrearSolicitud(db, estado);

        var (ok, mensaje) = svc.Eliminar(id);

        Assert.False(ok);
        Assert.NotNull(db.VacationRequests.Find(id));
        Assert.Contains("cancélala", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Eliminar_ArrastraLosDocumentosGenerados()
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
        Assert.Equal(1, svc.DocumentosAsociados(id));

        var (ok, _) = svc.Eliminar(id);

        Assert.True(ok);
        Assert.Null(db.VacationRequests.Find(id));
        // La cascada tiene que funcionar de verdad: si la FK no estuviera en cascada,
        // el DELETE habría fallado o habría dejado documentos huérfanos.
        Assert.Empty(db.VacationDocuments.Where(d => d.VacationRequestId == id));
    }

    [Fact]
    public void Cancelar_ConservaElComentarioPrevioDelAdministrador()
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var id = CrearSolicitud(db, VacationStatus.Aprobada);
        var v = db.VacationRequests.Find(id)!;
        v.ReviewComment = "Aprobada por el jefe.";
        db.SaveChanges();

        svc.Cancelar(id);

        Assert.Contains("Aprobada por el jefe.", db.VacationRequests.Find(id)!.ReviewComment);
    }

    // ── Pertenencia ─────────────────────────────────────────────────────────────

    [Fact]
    public void NoSePuedeCancelarLaSolicitudDeOtroDesarrollador()
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var ajena = CrearSolicitud(db, VacationStatus.Pendiente, OtroDevId);

        Assert.Throws<AuthorizationException>(() => svc.Cancelar(ajena));
        Assert.Equal(VacationStatus.Pendiente, db.VacationRequests.Find(ajena)!.Status);
    }

    [Fact]
    public void NoSePuedeEliminarLaSolicitudDeOtroDesarrollador()
    {
        var (db, svc) = NuevoEntornoVacaciones();
        var ajena = CrearSolicitud(db, VacationStatus.Pendiente, OtroDevId);

        Assert.Throws<AuthorizationException>(() => svc.Eliminar(ajena));
        Assert.NotNull(db.VacationRequests.Find(ajena));
    }

    [Fact]
    public void ElAdministradorSiPuedeSobreSolicitudesAjenas()
    {
        var (db, svc) = NuevoEntornoVacaciones(UserRole.Admin, devId: null);
        var ajena = CrearSolicitud(db, VacationStatus.Pendiente, OtroDevId);

        var (ok, _) = svc.Cancelar(ajena);

        Assert.True(ok);
    }

    // ── Puntaje fijado por el criterio ──────────────────────────────────────────

    private static (AppDbContext db, PerformanceScoringService svc, CurrentUserContext user, int criterioId)
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
        return (db, new PerformanceScoringService(db), Ctx.As(UserRole.Desarrollador, MiDevId), c.Id);
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
    public void ElPuntajeSeTomaDelCriterio_NoDeLoQueMandeElDesarrollador()
    {
        var (db, svc, user, criterioId) = NuevoEntornoPuntos(puntosDelCriterio: 5);

        // El desarrollador intenta colarse 999 puntos.
        var (ok, _, entrada) = svc.RegistrarAutocalificacion(Borrador(criterioId, 999), user);

        Assert.True(ok);
        Assert.Equal(5, entrada!.Points);
        Assert.Equal(5, db.PointEntries.Find(entrada.Id)!.Points);
    }

    [Fact]
    public void UnPuntajeNegativoInyectadoTampocoSeRespeta()
    {
        var (db, svc, user, criterioId) = NuevoEntornoPuntos(puntosDelCriterio: 3);

        var (ok, _, entrada) = svc.RegistrarAutocalificacion(Borrador(criterioId, -50), user);

        Assert.True(ok);
        Assert.Equal(3, entrada!.Points);
    }

    [Fact]
    public void LaEntradaNacePendienteYAtribuidaAlDesarrollador()
    {
        var (_, svc, user, criterioId) = NuevoEntornoPuntos();

        var borrador = Borrador(criterioId, 1);
        borrador.ApprovalStatus = PointApprovalStatus.Aprobado;   // intento de auto-aprobarse
        borrador.AssignedByUserId = 1;

        var (ok, _, entrada) = svc.RegistrarAutocalificacion(borrador, user);

        Assert.True(ok);
        Assert.Equal(PointApprovalStatus.Pendiente, entrada!.ApprovalStatus);
        Assert.Equal(MiDevId, entrada.SubmittedByDeveloperId);
        Assert.Null(entrada.AssignedByUserId);
    }

    [Fact]
    public void NoSePuedeAutocalificarConUnCriterioDesactivado()
    {
        var (db, svc, user, criterioId) = NuevoEntornoPuntos(activo: false);

        var (ok, _, _) = svc.RegistrarAutocalificacion(Borrador(criterioId, 1), user);

        Assert.False(ok);
        Assert.Empty(db.PointEntries);
    }

    [Fact]
    public void NoSePuedeAutocalificarConUnCriterioDeEquipo()
    {
        var (db, svc, user, criterioId) = NuevoEntornoPuntos(alcance: CriterionScope.Equipo);

        var (ok, _, _) = svc.RegistrarAutocalificacion(Borrador(criterioId, 1), user);

        Assert.False(ok);
        Assert.Empty(db.PointEntries);
    }

    [Fact]
    public void NoSePuedeRegistrarAActividadDeOtroDesarrollador()
    {
        var (db, svc, user, criterioId) = NuevoEntornoPuntos();

        var borrador = Borrador(criterioId, 1);
        borrador.DeveloperId = OtroDevId;

        Assert.Throws<AuthorizationException>(() => svc.RegistrarAutocalificacion(borrador, user));
        Assert.Empty(db.PointEntries);
    }
}
