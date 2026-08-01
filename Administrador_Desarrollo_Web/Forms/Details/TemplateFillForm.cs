using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Pide los valores de los marcadores <c>{{así}}</c> de una plantilla y muestra en vivo cómo va
/// quedando el texto. Lo que sale de aquí (<see cref="Texto"/>) es lo que se copia o se guarda.
///
/// Un marcador que se deje vacío se queda escrito tal cual en el resultado: es preferible que salte
/// a la vista un <c>{{cliente}}</c> sin llenar a mandarle al cliente un hueco silencioso.
/// </summary>
public class TemplateFillForm : ResponsiveForm
{
    private readonly TemplateService _templates;
    private readonly string _cuerpo;
    private readonly List<(string nombre, TextBox caja)> _campos = [];
    private TextBox _preview = null!;

    /// <summary>El texto con los marcadores ya sustituidos.</summary>
    public string Texto { get; private set; } = "";

    public TemplateFillForm(TemplateService templates, Template plantilla, List<string> marcadores)
    {
        _templates = templates;
        _cuerpo = plantilla.Body;
        BuildUI(plantilla, marcadores);
        Recalcular();
    }

    private void BuildUI(Template plantilla, List<string> marcadores)
    {
        Text = $"Rellenar — {plantilla.Title}";
        Size = new Size(920, 640);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label
        {
            Text = $"  ✏  Completa los datos  ({marcadores.Count} por rellenar)",
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont,
            TextAlign = ContentAlignment.MiddleLeft
        });

        // Izquierda: un campo por marcador. Derecha: cómo va quedando.
        var cuerpo = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Padding = new Padding(12, 8, 12, 4), BackColor = AppTheme.ContentBg
        };
        cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330f));
        cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        cuerpo.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var campos = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, BackColor = AppTheme.ContentBg, Margin = new Padding(0, 0, 10, 0)
        };
        foreach (var nombre in marcadores)
        {
            campos.Controls.Add(new Label
            {
                Text = nombre, AutoSize = true, Font = AppTheme.BoldFont,
                Margin = new Padding(0, 6, 0, 2)
            });
            var caja = new TextBox { Width = 295, Margin = new Padding(0, 0, 0, 4) };
            caja.TextChanged += (_, _) => Recalcular();
            campos.Controls.Add(caja);
            _campos.Add((nombre, caja));
        }

        var pnlPreview = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        pnlPreview.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        pnlPreview.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        pnlPreview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        pnlPreview.Controls.Add(new Label { Text = "Así quedará:", Dock = DockStyle.Fill, ForeColor = AppTheme.TextSecondary }, 0, 0);

        _preview = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = false,
            Font = AppTheme.MonoFont, BackColor = Color.White
        };
        pnlPreview.Controls.Add(_preview, 0, 1);

        cuerpo.Controls.Add(campos, 0, 0);
        cuerpo.Controls.Add(pnlPreview, 1, 0);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancelar = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnAceptar = AppTheme.MakePrimaryButton("Usar este texto", 150);
        btnAceptar.Click += (_, _) => { Recalcular(); DialogResult = DialogResult.OK; Close(); };
        btns.Controls.AddRange([btnCancelar, btnAceptar]);

        tbl.Controls.Add(hdr,    0, 0);
        tbl.Controls.Add(cuerpo, 0, 1);
        tbl.Controls.Add(btns,   0, 2);
        Controls.Add(tbl);

        AcceptButton = btnAceptar;
        CancelButton = btnCancelar;
        if (_campos.Count > 0) ActiveControl = _campos[0].caja;
    }

    private void Recalcular()
    {
        var valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (nombre, caja) in _campos)
        {
            var v = caja.Text.Trim();
            if (v.Length > 0) valores[nombre] = v;   // vacío = no sustituir, el marcador queda visible
        }

        Texto = _templates.Rellenar(_cuerpo, valores);
        _preview.Text = Texto.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
    }
}
