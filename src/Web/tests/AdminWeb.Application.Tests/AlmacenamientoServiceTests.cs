using AdminWeb.Application.Services;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Dtos.Programados;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// El explorador del contenedor de Azure Blob Storage.
///
/// <para><b>Nada de esto toca la red.</b> El cliente de Azure está detrás de
/// <see cref="IClienteDeBlobs"/> y aquí se sustituye por uno en memoria: lo que hay que comprobar
/// —que la cadena de conexión no se escape, que un prefijo mal escrito se rechace antes de crear una
/// carpeta absurda, que borrar una carpeta base avise— es decisión, no transferencia. Una prueba que
/// dependiera de una cuenta de Azure fallaría por motivos ajenos y acabaría desactivada.</para>
/// </summary>
public class AlmacenamientoServiceTests
{
    // ── Doble del cliente de Azure ──────────────────────────────────────────────

    /// <summary>Un contenedor de mentira: un diccionario de nombre de blob a sus metadatos.</summary>
    private sealed class ContenedorFalso : IClienteDeBlobs
    {
        public readonly Dictionary<string, Dictionary<string, string>> Blobs = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> CarpetasCreadas = [];
        public CredencialesDeBlob? UltimasCredenciales;
        public bool PuedeFirmar = true;

        public Task<(bool ok, string mensaje)> ProbarAsync(CredencialesDeBlob cred, CancellationToken ct = default)
        {
            UltimasCredenciales = cred;
            return Task.FromResult((true, $"Conexión correcta con el contenedor «{cred.Contenedor}»."));
        }

        public Task<IReadOnlyList<BlobDelContenedor>> ListarAsync(
            CredencialesDeBlob cred, string prefijo, CancellationToken ct = default)
        {
            UltimasCredenciales = cred;
            IReadOnlyList<BlobDelContenedor> filas =
            [
                .. Blobs
                    .Where(b => b.Key.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)
                             && !RutasDeBlob.EsMarcadorDeCarpeta(b.Key))
                    .Select(b => new BlobDelContenedor(b.Key, 1024, DateTimeOffset.UtcNow, b.Value))
            ];
            return Task.FromResult(filas);
        }

        public Task<IReadOnlyList<string>> ListarNombresAsync(
            CredencialesDeBlob cred, string? prefijo, CancellationToken ct = default)
        {
            IReadOnlyList<string> nombres =
            [
                .. Blobs.Keys.Where(k => prefijo == null || k.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
            ];
            return Task.FromResult(nombres);
        }

        public Task<bool> CrearCarpetaAsync(CredencialesDeBlob cred, string ruta, CancellationToken ct = default)
        {
            CarpetasCreadas.Add(ruta);
            return Task.FromResult(Blobs.TryAdd(ruta + "/", []));
        }

        public Task<bool> ExisteAsync(CredencialesDeBlob cred, string blob, CancellationToken ct = default) =>
            Task.FromResult(Blobs.ContainsKey(blob));

        public Task<bool> EliminarAsync(CredencialesDeBlob cred, string blob, CancellationToken ct = default) =>
            Task.FromResult(Blobs.Remove(blob));

        public Task<int> ContarEnCarpetaAsync(CredencialesDeBlob cred, string carpeta, CancellationToken ct = default) =>
            Task.FromResult(Blobs.Keys.Count(k => k.StartsWith(carpeta + "/", StringComparison.OrdinalIgnoreCase)));

        public Task<int> EliminarCarpetaAsync(CredencialesDeBlob cred, string carpeta, CancellationToken ct = default)
        {
            var afectados = Blobs.Keys
                .Where(k => k.StartsWith(carpeta + "/", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var k in afectados) Blobs.Remove(k);
            return Task.FromResult(afectados.Count);
        }

        public Task<IReadOnlyDictionary<string, string>> ObtenerMetadatosAsync(
            CredencialesDeBlob cred, string blob, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                Blobs.TryGetValue(blob, out var m) ? m : new Dictionary<string, string>());

        public Task ActualizarMetadatosAsync(CredencialesDeBlob cred, string blob,
            IDictionary<string, string> metadatos, CancellationToken ct = default)
        {
            Blobs[blob] = new Dictionary<string, string>(metadatos);
            return Task.CompletedTask;
        }

        public Task SubirAsync(CredencialesDeBlob cred, string blob, Stream contenido,
            IDictionary<string, string>? metadatos = null, CancellationToken ct = default)
        {
            Blobs[blob] = metadatos is null ? [] : new Dictionary<string, string>(metadatos);
            return Task.CompletedTask;
        }

        public Task<Stream> AbrirLecturaAsync(CredencialesDeBlob cred, string blob, CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));

        public Task<long> TamanoAsync(CredencialesDeBlob cred, string blob, CancellationToken ct = default) =>
            Task.FromResult(1024L);

        public Uri GenerarEnlaceDeDescarga(CredencialesDeBlob cred, string blob, double horasDeVigencia) =>
            PuedeFirmar
                ? new Uri($"https://cuenta.blob.core.windows.net/{cred.Contenedor}/{blob}?sig=xxx")
                : throw new InvalidOperationException(
                    "La cadena de conexión configurada no incluye la clave de la cuenta.");

        public string Explicar(Exception ex) => ex.Message;
    }

