using System.Diagnostics;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Pinta el cuerpo de una entrada del foro con sus enlaces pulsables.
///
/// El texto se guarda en llano: los enlaces se reconocen al pintar (<see cref="ForumRichText"/>) y
/// no en una tabla aparte. Así buscar en el foro sigue encontrando la dirección dentro del cuerpo,
/// y una entrada vieja gana los enlaces sin migrar nada.
///
/// Al pasar el ratón, el tooltip enseña <b>la dirección de verdad</b>. Importa cuando el enlace
/// lleva etiqueta —<c>[el ticket](https://…)</c>—, porque ahí lo que se lee y lo que se abre no
/// tienen por qué coincidir, y quien lee merece poder comprobarlo antes de pulsar.
/// </summary>
public static class CuerpoForo
{
    /// <summary>
    /// Un control con el texto ya formateado. Devuelve un Label normal cuando no hay ningún enlace:
    /// no vale la pena montar un LinkLabel para pintar un párrafo suelto.
    /// </summary>
    public static Control Crear(string texto, int anchoMaximo, Font fuente, Color color)
    {
        var segmentos = ForumRichText.Analizar(texto);

        if (!segmentos.Any(s => s.EsEnlace))
            return new Label
            {
                Text = texto, AutoSize = true, MaximumSize = new Size(anchoMaximo, 0),
                Font = fuente, ForeColor = color
            };

        var lbl = new EtiquetaConEnlaces
        {
            Text = string.Concat(segmentos.Select(s => s.Texto)),
            AutoSize = true, MaximumSize = new Size(anchoMaximo, 0),
            Font = fuente, ForeColor = color,
            LinkColor = AppTheme.SidebarActive,
            ActiveLinkColor = AppTheme.HeaderBg,
            VisitedLinkColor = AppTheme.SidebarActive,
            LinkBehavior = LinkBehavior.HoverUnderline,
            // Sin esto, un «&» del texto se comería la letra siguiente y la pintaría subrayada.
            UseMnemonic = false
        };

        int inicio = 0;
        foreach (var s in segmentos)
        {
            if (s.EsEnlace) lbl.Links.Add(inicio, s.Texto.Length, s.Url);
            inicio += s.Texto.Length;
        }

        var tip = new ToolTip { InitialDelay = 350, ReshowDelay = 100 };
        string? mostrado = null;
        lbl.MouseMove += (_, e) =>
        {
            var destino = lbl.EnlaceEn(e.X, e.Y)?.LinkData as string;
            if (destino == mostrado) return;   // sin esto el tooltip parpadearía en cada píxel
            mostrado = destino;
            if (destino == null) tip.SetToolTip(lbl, "");
            else tip.SetToolTip(lbl, $"{destino}\n(clic para abrirlo en el navegador)");
        };

        lbl.LinkClicked += (_, e) => Abrir(e.Link?.LinkData as string, lbl.FindForm());
        lbl.Disposed += (_, _) => tip.Dispose();
        return lbl;
    }

    /// <summary>
    /// Abre la dirección en el navegador. Se vuelve a validar aquí, y no solo al reconocerla: esto
    /// acaba en <c>Process.Start</c>, así que es el punto donde un esquema que no sea http/https
    /// convertiría una publicación cualquiera en un lanzador de programas.
    /// </summary>
    public static void Abrir(string? url, IWin32Window? owner)
    {
        if (!ForumRichText.EsEnlaceSeguro(url, out var destino))
        {
            MessageBox.Show("Ese enlace no es una dirección web válida, así que no se abre.",
                "Enlace", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try { Process.Start(new ProcessStartInfo(destino) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir el enlace:\n{ex.Message}", "Enlace", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Un LinkLabel que deja preguntar qué enlace hay bajo el ratón. <c>PointInLink</c> es protegido
    /// en el control original, y sin él no se puede enseñar el destino de CADA enlace: el tooltip
    /// tendría que ser uno solo para todo el párrafo, que es justo lo que no sirve cuando hay
    /// varios enlaces con etiqueta.
    /// </summary>
    private sealed class EtiquetaConEnlaces : LinkLabel
    {
        public Link? EnlaceEn(int x, int y) => PointInLink(x, y);
    }
}
