using AdminWeb.Application.Services;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Documentos;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Una base SQLite temporal por prueba. Mismo andamiaje que las ~1000 pruebas del escritorio, para
/// que puedan portarse tal cual: lo único que cambia es que el esquema se crea con el modelo de EF
/// en vez de con el migrador manual (el migrador se porta en su propio paso).
/// </summary>
internal static class TestDb
{
    /// <summary>
    /// Borra las bases que dejaron las EJECUCIONES ANTERIORES. Corre una sola vez, antes de crear la
    /// primera de esta tanda, porque es un constructor estático.
    ///
    /// <para><b>Hace falta porque nadie borra estos archivos.</b> Cada prueba crea el suyo en el
    /// temporal y al terminar el contexto se cierra, pero el archivo se queda: son unos 1 500 y cerca
    /// de 400 MB por ejecución completa. Tras unos meses de trabajo eso llena el disco.</para>
    ///
    /// <para>Y el modo en que se manifiesta es lo que obliga a arreglarlo aquí: cuando el disco se
    /// llena, la suite NO falla donde está el problema. Fallan cientos de pruebas sin ninguna
    /// relación entre sí con <c>SQLite Error 13: database or disk is full</c> lanzado desde
    /// <c>EnsureCreated</c>, que señala a cualquier sitio menos al verdadero; y quien lo vea
    /// pensará que el cambio que acaba de hacer rompió medio sistema.</para>
    ///
    /// <para><b>Al EMPEZAR y no al terminar</b>, y solo lo de hace más de una hora. Borrar al
    /// terminar exigiría que las ~1 500 pruebas cerraran su contexto —muchas no lo hacen, y SQLite
    /// mantiene el archivo tomado mientras viva la conexión—. El corte de una hora garantiza además
    /// que nunca se toca un archivo de la tanda en curso, ni aunque haya dos corriendo a la vez.</para>
    ///
    /// <para>El tope de borrados acota lo que puede tardar el barrido: con un rezago de cientos de
    /// miles de archivos, enumerar el temporal entero costaría minutos y parecería un cuelgue. Con
    /// tope, el rezago se drena en unas cuantas ejecuciones y ninguna se nota.</para>
    /// </summary>
    static TestDb()
    {
        // Nada de esto puede tumbar la suite: un fallo en el barrido saldría como
        // TypeInitializationException en TODAS las pruebas, que es peor que no barrer.
        try
        {
            var limite = DateTime.UtcNow.AddHours(-1);
            int borrados = 0;

            foreach (var archivo in Directory.EnumerateFiles(Path.GetTempPath(), "adminweb_*"))
            {
                if (borrados >= 20_000) break;
                try
                {
                    if (File.GetLastWriteTimeUtc(archivo) > limite) continue;
                    File.Delete(archivo);
                    borrados++;
                }
                catch { /* tomado por otro proceso o ya borrado: no es asunto de esta tanda */ }
            }
        }
        catch { /* sin temporal accesible se sigue igual; el barrido es higiene, no una regla */ }
    }

    public static AppDbContext New()
    {
        var path = Path.Combine(Path.GetTempPath(), "adminweb_" + Guid.NewGuid().ToString("N") + ".db");
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options;
        var db = new AppDbContext(opts);
        db.Database.EnsureCreated();
        return db;
    }
}

/// <summary>
/// Una identidad de prueba. Reemplaza al <c>Ctx.As(...)</c> del escritorio: allí fabricaba un
/// CurrentUserContext; aquí basta con implementar la interfaz, porque los servicios dependen de
/// ella y no de la implementación — que es justo lo que hace portable el código.
/// </summary>
internal sealed class UsuarioDePrueba : ICurrentUser
{
    public int? UserId { get; init; }
    public string? Username { get; init; }
    public string? FullName { get; init; }
    public UserRole? Role { get; init; }
    public int? DeveloperId { get; init; }

    public bool IsLoggedIn => UserId != null;
    public bool IsAdmin => Role == UserRole.Admin;
    public bool IsDesarrollador => Role == UserRole.Desarrollador;
    public bool IsOperaciones => Role == UserRole.Operaciones;

    public static UsuarioDePrueba Como(UserRole rol, int? developerId = null, int userId = 1) =>
        new() { UserId = userId, Username = rol.ToString().ToLowerInvariant(), FullName = rol.ToString(), Role = rol, DeveloperId = developerId };

    public static UsuarioDePrueba Anonimo() => new();
}

/// <summary>Origen fijo, para que la bitácora de las pruebas no dependa del entorno.</summary>
internal sealed class OrigenDePrueba : IRequestOrigin
{
    public string Describir() => "prueba";
}

/// <summary>
/// Un almacén de blobs que NUNCA se llega a usar.
///
/// Sirve para las pruebas que necesitan construir algo que depende del almacén pero no lo ejercitan:
/// sin cadena de conexión en la configuración, el servicio ni siquiera llega hasta aquí. Cada método
/// lanza en vez de devolver un valor inventado — si alguna prueba acabara pisando este camino,
/// conviene que se entere en el momento y no que pase en verde creyendo que probó algo.
/// </summary>
internal sealed class BlobsSinConfigurar : IClienteDeBlobs
{
    private static T No<T>() => throw new InvalidOperationException(
        "Esta prueba no debería llegar al almacén de blobs. Si de verdad lo necesita, usa un doble " +
        "de verdad en lugar de este.");

