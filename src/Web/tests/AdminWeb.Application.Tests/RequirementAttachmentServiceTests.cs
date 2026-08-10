using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Los documentos que cuelgan de un requerimiento: el de requerimiento y el de estimación.
///
/// Es el port de <c>RequirementAttachmentService</c> del escritorio, donde no tenía pruebas porque
/// leía del disco. Lo que hay que dejar amarrado:
///
///  · <b>La pantalla es del líder</b>, y estos documentos con ella. Un desarrollador no lista, no
///    sube y —sobre todo— no baja: sin esa última guarda, cualquiera con sesión leería la estimación
///    de un requerimiento probando identificadores.
///  · <b>Los bytes no salen en la lista.</b> Es lo que hace que la pantalla siga siendo usable con
///    veinte documentos colgados.
///  · <b>El nombre se sanea</b> antes de guardarse, porque acaba en una cabecera
///    <c>Content-Disposition</c> y en el disco de quien descarga.
/// </summary>
public class RequirementAttachmentServiceTests
{
    private static RequirementAttachmentService Svc(AppDbContext db, ICurrentUser quien) =>
        new(db, quien, new AuditService(db, quien, new OrigenDePrueba()));

    private static RequirementAttachmentService ComoLider(AppDbContext db) =>
        Svc(db, UsuarioDePrueba.Como(UserRole.Admin, developerId: null, userId: 99));

