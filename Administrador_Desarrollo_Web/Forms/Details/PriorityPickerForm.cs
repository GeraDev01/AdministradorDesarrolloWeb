using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Diálogo para elegir la prioridad (1..4) de un work item de Azure DevOps, donde 1 es la más alta.
/// Devuelve la prioridad seleccionada en <see cref="SelectedPriority"/>.
/// </summary>
public class PriorityPickerForm : ResponsiveForm
{
    private ComboBox _cbx = null!;

    /// <summary>Prioridad elegida (1..4). Solo válida si el diálogo se cerró con OK.</summary>
    public int SelectedPriority { get; private set; }

    public PriorityPickerForm(int externalId, string? currentPriority)
    {
        BuildUI(externalId, currentPriority);
    }

    private void BuildUI(int externalId, string? currentPriority)
    {
        Text = $"Cambiar prioridad — #{externalId}";
        Size = new Size(420, 210);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        Controls.Add(new Label
        {
            Text = "Nueva prioridad (se actualiza en Azure DevOps):",
            Location = new Point(20, 18), AutoSize = true, Font = AppTheme.BoldFont
        });

        _cbx = new ComboBox { Location = new Point(20, 46), Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var p in SlaPolicyStore.Prioridades)
            _cbx.Items.Add($"{p} — {SlaPolicyStore.NombrePrioridad(p)}");
        // Preseleccionar la actual si es un número válido; si no, dejar «Media» (3).
        _cbx.SelectedIndex = int.TryParse((currentPriority ?? "").Trim(), out var actual) && actual is >= 1 and <= 4
            ? actual - 1
            : 2;
        Controls.Add(_cbx);

        Controls.Add(new Label
        {
            Text = "Si hay una política de SLA para esa prioridad, el compromiso se ajusta automáticamente.",
            Location = new Point(20, 82), AutoSize = false, Size = new Size(360, 34),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });

        var btnOk = AppTheme.MakePrimaryButton("Aplicar", 120);
        btnOk.Location = new Point(150, 128);
        btnOk.Click += (_, _) => { SelectedPriority = _cbx.SelectedIndex + 1; DialogResult = DialogResult.OK; Close(); };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 110);
        btnCancel.Location = new Point(278, 128);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange([btnOk, btnCancel]);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}
