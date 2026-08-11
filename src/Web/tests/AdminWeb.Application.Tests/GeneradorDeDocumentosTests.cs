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
                new EquipoImpreso("Equipo Web", "Portales y APIs", "Ana Pérez", "#2563eb",
                    [
                        new IntegranteImpreso("Ana Pérez", "Senior", "Líder", "Coordina el portal público", true),
                        new IntegranteImpreso("Beto Ruiz", null, "Backend Dev", null, false)
                    ],
                    ["Portal", "API"], ["Migración"]),
                new EquipoImpreso("Equipo Móvil", null, null, null, [], [], [])
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
            [new EquipoImpreso("Equipo", null, null, "azul",
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
                "#16A34A", mucha, [], [])],
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
                [new IntegranteImpreso($"Líder {i}", null, "Líder", null, true)], [], []))
            .ToList();

        EsUnPdf(Generador.OrganizacionDeEquipos(new DatosDeEquipos(equipos, [], 11, "06/08/2026 10:00")));
    }
}
