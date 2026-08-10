using AdminWeb.Domain.Avisos;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Los avisos que llegan al navegador aunque la pestaña esté cerrada.
///
/// <para><b>Qué sustituye.</b> En el escritorio, la aplicación seguía viva escondida en la bandeja
/// del sistema y podía sacar un globo cuando hiciera falta. Una pestaña cerrada no puede hacer nada,
/// y ese es el único hueco real que deja la mudanza a la web. Esto lo tapa.</para>
///
/// <para><b>No sustituye al aviso in-app.</b> El aviso se guarda siempre en la base
/// (<see cref="NotificationService"/>) y ahí sigue cuando la persona vuelva; el push es un empujón
/// para que se entere antes. Si la entrega falla, o si nadie configuró las llaves, no se pierde
/// nada — por eso ningún fallo de aquí puede tumbar la operación que provocó el aviso.</para>
/// </summary>
public class AvisosPushService(
    AppDbContext db, ICurrentUser currentUser, IEnvioDeAvisosPush envio)
{
    /// <summary>¿Se puede ofrecer push en esta instalación?</summary>
    public bool Disponible => envio.Configurado;

    /// <summary>La llave que el navegador necesita para suscribirse. No es secreta.</summary>
    public string? LlavePublica => envio.LlavePublica;

    /// <summary>¿Tiene ESTE navegador el permiso ya dado?</summary>
    public async Task<bool> EstaSuscritoAsync(string endpoint, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        return await db.PushSubscriptions.AsNoTracking()
            .AnyAsync(s => s.Endpoint == endpoint && s.UserId == currentUser.UserId, ct);
    }

    /// <summary>
    /// Guarda o actualiza la suscripción de este navegador.
    ///
    /// Se busca por DIRECCIÓN DE ENTREGA y no por usuario: la dirección identifica al navegador, y
    /// el mismo navegador que vuelve a suscribirse trae la misma. Si no, cada renovación —que el
    /// navegador hace por su cuenta— dejaría una fila muerta y el aviso llegaría por duplicado.
    /// </summary>
    public async Task<(bool ok, string mensaje)> SuscribirAsync(
        string endpoint, string p256dh, string auth, string? descripcion, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);
        if (currentUser.UserId is not int userId) return (false, "No hay una sesión válida.");

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(p256dh) || string.IsNullOrWhiteSpace(auth))
            return (false, "El navegador no entregó una suscripción completa.");

        var existente = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (existente == null)
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                UserId = userId,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                Descripcion = Recortar(descripcion),
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            // La misma dirección puede acabar en manos de otra cuenta: un equipo compartido donde
            // alguien cierra sesión y entra otra persona. Se reasigna en vez de dejarla apuntando a
            // quien ya no lo usa, que recibiría avisos que no le tocan.
            existente.UserId = userId;
            existente.P256dh = p256dh;
            existente.Auth = auth;
            existente.Descripcion = Recortar(descripcion);
        }

        await db.SaveChangesAsync(ct);
        return (true, "Este navegador recibirá los avisos aunque cierres la pestaña.");
    }

    /// <summary>Retira el permiso de este navegador.</summary>
    public async Task<(bool ok, string mensaje)> CancelarAsync(string endpoint, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(currentUser);

        // Por dirección Y usuario: sin lo segundo, cualquiera con sesión podría desuscribir el
        // navegador de otra persona sabiendo su dirección de entrega.
        int borradas = await db.PushSubscriptions
            .Where(s => s.Endpoint == endpoint && s.UserId == currentUser.UserId)
            .ExecuteDeleteAsync(ct);

        return (true, borradas > 0
            ? "Este navegador dejará de recibir avisos."
            : "Este navegador no tenía avisos activados.");
    }

    /// <summary>
    /// Entrega un aviso a todos los navegadores de una persona.
    ///
    /// Lo llama quien acaba de crear un aviso in-app. <b>Nunca lanza</b>: el aviso ya está guardado
    /// y lo que se está haciendo aquí es el extra. Las suscripciones que el servicio de entrega
    /// rechaza por muertas se borran sobre la marcha — es la única forma de saberlo.
    /// </summary>
    public async Task EmpujarAsync(int userId, string titulo, string cuerpo, string? url,
        CancellationToken ct = default)
    {
        if (!envio.Configurado) return;

        var destinos = await db.PushSubscriptions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);
        if (destinos.Count == 0) return;

        var muertas = new List<string>();
        foreach (var destino in destinos)
        {
            bool viva = await envio.EnviarAsync(
                new AvisoPush(destino.Endpoint, destino.P256dh, destino.Auth, titulo, cuerpo, url), ct);

            if (viva) continue;
            muertas.Add(destino.Endpoint);
        }

        if (muertas.Count > 0)
            await db.PushSubscriptions.Where(s => muertas.Contains(s.Endpoint)).ExecuteDeleteAsync(ct);
    }

    private static string? Recortar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null
        : texto.Length <= 120 ? texto.Trim() : texto.Trim()[..120];
}
