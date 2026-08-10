using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Biblioteca de plantillas. La administra el administrador; el desarrollador solo LEE los tipos
/// de <c>TemplateService.TiposDelEquipo</c> (hoy, comentarios de DevOps). Se prueban los permisos
/// —los de lectura por rol y tipo, y los de escritura, que no cambiaron—, la validación, el
/// archivado frente al borrado, el contador de uso y —lo más delicado— la sustitución de
/// marcadores <c>{{así}}</c>.
///
/// Al final van las tres del catálogo inicial (<see cref="TemplateSeed"/>), que comprueban lo único
/// delicado de un sembrador: que no resucite lo que alguien borró a propósito.
/// </summary>
public class TemplateServiceTests
{
    private static TemplateService Svc(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, new AuditService(db, cu, new OrigenDePrueba()));

    private static TemplateService Admin(AppDbContext db, int userId = 99) =>
        Svc(db, UsuarioDePrueba.Como(UserRole.Admin, userId: userId));

    private static TemplateInput Datos(
        TemplateKind kind = TemplateKind.ScriptSql,
        string? titulo = "Bloqueos en SQL Server",
        string? cuerpo = "SELECT * FROM sys.dm_exec_requests;",
        string? descripcion = null,
        string? etiquetas = null,
        byte[]? archivo = null,
        string? nombreArchivo = null) =>
        new(kind, titulo, descripcion, cuerpo, etiquetas, archivo, nombreArchivo);

