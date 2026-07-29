using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Reasignar un work item de DevOps. Se puede elegir un desarrollador de la lista O escribir el
/// correo de CUALQUIER usuario de DevOps (aunque no esté registrado en la app), o quitar la
/// asignación. El correo es lo que DevOps entiende como identidad.
/// </summary>
public class ReassignWorkItemForm : Form
{
    private ComboBox _cbo = null!;
    private CheckBox _chkQuitar = null!;
    private readonly List<(string Email, int DevId)> _devs = [];

    /// <summary>Correo destino; vacío = quitar asignación.</summary>
    public string SelectedEmail { get; private set; } = "";
    /// <summary>Desarrollador destino registrado (para el aviso); null si es un usuario externo o se quitó.</summary>
    public int? SelectedDeveloperId { get; private set; }

    public ReassignWorkItemForm(int externalId, string title, string asignadoActual, IReadOnlyList<Developer> devs)
    {
        BuildUI(externalId, title, asignadoActual, devs);
    }

    private void BuildUI(int externalId, string title, string asignadoActual, IReadOnlyList<Developer> devs)
    {
        Text = "Reasignar ticket";
        Size = new Size(540, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        int y = 18;
        Controls.Add(new Label { Text = $"#{externalId} — {title}", Location = new Point(20, y), AutoSize = false, Size = new Size(490, 22), Font = AppTheme.BoldFont, AutoEllipsis = true });
        y += 30;
        Controls.Add(new Label { Text = $"Asignado actualmente a: {(string.IsNullOrWhiteSpace(asignadoActual) ? "(sin asignar)" : asignadoActual)}", Location = new Point(20, y), AutoSize = false, Size = new Size(490, 20), ForeColor = AppTheme.TextSecondary });
        y += 34;

        Controls.Add(new Label { Text = "Reasignar a (elige de la lista o escribe cualquier correo):", Location = new Point(20, y), AutoSize = true });
        y += 24;
        // Editable: se puede teclear el correo de un usuario que no está en la lista.
        _cbo = new ComboBox { Location = new Point(20, y), Width = 490, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
        foreach (var d in devs.Where(d => !string.IsNullOrWhiteSpace(d.Email)).OrderBy(d => d.FullName))
        {
            _cbo.Items.Add($"{d.FullName}  ·  {d.Email}");
            _devs.Add((d.Email!.Trim(), d.Id));
        }
        Controls.Add(_cbo);
        y += 38;

        _chkQuitar = new CheckBox { Text = "Quitar asignación (dejar sin asignar)", Location = new Point(20, y), AutoSize = true };
        _chkQuitar.CheckedChanged += (_, _) => _cbo.Enabled = !_chkQuitar.Checked;
        Controls.Add(_chkQuitar);
        y += 30;

        var btnOk = AppTheme.MakePrimaryButton("Reasignar", 120); btnOk.Location = new Point(270, 220); btnOk.Click += BtnOk_Click;
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Location = new Point(400, 220); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.AddRange([btnOk, btnCancel]);
        AcceptButton = btnOk;
    }

    private void BtnOk_Click(object? s, EventArgs e)
    {
        if (_chkQuitar.Checked)
        {
            SelectedEmail = ""; SelectedDeveloperId = null;
            DialogResult = DialogResult.OK; Close();
            return;
        }

        // Elegido de la lista → usar ese desarrollador. Tecleado libre → tomar el correo del texto.
        string email;
        if (_cbo.SelectedIndex >= 0 && _cbo.SelectedIndex < _devs.Count)
        {
            email = _devs[_cbo.SelectedIndex].Email;
            SelectedDeveloperId = _devs[_cbo.SelectedIndex].DevId;
        }
        else
        {
            var texto = _cbo.Text.Trim();
            int sep = texto.LastIndexOf('·');
            email = (sep >= 0 ? texto[(sep + 1)..] : texto).Trim();
            // Si el correo tecleado coincide con un desarrollador registrado, se avisa a su cuenta.
            SelectedDeveloperId = _devs.FirstOrDefault(d => string.Equals(d.Email, email, StringComparison.OrdinalIgnoreCase)).DevId is int id and > 0 ? id : null;
        }

        if (email.Length == 0 || !email.Contains('@'))
        {
            MessageBox.Show("Escribe un correo válido o marca «Quitar asignación».", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SelectedEmail = email;
        DialogResult = DialogResult.OK;
        Close();
    }
}
