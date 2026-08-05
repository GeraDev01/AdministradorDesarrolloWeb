using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Lo que un ticket de DevOps no cuenta en sus campos: las <b>regresiones</b> que colgó (bugs
/// hijos) y por <b>cuántas manos</b> pasó.
///
/// Las dos cosas responden la misma pregunta desde ángulos distintos: si un ticket acumula bugs
/// hijos, se dio por terminado antes de estarlo; si vuelve una y otra vez a la misma persona
/// después de haber pasado por otras, tampoco. Ninguna de las dos se ve en la rejilla, y las dos
/// cambian cómo se lee «entregado».
///
/// Se consulta a DevOps al abrirla, no al sincronizar: expandir relaciones y bajar revisiones de
/// cada ticket convertiría un sync de doscientos en varios cientos de peticiones.
/// </summary>
public class DevOpsTicketFichaForm : ResponsiveForm
{
    private readonly AzureDevOpsService _devOps;
    private readonly DevOpsTicket _ticket;
    private readonly string? _nombreDev;
    private readonly string? _emailDev;

    private Label _lblEstado = null!;
    private Label _kpiRegresiones = null!, _kpiAbiertas = null!, _kpiDevoluciones = null!;
    private ListView _lstBugs = null!;
    private ListView _lstAsignaciones = null!;
    private DevOpsFichaTicket? _ficha;

    /// <param name="nombreDev">Persona sobre la que se cuentan las devoluciones; por omisión, quien lo tiene ahora.</param>
    public DevOpsTicketFichaForm(AzureDevOpsService devOps, DevOpsTicket ticket,
                                 string? nombreDev = null, string? emailDev = null)
    {
        _devOps = devOps;
        _ticket = ticket;
        _nombreDev = string.IsNullOrWhiteSpace(nombreDev) ? ticket.AssignedTo : nombreDev;
        _emailDev = string.IsNullOrWhiteSpace(emailDev) ? ticket.AssignedToUniqueName : emailDev;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = $"Ficha del ticket #{_ticket.ExternalId}";
        Size = new Size(900, 700);
        MinimumSize = new Size(720, 560);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 96f));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));

        var hdr = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.HeaderBg, Margin = Padding.Empty };
        hdr.Controls.Add(new Label
        {
            Text = $"  🔷  #{_ticket.ExternalId}  {_ticket.Title}",
            Dock = DockStyle.Fill, ForeColor = Color.White, Font = AppTheme.HeaderFont,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
        });

        var kpis = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(12, 6, 12, 6), BackColor = AppTheme.ContentBg
        };
        kpis.Controls.Add(MakeKpi("🐞 Regresiones (bugs hijos)", out _kpiRegresiones, AppTheme.Danger));
        kpis.Controls.Add(MakeKpi("🔴 Sin cerrar", out _kpiAbiertas, AppTheme.Warning));
        kpis.Controls.Add(MakeKpi($"🔁 Devoluciones a {Corto(_nombreDev)}", out _kpiDevoluciones, AppTheme.SidebarActive));

        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Point(12, 4) };

        _lstBugs = MakeLista(["Bug", "Título", "Estado"], [70, 420, 140]);
        _lstBugs.DoubleClick += (_, _) => AbrirSeleccionado(_lstBugs);
        tabs.TabPages.Add(WrapTab("  🐞  Regresiones  ", _lstBugs));

        _lstAsignaciones = MakeLista(["Cuándo", "De", "A"], [150, 260, 260]);
        tabs.TabPages.Add(WrapTab("  🔁  Historial de asignación  ", _lstAsignaciones));

        _lblEstado = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(14, 0, 0, 0),
            Text = "Consultando DevOps…"
        };

        var btns = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10), BackColor = AppTheme.ContentBg };
        var btnCerrar = AppTheme.MakeSecondaryButton("Cerrar", 100);
        btnCerrar.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        var btnAbrirTicket = AppTheme.MakeSecondaryButton("↗ Abrir en DevOps", 175);
        btnAbrirTicket.Click += (_, _) => Abrir(_ticket.Url);
        btns.Controls.AddRange([btnCerrar, btnAbrirTicket]);

        outer.Controls.Add(hdr,       0, 0);
        outer.Controls.Add(kpis,      0, 1);
        outer.Controls.Add(tabs,      0, 2);
        outer.Controls.Add(_lblEstado, 0, 3);
        outer.Controls.Add(btns,      0, 4);
        Controls.Add(outer);
        AcceptButton = btnCerrar;
    }

    private static string Corto(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return "quien lo tiene";
        var partes = nombre.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return partes.Length > 0 ? partes[0] : nombre;
    }

    private static ListView MakeLista(string[] columnas, int[] anchos)
    {
        var lst = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
            MultiSelect = false, HideSelection = false, BorderStyle = BorderStyle.FixedSingle
        };
        for (int i = 0; i < columnas.Length; i++) lst.Columns.Add(columnas[i], anchos[i]);
        return lst;
    }

    private static TabPage WrapTab(string titulo, Control contenido)
    {
        var tab = new TabPage(titulo) { BackColor = AppTheme.ContentBg, Padding = new Padding(10, 8, 10, 10) };
        var pnl = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.ContentBg };
        pnl.Controls.Add(contenido);
        tab.Controls.Add(pnl);
        return tab;
    }

    private static Panel MakeKpi(string titulo, out Label valor, Color acento)
    {
        var card = new Panel { Width = 250, Height = 80, Margin = new Padding(0, 2, 10, 2), BackColor = AppTheme.CardBg, BorderStyle = BorderStyle.FixedSingle };
        card.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 5, BackColor = acento });
        var content = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.CardBg, Padding = new Padding(10, 6, 6, 6) };
        content.Controls.Add(new Label { Dock = DockStyle.Top, Height = 24, Text = titulo, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft });
        valor = new Label { Dock = DockStyle.Fill, Text = "…", Font = AppTheme.KpiValueFont, ForeColor = acento, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        content.Controls.Add(valor);
        card.Controls.Add(content);
        return card;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await CargarAsync();
    }

    private async Task CargarAsync()
    {
        try
        {
            _ficha = await _devOps.ObtenerFichaAsync(_ticket.ExternalId);
        }
        catch (Exception ex)
        {
            // Un fallo de red o un PAT sin permiso no puede tumbar la ventana: se dice y ya.
            _lblEstado.Text = $"No se pudo consultar DevOps: {ex.Message}";
            _lblEstado.ForeColor = AppTheme.Danger;
            _kpiRegresiones.Text = _kpiAbiertas.Text = _kpiDevoluciones.Text = "—";
            return;
        }
        if (IsDisposed || _ficha == null) return;

        _kpiRegresiones.Text = _ficha.Regresiones.ToString();
        _kpiAbiertas.Text = _ficha.RegresionesAbiertas.ToString();
        _kpiDevoluciones.Text = _ficha.DevolucionesA(_nombreDev, _emailDev).ToString();

        _lstBugs.BeginUpdate();
        _lstBugs.Items.Clear();
        foreach (var b in _ficha.BugsHijos)
        {
            var item = new ListViewItem($"#{b.Id}") { Tag = b.Url };
            item.SubItems.Add(b.Titulo);
            item.SubItems.Add(b.Cerrado ? $"✔ {b.Estado}" : $"🔴 {b.Estado}");
            item.UseItemStyleForSubItems = false;
            item.SubItems[2].ForeColor = b.Cerrado ? AppTheme.TextSecondary : AppTheme.Danger;
            _lstBugs.Items.Add(item);
        }
        if (_ficha.BugsHijos.Count == 0)
        {
            var vacio = new ListViewItem("—");
            vacio.SubItems.Add("Ningún bug cuelga de este ticket.");
            vacio.SubItems.Add("");
            vacio.ForeColor = AppTheme.TextSecondary;
            _lstBugs.Items.Add(vacio);
        }
        _lstBugs.EndUpdate();

        _lstAsignaciones.BeginUpdate();
        _lstAsignaciones.Items.Clear();
        foreach (var c in _ficha.Asignaciones)
        {
            var item = new ListViewItem(c.Fecha == default ? "—" : c.Fecha.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
            item.SubItems.Add(c.De ?? "(sin asignar)");
            item.SubItems.Add(c.A ?? "(sin asignar)");
            _lstAsignaciones.Items.Add(item);
        }
        if (_ficha.Asignaciones.Count == 0)
            _lstAsignaciones.Items.Add("(nunca cambió de dueño)");
        _lstAsignaciones.EndUpdate();

        int dev = _ficha.DevolucionesA(_nombreDev, _emailDev);
        _lblEstado.Text = _ficha.Regresiones == 0 && dev == 0
            ? "Sin regresiones ni devoluciones. Este ticket se cerró bien a la primera."
            : $"{_ficha.Regresiones} regresión(es) · {_ficha.Asignaciones.Count} cambio(s) de dueño · " +
              $"{dev} devolución(es) a {Corto(_nombreDev)}.";
        _lblEstado.ForeColor = _ficha.RegresionesAbiertas > 0 || dev > 0 ? AppTheme.Warning : AppTheme.TextSecondary;
    }

    private static void AbrirSeleccionado(ListView lst)
    {
        if (lst.SelectedItems.Count == 0) return;
        if (lst.SelectedItems[0].Tag is string url) Abrir(url);
    }

    private static void Abrir(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"No se pudo abrir:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
