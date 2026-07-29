using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Búsqueda global (Ctrl+K): escribe y salta a requerimientos, tickets de DevOps, desarrolladores o
/// sugerencias. Devuelve en <see cref="SelectedNavKey"/> la pantalla a la que navegar.
/// </summary>
public class GlobalSearchForm : Form
{
    private readonly SearchService _search;
    private TextBox _txt = null!;
    private ListBox _list = null!;
    private Label _lblHint = null!;
    private List<SearchHit> _hits = [];

    public string? SelectedNavKey { get; private set; }

    public GlobalSearchForm(SearchService search)
    {
        _search = search;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Búsqueda"; Size = new Size(620, 460);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        _txt = new TextBox { Location = new Point(16, 16), Width = 580, Font = new Font("Segoe UI", 13f), PlaceholderText = "Buscar requerimientos, tickets, desarrolladores, sugerencias…" };
        _txt.TextChanged += (_, _) => Refrescar();
        _txt.KeyDown += Txt_KeyDown;
        Controls.Add(_txt);

        _list = new ListBox { Location = new Point(16, 54), Size = new Size(580, 350), Font = AppTheme.DefaultFont, IntegralHeight = false };
        _list.DoubleClick += (_, _) => Aceptar();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Aceptar(); e.Handled = true; } };
        Controls.Add(_list);

        _lblHint = new Label { Location = new Point(16, 410), AutoSize = false, Size = new Size(580, 20), Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary, Text = "Escribe al menos 2 caracteres. Enter para abrir, Esc para cerrar." };
        Controls.Add(_lblHint);

        AcceptButton = null;   // Enter lo maneja el textbox/lista, no un botón
        KeyPreview = true;
    }

    private void Refrescar()
    {
        try { _hits = _search.Buscar(_txt.Text); }
        catch { _hits = []; }

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var h in _hits)
            _list.Items.Add($"{SearchService.Icono(h.Kind)}  {h.Texto}   —   {h.Detalle}");
        _list.EndUpdate();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;

        _lblHint.Text = _txt.Text.Trim().Length < 2
            ? "Escribe al menos 2 caracteres. Enter para abrir, Esc para cerrar."
            : $"{_hits.Count} resultado(s) · hasta {SearchService.MaxPorTipo} por tipo; afina si falta alguno.";
    }

    private void Txt_KeyDown(object? s, KeyEventArgs e)
    {
        // Flechas mueven la selección en la lista aunque el foco esté en la caja de texto.
        if (e.KeyCode is Keys.Down or Keys.Up && _list.Items.Count > 0)
        {
            int i = _list.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1);
            _list.SelectedIndex = Math.Clamp(i, 0, _list.Items.Count - 1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter) { Aceptar(); e.Handled = true; }
    }

    private void Aceptar()
    {
        int i = _list.SelectedIndex;
        if (i < 0 || i >= _hits.Count) return;
        SelectedNavKey = _hits[i].NavKey;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
