using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Manual;

/// <summary>
/// Un artículo del manual tal como se escribe en el catálogo.
/// </summary>
/// <param name="Clave">
/// El identificador ESTABLE del artículo, y la pieza de la que depende todo lo demás.
///
/// <para>No se enseña en ninguna pantalla y no tiene nada que ver con el título: es lo que el
/// arranque anota para saber que este artículo ya se sembró alguna vez. Por eso una clave <b>no se
/// renombra jamás</b> —renombrarla equivale a decir «esto es un artículo nuevo» y lo volvería a
/// insertar junto al que ya existe— y por eso el título sí se puede corregir con toda tranquilidad.</para>
/// </param>
/// <param name="Titulo">Lo que se lee en la lista y en el buscador. Es el segundo cinturón de
/// seguridad contra los duplicados; ver <see cref="ManualDeUso.SembrarAsync"/>.</param>
/// <param name="Etiquetas">
/// Ya normalizadas como las deja el servicio: en minúsculas, sin repetir y separadas por «, ». Se
/// escriben así porque este sembrador inserta la fila directamente y no pasa por
/// <c>ConocimientoService</c>; una etiqueta con mayúscula partiría en dos el índice de la base.
/// </param>
/// <param name="Cuerpo">El texto con el marcado mínimo que entiende <see cref="ConocimientoTexto"/>.</param>
public sealed record ArticuloDelManual(string Clave, string Titulo, string Etiquetas, string Cuerpo);

/// <summary>
/// El MANUAL DE USO de la plataforma, sembrado como artículos publicados de la base de conocimiento.
///
/// <para><b>Esto no es un juego de datos de demostración.</b> <see cref="Demo.DatosDeDemostracion"/>
/// inventa personas y trabajo para poder enseñar la aplicación, corre solo si alguien lo pide, solo
/// fuera de producción y solo sobre una base virgen. Este sembrador es lo contrario en las tres
/// cosas: es CONTENIDO DEL PRODUCTO —igual que los criterios de puntuación o las plantillas— y va
/// siempre, en cualquier base y en cualquier entorno. Sus tres guardas no se copian aquí porque
/// convertirían el manual en algo que la base de producción, que es justamente donde entra gente
/// nueva, nunca llegaría a tener.</para>
///
/// <para><b>Se siembra lo que FALTA y no se pisa NUNCA lo que haya.</b> Que alguien corrija un
/// artículo del manual es exactamente lo que se quiere que pase: es documentación viva y quien la
/// usa sabe mejor que nadie qué le falta. Un arranque que devolviera el texto a su versión de
/// fábrica haría que corregirlo no sirviera de nada, y a la segunda vez nadie volvería a intentarlo.
/// Por eso aquí no hay ninguna escritura sobre un artículo existente: solo inserciones, y solo de lo
/// que nunca se ha sembrado. Cómo se sabe qué es «nunca» está explicado en
/// <see cref="SembrarAsync"/>.</para>
///
/// <para><b>De quién son.</b> De nadie: ver <see cref="Firma"/> y <see cref="SinCuenta"/>.</para>
///
/// <para>Va dentro del candado del migrador, junto con los demás sembrados, por la misma razón que
/// ellos: dos instancias arrancando a la vez insertarían el manual por duplicado.</para>
/// </summary>
public static class ManualDeUso
{
    /// <summary>
    /// La fila de <c>AppSettings</c> donde queda anotado qué artículos ya se sembraron.
    ///
    /// <para><b>Por qué la memoria vive FUERA de los artículos.</b> La alternativa evidente era
    /// reconocer cada artículo por su título, y es justo la que rompe lo que hay que proteger:
    /// corregir un título —que es una corrección tan legítima como cualquier otra— haría que el
    /// arranque siguiente no lo reconociera y sembrara una segunda copia al lado. Anotando la clave
    /// aparte, el artículo puede cambiar de título, de texto, de etiquetas y de estado sin dejar de
    /// estar reconocido.</para>
    ///
    /// <para>Es el mismo patrón que ya usan <see cref="TemplateSeed.ClaveSembrado"/> y la marca de
    /// conversión de plazos del migrador, con una diferencia: aquí no se guarda un «1» sino la LISTA
    /// de claves, para que un artículo añadido en una versión posterior se siembre solo en las bases
    /// que ya existían, sin volver a tocar los que ya están.</para>
    /// </summary>
    public const string ClaveDelRegistro = "ManualSembrado";

