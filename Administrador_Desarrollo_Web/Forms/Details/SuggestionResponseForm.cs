using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Diálogo del administrador para atender una sugerencia: muestra su contenido y captura el nuevo
/// estado y una respuesta opcional para el autor. UI pura; el control que la abre persiste.
/// </summary>
public class SuggestionResponseForm : Form
{
    private ComboBox _cbxEstado = null!;
    private TextBox _txtRespuesta = null!;

    private static readonly SuggestionStatus[] Estados =
        [SuggestionStatus.Nueva, SuggestionStatus.EnRevision, SuggestionStatus.Aceptada,
         SuggestionStatus.Rechazada, SuggestionStatus.Implementada];

    public SuggestionStatus NuevoEstado => Estados[Math.Max(0, _cbxEstado.SelectedIndex)];
    public string? Respuesta => string.IsNullOrWhiteSpace(_txtRespuesta.Text) ? null : _txtRespuesta.Text.Trim();

    public SuggestionResponseForm(Suggestion sug, string autor)
    {
        BuildUI(sug, autor);
    }

    private void BuildUI(Suggestion sug, string autor)
    {
        Text = $"Atender sugerencia #{sug.Id}";
        Size = new Size(580, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        int y = 14;
        Controls.Add(new Label
        {
            Text = $"{SuggestionService.EtiquetaCategoria(sug.Category)}  ·  De: {autor}  ·  {sug.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}",
            Location = new Point(20, y), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });
        y += 24;
        Controls.Add(new Label { Text = sug.Title, Location = new Point(20, y), AutoSize = false, Size = new Size(530, 24), Font = AppTheme.BoldFont, AutoEllipsis = true });
        y += 30;

        var body = new TextBox
        {
            Location = new Point(20, y), Width = 530, Height = 170, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, Text = sug.Body, BackColor = Color.White
        };
        Controls.Add(body);
        y += 182;

        Controls.Add(new Label { Text = "Estado", Location = new Point(20, y), AutoSize = true, Font = AppTheme.BoldFont });
        y += 22;
        _cbxEstado = new ComboBox { Location = new Point(20, y), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxEstado.Items.AddRange([.. Estados.Select(SuggestionService.EtiquetaEstado)]);
        _cbxEstado.SelectedIndex = Math.Max(0, Array.IndexOf(Estados, sug.Status));
        Controls.Add(_cbxEstado);
        y += 38;

        Controls.Add(new Label { Text = "Respuesta para el autor (opcional)", Location = new Point(20, y), AutoSize = true, Font = AppTheme.BoldFont });
        y += 22;
        _txtRespuesta = new TextBox
        {
            Location = new Point(20, y), Width = 530, Height = 80, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Text = sug.AdminResponse ?? ""
        };
        Controls.Add(_txtRespuesta);
        y += 92;

        var guardar = AppTheme.MakePrimaryButton("Guardar", 130);
        guardar.Location = new Point(288, y);
        guardar.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        var cancelar = AppTheme.MakeSecondaryButton("Cancelar", 110);
        cancelar.Location = new Point(440, y);
        cancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.AddRange([guardar, cancelar]);
        AcceptButton = guardar; CancelButton = cancelar;
    }
}
