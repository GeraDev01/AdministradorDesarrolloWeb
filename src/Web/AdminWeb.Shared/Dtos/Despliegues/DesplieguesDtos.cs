
/// <summary>
/// Un servidor tal como viene en el JSON del alta masiva.
///
/// <para>Los nombres coinciden con los del escritorio a propósito: el archivo que el área ya tiene
/// preparado tiene que servir tal cual, sin reescribirlo. La lectura es indiferente a mayúsculas,
/// así que «Nombre» y «nombre» valen igual.</para>
///
/// <para>La contraseña llega EN CLARO en el archivo —no hay otra forma de importarla— y se cifra en
/// cuanto entra a la base. Ese archivo no debería quedarse en ningún disco compartido.</para>
/// </summary>
public record ServidorImportadoDto(
    string? Nombre,
    string? Host,
    int Puerto,
    string? Usuario,
    string? Contrasena,
    string? RutaRemota,
    string? Url);
