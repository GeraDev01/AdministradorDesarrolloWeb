using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Details;

public class MinuteDetailForm : Form
{
    private readonly AppDbContext _db;
    private readonly List<Developer> _devs;

    private ComboBox _cbxType = null!;
    private DateTimePicker _dtpDate = null!;
    private TextBox _txtTitle = null!;
    private RichTextBox _rtContent = null!;
    private DataGridView _gridItems = null!;

    public Minute Result { get; private set; } = new();

    public MinuteDetailForm(AppDbContext db, Minute? minute = null)
    {
        _db = db;
        _devs = db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).ToList();
        BuildUI();
        if (minute != null) Populate(minute);
    }

    private void BuildUI()
    {
        Text = "Minuta"; Size = new Size(760, 680);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var pnlHeader = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        pnlHeader.Controls.Add(new Label
        {
            Text = "  📋  Detalle de Minuta", Dock = DockStyle.Fill,
            ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft
        });

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1,
            Margin = Padding.Empty, Padding = new Padding(20, 12, 20, 0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));  // tipo + fecha
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));  // título
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 130f)); // contenido
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // action items
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));  // buttons
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // Row 0: tipo + fecha
        var row0 = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        row0.Controls.Add(new Label { Text = "Tipo:", AutoSize = true, Margin = new Padding(0, 12, 6, 0) });
        _cbxType = new ComboBox { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 8, 20, 0) };
        _cbxType.Items.AddRange(["Daily", "Sesión", "Otro"]);
        _cbxType.SelectedIndex = 0;
        row0.Controls.Add(_cbxType);
        row0.Controls.Add(new Label { Text = "Fecha:", AutoSize = true, Margin = new Padding(0, 12, 6, 0) });
        _dtpDate = new DateTimePicker { Width = 150, Format = DateTimePickerFormat.Short, Margin = new Padding(0, 8, 0, 0) };
        row0.Controls.Add(_dtpDate);
        body.Controls.Add(row0, 0, 0);

        // Row 1: título
        var row1 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50f));
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row1.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        row1.Controls.Add(new Label { Text = "Título *", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _txtTitle = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 8) };
        row1.Controls.Add(_txtTitle, 1, 0);
        body.Controls.Add(row1, 0, 1);

        // Row 2: contenido
        var row2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70f));
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        row2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        row2.Controls.Add(new Label { Text = "Contenido", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Padding = new Padding(0, 4, 0, 0) }, 0, 0);
        _rtContent = new RichTextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 6) };
        row2.Controls.Add(_rtContent, 1, 0);
        body.Controls.Add(row2, 0, 2);

        // Row 3: action items
        var row3 = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        row3.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        row3.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        row3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var itemsHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
        itemsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        itemsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 235f));
        itemsHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        itemsHeader.Controls.Add(new Label { Text = "Compromisos / Items de acción", Dock = DockStyle.Fill, Font = AppTheme.BoldFont, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var btnAdd = AppTheme.MakePrimaryButton("➕ Agregar", 110, 26); btnAdd.Margin = new Padding(0, 1, 6, 0); btnAdd.Click += BtnAddItem_Click;
        var btnDel = AppTheme.MakeDangerButton("🗑 Eliminar", 110, 26); btnDel.Margin = new Padding(0, 1, 0, 0); btnDel.Click += BtnDelItem_Click;
        btnPanel.Controls.AddRange([btnAdd, btnDel]);
        itemsHeader.Controls.Add(btnPanel, 1, 0);
        row3.Controls.Add(itemsHeader, 0, 0);

        _gridItems = AppTheme.MakeGrid();
        _gridItems.ReadOnly = false;
        _gridItems.AllowUserToAddRows = false;
        _gridItems.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;

        var colDesc = new DataGridViewTextBoxColumn { HeaderText = "Descripción", Name = "Desc", FillWeight = 42 };
        var colDev  = new DataGridViewComboBoxColumn
        {
            HeaderText = "Responsable", Name = "Dev", FillWeight = 25,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        };
        colDev.Items.Add("(Ninguno)");
        foreach (var d in _devs) colDev.Items.Add(d.FullName);
        colDev.DefaultCellStyle.NullValue = "(Ninguno)";
        var colDate = new DataGridViewTextBoxColumn { HeaderText = "Fecha límite", Name = "DueDate", FillWeight = 20 };
        var colDone = new DataGridViewCheckBoxColumn { HeaderText = "✓ Hecho", Name = "Done", FillWeight = 13 };
        _gridItems.Columns.AddRange([colDesc, colDev, colDate, colDone]);
        _gridItems.DataError += (_, e) => e.Cancel = true;

        var gridWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 2, 0, 4) };
        gridWrap.Controls.Add(_gridItems);
        row3.Controls.Add(gridWrap, 0, 1);
        body.Controls.Add(row3, 0, 3);

        // Row 4: buttons
        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100); btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnSave = AppTheme.MakePrimaryButton("Guardar", 100); btnSave.Click += BtnSave_Click;
        btnRow.Controls.AddRange([btnCancel, btnSave]);
        body.Controls.Add(btnRow, 0, 4);

        tbl.Controls.Add(pnlHeader, 0, 0);
        tbl.Controls.Add(body, 0, 1);
        Controls.Add(tbl);
        AcceptButton = btnSave;
    }

    private void Populate(Minute m)
    {
        Result = m;
        _cbxType.SelectedIndex = (int)m.Type;
        _dtpDate.Value = m.Date.ToLocalTime();
        _txtTitle.Text = m.Title;
        _rtContent.Text = m.Content ?? "";
        foreach (var item in m.ActionItems)
        {
            var devName = item.ResponsibleDeveloperId.HasValue
                ? _devs.FirstOrDefault(d => d.Id == item.ResponsibleDeveloperId)?.FullName ?? "(Ninguno)"
                : "(Ninguno)";
            _gridItems.Rows.Add(item.Description, devName, item.DueDate?.ToString("dd/MM/yyyy") ?? "", item.IsCompleted);
        }
    }

    private void BtnAddItem_Click(object? s, EventArgs e)
    {
        int i = _gridItems.Rows.Add("", "(Ninguno)", "", false);
        _gridItems.CurrentCell = _gridItems.Rows[i].Cells["Desc"];
        _gridItems.BeginEdit(true);
    }

    private void BtnDelItem_Click(object? s, EventArgs e)
    {
        if (_gridItems.CurrentRow != null) _gridItems.Rows.Remove(_gridItems.CurrentRow);
    }

    private void BtnSave_Click(object? s, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtTitle.Text))
        { MessageBox.Show("El título es obligatorio.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        Result.Type = (MinuteType)_cbxType.SelectedIndex;
        Result.Date = _dtpDate.Value.ToUniversalTime();
        Result.Title = _txtTitle.Text.Trim();
        Result.Content = string.IsNullOrWhiteSpace(_rtContent.Text) ? null : _rtContent.Text;

        Result.ActionItems.Clear();
        foreach (DataGridViewRow row in _gridItems.Rows)
        {
            var desc = row.Cells["Desc"].Value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(desc)) continue;
            var devName = row.Cells["Dev"].Value?.ToString();
            var dev = _devs.FirstOrDefault(d => d.FullName == devName);
            var dueStr = row.Cells["DueDate"].Value?.ToString();
            DateTime.TryParseExact(dueStr, ["dd/MM/yyyy", "d/M/yyyy"], null, System.Globalization.DateTimeStyles.None, out var due);
            bool done = row.Cells["Done"].Value is true;
            Result.ActionItems.Add(new MinuteActionItem
            {
                Description = desc,
                ResponsibleDeveloperId = dev?.Id,
                DueDate = string.IsNullOrEmpty(dueStr) ? null : due == default ? null : due,
                IsCompleted = done
            });
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
