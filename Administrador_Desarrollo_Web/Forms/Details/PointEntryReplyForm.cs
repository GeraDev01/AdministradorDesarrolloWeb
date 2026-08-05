using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// La réplica del desarrollador a una actividad rechazada: lee por qué se la rechazaron, ve el
/// ida y vuelta anterior si lo hubo, y escribe su argumento para devolverla a revisión.
///
/// El motivo del rechazo se muestra arriba y a la vista a propósito. Pedir que alguien argumente
/// sin tener delante lo que se le objetó es la forma más segura de que la segunda vuelta repita el
/// malentendido de la primera.
/// </summary>
public class PointEntryReplyForm : ResponsiveForm
{
    private readonly PointEntry _entrada;
    private TextBox _txtArgumento = null!;
    private Label _lblContador = null!;

    /// <summary>Lo que escribió el desarrollador (solo válido si el diálogo aceptó).</summary>
    public string Argumento { get; private set; } = "";

    public PointEntryReplyForm(PointEntry entrada)
    {
        _entrada = entrada;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Replicar el rechazo";
        Size = new Size(640, 640);
        MinimumSize = new Size(560, 520);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label
        {
            Text = "  🗣  Replicar el rechazo", Dock = DockStyle.Fill, ForeColor = Color.White,
            Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20, 12, 20, 12), BackColor = AppTheme.ContentBg };
        const int ancho = 570;
        int y = 4;

        body.Controls.Add(new Label
        {
            Text = $"{_entrada.Criterion?.Name ?? "Actividad"}   ({(_entrada.Points >= 0 ? "+" : "")}{_entrada.Points} pts)",
            Location = new Point(0, y), AutoSize = false, Size = new Size(ancho, 22),
            Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary
        });
        y += 28;

        if (_entrada.ReviewRound > 0)
        {
            body.Controls.Add(new Label
            {
                Text = $"Esta actividad ya ha ido y vuelto {_entrada.ReviewRound} vez/veces.",
                Location = new Point(0, y), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.Warning
            });
            y += 24;
        }

        // ── Motivo del rechazo ──────────────────────────────────────────────
        body.Controls.Add(new Label
        {
            Text = "Por qué te la rechazaron", Location = new Point(0, y), AutoSize = true,
            ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont
        });
        y += 20;
        var txtMotivo = new TextBox
        {
            Location = new Point(0, y), Size = new Size(ancho, 70),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle,
            Text = string.IsNullOrWhiteSpace(_entrada.ReviewComment)
                ? "El líder no dejó un motivo."
                : _entrada.ReviewComment
        };
        txtMotivo.ForeColor = string.IsNullOrWhiteSpace(_entrada.ReviewComment) ? AppTheme.TextSecondary : AppTheme.Danger;
        body.Controls.Add(txtMotivo);
        y += 80;

        // ── Historial, si ya hubo vueltas ───────────────────────────────────
        if (!string.IsNullOrWhiteSpace(_entrada.ReviewHistory))
        {
            body.Controls.Add(new Label
            {
                Text = "Lo que se ha dicho hasta ahora", Location = new Point(0, y), AutoSize = true,
                ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont
            });
            y += 20;
            body.Controls.Add(new TextBox
            {
                Location = new Point(0, y), Size = new Size(ancho, 100),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle,
                Text = _entrada.ReviewHistory!.Replace("\n", Environment.NewLine)
            });
            y += 110;
        }

        // ── Tu argumento ────────────────────────────────────────────────────
        body.Controls.Add(new Label
        {
            Text = "Tu argumento *", Location = new Point(0, y), AutoSize = true, Font = AppTheme.BoldFont
        });
        y += 20;
        body.Controls.Add(new Label
        {
            Text = "Explica por qué debería aprobarse. Si te faltó una evidencia, ciérrala primero con «✏ Corregir».",
            Location = new Point(0, y), AutoSize = false, Size = new Size(ancho, 18),
            ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont
        });
        y += 22;
        _txtArgumento = new TextBox
        {
            Location = new Point(0, y), Size = new Size(ancho, 120),
            Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true,
            MaxLength = PerformanceScoringService.MaxArgumento
        };
        _txtArgumento.TextChanged += (_, _) => PintarContador();
        body.Controls.Add(_txtArgumento);
        y += 128;

        _lblContador = new Label
        {
            Location = new Point(0, y), AutoSize = false, Size = new Size(ancho, 18),
            ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont
        };
        body.Controls.Add(_lblContador);
        y += 26;

        body.AutoScrollMinSize = new Size(0, y);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancelar = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnEnviar = AppTheme.MakePrimaryButton("Enviar a revisión", 175);
        btnEnviar.Click += BtnEnviar_Click;
        btns.Controls.AddRange([btnCancelar, btnEnviar]);

        outer.Controls.Add(hdr,  0, 0);
        outer.Controls.Add(body, 0, 1);
        outer.Controls.Add(btns, 0, 2);
        Controls.Add(outer);
        // Sin AcceptButton: el argumento es multilínea y Enter debe crear un salto de línea.
        CancelButton = btnCancelar;

        PintarContador();
    }

    private void PintarContador()
    {
        int usados = _txtArgumento.Text.Trim().Length;
        _lblContador.Text = usados == 0
            ? "Sin argumento no se puede enviar."
            : $"{usados} de {PerformanceScoringService.MaxArgumento} caracteres.";
        _lblContador.ForeColor = usados == 0 ? AppTheme.Warning : AppTheme.TextSecondary;
    }

    private void BtnEnviar_Click(object? s, EventArgs e)
    {
        var texto = _txtArgumento.Text.Trim();
        if (texto.Length == 0)
        {
            MessageBox.Show(
                "Escribe por qué crees que debería aprobarse.\n\n" +
                "Reenviarla sin decir nada le devuelve al líder un trabajo que ya hizo, sin nada nuevo que valorar.",
                "Falta el argumento", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtArgumento.Focus();
            return;
        }

        Argumento = texto;
        DialogResult = DialogResult.OK;
        Close();
    }
}
