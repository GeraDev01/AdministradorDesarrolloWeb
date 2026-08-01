using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Biblioteca de plantillas. La administra el administrador; el desarrollador solo LEE los tipos
/// de <c>TemplateService.TiposDelEquipo</c> (hoy, comentarios de DevOps). Se prueban los permisos
/// —los de lectura por rol y tipo, y los de escritura, que no cambiaron—, la validación, el
/// archivado frente al borrado, el contador de uso y —lo más delicado— la sustitución de
/// marcadores <c>{{así}}</c>.
/// </summary>
public class TemplateServiceTests
{
    private static TemplateService Svc(AppDbContext db, CurrentUserContext cu) =>
        new(db, cu, new AuditService(db, cu));

    private static TemplateService Admin(AppDbContext db, int userId = 99) =>
        Svc(db, Ctx.As(UserRole.Admin, userId: userId));

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
    public void Escritura_SigueSiendoSoloDelAdministrador()
    {
        // La lectura se abrió al desarrollador; escribir NO. La biblioteca es material curado:
        // si cualquiera edita, deja de ser una referencia y pasa a ser un cajón.
        var db = TestDb.New();
        var dev = Svc(db, Ctx.As(UserRole.Desarrollador, userId: 3));

        Assert.Throws<AuthorizationException>(() => dev.Crear(Datos()));
        Assert.Throws<AuthorizationException>(() => dev.Actualizar(1, Datos()));
        Assert.Throws<AuthorizationException>(() => dev.Eliminar(1));
        Assert.Throws<AuthorizationException>(() => dev.Duplicar(1));
        Assert.Throws<AuthorizationException>(() => dev.Archivar(1, true));
    }

    [Fact]
    public void Operaciones_SigueSinVerLasPlantillas()
    {
        // Demuestra que la guarda se escribió por rol POSITIVO y no por descarte («no es admin»):
        // abrir la lectura al desarrollador no puede regalársela a Operaciones de paso.
        var db = TestDb.New();
        var ops = Svc(db, Ctx.As(UserRole.Operaciones, userId: 4));
        Assert.Throws<AuthorizationException>(() => ops.Listar());
        Assert.Throws<AuthorizationException>(() => ops.Obtener(1));
        Assert.Throws<AuthorizationException>(() => ops.ConteoPorTipo());
    }

    // ── Lectura del desarrollador ────────────────────────────────────────────────

    /// <summary>Deja sembrado un catálogo mixto y devuelve el servicio con sesión de desarrollador.</summary>
    private static (TemplateService dev, int idDevOps, int idScript) CatalogoMixto(AppDbContext db)
    {
        var admin = Admin(db);
        var (_, _, tDevOps)  = admin.Crear(Datos(TemplateKind.ComentarioDevOps, "Avance del work item", "Avance: {{detalle}}"));
        var (_, _, tScript)  = admin.Crear(Datos(TemplateKind.ScriptSql, "Bloqueos", "SELECT 1;"));
        admin.Crear(Datos(TemplateKind.TicketFreshdesk, "Reporte de incidencia", "Cuerpo"));
        admin.Crear(Datos(TemplateKind.DocumentoEstimacion, "Entrega de estimación", "Cuerpo"));

        return (Svc(db, Ctx.As(UserRole.Desarrollador, userId: 3)), tDevOps!.Id, tScript!.Id);
    }

    [Fact]
    public void Desarrollador_VeSoloLasDeDevOps()
    {
        var db = TestDb.New();
        var (dev, idDevOps, _) = CatalogoMixto(db);

        var visibles = dev.Listar();

        var unica = Assert.Single(visibles);
        Assert.Equal(idDevOps, unica.Id);
        Assert.Equal(TemplateKind.ComentarioDevOps, unica.Kind);
    }

    [Fact]
    public void Desarrollador_PidiendoUnTipoProhibido_RecibeListaVacia()
    {
        // El filtro no puede depender de que la UI no ofrezca ese tipo en el combo.
        var db = TestDb.New();
        var (dev, _, _) = CatalogoMixto(db);

        Assert.Empty(dev.Listar(TemplateKind.ScriptSql));
        Assert.Empty(dev.Listar(TemplateKind.TicketFreshdesk));
    }

    [Fact]
    public void Desarrollador_Obtener_NoLeePorId_LoQueLaListaNoDa()
    {
        // Obtener es la puerta trasera clásica: hoy casi nadie lo llama, y por eso mismo sería el
        // que se quedara sin filtrar.
        var db = TestDb.New();
        var (dev, idDevOps, idScript) = CatalogoMixto(db);

        Assert.Null(dev.Obtener(idScript));
        Assert.NotNull(dev.Obtener(idDevOps));
        Assert.NotNull(Admin(db).Obtener(idScript));   // el admin sigue leyendo todo
    }

    [Fact]
    public void Desarrollador_NoVeLasArchivadas_AunqueLasPida()
    {
        var db = TestDb.New();
        var (dev, idDevOps, _) = CatalogoMixto(db);
        Admin(db).Archivar(idDevOps, true);

        Assert.Empty(dev.Listar(incluirArchivadas: true));               // se le ignora, sin error
        Assert.Contains(Admin(db).Listar(incluirArchivadas: true), t => t.Id == idDevOps);
    }