    private static RequirementAttachmentService ComoDesarrollador(AppDbContext db) =>
        Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 3, userId: 3));

    private static Requirement Requerimiento(AppDbContext db, string titulo = "Pantalla de pagos")
    {
        var r = new Requirement { Title = titulo, CreatedAt = DateTime.UtcNow, StatusChangedAt = DateTime.UtcNow };
        db.Requirements.Add(r);
        db.SaveChanges();
        return r;
    }

    // ── Alta ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ElDocumentoSeGuardaConSuTipoSuTamanoYElNombreSaneado()
    {
        var db = TestDb.New();
        var r = Requerimiento(db);

        var (ok, mensaje, id) = await ComoLider(db).AgregarAsync(
            r.Id, RequirementAttachmentKind.Estimacion, @"C:\docs\estimacion.xlsx", [1, 2, 3, 4, 5]);

        Assert.True(ok);
        Assert.Contains("estimación", mensaje, StringComparison.OrdinalIgnoreCase);

        var guardado = db.RequirementAttachments.AsNoTracking().Single(a => a.Id == id);
        Assert.Equal(r.Id, guardado.RequirementId);
        Assert.Equal(RequirementAttachmentKind.Estimacion, guardado.Kind);
        Assert.Equal("estimacion.xlsx", guardado.FileName);
        Assert.Equal(5, guardado.SizeBytes);
        Assert.Equal(99, guardado.UploadedByUserId);
    }

    [Fact]
    public async Task NoSeCuelgaNadaDeUnRequerimientoQueYaNoExiste()
    {
        var db = TestDb.New();

        var (ok, mensaje, _) = await ComoLider(db).AgregarAsync(
            999, RequirementAttachmentKind.Requerimiento, "doc.pdf", [1]);

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.RequirementAttachments);
    }

    [Fact]
    public async Task MasAlliaDelTopeDejaDeSerDocumentacionYSeRehusa()
    {
        var db = TestDb.New();
        var r = Requerimiento(db);
        var svc = ComoLider(db);

        for (int i = 0; i < RequirementAttachmentService.MaxPorRequerimiento; i++)
            await svc.AgregarAsync(r.Id, RequirementAttachmentKind.Otro, $"doc{i}.pdf", [1]);

        var (ok, mensaje, _) = await svc.AgregarAsync(
            r.Id, RequirementAttachmentKind.Otro, "uno_mas.pdf", [1]);

        Assert.False(ok);
        Assert.Contains(RequirementAttachmentService.MaxPorRequerimiento.ToString(), mensaje);
        Assert.Equal(RequirementAttachmentService.MaxPorRequerimiento, db.RequirementAttachments.Count());
    }

    // ── Lectura ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaListaVieneOrdenadaPorTipoYSinLosBytes()
    {
        // El orden es el del escritorio: el documento de requerimiento antes que el de estimación.
        var db = TestDb.New();
        var r = Requerimiento(db);
        var svc = ComoLider(db);
        await svc.AgregarAsync(r.Id, RequirementAttachmentKind.Estimacion, "estimacion.xlsx", [1, 2]);
        await svc.AgregarAsync(r.Id, RequirementAttachmentKind.Requerimiento, "requerimiento.docx", [1, 2, 3]);

        var lista = await svc.DeRequerimientoAsync(r.Id);

        Assert.Equal(2, lista.Count);
        Assert.Equal(RequirementAttachmentKind.Requerimiento, lista[0].Tipo);
        Assert.Equal("Requerimiento", lista[0].TipoTexto);
        Assert.Equal(3, lista[0].Bytes);
        Assert.Equal("Estimación", lista[1].TipoTexto);

        // El contrato de la lista no tiene dónde meter los bytes, y ese es justo el punto: no hay
        // forma de que un refresco de la pantalla se traiga decenas de megas sin querer.
        Assert.All(lista, a => Assert.False(string.IsNullOrEmpty(a.Nombre)));
    }

    [Fact]
    public async Task ElConteoPorRequerimientoSaleDeUnaSolaConsulta()
    {
        var db = TestDb.New();
        var uno = Requerimiento(db, "Uno");
        var otro = Requerimiento(db, "Otro");
        var vacio = Requerimiento(db, "Sin nada");
        var svc = ComoLider(db);
        await svc.AgregarAsync(uno.Id, RequirementAttachmentKind.Requerimiento, "a.pdf", [1]);
        await svc.AgregarAsync(uno.Id, RequirementAttachmentKind.Estimacion, "b.pdf", [1]);
        await svc.AgregarAsync(otro.Id, RequirementAttachmentKind.Requerimiento, "c.pdf", [1]);

        var conteo = await svc.ConteoPorRequerimientoAsync([uno.Id, otro.Id, vacio.Id]);

        Assert.Equal(2, conteo[uno.Id]);
        Assert.Equal(1, conteo[otro.Id]);
        Assert.False(conteo.ContainsKey(vacio.Id));
    }

    [Fact]
    public async Task ElContenidoSeBajaPorSuIdentificador()
    {
        var db = TestDb.New();
        var r = Requerimiento(db);
        var svc = ComoLider(db);
        var (_, _, id) = await svc.AgregarAsync(
            r.Id, RequirementAttachmentKind.Requerimiento, "requerimiento.docx", [1, 2, 3]);

        var (bytes, nombre) = await svc.BytesAsync(id);

        Assert.Equal(3, bytes.Length);
        Assert.Equal("requerimiento.docx", nombre);
    }

    [Fact]
    public async Task UnDocumentoQueYaNoEstaVuelveVacio()
    {
        // Vacío y no excepción: el endpoint lo traduce a 404, que es lo que corresponde a algo que ya
        // no está.
        var db = TestDb.New();

        var (bytes, nombre) = await ComoLider(db).BytesAsync(999);

        Assert.Empty(bytes);
        Assert.Equal("", nombre);
    }

    // ── Baja ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EliminarUnDocumentoSiLoBorra()
    {
        // Al revés que cancelar un requerimiento: un archivo subido por equivocación no es historia.
        var db = TestDb.New();
        var r = Requerimiento(db);
        var svc = ComoLider(db);
        var (_, _, id) = await svc.AgregarAsync(r.Id, RequirementAttachmentKind.Requerimiento, "doc.pdf", [1]);

        var (ok, _) = await svc.EliminarAsync(id);

        Assert.True(ok);
        Assert.Empty(db.RequirementAttachments);
    }

    [Fact]
    public async Task EliminarAlgoQueYaNoEstaLoDice()
    {
        var db = TestDb.New();

        var (ok, mensaje) = await ComoLider(db).EliminarAsync(999);

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    // ── Permisos ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnDesarrolladorNoBajaLosDocumentosDeUnRequerimiento()
    {
        // La misma barrera que el resto de la pantalla: es del líder de punta a punta. Sin esto,
        // cualquiera con sesión leería la estimación probando identificadores.
        var db = TestDb.New();
        var r = Requerimiento(db);
        var (_, _, id) = await ComoLider(db).AgregarAsync(
            r.Id, RequirementAttachmentKind.Estimacion, "estimacion.xlsx", [1, 2, 3]);

        await Assert.ThrowsAsync<AuthorizationException>(() => ComoDesarrollador(db).BytesAsync(id));
    }

    [Fact]
    public async Task UnDesarrolladorNiLosListaNiLosSubeNiLosBorra()
    {
        var db = TestDb.New();
        var r = Requerimiento(db);
        var (_, _, id) = await ComoLider(db).AgregarAsync(
            r.Id, RequirementAttachmentKind.Requerimiento, "doc.pdf", [1]);
        var dev = ComoDesarrollador(db);

        await Assert.ThrowsAsync<AuthorizationException>(() => dev.DeRequerimientoAsync(r.Id));
        await Assert.ThrowsAsync<AuthorizationException>(
            () => dev.AgregarAsync(r.Id, RequirementAttachmentKind.Requerimiento, "suyo.pdf", [1]));
        await Assert.ThrowsAsync<AuthorizationException>(() => dev.EliminarAsync(id));

        Assert.Single(db.RequirementAttachments);
    }
}
