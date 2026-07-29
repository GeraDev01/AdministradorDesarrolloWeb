using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>Alta/edición de un contacto (correo y/o enlace de Teams).</summary>
public class ContactDetailForm : Form
{
    private TextBox _txtName = null!, _txtTitle = null!, _txtCompany = null!, _txtEmail = null!, _txtTeams = null!, _txtPhone = null!, _txtNotes = null!;

    public Contact Result { get; private set; } = new();

    public ContactDetailForm(Contact? contact = null)
    {
        BuildUI();
        if (contact != null) Populate(contact);
    }

    private void BuildUI()
    {
        Text = "Contacto"; Size = new Size(480, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  📇  Contacto", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 8), Margin = Padding.Empty, AutoScroll = true };
        int y = 4;
        _txtName    = Field(body, "Nombre *", ref y);
        _txtTitle   = Field(body, "Puesto", ref y);
        _txtCompany = Field(body, "Empresa / Área", ref y);
        _txtEmail   = Field(body, "Correo", ref y);
        _txtTeams   = Field(body, "Enlace de Teams (https://teams.microsoft.com/l/chat/...)", ref y);
        _txtPhone   = Field(body, "Teléfono", ref y);

        body.Controls.Add(new Label { Text = "Notas", Location = new Point(24, y), AutoSize = true }); y += 22;
        _txtNotes = new TextBox { Location = new Point(24, y), Width = 410, Height = 60, Multiline = true, ScrollBars = ScrollBars.Vertical }; body.Controls.Add(_txtNotes); y += 70;
        body.AutoScrollMinSize = new Size(0, y);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnSave = AppTheme.MakePrimaryButton("Guardar", 110); btnSave.Click += BtnSave_Click;
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btns.Controls.AddRange([btnSave, btnCancel]);

        outer.Controls.Add(hdr, 0, 0); outer.Controls.Add(body, 0, 1); outer.Controls.Add(btns, 0, 2);
        Controls.Add(outer); AcceptButton = btnSave;
    }

    private static TextBox Field(Panel p, string label, ref int y)
    {
        p.Controls.Add(new Label { Text = label, Location = new Point(24, y), AutoSize = true }); y += 22;
        var box = new TextBox { Location = new Point(24, y), Width = 410 }; p.Controls.Add(box); y += 40;
        return box;
    }

    private void Populate(Contact c)
    {
        Result = c;
        _txtName.Text = c.Name; _txtTitle.Text = c.JobTitle ?? ""; _txtCompany.Text = c.Company ?? "";
        _txtEmail.Text = c.Email ?? ""; _txtTeams.Text = c.TeamsLink ?? ""; _txtPhone.Text = c.Phone ?? ""; _txtNotes.Text = c.Notes ?? "";
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtName.Text)) { MessageBox.Show("El nombre es obligatorio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Result.Name = _txtName.Text.Trim();
        Result.JobTitle = Trim(_txtTitle); Result.Company = Trim(_txtCompany); Result.Email = Trim(_txtEmail);
        Result.TeamsLink = Trim(_txtTeams); Result.Phone = Trim(_txtPhone); Result.Notes = Trim(_txtNotes);
        DialogResult = DialogResult.OK; Close();
    }

    private static string? Trim(TextBox t) => string.IsNullOrWhiteSpace(t.Text) ? null : t.Text.Trim();
}
