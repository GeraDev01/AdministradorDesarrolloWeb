namespace Administrador_Desarrollo_Web.Services;

/// <summary>Lo que hay que enseñarle a la persona cuando sí toca avisar.</summary>
public sealed record AvisoVersion(Version Version, string VersionTexto, string? Url, string? Novedades);

/// <summary>
/// Decide si hay que avisar de una versión nueva y con qué datos. Todo estático y puro: sin base,
/// sin disco y sin UI, porque la parte que importa —comparar dos versiones sin equivocarse— es
/// exactamente la que se rompe en silencio.
///
/// La versión publicada vive en tres AppSettings y NO en una tabla nueva, por dos razones
/// concretas de este proyecto: AppSettings ya existe en todas las bases (una tabla nueva no se
/// crearía hasta que el administrador abriera la app, y mientras tanto cada desarrollador vería un
/// error al arrancar) y ya está blindada contra escritura para el login restringido — una tabla
/// nueva nacería modificable por cualquiera al heredar db_datawriter.
/// </summary>
public static class UpdateNotice
{
    /// <summary>Última versión publicada, p. ej. «1.2.0».</summary>
    public const string KeyLatestVersion = "LatestAppVersion";
    /// <summary>De dónde bajarla: http(s):// o una ruta de red \\servidor\compartido\….</summary>
    public const string KeyDownloadUrl = "LatestAppDownloadUrl";
    /// <summary>Qué trae de nuevo. Texto libre, multilínea.</summary>
    public const string KeyReleaseNotes = "LatestAppReleaseNotes";

    /// <summary>
    /// ¿Toca avisar? Devuelve null cuando no: no hay versión publicada, está mal capturada, no es
    /// mayor que la que corre, o ya se avisó de ESA misma versión en este equipo.
    /// </summary>
    /// <param name="publicada">Valor crudo del AppSetting.</param>
    /// <param name="enMarcha">La versión del ejecutable actual.</param>
    /// <param name="yaAvisada">La última versión de la que se avisó aquí, o null.</param>
    public static AvisoVersion? Evaluar(
        string? publicada, string? url, string? novedades, Version enMarcha, string? yaAvisada)
    {
        var nueva = AppVersion.Normalizar(publicada);
        // Mal capturada («próximamente», «1.2.x») se trata como si no hubiera: mejor callar que
        // avisar de una versión que nadie puede identificar.
        if (nueva == null) return null;

        if (nueva <= enMarcha) return null;

        // Ya se avisó de esta misma: no se repite en cada arranque. Si sale otra más nueva, sí.
        var avisada = AppVersion.Normalizar(yaAvisada);
        if (avisada != null && nueva <= avisada) return null;

        return new AvisoVersion(nueva, AppVersion.Mostrar(nueva),
            UrlValida(url) ? url!.Trim() : null,
            string.IsNullOrWhiteSpace(novedades) ? null : novedades.Trim());
    }

    /// <summary>
    /// Si el enlace es de un tipo que tiene sentido abrir. Se filtra ANTES de lanzarlo con el
    /// shell: sin esto, un AppSetting mal capturado se convierte en «que Windows abra lo que sea
    /// que diga ahí».
    /// </summary>
    public static bool UrlValida(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var u = url.Trim();

        // Ruta de red: el equipo reparte por carpeta compartida tan a menudo como por enlace.
        if (u.StartsWith(@"\\", StringComparison.Ordinal)) return u.Length > 2 && !u.Contains('\n');

        return Uri.TryCreate(u, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile);
    }
}
