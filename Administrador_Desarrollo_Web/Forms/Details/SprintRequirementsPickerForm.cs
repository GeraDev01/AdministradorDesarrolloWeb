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
/// </summary>
public class SprintRequirementsPickerForm : ResponsiveForm
{
    private CheckedListBox _lst = null!;
    private Label _lblResumen = null!;
    private readonly List<Requirement> _candidatos;
    private readonly int _sprintId;

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
        BuildUI(sprintNombre);
    }

    private void BuildUI(string sprintNombre)
    {
        Text = $"Requerimientos del sprint «{sprintNombre}»";
        Size = new Size(680, 560);
        MinimumSize = new Size(560, 420);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(14, 10, 14, 10) };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        tbl.Controls.Add(new Label
        {
            Text = "Marca lo que entra al sprint. Lo que desmarques vuelve al backlog. Los requerimientos de OTROS sprints no se listan.",
            Dock = DockStyle.Fill, ForeColor = AppTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _lst = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
        foreach (var r in _candidatos)
        {
            int idx = _lst.Items.Add($"#{r.Id}  {r.Title}   ({EtiquetaEstado(r.Status)}, {r.ProgressPercent}%)");
            if (r.SprintId == _sprintId) _lst.SetItemChecked(idx, true);
        }
        _lst.ItemCheck += (_, _) => BeginInvoke(PintarResumen);
        tbl.Controls.Add(_lst, 0, 1);

        _lblResumen = new Label { Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary };
        tbl.Controls.Add(_lblResumen, 0, 2);

        var botones = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancelar = AppTheme.MakeSecondaryButton("Cancelar", 100);
        cancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var aceptar = AppTheme.MakePrimaryButton("Guardar", 110);
        aceptar.Click += (_, _) => Aceptar();
        botones.Controls.AddRange([cancelar, aceptar]);
        tbl.Controls.Add(botones, 0, 3);

        Controls.Add(tbl);
        AcceptButton = aceptar; CancelButton = cancelar;
        PintarResumen();
    }

    private void PintarResumen() =>
        _lblResumen.Text = $"{_lst.CheckedItems.Count} de {_candidatos.Count} marcados.";

    private void Aceptar()
    {
        Seleccionados.Clear();
        for (int i = 0; i < _candidatos.Count; i++)
            if (_lst.GetItemChecked(i)) Seleccionados.Add(_candidatos[i].Id);
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
