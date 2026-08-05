using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Evidencia de las actividades libres, y el buscador del cuadro de requerimientos del sprint.
///
/// En la evidencia hay dos cosas que cuidar: que el listado NO arrastre los archivos (la base es
/// compartida y se lee por red, así que traer los BLOB en cada refresco lo paga todo el equipo) y
/// que no se pueda colar un ejecutable — la evidencia acaba volcada a un temporal que se abre con
/// el programa asociado.
/// </summary>
public class DevActivityEvidenceTests
{
    private static readonly byte[] PngFalso = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static DevActivityService Svc(AppDbContext db, CurrentUserContext cu) =>
        new(db, cu, new AuditService(db, cu), new WorkSessionService(db, new AuditService(db, cu), cu));

    private static (AppDbContext db, Developer dev, DevActivity act, CurrentUserContext yo, CurrentUserContext jefa) Entorno()
    {
        var db = TestDb.New();
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev); db.SaveChanges();

        var yo = Ctx.As(UserRole.Desarrollador, developerId: dev.Id, userId: 10);
        var (_, _, act) = Svc(db, yo).Crear(dev.Id, "Apoyo a soporte", "Ayudé con la migración del cliente X.");

        return (db, dev, act!, yo, Ctx.As(UserRole.Admin, developerId: null, userId: 900));
    }

    // ── Adjuntar ─────────────────────────────────────────────────────────────

    [Fact]
    public void Agregar_GuardaLaEvidenciaYDetectaElTipoPorLosBytes()
    {
        var (db, _, act, yo, _) = Entorno();

        var (ok, _, ev) = Svc(db, yo).AgregarEvidencia(act.Id, "captura.png", PngFalso, "Pantalla del error");

        Assert.True(ok);
        Assert.NotNull(ev);
        var g = db.DevActivityAttachments.AsNoTracking().Single();
        Assert.Equal("image/png", g.ContentType);
        Assert.Equal(PngFalso.Length, g.SizeBytes);
        Assert.Equal("Pantalla del error", g.Description);
        Assert.True(g.EsImagen);
    }

    /// <summary>
    /// El tipo se decide por el CONTENIDO, no por el nombre: el nombre lo pone quien sube el
    /// archivo y llamarle «.pdf» a un PNG no lo convierte en PDF.
    /// </summary>
    [Fact]
    public void Agregar_ElNombreNoDecideElTipo()
    {
        var (db, _, act, yo, _) = Entorno();
        Svc(db, yo).AgregarEvidencia(act.Id, "documento.pdf", PngFalso);

        Assert.Equal("image/png", db.DevActivityAttachments.AsNoTracking().Single().ContentType);
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("script.ps1")]
    [InlineData("atajo.lnk")]
    [InlineData("macro.VBS")]
    public void Agregar_RechazaEjecutables(string nombre)
    {
        var (db, _, act, yo, _) = Entorno();

        var (ok, mensaje, _) = Svc(db, yo).AgregarEvidencia(act.Id, nombre, [1, 2, 3]);

        Assert.False(ok);
        Assert.Contains("No se admiten", mensaje);
        Assert.Empty(db.DevActivityAttachments);
    }

    [Fact]
    public void Agregar_RechazaLoVacioYLoDemasiadoGrande()
    {
        var (db, _, act, yo, _) = Entorno();
        var svc = Svc(db, yo);

        Assert.False(svc.AgregarEvidencia(act.Id, "vacio.png", []).ok);
        Assert.False(svc.AgregarEvidencia(act.Id, "enorme.png", new byte[DevActivityService.MaxEvidenciaBytes + 1]).ok);
        Assert.Empty(db.DevActivityAttachments);
    }

    [Fact]
    public void Agregar_TopeDeArchivosPorActividad()
    {
        var (db, _, act, yo, _) = Entorno();
        var svc = Svc(db, yo);

        for (int i = 0; i < DevActivityService.MaxEvidenciasPorActividad; i++)
            Assert.True(svc.AgregarEvidencia(act.Id, $"captura{i}.png", PngFalso).ok);

        var (ok, mensaje, _) = svc.AgregarEvidencia(act.Id, "una-mas.png", PngFalso);
        Assert.False(ok);
        Assert.Contains("máximo", mensaje);
        Assert.Equal(DevActivityService.MaxEvidenciasPorActividad, db.DevActivityAttachments.Count());
    }

    /// <summary>Cerrada es evidencia consolidada: mismo criterio que Renombrar.</summary>
    [Fact]
    public void Agregar_SeNiegaSiLaActividadEstaCerrada()
    {
        var (db, _, act, yo, _) = Entorno();
        Svc(db, yo).Cerrar(act.Id);

        var (ok, mensaje, _) = Svc(db, yo).AgregarEvidencia(act.Id, "tarde.png", PngFalso);

        Assert.False(ok);
        Assert.Contains("Reábrela", mensaje);
        Assert.Empty(db.DevActivityAttachments);
    }

    [Fact]
    public void Agregar_NoSeLeCuelgaEvidenciaAUnaActividadAjena()
    {
        var (db, _, act, _, _) = Entorno();
        var intruso = Ctx.As(UserRole.Desarrollador, developerId: 999, userId: 77);

        Assert.Throws<AuthorizationException>(() => Svc(db, intruso).AgregarEvidencia(act.Id, "x.png", PngFalso));
    }

    // ── Listar y leer ────────────────────────────────────────────────────────

    /// <summary>
    /// El listado NO trae los bytes: es lo que evita mover megas por la red en cada refresco de la
    /// rejilla. El contenido solo viaja por BytesDeEvidencia.
    /// </summary>
    [Fact]
    public void EvidenciasDe_NoArrastraElContenido()
    {
        var (db, _, act, yo, _) = Entorno();
        Svc(db, yo).AgregarEvidencia(act.Id, "captura.png", PngFalso);

        var lista = Svc(db, yo).EvidenciasDe(act.Id);

        Assert.Single(lista);
        Assert.Empty(lista[0].Bytes);
        Assert.Equal(PngFalso.Length, lista[0].SizeBytes);   // el tamaño sí, para poder mostrarlo

        var (bytes, nombre, tipo) = Svc(db, yo).BytesDeEvidencia(lista[0].Id);
        Assert.Equal(PngFalso, bytes);
        Assert.Equal("captura.png", nombre);
        Assert.Equal("image/png", tipo);
    }

    [Fact]
    public void ElAdministradorPuedeVerLaEvidenciaDeCualquiera()
    {
        var (db, _, act, yo, jefa) = Entorno();
        Svc(db, yo).AgregarEvidencia(act.Id, "captura.png", PngFalso);

        var lista = Svc(db, jefa).EvidenciasDe(act.Id);
        Assert.Single(lista);
        Assert.Equal(PngFalso, Svc(db, jefa).BytesDeEvidencia(lista[0].Id).bytes);
    }

    [Fact]
    public void OtroDesarrolladorNoVeLaEvidenciaAjena()
    {
        var (db, _, act, yo, _) = Entorno();
        Svc(db, yo).AgregarEvidencia(act.Id, "captura.png", PngFalso);
        var intruso = Ctx.As(UserRole.Desarrollador, developerId: 999, userId: 77);

        Assert.Throws<AuthorizationException>(() => Svc(db, intruso).EvidenciasDe(act.Id));
    }

    [Fact]
    public void ConteoEvidencias_AgrupaSinTraerArchivos()
    {
        var (db, dev, act, yo, _) = Entorno();
        var svc = Svc(db, yo);
        svc.AgregarEvidencia(act.Id, "a.png", PngFalso);
        svc.AgregarEvidencia(act.Id, "b.png", PngFalso);

        var (_, _, otra) = svc.Crear(dev.Id, "Otra actividad", null);

        var conteo = svc.ConteoEvidencias([act.Id, otra!.Id]);

        Assert.Equal(2, conteo[act.Id]);
        Assert.False(conteo.ContainsKey(otra.Id));   // sin evidencia no aparece
        Assert.Empty(svc.ConteoEvidencias([]));
    }

    // ── Quitar ───────────────────────────────────────────────────────────────

    [Fact]
    public void Eliminar_QuitaLaEvidencia()
    {
        var (db, _, act, yo, _) = Entorno();
        var (_, _, ev) = Svc(db, yo).AgregarEvidencia(act.Id, "captura.png", PngFalso);

        Assert.True(Svc(db, yo).EliminarEvidencia(ev!.Id).ok);
        Assert.Empty(db.DevActivityAttachments);
    }

    [Fact]
    public void Eliminar_SeNiegaSiLaActividadEstaCerrada()
    {
        var (db, _, act, yo, _) = Entorno();
        var (_, _, ev) = Svc(db, yo).AgregarEvidencia(act.Id, "captura.png", PngFalso);
        Svc(db, yo).Cerrar(act.Id);

        var (ok, _) = Svc(db, yo).EliminarEvidencia(ev!.Id);

        Assert.False(ok);
        Assert.Single(db.DevActivityAttachments);
    }

    /// <summary>Borrar la actividad (solo si no tiene tiempo) se lleva su evidencia por delante.</summary>
    [Fact]
    public void EliminarLaActividad_ArrastraSuEvidencia()
    {
        var (db, _, act, yo, _) = Entorno();
        Svc(db, yo).AgregarEvidencia(act.Id, "captura.png", PngFalso);

        Assert.True(Svc(db, yo).Eliminar(act.Id).ok);
        Assert.Empty(db.DevActivityAttachments);
    }

    [Fact]
    public void NombreSeguro_LimpiaRutasYCaracteresProhibidos()
    {
        Assert.Equal("captura.png", DevActivityService.NombreSeguro(@"C:\Users\ana\captura.png"));
        Assert.Equal("evidencia", DevActivityService.NombreSeguro("   "));
        Assert.DoesNotContain("..", DevActivityService.NombreSeguro(@"..\..\etc\passwd"));
    }
}

