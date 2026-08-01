namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Vigencia del enlace temporal de descarga (SAS). Mismo criterio que la herramienta Blobup que ya
/// usa el equipo: horas, con 24 por omisión.
/// </summary>
public class SasOptionsForm : ResponsiveForm
{
    private NumericUpDown _numHoras = null!;
    public double Horas { get; private set; } = 24;

    public SasOptionsForm(string blobName)
    {
        BuildUI(blobName);
    }

    private void BuildUI(string blobName)
    {
        Text = "Generar enlace de descarga";
        Size = new Size(520, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg };
        hdr.Controls.Add(new Label
        {
            Text = "  🔗  Enlace temporal de descarga", Dock = DockStyle.Fill,
            ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        var body = new Panel { Dock = DockStyle.Fill };
        int y = 14;
        body.Controls.Add(new Label
        {
            Text = blobName, Location = new Point(22, y), AutoSize = false, Size = new Size(460, 20),
            Font = AppTheme.BoldFont, AutoEllipsis = true
        });
        y += 30;

        body.Controls.Add(new Label { Text = "Vigencia (horas):", Location = new Point(22, y), AutoSize = true });
        y += 22;
        _numHoras = new NumericUpDown
        {
            Location = new Point(22, y), Width = 110,
            Minimum = 1, Maximum = 8760, Value = 24, DecimalPlaces = 0
        };
        body.Controls.Add(_numHoras);

        var flow = new FlowLayoutPanel { Location = new Point(145, y - 2), Size = new Size(340, 32), FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        foreach (var (texto, horas) in new[] { ("1 h", 1), ("24 h", 24), ("7 días", 168), ("30 días", 720) })
        {
            var b = AppTheme.MakeSecondaryButton(texto, 74, 26);
            b.Margin = new Padding(0, 0, 4, 0);
            int h = horas;
            b.Click += (_, _) => _numHoras.Value = h;
            flow.Controls.Add(b);
        }
        body.Controls.Add(flow);
        y += 44;

        body.Controls.Add(new Label
        {
            Text = "Cualquiera con el enlace podrá descargar el archivo completo hasta que caduque,\n" +
                   "sin necesidad de credenciales. Compártelo solo con quien deba tenerlo y usa la\n" +
                   "vigencia más corta que sirva.",
            Location = new Point(22, y), AutoSize = false, Size = new Size(460, 56),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.Warning
        });

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 8), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnOk = AppTheme.MakePrimaryButton("Generar enlace", 150);
        btnOk.Click += (_, _) => { Horas = (double)_numHoras.Value; DialogResult = DialogResult.OK; Close(); };
        btns.Controls.AddRange([btnCancel, btnOk]);

        tbl.Controls.Add(hdr, 0, 0);
        tbl.Controls.Add(body, 0, 1);
        tbl.Controls.Add(btns, 0, 2);
        Controls.Add(tbl);
        AcceptButton = btnOk;
    }
}
