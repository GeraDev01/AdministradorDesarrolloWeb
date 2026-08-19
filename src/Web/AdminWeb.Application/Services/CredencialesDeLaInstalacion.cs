using AdminWeb.Infrastructure.Integraciones;

namespace AdminWeb.Application.Services;

/// <summary>
/// Las credenciales de Azure DevOps de la INSTALACIÓN: las que no son de nadie en concreto.
///
/// <para>Vive aparte porque la usan dos clases distintas y porque es la única forma de hablar con
/// Azure DevOps desde algo que corre <b>sin sesión</b> —un trabajo de fondo, un barrido—. El camino
/// normal mira primero el token personal de quien pide, y esa consulta lleva su propia guarda de
/// «hay que estar dentro»: desde un ámbito sin sesión lanza antes de llegar a ninguna parte.</para>
///
/// <para><b>Lo que se publique con esto queda firmado por la cuenta compartida</b>, no por la
/// persona. Es una consecuencia con la que hay que contar cuando se escribe el texto: si importa
/// quién hizo algo, el nombre tiene que ir dentro del mensaje, porque el autor que verá quien lo lea
/// en DevOps no lo dirá.</para>
/// </summary>
public static class CredencialesDeLaInstalacion
{
    public static async Task<(CredencialesDevOps? credenciales, string problema)> ObtenerAsync(
        SettingsService configuracion, CancellationToken ct = default)
    {
        var organizacion = (await configuracion.ObtenerAsync(SettingsService.Claves.AzureDevOpsOrgUrl, ct))?.TrimEnd('/');
        var proyecto = await configuracion.ObtenerAsync(SettingsService.Claves.AzureDevOpsProject, ct);
        var pat = await configuracion.ObtenerAsync(SettingsService.Claves.AzureDevOpsPat, ct);

        if (string.IsNullOrEmpty(organizacion) || string.IsNullOrEmpty(proyecto))
            return (null, "Falta la URL de organización o el proyecto de Azure DevOps.");

        if (string.IsNullOrEmpty(pat))
            return (null, "No hay token de Azure DevOps de la instalación, y sin sesión no hay otro " +
                          "que usar. Captúralo en Configuración para que lo automático pueda funcionar.");

        return (new CredencialesDevOps(organizacion, proyecto, pat), "");
    }
}
