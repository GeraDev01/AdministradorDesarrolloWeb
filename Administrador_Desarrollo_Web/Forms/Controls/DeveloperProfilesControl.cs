using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Ficha de información general de cada desarrollador (solo administrador): fortalezas, debilidades,
/// stack técnico, salario y expectativas de crecimiento. Es una vista de referencia para el jefe; el
/// salario es confidencial y esta pantalla solo aparece para el rol Administrador.
/// </summary>
public class DeveloperProfilesControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly DeveloperProfileService _profiles;

    private DataGridView _gridDevs = null!;
    private List<Developer> _devs = [];

    private Label _lblDevName = null!;
    private TextBox _txtStrengths = null!, _txtWeaknesses = null!, _txtStack = null!, _txtGrowth = null!, _txtNotes = null!;
    private NumericUpDown _nudSalary = null!;
    private ComboBox _cbxCurrency = null!;
    private Label _lblUpdated = null!;
    private Button _btnSave = null!;

    public DeveloperProfilesControl(AppDbContext db, DeveloperProfileService profiles)
    {
        _db = db; _profiles = profiles;
        BuildUI();
        LoadDevs();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, BorderStyle = BorderStyle.None, SplitterWidth = 6 };

        // ── Izquierda: lista de desarrolladores ──────────────────
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 6, 10) };
        left.Controls.Add(new Label { Text = "Desarrolladores", Dock = DockStyle.Top, Height = 24, Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary });
        _gridDevs = AppTheme.MakeGrid();
        _gridDevs.Dock = DockStyle.Fill; _gridDevs.MultiSelect = false;
        _gridDevs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrollador", Name = "Dev", FillWeight = 78 });
        _gridDevs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ficha", Name = "Ficha", FillWeight = 22 });
        foreach (DataGridViewColumn c in _gridDevs.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        _gridDevs.SelectionChanged += (_, _) => CargarFicha();
        left.Controls.Add(_gridDevs);
        _gridDevs.BringToFront();
        split.Panel1.Controls.Add(left);

        // ── Derecha: ficha editable ──────────────────────────────
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(14, 12, 14, 12) };
        int y = 4;

        _lblDevName = new Label { Location = new Point(4, y), AutoSize = false, Size = new Size(560, 26), Font = AppTheme.HeaderFont, ForeColor = AppTheme.SidebarActive, Text = "Selecciona un desarrollador" };
        scroll.Controls.Add(_lblDevName); y += 28;
        scroll.Controls.Add(new Label { Text = "🔒 Información confidencial · visible solo para el administrador.", Location = new Point(4, y), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary }); y += 26;

        Campo(scroll, "Fortalezas", ref _txtStrengths, ref y, 70);
        Campo(scroll, "Debilidades / áreas de mejora", ref _txtWeaknesses, ref y, 70);
        Campo(scroll, "Stack técnico (lenguajes, frameworks, herramientas)", ref _txtStack, ref y, 60);

        scroll.Controls.Add(new Label { Text = "Salario", Location = new Point(4, y), AutoSize = true, Font = AppTheme.BoldFont }); y += 22;
        _nudSalary = new NumericUpDown { Location = new Point(4, y), Width = 180, Minimum = 0, Maximum = 100_000_000, DecimalPlaces = 2, ThousandsSeparator = true, Increment = 500 };
        _cbxCurrency = new ComboBox { Location = new Point(192, y), Width = 90, DropDownStyle = ComboBoxStyle.DropDown };
        _cbxCurrency.Items.AddRange(["MXN", "USD", "EUR"]);
        scroll.Controls.Add(_nudSalary); scroll.Controls.Add(_cbxCurrency);
        scroll.Controls.Add(new Label { Text = "0 = sin registrar.", Location = new Point(292, y + 3), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary });
        y += 40;

        Campo(scroll, "Expectativas de crecimiento", ref _txtGrowth, ref y, 70);
        Campo(scroll, "Notas generales", ref _txtNotes, ref y, 60);

        _btnSave = AppTheme.MakePrimaryButton("💾 Guardar ficha", 180, 34);
        _btnSave.Location = new Point(4, y); _btnSave.Click += (_, _) => Guardar();
        scroll.Controls.Add(_btnSave);
        _lblUpdated = new Label { Location = new Point(196, y + 8), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary };
        scroll.Controls.Add(_lblUpdated);
        y += 44;

        SetFormEnabled(false);
        split.Panel2.Controls.Add(scroll);

        Controls.Add(split);
        split.SplitterDistance = 300;
    }

    private static void Campo(Panel parent, string etiqueta, ref TextBox box, ref int y, int alto)
    {
        parent.Controls.Add(new Label { Text = etiqueta, Location = new Point(4, y), AutoSize = true, Font = AppTheme.BoldFont }); y += 22;
        box = new TextBox { Location = new Point(4, y), Width = 560, Height = alto, Multiline = true, ScrollBars = ScrollBars.Vertical };
        parent.Controls.Add(box); y += alto + 10;
    }

    private void LoadDevs()
    {
        _devs = _db.Developers.AsNoTracking().Where(d => d.IsActive).OrderBy(d => d.FullName).ToList();
        var conFicha = _db.DeveloperProfiles.AsNoTracking().Select(p => p.DeveloperId).ToHashSet();
        _gridDevs.Rows.Clear();
        foreach (var d in _devs)
            _gridDevs.Rows.Add(d.FullName, conFicha.Contains(d.Id) ? "✓" : "—");
        CargarFicha();
    }

    private Developer? Seleccionado() =>
        _gridDevs.CurrentRow is { Index: >= 0 } r && r.Index < _devs.Count ? _devs[r.Index] : null;

    private void CargarFicha()
    {
        var d = Seleccionado();
        if (d == null)
        {
            _lblDevName.Text = "Selecciona un desarrollador";
            LimpiarCampos(); SetFormEnabled(false); _lblUpdated.Text = "";
            return;
        }

        _lblDevName.Text = d.FullName;
        var f = _profiles.Obtener(d.Id);
        _txtStrengths.Text  = f?.Strengths ?? "";
        _txtWeaknesses.Text = f?.Weaknesses ?? "";
        _txtStack.Text      = f?.TechStack ?? "";
        _txtGrowth.Text     = f?.GrowthExpectations ?? "";
        _txtNotes.Text      = f?.Notes ?? "";
        _nudSalary.Value    = f?.Salary is decimal s ? Math.Clamp(s, _nudSalary.Minimum, _nudSalary.Maximum) : 0;
        _cbxCurrency.Text   = string.IsNullOrWhiteSpace(f?.Currency) ? "MXN" : f!.Currency;
        _lblUpdated.Text    = f != null ? $"Última actualización: {f.UpdatedAt.ToLocalTime():dd/MM/yyyy HH:mm}" : "Sin ficha todavía.";
        SetFormEnabled(true);
    }

    private void Guardar()
    {
        var d = Seleccionado();
        if (d == null) return;
        decimal? salario = _nudSalary.Value > 0 ? _nudSalary.Value : null;
        var (ok, mensaje) = _profiles.Guardar(d.Id, _txtStrengths.Text, _txtWeaknesses.Text, _txtStack.Text,
            salario, _cbxCurrency.Text, _txtGrowth.Text, _txtNotes.Text);
        if (ok) { LoadDevs(); ReSeleccionar(d.Id); }
        MessageBox.Show(mensaje, ok ? "Guardado" : "No se pudo guardar",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void ReSeleccionar(int devId)
    {
        int i = _devs.FindIndex(x => x.Id == devId);
        if (i >= 0 && i < _gridDevs.Rows.Count) _gridDevs.CurrentCell = _gridDevs.Rows[i].Cells["Dev"];
    }

    private void LimpiarCampos()
    {
        _txtStrengths.Clear(); _txtWeaknesses.Clear(); _txtStack.Clear(); _txtGrowth.Clear(); _txtNotes.Clear();
        _nudSalary.Value = 0; _cbxCurrency.Text = "MXN";
    }

    private void SetFormEnabled(bool on)
    {
        _txtStrengths.Enabled = _txtWeaknesses.Enabled = _txtStack.Enabled = _txtGrowth.Enabled = _txtNotes.Enabled = on;
        _nudSalary.Enabled = _cbxCurrency.Enabled = _btnSave.Enabled = on;
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadDevs(); }
}