    // ── Permisos ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Escritura_SigueSiendoSoloDelAdministrador()
    {
        // La lectura se abrió al desarrollador; escribir NO. La biblioteca es material curado:
        // si cualquiera edita, deja de ser una referencia y pasa a ser un cajón.
        var db = TestDb.New();
        var dev = Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 3));

        await Assert.ThrowsAsync<AuthorizationException>(() => dev.CrearAsync(Datos()));
        await Assert.ThrowsAsync<AuthorizationException>(() => dev.ActualizarAsync(1, Datos()));
        await Assert.ThrowsAsync<AuthorizationException>(() => dev.EliminarAsync(1));
        await Assert.ThrowsAsync<AuthorizationException>(() => dev.DuplicarAsync(1));
        await Assert.ThrowsAsync<AuthorizationException>(() => dev.ArchivarAsync(1, true));
    }

    [Fact]
    public async Task Operaciones_SigueSinVerLasPlantillas()
    {
        // Demuestra que la guarda se escribió por rol POSITIVO y no por descarte («no es admin»):
        // abrir la lectura al desarrollador no puede regalársela a Operaciones de paso.
        var db = TestDb.New();
        var ops = Svc(db, UsuarioDePrueba.Como(UserRole.Operaciones, userId: 4));
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.ListarAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.ObtenerAsync(1));
        await Assert.ThrowsAsync<AuthorizationException>(() => ops.ConteoPorTipoAsync());
    }

    // ── Lectura del desarrollador ────────────────────────────────────────────────

    /// <summary>Deja sembrado un catálogo mixto y devuelve el servicio con sesión de desarrollador.</summary>
    private static async Task<(TemplateService dev, int idDevOps, int idScript)> CatalogoMixto(AppDbContext db)
    {
        var admin = Admin(db);
        var (_, _, tDevOps) = await admin.CrearAsync(Datos(TemplateKind.ComentarioDevOps, "Avance del work item", "Avance: {{detalle}}"));
        var (_, _, tScript) = await admin.CrearAsync(Datos(TemplateKind.ScriptSql, "Bloqueos", "SELECT 1;"));
        await admin.CrearAsync(Datos(TemplateKind.TicketFreshdesk, "Reporte de incidencia", "Cuerpo"));
        await admin.CrearAsync(Datos(TemplateKind.DocumentoEstimacion, "Entrega de estimación", "Cuerpo"));

        return (Svc(db, UsuarioDePrueba.Como(UserRole.Desarrollador, userId: 3)), tDevOps!.Id, tScript!.Id);
    }

    [Fact]
    public async Task Desarrollador_VeSoloLasDeDevOps()
    {
        var db = TestDb.New();
        var (dev, idDevOps, _) = await CatalogoMixto(db);

        var visibles = await dev.ListarAsync();

        var unica = Assert.Single(visibles);
        Assert.Equal(idDevOps, unica.Id);
        Assert.Equal(TemplateKind.ComentarioDevOps, unica.Kind);
    }

    [Fact]
    public async Task Desarrollador_PidiendoUnTipoProhibido_RecibeListaVacia()
    {
        // El filtro no puede depender de que la UI no ofrezca ese tipo en el combo.
        var db = TestDb.New();
        var (dev, _, _) = await CatalogoMixto(db);

        Assert.Empty(await dev.ListarAsync(TemplateKind.ScriptSql));
        Assert.Empty(await dev.ListarAsync(TemplateKind.TicketFreshdesk));
    }

    [Fact]
    public async Task Desarrollador_Obtener_NoLeePorId_LoQueLaListaNoDa()
    {
        // Obtener es la puerta trasera clásica: hoy casi nadie lo llama, y por eso mismo sería el
        // que se quedara sin filtrar.
        var db = TestDb.New();
        var (dev, idDevOps, idScript) = await CatalogoMixto(db);

        Assert.Null(await dev.ObtenerAsync(idScript));
        Assert.NotNull(await dev.ObtenerAsync(idDevOps));
        Assert.NotNull(await Admin(db).ObtenerAsync(idScript));   // el admin sigue leyendo todo
    }

    [Fact]
    public async Task Desarrollador_NoVeLasArchivadas_AunqueLasPida()
    {
        var db = TestDb.New();
        var (dev, idDevOps, _) = await CatalogoMixto(db);
        await Admin(db).ArchivarAsync(idDevOps, true);

        Assert.Empty(await dev.ListarAsync(incluirArchivadas: true));               // se le ignora, sin error
        Assert.Contains(await Admin(db).ListarAsync(incluirArchivadas: true), t => t.Id == idDevOps);
    }

    [Fact]
    public async Task Desarrollador_ConteoPorTipo_SoloCuentaLoQueVe()
    {
        var db = TestDb.New();
        var (dev, _, _) = await CatalogoMixto(db);

        var conteo = await dev.ConteoPorTipoAsync();

        Assert.Equal([TemplateKind.ComentarioDevOps], conteo.Keys);
    }

    [Fact]
    public async Task Desarrollador_RegistrarUso_SumaEnLaVisible_YNoTocaLaOculta()
    {
        // Con la lectura abierta, RegistrarUso es la única escritura del desarrollador: solo debe
        // contar usos de lo que su sesión puede leer — y sin lanzar, porque traga excepciones.
        var db = TestDb.New();
        var (dev, idDevOps, idScript) = await CatalogoMixto(db);

        await dev.RegistrarUsoAsync(idDevOps);
        await dev.RegistrarUsoAsync(idScript);

        Assert.Equal(1, db.Templates.AsNoTracking().Single(t => t.Id == idDevOps).UsageCount);
        Assert.Equal(0, db.Templates.AsNoTracking().Single(t => t.Id == idScript).UsageCount);
    }

    [Fact]
    public async Task Anonimo_NoLeeNada()
    {
        var db = TestDb.New();
        var anon = Svc(db, UsuarioDePrueba.Anonimo());
        await Assert.ThrowsAsync<AuthorizationException>(() => anon.ListarAsync());
    }

    // ── Alta y validación ────────────────────────────────────────────────────────

    [Fact]
    public async Task Crear_GuardaLosDatosYRegistraEnLaBitacora()
    {
        var db = TestDb.New();
        var (ok, _, t) = await Admin(db).CrearAsync(Datos(
            TemplateKind.RespuestaFreshdesk, "Acuse de recibo", "Hola {{cliente}}:", "Primera respuesta.", "acuse, cliente"));

        Assert.True(ok);
        Assert.NotNull(t);
        var g = db.Templates.AsNoTracking().Single();
        Assert.Equal("Acuse de recibo", g.Title);
        Assert.Equal(TemplateKind.RespuestaFreshdesk, g.Kind);
        Assert.Equal("Primera respuesta.", g.Description);
        Assert.Equal("acuse, cliente", g.Tags);
        Assert.Equal(99, g.CreatedByUserId);
        Assert.False(g.IsArchived);
        Assert.Equal(0, g.UsageCount);
        Assert.Contains(db.AuditLogs, a => a.EntityType == "Template" && a.Action == AuditAction.Create);
    }

    [Theory]
    [InlineData("ab", "cuerpo suficiente")]      // título corto
    [InlineData("Título válido", "")]            // sin contenido ni archivo
    [InlineData("Título válido", "   ")]         // solo espacios
    public async Task Crear_RechazaLoIncompleto(string titulo, string cuerpo)
    {
        var db = TestDb.New();
        var (ok, mensaje, _) = await Admin(db).CrearAsync(Datos(titulo: titulo, cuerpo: cuerpo));
        Assert.False(ok);
        Assert.NotEmpty(mensaje);
        Assert.Empty(db.Templates);
    }

    [Fact]
    public async Task Crear_SinCuerpoPeroConArchivo_SeAcepta()
    {
        var db = TestDb.New();
        var (ok, _, _) = await Admin(db).CrearAsync(Datos(
            TemplateKind.DocumentoEstimacion, "Formato de estimación", cuerpo: "",
            archivo: [1, 2, 3], nombreArchivo: "estimacion.docx"));

        Assert.True(ok);
        var g = db.Templates.AsNoTracking().Single();
        Assert.Equal("", g.Body);
        Assert.Equal("estimacion.docx", g.FileName);
        Assert.Equal(3, g.FileBytes!.Length);
    }

    [Fact]
    public async Task Crear_NormalizaLasEtiquetas()
    {
        var db = TestDb.New();
        await Admin(db).CrearAsync(Datos(etiquetas: "  cierre , Cliente ,, cierre ; urgente "));
        Assert.Equal("cierre, Cliente, urgente", db.Templates.AsNoTracking().Single().Tags);
    }

    [Fact]
    public async Task Crear_ArchivoDemasiadoGrande_SeRechaza()
    {
        var db = TestDb.New();
        var enorme = new byte[TemplateService.MaxArchivoBytes + 1];
        var (ok, mensaje, _) = await Admin(db).CrearAsync(Datos(archivo: enorme, nombreArchivo: "gordo.bin"));
        Assert.False(ok);
        Assert.Contains("MB", mensaje);
        Assert.Empty(db.Templates);
    }

    // ── Edición, duplicado, archivado y baja ─────────────────────────────────────

    [Fact]
    public async Task Actualizar_CambiaLosCamposYSellaLaFecha()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        var (_, _, t) = await svc.CrearAsync(Datos());

        var (ok, _) = await svc.ActualizarAsync(t!.Id, Datos(
            TemplateKind.ScriptPowerShell, "Espacio en disco", "Get-CimInstance Win32_LogicalDisk"));

        Assert.True(ok);
        var g = db.Templates.AsNoTracking().Single();
        Assert.Equal(TemplateKind.ScriptPowerShell, g.Kind);
        Assert.Equal("Espacio en disco", g.Title);
        Assert.NotNull(g.UpdatedAt);
    }

    [Fact]
    public async Task Actualizar_Inexistente_NoRevienta()
    {
        var db = TestDb.New();
        var (ok, mensaje) = await Admin(db).ActualizarAsync(4321, Datos());
        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje);
    }

    [Fact]
    public async Task Duplicar_CopiaTodoConOtroTitulo()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        var (_, _, t) = await svc.CrearAsync(Datos(
            TemplateKind.ComentarioDevOps, "Cierre con evidencias", "Causa: {{causa}}", etiquetas: "cierre"));

        var (ok, _, copia) = await svc.DuplicarAsync(t!.Id);

        Assert.True(ok);
        Assert.Equal(2, db.Templates.Count());
        Assert.Equal("Cierre con evidencias (copia)", copia!.Title);
        Assert.Equal(t.Body, copia.Body);
        Assert.Equal(t.Kind, copia.Kind);
        Assert.Equal("cierre", copia.Tags);
    }

    [Fact]
    public async Task Duplicar_TituloAlLimite_NoDesbordaLaColumna()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        var (_, _, t) = await svc.CrearAsync(Datos(titulo: new string('x', TemplateService.MaxTitulo)));

        var (ok, _, copia) = await svc.DuplicarAsync(t!.Id);

        Assert.True(ok);
        Assert.True(copia!.Title.Length <= TemplateService.MaxTitulo);
        Assert.EndsWith(" (copia)", copia.Title);
    }

    [Fact]
    public async Task Archivar_LaSacaDeLaListaSinBorrarla()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        var (_, _, t) = await svc.CrearAsync(Datos());

        await svc.ArchivarAsync(t!.Id, true);

        Assert.Empty(await svc.ListarAsync());                                   // fuera del día a día
        Assert.Single(await svc.ListarAsync(incluirArchivadas: true));           // pero sigue ahí
        Assert.Single(db.Templates);

        await svc.ArchivarAsync(t.Id, false);
        Assert.Single(await svc.ListarAsync());
    }

    [Fact]
    public async Task Eliminar_SiBorra()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        var (_, _, t) = await svc.CrearAsync(Datos());

        var (ok, _) = await svc.EliminarAsync(t!.Id);

        Assert.True(ok);
        Assert.Empty(db.Templates);
        Assert.Contains(db.AuditLogs, a => a.EntityType == "Template" && a.Action == AuditAction.Delete);
    }

    // ── Listado ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Listar_FiltraPorTipoYPorTexto()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        await svc.CrearAsync(Datos(TemplateKind.ScriptSql, "Bloqueos", "SELECT 1", etiquetas: "diagnostico"));
        await svc.CrearAsync(Datos(TemplateKind.ScriptBash, "Espacio en disco", "df -h"));
        await svc.CrearAsync(Datos(TemplateKind.TicketFreshdesk, "Alta de incidencia", "Asunto: {{x}}"));

        Assert.Equal(3, (await svc.ListarAsync()).Count);
        Assert.Single(await svc.ListarAsync(TemplateKind.ScriptBash));
        Assert.Single(await svc.ListarAsync(texto: "Bloqueos"));
        Assert.Single(await svc.ListarAsync(texto: "diagnostico"));   // también busca en etiquetas
        Assert.Single(await svc.ListarAsync(texto: "df -h"));         // y en el contenido
        Assert.Empty(await svc.ListarAsync(texto: "no existe nada así"));
    }

    [Fact]
    public async Task Listar_PoneArribaLoMasUsado()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        await svc.CrearAsync(Datos(titulo: "Poco usada", cuerpo: "SELECT 1"));
        var (_, _, popular) = await svc.CrearAsync(Datos(titulo: "Muy usada", cuerpo: "SELECT 2"));

        await svc.RegistrarUsoAsync(popular!.Id);
        await svc.RegistrarUsoAsync(popular.Id);

        Assert.Equal("Muy usada", (await svc.ListarAsync())[0].Title);
    }

    [Fact]
    public async Task RegistrarUso_SumaYSellaLaFecha()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        var (_, _, t) = await svc.CrearAsync(Datos());

        await svc.RegistrarUsoAsync(t!.Id);
        await svc.RegistrarUsoAsync(t.Id);

        var g = db.Templates.AsNoTracking().Single();
        Assert.Equal(2, g.UsageCount);
        Assert.NotNull(g.LastUsedAt);
    }

    [Fact]
    public async Task ConteoPorTipo_NoCuentaLasArchivadas()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        await svc.CrearAsync(Datos(TemplateKind.ScriptSql, "Uno", "SELECT 1"));
        var (_, _, dos) = await svc.CrearAsync(Datos(TemplateKind.ScriptSql, "Dos", "SELECT 2"));
        await svc.ArchivarAsync(dos!.Id, true);

        Assert.Equal(1, (await svc.ConteoPorTipoAsync())[TemplateKind.ScriptSql]);
    }

    // ── Marcadores {{...}} ───────────────────────────────────────────────────────

    [Fact]
    public void Marcadores_LosDetectaSinRepetirYEnOrden()
    {
        var m = TemplateService.Marcadores("Hola {{cliente}}, el folio {{folio}} de {{cliente}} ya está.");
        Assert.Equal(new[] { "cliente", "folio" }, m);
    }

    [Fact]
    public void Marcadores_NoPreguntaLosQueSeRellenanSolos()
    {
        var m = TemplateService.Marcadores("{{fecha}} {{hora}} {{fechahora}} {{anio}} {{usuario}} {{yo}} {{cliente}}");
        Assert.Equal(new[] { "cliente" }, m);
    }

    [Fact]
    public void Marcadores_IgnoraLlavesQueNoSonMarcadores()
    {
        // Un bloque de PowerShell o un JSON de ejemplo no deben verse como huecos por rellenar.
        var cuerpo = """
                     if ($x) { Write-Output "hola" }
                     { "clave": "valor" }
                     Get-Item @{ Name = 'x' }
                     """;
        Assert.Empty(TemplateService.Marcadores(cuerpo));
    }

    [Fact]
    public void Marcadores_NoCruzanSaltosDeLinea()
    {
        Assert.Empty(TemplateService.Marcadores("{{ abierto\nsigue }}"));
    }

    [Fact]
    public void Rellenar_SustituyeLosIntegradosConLaSesionYElReloj()
    {
        var ahora = new DateTime(2026, 7, 30, 14, 5, 0);
        var texto = TemplateService.Rellenar(
            "{{fecha}} · {{hora}} · {{fechahora}} · {{anio}} · {{usuario}}", null, "gtellez", ahora);

        Assert.Equal("30/07/2026 · 14:05 · 30/07/2026 14:05 · 2026 · gtellez", texto);
    }

    [Fact]
    public void Rellenar_UsaLosValoresCapturadosSinDistinguirMayusculas()
    {
        var valores = new Dictionary<string, string> { ["Cliente"] = "ACME", ["folio"] = "12345" };
        var texto = TemplateService.Rellenar(
            "Hola {{cliente}}, tu folio {{FOLIO}} quedó atendido.", valores, "yo", DateTime.Now);

        Assert.Equal("Hola ACME, tu folio 12345 quedó atendido.", texto);
    }

    [Fact]
    public void Rellenar_ElMarcadorSinValorSeQuedaVisible()
    {
        // A propósito: es preferible ver un {{cliente}} sin llenar que mandar un hueco silencioso.
        var texto = TemplateService.Rellenar("Hola {{cliente}}:", null, "yo", DateTime.Now);
        Assert.Equal("Hola {{cliente}}:", texto);
    }

    [Fact]
    public void Rellenar_ToleraEspaciosDentroDeLasLlaves()
    {
        var valores = new Dictionary<string, string> { ["cliente"] = "ACME" };
        Assert.Equal("ACME", TemplateService.Rellenar("{{  cliente  }}", valores, "yo", DateTime.Now));
    }

    [Fact]
    public void Rellenar_DeLaInstancia_TomaElUsuarioDeLaSesion()
    {
        var db = TestDb.New();
        var cu = UsuarioDePrueba.Como(UserRole.Admin, userId: 99);            // Username = "admin"
        Assert.Equal("admin", Svc(db, cu).Rellenar("{{usuario}}"));
    }

    // ── Formato de archivo ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(TemplateKind.ScriptSql, ".sql")]
    [InlineData(TemplateKind.ScriptPowerShell, ".ps1")]
    [InlineData(TemplateKind.ScriptBash, ".sh")]
    [InlineData(TemplateKind.DocumentoEstimacion, ".md")]
    [InlineData(TemplateKind.RespuestaFreshdesk, ".txt")]
    public void ExtensionSugerida_PorTipo(TemplateKind kind, string esperada) =>
        Assert.Equal(esperada, TemplateService.ExtensionSugerida(kind));

    [Fact]
    public void CodificacionArchivo_ElPs1LlevaBOM_ElRestoNo()
    {
        // Windows PowerShell 5.1 lee un .ps1 sin BOM como ANSI y corrompe los acentos.
        Assert.NotEmpty(TemplateService.CodificacionArchivo(TemplateKind.ScriptPowerShell).GetPreamble());
        Assert.Empty(TemplateService.CodificacionArchivo(TemplateKind.ScriptBash).GetPreamble());
        Assert.Empty(TemplateService.CodificacionArchivo(TemplateKind.ScriptSql).GetPreamble());
    }

    [Fact]
    public void NombreArchivoSugerido_LimpiaElTituloYRespetaElAdjunto()
    {
        var conArchivo = new Template { Id = 1, Title = "Estimación", Kind = TemplateKind.DocumentoEstimacion, FileName = "formato.docx" };
        Assert.Equal("formato.docx", TemplateService.NombreArchivoSugerido(conArchivo));

        var soloTexto = new Template { Id = 2, Title = "Bloqueos: SQL/Server", Kind = TemplateKind.ScriptSql };
        var nombre = TemplateService.NombreArchivoSugerido(soloTexto);
        Assert.EndsWith(".sql", nombre);
        Assert.DoesNotContain("/", nombre);
        Assert.DoesNotContain(":", nombre);
    }

    // ── Catálogo inicial ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Seed_SiembraUnaVezYNoResucitaLoBorrado()
    {
        var db = TestDb.New();
        int sembradas = await TemplateSeed.SembrarAsync(db);
        Assert.True(sembradas > 0);

        // El administrador borra todo el catálogo de ejemplo…
        db.Templates.RemoveRange(db.Templates);
        await db.SaveChangesAsync();

        // …y el siguiente arranque NO se lo devuelve.
        Assert.Equal(0, await TemplateSeed.SembrarAsync(db));
        Assert.Empty(db.Templates);
    }

    [Fact]
    public async Task Seed_NoTocaUnaBaseQueYaTienePlantillas()
    {
        var db = TestDb.New();
        await Admin(db).CrearAsync(Datos(titulo: "Mía de verdad"));

        Assert.Equal(0, await TemplateSeed.SembrarAsync(db));
        Assert.Equal("Mía de verdad", db.Templates.AsNoTracking().Single().Title);
    }

    [Fact]
    public async Task Seed_TodoElCatalogoPasaLaValidacionYSeLee()
    {
        var db = TestDb.New();
        await TemplateSeed.SembrarAsync(db);

        var todas = await Admin(db).ListarAsync();
        Assert.NotEmpty(todas);
        Assert.All(todas, t =>
        {
            Assert.True(t.Title.Length is >= 3 and <= TemplateService.MaxTitulo);
            Assert.NotEmpty(t.Body);
            Assert.False(t.IsArchived);
        });
        // Hay material de las cinco familias que pidió el área.
        foreach (var k in new[]
                 {
                     TemplateKind.TicketFreshdesk, TemplateKind.RespuestaFreshdesk,
                     TemplateKind.ComentarioRequerimiento, TemplateKind.ComentarioDevOps,
                     TemplateKind.DocumentoEstimacion, TemplateKind.ScriptSql,
                     TemplateKind.ScriptPowerShell, TemplateKind.ScriptBash
                 })
            Assert.Contains(todas, t => t.Kind == k);
    }
}
