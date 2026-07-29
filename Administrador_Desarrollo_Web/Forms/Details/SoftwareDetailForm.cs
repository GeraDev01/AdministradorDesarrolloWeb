using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class SoftwareDetailForm : Form
{
    private TextBox _txtName = null!;
    private ComboBox _cbxCategory = null!;
    private ComboBox _cbxLicense = null!;
    private ComboBox _cbxStatus = null!;
    private TextBox _txtVersion = null!;
    private TextBox _txtPublisher = null!;
    private TextBox _txtLicenseKey = null!;
    private DateTimePicker _dtpExpiry = null!;
    private CheckBox _chkExpiryNull = null!;
    private TextBox _txtInstalledOn = null!;
    private TextBox _txtUrl = null!;
    private TextBox _txtNotes = null!;

    public Software Result { get; private set; } = new();

    public SoftwareDetailForm(Software? software = null)
    {
        BuildUI();
        if (software != null) Populate(software);
    }

    private void BuildUI()
    {
        Text = "Programa / Utiliería"; Size = new Size(540, 620);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  🛠  Programa / Utiliería", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty };
        int y = 15;

        // Nombre
        body.Controls.Add(new Label { Text = "Nombre *", Location = new Point(25, y), AutoSize = true });
        _txtName = new TextBox { Location = new Point(25, y + 20), Width = 485 };
        body.Controls.Add(_txtName);
        y += 56;

        // Categoría | Estado
        body.Controls.Add(new Label { Text = "Categoría *", Location = new Point(25, y), AutoSize = true });
        _cbxCategory = new ComboBox { Location = new Point(25, y + 20), Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var c in Enum.GetValues<SoftwareCategory>()) _cbxCategory.Items.Add(CategoryLabel(c));
        _cbxCategory.SelectedIndex = 0;
        body.Controls.Add(_cbxCategory);

        body.Controls.Add(new Label { Text = "Estado *", Location = new Point(290, y), AutoSize = true });
        _cbxStatus = new ComboBox { Location = new Point(290, y + 20), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var s in Enum.GetValues<SoftwareStatus>()) _cbxStatus.Items.Add(StatusLabel(s));
        _cbxStatus.SelectedIndex = 0;
        body.Controls.Add(_cbxStatus);
        y += 58;

        // Tipo de licencia | Versión
        body.Controls.Add(new Label { Text = "Tipo de licencia *", Location = new Point(25, y), AutoSize = true });
        _cbxLicense = new ComboBox { Location = new Point(25, y + 20), Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var l in Enum.GetValues<SoftwareLicenseType>()) _cbxLicense.Items.Add(LicenseLabel(l));
        _cbxLicense.SelectedIndex = 0;
        body.Controls.Add(_cbxLicense);

        body.Controls.Add(new Label { Text = "Versión", Location = new Point(290, y), AutoSize = true });
        _txtVersion = new TextBox { Location = new Point(290, y + 20), Width = 220, PlaceholderText = "ej: 2024.1.0" };
        body.Controls.Add(_txtVersion);
        y += 58;

        // Fabricante
        body.Controls.Add(new Label { Text = "Fabricante / Desarrollador", Location = new Point(25, y), AutoSize = true });
        _txtPublisher = new TextBox { Location = new Point(25, y + 20), Width = 485 };
        body.Controls.Add(_txtPublisher);
        y += 56;

        // Clave de licencia
        body.Controls.Add(new Label { Text = "Clave / Serial (se guarda en texto plano)", Location = new Point(25, y), AutoSize = true });
        _txtLicenseKey = new TextBox { Location = new Point(25, y + 20), Width = 450, UseSystemPasswordChar = true };
        var btnShowKey = new Button { Text = "👁", Location = new Point(480, y + 18), Width = 30, Height = 26, FlatStyle = FlatStyle.Flat };
        btnShowKey.Click += (_, _) => _txtLicenseKey.UseSystemPasswordChar = !_txtLicenseKey.UseSystemPasswordChar;
        body.Controls.AddRange([_txtLicenseKey, btnShowKey]);
        y += 56;

        // Vencimiento
        body.Controls.Add(new Label { Text = "Vencimiento de licencia", Location = new Point(25, y), AutoSize = true });
        _dtpExpiry = new DateTimePicker { Location = new Point(25, y + 20), Width = 170, Format = DateTimePickerFormat.Short };
        _chkExpiryNull = new CheckBox { Text = "Sin vencimiento", Location = new Point(205, y + 22), AutoSize = true };
        _chkExpiryNull.CheckedChanged += (_, _) => _dtpExpiry.Enabled = !_chkExpiryNull.Checked;
        _chkExpiryNull.Checked = true;
        body.Controls.AddRange([_dtpExpiry, _chkExpiryNull]);
        y += 56;

        // Instalado en
        body.Controls.Add(new Label { Text = "Instalado en (equipos / servidores)", Location = new Point(25, y), AutoSize = true });
        _txtInstalledOn = new TextBox { Location = new Point(25, y + 20), Width = 485, PlaceholderText = "ej: DESKTOP-DEV01, servidor-prod" };
        body.Controls.Add(_txtInstalledOn);
        y += 56;

        // URL
        body.Controls.Add(new Label { Text = "URL / Sitio oficial", Location = new Point(25, y), AutoSize = true });
        _txtUrl = new TextBox { Location = new Point(25, y + 20), Width = 485, PlaceholderText = "https://..." };
        body.Controls.Add(_txtUrl);
        y += 56;

        // Notas
        body.Controls.Add(new Label { Text = "Notas", Location = new Point(25, y), AutoSize = true });
        _txtNotes = new TextBox { Location = new Point(25, y + 20), Width = 485, Height = 55, Multiline = true, ScrollBars = ScrollBars.Vertical };
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

    private void Populate(Software s)
    {
        Result = s;
        _txtName.Text = s.Name;
        _cbxCategory.SelectedIndex = (int)s.Category;
        _cbxStatus.SelectedIndex = (int)s.Status;
        _cbxLicense.SelectedIndex = (int)s.LicenseType;
        _txtVersion.Text = s.Version ?? "";
        _txtPublisher.Text = s.Publisher ?? "";
        _txtLicenseKey.Text = s.LicenseKey ?? "";
        if (s.LicenseExpiry.HasValue) { _chkExpiryNull.Checked = false; _dtpExpiry.Value = s.LicenseExpiry.Value; }
        else _chkExpiryNull.Checked = true;
        _txtInstalledOn.Text = s.InstalledOn ?? "";
        _txtUrl.Text = s.Url ?? "";
        _txtNotes.Text = s.Notes ?? "";
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text))
        { MessageBox.Show("El nombre es obligatorio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        Result.Name = _txtName.Text.Trim();
        Result.Category = (SoftwareCategory)_cbxCategory.SelectedIndex;
        Result.Status = (SoftwareStatus)_cbxStatus.SelectedIndex;
        Result.LicenseType = (SoftwareLicenseType)_cbxLicense.SelectedIndex;
        Result.Version = string.IsNullOrWhiteSpace(_txtVersion.Text) ? null : _txtVersion.Text.Trim();
        Result.Publisher = string.IsNullOrWhiteSpace(_txtPublisher.Text) ? null : _txtPublisher.Text.Trim();
        Result.LicenseKey = string.IsNullOrWhiteSpace(_txtLicenseKey.Text) ? null : _txtLicenseKey.Text.Trim();
        Result.LicenseExpiry = _chkExpiryNull.Checked ? null : _dtpExpiry.Value.Date;
        Result.InstalledOn = string.IsNullOrWhiteSpace(_txtInstalledOn.Text) ? null : _txtInstalledOn.Text.Trim();
        Result.Url = string.IsNullOrWhiteSpace(_txtUrl.Text) ? null : _txtUrl.Text.Trim();
        Result.Notes = string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text.Trim();
        Result.UpdatedAt = DateTime.UtcNow;

        DialogResult = DialogResult.OK;
        Close();
    }

    internal static string CategoryLabel(SoftwareCategory c) => c switch
    {
        SoftwareCategory.IDE              => "💻 IDE / Editor",
        SoftwareCategory.ControlVersiones => "🔀 Control de versiones",
        SoftwareCategory.BaseDeDatos      => "🗄 Base de datos",
        SoftwareCategory.Navegador        => "🌐 Navegador",
        SoftwareCategory.Comunicacion     => "💬 Comunicación",
        SoftwareCategory.Disenio          => "🎨 Diseño",
        SoftwareCategory.Seguridad        => "🔒 Seguridad",
        SoftwareCategory.DevOps           => "🚀 DevOps / CI-CD",
        SoftwareCategory.Productividad    => "📋 Productividad",
        SoftwareCategory.UtileriaRed      => "🔌 Utiliería de red",
        _                                 => "📦 Otro"
    };

    internal static string LicenseLabel(SoftwareLicenseType l) => l switch
    {
        SoftwareLicenseType.Gratuita    => "🆓 Gratuita",
        SoftwareLicenseType.OpenSource  => "🌱 Open Source",
        SoftwareLicenseType.Comercial   => "💳 Comercial",
        SoftwareLicenseType.Prueba      => "⏳ Prueba / Trial",
        SoftwareLicenseType.Suscripcion => "🔄 Suscripción",
        _                               => l.ToString()
    };

    internal static string StatusLabel(SoftwareStatus s) => s switch
    {
        SoftwareStatus.EnUso        => "✅ En uso",
        SoftwareStatus.Instalado    => "📥 Instalado",
        SoftwareStatus.Desinstalado => "❌ Desinstalado",
        SoftwareStatus.Expirado     => "⚠ Expirado",
        _                           => s.ToString()
    };
}
