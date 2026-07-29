using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Pantalla del administrador para enviar comunicados al equipo. Cada comunicado se entrega como un
/// AVISO a la bandeja de los desarrolladores seleccionados (y les suena el globo de la bandeja).
/// </summary>
public class AnnouncementsControl : UserControl
{
    private readonly AnnouncementService _service;

    private TextBox _txtTitulo = null!, _txtCuerpo = null!;
    private CheckedListBox _lstDevs = null!;
    private CheckBox _chkTodos = null!;
    private Button _btnEnviar = null!;
    private List<AnnouncementService.Destinatario> _destinatarios = [];
    private bool _sincronizandoTodos;   // evita el bucle entre «Todos» y los ítems

    public AnnouncementsControl(AnnouncementService service)
    {
        _service = service;
        BuildUI();
        CargarDestinatarios();
    }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg; Dock = DockStyle.Fill;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 6, BorderStyle = BorderStyle.None };

        // ── Izquierda: redactar el comunicado ─────────────────────
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 12, 10, 6) };

        // Botón SIEMPRE visible al fondo (docked). Antes se anclaba a la parte inferior con una Y
        // absoluta dentro de un panel Dock=Fill sin dimensionar y quedaba fuera de la vista.
        var pnlBtn = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = AppTheme.ContentBg };
        _btnEnviar = AppTheme.MakePrimaryButton("📨 Enviar comunicado", 210, 36);
        _btnEnviar.Location = new Point(4, 9);
        _btnEnviar.Click += (_, _) => Enviar();
        pnlBtn.Controls.Add(_btnEnviar);

        // Área de redacción: llena el resto, con scroll si la ventana es baja.
        var compose = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = AppTheme.ContentBg };
        int y = 4;
        compose.Controls.Add(new Label { Text = "📢 Enviar comunicado al equipo", Location = new Point(4, y), AutoSize = true, Font = AppTheme.HeaderFont, ForeColor = AppTheme.SidebarActive }); y += 34;
        compose.Controls.Add(new Label { Text = "Aparece como un aviso en la bandeja de cada desarrollador.", Location = new Point(4, y), AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary }); y += 28;

        compose.Controls.Add(new Label { Text = "Título", Location = new Point(4, y), AutoSize = true, Font = AppTheme.BoldFont }); y += 22;
        _txtTitulo = new TextBox { Location = new Point(4, y), Width = 520, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        compose.Controls.Add(_txtTitulo); y += 34;

        compose.Controls.Add(new Label { Text = "Mensaje", Location = new Point(4, y), AutoSize = true, Font = AppTheme.BoldFont }); y += 22;
        _txtCuerpo = new TextBox { Location = new Point(4, y), Width = 520, Height = 260, Multiline = true, ScrollBars = ScrollBars.Vertical, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
        compose.Controls.Add(_txtCuerpo);

        left.Controls.Add(compose);   // fill (se agrega primero)
        left.Controls.Add(pnlBtn);    // abajo
        split.Panel1.Controls.Add(left);

        // ── Derecha: destinatarios ────────────────────────────────
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 12, 14, 12) };
        _lstDevs = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, Font = AppTheme.DefaultFont, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
        _chkTodos = new CheckBox { Text = "Todos", Dock = DockStyle.Top, Height = 26, Checked = true, Font = AppTheme.DefaultFont };
        // «Todos» marca/desmarca todos; y al marcar/desmarcar ítems, «Todos» refleja si están todos.
        _chkTodos.CheckedChanged += (_, _) =>
        {
            if (_sincronizandoTodos) return;
            _sincronizandoTodos = true;
            for (int i = 0; i < _lstDevs.Items.Count; i++) _lstDevs.SetItemChecked(i, _chkTodos.Checked);
            _sincronizandoTodos = false;
        };
        _lstDevs.ItemCheck += (_, _) =>
        {
            if (_sincronizandoTodos) return;
            // ItemCheck ocurre ANTES de aplicar el cambio; se lee el estado final en el siguiente ciclo.
            BeginInvoke(() =>
            {
                _sincronizandoTodos = true;
                bool todos = _lstDevs.Items.Count > 0;
                for (int i = 0; i < _lstDevs.Items.Count && todos; i++) todos = _lstDevs.GetItemChecked(i);
                _chkTodos.Checked = todos;
                _sincronizandoTodos = false;
            });
        };
        var lblDest = new Label { Text = "Destinatarios", Dock = DockStyle.Top, Height = 24, Font = AppTheme.BoldFont, ForeColor = AppTheme.TextPrimary };
        right.Controls.Add(_lstDevs);   // fill (se agrega primero)
        right.Controls.Add(_chkTodos);  // arriba
        right.Controls.Add(lblDest);    // más arriba
        split.Panel2.Controls.Add(right);

        Controls.Add(split);
        split.SplitterDistance = 560;
    }

    private void CargarDestinatarios()
    {
        _destinatarios = _service.DestinatariosPosibles();
        _sincronizandoTodos = true;
        _lstDevs.Items.Clear();
        foreach (var d in _destinatarios)
            _lstDevs.Items.Add(d.TieneCuenta ? d.Nombre : $"{d.Nombre}  —  (sin cuenta, no recibe)", isChecked: d.TieneCuenta);
        // «Todos» solo queda marcado si TODOS los listados pueden recibir (los sin cuenta van desmarcados).
        _chkTodos.Checked = _destinatarios.Count > 0 && _destinatarios.All(d => d.TieneCuenta);
        _sincronizandoTodos = false;
    }

    private void Enviar()
    {
        var ids = new List<int>();
        int conCuenta = 0;
        for (int i = 0; i < _lstDevs.Items.Count; i++)
            if (_lstDevs.GetItemChecked(i))
            {
                ids.Add(_destinatarios[i].DeveloperId);
                if (_destinatarios[i].TieneCuenta) conCuenta++;
            }

        if (ids.Count == 0)
        {
            MessageBox.Show("Selecciona al menos un desarrollador.", "Sin destinatarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (conCuenta == 0)
        {
            MessageBox.Show("Ninguno de los seleccionados tiene cuenta de usuario activa, así que nadie recibiría el aviso.",
                "Sin destinatarios con cuenta", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var nota = conCuenta < ids.Count ? $"  ({ids.Count - conCuenta} sin cuenta no lo recibirán)" : "";
        if (MessageBox.Show($"¿Enviar el comunicado a {conCuenta} desarrollador(es){nota}?", "Confirmar envío",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        var (ok, mensaje, _) = _service.Enviar(_txtTitulo.Text, _txtCuerpo.Text, ids);
        MessageBox.Show(mensaje, ok ? "Enviado" : "No se envió",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        if (ok) { _txtTitulo.Clear(); _txtCuerpo.Clear(); }
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) CargarDestinatarios(); }
}
