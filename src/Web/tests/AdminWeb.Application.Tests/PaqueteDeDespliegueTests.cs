using AdminWeb.Infrastructure.Integraciones;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Cómo se interpreta lo guardado en <c>AppRelease.ZipBlobUrl</c>.
///
/// <para>Existe por un defecto que solo se habría visto el día del corte: esa columna la comparten
/// las dos aplicaciones sobre la MISMA base, y cada una la llenó de una forma distinta. El
/// escritorio guarda la URL completa del blob; la web guarda el nombre relativo. Como la web usaba
/// el valor tal cual, cualquier versión dada de alta desde el escritorio —o sea, todas las que ya
/// existen— habría hecho que buscara un blob llamado literalmente «https://…» y el despliegue
/// habría fallado sin dar ninguna pista de por qué.</para>
///
/// <para>Ninguna prueba anterior lo detectaba porque todas usan nombres de blob ya normalizados.</para>
/// </summary>
public class PaqueteDeDespliegueTests
{
    private const string Contenedor = "despliegues";

    [Fact]
    public void Un_nombre_de_blob_se_devuelve_tal_cual()
    {
        Assert.Equal("releases/AppX_1.2.3.zip",
            RutasDeBlob.NombreDeBlobDesde("releases/AppX_1.2.3.zip", Contenedor));
    }

    /// <summary>El caso que rompía: lo que escribió el escritorio.</summary>
    [Fact]
    public void Una_URL_del_escritorio_se_traduce_a_nombre_de_blob()
    {
        Assert.Equal("releases/AppX_1.2.3.zip",
            RutasDeBlob.NombreDeBlobDesde(
                "https://cuenta.blob.core.windows.net/despliegues/releases/AppX_1.2.3.zip", Contenedor));
    }

    [Fact]
    public void Sin_saber_el_contenedor_se_descarta_el_primer_segmento_igual()
    {
        // En una URL de Azure Blob el primer segmento SIEMPRE es el contenedor.
        Assert.Equal("releases/AppX_1.2.3.zip",
            RutasDeBlob.NombreDeBlobDesde(
                "https://cuenta.blob.core.windows.net/despliegues/releases/AppX_1.2.3.zip"));
    }

    [Fact]
    public void El_contenedor_no_se_repite_cuando_la_URL_ya_lo_lleva()
    {
        var nombre = RutasDeBlob.NombreDeBlobDesde(
            "https://cuenta.blob.core.windows.net/despliegues/releases/x.zip", Contenedor);

        Assert.DoesNotContain("despliegues/despliegues", nombre);
        Assert.StartsWith("releases/", nombre);
    }

    [Fact]
    public void Los_espacios_escapados_de_la_URL_se_deshacen()
    {
        // El escritorio guardaba la URL ya escapada; el nombre real del blob lleva el espacio.
        Assert.Equal("releases/App X.zip",
            RutasDeBlob.NombreDeBlobDesde(
                "https://cuenta.blob.core.windows.net/despliegues/releases/App%20X.zip", Contenedor));
    }

    [Fact]
    public void Un_contenedor_con_otro_nombre_tambien_se_recorta()
    {
        // La cuenta pudo cambiar de contenedor entre una versión y otra: el primer segmento sigue
        // siendo el contenedor aunque no coincida con el configurado hoy.
        Assert.Equal("releases/x.zip",
            RutasDeBlob.NombreDeBlobDesde(
                "https://cuenta.blob.core.windows.net/contenedor-viejo/releases/x.zip", Contenedor));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_valor_no_hay_nombre(string? valor)
    {
        Assert.Null(RutasDeBlob.NombreDeBlobDesde(valor, Contenedor));
    }

    [Fact]
    public void Una_ruta_de_una_sola_parte_se_conserva()
    {
        // Un blob en la raíz del contenedor: no hay segmento que quitar más allá del contenedor.
        Assert.Equal("x.zip",
            RutasDeBlob.NombreDeBlobDesde("https://cuenta.blob.core.windows.net/despliegues/x.zip", Contenedor));
    }
}
