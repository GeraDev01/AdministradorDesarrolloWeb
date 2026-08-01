using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// Sube uno o varios archivos a una carpeta del contenedor, con barra de progreso (por bytes) y una
/// bitácora en vivo, al estilo de Blobup. Se puede cancelar; lo ya subido se conserva.
/// </summary>
public class BlobUploadForm : ResponsiveForm
{
    private readonly BlobStorageService _blob;
    private readonly string _carpeta;
    private readonly List<string> _archivos;
    private readonly CancellationTokenSource _cts = new();

    private ProgressBar _bar = null!;
    private TextBox _log = null!;
    private Button _btn = null!;
    private Label _lbl = null!;
    private bool _terminado;

    public int Subidos { get; private set; }
    public int Fallidos { get; private set; }

    public BlobUploadForm(BlobStorageService blob, string carpeta, IReadOnlyList<string> archivos)
    {
        _blob = blob; _carpeta = carpeta; _archivos = archivos.ToList();
        BuildUI();
        Shown += async (_, _) => await SubirTodoAsync();
        FormClosing += (_, e) =>
        {
            // Cerrar (X) mientras sube = cancelar; no se cierra de golpe para no dejar el upload a medias.
            if (!_terminado) { _cts.Cancel(); e.Cancel = true; _btn.Enabled = false; _btn.Text = "Cancelando…"; }
        };
    }

    private void BuildUI()
    {
        Text = "Subir a Azure Blob Storage";
        Size = new Size(640, 460);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        _lbl = new Label
        {
            Location = new Point(16, 14), AutoSize = false, Size = new Size(600, 40), Font = AppTheme.BoldFont,
            Text = $"Subiendo {_archivos.Count} archivo(s) a la carpeta «{_carpeta}»…"
        };
        _bar = new ProgressBar { Location = new Point(16, 58), Size = new Size(600, 22), Style = ProgressBarStyle.Continuous, Maximum = 100 };
        _log = new TextBox
        {
            Location = new Point(16, 92), Size = new Size(600, 290), Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.Gainsboro,
            Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.FixedSingle
        };
        _btn = AppTheme.MakeSecondaryButton("Cancelar", 110);
        _btn.Location = new Point(506, 392);
        _btn.Click += (_, _) =>
        {
            if (_terminado) { DialogResult = DialogResult.OK; Close(); }
            else { _cts.Cancel(); _btn.Enabled = false; _btn.Text = "Cancelando…"; }
        };

        Controls.AddRange([_lbl, _bar, _log, _btn]);
    }

    private async Task SubirTodoAsync()
    {
        long total = 0;
        var sizes = new long[_archivos.Count];
        for (int i = 0; i < _archivos.Count; i++)
        {
            try { sizes[i] = new FileInfo(_archivos[i]).Length; } catch { sizes[i] = 0; }
            total += sizes[i];
        }
        if (total <= 0) total = 1;

        long baseline = 0;
        for (int i = 0; i < _archivos.Count; i++)
        {
            if (_cts.IsCancellationRequested) { Log("⏹ Cancelado."); break; }

            var path = _archivos[i];
            var name = Path.GetFileName(path);
            var blobName = $"{_carpeta}/{name}";
            Log($"⬆ {name} ({Legible(sizes[i])})…");

            long b = baseline, tam = total;
            var progress = new Progress<long>(bytes =>
            {
                if (IsDisposed) return;
                _bar.Value = (int)Math.Min(100, Math.Max(0, (b + bytes) * 100 / tam));
            });

            try
            {
                await _blob.SubirArchivoAsync(path, blobName, progress, null, _cts.Token);
                Subidos++;
                Log($"  ✓ {name}");
            }
            catch (OperationCanceledException) { Log("⏹ Cancelado."); break; }
            catch (Exception ex) { Fallidos++; Log($"  ✗ {name}: {ex.Message}"); }

            baseline += sizes[i];
            if (!IsDisposed) _bar.Value = (int)Math.Min(100, baseline * 100 / total);
        }

        _terminado = true;
        if (IsDisposed) return;
        _bar.Value = 100;
        _lbl.Text = $"Listo: {Subidos} subido(s)" + (Fallidos > 0 ? $", {Fallidos} con error." : ".");
        _btn.Enabled = true;
        _btn.Text = "Cerrar";
    }

    private void Log(string line)
    {
        if (IsDisposed) return;
        _log.AppendText(line + Environment.NewLine);
    }

    private static string Legible(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024        => $"{bytes / 1024d:0.0} KB",
        _              => $"{bytes} B"
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _cts.Cancel(); _cts.Dispose(); }
        base.Dispose(disposing);
    }
}
