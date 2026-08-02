using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// El paso previo al despliegue: los hechos del sistema arriba (versión, destino, respaldo) y
/// debajo los puntos que la persona tiene que confirmar. «Desplegar» no se habilita hasta que
/// están todos marcados.
///
/// Sustituye a un MessageBox de «¿seguro?», que se contesta que sí por reflejo. Lo que aquí se
/// marque queda escrito con el despliegue como evidencia.
/// </summary>
public class DeploymentChecklistForm : ResponsiveForm
{
    private readonly string _version, _destino, _respaldo;
    private readonly bool _sinRespaldo;
    private readonly List<CheckBox> _casillas = [];
    private TextBox _txtNota = null!;
    private Button _btnDesplegar = null!;

    /// <summary>Las claves de los puntos marcados (solo válido si el diálogo aceptó).</summary>
    public List<string> Marcados { get; } = [];

    /// <summary>La nota libre que escribió quien despliega, o null.</summary>
    public string? Nota => string.IsNullOrWhiteSpace(_txtNota.Text) ? null : _txtNota.Text.Trim();

    public DeploymentChecklistForm(string version, string destino, string respaldo, bool sinRespaldo)
    {
        _version = version; _destino = destino; _respaldo = respaldo; _sinRespaldo = sinRespaldo;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Antes de desplegar";
        Size = new Size(620, 600);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        int y = 16;

        // ── Los hechos: salen del sistema, no de la buena fe de quien despliega ──
        var ficha = new Panel { Location = new Point(18, y), Size = new Size(566, 92), BackColor = AppTheme.CardBg };
        int fy = 10;
        Dato(ficha, "Versión", _version, ref fy);
        Dato(ficha, "Destino", _destino, ref fy);
        Dato(ficha, "Respaldo previo", _respaldo, ref fy, _sinRespaldo ? AppTheme.Danger : AppTheme.Success);
        Controls.Add(ficha);
        y += 100;

        if (_sinRespaldo)
        {
            Controls.Add(new Label
            {
                Text = "⚠  Vas a desplegar SIN respaldo previo: si algo sale mal no habrá copia para revertir.",
                Location = new Point(18, y), AutoSize = false, Size = new Size(566, 20),
                ForeColor = AppTheme.Danger, Font = AppTheme.BoldFont
            });
            y += 26;
        }

        Controls.Add(new Label
        {
            Text = "Confirma cada punto antes de continuar:", Location = new Point(18, y),
            AutoSize = true, Font = AppTheme.BoldFont
        });
        y += 26;

        foreach (var p in DeploymentChecklist.Puntos)
        {
            var chk = new CheckBox
            {
                Text = p.Texto, Tag = p.Clave,
                Location = new Point(22, y), AutoSize = false, Size = new Size(560, 20)
            };
            chk.CheckedChanged += (_, _) => PintarBoton();
            Controls.Add(chk);
            _casillas.Add(chk);
            y += 20;

            Controls.Add(new Label
            {
                Text = p.Ayuda, Location = new Point(42, y), AutoSize = false, Size = new Size(540, 18),
                Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
            });
            y += 24;
        }

        y += 6;
        Controls.Add(new Label { Text = "Nota (opcional): ticket, autorización, quién pidió el despliegue…", Location = new Point(18, y), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary });
        y += 20;
        _txtNota = new TextBox { Location = new Point(18, y), Width = 566, Height = 50, Multiline = true, AcceptsReturn = true };
        Controls.Add(_txtNota);
        y += 62;

        _btnDesplegar = AppTheme.MakePrimaryButton("🚀 Desplegar", 150);
        _btnDesplegar.Location = new Point(320, y);
        _btnDesplegar.Click += (_, _) => Aceptar();

        var cancelar = AppTheme.MakeSecondaryButton("Cancelar", 110);
        cancelar.Location = new Point(478, y);
        cancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange([_btnDesplegar, cancelar]);
        // Sin AcceptButton: desplegar con Enter es exactamente el reflejo que este diálogo viene
        // a interrumpir.
        CancelButton = cancelar;
        PintarBoton();
    }

    private static void Dato(Panel p, string etiqueta, string valor, ref int y, Color? color = null)
    {
        p.Controls.Add(new Label { Text = etiqueta + ":", Location = new Point(12, y), AutoSize = false, Size = new Size(110, 18), ForeColor = AppTheme.TextSecondary });
        p.Controls.Add(new Label
        {
            Text = valor, Location = new Point(126, y), AutoSize = false, Size = new Size(428, 18),
            Font = AppTheme.BoldFont, ForeColor = color ?? AppTheme.TextPrimary, AutoEllipsis = true
        });
        y += 24;
    }

    private void PintarBoton()
    {
        int faltan = _casillas.Count(c => !c.Checked);
        _btnDesplegar.Enabled = faltan == 0;
        _btnDesplegar.Text = faltan == 0 ? "🚀 Desplegar" : $"Faltan {faltan}";
    }

    private void Aceptar()
    {
        Marcados.Clear();
        // Se releen las casillas en vez de confiar en el contador: lo que se guarda como evidencia
        // tiene que ser lo que está marcado, no lo que el botón creía.
        foreach (var c in _casillas.Where(c => c.Checked))
            Marcados.Add((string)c.Tag!);

        if (DeploymentChecklist.Faltantes(Marcados).Count > 0) { PintarBoton(); return; }

        DialogResult = DialogResult.OK;
        Close();
    }
}
