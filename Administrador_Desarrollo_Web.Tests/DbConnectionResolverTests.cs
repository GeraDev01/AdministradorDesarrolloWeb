using Administrador_Desarrollo_Web.Data;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Precedencia de la conexión: configuración local &gt; incrustada en el ejecutable &gt; SQLite.
/// Es la regla que decide contra qué base trabaja la app, y equivocarse aquí es justo lo que
/// produce el "sigo viendo los datos viejos".
/// </summary>
public class DbConnectionResolverTests
{
    private const string Local     = "Data Source=local;Initial Catalog=LOCAL";
    private const string Incrustada = "Data Source=incrustada;Initial Catalog=EMBED";

    /// <summary>DbProviderConfig solo se construye desde archivo, así que se refleja para las pruebas.</summary>
    private static DbProviderConfig Cfg(DbProvider provider, string? conn = null, string? error = null, bool explicita = false)
    {
        var c = new DbProviderConfig { Provider = provider, SqlServerConnection = conn ?? "" };
        typeof(DbProviderConfig).GetProperty(nameof(DbProviderConfig.LoadError))!.SetValue(c, error);
        typeof(DbProviderConfig).GetProperty(nameof(DbProviderConfig.ConfiguracionExplicita))!.SetValue(c, explicita);
        return c;
    }

    [Fact]
    public void LaConfiguracionLocalGanaSobreLaIncrustada()
    {
        var r = DbConnectionResolver.Resolve(Cfg(DbProvider.SqlServer, Local, explicita: true), Incrustada);

        Assert.Equal(DbConnectionSource.ConfiguracionLocal, r.Source);
        Assert.Equal(Local, r.SqlServerConnection);
        Assert.True(r.UsaSqlServer);
    }

    [Fact]
    public void SinConfiguracionLocalSeUsaLaIncrustada()
    {
        // Equipo recién instalado: no existe dbprovider.json.
        var r = DbConnectionResolver.Resolve(Cfg(DbProvider.Sqlite), Incrustada);

        Assert.Equal(DbConnectionSource.Incrustada, r.Source);
        Assert.Equal(Incrustada, r.SqlServerConnection);
        Assert.True(r.UsaSqlServer);
    }

    [Fact]
    public void SinNadaSeCaeASqlite()
    {
        var r = DbConnectionResolver.Resolve(Cfg(DbProvider.Sqlite), embedded: null);

        Assert.Equal(DbConnectionSource.SqliteLocal, r.Source);
        Assert.False(r.UsaSqlServer);
    }

    [Fact]
    public void SiElUsuarioEligioSqliteAProposito_NoSeLeImponeLaIncrustada()
    {
        var r = DbConnectionResolver.Resolve(Cfg(DbProvider.Sqlite, explicita: true), Incrustada);

        Assert.Equal(DbConnectionSource.SqliteLocal, r.Source);
        Assert.False(r.UsaSqlServer);
    }

    [Fact]
    public void UnaConfiguracionLocalRotaNoSeTraga_YSeCaeALaIncrustada()
    {
        // DPAPI no pudo descifrar: Load() degrada a Sqlite y deja LoadError.
        var rota = Cfg(DbProvider.Sqlite, error: "no se pudo descifrar", explicita: true);

        var r = DbConnectionResolver.Resolve(rota, Incrustada);

        // El aviso debe llegar al usuario pase lo que pase.
        Assert.Equal("no se pudo descifrar", r.Warning);
        // Y como la elección de Sqlite no fue deliberada sino consecuencia del error,
        // la incrustada sirve de red de seguridad en vez de dejarlo en la base local.
        Assert.Equal(DbConnectionSource.Incrustada, r.Source);
    }

    [Fact]
    public void UnaConfiguracionLocalDeSqlServerSinCadenaNoSeUsa()
    {
        var r = DbConnectionResolver.Resolve(Cfg(DbProvider.SqlServer, conn: "", explicita: true), Incrustada);

        Assert.Equal(DbConnectionSource.Incrustada, r.Source);
    }

    [Fact]
    public void LaDescripcionExplicaElOrigen()
    {
        Assert.Contains("este equipo", DbConnectionResolver.Resolve(Cfg(DbProvider.SqlServer, Local, explicita: true), null).Descripcion);
        Assert.Contains("incrustada", DbConnectionResolver.Resolve(Cfg(DbProvider.Sqlite), Incrustada).Descripcion);
        Assert.Contains("SQLite", DbConnectionResolver.Resolve(Cfg(DbProvider.Sqlite), null).Descripcion);
    }
}
