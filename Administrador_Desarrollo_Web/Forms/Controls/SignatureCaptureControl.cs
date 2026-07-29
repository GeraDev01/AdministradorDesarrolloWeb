using System.Drawing.Drawing2D;
using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Lienzo para dibujar una firma con el mouse/pluma. Exporta un PNG transparente
/// recortado al trazo. 100% GDI+, sin dependencias.
/// </summary>
public class SignatureCaptureControl : Panel
{
    private readonly List<List<PointF>> _strokes = new();
    private List<PointF>? _current;
    private readonly Color _ink = Color.FromArgb(20, 24, 40);
    private const float PenWidth = 2.6f;

    public bool IsEmpty => _strokes.Count == 0;

    public SignatureCaptureControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        BorderStyle = BorderStyle.FixedSingle;
        Cursor = Cursors.Cross;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _current = new List<PointF> { e.Location };
        _strokes.Add(_current);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_current == null || e.Button != MouseButtons.Left) return;
        _current.Add(e.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _current = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // Línea guía de firma
        using (var guide = new Pen(Color.FromArgb(210, 216, 224)) { DashStyle = DashStyle.Dash })
            e.Graphics.DrawLine(guide, 20, Height - 24, Width - 20, Height - 24);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_ink, PenWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        DrawStrokes(e.Graphics, pen);
    }

    private void DrawStrokes(Graphics g, Pen pen)
    {
        foreach (var stroke in _strokes)
        {
            if (stroke.Count == 1)
                g.FillEllipse(new SolidBrush(pen.Color), stroke[0].X - PenWidth / 2, stroke[0].Y - PenWidth / 2, PenWidth, PenWidth);
            else if (stroke.Count > 1)
                g.DrawLines(pen, stroke.ToArray());
        }
    }

    public void Clear()
    {
        _strokes.Clear();
        _current = null;
        Invalidate();
    }

    /// <summary>
    /// Exporta la firma como PNG transparente recortado. Devuelve null si está vacía.
    /// </summary>
    public (byte[] png, int width, int height)? ExportPng()
    {
        if (IsEmpty) return null;
        using var layer = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(layer))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(_ink, PenWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            DrawStrokes(g, pen);
        }
        using var cropped = SignatureImaging.AutoCrop(layer);
        if (cropped == null) return null;
        return (SignatureImaging.ToPng(cropped), cropped.Width, cropped.Height);
    }
}
