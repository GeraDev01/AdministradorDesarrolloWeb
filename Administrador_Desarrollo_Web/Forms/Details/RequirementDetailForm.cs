using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Forms;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class RequirementDetailForm : ResponsiveForm
{
    private TextBox _txtTitle = null!;
    private TextBox _txtDesc = null!;
    private ComboBox _cbxStatus = null!;
    private ComboBox _cbxPriority = null!;
    private NumericUpDown _nudHours = null!;
    private DateTimePicker _dtpRequest = null!;
    private DateTimePicker _dtpCommitted = null!;
    private DateTimePicker _dtpActual = null!;
    private CheckBox _chkRequestNull = null!;
    private CheckBox _chkCommittedNull = null!;
    private CheckBox _chkActualNull = null!;
    private TrackBar _trackProgress = null!;
    private Label _lblProgressVal = null!;

    public Requirement Result { get; private set; } = new();

    public RequirementDetailForm(Requirement? req = null)
    {
        BuildUI();
        if (req != null) Populate(req);
    }

    private void BuildUI()
    {
        Text = "Requerimiento";
        Size = new Size(600, 680);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;

        var pnlHeader = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        pnlHeader.Controls.Add(new Label
        {
            Text = "  📋  Detalle del Requerimiento",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = AppTheme.HeaderFont,
            TextAlign = ContentAlignment.MiddleLeft
        });

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = Padding.Empty
        };

        int y = 12;

        // Título
        scroll.Controls.Add(new Label { Text = "Título *", Location = new Point(25, y), AutoSize = true });
        _txtTitle = new TextBox { Location = new Point(25, y + 20), Width = 540 };
        scroll.Controls.Add(_txtTitle);
        y += 56;

        // Descripción
        scroll.Controls.Add(new Label { Text = "Descripción", Location = new Point(25, y), AutoSize = true });
        _txtDesc = new TextBox
        {
            Location = new Point(25, y + 20),
            Width = 540, Height = 70,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };
        scroll.Controls.Add(_txtDesc);
        y += 100;

        // Estado y Prioridad en dos columnas
        scroll.Controls.Add(new Label { Text = "Estado", Location = new Point(25, y), AutoSize = true });
        _cbxStatus = new ComboBox
        {
            Location = new Point(25, y + 20),
            Width = 250,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var s in Enum.GetValues<RequirementStatus>())
            _cbxStatus.Items.Add(StatusLabel(s));
        _cbxStatus.SelectedIndex = 0;
        scroll.Controls.Add(_cbxStatus);

        scroll.Controls.Add(new Label { Text = "Prioridad", Location = new Point(305, y), AutoSize = true });
        _cbxPriority = new ComboBox
        {
            Location = new Point(305, y + 20),
            Width = 260,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cbxPriority.Items.AddRange(["Baja", "Media", "Alta", "Crítica"]);
        _cbxPriority.SelectedIndex = 1;
        scroll.Controls.Add(_cbxPriority);
        y += 60;

        // Estimación
        scroll.Controls.Add(new Label { Text = "Estimación (hrs)", Location = new Point(25, y), AutoSize = true });
        _nudHours = new NumericUpDown
        {
            Location = new Point(25, y + 20),
            Width = 130,
            DecimalPlaces = 1,
            Minimum = 0,
            Maximum = 9999,
            Increment = 0.5m
        };
        scroll.Controls.Add(_nudHours);
        y += 60;

        // Fechas
        AddDateRow(scroll, "Fecha de solicitud", ref _dtpRequest, ref _chkRequestNull, y);  y += 60;
        AddDateRow(scroll, "Fecha compromiso", ref _dtpCommitted, ref _chkCommittedNull, y); y += 60;
        AddDateRow(scroll, "Fecha entrega real", ref _dtpActual, ref _chkActualNull, y);     y += 60;

        // Progreso
        scroll.Controls.Add(new Label { Text = "Avance (%)", Location = new Point(25, y), AutoSize = true });
        _lblProgressVal = new Label
        {
            Text = "0%",
            Location = new Point(425, y),
            AutoSize = true,
            Font = AppTheme.BoldFont,
            ForeColor = AppTheme.SidebarActive
        };
        scroll.Controls.Add(_lblProgressVal);
        _trackProgress = new TrackBar
        {
            Location = new Point(25, y + 20),
            Width = 540,
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            SmallChange = 5,
            LargeChange = 10
        };
        _trackProgress.Scroll += (_, _) => _lblProgressVal.Text = $"{_trackProgress.Value}%";
        scroll.Controls.Add(_trackProgress);
        y += 70;

        scroll.AutoScrollMinSize = new Size(0, y + 20);

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
        outer.Controls.Add(scroll,    0, 1);
        outer.Controls.Add(pnlBtns,   0, 2);
        Controls.Add(outer);
        AcceptButton = btnSave;
    }

    private static void AddDateRow(Panel parent, string label, ref DateTimePicker dtp, ref CheckBox chkNull, int y)
    {
        parent.Controls.Add(new Label { Text = label, Location = new Point(25, y), AutoSize = true });
        dtp = new DateTimePicker
        {
            Location = new Point(25, y + 20),
            Width = 180,
            Format = DateTimePickerFormat.Short
        };
        chkNull = new CheckBox
        {
            Text = "N/A",
            Location = new Point(220, y + 22),
            AutoSize = true
        };
        var localDtp = dtp;
        var localChk = chkNull;
        chkNull.CheckedChanged += (_, _) => localDtp.Enabled = !localChk.Checked;
        parent.Controls.AddRange([dtp, chkNull]);
    }

    private void Populate(Requirement req)
    {
        Result = req;
        _txtTitle.Text = req.Title;
        _txtDesc.Text = req.Description ?? "";
        _cbxStatus.SelectedIndex = (int)req.Status;
        _cbxPriority.SelectedIndex = (int)req.Priority;
        _nudHours.Value = req.EstimateHours.HasValue ? (decimal)req.EstimateHours.Value : 0;

        SetDate(_dtpRequest, _chkRequestNull, req.RequestDate);
        SetDate(_dtpCommitted, _chkCommittedNull, req.CommittedDeliveryDate);
        SetDate(_dtpActual, _chkActualNull, req.ActualDeliveryDate);

        _trackProgress.Value = Math.Clamp(req.ProgressPercent, 0, 100);
        _lblProgressVal.Text = $"{_trackProgress.Value}%";
    }

    private static void SetDate(DateTimePicker dtp, CheckBox chk, DateTime? date)
    {
        if (date == null) { chk.Checked = true; dtp.Enabled = false; }
        else { dtp.Value = date.Value; }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtTitle.Text))
        { MessageBox.Show("El título es obligatorio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        Result.Title = _txtTitle.Text.Trim();
        Result.Description = string.IsNullOrWhiteSpace(_txtDesc.Text) ? null : _txtDesc.Text.Trim();
        Result.Status = (RequirementStatus)_cbxStatus.SelectedIndex;
        Result.Priority = (RequirementPriority)_cbxPriority.SelectedIndex;
        Result.EstimateHours = _nudHours.Value > 0 ? _nudHours.Value : null;
        Result.RequestDate = _chkRequestNull.Checked ? null : _dtpRequest.Value.Date;
        Result.CommittedDeliveryDate = _chkCommittedNull.Checked ? null : _dtpCommitted.Value.Date;
        Result.ActualDeliveryDate = _chkActualNull.Checked ? null : _dtpActual.Value.Date;
        Result.ProgressPercent = _trackProgress.Value;

        DialogResult = DialogResult.OK;
        Close();
    }

    private static string StatusLabel(RequirementStatus s) => s switch
    {
        RequirementStatus.PorEstimar   => "Por estimar",
        RequirementStatus.Estimado     => "Estimado",
        RequirementStatus.EnDesarrollo => "En desarrollo",
        RequirementStatus.EnPruebas    => "En pruebas",
        RequirementStatus.PorEntregar  => "Por entregar",
        RequirementStatus.Entregado    => "Entregado",
        RequirementStatus.Cancelado    => "Cancelado",
        _                              => s.ToString()
    };
}
