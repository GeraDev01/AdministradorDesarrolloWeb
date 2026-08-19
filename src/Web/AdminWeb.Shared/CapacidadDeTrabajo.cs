namespace AdminWeb.Shared;

/// <summary>
/// CUÁNTO TRABAJO CABE EN UNA PERSONA.
///
/// <para>Cuarenta horas: la jornada semanal. Es la cifra contra la que se lee todo lo demás de la
/// planeación —«tiene 50 h estimadas pendientes» no dice nada hasta que se sabe que la referencia
/// son 40—, y por eso tiene que poder ENSEÑARSE, no solo aplicarse por dentro.</para>
///
/// <para><b>Por qué vive en Shared y no donde nació.</b> Estaba en
/// <c>AdminWeb.Application.Services.CapacityStats</c>, que es donde se usa para clasificar. Pero el
/// cliente que corre en el navegador solo referencia este proyecto —es una frontera dura del
/// repositorio—, así que desde allí el número únicamente podía llegar colgado del DTO de una
/// pantalla concreta. Resultado: ninguna otra pantalla podía decir «la capacidad son 40 h por
/// persona» sin escribirse su propio 40, y dos cuarentas en dos sitios son un cuarenta que un día
/// vale 45 y otro no. Aquí lo leen los dos lados.</para>
///
/// <para><b>Lo que este número NO es.</b> No es un presupuesto de un periodo ni una cuota que haya
/// que gastar. Es el umbral de CARGA PENDIENTE a partir del cual se considera que alguien está
/// sobrecargado y conviene no darle lo siguiente. Por eso no lleva ninguna ventana temporal pegada:
/// quien lo multiplique por personas para hablar del equipo tiene que decir por cuántas personas
/// multiplica y cuáles cuenta.</para>
///
/// <para>Es una constante y no un ajuste de la base a propósito, por ahora: no hay ninguna pantalla
/// donde se configure, y un ajuste que solo se puede cambiar recompilando es un ajuste que miente.
/// El día que haga falta por persona o por equipo, este es el sitio del que colgar la consulta.</para>
/// </summary>
public static class CapacidadDeTrabajo
{
    /// <summary>Horas de trabajo que se consideran una carga completa para UNA persona.</summary>
    public const double HorasPorPersona = 40;
}
