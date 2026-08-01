using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Gestor de firmas preguardadas: dibujar, importar imagen, marcar predeterminada
/// y eliminar. Alcance actual: firmas compartidas / del gerente (OwnerDeveloperId = null).
/// </summary>
public class SignatureManagerForm : ResponsiveForm
{
    private readonly SignatureService _sig;
    private DataGridView _grid = null!;
    private List<SignatureProfile> _items = [];

    public SignatureManagerForm(SignatureService sig)
    {
        _sig = sig;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        Text = "Firmas guardadas";
        Size = new Size(720, 500);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  🖊  Firmas guardadas", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 8, 10, 6), BackColor = AppTheme.ContentBg };
        var btnDraw   = AppTheme.MakePrimaryButton("✍ Dibujar nueva", 140); btnDraw.Margin = new Padding(0, 0, 6, 0); btnDraw.Click += BtnDraw_Click;
        var btnImport = AppTheme.MakeSecondaryButton("🖼 Importar imagen", 150); btnImport.Margin = new Padding(0, 0, 6, 0); btnImport.Click += BtnImport_Click;
        var btnDefault= AppTheme.MakeSecondaryButton("⭐ Predeterminada", 150); btnDefault.Margin = new Padding(0, 0, 6, 0); btnDefault.Click += BtnDefault_Click;
        var btnDelete = AppTheme.MakeDangerButton("🗑 Eliminar", 120); btnDelete.Margin = new Padding(0, 0, 6, 0); btnDelete.Click += BtnDelete_Click;
        toolbar.Controls.AddRange([btnDraw, btnImport, btnDefault, btnDelete]);

        _grid = AppTheme.MakeGrid();
        _grid.RowTemplate.Height = 56;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", Name = "Id", Width = 45, FillWeight = 6 });
        _grid.Columns.Add(new DataGridViewImageColumn { HeaderText = "Firma", Name = "Preview", FillWeight = 34, ImageLayout = DataGridViewImageCellLayout.Zoom });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nombre", Name = "Name", FillWeight = 34 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Predet.", Name = "Default", FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Creada", Name = "Created", FillWeight = 14 });
        _grid.Columns["Id"]!.Visible = false;

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        outer.Controls.Add(hdr,     0, 0);
        outer.Controls.Add(toolbar, 0, 1);
        outer.Controls.Add(pnlGrid, 0, 2);
        Controls.Add(outer);
    }

    private void LoadData()
    {
        _items = _sig.GetAll();
        _grid.Rows.Clear();
        foreach (var s in _items)
        {
            Image thumb;
            try { thumb = SignatureImaging.FromBytes(s.PngBytes); } catch { thumb = new Bitmap(1, 1); }
            int i = _grid.Rows.Add(s.Id, thumb, s.DisplayName, s.IsDefault ? "⭐ Sí" : "", s.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy"));
            _grid.Rows[i].Cells["Preview"].Style.BackColor = Color.White;
            _grid.Rows[i].Cells["Preview"].Style.SelectionBackColor = Color.White;
        }
    }

    private SignatureProfile? Selected()
    {
        if (_grid.CurrentRow?.Cells["Id"].Value is not int id) return null;
        return _items.FirstOrDefault(s => s.Id == id);
    }

    private void BtnDraw_Click(object? s, EventArgs e)
    {
        using var frm = new SignatureCaptureForm();
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        _sig.Create(frm.DisplayName, frm.Png, frm.PngWidth, frm.PngHeight, ownerDeveloperId: null, isDefault: frm.IsDefault);
        LoadData();
    }

    private void BtnImport_Click(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", Title = "Seleccionar imagen de firma" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var loaded = new Bitmap(dlg.FileName);
            using var transparent = SignatureImaging.MakeNearWhiteTransparent(loaded);
            using var cropped = SignatureImaging.AutoCrop(transparent);
            if (cropped == null) { MessageBox.Show("La imagen quedó vacía tras quitar el fondo.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            var png = SignatureImaging.ToPng(cropped);
            var name = Path.GetFileNameWithoutExtension(dlg.FileName);
            using var nameFrm = new SignaturePromptForm(name, _items.Count == 0);
            if (nameFrm.ShowDialog(this) != DialogResult.OK) return;
            _sig.Create(nameFrm.DisplayName, png, cropped.Width, cropped.Height, ownerDeveloperId: null, isDefault: nameFrm.IsDefault);
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo importar la imagen:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnDefault_Click(object? s, EventArgs e)
    {
        var sig = Selected();
        if (sig == null) { MessageBox.Show("Selecciona una firma.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        _sig.SetDefault(sig.Id);
        LoadData();
    }

    private void BtnDelete_Click(object? s, EventArgs e)
    {
        var sig = Selected();
        if (sig == null) { MessageBox.Show("Selecciona una firma.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"¿Eliminar la firma '{sig.DisplayName}'?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _sig.Delete(sig.Id);
        LoadData();
    }
}
