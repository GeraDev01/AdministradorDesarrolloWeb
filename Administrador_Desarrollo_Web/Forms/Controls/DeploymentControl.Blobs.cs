using System.IO;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// Explorador del contenedor de Azure Blob Storage: qué hay en cada carpeta y edición de los
/// metadatos de un blob existente. Solo Admin — desde aquí se toca directamente el almacenamiento.
/// </summary>
public partial class DeploymentControl
{
    private ComboBox _cbxBlobFolder = null!, _cbxBlobSort = null!;
    private TextBox _txtBlobFilter = null!;
    private DataGridView _gridBlobs = null!;
    private Label _lblBlobStatus = null!;
    private List<BlobItem> _blobs = [];

    private TabPage BuildBlobsTab()
    {
        // Esta pestaña NO usa NewTab: sus barras se miden solas.
        //
        // Con alturas fijas hay que acertar a mano el relleno, el alto del botón y su margen; erré
        // el cálculo por 2 px y los botones salían cortados por abajo. Con AutoSize la fila crece
        // hasta lo que ocupa su contenido, así que deja de depender de que yo sume bien — y
        // sobrevive al escalado de pantalla, que cambia esas medidas.
        var tab = new TabPage("  ☁  Blob Storage  ") { BackColor = AppTheme.ContentBg };
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // botones
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // filtro y orden
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // estado
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // rejilla: todo lo que sobre
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            // Con los botones de eliminar la barra ya no cabe en una sola fila en pantallas angostas:
            // se permite que envuelva a una segunda línea en vez de cortar los últimos botones.
            WrapContents = true, Padding = new Padding(10, 6, 10, 6), BackColor = AppTheme.ContentBg
        };
        toolbar.Controls.Add(new Label { Text = "Carpeta:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });

        _cbxBlobFolder = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDown, Margin = new Padding(0, 4, 8, 0) };
        _cbxBlobFolder.SelectedIndexChanged += async (_, _) => await CargarBlobsAsync();
        toolbar.Controls.Add(_cbxBlobFolder);

        var bList = AppTheme.MakeSecondaryButton("🔄 Listar", 105, 28); bList.Margin = new Padding(0, 4, 6, 0);
        bList.Click += async (_, _) => await CargarBlobsAsync();
        var bSubir = AppTheme.MakePrimaryButton("⬆ Subir archivos", 165, 28); bSubir.Margin = new Padding(0, 4, 6, 0);
        bSubir.Click += async (_, _) => await SubirArchivosAsync();
        var bNueva = AppTheme.MakeSecondaryButton("📁 Nueva carpeta", 155, 28); bNueva.Margin = new Padding(0, 4, 6, 0);
        bNueva.Click += async (_, _) => await CrearCarpetaAsync();
        toolbar.Controls.AddRange([bList, bSubir, bNueva]);
        var bMeta = AppTheme.MakePrimaryButton("🏷 Metadatos", 130, 28); bMeta.Margin = new Padding(0, 4, 6, 0);
        bMeta.Click += async (_, _) => await EditarMetadatosAsync();
        var bSas = AppTheme.MakePrimaryButton("🔗 Enlace de descarga", 180, 28); bSas.Margin = new Padding(0, 4, 6, 0);
        bSas.BackColor = AppTheme.Success;
        bSas.Click += (_, _) => GenerarSas();
        var bCopy = AppTheme.MakeSecondaryButton("📋 Copiar nombre", 150, 28); bCopy.Margin = new Padding(0, 4, 6, 0);
        bCopy.Click += (_, _) => CopiarNombre();
        var bDel = AppTheme.MakeDangerButton("🗑 Eliminar archivo", 165, 28); bDel.Margin = new Padding(0, 4, 6, 0);
        bDel.Click += async (_, _) => await EliminarBlobSeleccionadoAsync();
        var bDelCarpeta = AppTheme.MakeDangerButton("🗑 Eliminar carpeta", 175, 28); bDelCarpeta.Margin = new Padding(0, 4, 0, 0);
        bDelCarpeta.Click += async (_, _) => await EliminarCarpetaSeleccionadaAsync();
        toolbar.Controls.AddRange([bMeta, bSas, bCopy, bDel, bDelCarpeta]);

        // ── Filtro y orden ───────────────────────────────────────
        var filtros = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(10, 4, 10, 6), BackColor = AppTheme.ContentBg
        };
        filtros.Controls.Add(new Label { Text = "Filtrar:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        _txtBlobFilter = new TextBox { Width = 240, Margin = new Padding(0, 4, 12, 0), PlaceholderText = "versión, sistema o parte del nombre" };
        _txtBlobFilter.TextChanged += (_, _) => PintarBlobs();
        filtros.Controls.Add(_txtBlobFilter);

        filtros.Controls.Add(new Label { Text = "Ordenar por:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        _cbxBlobSort = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 4, 0, 0) };
        _cbxBlobSort.Items.AddRange(["Fecha (más reciente primero)", "Fecha (más antigua primero)",
                                     "Versión (descendente)", "Versión (ascendente)",
                                     "Tamaño (mayor primero)"]);
        _cbxBlobSort.SelectedIndex = 0;
        _cbxBlobSort.SelectedIndexChanged += (_, _) => PintarBlobs();
        filtros.Controls.Add(_cbxBlobSort);

        _lblBlobStatus = new Label
        {
            Dock = DockStyle.Top, AutoSize = false, Height = 22,
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 12, 0),
            AutoEllipsis = true,
            Text = "Elige una carpeta y pulsa Listar."
        };

        _gridBlobs = AppTheme.MakeGrid();
        _gridBlobs.MultiSelect = false;
        _gridBlobs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Archivo",   Name = "Name", FillWeight = 40 });
        _gridBlobs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tamaño",    Name = "Size", FillWeight = 12 });
        _gridBlobs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Modificado", Name = "Date", FillWeight = 18 });
        _gridBlobs.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Metadatos", Name = "Meta", FillWeight = 30 });
        _gridBlobs.CellDoubleClick += async (_, ev) => { if (ev.RowIndex >= 0) await EditarMetadatosAsync(); };

        var pGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 10), BackColor = AppTheme.ContentBg };
        pGrid.Controls.Add(_gridBlobs);

        body.Controls.Add(toolbar,        0, 0);
        body.Controls.Add(filtros,        0, 1);
        body.Controls.Add(_lblBlobStatus, 0, 2);
        body.Controls.Add(pGrid,          0, 3);
        tab.Controls.Add(body);
        return tab;
    }

