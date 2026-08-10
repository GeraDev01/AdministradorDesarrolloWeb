using AdminWeb.Application.Services;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Dtos.Administracion;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Los «probar conexión» de la pantalla de configuración.
///
/// <para><b>Aquí no se toca la red.</b> Las tres integraciones están detrás de una interfaz y aquí se
/// sustituyen por dobles: lo que hay que comprobar —que se prueba lo GUARDADO y no lo que mande el
/// navegador, que sin nada guardado se dice en vez de intentarlo, y que el mensaje no repite el
/// secreto— es decisión, no transferencia.</para>
///
/// <para>La regla que estas pruebas protegen es la que se decidió y no se relaja: <b>la petición dice
/// QUÉ probar, nunca CON QUÉ</b>. El día que alguien añada un parámetro «cadena» por comodidad, el
/// endpoint se convierte en un probador de credenciales ajenas para cualquiera con sesión de líder, y
/// la prueba que lo delata es <see cref="Sql_PruebaLaCadenaGuardada_NoUnaQueLlegueDeFuera"/>.</para>
/// </summary>
public class PruebasDeConexionServiceTests
{
    private const string CadenaSql =
        "server=mi-srv.database.windows.net; uid=usuario; pwd=SuperSecreta123; database=MI_BD";

    private const string CadenaBlob =
        "DefaultEndpointsProtocol=https;AccountName=cuenta;AccountKey=c2VjcmV0bw==;EndpointSuffix=core.windows.net";

    // ── Dobles ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Un contenedor de Azure del que solo interesa la prueba.
    ///
    /// El resto de la interfaz lanza a propósito en vez de devolver vacío: si alguna de estas rutas
    /// se llamara desde la comprobación de conexión, la prueba tiene que fallar y decirlo, no pasar
    /// en silencio.
    /// </summary>
    private sealed class BlobDeMentira : IClienteDeBlobs
    {
        public (bool ok, string mensaje) Respuesta = (true, "Conexión correcta con el contenedor «despliegues».");
        public CredencialesDeBlob? Credenciales;

        public Task<(bool ok, string mensaje)> ProbarAsync(CredencialesDeBlob cred, CancellationToken ct = default)
        {
            Credenciales = cred;
            return Task.FromResult(Respuesta);
        }

        private static Exception NoTocaAqui([System.Runtime.CompilerServices.CallerMemberName] string que = "") =>
            new NotSupportedException($"Probar la conexión no debe llamar a {que}.");

        public Task<IReadOnlyList<BlobDelContenedor>> ListarAsync(CredencialesDeBlob c, string p, CancellationToken ct = default) => throw NoTocaAqui();
        public Task<IReadOnlyList<string>> ListarNombresAsync(CredencialesDeBlob c, string? p, CancellationToken ct = default) => throw NoTocaAqui();
        public Task<bool> CrearCarpetaAsync(CredencialesDeBlob c, string r, CancellationToken ct = default) => throw NoTocaAqui();
        public Task<bool> ExisteAsync(CredencialesDeBlob c, string b, CancellationToken ct = default) => throw NoTocaAqui();
        public Task<bool> EliminarAsync(CredencialesDeBlob c, string b, CancellationToken ct = default) => throw NoTocaAqui();
        public Task<int> ContarEnCarpetaAsync(CredencialesDeBlob c, string f, CancellationToken ct = default) => throw NoTocaAqui();
        public Task<int> EliminarCarpetaAsync(CredencialesDeBlob c, string f, CancellationToken ct = default) => throw NoTocaAqui();
        public Task<IReadOnlyDictionary<string, string>> ObtenerMetadatosAsync(CredencialesDeBlob c, string b, CancellationToken ct = default) => throw NoTocaAqui();
        public Task ActualizarMetadatosAsync(CredencialesDeBlob c, string b, IDictionary<string, string> m, CancellationToken ct = default) => throw NoTocaAqui();
        public Task SubirAsync(CredencialesDeBlob c, string b, Stream s, IDictionary<string, string>? m = null, CancellationToken ct = default) => throw NoTocaAqui();
        public Task<Stream> AbrirLecturaAsync(CredencialesDeBlob c, string b, CancellationToken ct = default) => throw NoTocaAqui();
        public Task<long> TamanoAsync(CredencialesDeBlob c, string b, CancellationToken ct = default) => throw NoTocaAqui();
        public Uri GenerarEnlaceDeDescarga(CredencialesDeBlob c, string b, double h) => throw NoTocaAqui();
        public string Explicar(Exception ex) => ex.Message;
    }

