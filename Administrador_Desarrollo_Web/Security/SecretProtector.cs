using System.Security.Cryptography;
using System.Text;

namespace Administrador_Desarrollo_Web.Security;

/// <summary>
/// Cifrado atado a la cuenta de Windows que lo escribió (DPAPI, ámbito de usuario).
///
/// ÚSALO SOLO PARA ARCHIVOS LOCALES DEL EQUIPO —<c>dbprovider.json</c>,
/// <c>devops-personal.json</c>—, donde que nadie más pueda descifrarlos es justo el objetivo.
///
/// NO lo uses para nada que se guarde en la BASE COMPARTIDA: ahí el aislamiento se vuelve un
/// defecto, porque la PC que guardó el valor es la única que puede leerlo. Para eso está
/// <see cref="SharedSecretProtector"/>.
/// </summary>
public static class SecretProtector
{
    public static string Protect(string plainText)
    {
        var data = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string cipherText)
    {
        var data = Convert.FromBase64String(cipherText);
        var decrypted = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    public static bool TryUnprotect(string cipherText, out string plainText)
    {
        try
        {
            plainText = Unprotect(cipherText);
            return true;
        }
        catch
        {
            plainText = "";
            return false;
        }
    }
}
