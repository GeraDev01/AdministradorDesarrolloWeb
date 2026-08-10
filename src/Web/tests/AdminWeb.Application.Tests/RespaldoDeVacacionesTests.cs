using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El documento de respaldo de una solicitud de vacaciones: subirlo y abrirlo.
///
/// Era la única regresión funcional del port —el escritorio lo permitía y la web solo pintaba un
/// clip—, así que lo que hay que dejar amarrado es justo lo que se acaba de cerrar:
///
///  · <b>Quién puede abrirlo.</b> Su dueño y el líder, nadie más. Es la razón de que los bytes salgan
///    por un solo sitio; si esta prueba se cayera, cualquier desarrollador con sesión podría leer el
///    justificante médico de un compañero probando identificadores.
///  · <b>Cuándo se puede cambiar.</b> Solo mientras la solicitud siga pendiente: después, el líder ya
///    resolvió mirando el documento que había.
///  · <b>Qué se guarda.</b> El nombre saneado, no el que escribió quien subió el archivo.
/// </summary>
public class RespaldoDeVacacionesTests
{
    private const int MiDevId = 7;
    private const int OtroDevId = 8;

    private static AppDbContext BaseConDosDesarrolladores()
    {
        var db = TestDb.New();
        db.Developers.Add(new Developer { Id = MiDevId, FullName = "Ana", IsActive = true });
        db.Developers.Add(new Developer { Id = OtroDevId, FullName = "Beto", IsActive = true });
        db.SaveChanges();
        return db;
    }

    private static VacationRequestService Svc(AppDbContext db, ICurrentUser quien) =>
        Fabrica.Vacaciones(db, quien);

