using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Redacción de una sugerencia (modo nuevo) o su consulta con la respuesta del administrador (modo
/// solo lectura). Es UI pura, sin dependencias de base ni de DI: el control que la abre lee las
/// propiedades y persiste.
/// </summary>
public class SuggestionForm : Form
{
    private ComboBox _cbxCat = null!;
    private TextBox _txtTitle = null!;
    private TextBox _txtBody = null!;
    private CheckBox _chkAnon = null!;

    public SuggestionCategory Categoria => (SuggestionCategory)Math.Max(0, _cbxCat.SelectedIndex);
    public string Titulo => _txtTitle.Text.Trim();
    public string Cuerpo => _txtBody.Text.Trim();
    public bool Anonima => _chkAnon.Checked;

    /// <summary>Diálogo para escribir una sugerencia nueva.</summary>
    public SuggestionForm() => BuildUI(null);

    /// <summary>Diálogo de solo lectura para ver una sugerencia ya enviada y su respuesta.</summary>
    public SuggestionForm(Suggestion ver) => BuildUI(ver);

    private void BuildUI(Suggestion? ver)
    {
        bool soloLectura = ver != null;
        Text = soloLectura ? $"Sugerencia #{ver!.Id}" : "Nueva sugerencia";
        Size = new Size(560, soloLectura ? 560 : 470);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        int y = 16;
        Controls.Add(new Label { Text = "¿Sobre qué es tu propuesta?", Location = new Point(20, y), AutoSize = true, Font = AppTheme.BoldFont });
        y += 24;
        _cbxCat = new ComboBox { Location = new Point(20, y), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = !soloLectura };
        _cbxCat.Items.AddRange(["Producto (la aplicación)", "Departamento", "Otro"]);
        _cbxCat.SelectedIndex = soloLectura ? (int)ver!.Category : 0;
        Controls.Add(_cbxCat);
        y += 38;

        Controls.Add(new Label { Text = "Título", Location = new Point(20, y), AutoSize = true, Font = AppTheme.BoldFont });
        y += 22;
        _txtTitle = new TextBox { Location = new Point(20, y), Width = 500, MaxLength = SuggestionService.MaxTitulo, ReadOnly = soloLectura };
        if (soloLectura) _txtTitle.Text = ver!.Title;
        Controls.Add(_txtTitle);
        y += 34;

        Controls.Add(new Label { Text = "Descripción (qué propones y por qué mejora)", Location = new Point(20, y), AutoSize = true, Font = AppTheme.BoldFont });
        y += 22;
        _txtBody = new TextBox { Location = new Point(20, y), Width = 500, Height = soloLectura ? 120 : 180, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = SuggestionService.MaxCuerpo, ReadOnly = soloLectura };
        if (soloLectura) _txtBody.Text = ver!.Body;
        Controls.Add(_txtBody);
        y += (soloLectura ? 120 : 180) + 10;

        _chkAnon = new CheckBox
        {
            Text = "Enviar sin mostrar mi nombre al administrador",
            Location = new Point(20, y), AutoSize = true, Checked = soloLectura && ver!.Anonymous, Enabled = !soloLectura
        };
        Controls.Add(_chkAnon);
        y += 30;

        if (soloLectura)
        {
            // Estado + respuesta del administrador.
            Controls.Add(new Label
            {
                Text = $"Estado: {SuggestionService.EtiquetaEstado(ver!.Status)}",
                Location = new Point(20, y), AutoSize = true, Font = AppTheme.BoldFont, ForeColor = ColorEstado(ver.Status)
            });
            y += 24;
            Controls.Add(new Label { Text = "Respuesta del administrador:", Location = new Point(20, y), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary });
            y += 20;
            var resp = new TextBox
            {
                Location = new Point(20, y), Width = 500, Height = 60, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Text = string.IsNullOrWhiteSpace(ver.AdminResponse) ? "(sin respuesta todavía)" : ver.AdminResponse
            };
            Controls.Add(resp);
            y += 70;

            var cerrar = AppTheme.MakeSecondaryButton("Cerrar", 110);
            cerrar.Location = new Point(410, y);
            cerrar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cerrar);
            CancelButton = cerrar;
        }
        else
        {
            var enviar = AppTheme.MakePrimaryButton("Enviar 💡", 130);
            enviar.Location = new Point(258, y);
            enviar.Click += Enviar_Click;
            var cancelar = AppTheme.MakeSecondaryButton("Cancelar", 110);
            cancelar.Location = new Point(410, y);
            cancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.AddRange([enviar, cancelar]);
            AcceptButton = enviar; CancelButton = cancelar;
        }
    }

    private void Enviar_Click(object? s, EventArgs e)
    {
        if (Titulo.Length < 3)
        {
            MessageBox.Show("Escribe un título (al menos 3 caracteres).", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtTitle.Focus(); return;
        }
        if (Cuerpo.Length < 5)
        {
            MessageBox.Show("Describe tu sugerencia (al menos 5 caracteres).", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtBody.Focus(); return;
        }
        DialogResult = DialogResult.OK; Close();
    }

    private static Color ColorEstado(SuggestionStatus s) => s switch
    {
        SuggestionStatus.Aceptada or SuggestionStatus.Implementada => AppTheme.Success,
        SuggestionStatus.Rechazada => AppTheme.Danger,
        SuggestionStatus.EnRevision => AppTheme.Warning,
        _ => AppTheme.TextSecondary
    };
}