    // ── Andamiaje ───────────────────────────────────────────────────────────────

    private sealed record Entorno(
        PruebasDeConexionService Pruebas, BlobDeMentira Blob, AppDbContext Db);

    private static async Task<Entorno> ArmarAsync(ICurrentUser? quien = null,
        string? cadenaSql = null, string? cadenaBlob = null, bool correoHabilitado = false)
    {
        var db = TestDb.New();
        var actual = quien ?? UsuarioDePrueba.Como(UserRole.Admin);
        var auditoria = new AuditService(db, actual, new OrigenDePrueba());
        var ajustes = new SettingsService(db, actual, auditoria);

        // Se guarda por el servicio de configuración a propósito: es él quien decide que una clave
        // terminada en «ConnectionString» es secreta y la cifra antes de tocar la base. Así la prueba
        // recorre también el descifrado, que es donde de verdad se ve si «lo guardado» llega entero.
        var comoAdmin = new SettingsService(db, UsuarioDePrueba.Como(UserRole.Admin), auditoria);
        if (cadenaSql != null)
            await comoAdmin.GuardarAsync(SettingsService.Claves.AzureSqlConnectionString, cadenaSql);
        if (cadenaBlob != null)
            await comoAdmin.GuardarAsync(SettingsService.Claves.AzureBlobConnectionString, cadenaBlob);
        if (correoHabilitado)
        {
            await comoAdmin.GuardarAsync(SettingsService.Claves.EmailEnabled, "true");
            await comoAdmin.GuardarAsync(SettingsService.Claves.EmailAddress, "app@empresa.com");
            await comoAdmin.GuardarAsync(SettingsService.Claves.EmailPassword, "clave-de-aplicacion");
            await comoAdmin.GuardarAsync(SettingsService.Claves.EmailSmtpHost, "smtp.empresa.com");
        }

        var blob = new BlobDeMentira();
        var correo = new CorreoDeMentira();

        var almacen = new AlmacenamientoService(ajustes, blob, actual, auditoria);
        var resumen = new DigestService(db, ajustes, correo, auditoria, actual);
        var ingesta = new IngestaDeCorreoService(db, ajustes, correo, resumen, auditoria, actual);

        return new Entorno(
            new PruebasDeConexionService(db, almacen, ingesta, actual), blob, db);
    }

    // ── La regla que no se relaja ───────────────────────────────────────────────

    /// <summary>
    /// Se prueba la conexión REAL de esta aplicación, no una cadena guardada en una fila.
    ///
    /// <para>Antes leía <c>AzureSqlConnectionString</c> anunciándola como «la que usa el escritorio».
    /// No lo era: en el escritorio esa constante está declarada y no la lee ni la escribe nadie. Era
    /// un campo que no gobernaba nada y una prueba que no probaba nada.</para>
    ///
    /// <para>La base de las pruebas es un SQLite temporal, así que conecta de verdad y el mensaje
    /// nombra el archivo que hay detrás — que es exactamente lo que se quiere poder mirar.</para>
    /// </summary>
    [Fact]
    public async Task Sql_PruebaLaConexionDeLaAplicacion()
    {
        var e = await ArmarAsync();

        var (ok, mensaje) = await e.Pruebas.ProbarAsync(PruebaDeConexion.Sql);

        Assert.True(ok, mensaje);
        Assert.Contains("Conectado", mensaje);
    }

