using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>Historial de rotaciones de desarrolladores entre equipos.</summary>
public class TeamRotationHistoryForm : Form
{
    private readonly AppDbContext _db;
    private DataGridView _grid = null!;

    public TeamRotationHistoryForm(AppDbContext db)
    {
        _db = db;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        Text = "Historial de rotaciones";
        Size = new Size(820, 520);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MinimumSize = new Size(680, 380);
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label { Text = "  📜  Historial de rotaciones", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Fecha",         Name = "Date", FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Desarrollador", Name = "Dev",  FillWeight = 24 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "De",            Name = "From", FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "A",             Name = "To",   FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nota",          Name = "Note", FillWeight = 22 });

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 10), Margin = Padding.Empty, BackColor = AppTheme.ContentBg };
        pnlGrid.Controls.Add(_grid);

        outer.Controls.Add(hdr,     0, 0);
        outer.Controls.Add(pnlGrid, 0, 1);
        Controls.Add(outer);
    }

    private void LoadData()
    {
        var rows = _db.TeamRotations.AsNoTracking().OrderByDescending(r => r.RotatedAt).Take(1000).ToList();
        _grid.Rows.Clear();
        foreach (var r in rows)
        {
            int i = _grid.Rows.Add(r.RotatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), r.DeveloperName, r.FromTeamName, r.ToTeamName, r.Note ?? "");
            _grid.Rows[i].Cells["To"].Style.ForeColor = AppTheme.SidebarActive;
            _grid.Rows[i].Cells["To"].Style.Font = AppTheme.BoldFont;
        }
        if (rows.Count == 0) _grid.Rows.Add("—", "(sin rotaciones registradas)", "", "", "");
    }
}
