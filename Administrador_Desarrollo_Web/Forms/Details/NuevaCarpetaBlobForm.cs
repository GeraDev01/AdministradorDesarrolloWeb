using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Alta de una carpeta en el contenedor de Blob Storage. Se muestra la ruta completa que quedará,
/// porque en Azure la "carpeta" es literalmente el prefijo del nombre del archivo y conviene ver
/// exactamente dónde va a quedar antes de crearla.
/// </summary>
public class NuevaCarpetaBlobForm : Form
{
    private readonly string _padre;
    private TextBox _txtNombre = null!;
    private Label _lblPreview = null!;
    private CheckBox _chkRaiz = null!;

    public string RutaCompleta =>
        _chkRaiz.Checked || _padre.Length == 0
            ? _txtNombre.Text.Trim()
            : $"{_padre}/{_txtNombre.Text.Trim()}";

    public NuevaCarpetaBlobForm(string carpetaPadre)
    {
        _padre = BlobStorageService.Normalizar(carpetaPadre, "");
        BuildUI();
        Actualizar();
    }

    private void BuildUI()
    {
        Text = "Nueva carpeta";
        Size = new Size(540, 320);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg };
        hdr.Controls.Add(new Label
        {
            Text = "  📁  Nueva carpeta en Blob Storage", Dock = DockStyle.Fill,
            ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        var body = new Panel { Dock = DockStyle.Fill };
        int y = 14;

        body.Controls.Add(new Label { Text = "Nombre de la carpeta *", Location = new Point(22, y), AutoSize = true });
        y += 22;
        _txtNombre = new TextBox { Location = new Point(22, y), Width = 470, PlaceholderText = "QA, Productivo, Infrasur…" };
        _txtNombre.TextChanged += (_, _) => Actualizar();
        body.Controls.Add(_txtNombre);
        y += 34;

        _chkRaiz = new CheckBox
        {
            Text = _padre.Length == 0 ? "Crear en la raíz del contenedor" : $"Crear en la raíz del contenedor (no dentro de «{_padre}»)",
            Location = new Point(22, y), AutoSize = true, Checked = _padre.Length == 0, Enabled = _padre.Length > 0
        };
        _chkRaiz.CheckedChanged += (_, _) => Actualizar();
        body.Controls.Add(_chkRaiz);
        y += 32;

        body.Controls.Add(new Label { Text = "Quedará como:", Location = new Point(22, y), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary });
        y += 20;
        _lblPreview = new Label
        {
            Location = new Point(22, y), AutoSize = false, Size = new Size(470, 24),
            Font = AppTheme.BoldFont, ForeColor = AppTheme.SidebarActive, AutoEllipsis = true
        };
        body.Controls.Add(_lblPreview);
        y += 32;

        body.Controls.Add(new Label
        {
            Text = "En Azure las carpetas no existen como tales: son el prefijo del nombre del archivo.\n" +
                   "Al crearla se sube un marcador vacío para que se vea aquí y en el portal aunque\n" +
                   "todavía no tenga nada dentro. Puedes anidar con «/».",
            Location = new Point(22, y), AutoSize = false, Size = new Size(470, 52),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 8), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnOk = AppTheme.MakePrimaryButton("Crear carpeta", 150);
        btnOk.Click += BtnOk_Click;
        btns.Controls.AddRange([btnCancel, btnOk]);

        tbl.Controls.Add(hdr, 0, 0);
        tbl.Controls.Add(body, 0, 1);
        tbl.Controls.Add(btns, 0, 2);
        Controls.Add(tbl);
        AcceptButton = btnOk;
    }

    private void Actualizar()
    {
        var ruta = RutaCompleta.Trim();
        if (ruta.Length == 0) { _lblPreview.Text = "—"; return; }

        var error = BlobStorageService.ValidarPrefijo(ruta);
        if (error != null)
        {
            _lblPreview.ForeColor = AppTheme.Danger;
            _lblPreview.Text = error;
            return;
        }
        _lblPreview.ForeColor = AppTheme.SidebarActive;
        _lblPreview.Text = BlobStorageService.Normalizar(ruta, "") + "/";
    }

    private void BtnOk_Click(object? s, EventArgs e)
    {
        var ruta = RutaCompleta.Trim();
        if (ruta.Length == 0)
        {
            MessageBox.Show("Escribe el nombre de la carpeta.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtNombre.Focus();
            return;
        }
        var error = BlobStorageService.ValidarPrefijo(ruta);
        if (error != null)
        {
            MessageBox.Show(error, "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtNombre.Focus();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