    public Task<(bool ok, string mensaje)> ProbarAsync(CredencialesDeBlob c, CancellationToken ct = default) => No<Task<(bool, string)>>();
    public Task<IReadOnlyList<BlobDelContenedor>> ListarAsync(CredencialesDeBlob c, string prefijo, CancellationToken ct = default) => No<Task<IReadOnlyList<BlobDelContenedor>>>();
    public Task<IReadOnlyList<string>> ListarNombresAsync(CredencialesDeBlob c, string? prefijo, CancellationToken ct = default) => No<Task<IReadOnlyList<string>>>();
    public Task<bool> CrearCarpetaAsync(CredencialesDeBlob c, string ruta, CancellationToken ct = default) => No<Task<bool>>();
    public Task<bool> ExisteAsync(CredencialesDeBlob c, string blob, CancellationToken ct = default) => No<Task<bool>>();
    public Task<bool> EliminarAsync(CredencialesDeBlob c, string blob, CancellationToken ct = default) => No<Task<bool>>();
    public Task<int> ContarEnCarpetaAsync(CredencialesDeBlob c, string carpeta, CancellationToken ct = default) => No<Task<int>>();
    public Task<int> EliminarCarpetaAsync(CredencialesDeBlob c, string carpeta, CancellationToken ct = default) => No<Task<int>>();
    public Task<IReadOnlyDictionary<string, string>> ObtenerMetadatosAsync(CredencialesDeBlob c, string blob, CancellationToken ct = default) => No<Task<IReadOnlyDictionary<string, string>>>();
    public Task ActualizarMetadatosAsync(CredencialesDeBlob c, string blob, IDictionary<string, string> m, CancellationToken ct = default) => No<Task>();
    public Task SubirAsync(CredencialesDeBlob c, string blob, Stream contenido, IDictionary<string, string>? m = null, CancellationToken ct = default) => No<Task>();
    public Task<Stream> AbrirLecturaAsync(CredencialesDeBlob c, string blob, CancellationToken ct = default) => No<Task<Stream>>();
    public Task<long> TamanoAsync(CredencialesDeBlob c, string blob, CancellationToken ct = default) => No<Task<long>>();
    public Uri GenerarEnlaceDeDescarga(CredencialesDeBlob c, string blob, double horas) => No<Uri>();
    public string Explicar(Exception ex) => ex.Message;
}

/// <summary>Una descarga de carpeta remota que nunca sale a la red: no hay nada que traer.</summary>
internal sealed class DescargaSinRed : IDescargaDeCarpetaRemota
{
    public Task<int> DescargarAsync(CredencialesDeCarpetaRemota credenciales, string carpetaLocal,
        IProgress<string> avance, CancellationToken ct = default) => Task.FromResult(0);
}

internal static class Fabrica
{
    public static AuthService Auth(AppDbContext db, ICurrentUser? usuario = null)
    {
        var actual = usuario ?? UsuarioDePrueba.Anonimo();
        return new AuthService(db, actual, new AuditService(db, actual, new OrigenDePrueba()));
    }

    /// <summary>
    /// El servicio de las solicitudes propias, ya con sus dependencias.
    ///
    /// Se arma aquí y no en cada archivo de pruebas porque son cuatro piezas y crecen: cuando entró
    /// la firma del colaborador hubo que añadirle SignatureService, y con la construcción repetida
    /// eso fueron tres archivos tocados para no probar nada nuevo.
    /// </summary>
    public static VacationRequestService Vacaciones(AppDbContext db, ICurrentUser usuario)
    {
        var auditoria = new AuditService(db, usuario, new OrigenDePrueba());
        return new VacationRequestService(db, usuario, auditoria, new SignatureService(db, usuario, auditoria));
    }

    /// <summary>
    /// El saldo de vacaciones, con su calendario laboral. Los dos van juntos siempre: el saldo cuenta
    /// días LABORABLES, así que sin calendario no sabría cuáles descontar.
    /// </summary>
    public static SaldoDeVacacionesService Saldo(AppDbContext db, ICurrentUser usuario)
    {
        var auditoria = new AuditService(db, usuario, new OrigenDePrueba());
        return new SaldoDeVacacionesService(
            db, usuario, auditoria, new SettingsService(db, usuario, auditoria),
            new CalendarioLaboralService(db));
    }

    /// <summary>El lado del líder: resolver, emitir el documento y firmarlo.</summary>
    public static DocumentoDeVacacionesService DocumentoDeVacaciones(AppDbContext db, ICurrentUser usuario)
    {
        var auditoria = new AuditService(db, usuario, new OrigenDePrueba());
        return new DocumentoDeVacacionesService(
            db, usuario, new SettingsService(db, usuario, auditoria),
            new SignatureService(db, usuario, auditoria),
            new GeneradorDeDocumentosQuestPdf(), new PlantillaDeVacacionesOpenXml(), auditoria,
            Vacaciones(db, usuario), Saldo(db, usuario));
    }
}
