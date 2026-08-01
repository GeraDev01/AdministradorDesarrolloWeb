using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Instancia única. Lo que se prueba aquí no es «que no abra dos veces» sino los dos casos que, mal
/// resueltos, dejan la aplicación inservible: que la segunda ejecución consiga despertar a la
/// primera (si no, con la ventana en la bandeja parecería que el programa no arranca), y que un
/// cierre —limpio o no— libere el paso (si no, nadie podría volver a abrirla hasta reiniciar).
///
/// Cada prueba usa un nombre propio para no chocar con otra que corra en paralelo ni con la
/// aplicación de verdad abierta en este equipo.
/// </summary>
public class SingleInstanceTests
{
    private static string NombreUnico([System.Runtime.CompilerServices.CallerMemberName] string caso = "") =>
        $"ADWTest.{caso}.{Guid.NewGuid():N}";

    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(10);

    [Fact]
    public void LaPrimeraManda_LaSegundaNo()
    {
        var nombre = NombreUnico();

        using var primera = SingleInstance.Adquirir(nombre);
        using var segunda = SingleInstance.Adquirir(nombre);

        Assert.True(primera.EsPrimera);
        Assert.False(segunda.EsPrimera);
    }

    [Fact]
    public void DentroDelMismoProceso_TambienSeDetecta()
    {
        // No es un caso rebuscado: es lo que hace que la prueba de arriba valga. Un mutex es
        // reentrante para el hilo que ya lo posee, así que resolverlo con WaitOne(0) diría «soy la
        // primera» las dos veces y la guarda no serviría de nada.
        var nombre = NombreUnico();

        using var primera = SingleInstance.Adquirir(nombre);
        using var segunda = SingleInstance.Adquirir(nombre);
        using var tercera = SingleInstance.Adquirir(nombre);

        Assert.True(primera.EsPrimera);
        Assert.False(segunda.EsPrimera);
        Assert.False(tercera.EsPrimera);
    }

    [Fact]
    public void NombresDistintos_NoSeEstorban()
    {
        using var una  = SingleInstance.Adquirir(NombreUnico());
        using var otra = SingleInstance.Adquirir(NombreUnico());

        Assert.True(una.EsPrimera);
        Assert.True(otra.EsPrimera);
    }

    [Fact]
    public void LaSegunda_DespiertaALaPrimera()
    {
        // El caso importante: sin esto, con la ventana escondida en la bandeja alguien haría doble
        // clic al ejecutable y no pasaría absolutamente nada.
        var nombre = NombreUnico();
        using var primera = SingleInstance.Adquirir(nombre);

        using var llamaron = new ManualResetEventSlim(false);
        primera.OtraInstanciaLlamo += () => llamaron.Set();

        using (var segunda = SingleInstance.Adquirir(nombre))
        {
            Assert.False(segunda.EsPrimera);
            segunda.PedirQueSeMuestre();
        }

        Assert.True(llamaron.Wait(Espera), "La primera instancia no se enteró de que otra intentó abrirse.");
    }

    [Fact]
    public void LaEscuchaSeRearma_UnaSegundaLlamadaMasTardeTambienDespierta()
    {
        // Lo que importa no es cuántos despertares llegan, sino que la escucha siga viva: quien
        // cierra la ventana con la X por la mañana y vuelve a abrir el ejecutable por la tarde tiene
        // que ver la aplicación otra vez.
        //
        // NO se comprueba que tres llamadas seguidas produzcan tres despertares: el evento es de
        // auto-reset y no lleva contador, así que dos Set() muy juntos se funden en uno. Es el
        // comportamiento correcto —tres dobles clics seguidos deben traer la ventana al frente, no
        // hacerlo tres veces— y afirmar lo contrario hacía que esta prueba fallara a ratos.
        var nombre = NombreUnico();
        using var primera = SingleInstance.Adquirir(nombre);

        using var despertada = new ManualResetEventSlim(false);
        primera.OtraInstanciaLlamo += () => despertada.Set();

        for (int intento = 1; intento <= 3; intento++)
        {
            despertada.Reset();
            using (var otra = SingleInstance.Adquirir(nombre))
                otra.PedirQueSeMuestre();

            Assert.True(despertada.Wait(Espera), $"La llamada #{intento} no despertó a la instancia viva.");
        }
    }

    [Fact]
    public void LaPrimera_NoSeLlamaASiMisma()
    {
        // Si se despertara sola, la ventana saltaría al frente sin que nadie la haya pedido.
        var nombre = NombreUnico();
        using var primera = SingleInstance.Adquirir(nombre);

        using var llamaron = new ManualResetEventSlim(false);
        primera.OtraInstanciaLlamo += () => llamaron.Set();

        primera.PedirQueSeMuestre();

        Assert.False(llamaron.Wait(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public void AlCerrarse_DejaPasarALaSiguiente()
    {
        // Cerrar la aplicación y volver a abrirla es lo más normal del mundo; si el paso no se
        // liberara, habría que reiniciar Windows para poder entrar otra vez.
        var nombre = NombreUnico();

        var primera = SingleInstance.Adquirir(nombre);
        Assert.True(primera.EsPrimera);
        primera.Dispose();

        using var siguiente = SingleInstance.Adquirir(nombre);
        Assert.True(siguiente.EsPrimera);
    }

    [Fact]
    public void DisposeDosVeces_NoRevienta()
    {
        var instancia = SingleInstance.Adquirir(NombreUnico());
        instancia.Dispose();
        instancia.Dispose();   // el `using` de Main puede coincidir con un cierre ya hecho
    }

    [Fact]
    public void PedirQueSeMuestre_TrasCerrar_NoRevienta()
    {
        var nombre = NombreUnico();
        using var primera = SingleInstance.Adquirir(nombre);

        var segunda = SingleInstance.Adquirir(nombre);
        segunda.Dispose();
        segunda.PedirQueSeMuestre();   // la otra ya no está: no hay nada que despertar, pero no truena
    }
}
