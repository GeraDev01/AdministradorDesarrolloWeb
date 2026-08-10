using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Dtos.Programados;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Application.Services;

/// <summary>
/// El explorador del contenedor de Azure Blob Storage: qué hay en cada carpeta, qué se sube, qué se
/// borra y qué enlaces de descarga se firman.
///
/// <para><b>Es la mitad de arriba del <c>BlobStorageService</c> del escritorio.</b> La otra mitad
/// —las llamadas al SDK— vive en <see cref="IClienteDeBlobs"/>, en Infrastructure. Aquí queda lo que
/// no es transferencia: de dónde salen las credenciales, quién puede tocar esto y qué se anota en la
/// bitácora.</para>
///
/// <para><b>La cadena de conexión no sale de aquí jamás.</b> Se lee de la configuración para armar
/// la llamada y se queda dentro; ningún DTO, ningún mensaje de error y ninguna línea de bitácora la
/// repiten. En el escritorio esto no se planteaba porque el valor no salía del proceso; aquí el
/// destino de cualquier respuesta es un navegador con la consola abierta.</para>
///
/// <para><b>Solo el líder.</b> Es la misma regla del escritorio: desde esta pantalla se toca
/// directamente el almacenamiento —se borran versiones y respaldos— y no es una operación del área
/// de despliegues, es una de administración.</para>
/// </summary>
public class AlmacenamientoService(
    SettingsService configuracion,
    IClienteDeBlobs blobs,
    ICurrentUser currentUser,
    AuditService audit)
{
    /// <summary>Contenedor de por omisión cuando la configuración no dice otro.</summary>
    private const string ContenedorPorOmision = "despliegues";

    /// <summary>
    /// Carpeta de los respaldos de la base de datos.
    ///
    /// <para><b>La web NO respalda la base.</b> Azure SQL trae respaldo continuo propio y
    /// restauración a un punto en el tiempo; montar encima un respaldo manual sería mantener una
    /// copia peor de algo que ya existe, y una copia peor en la que alguien confiaría. Por eso
    /// <c>BackupService</c> y <c>AutoBackupService</c> del escritorio no se portan.</para>
    ///
    /// <para>La carpeta sí se sigue ofreciendo en el explorador: ahí están los respaldos que dejó el
    /// escritorio antes del corte, y esconderlos no los borra — solo los vuelve inalcanzables desde
    /// la única pantalla que puede administrarlos.</para>
    /// </summary>
    private const string PrefijoRespaldosDeBasePorOmision = "backups";

    // ── Configuración en efecto ─────────────────────────────────────────────────

    /// <summary>¿Hay cadena de conexión guardada? Es lo único que se puede saber de ella desde fuera.</summary>
    public async Task<bool> EstaConfiguradoAsync(CancellationToken ct = default) =>
        await CadenaAsync(ct) != null;

    private async Task<string?> CadenaAsync(CancellationToken ct) =>
        await configuracion.ObtenerAsync(SettingsService.Claves.AzureBlobConnectionString, ct)
            is { Length: > 0 } guardada ? guardada : null;

    /// <summary>Contenedor configurado, o el de por omisión. Vacío cuenta como «no puesto»: guardarlo
    /// en blanco es la forma de volver al valor de siempre.</summary>
    public async Task<string> ContenedorAsync(CancellationToken ct = default) =>
        await configuracion.ObtenerAsync(SettingsService.Claves.AzureBlobContainer, ct)
            is { Length: > 0 } guardado ? guardado : ContenedorPorOmision;

    /// <summary>Carpeta donde se guardan los paquetes de las versiones.</summary>
    public async Task<string> CarpetaDeVersionesAsync(CancellationToken ct = default) =>
        RutasDeBlob.Normalizar(
            await configuracion.ObtenerAsync(SettingsService.Claves.AzureBlobReleasesPrefix, ct),
            RutasDeBlob.PrefijoVersionesPorOmision);

    /// <summary>Carpeta donde se guardan los respaldos previos de las carpetas remotas.</summary>
    public async Task<string> CarpetaDeRespaldosDeDespliegueAsync(CancellationToken ct = default) =>
        RutasDeBlob.Normalizar(
            await configuracion.ObtenerAsync(SettingsService.Claves.AzureBlobDeployBackupsPrefix, ct),
            RutasDeBlob.PrefijoRespaldosDeDesplieguePorOmision);

    private async Task<string> CarpetaDeRespaldosDeBaseAsync(CancellationToken ct) =>
        RutasDeBlob.Normalizar(
            await configuracion.ObtenerAsync(SettingsService.Claves.AzureBlobBackupsPrefix, ct),
            PrefijoRespaldosDeBasePorOmision);

    /// <summary>
    /// Las subcarpetas de destino que se ofrecen para las versiones.
    ///
    /// <para>Salen de la configuración, separadas por «;» —es la clave <c>AzureBlobEnvironmentFolders</c>,
    /// «QA;Operaciones;Productivo»— y se completan con las que de verdad existan hoy bajo la carpeta
    /// de versiones. Las dos fuentes hacen falta: la configurada porque un entorno nuevo tiene que
    /// poder elegirse ANTES de que exista ninguna versión suya, y la del contenedor porque lo que ya
    /// está ahí no se puede dejar de ver por no estar en la lista.</para>
    /// </summary>
    public async Task<IReadOnlyList<string>> SubcarpetasDeVersionesAsync(CancellationToken ct = default)
    {
        var configuradas = (await configuracion.ObtenerAsync(SettingsService.Claves.AzureBlobEnvironmentFolders, ct) ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(c => RutasDeBlob.Normalizar(c, ""))
            .Where(c => c.Length > 0);

        var todas = new HashSet<string>(configuradas, StringComparer.OrdinalIgnoreCase);

        if (await CadenaAsync(ct) is not null)
        {
            var raiz = await CarpetaDeVersionesAsync(ct);
            try
            {
                var nombres = await blobs.ListarNombresAsync(await CredencialesAsync(ct), raiz + "/", ct);
                foreach (var c in RutasDeBlob.DerivarSubcarpetas(nombres, raiz)) todas.Add(c);
            }
            catch
            {
                // Silencioso a propósito, igual que en el escritorio: si el listado falla se ofrece
                // lo configurado y quien esté delante puede teclear la carpeta a mano. Reventar aquí
                // dejaría sin pantalla algo que solo necesitaba una sugerencia.
            }
        }

        return [.. todas.OrderBy(c => c, StringComparer.OrdinalIgnoreCase)];
    }

    private async Task<CredencialesDeBlob> CredencialesAsync(CancellationToken ct)
    {
        var cadena = await CadenaAsync(ct)
            ?? throw new InvalidOperationException(
                "Azure Blob Storage no está configurado. Ve a Configuración y captura la cadena de conexión.");

        return new CredencialesDeBlob(cadena, await ContenedorAsync(ct));
    }

    // ── Lectura ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// El estado del almacenamiento y las carpetas que se pueden explorar.
    ///
    /// <para>Las carpetas base van siempre, existan o no en el contenedor: son a donde escribe la
    /// aplicación, y no ofrecerlas por estar vacías haría imposible llegar a ellas el primer día.</para>
    /// </summary>
    public async Task<AlmacenamientoDto> EstadoAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var versiones = await CarpetaDeVersionesAsync(ct);
        var respaldosDespliegue = await CarpetaDeRespaldosDeDespliegueAsync(ct);
        var respaldosBase = await CarpetaDeRespaldosDeBaseAsync(ct);
        var contenedor = await ContenedorAsync(ct);

        if (await CadenaAsync(ct) is null)
            return new AlmacenamientoDto(false, contenedor, [], versiones, respaldosDespliegue,
                "Azure Blob Storage no está configurado. Captura la cadena de conexión en Configuración.");

        var carpetas = new List<string> { versiones, respaldosDespliegue, respaldosBase };
        string? aviso = null;

        try
        {
            var nombres = await blobs.ListarNombresAsync(await CredencialesAsync(ct), null, ct);

            // Hasta dos niveles: interesa distinguir «releases/QA» de «releases/Productivo», no solo
            // ver «releases».
            var encontradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var nombre in nombres)
            {
                var partes = nombre.Split('/');
                if (partes.Length > 1) encontradas.Add(partes[0]);
                if (partes.Length > 2) encontradas.Add($"{partes[0]}/{partes[1]}");
            }

            foreach (var c in encontradas.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
                if (!carpetas.Contains(c, StringComparer.OrdinalIgnoreCase)) carpetas.Add(c);
        }
        catch (Exception ex)
        {
            aviso = $"No se pudieron listar las carpetas: {blobs.Explicar(ex)}";
        }

        return new AlmacenamientoDto(true, contenedor, carpetas, versiones, respaldosDespliegue, aviso);
    }

    /// <summary>
    /// El contenido de una carpeta.
    ///
    /// <para>Trae la carpeta ENTERA y no una página: el filtro y el orden se aplican en el navegador
    /// sobre lo ya traído. Es la misma decisión del escritorio —«filtrar y reordenar no vuelve a
    /// consultar Azure: sería una petición de red por cada tecla»— y aquí pesa más, porque cada
    /// consulta cruza además el enlace entre el servidor y el navegador.</para>
    /// </summary>
    public async Task<ListadoDeBlobsDto> ListarAsync(string? carpeta, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var normal = RutasDeBlob.Normalizar(carpeta, await CarpetaDeVersionesAsync(ct));
        var credenciales = await CredencialesAsync(ct);

        var encontrados = await blobs.ListarAsync(credenciales, normal + "/", ct);

        var archivos = encontrados
            .Select(b => new ArchivoDeBlobDto(
                b.Nombre,
                RutasDeBlob.NombreCorto(b.Nombre),
                b.Bytes,
                RutasDeBlob.TamanoLegible(b.Bytes),
                b.Modificado,
                [.. b.Metadatos.Select(m => new MetadatoDeBlobDto(m.Key, m.Value))
                              .OrderBy(m => m.Clave, StringComparer.OrdinalIgnoreCase)]))
            .OrderByDescending(a => a.ModificadoUtc)
            .ToList();

        long total = archivos.Sum(a => a.Bytes);
        var resumen = archivos.Count == 0
            ? "La carpeta está vacía o todavía no existe."
            : $"{archivos.Count} archivo(s) — {total / 1024d / 1024d:0.0} MB.";

        return new ListadoDeBlobsDto(normal, archivos, total, resumen);
    }

    /// <summary>Los metadatos actuales de un blob, releídos del servidor.</summary>
    public async Task<IReadOnlyList<MetadatoDeBlobDto>> MetadatosAsync(string blob, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var actuales = await blobs.ObtenerMetadatosAsync(await CredencialesAsync(ct), blob, ct);
        return [.. actuales.Select(m => new MetadatoDeBlobDto(m.Key, m.Value))
                          .OrderBy(m => m.Clave, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Abre un blob para descargarlo a través de la API.
    ///
    /// <para>Existe además del enlace temporal porque son cosas distintas: esto lo baja quien ya
    /// tiene sesión y permiso, y no deja ningún enlace suelto por ahí. El enlace firmado es para
    /// dárselo a alguien que NO entra a la aplicación.</para>
    /// </summary>
    public async Task<(Stream contenido, string nombre)> AbrirParaDescargaAsync(
        string blob, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var credenciales = await CredencialesAsync(ct);
        var contenido = await blobs.AbrirLecturaAsync(credenciales, blob, ct);

        await audit.RecordAsync(AuditAction.ConfigChange, "Blob", blob, $"Descarga de {blob}", ct);
        return (contenido, RutasDeBlob.NombreCorto(blob));
    }

    /// <summary>
    /// Abre el paquete de una versión para desplegarlo. <b>Sin guarda de sesión, y a propósito.</b>
    ///
    /// <para>El motor de despliegue corre SIN sesión: el programado usa la identidad ficticia
    /// «programado» y el manual se lanza en un <c>Task.Run</c> donde el <c>HttpContext</c> ya no
    /// existe. Llamando a <see cref="AbrirParaDescargaAsync"/>, su <c>RequireAdmin</c> saltaba
    /// siempre y el despliegue marcaba TODOS los servidores como fallidos con «Esta operación
    /// requiere permisos de líder» — un mensaje que además señalaba en la dirección equivocada.</para>
    ///
    /// <para>No abre ningún agujero: este método no está publicado en ninguna ruta de la API, y
    /// quien decide si un despliegue puede ocurrir es el propio motor, que ya comprobó permisos,
    /// checklist y perfil antes de crear el trabajo. La autorización vive donde se toma la decisión,
    /// no donde se leen los bytes.</para>
    ///
    /// <para>Tampoco anota en la bitácora, al revés que la descarga del explorador: el despliegue ya
    /// deja su propio expediente, y una línea de «Descarga de …» por cada uno sería ruido que
    /// dificultaría encontrar las descargas que sí hizo una persona.</para>
    ///
    /// <para>Acepta lo que haya en <c>ZipBlobUrl</c> en cualquiera de sus dos formas —nombre de blob
    /// o URL absoluta— porque esa columna la llenaron las dos aplicaciones con criterios distintos.</para>
    /// </summary>
    public async Task<Stream> AbrirPaqueteDeDespliegueAsync(
        string blobOUrl, CancellationToken ct = default)
    {
        var credenciales = await CredencialesAsync(ct);

        var nombre = RutasDeBlob.NombreDeBlobDesde(blobOUrl, credenciales.Contenedor)
            ?? throw new InvalidOperationException(
                $"No se pudo interpretar «{blobOUrl}» como un paquete del almacén.");

        return await blobs.AbrirLecturaAsync(credenciales, nombre, ct);
    }

    /// <summary>
    /// Comprueba la conexión con lo que YA está guardado.
    ///
    /// <para>Cambia respecto al escritorio: allí se podían probar valores sueltos sin guardarlos, para
    /// no tener que descubrir después que la cadena era la equivocada. Aquí no, y no es un olvido —
    /// aceptar una cadena de conexión por parámetro convertiría este endpoint en un probador de
    /// credenciales de Azure ajenas, disponible para cualquiera con una sesión de líder. Se guarda
    /// primero y se prueba después; el mensaje sigue diciendo exactamente qué falla.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> ProbarConexionAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        if (await CadenaAsync(ct) is null)
            return (false, "Falta la cadena de conexión de Azure Blob Storage. Captúrala en Configuración.");

        return await blobs.ProbarAsync(await CredencialesAsync(ct), ct);
    }

    // ── Escritura ───────────────────────────────────────────────────────────────

    /// <summary>Crea una carpeta. Es idempotente: volver a crearla no borra lo que tenga dentro.</summary>
    public async Task<(bool ok, string mensaje)> CrearCarpetaAsync(string? ruta, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        if (RutasDeBlob.ValidarPrefijo(ruta) is { } error) return (false, error);

        var normal = RutasDeBlob.Normalizar(ruta, "");
        if (normal.Length == 0) return (false, "Escribe el nombre de la carpeta.");

        try
        {
            bool creada = await blobs.CrearCarpetaAsync(await CredencialesAsync(ct), normal, ct);

            await audit.RecordAsync(AuditAction.ConfigChange, "Blob", normal,
                creada ? $"Carpeta creada en Blob Storage: {normal}" : $"La carpeta {normal} ya existía", ct);

            return (true, creada
                ? $"Carpeta «{normal}» creada."
                : $"La carpeta «{normal}» ya existía; no se modificó su contenido.");
        }
        catch (Exception ex)
        {
            return (false, blobs.Explicar(ex));
        }
    }

    /// <summary>
    /// Sube un archivo a una carpeta.
    /// </summary>
    /// <param name="sobrescribir">
    /// Si ya existe uno con ese nombre. Azure sobrescribe sin preguntar, así que la comprobación se
    /// hace aquí y la decisión la toma quien está delante — igual que en el escritorio, donde la
    /// pantalla avisaba antes de subir.
    /// </param>
    public async Task<(bool ok, string mensaje)> SubirAsync(
        string? carpeta, string nombreDeArchivo, Stream contenido, bool sobrescribir, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var limpio = ArchivosSubidos.NombreSeguro(nombreDeArchivo);
        if (limpio.Length == 0) return (false, "El archivo no tiene un nombre utilizable.");

        var destino = RutasDeBlob.Normalizar(carpeta, await CarpetaDeVersionesAsync(ct));
        var blob = $"{destino}/{limpio}";
        var credenciales = await CredencialesAsync(ct);

        try
        {
            if (!sobrescribir && await blobs.ExisteAsync(credenciales, blob, ct))
                return (false, $"«{limpio}» ya existe en «{destino}». Marca sobrescribir si quieres reemplazarlo.");

            await blobs.SubirAsync(credenciales, blob, contenido, null, ct);

            await audit.RecordAsync(AuditAction.ConfigChange, "Blob", blob,
                $"Subida a Blob Storage: {limpio} en «{destino}»", ct);

            return (true, $"«{limpio}» subido a «{destino}».");
        }
        catch (Exception ex)
        {
            return (false, blobs.Explicar(ex));
        }
    }

    /// <summary>
    /// Reemplaza los metadatos de un blob. Azure NO hace mezcla: lo que se manda es lo que queda, así
    /// que quien llama envía el conjunto completo.
    /// </summary>
    public async Task<(bool ok, string mensaje)> GuardarMetadatosAsync(
        string blob, IReadOnlyList<MetadatoDeBlobDto> metadatos, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        foreach (var m in metadatos)
            if (RutasDeBlob.ValidarClaveDeMetadato(m.Clave) is { } error) return (false, error);

        var credenciales = await CredencialesAsync(ct);

        try
        {
            // Se releen del servidor y no se usan los de la lista de la pantalla: pudieron cambiar
            // desde el portal o desde el escritorio, y guardar reemplaza el conjunto entero.
            var anteriores = await blobs.ObtenerMetadatosAsync(credenciales, blob, ct);

            var nuevos = metadatos
                .Where(m => !string.IsNullOrWhiteSpace(m.Clave))
                .ToDictionary(m => m.Clave.Trim(), m => m.Valor ?? "", StringComparer.OrdinalIgnoreCase);

            await blobs.ActualizarMetadatosAsync(credenciales, blob, nuevos, ct);

            await audit.RecordDetailedAsync(AuditAction.ConfigChange, "Blob", blob,
                $"Metadatos actualizados en {blob}", AuditOutcome.Exito,
                oldValues: anteriores, newValues: nuevos, ct: ct);

            return (true, $"Metadatos guardados en {RutasDeBlob.NombreCorto(blob)}.");
        }
        catch (Exception ex)
        {
            return (false, blobs.Explicar(ex));
        }
    }

    /// <summary>Elimina un archivo. No tiene vuelta atrás; la confirmación la hace la pantalla.</summary>
    public async Task<(bool ok, string mensaje)> EliminarAsync(string blob, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        try
        {
            bool borrado = await blobs.EliminarAsync(await CredencialesAsync(ct), blob, ct);

            // La bitácora refleja lo que de verdad pasó: si otro ya lo había borrado, no se miente
            // diciendo que esta persona lo eliminó.
            await audit.RecordAsync(AuditAction.Delete, "Blob", blob,
                borrado ? $"Archivo eliminado de Blob Storage: {blob}"
                        : $"Eliminar {blob}: el archivo ya no existía", ct);

            return (true, borrado
                ? $"Archivo «{RutasDeBlob.NombreCorto(blob)}» eliminado."
                : $"El archivo «{RutasDeBlob.NombreCorto(blob)}» ya no existía.");
        }
        catch (Exception ex)
        {
            return (false, blobs.Explicar(ex));
        }
    }

    /// <summary>
    /// Qué se llevaría por delante borrar una carpeta. Se consulta ANTES de preguntar para que la
    /// confirmación diga el alcance real en vez de «¿seguro?».
    /// </summary>
    public async Task<AlcanceDeBorradoDto> AlcanceDeBorradoAsync(string? carpeta, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var normal = RutasDeBlob.Normalizar(carpeta, "");
        if (normal.Length == 0) return new AlcanceDeBorradoDto("", 0, false);

        int cuantos = await blobs.ContarEnCarpetaAsync(await CredencialesAsync(ct), normal, ct);
        return new AlcanceDeBorradoDto(normal, cuantos, await EsCarpetaBaseAsync(normal, ct));
    }

    /// <summary>Elimina una carpeta y TODO lo que cuelga de ella, subcarpetas incluidas.</summary>
    public async Task<(bool ok, string mensaje)> EliminarCarpetaAsync(string? carpeta, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var normal = RutasDeBlob.Normalizar(carpeta, "");
        if (normal.Length == 0) return (false, "Indica la carpeta que quieres eliminar.");

        try
        {
            int borrados = await blobs.EliminarCarpetaAsync(await CredencialesAsync(ct), normal, ct);

            await audit.RecordAsync(AuditAction.Delete, "Blob", normal,
                borrados > 0
                    ? $"Carpeta eliminada de Blob Storage: {normal} ({borrados} elemento(s))"
                    : $"Eliminar carpeta {normal}: no había nada que borrar", ct);

            return (true, borrados > 0
                ? $"Carpeta «{normal}» eliminada ({borrados} elemento(s))."
                : $"La carpeta «{normal}» estaba vacía o ya no existía.");
        }
        catch (Exception ex)
        {
            return (false, blobs.Explicar(ex));
        }
    }

    /// <summary>
    /// Firma un enlace temporal de solo lectura para UN archivo.
    ///
    /// <para>El enlace da acceso a ese archivo a quien lo tenga, así que queda en la bitácora quién
    /// lo generó, para qué archivo y hasta cuándo sirve. Es la decisión del escritorio y se conserva
    /// tal cual: sin esa línea, un archivo filtrado no tendría a quién atribuirse.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje, EnlaceDeDescargaDto? enlace)> EnlaceDeDescargaAsync(
        string blob, double horas, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        if (horas is < 1 or > 8760)
            return (false, "La vigencia debe estar entre 1 hora y 1 año.", null);

        try
        {
            var uri = blobs.GenerarEnlaceDeDescarga(await CredencialesAsync(ct), blob, horas);
            var caduca = DateTime.UtcNow.AddHours(horas);

            await audit.RecordAsync(AuditAction.ConfigChange, "Blob", blob,
                $"Enlace de descarga generado (vigencia {horas:0} h, caduca {caduca:dd/MM/yyyy HH:mm} UTC)", ct);

            return (true, $"Enlace generado. Caduca el {caduca.ToLocalTime():dd/MM/yyyy HH:mm}.",
                new EnlaceDeDescargaDto(uri.ToString(), caduca));
        }
        catch (InvalidOperationException ex)
        {
            return (false, ex.Message, null);
        }
        catch (Exception ex)
        {
            return (false, blobs.Explicar(ex), null);
        }
    }

    // ── Uso interno del servidor ────────────────────────────────────────────────

    /// <summary>
    /// Sube el ZIP del respaldo previo a un despliegue.
    ///
    /// <para><b>Sin guarda de autorización, a propósito.</b> Lo llama el respaldo previo, que corre
    /// dentro de un despliegue —y un despliegue programado no tiene ninguna persona detrás—. La
    /// autorización de ese despliegue ya se comprobó al agendarlo; volver a exigirla aquí haría que
    /// el respaldo fallara justo cuando corre solo, que es cuando más falta hace.</para>
    /// </summary>
    public async Task SubirRespaldoDeDespliegueAsync(
        string blob, Stream contenido, IDictionary<string, string> metadatos, CancellationToken ct = default)
        => await blobs.SubirAsync(await CredencialesAsync(ct), blob, contenido, metadatos, ct);

    /// <summary>
    /// ¿Borrar esta carpeta se llevaría una a donde escribe la aplicación?
    ///
    /// <para>Cuenta también cuando es una carpeta PADRE: borrar «prod» cuando las versiones están en
    /// «prod/releases» se lleva el histórico entero igual, y sin este aviso nadie lo vería venir.</para>
    /// </summary>
    private async Task<bool> EsCarpetaBaseAsync(string carpeta, CancellationToken ct)
    {
        string[] bases =
        [
            await CarpetaDeVersionesAsync(ct),
            await CarpetaDeRespaldosDeDespliegueAsync(ct),
            await CarpetaDeRespaldosDeBaseAsync(ct)
        ];

        return bases.Any(b =>
            b.Equals(carpeta, StringComparison.OrdinalIgnoreCase) ||
            b.StartsWith(carpeta + "/", StringComparison.OrdinalIgnoreCase));
    }
}
