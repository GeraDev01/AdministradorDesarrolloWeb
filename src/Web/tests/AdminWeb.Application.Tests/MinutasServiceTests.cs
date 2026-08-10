using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Administracion;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Las minutas y sus compromisos.
///
/// Lo que se cuida aquí es la sincronización de los compromisos, que es donde el escritorio tenía el
/// defecto: al guardar, el conjunto de «ids que se conservan» se tomaba tal cual del formulario, así
/// que un id de OTRA minuta bastaba para salvar de la baja a un compromiso ajeno. Y la fecha, que es
/// un día del calendario y tiene que seguir siendo el mismo día se mire desde donde se mire.
/// </summary>
public class MinutasServiceTests
{
    private static MinutasService Svc(AppDbContext db, ICurrentUser? cu = null)
    {
        var actual = cu ?? UsuarioDePrueba.Como(UserRole.Admin, userId: 1);
        return new MinutasService(db, actual, new AuditService(db, actual, new OrigenDePrueba()));
    }

    private static int SembrarDesarrollador(AppDbContext db, string nombre)
    {
        var dev = new Developer { FullName = nombre, IsActive = true };
        db.Developers.Add(dev);
        db.SaveChanges();
        return dev.Id;
    }

    private static GuardarMinutaRequest Nueva(
        string titulo = "Daily del martes",
        MinuteType tipo = MinuteType.Daily,
        DateTime? fecha = null,
        params CompromisoRequest[] compromisos) =>
        new(0, tipo, fecha ?? new DateTime(2026, 8, 4), titulo, "Se habló de la entrega.", compromisos);

    // ── Permisos y validación ────────────────────────────────────────────────────

