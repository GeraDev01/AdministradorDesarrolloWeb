using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>Alta/edición de un hito de un desarrollador (logro, proyecto, certificación…).</summary>
public class DeveloperMilestoneForm : ResponsiveForm
{
    private DateTimePicker _dtp = null!;
    private ComboBox _cbKind = null!;
    private TextBox _txtTitle = null!, _txtDesc = null!;

    private static readonly MilestoneKind[] Kinds =
        [MilestoneKind.Logro, MilestoneKind.Proyecto, MilestoneKind.Certificacion, MilestoneKind.Reconocimiento, MilestoneKind.Otro];

    public DeveloperMilestone Result { get; private set; }

    public DeveloperMilestoneForm(DeveloperMilestone? existing = null)
    {
        Result = existing ?? new DeveloperMilestone();
        BuildUI();
        if (existing != null) Populate(existing);
    }

    private void BuildUI()
    {
        Text = "Hito del desarrollador"; Size = new Size(560, 400);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        int y = 16;
        Controls.Add(new Label { Text = "Fecha", Location = new Point(20, y), AutoSize = true });
        Controls.Add(new Label { Text = "Tipo", Location = new Point(240, y), AutoSize = true }); y += 20;
        _dtp = new DateTimePicker { Location = new Point(20, y), Width = 200, Format = DateTimePickerFormat.Short };
        _cbKind = new ComboBox { Location = new Point(240, y), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbKind.Items.AddRange(["🏆 Logro", "📁 Proyecto", "🎓 Certificación", "⭐ Reconocimiento", "• Otro"]);
        _cbKind.SelectedIndex = 0;
        Controls.AddRange([_dtp, _cbKind]); y += 40;

        Controls.Add(new Label { Text = "Título *", Location = new Point(20, y), AutoSize = true }); y += 20;
        _txtTitle = new TextBox { Location = new Point(20, y), Width = 500 };
        Controls.Add(_txtTitle); y += 38;

        Controls.Add(new Label { Text = "Descripción", Location = new Point(20, y), AutoSize = true }); y += 20;
        _txtDesc = new TextBox { Location = new Point(20, y), Width = 500, Height = 120, Multiline = true, ScrollBars = ScrollBars.Vertical };
        Controls.Add(_txtDesc); y += 130;

        var btnSave = AppTheme.MakePrimaryButton("Guardar", 110); btnSave.Location = new Point(300, y); btnSave.Click += BtnSave_Click;
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Location = new Point(420, y); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.AddRange([btnSave, btnCancel]);
        AcceptButton = btnSave;
    }

    private void Populate(DeveloperMilestone m)
    {
        _dtp.Value = m.Date == default ? DateTime.Today : m.Date;
        _cbKind.SelectedIndex = Math.Max(0, Array.IndexOf(Kinds, m.Kind));
        _txtTitle.Text = m.Title;
        _txtDesc.Text = m.Description ?? "";
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtTitle.Text))
        { MessageBox.Show("El título es obligatorio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        Result.Date = _dtp.Value.Date;
        Result.Kind = Kinds[Math.Max(0, _cbKind.SelectedIndex)];
        Result.Title = _txtTitle.Text.Trim();
        Result.Description = string.IsNullOrWhiteSpace(_txtDesc.Text) ? null : _txtDesc.Text.Trim();
        DialogResult = DialogResult.OK; Close();
    }
}
