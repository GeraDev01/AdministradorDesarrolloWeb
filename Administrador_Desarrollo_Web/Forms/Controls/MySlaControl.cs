using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Compromisos del desarrollador: qué debe atender, para cuándo, y el botón que publica el
/// comentario de avance en el ticket de Azure DevOps sin salir de la aplicación.
/// </summary>
public class MySlaControl : UserControl
{
    private readonly CurrentUserContext _currentUser;
    private readonly SlaService _sla;
    private readonly WorkSessionService _work;
    private readonly AzureDevOpsService _devops;

    private DataGridView _grid = null!;
    private CheckBox _chkHistorial = null!;
    private Button _btnComentar = null!, _btnPosponer = null!, _btnAbrir = null!, _btnPat = null!;
    private Label _lblResumen = null!;
    private List<SlaCommitment> _rows = [];

    public MySlaControl(CurrentUserContext currentUser, SlaService sla, WorkSessionService work, AzureDevOpsService devops)
    {
        _currentUser = currentUser; _sla = sla; _work = work; _devops = devops;
        BuildUI();
        LoadData();
    }

    private void AbrirConfiguracionPat()
    {
        using var frm = new MyDevOpsPatForm(_devops);
        frm.ShowDialog(FindForm());
        RefreshButtons();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5),
            BackColor = AppTheme.ContentBg, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 620f));   // ancho de los botones + sus márgenes
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _chkHistorial = new CheckBox { Text = "Ver también los cerrados", AutoSize = true, Margin = new Padding(0, 7, 0, 0) };
        _chkHistorial.CheckedChanged += (_, _) => LoadData();
        var izq = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg };
        izq.Controls.Add(_chkHistorial);
        toolbar.Controls.Add(izq, 0, 0);

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppTheme.ContentBg };
        _btnComentar = AppTheme.MakePrimaryButton("💬 Comentar en DevOps", 200); _btnComentar.Margin = new Padding(4, 2, 0, 0); _btnComentar.Click += BtnComentar_Click;
        _btnPosponer = AppTheme.MakeSecondaryButton("⏰ Posponer", 130);          _btnPosponer.Margin = new Padding(4, 2, 0, 0); _btnPosponer.Click += BtnPosponer_Click;
        _btnAbrir    = AppTheme.MakeSecondaryButton("🔗 Abrir ticket", 140);      _btnAbrir.Margin = new Padding(4, 2, 0, 0); _btnAbrir.Click += BtnAbrir_Click;
        _btnPat      = AppTheme.MakeSecondaryButton("🔑 Mi PAT", 110);            _btnPat.Margin = new Padding(4, 2, 0, 0); _btnPat.Click += (_, _) => AbrirConfiguracionPat();
        btns.Controls.AddRange([_btnComentar, _btnPosponer, _btnAbrir, _btnPat]);
        toolbar.Controls.Add(btns, 1, 0);

        _lblResumen = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.BoldFont, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0), BackColor = AppTheme.ContentBg
        };

        _grid = AppTheme.MakeGrid();
        _grid.MultiSelect = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",           Name = "Id",     Width = 45, FillWeight = 4 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Objetivo",     Name = "Target", FillWeight = 32 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Ticket",       Name = "Ticket", FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Vence",        Name = "Due",    FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Falta",        Name = "Left",   FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Estado",       Name = "State",  FillWeight = 13 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Comentarios",  Name = "Comm",   FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Notas",        Name = "Notes",  FillWeight = 15 });
        _grid.SelectionChanged += (_, _) => RefreshButtons();

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(toolbar,   0, 0);
        tbl.Controls.Add(_lblResumen, 0, 1);
        tbl.Controls.Add(pnlGrid,   0, 2);
        Controls.Add(tbl);
    }

    private void LoadData()
    {
        if (_currentUser.DeveloperId is not int devId)
        {
            _rows = [];
            _grid.Rows.Clear();
            _lblResumen.Text = "Tu cuenta no está vinculada a un desarrollador.";
            _lblResumen.ForeColor = AppTheme.TextSecondary;
            RefreshButtons();
            return;
        }

        _rows = _sla.DeDesarrollador(devId, soloActivos: !_chkHistorial.Checked);
        var ahora = DateTime.UtcNow;

        _grid.Rows.Clear();
        foreach (var s in _rows)
        {
            var restante = s.DueAtUtc - ahora;
            int i = _grid.Rows.Add(
                s.Id,
                SlaService.DescribirObjetivo(s),
                s.DevOpsTicketExternalId is int t ? $"#{t}" : "—",
                s.DueAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                s.Status == SlaStatus.Activo ? Restante(restante) : "—",
                EstadoLabel(s, ahora),
                s.CommentCount == 0 ? "ninguno" : $"{s.CommentCount} · {s.LastCommentAtUtc?.ToLocalTime():dd/MM HH:mm}",
                s.Notes ?? "");

            _grid.Rows[i].Cells["State"].Style.Font = AppTheme.BoldFont;
            _grid.Rows[i].Cells["State"].Style.ForeColor = ColorEstado(s, ahora);
            if (s.EstaVencido(ahora)) _grid.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 235);
            else if (s.TocaRecordar(ahora)) _grid.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 220);
        }

        int urgentes = _rows.Count(s => s.EstaVencido(ahora) || s.TocaRecordar(ahora));
        _lblResumen.Text = _rows.Count == 0
            ? "No tienes compromisos pendientes."
            : urgentes == 0
                ? $"{_rows.Count} compromiso(s) en plazo. Nada urgente."
                : $"⚠  {urgentes} compromiso(s) requieren que comentes el ticket ahora.";
        _lblResumen.ForeColor = urgentes > 0 ? AppTheme.Danger : AppTheme.TextSecondary;

        RefreshButtons();
    }

    private static string Restante(TimeSpan t)
    {
        if (t.TotalSeconds <= 0) return "vencido";
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m";
    }

    private static string EstadoLabel(SlaCommitment s, DateTime ahora) => s.Status switch
    {
        SlaStatus.Activo   => s.EstaVencido(ahora) ? "⚠ Fuera de plazo"
                            : s.TocaRecordar(ahora) ? "🔔 Toca comentar" : "🟢 En plazo",
        SlaStatus.Cumplido => "✅ Cumplido",
        SlaStatus.Vencido  => "❌ Vencido",
        _                  => "⚪ Cancelado"
    };

    private static Color ColorEstado(SlaCommitment s, DateTime ahora) => s.Status switch
    {
        SlaStatus.Activo   => s.EstaVencido(ahora) ? AppTheme.Danger
                            : s.TocaRecordar(ahora) ? AppTheme.Warning : AppTheme.Success,
        SlaStatus.Cumplido => AppTheme.Success,
        SlaStatus.Vencido  => AppTheme.Danger,
        _                  => AppTheme.TextSecondary
    };

    private SlaCommitment? Seleccionado() =>
        _grid.CurrentRow?.Cells["Id"].Value is int id ? _rows.FirstOrDefault(s => s.Id == id) : null;

    private void RefreshButtons()
    {
        var s = Seleccionado();
        bool activo = s?.Status == SlaStatus.Activo;
        _btnComentar.Enabled = activo && s!.DevOpsTicketExternalId != null;
        _btnPosponer.Enabled = activo;
        _btnAbrir.Enabled = !string.IsNullOrWhiteSpace(s?.DevOpsTicketUrl);
    }

    private async void BtnComentar_Click(object? sender, EventArgs e)
    {
        if (Seleccionado() is not { } sla) return;

        // Sin PAT propio el comentario no puede firmarse a nombre de quien lo escribe: se pide antes
        // de redactar, en lugar de dejar que falle después de haberlo escrito.
        if (!AzureDevOpsService.TienePatPersonal)
        {
            if (MessageBox.Show(
                    "Para comentar en Azure DevOps necesitas capturar tu Personal Access Token.\n\n" +
                    "Los comentarios quedan firmados con tu cuenta, por eso el token es personal.\n\n" +
                    "¿Capturarlo ahora?",
                    "Falta tu PAT", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;

            AbrirConfiguracionPat();
            if (!AzureDevOpsService.TienePatPersonal) return;
        }

        // Se ofrece un borrador con el tiempo dedicado: es lo que el jefe quiere ver en el ticket
        // y evita que el comentario sea un "sigo en eso" sin información.
        int segundos = sla.ActivityId is int actId
            ? _work.GetTotalSecondsByActivity(actId)
            : sla.RequirementId is int reqId && _currentUser.DeveloperId is int dev
                ? _work.GetTotalSeconds(dev, reqId)
                : 0;

        var borrador = $"Avance: tiempo dedicado {WorkSessionService.Format(segundos)}. ";

        using var frm = new SlaCommentForm(SlaService.DescribirObjetivo(sla), sla.DevOpsTicketExternalId!.Value, borrador);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        _btnComentar.Enabled = false;
        UseWaitCursor = true;
        try
        {
            var (ok, mensaje) = await _sla.ComentarTicketAsync(sla.Id, frm.Comentario, frm.Evidencias);
            LoadData();
            MessageBox.Show(mensaje, ok ? "Publicado" : "No se pudo publicar",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
            RefreshButtons();
        }
    }

    private void BtnPosponer_Click(object? sender, EventArgs e)
    {
        if (Seleccionado() is not { } sla) return;
        var (ok, mensaje) = _sla.Posponer(sla.Id, 4);
        LoadData();
        if (!ok) MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void BtnAbrir_Click(object? sender, EventArgs e)
    {
        if (Seleccionado()?.DevOpsTicketUrl is not { Length: > 0 } url) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir el enlace:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }
}
