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

    // ── Organización de equipos ─────────────────────────────────────────────────

    [Fact]
    public void LaOrganizacionDeEquipos_SeGenera()
    {
        var datos = new DatosDeEquipos(
            [
                new EquipoImpreso("Equipo Web", "Portales y APIs", "Ana Pérez", "#2563eb",
                    ["Ana Pérez", "Beto Ruiz"], ["Portal", "API"], ["Migración"]),
                new EquipoImpreso("Equipo Móvil", null, null, null, [], [], [])
            ],
            ["Carla Díaz"],
            "06/08/2026 10:00");

        EsUnPdf(Generador.OrganizacionDeEquipos(datos));
    }

    [Fact]
    public void UnColorDeEquipoMalEscrito_NoImpideImprimir()
    {
        // El color lo teclea una persona en una pantalla. Que se equivoque no puede dejar sin
        // documento a todo el mundo; se cae al color de siempre y el papel sale igual.
        var datos = new DatosDeEquipos(
            [new EquipoImpreso("Equipo", null, null, "azul", ["Ana"], [], [])],
            [], "06/08/2026 10:00");

        EsUnPdf(Generador.OrganizacionDeEquipos(datos));
    }

    [Fact]
    public void SinEquipos_SeGeneraIgual()
    {
        EsUnPdf(Generador.OrganizacionDeEquipos(new DatosDeEquipos([], [], "06/08/2026 10:00")));
    }
}
