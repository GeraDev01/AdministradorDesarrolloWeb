using Administrador_Desarrollo_Web.Forms.Details;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Controls;

/// <summary>
/// La biblioteca de plantillas del área: cuerpos de ticket de Freshdesk, respuestas al cliente,
/// observaciones para requerimientos y work items de Azure DevOps, documentos de entrega de
/// estimaciones y scripts de utilería (SQL, PowerShell, Bash).
///
/// Es LA MISMA pantalla para dos roles (el patrón de DeploymentControl): el administrador la ve
/// completa; el desarrollador la ve en SOLO CONSULTA — copiar y guardar, sin botones de escritura —
/// y únicamente con los tipos que el servicio le deja leer. El filtrado real vive en
/// TemplateService.Legibles(); aquí solo se decide qué botones existen.
///
/// El trabajo diario es de dos botones: **📋 Copiar** (deja el texto listo en el portapapeles,
/// preguntando antes lo que haya que rellenar) y **💾 Guardar como…** (lo baja a un archivo con la
/// extensión que le toca).
///
/// La aplicación NO ejecuta los scripts: los entrega. Quién los corre, contra qué servidor y con qué
/// credenciales es una decisión que se toma fuera, con los ojos puestos en lo que se va a ejecutar.
/// </summary>
public class TemplatesControl : UserControl
{
    private readonly TemplateService _templates;
    private readonly bool _esAdmin;

    private TextBox _txtBuscar = null!;
    private ComboBox _cbxTipo = null!;
    private CheckBox? _chkArchivadas;
    private DataGridView _grid = null!;
    private Label _lblEstado = null!, _lblTitulo = null!, _lblMeta = null!, _lblDescripcion = null!;
    private TextBox _txtVistaPrevia = null!;
    private Button _btnCopiar = null!, _btnGuardarComo = null!;
    private Button? _btnEditar, _btnDuplicar, _btnEliminar, _btnArchivar;

    private List<Template> _rows = [];

    /// <summary>Los tipos en el orden del enum; el combo agrega «Todos» al frente.</summary>
    private static readonly TemplateKind[] Tipos = Enum.GetValues<TemplateKind>();

    /// <summary>Los tipos que el combo ofrece a ESTA sesión: al desarrollador no se le muestran
    /// filtros de tipos que la consulta jamás le devolverá (elegir «Script SQL» y ver la lista
    /// vacía se lee como un error).</summary>
    private readonly TemplateKind[] _tipos;

