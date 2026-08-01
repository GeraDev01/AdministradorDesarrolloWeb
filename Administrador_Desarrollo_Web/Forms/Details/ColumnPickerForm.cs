namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Elige qué columnas se ven en una lista. Marcada = visible.
///
/// Se exige dejar al menos una: una rejilla sin columnas se ve como si la pantalla estuviera rota, y
/// desde ahí no hay forma evidente de volver.
/// </summary>
public class ColumnPickerForm : ResponsiveForm
{
    private CheckedListBox _lista = null!;
    private Label _lblResumen = null!;

    private readonly List<(string nombre, string titulo, bool visible)> _columnas;

    /// <summary>Nombres de las columnas que deben quedar visibles. Solo válido si terminó en OK.</summary>
    public HashSet<string> Visibles { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

    public ColumnPickerForm(DataGridView grid)
    {
        _columnas = grid.Columns.Cast<DataGridViewColumn>()
            .Select(c => (c.Name, string.IsNullOrWhiteSpace(c.HeaderText) ? c.Name : c.HeaderText, c.Visible))
            .ToList();
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Columnas";
        Size = new Size(340, 460);
        MinimumSize = new Size(300, 320);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;
        MinimizeBox = false;
        MaximizeBox = false;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));   // encabezado
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // lista
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));   // resumen
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));   // botones
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label
        {
            Text = "  🧱  Qué columnas quieres ver", Dock = DockStyle.Fill, ForeColor = Color.White,
            Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        _lista = new CheckedListBox
        {
            Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false,
            BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White,
            Margin = new Padding(12, 8, 12, 4), Font = AppTheme.DefaultFont
        };
        foreach (var (_, titulo, visible) in _columnas) _lista.Items.Add(titulo, visible);
        // ItemCheck se dispara ANTES de que el estado cambie: el resumen se recalcula después.
        _lista.ItemCheck += (_, e) => BeginInvoke(() => PintarResumen(e));

        var pnlLista = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 0), BackColor = AppTheme.ContentBg };
        pnlLista.Controls.Add(_lista);

        _lblResumen = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0)
        };

        var btns = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10), BackColor = AppTheme.ContentBg
        };
        var btnCancelar = AppTheme.MakeSecondaryButton("Cancelar", 90);
        btnCancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnAceptar = AppTheme.MakePrimaryButton("Aceptar", 100);
        btnAceptar.Click += Aceptar;
        var btnTodas = AppTheme.MakeSecondaryButton("Mostrar todas", 130);
        btnTodas.Click += (_, _) =>
        {
            for (int i = 0; i < _lista.Items.Count; i++) _lista.SetItemChecked(i, true);
            PintarResumen(null);
        };
        btns.Controls.AddRange([btnCancelar, btnAceptar, btnTodas]);

        tbl.Controls.Add(hdr,         0, 0);
        tbl.Controls.Add(pnlLista,    0, 1);
        tbl.Controls.Add(_lblResumen, 0, 2);
        tbl.Controls.Add(btns,        0, 3);
        Controls.Add(tbl);

        AcceptButton = btnAceptar;
        CancelButton = btnCancelar;
        PintarResumen(null);
    }

    /// <summary>
    /// <paramref name="cambio"/> llega desde ItemCheck, que se dispara ANTES de aplicar el cambio;
    /// se cuenta con el valor nuevo para que el resumen no vaya un clic atrasado.
    /// </summary>
    private void PintarResumen(ItemCheckEventArgs? cambio)
    {
        if (IsDisposed) return;
        int visibles = ContarVisibles(cambio);
        _lblResumen.Text = visibles == 0
            ? "Deja al menos una columna visible."
            : $"{visibles} de {_lista.Items.Count} columnas visibles.";
        _lblResumen.ForeColor = visibles == 0 ? AppTheme.Danger : AppTheme.TextSecondary;
    }

    private int ContarVisibles(ItemCheckEventArgs? cambio)
    {
        int n = 0;
        for (int i = 0; i < _lista.Items.Count; i++)
        {
            bool marcada = cambio != null && cambio.Index == i
                ? cambio.NewValue == CheckState.Checked
                : _lista.GetItemChecked(i);
            if (marcada) n++;
        }
        return n;
    }

    private void Aceptar(object? sender, EventArgs e)
    {
        var visibles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _columnas.Count; i++)
            if (_lista.GetItemChecked(i)) visibles.Add(_columnas[i].nombre);

        if (visibles.Count == 0)
        {
            MessageBox.Show("Deja al menos una columna visible.", "Columnas",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Visibles = visibles;
        DialogResult = DialogResult.OK;
        Close();
    }
}