    [Fact]
    public void Desarrollador_ConteoPorTipo_SoloCuentaLoQueVe()
    {
        var db = TestDb.New();
        var (dev, _, _) = CatalogoMixto(db);

        var conteo = dev.ConteoPorTipo();

        Assert.Equal([TemplateKind.ComentarioDevOps], conteo.Keys);
    }

    [Fact]
    public void Desarrollador_RegistrarUso_SumaEnLaVisible_YNoTocaLaOculta()
    {
        // Con la lectura abierta, RegistrarUso es la única escritura del desarrollador: solo debe
        // contar usos de lo que su sesión puede leer — y sin lanzar, porque traga excepciones.
        var db = TestDb.New();
        var (dev, idDevOps, idScript) = CatalogoMixto(db);

        dev.RegistrarUso(idDevOps);
        dev.RegistrarUso(idScript);

        Assert.Equal(1, db.Templates.AsNoTracking().Single(t => t.Id == idDevOps).UsageCount);
        Assert.Equal(0, db.Templates.AsNoTracking().Single(t => t.Id == idScript).UsageCount);
    }

    [Fact]
    public void Anonimo_NoLeeNada()
    {
        var db = TestDb.New();
        var anon = Svc(db, new CurrentUserContext());
        Assert.Throws<AuthorizationException>(() => anon.Listar());
    }

    // ── Alta y validación ────────────────────────────────────────────────────────

    [Fact]
    public void Crear_GuardaLosDatosYRegistraEnLaBitacora()
    {
        var db = TestDb.New();
        var (ok, _, t) = Admin(db).Crear(Datos(
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
    public void Crear_RechazaLoIncompleto(string titulo, string cuerpo)
    {
        var db = TestDb.New();
        var (ok, mensaje, _) = Admin(db).Crear(Datos(titulo: titulo, cuerpo: cuerpo));
        Assert.False(ok);
        Assert.NotEmpty(mensaje);
        Assert.Empty(db.Templates);
    }

    [Fact]
    public void Crear_SinCuerpoPeroConArchivo_SeAcepta()
    {
        var db = TestDb.New();
        var (ok, _, _) = Admin(db).Crear(Datos(
            TemplateKind.DocumentoEstimacion, "Formato de estimación", cuerpo: "",
            archivo: [1, 2, 3], nombreArchivo: "estimacion.docx"));

        Assert.True(ok);
        var g = db.Templates.AsNoTracking().Single();
        Assert.Equal("", g.Body);
        Assert.Equal("estimacion.docx", g.FileName);
        Assert.Equal(3, g.FileBytes!.Length);
    }

    [Fact]
    public void Crear_NormalizaLasEtiquetas()
    {
        var db = TestDb.New();
        Admin(db).Crear(Datos(etiquetas: "  cierre , Cliente ,, cierre ; urgente "));
        Assert.Equal("cierre, Cliente, urgente", db.Templates.AsNoTracking().Single().Tags);
    }

    [Fact]
    public void Crear_ArchivoDemasiadoGrande_SeRechaza()
    {
        var db = TestDb.New();
        var enorme = new byte[TemplateService.MaxArchivoBytes + 1];
        var (ok, mensaje, _) = Admin(db).Crear(Datos(archivo: enorme, nombreArchivo: "gordo.bin"));
        Assert.False(ok);
        Assert.Contains("MB", mensaje);
        Assert.Empty(db.Templates);
    }

    // ── Edición, duplicado, archivado y baja ─────────────────────────────────────

    [Fact]
    public void Actualizar_CambiaLosCamposYSellaLaFecha()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        var (_, _, t) = svc.Crear(Datos());

        var (ok, _) = svc.Actualizar(t!.Id, Datos(
            TemplateKind.ScriptPowerShell, "Espacio en disco", "Get-CimInstance Win32_LogicalDisk"));

        Assert.True(ok);
        var g = db.Templates.AsNoTracking().Single();
        Assert.Equal(TemplateKind.ScriptPowerShell, g.Kind);
        Assert.Equal("Espacio en disco", g.Title);
        Assert.NotNull(g.UpdatedAt);
    }

    [Fact]
    public void Actualizar_Inexistente_NoRevienta()
    {
        var db = TestDb.New();
        var (ok, mensaje) = Admin(db).Actualizar(4321, Datos());
        Assert.False(ok);
        Assert.Contains("ya no existe", mensaje);
    }

    [Fact]
    public void Duplicar_CopiaTodoConOtroTitulo()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        var (_, _, t) = svc.Crear(Datos(
            TemplateKind.ComentarioDevOps, "Cierre con evidencias", "Causa: {{causa}}", etiquetas: "cierre"));

        var (ok, _, copia) = svc.Duplicar(t!.Id);

        Assert.True(ok);
        Assert.Equal(2, db.Templates.Count());
        Assert.Equal("Cierre con evidencias (copia)", copia!.Title);
        Assert.Equal(t.Body, copia.Body);
        Assert.Equal(t.Kind, copia.Kind);
        Assert.Equal("cierre", copia.Tags);
    }

    [Fact]
    public void Duplicar_TituloAlLimite_NoDesbordaLaColumna()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        var (_, _, t) = svc.Crear(Datos(titulo: new string('x', TemplateService.MaxTitulo)));

        var (ok, _, copia) = svc.Duplicar(t!.Id);

        Assert.True(ok);
        Assert.True(copia!.Title.Length <= TemplateService.MaxTitulo);
        Assert.EndsWith(" (copia)", copia.Title);
    }

