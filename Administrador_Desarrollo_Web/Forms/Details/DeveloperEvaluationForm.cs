using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>Alta/edición de una evaluación de líder: fecha, periodo, calificación, fortalezas,
/// debilidades y comentarios.</summary>
public class DeveloperEvaluationForm : Form
{
    private DateTimePicker _dtp = null!;
    private TextBox _txtPeriod = null!, _txtStrengths = null!, _txtWeaknesses = null!, _txtComments = null!;
    private ComboBox _cbRating = null!;

    public DeveloperEvaluation Result { get; private set; }

    public DeveloperEvaluationForm(DeveloperEvaluation? existing = null)
    {
        Result = existing ?? new DeveloperEvaluation();
        BuildUI();
        if (existing != null) Populate(existing);
    }

    private void BuildUI()
    {
        Text = "Evaluación del líder"; Size = new Size(580, 620);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        int y = 16;
        Controls.Add(new Label { Text = "Fecha de la evaluación", Location = new Point(20, y), AutoSize = true });
        Controls.Add(new Label { Text = "Periodo (opcional)", Location = new Point(300, y), AutoSize = true }); y += 20;
        _dtp = new DateTimePicker { Location = new Point(20, y), Width = 260, Format = DateTimePickerFormat.Short };
        _txtPeriod = new TextBox { Location = new Point(300, y), Width = 240, PlaceholderText = "2026 Q3, Semestre 1…" };
        Controls.AddRange([_dtp, _txtPeriod]); y += 38;

        Controls.Add(new Label { Text = "Calificación global", Location = new Point(20, y), AutoSize = true }); y += 20;
        _cbRating = new ComboBox { Location = new Point(20, y), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbRating.Items.AddRange(["(sin calificar)", "1 - Deficiente", "2 - Bajo", "3 - Aceptable", "4 - Bueno", "5 - Sobresaliente"]);
        _cbRating.SelectedIndex = 0;
        Controls.Add(_cbRating); y += 40;

        AddArea("Fortalezas", ref _txtStrengths, ref y, "Una por renglón: lo que hace muy bien.");
        AddArea("Debilidades / áreas de mejora", ref _txtWeaknesses, ref y, "Una por renglón: en qué puede crecer.");
        AddArea("Comentarios", ref _txtComments, ref y, null);

        var btnSave = AppTheme.MakePrimaryButton("Guardar", 110); btnSave.Location = new Point(320, y + 4); btnSave.Click += BtnSave_Click;
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Location = new Point(440, y + 4); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.AddRange([btnSave, btnCancel]);
        AcceptButton = btnSave;
    }

    private void AddArea(string label, ref TextBox box, ref int y, string? hint)
    {
        Controls.Add(new Label { Text = label, Location = new Point(20, y), AutoSize = true }); y += 20;
        box = new TextBox { Location = new Point(20, y), Width = 520, Height = 78, Multiline = true, ScrollBars = ScrollBars.Vertical };
        Controls.Add(box); y += 82;
        if (hint != null) { Controls.Add(new Label { Text = hint, Location = new Point(20, y), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary }); y += 18; }
        else y += 4;
    }

    private void Populate(DeveloperEvaluation e)
    {
        _dtp.Value = e.EvaluationDate == default ? DateTime.Today : e.EvaluationDate;
        _txtPeriod.Text = e.PeriodLabel ?? "";
        _cbRating.SelectedIndex = e.OverallRating is >= 1 and <= 5 ? e.OverallRating.Value : 0;
        _txtStrengths.Text = e.Strengths ?? "";
        _txtWeaknesses.Text = e.Weaknesses ?? "";
        _txtComments.Text = e.Comments ?? "";
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtStrengths.Text) && string.IsNullOrWhiteSpace(_txtWeaknesses.Text) && string.IsNullOrWhiteSpace(_txtComments.Text) && _cbRating.SelectedIndex == 0)
        { MessageBox.Show("Captura al menos una calificación o algún texto.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        Result.EvaluationDate = _dtp.Value.Date;
        Result.PeriodLabel = string.IsNullOrWhiteSpace(_txtPeriod.Text) ? null : _txtPeriod.Text.Trim();
        Result.OverallRating = _cbRating.SelectedIndex >= 1 ? _cbRating.SelectedIndex : null;
        Result.Strengths = string.IsNullOrWhiteSpace(_txtStrengths.Text) ? null : _txtStrengths.Text.Trim();
        Result.Weaknesses = string.IsNullOrWhiteSpace(_txtWeaknesses.Text) ? null : _txtWeaknesses.Text.Trim();
        Result.Comments = string.IsNullOrWhiteSpace(_txtComments.Text) ? null : _txtComments.Text.Trim();
        DialogResult = DialogResult.OK; Close();
    }
}
