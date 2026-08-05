using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Permisos: el desarrollador solicita, el administrador resuelve.
///
/// Lo que se protege aquí es la frontera entre los dos papeles. Que nadie se autoapruebe, que una
/// solicitud ya resuelta no se pueda reescribir por detrás, y que un rechazo nunca salga sin
/// motivo: sin él, el solicitante recibe un «no» y no sabe qué hacer con la respuesta.
/// </summary>
public class LeaveRequestServiceTests
{
    private const int AdminUserId = 900;
    private const int DevUserId = 10;

    private static LeaveRequestService Svc(AppDbContext db, CurrentUserContext cu) =>
        new(db, cu, new AuditService(db, cu));

    private static (AppDbContext db, Developer dev, CurrentUserContext yo, CurrentUserContext jefa) Entorno()
    {
        var db = TestDb.New();
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();

        return (db, dev,
                Ctx.As(UserRole.Desarrollador, developerId: dev.Id, userId: DevUserId),
                Ctx.As(UserRole.Admin, developerId: null, userId: AdminUserId));
    }

    private static LeaveRequest Borrador(Developer dev, string? motivo = "Cita con el dentista",
                                         int dias = 1, byte[]? adjunto = null) => new()
    {
        DeveloperId = dev.Id,
        Type = LeaveType.CitaMedica,
        Date = new DateTime(2026, 9, 10),
        DaysCount = dias,
        Reason = motivo,
        Notes = "Aviso con antelación",
        AttachmentBytes = adjunto,
        AttachmentFileName = adjunto == null ? null : "receta.pdf"
    };

    // ── Solicitar ────────────────────────────────────────────────────────────

    [Fact]
    public void Solicitar_NacePendienteYComoSolicitudDelDesarrollador()
    {
        var (db, dev, yo, _) = Entorno();

        var (ok, _, sol) = Svc(db, yo).Solicitar(Borrador(dev));

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(LeaveStatus.Pendiente, g.Status);
        Assert.Equal(dev.Id, g.RequestedByDeveloperId);
        Assert.True(g.EsSolicitudDelDesarrollador);
        Assert.Null(g.ReviewedById);
        Assert.Null(g.ApprovedBy);   // nadie la ha autorizado todavía
        Assert.NotNull(sol);
    }

    [Fact]
    public void Solicitar_ExigeMotivo()
    {
        var (db, dev, yo, _) = Entorno();

        var (ok, mensaje, _) = Svc(db, yo).Solicitar(Borrador(dev, motivo: "   "));

        Assert.False(ok);
        Assert.Contains("motivo", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.LeaveRequests);
    }

    [Fact]
    public void Solicitar_GuardaElJustificante()
    {
        var (db, dev, yo, _) = Entorno();
        var (ok, _, _) = Svc(db, yo).Solicitar(Borrador(dev, adjunto: [1, 2, 3, 4]));

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(4, g.AttachmentBytes!.Length);
        Assert.Equal("receta.pdf", g.AttachmentFileName);
    }

    /// <summary>Un nombre de archivo sin bytes pintaría un adjunto que no existe.</summary>
    [Fact]
    public void Solicitar_SinBytesNoDejaNombreDeAdjuntoSuelto()
    {
        var (db, dev, yo, _) = Entorno();
        var b = Borrador(dev);
        b.AttachmentFileName = "fantasma.pdf";
        b.AttachmentBytes = null;

        Svc(db, yo).Solicitar(b);

        Assert.Null(db.LeaveRequests.AsNoTracking().Single().AttachmentFileName);
    }

