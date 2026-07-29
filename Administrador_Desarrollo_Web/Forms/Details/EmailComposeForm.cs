using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>Redacta y envía un correo (SMTP) opcionalmente con un adjunto.</summary>
public class EmailComposeForm : Form
{
    private readonly EmailService _email;
    private TextBox _txtTo = null!, _txtSubject = null!, _txtBody = null!;
    private Label _lblAttach = null!;
    private string? _attachmentPath;
    private Button _btnSend = null!;

    public EmailComposeForm(EmailService email, string? toPrefill = null, string? subjectPrefill = null, string? attachmentPath = null)
    {
        _email = email; _attachmentPath = attachmentPath;
        BuildUI();
        if (toPrefill != null) _txtTo.Text = toPrefill;
        if (subjectPrefill != null) _txtSubject.Text = subjectPrefill;
        UpdateAttachLabel();
    }

    private void BuildUI()
    {
        Text = "Redactar correo"; Size = new Size(620, 540);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable; MinimizeBox = false; MinimumSize = new Size(500, 420);
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  ✉  Redactar correo", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(16, 12, 16, 8), Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));

        _txtTo = LabeledBox(body, "Para (separa varios con coma):", 0);
        _txtSubject = LabeledBox(body, "Asunto:", 1);

        body.Controls.Add(new Label { Text = "Mensaje:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        _txtBody = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Margin = new Padding(0, 2, 0, 4) };
        body.Controls.Add(_txtBody, 0, 3);

        var attachRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        var btnAttach = AppTheme.MakeSecondaryButton("📎 Adjuntar", 110); btnAttach.Margin = new Padding(0, 2, 8, 0); btnAttach.Click += BtnAttach_Click;
        var btnClear = AppTheme.MakeSecondaryButton("✖", 34); btnClear.Margin = new Padding(0, 2, 8, 0); btnClear.Click += (_, _) => { _attachmentPath = null; UpdateAttachLabel(); };
        _lblAttach = new Label { Text = "(sin adjunto)", AutoSize = true, Margin = new Padding(0, 8, 0, 0), ForeColor = AppTheme.TextSecondary };
        attachRow.Controls.AddRange([btnAttach, btnClear, _lblAttach]);
        body.Controls.Add(attachRow, 0, 4);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        _btnSend = AppTheme.MakePrimaryButton("Enviar", 110); _btnSend.Click += BtnSend_Click;
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btns.Controls.AddRange([_btnSend, btnCancel]);

        outer.Controls.Add(hdr, 0, 0); outer.Controls.Add(body, 0, 1); outer.Controls.Add(btns, 0, 2);
        Controls.Add(outer); AcceptButton = _btnSend;
    }

    private static TextBox LabeledBox(TableLayoutPanel parent, string label, int row)
    {
        var pnl = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        pnl.Controls.Add(new Label { Text = label, Dock = DockStyle.Top, Height = 18 });
        var box = new TextBox { Dock = DockStyle.Top, Margin = new Padding(0, 2, 0, 0) };
        pnl.Controls.Add(box); box.BringToFront();
        parent.Controls.Add(pnl, 0, row);
        return box;
    }

    private void BtnAttach_Click(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Title = "Adjuntar archivo" };
        if (dlg.ShowDialog(this) == DialogResult.OK) { _attachmentPath = dlg.FileName; UpdateAttachLabel(); }
    }

    private void UpdateAttachLabel()
    {
        _lblAttach.Text = _attachmentPath == null ? "(sin adjunto)" : "📎 " + Path.GetFileName(_attachmentPath);
        _lblAttach.ForeColor = _attachmentPath == null ? AppTheme.TextSecondary : AppTheme.Success;
    }

    private async void BtnSend_Click(object? s, EventArgs e)
    {
        var to = _txtTo.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (to.Length == 0) { MessageBox.Show("Indica al menos un destinatario.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        _btnSend.Enabled = false; Cursor = Cursors.WaitCursor;
        try
        {
            await _email.SendAsync(to, _txtSubject.Text.Trim(), _txtBody.Text, _attachmentPath);
            MessageBox.Show("Correo enviado.", "Listo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo enviar el correo:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _btnSend.Enabled = true; Cursor = Cursors.Default; }
    }
}
