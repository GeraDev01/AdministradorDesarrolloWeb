using System.Reflection;

namespace AdminWeb.Shared;

/// <summary>
/// Qué versión está corriendo.
///
/// <para>Existe porque al retirar Velopack la web dejó de decirlo en ninguna parte, y sin eso un
/// reporte de fallo no se puede situar: «a mí no me pasa» tanto puede significar que no pasa como
/// que quien lo dice tiene otra versión delante.</para>
///
/// <para>Se lee del ATRIBUTO del ensamblado y no de una constante escrita a mano. La diferencia
/// importa: una constante hay que acordarse de subirla en cada despliegue, y el día que se olvide
/// —que es siempre— la pantalla dirá un número falso, que es peor que no decir ninguno. El atributo
/// lo compone la compilación a partir de <c>Version</c> y del SHA que inyecta el pipeline.</para>
///
/// <para>Vive en Shared porque lo enseñan los dos lados: el pie del menú en el navegador y el
/// <c>/api/health</c> del servidor. Cada uno lee el suyo, y que coincidan es justamente lo que
/// confirma que el cliente que está en el navegador es el que sirvió esa API.</para>
/// </summary>
public static class VersionDeLaAplicacion
{
    /// <summary>
    /// La versión completa, «1.0.0+a305d69» cuando la compiló el pipeline y «1.0.0» cuando la
    /// compiló alguien en su máquina.
    /// </summary>
    public static string Completa { get; } = Leer();

    /// <summary>Solo la parte legible, sin el SHA. Es lo que se dice en voz alta.</summary>
    public static string Corta => Completa.Split('+')[0];

    /// <summary>Los siete primeros del SHA, o null si no lo compiló el pipeline.</summary>
    public static string? Commit
    {
        get
        {
            var partes = Completa.Split('+');
            return partes.Length < 2 || partes[1].Length == 0
                ? null
                : partes[1][..Math.Min(7, partes[1].Length)];
        }
    }

    private static string Leer()
    {
        var ensamblado = Assembly.GetEntryAssembly() ?? typeof(VersionDeLaAplicacion).Assembly;

        // InformationalVersion es el único que lleva el SHA; Version a secas lo pierde.
        var informativa = ensamblado
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informativa)) return informativa;

        // Sin atributo —no debería pasar, pero no vale la pena reventar por esto— se cae a la
        // versión del ensamblado antes que a un texto inventado.
        return ensamblado.GetName().Version?.ToString(3) ?? "desconocida";
    }
}
