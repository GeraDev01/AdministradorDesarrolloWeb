namespace Administrador_Desarrollo_Web.Forms;

/// <summary>
/// Hace que Enter escriba un renglón nuevo en los cuadros de varias líneas, en lugar de guardar.
///
/// EL PROBLEMA. Windows Forms decide a quién le toca la tecla Enter: si el control con el foco no la
/// reclama, se la queda el <see cref="Form.AcceptButton"/> del diálogo. Y un <see cref="TextBox"/>
/// solo la reclama cuando tiene <c>AcceptsReturn = true</c>, que <b>viene apagado por omisión</b>.
/// Como casi todos los diálogos de la aplicación tienen botón «Guardar» como AcceptButton, escribir
/// un changelog o unas notas y pulsar Enter guardaba y cerraba la ventana a media frase, en vez de
/// bajar de renglón. <see cref="RichTextBox"/> tiene el mismo problema y ni siquiera expone esa
/// propiedad.
///
/// LA SOLUCIÓN. Se aplica de una sola pasada sobre el árbol de controles, desde
/// <see cref="ResponsiveForm"/>, en lugar de recordar poner la propiedad en cada cuadro nuevo: eso
/// es justo lo que se olvidó en los cuarenta y tantos que ya existían.
///
/// LOS DE SOLO LECTURA SE SALTAN a propósito: ahí no se escribe nada, y que Enter siga cerrando el
/// visor con el botón «Cerrar» es lo que espera cualquiera.
/// </summary>
public static class EntradaDeTexto
{
    /// <summary>
    /// Recorre <paramref name="raiz"/> y sus descendientes dejando que los cuadros de varias líneas
    /// se queden con el Enter. Es idempotente: llamarla dos veces no duplica nada.
    /// </summary>
    public static void PermitirSaltoDeLinea(Control raiz)
    {
        foreach (var control in Descendientes(raiz))
        {
            switch (control)
            {
                // En un TextBox la propiedad se traduce al estilo ES_WANTRETURN de Windows, que es
                // exactamente lo que mira el diálogo para saber si la tecla es del cuadro.
                case TextBox { Multiline: true, ReadOnly: false } caja:
                    caja.AcceptsReturn = true;
                    break;

                // RichTextBox no tiene AcceptsReturn. La vía equivalente es reclamar la tecla en
                // PreviewKeyDown: marcarla como «de entrada» hace que el mensaje llegue al control
                // en vez de convertirse en la pulsación del botón por omisión.
                case RichTextBox { ReadOnly: false } rico:
                    rico.PreviewKeyDown -= ReclamarEnter;   // idempotente
                    rico.PreviewKeyDown += ReclamarEnter;
                    break;
            }
        }
    }

    /// <summary>Internal para poder probar la decisión sin levantar una ventana de verdad.</summary>
    internal static void ReclamarEnter(object? sender, PreviewKeyDownEventArgs e)
    {
        // Alt+Enter es de Windows (propiedades), no del cuadro.
        if (e.KeyCode == Keys.Enter && !e.Alt) e.IsInputKey = true;
    }

    private static IEnumerable<Control> Descendientes(Control raiz)
    {
        foreach (Control hijo in raiz.Controls)
        {
            yield return hijo;
            foreach (var nieto in Descendientes(hijo)) yield return nieto;
        }
    }
}