    /// <summary>Puebla el combo con las carpetas que existen realmente en el contenedor.</summary>
    private async Task CargarCarpetasBlobAsync(bool forzar = false)
    {
        if (_cbxBlobFolder.Items.Count > 0 && !forzar) return;
        if (!_blob.IsConfigured)
        {
            _lblBlobStatus.ForeColor = AppTheme.Warning;
            _lblBlobStatus.Text = "⚠  Azure Blob Storage no está configurado (Configuración → Azure Blob Storage).";
            return;
        }

        var seleccion = _cbxBlobFolder.Text;

        // Las tres carpetas base van siempre, aunque todavía no existan: son a donde escribe la app.
        var carpetas = new List<string> { _blob.PrefijoVersiones, _blob.PrefijoRespaldosBd, _blob.PrefijoRespaldosDespliegue };
        try
        {
            foreach (var c in await _blob.ListarCarpetasAsync())
                if (!carpetas.Contains(c, StringComparer.OrdinalIgnoreCase)) carpetas.Add(c);
        }
        catch (Exception ex)
        {
            _lblBlobStatus.ForeColor = AppTheme.Danger;
            _lblBlobStatus.Text = $"✗  No se pudieron listar las carpetas: {ex.Message}";
        }

        _cbxBlobFolder.Items.Clear();
        _cbxBlobFolder.Items.AddRange([.. carpetas]);
        _cbxBlobFolder.SelectedIndex = Math.Max(0, carpetas.FindIndex(c => c.Equals(seleccion, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Crea una carpeta en el contenedor. Se propone como raíz la carpeta actualmente elegida, que
    /// es lo que casi siempre se quiere: colgar «QA» o «Infrasur» debajo de «releases».
    /// </summary>
    private async Task CrearCarpetaAsync()
    {
        if (!_blob.IsConfigured)
        {
            MessageBox.Show("Configura primero Azure Blob Storage.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var padre = BlobStorageService.Normalizar(_cbxBlobFolder.Text, _blob.PrefijoVersiones);
        using var frm = new NuevaCarpetaBlobForm(padre);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var (creada, ruta) = await _blob.CrearCarpetaAsync(frm.RutaCompleta);

            _audit.RecordDetailed(AuditAction.ConfigChange, "Blob", ruta,
                creada ? $"Carpeta creada en Blob Storage: {ruta}" : $"La carpeta {ruta} ya existía",
                Models.AuditOutcome.Exito);

            await CargarCarpetasBlobAsync(forzar: true);
            _cbxBlobFolder.Text = ruta;
            await CargarBlobsAsync();

            _lblBlobStatus.ForeColor = creada ? AppTheme.Success : AppTheme.TextSecondary;
            _lblBlobStatus.Text = creada
                ? $"✓  Carpeta «{ruta}» creada."
                : $"La carpeta «{ruta}» ya existía; no se modificó su contenido.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo crear la carpeta:\n\n{ex.Message}", "Nueva carpeta",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Sube archivos del equipo a la carpeta actualmente elegida (como Blobup). Avisa antes de
    /// sobrescribir los que ya existan y muestra el progreso en un diálogo cancelable.
    /// </summary>
    private async Task SubirArchivosAsync()
    {
        if (!_blob.IsConfigured)
        {
            MessageBox.Show("Configura primero Azure Blob Storage.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var carpeta = BlobStorageService.Normalizar(_cbxBlobFolder.Text, _blob.PrefijoVersiones);

        using var ofd = new OpenFileDialog { Title = $"Subir a {carpeta}/", Multiselect = true };
        if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;
        var archivos = ofd.FileNames.ToList();
        if (archivos.Count == 0) return;

        // Detectar conflictos antes de subir para poder avisar (Azure sobrescribe sin preguntar).
        List<string> existentes;
        try
        {
            existentes = [];
            foreach (var f in archivos)
                if (await _blob.ExisteBlobAsync($"{carpeta}/{Path.GetFileName(f)}"))
                    existentes.Add(Path.GetFileName(f));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo comprobar el contenido de la carpeta:\n{ex.Message}", "Subir",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (existentes.Count > 0)
        {
            var muestra = string.Join(", ", existentes.Take(8)) + (existentes.Count > 8 ? "…" : "");
            var r = MessageBox.Show(
                $"{existentes.Count} archivo(s) ya existen en «{carpeta}»:\n\n  {muestra}\n\n" +
                "Sí = sobrescribirlos\nNo = omitir los que ya existen y subir el resto\nCancelar = no subir nada",
                "Archivos existentes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.No)
                archivos = archivos.Where(f => !existentes.Contains(Path.GetFileName(f))).ToList();
        }

        if (archivos.Count == 0)
        {
            _lblBlobStatus.ForeColor = AppTheme.TextSecondary;
            _lblBlobStatus.Text = "No quedó nada por subir.";
            return;
        }

        using (var frm = new BlobUploadForm(_blob, carpeta, archivos))
        {
            frm.ShowDialog(FindForm());

            if (frm.Subidos > 0)
                _audit.RecordDetailed(AuditAction.ConfigChange, "Blob", carpeta,
                    $"Subida a Blob Storage: {frm.Subidos} archivo(s) en «{carpeta}»", Models.AuditOutcome.Exito);

            await CargarBlobsAsync();
            _lblBlobStatus.ForeColor = frm.Fallidos > 0 ? AppTheme.Warning : AppTheme.Success;
            _lblBlobStatus.Text = frm.Fallidos > 0
                ? $"✓  {frm.Subidos} archivo(s) subido(s), {frm.Fallidos} con error."
                : $"✓  {frm.Subidos} archivo(s) subido(s) a «{carpeta}».";
        }
    }

    private async Task CargarBlobsAsync()
    {
        if (!_blob.IsConfigured) return;

        var carpeta = BlobStorageService.Normalizar(_cbxBlobFolder.Text, _blob.PrefijoVersiones);
        _lblBlobStatus.ForeColor = AppTheme.TextSecondary;
        _lblBlobStatus.Text = $"Listando {carpeta}/…";
        _gridBlobs.Rows.Clear();

        try
        {
            _blobs = await _blob.ListarDetalladoAsync(carpeta + "/");
            PintarBlobs();
        }
        catch (Exception ex)
        {
            _lblBlobStatus.ForeColor = AppTheme.Danger;
            _lblBlobStatus.Text = $"✗  {ex.Message}";
        }
    }

    /// <summary>
    /// Aplica filtro y orden sobre lo ya descargado. Filtrar y reordenar NO vuelve a consultar
    /// Azure: sería una petición de red por cada tecla.
    /// </summary>
    private void PintarBlobs()
    {
        var texto = _txtBlobFilter.Text.Trim();
        var lista = _blobs.AsEnumerable();

        if (texto.Length > 0)
            // Se busca también en los metadatos: así «Infrasur» o un checksum encuentran el archivo
            // aunque no aparezcan en el nombre.
            lista = lista.Where(b =>
                b.Name.Contains(texto, StringComparison.OrdinalIgnoreCase) ||
                b.Metadata.Any(m => m.Value.Contains(texto, StringComparison.OrdinalIgnoreCase)));

        lista = _cbxBlobSort.SelectedIndex switch
        {
            1 => lista.OrderBy(b => b.LastModified),
            2 => lista.OrderByDescending(b => VersionDe(b), VersionComparer.Instancia).ThenByDescending(b => b.LastModified),
            3 => lista.OrderBy(b => VersionDe(b), VersionComparer.Instancia).ThenBy(b => b.LastModified),
            4 => lista.OrderByDescending(b => b.SizeBytes),
            _ => lista.OrderByDescending(b => b.LastModified)
        };

        var filtrados = lista.ToList();
        _gridBlobs.Rows.Clear();
        foreach (var b in filtrados)
            _gridBlobs.Rows.Add(
                b.Name,
                b.SizeLegible,
                b.LastModified?.LocalDateTime.ToString("dd/MM/yyyy HH:mm") ?? "—",
                b.Metadata.Count == 0 ? "—" : string.Join(", ", b.Metadata.Select(m => $"{m.Key}={m.Value}")));

        long total = filtrados.Sum(b => b.SizeBytes);
        _lblBlobStatus.ForeColor = AppTheme.TextSecondary;
        _lblBlobStatus.Text = _blobs.Count == 0
            ? "La carpeta está vacía o todavía no existe."
            : filtrados.Count == _blobs.Count
                ? $"{filtrados.Count} archivo(s) — {total / 1024d / 1024d:0.0} MB."
                : $"{filtrados.Count} de {_blobs.Count} archivo(s) — {total / 1024d / 1024d:0.0} MB.";
    }

    /// <summary>Versión del blob: la de sus metadatos si la tiene, o la deducida del nombre.</summary>
    private static string VersionDe(BlobItem b)
    {
        if (b.Metadata.TryGetValue("version", out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        var m = System.Text.RegularExpressions.Regex.Match(b.ShortName, @"\d+(\.\d+)+");
        return m.Success ? m.Value : b.ShortName;
    }

    /// <summary>
    /// Compara versiones por número y no como texto: alfabéticamente «1.10» va antes que «1.9»,
    /// que es justo lo contrario de lo que espera quien busca la última versión.
    /// </summary>
    private sealed class VersionComparer : IComparer<string>
    {
        public static readonly VersionComparer Instancia = new();

        public int Compare(string? x, string? y)
        {
            var a = Partes(x); var b = Partes(y);
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                int va = i < a.Length ? a[i] : 0;
                int vb = i < b.Length ? b[i] : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        }

        private static int[] Partes(string? v) =>
            (v ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries)
                     .Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0)
                     .ToArray();
    }

    private void GenerarSas()
    {
        if (BlobSeleccionado() is not { } blob)
        {
            MessageBox.Show("Selecciona un archivo.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var frm = new SasOptionsForm(blob.Name);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            var uri = _blob.GenerarSasDescarga(blob.Name, frm.Horas);
            var caduca = DateTime.Now.AddHours(frm.Horas);

            // El enlace da acceso al archivo a quien lo tenga: queda en bitácora quién lo generó,
            // para qué archivo y hasta cuándo sirve.
            _audit.RecordDetailed(AuditAction.ConfigChange, "Blob", blob.Name,
                $"Enlace SAS de descarga generado (vigencia {frm.Horas:0} h, caduca {caduca:dd/MM/yyyy HH:mm})",
                Models.AuditOutcome.Exito);

            try { Clipboard.SetText(uri.ToString()); } catch { }

            using var ver = new Details.ChangelogEditForm($"enlace de {blob.ShortName}", uri.ToString(), soloLectura: true);
            _lblBlobStatus.ForeColor = AppTheme.Success;
            _lblBlobStatus.Text = $"✓  Enlace copiado al portapapeles. Caduca el {caduca:dd/MM/yyyy HH:mm}.";
            ver.ShowDialog(FindForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo generar el enlace:\n\n{ex.Message}", "Enlace de descarga",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private BlobItem? BlobSeleccionado() =>
        _gridBlobs.CurrentRow?.Cells["Name"].Value is string n ? _blobs.FirstOrDefault(b => b.Name == n) : null;

    private async Task EditarMetadatosAsync()
    {
        if (BlobSeleccionado() is not { } blob)
        {
            MessageBox.Show("Selecciona un archivo.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            // Se releen del servidor, no se usan los de la lista: pudieron cambiar desde el portal
            // o desde otra instancia, y guardar reemplaza el conjunto completo.
            var actuales = await _blob.ObtenerMetadatosAsync(blob.Name);

            using var frm = new BlobMetadataForm(blob.Name, actuales);
            if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

            await _blob.ActualizarMetadatosAsync(blob.Name, frm.Metadata);
            _audit.RecordDetailed(AuditAction.ConfigChange, "Blob", blob.Name,
                $"Metadatos actualizados en {blob.Name}", Models.AuditOutcome.Exito,
                oldValues: actuales, newValues: frm.Metadata);

            await CargarBlobsAsync();
            _lblBlobStatus.ForeColor = AppTheme.Success;
            _lblBlobStatus.Text = $"✓  Metadatos guardados en {blob.ShortName}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudieron guardar los metadatos:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CopiarNombre()
    {
        if (BlobSeleccionado() is not { } blob) return;
        try { Clipboard.SetText(blob.Name); _lblBlobStatus.Text = $"Copiado: {blob.Name}"; }
        catch { /* el portapapeles puede estar ocupado por otra aplicación */ }
    }

    /// <summary>Elimina el archivo (blob) seleccionado, con confirmación. La acción es irreversible.</summary>
    private async Task EliminarBlobSeleccionadoAsync()
    {
        if (!_blob.IsConfigured)
        {
            MessageBox.Show("Configura primero Azure Blob Storage.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (BlobSeleccionado() is not { } blob)
        {
            MessageBox.Show("Selecciona un archivo.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(
                $"¿Eliminar definitivamente este archivo?\n\n{blob.Name}\n{blob.SizeLegible}\n\n" +
                "Esta acción no se puede deshacer.",
                "Eliminar archivo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        try
        {
            UseWaitCursor = true;
            var borrado = await _blob.EliminarBlobAsync(blob.Name);
            // La bitácora refleja lo que de verdad pasó: si otro ya lo había borrado, no se miente
            // diciendo que este usuario lo eliminó.
            _audit.RecordDetailed(AuditAction.ConfigChange, "Blob", blob.Name,
                borrado ? $"Archivo eliminado de Blob Storage: {blob.Name}"
                        : $"Eliminar {blob.Name}: el archivo ya no existía",
                Models.AuditOutcome.Exito);

            await CargarBlobsAsync();
            _lblBlobStatus.ForeColor = AppTheme.Success;
            _lblBlobStatus.Text = borrado
                ? $"✓  Archivo «{blob.ShortName}» eliminado."
                : $"El archivo «{blob.ShortName}» ya no existía.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo eliminar el archivo:\n\n{ex.Message}", "Eliminar archivo",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }

    /// <summary>
    /// Elimina la carpeta actualmente elegida y TODO su contenido (archivos y subcarpetas). Cuenta
    /// primero los elementos para avisar del alcance y advierte extra si es una carpeta base.
    /// </summary>
    private async Task EliminarCarpetaSeleccionadaAsync()
    {
        if (!_blob.IsConfigured)
        {
            MessageBox.Show("Configura primero Azure Blob Storage.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var carpeta = BlobStorageService.Normalizar(_cbxBlobFolder.Text, "");
        if (carpeta.Length == 0)
        {
            MessageBox.Show("Elige en el combo la carpeta que quieres eliminar.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        int cuantos;
        try { cuantos = await _blob.ContarBlobsEnCarpetaAsync(carpeta); }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo revisar la carpeta:\n\n{ex.Message}", "Eliminar carpeta",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (cuantos == 0)
        {
            MessageBox.Show($"La carpeta «{carpeta}» está vacía o ya no existe.", "Eliminar carpeta",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Aviso extra si al borrar se llevaría una carpeta base a donde escribe la app (versiones o
        // respaldos): tanto si es exactamente esa carpeta como si es una CARPETA PADRE que la contiene
        // (p. ej. borrar «prod» cuando las versiones están en «prod/releases»).
        bool esBase = new[] { _blob.PrefijoVersiones, _blob.PrefijoRespaldosBd, _blob.PrefijoRespaldosDespliegue }
            .Any(p => p.Equals(carpeta, StringComparison.OrdinalIgnoreCase)
                   || p.StartsWith(carpeta + "/", StringComparison.OrdinalIgnoreCase));
        var avisoBase = esBase
            ? "\n\n⚠ Contiene una CARPETA BASE del sistema (versiones o respaldos): al borrarla se elimina TODO ese histórico."
            : "";

        if (MessageBox.Show(
                $"¿Eliminar la carpeta «{carpeta}» y TODO su contenido?\n\n" +
                $"Se borrarán {cuantos} elemento(s), incluidas sus subcarpetas.{avisoBase}\n\n" +
                "Esta acción no se puede deshacer.",
                "Eliminar carpeta", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        try
        {
            UseWaitCursor = true;
            int borrados = await _blob.EliminarCarpetaAsync(carpeta);
            _audit.RecordDetailed(AuditAction.ConfigChange, "Blob", carpeta,
                borrados > 0 ? $"Carpeta eliminada de Blob Storage: {carpeta} ({borrados} elemento(s))"
                             : $"Eliminar carpeta {carpeta}: no había nada que borrar",
                Models.AuditOutcome.Exito);

            await CargarCarpetasBlobAsync(forzar: true);
            await CargarBlobsAsync();
            _lblBlobStatus.ForeColor = AppTheme.Success;
            _lblBlobStatus.Text = $"✓  Carpeta «{carpeta}» eliminada ({borrados} elemento(s)).";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo eliminar la carpeta:\n\n{ex.Message}", "Eliminar carpeta",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UseWaitCursor = false; }
    }
}
