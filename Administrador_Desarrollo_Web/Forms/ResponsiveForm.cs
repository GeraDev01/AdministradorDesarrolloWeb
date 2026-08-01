namespace Administrador_Desarrollo_Web.Forms;

/// <summary>Lo que hay que cambiarle a una ventana para que quepa en su pantalla.</summary>
/// <param name="Tamano">Tamaño final.</param>
/// <param name="Minimo">Mínimo final (aflojado si el declarado no cabía).</param>
/// <param name="Ubicacion">Esquina superior izquierda, ya metida dentro del área útil.</param>
/// <param name="LiberarBorde">Si el borde fijo debe volverse redimensionable.</param>
public readonly record struct AjusteVentana(Size Tamano, Size Minimo, Point Ubicacion, bool LiberarBorde);

/// <summary>
/// Ajusta una ventana a la pantalla en la que se abre.
///
/// La aplicación se escribió con diálogos de tamaño FIJO y controles colocados por coordenadas
/// absolutas. Eso funciona en el monitor donde se diseñó y se rompe en cualquier otro: en una
/// pantalla de portátil —o con Windows al 125 %— una ventana de 790 px de alto no cabe, y como
/// además es <c>FixedDialog</c>, no se puede ni redimensionar ni desplazar: los botones de
/// «Guardar» y «Cancelar» quedan literalmente fuera de la pantalla, sin forma de alcanzarlos.
///
/// Reordenar setenta formularios a <c>TableLayoutPanel</c> es otra tarea, y mucho más arriesgada.
/// Esto resuelve lo que de verdad deja a alguien atascado:
///  · nada queda fuera de alcance —si no cabe, aparece barra de desplazamiento—;
///  · una ventana recortada deja de ser fija, para que se pueda agrandar o maximizar;
///  · ninguna ventana nace más grande que el área útil ni fuera de ella.
/// </summary>
public static class ResponsiveLayout
{
    /// <summary>Margen que se deja libre para que el borde de la ventana no quede pegado.</summary>
    public const int Holgura = 16;

    /// <summary>Por debajo de esto no se encoge: una ventana de 100 px no le sirve a nadie.</summary>
    public const int AnchoMinimoAbsoluto = 320;
    public const int AltoMinimoAbsoluto  = 240;

    /// <summary>
    /// El cálculo, sin tocar la ventana: así se puede probar con pantallas que no existen en la
    /// máquina donde corren las pruebas.
    /// </summary>
    public static AjusteVentana Calcular(Size tamano, Size minimo, Point ubicacion, Rectangle area)
    {
        int maxAncho = Math.Max(AnchoMinimoAbsoluto, area.Width  - Holgura);
        int maxAlto  = Math.Max(AltoMinimoAbsoluto,  area.Height - Holgura);

        // El mínimo declarado por el formulario puede ser MAYOR que la pantalla; si no se afloja
        // primero, asignar Size no reduce nada y el ajuste no serviría de nada.
        var minFinal = new Size(Math.Min(minimo.Width, maxAncho), Math.Min(minimo.Height, maxAlto));

        bool recortada = tamano.Width > maxAncho || tamano.Height > maxAlto;
        var tamFinal = recortada
            ? new Size(Math.Min(tamano.Width, maxAncho), Math.Min(tamano.Height, maxAlto))
            : tamano;

        // Y que no nazca fuera del área útil: un CenterParent sobre una ventana maximizada en otro
        // monitor puede dejarla a medias fuera. El Max(area.Left/Top) va al final para que, si la
        // ventana es más ancha que la pantalla, gane el borde izquierdo y no el derecho.
        var x = Math.Max(area.Left, Math.Min(ubicacion.X, area.Right  - tamFinal.Width));
        var y = Math.Max(area.Top,  Math.Min(ubicacion.Y, area.Bottom - tamFinal.Height));

        return new AjusteVentana(tamFinal, minFinal, new Point(x, y), recortada);
    }

    /// <summary>Aplica el cálculo a una ventana real.</summary>
    public static void Ajustar(Form form)
    {
        if (form is null || form.IsDisposed) return;

        var area = Screen.FromControl(form).WorkingArea;
        var ajuste = Calcular(form.Size, form.MinimumSize, form.Location, area);

        // El mínimo se afloja SIEMPRE, incluso maximizada: la ventana principal nace maximizada,
        // pero en cuanto alguien la restaura vuelve a su mínimo, y si ese mínimo no cabe queda otra
        // vez con los bordes fuera de la pantalla.
        if (ajuste.Minimo != form.MinimumSize) form.MinimumSize = ajuste.Minimo;

        // Una maximizada ya ocupa exactamente el área útil: no hay más que ajustar.
        if (form.WindowState == FormWindowState.Maximized) return;

        if (ajuste.Tamano != form.Size) form.Size = ajuste.Tamano;

        // Fija y recortada es la peor combinación: no cabe y tampoco se puede agrandar.
        if (ajuste.LiberarBorde && form.FormBorderStyle is FormBorderStyle.FixedDialog
                                                        or FormBorderStyle.FixedSingle
                                                        or FormBorderStyle.Fixed3D)
        {
            form.FormBorderStyle = FormBorderStyle.Sizable;
            form.MaximizeBox = true;   // en pantallas pequeñas, maximizar suele ser la salida
        }

        // Con controles en coordenadas absolutas, esto es lo que evita que algo quede inalcanzable:
        // en vez de recortarse, aparece la barra de desplazamiento. Con un hijo Dock=Fill no hace
        // nada, así que es inofensivo en las pantallas que sí usan TableLayoutPanel.
        form.AutoScroll = true;

        if (ajuste.Ubicacion != form.Location) form.Location = ajuste.Ubicacion;
    }
}

/// <summary>
/// Base de los diálogos de la aplicación: aplica <see cref="ResponsiveLayout.Ajustar"/> al abrirse.
///
/// Va en <c>OnLoad</c> y no en el constructor a propósito: para entonces el formulario ya fijó su
/// tamaño y su borde en su propio <c>BuildUI</c>, así que hay algo real que ajustar.
/// </summary>
public class ResponsiveForm : Form
{
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try { ResponsiveLayout.Ajustar(this); }
        catch { /* un ajuste de tamaño jamás debe impedir que se abra la ventana */ }
    }
}
