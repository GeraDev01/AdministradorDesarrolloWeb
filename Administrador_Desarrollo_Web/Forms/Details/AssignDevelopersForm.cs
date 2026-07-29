using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Forms;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class AssignDevelopersForm : Form
{
    private CheckedListBox _lstDevs = null!;
    private readonly List<Developer> _allDevs;

    public List<int> SelectedDevIds { get; private set; } = [];

    public AssignDevelopersForm(List<Developer> allDevs, List<int> currentIds)
    {
        _allDevs = allDevs;
        BuildUI(currentIds);
    }

    private void BuildUI(List<int> currentIds)
    {
        Text = "Asignar Desarrolladores";
        Size = new Size(380, 400);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = AppTheme.ContentBg;
        Font = AppTheme.DefaultFont;

        var pnlHeader = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        pnlHeader.Controls.Add(new Label
        {
            Text = "  👥  Asignar Desarrolladores",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = AppTheme.HeaderFont,
            TextAlign = ContentAlignment.MiddleLeft
        });

        _lstDevs = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            Font = AppTheme.DefaultFont,
            BorderStyle = BorderStyle.None,
            BackColor = AppTheme.ContentBg
        };

        foreach (var dev in _allDevs.Where(d => d.IsActive))
        {
            int idx = _lstDevs.Items.Add($"{dev.FullName} ({dev.Seniority ?? "—"})");
            if (currentIds.Contains(dev.Id))
                _lstDevs.SetItemChecked(idx, true);
        }

        var pnlBtns = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10),
            BackColor = AppTheme.ContentBg
        };
        var btnOk = AppTheme.MakePrimaryButton("Aceptar", 100);
        btnOk.Click += BtnOk_Click;
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        pnlBtns.Controls.AddRange([btnOk, btnCancel]);

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.Controls.Add(pnlHeader, 0, 0);
        outer.Controls.Add(_lstDevs,  0, 1);
        outer.Controls.Add(pnlBtns,   0, 2);
        Controls.Add(outer);
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        SelectedDevIds = [];
        var activeDevs = _allDevs.Where(d => d.IsActive).ToList();
        for (int i = 0; i < _lstDevs.Items.Count; i++)
            if (_lstDevs.GetItemChecked(i))
                SelectedDevIds.Add(activeDevs[i].Id);

        DialogResult = DialogResult.OK;
        Close();
    }
}
