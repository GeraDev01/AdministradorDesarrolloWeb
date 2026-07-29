using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Forms.Controls;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Diálogo para dibujar una firma nueva con el mouse y nombrarla.
/// Devuelve el PNG transparente ya recortado.
/// </summary>
public class SignatureCaptureForm : Form
{
    private SignatureCaptureControl _pad = null!;
    private TextBox _txtName = null!;
    private CheckBox _chkDefault = null!;

    public byte[] Png { get; private set; } = [];
    public int PngWidth { get; private set; }
    public int PngHeight { get; private set; }
    public string DisplayName => _txtName.Text.Trim();
    public bool IsDefault => _chkDefault.Checked;

    public SignatureCaptureForm(string? suggestedName = null)
    {
        BuildUI();
        if (suggestedName != null) _txtName.Text = suggestedName;
    }

    private void BuildUI()
    {
        Text = "Dibujar firma";
        Size = new Size(560, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  ✍  Dibujar firma", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        // Cuerpo: nombre + opciones + lienzo
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3,
            Padding = new Padding(16, 12, 16, 8), Margin = Padding.Empty
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110f));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));

        body.Controls.Add(new Label { Text = "Nombre:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _txtName = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 8, 3) };
        body.Controls.Add(_txtName, 1, 0);
        var btnClear = AppTheme.MakeSecondaryButton("🧹 Limpiar", 100);
        btnClear.Margin = new Padding(0, 1, 0, 1);
        btnClear.Click += (_, _) => _pad.Clear();
        body.Controls.Add(btnClear, 2, 0);

        _pad = new SignatureCaptureControl { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
        body.Controls.Add(_pad, 0, 1);
        body.SetColumnSpan(_pad, 3);

        _chkDefault = new CheckBox { Text = "Marcar como firma predeterminada", Dock = DockStyle.Fill, Checked = true };
        body.Controls.Add(_chkDefault, 0, 2);
        body.SetColumnSpan(_chkDefault, 3);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnSave = AppTheme.MakePrimaryButton("Guardar", 110);
        btnSave.Click += BtnSave_Click;
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btns.Controls.AddRange([btnSave, btnCancel]);

        outer.Controls.Add(hdr,  0, 0);
        outer.Controls.Add(body, 0, 1);
        outer.Controls.Add(btns, 0, 2);
        Controls.Add(outer);
        AcceptButton = btnSave;
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text))
        { MessageBox.Show("Ponle un nombre a la firma.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        var result = _pad.ExportPng();
        if (result == null)
        { MessageBox.Show("Dibuja la firma antes de guardar.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        Png = result.Value.png;
        PngWidth = result.Value.width;
        PngHeight = result.Value.height;
        DialogResult = DialogResult.OK;
        Close();
    }
}
