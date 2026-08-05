using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// El checklist previo al despliegue. Lo que se prueba es la parte que sostiene la garantía: que
/// no se puede desplegar con puntos sin marcar, y que la evidencia que queda escrita dice quién
/// confirmó qué — incluidos los hechos que NO dependen de que alguien los marque.
/// </summary>
public class DeploymentChecklistTests
{
    private static readonly DateTime Cuando = new(2026, 8, 2, 22, 15, 0);
    private static string[] TodasLasClaves => DeploymentChecklist.Puntos.Select(p => p.Clave).ToArray();

    [Fact]
    public void SinMarcarNada_FaltanTodos()
    {
        Assert.Equal(DeploymentChecklist.Puntos.Length, DeploymentChecklist.Faltantes([]).Count);
    }

    [Fact]
    public void ConTodosMarcados_NoFaltaNinguno()
    {
        Assert.Empty(DeploymentChecklist.Faltantes(TodasLasClaves));
    }

    [Fact]
    public void FaltaUno_LoIdentifica()
    {
        var sinReversion = TodasLasClaves.Where(c => c != "reversion").ToArray();

        var faltan = DeploymentChecklist.Faltantes(sinReversion);

        Assert.Equal("reversion", Assert.Single(faltan).Clave);
    }

    [Fact]
    public void CadaPunto_TieneClaveUnica_TextoYAyuda()
    {
        // Las claves quedan escritas en la evidencia: duplicarlas la haría ambigua.
        Assert.Equal(DeploymentChecklist.Puntos.Length, DeploymentChecklist.Puntos.Select(p => p.Clave).Distinct().Count());
        Assert.All(DeploymentChecklist.Puntos, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Texto));
            Assert.False(string.IsNullOrWhiteSpace(p.Ayuda));   // un checklist que no explica se marca sin leer
        });
    }

    [Fact]
    public void LaEvidencia_DiceQuienCuandoQueYADonde()
    {
        var texto = DeploymentChecklist.Evidencia(
            "Gerardo Manjarrez", Cuando, "Portal v2.4.1", "3 servidor(es) seleccionados", "todos",
            TodasLasClaves, nota: "Autorizado en el ticket #4412");

        Assert.Contains("Gerardo Manjarrez", texto);
        Assert.Contains("02/08/2026 22:15", texto);
        Assert.Contains("Portal v2.4.1", texto);
        Assert.Contains("3 servidor(es) seleccionados", texto);
        Assert.Contains("Respaldo previo: todos", texto);
        Assert.Contains("#4412", texto);
    }

    [Fact]
    public void LaEvidencia_DistingueLoMarcadoDeLoNoMarcado()
    {
        // El caso que importa para una auditoría: tiene que poder registrar que algo NO se
        // confirmó. Si solo supiera escribir «todo bien», no sería evidencia de nada.
        var soloUno = new[] { "version" };

        var texto = DeploymentChecklist.Evidencia(
            "Ana", Cuando, "v1", "1 servidor", "ninguno", soloUno, null);

        Assert.Contains("[x] Verifiqué que la versión es la correcta", texto);
        Assert.Contains("[ ] Sé cómo revertir si algo sale mal", texto);
    }

    [Fact]
    public void LaEvidencia_SinNota_NoDejaLaEtiquetaVacia()
    {
        // La nota es obligatoria en el diálogo, pero el redactor sigue aceptándola vacía: tiene que
        // poder escribir la evidencia de los despliegues anteriores a esa regla sin inventar una.
        var texto = DeploymentChecklist.Evidencia("Ana", Cuando, "v1", "1 servidor", "todos", TodasLasClaves, null);
        Assert.DoesNotContain("Nota:", texto);
    }

    [Fact]
    public void LaEvidencia_RegistraElRespaldoAunqueSeaNinguno()
    {
        // El respaldo sale del sistema, no de la buena fe: es el dato que se pide primero cuando
        // un despliegue sale mal.
        var texto = DeploymentChecklist.Evidencia("Ana", Cuando, "v1", "1 servidor", "ninguno", TodasLasClaves, null);
        Assert.Contains("Respaldo previo: ninguno", texto);
    }

    // ── La nota, que es obligatoria en todos los despliegues ─────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t ")]
    public void SinNota_NoAlcanza(string? nota)
    {
        Assert.False(DeploymentChecklist.NotaSuficiente(nota));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(" x ")]
    public void NotaDeRelleno_NoAlcanza(string nota)
    {
        // El caso que la regla viene a impedir: la tecla que se pulsa para que el botón se encienda.
        Assert.False(DeploymentChecklist.NotaSuficiente(nota));
    }

    [Theory]
    [InlineData("CAB-233")]
    [InlineData("#44")]
    [InlineData("  Ticket 4412: lo pidió Soporte  ")]
    public void NotaConUnaReferenciaReal_Alcanza(string nota)
    {
        // Una referencia corta es una justificación completa: el mínimo no mide redacción.
        Assert.True(DeploymentChecklist.NotaSuficiente(nota));
    }

    [Fact]
    public void LaNotaSeMideSinEspacios()
    {
        // Si el trim no se aplicara, tres espacios pasarían el mínimo y la regla no protegería nada.
        Assert.Equal(3, DeploymentChecklist.MinimoNota);
        Assert.False(DeploymentChecklist.NotaSuficiente(new string(' ', DeploymentChecklist.MinimoNota)));
    }
}
