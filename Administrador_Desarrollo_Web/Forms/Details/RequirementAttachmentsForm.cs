using System.Diagnostics;
using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Gestiona los documentos adjuntos de un requerimiento: adjuntar el documento
/// de requerimiento y el de estimación, abrirlos, guardarlos y eliminarlos.
/// </summary>
public class RequirementAttachmentsForm : ResponsiveForm
{
    private readonly RequirementAttachmentService _svc;
    private readonly int _requirementId;
    private readonly string _requirementTitle;

    private DataGridView _grid = null!;
    private List<AttachmentMeta> _items = [];

    public RequirementAttachmentsForm(RequirementAttachmentService svc, int requirementId, string requirementTitle)
    {
        _svc = svc; _requirementId = requirementId; _requirementTitle = requirementTitle;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        Text = "Adjuntos del requerimiento";
        Size = new Size(920, 500);
        MinimumSize = new Size(820, 360);
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
        var title = _requirementTitle.Length > 60 ? _requirementTitle[..60] + "…" : _requirementTitle;
        hdr.Controls.Add(new Label { Text = $"  📎  Adjuntos — {title}", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 8, 10, 6), BackColor = AppTheme.ContentBg };
        var btnReq  = AppTheme.MakePrimaryButton("📎 Adjuntar requerimiento", 200); btnReq.Margin = new Padding(0, 0, 6, 0); btnReq.Click += (_, _) => Attach(RequirementAttachmentKind.Requerimiento);
        var btnEst  = AppTheme.MakePrimaryButton("📎 Adjuntar estimación", 185); btnEst.Margin = new Padding(0, 0, 6, 0); btnEst.BackColor = AppTheme.Success; btnEst.Click += (_, _) => Attach(RequirementAttachmentKind.Estimacion);
        var btnOpen = AppTheme.MakeSecondaryButton("📄 Abrir", 100); btnOpen.Margin = new Padding(0, 0, 6, 0); btnOpen.Click += BtnOpen_Click;
        var btnSave = AppTheme.MakeSecondaryButton("💾 Guardar como", 140); btnSave.Margin = new Padding(0, 0, 6, 0); btnSave.Click += BtnSaveAs_Click;
        var btnDel  = AppTheme.MakeDangerButton("🗑 Eliminar", 110); btnDel.Margin = new Padding(0, 0, 6, 0); btnDel.Click += BtnDelete_Click;
        toolbar.Controls.AddRange([btnReq, btnEst, btnOpen, btnSave, btnDel]);

        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo",     Name = "Kind",   FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Archivo",  Name = "File",   FillWeight = 46 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tamaño",   Name = "Size",   FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Subido",   Name = "Date",   FillWeight = 19 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", Name = "Id", Visible = false });
        _grid.CellDoubleClick += (_, _) => BtnOpen_Click(null, EventArgs.Empty);

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        outer.Controls.Add(hdr,     0, 0);
        outer.Controls.Add(toolbar, 0, 1);
        outer.Controls.Add(pnlGrid, 0, 2);
        Controls.Add(outer);
    }

    private void LoadData()
    {
        _items = _svc.GetMetaForRequirement(_requirementId);
        _grid.Rows.Clear();
        foreach (var a in _items)
        {
            int i = _grid.Rows.Add(RequirementAttachmentService.KindLabel(a.Kind), a.FileName, FormatSize(a.SizeBytes),
                a.UploadedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), a.Id);
            _grid.Rows[i].Cells["Kind"].Style.ForeColor = a.Kind == RequirementAttachmentKind.Estimacion ? AppTheme.Success : AppTheme.SidebarActive;
            _grid.Rows[i].Cells["Kind"].Style.Font = AppTheme.BoldFont;
        }
    }

    private AttachmentMeta? Selected()
    {
        if (_grid.CurrentRow?.Cells["Id"].Value is not int id) return null;
        return _items.FirstOrDefault(a => a.Id == id);
    }

    private void Attach(RequirementAttachmentKind kind)
    {
        using var dlg = new OpenFileDialog
        {
            Title = $"Seleccionar documento de {RequirementAttachmentService.KindLabel(kind).ToLower()}",
            Filter = "Documentos (*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.txt;*.png;*.jpg;*.zip)|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.txt;*.png;*.jpg;*.jpeg;*.zip|Todos los archivos (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var (ok, error) = _svc.Add(_requirementId, kind, dlg.FileName);
        if (!ok) { MessageBox.Show(error, "No se pudo adjuntar", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        LoadData();
    }

    private void BtnOpen_Click(object? s, EventArgs e)
    {
        var a = Selected();
        if (a == null) { MessageBox.Show("Selecciona un adjunto.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "advweb_att_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, SanitizeFileName(a.FileName));
            File.WriteAllBytes(path, _svc.GetBytes(a.Id));
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir el archivo:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnSaveAs_Click(object? s, EventArgs e)
    {
        var a = Selected();
        if (a == null) { MessageBox.Show("Selecciona un adjunto.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var ext = Path.GetExtension(a.FileName);
        using var dlg = new SaveFileDialog
        {
            FileName = a.FileName,
            Filter = string.IsNullOrEmpty(ext) ? "Todos los archivos (*.*)|*.*" : $"(*{ext})|*{ext}|Todos los archivos (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllBytes(dlg.FileName, _svc.GetBytes(a.Id)); }
        catch (Exception ex) { MessageBox.Show($"No se pudo guardar:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void BtnDelete_Click(object? s, EventArgs e)
    {
        var a = Selected();
        if (a == null) { MessageBox.Show("Selecciona un adjunto.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"¿Eliminar '{a.FileName}'?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _svc.Delete(a.Id);
        LoadData();
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes; int u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return $"{size:0.#} {units[u]}";
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "adjunto" : name;
    }
}