    [Fact]
    public async Task Listar_LoExigeElLider()
    {
        var db = TestDb.New();
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 2));

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.ListarAsync(null, null, null));
    }

    [Fact]
    public async Task Guardar_LoExigeElLider()
    {
        var db = TestDb.New();
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Operaciones, userId: 2));

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.GuardarAsync(Nueva()));
        Assert.Empty(db.Minutes);
    }

    [Fact]
    public async Task Guardar_ExigeTitulo()
    {
        var db = TestDb.New();

        var (ok, mensaje, _) = await Svc(db).GuardarAsync(Nueva(titulo: "   "));

        Assert.False(ok);
        Assert.Equal("El título es obligatorio.", mensaje);
        Assert.Empty(db.Minutes);
    }

    [Fact]
    public async Task Guardar_ConUnResponsableQueYaNoExiste_LoDiceEnVezDeReventar()
    {
        var db = TestDb.New();

        var (ok, mensaje, _) = await Svc(db).GuardarAsync(
            Nueva(compromisos: new CompromisoRequest(0, "Revisar", 999, null, false)));

        Assert.False(ok);
        Assert.Contains("responsable", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Minutes);
    }

    [Fact]
    public async Task Guardar_MinutaInexistente_LoDice()
    {
        var db = TestDb.New();

        var (ok, mensaje, _) = await Svc(db).GuardarAsync(
            new GuardarMinutaRequest(404, MinuteType.Daily, DateTime.Today, "Fantasma", null, []));

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje);
    }

    // ── Alta ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guardar_Alta_GuardaLaMinutaConSusCompromisos()
    {
        var db = TestDb.New();
        int ana = SembrarDesarrollador(db, "Ana");

        var (ok, mensaje, id) = await Svc(db).GuardarAsync(Nueva(
            compromisos:
            [
                new CompromisoRequest(0, " Revisar el PR ", ana, new DateTime(2026, 8, 8), false),
                new CompromisoRequest(0, "Avisar al cliente", null, null, true)
            ]));

        Assert.True(ok);
        Assert.Contains("2 compromiso(s)", mensaje);
        Assert.Contains("1 pendiente(s)", mensaje);

        var minuta = db.Minutes.Include(m => m.ActionItems).AsNoTracking().Single();
        Assert.Equal(id, minuta.Id);
        Assert.Equal("Daily del martes", minuta.Title);
        Assert.Equal(2, minuta.ActionItems.Count);
        Assert.Contains(minuta.ActionItems, i => i.Description == "Revisar el PR" && i.ResponsibleDeveloperId == ana);

        // Queda registrada, con el título como detalle.
        var renglon = db.AuditLogs.AsNoTracking().Single();
        Assert.Equal(AuditAction.Create, renglon.Action);
        Assert.Equal("Minute", renglon.EntityType);
    }

    [Fact]
    public async Task Guardar_DescartaLosCompromisosSinDescripcion()
    {
        var db = TestDb.New();

        await Svc(db).GuardarAsync(Nueva(compromisos:
        [
            new CompromisoRequest(0, "Con texto", null, null, false),
            new CompromisoRequest(0, "   ", null, null, false)
        ]));

        Assert.Single(db.MinuteActionItems);
    }

    [Fact]
    public async Task Guardar_LaFechaEsElMismoDiaSeMireDesdeDondeSeMire()
    {
        var db = TestDb.New();

        var (_, _, id) = await Svc(db).GuardarAsync(Nueva(fecha: new DateTime(2026, 8, 4)));
        var minuta = await Svc(db).ObtenerAsync(id);

        Assert.Equal(new DateTime(2026, 8, 4), minuta!.Fecha.Date);

        // Guardada a mediodía y no a medianoche: el escritorio pinta esa columna con ToLocalTime(),
        // y una medianoche UTC se le convierte en el día anterior en cualquier huso al oeste.
        var guardada = db.Minutes.AsNoTracking().Single();
        Assert.Equal(12, guardada.Date.Hour);
        Assert.Equal(new DateTime(2026, 8, 4), guardada.Date.AddHours(-11).Date);   // huso +11
        Assert.Equal(new DateTime(2026, 8, 4), guardada.Date.AddHours(11).Date);    // huso -11
    }

    // ── Edición ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guardar_Edicion_SincronizaLosCompromisos()
    {
        var db = TestDb.New();
        int ana = SembrarDesarrollador(db, "Ana");

        var (_, _, id) = await Svc(db).GuardarAsync(Nueva(compromisos:
        [
            new CompromisoRequest(0, "Se queda", null, null, false),
            new CompromisoRequest(0, "Se va", null, null, false)
        ]));

        var original = await Svc(db).ObtenerAsync(id);
        var seQueda = original!.Compromisos.Single(c => c.Descripcion == "Se queda");

        var (ok, _, _) = await Svc(db).GuardarAsync(new GuardarMinutaRequest(
            id, MinuteType.Sesion, new DateTime(2026, 8, 5), "Sesión de diseño", "Otro contenido",
            [
                new CompromisoRequest(seQueda.Id, "Se queda, ya cumplido", ana, null, true),
                new CompromisoRequest(0, "Nuevo", null, null, false)
            ]));

        Assert.True(ok);

        var minuta = db.Minutes.Include(m => m.ActionItems).AsNoTracking().Single();
        Assert.Equal(MinuteType.Sesion, minuta.Type);
        Assert.Equal("Sesión de diseño", minuta.Title);

        var descripciones = minuta.ActionItems.Select(i => i.Description).OrderBy(d => d).ToList();
        Assert.Equal(["Nuevo", "Se queda, ya cumplido"], descripciones);

        var conservado = minuta.ActionItems.Single(i => i.Id == seQueda.Id);
        Assert.True(conservado.IsCompleted);
        Assert.Equal(ana, conservado.ResponsibleDeveloperId);
    }

    [Fact]
    public async Task Guardar_NoSalvaCompromisosDeOtraMinuta()
    {
        var db = TestDb.New();

        var (_, _, ajena) = await Svc(db).GuardarAsync(Nueva(titulo: "Ajena",
            compromisos: new CompromisoRequest(0, "De la otra", null, null, false)));
        var (_, _, mia) = await Svc(db).GuardarAsync(Nueva(titulo: "Mía",
            compromisos: new CompromisoRequest(0, "Mío", null, null, false)));

        int idAjeno = (await Svc(db).ObtenerAsync(ajena))!.Compromisos.Single().Id;

        // Se manda el id de un compromiso de OTRA minuta. En el escritorio ese id entraba en el
        // conjunto de «conservados» y el compromiso propio se iba sin que nadie lo pidiera; aquí el
        // id ajeno se trata como un alta y lo propio se sincroniza contra lo que de verdad es suyo.
        var (ok, _, _) = await Svc(db).GuardarAsync(new GuardarMinutaRequest(
            mia, MinuteType.Daily, new DateTime(2026, 8, 4), "Mía", null,
            [new CompromisoRequest(idAjeno, "Colado", null, null, false)]));

        Assert.True(ok);

        // El de la otra minuta sigue donde estaba, con su texto original.
        var deLaOtra = db.MinuteActionItems.AsNoTracking().Single(i => i.Id == idAjeno);
        Assert.Equal("De la otra", deLaOtra.Description);
        Assert.Equal(ajena, deLaOtra.MinuteId);

        // Y en la mía quedó exactamente uno: el que se mandó, dado de alta.
        var mios = db.MinuteActionItems.AsNoTracking().Where(i => i.MinuteId == mia).ToList();
        Assert.Equal("Colado", Assert.Single(mios).Description);
    }

    // ── Lista y filtros ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Listar_FiltraPorTipoYPorPeriodoContandoLosPendientes()
    {
        var db = TestDb.New();
        var svc = Svc(db);

        await svc.GuardarAsync(Nueva(titulo: "Daily de marzo", tipo: MinuteType.Daily,
            fecha: new DateTime(2026, 3, 10),
            compromisos:
            [
                new CompromisoRequest(0, "Pendiente", null, null, false),
                new CompromisoRequest(0, "Cumplido", null, null, true)
            ]));
        await svc.GuardarAsync(Nueva(titulo: "Sesión de mayo", tipo: MinuteType.Sesion,
            fecha: new DateTime(2026, 5, 10)));

        var todo = await svc.ListarAsync(null, null, null);
        Assert.Equal(2, todo.Minutas.Count);
        Assert.Equal(3, todo.Tipos.Count);

        var marzo = todo.Minutas.Single(m => m.Titulo == "Daily de marzo");
        Assert.Equal("Daily", marzo.TipoTexto);
        Assert.Equal(2, marzo.Compromisos);
        Assert.Equal(1, marzo.Pendientes);

        var porTipo = await svc.ListarAsync(MinuteType.Sesion, null, null);
        Assert.Equal("Sesión de mayo", Assert.Single(porTipo.Minutas).Titulo);

        // El día «hasta» entra completo: la minuta del 10 de marzo cae dentro de un filtro que
        // termina el 10 de marzo.
        var porFecha = await svc.ListarAsync(null, new DateTime(2026, 3, 1), new DateTime(2026, 3, 10));
        Assert.Equal("Daily de marzo", Assert.Single(porFecha.Minutas).Titulo);
    }

    [Fact]
    public async Task Listar_TraeSoloLosDesarrolladoresActivosComoResponsables()
    {
        var db = TestDb.New();
        SembrarDesarrollador(db, "Ana");
        db.Developers.Add(new Developer { FullName = "Ya no está", IsActive = false });
        db.SaveChanges();

        var datos = await Svc(db).ListarAsync(null, null, null);

        Assert.Equal("Ana", Assert.Single(datos.Responsables).Texto);
    }

    // ── Baja y exportación ───────────────────────────────────────────────────────

    [Fact]
    public async Task Eliminar_SeLlevaLosCompromisosYAvisaDeLosPendientes()
    {
        var db = TestDb.New();
        var (_, _, id) = await Svc(db).GuardarAsync(Nueva(compromisos:
            new CompromisoRequest(0, "Sin cerrar", null, null, false)));

        var (ok, mensaje) = await Svc(db).EliminarAsync(id);

        Assert.True(ok);
        Assert.Contains("1 compromiso(s) que seguían pendientes", mensaje);
        Assert.Empty(db.Minutes);
        Assert.Empty(db.MinuteActionItems);
    }

    [Fact]
    public async Task Eliminar_UnaQueYaNoEsta_LoDice()
    {
        var db = TestDb.New();

        var (ok, mensaje) = await Svc(db).EliminarAsync(404);

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje);
    }

    [Fact]
    public async Task Excel_ExportaLoQueElFiltroEnsena()
    {
        var db = TestDb.New();
        var svc = Svc(db);
        await svc.GuardarAsync(Nueva(titulo: "De marzo", fecha: new DateTime(2026, 3, 10)));
        await svc.GuardarAsync(Nueva(titulo: "De mayo", fecha: new DateTime(2026, 5, 10)));

        var libro = await svc.ExcelAsync(null, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));

        // Es un ZIP: comprobar la firma descarta el fallo real —un flujo vacío o a medio escribir—
        // sin abrir el archivo en la prueba.
        Assert.True(libro.Length > 0);
        Assert.Equal((byte)'P', libro[0]);
        Assert.Equal((byte)'K', libro[1]);
    }
}
