namespace Administrador_Desarrollo_Web.Forms.Controls;

public partial class DeploymentControl
{
    private RichTextBox _txtBackupLog = null!;
    private Button _btnBackupAzure = null!, _btnBackupLocal = null!;
    private Label _lblBackupStatus = null!;

    private TabPage BuildBackupTab()
    {
        var (tab, body) = NewTab("  💾  Respaldo BD  ", 120);

        var top = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 6), BackColor = AppTheme.ContentBg };
        top.Controls.Add(new Label
        {
            Text = "Respalda la base de datos SQLite a Azure Blob Storage o a una carpeta local.\nLa copia en Azure requiere configurar la connection string en Configuración.",
            Location = new Point(0, 4), AutoSize = false, Size = new Size(900, 40), Font = AppTheme.DefaultFont, ForeColor = AppTheme.TextSecondary
        });
        _btnBackupAzure = AppTheme.MakePrimaryButton("☁ Respaldar en Azure", 200, 34); _btnBackupAzure.Location = new Point(0, 52); _btnBackupAzure.Click += BtnBackupAzure_Click;
        _btnBackupLocal = AppTheme.MakeSecondaryButton("📁 Copia local", 160, 34); _btnBackupLocal.Location = new Point(210, 52); _btnBackupLocal.Click += BtnBackupLocal_Click;
        _lblBackupStatus = new Label { Location = new Point(385, 60), AutoSize = true, Font = AppTheme.BoldFont };
        top.Controls.AddRange([_btnBackupAzure, _btnBackupLocal, _lblBackupStatus]);

        _txtBackupLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(20, 24, 33), ForeColor = Color.FromArgb(220, 230, 240), Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None };
        var pLog = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 0, 20, 16), BackColor = AppTheme.ContentBg }; pLog.Controls.Add(_txtBackupLog);

        body.Controls.Add(top, 0, 0);
        body.Controls.Add(pLog, 0, 1);
        return tab;
    }

    private async void BtnBackupAzure_Click(object? s, EventArgs e)
    {
        if (!_blob.IsConfigured)
        {
            MessageBox.Show("Azure Blob Storage no está configurado.\nVe a Configuración y agrega la connection string.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _txtBackupLog.Clear();
        _btnBackupAzure.Enabled = _btnBackupLocal.Enabled = false;
        _lblBackupStatus.ForeColor = AppTheme.SidebarActive; _lblBackupStatus.Text = "⏳ Respaldando...";
        var progress = new Progress<string>(line => { _txtBackupLog.AppendText(line + Environment.NewLine); });
        try
        {
            var url = await _backup.BackupToAzureAsync(progress);
            _lblBackupStatus.ForeColor = AppTheme.Success; _lblBackupStatus.Text = "✅ Respaldo completado.";
        }
        catch (Exception ex)
        {
            _txtBackupLog.AppendText($"❌ Error: {ex.Message}{Environment.NewLine}");
            _lblBackupStatus.ForeColor = AppTheme.Danger; _lblBackupStatus.Text = "❌ Error.";
        }
        finally { _btnBackupAzure.Enabled = _btnBackupLocal.Enabled = true; }
    }

    private void BtnBackupLocal_Click(object? s, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "Carpeta destino del respaldo", UseDescriptionForTitle = true };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            _backup.BackupLocalCopy(dlg.SelectedPath);
            _lblBackupStatus.ForeColor = AppTheme.Success; _lblBackupStatus.Text = "✅ Copia local creada.";
            _txtBackupLog.AppendText($"✅ Copia local guardada en: {dlg.SelectedPath}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            _lblBackupStatus.ForeColor = AppTheme.Danger; _lblBackupStatus.Text = "❌ Error.";
            _txtBackupLog.AppendText($"❌ Error: {ex.Message}{Environment.NewLine}");
        }
    }
}
