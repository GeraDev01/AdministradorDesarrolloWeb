using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Opciones de sincronización SELECTIVA: elegir tipos de work item, estados y/o personas para no
/// traer miles de items y que la sync sea rápida. Sin nada marcado equivale a traer todo (menos los
/// Removed). Los filtros se combinan con Y (tipos Y estados Y personas).
/// </summary>
public class DevOpsSyncOptionsForm : Form
{
    private CheckedListBox _clbTypes = null!;
    private CheckedListBox _clbStates = null!;
    private CheckedListBox _clbPeople = null!;
    private readonly List<(string Label, string Email)> _people;

    /// <summary>Filtro resultante (tras aceptar).</summary>
    public DevOpsSyncFilter Filter { get; private set; } = new([], [], []);

    public DevOpsSyncOptionsForm(IEnumerable<string> knownTypes, IEnumerable<string> knownStates, IReadOnlyList<Developer> devs)
    {
        // Tipos: los que ya se hayan visto + un catálogo por omisión (por si aún no hay datos).
        var tipos = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Bug", "Task", "User Story", "Product Backlog Item", "Feature", "Epic", "Issue" };
        foreach (var t in knownTypes) if (!string.IsNullOrWhiteSpace(t)) tipos.Add(t.Trim());

        // Estados: los que ya se hayan visto + los comunes de las plantillas Agile/Scrum/Basic.
        var estados = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
            { "New", "Active", "Resolved", "Closed", "To Do", "Doing", "Done", "In Progress", "Approved", "Committed" };
        foreach (var s in knownStates) if (!string.IsNullOrWhiteSpace(s)) estados.Add(s.Trim());

        // Personas: solo desarrolladores CON correo (el correo es lo que empata en la WIQL).
        _people = devs.Where(d => !string.IsNullOrWhiteSpace(d.Email))
                      .OrderBy(d => d.FullName)
                      .Select(d => (d.FullName, d.Email!.Trim()))
                      .ToList();

        BuildUI(tipos, estados);
    }

    private void BuildUI(IEnumerable<string> tipos, IEnumerable<string> estados)
    {
        Text = "Sincronización selectiva";
        Size = new Size(760, 540);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 3, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        for (int i = 0; i < 3; i++) root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));

        var intro = new Label
        {
            Dock = DockStyle.Fill, ForeColor = AppTheme.TextSecondary,
            Text = "Marca solo lo que necesitas para que la sincronización sea rápida. Si no marcas nada, se traen todos.\n" +
                   "Los tres filtros se combinan (tipos Y estados Y personas)."
        };
        root.Controls.Add(intro, 0, 0);
        root.SetColumnSpan(intro, 3);

        root.Controls.Add(new Label { Text = "Tipos de work item", Dock = DockStyle.Fill, Font = AppTheme.BoldFont }, 0, 1);
        root.Controls.Add(new Label { Text = "Estados (columna)",  Dock = DockStyle.Fill, Font = AppTheme.BoldFont }, 1, 1);
        root.Controls.Add(new Label { Text = "Personas (por correo)", Dock = DockStyle.Fill, Font = AppTheme.BoldFont }, 2, 1);

        _clbTypes = MakeList(new Padding(0, 0, 6, 0));
        foreach (var t in tipos) _clbTypes.Items.Add(t);
        root.Controls.Add(_clbTypes, 0, 2);

        _clbStates = MakeList(new Padding(3, 0, 3, 0));
        foreach (var s in estados) _clbStates.Items.Add(s);
        root.Controls.Add(_clbStates, 1, 2);

        _clbPeople = MakeList(new Padding(6, 0, 0, 0));
        foreach (var (label, _) in _people) _clbPeople.Items.Add(label);
        if (_people.Count == 0) _clbPeople.Items.Add("(no hay desarrolladores con correo)");
        root.Controls.Add(_clbPeople, 2, 2);

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnOk = AppTheme.MakePrimaryButton("Sincronizar", 140);
        btnOk.Click += BtnOk_Click;
        flow.Controls.AddRange([btnOk, btnCancel]);
        root.Controls.Add(flow, 0, 3);
        root.SetColumnSpan(flow, 3);

        Controls.Add(root);
        AcceptButton = btnOk;
    }

    private static CheckedListBox MakeList(Padding margin) => new()
    {
        Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false,
        BorderStyle = BorderStyle.FixedSingle, Margin = margin
    };

    private void BtnOk_Click(object? s, EventArgs e)
    {
        var types  = _clbTypes.CheckedItems.Cast<string>().ToList();
        var states = _clbStates.CheckedItems.Cast<string>().ToList();
        var emails = new List<string>();
        foreach (int idx in _clbPeople.CheckedIndices)
            if (idx < _people.Count) emails.Add(_people[idx].Email);

        Filter = new DevOpsSyncFilter(types, emails, states);
        DialogResult = DialogResult.OK;
        Close();
    }
}
