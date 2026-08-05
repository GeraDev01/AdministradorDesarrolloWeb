using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Pantalla del DESARROLLADOR: sus work items de Azure DevOps, es decir, los asignados a él.
///
/// <b>Sincroniza por su cuenta</b>, sin esperar al administrador: con su PAT personal pide a DevOps
/// lo asignado a sí mismo (macro <c>@Me</c>) dentro de una ventana de días. Antes solo leía lo que
/// el administrador hubiera traído, así que un ticket recién asignado no aparecía hasta que a otra
/// persona le diera por sincronizar.
///
/// Para leer la lista sigue empatando por identidad (<see cref="DevOpsIdentityMatcher"/>), que
/// además recoge lo que trajo el administrador. Comentar, cambiar prioridad y sincronizar van
/// contra DevOps con el PAT PERSONAL, para que todo quede firmado a nombre de cada quien.
/// </summary>
public class MyDevOpsTicketsControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly AzureDevOpsService _devOps;
    private readonly CurrentUserContext _currentUser;

    private DataGridView _grid = null!;
    private TextBox _txtSearch = null!;
    private Label _lblStatus = null!;
    private Label _lblEmpty = null!;
    private List<DevOpsTicket> _mine = [];

    // Filtros
    private ComboBox _cbxEstado = null!, _cbxTipo = null!, _cbxIteracion = null!, _cbxDias = null!;
    private CheckBox _chkSoloAbiertos = null!, _chkSinEstimar = null!;
    private Button _btnSync = null!;
    private readonly ToolTip _tip = new();
    private bool _suspenderFiltros;
    private bool _sincronizando;

    /// <summary>Ventanas de tiempo del combo; null = todo el historial.</summary>
    private static readonly (string Etiqueta, int? Dias)[] Ventanas =
    [
        ("Últimos 30 días", 30),
        ("Últimos 90 días", MyDevOpsTicketFilter.DiasPorOmision),
        ("Último año", 365),
        ("Todo el historial", null),
    ];

    private const string ClaveColumnas = "my-devops.tickets";

    public MyDevOpsTicketsControl(AppDbContext db, AzureDevOpsService devOps, CurrentUserContext currentUser)
    {
        _db = db; _devOps = devOps; _currentUser = currentUser;
        BuildUI();
        LoadData();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));   // acciones
        tbl.RowStyles.Add(new RowStyle(SizeType.AutoSize));        // filtros (se acomodan en dos líneas si hace falta)
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // rejilla
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));   // estado
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Toolbar (AutoSize para que nunca se corten los botones) ──
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(10, 8, 10, 4), BackColor = AppTheme.ContentBg
        };

        _btnSync = AppTheme.MakePrimaryButton("⟳  Sincronizar mis tickets", 210);
        _btnSync.Margin = new Padding(0, 0, 8, 0);
        _btnSync.Click += (_, _) => _ = SincronizarAsync();

        var btnRefresh = AppTheme.MakeSecondaryButton("🔄  Actualizar", 130);
        btnRefresh.Margin = new Padding(0, 0, 8, 0);
        btnRefresh.Click += (_, _) => LoadData();

        _txtSearch = new TextBox { Width = 240, Font = AppTheme.DefaultFont, PlaceholderText = "Buscar por título, ID, estado…", Margin = new Padding(0, 2, 8, 0) };
        _txtSearch.TextChanged += (_, _) => Filter();

        var btnPat = AppTheme.MakeSecondaryButton("🔑  Mi PAT de DevOps", 180);
        btnPat.Margin = new Padding(0, 0, 8, 0);
        btnPat.Click += (_, _) =>
        {
            using var f = new MyDevOpsPatForm(_devOps);
            f.ShowDialog(FindForm());
            PintarBotonSync();   // pudo capturar su PAT justo ahora
        };

        toolbar.Controls.AddRange([_btnSync, btnRefresh, _txtSearch, btnPat]);

        // ── Filtros ──────────────────────────────────────────────────
        var filtros = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 0, 10, 6), BackColor = AppTheme.ContentBg
        };

        _cbxDias = ComboFiltro(null, 165);
        foreach (var (etiqueta, _) in Ventanas) _cbxDias.Items.Add(etiqueta);
        _cbxDias.SelectedIndex = Array.FindIndex(Ventanas, v => v.Dias == MyDevOpsTicketFilter.DiasPorOmision);

        _cbxEstado    = ComboFiltro("Todos los estados");
        _cbxTipo      = ComboFiltro("Todos los tipos");
        _cbxIteracion = ComboFiltro("Todas las iteraciones", 175);

        _chkSoloAbiertos = new CheckBox
        {
            Text = "Solo sin cerrar", AutoSize = true, Checked = true,
            Font = AppTheme.DefaultFont, Margin = new Padding(4, 7, 8, 0)
        };

        foreach (var c in new Control[] { _cbxDias, _cbxEstado, _cbxTipo, _cbxIteracion })
            ((ComboBox)c).SelectedIndexChanged += (_, _) => Filter();
        _chkSoloAbiertos.CheckedChanged += (_, _) => Filter();

        _chkSinEstimar = new CheckBox
        {
            Text = "Solo sin estimar", AutoSize = true,
            Font = AppTheme.DefaultFont, Margin = new Padding(4, 7, 8, 0)
        };
        _chkSinEstimar.CheckedChanged += (_, _) => Filter();

        var btnLimpiar = AppTheme.MakeSecondaryButton("Limpiar", 80, 26);
        btnLimpiar.Margin = new Padding(0, 3, 0, 0);
        btnLimpiar.Click += (_, _) => LimpiarFiltros();

        filtros.Controls.AddRange([_cbxDias, _cbxEstado, _cbxTipo, _cbxIteracion, _chkSoloAbiertos, _chkSinEstimar, btnLimpiar]);

        // ── Cuerpo: grid + mensaje de vacío superpuesto ──────────────
        var pnlBody = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4), BackColor = AppTheme.ContentBg };

        _grid = AppTheme.MakeGrid();
        _grid.Dock = DockStyle.Fill;
        _grid.MultiSelect = false;
        _grid.AutoGenerateColumns = false;
        _grid.Columns.AddRange(
            Col("ID", "ExternalId", 65),
            Col("Tipo", "WorkItemType", 105),
            Col("Título", "Title", 300),
            Col("Estado", "State", 100),
            Col("Prioridad", "Priority", 85),
            Col("Iteración", "IterationPath", 150),
            Col("Pts", "StoryPoints", 45),
            Col("⏱ Est.", "EstimatedHours", 70),
            Col("💬", "CommentCount", 42),
            Col("Actualizado", "UpdatedAtExternal", 120)
        );
        _grid.Columns["UpdatedAtExternal"]!.DefaultCellStyle.Format = "dd/MM/yyyy";
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        _grid.CellFormatting += Grid_CellFormatting;
        _grid.CellDoubleClick += Grid_CellDoubleClick;
        _grid.MouseDown += Grid_MouseDown;

        _lblEmpty = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Visible = false,
            Font = AppTheme.DefaultFont, ForeColor = AppTheme.TextSecondary, BackColor = AppTheme.ContentBg
        };

        pnlBody.Controls.Add(_grid);
        pnlBody.Controls.Add(_lblEmpty);
        _lblEmpty.BringToFront();

        _lblStatus = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0)
        };

        GridColumns.Habilitar(_grid, ClaveColumnas);
        toolbar.Controls.Add(GridColumns.CrearBoton(_grid, ClaveColumnas));

        tbl.Controls.Add(toolbar,    0, 0);
        tbl.Controls.Add(filtros,    0, 1);
        tbl.Controls.Add(pnlBody,    0, 2);
        tbl.Controls.Add(_lblStatus, 0, 3);
        Controls.Add(tbl);

        PintarBotonSync();
    }

    private static ComboBox ComboFiltro(string? todos, int ancho = 150)
    {
        var cbx = new ComboBox
        {
            Width = ancho, DropDownStyle = ComboBoxStyle.DropDownList,
            Font = AppTheme.DefaultFont, Margin = new Padding(0, 3, 6, 3)
        };
        if (todos != null) { cbx.Items.Add(todos); cbx.SelectedIndex = 0; }
        return cbx;
    }

    /// <summary>Sin PAT personal no hay con qué preguntarle a DevOps; el botón lo dice en vez de fallar al pulsarlo.</summary>
    private void PintarBotonSync()
    {
        bool puede = _devOps.PuedeSincronizarMisTickets;
        _btnSync.Enabled = puede && !_sincronizando;
        _btnSync.Text = _sincronizando ? "⟳  Sincronizando…" : "⟳  Sincronizar mis tickets";
        _tip.SetToolTip(_btnSync, puede
            ? "Trae de Azure DevOps los work items asignados a ti, usando tu PAT personal."
            : "Captura tu PAT en «Mi PAT de DevOps» para poder sincronizar por tu cuenta.");
    }

    private async Task SincronizarAsync()
    {
        if (_sincronizando) return;
        _sincronizando = true;
        PintarBotonSync();
        UseWaitCursor = true;
        try
        {
            // Se pide la misma ventana que está viendo; «Todo el historial» se acota a un año para
            // no arrastrar de golpe años de work items cerrados por una sola pulsación.
            int dias = VentanaSeleccionada() ?? 365;
            var r = await _devOps.SincronizarMisTicketsAsync(dias);
            LoadData();
            _lblStatus.Text = $"Sincronizado: {r.Added} nuevo(s), {r.Updated} actualizado(s).";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudieron traer tus tickets de Azure DevOps:\n\n{ex.Message}",
                "Sincronizar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
            _sincronizando = false;
            PintarBotonSync();
        }
    }

    private int? VentanaSeleccionada() =>
        _cbxDias.SelectedIndex >= 0 ? Ventanas[_cbxDias.SelectedIndex].Dias : MyDevOpsTicketFilter.DiasPorOmision;

    private void LimpiarFiltros()
    {
        _suspenderFiltros = true;
        try
        {
            _txtSearch.Clear();
            _cbxEstado.SelectedIndex = _cbxTipo.SelectedIndex = _cbxIteracion.SelectedIndex = 0;
            _cbxDias.SelectedIndex = Array.FindIndex(Ventanas, v => v.Dias == MyDevOpsTicketFilter.DiasPorOmision);
            _chkSoloAbiertos.Checked = true;
            _chkSinEstimar.Checked = false;
        }
        finally { _suspenderFiltros = false; }
        Filter();
    }

    /// <summary>Rellena un combo con lo que hay en los datos, conservando lo elegido si sigue existiendo.</summary>
    private static void PoblarFiltro(ComboBox cbx, IEnumerable<string?> valores)
    {
        var previo = cbx.SelectedIndex > 0 ? cbx.SelectedItem as string : null;
        var todos = (string)cbx.Items[0]!;

        cbx.BeginUpdate();
        cbx.Items.Clear();
        cbx.Items.Add(todos);
        foreach (var v in MyDevOpsTicketFilter.Opciones(valores)) cbx.Items.Add(v);
        cbx.EndUpdate();

        int idx = previo != null ? cbx.Items.IndexOf(previo) : 0;
        cbx.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private static string? ValorFiltro(ComboBox cbx) =>
        cbx.SelectedIndex > 0 ? cbx.SelectedItem as string : null;

    private void LoadData()
    {
        var devId = _currentUser.DeveloperId;
        if (devId == null)
        {
            _mine = [];
            ShowEmpty("Tu cuenta no está vinculada a un desarrollador, así que no hay tickets que mostrar.\n" +
                      "Pide a un líder que vincule tu usuario a tu ficha de desarrollador.");
            Filter();
            return;
        }

        var dev = _db.Developers.Find(devId.Value);
        if (dev == null) { _mine = []; ShowEmpty("No se encontró tu ficha de desarrollador."); Filter(); return; }

        _mine = DevOpsTicketQuery.ForDeveloper(_db, dev);

        // Los combos se arman con lo que hay: ofrecer iteraciones o estados inexistentes convierte
        // el filtro en una lista de callejones sin salida.
        _suspenderFiltros = true;
        try
        {
            PoblarFiltro(_cbxEstado,    _mine.Select(t => t.State));
            PoblarFiltro(_cbxTipo,      _mine.Select(t => t.WorkItemType));
            PoblarFiltro(_cbxIteracion, _mine.Select(t => t.IterationPath));
        }
        finally { _suspenderFiltros = false; }

        if (_mine.Count == 0)
        {
            bool sinTickets = !_db.DevOpsTickets.Any();
            ShowEmpty(sinTickets
                ? "Todavía no hay tickets de DevOps sincronizados.\n" +
                  "Pulsa «⟳ Sincronizar mis tickets» para traer los tuyos con tu PAT personal."
                : $"No encontramos tickets de DevOps a tu nombre ({dev.FullName}).\n\n" +
                  "Pulsa «⟳ Sincronizar mis tickets»: pregunta a DevOps por lo asignado a la cuenta de TU PAT,\n" +
                  "así que funciona aunque el correo de tu ficha no coincida con el de tu cuenta de DevOps.");
        }
        else _lblEmpty.Visible = false;

        Filter();
    }

    private void ShowEmpty(string mensaje)
    {
        _lblEmpty.Text = mensaje;
        _lblEmpty.Visible = true;
        _lblEmpty.BringToFront();
    }

    private void Filter()
    {
        if (_suspenderFiltros) return;

        var data = MyDevOpsTicketFilter.Aplicar(_mine, new MyDevOpsFilter(
            _txtSearch.Text,
            ValorFiltro(_cbxEstado),
            ValorFiltro(_cbxTipo),
            ValorFiltro(_cbxIteracion),
            _chkSoloAbiertos.Checked,
            VentanaSeleccionada()), DateTime.UtcNow);

        // Lo que te asignaron y todavía no dijiste cuánto te va a llevar. Se cuenta solo sobre lo
        // ABIERTO: pedir la estimación de un ticket ya cerrado no sirve para planear nada.
        if (_chkSinEstimar.Checked)
            data = data.Where(t => t.SinEstimar && !AzureDevOpsService.EsCerrado(t.State)).ToList();

        _grid.DataSource = data;
        if (_mine.Count > 0) _lblEmpty.Visible = false;

        _lblStatus.Text = MyDevOpsTicketFilter.Resumen(
            data.Count, _mine.Count,
            _mine.Count(t => !AzureDevOpsService.EsCerrado(t.State)),
            _mine.Count > 0 ? _mine.Max(t => t.SyncedAt) : null);

        PintarPendientesDeEstimar();
    }

    /// <summary>
    /// Aviso de lo que falta por estimar. No bloquea nada: si una sincronización trae doscientos
    /// tickets viejos, dejar a alguien sin poder trabajar hasta estimarlos todos sería peor que el
    /// problema que resuelve.
    /// </summary>
    private void PintarPendientesDeEstimar()
    {
        int faltan = _mine.Count(t => t.SinEstimar && !AzureDevOpsService.EsCerrado(t.State));

        _chkSinEstimar.Text = faltan > 0 ? $"Solo sin estimar ({faltan})" : "Solo sin estimar";
        _chkSinEstimar.ForeColor = faltan > 0 ? AppTheme.Warning : AppTheme.TextPrimary;
        _chkSinEstimar.Font = faltan > 0 ? AppTheme.BoldFont : AppTheme.DefaultFont;
        _tip.SetToolTip(_chkSinEstimar, faltan > 0
            ? $"Tienes {faltan} ticket(s) asignados sin estimar.\n" +
              "Clic derecho sobre uno → «⏱ Estimar»; se guarda en el campo Effort del work item."
            : "Todos tus tickets abiertos están estimados.");
    }

    // ── Formato del grid ──────────────────────────────────────────
    private void Grid_CellFormatting(object? s, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Rows.Count <= e.RowIndex) return;
        var ticket = (_grid.DataSource as List<DevOpsTicket>)?[e.RowIndex];
        if (ticket == null) return;
        var col = _grid.Columns[e.ColumnIndex].Name;

        if (col == "State") { e.CellStyle.ForeColor = StateColor(ticket.State); e.CellStyle.Font = AppTheme.BoldFont; }
        if (col == "CommentCount" && ticket.CommentCount == 0) { e.Value = ""; e.FormattingApplied = true; }
    }

    private void Grid_CellDoubleClick(object? s, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var ticket = (_grid.DataSource as List<DevOpsTicket>)?[e.RowIndex];
        if (ticket == null) return;
        if (_grid.Columns[e.ColumnIndex].Name == "CommentCount") ShowCommentsDialog(ticket);
        else OpenInDevOps(ticket);
    }

    private void Grid_MouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var hit = _grid.HitTest(e.X, e.Y);
        if (hit.RowIndex < 0) return;
        _grid.ClearSelection();
        _grid.Rows[hit.RowIndex].Selected = true;
        var ticket = (_grid.DataSource as List<DevOpsTicket>)?[hit.RowIndex];
        if (ticket == null) return;

        var menu = new ContextMenuStrip();
        menu.Items.Add(ticket.SinEstimar ? "⏱  Estimar (pendiente)…" : "⏱  Cambiar mi estimación…",
                       null, (_, _) => EstimarAsync(ticket));
        menu.Items.Add("🔍  Ficha: regresiones y devoluciones…", null, (_, _) => VerFicha(ticket));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("🔗  Abrir en Azure DevOps", null, (_, _) => OpenInDevOps(ticket));
        menu.Items.Add("💬  Ver / agregar comentarios", null, (_, _) => ShowCommentsDialog(ticket));
        menu.Items.Add("🔧  Cambiar prioridad…", null, (_, _) => CambiarPrioridadAsync(ticket));
        menu.Show(_grid, e.Location);
    }

    /// <summary>
    /// Captura la estimación y la escribe en el campo Effort del work item. Se puede volver a
    /// estimar: una estimación que resultó equivocada y no se corrige deja de servir para planear.
    /// </summary>
    private async void EstimarAsync(DevOpsTicket ticket)
    {
        using var frm = new EstimarTicketForm(ticket);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var (ok, escrito, mensaje) = await _devOps.EstimarTicketAsync(
                ticket.ExternalId, frm.Horas, _currentUser.DeveloperId);
            LoadData();
            MessageBox.Show(mensaje, ok && escrito ? "Estimado" : "Con aviso",
                MessageBoxButtons.OK, ok && escrito ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo estimar:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Regresiones (bugs hijos) y cuántas veces le devolvieron el ticket.</summary>
    private void VerFicha(DevOpsTicket ticket)
    {
        using var frm = new DevOpsTicketFichaForm(_devOps, ticket);
        frm.ShowDialog(FindForm());
    }

    // ── Cambiar prioridad en DevOps (con el PAT personal) ─────────
    private async void CambiarPrioridadAsync(DevOpsTicket ticket)
    {
        using var frm = new PriorityPickerForm(ticket.ExternalId, ticket.Priority);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            await _devOps.ChangePriorityAsync(ticket.ExternalId, frm.SelectedPriority, _currentUser.UserId);
            LoadData();
            MessageBox.Show(
                $"Prioridad del ticket #{ticket.ExternalId} cambiada a {frm.SelectedPriority} " +
                $"({SlaPolicyStore.NombrePrioridad(frm.SelectedPriority)}) en DevOps.\n" +
                "Si hay una política de SLA para esa prioridad, tu compromiso se ajustó.",
                "Prioridad actualizada", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo cambiar la prioridad:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void OpenInDevOps(DevOpsTicket ticket)
    {
        if (!string.IsNullOrEmpty(ticket.Url))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ticket.Url) { UseShellExecute = true });
    }

    // ── Comentarios + EVIDENCIAS (con el PAT personal del desarrollador) ───────
    private void ShowCommentsDialog(DevOpsTicket ticket)
    {
        using var dlg = new Form
        {
            Text = $"Comentarios — #{ticket.ExternalId}: {ticket.Title}",
            Size = new Size(680, 580), StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(480, 420), BackColor = AppTheme.ContentBg, Font = AppTheme.DefaultFont
        };
        if (AppTheme.AppIcon != null) dlg.Icon = AppTheme.AppIcon;

        var pnlComments = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Padding = new Padding(12) };
        pnlComments.Controls.Add(new Label { Text = "Cargando comentarios…", AutoSize = true, Location = new Point(12, 12), ForeColor = AppTheme.TextSecondary });

        // Evidencias adjuntas pendientes de enviar en este comentario
        var evidencias = new List<(byte[] bytes, string fileName)>();

        var pnlNew = new Panel { Dock = DockStyle.Bottom, Height = 160, BackColor = AppTheme.ContentBg, Padding = new Padding(12, 8, 12, 8) };
        var lblNew = new Label { Text = "Nuevo comentario (se publica con TU PAT, a tu nombre):", Location = new Point(12, 8), AutoSize = true, Font = AppTheme.BoldFont };
        var txtNew = new TextBox { Multiline = true, Location = new Point(12, 30), Size = new Size(dlg.ClientSize.Width - 150, 54), ScrollBars = ScrollBars.Vertical, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };

        var btnAttach = AppTheme.MakeSecondaryButton("📎  Imagen…", 120);
        btnAttach.Location = new Point(12, 92);
        btnAttach.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        var btnPaste = AppTheme.MakeSecondaryButton("📋  Pegar captura", 140);
        btnPaste.Location = new Point(140, 92);
        btnPaste.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        var lblAttach = new Label { Location = new Point(292, 98), AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont, Anchor = AnchorStyles.Left | AnchorStyles.Top };

        var btnSend = AppTheme.MakePrimaryButton("Enviar 💬", 120);
        btnSend.Location = new Point(dlg.ClientSize.Width - 132, 30);
        btnSend.Anchor = AnchorStyles.Right | AnchorStyles.Top;

        void ActualizarAdjuntos()
        {
            lblAttach.Text = evidencias.Count == 0
                ? "Sin evidencias adjuntas."
                : $"📎 {evidencias.Count} evidencia(s): {string.Join(", ", evidencias.Select(e => e.fileName))}  (clic para quitar)";
        }
        ActualizarAdjuntos();
        lblAttach.Click += (_, _) => { if (evidencias.Count > 0) { evidencias.Clear(); ActualizarAdjuntos(); } };

        btnAttach.Click += (_, _) =>
        {
            using var ofd = new OpenFileDialog
            {
                Multiselect = true, Title = "Adjuntar evidencia",
                Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|Todos los archivos|*.*"
            };
            if (ofd.ShowDialog(dlg) != DialogResult.OK) return;
            foreach (var f in ofd.FileNames)
                evidencias.Add((System.IO.File.ReadAllBytes(f), System.IO.Path.GetFileName(f)));
            ActualizarAdjuntos();
        };

        btnPaste.Click += (_, _) =>
        {
            if (!Clipboard.ContainsImage())
            {
                MessageBox.Show("No hay una imagen en el portapapeles.\nToma una captura (Win + Shift + S) y vuelve a pulsar «Pegar captura».",
                    "Portapapeles", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var img = Clipboard.GetImage();
            if (img == null)
            {
                MessageBox.Show("No se pudo leer la imagen del portapapeles. Vuelve a copiarla e inténtalo de nuevo.",
                    "Portapapeles", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var ms = new System.IO.MemoryStream();
            img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            evidencias.Add((ms.ToArray(), $"captura_{DateTime.Now:yyyyMMdd_HHmmss}.png"));
            ActualizarAdjuntos();
        };

        btnSend.Click += async (_, _) =>
        {
            var text = txtNew.Text.Trim();
            if (text.Length == 0 && evidencias.Count == 0) return;
            btnSend.Enabled = btnAttach.Enabled = btnPaste.Enabled = false; btnSend.Text = "Enviando…";
            try
            {
                if (evidencias.Count > 0)
                    await _devOps.PostCommentWithEvidenceAsync(ticket.ExternalId, text, evidencias);
                else
                    await _devOps.PostCommentAsync(ticket.ExternalId, text);
                txtNew.Clear();
                evidencias.Clear(); ActualizarAdjuntos();
                // PostCommentAsync ya incrementó y guardó CommentCount sobre esta MISMA entidad rastreada
                // (el DbContext es singleton), así que aquí NO se vuelve a sumar: solo se repinta el grid.
                _grid.Refresh();
                await RefreshComments(pnlComments, ticket.ExternalId);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"No se pudo enviar el comentario:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { btnSend.Enabled = btnAttach.Enabled = btnPaste.Enabled = true; btnSend.Text = "Enviar 💬"; }
        };

        pnlNew.Controls.AddRange([lblNew, txtNew, btnAttach, btnPaste, lblAttach, btnSend]);
        dlg.Controls.AddRange([pnlComments, pnlNew]);
        _ = RefreshComments(pnlComments, ticket.ExternalId);
        dlg.ShowDialog(FindForm());
    }

    private async Task RefreshComments(Panel pnl, int externalId)
    {
        List<DevOpsComment> comments;
        try { comments = await _devOps.GetCommentsAsync(externalId); }
        catch (Exception ex)
        {
            pnl.Controls.Clear();
            pnl.Controls.Add(new Label { Text = $"Error al cargar comentarios: {ex.Message}", AutoSize = true, Location = new Point(12, 12), ForeColor = AppTheme.Danger });
            return;
        }

        pnl.Controls.Clear();
        if (comments.Count == 0)
        {
            pnl.Controls.Add(new Label { Text = "Sin comentarios todavía.", AutoSize = true, Location = new Point(12, 12), ForeColor = AppTheme.TextSecondary });
            return;
        }

        int y = 8;
        foreach (var c in comments.OrderBy(x => x.CreatedAt))
        {
            var bubble = new Panel { Location = new Point(8, y), Width = pnl.ClientSize.Width - 28, BackColor = Color.FromArgb(241, 245, 249), Padding = new Padding(10), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            var header = new Label { Text = $"🧑  {c.Author}   ·   {c.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}", AutoSize = true, Location = new Point(10, 8), Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary };
            var body = new Label { Text = System.Text.RegularExpressions.Regex.Replace(c.Text, "<[^>]*>", ""), AutoSize = true, Location = new Point(10, 28), MaximumSize = new Size(bubble.Width - 20, 0) };
            bubble.Controls.AddRange([header, body]);
            bubble.Height = body.Bottom + 10;
            pnl.Controls.Add(bubble);
            y += bubble.Height + 8;
        }
        pnl.AutoScrollMinSize = new Size(0, y + 12);
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }

    // ── Helpers ───────────────────────────────────────────────────
    private static Color StateColor(string state) => (state ?? "").ToLower() switch
    {
        "active" or "in progress" or "doing" or "in development" => AppTheme.SidebarActive,
        "resolved" or "done" or "completed"                       => AppTheme.Success,
        "closed"                                                   => Color.Gray,
        "new" or "to do"                                           => AppTheme.TextSecondary,
        _                                                          => AppTheme.TextPrimary
    };

    private static DataGridViewTextBoxColumn Col(string header, string prop, int w) => new()
    {
        HeaderText = header, DataPropertyName = prop, Name = prop, Width = w, ReadOnly = true
    };
}
