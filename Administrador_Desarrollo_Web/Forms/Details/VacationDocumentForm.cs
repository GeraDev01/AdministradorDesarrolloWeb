using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Genera el documento de solicitud de vacaciones desde la plantilla, lo muestra
/// como PDF (WebView2), permite firmarlo con una firma preguardada del gerente y
/// exportar el PDF firmado.
/// </summary>
public class VacationDocumentForm : Form
{
    private readonly AppDbContext _db;
    private readonly VacationDocumentService _docSvc;
    private readonly IDocxToPdfConverter _converter;
    private readonly SignatureService _sig;
    private readonly SettingsService _settings;
    private readonly CurrentUserContext _currentUser;
    private readonly AuditService _audit;

    private readonly VacationRequest _req;
    private Developer _dev = null!;
    private VacationDocument? _doc;

    private WebView2 _web = null!;
    private ComboBox _cbxFirma = null!;
    private Label _lblStatus = null!;
    private Button _btnGen = null!, _btnSign = null!, _btnExport = null!, _btnFirmas = null!;

    private byte[]? _currentPdf;
    private byte[]? _currentDocx;
    private bool _isSigned;
    private string? _tempPdfPath;

    public VacationDocumentForm(AppDbContext db, VacationDocumentService docSvc, IDocxToPdfConverter converter,
        SignatureService sig, SettingsService settings, CurrentUserContext currentUser, AuditService audit,
        VacationRequest req)
    {
        _db = db; _docSvc = docSvc; _converter = converter; _sig = sig;
        _settings = settings; _currentUser = currentUser; _audit = audit; _req = req;

        _dev = _db.Developers.AsNoTracking().FirstOrDefault(d => d.Id == req.DeveloperId)
               ?? new Developer { FullName = req.Developer?.FullName ?? "—" };
        _doc = _db.VacationDocuments.FirstOrDefault(d => d.VacationRequestId == req.Id);

        BuildUI();
        Load += async (_, _) => await InitAsync();
    }

    private void BuildUI()
    {
        Text = "Documento de solicitud de vacaciones";
        Size = new Size(1080, 760);
        MinimumSize = new Size(900, 560);
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
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label
        {
            Text = $"  📄  Solicitud de vacaciones — {_dev.FullName}",
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 9, 10, 6), BackColor = AppTheme.ContentBg };
        _btnGen = AppTheme.MakeSecondaryButton("🔄 Generar / Previsualizar", 190); _btnGen.Margin = new Padding(0, 0, 8, 0); _btnGen.Click += async (_, _) => await GeneratePreviewAsync(sign: false);
        toolbar.Controls.Add(_btnGen);

