using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Estado con una nota corta («vuelvo 15:30»). La nota es opcional a propósito: obligarla haría que
/// la gente dejara de marcar su estado, y un tablero que nadie marca no sirve para nada.
/// </summary>
public class PresenceNoteForm : ResponsiveForm
{
    private ComboBox _cbxEstado = null!;
    private TextBox _txtNota = null!;

    private static readonly PresenceState[] Elegibles =
        Enum.GetValues<PresenceState>().Where(e => e != PresenceState.Ausente).ToArray();

    public PresenceState Estado => Elegibles[Math.Max(0, _cbxEstado.SelectedIndex)];
    public string Nota => _txtNota.Text.Trim();

    public PresenceNoteForm(PresenceState estadoActual, string? notaActual)
    {
        BuildUI(estadoActual, notaActual);
    }

    private void BuildUI(PresenceState estadoActual, string? notaActual)
    {
        Text = "Mi estado";
        Size = new Size(420, 230);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        Controls.Add(new Label { Text = "Estado", Location = new Point(20, 18), AutoSize = true, Font = AppTheme.BoldFont });
        _cbxEstado = new ComboBox { Location = new Point(20, 42), Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var e in Elegibles) _cbxEstado.Items.Add($"{PresenceService.Icono(e)}  {PresenceService.Etiqueta(e)}");
        int idx = Array.IndexOf(Elegibles, estadoActual);
        _cbxEstado.SelectedIndex = idx >= 0 ? idx : 0;
        Controls.Add(_cbxEstado);

        Controls.Add(new Label { Text = "Nota (opcional)", Location = new Point(20, 82), AutoSize = true, Font = AppTheme.BoldFont });
        _txtNota = new TextBox { Location = new Point(20, 106), Width = 360, MaxLength = 200, PlaceholderText = "vuelvo 15:30" };
        if (!string.IsNullOrWhiteSpace(notaActual)) _txtNota.Text = notaActual;
        Controls.Add(_txtNota);

        var aceptar = AppTheme.MakePrimaryButton("Guardar", 110);
        aceptar.Location = new Point(160, 148);
        aceptar.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        var cancelar = AppTheme.MakeSecondaryButton("Cancelar", 100);
        cancelar.Location = new Point(280, 148);
        cancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.AddRange([aceptar, cancelar]);

        AcceptButton = aceptar; CancelButton = cancelar;
    }
}
