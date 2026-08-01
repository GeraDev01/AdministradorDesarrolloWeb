using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms;

/// <summary>
/// Deja que cada quien elija qué columnas ve en una lista, y se acuerda de ello entre sesiones.
///
/// Nace de las listas de vínculos de tickets: con once o más columnas en modo <c>Fill</c>, los
/// títulos se empalman y no hay forma de leerlos. Ahora se esconden las que no interesan y las que
/// quedan se reparten el ancho.
///
/// Es de uso general: cualquier rejilla del proyecto puede llamarlo con una clave estable.
/// </summary>
public static class GridColumns
{
    /// <summary>
    /// Prepara la rejilla: aplica lo que esa persona guardó y deja el clic derecho sobre el
    /// encabezado abriendo el selector (que es donde la gente lo busca en Windows).
    /// </summary>
    public static void Habilitar(DataGridView grid, string clave)
    {
        Aplicar(grid, clave);
        // Al re-enlazar los datos, una rejilla puede recrear columnas: se vuelve a aplicar.
        grid.DataBindingComplete += (_, _) => Aplicar(grid, clave);
        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) Elegir(grid, clave);
        };
    }

    /// <summary>Botón para la barra de herramientas que abre el mismo selector.</summary>
    public static Button CrearBoton(DataGridView grid, string clave, int ancho = 125)
    {
        var btn = AppTheme.MakeSecondaryButton("🧱 Columnas", ancho);
        btn.Click += (_, _) => Elegir(grid, clave);
        return btn;
    }

    /// <summary>Abre el selector y guarda el resultado para la próxima vez.</summary>
    public static void Elegir(DataGridView grid, string clave)
    {
        if (grid.Columns.Count == 0) return;

        using var frm = new ColumnPickerForm(grid);
        if (frm.ShowDialog(grid.FindForm()) != DialogResult.OK) return;

        foreach (DataGridViewColumn c in grid.Columns)
            c.Visible = frm.Visibles.Contains(c.Name);

        GridLayoutConfig.Usuario.Guardar(clave, Ocultas(grid));
    }

    private static void Aplicar(DataGridView grid, string clave)
    {
        var ocultas = GridLayoutConfig.Usuario.Ocultas(clave);
        if (ocultas.Count == 0) return;   // nada guardado: se respeta el diseño de la pantalla

        var esconder = new HashSet<string>(ocultas, StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewColumn c in grid.Columns)
            c.Visible = !esconder.Contains(c.Name);

        // Red de seguridad por si el archivo se editó a mano: una rejilla sin columnas parece rota
        // y desde ahí no hay clic derecho sobre ningún encabezado con el que volver.
        if (!grid.Columns.Cast<DataGridViewColumn>().Any(c => c.Visible) && grid.Columns.Count > 0)
            grid.Columns[0].Visible = true;
    }

    private static IEnumerable<string> Ocultas(DataGridView grid) =>
        grid.Columns.Cast<DataGridViewColumn>().Where(c => !c.Visible).Select(c => c.Name);
}
