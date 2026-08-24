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
///  · el puntaje de una entrada lo fija el criterio, nunca el desarrollador;
///  · y ya no puede darse puntos a sí mismo, que es lo que retiró la puerta única.
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
        return (db, Fabrica.Vacaciones(db, user));
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

    // ── Autocalificarse: retirado ───────────────────────────────────────────────
    //
    // Estas pruebas fueron las que fijaron, en su día, que el desarrollador eligiera QUÉ registra y
    // nunca CUÁNTO vale. La regla no se cayó con el camino: es la misma que hace que el pool tase
    // desde una matriz y no desde quien trabaja, y sigue vigilada donde queda un llamador —corregir
    // una entrada de la cola—. Lo que se retiró es la puerta, y eso es lo que se prueba ahora.

    // El montaje perdió sus dos parámetros de forma —«criterio desactivado» y «criterio de equipo»—
    // porque las pruebas que los usaban desaparecieron con la puerta: los dos motivos de rechazo que
    // fijaban están hoy detrás del apagado, y probarlos aquí sería probar que el código muerto sigue
    // ahí. La validación que los aplica sigue vigilada en ActividadEvidenciaTests, contra corregir.
    private static (AppDbContext db, PerformanceScoringService svc, int criterioId)
        NuevoEntornoPuntos(int puntosDelCriterio = 5)
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = MiDevId, FullName = "Yo", IsActive = true });
        var c = new ScoringCriterion
        {
            Name = "Documentó el módulo",
            DefaultPoints = puntosDelCriterio,
            IsActive = true,
            Scope = CriterionScope.Individual,
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

    /// <summary>
    /// Ni con un criterio impecable: el desarrollador ya no se puede dar puntos.
    ///
    /// <para>La comprobación de que no queda fila es la mitad importante. Un apagado que rechaza
    /// pero deja algo escrito —una entrada a medias, un pendiente que nadie va a revisar— sería peor
    /// que no haberlo apagado, porque el residuo aparecería en la cola del líder sin que nadie sepa
    /// de dónde salió.</para>
    /// </summary>
    [Fact]
    public async Task Autocalificarse_SeRetiro_YNoDejaFila()
    {
        var (db, svc, criterioId) = NuevoEntornoPuntos(puntosDelCriterio: 5);

        var (ok, mensaje, entrada) = await svc.RegistrarAutocalificacionAsync(Borrador(criterioId, 999));

        Assert.False(ok);
        Assert.Null(entrada);
        Assert.Contains("pool", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.PointEntries);
    }

    /// <summary>
    /// El puntaje lo sigue fijando el criterio, y esto es lo que queda de aquella regla: se prueba
    /// sobre CORREGIR, que es el llamador que sobrevivió.
    ///
    /// <para>Importa porque la cola no se vació al apagar la puerta: mientras haya entradas
    /// pendientes o rechazadas, corregir una es una escritura del desarrollador sobre sus propios
    /// puntos, y un 999 colado por ahí valdría exactamente lo mismo que colado por el registro.</para>
    /// </summary>
    [Fact]
    public async Task AlCorregir_ElPuntajeSeTomaDelCriterio_NoDeLoQueMandeElDesarrollador()
    {
        var (db, svc, criterioId) = NuevoEntornoPuntos(puntosDelCriterio: 5);

        var pendiente = new PointEntry
        {
            DeveloperId = MiDevId, CriterionId = criterioId, Points = 5, Year = 2026, Month = 7,
            Comment = "hecho", Date = DateTime.UtcNow,
            ApprovalStatus = PointApprovalStatus.Pendiente, SubmittedByDeveloperId = MiDevId
        };
        db.PointEntries.Add(pendiente); db.SaveChanges();

        // El desarrollador intenta colarse 999 puntos al corregir.
        var (ok, _) = await svc.EditarAutocalificacionAsync(pendiente.Id, Borrador(criterioId, 999));

        Assert.True(ok);
        Assert.Equal(5, db.PointEntries.Find(pendiente.Id)!.Points);
    }

    /// <summary>
    /// Y LA SESIÓN SE COMPRUEBA ANTES QUE EL APAGADO: registrar a nombre de otro sigue lanzando, no
    /// contestando «se retiró».
    ///
    /// <para>No es una sutileza: si el apagado se hubiera puesto por delante de la autorización, un
    /// intento de escribir sobre la ficha de otra persona dejaría de distinguirse de una llamada
    /// legítima a una puerta cerrada, y el día que la puerta se reabriera —o que alguien copiara
    /// este método para otra cosa— la comprobación de dueño ya no estaría donde se creía.</para>
    /// </summary>
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
