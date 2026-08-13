using AdminWeb.Domain.Documentos;
using AdminWeb.Infrastructure.Documentos;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Los tres documentos en PDF.
///
/// <b>Lo que se comprueba es que SALGAN, no cómo se ven.</b> El aspecto se revisa abriendo el
/// archivo; lo que una prueba puede atrapar —y es justo lo que se rompe en silencio— es que la
/// generación reviente con datos reales: un campo vacío, una lista sin elementos, una firma que no
/// llegó, un color mal escrito. En el escritorio esto ni se planteaba porque el documento se armaba
/// desde una plantilla y fallaba en la máquina de quien lo pedía; aquí falla en el servidor, de
/// noche, para quien esté esperando su solicitud de vacaciones.
///
/// Y hay una razón más: QuestPDF exige declarar su licencia antes de generar nada. Si eso se
/// perdiera, todos los documentos fallarían a la vez y solo en ejecución.
/// </summary>
public class GeneradorDeDocumentosTests
{
    private static readonly IGeneradorDeDocumentos Generador = new GeneradorDeDocumentosQuestPdf();

    /// <summary>Un PDF de verdad empieza por «%PDF» y no está vacío.</summary>
    private static void EsUnPdf(byte[] bytes)
    {
        Assert.True(bytes.Length > 1000, $"El PDF salió de {bytes.Length} bytes: eso no es un documento.");
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
    }

    /// <summary>
    /// Cuántas hojas tiene el documento, contando los objetos «/Type /Page» del propio PDF.
    ///
    /// <para>Se cuenta sobre los bytes y no se pregunta a QuestPDF porque lo que hay que comprobar es
    /// lo que sale, no lo que se pidió. Es la única forma de afirmar en una prueba que una rama no
    /// comparte hoja con otra —que es la decisión de maquetado que sostiene todo el organigrama— sin
    /// tener que abrir el archivo y mirarlo.</para>
    ///
    /// <para>Se descartan los «/Type /Pages», que son el índice de páginas y no una hoja: por eso se
    /// exige que detrás de «/Page» no venga una «s».</para>
    /// </summary>
    private static int CuantasHojas(byte[] pdf)
    {
        var texto = System.Text.Encoding.Latin1.GetString(pdf);
        int hojas = 0, desde = 0;

        while (texto.IndexOf("/Type /Page", desde, StringComparison.Ordinal) is int i and >= 0)
        {
            desde = i + "/Type /Page".Length;
            if (desde >= texto.Length || texto[desde] != 's') hojas++;
        }

        return hojas;
    }

    private static DatosDeVacaciones Vacaciones(byte[]? firma = null, string observaciones = "") => new(
        Nombre: "Ana Pérez", FechaSolicitud: "6 de Agosto de 2026",
        Departamento: "Desarrollo Web", Puesto: "Desarrolladora", JefeDirecto: "Gerardo Manjarrez",
        FechaIngreso: "1 de Marzo de 2023", TotalDias: "5",
        Periodo: "10 de Agosto al 14 de Agosto de 2026",
        FechaInicio: "10 de Agosto de 2026", FechaFin: "14 de Agosto de 2026",
        FechaRegreso: "Lunes 17 de Agosto de 2026", DiasPendientes: "7",
        Autorizada: firma != null, Rechazada: false,
        Observaciones: observaciones, FirmaDelJefe: firma);

