using System.Text.RegularExpressions;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// LA CONDICIÓN CON LA QUE SE ABRIÓ EL ORGANIGRAMA A LA EDICIÓN, comprobada y no solo escrita en un
/// comentario.
///
/// <para>El diagrama de «/equipos» fue de solo lectura a propósito durante un año, y el motivo está
/// en la cabecera de la pantalla: un diagrama editable daría <b>dos caminos</b> para mover a alguien
/// de equipo —la lista con botones, con su motivo, su rotación registrada y sus reglas de líder y de
/// rol, y el diagrama—, y dos caminos para la misma escritura acaban con uno de los dos olvidándose
/// de una regla. Se abrió quitando el segundo camino, no aceptándolo: la soltada llama al mismo
/// método que el botón.</para>
///
/// <para><b>Eso es exactamente lo que no se ve al revisar un cambio.</b> Un atajo que arme su propia
/// petición «porque desde ahí es más directo» compila, funciona el día que se escribe y se lee igual
/// de bien que el que llama al método común; lo que hace es dejar el historial de rotaciones
/// mintiendo el día que una de las reglas cambie en un sitio y no en el otro. Estas pruebas leen el
/// marcado y cuentan los sitios que escriben, para que ese atajo se ponga rojo el día que se
/// escriba.</para>
///
/// <para>Viven en el proyecto de la API porque es el único que ve <c>AdminWeb.Client</c>, igual que
/// <see cref="RecorridosGuiadosTests"/>, y por la misma razón se leen los archivos del proyecto y no
/// los compilados: lo que se vigila es cómo está escrita la pantalla.</para>
/// </summary>
public class CaminosDeEscrituraDeEquiposTests
{
    private static readonly string Pantalla =
        Path.Combine(RaizDelCliente(), "Paginas", "Catalogos", "Equipos.razor");

    private static string Marcado() => File.ReadAllText(Pantalla);

    [Fact]
    public void CAMBIAR_A_ALGUIEN_DE_EQUIPO_se_escribe_por_un_solo_sitio()
    {
        // Los tres botones de «Organización» y la soltada del diagrama son CUATRO gestos, y tienen
        // que ser UNA sola petición. Si este número sube, la pregunta no es cuál de los dos está
        // bien: es que el segundo camino ya existe.
        var marcado = Marcado();

        Assert.Equal(1, Cuantas(marcado, @"api/personas/equipos/integrantes"));
        Assert.Equal(1, Cuantas(marcado, @"new\s+MoverIntegrantesRequest"));
    }

    [Fact]
    public void LA_SOLTADA_DE_UNA_PERSONA_pasa_por_el_metodo_comun()
    {
        // No basta con que haya una sola petición: hay que ver que la soltada llegue hasta ella. Se
        // comprueba dentro del cuerpo de SoltarEn, que es el manejador de la soltada.
        var soltarEn = Cuerpo(Marcado(), "SoltarEn");

        Assert.Contains("MoverPersonasAsync", soltarEn);
        Assert.DoesNotContain("Enviar(", soltarEn);
    }

    [Fact]
    public void PASAR_DE_UN_EQUIPO_A_OTRO_tiene_boton_y_no_solo_arrastre()
    {
        // La otra mitad de la condición: lo que solo se pudiera arrastrar dejaría fuera a quien
        // navega con el teclado.
        //
        // Y «lo mismo» es el MISMO movimiento, no un resultado parecido. Con «Mover al equipo» —que
        // solo lee la lista de quien no tiene equipo— y «Quitar del equipo» —que siempre manda a
        // «sin equipo»—, llevar a alguien de un equipo a otro sin ratón eran dos operaciones, o sea
        // DOS rotaciones en el historial y una de ellas a «sin equipo», donde esa persona nunca
        // estuvo. Arrastrar habría quedado como la única forma de dejarlo bien escrito.
        var marcado = Marcado();

        Assert.Contains(@"Click=""@PasarAOtroEquipo""", marcado);
        Assert.Contains("MoverPersonasAsync", Cuerpo(marcado, "PasarAOtroEquipo"));
    }

    [Fact]
    public void LA_JERARQUIA_se_guarda_con_la_peticion_del_editor()
    {
        // Colgar un equipo de otro va a la misma dirección tanto si se hace arrastrando la cabecera
        // como si se elige en «Cuelga de». Son dos sitios que arman la petición —el editor y la
        // soltada— y esa petición GUARDA LO QUE TRAE: la de la soltada tiene que llevar el nombre,
        // la descripción y el color del equipo, o colgarlo los borraría.
        var colgar = Cuerpo(Marcado(), "ColgarEquipo");

        Assert.Contains(@"""api/personas/equipos""", colgar);
        foreach (var campo in new[] { "equipo.Id", "equipo.Nombre", "equipo.Descripcion", "equipo.ColorHex" })
            Assert.Contains(campo, colgar);
    }

    // ── Herramientas ─────────────────────────────────────────────────────────────

    private static int Cuantas(string texto, string patron) =>
        Regex.Matches(texto, patron).Count;

    /// <summary>
    /// El texto de un método, desde su DECLARACIÓN hasta lo siguiente que hay en el <c>@code</c>. No
    /// es un analizador de C# y no pretende serlo: alcanza para preguntar «¿este método nombra a
    /// aquel?», que es lo único que aquí hace falta.
    ///
    /// <para>Se busca la declaración y no el nombre a secas: en una pantalla, el primer sitio donde
    /// aparece «SoltarEn(» es el manejador escrito en el marcado, muchas líneas antes del método, y
    /// leer desde ahí devolvería marcado en vez de código.</para>
    /// </summary>
    private static string Cuerpo(string marcado, string metodo)
    {
        var declaracion = Regex.Match(
            marcado,
            $@"^\s*(?:private|protected|public|internal)[^\n(]*\b{Regex.Escape(metodo)}\s*\(",
            RegexOptions.Multiline);

        Assert.True(declaracion.Success,
            $"«{metodo}» ya no se declara en Equipos.razor. Si se renombró, esta prueba señala el " +
            "sitio que hay que volver a mirar; no la borres sin comprobar que la escritura sigue " +
            "siendo una sola.");

        // Hasta lo siguiente del @code: otro comentario de documentación, otro miembro o el
        // separador de sección. Así está ordenado el archivo.
        var desde = declaracion.Index + declaracion.Length;
        var siguiente = Regex.Match(
            marcado[desde..], @"\n    (?:///|// ──|private|protected|public|internal)");

        return marcado[declaracion.Index..(siguiente.Success ? desde + siguiente.Index : marcado.Length)];
    }

    /// <summary>
    /// Sube desde la carpeta de salida hasta encontrar el proyecto del cliente, igual que
    /// <see cref="RecorridosGuiadosTests"/>: por marca y no contando «..», que se rompe el día que
    /// cambie la estructura de carpetas de compilación.
    /// </summary>
    private static string RaizDelCliente()
    {
        const string relativa = "src/Web/AdminWeb.Client/AdminWeb.Client.csproj";

        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta != null)
        {
            var candidato = Path.Combine(carpeta.FullName, relativa);
            if (File.Exists(candidato)) return Path.GetDirectoryName(candidato)!;
            carpeta = carpeta.Parent;
        }

        throw new DirectoryNotFoundException(
            $"No se encontró «{relativa}» subiendo desde {AppContext.BaseDirectory}.");
    }
}
