using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Data;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class LeaveRequestDetailForm : ResponsiveForm
{
    private readonly List<Developer> _devs;
    private ComboBox _cbxDev = null!;
    private ComboBox _cbxType = null!;
    private DateTimePicker _dtpDate = null!;
    private NumericUpDown _nudDays = null!;
    private TextBox _txtReason = null!;
    private TextBox _txtApprovedBy = null!;
    private TextBox _txtNotes = null!;

    public LeaveRequest Result { get; private set; } = new();

    public LeaveRequestDetailForm(AppDbContext db, LeaveRequest? leave = null)
    {
        _devs = [.. db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName)];
        BuildUI();
        if (leave != null) Populate(leave);
    }

    private void BuildUI()
    {
        Text = "Registro de Permiso"; Size = new Size(480, 500);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  📋  Registro de Permiso", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty };
        int y = 15;

        // Desarrollador
        body.Controls.Add(new Label { Text = "Desarrollador *", Location = new Point(25, y), AutoSize = true });
        _cbxDev = new ComboBox { Location = new Point(25, y + 20), Width = 420, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var d in _devs) _cbxDev.Items.Add(d.FullName);
        if (_cbxDev.Items.Count > 0) _cbxDev.SelectedIndex = 0;
        body.Controls.Add(_cbxDev);
        y += 56;

        // Tipo | Días
        body.Controls.Add(new Label { Text = "Tipo de permiso *", Location = new Point(25, y), AutoSize = true });
        _cbxType = new ComboBox { Location = new Point(25, y + 20), Width = 270, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var t in Enum.GetValues<LeaveType>()) _cbxType.Items.Add(TypeLabel(t));
        _cbxType.SelectedIndex = 0;
        body.Controls.Add(_cbxType);

        body.Controls.Add(new Label { Text = "Días", Location = new Point(315, y), AutoSize = true });
        _nudDays = new NumericUpDown { Location = new Point(315, y + 20), Width = 80, Minimum = 1, Maximum = 365, Value = 1 };
        body.Controls.Add(_nudDays);
        y += 56;

        // Fecha
        body.Controls.Add(new Label { Text = "Fecha de inicio", Location = new Point(25, y), AutoSize = true });
        _dtpDate = new DateTimePicker { Location = new Point(25, y + 20), Width = 175, Format = DateTimePickerFormat.Short };
        body.Controls.Add(_dtpDate);
        y += 56;

        // Motivo
        body.Controls.Add(new Label { Text = "Motivo / Descripción", Location = new Point(25, y), AutoSize = true });
        _txtReason = new TextBox { Location = new Point(25, y + 20), Width = 420, Height = 55, Multiline = true, ScrollBars = ScrollBars.Vertical };
        body.Controls.Add(_txtReason);
        y += 75;

        // Aprobado por
        body.Controls.Add(new Label { Text = "Aprobado / Autorizado por", Location = new Point(25, y), AutoSize = true });
        _txtApprovedBy = new TextBox { Location = new Point(25, y + 20), Width = 420 };
        body.Controls.Add(_txtApprovedBy);
        y += 56;

        // Notas
        body.Controls.Add(new Label { Text = "Notas adicionales", Location = new Point(25, y), AutoSize = true });
        _txtNotes = new TextBox { Location = new Point(25, y + 20), Width = 420, Height = 44, Multiline = true };
        body.Controls.Add(_txtNotes);

        var pnlBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10), BackColor = AppTheme.ContentBg
        };
        var btnSave = AppTheme.MakePrimaryButton("Guardar", 100);
        btnSave.Click += BtnSave_Click;
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        pnlBtns.Controls.AddRange([btnSave, btnCancel]);

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.Controls.Add(hdr,     0, 0);
        outer.Controls.Add(body,    0, 1);
        outer.Controls.Add(pnlBtns, 0, 2);
        Controls.Add(outer);
        AcceptButton = btnSave;
    }

    private void Populate(LeaveRequest l)
    {
        Result = l;
        var idx = _devs.FindIndex(d => d.Id == l.DeveloperId);
        _cbxDev.SelectedIndex = idx >= 0 ? idx : 0;
        _cbxType.SelectedIndex = (int)l.Type;
        _dtpDate.Value = l.Date;
        _nudDays.Value = Math.Clamp(l.DaysCount, 1, 365);
        _txtReason.Text = l.Reason ?? "";
        _txtApprovedBy.Text = l.ApprovedBy ?? "";
        _txtNotes.Text = l.Notes ?? "";
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (_cbxDev.SelectedIndex < 0)
        { MessageBox.Show("Selecciona un desarrollador.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        Result.DeveloperId = _devs[_cbxDev.SelectedIndex].Id;
        Result.Type = (LeaveType)_cbxType.SelectedIndex;
        Result.Date = _dtpDate.Value.Date;
        Result.DaysCount = (int)_nudDays.Value;
        Result.Reason = string.IsNullOrWhiteSpace(_txtReason.Text) ? null : _txtReason.Text.Trim();
        Result.ApprovedBy = string.IsNullOrWhiteSpace(_txtApprovedBy.Text) ? null : _txtApprovedBy.Text.Trim();
        Result.Notes = string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }

    internal static string TypeLabel(LeaveType t) => t switch
    {
        LeaveType.PermisoPersonal => "🙋 Permiso personal",
        LeaveType.Incapacidad     => "🏥 Incapacidad",
        LeaveType.CitaMedica      => "🩺 Cita médica",
        LeaveType.AsuntoFamiliar  => "👨‍👩‍👧 Asunto familiar",
        LeaveType.Capacitacion    => "📚 Capacitación",
        _                         => "📋 Otro"
    };
}