    [Fact]
    public void Solicitar_NoSePuedePedirEnNombreDeOtro()
    {
        var (db, dev, _, _) = Entorno();
        var otro = Ctx.As(UserRole.Desarrollador, developerId: dev.Id + 99, userId: 77);

        Assert.Throws<AuthorizationException>(() => Svc(db, otro).Solicitar(Borrador(dev)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(LeaveRequestService.MaxDias + 1)]
    public void Solicitar_RechazaDuracionesImposibles(int dias)
    {
        var (db, dev, yo, _) = Entorno();
        var (ok, _, _) = Svc(db, yo).Solicitar(Borrador(dev, dias: dias));

        Assert.False(ok);
        Assert.Empty(db.LeaveRequests);
    }

    // ── Registro del administrador ───────────────────────────────────────────

    /// <summary>
    /// Registrar un permiso ES concederlo: nace aprobado. Dejarlo pendiente le crearía al
    /// administrador un trámite que él mismo acaba de resolver.
    /// </summary>
    [Fact]
    public void RegistrarPorAdministrador_NaceAprobada()
    {
        var (db, dev, _, jefa) = Entorno();

        var (ok, _, _) = Svc(db, jefa).RegistrarPorAdministrador(Borrador(dev, motivo: null));

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(LeaveStatus.Aprobada, g.Status);
        Assert.Null(g.RequestedByDeveloperId);       // no es una solicitud del desarrollador
        Assert.Equal(AdminUserId, g.ReviewedById);
    }

    [Fact]
    public void RegistrarPorAdministrador_SoloAdministrador()
    {
        var (db, dev, yo, _) = Entorno();
        Assert.Throws<AuthorizationException>(() => Svc(db, yo).RegistrarPorAdministrador(Borrador(dev)));
    }

    // ── Resolver ─────────────────────────────────────────────────────────────

    [Fact]
    public void Aprobar_DejaConstanciaDeQuienResolvio()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = Svc(db, yo).Solicitar(Borrador(dev));

        var (ok, _) = Svc(db, jefa).Aprobar(sol!.Id, "Adelante, avisa a tu equipo.");

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(LeaveStatus.Aprobada, g.Status);
        Assert.Equal(AdminUserId, g.ReviewedById);
        Assert.NotNull(g.ReviewedAt);
        Assert.Equal("Adelante, avisa a tu equipo.", g.ReviewComment);
    }

    [Fact]
    public void Rechazar_ExigeMotivo()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = Svc(db, yo).Solicitar(Borrador(dev));

        var (ok, mensaje) = Svc(db, jefa).Rechazar(sol!.Id, "  ");

        Assert.False(ok);
        Assert.Contains("motivo", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LeaveStatus.Pendiente, db.LeaveRequests.AsNoTracking().Single().Status);
    }

    [Fact]
    public void Rechazar_GuardaElMotivoQueLeeElSolicitante()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = Svc(db, yo).Solicitar(Borrador(dev));