/// <summary>
/// Buscador del cuadro «requerimientos del sprint». Con el backlog crecido la lista deja de ser
/// navegable a ojo, y lo que hace útil al filtro es que se pueda escribir cualquier cosa que uno
/// recuerde del requerimiento: su número, un trozo del título o su estado.
/// </summary>
public class SprintPickerFiltroTests
{
    private static Requirement Req(int id, string titulo, RequirementStatus estado = RequirementStatus.EnDesarrollo) =>
        new() { Id = id, Title = titulo, Status = estado };

    [Theory]
    [InlineData("pagos")]
    [InlineData("PAGOS")]        // sin distinguir mayúsculas
    [InlineData("42")]           // por número
    [InlineData("#42")]          // con almohadilla, como se ve en pantalla
    [InlineData("desarrollo")]   // por estado
    public void Coincide_BuscaPorNumeroTituloYEstado(string texto)
    {
        var r = Req(42, "Pasarela de pagos");
        Assert.True(SprintRequirementsPickerForm.Coincide(r, texto));
    }

    /// <summary>
    /// Se exigen TODAS las palabras: «pagos pruebas» busca lo de pagos que además está en pruebas.
    /// Con un OR, escribir más palabras devolvería más resultados, que es lo contrario de filtrar.
    /// </summary>
    [Fact]
    public void Coincide_ExigeTodasLasPalabras()
    {
        var enPruebas = Req(42, "Pasarela de pagos", RequirementStatus.EnPruebas);
        var enDesarrollo = Req(43, "Pasarela de pagos", RequirementStatus.EnDesarrollo);

        Assert.True(SprintRequirementsPickerForm.Coincide(enPruebas, "pagos pruebas"));
        Assert.False(SprintRequirementsPickerForm.Coincide(enDesarrollo, "pagos pruebas"));
    }

    [Fact]
    public void Coincide_TextoVacioNoExcluyeNada()
    {
        Assert.True(SprintRequirementsPickerForm.Coincide(Req(1, "Lo que sea"), ""));
        Assert.True(SprintRequirementsPickerForm.Coincide(Req(1, "Lo que sea"), "   "));
    }

    [Fact]
    public void Coincide_DescartaLoQueNoTieneNadaQueVer()
    {
        Assert.False(SprintRequirementsPickerForm.Coincide(Req(42, "Pasarela de pagos"), "facturación"));
    }
}
