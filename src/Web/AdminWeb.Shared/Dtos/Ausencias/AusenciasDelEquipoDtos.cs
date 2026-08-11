namespace AdminWeb.Shared.Dtos.Ausencias;

/// <summary>
/// Quién más del equipo va a estar fuera en unas fechas concretas.
///
/// <para><b>Para qué existe.</b> Es la única ayuda del asistente de vacaciones que evita un problema
/// que de verdad pasa: dos personas del mismo equipo fuera la misma semana. El saldo y los días
/// hábiles se pueden calcular a mano; esto no, porque el dato está en las solicitudes de los demás y
/// nadie lo tiene delante al elegir sus fechas. Se consulta en el paso del asistente que pregunta
/// justamente eso, con las fechas ya elegidas.</para>
///
/// <para><b>No bloquea nada.</b> Coincidir puede ser perfectamente razonable —y quien decide es el
/// líder al resolver—; lo que no puede pasar es que se coincida sin saberlo. Por eso esto se
/// enseña, se puede volver atrás a cambiar las fechas, y se puede seguir adelante igual.</para>
///
/// <para><b>Va aparte de <c>MisVacacionesDto</c> a propósito</b>, y es la única consulta de estas
/// pantallas que no viaja con la carga inicial: depende de dos fechas que la persona todavía no ha
/// elegido cuando la pantalla se pinta. Meterla en la carga obligaría a devolver algo inventado —el
/// mes en curso, la semana próxima— que casi nunca sería el rango que se acaba pidiendo.</para>
/// </summary>
/// <param name="Inicio">El rango consultado, ya normalizado por el servidor (fechas sin hora, y en
/// orden). Vuelve en la respuesta para que la pantalla pueda comprobar que lo que enseña corresponde
/// a las fechas que hay ahora en el formulario y no a una consulta anterior que llegó tarde.</param>
/// <param name="Fuera">Un renglón por solicitud aprobada que se solapa, ordenado por fecha de
/// inicio. Puede traer dos renglones de la misma persona si tiene dos periodos que caen dentro.</param>
/// <param name="Resumen">La frase que resume el cruce, escrita por el servidor para que la pantalla
/// no tenga que decidir cuándo se dice «nadie», «una persona» o «tres personas».</param>
public record AusenciasDelEquipoDto(
    DateTime Inicio,
    DateTime Fin,
    IReadOnlyList<CompaneroFueraDto> Fuera,
    string Resumen);

/// <summary>
/// Alguien del equipo con vacaciones APROBADAS que pisan las fechas consultadas.
///
/// <para><b>Lo que NO lleva, y no es un olvido.</b> Ni el comentario de la solicitud, ni su
/// identificador, ni nada que permita operar sobre ella: esto lo consulta cualquier desarrollador
/// sobre las solicitudes de sus compañeros, así que lleva lo mínimo que responde a la pregunta —quién
/// y qué días— y nada más. Un motivo ajeno no es asunto de quien está eligiendo sus fechas.</para>
///
/// <para><b>Y no lleva PERMISOS</b>, solo vacaciones. Un permiso trae su tipo, y entre los tipos
/// están «Incapacidad» y «Cita médica»: enseñárselo al resto del equipo sería repartir información de
/// salud de otra persona por una pantalla de planeación. Con las vacaciones se responde la pregunta
/// que importa —«¿quién no va a estar esa semana?»— sin contar nada de nadie.</para>
/// </summary>
/// <param name="DiasQueSeSolapan">Días naturales que caen dentro del rango consultado, contando el
/// primero y el último. No son los días que dura su vacación entera: es cuánto pisa lo tuyo, que es
/// lo que ayuda a decidir si mover unas fechas o no.</param>
/// <param name="MismoEquipo">Está en el mismo equipo que quien pregunta. Se marca en vez de filtrar
/// porque en una casa de once personas coincidir con alguien de otro equipo también deja un hueco;
/// pero coincidir con el de al lado es lo que hay que ver primero.</param>
public record CompaneroFueraDto(
    string Nombre,
    DateTime Inicio,
    DateTime Fin,
    int DiasQueSeSolapan,
    bool MismoEquipo);
