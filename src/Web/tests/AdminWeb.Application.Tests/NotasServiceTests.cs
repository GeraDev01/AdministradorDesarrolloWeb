using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Notas;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Notas y pendientes del líder.
///
/// Lo que se cuida aquí es lo que el escritorio no podía cuidar porque no tenía servidor: que el
/// módulo entero sea del líder, que un desarrollador inexistente se explique en vez de reventar con
/// un error de clave foránea, y que la fecha de recordatorio siga siendo el MISMO DÍA se mire desde
/// donde se mire — la web y el escritorio leen esa columna de la misma base mientras convivan.
/// </summary>
public class NotasServiceTests
{
    private static NotasService Svc(AppDbContext db, ICurrentUser? cu = null)
    {
        var actual = cu ?? UsuarioDePrueba.Como(UserRole.Admin, userId: 1);
        return new NotasService(db, actual, new AuditService(db, actual, new OrigenDePrueba()));
    }

    private static int SembrarDesarrollador(AppDbContext db, string nombre, bool activo = true)
    {
        var dev = new Developer { FullName = nombre, IsActive = activo };
        db.Developers.Add(dev);
        db.SaveChanges();
        return dev.Id;
    }

    private static GuardarNotaRequest Nueva(
        string titulo = "Ana pidió cambiar de proyecto",
        string? contenido = "Lo comentó al final de la daily.",
        int? desarrolladorId = null,
        NotePriority prioridad = NotePriority.Media,
        DateTime? recordatorio = null,
        bool completada = false) =>
        new(0, titulo, contenido, desarrolladorId, prioridad, recordatorio, completada);

