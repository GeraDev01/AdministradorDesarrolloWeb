using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// «Hay una versión nueva». Se abre con <c>Show</c> y no con <c>ShowDialog</c> a propósito: es un
/// aviso, no un trámite — nadie debe tener que atenderlo para empezar a trabajar.
/// </summary>
public class UpdateNoticeForm : ResponsiveForm
{
    private readonly AvisoVersion _aviso;

    public UpdateNoticeForm(AvisoVersion aviso)
    {
        _aviso = aviso;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Hay una versión nueva";
        Size = new Size(560, 420);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        var tbl = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(18, 14, 18, 14) };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        tbl.Controls.Add(new Label
        {
            Text = $"⬆  Versión {_aviso.VersionTexto} disponible",
            Dock = DockStyle.Fill, Font = AppTheme.HeaderFont, ForeColor = AppTheme.TextPrimary
        }, 0, 0);

        tbl.Controls.Add(new Label
        {
            Text = $"Estás usando la {AppVersion.Texto}.",
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
            Text = (_aviso.Novedades ?? "(sin notas de esta versión)")
                   .Replace("\r\n", "\n").Replace("\n", Environment.NewLine)
        }, 0, 3);

        var botones = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0) };

        var despues = AppTheme.MakeSecondaryButton("Después", 110);
        despues.Click += (_, _) => Close();
        botones.Controls.Add(despues);

        // El enlace solo se ofrece si es de un tipo que tiene sentido abrir: el servicio ya lo
        // filtró, y si vino mal capturado se muestra el aviso igual, sin botón.
        if (_aviso.Url is { Length: > 0 } url)
        {
            var descargar = AppTheme.MakePrimaryButton("⬇ Descargar", 140);
            descargar.Click += (_, _) => Abrir(url);
            botones.Controls.Add(descargar);
        }
        else
        {
            botones.Controls.Add(new Label
            {
                Text = "Pídele el instalador al administrador.", AutoSize = true,
                ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont, Margin = new Padding(0, 12, 8, 0)
            });
        }

        tbl.Controls.Add(botones, 0, 4);
        Controls.Add(tbl);
        CancelButton = despues;
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
}
