using System.Reflection;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// La versión del ejecutable EN MARCHA.
///
/// Parece trivial y no lo es: la aplicación se reparte como .exe único autocontenido, y ahí
/// <c>Assembly.Location</c> viene VACÍO (FileVersionInfo sobre esa cadena lanza) y
/// <c>AppContext.BaseDirectory</c> apunta a la carpeta temporal de autoextracción, no al .exe. Lo
/// que sí viaja siempre son los ATRIBUTOS del ensamblado, así que de ahí se lee.
///
/// El otro detalle: desde .NET 8 el SDK le pega el hash de git a InformationalVersion
/// («1.0.0+f896b4d…»), y <c>Version.TryParse</c> falla con eso. Por eso se corta en el «+».
/// </summary>
public static class AppVersion
{
    private static readonly Lazy<Version> _actual = new(Calcular);

    /// <summary>Versión comparable del ejecutable en marcha. Nunca lanza; en el peor caso, 0.0.0.0.</summary>
    public static Version Actual => _actual.Value;

    /// <summary>Cómo mostrársela a una persona: «1.2.0» (sin el cuarto componente si es 0).</summary>
    public static string Texto => Mostrar(Actual);

    private static Version Calcular()
    {
        try
        {
            var asm = typeof(AppVersion).Assembly;
            return Resolver(
                asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version,
                asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                asm.GetName().Version);
        }
        catch { return new Version(0, 0, 0, 0); }
    }

    /// <summary>
    /// La resolución, pura: FileVersion primero (es la que fija el csproj y la que Windows muestra
    /// en las propiedades del archivo), luego la informativa ya limpia de hash, y por último la del
    /// nombre del ensamblado.
    /// </summary>
    internal static Version Resolver(string? fileVersion, string? informational, Version? ensamblado)
    {
        foreach (var candidata in new[] { Normalizar(fileVersion), Normalizar(informational) })
            if (candidata != null) return candidata;
        return ensamblado != null ? ACuatro(ensamblado) : new Version(0, 0, 0, 0);
    }

    /// <summary>
    /// Convierte un texto de versión a algo comparable, o null si no se puede. Tolera «v1.2.0»,
    /// espacios y el sufijo «+sha» que agrega el SDK.
    /// </summary>
    public static Version? Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var limpio = texto.Trim().TrimStart('v', 'V');

        // «1.0.0+f896b4d…» y «1.0.0-beta.1»: se corta en el primer sufijo.
        int corte = limpio.IndexOfAny(['+', '-', ' ']);
        if (corte > 0) limpio = limpio[..corte];

        return Version.TryParse(limpio, out var v) ? ACuatro(v) : null;
    }

    /// <summary>
    /// Rellena a cuatro componentes. Es OBLIGATORIO antes de comparar: los componentes no
    /// especificados valen -1, así que «1.2» resulta MENOR que «1.2.0» y publicar la versión sin
    /// el tercer número diría «no hay nada nuevo» por accidente.
    /// </summary>
    private static Version ACuatro(Version v) =>
        new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    /// <summary>Texto corto: se omite el cuarto componente cuando es 0, que es lo normal.</summary>
    public static string Mostrar(Version v) =>
        v.Revision > 0 ? v.ToString(4) : v.ToString(3);
}
