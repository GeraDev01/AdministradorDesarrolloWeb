using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Asignación de un SLA: objetivo (requerimiento o actividad), responsable, fecha límite,
/// cadencia de recordatorio y ticket de Azure DevOps que hay que comentar.
/// </summary>
public class SlaAssignForm : Form
{
    private readonly AppDbContext _db;

    private ComboBox _cbxTipo = null!, _cbxObjetivo = null!, _cbxDev = null!, _cbxCadencia = null!;
    private DateTimePicker _dtpFecha = null!, _dtpHora = null!;
    private TextBox _txtTicket = null!, _txtUrl = null!, _txtNotas = null!;
    private Label _lblAyudaObjetivo = null!;

    private List<Requirement> _reqs = [];
    private List<DevActivity> _acts = [];
    private List<Developer> _devs = [];

    public SlaTarget Objetivo { get; private set; }
    public int DeveloperId { get; private set; }
    public DateTime VenceLocal { get; private set; }
    public int CadaHoras { get; private set; }
    public int? TicketId { get; private set; }
    public string? Url { get; private set; }
    public string? Notas { get; private set; }

    public SlaAssignForm(AppDbContext db)
    {
        _db = db;
        CargarCatalogos();
        BuildUI();
        TipoChanged(null, EventArgs.Empty);
    }

    private void CargarCatalogos()
    {
        _reqs = _db.Requirements
            .Where(r => r.Status != RequirementStatus.Entregado && r.Status != RequirementStatus.Cancelado)
            .OrderByDescending(r => r.Id).AsNoTracking().ToList();
        _acts = _db.DevActivities.Include(a => a.Developer)
            .Where(a => a.Status == DevActivityStatus.Abierta)
            .OrderByDescending(a => a.CreatedAt).AsNoTracking().ToList();
        _devs = _db.Developers.Where(d => d.IsActive).OrderBy(d => d.FullName).AsNoTracking().ToList();
    }

