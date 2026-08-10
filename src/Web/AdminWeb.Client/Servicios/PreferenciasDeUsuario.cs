using System.Net.Http.Json;
using System.Text.Json;
using AdminWeb.Shared.Dtos;

namespace AdminWeb.Client.Servicios;

/// <summary>
/// Las preferencias de interfaz de quien está usando la aplicación, guardadas en el servidor.
///
/// Se cachean en memoria durante la sesión porque se leen al pintar cada rejilla y no cambian solas:
/// pedirlas al servidor en cada repintado sería un viaje por nada.
/// </summary>
public class PreferenciasDeUsuario(HttpClient http)
{
    private readonly Dictionary<string, string?> _cache = [];

    /// <summary>
    /// Las columnas que esta persona tiene OCULTAS en una rejilla.
    ///
    /// Se guarda lo oculto y no lo visible, y esa decisión viene del escritorio: al guardar lo
    /// visible, una columna nueva quedaba escondida para todo el que ya tuviera preferencias
    /// guardadas, y nadie entendía por qué a unos les aparecía y a otros no.
    /// </summary>
    public async Task<HashSet<string>> ColumnasOcultasAsync(string claveDeRejilla)
    {
        var json = await LeerAsync($"columnas.{claveDeRejilla}");
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
        }
        catch
        {
            // Una preferencia corrupta no puede impedir ver la pantalla: se ignora y se muestran
            // todas las columnas, que es el estado por omisión.
            return [];
        }
    }

    public Task GuardarColumnasOcultasAsync(string claveDeRejilla, IEnumerable<string> ocultas)
    {
        var lista = ocultas.ToHashSet();
        // Sin nada oculto se borra la preferencia en vez de guardar una lista vacía: así la persona
        // vuelve al estado por omisión de verdad y hereda las columnas que se agreguen después.
        return GuardarAsync($"columnas.{claveDeRejilla}", lista.Count == 0 ? null : JsonSerializer.Serialize(lista));
    }

    public async Task<string?> LeerAsync(string clave)
    {
        if (_cache.TryGetValue(clave, out var enCache)) return enCache;

        try
        {
            var dto = await http.GetFromJsonAsync<PreferenciaDto>($"api/preferencias/{Uri.EscapeDataString(clave)}");
            return _cache[clave] = dto?.Json;
        }
        catch
        {
            // Sin preferencias se trabaja igual; no vale la pena molestar a nadie con esto.
            return _cache[clave] = null;
        }
    }

    public async Task GuardarAsync(string clave, string? json)
    {
        _cache[clave] = json;
        try
        {
            await http.PutAsJsonAsync($"api/preferencias/{Uri.EscapeDataString(clave)}",
                new PreferenciaDto(clave, json));
        }
        catch
        {
            // Que no se pueda guardar una preferencia no debe interrumpir lo que la persona estaba
            // haciendo. Queda aplicada en esta sesión aunque no sobreviva a la siguiente.
        }
    }
}
