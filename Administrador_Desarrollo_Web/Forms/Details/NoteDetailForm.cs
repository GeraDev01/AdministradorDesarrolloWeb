using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Data;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class NoteDetailForm : Form
{
    private readonly List<Developer> _devs;
    private TextBox _txtTitle = null!;
    private TextBox _txtContent = null!;
    private ComboBox _cbxDev = null!;
    private ComboBox _cbxPriority = null!;
    private DateTimePicker _dtpReminder = null!;
    private CheckBox _chkReminderNull = null!;
    private CheckBox _chkCompleted = null!;

    public Note Result { get; private set; } = new();

    public NoteDetailForm(AppDbContext db, Note? note = null)
    {
        _devs = db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).ToList();
        BuildUI();
        if (note != null) Populate(note);
    }

    private void BuildUI()
    {
        Text = "Nota / Recordatorio"; Size = new Size(460, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  📌  Nota / Recordatorio", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = false, Padding = Padding.Empty, Margin = Padding.Empty };
        int y = 15;

        AddRow(body, "Título *", ref _txtTitle, ref y);

        body.Controls.Add(new Label { Text = "Desarrollador (quién lo comentó)", Location = new Point(25, y), AutoSize = true });
        y += 20;
        _cbxDev = new ComboBox { Location = new Point(25, y), Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxDev.Items.Add("(Ninguno)");
        foreach (var d in _devs) _cbxDev.Items.Add(d.FullName);
        _cbxDev.SelectedIndex = 0;
        body.Controls.Add(_cbxDev); y += 38;

        body.Controls.Add(new Label { Text = "Prioridad", Location = new Point(25, y), AutoSize = true });
        y += 20;
        _cbxPriority = new ComboBox { Location = new Point(25, y), Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxPriority.Items.AddRange(["Baja", "Media", "Alta"]);
        _cbxPriority.SelectedIndex = 1;
        body.Controls.Add(_cbxPriority); y += 38;

        body.Controls.Add(new Label { Text = "Recordatorio (fecha)", Location = new Point(25, y), AutoSize = true });
        y += 20;
        _dtpReminder = new DateTimePicker { Location = new Point(25, y), Width = 160, Format = DateTimePickerFormat.Short };
        _chkReminderNull = new CheckBox { Text = "Sin recordatorio", Location = new Point(200, y + 2), AutoSize = true };
        _chkReminderNull.CheckedChanged += (_, _) => _dtpReminder.Enabled = !_chkReminderNull.Checked;
        _chkReminderNull.Checked = true;
        body.Controls.AddRange([_dtpReminder, _chkReminderNull]); y += 38;

        _chkCompleted = new CheckBox { Text = "Completado / Resuelto", Location = new Point(25, y), AutoSize = true };
        body.Controls.Add(_chkCompleted); y += 32;

        body.Controls.Add(new Label { Text = "Notas adicionales", Location = new Point(25, y), AutoSize = true });
        y += 20;
        _txtContent = new TextBox { Location = new Point(25, y), Width = 400, Height = 70, Multiline = true, ScrollBars = ScrollBars.Vertical };
        body.Controls.Add(_txtContent); y += 78;

        var btnsPnl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = AppTheme.MakePrimaryButton("Guardar", 100); btnSave.Click += BtnSave_Click;
        btnsPnl.Controls.AddRange([btnCancel, btnSave]);

        tbl.Controls.Add(hdr,     0, 0);
        tbl.Controls.Add(body,    0, 1);
        tbl.Controls.Add(btnsPnl, 0, 2);
        Controls.Add(tbl);
        AcceptButton = btnSave;
    }

    private static void AddRow(Panel p, string label, ref TextBox box, ref int y)
    {
        p.Controls.Add(new Label { Text = label, Location = new Point(25, y), AutoSize = true });
        y += 20;
        box = new TextBox { Location = new Point(25, y), Width = 400 };
        p.Controls.Add(box); y += 38;
    }

    private void Populate(Note n)
    {
        Result = n;
        _txtTitle.Text = n.Title;
        _txtContent.Text = n.Content ?? "";
        if (n.DeveloperId.HasValue)
        {
            var idx = _devs.FindIndex(d => d.Id == n.DeveloperId);
            _cbxDev.SelectedIndex = idx >= 0 ? idx + 1 : 0;
        }
        _cbxPriority.SelectedIndex = (int)n.Priority;
        if (n.ReminderDate.HasValue) { _chkReminderNull.Checked = false; _dtpReminder.Value = n.ReminderDate.Value; }
        else _chkReminderNull.Checked = true;
        _chkCompleted.Checked = n.IsCompleted;
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtTitle.Text)) { MessageBox.Show("El título es obligatorio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Result.Title = _txtTitle.Text.Trim();
        Result.Content = string.IsNullOrWhiteSpace(_txtContent.Text) ? null : _txtContent.Text.Trim();
        Result.DeveloperId = _cbxDev.SelectedIndex > 0 ? _devs[_cbxDev.SelectedIndex - 1].Id : null;
        Result.Priority = (NotePriority)_cbxPriority.SelectedIndex;
        Result.ReminderDate = _chkReminderNull.Checked ? null : _dtpReminder.Value.Date;
        Result.IsCompleted = _chkCompleted.Checked;
        DialogResult = DialogResult.OK; Close();
    }
}