    /// <summary>Lo que se escribe en la fila del registro para quien la encuentre en Configuración.</summary>
    public const string DescripcionDelRegistro =
        "Artículos del manual de uso que ya se sembraron en esta base. No se edita a mano: es lo que " +
        "impide que el arranque vuelva a insertar un artículo que alguien corrigió o retiró.";

    /// <summary>
    /// Con qué nombre quedan firmados los artículos del manual.
    ///
    /// <para><b>No se firman con la cuenta de nadie</b>, y la decisión tiene consecuencias que
    /// conviene tener escritas. Firmarlos con la cuenta del administrador inicial —la opción
    /// cómoda— tenía tres problemas: esa cuenta puede no existir (se renombra, se da de baja, o la
    /// base viene de otra instalación), el manual aparecería dentro de «solo los míos» de una persona
    /// que no escribió una línea, y el día que esa persona se fuera del equipo la documentación del
    /// producto quedaría firmada por un fantasma.</para>
    ///
    /// <para>Que no haya cuenta detrás <b>no rompe ninguna pantalla</b>, y eso está comprobado, no
    /// supuesto: <c>KnowledgeArticle.AuthorName</c> es texto congelado y no tiene clave foránea a
    /// <c>Users</c> —a propósito, porque la documentación tiene que sobrevivir a que alguien se vaya—,
    /// así que ni la lista ni el artículo consultan la tabla de cuentas para pintar el autor.</para>
    /// </summary>
    public const string Firma = "Manual de la plataforma";

    /// <summary>
    /// El identificador de usuario con el que quedan: ninguno.
    ///
    /// <para>Cero no es un identificador válido —las cuentas empiezan en 1— y ése es justamente el
    /// efecto que se busca, porque de él salen tres comportamientos correctos sin escribir una regla
    /// nueva en ningún sitio: el manual no le sale a nadie como «mío», ningún desarrollador puede
    /// reescribirlo por su cuenta (editar exige ser el autor o ser el líder) y el líder SÍ puede
    /// corregirlo sin que el artículo caiga a la cola de revisión, porque volver a la cola solo le
    /// pasa a un autor que no es líder.</para>
    /// </summary>
    public const int SinCuenta = 0;

    /// <summary>La etiqueta que llevan todos: es por donde se encuentra el manual entero.</summary>
    public const string Etiqueta = "manual";

    /// <summary>La que llevan además los términos cortos.</summary>
    public const string EtiquetaDelGlosario = "glosario";

    /// <summary>
    /// El manual completo, en el orden en que conviene leerlo: primero las áreas y al final el
    /// glosario. El orden se conserva en la pantalla porque de él salen las fechas de publicación
    /// (ver <see cref="SembrarAsync"/>), no porque la lista lo respete por sí sola.
    /// </summary>
    public static IReadOnlyList<ArticuloDelManual> Articulos { get; } =
        [.. CatalogoDelManual.Areas, .. GlosarioDelManual.Terminos];

