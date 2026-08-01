using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>Alta/edición de una actividad libre del desarrollador.</summary>
public class DevActivityForm : ResponsiveForm
{
    private TextBox _txtTitle = null!;
    private TextBox _txtDescription = null!;

    public string Titulo => _txtTitle.Text.Trim();
    public string? Descripcion => string.IsNullOrWhiteSpace(_txtDescription.Text) ? null : _txtDescription.Text.Trim();

    public DevActivityForm(DevActivity? existente = null)
    {
        BuildUI(existente != null);
        if (existente != null)
        {
            _txtTitle.Text = existente.Title;
            _txtDescription.Text = existente.Description ?? "";
        }
    }

    private void BuildUI(bool edicion)
    {
        Text = edicion ? "Editar actividad" : "Nueva actividad";
        Size = new Size(520, 380);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
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
            Text = edicion ? "  🧩  Editar actividad" : "  🧩  Nueva actividad",
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont,
            TextAlign = ContentAlignment.MiddleLeft
        });

        var body = new Panel { Dock = DockStyle.Fill, Padding = Padding.Empty };
        int y = 14;
        body.Controls.Add(new Label
        {
            Text = "Para el trabajo que no corresponde a ninguno de tus requerimientos asignados\n(soporte, juntas, investigación, apoyo a otro equipo…).",
            Location = new Point(25, y), AutoSize = false, Size = new Size(450, 34),
            ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont
        });
        y += 42;

        body.Controls.Add(new Label { Text = "Título *", Location = new Point(25, y), AutoSize = true }); y += 20;
        _txtTitle = new TextBox { Location = new Point(25, y), Width = 450, MaxLength = 200 };
        body.Controls.Add(_txtTitle); y += 40;

        body.Controls.Add(new Label { Text = "Descripción (opcional):", Location = new Point(25, y), AutoSize = true }); y += 20;
        _txtDescription = new TextBox { Location = new Point(25, y), Width = 450, Height = 100, Multiline = true, ScrollBars = ScrollBars.Vertical };
        body.Controls.Add(_txtDescription);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = AppTheme.MakePrimaryButton(edicion ? "Guardar" : "Crear", 120);
        btnSave.Click += (_, _) =>
        {
            if (Titulo.Length == 0)
            {
                MessageBox.Show("Escribe un título para la actividad.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _txtTitle.Focus();
                return;
            }
            DialogResult = DialogResult.OK; Close();
        };
        btns.Controls.AddRange([btnCancel, btnSave]);

        tbl.Controls.Add(hdr, 0, 0);
        tbl.Controls.Add(body, 0, 1);
        tbl.Controls.Add(btns, 0, 2);
        Controls.Add(tbl);
        AcceptButton = btnSave;
    }
}
