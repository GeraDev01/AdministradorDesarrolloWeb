namespace AdminWeb.Shared.Enums;

/// <summary>
/// Qué se hizo. Del 0 al 8 son los MISMOS valores que el escritorio, porque la bitácora es la misma
/// tabla y los números ya escritos no se pueden reinterpretar.
/// </summary>
public enum AuditAction
{
    Login = 0,
    Logout = 1,
    Create = 2,
    Update = 3,
    Delete = 4,
    Deploy = 5,
    Backup = 6,
    PasswordChange = 7,
    ConfigChange = 8,

    /// <summary>
    /// Alguien CONSULTÓ un dato sensible: hoy, la clave de licencia de un programa.
    ///
    /// <para>Es el único valor que el escritorio no tiene, y se añade solo aquí a propósito. Se
    /// puede hacer sin riesgo porque la web nunca escribe en la base de producción mientras el
    /// escritorio siga en ella: para cuando esta fila exista, el escritorio ya estará retirado. Aun
    /// así, su pantalla de bitácora no se rompería —arma su filtro por índice y pinta lo que no
    /// conoce con <c>ToString()</c>—, así que lo peor que podría pasar es una fila que dijera «9».</para>
    ///
    /// <para>Hacía falta un valor propio y no reusar <see cref="Update"/>: leer un secreto no
    /// modifica nada, y mezclarlo con las modificaciones haría imposible responder «¿quién ha visto
    /// esta clave?», que es justo la pregunta para la que se registra.</para>
    /// </summary>
    Read = 9
}

/// <summary>
/// Cómo terminó la operación registrada. Una bitácora que solo guarda lo que sí ocurrió no sirve
/// para detectar a alguien intentando lo que no le toca.
/// </summary>
public enum AuditOutcome
{
    Exito = 0,
    Fallo = 1,
    Denegado = 2
}