    /// <summary>
    /// Siembra los artículos del manual que todavía no se hayan sembrado nunca. Devuelve cuántos
    /// insertó, para escribirlo en el registro del arranque.
    ///
    /// <para><b>Las dos comprobaciones, y por qué hacen falta las dos.</b></para>
    /// <list type="number">
    /// <item><b>El registro</b> (<see cref="ClaveDelRegistro"/>) decide si el artículo se INSERTA:
    /// si la clave está anotada, no se vuelve a insertar nunca, pase lo que pase.</item>
    /// <item><b>El título</b> es la red por debajo. La fila del registro vive en una tabla que se
    /// enseña en la pantalla de Configuración y que, por lo tanto, se puede vaciar; sin esta segunda
    /// comprobación, vaciarla insertaría el manual entero por segunda vez. Con ella, lo que ya está
    /// se reconoce igual y solo se re-anota la clave.</item>
    /// </list>
    ///
    /// <para><b>Las fechas se escalonan</b> un segundo hacia atrás por cada artículo, en el orden del
    /// catálogo. No es cosmético: la base de conocimiento ordena por fecha descendente, así que sin
    /// escalonar, el orden de lectura dependería de en qué orden le asignara la base los
    /// identificadores. Con esto, «Primeros pasos» queda arriba y el glosario al final.</para>
    ///
    /// <para><b>Y CORRIGE el artículo que nadie ha tocado</b>, que es lo que antes no hacía y costó
    /// caro. Este manual describe cómo funciona la plataforma, y la plataforma cambia: cuando se
    /// retiró la autocalificación, el texto de aquí se reescribió y <b>no llegó a producción</b>,
    /// porque el registro decía que esas claves ya estaban sembradas. El manual siguió explicando
    /// durante semanas un camino que la aplicación ya rechazaba — que es peor que no tener manual.</para>
    ///
    /// <para><b>Las cuatro condiciones para corregir</b>, y las tres primeras son la misma promesa de
    /// siempre dicha con precisión: no se deshace la decisión de nadie.</para>
    /// <list type="number">
    /// <item><b>Es NUESTRO</b>: lo firma el manual (<see cref="SinCuenta"/> y <see cref="Firma"/>).
    /// Un artículo de otra persona que se llame igual no se toca — eso ya lo probaba una prueba.</item>
    /// <item><b>Nadie lo editó</b>: <c>UpdatedAtUtc</c> sigue nulo. En cuanto alguien lo corrige, el
    /// artículo pasa a ser suyo y este método no vuelve a escribirlo jamás.</item>
    /// <item><b>Sigue publicado</b>: si el líder lo retiró, retirado se queda.</item>
    /// <item><b>Y de verdad cambió</b>: se compara el cuerpo ya saneado, así que un arranque normal
    /// no escribe nada.</item>
    /// </list>
    ///
    /// <para>No se toca el TÍTULO ni las etiquetas ni el estado, solo el CUERPO. Y no se escribe
    /// <c>UpdatedAtUtc</c>: no lo editó una persona, y marcarlo cerraría la puerta a la siguiente
    /// corrección.</para>
    ///
    /// <para>Se prefirió esto a una versión por artículo escrita a mano —«bumpea el número cuando
    /// cambies el texto»— justamente porque ese número se olvida, y olvidarlo devuelve el problema
    /// entero sin que nadie se entere. El catálogo es la fuente; comparar es gratis.</para>
    /// </summary>
    public static async Task<int> SembrarAsync(AppDbContext db, CancellationToken ct = default)
    {
        // Sin AsNoTracking a propósito: si la fila existe, se va a modificar.
        var registro = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == ClaveDelRegistro, ct);
        var sembradas = LeerClaves(registro?.Value);

        var pendientes = Articulos
            .Select((articulo, orden) => (articulo, orden))
            .Where(x => !sembradas.Contains(x.articulo.Clave))
            .ToList();

        // Se traen TODAS las filas del manual, no solo las de lo pendiente: hacen falta las que ya
        // están para poder corregirlas. Son unas decenas y esto corre una vez por arranque, dentro
        // del candado. Y SIN AsNoTracking, porque algunas se van a modificar.
        var titulos = Articulos.Select(a => a.Titulo).ToList();
        var filas = await db.KnowledgeArticles
            .Where(a => titulos.Contains(a.Title))
            .ToListAsync(ct);

        var yaEstan = filas.Select(a => a.Title).ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        var ahora = DateTime.UtcNow;
        int agregados = 0;

        foreach (var (articulo, orden) in pendientes)
        {
            // Se anota SIEMPRE, se haya insertado o se haya encontrado ya puesto: en los dos casos
            // este artículo queda resuelto y no hay nada que volver a mirar el arranque que viene.
            sembradas.Add(articulo.Clave);
            if (yaEstan.Contains(articulo.Titulo)) continue;

            var cuando = ahora.AddSeconds(-orden);

            db.KnowledgeArticles.Add(new KnowledgeArticle
            {
                Title  = articulo.Titulo,
                // Por el mismo saneador que el texto que escribe una persona: uniforma los saltos de
                // línea —este texto nace con los del archivo fuente— y quita los controles invisibles.
                Body   = ConocimientoTexto.Sanear(articulo.Cuerpo),
                Tags   = articulo.Etiquetas,
                // Nace PUBLICADO: el manual no pasa por la cola del líder porque no es una propuesta
                // de nadie, y un manual en borrador no lo vería ni quien lo necesita.
                Status = KnowledgeStatus.Publicado,

                AuthorUserId      = SinCuenta,
                AuthorName        = Firma,
                // Sin ficha de desarrollador: no hay a quién abonarle puntos por esto, y que el campo
                // vaya nulo es lo que impide que una aprobación posterior intente otorgarlos.
                AuthorDeveloperId = null,

                CreatedAtUtc   = cuando,
                PublishedAtUtc = cuando
            });
            agregados++;
        }