        toolbar.Controls.Add(new Label { Text = "Firma:", AutoSize = true, Margin = new Padding(4, 8, 4, 0), ForeColor = AppTheme.TextSecondary });
        _cbxFirma = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 8, 0) };
        toolbar.Controls.Add(_cbxFirma);

        _btnFirmas = AppTheme.MakeSecondaryButton("🖊 Firmas...", 110); _btnFirmas.Margin = new Padding(0, 0, 8, 0); _btnFirmas.Click += BtnFirmas_Click;
        toolbar.Controls.Add(_btnFirmas);

        _btnSign = AppTheme.MakePrimaryButton("✍ Firmar y exportar PDF", 200); _btnSign.Margin = new Padding(0, 0, 8, 0); _btnSign.BackColor = AppTheme.Success; _btnSign.Click += async (_, _) => await SignAndExportAsync();
        toolbar.Controls.Add(_btnSign);

        _btnExport = AppTheme.MakeSecondaryButton("💾 Exportar PDF...", 150); _btnExport.Margin = new Padding(0, 0, 8, 0); _btnExport.Click += BtnExport_Click;
        toolbar.Controls.Add(_btnExport);

        _lblStatus = new Label { Text = "", AutoSize = true, Margin = new Padding(6, 8, 4, 0), ForeColor = AppTheme.TextSecondary };
        toolbar.Controls.Add(_lblStatus);

        _web = new WebView2 { Dock = DockStyle.Fill };
        var pnlWeb = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlWeb.Controls.Add(_web);

        outer.Controls.Add(hdr,     0, 0);
        outer.Controls.Add(toolbar, 0, 1);
        outer.Controls.Add(pnlWeb,  0, 2);
        Controls.Add(outer);

        FormClosed += (_, _) => Cleanup();
    }

    private async Task InitAsync()
    {
        RefreshFirmaCombo();
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null,
                Path.Combine(Path.GetTempPath(), "advweb_webview2"));
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo iniciar el visor (WebView2):\n{ex.Message}", "Visor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (!_converter.IsAvailable(out var diag))
            SetStatus("⚠ " + diag, AppTheme.Warning);

        // Si ya hay un PDF firmado guardado, mostrarlo; si no, generar borrador.
        if (_doc?.SignedPdfBytes is { Length: > 0 } pdf)
        {
            _currentPdf = pdf; _isSigned = true;
            await ShowPdfAsync(pdf);
            SetStatus("✅ Documento firmado.", AppTheme.Success);
        }
        else
        {
            await GeneratePreviewAsync(sign: false);
        }
    }

    private void RefreshFirmaCombo()
    {
        var firmas = _sig.GetVisible(null); // firmas compartidas / del gerente
        _cbxFirma.Items.Clear();
        foreach (var f in firmas) _cbxFirma.Items.Add(new FirmaItem(f));
        if (_cbxFirma.Items.Count > 0) _cbxFirma.SelectedIndex = 0;
    }

    private SignatureProfile? SelectedFirma() => (_cbxFirma.SelectedItem as FirmaItem)?.Profile;

    private VacationDocFields BuildFields()
    {
        var departamento = _settings.Get(SettingsService.Keys.VacationDepartamento);
        if (string.IsNullOrWhiteSpace(departamento)) departamento = "DESARROLLO";
        var puesto = _settings.Get(SettingsService.Keys.VacationPuestoDefault);
        if (string.IsNullOrWhiteSpace(puesto)) puesto = _dev.Seniority ?? "Desarrollador";
        var jefe = _settings.Get(SettingsService.Keys.VacationJefeDirecto);
        if (string.IsNullOrWhiteSpace(jefe)) jefe = _currentUser.User?.FullName ?? "";

        return VacationDocumentService.BuildFields(_req, _dev, departamento!, puesto!, jefe!);
    }

    private async Task GeneratePreviewAsync(bool sign)
    {
        if (!_docSvc.TemplateExists)
        {
            SetStatus("⚠ No se encontró la plantilla en la carpeta Plantillas.", AppTheme.Danger);
            return;
        }

        SignaturePng? firma = null;
        if (sign)
        {
            var sel = SelectedFirma();
            if (sel == null) { MessageBox.Show("No hay firma seleccionada. Crea una en 'Firmas...'.", "Firma", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            firma = new SignaturePng(sel.PngBytes, sel.WidthPx, sel.HeightPx);
        }

        SetBusy(true, sign ? "Firmando y convirtiendo a PDF..." : "Generando documento...");
        _isSigned = false;
        try
        {
            var fields = BuildFields();
            byte[] docx = await Task.Run(() => _docSvc.GenerateDocx(fields, firma));
            _currentDocx = docx;

            byte[] pdf = await _converter.ConvertAsync(docx);
            _currentPdf = pdf;
            _isSigned = sign;

            await ShowPdfAsync(pdf);
            SetStatus(sign ? "✅ Firmado. Recuerda exportar o se guarda en la BD." : "Borrador (sin firma).",
                      sign ? AppTheme.Success : AppTheme.TextSecondary);
        }
        catch (Exception ex)
        {
            HandleConversionError(ex);
        }
        finally { SetBusy(false); }
    }

    private async Task SignAndExportAsync()
    {
        var sel = SelectedFirma();
        if (sel == null)
        {
            if (MessageBox.Show("No hay firma preguardada. ¿Crear una ahora?", "Firma", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                BtnFirmas_Click(this, EventArgs.Empty);
            return;
        }

        await GeneratePreviewAsync(sign: true);
        if (!_isSigned || _currentPdf == null) return;

        // Persistir el documento firmado
        try
        {
            _doc ??= new VacationDocument { VacationRequestId = _req.Id, CreatedAtUtc = DateTime.UtcNow };
            _doc.Source = VacationDocSource.Generado;
            _doc.Status = VacationDocStatus.Firmado;
            _doc.FileName = $"Solicitud_Vacaciones_{_dev.FullName}_{_req.StartDate:yyyyMMdd}.pdf".Replace(' ', '_');
            _doc.DocxBytes = _currentDocx;
            _doc.SignedPdfBytes = _currentPdf;
            _doc.PdfChecksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_currentPdf));
            _doc.SignatureProfileId = sel.Id;
            _doc.SignedByUserId = _currentUser.User?.Id;
            _doc.SignedAtUtc = DateTime.UtcNow;

            if (_doc.Id == 0) _db.VacationDocuments.Add(_doc);
            _db.SaveChanges();
            _audit.Record(AuditAction.Update, "VacationDocument", _doc.Id.ToString(), $"Firmado: {_dev.FullName}");
        }
        catch (DbUpdateConcurrencyException)
        {
            MessageBox.Show("Otro usuario modificó este documento. Vuelve a abrirlo.", "Conflicto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Ofrecer exportar de inmediato
        BtnExport_Click(this, EventArgs.Empty);
    }

    private void BtnFirmas_Click(object? s, EventArgs e)
    {
        using var frm = new SignatureManagerForm(_sig);
        frm.ShowDialog(this);
        RefreshFirmaCombo();
    }

    private void BtnExport_Click(object? s, EventArgs e)
    {
        if (_currentPdf == null) { MessageBox.Show("Genera el documento primero.", "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var dlg = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"Solicitud_Vacaciones_{_dev.FullName}_{_req.StartDate:yyyyMMdd}.pdf".Replace(' ', '_')
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, _currentPdf);
            SetStatus($"💾 Exportado: {dlg.FileName}", AppTheme.Success);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo guardar el PDF:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ShowPdfAsync(byte[] pdf)
    {
        if (_web.CoreWebView2 == null) return;
        try
        {
            _tempPdfPath ??= Path.Combine(Path.GetTempPath(), "advweb_preview_" + Guid.NewGuid().ToString("N") + ".pdf");
            await File.WriteAllBytesAsync(_tempPdfPath, pdf);
            _web.CoreWebView2.Navigate(new Uri(_tempPdfPath).AbsoluteUri);
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo mostrar el PDF: " + ex.Message, AppTheme.Danger);
        }
    }

    private void HandleConversionError(Exception ex)
    {
        if (!_converter.IsAvailable(out var diag))
        {
            SetStatus("⚠ " + diag, AppTheme.Danger);
            var msg = diag + "\n\n¿Deseas exportar el documento en Word (.docx) mientras tanto?";
            if (_currentDocx != null && MessageBox.Show(msg, "LibreOffice no disponible", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                ExportDocx();
        }
        else
        {
            SetStatus("Error al convertir: " + ex.Message, AppTheme.Danger);
            MessageBox.Show($"No se pudo convertir a PDF:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportDocx()
    {
        if (_currentDocx == null) return;
        using var dlg = new SaveFileDialog { Filter = "Word (*.docx)|*.docx", FileName = "Solicitud_Vacaciones.docx" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllBytes(dlg.FileName, _currentDocx);
        SetStatus($"💾 DOCX exportado: {dlg.FileName}", AppTheme.Success);
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _btnGen.Enabled = _btnSign.Enabled = _btnExport.Enabled = _btnFirmas.Enabled = _cbxFirma.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        if (status != null) SetStatus(status, AppTheme.TextSecondary);
    }

    private void SetStatus(string text, Color color)
    {
        _lblStatus.Text = text;
        _lblStatus.ForeColor = color;
    }

    private void Cleanup()
    {
        try { if (_tempPdfPath != null && File.Exists(_tempPdfPath)) File.Delete(_tempPdfPath); } catch { }
        try { _web?.Dispose(); } catch { }
    }

    private sealed class FirmaItem(SignatureProfile p)
    {
        public SignatureProfile Profile { get; } = p;
        public override string ToString() => Profile.IsDefault ? $"⭐ {Profile.DisplayName}" : Profile.DisplayName;
    }
}
