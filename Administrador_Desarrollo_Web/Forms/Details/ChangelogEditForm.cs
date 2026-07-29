namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Changelog de una versión. En modo <paramref name="soloLectura"/> se usa como visor: quien
/// despliega necesita saber qué trae la versión que va a subir, pero editar el historial de
/// cambios es del administrador.
/// </summary>
public class ChangelogEditForm : Form
{
    private RichTextBox _rt = null!;
    public string Changelog => _rt.Text.Trim();

    public ChangelogEditForm(string version, string? current, bool soloLectura = false)
    {
        BuildUI(version, soloLectura);
        _rt.Text = string.IsNullOrWhiteSpace(current)
            ? (soloLectura ? "(esta versión no tiene changelog registrado)" : "")
            : current;
        _rt.ReadOnly = soloLectura;
        if (soloLectura)
        {
            _rt.BackColor = AppTheme.ContentBg;
            _rt.ForeColor = AppTheme.TextPrimary;
        }
    }

    private void BuildUI(string version, bool soloLectura)
    {
        Text = $"Changelog — versión {version}"; Size = new Size(560, 460);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg };
        hdr.Controls.Add(new Label
        {
            Text = $"  📝  Changelog de la versión {version}" + (soloLectura ? "   (solo lectura)" : ""),
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont,
            TextAlign = ContentAlignment.MiddleLeft
        });

        _rt = new RichTextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10f) };
        var pBody = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 8), BackColor = AppTheme.ContentBg }; pBody.Controls.Add(_rt);

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(12, 8, 12, 8) };
        if (soloLectura)
        {
            // Sin botón Guardar: que no exista es más claro que tenerlo deshabilitado.
            var btnClose = AppTheme.MakePrimaryButton("Cerrar", 100);
            btnClose.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            btnRow.Controls.Add(btnClose);
            AcceptButton = btnClose;
        }
        else
        {
            var btnSave = AppTheme.MakePrimaryButton("Guardar", 100); btnSave.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
            var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            btnRow.Controls.AddRange([btnSave, btnCancel]);
            AcceptButton = btnSave;
        }

        tbl.Controls.Add(hdr, 0, 0);
        tbl.Controls.Add(pBody, 0, 1);
        tbl.Controls.Add(btnRow, 0, 2);
        Controls.Add(tbl);
    }
}