    // ── Andamiaje ───────────────────────────────────────────────────────────────

    private const string CadenaDePrueba =
        "DefaultEndpointsProtocol=https;AccountName=cuenta;AccountKey=c2VjcmV0bw==;EndpointSuffix=core.windows.net";

    private static async Task<(AlmacenamientoService almacen, ContenedorFalso contenedor, AppDbContext db)>
        ArmarAsync(ICurrentUser? quien = null, bool configurado = true)
    {
        var db = TestDb.New();
        var actual = quien ?? UsuarioDePrueba.Como(UserRole.Admin);
        var auditoria = new AuditService(db, actual, new OrigenDePrueba());
        var configuracion = new SettingsService(db, actual, auditoria);

        if (configurado)
        {
            // Se guarda por el servicio de configuración a propósito: es él quien decide que una
            // clave terminada en «ConnectionString» es secreta y la cifra antes de tocar la base.
            var admin = new SettingsService(db, UsuarioDePrueba.Como(UserRole.Admin), auditoria);
            await admin.GuardarAsync(SettingsService.Claves.AzureBlobConnectionString, CadenaDePrueba);
        }

        var contenedor = new ContenedorFalso();
        return (new AlmacenamientoService(configuracion, contenedor, actual, auditoria), contenedor, db);
    }

    // ── El secreto no sale ──────────────────────────────────────────────────────

    [Fact]
    public async Task El_estado_dice_si_esta_configurado_pero_nunca_la_cadena()
    {
        var (almacen, _, _) = await ArmarAsync();

        var estado = await almacen.EstadoAsync();

        Assert.True(estado.Configurado);

        // Ni la cadena entera ni la clave de la cuenta pueden aparecer en NADA de lo que viaja al
        // navegador: ni en el contenedor, ni en las carpetas, ni en el aviso.
        var todo = string.Join("|", estado.Contenedor, string.Join("|", estado.Carpetas),
            estado.CarpetaDeVersiones, estado.CarpetaDeRespaldosDeDespliegue, estado.Aviso ?? "");

        Assert.DoesNotContain("AccountKey", todo);
        Assert.DoesNotContain("c2VjcmV0bw==", todo);
    }

    [Fact]
    public void Las_credenciales_no_se_imprimen_al_interpolarlas()
    {
        // El ToString de serie de un record imprime todos sus miembros; sin sobrescribirlo, la cadena
        // acabaría en cualquier traza o mensaje de error donde alguien interpole las credenciales.
        var credenciales = new CredencialesDeBlob(CadenaDePrueba, "despliegues");

        Assert.DoesNotContain("AccountKey", $"{credenciales}");
        Assert.Contains("oculta", $"{credenciales}");
    }

    [Fact]
    public async Task Sin_configurar_lo_dice_en_vez_de_reventar()
    {
        var (almacen, _, _) = await ArmarAsync(configurado: false);

        var estado = await almacen.EstadoAsync();

        Assert.False(estado.Configurado);
        Assert.Contains("no está configurado", estado.Aviso);
        Assert.Empty(estado.Carpetas);
    }

    // ── Autorización ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Operaciones_no_administra_el_contenedor()
    {
        // La pestaña de Blob era Solo Admin en el escritorio y lo sigue siendo: desde aquí se borran
        // versiones y respaldos, que no es desplegar sino administrar.
        var (almacen, _, _) = await ArmarAsync(UsuarioDePrueba.Como(UserRole.Operaciones));

        await Assert.ThrowsAsync<AuthorizationException>(() => almacen.EstadoAsync());
        await Assert.ThrowsAsync<AuthorizationException>(() => almacen.ListarAsync("releases"));
        await Assert.ThrowsAsync<AuthorizationException>(() => almacen.EliminarAsync("releases/x.zip"));
    }

    // ── Carpetas ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("re:leases")]
    [InlineData("carpeta<mala>")]
    [InlineData("con|barra")]
    public async Task Un_nombre_de_carpeta_imposible_se_rechaza_antes_de_crear_nada(string ruta)
    {
        var (almacen, contenedor, _) = await ArmarAsync();

        var (ok, _) = await almacen.CrearCarpetaAsync(ruta);

        Assert.False(ok);
        Assert.Empty(contenedor.CarpetasCreadas);
    }

