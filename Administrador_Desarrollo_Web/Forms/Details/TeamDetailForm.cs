using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>Alta/edición de un equipo: nombre, descripción y color.
/// El líder se asigna con el rol "Líder" desde el tablero de equipos.</summary>
public class TeamDetailForm : Form
{
    private TextBox _txtName = null!;
    private TextBox _txtDesc = null!;
    private Panel _pnlColor = null!;
    private string _colorHex;

    public Team Result { get; private set; } = new();

    public TeamDetailForm(Team? team = null)
    {
        _colorHex = team?.ColorHex ?? "#2563EB";
        BuildUI();
        if (team != null) Populate(team);
    }

    private void BuildUI()
    {
        Text = "Equipo";
        Size = new Size(460, 500);
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
        hdr.Controls.Add(new Label { Text = "  👥  Equipo", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty, AutoScroll = true };
        const int x = 24, w = 400;
        int y = 14;

        body.Controls.Add(new Label { Text = "Nombre *", Location = new Point(x, y), AutoSize = true }); y += 22;
        _txtName = new TextBox { Location = new Point(x, y), Width = w }; body.Controls.Add(_txtName); y += 40;

        body.Controls.Add(new Label { Text = "Descripción", Location = new Point(x, y), AutoSize = true }); y += 22;
        _txtDesc = new TextBox { Location = new Point(x, y), Width = w, Height = 70, Multiline = true, ScrollBars = ScrollBars.Vertical }; body.Controls.Add(_txtDesc); y += 82;

        body.Controls.Add(new Label { Text = "Color del equipo", Location = new Point(x, y), AutoSize = true }); y += 22;
        _pnlColor = new Panel { Location = new Point(x, y), Size = new Size(60, 28), BorderStyle = BorderStyle.FixedSingle, BackColor = ParseHex(_colorHex) };
        var btnColor = AppTheme.MakeSecondaryButton("🎨 Cambiar color", 150); btnColor.Location = new Point(x + 70, y - 1); btnColor.Click += BtnColor_Click;
        body.Controls.AddRange([_pnlColor, btnColor]); y += 44;

        body.Controls.Add(new Label { Text = "El líder y los roles se asignan en el tablero (clic derecho en la tarjeta).", Location = new Point(x, y), AutoSize = false, Size = new Size(w, 34), ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont }); y += 44;
        body.AutoScrollMinSize = new Size(0, y);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnSave = AppTheme.MakePrimaryButton("Guardar", 110); btnSave.Click += BtnSave_Click;
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btns.Controls.AddRange([btnSave, btnCancel]);

        outer.Controls.Add(hdr,  0, 0);
        outer.Controls.Add(body, 0, 1);
        outer.Controls.Add(btns, 0, 2);
        Controls.Add(outer);
        AcceptButton = btnSave;
    }

    private void BtnColor_Click(object? s, EventArgs e)
    {
        using var dlg = new ColorDialog { Color = ParseHex(_colorHex), FullOpen = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _colorHex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
        _pnlColor.BackColor = dlg.Color;
    }

    private void Populate(Team t)
    {
        Result = t;
        _txtName.Text = t.Name;
        _txtDesc.Text = t.Description ?? "";
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text))
        { MessageBox.Show("El nombre del equipo es obligatorio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        Result.Name = _txtName.Text.Trim();
        Result.Description = string.IsNullOrWhiteSpace(_txtDesc.Text) ? null : _txtDesc.Text.Trim();
        Result.ColorHex = _colorHex;
        DialogResult = DialogResult.OK; Close();
    }

    private static Color ParseHex(string hex)
    {
        try { return ColorTranslator.FromHtml(hex); } catch { return AppTheme.SidebarActive; }
    }
}
