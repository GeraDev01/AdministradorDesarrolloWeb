using System.Diagnostics;
using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Detalle del puntaje de un equipo en un período: sus puntos propios (criterios de
/// equipo, borrables) y el aporte individual de cada integrante (solo lectura).
/// Total = aporte de integrantes + puntos propios del equipo.
/// </summary>
public class TeamPointsDetailForm : ResponsiveForm
{
    private static readonly string[] Months =
        ["Enero","Febrero","Marzo","Abril","Mayo","Junio","Julio","Agosto","Septiembre","Octubre","Noviembre","Diciembre"];

    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly int _teamId, _month, _year;
    private readonly string _teamName;

    private DataGridView _gridTeam = null!;
    private DataGridView _gridMembers = null!;
    private Label _lblSummary = null!;
    private List<TeamPointEntry> _teamEntries = [];

    public bool Changed { get; private set; }

    public TeamPointsDetailForm(AppDbContext db, AuditService audit, int teamId, string teamName, int month, int year)
    {
        _db = db; _audit = audit; _teamId = teamId; _teamName = teamName; _month = month; _year = year;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        Text = $"Puntaje del equipo — {_teamName}";
        Size = new Size(860, 620);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MinimumSize = new Size(720, 460);
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));   // header
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));   // summary
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));   // team toolbar
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));    // team grid
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));   // members label
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));    // members grid

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = $"  🏆  Puntaje del equipo — {_teamName}", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        _lblSummary = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.BoldFont, Padding = new Padding(14, 0, 0, 0) };

        var teamBar = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = AppTheme.ContentBg };
        teamBar.Controls.Add(new Label { Text = "Puntos propios del equipo:", Location = new Point(12, 12), AutoSize = true, Font = AppTheme.BoldFont });
        var btnView = AppTheme.MakeSecondaryButton("👁 Ver captura", 140); btnView.Location = new Point(560, 8); btnView.Click += BtnViewShot_Click;
        var btnDel  = AppTheme.MakeDangerButton("🗑 Eliminar", 120);       btnDel.Location  = new Point(705, 8); btnDel.Click  += BtnDelTeamEntry_Click;
        btnView.Anchor = btnDel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        teamBar.Controls.AddRange([btnView, btnDel]);

        _gridTeam = AppTheme.MakeGrid();
        _gridTeam.MultiSelect = true;
        _gridTeam.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", Name = "Id", Visible = false });
        _gridTeam.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Criterio",   Name = "Crit",    FillWeight = 26 });
        _gridTeam.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Descripción", Name = "Desc",   FillWeight = 30 });
        _gridTeam.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Puntos",     Name = "Pts",     FillWeight = 9  });
        _gridTeam.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha",      Name = "Date",    FillWeight = 12 });
        _gridTeam.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "📷",         Name = "Shot",    FillWeight = 5  });
        _gridTeam.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Comentario", Name = "Comment", FillWeight = 18 });
        _gridTeam.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) BtnViewShot_Click(null, EventArgs.Empty); };
        var pnlTeam = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 6), Margin = Padding.Empty, BackColor = AppTheme.ContentBg };
        pnlTeam.Controls.Add(_gridTeam);

        var lblMembers = new Label { Dock = DockStyle.Fill, Text = "  Aporte de integrantes (puntos individuales):", TextAlign = ContentAlignment.MiddleLeft, Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary };

        _gridMembers = AppTheme.MakeGrid();
        _gridMembers.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrollador",       Name = "Dev", FillWeight = 70 });
        _gridMembers.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Puntos individuales", Name = "Pts", FillWeight = 30 });
        var pnlMembers = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 10), Margin = Padding.Empty, BackColor = AppTheme.ContentBg };
        pnlMembers.Controls.Add(_gridMembers);

        outer.Controls.Add(hdr,        0, 0);
        outer.Controls.Add(_lblSummary,0, 1);
        outer.Controls.Add(teamBar,    0, 2);
        outer.Controls.Add(pnlTeam,    0, 3);
        outer.Controls.Add(lblMembers, 0, 4);
        outer.Controls.Add(pnlMembers, 0, 5);
        Controls.Add(outer);
    }

    private void LoadData()
    {
        _teamEntries = _db.TeamPointEntries.Include(t => t.Criterion)
            .Where(t => t.TeamId == _teamId && t.Year == _year && t.Month == _month)
            .OrderByDescending(t => t.Date).AsNoTracking().ToList();

        var members = _db.Developers.Where(d => d.TeamId == _teamId && d.IsActive).OrderBy(d => d.FullName).AsNoTracking().ToList();
        // Solo puntos individuales APROBADOS aportan al total del equipo (coherente con el ranking).
        var indiv = _db.PointEntries.Where(p => p.Year == _year && p.Month == _month && p.ApprovalStatus == PointApprovalStatus.Aprobado).AsNoTracking().ToList();

        // Team own entries
        _gridTeam.Rows.Clear();
        foreach (var te in _teamEntries)
        {
            int i = _gridTeam.Rows.Add(te.Id, te.Criterion.Name, te.Criterion.Description ?? "",
                (te.Points >= 0 ? "+" : "") + te.Points,
                te.Date.ToLocalTime().ToString("dd/MM/yyyy"),
                te.Screenshot != null ? "📷" : "",
                te.Comment ?? "");
            _gridTeam.Rows[i].Cells["Pts"].Style.ForeColor = te.Points >= 0 ? AppTheme.Success : AppTheme.Danger;
            _gridTeam.Rows[i].Cells["Pts"].Style.Font = AppTheme.BoldFont;
        }
        if (_teamEntries.Count == 0) _gridTeam.Rows.Add("", "(sin puntos propios en este período)", "", "", "", "", "");

        // Member contributions
        int membersSum = 0;
        _gridMembers.Rows.Clear();
        foreach (var m in members)
        {
            int mp = indiv.Where(e => e.DeveloperId == m.Id).Sum(e => e.Points);
            membersSum += mp;
            int i = _gridMembers.Rows.Add(m.FullName, (mp >= 0 ? "+" : "") + mp);
            _gridMembers.Rows[i].Cells["Pts"].Style.ForeColor = mp >= 0 ? AppTheme.Success : (mp < 0 ? AppTheme.Danger : AppTheme.TextSecondary);
        }
        if (members.Count == 0) _gridMembers.Rows.Add("(sin integrantes)", "");

        int teamOwn = _teamEntries.Sum(t => t.Points);
        int total = membersSum + teamOwn;
        string monthName = _month >= 1 && _month <= 12 ? Months[_month - 1] : _month.ToString();
        _lblSummary.Text = $"{monthName} {_year}   ·   Total del equipo {(total >= 0 ? "+" : "")}{total}   =   integrantes {(membersSum >= 0 ? "+" : "")}{membersSum}   +   propios del equipo {(teamOwn >= 0 ? "+" : "")}{teamOwn}";
        _lblSummary.ForeColor = total > 0 ? AppTheme.Success : total < 0 ? AppTheme.Danger : AppTheme.TextPrimary;
    }

    private void BtnDelTeamEntry_Click(object? s, EventArgs e)
    {
        var ids = _gridTeam.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Cells["Id"].Value).OfType<int>().ToList();
        if (ids.Count == 0) { MessageBox.Show("Selecciona al menos una entrada propia del equipo.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"¿Eliminar {ids.Count} entrada(s) de puntos del equipo?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var toRemove = _db.TeamPointEntries.Where(t => ids.Contains(t.Id)).ToList();
        _db.TeamPointEntries.RemoveRange(toRemove);
        _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "TeamPointEntry", string.Join(",", ids), $"{ids.Count} entradas de equipo eliminadas");
        Changed = true;
        LoadData();
    }

    private void BtnViewShot_Click(object? s, EventArgs e)
    {
        if (_gridTeam.CurrentRow?.Cells["Id"].Value is not int id)
        { MessageBox.Show("Selecciona una entrada.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var te = _teamEntries.FirstOrDefault(t => t.Id == id);
        if (te?.Screenshot == null || te.Screenshot.Length == 0)
        { MessageBox.Show("Esta entrada no tiene captura.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "advweb_shot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var name = string.IsNullOrWhiteSpace(te.ScreenshotFileName) ? "captura.png" : te.ScreenshotFileName!;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            var path = Path.Combine(dir, name);
            File.WriteAllBytes(path, te.Screenshot);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"No se pudo abrir la captura:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