    // ── Permisos ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Listar_LoExigeElLider()
    {
        var db = TestDb.New();
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 7, userId: 2));

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.ListarAsync());
    }

    [Fact]
    public async Task Guardar_LoExigeElLider()
    {
        var db = TestDb.New();
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Operaciones, userId: 2));

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.GuardarAsync(Nueva()));
        Assert.Empty(db.Notes);
    }

    /// <summary>
    /// Una nota puede recoger lo que alguien pidió en privado. Que lleve el nombre de un
    /// desarrollador NO se la abre: el campo es de origen, no de destinatario.
    /// </summary>
    [Fact]
    public async Task Completar_NoLaAbreAlDesarrolladorAlQueApunta()
    {
        var db = TestDb.New();
        int ana = SembrarDesarrollador(db, "Ana");
        var (_, _, id) = await Svc(db).GuardarAsync(Nueva(desarrolladorId: ana));

        var suya = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: ana, userId: 2));

        await Assert.ThrowsAsync<AuthorizationException>(() => suya.CompletarAsync(id, true));
        await Assert.ThrowsAsync<AuthorizationException>(() => suya.EliminarAsync(id));
        Assert.False(db.Notes.AsNoTracking().Single().IsCompleted);
    }

    // ── Validación ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Guardar_ExigeTitulo()
    {
        var db = TestDb.New();

        var (ok, mensaje, _) = await Svc(db).GuardarAsync(Nueva(titulo: "   "));

        Assert.False(ok);
        Assert.Equal("El título es obligatorio.", mensaje);
        Assert.Empty(db.Notes);
    }

    [Fact]
    public async Task Guardar_ConUnDesarrolladorQueYaNoExiste_LoDiceEnVezDeReventar()
    {
        var db = TestDb.New();

        var (ok, mensaje, _) = await Svc(db).GuardarAsync(Nueva(desarrolladorId: 999));

        Assert.False(ok);
        Assert.Contains("desarrollador", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.Notes);
    }

    [Fact]
    public async Task Guardar_NotaInexistente_LoDiceEnVezDeCrearlaDeNuevo()
    {
        var db = TestDb.New();

        var (ok, mensaje, _) = await Svc(db).GuardarAsync(
            new GuardarNotaRequest(404, "Fantasma", null, null, NotePriority.Alta, null, false));

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje);
        Assert.Empty(db.Notes);
    }

    [Fact]
    public async Task Guardar_ContenidoDemasiadoLargo_SeRechazaConSuTope()
    {
        var db = TestDb.New();

        var (ok, mensaje, _) = await Svc(db).GuardarAsync(
            Nueva(contenido: new string('x', NotasService.MaxContenido + 1)));

        Assert.False(ok);
        Assert.Contains(NotasService.MaxContenido.ToString(), mensaje);
        Assert.Empty(db.Notes);
    }

    // ── Alta y edición ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Guardar_Alta_GuardaLaNotaYQuedaEnLaBitacora()
    {
        var db = TestDb.New();
        int ana = SembrarDesarrollador(db, "Ana");

        var (ok, mensaje, id) = await Svc(db).GuardarAsync(Nueva(
            titulo: "  Revisar el acceso de Ana  ", desarrolladorId: ana, prioridad: NotePriority.Alta));

        Assert.True(ok);
        Assert.Contains("Revisar el acceso de Ana", mensaje);

        var nota = db.Notes.AsNoTracking().Single();
        Assert.Equal(id, nota.Id);
        Assert.Equal("Revisar el acceso de Ana", nota.Title);      // recortada
        Assert.Equal(ana, nota.DeveloperId);
        Assert.Equal(NotePriority.Alta, nota.Priority);
        Assert.False(nota.IsCompleted);

        var renglon = db.AuditLogs.AsNoTracking().Single();
        Assert.Equal(AuditAction.Create, renglon.Action);
        Assert.Equal("Note", renglon.EntityType);
    }

    [Fact]
    public async Task Guardar_ContenidoEnBlanco_SeGuardaComoNulo()
    {
        var db = TestDb.New();

        await Svc(db).GuardarAsync(Nueva(contenido: "   "));

        Assert.Null(db.Notes.AsNoTracking().Single().Content);
    }

    [Fact]
    public async Task Guardar_Edicion_CambiaLoCapturadoYConservaElAlta()
    {
        var db = TestDb.New();
        int beto = SembrarDesarrollador(db, "Beto");
        var (_, _, id) = await Svc(db).GuardarAsync(Nueva());

        var alta = db.Notes.AsNoTracking().Single().CreatedAt;

        var (ok, _, mismoId) = await Svc(db).GuardarAsync(new GuardarNotaRequest(
            id, "Ya se habló con Ana", "Queda cerrado.", beto, NotePriority.Baja,
            new DateTime(2026, 8, 20), true));

        Assert.True(ok);
        Assert.Equal(id, mismoId);

        var nota = db.Notes.AsNoTracking().Single();
        Assert.Equal("Ya se habló con Ana", nota.Title);
        Assert.Equal(beto, nota.DeveloperId);
        Assert.Equal(NotePriority.Baja, nota.Priority);
        Assert.True(nota.IsCompleted);
        Assert.Equal(alta, nota.CreatedAt);   // la fecha de alta no la mueve una edición
    }

    /// <summary>
    /// El día del recordatorio tiene que ser el mismo se mire desde donde se mire: el escritorio lee
    /// esa columna de la misma base y la pinta sin convertir.
    /// </summary>
    [Fact]
    public async Task Guardar_ElRecordatorioEsElMismoDiaSeMireDesdeDondeSeMire()
    {
        var db = TestDb.New();

        await Svc(db).GuardarAsync(Nueva(recordatorio: new DateTime(2026, 8, 20, 17, 45, 0)));

        var guardada = db.Notes.AsNoTracking().Single();
        var fecha = guardada.ReminderDate!.Value;

        // A mediodía y no a medianoche: aguanta once horas de desfase en las dos direcciones.
        Assert.Equal(12, fecha.Hour);
        Assert.Equal(new DateTime(2026, 8, 20), fecha.AddHours(-11).Date);   // huso +11
        Assert.Equal(new DateTime(2026, 8, 20), fecha.AddHours(11).Date);    // huso -11
    }

    [Fact]
    public async Task Guardar_SinRecordatorio_SeGuardaVacio()
    {
        var db = TestDb.New();

        await Svc(db).GuardarAsync(Nueva(recordatorio: null));

        Assert.Null(db.Notes.AsNoTracking().Single().ReminderDate);
    }

    // ── Completar y reabrir ──────────────────────────────────────────────────────

    [Fact]
    public async Task Completar_CierraYReabre()
    {
        var db = TestDb.New();
        var (_, _, id) = await Svc(db).GuardarAsync(Nueva());

        var (cerrada, mensajeCierre) = await Svc(db).CompletarAsync(id, true);
        Assert.True(cerrada);
        Assert.Contains("completada", mensajeCierre);
        Assert.True(db.Notes.AsNoTracking().Single().IsCompleted);

        var (reabierta, mensajeReapertura) = await Svc(db).CompletarAsync(id, false);
        Assert.True(reabierta);
        Assert.Contains("pendientes", mensajeReapertura);
        Assert.False(db.Notes.AsNoTracking().Single().IsCompleted);
    }

    [Fact]
    public async Task Completar_DosVecesLoMismo_NoInventaUnSegundoCambio()
    {
        var db = TestDb.New();
        var (_, _, id) = await Svc(db).GuardarAsync(Nueva());
        await Svc(db).CompletarAsync(id, true);

        var (ok, mensaje) = await Svc(db).CompletarAsync(id, true);

        Assert.False(ok);
        Assert.Contains("ya estaba completada", mensaje);

        // Un solo apunte de cierre en la bitácora, no dos.
        Assert.Single(db.AuditLogs.AsNoTracking().Where(a => a.Details!.StartsWith("Completada:")));
    }

    [Fact]
    public async Task Completar_UnaQueYaNoEsta_LoDice()
    {
        var db = TestDb.New();

        var (ok, mensaje) = await Svc(db).CompletarAsync(404, true);

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje);
    }

    // ── Baja ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Eliminar_AvisaSiSeguiaPendiente()
    {
        var db = TestDb.New();
        var (_, _, id) = await Svc(db).GuardarAsync(Nueva());

        var (ok, mensaje) = await Svc(db).EliminarAsync(id);

        Assert.True(ok);
        Assert.Contains("seguía pendiente", mensaje);
        Assert.Empty(db.Notes);
        Assert.Contains(db.AuditLogs.AsNoTracking(), a => a.Action == AuditAction.Delete && a.EntityType == "Note");
    }

    [Fact]
    public async Task Eliminar_UnaQueYaNoEsta_LoDice()
    {
        var db = TestDb.New();

        var (ok, mensaje) = await Svc(db).EliminarAsync(404);

        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje);
    }

    // ── Lista ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Listar_TraeLoRecienteArribaConSuEtiquetaYSuOrigen()
    {
        var db = TestDb.New();
        int ana = SembrarDesarrollador(db, "Ana");
        var svc = Svc(db);

        await svc.GuardarAsync(Nueva(titulo: "La primera", prioridad: NotePriority.Baja));
        await svc.GuardarAsync(Nueva(titulo: "La segunda", desarrolladorId: ana, prioridad: NotePriority.Alta));

        var datos = await svc.ListarAsync();

        Assert.Equal(2, datos.Notas.Count);
        Assert.Equal("La segunda", datos.Notas[0].Titulo);       // más reciente arriba
        Assert.Equal("Ana", datos.Notas[0].Desarrollador);
        Assert.Equal("🔴 Alta", datos.Notas[0].PrioridadTexto);
        Assert.Null(datos.Notas[1].Desarrollador);               // sin origen apuntado
        Assert.Equal(3, datos.Prioridades.Count);
    }

    [Fact]
    public async Task Listar_MarcaAtrasadaSoloLaPendienteConElRecordatorioPasado()
    {
        var db = TestDb.New();
        var svc = Svc(db);

        await svc.GuardarAsync(Nueva(titulo: "Vencida", recordatorio: DateTime.Today.AddDays(-3)));
        await svc.GuardarAsync(Nueva(titulo: "Para la semana que viene", recordatorio: DateTime.Today.AddDays(7)));
        await svc.GuardarAsync(Nueva(titulo: "Sin fecha"));
        var (_, _, cerrada) = await svc.GuardarAsync(
            Nueva(titulo: "Vencida pero ya cerrada", recordatorio: DateTime.Today.AddDays(-3)));
        await svc.CompletarAsync(cerrada, true);

        var datos = await svc.ListarAsync();

        Assert.True(datos.Notas.Single(n => n.Titulo == "Vencida").Atrasada);
        Assert.False(datos.Notas.Single(n => n.Titulo == "Para la semana que viene").Atrasada);
        Assert.False(datos.Notas.Single(n => n.Titulo == "Sin fecha").Atrasada);
        Assert.False(datos.Notas.Single(n => n.Titulo == "Vencida pero ya cerrada").Atrasada);

        Assert.Equal(3, datos.Pendientes);
        Assert.Equal(1, datos.Vencidas);
    }

    [Fact]
    public async Task Listar_SoloPendientes_EscondeLasCerradasSinFalsearLosContadores()
    {
        var db = TestDb.New();
        var svc = Svc(db);

        await svc.GuardarAsync(Nueva(titulo: "Sigue abierta"));
        var (_, _, id) = await svc.GuardarAsync(Nueva(titulo: "Ya cerrada"));
        await svc.CompletarAsync(id, true);

        var todas = await svc.ListarAsync();
        Assert.Equal(2, todas.Notas.Count);

        var pendientes = await svc.ListarAsync(soloPendientes: true);
        Assert.Equal("Sigue abierta", Assert.Single(pendientes.Notas).Titulo);

        // El contador NO lo mueve el filtro: cuenta sobre todas las notas.
        Assert.Equal(1, pendientes.Pendientes);
    }

    [Fact]
    public async Task Listar_TraeSoloLosDesarrolladoresActivosComoOrigen()
    {
        var db = TestDb.New();
        SembrarDesarrollador(db, "Ana");
        SembrarDesarrollador(db, "Ya no está", activo: false);

        var datos = await Svc(db).ListarAsync();

        Assert.Equal("Ana", Assert.Single(datos.Desarrolladores).Texto);
    }

    // ── Exportación ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Excel_ExportaLoQueElFiltroEnsena()
    {
        var db = TestDb.New();
        var svc = Svc(db);
        await svc.GuardarAsync(Nueva(titulo: "Sigue abierta"));
        var (_, _, id) = await svc.GuardarAsync(Nueva(titulo: "Ya cerrada"));
        await svc.CompletarAsync(id, true);

        var libro = await svc.ExcelAsync(soloPendientes: true);

        // Es un ZIP: comprobar la firma descarta el fallo real —un flujo vacío o a medio escribir—
        // sin abrir el archivo en la prueba.
        Assert.True(libro.Length > 0);
        Assert.Equal((byte)'P', libro[0]);
        Assert.Equal((byte)'K', libro[1]);
    }

    [Fact]
    public async Task Excel_LoExigeElLider()
    {
        var db = TestDb.New();
        var svc = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: 1, userId: 2));

        await Assert.ThrowsAsync<AuthorizationException>(() => svc.ExcelAsync());
    }
}
