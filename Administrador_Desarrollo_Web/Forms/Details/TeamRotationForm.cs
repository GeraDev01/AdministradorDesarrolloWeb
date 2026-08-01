using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Rotación de desarrolladores entre equipos: elige uno o varios desarrolladores
/// y el equipo destino (o "Sin equipo"). El control aplica los movimientos y los
/// registra en el historial de rotaciones.
/// </summary>
public class TeamRotationForm : ResponsiveForm
{
    private readonly List<(int Id, string Label)> _devs;
    private readonly List<Team> _teams;

    private CheckedListBox _clbDevs = null!;
    private ComboBox _cbxTarget = null!;
    private TextBox _txtNote = null!;

    public List<int> DevIds { get; private set; } = [];
    public int? TargetTeamId { get; private set; }
    public string? Note { get; private set; }

    public TeamRotationForm(AppDbContext db, int? preselectDevId = null)
    {
        _teams = db.Teams.OrderBy(t => t.Name).ToList();
        var teamName = _teams.ToDictionary(t => t.Id, t => t.Name);
        _devs = db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName)
            .Select(d => new { d.Id, d.FullName, d.TeamId })
            .AsEnumerable()
            .Select(d => (d.Id, Label: $"{d.FullName}   —   [{(d.TeamId != null && teamName.ContainsKey(d.TeamId.Value) ? teamName[d.TeamId.Value] : "Sin equipo")}]"))
            .ToList();
        BuildUI(preselectDevId);
    }

    private void BuildUI(int? preselectDevId)
    {
        Text = "Rotación entre equipos";
        Size = new Size(500, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

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
        hdr.Controls.Add(new Label { Text = "  🔄  Rotación entre equipos", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 14, 24, 8), Margin = Padding.Empty };
        int y = 4;

        body.Controls.Add(new Label { Text = "Desarrolladores a rotar *  (marca uno o varios)", Location = new Point(0, y), AutoSize = true }); y += 22;
        _clbDevs = new CheckedListBox { Location = new Point(0, y), Width = 440, Height = 210, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle };
        foreach (var d in _devs) _clbDevs.Items.Add(d.Label);
        if (preselectDevId != null)
        {
            int idx = _devs.FindIndex(d => d.Id == preselectDevId.Value);
            if (idx >= 0) _clbDevs.SetItemChecked(idx, true);
        }
        body.Controls.Add(_clbDevs); y += 222;

        body.Controls.Add(new Label { Text = "Equipo destino *", Location = new Point(0, y), AutoSize = true }); y += 22;
        _cbxTarget = new ComboBox { Location = new Point(0, y), Width = 440, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxTarget.Items.Add("(Sin equipo)");
        foreach (var t in _teams) _cbxTarget.Items.Add(t.Name);
        _cbxTarget.SelectedIndex = 0;
        body.Controls.Add(_cbxTarget); y += 40;

        body.Controls.Add(new Label { Text = "Nota (opcional):", Location = new Point(0, y), AutoSize = true }); y += 22;
        _txtNote = new TextBox { Location = new Point(0, y), Width = 440 };
        body.Controls.Add(_txtNote);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnOk = AppTheme.MakePrimaryButton("Rotar", 110); btnOk.Click += BtnOk_Click;
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btns.Controls.AddRange([btnOk, btnCancel]);

        outer.Controls.Add(hdr,  0, 0);
        outer.Controls.Add(body, 0, 1);
        outer.Controls.Add(btns, 0, 2);
        Controls.Add(outer);
        AcceptButton = btnOk;
    }

    private void BtnOk_Click(object? s, EventArgs e)
    {
        var idx = _clbDevs.CheckedIndices.Cast<int>().ToList();
        if (idx.Count == 0) { MessageBox.Show("Marca al menos un desarrollador.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        DevIds = idx.Select(i => _devs[i].Id).ToList();
        TargetTeamId = _cbxTarget.SelectedIndex > 0 ? _teams[_cbxTarget.SelectedIndex - 1].Id : null;
        Note = string.IsNullOrWhiteSpace(_txtNote.Text) ? null : _txtNote.Text.Trim();
        DialogResult = DialogResult.OK; Close();
    }
}
