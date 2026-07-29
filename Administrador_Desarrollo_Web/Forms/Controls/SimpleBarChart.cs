using System.Drawing.Drawing2D;
using Administrador_Desarrollo_Web.Forms;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>Gráfica de barras horizontales dibujada con GDI+ (sin dependencias externas).</summary>
public class SimpleBarChart : Panel
{
    private string _title = "";
    private List<(string Label, double Value)> _data = [];

    private static readonly Color[] Palette =
    [
        Color.FromArgb(37, 99, 235), Color.FromArgb(34, 197, 94), Color.FromArgb(234, 88, 12),
        Color.FromArgb(139, 92, 246), Color.FromArgb(20, 184, 166), Color.FromArgb(239, 68, 68),
        Color.FromArgb(245, 158, 11), Color.FromArgb(219, 39, 119)
    ];

    public SimpleBarChart()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
    }

    public void SetData(string title, List<(string Label, double Value)> data)
    {
        _title = title;
        _data = data ?? [];
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        int pad = 14;
        using var titleFont = new Font("Segoe UI Semibold", 11f);
        g.DrawString(_title, titleFont, new SolidBrush(AppTheme.TextPrimary), pad, 10);

        int top = 42;
        if (_data.Count == 0)
        {
            g.DrawString("(sin datos para graficar)", AppTheme.DefaultFont, new SolidBrush(AppTheme.TextSecondary), pad, top);
            return;
        }

        int labelW = 170;
        int valueW = 70;
        int barLeft = pad + labelW;
        int barRight = Width - pad - valueW;
        int barAreaW = Math.Max(40, barRight - barLeft);

        double maxAbs = Math.Max(1, _data.Max(d => Math.Abs(d.Value)));
        int available = Height - top - pad;
        int rowH = Math.Clamp(available / _data.Count, 16, 40);

        var lblFont = AppTheme.DefaultFont; // fuente compartida: NO hacer dispose
        using var valFont = new Font("Segoe UI Semibold", 9f);
        using var axisPen = new Pen(AppTheme.Border);

        for (int i = 0; i < _data.Count; i++)
        {
            var (label, value) = _data[i];
            int y = top + i * rowH;
            int barH = Math.Max(8, rowH - 8);
            int cy = y + (rowH - barH) / 2;

            // etiqueta (truncada)
            string lbl = label.Length > 24 ? label[..24] + "…" : label;
            var lblRect = new RectangleF(pad, y, labelW - 6, rowH);
            g.DrawString(lbl, lblFont, new SolidBrush(AppTheme.TextPrimary), lblRect,
                new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });

            // barra
            int w = (int)(Math.Abs(value) / maxAbs * barAreaW);
            var color = value < 0 ? AppTheme.Danger : Palette[i % Palette.Length];
            using (var brush = new SolidBrush(color))
                g.FillRectangle(brush, barLeft, cy, Math.Max(2, w), barH);

            // valor
            string valTxt = value == Math.Floor(value) ? ((long)value).ToString() : value.ToString("0.#");
            g.DrawString(valTxt, valFont, new SolidBrush(AppTheme.TextPrimary),
                new RectangleF(barLeft + w + 6, y, valueW, rowH),
                new StringFormat { LineAlignment = StringAlignment.Center });
        }

        g.DrawLine(axisPen, barLeft, top, barLeft, top + _data.Count * rowH);
    }

    protected override void OnResize(EventArgs eventargs) { base.OnResize(eventargs); Invalidate(); }
}
