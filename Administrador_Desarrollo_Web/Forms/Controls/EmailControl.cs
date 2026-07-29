using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Bandeja de correo: lista los mensajes de una carpeta del buzón, permite verlos,
/// convertir uno en requerimiento e importar (ingerir) los no leídos como requerimientos.
/// </summary>
public class EmailControl : UserControl
{
    private readonly EmailService _email;
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    private ComboBox _cbxFolder = null!;
    private DataGridView _grid = null!;
    private TextBox _txtBody = null!;
    private Label _lblStatus = null!;
    private List<EmailMessageInfo> _messages = [];
    private bool _foldersLoaded;

    public EmailControl(EmailService email, AppDbContext db, AuditService audit)
    {
        _email = email; _db = db; _audit = audit;
        BuildUI();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 8, 10, 5), BackColor = AppTheme.ContentBg };
        toolbar.Controls.Add(new Label { Text = "Carpeta:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _cbxFolder = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 8, 0) };
        _cbxFolder.SelectedIndexChanged += async (_, _) => await LoadMessagesAsync();
        toolbar.Controls.Add(_cbxFolder);
        var btnLoad = AppTheme.MakePrimaryButton("🔄 Cargar", 100); btnLoad.Margin = new Padding(0, 2, 8, 0); btnLoad.Click += async (_, _) => await LoadMessagesAsync();
        var btnConv = AppTheme.MakeSecondaryButton("➡ Convertir en requerimiento", 220); btnConv.Margin = new Padding(0, 2, 8, 0); btnConv.Click += BtnConvert_Click;
        var btnIngest = AppTheme.MakeSecondaryButton("📥 Importar no leídos", 175); btnIngest.Margin = new Padding(0, 2, 8, 0); btnIngest.Click += async (_, _) => await IngestAsync();
        var btnCompose = AppTheme.MakeSecondaryButton("✉ Redactar", 110); btnCompose.Margin = new Padding(0, 2, 8, 0); btnCompose.Click += (_, _) => { using var f = new EmailComposeForm(_email); f.ShowDialog(FindForm()); };
        _lblStatus = new Label { Text = "", AutoSize = true, Margin = new Padding(6, 8, 0, 0), ForeColor = AppTheme.TextSecondary };
        toolbar.Controls.AddRange([btnLoad, btnConv, btnIngest, btnCompose, _lblStatus]);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 320, Panel1MinSize = 160, Panel2MinSize = 100 };

        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha", Name = "Date", FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "De", Name = "From", FillWeight = 26 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Asunto", Name = "Subject", FillWeight = 34 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Vista previa", Name = "Preview", FillWeight = 24 });
        _grid.SelectionChanged += (_, _) => ShowBody();
        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg };
        pnlGrid.Controls.Add(_grid);
        split.Panel1.Controls.Add(pnlGrid);

        _txtBody = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
        var pnlBody = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg };
        pnlBody.Controls.Add(_txtBody);
        split.Panel2.Controls.Add(pnlBody);

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(split, 0, 1);
        Controls.Add(tbl);
    }

    private async Task EnsureFoldersAsync()
    {
        if (_foldersLoaded) return;
        if (!_email.IsConfigured(out var diag)) { SetStatus("⚠ " + diag, AppTheme.Warning); return; }
        try
        {
            SetBusy(true, "Cargando carpetas...");
            var folders = await _email.ListFoldersAsync();
            _cbxFolder.Items.Clear();
            foreach (var f in folders) _cbxFolder.Items.Add(f);
            var pref = _email.RequirementsFolder;
            int idx = _cbxFolder.Items.IndexOf(pref);
            _cbxFolder.SelectedIndex = idx >= 0 ? idx : 0;
            _foldersLoaded = true;
        }
        catch (Exception ex) { SetStatus("Error IMAP: " + ex.Message, AppTheme.Danger); }
        finally { SetBusy(false); }
    }

    private async Task LoadMessagesAsync()
    {
        if (!_email.IsConfigured(out var diag)) { SetStatus("⚠ " + diag, AppTheme.Warning); return; }
        if (_cbxFolder.SelectedItem is not string folder) return;
        try
        {
            SetBusy(true, "Cargando mensajes...");
            _messages = await _email.FetchRecentAsync(folder, 50);
            _grid.Rows.Clear();
            foreach (var m in _messages)
                _grid.Rows.Add(m.Date.ToString("dd/MM/yyyy HH:mm"), m.From, m.Subject, m.Preview);
            SetStatus($"{_messages.Count} mensaje(s).", AppTheme.TextSecondary);
        }
        catch (Exception ex) { SetStatus("Error: " + ex.Message, AppTheme.Danger); }
        finally { SetBusy(false); }
    }

    private EmailMessageInfo? Selected()
    {
        if (_grid.CurrentRow == null) return null;
        int i = _grid.CurrentRow.Index;
        return i >= 0 && i < _messages.Count ? _messages[i] : null;
    }

    private void ShowBody()
    {
        var m = Selected();
        _txtBody.Text = m == null ? "" : $"De: {m.From}\r\nFecha: {m.Date:dd/MM/yyyy HH:mm}\r\nAsunto: {m.Subject}\r\n\r\n{m.Body}";
    }

    private void BtnConvert_Click(object? s, EventArgs e)
    {
        var m = Selected();
        if (m == null) { MessageBox.Show("Selecciona un mensaje.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var req = new Requirement
        {
            Title = string.IsNullOrWhiteSpace(m.Subject) ? "(sin asunto)" : m.Subject.Trim(),
            Description = $"De: {m.From}\nFecha: {m.Date:dd/MM/yyyy HH:mm}\n\n{m.Body}".Trim(),
            Source = RequirementSource.Email, Status = RequirementStatus.PorEstimar,
            CreatedAt = DateTime.UtcNow, StatusChangedAt = DateTime.UtcNow
        };
        _db.Requirements.Add(req); _db.SaveChanges();
        _audit.Record(AuditAction.Create, "Requirement", req.Id.ToString(), $"Desde correo: {req.Title}");
        MessageBox.Show($"Requerimiento #{req.Id} creado.", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task IngestAsync()
    {
        if (!_email.IsConfigured(out var diag)) { MessageBox.Show(diag, "Correo", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (_cbxFolder.SelectedItem is not string folder) return;
        try
        {
            SetBusy(true, "Importando...");
            int n = await _email.IngestRequirementsAsync(folder);
            SetStatus($"Importados {n} requerimiento(s) desde '{folder}'.", AppTheme.Success);
            await LoadMessagesAsync();
            MessageBox.Show($"Se importaron {n} requerimiento(s) desde los correos no leídos.", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { SetStatus("Error: " + ex.Message, AppTheme.Danger); MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy, string? status = null) { Cursor = busy ? Cursors.WaitCursor : Cursors.Default; if (status != null) SetStatus(status, AppTheme.TextSecondary); }
    private void SetStatus(string t, Color c) { _lblStatus.Text = t; _lblStatus.ForeColor = c; }

    protected override async void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) await EnsureFoldersAsync();
    }
}
