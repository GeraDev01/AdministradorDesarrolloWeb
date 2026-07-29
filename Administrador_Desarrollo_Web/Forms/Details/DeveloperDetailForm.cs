using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class DeveloperDetailForm : Form
{
    private TextBox _txtName = null!;
    private TextBox _txtEmail = null!;
    private TextBox _txtPhone = null!;
    private ComboBox _cbxSeniority = null!;
    private CheckBox _chkActive = null!;
    private DateTimePicker _dtpHireDate = null!;
    private CheckBox _chkHireDateNull = null!;
    private TextBox _txtAddress = null!;
    private TextBox _txtEquipmentSerial = null!;
    private NumericUpDown _nudVacationDays = null!;
    private Button _btnCalcLft = null!;
    private Label _lblLft = null!;
    private TextBox _txtNotes = null!;

    public Developer Result { get; private set; } = new();

    public DeveloperDetailForm(Developer? dev = null)
    {
        BuildUI();
        if (dev != null) Populate(dev);
    }

    private void BuildUI()
    {
        Text = "Desarrollador";
        Size = new Size(480, 750);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;

        var pnlHeader = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        pnlHeader.Controls.Add(new Label
        {
            Text = "  👤  Información del Desarrollador",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = AppTheme.HeaderFont,
            TextAlign = ContentAlignment.MiddleLeft
        });

        // AutoScroll: red de seguridad si el contenido no cabe (p. ej. a 125/150 % de escala de
        // pantalla), para que la caja de Notas nunca quede cortada como antes.
        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 12), AutoScroll = true };
        int y = 15;

        AddField(pnl, "Nombre completo *", ref _txtName, ref y);
        AddField(pnl, "Correo electrónico", ref _txtEmail, ref y);
        AddField(pnl, "Teléfono", ref _txtPhone, ref y);

        pnl.Controls.Add(new Label { Text = "Seniority", Location = new Point(25, y), AutoSize = true });
        _cbxSeniority = new ComboBox
        {
            Location = new Point(25, y + 22),
            Width = 420,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cbxSeniority.Items.AddRange(["Junior", "Mid", "Senior", "Lead", "Arquitecto"]);
        _cbxSeniority.SelectedIndex = 0;
        pnl.Controls.Add(_cbxSeniority);
        y += 58;

        _chkActive = new CheckBox
        {
            Text = "Activo",
            Location = new Point(25, y),
            AutoSize = true,
            Checked = true,
            Font = AppTheme.DefaultFont
        };
        pnl.Controls.Add(_chkActive);
        y += 36;

        // Fecha de ingreso
        pnl.Controls.Add(new Label { Text = "Fecha de ingreso", Location = new Point(25, y), AutoSize = true });
        _dtpHireDate = new DateTimePicker { Location = new Point(25, y + 20), Width = 175, Format = DateTimePickerFormat.Short };
        _chkHireDateNull = new CheckBox { Text = "No especificada", Location = new Point(210, y + 22), AutoSize = true };
        _chkHireDateNull.CheckedChanged += (_, _) => _dtpHireDate.Enabled = !_chkHireDateNull.Checked;
        _chkHireDateNull.Checked = true;
        pnl.Controls.AddRange([_dtpHireDate, _chkHireDateNull]);
        y += 56;

        // Dirección
        pnl.Controls.Add(new Label { Text = "Dirección", Location = new Point(25, y), AutoSize = true });
        _txtAddress = new TextBox { Location = new Point(25, y + 20), Width = 420, Height = 44, Multiline = true };
        pnl.Controls.Add(_txtAddress);
        y += 76;

        // Número de serie del equipo (computadora asignada)
        pnl.Controls.Add(new Label { Text = "Número de serie del equipo", Location = new Point(25, y), AutoSize = true });
        _txtEquipmentSerial = new TextBox { Location = new Point(25, y + 20), Width = 420 };
        pnl.Controls.Add(_txtEquipmentSerial);
        y += 58;

        // Vacaciones — se pueden calcular con la fecha de ingreso conforme a la LFT (Art. 76/77).
        pnl.Controls.Add(new Label { Text = "Días de vacaciones al año", Location = new Point(25, y), AutoSize = true });
        _nudVacationDays = new NumericUpDown { Location = new Point(25, y + 20), Width = 90, Minimum = 0, Maximum = 365, Value = 15 };
        pnl.Controls.Add(_nudVacationDays);
        _btnCalcLft = AppTheme.MakeSecondaryButton("📅 Calcular según LFT", 170, 26);
        _btnCalcLft.Location = new Point(125, y + 19);
        _btnCalcLft.Click += BtnCalcLft_Click;
        pnl.Controls.Add(_btnCalcLft);
        _lblLft = new Label { Location = new Point(27, y + 50), AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont };
        pnl.Controls.Add(_lblLft);
        y += 78;

        pnl.Controls.Add(new Label { Text = "Notas", Location = new Point(25, y), AutoSize = true });
        _txtNotes = new TextBox
        {
            Location = new Point(25, y + 22),
            Width = 420,
            Height = 80,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };
        pnl.Controls.Add(_txtNotes);

        var pnlBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10),
            BackColor = AppTheme.ContentBg
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
        outer.Controls.Add(pnlHeader, 0, 0);
        outer.Controls.Add(pnl,       0, 1);
        outer.Controls.Add(pnlBtns,   0, 2);
        Controls.Add(outer);
        AcceptButton = btnSave;
    }

    private static void AddField(Panel parent, string label, ref TextBox box, ref int y)
    {
        parent.Controls.Add(new Label { Text = label, Location = new Point(25, y), AutoSize = true });
        box = new TextBox { Location = new Point(25, y + 22), Width = 420 };
        parent.Controls.Add(box);
        y += 58;
    }

    private void Populate(Developer dev)
    {
        Result = dev;
        _txtName.Text = dev.FullName;
        _txtEmail.Text = dev.Email ?? "";
        _txtPhone.Text = dev.Phone ?? "";
        var idx = _cbxSeniority.Items.IndexOf(dev.Seniority ?? "Junior");
        _cbxSeniority.SelectedIndex = idx >= 0 ? idx : 0;
        _chkActive.Checked = dev.IsActive;
        if (dev.HireDate.HasValue) { _chkHireDateNull.Checked = false; _dtpHireDate.Value = dev.HireDate.Value; ActualizarNotaLft(dev.HireDate.Value); }
        else _chkHireDateNull.Checked = true;
        _txtAddress.Text = dev.Address ?? "";
        _txtEquipmentSerial.Text = dev.EquipmentSerial ?? "";
        _nudVacationDays.Value = Math.Clamp(dev.VacationDaysLeft, 0, 365);
        _txtNotes.Text = dev.Notes ?? "";
    }

    /// <summary>Rellena los días de vacaciones a partir de la fecha de ingreso, conforme a la LFT.
    /// Es una sugerencia: el administrador puede ajustar el valor después.</summary>
    private void BtnCalcLft_Click(object? sender, EventArgs e)
    {
        if (_chkHireDateNull.Checked)
        { MessageBox.Show("Primero captura la fecha de ingreso.", "Vacaciones (LFT)", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        var ingreso = _dtpHireDate.Value.Date;
        if (ingreso > DateTime.Today)
        { MessageBox.Show("La fecha de ingreso no puede ser futura.", "Vacaciones (LFT)", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        int dias = LftVacaciones.DiasCorrespondientes(ingreso, DateTime.Today);
        _nudVacationDays.Value = Math.Clamp(dias, (int)_nudVacationDays.Minimum, (int)_nudVacationDays.Maximum);
        ActualizarNotaLft(ingreso);
    }

    private void ActualizarNotaLft(DateTime ingreso)
    {
        int anios = LftVacaciones.AniosCumplidos(ingreso, DateTime.Today);
        _lblLft.Text = anios >= 1
            ? $"LFT: {anios} año(s) de antigüedad → {LftVacaciones.DiasPorAnios(anios)} días al año."
            : "LFT: menos de un año → proporcional del primer año (12 días).";
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text))
        { MessageBox.Show("El nombre es obligatorio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        Result.FullName = _txtName.Text.Trim();
        Result.Email = string.IsNullOrWhiteSpace(_txtEmail.Text) ? null : _txtEmail.Text.Trim();
        Result.Phone = string.IsNullOrWhiteSpace(_txtPhone.Text) ? null : _txtPhone.Text.Trim();
        Result.Seniority = _cbxSeniority.SelectedItem?.ToString();
        Result.IsActive = _chkActive.Checked;
        Result.HireDate = _chkHireDateNull.Checked ? null : _dtpHireDate.Value.Date;
        Result.Address = string.IsNullOrWhiteSpace(_txtAddress.Text) ? null : _txtAddress.Text.Trim();
        Result.EquipmentSerial = string.IsNullOrWhiteSpace(_txtEquipmentSerial.Text) ? null : _txtEquipmentSerial.Text.Trim();
        Result.VacationDaysLeft = (int)_nudVacationDays.Value;
        Result.Notes = string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }
}
