using System.Globalization;
using System.Text;
using AdminWeb.Domain.Entities;

namespace AdminWeb.Application.Services;

/// <summary>
/// Decide si un work item de Azure DevOps «es de» un desarrollador.
///
/// <para>Conserva el nombre del escritorio para poder cotejarlo con el original durante el corte.
/// El código es el mismo: es cálculo puro y no había nada que adaptar.</para>
///
/// DevOps identifica al asignado con un nombre para mostrar («Daniel Lopez») y, cuando está
/// disponible, un <c>uniqueName</c> que es su correo/UPN. El correo es la llave FIABLE: es estable y
/// no depende de acentos ni de segundos nombres. En los datos reales el nombre para mostrar de
/// DevOps viene SIN acentos («Daniel Lopez») y a veces recortado («Jesus Canul» en vez de «Jesus
/// Abraham Canul»), así que empatar por nombre es frágil y solo se usa como respaldo, normalizado.
/// </summary>
public static class DevOpsIdentityMatcher
{
    /// <summary>true si el ticket, con el asignado que traiga, corresponde al desarrollador dado.</summary>
    public static bool Corresponde(string? asignadoNombre, string? asignadoCorreo, Developer dev)
    {
        // 1) Preferente: por correo exacto (ignorando mayúsculas). Es lo que llena la sincronización.
        //    Si AMBOS traen correo, esa comparación es la definitiva: correos distintos = personas
        //    distintas, y no se cae al respaldo por nombre.
        var correoDev = Normalizar(dev.Email);
        var correoTicket = Normalizar(asignadoCorreo);
        if (correoDev.Length > 0 && correoTicket.Length > 0)
            return correoDev == correoTicket;

        // 2) Respaldo: por nombre, sin acentos ni mayúsculas y con espacios colapsados. Sirve para
        //    los tickets viejos sincronizados antes de que existiera la columna del correo, y para
        //    los desarrolladores que no tienen correo en su ficha.
        var nombreDev = Plegar(dev.FullName);
        var nombreTicket = Plegar(asignadoNombre);
        return nombreDev.Length > 0 && nombreDev == nombreTicket;
    }

    /// <summary>Primer desarrollador de la lista que corresponda al asignado del ticket, o null.</summary>
    public static Developer? Buscar(
        string? asignadoNombre, string? asignadoCorreo, IEnumerable<Developer> desarrolladores)
    {
        foreach (var dev in desarrolladores)
            if (Corresponde(asignadoNombre, asignadoCorreo, dev))
                return dev;

        return null;
    }

    private static string Normalizar(string? s) => (s ?? "").Trim().ToLowerInvariant();

    /// <summary>Minúsculas, sin acentos y con espacios colapsados, para comparar nombres.</summary>
    private static string Plegar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";

        var descompuesto = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);

        foreach (var c in descompuesto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
