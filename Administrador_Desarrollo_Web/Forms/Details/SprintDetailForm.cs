using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Alta/edición de un sprint: nombre, objetivo y las dos fechas. Es UI pura — valida lo mínimo
/// para no molestar (el servicio revalida de todos modos) y quien la abre persiste.
/// </summary>
public class SprintDetailForm : ResponsiveForm
{
    private TextBox _txtNombre = null!;
    private TextBox _txtObjetivo = null!;
    private DateTimePicker _dtpInicio = null!;
    private DateTimePicker _dtpFin = null!;
    private Label _lblDuracion = null!;

    public string Nombre => _txtNombre.Text.Trim();
    public string? Objetivo => string.IsNullOrWhiteSpace(_txtObjetivo.Text) ? null : _txtObjetivo.Text.Trim();
    public DateTime Inicio => _dtpInicio.Value.Date;
    public DateTime Fin => _dtpFin.Value.Date;

    /// <summary>Nuevo sprint (con fechas propuestas: hoy + dos semanas).</summary>
    public SprintDetailForm() : this(null) { }

    /// <summary>Editar uno existente.</summary>
    public SprintDetailForm(Sprint? editar)
    {
        BuildUI(editar);
    }

    private void BuildUI(Sprint? s)
    {
        Text = s == null ? "Nuevo sprint" : $"Sprint «{s.Name}»";
        Size = new Size(520, 400);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        int y = 16;
        Controls.Add(new Label { Text = "Nombre *", Location = new Point(20, y), AutoSize = true, Font = AppTheme.BoldFont });
        y += 22;
        _txtNombre = new TextBox { Location = new Point(20, y), Width = 460, MaxLength = SprintService.MaxNombre, Text = s?.Name ?? "" };
        Controls.Add(_txtNombre);
        y += 36;

        Controls.Add(new Label { Text = "Objetivo (qué se quiere poder decir al terminarlo)", Location = new Point(20, y), AutoSize = true, Font = AppTheme.BoldFont });
        y += 22;
        // AcceptsReturn: sin esto, Enter dentro del objetivo dispara el AcceptButton y cierra el
        // diálogo a media escritura.
        _txtObjetivo = new TextBox
        {
            Location = new Point(20, y), Width = 460, Height = 70, Multiline = true, AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical, MaxLength = SprintService.MaxObjetivo, Text = s?.Goal ?? ""
        };
        Controls.Add(_txtObjetivo);
        y += 82;

        Controls.Add(new Label { Text = "Inicio", Location = new Point(20, y), AutoSize = true, Font = AppTheme.BoldFont });
        Controls.Add(new Label { Text = "Fin (inclusive)", Location = new Point(260, y), AutoSize = true, Font = AppTheme.BoldFont });
        y += 22;
        _dtpInicio = new DateTimePicker { Location = new Point(20, y), Width = 210, Format = DateTimePickerFormat.Long, Value = s?.StartDate ?? DateTime.Today };
        _dtpFin = new DateTimePicker { Location = new Point(260, y), Width = 220, Format = DateTimePickerFormat.Long, Value = s?.EndDate ?? DateTime.Today.AddDays(13) };
        _dtpInicio.ValueChanged += (_, _) => PintarDuracion();
        _dtpFin.ValueChanged += (_, _) => PintarDuracion();
        Controls.AddRange([_dtpInicio, _dtpFin]);
        y += 34;

        _lblDuracion = new Label { Location = new Point(20, y), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary };
        Controls.Add(_lblDuracion);
        PintarDuracion();
        y += 34;

        var guardar = AppTheme.MakePrimaryButton("Guardar", 110);
        guardar.Location = new Point(250, y);
        guardar.Click += Guardar_Click;
        var cancelar = AppTheme.MakeSecondaryButton("Cancelar", 100);
        cancelar.Location = new Point(370, y);
        cancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.AddRange([guardar, cancelar]);
        AcceptButton = guardar; CancelButton = cancelar;
    }

    private void PintarDuracion()
    {
        int dias = (Fin - Inicio).Days + 1;
        _lblDuracion.ForeColor = dias < 1 || dias > SprintService.MaxDias ? AppTheme.Danger : AppTheme.TextSecondary;
        _lblDuracion.Text = dias < 1
            ? "El fin es anterior al inicio."
            : $"{dias} día(s) naturales, extremos inclusive.";
    }

    private void Guardar_Click(object? s, EventArgs e)
    {
        if (Nombre.Length < 3)
        {
            MessageBox.Show("Ponle un nombre al sprint (al menos 3 caracteres).", "Validación",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtNombre.Focus(); return;
        }
        if (Fin < Inicio)
        {
            MessageBox.Show("La fecha de fin no puede ser anterior a la de inicio.", "Validación",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _dtpFin.Focus(); return;
        }
        DialogResult = DialogResult.OK; Close();
    }
}
