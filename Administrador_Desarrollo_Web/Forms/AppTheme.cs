namespace Administrador_Desarrollo_Web.Forms;

public static class AppTheme
{
    public static readonly Color SidebarBg      = Color.FromArgb(26, 43, 74);
    public static readonly Color SidebarHover   = Color.FromArgb(44, 72, 117);
    public static readonly Color SidebarActive  = Color.FromArgb(37, 99, 235);
    public static readonly Color SidebarText    = Color.White;
    public static readonly Color ContentBg      = Color.FromArgb(248, 250, 252);
    public static readonly Color HeaderBg       = Color.FromArgb(37, 99, 235);
    public static readonly Color HeaderText     = Color.White;
    public static readonly Color CardBg         = Color.White;
    public static readonly Color GridHeader     = Color.FromArgb(37, 99, 235);
    public static readonly Color GridHeaderText = Color.White;
    public static readonly Color GridAlt        = Color.FromArgb(240, 245, 255);
    public static readonly Color Success        = Color.FromArgb(34, 197, 94);
    public static readonly Color Warning        = Color.FromArgb(245, 158, 11);
    public static readonly Color Danger         = Color.FromArgb(239, 68, 68);
    public static readonly Color Border         = Color.FromArgb(203, 213, 225);
    public static readonly Color TextPrimary    = Color.FromArgb(15, 23, 42);
    public static readonly Color TextSecondary  = Color.FromArgb(100, 116, 139);

    public static readonly Font DefaultFont  = new("Segoe UI", 9.5f);
    public static readonly Font BoldFont     = new("Segoe UI Semibold", 9.5f);
    public static readonly Font HeaderFont   = new("Segoe UI Semibold", 12f);
    public static readonly Font TitleFont    = new("Segoe UI", 22f, FontStyle.Bold);
    public static readonly Font NavFont      = new("Segoe UI", 10f);
    public static readonly Font NavFontBold  = new("Segoe UI Semibold", 10f);
    public static readonly Font SmallFont    = new("Segoe UI", 8.5f);
    public static readonly Font KpiValueFont = new("Segoe UI", 16f, FontStyle.Bold);
    public static readonly Font TimerFont    = new("Segoe UI", 20f, FontStyle.Bold);
    public static readonly Font MonoFont     = new("Consolas", 11f);

    private static Icon? _appIcon;
    private static bool _appIconLoaded;

    /// <summary>Ícono del ejecutable, para que las ventanas y la barra de tareas lo compartan.</summary>
    public static Icon? AppIcon
    {
        get
        {
            if (_appIconLoaded) return _appIcon;
            _appIconLoaded = true;
            try { _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { _appIcon = null; }   // sin ícono se usa el predeterminado; no vale tirar la app por esto
            return _appIcon;
        }
    }

    public static Button MakePrimaryButton(string text, int width = 120, int height = 34)
    {
        return new Button
        {
            Text = text,
            Width = width,
            Height = height,
            FlatStyle = FlatStyle.Flat,
            BackColor = SidebarActive,
            ForeColor = Color.White,
            Font = BoldFont,
            Cursor = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 }
        };
    }

    public static Button MakeDangerButton(string text, int width = 120, int height = 34)
    {
        return new Button
        {
            Text = text,
            Width = width,
            Height = height,
            FlatStyle = FlatStyle.Flat,
            BackColor = Danger,
            ForeColor = Color.White,
            Font = BoldFont,
            Cursor = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 }
        };
    }

    public static Button MakeSecondaryButton(string text, int width = 120, int height = 34)
    {
        return new Button
        {
            Text = text,
            Width = width,
            Height = height,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(226, 232, 240),
            ForeColor = TextPrimary,
            Font = NavFont,
            Cursor = Cursors.Hand,
            FlatAppearance = { BorderSize = 0 }
        };
    }

    public static DataGridView MakeGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Border,
            BackgroundColor = CardBg,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Font = DefaultFont,
                BackColor = CardBg,
                ForeColor = TextPrimary,
                SelectionBackColor = Color.FromArgb(219, 234, 254),
                SelectionForeColor = TextPrimary,
                Padding = new Padding(4, 2, 4, 2)
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = GridHeader,
                ForeColor = GridHeaderText,
                Font = BoldFont,
                Alignment = DataGridViewContentAlignment.MiddleLeft,   // centrado vertical: el texto no se corta arriba/abajo
                WrapMode = DataGridViewTriState.False,
                Padding = new Padding(6, 8, 6, 8)
            },
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = GridAlt,
                SelectionBackColor = Color.FromArgb(219, 234, 254),
                SelectionForeColor = TextPrimary
            },
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 40,
            RowTemplate = { Height = 32 }
        };
        // El tema de Windows en algunos equipos no pinta el texto del encabezado (queda solo la
        // franja). Lo dibujamos nosotros (fondo + texto) para garantizar que SIEMPRE se vea.
        grid.CellPainting += PaintColumnHeader;
        return grid;
    }

    private static readonly SolidBrush HeaderBrush = new(GridHeader);
    private static readonly SolidBrush HeaderTextBrush = new(GridHeaderText);
    private static readonly Pen HeaderLinePen = new(Color.FromArgb(29, 78, 216));
    private static readonly StringFormat HeaderFormat = new()
    {
        LineAlignment = StringAlignment.Center,   // vertical centrado
        Alignment = StringAlignment.Near,         // horizontal izquierda
        FormatFlags = StringFormatFlags.NoWrap,
        Trimming = StringTrimming.EllipsisCharacter
    };

    private static void PaintColumnHeader(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex != -1 || e.ColumnIndex < 0 || sender is not DataGridView grid) return;   // solo encabezados de columna
        e.Graphics.FillRectangle(HeaderBrush, e.CellBounds);
        e.Graphics.DrawLine(HeaderLinePen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
        // La fuente confiable del texto es HeaderText de la columna (e.FormattedValue puede venir vacío).
        // Se dibuja con GDI+ (DrawString), igual que FillRectangle, para que siempre pinte.
        var text = grid.Columns[e.ColumnIndex].HeaderText;
        var rect = e.CellBounds; rect.Inflate(-8, 0);
        e.Graphics.DrawString(text, BoldFont, HeaderTextBrush, (RectangleF)rect, HeaderFormat);
        e.Handled = true;
    }

    public static Color StatusColor(Models.RequirementStatus status) => status switch
    {
        Models.RequirementStatus.PorEstimar   => Color.FromArgb(148, 163, 184),
        Models.RequirementStatus.Estimado     => Color.FromArgb(96, 165, 250),
        Models.RequirementStatus.EnDesarrollo => Color.FromArgb(251, 191, 36),
        Models.RequirementStatus.EnPruebas    => Color.FromArgb(167, 139, 250),
        Models.RequirementStatus.PorEntregar  => Color.FromArgb(251, 146, 60),
        Models.RequirementStatus.Entregado    => Color.FromArgb(34, 197, 94),
        Models.RequirementStatus.Cancelado    => Color.FromArgb(239, 68, 68),
        _                                     => Color.Gray
    };
}
