using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Plantillas;

/// <summary>
/// Una plantilla en la lista.
///
/// El cuerpo NO viaja aquí: la biblioteca puede tener cuerpos de 100 000 caracteres y traerlos
/// todos para pintar una rejilla de cinco columnas sería mandar megabytes por nada. Se pide al
/// abrir la vista previa (<see cref="PlantillaDetalleDto"/>).
/// </summary>
/// <param name="TieneArchivo">Si trae un adjunto. El binario nunca viaja en el DTO: se descarga por
/// su propio endpoint.</param>
public record PlantillaListaDto(
    int Id,
    TemplateKind Tipo,
    string TipoTexto,
    string Icono,
    string Titulo,
    string? Descripcion,
    string? Etiquetas,
    int Usos,
    DateTime? UltimoUso,
    DateTime? Actualizada,
    bool Archivada,
    bool TieneArchivo,
    string? NombreArchivo);

/// <summary>
/// La plantilla con su contenido, para la vista previa y para copiar.
/// </summary>
/// <param name="Cuerpo">El texto reutilizable, con sus marcadores <c>{{nombre}}</c> sin rellenar.</param>
/// <param name="Marcadores">Los marcadores que hay que preguntar antes de copiar, en orden de
/// aparición y sin los integrados (fecha, usuario…), que se resuelven solos. Los calcula el
/// servidor con la misma expresión regular que usaba el escritorio: si el cliente los buscara por
/// su cuenta, tarde o temprano las dos listas dejarían de coincidir.</param>
/// <param name="EsScript">Se guarda para ejecutarse FUERA de la aplicación. La pantalla lo avisa;
/// la aplicación nunca ejecuta nada de lo que hay aquí.</param>
public record PlantillaDetalleDto(
    int Id,
    TemplateKind Tipo,
    string TipoTexto,
    string Icono,
    string Titulo,
    string? Descripcion,
    string? Etiquetas,
    string Cuerpo,
    IReadOnlyList<string> Marcadores,
    bool EsScript,
    int Usos,
    DateTime? UltimoUso,
    DateTime? Actualizada,
    bool Archivada,
    bool TieneArchivo,
    string? NombreArchivo,
    string NombreArchivoSugerido);

/// <summary>Cuántas plantillas activas hay de cada tipo, para los contadores de la pantalla.</summary>
public record ConteoDePlantillasDto(TemplateKind Tipo, string TipoTexto, string Icono, int Cuantas);

/// <summary>
/// Lo que la persona escribió para cada marcador antes de copiar.
///
/// Viaja al servidor —en vez de sustituirse en el navegador— porque la sustitución tiene reglas que
/// el escritorio ya fijó: los marcadores integrados (fecha, hora, usuario) se resuelven solos, la
/// búsqueda no distingue mayúsculas y un marcador sin valor se deja tal cual a la vista. Repetir
/// esa lógica en el cliente sería tener dos versiones de la misma regla, y la segunda envejecería.
/// </summary>
public record RellenoDePlantillaDto(Dictionary<string, string> Valores);

/// <summary>El cuerpo ya rellenado, listo para el portapapeles.</summary>
public record TextoDePlantillaDto(string Texto);
