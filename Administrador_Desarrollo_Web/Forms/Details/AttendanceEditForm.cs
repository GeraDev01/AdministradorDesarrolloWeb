using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Corrección de asistencia por el líder, y alta de un día que nadie marcó.
///
/// El motivo es obligatorio a propósito: este dato acaba pesando en una nómina, y una corrección sin
/// explicación es indistinguible de una manipulación. Lo escrito aquí queda en la fila y, junto con
/// las horas anteriores, en la bitácora.
/// </summary>
public class AttendanceEditForm : ResponsiveForm
{
    private DateTimePicker _dtpEntrada = null!, _dtpSalida = null!;
    private CheckBox _chkSinSalida = null!;
    private TextBox _txtMotivo = null!;
    private ComboBox? _cbxPersona;

    /// <summary>Cuenta elegida en el modo alta. En el modo corrección no se usa.</summary>
    public int UserIdElegido { get; private set; }
    public DateTime EntradaLocal { get; private set; }
    public DateTime? SalidaLocal { get; private set; }
    public string Motivo { get; private set; } = "";

    /// <summary>Corregir un registro que ya existe.</summary>
    public AttendanceEditForm(string persona, DateTime entradaLocal, DateTime? salidaLocal,
        string? solicitud = null)
        => BuildUI($"Corregir asistencia — {persona}", null, entradaLocal, salidaLocal, solicitud);

    /// <summary>Dar de alta un día que nadie marcó. <paramref name="personas"/> es (id, nombre).</summary>
    public AttendanceEditForm(IReadOnlyList<(int UserId, string Nombre)> personas)
        => BuildUI("Registrar un día olvidado", personas,
                   DateTime.Today.AddHours(9), DateTime.Today.AddHours(18), null);

    private void BuildUI(string titulo, IReadOnlyList<(int UserId, string Nombre)>? personas,
        DateTime entradaLocal, DateTime? salidaLocal, string? solicitud)
    {
        Text = titulo;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        BackColor = AppTheme.CardBg;
        Font = AppTheme.DefaultFont;
        ClientSize = new Size(480, personas != null ? 360 : 330);

        int y = 18;

        if (personas != null)
        {
            Controls.Add(Etiqueta("Persona:", y));
            _cbxPersona = new ComboBox
            {
                Left = 20, Top = y + 22, Width = 430, DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var p in personas) _cbxPersona.Items.Add(new ItemPersona(p.UserId, p.Nombre));
            if (_cbxPersona.Items.Count > 0) _cbxPersona.SelectedIndex = 0;
            Controls.Add(_cbxPersona);
            y += 60;
        }

        // ShowUpDown y no el calendario desplegable: aquí lo que casi siempre se ajusta es la HORA,
        // y con las flechas se cambia sin abrir nada.
        Controls.Add(Etiqueta("Entrada:", y));
        _dtpEntrada = new DateTimePicker
        {
            Left = 20, Top = y + 22, Width = 210, Format = DateTimePickerFormat.Custom,
            CustomFormat = "dd/MM/yyyy HH:mm", ShowUpDown = true, Value = entradaLocal
        };
        Controls.Add(_dtpEntrada);

        Controls.Add(Etiqueta("Salida:", y, 240));
        _dtpSalida = new DateTimePicker
        {
            Left = 240, Top = y + 22, Width = 210, Format = DateTimePickerFormat.Custom,
            CustomFormat = "dd/MM/yyyy HH:mm", ShowUpDown = true,
            Value = salidaLocal ?? entradaLocal.AddHours(8)
        };
        Controls.Add(_dtpSalida);
        y += 56;

        _chkSinSalida = new CheckBox
        {
            Left = 240, Top = y, Width = 210, AutoSize = true,
            Text = "Dejarla sin salida (abierta)", Checked = salidaLocal == null
        };
        _chkSinSalida.CheckedChanged += (_, _) => _dtpSalida.Enabled = !_chkSinSalida.Checked;
        _dtpSalida.Enabled = !_chkSinSalida.Checked;
        Controls.Add(_chkSinSalida);
        y += 30;

        if (!string.IsNullOrWhiteSpace(solicitud))
        {
            var lblSolicitud = new Label
            {
                Left = 20, Top = y, Width = 430, Height = 34, AutoSize = false,
                Text = $"🙋 Lo que pidió: {solicitud}",
                ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont
            };
            Controls.Add(lblSolicitud);
            y += 38;
        }

        Controls.Add(Etiqueta("Motivo de la corrección *:", y));
        _txtMotivo = new TextBox
        {
            Left = 20, Top = y + 22, Width = 430, Height = 60, Multiline = true,
            ScrollBars = ScrollBars.Vertical, MaxLength = 500
        };
        Controls.Add(_txtMotivo);
        y += 92;

        var btnGuardar = AppTheme.MakePrimaryButton("Guardar", 120);
        btnGuardar.Left = 210; btnGuardar.Top = y;
        btnGuardar.Click += Guardar_Click;

        var btnCancelar = AppTheme.MakeSecondaryButton("Cancelar", 110);
        btnCancelar.Left = 340; btnCancelar.Top = y;
        btnCancelar.DialogResult = DialogResult.Cancel;

        Controls.AddRange([btnGuardar, btnCancelar]);
        AcceptButton = btnGuardar;
        CancelButton = btnCancelar;
    }

    private static Label Etiqueta(string texto, int top, int left = 20) => new()
    {
        Left = left, Top = top, AutoSize = true, Text = texto, Font = AppTheme.BoldFont
    };

    private void Guardar_Click(object? sender, EventArgs e)
    {
        var motivo = _txtMotivo.Text.Trim();
        if (motivo.Length == 0)
        {
            MessageBox.Show("Escribe el motivo: es lo que queda como explicación del cambio.",
                "Falta el motivo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtMotivo.Focus();
            return;
        }

        // La validación de fondo (salida posterior, sin solapes, sin futuro) vive en el servicio;
        // esta es solo para no hacer ir y volver por lo evidente.
        if (!_chkSinSalida.Checked && _dtpSalida.Value <= _dtpEntrada.Value)
        {
            MessageBox.Show("La salida tiene que ser posterior a la entrada.",
                "Horario inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_cbxPersona != null)
        {
            if (_cbxPersona.SelectedItem is not ItemPersona elegida)
            {
                MessageBox.Show("Elige la persona.", "Falta la persona",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            UserIdElegido = elegida.UserId;
        }

        EntradaLocal = _dtpEntrada.Value;
        SalidaLocal  = _chkSinSalida.Checked ? null : _dtpSalida.Value;
        Motivo       = motivo;
        DialogResult = DialogResult.OK;
    }

    private sealed record ItemPersona(int UserId, string Nombre)
    {
        public override string ToString() => Nombre;
    }
}