    [Fact]
    public async Task La_ruta_de_la_carpeta_se_normaliza_antes_de_crearla()
    {
        var (almacen, contenedor, _) = await ArmarAsync();

        // Un prefijo mal escrito no falla en Azure: crea una carpeta con nombre raro. Por eso se sanea.
        var (ok, _) = await almacen.CrearCarpetaAsync(@"  /releases\QA/  ");

        Assert.True(ok);
        Assert.Equal("releases/QA", contenedor.CarpetasCreadas.Single());
    }

    [Fact]
    public async Task Crear_una_carpeta_que_ya_existe_no_es_un_error()
    {
        var (almacen, _, _) = await ArmarAsync();

        await almacen.CrearCarpetaAsync("releases/QA");
        var (ok, mensaje) = await almacen.CrearCarpetaAsync("releases/QA");

        // Idempotente a propósito: volver a crearla no puede borrar lo que tenga dentro.
        Assert.True(ok);
        Assert.Contains("ya existía", mensaje);
    }

    [Fact]
    public async Task Borrar_una_carpeta_base_se_avisa_como_tal()
    {
        var (almacen, contenedor, _) = await ArmarAsync();
        contenedor.Blobs["releases/QA/paquete.zip"] = [];
        contenedor.Blobs["releases/paquete.zip"] = [];

        var alcance = await almacen.AlcanceDeBorradoAsync("releases");

        Assert.Equal(2, alcance.Elementos);
        Assert.True(alcance.EsCarpetaBase);
    }

    [Fact]
    public async Task Borrar_una_carpeta_PADRE_de_una_base_tambien_avisa()
    {
        var (almacen, contenedor, db) = await ArmarAsync();

        // Con las versiones en «prod/releases», borrar «prod» se lleva el histórico entero igual, y
        // sin este aviso nadie lo vería venir.
        var auditoria = new AuditService(db, UsuarioDePrueba.Como(UserRole.Admin), new OrigenDePrueba());
        await new SettingsService(db, UsuarioDePrueba.Como(UserRole.Admin), auditoria)
            .GuardarAsync(SettingsService.Claves.AzureBlobReleasesPrefix, "prod/releases");

        contenedor.Blobs["prod/releases/paquete.zip"] = [];

        var alcance = await almacen.AlcanceDeBorradoAsync("prod");

        Assert.True(alcance.EsCarpetaBase);
    }

    // ── Archivos ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task El_listado_omite_los_marcadores_de_carpeta()
    {
        var (almacen, contenedor, _) = await ArmarAsync();
        contenedor.Blobs["releases/"] = [];
        contenedor.Blobs["releases/paquete.zip"] = new Dictionary<string, string> { ["version"] = "1.2.3" };

        var listado = await almacen.ListarAsync("releases");

        var archivo = Assert.Single(listado.Archivos);
        Assert.Equal("paquete.zip", archivo.NombreCorto);
        Assert.Equal("version", archivo.Metadatos.Single().Clave);
    }

    [Fact]
    public async Task Subir_no_pisa_lo_que_ya_existe_si_no_se_pidio()
    {
        var (almacen, contenedor, _) = await ArmarAsync();
        contenedor.Blobs["releases/paquete.zip"] = new Dictionary<string, string> { ["marca"] = "original" };

        using var contenido = new MemoryStream([9, 9, 9]);
        var (ok, mensaje) = await almacen.SubirAsync("releases", "paquete.zip", contenido, sobrescribir: false);

        // Azure sobrescribe sin preguntar: la decisión la toma quien está delante, no el SDK.
        Assert.False(ok);
        Assert.Contains("ya existe", mensaje);
        Assert.Equal("original", contenedor.Blobs["releases/paquete.zip"]["marca"]);
    }

    [Fact]
    public async Task Subir_limpia_el_nombre_del_archivo()
    {
        var (almacen, contenedor, _) = await ArmarAsync();

        using var contenido = new MemoryStream([1]);
        var (ok, _) = await almacen.SubirAsync("releases", @"..\..\otro sitio\paquete.zip", contenido, true);

        Assert.True(ok);
        Assert.All(contenedor.Blobs.Keys, k => Assert.DoesNotContain("..", k));
    }

    // ── Metadatos ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("versión")]   // los acentos son letras Unicode, pero Azure los rechaza
    [InlineData("2版")]
    [InlineData("mi-clave")]
    [InlineData("1version")]
    public async Task Un_nombre_de_metadato_que_Azure_rechazaria_se_para_aqui(string clave)
    {
        var (almacen, contenedor, _) = await ArmarAsync();
        contenedor.Blobs["releases/paquete.zip"] = [];

        var (ok, mensaje) = await almacen.GuardarMetadatosAsync(
            "releases/paquete.zip", [new MetadatoDeBlobDto(clave, "x")]);

        // Sin esto el SDK falla con un error que no dice cuál es el problema.
        Assert.False(ok);
        Assert.Contains(clave, mensaje);
    }

