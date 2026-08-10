using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Alta y edición de una actividad del pool.
///
/// El campo de puntos es una ETIQUETA, no un cuadro de texto: el valor sale de la matriz según el
/// tipo y la complejidad, y esa es toda la gracia del sistema. Se muestra en vivo mientras se elige,
/// para que quien publica vea exactamente lo que va a valer antes de guardar.
/// </summary>
public class PoolActivityForm : ResponsiveForm
{
    private readonly IReadOnlyList<PoolPointsMatrixEntry> _matriz;

    private TextBox _txtTitulo = null!, _txtDescripcion = null!, _txtUrl = null!;
    private ComboBox _cbxTipo = null!, _cbxComplejidad = null!;
    private Label _lblPuntos = null!;

    /// <summary>La actividad tal como quedó capturada. Los puntos los recalcula el servicio.</summary>
    public PoolActivity Resultado { get; private set; } = new();

    public PoolActivityForm(IReadOnlyList<PoolPointsMatrixEntry> matriz, PoolActivity? existente = null)
    {
        _matriz = matriz;
        BuildUI(existente);
    }

    private void BuildUI(PoolActivity? existente)
    {
        Text = existente == null ? "Publicar actividad en el pool" : "Editar actividad del pool";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        BackColor = AppTheme.CardBg;
        Font = AppTheme.DefaultFont;
        ClientSize = new Size(560, 470);

        Controls.Add(Etiqueta("¿Qué hay que hacer? *", 18));
        _txtTitulo = new TextBox { Left = 20, Top = 40, Width = 510, MaxLength = 200, Text = existente?.Title ?? "" };
        Controls.Add(_txtTitulo);

        Controls.Add(Etiqueta("Tipo:", 76));
        _cbxTipo = new ComboBox { Left = 20, Top = 98, Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var t in Enum.GetValues<PoolWorkType>()) _cbxTipo.Items.Add(new Item<PoolWorkType>(t, PoolSeed.Etiqueta(t)));
        _cbxTipo.SelectedIndex = (int)(existente?.WorkType ?? PoolWorkType.Bug);
        _cbxTipo.SelectedIndexChanged += (_, _) => PintarPuntos();
        Controls.Add(_cbxTipo);

        Controls.Add(Etiqueta("Complejidad:", 76, 290));
        _cbxComplejidad = new ComboBox { Left = 290, Top = 98, Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var c in Enum.GetValues<PoolComplexity>()) _cbxComplejidad.Items.Add(new Item<PoolComplexity>(c, PoolSeed.Etiqueta(c)));
        _cbxComplejidad.SelectedIndex = (int)(existente?.Complexity ?? PoolComplexity.Media);
        _cbxComplejidad.SelectedIndexChanged += (_, _) => PintarPuntos();
        Controls.Add(_cbxComplejidad);

        // El valor no se teclea: sale de la matriz. Verlo aquí, antes de guardar, es lo que evita
        // publicar sin darse cuenta una actividad de 25 puntos donde se quería una de 5.
        _lblPuntos = new Label
        {
            Left = 20, Top = 140, Width = 510, Height = 46, AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.KpiValueFont,
            ForeColor = AppTheme.Success
        };
        Controls.Add(_lblPuntos);

        Controls.Add(Etiqueta("Detalle (opcional):", 194));
        _txtDescripcion = new TextBox
        {
            Left = 20, Top = 216, Width = 510, Height = 110, Multiline = true,
            ScrollBars = ScrollBars.Vertical, Text = existente?.Description ?? ""
        };
        Controls.Add(_txtDescripcion);

        Controls.Add(Etiqueta("Enlace al work item o ticket (opcional):", 338));
        _txtUrl = new TextBox
        {
            Left = 20, Top = 360, Width = 510, MaxLength = 500, Text = existente?.ExternalUrl ?? "",
            PlaceholderText = "https://dev.azure.com/…"
        };
        Controls.Add(_txtUrl);

        var btnGuardar = AppTheme.MakePrimaryButton(existente == null ? "Publicar" : "Guardar", 130);
        btnGuardar.Left = 280; btnGuardar.Top = 410;
        btnGuardar.Click += Guardar_Click;

        var btnCancelar = AppTheme.MakeSecondaryButton("Cancelar", 110);
        btnCancelar.Left = 420; btnCancelar.Top = 410;
        btnCancelar.DialogResult = DialogResult.Cancel;

        Controls.AddRange([btnGuardar, btnCancelar]);
        AcceptButton = btnGuardar;
        CancelButton = btnCancelar;

        PintarPuntos();
    }

    private static Label Etiqueta(string texto, int top, int left = 20) => new()
    {
        Left = left, Top = top, AutoSize = true, Text = texto, Font = AppTheme.BoldFont
    };

    private void PintarPuntos()
    {
        var celda = _matriz.FirstOrDefault(m => m.WorkType == TipoElegido && m.Complexity == ComplejidadElegida);
        if (celda == null)
        {
            _lblPuntos.Text = "⚠ Esa combinación no tiene puntos configurados.";
            _lblPuntos.ForeColor = AppTheme.Warning;
            return;
        }

        var plazo = celda.DiasLimite > 0 ? $"  ·  {celda.DiasLimite} días para entregarla" : "  ·  sin fecha límite";
        _lblPuntos.Text = $"Vale {celda.Points} puntos{plazo}";
        _lblPuntos.ForeColor = celda.Points > 0 ? AppTheme.Success : AppTheme.Warning;
    }

    private PoolWorkType TipoElegido => ((Item<PoolWorkType>)_cbxTipo.SelectedItem!).Valor;
    private PoolComplexity ComplejidadElegida => ((Item<PoolComplexity>)_cbxComplejidad.SelectedItem!).Valor;

    private void Guardar_Click(object? sender, EventArgs e)
    {
        var titulo = _txtTitulo.Text.Trim();
        if (titulo.Length == 0)
        {
            MessageBox.Show("Escribe qué hay que hacer: es lo que va a leer quien la tome.",
                "Falta el título", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtTitulo.Focus();
            return;
        }

        // El resto de validaciones (enlace, matriz) vive en el servicio; aquí solo lo evidente.
        Resultado = new PoolActivity
        {
            Title       = titulo,
            Description = _txtDescripcion.Text.Trim(),
            WorkType    = TipoElegido,
            Complexity  = ComplejidadElegida,
            ExternalUrl = _txtUrl.Text.Trim()
        };
        DialogResult = DialogResult.OK;
    }

    private sealed record Item<T>(T Valor, string Texto)
    {
        public override string ToString() => Texto;
    }
}
