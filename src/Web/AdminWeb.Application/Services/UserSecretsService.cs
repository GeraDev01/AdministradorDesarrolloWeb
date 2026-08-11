using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Cifra y descifra secretos. La implementación vive en la API (protección de datos de ASP.NET, con
/// el llavero en Blob y cifrado con un certificado); se abstrae aquí para que los servicios no
/// dependan de esa pieza y para poder probarlos sin ella.
/// </summary>
public interface IProtectorDeSecretos
{
    string Proteger(string valorEnClaro);

    /// <summary>Devuelve null si el texto no se puede descifrar (llave rotada, dato corrupto).</summary>
    string? Desproteger(string cifrado);
}

/// <summary>
/// Los secretos personales de cada usuario, guardados cifrados del lado del servidor.
///
/// La regla que gobierna esta clase: <b>el valor en claro nunca sale hacia el navegador</b>. Las
/// pantallas solo preguntan si hay secreto configurado o no —igual que hacía el diálogo del
/// escritorio, que tampoco mostraba nunca el token guardado— y las operaciones que lo necesitan
/// (comentar un ticket, reportar tiempo) las ejecuta la API en nombre del usuario.
/// </summary>
public class UserSecretsService(AppDbContext db, ICurrentUser currentUser, IProtectorDeSecretos protector, AuditService audit)
{
    /// <summary>
    /// El secreto en claro de una persona, para que la API actúe en su nombre. Es el único método
    /// que devuelve el valor descifrado y por eso NO debe exponerse en ningún endpoint.
    /// </summary>
    public async Task<string?> ObtenerEnClaroAsync(int userId, string proposito, CancellationToken ct = default)
    {
        var fila = await db.UserSecrets.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Proposito == proposito, ct);

        return fila == null ? null : protector.Desproteger(fila.CipherText);
    }

    /// <summary>El secreto del usuario de la petición.</summary>
    public Task<string?> ObtenerMioEnClaroAsync(string proposito, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        return currentUser.UserId is int id
            ? ObtenerEnClaroAsync(id, proposito, ct)
            : Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Si hay secreto configurado Y SE PUEDE LEER. Es lo único que se le puede contestar a una
    /// pantalla sobre esto.
    ///
    /// <para><b>Antes solo comprobaba que la FILA existiera, y eso era una mentira útil de detectar
    /// tarde.</b> Si el llavero de Data Protection se pierde —cosa que pasaba en cada reinicio del
    /// contenedor antes de persistirlo— la fila sigue ahí y el descifrado devuelve null. La pantalla
    /// decía «Listo: se usará tu token personal» mientras cada petición caía en silencio al PAT de la
    /// instalación, así que los comentarios en DevOps quedaban firmados por la cuenta compartida y
    /// nadie entendía por qué.</para>
    ///
    /// <para>Ahora se intenta descifrar de verdad. Cuesta una desprotección por consulta, que es
    /// trabajo en memoria sobre unos pocos bytes, y a cambio la respuesta significa lo que dice.</para>
    /// </summary>
    public async Task<bool> TengoConfiguradoAsync(string proposito, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int id) return false;

        var cifrado = await db.UserSecrets.AsNoTracking()
            .Where(s => s.UserId == id && s.Proposito == proposito)
            .Select(s => s.CipherText)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrEmpty(cifrado)) return false;

        // Desproteger ya traga su propia excepción y devuelve null cuando la llave no sirve.
        return protector.Desproteger(cifrado) != null;
    }

    /// <summary>
    /// Guarda o reemplaza el secreto propio. Sin parámetro de usuario a propósito: cada quien
    /// configura el suyo, y un id por parámetro basta que un llamador futuro lo pase mal para que
    /// alguien escriba el secreto de otra persona.
    /// </summary>
    public async Task<(bool ok, string mensaje)> GuardarMioAsync(
        string proposito, string valorEnClaro, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.");

        valorEnClaro = (valorEnClaro ?? "").Trim();
        if (valorEnClaro.Length == 0) return (false, "El valor está vacío.");

        var fila = await db.UserSecrets
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Proposito == proposito, ct);

        if (fila == null)
        {
            fila = new UserSecret { UserId = userId, Proposito = proposito };
            db.UserSecrets.Add(fila);
        }

        fila.CipherText = protector.Proteger(valorEnClaro);
        fila.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // A la bitácora va el hecho, jamás el valor.
        await audit.RecordAsync(Shared.Enums.AuditAction.Update, "UserSecret", fila.Id.ToString(),
            $"Secreto «{proposito}» configurado", ct);

        return (true, "Guardado. No vuelve a mostrarse.");
    }

    /// <summary>Borra el secreto propio.</summary>
    public async Task<(bool ok, string mensaje)> BorrarMioAsync(string proposito, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.");

        var fila = await db.UserSecrets
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Proposito == proposito, ct);
        if (fila == null) return (false, "No tenías nada configurado.");

        db.UserSecrets.Remove(fila);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(Shared.Enums.AuditAction.Delete, "UserSecret", fila.Id.ToString(),
            $"Secreto «{proposito}» borrado", ct);

        return (true, "Borrado.");
    }
}