    private void BuildUI()
    {
        Text = "Asignar SLA";
        Size = new Size(560, 620);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg };
        hdr.Controls.Add(new Label { Text = "  ⏱  Asignar SLA y recordatorios", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int y = 14;

        body.Controls.Add(new Label { Text = "Aplicar a:", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cbxTipo = new ComboBox { Location = new Point(25, y), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxTipo.Items.AddRange(["Requerimiento", "Actividad libre"]);
        _cbxTipo.SelectedIndex = 0;
        _cbxTipo.SelectedIndexChanged += TipoChanged;
        body.Controls.Add(_cbxTipo); y += 38;

        body.Controls.Add(new Label { Text = "Objetivo *", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cbxObjetivo = new ComboBox { Location = new Point(25, y), Width = 490, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxObjetivo.SelectedIndexChanged += ObjetivoChanged;
        body.Controls.Add(_cbxObjetivo); y += 24;
        _lblAyudaObjetivo = new Label { Location = new Point(25, y), AutoSize = false, Size = new Size(490, 16), Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary };
        body.Controls.Add(_lblAyudaObjetivo); y += 24;

        body.Controls.Add(new Label { Text = "Responsable *", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cbxDev = new ComboBox { Location = new Point(25, y), Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var d in _devs) _cbxDev.Items.Add(d.FullName);
        if (_cbxDev.Items.Count > 0) _cbxDev.SelectedIndex = 0;
        body.Controls.Add(_cbxDev); y += 38;

        body.Controls.Add(new Label { Text = "Fecha y hora límite *", Location = new Point(25, y), AutoSize = true }); y += 20;
        _dtpFecha = new DateTimePicker { Location = new Point(25, y), Width = 180, Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(1) };
        _dtpHora = new DateTimePicker { Location = new Point(215, y), Width = 110, Format = DateTimePickerFormat.Time, ShowUpDown = true, Value = DateTime.Today.AddHours(18) };
        body.Controls.AddRange([_dtpFecha, _dtpHora]); y += 38;

        body.Controls.Add(new Label { Text = "Recordar cada:", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cbxCadencia = new ComboBox { Location = new Point(25, y), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxCadencia.Items.AddRange(["4 horas", "8 horas", "12 horas", "24 horas (diario)", "48 horas", "Solo al vencer"]);
        _cbxCadencia.SelectedIndex = 3;
        body.Controls.Add(_cbxCadencia); y += 38;

        body.Controls.Add(new Label { Text = "Ticket de Azure DevOps (número de work item):", Location = new Point(25, y), AutoSize = true }); y += 20;
        _txtTicket = new TextBox { Location = new Point(25, y), Width = 140, PlaceholderText = "12551" };
        _txtTicket.TextChanged += (_, _) => SugerirUrl();
        body.Controls.Add(_txtTicket);
        body.Controls.Add(new Label { Text = "  (opcional; sin él no hay recordatorio de comentar)", Location = new Point(175, y + 4), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary });
        y += 34;

        _txtUrl = new TextBox { Location = new Point(25, y), Width = 490, PlaceholderText = "URL del work item (se sugiere sola)" };
        body.Controls.Add(_txtUrl); y += 38;

        body.Controls.Add(new Label { Text = "Notas (opcional):", Location = new Point(25, y), AutoSize = true }); y += 20;
        _txtNotas = new TextBox { Location = new Point(25, y), Width = 490, Height = 60, Multiline = true };
        body.Controls.Add(_txtNotas); y += 70;

        body.AutoScrollMinSize = new Size(0, y);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnOk = AppTheme.MakePrimaryButton("Asignar SLA", 140);
        btnOk.Click += BtnOk_Click;
        btns.Controls.AddRange([btnCancel, btnOk]);

        tbl.Controls.Add(hdr, 0, 0);
        tbl.Controls.Add(body, 0, 1);
        tbl.Controls.Add(btns, 0, 2);
        Controls.Add(tbl);
        AcceptButton = btnOk;
    }

    private bool EsRequerimiento => _cbxTipo.SelectedIndex == 0;

    private void TipoChanged(object? s, EventArgs e)
    {
        _cbxObjetivo.Items.Clear();
        if (EsRequerimiento)
        {
            foreach (var r in _reqs) _cbxObjetivo.Items.Add($"#{r.Id}  {r.Title}");
            _lblAyudaObjetivo.Text = _reqs.Count == 0 ? "No hay requerimientos abiertos." : "";
        }
        else
        {
            foreach (var a in _acts) _cbxObjetivo.Items.Add($"#{a.Id}  {a.Title}  ({a.Developer?.FullName})");
            _lblAyudaObjetivo.Text = _acts.Count == 0 ? "No hay actividades libres abiertas." : "";
        }
        if (_cbxObjetivo.Items.Count > 0) _cbxObjetivo.SelectedIndex = 0;
        ObjetivoChanged(null, EventArgs.Empty);
    }

    /// <summary>
    /// Al elegir el objetivo se preselecciona el responsable obvio y, si el requerimiento vino de
    /// DevOps, se rellenan el ticket y la URL: es justo el caso que motiva el recordatorio.
    /// </summary>
    private void ObjetivoChanged(object? s, EventArgs e)
    {
        if (_cbxObjetivo.SelectedIndex < 0) return;

        if (EsRequerimiento)
        {
            var r = _reqs[_cbxObjetivo.SelectedIndex];
            if (r.Source == RequirementSource.AzureDevOps && int.TryParse(r.ExternalId, out int ext))
            {
                _txtTicket.Text = ext.ToString();
                if (!string.IsNullOrWhiteSpace(r.ExternalUrl)) _txtUrl.Text = r.ExternalUrl;
            }
            // Responsable sugerido: quien ya lo tiene asignado.
            var asignado = _db.Assignments.Where(a => a.RequirementId == r.Id)
                .Select(a => a.DeveloperId).FirstOrDefault();
            SeleccionarDev(asignado);
        }
        else
        {
            var a = _acts[_cbxObjetivo.SelectedIndex];
            SeleccionarDev(a.DeveloperId);   // la actividad ya es de alguien
        }
    }

    private void SeleccionarDev(int devId)
    {
        int i = _devs.FindIndex(d => d.Id == devId);
        if (i >= 0) _cbxDev.SelectedIndex = i;
    }

    private void SugerirUrl()
    {
        if (!string.IsNullOrWhiteSpace(_txtUrl.Text)) return;
        if (!int.TryParse(_txtTicket.Text.Trim(), out int id)) return;
        // Se deja como sugerencia editable; la organización real la conoce la configuración.
        _txtUrl.PlaceholderText = $"…/_workitems/edit/{id}";
    }

    private static readonly int[] Cadencias = [4, 8, 12, 24, 48, 0];

    private void BtnOk_Click(object? s, EventArgs e)
    {
        if (_cbxObjetivo.SelectedIndex < 0)
        { MessageBox.Show("Selecciona el objetivo.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (_cbxDev.SelectedIndex < 0)
        { MessageBox.Show("Selecciona el responsable.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        var vence = _dtpFecha.Value.Date + _dtpHora.Value.TimeOfDay;
        if (vence <= DateTime.Now)
        { MessageBox.Show("La fecha límite debe ser futura.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        int? ticket = null;
        if (!string.IsNullOrWhiteSpace(_txtTicket.Text))
        {
            if (!int.TryParse(_txtTicket.Text.Trim(), out int t) || t <= 0)
            { MessageBox.Show("El ticket debe ser el número del work item.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            ticket = t;
        }

        Objetivo = EsRequerimiento
            ? SlaTarget.Requerimiento(_reqs[_cbxObjetivo.SelectedIndex].Id)
            : SlaTarget.Actividad(_acts[_cbxObjetivo.SelectedIndex].Id);
        DeveloperId = _devs[_cbxDev.SelectedIndex].Id;
        VenceLocal = vence;
        CadaHoras = Cadencias[_cbxCadencia.SelectedIndex];
        TicketId = ticket;
        Url = string.IsNullOrWhiteSpace(_txtUrl.Text) ? null : _txtUrl.Text.Trim();
        Notas = string.IsNullOrWhiteSpace(_txtNotas.Text) ? null : _txtNotas.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }
}
