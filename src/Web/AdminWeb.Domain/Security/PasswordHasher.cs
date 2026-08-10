namespace AdminWeb.Domain.Security;

/// <summary>
/// Hashing de contraseñas. Copiado del escritorio SIN cambiar nada, y el workFactor 12 es
/// intocable: es el que produjo los hashes que ya están en la base, y son esos los que la web tiene
/// que validar para que nadie tenga que cambiar su contraseña por la mudanza.
/// </summary>
public static class PasswordHasher
{
    public static string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public static bool Verify(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(password, hash);
}
