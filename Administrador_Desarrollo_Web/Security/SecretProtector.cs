using System.Security.Cryptography;
using System.Text;

namespace Administrador_Desarrollo_Web.Security;

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
