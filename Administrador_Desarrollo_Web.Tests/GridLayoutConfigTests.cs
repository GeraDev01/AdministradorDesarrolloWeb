using Administrador_Desarrollo_Web.Data;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Las columnas que cada quien esconde en una lista. Es preferencia de PANTALLA y vive en el equipo
/// de la persona: si estuviera en la base compartida, esconder una columna se la escondería a todo
/// el equipo.
///
/// Se guarda lo OCULTO, no lo visible, y estas pruebas lo fijan: es lo que hace que una columna
/// nueva aparezca sola en la siguiente versión en vez de nacer invisible para quien ya había
/// configurado esa lista.
/// </summary>
public class GridLayoutConfigTests : IDisposable
{
    private readonly string _ruta =
        Path.Combine(Path.GetTempPath(), "advtest_cols_" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        try { if (File.Exists(_ruta)) File.Delete(_ruta); } catch { }
    }

    [Fact]
    public void SinArchivo_NoEsconde_Nada()
    {
        var cfg = new GridLayoutConfig(_ruta);
        Assert.Empty(cfg.Ocultas("ticket-links.vinculos"));
    }

    [Fact]
    public void LoQueSeEscondeSobreviveAlReinicio()
    {
        new GridLayoutConfig(_ruta).Guardar("ticket-links.vinculos", ["Notes", "LinkedBy"]);

        // Otra instancia = la aplicación abierta de nuevo.
        var recargada = new GridLayoutConfig(_ruta);
        Assert.Equal(["Notes", "LinkedBy"], recargada.Ocultas("ticket-links.vinculos"));
    }

    [Fact]
    public void CadaListaSeAcuerdaDeLoSuyo()
    {
        var cfg = new GridLayoutConfig(_ruta);
        cfg.Guardar("ticket-links.vinculos", ["Notes"]);
        cfg.Guardar("ticket-links.devops", ["AssignedTo"]);

        var recargada = new GridLayoutConfig(_ruta);
        Assert.Equal(["Notes"], recargada.Ocultas("ticket-links.vinculos"));
        Assert.Equal(["AssignedTo"], recargada.Ocultas("ticket-links.devops"));
        Assert.Empty(recargada.Ocultas("ticket-links.freshdesk"));
    }

    [Fact]
    public void VolverAMostrarlasTodas_DejaLaListaLimpia()
    {
        var cfg = new GridLayoutConfig(_ruta);
        cfg.Guardar("vinculos", ["Notes", "LinkedBy"]);
        cfg.Guardar("vinculos", []);   // el usuario pulsó «Mostrar todas»

        Assert.Empty(cfg.Ocultas("vinculos"));
        Assert.Empty(new GridLayoutConfig(_ruta).Ocultas("vinculos"));
    }

    [Fact]
    public void SeLimpianDuplicadosYEspacios()
    {
        var cfg = new GridLayoutConfig(_ruta);
        cfg.Guardar("vinculos", ["  Notes  ", "Notes", "", "   ", "LinkedBy"]);

        Assert.Equal(["Notes", "LinkedBy"], cfg.Ocultas("vinculos"));
    }

    [Fact]
    public void UnaColumnaNueva_NoNaceInvisible()
    {
        // Quien ya había escondido «Notes» actualiza a una versión con una columna nueva: como se
        // guarda lo oculto y no lo visible, la nueva no está en la lista y por tanto se ve.
        new GridLayoutConfig(_ruta).Guardar("vinculos", ["Notes"]);

        var ocultas = new GridLayoutConfig(_ruta).Ocultas("vinculos");
        Assert.DoesNotContain("ColumnaNuevaDeLaSiguienteVersion", ocultas);
    }

    [Fact]
    public void ArchivoCorrupto_SeIgnoraSinReventar()
    {
        File.WriteAllText(_ruta, "{ esto no es json válido ");

        var cfg = new GridLayoutConfig(_ruta);

        Assert.Empty(cfg.Ocultas("vinculos"));
        // Y sigue sirviendo: se puede volver a guardar encima.
        cfg.Guardar("vinculos", ["Notes"]);
        Assert.Equal(["Notes"], new GridLayoutConfig(_ruta).Ocultas("vinculos"));
    }

    [Fact]
    public void RutaEnCarpetaInexistente_SeCrea()
    {
        var anidada = Path.Combine(Path.GetTempPath(), "advtest_" + Guid.NewGuid().ToString("N"), "columnas.json");
        try
        {
            new GridLayoutConfig(anidada).Guardar("vinculos", ["Notes"]);
            Assert.True(File.Exists(anidada));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(anidada)!, recursive: true); } catch { }
        }
    }
}
