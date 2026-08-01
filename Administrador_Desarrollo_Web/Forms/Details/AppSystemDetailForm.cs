using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class AppSystemDetailForm : ResponsiveForm
{
    private TextBox _txtName = null!;
    private TextBox _txtDesc = null!;
    private ComboBox _cboBlob = null!;
    private CheckBox _chkActive = null!;
    private readonly IReadOnlyList<string> _carpetasBlob;

    public AppSystem Result { get; private set; } = new();

    public AppSystemDetailForm(IReadOnlyList<string> carpetasBlob, AppSystem? system = null)
    {
        _carpetasBlob = carpetasBlob;
        BuildUI();
        if (system != null) Populate(system);
    }

    private void BuildUI()
    {
        Text = "Sistema / Aplicativo"; Size = new Size(480, 400);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  🖥  Sistema / Aplicativo", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty, Margin = Padding.Empty };
        int y = 15;
        AddRow(body, "Nombre *", ref _txtName, ref y);
        AddRow(body, "Descripción", ref _txtDesc, ref y);

        // Asociación con una carpeta de BLOB STORAGE (no una carpeta local): es la subcarpeta de
        // versiones (QA, Productivo, un cliente…) donde viven los paquetes del sistema. Se propone
        // como destino al crear una nueva versión. Editable: se puede elegir de las existentes o
        // teclear una que se creará luego en Despliegues → Blob Storage.
        body.Controls.Add(new Label { Text = "Carpeta en Blob Storage (destino por defecto de versiones)", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cboBlob = new ComboBox { Location = new Point(25, y), Width = 420, DropDownStyle = ComboBoxStyle.DropDown };
        foreach (var c in _carpetasBlob) _cboBlob.Items.Add(c);
        body.Controls.Add(_cboBlob); y += 34;
        body.Controls.Add(new Label
        {
            Text = _carpetasBlob.Count == 0
                ? "No hay subcarpetas aún. Créalas en Despliegues → Blob Storage. Déjalo vacío para la raíz de versiones."
                : "Déjalo vacío para usar la raíz de versiones. Las carpetas se administran en Despliegues → Blob Storage.",
            Location = new Point(25, y), AutoSize = false, Size = new Size(420, 32),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });
        y += 40;

        _chkActive = new CheckBox { Text = "Activo", Location = new Point(25, y), Checked = true, AutoSize = true };
        body.Controls.Add(_chkActive); y += 30;

        var btnsPnl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = AppTheme.MakePrimaryButton("Guardar", 100); btnSave.Click += BtnSave_Click;
        btnsPnl.Controls.AddRange([btnCancel, btnSave]);

        outer.Controls.Add(hdr,     0, 0);
        outer.Controls.Add(body,    0, 1);
        outer.Controls.Add(btnsPnl, 0, 2);
        Controls.Add(outer); AcceptButton = btnSave;
    }

    private static void AddRow(Panel p, string label, ref TextBox box, ref int y)
    {
        p.Controls.Add(new Label { Text = label, Location = new Point(25, y), AutoSize = true }); y += 20;
        box = new TextBox { Location = new Point(25, y), Width = 420 };
        p.Controls.Add(box); y += 38;
    }

    private void Populate(AppSystem s)
    {
        Result = s;
        _txtName.Text = s.Name;
        _txtDesc.Text = s.Description ?? "";
        _cboBlob.Text = s.DefaultBlobFolder ?? "";
        _chkActive.Checked = s.IsActive;
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text)) { MessageBox.Show("El nombre es obligatorio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Result.Name = _txtName.Text.Trim();
        Result.Description = string.IsNullOrWhiteSpace(_txtDesc.Text) ? null : _txtDesc.Text.Trim();
        // Se guarda la subcarpeta de Blob (normalizada sin barras). El origen LOCAL de build ya no se
        // captura aquí: se indica al crear cada versión, que es donde tiene sentido.
        Result.DefaultBlobFolder = string.IsNullOrWhiteSpace(_cboBlob.Text) ? null : _cboBlob.Text.Trim().Trim('/', '\\');
        Result.IsActive = _chkActive.Checked;
        DialogResult = DialogResult.OK; Close();
    }
}
