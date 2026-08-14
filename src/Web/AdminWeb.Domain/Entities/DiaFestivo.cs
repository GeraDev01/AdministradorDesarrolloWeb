namespace AdminWeb.Domain.Entities;

/// <summary>
/// Un día que NO se trabaja, y que por tanto no consume vacaciones.
///
/// <para><b>Por qué hay tabla y no solo la regla.</b> Los días del artículo 74 los calcula
/// <c>FestivosDeLey</c> y podrían resolverse al vuelo, pero una casa tiene días propios que la ley no
/// conoce —el puente que se da, el aniversario de la empresa, el día que cierra la oficina— y esos
/// hay que poder escribirlos. La tabla se SIEMBRA desde la regla y admite añadidos a mano.</para>
///
/// <para><b>Y por qué se siembra en vez de calcularse cada vez:</b> para que se puedan mirar. Un
/// festivo que solo existe dentro de una fórmula no se puede revisar antes de que alguien pida
/// vacaciones y le salga un número que no esperaba.</para>
/// </summary>
public class DiaFestivo
{
    public int Id { get; set; }

    /// <summary>
    /// El día. Sin hora: es una fecha de calendario y compararla con horas dentro haría que un festivo
    /// contara o no según a qué hora se guardó.
    /// </summary>
    public DateTime Fecha { get; set; }

    /// <summary>Por qué no se trabaja. Se enseña tal cual a quien mira el calendario.</summary>
    public string Motivo { get; set; } = "";

    /// <summary>
    /// Si sale del artículo 74 o lo puso la casa.
    ///
    /// <para>Importa para poder RESEMBRAR: al añadir años nuevos se reponen los de ley que falten sin
    /// tocar los propios, y sin este dato habría que elegir entre duplicar los de ley o borrar los que
    /// alguien escribió a mano.</para>
    /// </summary>
    public bool EsDeLey { get; set; }
}
