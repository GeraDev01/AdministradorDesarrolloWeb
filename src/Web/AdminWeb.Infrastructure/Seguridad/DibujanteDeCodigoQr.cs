using AdminWeb.Domain.Security;
using QRCoder;

namespace AdminWeb.Infrastructure.Seguridad;

/// <summary>
/// Dibuja el código QR del alta del segundo factor.
///
/// <para><b>Se usa el generador de PNG por bytes y NO el de mapa de bits.</b> QRCoder trae varias
/// salidas y algunas —<c>QRCode</c>, <c>ArtQRCode</c>— dependen de <c>System.Drawing</c>, que es una
/// pieza de Windows: en el contenedor Linux donde corre esto no existe, y el fallo no aparecería al
/// compilar sino la primera vez que alguien pidiera su QR en producción, con una excepción de tipo
/// no encontrado. <c>PngByteQRCode</c> escribe el PNG a mano en memoria y no depende de nada del
/// sistema operativo. <b>Si alguien cambia de generador, hay que comprobar esto antes.</b></para>
///
/// <para>Es el ÚNICO archivo de todo el sistema que conoce la librería de códigos QR: el servicio
/// del segundo factor solo ve <see cref="IDibujanteDeCodigoQr"/>.</para>
/// </summary>
public sealed class DibujanteDeCodigoQrCoder : IDibujanteDeCodigoQr
{
    /// <summary>
    /// Píxeles por módulo (cada cuadrito del código).
    ///
    /// <para>Ocho da una imagen de unos 300 px de lado para el contenido que se mete aquí, que es lo
    /// que necesita la cámara de un teléfono para leer una pantalla a medio metro sin que la persona
    /// tenga que acercarse. Bajarlo mucho hace que el código se lea mal en pantallas normales, que
    /// es el defecto más difícil de reportar: «a veces no escanea».</para>
    /// </summary>
    private const int PixelesPorModulo = 8;

    public byte[] DibujarPng(string contenido)
    {
        // Nivel de corrección de errores medio (~15 % de la imagen puede estar dañada y seguir
        // leyéndose). Es el equilibrio habitual: más corrección agranda el código sin aportar nada
        // en una pantalla —donde no hay manchas ni dobleces— y menos lo vuelve frágil ante brillos
        // y reflejos, que en una pantalla sí los hay.
        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(contenido, QRCodeGenerator.ECCLevel.M);

        var png = new PngByteQRCode(datos);
        return png.GetGraphic(PixelesPorModulo);
    }
}
