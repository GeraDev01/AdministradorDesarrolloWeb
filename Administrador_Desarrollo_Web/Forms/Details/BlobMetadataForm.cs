using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Editor de los metadatos de un blob. Azure no hace merge al guardarlos: lo que se envía es lo
/// que queda, así que la rejilla siempre muestra y devuelve el conjunto COMPLETO.
/// </summary>
public class BlobMetadataForm : ResponsiveForm
{
    private DataGridView _grid = null!;
    private Label _lblError = null!;

    public Dictionary<string, string> Metadata { get; private set; } = [];

    public BlobMetadataForm(string blobName, IDictionary<string, string> actuales)
    {
        BuildUI(blobName);
        foreach (var (k, v) in actuales.OrderBy(p => p.Key)) _grid.Rows.Add(k, v);
    }

    private void BuildUI(string blobName)
    {
        Text = "Metadatos del blob";
        Size = new Size(640, 500);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(560, 400);
        MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg };
        hdr.Controls.Add(new Label
        {
            Text = $"  🏷  {blobName}", Dock = DockStyle.Fill, ForeColor = Color.White,
            Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
        });

        _grid = AppTheme.MakeGrid();
        _grid.ReadOnly = false;
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nombre", Name = "K", FillWeight = 40 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Valor",  Name = "V", FillWeight = 60 });

        var pBody = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 4), BackColor = AppTheme.ContentBg };
        pBody.Controls.Add(_grid);

        _lblError = new Label
        {
            Dock = DockStyle.Fill, ForeColor = AppTheme.Danger, Font = AppTheme.SmallFont,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(16, 0, 0, 0),
            Text = "Nombres: solo letras, números y guion bajo, sin empezar por número. Usa la última fila para agregar."
        };
        _lblError.ForeColor = AppTheme.TextSecondary;

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12, 8, 12, 8), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = AppTheme.MakePrimaryButton("Guardar metadatos", 170);
        btnSave.Click += BtnSave_Click;
        btns.Controls.AddRange([btnCancel, btnSave]);

        tbl.Controls.Add(hdr, 0, 0);
        tbl.Controls.Add(pBody, 0, 1);
        tbl.Controls.Add(_lblError, 0, 2);
        tbl.Controls.Add(btns, 0, 3);
        Controls.Add(tbl);
        AcceptButton = btnSave;
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (DataGridViewRow fila in _grid.Rows)
        {
            if (fila.IsNewRow) continue;
            var clave = fila.Cells["K"].Value?.ToString()?.Trim() ?? "";
            var valor = fila.Cells["V"].Value?.ToString() ?? "";

            if (clave.Length == 0 && valor.Trim().Length == 0) continue;   // fila vacía: se ignora

            var error = BlobStorageService.ValidarClaveMetadato(clave);
            if (error != null) { Mostrar(error); return; }

            if (!resultado.TryAdd(clave, valor))
            {
                Mostrar($"«{clave}» está repetido. Azure no admite dos metadatos con el mismo nombre.");
                return;
            }
        }

        Metadata = resultado;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Mostrar(string mensaje)
    {
        _lblError.ForeColor = AppTheme.Danger;
        _lblError.Text = "✗  " + mensaje;
    }
}
