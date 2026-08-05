using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Qué requerimientos van en el sprint. Lo marcado es lo que queda: desmarcar saca del sprint.
///
/// Se ofrecen los requerimientos SIN sprint más los de ESTE sprint; los que ya viven en otro
/// sprint no aparecen — moverlos debe ser una decisión tomada desde el otro sprint, no un
/// accidente de casilla. Los cancelados tampoco: no hay nada que seguirles.
///
/// Con el backlog crecido la lista deja de ser navegable a ojo, así que hay <b>buscador</b> por
/// número, título y estado. Lo marcado NO vive en la lista visible sino en un conjunto de ids
/// (<see cref="_marcados"/>): si dependiera de las casillas pintadas, filtrar borraría en silencio
/// todo lo que se hubiera marcado antes de escribir en el buscador.
/// </summary>
public class SprintRequirementsPickerForm : ResponsiveForm
{
    private CheckedListBox _lst = null!;
    private TextBox _txtBuscar = null!;
    private CheckBox _chkSoloMarcados = null!;
    private Label _lblResumen = null!;

    private readonly List<Requirement> _candidatos;
    private readonly int _sprintId;

    /// <summary>Ids marcados ahora mismo, con independencia de lo que el filtro deje ver.</summary>
    private readonly HashSet<int> _marcados = [];

    /// <summary>Lo que está pintado en la lista, en orden: la casilla i corresponde a _visibles[i].</summary>
    private List<Requirement> _visibles = [];

    /// <summary>Evita que repintar la lista se tome por clics del usuario y ensucie _marcados.</summary>
    private bool _repintando;

    /// <summary>Ids que deben quedar en el sprint (solo válido si el diálogo aceptó).</summary>
    public List<int> Seleccionados { get; } = [];

    public SprintRequirementsPickerForm(AppDbContext db, int sprintId, string sprintNombre)
    {
        _sprintId = sprintId;
        _candidatos = db.Requirements.AsNoTracking()
            .Where(r => r.Status != RequirementStatus.Cancelado
                     && (r.SprintId == null || r.SprintId == sprintId))
            .OrderBy(r => r.SprintId == sprintId ? 0 : 1)   // primero lo que ya está dentro
            .ThenByDescending(r => r.Id)
            .ToList();

        foreach (var r in _candidatos.Where(r => r.SprintId == sprintId))
            _marcados.Add(r.Id);

        BuildUI(sprintNombre);
    }

