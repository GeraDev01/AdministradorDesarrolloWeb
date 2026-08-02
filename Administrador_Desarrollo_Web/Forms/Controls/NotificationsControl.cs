using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Bandeja de avisos del usuario: «te asignaron un ticket en DevOps», «el administrador te asignó un
/// requerimiento», etc. Los avisos son persistentes y por usuario.
/// </summary>
public class NotificationsControl : UserControl
{
    private readonly NotificationService _notif;
    private readonly CurrentUserContext _currentUser;

    private DataGridView _grid = null!;
    private Label _lblEmpty = null!;
    private List<Notification> _items = [];

    /// <summary>Se dispara al marcar leído/todo-leído, para refrescar el contador del menú.</summary>
    public event Action? UnreadChanged;

    public NotificationsControl(NotificationService notif, CurrentUserContext currentUser)
    {
        _notif = notif; _currentUser = currentUser;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 8, 10, 4) };
        var btnRead = AppTheme.MakePrimaryButton("✓  Marcar todo leído", 190); btnRead.Margin = new Padding(0, 0, 8, 0);
        btnRead.Click += (_, _) => { _notif.MarkAllRead(UserId); UnreadChanged?.Invoke(); LoadData(); };
        var btnReload = AppTheme.MakeSecondaryButton("🔄  Recargar", 120);
        btnReload.Click += (_, _) => LoadData();
        toolbar.Controls.AddRange([btnRead, btnReload]);

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg };
        _grid = AppTheme.MakeGrid();
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.MultiSelect = false;
        _grid.Columns.AddRange(
            Col("", "Dot", 28),
            Col("Fecha", "Date", 130),
            Col("Tipo", "Kind", 150),
            Col("Aviso", "Title", 260),
            Col("Detalle", "Message", 360)
        );
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        _grid.CellDoubleClick += Grid_CellDoubleClick;
        _grid.CellFormatting += Grid_CellFormatting;

        _lblEmpty = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Visible = false, ForeColor = AppTheme.TextSecondary, Text = "No tienes avisos." };
        pnl.Controls.Add(_grid);
        pnl.Controls.Add(_lblEmpty);

        tbl.Controls.Add(toolbar, 0, 0);
        tbl.Controls.Add(pnl, 0, 1);
        Controls.Add(tbl);
    }

    private int UserId => _currentUser.User?.Id ?? 0;

    private void LoadData()
    {
        _items = UserId == 0 ? [] : _notif.Recent(UserId);
        _grid.Rows.Clear();
        foreach (var n in _items)
            _grid.Rows.Add(n.ReadAt == null ? "●" : "", n.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                KindLabel(n.Kind), n.Title, n.Message);
        _lblEmpty.Visible = _items.Count == 0;
        _lblEmpty.BringToFront();
    }

    private void Grid_CellFormatting(object? s, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _items.Count) return;
        bool unread = _items[e.RowIndex].ReadAt == null;
        if (e.ColumnIndex == 0) e.CellStyle.ForeColor = AppTheme.SidebarActive;
        if (unread) e.CellStyle.Font = AppTheme.BoldFont;
    }

    private void Grid_CellDoubleClick(object? s, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _items.Count) return;
        var n = _items[e.RowIndex];
        if (n.ReadAt == null) { _notif.MarkRead(n.Id); UnreadChanged?.Invoke(); }
        // Solo http(s): este campo lo abre el SHELL, así que cualquier otra cosa —una clave de
        // navegación interna, un valor mal capturado— fallaría en silencio y, peor, se saltaría
        // el mensaje completo de abajo dejando el doble clic sin hacer nada.
        if (n.Url is { Length: > 0 } destino
            && Uri.TryCreate(destino, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(destino) { UseShellExecute = true }); } catch { }
        else if (!string.IsNullOrWhiteSpace(n.Message))   // sin enlace usable: se lee completo aquí
            MessageBox.Show(n.Message, n.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoadData();
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }

    private static string KindLabel(NotificationKind k) => k switch
    {
        NotificationKind.DevOpsAssigned      => "🔷 Ticket DevOps",
        NotificationKind.RequirementAssigned => "📋 Requerimiento",
        NotificationKind.FreshDeskAssigned   => "🎫 Ticket Freshdesk",
        NotificationKind.Comunicado          => "📢 Comunicado",
        _                                    => "🔔 Aviso"
    };

    private static DataGridViewTextBoxColumn Col(string header, string name, int w) => new()
    { HeaderText = header, Name = name, Width = w, ReadOnly = true };
}
