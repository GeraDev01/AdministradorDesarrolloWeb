using System.Globalization;
using System.Text;
using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Decide si un work item de Azure DevOps «es de» un desarrollador.
///
/// DevOps identifica al asignado con un nombre para mostrar («Daniel Lopez») y, cuando está
/// disponible, un <c>uniqueName</c> que es su correo/UPN. El correo es la llave FIABLE: es estable
/// y no depende de acentos ni de segundos nombres. En los datos reales el nombre para mostrar de
/// DevOps viene SIN acentos («Daniel Lopez») y a veces recortado («Jesus Canul» en vez de «Jesus
/// Abraham Canul»), así que empatar por nombre es frágil y solo se usa como respaldo, normalizado.
/// </summary>
public static class DevOpsIdentityMatcher
{
    /// <summary>true si el ticket, con el asignado que traiga, corresponde al desarrollador dado.</summary>
    public static bool Matches(string? assignedToDisplay, string? assignedToEmail, Developer dev)
    {
        // 1) Preferente: por correo exacto (ignorando mayúsculas). Es lo que llena el sync nuevo.
        //    Si AMBOS traen correo, esa comparación es la definitiva: correos distintos = personas
        //    distintas, y no se cae al respaldo por nombre.
        var devEmail = Norm(dev.Email);
        var tkEmail  = Norm(assignedToEmail);
        if (devEmail.Length > 0 && tkEmail.Length > 0)
            return devEmail == tkEmail;

        // 2) Respaldo: por nombre, sin acentos ni mayúsculas y con espacios colapsados. Sirve antes
        //    de re-sincronizar (tickets viejos sin correo) o si el desarrollador no tiene correo.
        var devName = Fold(dev.FullName);
        var tkName  = Fold(assignedToDisplay);
        return devName.Length > 0 && devName == tkName;
    }

    /// <summary>Primer desarrollador de la lista que corresponda al asignado del ticket, o null.</summary>
    public static Developer? Find(string? assignedToDisplay, string? assignedToEmail, IEnumerable<Developer> devs)
    {
        foreach (var d in devs)
            if (Matches(assignedToDisplay, assignedToEmail, d))
                return d;
        return null;
    }

    private static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();

    /// <summary>Minúsculas, sin acentos y con espacios colapsados, para comparar nombres.</summary>
    private static string Fold(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