    private void BuildUI(string sprintNombre)
    {
        Text = $"Requerimientos del sprint «{sprintNombre}»";
        Size = new Size(720, 600);
        MinimumSize = new Size(560, 440);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(14, 10, 14, 10) };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));   // ayuda
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));   // buscador
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // lista
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));   // resumen
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));   // botones
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        tbl.Controls.Add(new Label
        {
            Text = "Marca lo que entra al sprint. Lo que desmarques vuelve al backlog. Los requerimientos de OTROS sprints no se listan.",
            Dock = DockStyle.Fill, ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        // ── Buscador ────────────────────────────────────────────────────────────
        var barra = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };

        _txtBuscar = new TextBox
        {
            Width = 300, Margin = new Padding(0, 3, 8, 0),
            PlaceholderText = "Buscar por número, título o estado…"
        };
        _txtBuscar.TextChanged += (_, _) => Repintar();

        var btnLimpiar = AppTheme.MakeSecondaryButton("✖ Limpiar", 95, 26);
        btnLimpiar.Margin = new Padding(0, 3, 12, 0);
        btnLimpiar.Click += (_, _) => { _txtBuscar.Clear(); _txtBuscar.Focus(); };

        // Con el buscador vacío la lista puede ser larguísima; esto responde de un clic a la
        // pregunta que uno se hace antes de guardar: «¿qué acabo de dejar dentro?».
        _chkSoloMarcados = new CheckBox { Text = "Solo los marcados", AutoSize = true, Margin = new Padding(0, 7, 12, 0) };
        _chkSoloMarcados.CheckedChanged += (_, _) => Repintar();

        var btnTodos = AppTheme.MakeSecondaryButton("Marcar visibles", 130, 26);
        btnTodos.Margin = new Padding(0, 3, 6, 0);
        btnTodos.Click += (_, _) => MarcarVisibles(true);

        var btnNinguno = AppTheme.MakeSecondaryButton("Desmarcar visibles", 150, 26);
        btnNinguno.Margin = new Padding(0, 3, 0, 0);
        btnNinguno.Click += (_, _) => MarcarVisibles(false);

        barra.Controls.AddRange([_txtBuscar, btnLimpiar, _chkSoloMarcados, btnTodos, btnNinguno]);
        tbl.Controls.Add(barra, 0, 1);

        // ── Lista ───────────────────────────────────────────────────────────────
        _lst = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
        _lst.ItemCheck += Lst_ItemCheck;
        tbl.Controls.Add(_lst, 0, 2);

        _lblResumen = new Label { Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary };
        tbl.Controls.Add(_lblResumen, 0, 3);

        var botones = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancelar = AppTheme.MakeSecondaryButton("Cancelar", 100);
        cancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var aceptar = AppTheme.MakePrimaryButton("Guardar", 110);
        aceptar.Click += (_, _) => Aceptar();
        botones.Controls.AddRange([cancelar, aceptar]);
        tbl.Controls.Add(botones, 0, 4);

        Controls.Add(tbl);
        // Sin AcceptButton: con el foco en el buscador, un Enter para «buscar ya» cerraría el
        // diálogo guardando. Se guarda pulsando el botón.
        CancelButton = cancelar;
        Repintar();
    }

    /// <summary>
    /// ItemCheck se dispara ANTES de que la casilla cambie, así que el valor bueno es e.NewValue,
    /// no GetItemChecked. Se ignora mientras se repinta: ahí los cambios los hace el código.
    /// </summary>
    private void Lst_ItemCheck(object? s, ItemCheckEventArgs e)
    {
        if (_repintando || e.Index < 0 || e.Index >= _visibles.Count) return;

        int id = _visibles[e.Index].Id;
        if (e.NewValue == CheckState.Checked) _marcados.Add(id);
        else _marcados.Remove(id);

        // El resumen se actualiza después de que el control aplique el cambio.
        BeginInvoke(PintarResumen);
    }

    private void MarcarVisibles(bool marcar)
    {
        foreach (var r in _visibles)
        {
            if (marcar) _marcados.Add(r.Id);
            else _marcados.Remove(r.Id);
        }
        Repintar();
    }

    /// <summary>Rehace la lista visible aplicando el buscador, conservando lo marcado.</summary>
    private void Repintar()
    {
        var texto = _txtBuscar.Text.Trim();
        IEnumerable<Requirement> q = _candidatos;

        if (_chkSoloMarcados.Checked) q = q.Where(r => _marcados.Contains(r.Id));
        if (texto.Length > 0) q = q.Where(r => Coincide(r, texto));

        _visibles = q.ToList();

        _repintando = true;
        try
        {
            _lst.BeginUpdate();
            _lst.Items.Clear();
            foreach (var r in _visibles)
            {
                int idx = _lst.Items.Add(Etiqueta(r));
                _lst.SetItemChecked(idx, _marcados.Contains(r.Id));
            }
            _lst.EndUpdate();
        }
        finally { _repintando = false; }

        PintarResumen();
    }

    /// <summary>
    /// Coincide por número (con o sin «#»), título o estado. Se parte en palabras y se exigen
    /// TODAS: escribir «pagos pruebas» busca lo de pagos que además está en pruebas, en vez de
    /// devolver todo lo que tenga cualquiera de las dos.
    /// </summary>
    internal static bool Coincide(Requirement r, string texto)
    {
        var campos = $"#{r.Id} {r.Id} {r.Title} {EtiquetaEstado(r.Status)}";
        foreach (var palabra in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!campos.Contains(palabra, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private static string Etiqueta(Requirement r) =>
        $"#{r.Id}  {r.Title}   ({EtiquetaEstado(r.Status)}, {r.ProgressPercent}%)";

    private void PintarResumen()
    {
        // Se cuenta sobre _marcados, no sobre las casillas: hay marcados que el filtro no enseña y
        // callarlos haría creer que se perdieron.
        int marcados = _marcados.Count;
        bool filtrando = _txtBuscar.Text.Trim().Length > 0 || _chkSoloMarcados.Checked;

        _lblResumen.Text = filtrando
            ? $"{marcados} de {_candidatos.Count} marcados en total  ·  mostrando {_visibles.Count}"
            : $"{marcados} de {_candidatos.Count} marcados.";

        if (filtrando && _visibles.Count == 0)
        {
            _lblResumen.Text += "  ·  ningún requerimiento coincide";
            _lblResumen.ForeColor = AppTheme.Warning;
        }
        else _lblResumen.ForeColor = AppTheme.TextSecondary;
    }

    private void Aceptar()
    {
        Seleccionados.Clear();
        // Se recorren los candidatos y no _marcados para conservar el orden y descartar cualquier
        // id que ya no exista entre ellos.
        foreach (var r in _candidatos)
            if (_marcados.Contains(r.Id)) Seleccionados.Add(r.Id);

        DialogResult = DialogResult.OK; Close();
    }

    private static string EtiquetaEstado(RequirementStatus s) => s switch
    {
        RequirementStatus.PorEstimar   => "por estimar",
        RequirementStatus.Estimado     => "estimado",
        RequirementStatus.EnDesarrollo => "en desarrollo",
        RequirementStatus.EnPruebas    => "en pruebas",
        RequirementStatus.PorEntregar  => "por entregar",
        RequirementStatus.Entregado    => "entregado",
        _                              => s.ToString().ToLowerInvariant()
    };
}