    /// <summary>Un PNG mínimo válido, para que la firma se pruebe con una imagen de verdad.</summary>
    private static byte[] FirmaDePrueba() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89,
        0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54,
        0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01,
        0x0D, 0x0A, 0x2D, 0xB4,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    ];

    // ── Solicitud de vacaciones ─────────────────────────────────────────────────

    [Fact]
    public void LaSolicitudSinFirmar_SeGenera()
    {
        // El borrador es el caso normal: la solicitud existe desde que se pide, mucho antes de que
        // nadie la firme. Si solo funcionara firmada, no habría nada que enseñarle a quien la pidió.
        EsUnPdf(Generador.SolicitudDeVacaciones(Vacaciones()));
    }

    [Fact]
    public void LaSolicitudFirmada_SeGenera()
    {
        EsUnPdf(Generador.SolicitudDeVacaciones(Vacaciones(FirmaDePrueba())));
    }

    [Fact]
    public void ConObservacionesLargas_NoRevienta()
    {
        // El comentario del líder lo escribe una persona en una caja de texto sin tope práctico.
        EsUnPdf(Generador.SolicitudDeVacaciones(Vacaciones(observaciones: new string('x', 4000))));
    }

    // ── Ficha de desarrollador ──────────────────────────────────────────────────

    [Fact]
    public void LaFichaDeAlguienReciente_SeGeneraAunqueNoTengaNada()
    {
        // Quien acaba de entrar no tiene evaluaciones, ni hitos, ni equipo, ni puntos. Es justo la
        // ficha que más se pide —para ver cómo va— y la que más fácil se rompe si se asume que
        // siempre hay algo que enseñar.
        var ficha = new DatosDeFicha(
            "Beto Ruiz", null, null, null, null, null, null,
            0, "0:00", 0, [], [], "06/08/2026 10:00");

        EsUnPdf(Generador.FichaDeDesarrollador(ficha));
    }

    [Fact]
    public void LaFichaCompleta_SeGenera()
    {
        var ficha = new DatosDeFicha(
            "Ana Pérez", "ana@ejemplo.test", "555-1234", "Senior", "Equipo Web", "Líder técnica",
            new DateTime(2023, 3, 1), 240, "182:15", 3,
            [
                new EvaluacionImpresa(new DateTime(2026, 6, 30), "1er semestre", 9,
                    "Resuelve sola y documenta.", "Delegar más.", "Buen semestre.", "Gerardo Manjarrez"),
                new EvaluacionImpresa(new DateTime(2025, 12, 31), null, null, null, null, null, null)
            ],
            [
                new HitoImpreso(new DateTime(2025, 9, 1), MilestoneKind.Certificacion, "Azure Developer", "AZ-204"),
                new HitoImpreso(new DateTime(2024, 5, 10), MilestoneKind.Otro, "Sin descripción", null)
            ],
            "06/08/2026 10:00");

        EsUnPdf(Generador.FichaDeDesarrollador(ficha));
    }

    // ── Organigrama de equipos ──────────────────────────────────────────────────

    [Fact]
    public void ElOrganigramaDeEquipos_SeGenera()
    {
        var datos = new DatosDeEquipos(
            [
                new EquipoImpreso("Equipo Web", "Portales y APIs", "Ana Pérez", "#2563eb", null,
                    [
                        new IntegranteImpreso("Ana Pérez", "Senior", "Líder", "Coordina el portal público", true),
                        new IntegranteImpreso("Beto Ruiz", null, "Backend Dev", null, false)
                    ],
                    ["Portal", "API"], ["Migración"]),
                // Un SUBEQUIPO, que se imprime igual y además dice de quién cuelga.
                new EquipoImpreso("Equipo Móvil", null, null, null, "Equipo Web", [], [], [])
            ],
            [new IntegranteImpreso("Carla Díaz", "Junior", "Sin rol", null, false)],
            3,
            "06/08/2026 10:00");

        EsUnPdf(Generador.OrganizacionDeEquipos(datos));
    }

    [Fact]
    public void UnColorDeEquipoMalEscrito_NoImpideImprimir()
    {
        // El color lo teclea una persona en una pantalla. Que se equivoque no puede dejar sin
        // documento a todo el mundo; se cae al color de siempre y el papel sale igual.
        var datos = new DatosDeEquipos(
            [new EquipoImpreso("Equipo", null, null, "azul", null,
                [new IntegranteImpreso("Ana", null, "QA", null, false)], [], [])],
            [], 1, "06/08/2026 10:00");

        EsUnPdf(Generador.OrganizacionDeEquipos(datos));
    }

    [Fact]
    public void SinEquipos_SeGeneraIgual()
    {
        EsUnPdf(Generador.OrganizacionDeEquipos(new DatosDeEquipos([], [], 0, "06/08/2026 10:00")));
    }

    /// <summary>
    /// El caso que decide si el diagrama sirve o no: un equipo que no cabe en una hoja.
    ///
    /// <para>Es la diferencia entre dibujar el organigrama con las piezas de QuestPDF y mandarle un
    /// SVG ya hecho. Un SVG entra como UNA imagen y una imagen no se parte: sesenta personas o salen
    /// encogidas hasta ser ilegibles o no salen. Aquí las cajas son contenido, así que la que no cabe
    /// sigue en la página siguiente. Si esto reventara —y las excepciones de maquetado de QuestPDF
    /// solo aparecen al generar—, se enteraría el líder que aprieta el botón delante de su equipo.</para>
    /// </summary>
    [Fact]
    public void UnEquipoMasAltoQueLaHoja_SeSigueGenerando()
    {
        var mucha = Enumerable.Range(1, 60)
            .Select(i => new IntegranteImpreso(
                $"Persona número {i} con apellido largo", "Semisenior", "Fullstack",
                "Atiende incidencias del sistema de facturación y mantiene sus pruebas", i == 1))
            .ToList();

        var datos = new DatosDeEquipos(
            [new EquipoImpreso("Equipo enorme", "Todo el mundo aquí dentro.", "Persona número 1",
                "#16A34A", null, mucha, [], [])],
            [], 60, "06/08/2026 10:00");

        EsUnPdf(Generador.OrganizacionDeEquipos(datos));
    }

    /// <summary>Con más equipos que columnas hay varias filas, y cada una vuelve a colgar de su barra.</summary>
    [Fact]
    public void ConMuchosEquipos_ElDiagramaSeReparteEnVariasFilas()
    {
        var equipos = Enumerable.Range(1, 11)
            .Select(i => new EquipoImpreso($"Equipo {i}", i % 2 == 0 ? null : $"Se dedica a lo número {i}",
                $"Líder {i}", i % 3 == 0 ? null : "#2563EB",
                // Los pares cuelgan del anterior: en una hoja de varias filas, un padre y su
                // subequipo acaban en renglones distintos y la caja tiene que decirlo igual.
                i % 2 == 0 ? $"Equipo {i - 1}" : null,
                [new IntegranteImpreso($"Líder {i}", null, "Líder", null, true)], [], []))
            .ToList();

        EsUnPdf(Generador.OrganizacionDeEquipos(new DatosDeEquipos(equipos, [], 11, "06/08/2026 10:00")));
    }

    /// <summary>
    /// <b>Toda fila lleva barra, incluida la que se queda con una sola caja.</b>
    ///
    /// <para>Es la cuenta que decide dónde acaba la barra de la que cuelgan las cajas de un renglón, y
    /// se comprueba aquí porque el fallo que vigila no revienta nada: dibuja una línea que dice algo
    /// falso. Con cuatro equipos raíz —tres arriba y uno abajo— el de abajo se quedaba sin barra y su
    /// bajada salía justo del canto de la caja de encima, o sea que el papel decía que un equipo raíz
    /// colgaba de otro. Ninguna prueba de «que el documento salga» puede verlo, y las que reparten
    /// equipos en filas usaban 11 —tres, tres, tres y dos—, que nunca deja una caja sola.</para>
    ///
    /// <para>Medido en celdas: 0,5 es el centro de la primera, que es por donde baja su caja. Que el
    /// extremo derecho sea siempre MAYOR que 0,5 es exactamente «la barra tiene ancho».</para>
    /// </summary>
    [Theory]
    [InlineData(1, 1.5f)]   // la caja sola: la barra llega al centro del papel, donde baja el nodo
    [InlineData(2, 1.5f)]   // dos cajas: del centro de la primera al de la segunda, como siempre
    [InlineData(3, 2.5f)]   // la fila llena: hasta el centro de la tercera y ni un punto más
    public void LaBarraDeUnaFila_LlegaHastaLaUltimaBajada_yNuncaTieneAnchoCero(int cuantas, float esperado)
    {
        float derecha = GeneradorDeDocumentosQuestPdf.ExtremoDerechoDeLaBarra(cuantas);

        Assert.Equal(esperado, derecha);
        Assert.True(derecha > 0.5f,
            $"Con {cuantas} caja(s) la barra sale de ancho cero: la bajada queda suelta y parece " +
            "salir de la caja que tenga encima.");
    }

    /// <summary>
    /// Cuatro equipos raíz: tres en el primer renglón y UNO en el segundo, que es la forma que tiene
    /// hoy la casa —todos los equipos planos— en cuanto hay cuatro. Aquí solo se exige que salga; que
    /// la caja sola cuelgue de su barra y no de la de encima lo fija la prueba de arriba.
    /// </summary>
    [Fact]
    public void CuatroEquiposRaiz_DejanUnaCajaSolaEnElSegundoRenglon_ySeGenera()
    {
        var equipos = Enumerable.Range(1, 4).Select(i => Equipo($"Equipo {i}", null)).ToList();

        EsUnPdf(Generador.OrganizacionDeEquipos(new DatosDeEquipos(equipos, [], 4, "06/08/2026 10:00")));
    }

    // ── El organigrama cuando los equipos cuelgan unos de otros ─────────────────

    /// <summary>Un equipo, con su nombre y de quién cuelga, para armar árboles sin repetir la receta.</summary>
    private static EquipoImpreso Equipo(string nombre, string? padre, string? color = null, int gente = 1) =>
        new(nombre, $"Se dedica a lo de {nombre}.", gente > 0 ? $"Líder de {nombre}" : null, color, padre,
            [.. Enumerable.Range(1, gente).Select(i =>
                new IntegranteImpreso($"{nombre} · persona {i}", "Semisenior", i == 1 ? "Líder" : "Fullstack",
                    "Atiende incidencias del sistema de facturación", i == 1))],
            [$"Sistema de {nombre}"], [$"Proyecto de {nombre}"]);

    /// <summary>
    /// El caso que trae la jerarquía: un árbol de varios niveles se dibuja entero y sin reventar.
    ///
    /// <para>Las excepciones de maquetado de QuestPDF solo aparecen al generar, así que sin esta
    /// prueba el primero en enterarse de que el organigrama ya no sale sería quien aprieta el botón
    /// delante de su equipo.</para>
    /// </summary>
    [Fact]
    public void UnArbolDeVariosNiveles_SeGenera()
    {
        var datos = new DatosDeEquipos(
            [
                Equipo("Desarrollo Web", null, "#2563EB", 3),
                Equipo("Front", "Desarrollo Web", "#16A34A", 2),
                Equipo("Componentes", "Front", null, 1),
                Equipo("Accesibilidad", "Componentes", null, 1),
                Equipo("Back", "Desarrollo Web", null, 3)
            ],
            [new IntegranteImpreso("Karla Nieto", "Junior", "Sin rol", null, false)],
            11, "06/08/2026 10:00");

        EsUnPdf(Generador.OrganizacionDeEquipos(datos));
    }

    /// <summary>
    /// <b>Cada rama en su hoja, y los equipos raíz todos en la primera.</b> Es la decisión que sostiene
    /// el diagrama: un subárbol partido entre dos hojas se lee como dos organigramas distintos, así que
    /// la hoja se reparte por ramas y no por altura.
    /// </summary>
    [Fact]
    public void CadaRamaConSubequipos_ArrancaEnSuPropiaHoja()
    {
        // Dos ramas y un equipo suelto. Todo esto cabría de sobra en una hoja por altura: si el
        // reparto fuera por altura, saldría un solo papel con las dos ramas mezcladas.
        var datos = new DatosDeEquipos(
            [
                Equipo("Web", null, "#2563EB"),
                Equipo("Front", "Web"),
                Equipo("Datos", null, "#16A34A"),
                Equipo("Reportes", "Datos"),
                Equipo("Soporte", null)
            ],
            [], 5, "06/08/2026 10:00");

        var pdf = Generador.OrganizacionDeEquipos(datos);

        EsUnPdf(pdf);
        Assert.Equal(3, CuantasHojas(pdf));   // la de la organización y una por cada rama
    }

    /// <summary>
    /// <b>Sin jerarquía, el documento sigue siendo el de siempre: una sola hoja.</b>
    ///
    /// <para>Es la prueba que guarda a quien ya usa esto. La aplicación está en producción con todos
    /// los equipos planos, y el reparto por ramas no puede convertir un papel de una hoja en cinco
    /// mientras nadie cuelgue un equipo de otro.</para>
    /// </summary>
    [Fact]
    public void SinSubequipos_ElDocumentoSigueCabiendoEnUnaHoja()
    {
        var datos = new DatosDeEquipos(
            [Equipo("Web", null, "#2563EB"), Equipo("Datos", null), Equipo("Soporte", null)],
            [new IntegranteImpreso("Karla Nieto", null, "Sin rol", null, false)],
            4, "06/08/2026 10:00");

        var pdf = Generador.OrganizacionDeEquipos(datos);

        EsUnPdf(pdf);
        Assert.Equal(1, CuantasHojas(pdf));
    }

    /// <summary>
    /// <b>Con UNA sola rama no hay reparto: su árbol va en la primera hoja.</b>
    ///
    /// <para>Es la foto de esta casa —un equipo raíz y todo colgando de él— y era la que peor salía:
    /// la primera hoja gastaba tres cuartos de página en una caja sola y mandaba a pasar página para
    /// ver tres subequipos que cabían debajo de sobra. Repartir tiene sentido cuando hay varias ramas
    /// que no se pueden mezclar; con una, el reparto es la mitad del papel en blanco.</para>
    ///
    /// <para><b>El caso es el MÁS PEQUEÑO que distingue las dos maquetaciones</b>, y no el de la casa,
    /// a propósito: un padre y un hijo, sin gente. Antes daba dos hojas —una para la organización y
    /// otra para la rama— y ahora tiene que dar una. Con el árbol entero de la casa las dos
    /// maquetaciones dan dos hojas, porque el contenido desborda por altura de todas formas, y la
    /// prueba no distinguiría nada: pasaría igual con el reparto puesto y quitado.</para>
    ///
    /// <para>Y dice menos de lo que parece, que conviene saberlo: que quepa NO significa que esté bien
    /// maquetado. Eso se mira.</para>
    /// </summary>
    [Fact]
    public void UNA_SOLA_RAMA_no_abre_hoja_aparte()
    {
        var datos = new DatosDeEquipos(
            [Equipo("Desarrollo Web", null, "#2563EB"), Equipo("Soporte", "Desarrollo Web")],
            [], 2, "13/08/2026 20:26");

        var pdf = Generador.OrganizacionDeEquipos(datos);

        EsUnPdf(pdf);
        Assert.Equal(1, CuantasHojas(pdf));
    }

    /// <summary>
    /// <b>El texto que el PDF lleva DENTRO tiene que decir lo mismo que el que se ve.</b>
    ///
    /// <para>Lato une «ti» en un solo glifo y lo incrusta sin correspondencia de vuelta a las dos
    /// letras. En el papel se leía bien y el texto del archivo perdía la pareja: «prácticas» se
    /// copiaba «práccas» y «características» salía «caracteríscas». O sea que el Ctrl+F no encontraba
    /// media hoja, copiar y pegar producía faena, y un lector de pantalla lee lo que se copia.</para>
    ///
    /// <para><b>Esto no lo caza mirando el documento</b>, que es justo por lo que estuvo así: hay que
    /// abrirlo y copiar. Se comprueba sobre los bytes buscando la pareja de letras cruda dentro de la
    /// tabla de correspondencias del propio PDF, que es de donde el lector saca lo que copia.</para>
    /// </summary>
    [Fact]
    public void EL_TEXTO_DEL_PDF_no_se_come_la_pareja_ti()
    {
        var datos = new DatosDeEquipos(
            [
                new EquipoImpreso("Desarrollo", "Mantiene las plataformas.", null, "#2563EB", null,
                    [new IntegranteImpreso("Ana Ruiz", "Senior", "Líder",
                        "Supervisa las mejores prácticas de codificación en tiempo y forma.", true)],
                    [], [])
            ],
            [], 1, "13/08/2026 20:26");

        var pdf = Generador.OrganizacionDeEquipos(datos);
        EsUnPdf(pdf);

        // Un glifo que no sabe decir qué letra es se declara apuntando a <0000>. Con la ligadura
        // encendida, el de «ti» era exactamente eso.
        Assert.False(TieneGlifosSinLetra(pdf),
            "El PDF lleva glifos sin correspondencia a letra: lo que se copie de ahí saldrá incompleto.");
    }

    /// <summary>
    /// ¿Hay algún glifo declarado sin decir qué letra es? Se mira la tabla /ToUnicode del propio
    /// documento, que es de donde el lector saca el texto al copiar.
    /// </summary>
    private static bool TieneGlifosSinLetra(byte[] pdf)
    {
        foreach (var cmap in FlujosDescomprimidos(pdf))
        {
            if (!cmap.Contains("beginbfchar", StringComparison.Ordinal)
                && !cmap.Contains("beginbfrange", StringComparison.Ordinal)) continue;

            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(cmap, @"<[0-9A-Fa-f]+>\s*<(0000)+>"))
                if (m.Success) return true;
        }
        return false;
    }

    /// <summary>Los flujos del PDF, descomprimidos cuando se puede. Los que no, se ignoran: aquí solo
    /// interesan las tablas de texto, que sí lo están.</summary>
    private static IEnumerable<string> FlujosDescomprimidos(byte[] pdf)
    {
        const string abre = "stream";
        const string cierra = "endstream";
        var texto = System.Text.Encoding.Latin1.GetString(pdf);

        int i = 0;
        while ((i = texto.IndexOf(abre, i, StringComparison.Ordinal)) >= 0)
        {
            int ini = i + abre.Length;
            while (ini < texto.Length && (texto[ini] == '\r' || texto[ini] == '\n')) ini++;

            int fin = texto.IndexOf(cierra, ini, StringComparison.Ordinal);
            if (fin < 0) yield break;

            var datos = System.Text.Encoding.Latin1.GetBytes(texto[ini..fin]);
            string? claro = null;
            try
            {
                using var origen = new MemoryStream(datos);
                using var zip = new System.IO.Compression.ZLibStream(
                    origen, System.IO.Compression.CompressionMode.Decompress);
                using var destino = new MemoryStream();
                zip.CopyTo(destino);
                claro = System.Text.Encoding.Latin1.GetString(destino.ToArray());
            }
            catch
            {
                // Flujo sin comprimir o comprimido de otra forma: no es una tabla de texto.
            }

            if (claro is not null) yield return claro;
            i = fin + cierra.Length;
        }
    }

    /// <summary>Un solo equipo, sin nada colgando y sin nadie fuera: el organigrama más pequeño que existe.</summary>
    [Fact]
    public void UnSoloEquipo_SeGenera()
    {
        var pdf = Generador.OrganizacionDeEquipos(
            new DatosDeEquipos([Equipo("Web", null, "#2563EB")], [], 1, "06/08/2026 10:00"));

        EsUnPdf(pdf);
        Assert.Equal(1, CuantasHojas(pdf));
    }

    /// <summary>
    /// Una cadena más honda que los niveles con sangría. A partir del tope la sangría deja de crecer
    /// —si no, las cajas del fondo acabarían siendo una columna de palabras sueltas—, y lo que no
    /// puede pasar es que el documento se caiga o que un equipo se quede sin dibujar.
    /// </summary>
    [Fact]
    public void UnaCadenaMuyHonda_NoSeSaleDeLaHoja()
    {
        var equipos = Enumerable.Range(1, 15)
            .Select(i => Equipo($"Nivel {i}", i == 1 ? null : $"Nivel {i - 1}"))
            .ToList();

        EsUnPdf(Generador.OrganizacionDeEquipos(new DatosDeEquipos(equipos, [], 15, "06/08/2026 10:00")));
    }

    /// <summary>
    /// Una rama tan grande que no cabe en una hoja. Sigue saliendo, y sigue siendo la misma rama: las
    /// hojas que la continúan repiten en la cabecera de quién es, que es lo que impide leerlas como
    /// otro organigrama.
    /// </summary>
    [Fact]
    public void UnaRamaMasAltaQueLaHoja_SeSigueGenerando()
    {
        var equipos = new List<EquipoImpreso> { Equipo("Desarrollo", null, "#2563EB", 5) };
        equipos.AddRange(Enumerable.Range(1, 12).Select(i => Equipo($"Célula {i}", "Desarrollo", null, 6)));

        var pdf = Generador.OrganizacionDeEquipos(new DatosDeEquipos(equipos, [], 77, "06/08/2026 10:00"));

        EsUnPdf(pdf);
        Assert.True(CuantasHojas(pdf) > 2, "Una rama de doce equipos de seis personas no cabe en una hoja.");
    }

    /// <summary>
    /// UNA CAJA más alta que la hoja, dentro de una rama. Es el caso que vigila la maquetada en capas
    /// con la que se dibujan las líneas del árbol: la caja manda la altura y las verticales se ajustan
    /// a ella, y hay maquetados de QuestPDF que se niegan a partirse cuando lo que llevan dentro no
    /// cabe en una hoja. Aquí tiene que partirse igual que se partía antes.
    ///
    /// <para>Existe la gemela para la PRIMERA hoja (UnEquipoMasAltoQueLaHoja_SeSigueGenerando), que
    /// usa otro maquetado: allí las cajas van en filas y aquí en un árbol con sangría.</para>
    /// </summary>
    [Fact]
    public void UnSubequipoMasAltoQueLaHoja_SeSigueGenerando()
    {
        var equipos = new List<EquipoImpreso>
        {
            Equipo("Plataforma", null, "#2563EB", 2),
            Equipo("Célula gigante", "Plataforma", null, 80),
            Equipo("Célula pequeña", "Plataforma", null, 2)
        };

        var pdf = Generador.OrganizacionDeEquipos(new DatosDeEquipos(equipos, [], 84, "06/08/2026 10:00"));

        EsUnPdf(pdf);
        Assert.True(CuantasHojas(pdf) > 2, "Un subequipo de ochenta personas no cabe en una hoja.");
    }

    /// <summary>
    /// Un círculo escrito a mano contra la base —la aplicación no deja crearlo— ni cuelga la
    /// generación ni hace desaparecer equipos del papel.
    ///
    /// <para>Un documento que no termina de generarse no es un error que alguien pueda diagnosticar:
    /// es un botón que se queda girando y un servidor ocupado hasta que lo maten.</para>
    /// </summary>
    [Fact]
    public void UnCirculoEntreEquipos_NoDejaElDocumentoDandoVueltas()
    {
        var datos = new DatosDeEquipos(
            [Equipo("Uno", "Dos"), Equipo("Dos", "Tres"), Equipo("Tres", "Uno")],
            [], 3, "06/08/2026 10:00");

        EsUnPdf(Generador.OrganizacionDeEquipos(datos));
    }

    /// <summary>
    /// Un padre que no está en la lista se dibuja como raíz y no se pierde. Solo puede llegar así si
    /// alguien borró el equipo padre entre la consulta y la impresión, pero en un organigrama faltar
    /// no es una caja menos: es un equipo que oficialmente no está en ninguna parte.
    /// </summary>
    [Fact]
    public void UnPadreQueNoEstaEnLaLista_NoDejaAlEquipoFueraDelPapel()
    {
        var pdf = Generador.OrganizacionDeEquipos(new DatosDeEquipos(
            [Equipo("Huérfano", "Un equipo que ya no existe")], [], 1, "06/08/2026 10:00"));

        EsUnPdf(pdf);
        // Sin rama que llevarse a otra hoja: sale como un equipo raíz más de la primera.
        Assert.Equal(1, CuantasHojas(pdf));
    }

    /// <summary>
    /// Un subequipo sin color hereda el de su rama, y si en toda la rama nadie eligió ninguno se cae
    /// al gris de siempre. Lo que se comprueba aquí es que ninguno de los dos caminos impide imprimir
    /// —el color acaba en un <c>Background</c>, y un valor que QuestPDF no entienda revienta al
    /// generar, no al escribirlo—.
    /// </summary>
    [Fact]
    public void UnaRamaSinColores_SeImprimeIgual()
    {
        var datos = new DatosDeEquipos(
            [
                Equipo("Con color", null, "#2563EB"),
                Equipo("Hereda", "Con color"),
                Equipo("Sin nada", null),
                Equipo("Tampoco", "Sin nada"),
                Equipo("Mal escrito", null, "azul"),
                Equipo("Hereda lo malo", "Mal escrito")
            ],
            [], 6, "06/08/2026 10:00");

        EsUnPdf(Generador.OrganizacionDeEquipos(datos));
    }
}
