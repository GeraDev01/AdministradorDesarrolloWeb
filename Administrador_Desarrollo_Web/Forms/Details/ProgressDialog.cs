namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>Diálogo modal-no-bloqueante con log y barra de progreso para tareas largas.</summary>
public class ProgressDialog : ResponsiveForm
{
    private RichTextBox _txtLog = null!;
    private Button _btnClose = null!, _btnCancel = null!;
    private readonly CancellationTokenSource _cts = new();

    public CancellationToken Token => _cts.Token;

    public ProgressDialog(string title)
    {
        BuildUI(title);
    }

    private void BuildUI(string title)
    {
        Text = title; Size = new Size(620, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        ControlBox = false; BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg };
        hdr.Controls.Add(new Label { Text = "  ⏳  " + title, Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        _txtLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(20, 24, 33), ForeColor = Color.FromArgb(220, 230, 240), Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None };
        var pLog = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 4), BackColor = AppTheme.ContentBg }; pLog.Controls.Add(_txtLog);

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(12, 8, 12, 8) };
        _btnClose = AppTheme.MakePrimaryButton("Cerrar", 100); _btnClose.Enabled = false; _btnClose.Click += (_, _) => Close();
        _btnCancel = AppTheme.MakeDangerButton("Cancelar", 100); _btnCancel.Click += (_, _) => { _cts.Cancel(); _btnCancel.Enabled = false; };
        btnRow.Controls.AddRange([_btnClose, _btnCancel]);

        tbl.Controls.Add(hdr, 0, 0);
        tbl.Controls.Add(pLog, 0, 1);
        tbl.Controls.Add(btnRow, 0, 2);
        Controls.Add(tbl);
    }

    public void Append(string line)
    {
        if (IsDisposed || _txtLog.IsDisposed) return;
        _txtLog.AppendText(line + Environment.NewLine);
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.ScrollToCaret();
        // Sin Application.DoEvents(): reentraba en el bucle de mensajes y permitía volver a pulsar
        // el botón que lanzó la tarea, disparando una segunda ejecución encima de la primera.
        // Las tareas largas ahora corren fuera del hilo de UI, así que la ventana se repinta sola.
    }

    public void MarkDone()
    {
        _btnClose.Enabled = true;
        _btnCancel.Enabled = false;
        ControlBox = true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_btnClose.Enabled) { e.Cancel = true; return; } // no cerrar mientras corre
        base.OnFormClosing(e);
    }
}
