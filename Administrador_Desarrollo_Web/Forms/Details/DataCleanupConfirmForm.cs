using Administrador_Desarrollo_Web.Services;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>
/// El último paso antes de borrar: la lista exacta de lo que se va, con cuántos registros tiene
/// cada apartado, y una palabra que hay que escribir.
///
/// Por qué escribir «BORRAR» y no un «¿seguro?»: esta operación no tiene deshacer ni papelera. Un
/// diálogo de sí/no se contesta con el mismo clic con el que se llegó hasta él —y aquí el clic de
/// más no cuesta una fila, cuesta el apartado completo—. Teclear una palabra obliga a leer el
/// recuento que está justo encima.
/// </summary>
public class DataCleanupConfirmForm : ResponsiveForm
{
    private readonly IReadOnlyList<(AreaLimpieza area, int cuantos)> _seleccion;
    private TextBox _txtFrase = null!;
    private Button _btnBorrar = null!;

    public DataCleanupConfirmForm(IReadOnlyList<(AreaLimpieza area, int cuantos)> seleccion)
    {
        _seleccion = seleccion;
        BuildUI();
    }

    private void BuildUI()
    {
        Text = "Confirmar limpieza";
        Size = new Size(660, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = AppTheme.ContentBg; Font = AppTheme.DefaultFont;
        if (AppTheme.AppIcon != null) Icon = AppTheme.AppIcon;

        int total = _seleccion.Sum(s => Math.Max(0, s.cuantos));

        Controls.Add(new Label
        {
            Text = $"Se van a borrar {total} registro(s) de {_seleccion.Count} apartado(s).",
            Location = new Point(18, 16), AutoSize = false, Size = new Size(610, 22),
            Font = AppTheme.HeaderFont, ForeColor = AppTheme.Danger
        });

        Controls.Add(new Label
        {
            Text = "Esto no se puede deshacer y no hay papelera. Lo arrastrado por cada apartado va incluido.",
            Location = new Point(18, 40), AutoSize = false, Size = new Size(610, 18),
            Font = AppTheme.SmallFont, ForeColor = AppTheme.TextSecondary
        });

        // El detalle en una lista y no en un MessageBox: con quince apartados marcados, el texto de
        // un MessageBox se vuelve un muro que nadie lee.
        var lista = AppTheme.MakeGrid();
        lista.Dock = DockStyle.None;   // MakeGrid la deja rellenando; aquí va colocada, antes del tamaño
        lista.Location = new Point(18, 68);
        lista.Size = new Size(610, 300);
        lista.AutoGenerateColumns = false;
        lista.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Apartado", Name = "Area", FillWeight = 34 });
        lista.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Registros", Name = "Num", FillWeight = 14 });
        lista.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Además se lleva", Name = "Mas", FillWeight = 52 });
        foreach (DataGridViewColumn col in lista.Columns) col.SortMode = DataGridViewColumnSortMode.NotSortable;

        foreach (var (area, cuantos) in _seleccion)
        {
            int i = lista.Rows.Add(area.Nombre, cuantos < 0 ? "?" : cuantos.ToString(), area.Arrastra);
            if (area.Advertencia != null)
            {
                lista.Rows[i].Cells["Area"].Style.ForeColor = AppTheme.Danger;
                lista.Rows[i].Cells["Area"].Style.Font = AppTheme.BoldFont;
                lista.Rows[i].Cells["Mas"].Value = $"{area.Arrastra}  —  ⚠ {area.Advertencia}";
            }
        }
        Controls.Add(lista);

        Controls.Add(new Label
        {
            Text = $"Escribe {DataCleanupService.FraseConfirmacion} para confirmar:",
            Location = new Point(18, 382), AutoSize = true, Font = AppTheme.BoldFont
        });

        _txtFrase = new TextBox { Location = new Point(18, 404), Width = 200, CharacterCasing = CharacterCasing.Upper };
        _txtFrase.TextChanged += (_, _) => PintarBoton();
        Controls.Add(_txtFrase);

        _btnBorrar = AppTheme.MakeDangerButton("🧹 Borrar", 160);
        _btnBorrar.Location = new Point(348, 448);
        _btnBorrar.Click += (_, _) =>
        {
            if (!FraseCorrecta()) { PintarBoton(); _txtFrase.Focus(); return; }
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancelar = AppTheme.MakeSecondaryButton("Cancelar", 110);
        cancelar.Location = new Point(518, 448);
        cancelar.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange([_btnBorrar, cancelar]);
        // Sin AcceptButton: borrar con Enter es el reflejo que este diálogo viene a interrumpir.
        CancelButton = cancelar;
        PintarBoton();
    }

    /// <summary>
    /// El cuadro ya sube a mayúsculas, así que la comparación exacta no castiga a nadie por escribir
    /// en minúsculas: lo único que rechaza es otra palabra.
    /// </summary>
    private bool FraseCorrecta() => _txtFrase.Text.Trim() == DataCleanupService.FraseConfirmacion;

    private void PintarBoton() => _btnBorrar.Enabled = FraseCorrecta();
}
