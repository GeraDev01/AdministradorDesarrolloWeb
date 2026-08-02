using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// «Hay una versión nueva». Se abre con <c>Show</c> y no con <c>ShowDialog</c> a propósito: es un
/// aviso, no un trámite — nadie debe tener que atenderlo para empezar a trabajar.
///
/// Dos modos, según cómo se repartió esta copia:
///  · <b>Automática</b> (instalada con Velopack): «Actualizar ahora» la descarga con barra de
///    avance y la deja lista; se aplica al cerrar la aplicación, no de golpe — esta app vive en la
///    bandeja todo el día y reiniciarla a media captura perdería trabajo.
///  · <b>Manual</b> (copia portátil): «Descargar» abre el enlace y ya; el resto lo hace la persona.
/// </summary>
public class UpdateNoticeForm : ResponsiveForm
{
    private readonly OfertaActualizacion _oferta;
    private readonly UpdateService? _updates;

    private Button _btnAccion = null!;
    private Button _btnDespues = null!;
    private ProgressBar _barra = null!;
    private Label _lblAvance = null!;
    private CancellationTokenSource? _cts;
    /// <summary>Ya se descargó: el botón principal pasa de «Actualizar» a «Reiniciar».</summary>
    private bool _descargada;

    public UpdateNoticeForm(OfertaActualizacion oferta, UpdateService? updates = null)
    {
        _oferta = oferta; _updates = updates;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Hay una versión nueva";
        Size = new Size(560, 440);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Padding = new Padding(18, 14, 18, 14) };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));   // título
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));   // versión en marcha
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));   // «novedades»
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // notas
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));   // avance
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));   // botones
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        tbl.Controls.Add(new Label
        {
            Text = $"⬆  Versión {_oferta.VersionTexto} disponible",
            Dock = DockStyle.Fill, Font = AppTheme.HeaderFont, ForeColor = AppTheme.TextPrimary
        }, 0, 0);

        tbl.Controls.Add(new Label
        {
            Text = $"Estás usando la {AppVersion.Texto}." +
                   (_oferta.Modo == ModoActualizacion.Automatica
                       ? "  Esta copia se actualiza sola."
                       : "  Esta copia es portátil: la descarga es manual."),
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        }, 0, 1);

        tbl.Controls.Add(new Label
        {
            Text = "Novedades:", Dock = DockStyle.Fill, Font = AppTheme.BoldFont, ForeColor = AppTheme.TextSecondary
        }, 0, 2);

        tbl.Controls.Add(new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
            Text = (_oferta.Novedades ?? "(sin notas de esta versión)")
                   .Replace("\r\n", "\n").Replace("\n", Environment.NewLine)
        }, 0, 3);

        var pnlAvance = new Panel { Dock = DockStyle.Fill };
        _barra = new ProgressBar { Dock = DockStyle.Top, Height = 16, Visible = false, Maximum = 100 };
        _lblAvance = new Label { Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary };
        pnlAvance.Controls.Add(_lblAvance);
        pnlAvance.Controls.Add(_barra);
        tbl.Controls.Add(pnlAvance, 0, 4);

        var botones = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };

        _btnDespues = AppTheme.MakeSecondaryButton("Después", 110);
        _btnDespues.Click += (_, _) => { _cts?.Cancel(); Close(); };
        botones.Controls.Add(_btnDespues);

        if (_oferta.Modo == ModoActualizacion.Automatica && _updates != null)
        {
            _btnAccion = AppTheme.MakePrimaryButton("⬇ Actualizar ahora", 170);
            // UN solo manejador que despacha según el estado. Enganchar y desenganchar lambdas no
            // funciona: cada lambda es una instancia distinta y el «-=» no quita nada.
            _btnAccion.Click += async (_, _) =>
            {
                if (_descargada) Reiniciar();
                else await DescargarAsync();
            };
            botones.Controls.Add(_btnAccion);
        }
        else if (_oferta.Url is { Length: > 0 } url)
        {
            _btnAccion = AppTheme.MakePrimaryButton("⬇ Descargar", 140);
            _btnAccion.Click += (_, _) => Abrir(url);
            botones.Controls.Add(_btnAccion);
        }
        else
        {
            botones.Controls.Add(new Label
            {
                Text = "Pídele el instalador al administrador.", AutoSize = true,
                ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont, Margin = new Padding(0, 12, 8, 0)
            });
        }

        tbl.Controls.Add(botones, 0, 5);
        Controls.Add(tbl);
        CancelButton = _btnDespues;

        // Si esta MISMA versión ya se descargó (se pidió actualizar y luego «Después»), el diálogo
        // arranca en «Reiniciar ahora»: volver a bajar 40 MB de lo que ya está en disco sería
        // tirar el trabajo hecho.
        if (_oferta.Modo == ModoActualizacion.Automatica
            && _updates?.VersionListaParaAplicar == _oferta.VersionTexto)
        {
            _descargada = true;
            _btnAccion.Text = "Reiniciar ahora";
            _lblAvance.ForeColor = AppTheme.Success;
            _lblAvance.Text = "✓ Ya está descargada. Se instalará al salir de la aplicación.";
            _btnDespues.Text = "Cerrar";
        }
    }

    private async Task DescargarAsync()
    {
        if (_updates == null) return;

        _btnAccion.Enabled = false;
        _btnAccion.Text = "Descargando…";
        _barra.Visible = true;
        _barra.Value = 0;
        _lblAvance.Text = "Descargando en segundo plano; puedes seguir trabajando.";
        _cts = new CancellationTokenSource();

        // El Progress se crea en el hilo de UI, así que sus llamadas vuelven a él solas.
        var avance = new Progress<int>(p => { if (!IsDisposed) _barra.Value = Math.Clamp(p, 0, 100); });
        // Se pasa la oferta: la ventana descarga EXACTAMENTE lo que anunció, no lo que el servicio
        // (que es Singleton) tenga guardado por otra consulta posterior.
        var (ok, mensaje) = await _updates.DescargarAsync(_oferta, avance, _cts.Token);

        if (IsDisposed) return;
        _barra.Visible = false;

        if (!ok)
        {
            _lblAvance.ForeColor = AppTheme.Danger;
            _lblAvance.Text = mensaje;
            _btnAccion.Enabled = true;
            _btnAccion.Text = "⬇ Reintentar";
            return;
        }

        // Descargada. NO se arma aquí el updater: solo espera 60 segundos a ver morir este proceso
        // y esta aplicación vive en la bandeja durante horas. Se arma en el cierre real
        // (MainForm.OnFormClosing), y así además se lanza UNO SOLO — dos updaters compitiendo por
        // el mismo bloqueo dejarían la instalación a medias.
        _descargada = true;
        _lblAvance.ForeColor = AppTheme.Success;
        _lblAvance.Text = "✓ Lista. Se instalará al salir de la aplicación (bandeja → Salir).";
        _btnAccion.Text = "Reiniciar ahora";
        _btnAccion.Enabled = true;
        _btnDespues.Text = "Cerrar";
    }

    /// <summary>
    /// Cierra la aplicación por su camino normal para que la actualización se aplique y vuelva a
    /// abrir. NO mata el proceso: el cierre pasa por MainForm.OnFormClosing, que es donde se avisa
    /// de un despliegue en curso, se cierra la jornada y se consolidan los cronómetros. Matarlo
    /// aquí dejaría la jornada abierta y el tiempo cronometrado sin guardar.
    /// </summary>
    private void Reiniciar()
    {
        if (_updates == null) return;
        if (MessageBox.Show(
                "Se cerrará la aplicación para instalar la actualización y volverá a abrirse." +
                Environment.NewLine + Environment.NewLine + "¿Continuar?",
                "Reiniciar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        _updates.PedirReinicioTrasAplicar();

        // La referencia se toma ANTES de cerrar: esta ventana es propiedad de MainForm y queda
        // desechada en cuanto aquella se cierre.
        var principal = Owner as MainForm;
        if (principal == null || !principal.CerrarOrdenadamente())
            MessageBox.Show("No se cerró la aplicación; la actualización se instalará al salir.",
                "Actualizar", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Abrir(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo abrir el enlace:\n{ex.Message}\n\n{url}", "Descargar",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnFormClosed(e);
    }
}
