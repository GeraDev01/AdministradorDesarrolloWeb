using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>Agenda un despliegue: versión, perfil, hora y cuánto se tolera arrancar tarde.</summary>
public class ScheduleDeploymentForm : Form
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    private ComboBox _cbxSistema = null!, _cbxVersion = null!, _cbxPerfil = null!, _cbxTolerancia = null!;
    private RadioButton _rbPerfil = null!, _rbServidores = null!;
    private Button _btnElegirServidores = null!;
    private Label _lblServidores = null!;
    private DateTimePicker _dtpFecha = null!, _dtpHora = null!;
    private TextBox _txtNotas = null!;

    private List<AppSystem> _sistemas = [];
    private List<AppRelease> _versiones = [];
    private List<DeploymentProfile> _perfiles = [];
    private List<DeploymentTarget> _servidores = [];
    private List<int> _targetIds = [];

    public int ReleaseId { get; private set; }
    public int ProfileId { get; private set; }
    /// <summary>true = agendar a servidores directos (<see cref="TargetIds"/>); false = por perfil.</summary>
    public bool PorServidores { get; private set; }
    public List<int> TargetIds { get; private set; } = [];
    public DateTime CuandoLocal { get; private set; }
    public int ToleranciaMinutos { get; private set; }
    public string? Notas { get; private set; }

    public ScheduleDeploymentForm(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db; _currentUser = currentUser;
        BuildUI();
        CargarSistemas();
    }

    private void BuildUI()
    {
        Text = "Programar despliegue";
        Size = new Size(540, 590);
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
        hdr.Controls.Add(new Label { Text = "  🗓  Programar despliegue", Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont, TextAlign = ContentAlignment.MiddleLeft });

        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int y = 14;

        body.Controls.Add(new Label { Text = "Sistema *", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cbxSistema = new ComboBox { Location = new Point(25, y), Width = 460, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxSistema.SelectedIndexChanged += (_, _) => CargarVersiones();
        body.Controls.Add(_cbxSistema); y += 38;

        body.Controls.Add(new Label { Text = "Versión *", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cbxVersion = new ComboBox { Location = new Point(25, y), Width = 460, DropDownStyle = ComboBoxStyle.DropDownList };
        body.Controls.Add(_cbxVersion); y += 38;

        body.Controls.Add(new Label { Text = "Destino *", Location = new Point(25, y), AutoSize = true }); y += 22;
        _rbPerfil = new RadioButton { Text = "Perfil guardado", Location = new Point(25, y), AutoSize = true, Checked = true };
        body.Controls.Add(_rbPerfil); y += 24;
        _cbxPerfil = new ComboBox { Location = new Point(45, y), Width = 440, DropDownStyle = ComboBoxStyle.DropDownList };
        body.Controls.Add(_cbxPerfil); y += 34;
        _rbServidores = new RadioButton { Text = "Servidores directos", Location = new Point(25, y), AutoSize = true };
        body.Controls.Add(_rbServidores); y += 24;
        _btnElegirServidores = AppTheme.MakeSecondaryButton("🖧 Elegir servidores…", 190, 26);
        _btnElegirServidores.Location = new Point(45, y);
        _btnElegirServidores.Click += ElegirServidores;
        body.Controls.Add(_btnElegirServidores);
        _lblServidores = new Label { Location = new Point(245, y + 4), AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont, Text = "ningún servidor elegido" };
        body.Controls.Add(_lblServidores); y += 38;
        _rbPerfil.CheckedChanged += (_, _) => ActualizarModo();
        _rbServidores.CheckedChanged += (_, _) => ActualizarModo();

        body.Controls.Add(new Label { Text = "Fecha y hora *", Location = new Point(25, y), AutoSize = true }); y += 20;
        _dtpFecha = new DateTimePicker { Location = new Point(25, y), Width = 180, Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(1) };
        _dtpHora = new DateTimePicker { Location = new Point(215, y), Width = 110, Format = DateTimePickerFormat.Time, ShowUpDown = true, Value = DateTime.Today.AddHours(2) };
        body.Controls.AddRange([_dtpFecha, _dtpHora]); y += 38;

        body.Controls.Add(new Label { Text = "Tolerancia para arrancar tarde:", Location = new Point(25, y), AutoSize = true }); y += 20;
        _cbxTolerancia = new ComboBox { Location = new Point(25, y), Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
        _cbxTolerancia.Items.AddRange(["15 minutos", "30 minutos", "1 hora", "2 horas", "4 horas"]);
        _cbxTolerancia.SelectedIndex = 2;
        body.Controls.Add(_cbxTolerancia); y += 24;
        body.Controls.Add(new Label
        {
            Text = "Pasado ese margen se marca como Perdido y no se ejecuta:\ndesplegar a deshora sin que nadie lo espere es peor que no hacerlo.",
            Location = new Point(25, y), AutoSize = false, Size = new Size(460, 32),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });
        y += 42;

        body.Controls.Add(new Label { Text = "Notas (opcional):", Location = new Point(25, y), AutoSize = true }); y += 20;
        _txtNotas = new TextBox { Location = new Point(25, y), Width = 460, Height = 55, Multiline = true };
        body.Controls.Add(_txtNotas); y += 66;

        body.AutoScrollMinSize = new Size(0, y);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCancel = AppTheme.MakeSecondaryButton("Cancelar", 100);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var btnOk = AppTheme.MakePrimaryButton("Programar", 140);
        btnOk.Click += BtnOk_Click;
        btns.Controls.AddRange([btnCancel, btnOk]);

        tbl.Controls.Add(hdr, 0, 0);
        tbl.Controls.Add(body, 0, 1);
        tbl.Controls.Add(btns, 0, 2);
        Controls.Add(tbl);
        AcceptButton = btnOk;
    }

    private void CargarSistemas()
    {
        _sistemas = _db.AppSystems.Where(s => s.IsActive).OrderBy(s => s.Name).AsNoTracking().ToList();
        foreach (var s in _sistemas) _cbxSistema.Items.Add(s.Name);
        if (_sistemas.Count > 0) _cbxSistema.SelectedIndex = 0;

        // Mismo filtro que en el despliegue en vivo: Operaciones solo ve sus perfiles habilitados.
        _perfiles = _db.DeploymentProfiles
            .Where(p => !p.IsAdHoc && (_currentUser.IsAdmin || p.AllowedForOperaciones))
            .Include(p => p.ProfileTargets)
            .OrderBy(p => p.Name).AsNoTracking().ToList();
        foreach (var p in _perfiles) _cbxPerfil.Items.Add(p.Name);
        if (_perfiles.Count > 0) _cbxPerfil.SelectedIndex = 0;

        // Servidores directos (como en la pestaña Desplegar): Operaciones también puede elegirlos.
        _servidores = _db.DeploymentTargets.Where(t => t.IsActive).OrderBy(t => t.Nombre).AsNoTracking().ToList();

        // Si no hay perfiles disponibles (típico en Operaciones sin perfiles habilitados), arranca en
        // «servidores directos» para que sí pueda programar.
        if (_perfiles.Count == 0) _rbServidores.Checked = true;
        ActualizarModo();
    }

    private void ActualizarModo()
    {
        _cbxPerfil.Enabled = _rbPerfil.Checked;
        _btnElegirServidores.Enabled = _rbServidores.Checked;
        _lblServidores.Enabled = _rbServidores.Checked;
    }

    private void ElegirServidores(object? s, EventArgs e)
    {
        // conRespaldo: false → los programados respaldan TODO (opción segura para algo desatendido),
        // así que no se ofrece elegir el respaldo por servidor (evita mostrar un control sin efecto).
        using var frm = new DeployServersPickerForm(_servidores, _targetIds, [], _perfiles, conRespaldo: false);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        _targetIds = frm.SelectedTargetIds;
        _lblServidores.Text = _targetIds.Count == 0 ? "ningún servidor elegido" : $"{_targetIds.Count} servidor(es) elegido(s)";
    }

    private void CargarVersiones()
    {
        _cbxVersion.Items.Clear();
        if (_cbxSistema.SelectedIndex < 0) return;
        int sysId = _sistemas[_cbxSistema.SelectedIndex].Id;
        _versiones = _db.AppReleases.Where(r => r.AppSystemId == sysId)
            .OrderByDescending(r => r.CreatedAt).AsNoTracking().ToList();
        foreach (var r in _versiones) _cbxVersion.Items.Add($"v{r.Version}  ({r.CreatedAt.ToLocalTime():dd/MM/yyyy})");
        if (_versiones.Count > 0) _cbxVersion.SelectedIndex = 0;
    }

    private static readonly int[] Tolerancias = [15, 30, 60, 120, 240];

    private void BtnOk_Click(object? s, EventArgs e)
    {
        if (_cbxVersion.SelectedIndex < 0)
        { MessageBox.Show("Selecciona una versión.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        if (_rbServidores.Checked)
        {
            if (_targetIds.Count == 0)
            { MessageBox.Show("Elige al menos un servidor destino.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            PorServidores = true;
            TargetIds = _targetIds;
        }
        else
        {
            if (_cbxPerfil.SelectedIndex < 0)
            { MessageBox.Show("Selecciona un perfil (o cambia a «servidores directos»).", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            PorServidores = false;
            ProfileId = _perfiles[_cbxPerfil.SelectedIndex].Id;
        }

        var cuando = _dtpFecha.Value.Date + _dtpHora.Value.TimeOfDay;
        if (cuando <= DateTime.Now.AddMinutes(1))
        { MessageBox.Show("La hora debe ser futura.", "Validación", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        ReleaseId = _versiones[_cbxVersion.SelectedIndex].Id;
        CuandoLocal = cuando;
        ToleranciaMinutos = Tolerancias[_cbxTolerancia.SelectedIndex];
        Notas = string.IsNullOrWhiteSpace(_txtNotas.Text) ? null : _txtNotas.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }
}
