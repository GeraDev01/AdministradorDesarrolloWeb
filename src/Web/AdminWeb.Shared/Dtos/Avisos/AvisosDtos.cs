using AdminWeb.Shared.Enums;

namespace AdminWeb.Shared.Dtos.Avisos;

/// <summary>
/// Un aviso de la bandeja del usuario.
///
/// Dos cosas que el DTO decide y la pantalla no:
///
/// 1. <paramref name="CreadoUtc"/> viaja SIEMPRE con <c>Kind = Utc</c>. Lo que EF lee de la base
///    vuelve sin zona («Unspecified») y, si se mandara así, el navegador lo interpretaría como hora
///    local y un aviso de hace diez minutos se leería con seis horas de desfase.
///
/// 2. <paramref name="Url"/> ya viene filtrada a http/https por el servidor. El escritorio abría este
///    campo con el shell y por eso solo aceptaba esos dos esquemas; en el navegador la razón es más
///    apremiante todavía, porque un <c>javascript:</c> pegado en un aviso se ejecutaría en la sesión
///    de quien lo abre. Lo que no pasa el filtro llega en null y el aviso se lee sin enlace.
/// </summary>
public record AvisoDto(
    int Id,
    NotificationKind Tipo,
    string TipoTexto,
    string Titulo,
    string Mensaje,
    string? Url,
    DateTime CreadoUtc,
    bool Leido);

/// <summary>
/// Cuántos avisos sin leer tiene quien pregunta. Lo consume el contador del menú, así que se sirve
/// aparte de la rejilla: pedir la primera página completa solo para pintar un número sería traer
/// cincuenta filas para mostrar un «3».
/// </summary>
public record ContadorAvisosDto(int NoLeidos);
