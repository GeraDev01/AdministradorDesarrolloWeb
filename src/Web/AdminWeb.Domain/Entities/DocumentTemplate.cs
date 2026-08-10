namespace AdminWeb.Domain.Entities;

/// <summary>Las plantillas que el área puede sustituir. Una fila por clave.</summary>
public static class PlantillaDeDocumento
{
    public const string SolicitudDeVacaciones = "vacaciones.solicitud";
}

/// <summary>
/// Una plantilla de documento subida desde la web, guardada entera en la base.
///
/// <para><b>Por qué en la base y no en disco.</b> El servidor corre en un contenedor: su sistema de
/// archivos es efímero, así que una plantilla dejada ahí desaparece en el siguiente despliegue. Y
/// con dos instancias detrás de un balanceador cada una tendría la suya, de modo que el documento
/// saldría con un formato u otro según a qué instancia tocara atender. Una plantilla que se pierde
/// al reiniciar es peor que no poder subirla.</para>
///
/// <para><b>Por qué una tabla y no AppSettings.</b> Esa tabla es de pares clave-valor de TEXTO y su
/// contenido viaja entero al navegador cada vez que se abre Configuración: meter ahí 124 KB de Word
/// en base64 significaría descargarlos en cada carga de esa pantalla. Aquí los bytes solo salen
/// cuando alguien pide descargar la plantilla.</para>
///
/// <para>No hay historial de versiones a propósito: la anterior se reemplaza. Quien quiera
/// conservarla la descarga antes, y la de FÁBRICA no se puede perder nunca porque va incrustada en
/// el ensamblado — restablecer es siempre posible.</para>
/// </summary>
public class DocumentTemplate
{
    public int Id { get; set; }

    /// <summary>Qué plantilla es. Ver <see cref="PlantillaDeDocumento"/>. Única.</summary>
    public string Clave { get; set; } = "";

    /// <summary>El .docx completo.</summary>
    public byte[] Contenido { get; set; } = [];

    /// <summary>Con qué nombre se subió. Es lo que se devuelve al descargarla.</summary>
    public string? NombreDeArchivo { get; set; }

    /// <summary>Quién la subió, por nombre y no por id: es para leerlo en la pantalla.</summary>
    public string? SubidaPor { get; set; }

    public DateTime SubidaEnUtc { get; set; } = DateTime.UtcNow;
}