    private static VacationRequestService ComoDesarrollador(AppDbContext db, int devId = MiDevId) =>
        Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, devId, userId: devId));

    private static VacationRequestService ComoLider(AppDbContext db) =>
        Svc(db, UsuarioDePrueba.Como(UserRole.Admin, developerId: null, userId: 99));

    private static VacationRequest Vacacion(AppDbContext db,
        VacationStatus estado = VacationStatus.Pendiente, int devId = MiDevId,
        byte[]? adjunto = null, string? nombre = null)
    {
        var v = new VacationRequest
        {
            DeveloperId = devId,
            StartDate = new DateTime(2026, 9, 1),
            EndDate = new DateTime(2026, 9, 5),
            Status = estado,
            AttachmentBytes = adjunto,
            AttachmentFileName = nombre,
            CreatedAt = DateTime.UtcNow
        };
        db.VacationRequests.Add(v);
        db.SaveChanges();
        return v;
    }

    // ── Subida ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElRespaldoSeGuardaConElNombreYaSaneado()
    {
        var db = BaseConDosDesarrolladores();
        var v = Vacacion(db);

        // El navegador manda la ruta completa cuando es de Windows, y el servidor corre en Linux: si
        // no se limpiara, el «nombre» del archivo acabaría siendo «C:\Users\ana\constancia.pdf».
        var (ok, mensaje) = await ComoDesarrollador(db).AdjuntarRespaldoAsync(
            v.Id, [1, 2, 3, 4], @"C:\Users\ana\constancia.pdf");

        Assert.True(ok);
        Assert.Contains("respaldo", mensaje, StringComparison.OrdinalIgnoreCase);

        var guardada = db.VacationRequests.AsNoTracking().Single(x => x.Id == v.Id);
        Assert.Equal("constancia.pdf", guardada.AttachmentFileName);
        Assert.Equal(4, guardada.AttachmentBytes!.Length);
    }

    [Fact]
    public async Task ElRespaldoNuevoReemplazaAlQueYaHabia()
    {
        var db = BaseConDosDesarrolladores();
        var v = Vacacion(db, adjunto: [9], nombre: "viejo.pdf");

        var (ok, _) = await ComoDesarrollador(db).AdjuntarRespaldoAsync(v.Id, [1, 2], "nuevo.pdf");

        Assert.True(ok);
        var guardada = db.VacationRequests.AsNoTracking().Single(x => x.Id == v.Id);
        Assert.Equal("nuevo.pdf", guardada.AttachmentFileName);
        Assert.Equal(2, guardada.AttachmentBytes!.Length);
    }

    [Theory]
    [InlineData(VacationStatus.Aprobada)]
    [InlineData(VacationStatus.Rechazada)]
    [InlineData(VacationStatus.Cancelada)]
    public async Task NoSeCambiaElRespaldoDeUnaSolicitudYaResuelta(VacationStatus estado)
    {
        // El líder resolvió mirando el documento que había: cambiarlo por debajo dejaría su decisión
        // hablando de otro papel.
        var db = BaseConDosDesarrolladores();
        var v = Vacacion(db, estado, adjunto: [9], nombre: "original.pdf");

        var (ok, mensaje) = await ComoDesarrollador(db).AdjuntarRespaldoAsync(v.Id, [1, 2], "otro.pdf");

        Assert.False(ok);
        Assert.Contains(estado.ToString(), mensaje);
        Assert.Equal("original.pdf", db.VacationRequests.AsNoTracking().Single(x => x.Id == v.Id).AttachmentFileName);
    }

    [Fact]
    public async Task NoSeAdjuntaALaSolicitudDeOtroDesarrollador()
    {
        var db = BaseConDosDesarrolladores();
        var ajena = Vacacion(db, devId: OtroDevId);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => ComoDesarrollador(db).AdjuntarRespaldoAsync(ajena.Id, [1, 2], "constancia.pdf"));

        Assert.Null(db.VacationRequests.AsNoTracking().Single(x => x.Id == ajena.Id).AttachmentFileName);
    }

    [Fact]
    public async Task AdjuntarAAlgoQueYaNoExisteLoDice()
    {
        var db = BaseConDosDesarrolladores();

        var (ok, mensaje) = await ComoDesarrollador(db).AdjuntarRespaldoAsync(999, [1], "x.pdf");

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    // ── Descarga ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElDuenoBajaSuPropioRespaldo()
    {
        var db = BaseConDosDesarrolladores();
        var v = Vacacion(db, adjunto: [1, 2, 3], nombre: "constancia.pdf");

        var (bytes, nombre) = await ComoDesarrollador(db).AdjuntoAsync(v.Id);

        Assert.Equal(3, bytes.Length);
        Assert.Equal("constancia.pdf", nombre);
    }

    [Fact]
    public async Task ElLiderBajaElRespaldoDeCualquiera()
    {
        // Es la mitad de la razón de que exista: resolver una solicitud es leer lo que la justifica.
        var db = BaseConDosDesarrolladores();
        var v = Vacacion(db, devId: OtroDevId, adjunto: [1, 2, 3], nombre: "constancia.pdf");

        var (bytes, _) = await ComoLider(db).AdjuntoAsync(v.Id);

        Assert.Equal(3, bytes.Length);
    }

    [Fact]
    public async Task NoSeBajaElRespaldoDeOtroDesarrollador()
    {
        // La prueba que de verdad importa: sin ella, cualquiera con sesión leería el justificante
        // médico de un compañero probando identificadores en la barra de direcciones.
        var db = BaseConDosDesarrolladores();
        var ajena = Vacacion(db, devId: OtroDevId, adjunto: [1, 2, 3], nombre: "constancia.pdf");

        await Assert.ThrowsAsync<AuthorizationException>(
            () => ComoDesarrollador(db).AdjuntoAsync(ajena.Id));
    }

    [Fact]
    public async Task ElRespaldoDeUnaSolicitudQueNoExisteVuelveVacio()
    {
        // Vacío y no excepción: el endpoint lo traduce a 404, que es lo que corresponde a algo que no
        // está. Un 403 aquí delataría además si el identificador existe o no.
        var db = BaseConDosDesarrolladores();

        var (bytes, nombre) = await ComoDesarrollador(db).AdjuntoAsync(999);

        Assert.Empty(bytes);
        Assert.Equal("", nombre);
    }

    [Fact]
    public async Task UnaSolicitudSinRespaldoVuelveVacia()
    {
        var db = BaseConDosDesarrolladores();
        var v = Vacacion(db);

        var (bytes, _) = await ComoDesarrollador(db).AdjuntoAsync(v.Id);

        Assert.Empty(bytes);
    }

    // ── Lo que la pantalla ofrece ────────────────────────────────────────────────

    [Fact]
    public async Task LaListaSoloOfreceAdjuntarEnLoQueSigueEsperandoRespuesta()
    {
        // El botón sale de lo que responde el servidor, así que lo que se ve y lo que se permite no
        // pueden discrepar.
        var db = BaseConDosDesarrolladores();
        var pendiente = Vacacion(db);
        var aprobada = Vacacion(db, VacationStatus.Aprobada);

        var usuario = UsuarioDePrueba.Como(UserRole.Desarrollador, MiDevId, userId: MiDevId);
        var auditoria = new AuditService(db, usuario, new OrigenDePrueba());
        var ausencias = new AusenciasService(db, usuario, new LeaveRequestService(db, usuario, auditoria), auditoria);

        var mias = await ausencias.MisVacacionesAsync();
        var porId = mias.Solicitudes.ToDictionary(s => s.Id);

        Assert.True(porId[pendiente.Id].SePuedeAdjuntar);
        Assert.False(porId[aprobada.Id].SePuedeAdjuntar);

        // El tope que enseña el formulario es el que de verdad se aplica al subir.
        Assert.Equal(ArchivosSubidos.MaxBytes, mias.MaxRespaldoBytes);
    }
}
