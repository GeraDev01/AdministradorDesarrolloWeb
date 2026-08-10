using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

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

    private static LeaveRequestService Svc(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, new AuditService(db, cu, new OrigenDePrueba()));

    private static (AppDbContext db, Developer dev, ICurrentUser yo, ICurrentUser jefa) Entorno()
    {
        var db = TestDb.New();
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();

        return (db, dev,
                UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id, userId: DevUserId),
                UsuarioDePrueba.Como(UserRole.Admin, developerId: null, userId: AdminUserId));
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
    public async Task Solicitar_NacePendienteYComoSolicitudDelDesarrollador()
    {
        var (db, dev, yo, _) = Entorno();

        var (ok, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev));

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
    public async Task Solicitar_ExigeMotivo()
    {
        var (db, dev, yo, _) = Entorno();

        var (ok, mensaje, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, motivo: "   "));

        Assert.False(ok);
        Assert.Contains("motivo", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.LeaveRequests);
    }

    [Fact]
    public async Task Solicitar_GuardaElJustificante()
    {
        var (db, dev, yo, _) = Entorno();
        var (ok, _, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, adjunto: [1, 2, 3, 4]));

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(4, g.AttachmentBytes!.Length);
        Assert.Equal("receta.pdf", g.AttachmentFileName);
    }

    /// <summary>Un nombre de archivo sin bytes pintaría un adjunto que no existe.</summary>
    [Fact]
    public async Task Solicitar_SinBytesNoDejaNombreDeAdjuntoSuelto()
    {
        var (db, dev, yo, _) = Entorno();
        var b = Borrador(dev);
        b.AttachmentFileName = "fantasma.pdf";
        b.AttachmentBytes = null;

        await Svc(db, yo).SolicitarAsync(b);

        Assert.Null(db.LeaveRequests.AsNoTracking().Single().AttachmentFileName);
    }

    [Fact]
    public async Task Solicitar_NoSePuedePedirEnNombreDeOtro()
    {
        var (db, dev, _, _) = Entorno();
        var otro = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id + 99, userId: 77);

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, otro).SolicitarAsync(Borrador(dev)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(LeaveRequestService.MaxDias + 1)]
    public async Task Solicitar_RechazaDuracionesImposibles(int dias)
    {
        var (db, dev, yo, _) = Entorno();
        var (ok, _, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, dias: dias));

        Assert.False(ok);
        Assert.Empty(db.LeaveRequests);
    }

    // ── Registro del administrador ───────────────────────────────────────────

    /// <summary>
    /// Registrar un permiso ES concederlo: nace aprobado. Dejarlo pendiente le crearía al
    /// administrador un trámite que él mismo acaba de resolver.
    /// </summary>
    [Fact]
    public async Task RegistrarPorAdministrador_NaceAprobada()
    {
        var (db, dev, _, jefa) = Entorno();

        var (ok, _, _) = await Svc(db, jefa).RegistrarPorAdministradorAsync(Borrador(dev, motivo: null));

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(LeaveStatus.Aprobada, g.Status);
        Assert.Null(g.RequestedByDeveloperId);       // no es una solicitud del desarrollador
        Assert.Equal(AdminUserId, g.ReviewedById);
    }

    [Fact]
    public async Task RegistrarPorAdministrador_SoloAdministrador()
    {
        var (db, dev, yo, _) = Entorno();
        await Assert.ThrowsAsync<AuthorizationException>(
            () => Svc(db, yo).RegistrarPorAdministradorAsync(Borrador(dev)));
    }

    // ── Resolver ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Aprobar_DejaConstanciaDeQuienResolvio()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev));

        var (ok, _) = await Svc(db, jefa).AprobarAsync(sol!.Id, "Adelante, avisa a tu equipo.");

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(LeaveStatus.Aprobada, g.Status);
        Assert.Equal(AdminUserId, g.ReviewedById);
        Assert.NotNull(g.ReviewedAt);
        Assert.Equal("Adelante, avisa a tu equipo.", g.ReviewComment);
    }

    [Fact]
    public async Task Rechazar_ExigeMotivo()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev));

        var (ok, mensaje) = await Svc(db, jefa).RechazarAsync(sol!.Id, "  ");

        Assert.False(ok);
        Assert.Contains("motivo", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LeaveStatus.Pendiente, db.LeaveRequests.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task Rechazar_GuardaElMotivoQueLeeElSolicitante()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev));

        var (ok, _) = await Svc(db, jefa).RechazarAsync(sol!.Id, "Esa semana hay entrega.");

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(LeaveStatus.Rechazada, g.Status);
        Assert.Equal("Esa semana hay entrega.", g.ReviewComment);
    }

    [Fact]
    public async Task Resolver_SoloAdministrador_ElSolicitanteNoSeAutoaprueba()
    {
        var (db, dev, yo, _) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev));

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, yo).AprobarAsync(sol!.Id));
        Assert.Equal(LeaveStatus.Pendiente, db.LeaveRequests.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task Resolver_NoSeVuelveAResolverLoYaResuelto()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev));
        await Svc(db, jefa).AprobarAsync(sol!.Id);

        var (ok, mensaje) = await Svc(db, jefa).RechazarAsync(sol.Id, "me arrepentí");

        Assert.False(ok);
        Assert.Contains("ya está", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LeaveStatus.Aprobada, db.LeaveRequests.AsNoTracking().Single().Status);
    }

    // ── Editar / cancelar / eliminar ─────────────────────────────────────────

    [Fact]
    public async Task Editar_SoloMientrasSigaPendiente()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev));

        var cambios = Borrador(dev, motivo: "Cambió la cita", dias: 2);
        Assert.True((await Svc(db, yo).EditarAsync(sol!.Id, cambios)).ok);
        Assert.Equal(2, db.LeaveRequests.AsNoTracking().Single().DaysCount);

        await Svc(db, jefa).AprobarAsync(sol.Id);

        var (ok, _) = await Svc(db, yo).EditarAsync(sol.Id, Borrador(dev, dias: 30));
        Assert.False(ok);
        Assert.Equal(2, db.LeaveRequests.AsNoTracking().Single().DaysCount);   // intacta
    }

    /// <summary>
    /// Cancelar no es resolver: llenar ReviewedById aquí haría pasar la renuncia del solicitante
    /// por una decisión del administrador.
    /// </summary>
    [Fact]
    public async Task Cancelar_NoSeAnotaComoUnaResolucionDelJefe()
    {
        var (db, dev, yo, _) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev));

        var (ok, _) = await Svc(db, yo).CancelarAsync(sol!.Id, "ya no hace falta");

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(LeaveStatus.Cancelada, g.Status);
        Assert.Null(g.ReviewedById);
        Assert.Contains("ya no hace falta", g.ReviewComment);
    }

    [Fact]
    public async Task Cancelar_TambienUnaYaAprobadaQueNoSeVaATomar()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev));
        await Svc(db, jefa).AprobarAsync(sol!.Id);

        Assert.True((await Svc(db, yo).CancelarAsync(sol.Id)).ok);
        Assert.Equal(LeaveStatus.Cancelada, db.LeaveRequests.AsNoTracking().Single().Status);
    }

    [Fact]
    public async Task Eliminar_ElDesarrolladorNoBorraLoYaResuelto()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev));
        await Svc(db, jefa).AprobarAsync(sol!.Id);

        var (ok, mensaje) = await Svc(db, yo).EliminarAsync(sol.Id);

        Assert.False(ok);
        Assert.Contains("historial", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Single(db.LeaveRequests);
    }

    /// <summary>El administrador sí puede depurar cualquier fila: es quien mantiene el registro.</summary>
    [Fact]
    public async Task Eliminar_ElAdministradorSiPuedeConLoResuelto()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev));
        await Svc(db, jefa).AprobarAsync(sol!.Id);

        Assert.True((await Svc(db, jefa).EliminarAsync(sol.Id)).ok);
        Assert.Empty(db.LeaveRequests);
    }

    // ── Corregir sin tocar el justificante ───────────────────────────────────
    //
    // Esta es la razón de que CorregirPendienteAsync exista. EditarAsync reemplaza la solicitud
    // entera con lo que se le mande, adjunto incluido: desde la pantalla del líder —que no sube
    // archivos— arreglar una palabra del motivo se habría llevado por delante el justificante que
    // subió quien pidió el permiso, y nadie se habría enterado hasta necesitarlo.

    [Fact]
    public async Task Corregir_NoBorraElJustificante()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev, adjunto: [1, 2, 3, 4]));

        var (ok, _) = await Svc(db, jefa).CorregirPendienteAsync(
            sol!.Id, LeaveType.AsuntoFamiliar, new DateTime(2026, 9, 12), 3,
            "Se movió la cita", "Avisado al equipo");

        Assert.True(ok);

        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, g.AttachmentBytes);
        Assert.Equal("receta.pdf", g.AttachmentFileName);

        // Y lo que sí se pidió cambiar, cambió.
        Assert.Equal(LeaveType.AsuntoFamiliar, g.Type);
        Assert.Equal(new DateTime(2026, 9, 12), g.Date);
        Assert.Equal(3, g.DaysCount);
        Assert.Equal("Se movió la cita", g.Reason);
        Assert.Equal("Avisado al equipo", g.Notes);
    }

    /// <summary>
    /// Corregir enmienda, no resuelve: la solicitud sigue esperando respuesta y sigue siendo de quien
    /// la escribió. Sin esto, el líder podría convertir una corrección en una aprobación silenciosa.
    /// </summary>
    [Fact]
    public async Task Corregir_NoResuelveNiCambiaDeDueno()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev));

        Assert.True((await Svc(db, jefa).CorregirPendienteAsync(
            sol!.Id, LeaveType.CitaMedica, new DateTime(2026, 9, 10), 1, "Otro motivo", null)).ok);

        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(LeaveStatus.Pendiente, g.Status);
        Assert.Equal(dev.Id, g.DeveloperId);
        Assert.Equal(dev.Id, g.RequestedByDeveloperId);
        Assert.Null(g.ReviewedById);
        Assert.Null(g.ApprovedBy);
    }

    [Fact]
    public async Task Corregir_SoloMientrasSigaPendiente()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev, adjunto: [7, 7]));
        await Svc(db, jefa).AprobarAsync(sol!.Id);

        var (ok, mensaje) = await Svc(db, jefa).CorregirPendienteAsync(
            sol.Id, LeaveType.Otro, new DateTime(2026, 10, 1), 5, "tarde", null);

        Assert.False(ok);
        Assert.Contains("Aprobada", mensaje);

        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(LeaveType.CitaMedica, g.Type);          // intacta
        Assert.Equal(new byte[] { 7, 7 }, g.AttachmentBytes);
    }

    [Fact]
    public async Task Corregir_NoSePuedeSobreLoAjeno()
    {
        var (db, dev, yo, _) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev));
        var otro = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id + 99, userId: 77);

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, otro).CorregirPendienteAsync(
            sol!.Id, LeaveType.Otro, new DateTime(2026, 9, 10), 1, "no es mío", null));
    }

    /// <summary>
    /// Los topes son los MISMOS del alta: corregir no es una puerta de atrás para escribir un permiso
    /// que la solicitud original no habría permitido.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(LeaveRequestService.MaxDias + 1)]
    public async Task Corregir_AplicaLosMismosTopesQueElAlta(int dias)
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev, adjunto: [5]));

        var (ok, _) = await Svc(db, jefa).CorregirPendienteAsync(
            sol!.Id, LeaveType.CitaMedica, new DateTime(2026, 9, 10), dias, "motivo", null);

        Assert.False(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(1, g.DaysCount);
        Assert.Equal(new byte[] { 5 }, g.AttachmentBytes);   // un rechazo tampoco toca el adjunto
    }

    /// <summary>Vaciar el motivo de una solicitud del desarrollador se rechaza, igual que al pedirla.</summary>
    [Fact]
    public async Task Corregir_NoDejaUnaSolicitudSinMotivo()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev));

        var (ok, mensaje) = await Svc(db, jefa).CorregirPendienteAsync(
            sol!.Id, LeaveType.CitaMedica, new DateTime(2026, 9, 10), 1, "   ", null);

        Assert.False(ok);
        Assert.Contains("motivo", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Cita con el dentista", db.LeaveRequests.AsNoTracking().Single().Reason);
    }

    [Fact]
    public async Task Corregir_UnaQueYaNoEsta_LoDice()
    {
        var (db, _, _, jefa) = Entorno();

        var (ok, mensaje) = await Svc(db, jefa).CorregirPendienteAsync(
            404, LeaveType.Otro, new DateTime(2026, 9, 10), 1, "motivo", null);

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje);
    }

    // ── Lectura ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeDesarrollador_NoDejaVerLoDeOtro()
    {
        var (db, dev, _, _) = Entorno();
        var otro = UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id + 99, userId: 77);

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, otro).DeDesarrolladorAsync(dev.Id));
    }

    [Fact]
    public async Task Todas_SoloAdministrador()
    {
        var (db, _, yo, jefa) = Entorno();

        await Assert.ThrowsAsync<AuthorizationException>(() => Svc(db, yo).TodasAsync());
        Assert.Empty(await Svc(db, jefa).TodasAsync());   // no revienta con la tabla vacía
    }

    [Fact]
    public async Task Todas_PoneLasPendientesPrimero()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, resuelta) = await Svc(db, yo).SolicitarAsync(Borrador(dev));
        await Svc(db, jefa).AprobarAsync(resuelta!.Id);
        await Svc(db, yo).SolicitarAsync(Borrador(dev, motivo: "otra cosa"));

        var todas = await Svc(db, jefa).TodasAsync();

        Assert.Equal(2, todas.Count);
        Assert.Equal(LeaveStatus.Pendiente, todas[0].Status);
    }

    [Fact]
    public async Task PendientesCount_CuentaSoloLoQueEsperaRespuesta()
    {
        var (db, dev, yo, jefa) = Entorno();
        await Svc(db, yo).SolicitarAsync(Borrador(dev));
        var (_, _, otra) = await Svc(db, yo).SolicitarAsync(Borrador(dev, motivo: "otra"));
        await Svc(db, jefa).AprobarAsync(otra!.Id);

        Assert.Equal(1, await Svc(db, jefa).PendientesCountAsync());
        Assert.Equal(0, await Svc(db, yo).PendientesCountAsync());   // no es asunto del desarrollador
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