    public TemplatesControl(TemplateService templates, ICurrentUser currentUser)
    {
        _templates = templates;
        _esAdmin = currentUser.IsAdmin;
        _tipos = _esAdmin ? Tipos : TemplateService.TiposDelEquipo;
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
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));   // acciones
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));   // filtros
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // lista + vista previa
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));   // estado
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // ── Acciones ───────────────────────────────────────────────
        var acciones = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(10, 6, 10, 2), BackColor = AppTheme.ContentBg
        };

        _btnCopiar = AppTheme.MakePrimaryButton("📋 Copiar", 130);
        _btnCopiar.Margin = new Padding(0, 0, 6, 0);
        _btnCopiar.Click += (_, _) => Copiar();

        _btnGuardarComo = AppTheme.MakeSecondaryButton("💾 Guardar como…", 165);
        _btnGuardarComo.Margin = new Padding(0, 0, 18, 0);
        _btnGuardarComo.Click += (_, _) => GuardarComo();

        acciones.Controls.AddRange([_btnCopiar, _btnGuardarComo]);

        // Los botones de escritura solo EXISTEN para el administrador. No se deshabilitan: un
        // botón gris invita a preguntar por qué; uno ausente no. La biblioteca es material curado.
        if (_esAdmin)
        {
            var btnNueva = AppTheme.MakeSecondaryButton("➕ Nueva", 105);
            btnNueva.Margin = new Padding(0, 0, 6, 0);
            btnNueva.Click += (_, _) => Nueva();

            _btnEditar = AppTheme.MakeSecondaryButton("✏ Editar", 100);
            _btnEditar.Margin = new Padding(0, 0, 6, 0);
            _btnEditar.Click += (_, _) => Editar();

            _btnDuplicar = AppTheme.MakeSecondaryButton("⧉ Duplicar", 110);
            _btnDuplicar.Margin = new Padding(0, 0, 6, 0);
            _btnDuplicar.Click += (_, _) => Duplicar();

            _btnArchivar = AppTheme.MakeSecondaryButton("🗄 Archivar", 120);
            _btnArchivar.Margin = new Padding(0, 0, 6, 0);
            _btnArchivar.Click += (_, _) => Archivar();

            _btnEliminar = AppTheme.MakeDangerButton("🗑 Eliminar", 115);
            _btnEliminar.Margin = new Padding(0, 0, 0, 0);
            _btnEliminar.Click += (_, _) => Eliminar();

            acciones.Controls.AddRange([btnNueva, _btnEditar, _btnDuplicar, _btnArchivar, _btnEliminar]);
        }
        else
        {
            acciones.Controls.Add(new Label
            {
                Text = "Solo consulta — las plantillas las administra el líder",
                AutoSize = true, ForeColor = AppTheme.TextSecondary, Font = AppTheme.SmallFont,
                Margin = new Padding(4, 10, 0, 0)
            });
        }

        // ── Filtros ────────────────────────────────────────────────
        var filtros = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(10, 2, 10, 4), BackColor = AppTheme.ContentBg
        };

        _txtBuscar = new TextBox { Width = 260, Margin = new Padding(0, 2, 10, 0), PlaceholderText = "Buscar en título, etiquetas y contenido…" };
        _txtBuscar.TextChanged += (_, _) => LoadData();

        _cbxTipo = new ComboBox { Width = 230, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 2, 10, 0) };
        _cbxTipo.Items.Add("Todos los tipos");
        foreach (var k in _tipos) _cbxTipo.Items.Add($"{TemplateService.IconoTipo(k)}  {TemplateService.EtiquetaTipo(k)}");
        _cbxTipo.SelectedIndex = 0;
        _cbxTipo.SelectedIndexChanged += (_, _) => LoadData();

        filtros.Controls.AddRange([_txtBuscar, _cbxTipo]);

        // «Ver archivadas» solo para el administrador: el servicio se lo ignora al desarrollador,
        // y una casilla que no hace nada es peor que ninguna.
        if (_esAdmin)
        {
            _chkArchivadas = new CheckBox { Text = "Ver archivadas", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            _chkArchivadas.CheckedChanged += (_, _) => LoadData();
            filtros.Controls.Add(_chkArchivadas);
        }

        // ── Lista + vista previa ───────────────────────────────────
        var cuerpo = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Padding = new Padding(10, 2, 10, 4), BackColor = AppTheme.ContentBg
        };
        cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
        cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
        cuerpo.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _grid = AppTheme.MakeGrid();
        _grid.Margin = new Padding(0, 0, 8, 0);
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tipo",       Name = "Tipo",   FillWeight = 24 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Título",     Name = "Titulo", FillWeight = 40 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Etiquetas",  Name = "Tags",   FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Usos",       Name = "Usos",   FillWeight = 7,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Actualizada", Name = "Fecha", FillWeight = 14 });
        // El mapeo fila→entidad usa el índice de _rows: si se permitiera ordenar por encabezado, la
        // fila visible dejaría de coincidir con _rows y se copiaría o borraría la plantilla equivocada.
        foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        _grid.SelectionChanged += (_, _) => PintarSeleccion();
        _grid.CellDoubleClick += (_, ev) => { if (ev.RowIndex >= 0) Copiar(); };

        cuerpo.Controls.Add(_grid, 0, 0);
        cuerpo.Controls.Add(ConstruirVistaPrevia(), 1, 0);

        _lblEstado = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0)
        };

        tbl.Controls.Add(acciones,   0, 0);
        tbl.Controls.Add(filtros,    0, 1);
        tbl.Controls.Add(cuerpo,     0, 2);
        tbl.Controls.Add(_lblEstado, 0, 3);
        Controls.Add(tbl);
    }

    private Control ConstruirVistaPrevia()
    {
        var pnl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            BackColor = AppTheme.CardBg, Padding = new Padding(12, 10, 12, 10), Margin = Padding.Empty
        };
        pnl.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));   // título
        pnl.RowStyles.Add(new RowStyle(SizeType.Absolute, 20f));   // meta
        pnl.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));   // descripción
        pnl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // contenido
        pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        _lblTitulo = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.HeaderFont, ForeColor = AppTheme.TextPrimary,
            AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
        };
        _lblMeta = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary,
            AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
        };
        _lblDescripcion = new Label
        {
            Dock = DockStyle.Fill, Font = AppTheme.DefaultFont, ForeColor = AppTheme.TextSecondary,
            AutoEllipsis = true, TextAlign = ContentAlignment.TopLeft
        };
        _txtVistaPrevia = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = false,
            Font = AppTheme.MonoFont, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle
        };

        pnl.Controls.Add(_lblTitulo,      0, 0);
        pnl.Controls.Add(_lblMeta,        0, 1);
        pnl.Controls.Add(_lblDescripcion, 0, 2);
        pnl.Controls.Add(_txtVistaPrevia, 0, 3);
        return pnl;
    }

    // ── Datos ────────────────────────────────────────────────────────────────────

    private void LoadData()
    {
        // Se recuerda cuál estaba seleccionada para no perder el sitio al teclear en el buscador.
        int? seleccionada = Seleccionada()?.Id;

        TemplateKind? tipo = _cbxTipo.SelectedIndex > 0 ? _tipos[_cbxTipo.SelectedIndex - 1] : null;
        try
        {
            _rows = _templates.Listar(tipo, _txtBuscar.Text, _chkArchivadas?.Checked ?? false);
        }
        catch (AuthorizationException ex)
        {
            _rows = [];
            _lblEstado.Text = ex.Message;
            _grid.Rows.Clear();
            PintarSeleccion();
            return;
        }

        _grid.Rows.Clear();
        foreach (var t in _rows)
        {
            int i = _grid.Rows.Add(
                $"{TemplateService.IconoTipo(t.Kind)}  {TemplateService.EtiquetaTipo(t.Kind)}",
                t.Title + (t.FileBytes is { Length: > 0 } ? "  📎" : ""),
                t.Tags ?? "—",
                t.UsageCount,
                (t.UpdatedAt ?? t.CreatedAt).ToLocalTime().ToString("dd/MM/yyyy"));

            if (t.IsArchived)
            {
                _grid.Rows[i].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;
                _grid.Rows[i].Cells["Titulo"].Value = "🗄  " + _grid.Rows[i].Cells["Titulo"].Value;
            }
            if (t.UsageCount > 0) _grid.Rows[i].Cells["Usos"].Style.Font = AppTheme.BoldFont;
        }

        if (seleccionada is int id)
        {
            int idx = _rows.FindIndex(t => t.Id == id);
            if (idx >= 0) _grid.CurrentCell = _grid.Rows[idx].Cells[0];
        }

        _lblEstado.Text = _rows.Count == 0
            ? _esAdmin
                ? "No hay plantillas con ese filtro.  Pulsa ➕ Nueva para crear una."
                : "No hay plantillas con ese filtro."
            : $"{_rows.Count} plantilla(s).  Doble clic copia la seleccionada.  Los marcadores {{{{así}}}} se preguntan al copiar.";

        PintarSeleccion();
    }

    private Template? Seleccionada() =>
        _grid.CurrentRow is { Index: >= 0 } r && r.Index < _rows.Count ? _rows[r.Index] : null;

    private void PintarSeleccion()
    {
        var t = Seleccionada();
        bool hay = t != null;
        bool hayTexto = t is { Body.Length: > 0 };

        // Los botones de escritura son null en modo consulta: se creó solo lo que el rol puede usar.
        if (_btnEditar   != null) _btnEditar.Enabled   = hay;
        if (_btnDuplicar != null) _btnDuplicar.Enabled = hay;
        if (_btnEliminar != null) _btnEliminar.Enabled = hay;
        if (_btnArchivar != null)
        {
            _btnArchivar.Enabled = hay;
            _btnArchivar.Text = t is { IsArchived: true } ? "♻ Restaurar" : "🗄 Archivar";
        }
        _btnGuardarComo.Enabled = hay;
        // Sin cuerpo no hay nada que copiar: esa plantilla es solo el archivo adjunto.
        _btnCopiar.Enabled = hayTexto;

        if (t == null)
        {
            _lblTitulo.Text = "";
            _lblMeta.Text = "";
            _lblDescripcion.Text = "Selecciona una plantilla para verla aquí.";
            _txtVistaPrevia.Text = "";
            return;
        }

        _lblTitulo.Text = t.Title;

        var meta = new List<string>
        {
            $"{TemplateService.IconoTipo(t.Kind)} {TemplateService.EtiquetaTipo(t.Kind)}",
            $"{t.UsageCount} uso(s)"
        };
        if (!string.IsNullOrWhiteSpace(t.Tags)) meta.Add(t.Tags!);
        if (t.FileBytes is { Length: > 0 }) meta.Add($"📎 {t.FileName} ({t.FileBytes.Length / 1024.0:N0} KB)");
        if (t.IsArchived) meta.Add("🗄 archivada");
        var marcadores = TemplateService.Marcadores(t.Body);
        if (marcadores.Count > 0) meta.Add($"{marcadores.Count} marcador(es)");
        _lblMeta.Text = string.Join("   ·   ", meta);

        _lblDescripcion.Text = string.IsNullOrWhiteSpace(t.Description) ? "" : t.Description;
        _txtVistaPrevia.Text = t.Body.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        _txtVistaPrevia.SelectionStart = 0;
        _txtVistaPrevia.SelectionLength = 0;
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }

    // ── Acciones ─────────────────────────────────────────────────────────────────

    private void Nueva()
    {
        // Si hay un tipo filtrado, la nueva plantilla nace ya de ese tipo.
        TemplateKind? tipo = _cbxTipo.SelectedIndex > 0 ? _tipos[_cbxTipo.SelectedIndex - 1] : null;
        using var frm = new TemplateDetailForm(tipoInicial: tipo);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        Ejecutar(() =>
        {
            var (ok, mensaje, _) = _templates.Crear(frm.Result);
            LoadData();
            if (!ok) Avisar(mensaje);
            else _lblEstado.Text = mensaje;
        });
    }

    private void Editar()
    {
        if (Seleccionada() is not { } t) return;
        using var frm = new TemplateDetailForm(t);
        if (frm.ShowDialog(FindForm()) != DialogResult.OK) return;

        Ejecutar(() =>
        {
            var (ok, mensaje) = _templates.Actualizar(t.Id, frm.Result);
            LoadData();
            if (!ok) Avisar(mensaje);
            else _lblEstado.Text = mensaje;
        });
    }

    private void Duplicar()
    {
        if (Seleccionada() is not { } t) return;
        Ejecutar(() =>
        {
            var (ok, mensaje, _) = _templates.Duplicar(t.Id);
            LoadData();
            if (!ok) Avisar(mensaje);
            else _lblEstado.Text = mensaje;
        });
    }

    private void Archivar()
    {
        if (Seleccionada() is not { } t) return;
        Ejecutar(() =>
        {
            var (ok, mensaje) = _templates.Archivar(t.Id, !t.IsArchived);
            LoadData();
            if (!ok) Avisar(mensaje);
            else _lblEstado.Text = mensaje;
        });
    }

    private void Eliminar()
    {
        if (Seleccionada() is not { } t) return;
        if (MessageBox.Show(
                $"¿Eliminar la plantilla «{t.Title}»?\n\nEsta acción no se puede deshacer. " +
                "Si solo quieres sacarla de la lista, usa 🗄 Archivar.",
                "Eliminar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        Ejecutar(() =>
        {
            var (ok, mensaje) = _templates.Eliminar(t.Id);
            LoadData();
            if (!ok) Avisar(mensaje);
            else _lblEstado.Text = mensaje;
        });
    }

    private void Copiar()
    {
        if (Seleccionada() is not { } t) return;
        if (t.Body.Length == 0)
        {
            Avisar("Esta plantilla es solo un archivo adjunto: usa 💾 Guardar como… para bajarlo.");
            return;
        }
        if (ResolverTexto(t) is not { } texto) return;
        if (texto.Length == 0) { Avisar("El texto quedó vacío; no hay nada que copiar."); return; }

        try
        {
            Clipboard.SetText(texto);
        }
        catch (Exception ex)
        {
            // El portapapeles lo puede tener tomado otra aplicación; no es motivo para tirar nada.
            Avisar($"No se pudo copiar al portapapeles:\n{ex.Message}");
            return;
        }

        Ejecutar(() =>
        {
            _templates.RegistrarUso(t.Id);
            LoadData();
            _lblEstado.Text = $"Copiado al portapapeles: «{t.Title}» ({texto.Length:N0} caracteres).";
        });
    }

    private void GuardarComo()
    {
        if (Seleccionada() is not { } t) return;

        bool esArchivo = t.FileBytes is { Length: > 0 };
        // Con archivo adjunto se baja el archivo tal cual; si además hay cuerpo, se pregunta.
        if (esArchivo && t.Body.Length > 0)
        {
            var r = MessageBox.Show(
                $"Esta plantilla trae el archivo «{t.FileName}» y además contenido de texto.\n\n" +
                "Sí = guardar el archivo adjunto\nNo = guardar el texto",
                "Qué guardar", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;
            esArchivo = r == DialogResult.Yes;
        }

        string texto = "";
        if (!esArchivo)
        {
            if (ResolverTexto(t) is not { } resuelto) return;
            texto = resuelto;
        }

        var nombre = esArchivo ? t.FileName! : TemplateService.NombreArchivoSugerido(t);
        // Un adjunto puede venir sin extensión; entonces no se ofrece un filtro inventado.
        var ext = Path.GetExtension(nombre);
        using var dlg = new SaveFileDialog
        {
            Title = "Guardar plantilla",
            FileName = nombre,
            DefaultExt = ext.TrimStart('.'),
            Filter = ext.Length > 0
                ? $"Archivo {ext} (*{ext})|*{ext}|Todos los archivos (*.*)|*.*"
                : "Todos los archivos (*.*)|*.*",
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        try
        {
            if (esArchivo)
                File.WriteAllBytes(dlg.FileName, t.FileBytes!);
            else
                // Salto de línea de Windows y la codificación que le toca al tipo (un .ps1 sin BOM
                // lo lee Windows PowerShell 5.1 como ANSI y le rompe los acentos).
                File.WriteAllText(dlg.FileName,
                    texto.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                    TemplateService.CodificacionArchivo(t.Kind));
        }
        catch (Exception ex)
        {
            Avisar($"No se pudo guardar el archivo:\n{ex.Message}");
            return;
        }

        Ejecutar(() =>
        {
            _templates.RegistrarUso(t.Id);
            LoadData();
            _lblEstado.Text = TemplateService.EsScript(t.Kind)
                ? $"Guardado en {dlg.FileName}.  Revísalo antes de ejecutarlo: la aplicación no lo corre por ti."
                : $"Guardado en {dlg.FileName}.";
        });
    }

    /// <summary>
    /// El texto final de la plantilla. Si trae marcadores por rellenar, se piden; si el usuario
    /// cancela ese diálogo, devuelve null y la acción no sigue.
    /// </summary>
    private string? ResolverTexto(Template t)
    {
        var marcadores = TemplateService.Marcadores(t.Body);
        if (marcadores.Count == 0) return _templates.Rellenar(t.Body);

        using var frm = new TemplateFillForm(_templates, t, marcadores);
        return frm.ShowDialog(FindForm()) == DialogResult.OK ? frm.Texto : null;
    }

    // ── Utilidades ───────────────────────────────────────────────────────────────

    /// <summary>Corre la acción traduciendo un rechazo de permisos a un aviso, no a un cierre.</summary>
    private void Ejecutar(Action accion)
    {
        try { accion(); }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void Avisar(string mensaje) =>
        MessageBox.Show(mensaje, "No se pudo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
