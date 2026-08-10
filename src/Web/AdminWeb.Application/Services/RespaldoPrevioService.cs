using System.IO.Compression;
using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Integraciones;

namespace AdminWeb.Application.Services;

/// <summary>
/// Respalda la carpeta remota de un servidor ANTES de sobrescribirla, y sube el ZIP al mismo Azure
/// Blob Storage ya configurado, bajo la carpeta de respaldos de despliegue.
///
/// <para><b>Es lo que permite revertir.</b> Sin esto, el despliegue sobrescribe el destino sin
/// conservar copia de lo que había y un despliegue malo no tiene vuelta atrás.</para>
///
/// <para><b>Regla deliberada que se conserva del escritorio: si el respaldo falla, NO se despliega.</b>
/// Un respaldo que «se intentó» no sirve de nada el día que hay que revertir, y desplegar creyendo
/// que hay red de seguridad es peor que desplegar sabiendo que no la hay. Quien llama debe dejar que
/// la excepción suba.</para>
///
/// <para><b>Lo que NO se porta es el respaldo de la BASE DE DATOS</b> (<c>BackupService</c> y
/// <c>AutoBackupService</c> del escritorio). Azure SQL trae respaldo continuo y restauración a un
/// punto en el tiempo; replicar encima un respaldo manual sería mantener una copia peor de algo que
/// ya existe. Este servicio respalda carpetas remotas, que es otra cosa y sí hace falta.</para>
/// </summary>
public class RespaldoPrevioService(
    SettingsService configuracion,
    AlmacenamientoService almacenamiento,
    IDescargaDeCarpetaRemota descarga)
{
    /// <summary>
    /// Permite al líder desactivar el respaldo previo si asume el riesgo. Lo que no diga
    /// explícitamente «false» cuenta como activado: el valor seguro es el que protege.
    /// </summary>
    public async Task<bool> EstaHabilitadoAsync(CancellationToken ct = default) =>
        !string.Equals(
            await configuracion.ObtenerAsync(SettingsService.Claves.DeployBackupEnabled, ct),
            "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Respalda la carpeta remota del servidor y sube el ZIP.
    /// </summary>
    /// <param name="contrasenaEnClaro">
    /// Ya descifrada. La descifra quien despliega, que es quien tiene el servidor delante; pasarla
    /// cifrada aquí obligaría a este servicio a conocer el esquema de cifrado sin ninguna ganancia.
    /// </param>
    /// <param name="despliegueId">Identificador del despliegue, para poder ubicar el respaldo después.</param>
    /// <returns>
    /// El NOMBRE del blob del respaldo, o null si no había nada que respaldar (carpeta remota vacía o
    /// inexistente: es el primer despliegue a ese servidor) o si el respaldo está desactivado.
    ///
    /// <para>Cambia respecto al escritorio, que devolvía la URL absoluta del blob. El nombre relativo
    /// sirve para lo mismo —volver a encontrarlo— y no lleva el nombre de la cuenta de almacenamiento
    /// pegado a cada línea del registro del despliegue.</para>
    /// </returns>
    public async Task<string?> RespaldarAsync(
        DeploymentTarget servidor, string contrasenaEnClaro, int despliegueId,
        IProgress<string> avance, CancellationToken ct = default)
    {
        if (!await EstaHabilitadoAsync(ct))
        {
            avance.Report("  ⚠  Respaldo previo DESACTIVADO en configuración: se despliega sin red de seguridad.");
            return null;
        }

        if (!await almacenamiento.EstaConfiguradoAsync(ct))
            throw new InvalidOperationException(
                "No se puede respaldar antes de desplegar: falta configurar Azure Blob Storage. " +
                "Configúralo, o desactiva el respaldo previo si asumes el riesgo.");

        avance.Report($"  💾  Respaldando {servidor.RutaRemota} antes de sobrescribir…");

        var carpetaTemporal = Path.Combine(Path.GetTempPath(), $"respaldo_{despliegueId}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(carpetaTemporal);
        var zip = Path.Combine(Path.GetTempPath(),
            $"{Sanear(servidor.Nombre)}_{DateTime.UtcNow:yyyy-MM-dd_HHmmss}.zip");

        try
        {
            var credenciales = new CredencialesDeCarpetaRemota(
                servidor.Host, servidor.Puerto, servidor.Usuario, contrasenaEnClaro, servidor.RutaRemota);

            int archivos = await descarga.DescargarAsync(credenciales, carpetaTemporal, avance, ct);
            if (archivos == 0) return null;

            await Task.Run(() => ZipFile.CreateFromDirectory(carpetaTemporal, zip, CompressionLevel.Optimal, false), ct);
            long tamano = new FileInfo(zip).Length;

            var blob = $"{await almacenamiento.CarpetaDeRespaldosDeDespliegueAsync(ct)}/" +
                       $"{Sanear(servidor.Nombre)}/despliegue{despliegueId}_{DateTime.UtcNow:yyyy-MM-dd_HHmmss}.zip";

            // Metadatos para poder identificar el respaldo sin abrirlo el día que haya que revertir.
            var metadatos = new Dictionary<string, string>
            {
                ["servidor"] = Sanear(servidor.Nombre),
                ["ruta_remota"] = servidor.RutaRemota,
                ["despliegue"] = despliegueId.ToString(),
                ["archivos"] = archivos.ToString(),
                ["respaldado_utc"] = DateTime.UtcNow.ToString("O")
            };

            await using (var lectura = File.OpenRead(zip))
                await almacenamiento.SubirRespaldoDeDespliegueAsync(blob, lectura, metadatos, ct);

            avance.Report($"  ✓  Respaldo subido: {archivos} archivos, {tamano / 1024d / 1024d:0.0} MB → {blob}");
            return blob;
        }
        finally
        {
            // El servidor puede desplegar muchas veces al día: dejar los temporales sin borrar llenaría
            // el disco de la aplicación con copias de carpetas remotas enteras.
            try { Directory.Delete(carpetaTemporal, recursive: true); } catch { }
            try { if (File.Exists(zip)) File.Delete(zip); } catch { }
        }
    }

    /// <summary>Deja el nombre del servidor utilizable como parte de una ruta de blob.</summary>
    private static string Sanear(string nombre)
    {
        var limpio = new string([.. nombre.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_')]);
        return limpio.Length == 0 ? "servidor" : limpio;
    }
}
