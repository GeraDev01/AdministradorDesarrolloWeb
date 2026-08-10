using System.Net;
using System.Text.Json;
using AdminWeb.Domain.Avisos;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;

namespace AdminWeb.Infrastructure.Avisos;

/// <summary>
/// Entrega los avisos push firmándolos con las llaves VAPID de la aplicación.
///
/// <para><b>Las llaves se generan UNA vez y no cambian.</b> El navegador se suscribe atado a la
/// llave pública: si se regenera el par, todas las suscripciones guardadas dejan de valer de golpe y
/// nadie vuelve a recibir un aviso hasta que abra la aplicación y acepte otra vez. Van en la
/// configuración del servidor (y en Azure, en Key Vault), no en el código.</para>
///
/// <para>Si no están configuradas, esto NO revienta: se comporta como «no hay push». Es lo correcto
/// —los avisos in-app siguen funcionando y son los que de verdad importan— y evita que una
/// instalación sin llaves no pueda ni arrancar.</para>
/// </summary>
public class EnvioDeAvisosPush : IEnvioDeAvisosPush
{
    private readonly PushServiceClient? _cliente;
    private readonly ILogger<EnvioDeAvisosPush> _log;

    public EnvioDeAvisosPush(
        HttpClient http, OpcionesDeAvisosPush opciones, ILogger<EnvioDeAvisosPush> log)
    {
        _log = log;
        LlavePublica = opciones.LlavePublica;

        if (string.IsNullOrWhiteSpace(opciones.LlavePublica)
            || string.IsNullOrWhiteSpace(opciones.LlavePrivada)
            || string.IsNullOrWhiteSpace(opciones.Sujeto))
        {
            _log.LogInformation(
                "Avisos push desactivados: faltan las llaves VAPID. Los avisos dentro de la " +
                "aplicación siguen funcionando.");
            return;
        }

        _cliente = new PushServiceClient(http)
        {
            DefaultAuthentication = new VapidAuthentication(opciones.LlavePublica, opciones.LlavePrivada)
            {
                Subject = opciones.Sujeto
            }
        };
    }

    public bool Configurado => _cliente != null;

    public string? LlavePublica { get; }

    public async Task<bool> EnviarAsync(AvisoPush aviso, CancellationToken ct = default)
    {
        if (_cliente == null) return true;   // sin push configurado no hay suscripción que invalidar

        var destino = new Lib.Net.Http.WebPush.PushSubscription
        {
            Endpoint = aviso.Endpoint
        };
        destino.SetKey(PushEncryptionKeyName.P256DH, aviso.P256dh);
        destino.SetKey(PushEncryptionKeyName.Auth, aviso.Auth);

        // El cuerpo va como JSON porque lo lee el service worker del navegador, que es quien decide
        // cómo pintar el aviso. Mandar texto suelto obligaría a acordar un formato a mano.
        var contenido = JsonSerializer.Serialize(new
        {
            titulo = aviso.Titulo,
            cuerpo = aviso.Cuerpo,
            url = aviso.Url
        });

        try
        {
            await _cliente.RequestPushMessageDeliveryAsync(destino, new PushMessage(contenido), ct);
            return true;
        }
        catch (PushServiceClientException ex) when (
            ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // La suscripción murió: el navegador se desinstaló o se limpió el sitio. No es un error
            // que haya que registrar como tal —pasa constantemente— y quien llama debe borrarla.
            return false;
        }
        catch (Exception ex)
        {
            // Un fallo de entrega NO puede tumbar lo que lo provocó. El aviso in-app ya se guardó y
            // es el que cuenta; esto era el extra.
            _log.LogWarning(ex, "No se pudo entregar un aviso push.");
            return true;
        }
    }
}

/// <summary>
/// Las llaves VAPID. Se generan una vez —hay herramientas de línea de comandos para ello— y viven en
/// la configuración del servidor: <c>AdminWeb:Push:LlavePublica</c>, <c>…:LlavePrivada</c> y
/// <c>…:Sujeto</c> (una dirección <c>mailto:</c> de contacto, que el servicio de entrega exige).
/// </summary>
public class OpcionesDeAvisosPush
{
    public string? LlavePublica { get; set; }
    public string? LlavePrivada { get; set; }
    public string? Sujeto { get; set; }
}
