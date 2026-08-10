using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// «Mis vacaciones» y «Mis permisos»: lo que las dos pantallas reciben de una sola vez, y las dos
/// operaciones que esta capa sí decide.
///
/// Cancelar, eliminar y resolver siguen siendo de <see cref="VacationRequestService"/> y
/// <see cref="LeaveRequestService"/>, que ya tienen sus pruebas; aquí se prueba lo propio:
///
///  · el SALDO, que descuenta lo aprobado del año pero no lo pendiente, y que no bloquea nada;
///  · el ALTA de vacaciones, que en el escritorio vivía en la pantalla y aquí baja al servidor;
///  · el JUSTIFICANTE, que se cuelga reenviando la solicitud entera y no debe perder por el camino
///    el motivo ni las fechas.
/// </summary>
public class AusenciasServiceTests
{
    private const int MiDevId = 7;
    private const int OtroDevId = 8;

    private static readonly int Anio = DateTime.Today.Year;

    private static (AppDbContext db, AusenciasService svc) Nuevo(
        int? devId = MiDevId, UserRole rol = UserRole.Desarrollador)
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = MiDevId, FullName = "Ana", IsActive = true, VacationDaysLeft = 15 });
        db.Developers.Add(new Developer { Id = OtroDevId, FullName = "Beto", IsActive = true });
        db.SaveChanges();

        var usuario = UsuarioDePrueba.Como(rol, devId);
        var auditoria = new AuditService(db, usuario, new OrigenDePrueba());
        var permisos = new LeaveRequestService(db, usuario, auditoria);
        return (db, new AusenciasService(db, usuario, permisos, auditoria));
    }

    private static VacationRequest Vacacion(AppDbContext db, DateTime inicio, DateTime fin,
        VacationStatus estado, int devId = MiDevId, string? adjunto = null)
    {
        var v = new VacationRequest
        {
            DeveloperId = devId,
            StartDate = inicio,
            EndDate = fin,
            Status = estado,
            AttachmentFileName = adjunto,
            AttachmentBytes = adjunto == null ? null : [1, 2, 3],
            CreatedAt = DateTime.UtcNow
        };
        db.VacationRequests.Add(v);
        db.SaveChanges();
        return v;
    }

    private static LeaveRequest Permiso(AppDbContext db, DateTime desde, int dias, LeaveStatus estado,
        int devId = MiDevId, bool loPidioElDesarrollador = true)
    {
        var l = new LeaveRequest
        {
            DeveloperId = devId,
            Type = LeaveType.CitaMedica,
            Date = desde,
            DaysCount = dias,
            Reason = "control",
            Notes = "por la mañana",
            Status = estado,
            RequestedByDeveloperId = loPidioElDesarrollador ? devId : null,
            CreatedAt = DateTime.UtcNow
        };
        db.LeaveRequests.Add(l);
        db.SaveChanges();
        return l;
    }

    // ── Saldo de vacaciones ─────────────────────────────────────────────────────

    [Fact]
    public async Task ElSaldoDescuentaLoAprobadoDeEsteAnioYNoLoDeAnterioresNiLoPendiente()
    {
        var (db, svc) = Nuevo();
        Vacacion(db, new DateTime(Anio, 3, 1), new DateTime(Anio, 3, 5), VacationStatus.Aprobada);
        Vacacion(db, new DateTime(Anio - 1, 3, 1), new DateTime(Anio - 1, 3, 10), VacationStatus.Aprobada);
        Vacacion(db, new DateTime(Anio, 6, 1), new DateTime(Anio, 6, 2), VacationStatus.Pendiente);

        var mias = await svc.MisVacacionesAsync();

        Assert.True(mias.Saldo.TieneFicha);
        Assert.Equal(15, mias.Saldo.Asignados);
        Assert.Equal(5, mias.Saldo.TomadosEsteAnio);
        Assert.Equal(10, mias.Saldo.Disponibles);
        Assert.Equal(2, mias.Saldo.PendientesDeAprobacion);
    }

    [Fact]
    public async Task LosPendientesCuentanAunqueSeanDeOtroAnio()
    {
        // Una solicitud vieja sin responder sigue siendo tiempo comprometido: esconderla haría creer
        // que hay más margen del que hay.
        var (db, svc) = Nuevo();
        Vacacion(db, new DateTime(Anio - 1, 12, 28), new DateTime(Anio - 1, 12, 31), VacationStatus.Pendiente);

        var mias = await svc.MisVacacionesAsync();

        Assert.Equal(4, mias.Saldo.PendientesDeAprobacion);
        Assert.Equal(0, mias.Saldo.TomadosEsteAnio);
    }

    [Fact]
    public async Task SinFichaDeDesarrolladorNoHaySaldoNiSolicitudes()
    {
        var (_, svc) = Nuevo(devId: null, rol: UserRole.Admin);

        var mias = await svc.MisVacacionesAsync();

        Assert.False(mias.Saldo.TieneFicha);
        Assert.Empty(mias.Solicitudes);
    }

    // ── Lista de vacaciones ─────────────────────────────────────────────────────

    [Fact]
    public async Task CadaSolicitudDiceQueSePuedeHacerConElla()
    {
        var (db, svc) = Nuevo();
        var pendiente = Vacacion(db, new DateTime(Anio, 3, 1), new DateTime(Anio, 3, 2), VacationStatus.Pendiente);
        var aprobada = Vacacion(db, new DateTime(Anio, 4, 1), new DateTime(Anio, 4, 2), VacationStatus.Aprobada);
        var rechazada = Vacacion(db, new DateTime(Anio, 5, 1), new DateTime(Anio, 5, 2), VacationStatus.Rechazada);

        var mias = await svc.MisVacacionesAsync();
        var porId = mias.Solicitudes.ToDictionary(s => s.Id);

        Assert.True(porId[pendiente.Id].SePuedeCancelar);
        Assert.True(porId[pendiente.Id].SePuedeEliminar);

        // Una aprobada se cancela (dejo de tomarla) pero no se borra: es una decisión del líder.
        Assert.True(porId[aprobada.Id].SePuedeCancelar);
        Assert.False(porId[aprobada.Id].SePuedeEliminar);

        // Una rechazada es historial y no ofrece nada.
        Assert.False(porId[rechazada.Id].SePuedeCancelar);
        Assert.False(porId[rechazada.Id].SePuedeEliminar);
    }

    [Fact]
    public async Task LaListaDiceSiHayRespaldoYCuantosDocumentosCaerianConLaSolicitud()
    {
        var (db, svc) = Nuevo();
        var conRespaldo = Vacacion(db, new DateTime(Anio, 3, 1), new DateTime(Anio, 3, 3),
            VacationStatus.Pendiente, adjunto: "constancia.pdf");
        var sinNada = Vacacion(db, new DateTime(Anio, 4, 1), new DateTime(Anio, 4, 1), VacationStatus.Pendiente);

        db.VacationDocuments.Add(new VacationDocument
        {
            VacationRequestId = conRespaldo.Id,
            FileName = "solicitud.docx",
            DocxBytes = [1],
            CreatedAtUtc = DateTime.UtcNow
        });
        db.SaveChanges();

        var mias = await svc.MisVacacionesAsync();
        var porId = mias.Solicitudes.ToDictionary(s => s.Id);

        Assert.True(porId[conRespaldo.Id].TieneRespaldo);
        Assert.Equal(1, porId[conRespaldo.Id].DocumentosGenerados);
        Assert.Equal(3, porId[conRespaldo.Id].Dias);

        Assert.False(porId[sinNada.Id].TieneRespaldo);
        Assert.Equal(0, porId[sinNada.Id].DocumentosGenerados);
        Assert.Equal(1, porId[sinNada.Id].Dias);
    }

    // ── Alta de vacaciones ──────────────────────────────────────────────────────

    [Fact]
    public async Task LaSolicitudNacePendienteYANombreDeQuienLaPide()
    {
        var (db, svc) = Nuevo();

        var (ok, _, id) = await svc.SolicitarVacacionesAsync(
            new DateTime(Anio, 9, 1), new DateTime(Anio, 9, 5), "  viaje  ");

        Assert.True(ok);
        var v = db.VacationRequests.AsNoTracking().Single(x => x.Id == id);
        Assert.Equal(VacationStatus.Pendiente, v.Status);
        Assert.Equal(MiDevId, v.DeveloperId);
        Assert.Equal("viaje", v.Comment);
        Assert.Null(v.ReviewedById);
        Assert.Null(v.ReviewedAt);
    }

    [Fact]
    public async Task NoSePuedePedirUnRangoAlReves()
    {
        var (db, svc) = Nuevo();

        var (ok, mensaje, _) = await svc.SolicitarVacacionesAsync(
            new DateTime(Anio, 9, 5), new DateTime(Anio, 9, 1), null);

        Assert.False(ok);
        Assert.Contains("fin", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.VacationRequests);
    }

    [Fact]
    public async Task SinFichaNoSePuedePedirNada()
    {
        var (db, svc) = Nuevo(devId: null, rol: UserRole.Admin);

        var (ok, _, _) = await svc.SolicitarVacacionesAsync(
            new DateTime(Anio, 9, 1), new DateTime(Anio, 9, 2), null);

        Assert.False(ok);
        Assert.Empty(db.VacationRequests);
    }

    [Fact]
    public async Task ElSaldoAgotadoNoImpidePedir()
    {
        // Decisión del escritorio que se conserva: el saldo informa, no veta. Quien concede o niega
        // es el líder, y hay casos que la ficha no conoce.
        var (db, svc) = Nuevo();
        Vacacion(db, new DateTime(Anio, 1, 1), new DateTime(Anio, 1, 31), VacationStatus.Aprobada);

        var (ok, _, _) = await svc.SolicitarVacacionesAsync(
            new DateTime(Anio, 9, 1), new DateTime(Anio, 9, 2), null);

        Assert.True(ok);
        Assert.Equal(2, db.VacationRequests.Count());
    }

    // ── Permisos ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElResumenSeparaLoPendienteDeLoAprobadoDelAnio()
    {
        var (db, svc) = Nuevo();
        Permiso(db, new DateTime(Anio, 2, 10), 2, LeaveStatus.Aprobada);
        Permiso(db, new DateTime(Anio, 3, 10), 3, LeaveStatus.Aprobada);
        Permiso(db, new DateTime(Anio - 1, 3, 10), 5, LeaveStatus.Aprobada);
        Permiso(db, new DateTime(Anio, 4, 10), 1, LeaveStatus.Pendiente);

        var mios = await svc.MisPermisosAsync();

        Assert.True(mios.TieneFicha);
        Assert.Equal(1, mios.Resumen.EsperandoRespuesta);
        Assert.Equal(2, mios.Resumen.AprobadosEsteAnio);
        Assert.Equal(5, mios.Resumen.DiasAprobadosEsteAnio);
        Assert.NotEmpty(mios.TiposDePermiso);
    }

    [Fact]
    public async Task CadaPermisoTraeSuUltimoDiaYaCalculadoYLoQueSePuedeHacerConEl()
    {
        var (db, svc) = Nuevo();
        var pendiente = Permiso(db, new DateTime(Anio, 4, 10), 5, LeaveStatus.Pendiente);
        var aprobado = Permiso(db, new DateTime(Anio, 5, 10), 1, LeaveStatus.Aprobada);
        var delLider = Permiso(db, new DateTime(Anio, 6, 10), 1, LeaveStatus.Aprobada,
            loPidioElDesarrollador: false);

        var mios = await svc.MisPermisosAsync();
        var porId = mios.Solicitudes.ToDictionary(s => s.Id);

        // Cinco días desde el 10 llegan al 14, no al 15: contar a mano es donde se cuela el error.
        Assert.Equal(new DateTime(Anio, 4, 14), porId[pendiente.Id].Hasta);
        Assert.True(porId[pendiente.Id].SePuedeCancelar);
        Assert.True(porId[pendiente.Id].SePuedeAdjuntar);
        Assert.True(porId[pendiente.Id].SePuedeEliminar);

        // Ya resuelto: se puede retirar, pero ni se toca el justificante ni se borra.
        Assert.True(porId[aprobado.Id].SePuedeCancelar);
        Assert.False(porId[aprobado.Id].SePuedeAdjuntar);
        Assert.False(porId[aprobado.Id].SePuedeEliminar);

        Assert.True(porId[delLider.Id].LoRegistroElLider);
        Assert.False(porId[pendiente.Id].LoRegistroElLider);
    }

    // ── Justificante ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdjuntarElJustificanteNoBorraElMotivoNiLasFechas()
    {
        // EditarAsync reemplaza la solicitud entera; si esta capa no reenviara los demás campos, un
        // adjunto dejaría el permiso sin motivo y sin fecha.
        var (db, svc) = Nuevo();
        var permiso = Permiso(db, new DateTime(Anio, 9, 10), 2, LeaveStatus.Pendiente);

        var (ok, _) = await svc.AdjuntarJustificanteAsync(permiso.Id, [1, 2, 3], "receta.pdf");

        Assert.True(ok);
        var guardado = db.LeaveRequests.AsNoTracking().Single(l => l.Id == permiso.Id);
        Assert.Equal("receta.pdf", guardado.AttachmentFileName);
        Assert.Equal(3, guardado.AttachmentBytes!.Length);
        Assert.Equal("control", guardado.Reason);
        Assert.Equal("por la mañana", guardado.Notes);
        Assert.Equal(new DateTime(Anio, 9, 10), guardado.Date);
        Assert.Equal(2, guardado.DaysCount);
        Assert.Equal(LeaveType.CitaMedica, guardado.Type);
        Assert.Equal(LeaveStatus.Pendiente, guardado.Status);
    }

    [Fact]
    public async Task NoSeAdjuntaSobreUnPermisoYaResuelto()
    {
        var (db, svc) = Nuevo();
        var permiso = Permiso(db, new DateTime(Anio, 9, 10), 1, LeaveStatus.Aprobada);

        var (ok, mensaje) = await svc.AdjuntarJustificanteAsync(permiso.Id, [1, 2, 3], "receta.pdf");

        Assert.False(ok);
        Assert.Contains("Aprobada", mensaje);
        Assert.Null(db.LeaveRequests.AsNoTracking().Single(l => l.Id == permiso.Id).AttachmentFileName);
    }

    [Fact]
    public async Task NoSeAdjuntaAlPermisoDeOtroDesarrollador()
    {
        var (db, svc) = Nuevo();
        var ajeno = Permiso(db, new DateTime(Anio, 9, 10), 1, LeaveStatus.Pendiente, devId: OtroDevId);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.AdjuntarJustificanteAsync(ajeno.Id, [1, 2, 3], "receta.pdf"));

        Assert.Null(db.LeaveRequests.AsNoTracking().Single(l => l.Id == ajeno.Id).AttachmentFileName);
    }

    [Fact]
    public async Task AdjuntarAAlgoQueYaNoExisteLoDice()
    {
        var (_, svc) = Nuevo();

        var (ok, mensaje) = await svc.AdjuntarJustificanteAsync(999, [1, 2, 3], "receta.pdf");

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje, StringComparison.OrdinalIgnoreCase);
    }
}
