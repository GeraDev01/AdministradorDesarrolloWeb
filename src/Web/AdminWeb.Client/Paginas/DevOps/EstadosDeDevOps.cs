namespace AdminWeb.Client.Paginas.DevOps;

/// <summary>
/// Cómo se pinta el estado de un work item de Azure DevOps.
///
/// <para>Vive aquí y no dentro de cada pantalla porque el mapa estaba COPIADO palabra por palabra en
/// AzureDevOps y en MisTickets: son la misma columna de la misma tabla vista por el líder y por el
/// desarrollador, y con dos copias se podía corregir una y olvidar la otra. Quien añada un estado
/// nuevo —una plantilla de proceso de DevOps puede tenerlos— lo añade una sola vez.</para>
///
/// <para>Los estados de un sistema AJENO no se pintan nunca con el acento de la aplicación. El acento
/// señala lo que hay que hacer aquí dentro; que un ticket de DevOps esté activo no es una acción
/// nuestra. Por eso «active» va al color de información y no al primario.</para>
///
/// <para>Devuelve una cadena var(…) y no un color de .NET a propósito: solo se interpola dentro de un
/// atributo style y quien la resuelve es el navegador, así que sigue al tema claro o al oscuro sin
/// que esta clase se entere. Si algún día hiciera falta el color de verdad —para un Excel o un PDF—,
/// esto no sirve y habría que resolverlo del lado del servidor.</para>
/// </summary>
public static class EstadosDeDevOps
{
    /// <summary>
    /// El color del estado, o <c>inherit</c> si es uno que no conocemos: un estado desconocido se
    /// lee con el color del texto normal, que dice «esto no lo sé interpretar» mejor que un color
    /// elegido al azar.
    ///
    /// <para>Todo lo que salga de aquí acaba siendo el color de un TEXTO —la palabra del estado
    /// dentro de un <c>&lt;b&gt;</c>—, así que cualquier valor nuevo tiene que aguantar 4,5:1 contra
    /// la tarjeta EN LOS DOS MODOS. Los escalones de la escala base (<c>--rz-base-300/400</c>) no lo
    /// aguantan y no valen aquí, por muy «apagados» que se vean en la maqueta: sirven para rellenos
    /// y para la pista de una barra, no para leer.</para>
    /// </summary>
    public static string Color(string? estado) => (estado ?? "").ToLowerInvariant() switch
    {
        "active" or "in progress" or "doing" or "in development" => "var(--rz-info)",
        "resolved" or "done" or "completed" => "var(--rz-success)",

        // Lo que ya no pide nada —cerrado— y lo que todavía no ha empezado —nuevo— comparten el gris
        // apagado, y comparten a propósito: el nombre del estado va SIEMPRE escrito al lado, así que
        // el color agrupa y es la palabra la que identifica. Es el mismo criterio que EtiquetasDeTrabajo
        // aplica a los siete estados de un requerimiento en cinco cajones.
        //
        // «Nuevo» llevaba var(--rz-base-400) para verse «todavía más apagado» que cerrado, y eso hay
        // que no reponerlo: ese escalón vale #b0a89a en claro, que sobre la tarjeta blanca da 2,36 —lo
        // dice el propio tema.css, donde está anotado como INSUFICIENTE hasta para el borde de un
        // campo, que solo necesita 3—. Aquí no es un borde, es TEXTO, y el listón son 4,5. En oscuro
        // tampoco se salvaba: #6b6255 sobre el fondo grafito da 2,9. O sea, un estado que se leía mal
        // en los dos modos por ganar un matiz que nadie iba a notar. La escala de grises legibles
        // tiene UN escalón para texto secundario, y es éste.
        "closed" or "new" or "to do" => "var(--rz-text-secondary-color)",

        _ => "inherit"
    };
}