    /// <summary>
    /// El mensaje no puede llevar dentro una credencial: acaba pintado en el navegador y en la
    /// consola de quien la tenga abierta.
    /// </summary>
    [Fact]
    public async Task Sql_ElMensajeNuncaRepiteUnSecreto()
    {
        var e = await ArmarAsync();

        var (_, mensaje) = await e.Pruebas.ProbarAsync(PruebaDeConexion.Sql);

        Assert.DoesNotContain("pwd=", mensaje, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", mensaje, StringComparison.OrdinalIgnoreCase);
    }

    // ── Enrutado a cada integración ─────────────────────────────────────────────

    [Fact]
    public async Task Blob_ReusaLaPruebaQueYaExistia()
    {
        var e = await ArmarAsync(cadenaBlob: CadenaBlob);

        var (ok, mensaje) = await e.Pruebas.ProbarAsync(PruebaDeConexion.Blob);

        Assert.True(ok);
        Assert.Contains("contenedor", mensaje);

        // Las credenciales las armó el servicio de almacenamiento con lo guardado; el contenedor de
        // por omisión entra solo cuando no hay ninguno configurado.
        Assert.Equal(CadenaBlob, e.Blob.Credenciales!.CadenaDeConexion);
        Assert.Equal("despliegues", e.Blob.Credenciales.Contenedor);
    }

    [Fact]
    public async Task Blob_SinConfigurar_LoDiceEnVezDeReventar()
    {
        var e = await ArmarAsync();

        var (ok, mensaje) = await e.Pruebas.ProbarAsync(PruebaDeConexion.Blob);

        Assert.False(ok);
        Assert.Contains("Azure Blob Storage", mensaje);
        Assert.Null(e.Blob.Credenciales);
    }

    [Fact]
    public async Task Correo_ReusaLaPruebaQueYaExistia()
    {
        var e = await ArmarAsync(correoHabilitado: true);

        var (ok, mensaje) = await e.Pruebas.ProbarAsync(PruebaDeConexion.Correo);

        Assert.True(ok);
        Assert.Contains("Conexión exitosa", mensaje);
    }

    /// <summary>
    /// Los dos textos del diagnóstico del correo son los del escritorio y distinguen «está apagado»
    /// de «está encendido pero incompleto», que llevan a arreglos distintos en la misma pantalla.
    /// </summary>
    [Fact]
    public async Task Correo_Apagado_LoDiceConSusPalabras()
    {
        var e = await ArmarAsync();

        var (ok, mensaje) = await e.Pruebas.ProbarAsync(PruebaDeConexion.Correo);

        Assert.False(ok);
        Assert.Contains("no está habilitado", mensaje);
    }

    // ── Autorización ────────────────────────────────────────────────────────────

    /// <summary>
    /// Probar es del líder y de nadie más. Importa especialmente en SQL: ahí la lectura de la
    /// configuración (<c>SettingsService.ObtenerAsync</c>) NO lleva guarda —es la lectura interna del
    /// servidor—, así que sin esta comprobación cualquiera con sesión podría usar la respuesta para
    /// averiguar si una base existe y si sus credenciales siguen sirviendo.
    /// </summary>
    [Theory]
    [InlineData(PruebaDeConexion.Blob)]
    [InlineData(PruebaDeConexion.Sql)]
    [InlineData(PruebaDeConexion.Correo)]
    public async Task Probar_EsSoloDelLider(PruebaDeConexion que)
    {
        var e = await ArmarAsync(
            UsuarioDePrueba.Como(UserRole.Operaciones, userId: 9),
            cadenaSql: CadenaSql, cadenaBlob: CadenaBlob, correoHabilitado: true);

        await Assert.ThrowsAsync<AuthorizationException>(() => e.Pruebas.ProbarAsync(que));
    }
}
