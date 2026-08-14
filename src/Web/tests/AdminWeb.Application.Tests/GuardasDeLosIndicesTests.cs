using System.Text.RegularExpressions;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// LAS GUARDAS DE LOS ÍNDICES DE SQL SERVER, leídas del propio migrador.
///
/// <para><b>Por qué existe esta prueba.</b> <c>ExecIndex</c> decide si tiene que crear un índice
/// preguntando «¿hay ya uno que EMPIECE por esta columna?», y esa columna se le pasa aparte de la
/// lista de columnas del índice. Si las dos no coinciden, la guarda busca un índice distinto del que
/// crea, y entonces pasa una de dos cosas, las dos malas: el índice se intenta crear en CADA arranque
/// y falla con «ya existe», dejando encendido el aviso de ESQUEMA INCOMPLETO para siempre —y con él
/// deja de servir para avisar de lo que sí importa—; o bien, si otro índice de la tabla empieza por
/// la columna declarada, el nuestro no se crea nunca y nadie se entera.</para>
///
/// <para>Pasó de verdad, con <c>IX_Pool_Equipo</c>: se declaró «EquipoId» y el índice es
/// <c>(Status, EquipoId)</c>. Salió en el registro de producción al segundo despliegue, no al
/// primero, porque hasta que el índice no existe la sentencia funciona.</para>
///
/// <para><b>Se lee el ARCHIVO y no se ejecuta nada</b>, que es lo único que permite comprobarlo: la
/// rama de SQL Server no la ejerce ninguna prueba —no hay un SQL Server que levantar— y su modo de
/// fallo es silencioso por diseño, porque el migrador se traga la excepción y la apunta.</para>
/// </summary>
public class GuardasDeLosIndicesTests
{
    private static readonly string Migrador = File.ReadAllText(RutaDelMigrador());

    private static string RutaDelMigrador()
    {
        const string relativa = "src/Web/AdminWeb.Infrastructure/Data/DatabaseMigrator.cs";

        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta != null)
        {
            var candidato = Path.Combine(carpeta.FullName, relativa);
            if (File.Exists(candidato)) return candidato;
            carpeta = carpeta.Parent;
        }

        throw new FileNotFoundException($"No se encontró «{relativa}» subiendo desde {AppContext.BaseDirectory}.");
    }

    /// <summary>Cada llamada a las dos ayudas de índices, con lo que declara y lo que crea.</summary>
    private static IEnumerable<(string Ayuda, string Tabla, string Indice, string Declarada, string Columnas)> Llamadas()
    {
        var patron = new Regex(
            @"\b(ExecIndex|ExecIndiceUnico)\(\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*""([^""]+)""\s*\)");

        foreach (Match m in patron.Matches(Migrador))
            yield return (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value,
                          m.Groups[4].Value, m.Groups[5].Value);
    }

    /// <summary>El cinturón contra el falso verde: si el patrón dejara de encontrar llamadas, la
    /// prueba de abajo pasaría sola sin comprobar ninguna.</summary>
    [Fact]
    public void HAY_LLAMADAS_QUE_MIRAR()
    {
        Assert.True(Llamadas().Count() > 20,
            "No se están encontrando las llamadas a ExecIndex; la comprobación de abajo no vale nada.");
    }

    /// <summary>
    /// <b>La columna que se declara tiene que ser la PRIMERA del índice.</b> Es el contrato de
    /// <c>ExecIndex</c>, y romperlo no da error de compilación ni falla ninguna prueba: falla en
    /// producción, en el registro, y solo a partir del segundo despliegue.
    /// </summary>
    [Fact]
    public void LA_COLUMNA_DECLARADA_ES_LA_PRIMERA_DEL_INDICE()
    {
        var desajustadas = Llamadas()
            .Select(l => new
            {
                l.Indice,
                l.Tabla,
                l.Declarada,
                Real = l.Columnas.Split(',')[0].Trim().Trim('[', ']')
            })
            .Where(x => x.Real != x.Declarada)
            .Select(x => $"{x.Indice} sobre {x.Tabla}: declara «{x.Declarada}» y su primera columna " +
                         $"es «{x.Real}»")
            .ToList();

        Assert.True(desajustadas.Count == 0,
            "Estas guardas buscan un índice distinto del que crean. O el índice se intentará crear en "
            + "cada arranque y dejará el aviso de esquema incompleto encendido, o no se creará nunca:\n  "
            + string.Join("\n  ", desajustadas));
    }

    /// <summary>
    /// Y el otro lado de lo mismo: un índice comprobado POR NOMBRE no puede llamarse igual que uno que
    /// EF cree desde el modelo, porque entonces se duplicaría.
    ///
    /// <para>Los cuatro que hay están justificados en su comentario, y ninguno está en el modelo. La
    /// lista va escrita para que el día que aparezca un quinto alguien tenga que justificarlo también:
    /// preguntar por nombre es lo correcto cuando EF no va a crear ese índice, y lo peor que se puede
    /// hacer cuando sí.</para>
    /// </summary>
    [Fact]
    public void LOS_COMPROBADOS_POR_NOMBRE_SIGUEN_SIENDO_LOS_CONOCIDOS()
    {
        var porNombre = new Regex(@"sys\.indexes WHERE name = '([^']+)'")
            .Matches(Migrador)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(
            ["IX_DescRol_EquipoRol", "IX_Festivo_Fecha", "IX_Pool_Equipo", "UX_PoolMatrix"],
            porNombre);
    }
}
