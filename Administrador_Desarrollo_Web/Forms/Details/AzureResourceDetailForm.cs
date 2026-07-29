using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class AzureResourceDetailForm : Form
{
    private TextBox _txtName = null!;
    private ComboBox _cbxType = null!;
    private ComboBox _cbxStatus = null!;
    private ComboBox _cbxEnv = null!;
    private TextBox _txtRegion = null!;
    private TextBox _txtResourceGroup = null!;
    private TextBox _txtSubscription = null!;
    private TextBox _txtUrl = null!;
    private NumericUpDown _nudCost = null!;
    private TextBox _txtNotes = null!;

    public AzureResource Result { get; private set; } = new();

    public AzureResourceDetailForm(AzureResource? resource = null)
    {
        BuildUI();
        if (resource != null) Populate(resource);
    }

    private void BuildUI()
    {
        Text = "Recurso Azure"; Size = new Size(560, 600);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  ☁  Recurso de Azure", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty };
        int y = 15;

        // Nombre
        body.Controls.Add(new Label { Text = "Nombre *", Location = new Point(25, y), AutoSize = true });
        _txtName = new TextBox { Location = new Point(25, y + 20), Width = 500 };
        body.Controls.Add(_txtName);
        y += 56;

        // Tipo | Ambiente
        body.Controls.Add(new Label { Text = "Tipo de recurso *", Location = new Point(25, y), AutoSize = true });
        _cbxType = new ComboBox { Location = new Point(25, y + 20), Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var t in Enum.GetValues<AzureResourceType>()) _cbxType.Items.Add(TypeLabel(t));
        _cbxType.SelectedIndex = 0;
        body.Controls.Add(_cbxType);

        body.Controls.Add(new Label { Text = "Ambiente *", Location = new Point(290, y), AutoSize = true });
        _cbxEnv = new ComboBox { Location = new Point(290, y + 20), Width = 235, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var e in Enum.GetValues<AzureEnvironment>()) _cbxEnv.Items.Add(EnvLabel(e));
        _cbxEnv.SelectedIndex = 0;
        body.Controls.Add(_cbxEnv);
        y += 58;

        // Estado | Región
        body.Controls.Add(new Label { Text = "Estado *", Location = new Point(25, y), AutoSize = true });
        _cbxStatus = new ComboBox { Location = new Point(25, y + 20), Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var s in Enum.GetValues<AzureResourceStatus>()) _cbxStatus.Items.Add(StatusLabel(s));
        _cbxStatus.SelectedIndex = 0;
        body.Controls.Add(_cbxStatus);

        body.Controls.Add(new Label { Text = "Región", Location = new Point(290, y), AutoSize = true });
        _txtRegion = new TextBox { Location = new Point(290, y + 20), Width = 235, PlaceholderText = "ej: eastus" };
        body.Controls.Add(_txtRegion);
        y += 58;

        // Resource Group
        body.Controls.Add(new Label { Text = "Resource Group", Location = new Point(25, y), AutoSize = true });
        _txtResourceGroup = new TextBox { Location = new Point(25, y + 20), Width = 500 };
        body.Controls.Add(_txtResourceGroup);
        y += 56;

        // Suscripción
        body.Controls.Add(new Label { Text = "Suscripción / Cuenta", Location = new Point(25, y), AutoSize = true });
        _txtSubscription = new TextBox { Location = new Point(25, y + 20), Width = 500 };
        body.Controls.Add(_txtSubscription);
        y += 56;

        // URL
        body.Controls.Add(new Label { Text = "URL / Endpoint", Location = new Point(25, y), AutoSize = true });
        _txtUrl = new TextBox { Location = new Point(25, y + 20), Width = 500, PlaceholderText = "https://..." };
        body.Controls.Add(_txtUrl);
        y += 56;

        // Costo mensual
        body.Controls.Add(new Label { Text = "Costo mensual estimado (USD)", Location = new Point(25, y), AutoSize = true });
        _nudCost = new NumericUpDown
        {
            Location = new Point(25, y + 20), Width = 130,
            DecimalPlaces = 2, Minimum = 0, Maximum = 99999, Increment = 1
        };
        body.Controls.Add(_nudCost);
        y += 56;

        // Notas
        body.Controls.Add(new Label { Text = "Notas", Location = new Point(25, y), AutoSize = true });
        _txtNotes = new TextBox
        {
            Location = new Point(25, y + 20), Width = 500, Height = 60,
            Multiline = true, ScrollBars = ScrollBars.Vertical
        };
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

    private void Populate(AzureResource r)
    {
        Result = r;
        _txtName.Text = r.Name;
        _cbxType.SelectedIndex = (int)r.ResourceType;
        _cbxStatus.SelectedIndex = (int)r.Status;
        _cbxEnv.SelectedIndex = (int)r.Environment;
        _txtRegion.Text = r.Region ?? "";
        _txtResourceGroup.Text = r.ResourceGroup ?? "";
        _txtSubscription.Text = r.SubscriptionName ?? "";
        _txtUrl.Text = r.Url ?? "";
        _nudCost.Value = r.MonthlyCostEstimate.HasValue ? Math.Min(r.MonthlyCostEstimate.Value, 99999) : 0;
        _txtNotes.Text = r.Notes ?? "";
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text))
        { MessageBox.Show("El nombre es obligatorio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        Result.Name = _txtName.Text.Trim();
        Result.ResourceType = (AzureResourceType)_cbxType.SelectedIndex;
        Result.Status = (AzureResourceStatus)_cbxStatus.SelectedIndex;
        Result.Environment = (AzureEnvironment)_cbxEnv.SelectedIndex;
        Result.Region = string.IsNullOrWhiteSpace(_txtRegion.Text) ? null : _txtRegion.Text.Trim();
        Result.ResourceGroup = string.IsNullOrWhiteSpace(_txtResourceGroup.Text) ? null : _txtResourceGroup.Text.Trim();
        Result.SubscriptionName = string.IsNullOrWhiteSpace(_txtSubscription.Text) ? null : _txtSubscription.Text.Trim();
        Result.Url = string.IsNullOrWhiteSpace(_txtUrl.Text) ? null : _txtUrl.Text.Trim();
        Result.MonthlyCostEstimate = _nudCost.Value > 0 ? _nudCost.Value : null;
        Result.Notes = string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text.Trim();
        Result.UpdatedAt = DateTime.UtcNow;

        DialogResult = DialogResult.OK;
        Close();
    }

    internal static string TypeLabel(AzureResourceType t) => t switch
    {
        AzureResourceType.AppService        => "🌐 App Service",
        AzureResourceType.SqlDatabase       => "🗄 SQL Database",
        AzureResourceType.StorageAccount    => "📦 Storage Account",
        AzureResourceType.KeyVault          => "🔑 Key Vault",
        AzureResourceType.FunctionApp       => "⚡ Function App",
        AzureResourceType.ContainerRegistry => "📋 Container Registry",
        AzureResourceType.ServiceBus        => "📨 Service Bus",
        AzureResourceType.CosmosDb          => "🌌 Cosmos DB",
        AzureResourceType.RedisCache        => "🔴 Redis Cache",
        AzureResourceType.VirtualMachine    => "💻 Máquina Virtual",
        _                                   => "☁ Otro"
    };

    internal static string StatusLabel(AzureResourceStatus s) => s switch
    {
        AzureResourceStatus.EnUso    => "✅ En uso",
        AzureResourceStatus.NoUsado  => "⬜ Sin usar",
        AzureResourceStatus.EnPrueba => "🧪 En prueba",
        AzureResourceStatus.Archivado => "🗃 Archivado",
        _                            => s.ToString()
    };

    internal static string EnvLabel(AzureEnvironment e) => e switch
    {
        AzureEnvironment.Produccion => "🔴 Producción",
        AzureEnvironment.Staging    => "🟡 Staging",
        AzureEnvironment.Desarrollo => "🟢 Desarrollo",
        AzureEnvironment.Compartido => "🔵 Compartido",
        _                           => e.ToString()
    };
}