        int corregidos = Corregir(filas, sembradas);

        Anotar(db, registro, sembradas);

        // Un solo guardado para los artículos y para el registro: si algo falla, no queda un manual a
        // medias con la lista diciendo que está completo.
        await db.SaveChangesAsync(ct);
        return agregados + corregidos;
    }

    /// <summary>
    /// Pone al día el cuerpo de los artículos del manual QUE NADIE HA TOCADO. Devuelve cuántos
    /// cambió; en un arranque normal, cero.
    ///
    /// <para>Las condiciones y el porqué de cada una están en <see cref="SembrarAsync"/>. Aquí solo
    /// se insiste en la que más fácil se rompe al tocar esto: <b>se compara contra el cuerpo ya
    /// SANEADO</b>, que es el que se guardó al sembrar. Comparando contra el texto crudo del archivo
    /// fuente, la normalización de saltos de línea haría que todos los artículos parecieran distintos
    /// en cada arranque y esto reescribiría el manual entero cada madrugada.</para>
    /// </summary>
    private static int Corregir(List<KnowledgeArticle> filas, HashSet<string> sembradas)
    {
        int corregidos = 0;

        foreach (var articulo in Articulos)
        {
            // Solo lo que YA se había sembrado: lo que se acaba de insertar en este mismo arranque
            // nace con el texto de hoy y no hay nada que corregir.
            if (!sembradas.Contains(articulo.Clave)) continue;

            var fila = filas.FirstOrDefault(
                a => string.Equals(a.Title, articulo.Titulo, StringComparison.CurrentCultureIgnoreCase));

            // Lo borraron, o le cambiaron el título —que es una forma de hacerlo suyo—.
            if (fila is null) continue;

            // No es nuestro: alguien escribió un artículo con el mismo título.
            if (fila.AuthorUserId != SinCuenta || fila.AuthorName != Firma) continue;

            // Alguien lo corrigió: a partir de ahí es suyo.
            if (fila.UpdatedAtUtc is not null) continue;

            // El líder lo retiró de publicación.
            if (fila.Status != KnowledgeStatus.Publicado) continue;

            var cuerpo = ConocimientoTexto.Sanear(articulo.Cuerpo);
            if (string.Equals(fila.Body, cuerpo, StringComparison.Ordinal)) continue;

            // El CUERPO y nada más. Y sin tocar UpdatedAtUtc: no lo editó una persona, y marcarlo
            // cerraría la puerta a la corrección siguiente.
            fila.Body = cuerpo;
            corregidos++;
        }

        return corregidos;
    }

    /// <summary>
    /// Las claves ya anotadas. Se conservan las que no estén en el catálogo actual: si un artículo se
    /// retira de una versión posterior, su clave sigue siendo suya y nadie debería reutilizarla.
    /// </summary>
    private static HashSet<string> LeerClaves(string? valor) =>
        (valor ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void Anotar(AppDbContext db, AppSetting? registro, HashSet<string> claves)
    {
        // Ordenadas para que la fila sea legible y para que dos bases con el mismo manual guarden el
        // mismo texto, que es lo que permite compararlas de un vistazo.
        var valor = string.Join(",", claves.OrderBy(c => c, StringComparer.Ordinal));

        if (registro == null)
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = ClaveDelRegistro, Value = valor, Description = DescripcionDelRegistro
            });
            return;
        }

        registro.Value = valor;
        // La descripción solo se rellena si falta. Si alguien anotó ahí algo suyo, se respeta: pisarlo
        // sería exactamente lo que este sembrador promete no hacer.
        if (string.IsNullOrWhiteSpace(registro.Description)) registro.Description = DescripcionDelRegistro;
    }
}
