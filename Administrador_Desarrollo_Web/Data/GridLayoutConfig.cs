using System.Text.Json;

namespace Administrador_Desarrollo_Web.Data;

/// <summary>
/// Qué columnas escondió cada persona en cada lista, guardado EN SU EQUIPO
/// (<c>%AppData%\AdministradorDesarrolloWeb\columnas.json</c>).
///
/// No va en la base compartida a propósito: es una preferencia de pantalla, y el ancho de monitor y
/// las columnas que a cada quien le importan no son los mismos. Guardarlo en <c>AppSettings</c>
/// habría hecho que quien esconde una columna se la esconda a todo el equipo. Además, el login
/// restringido con el que corren los desarrolladores no puede escribir en esa tabla.
///
/// Se guarda lo OCULTO, no lo visible: así, cuando una versión nueva agrega una columna, aparece
/// —que es lo que se espera de algo nuevo— en vez de quedar invisible para quien ya había
/// configurado esa lista.
/// </summary>
public sealed class GridLayoutConfig
{
    private readonly string _ruta;
    private readonly Dictionary<string, string[]> _ocultas = new(StringComparer.OrdinalIgnoreCase);

    public GridLayoutConfig(string ruta)
    {
        _ruta = ruta;
        Cargar();
    }

    public static string RutaPredeterminada => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AdministradorDesarrolloWeb", "columnas.json");

    private static GridLayoutConfig? _usuario;

    /// <summary>La configuración de quien está usando este equipo. Se lee una sola vez.</summary>
    public static GridLayoutConfig Usuario => _usuario ??= new GridLayoutConfig(RutaPredeterminada);

    /// <summary>Columnas ocultas de esa lista. Vacío = se respeta el diseño original de la pantalla.</summary>
    public IReadOnlyList<string> Ocultas(string rejilla) =>
        _ocultas.TryGetValue(rejilla, out var v) ? v : [];

    public void Guardar(string rejilla, IEnumerable<string> ocultas)
    {
        var limpias = ocultas
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (limpias.Length == 0) _ocultas.Remove(rejilla);
        else _ocultas[rejilla] = limpias;

        Persistir();
    }

    private void Cargar()
    {
        try
        {
            if (!File.Exists(_ruta)) return;
            var raw = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(_ruta));
            if (raw == null) return;
            foreach (var (rejilla, columnas) in raw)
                if (columnas is { Length: > 0 }) _ocultas[rejilla] = columnas;
        }
        catch
        {
            // Archivo corrupto o de otra versión: se parte del diseño original de cada pantalla.
            // No vale un aviso — es una preferencia de columnas, no datos de nadie.
            _ocultas.Clear();
        }
    }

    private void Persistir()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_ruta)!);
            File.WriteAllText(_ruta,
                JsonSerializer.Serialize(_ocultas, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Disco lleno o carpeta sin permisos: la elección se aplica igual en esta sesión, solo
            // que no sobrevive al reinicio. Interrumpir con un error por esto sería peor.
        }
    }
}