    [Fact]
    public void Archivar_LaSacaDeLaListaSinBorrarla()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        var (_, _, t) = svc.Crear(Datos());

        svc.Archivar(t!.Id, true);

        Assert.Empty(svc.Listar());                                   // fuera del día a día
        Assert.Single(svc.Listar(incluirArchivadas: true));           // pero sigue ahí
        Assert.Single(db.Templates);

        svc.Archivar(t.Id, false);
        Assert.Single(svc.Listar());
    }

    [Fact]
    public void Eliminar_SiBorra()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        var (_, _, t) = svc.Crear(Datos());

        var (ok, _) = svc.Eliminar(t!.Id);

        Assert.True(ok);
        Assert.Empty(db.Templates);
        Assert.Contains(db.AuditLogs, a => a.EntityType == "Template" && a.Action == AuditAction.Delete);
    }

    // ── Listado ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Listar_FiltraPorTipoYPorTexto()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        svc.Crear(Datos(TemplateKind.ScriptSql, "Bloqueos", "SELECT 1", etiquetas: "diagnostico"));
        svc.Crear(Datos(TemplateKind.ScriptBash, "Espacio en disco", "df -h"));
        svc.Crear(Datos(TemplateKind.TicketFreshdesk, "Alta de incidencia", "Asunto: {{x}}"));

        Assert.Equal(3, svc.Listar().Count);
        Assert.Single(svc.Listar(TemplateKind.ScriptBash));
        Assert.Single(svc.Listar(texto: "Bloqueos"));
        Assert.Single(svc.Listar(texto: "diagnostico"));   // también busca en etiquetas
        Assert.Single(svc.Listar(texto: "df -h"));         // y en el contenido
        Assert.Empty(svc.Listar(texto: "no existe nada así"));
    }

    [Fact]
    public void Listar_PoneArribaLoMasUsado()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        svc.Crear(Datos(titulo: "Poco usada", cuerpo: "SELECT 1"));
        var (_, _, popular) = svc.Crear(Datos(titulo: "Muy usada", cuerpo: "SELECT 2"));

        svc.RegistrarUso(popular!.Id);
        svc.RegistrarUso(popular.Id);

        Assert.Equal("Muy usada", svc.Listar()[0].Title);
    }

    [Fact]
    public void RegistrarUso_SumaYSellaLaFecha()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        var (_, _, t) = svc.Crear(Datos());

        svc.RegistrarUso(t!.Id);
        svc.RegistrarUso(t.Id);

        var g = db.Templates.AsNoTracking().Single();
        Assert.Equal(2, g.UsageCount);
        Assert.NotNull(g.LastUsedAt);
    }

    [Fact]
    public void ConteoPorTipo_NoCuentaLasArchivadas()
    {
        var db = TestDb.New();
        var svc = Admin(db);
        svc.Crear(Datos(TemplateKind.ScriptSql, "Uno", "SELECT 1"));
        var (_, _, dos) = svc.Crear(Datos(TemplateKind.ScriptSql, "Dos", "SELECT 2"));
        svc.Archivar(dos!.Id, true);

        Assert.Equal(1, svc.ConteoPorTipo()[TemplateKind.ScriptSql]);
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
        var cu = Ctx.As(UserRole.Admin, userId: 99);            // Username = "admin"
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
    public void Seed_SiembraUnaVezYNoResucitaLoBorrado()
    {
        var db = TestDb.New();
        int sembradas = TemplateSeed.Sembrar(db);
        Assert.True(sembradas > 0);

        // El administrador borra todo el catálogo de ejemplo…
        db.Templates.RemoveRange(db.Templates);
        db.SaveChanges();

        // …y el siguiente arranque NO se lo devuelve.
        Assert.Equal(0, TemplateSeed.Sembrar(db));
        Assert.Empty(db.Templates);
    }

    [Fact]
    public void Seed_NoTocaUnaBaseQueYaTienePlantillas()
    {
        var db = TestDb.New();
        Admin(db).Crear(Datos(titulo: "Mía de verdad"));

        Assert.Equal(0, TemplateSeed.Sembrar(db));
        Assert.Equal("Mía de verdad", db.Templates.AsNoTracking().Single().Title);
    }

    [Fact]
    public void Seed_TodoElCatalogoPasaLaValidacionYSeLee()
    {
        var db = TestDb.New();
        TemplateSeed.Sembrar(db);

        var todas = Admin(db).Listar();
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
