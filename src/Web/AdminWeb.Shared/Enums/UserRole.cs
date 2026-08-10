namespace AdminWeb.Shared.Enums;

/// <summary>
/// Rol de una cuenta. Los valores son los MISMOS enteros que guarda la base desde el escritorio
/// (Admin=0, Operaciones=1, Desarrollador=2): la web lee las mismas filas, así que renumerarlos
/// convertiría a todos los administradores en operaciones de un día para otro.
///
/// Vive en Shared y no en Domain porque el cliente Blazor necesita conocerlo para pintar el menú,
/// y Shared es lo único que el navegador descarga.
/// </summary>
public enum UserRole
{
    Admin = 0,
    Operaciones = 1,
    Desarrollador = 2
}
