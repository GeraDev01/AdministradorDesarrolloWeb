namespace AdminWeb.Shared.Dtos.Busqueda;

/// <summary>
/// Un resultado de la búsqueda global.
/// </summary>
/// <param name="Tipo">Qué es, ya en palabras («Requerimiento», «Ticket de DevOps»…). Viene resuelto
/// del servidor para que el cliente no tenga que conocer el enum ni traducirlo por su cuenta.</param>
/// <param name="Texto">La línea principal, tal como se lee en la lista.</param>
/// <param name="Ruta">
/// A qué pantalla de la WEB lleva, ya traducida.
///
/// <para>El servicio devuelve por dentro la clave de navegación del escritorio («requirements»,
/// «developers»), que allí era una clave interna de un diccionario de controles. Aquí son URLs de
/// verdad y no coinciden («requerimientos», «desarrolladores»), así que la traducción se hace en la
/// frontera de la API. Mandar la clave del escritorio al navegador habría obligado al cliente a
/// mantener el mismo mapa, y ese es justo el sitio donde se olvidaría al añadir una pantalla.</para>
/// </param>
public record ResultadoDeBusquedaDto(string Tipo, string Texto, string Detalle, string Ruta);
