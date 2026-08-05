using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Cuánto le va a llevar al desarrollador el ticket que le asignaron. Se captura en horas y
/// minutos porque «2.75 h» no es como nadie piensa el tiempo, y se guarda en el campo Effort del
/// work item: si solo viviera aquí, para el resto de la empresa el ticket seguiría sin estimar.
/// </summary>
public class EstimarTicketForm : ResponsiveForm
{
    private readonly DevOpsTicket _ticket;
    private NumericUpDown _nudHoras = null!, _nudMinutos = null!;
    private Label _lblResumen = null!;

    /// <summary>Estimación en horas decimales (solo válida si el diálogo aceptó).</summary>
    public double Horas { get; private set; }

    public EstimarTicketForm(DevOpsTicket ticket)
    {
        _ticket = ticket;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = $"Estimar el ticket #{_ticket.ExternalId}";
        Size = new Size(520, 380);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
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
            Text = "  ⏱  ¿Cuánto te va a llevar?", Dock = DockStyle.Fill, ForeColor = Color.White,
            Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 14, 24, 10), BackColor = AppTheme.ContentBg };
        const int ancho = 445;
        int y = 0;

        body.Controls.Add(new Label
        {
            Text = $"#{_ticket.ExternalId}  {_ticket.Title}",
            Location = new Point(0, y), AutoSize = false, Size = new Size(ancho, 40),
            Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary
        });
        y += 46;

        body.Controls.Add(new Label
        {
            Text = "Tu estimación se guarda en el campo Effort del ticket, así que vale también fuera de esta aplicación.",
            Location = new Point(0, y), AutoSize = false, Size = new Size(ancho, 34),
            ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont
        });
        y += 42;

        var fila = new FlowLayoutPanel { Location = new Point(0, y), Size = new Size(ancho, 34), FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _nudHoras = new NumericUpDown { Width = 80, Minimum = 0, Maximum = 1000, Value = 0 };
        _nudMinutos = new NumericUpDown { Width = 80, Minimum = 0, Maximum = 59, Value = 0, Increment = 15 };
        _nudHoras.ValueChanged += (_, _) => PintarResumen();
        _nudMinutos.ValueChanged += (_, _) => PintarResumen();
        fila.Controls.AddRange([
            _nudHoras,   new Label { Text = "horas", AutoSize = true, Margin = new Padding(6, 7, 16, 0) },
            _nudMinutos, new Label { Text = "minutos", AutoSize = true, Margin = new Padding(6, 7, 0, 0) }]);
        body.Controls.Add(fila);
        y += 44;

        _lblResumen = new Label
        {
            Location = new Point(0, y), AutoSize = false, Size = new Size(ancho, 22),
            Font = AppTheme.BoldFont
        };
        body.Controls.Add(_lblResumen);
        y += 30;

        body.Controls.Add(new Label
        {
            Text = "Estima el trabajo, no el calendario: cuántas horas de tu tiempo, no en cuántos días lo entregas.",
            Location = new Point(0, y), AutoSize = false, Size = new Size(ancho, 34),
            ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont
        });

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancelar = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnGuardar = AppTheme.MakePrimaryButton("Guardar estimación", 180);
        btnGuardar.Click += BtnGuardar_Click;
        btns.Controls.AddRange([btnCancelar, btnGuardar]);

        outer.Controls.Add(hdr,  0, 0);
        outer.Controls.Add(body, 0, 1);
        outer.Controls.Add(btns, 0, 2);
        Controls.Add(outer);
        AcceptButton = btnGuardar;
        CancelButton = btnCancelar;

        // Si ya tenía estimación (propia o puesta en DevOps), se precarga para corregirla.
        if (_ticket.EstimatedHours is double h && h > 0)
        {
            int totalMin = (int)Math.Round(h * 60);
            _nudHoras.Value = Math.Min(_nudHoras.Maximum, totalMin / 60);
            _nudMinutos.Value = totalMin % 60;
        }
        PintarResumen();
    }

    private double HorasCapturadas => (double)_nudHoras.Value + (double)_nudMinutos.Value / 60.0;

    private void PintarResumen()
    {
        double h = HorasCapturadas;
        _lblResumen.Text = h <= 0 ? "Sin estimación no se puede guardar." : $"Se guardará como {h:0.##} h de Effort.";
        _lblResumen.ForeColor = h <= 0 ? AppTheme.Warning : AppTheme.Success;
    }

    private void BtnGuardar_Click(object? s, EventArgs e)
    {
        double h = HorasCapturadas;
        if (h <= 0)
        {
            MessageBox.Show("Captura cuánto te va a llevar. Aunque sea aproximado: sin estimación nadie puede planear alrededor de este ticket.",
                "Falta la estimación", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _nudHoras.Focus();
            return;
        }
        Horas = Math.Round(h, 2);
        DialogResult = DialogResult.OK;
        Close();
    }
}