        var (ok, _) = Svc(db, jefa).Rechazar(sol!.Id, "Esa semana hay entrega.");

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(LeaveStatus.Rechazada, g.Status);
        Assert.Equal("Esa semana hay entrega.", g.ReviewComment);
    }

    [Fact]
    public void Resolver_SoloAdministrador_ElSolicitanteNoSeAutoaprueba()
    {
        var (db, dev, yo, _) = Entorno();
        var (_, _, sol) = Svc(db, yo).Solicitar(Borrador(dev));

        Assert.Throws<AuthorizationException>(() => Svc(db, yo).Aprobar(sol!.Id));
        Assert.Equal(LeaveStatus.Pendiente, db.LeaveRequests.AsNoTracking().Single().Status);
    }

    [Fact]
    public void Resolver_NoSeVuelveAResolverLoYaResuelto()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = Svc(db, yo).Solicitar(Borrador(dev));
        Svc(db, jefa).Aprobar(sol!.Id);

        var (ok, mensaje) = Svc(db, jefa).Rechazar(sol.Id, "me arrepentí");

        Assert.False(ok);
        Assert.Contains("ya está", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LeaveStatus.Aprobada, db.LeaveRequests.AsNoTracking().Single().Status);
    }

    // ── Editar / cancelar / eliminar ─────────────────────────────────────────

    [Fact]
    public void Editar_SoloMientrasSigaPendiente()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = Svc(db, yo).Solicitar(Borrador(dev));

        var cambios = Borrador(dev, motivo: "Cambió la cita", dias: 2);
        Assert.True(Svc(db, yo).Editar(sol!.Id, cambios).ok);
        Assert.Equal(2, db.LeaveRequests.AsNoTracking().Single().DaysCount);

        Svc(db, jefa).Aprobar(sol.Id);

        var (ok, _) = Svc(db, yo).Editar(sol.Id, Borrador(dev, dias: 30));
        Assert.False(ok);
        Assert.Equal(2, db.LeaveRequests.AsNoTracking().Single().DaysCount);   // intacta
    }

    /// <summary>
    /// Cancelar no es resolver: llenar ReviewedById aquí haría pasar la renuncia del solicitante
    /// por una decisión del administrador.
    /// </summary>
    [Fact]
    public void Cancelar_NoSeAnotaComoUnaResolucionDelJefe()
    {
        var (db, dev, yo, _) = Entorno();
        var (_, _, sol) = Svc(db, yo).Solicitar(Borrador(dev));

        var (ok, _) = Svc(db, yo).Cancelar(sol!.Id, "ya no hace falta");

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(LeaveStatus.Cancelada, g.Status);
        Assert.Null(g.ReviewedById);
        Assert.Contains("ya no hace falta", g.ReviewComment);
    }

    [Fact]
    public void Cancelar_TambienUnaYaAprobadaQueNoSeVaATomar()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = Svc(db, yo).Solicitar(Borrador(dev));
        Svc(db, jefa).Aprobar(sol!.Id);

        Assert.True(Svc(db, yo).Cancelar(sol.Id).ok);
        Assert.Equal(LeaveStatus.Cancelada, db.LeaveRequests.AsNoTracking().Single().Status);
    }

    [Fact]
    public void Eliminar_ElDesarrolladorNoBorraLoYaResuelto()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = Svc(db, yo).Solicitar(Borrador(dev));
        Svc(db, jefa).Aprobar(sol!.Id);

        var (ok, mensaje) = Svc(db, yo).Eliminar(sol.Id);

        Assert.False(ok);
        Assert.Contains("historial", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Single(db.LeaveRequests);
    }

    /// <summary>El administrador sí puede depurar cualquier fila: es quien mantiene el registro.</summary>
    [Fact]
    public void Eliminar_ElAdministradorSiPuedeConLoResuelto()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = Svc(db, yo).Solicitar(Borrador(dev));
        Svc(db, jefa).Aprobar(sol!.Id);

        Assert.True(Svc(db, jefa).Eliminar(sol.Id).ok);
        Assert.Empty(db.LeaveRequests);
    }

    // ── Lectura ──────────────────────────────────────────────────────────────

    [Fact]
    public void DeDesarrollador_NoDejaVerLoDeOtro()
    {
        var (db, dev, _, _) = Entorno();
        var otro = Ctx.As(UserRole.Desarrollador, developerId: dev.Id + 99, userId: 77);

        Assert.Throws<AuthorizationException>(() => Svc(db, otro).DeDesarrollador(dev.Id));
    }

    [Fact]
    public void Todas_SoloAdministrador()
    {
        var (db, _, yo, jefa) = Entorno();

        Assert.Throws<AuthorizationException>(() => Svc(db, yo).Todas());
        Assert.Empty(Svc(db, jefa).Todas());   // no revienta con la tabla vacía
    }

    [Fact]
    public void Todas_PoneLasPendientesPrimero()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, resuelta) = Svc(db, yo).Solicitar(Borrador(dev));
        Svc(db, jefa).Aprobar(resuelta!.Id);
        Svc(db, yo).Solicitar(Borrador(dev, motivo: "otra cosa"));

        var todas = Svc(db, jefa).Todas();

        Assert.Equal(2, todas.Count);
        Assert.Equal(LeaveStatus.Pendiente, todas[0].Status);
    }

    [Fact]
    public void PendientesCount_CuentaSoloLoQueEsperaRespuesta()
    {
        var (db, dev, yo, jefa) = Entorno();
        Svc(db, yo).Solicitar(Borrador(dev));
        var (_, _, otra) = Svc(db, yo).Solicitar(Borrador(dev, motivo: "otra"));
        Svc(db, jefa).Aprobar(otra!.Id);

        Assert.Equal(1, Svc(db, jefa).PendientesCount());
        Assert.Equal(0, Svc(db, yo).PendientesCount());   // no es asunto del desarrollador
    }

    [Fact]
    public void EndDate_CuentaElDiaDeInicio()
    {
        var l = new LeaveRequest { Date = new DateTime(2026, 9, 10), DaysCount = 3 };
        Assert.Equal(new DateTime(2026, 9, 12), l.EndDate);   // 10, 11 y 12

        var unDia = new LeaveRequest { Date = new DateTime(2026, 9, 10), DaysCount = 1 };
        Assert.Equal(new DateTime(2026, 9, 10), unDia.EndDate);
    }
}
