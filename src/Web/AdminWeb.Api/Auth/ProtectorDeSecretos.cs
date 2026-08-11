using AdminWeb.Application.Services;
using Microsoft.AspNetCore.DataProtection;

namespace AdminWeb.Api.Auth;

/// <summary>
/// Cifrado de secretos con la protección de datos de ASP.NET Core.
///
/// Sustituye al DPAPI del escritorio, que ataba cada secreto a la cuenta de Windows de la máquina
/// donde se capturó — y por eso el token se perdía al cambiar de computadora. Aquí las llaves las
/// gestiona el servidor —en Azure, un llavero en Blob cifrado con un certificado; ver
/// <c>Arranque/Llavero.cs</c>—, así que el secreto sigue a la persona.
///
/// El propósito va en el nombre del protector: eso hace que un texto cifrado para un uso no se pueda
/// descifrar como si fuera de otro, aunque alguien lograra moverlo de fila.
/// </summary>
public class ProtectorDeSecretos(IDataProtectionProvider proveedor) : IProtectorDeSecretos
{
    private const string Proposito = "AdminWeb.SecretosDeUsuario.v1";

    private IDataProtector Protector => proveedor.CreateProtector(Proposito);

    public string Proteger(string valorEnClaro) => Protector.Protect(valorEnClaro);

    public string? Desproteger(string cifrado)
    {
        // Un secreto que ya no se puede descifrar (llave rotada, dato manipulado) NO debe tumbar la
        // petición: se trata como «no configurado» y la persona lo vuelve a capturar.
        try { return Protector.Unprotect(cifrado); }
        catch { return null; }
    }
}