    [Fact]
    public async Task Guardar_metadatos_reemplaza_el_conjunto_completo()
    {
        var (almacen, contenedor, _) = await ArmarAsync();
        contenedor.Blobs["releases/paquete.zip"] = new Dictionary<string, string>
        {
            ["version"] = "1.0.0",
            ["sistema"] = "portal"
        };

        var (ok, _) = await almacen.GuardarMetadatosAsync(
            "releases/paquete.zip", [new MetadatoDeBlobDto("version", "2.0.0")]);

        Assert.True(ok);

        // Azure no hace mezcla, y esto lo refleja: lo que se manda es lo que queda.
        var quedaron = contenedor.Blobs["releases/paquete.zip"];
        Assert.Equal("2.0.0", quedaron["version"]);
        Assert.False(quedaron.ContainsKey("sistema"));
    }

    // ── Enlaces de descarga ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(9000)]
    public async Task La_vigencia_del_enlace_tiene_limites(double horas)
    {
        var (almacen, _, _) = await ArmarAsync();

        var (ok, _, enlace) = await almacen.EnlaceDeDescargaAsync("releases/paquete.zip", horas);

        Assert.False(ok);
        Assert.Null(enlace);
    }

    [Fact]
    public async Task Generar_un_enlace_queda_en_la_bitacora()
    {
        var (almacen, _, db) = await ArmarAsync();

        var (ok, _, enlace) = await almacen.EnlaceDeDescargaAsync("releases/paquete.zip", 8);

        Assert.True(ok);
        Assert.NotNull(enlace);

        // El enlace da acceso al archivo a quien lo tenga: sin esta línea, un archivo filtrado no
        // tendría a quién atribuirse.
        var anotado = await db.AuditLogs.AsNoTracking()
            .AnyAsync(a => a.EntityType == "Blob" && a.Details!.Contains("Enlace de descarga"));
        Assert.True(anotado);
    }

    [Fact]
    public async Task Si_la_cadena_no_puede_firmar_se_dice_claro()
    {
        var (almacen, contenedor, _) = await ArmarAsync();
        contenedor.PuedeFirmar = false;

        var (ok, mensaje, _) = await almacen.EnlaceDeDescargaAsync("releases/paquete.zip", 8);

        Assert.False(ok);
        Assert.Contains("clave de la cuenta", mensaje);
    }

    // ── Subcarpetas de destino ──────────────────────────────────────────────────

    [Fact]
    public async Task Las_subcarpetas_salen_de_la_configuracion_y_de_lo_que_existe()
    {
        var (almacen, contenedor, db) = await ArmarAsync();

        var auditoria = new AuditService(db, UsuarioDePrueba.Como(UserRole.Admin), new OrigenDePrueba());
        await new SettingsService(db, UsuarioDePrueba.Como(UserRole.Admin), auditoria)
            .GuardarAsync(SettingsService.Claves.AzureBlobEnvironmentFolders, "QA; Operaciones ;Productivo");

        // Un entorno nuevo tiene que poder elegirse ANTES de que exista ninguna versión suya; y lo que
        // ya está en el contenedor no puede dejar de verse por no estar en la lista configurada.
        contenedor.Blobs["releases/Infrasur/paquete.zip"] = [];

        var carpetas = await almacen.SubcarpetasDeVersionesAsync();

        Assert.Contains("QA", carpetas);
        Assert.Contains("Operaciones", carpetas);
        Assert.Contains("Productivo", carpetas);
        Assert.Contains("Infrasur", carpetas);
    }

    // ── Reglas de nombres, sin red de por medio ─────────────────────────────────

    [Theory]
    [InlineData("/releases/", "releases")]
    [InlineData(@"releases\QA", "releases/QA")]
    [InlineData("a//b", "a/b")]
    [InlineData("   ", "porOmision")]
    [InlineData(null, "porOmision")]
    public void Normalizar_deja_el_prefijo_como_lo_espera_Azure(string? entrada, string esperado) =>
        Assert.Equal(esperado, RutasDeBlob.Normalizar(entrada, "porOmision"));

    [Fact]
    public void Las_subcarpetas_se_deducen_de_los_nombres_de_blob()
    {
        var nombres = new[]
        {
            "releases/QA/paquete.zip",
            "releases/QA/otro.zip",
            "releases/Productivo/paquete.zip",
            "releases/suelto.zip",          // sin subcarpeta: no cuenta
            "backups/app.db"                // otra rama: no cuenta
        };

        Assert.Equal(["Productivo", "QA"], RutasDeBlob.DerivarSubcarpetas(nombres, "releases"));
    }

    [Fact]
    public void El_marcador_de_carpeta_se_reconoce_por_la_barra_final() =>
        Assert.True(RutasDeBlob.EsMarcadorDeCarpeta("releases/QA/"));
}
