using AdminWeb.Application.Services;
using AdminWeb.Domain.Documentos;
using AdminWeb.Infrastructure.Documentos;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Los CAMPOS del documento de solicitud de vacaciones: lo que decide qué dice el papel.
///
/// Es la parte que se porta literalmente de <c>VacationDocumentService.BuildFields</c> del
/// escritorio, y la que hay que cubrir: el día de regreso saltando el fin de semana, las fechas
/// largas en español y las dos casillas de autorización. Todo eso se calcula sin base y sin PDF, así
/// que se puede comprobar carácter a carácter — que es justo lo que no se podía hacer cuando el
/// resultado era un DOCX rellenado desde una plantilla.
///
/// Las fechas de las pruebas son de agosto de 2026 a propósito: el 14 cae en viernes y el 17 en
/// lunes, que es el caso que hace visible la regla del fin de semana.
/// </summary>
public class DocumentoDeVacacionesTests
{
    private static readonly DateTime Jueves = new(2026, 8, 13);
    private static readonly DateTime Viernes = new(2026, 8, 14);
    private static readonly DateTime Sabado = new(2026, 8, 15);
    private static readonly DateTime Domingo = new(2026, 8, 16);
    private static readonly DateTime Lunes = new(2026, 8, 17);

    private static DatosDeVacaciones Campos(
        DateTime? inicio = null,
        DateTime? fin = null,
        VacationStatus estado = VacationStatus.Pendiente,
        string? respuestaDelLider = null,
        string? comentario = null,
        DateTime? ingreso = null,
        int diasPendientes = 7,
        byte[]? firma = null) =>
        DocumentoDeVacacionesService.Campos(
            nombre: "Ana Pérez",
            fechaDeIngreso: ingreso,
            diasPendientes: diasPendientes,
            inicio: inicio ?? new DateTime(2026, 8, 10),
            fin: fin ?? Viernes,
            estado: estado,
            respuestaDelLider: respuestaDelLider,
            comentario: comentario,
            departamento: "Desarrollo Web",
            puesto: "Desarrolladora",
            jefeDirecto: "Gerardo Manjarrez",
            firmaDelJefe: firma,
            hoy: new DateTime(2026, 8, 6));

    // ── El día de regreso ───────────────────────────────────────────────────────

    [Fact]
    public void SiLasVacacionesTerminanEnJueves_SeRegresaElViernes()
    {
        // Entre semana no hay nada que saltar: el día siguiente es hábil y ese es el que se imprime.
        Assert.Equal(Viernes, DocumentoDeVacacionesService.SiguienteDiaHabil(Jueves));
    }

    [Fact]
    public void SiLasVacacionesTerminanEnViernes_SeSaltaElFinDeSemana()
    {
        // Es el caso que justifica la regla: sin ella el papel citaría a la persona un sábado.
        Assert.Equal(Lunes, DocumentoDeVacacionesService.SiguienteDiaHabil(Viernes));
    }

    [Fact]
    public void SiLasVacacionesTerminanEnSabado_SeRegresaElLunes()
    {
        // Un período puede acabar en fin de semana (se pide por fechas naturales, no por días
        // hábiles). El domingo tampoco vale, así que hay que saltar dos veces.
        Assert.Equal(Lunes, DocumentoDeVacacionesService.SiguienteDiaHabil(Sabado));
    }

    [Fact]
    public void SiLasVacacionesTerminanEnDomingo_SeRegresaElLunes()
    {
        Assert.Equal(Lunes, DocumentoDeVacacionesService.SiguienteDiaHabil(Domingo));
    }

    [Fact]
    public void ElDiaDeRegresoSeImprimeConSuDiaDeLaSemana()
    {
        // El nombre del día no es adorno: es lo que permite comprobar de un vistazo que no se está
        // citando a nadie en fin de semana.
        Assert.Equal("Lunes 17 de Agosto de 2026", Campos(fin: Viernes).FechaRegreso);
    }

    // ── Las fechas en español ───────────────────────────────────────────────────

    [Fact]
    public void LasFechasSeEscribenEnEspanolYConElMesEnMayuscula()
    {
        var d = Campos(inicio: new DateTime(2026, 8, 10), fin: Viernes);

        Assert.Equal("10 de Agosto de 2026", d.FechaInicio);
        Assert.Equal("14 de Agosto de 2026", d.FechaFin);
        Assert.Equal("6 de Agosto de 2026", d.FechaSolicitud);
    }

    [Fact]
    public void ElPeriodoLlevaElAnioUnaSolaVez()
    {
        // «Del 10 de Agosto al 14 de Agosto de 2026»: el año al final y no en las dos puntas, que es
        // como lo escribía la plantilla del escritorio.
        Assert.Equal("10 de Agosto al 14 de Agosto de 2026",
            Campos(inicio: new DateTime(2026, 8, 10), fin: Viernes).Periodo);
    }

    [Fact]
    public void ElNombreDelMesNoDependeDeLaCulturaDelServidor()
    {
        // La cultura es fija dentro del servicio: el papel se archiva en el expediente de una
        // persona y tiene que leerse igual se genere donde se genere.
        Assert.Equal("1 de Enero de 2026", DocumentoDeVacacionesService.FechaLarga(new DateTime(2026, 1, 1)));
        Assert.Equal("31 de Diciembre de 2025", DocumentoDeVacacionesService.FechaLarga(new DateTime(2025, 12, 31)));
    }

    [Fact]
    public void SinFechaDeIngreso_SeImprimeUnGuion()
    {
        // La ficha puede no tenerla, y dejar el hueco en blanco haría parecer que el campo se olvidó.
        Assert.Equal("—", Campos(ingreso: null).FechaIngreso);
        Assert.Equal("1 de Marzo de 2023", Campos(ingreso: new DateTime(2023, 3, 1)).FechaIngreso);
    }

    // ── Las casillas de autorización ────────────────────────────────────────────

    [Fact]
    public void UnaSolicitudAprobada_MarcaSoloLaCasillaDeAutorizada()
    {
        var d = Campos(estado: VacationStatus.Aprobada);
        Assert.True(d.Autorizada);
        Assert.False(d.Rechazada);
    }

    [Fact]
    public void UnaSolicitudRechazada_MarcaSoloLaCasillaDeRechazada()
    {
        var d = Campos(estado: VacationStatus.Rechazada);
        Assert.False(d.Autorizada);
        Assert.True(d.Rechazada);
    }

    [Theory]
    [InlineData(VacationStatus.Pendiente)]
    [InlineData(VacationStatus.Cancelada)]
    public void SinDecision_LasDosCasillasSalenEnBlanco(VacationStatus estado)
    {
        // El borrador se emite antes de resolverse: sale con las dos vacías, que es exactamente el
        // papel que se lleva a firmar. Una cancelada tampoco es una autorización ni un rechazo.
        var d = Campos(estado: estado);
        Assert.False(d.Autorizada);
        Assert.False(d.Rechazada);
    }

    // ── Las observaciones ───────────────────────────────────────────────────────

    [Fact]
    public void LaRespuestaDelLiderMandaSobreElComentarioDeQuienPidio()
    {
        // Si hay una decisión escrita, es lo que tiene que leerse en el papel.
        Assert.Equal("Se aprueba salvo el viernes.",
            Campos(respuestaDelLider: "Se aprueba salvo el viernes.", comentario: "Viaje familiar").Observaciones);
    }

    [Fact]
    public void SinRespuesta_SeImprimeLoQueEscribioQuienPidio()
    {
        Assert.Equal("Viaje familiar", Campos(comentario: "Viaje familiar").Observaciones);
    }

    [Fact]
    public void SinNadaEscrito_LasObservacionesQuedanVaciasYNoNulas()
    {
        // El generador decide con IsNullOrWhiteSpace si imprime el bloque; una cadena vacía y un nulo
        // se comportan igual ahí, pero devolver nulo obligaría a comprobarlo en cada uso.
        Assert.Equal("", Campos().Observaciones);
    }

    // ── El resto de los campos ──────────────────────────────────────────────────

    [Fact]
    public void LosDiasCuentanElPrimeroYElUltimo()
    {
        // Del 10 al 14 son cinco días, no cuatro: es el error de un día que se cuela al restar
        // fechas sin pensarlo.
        Assert.Equal("5", Campos(inicio: new DateTime(2026, 8, 10), fin: Viernes).TotalDias);
        Assert.Equal(5, DocumentoDeVacacionesService.Dias(new DateTime(2026, 8, 10), Viernes));
        Assert.Equal(1, DocumentoDeVacacionesService.Dias(Viernes, Viernes));
    }

    [Fact]
    public void LosDatosDeLaFichaYDeLaConfiguracionSeImprimenTalCual()
    {
        var d = Campos(diasPendientes: 3);

        Assert.Equal("Ana Pérez", d.Nombre);
        Assert.Equal("Desarrollo Web", d.Departamento);
        Assert.Equal("Desarrolladora", d.Puesto);
        Assert.Equal("Gerardo Manjarrez", d.JefeDirecto);
        Assert.Equal("3", d.DiasPendientes);
    }

    [Fact]
    public void ElBorradorViajaSinFirmaYElFirmadoConElla()
    {
        Assert.Null(Campos().FirmaDelJefe);
        Assert.NotNull(Campos(estado: VacationStatus.Aprobada, firma: [1, 2, 3]).FirmaDelJefe);
    }

    // ── Los campos y el generador, juntos ───────────────────────────────────────

    [Fact]
    public void LosCamposCalculadosProducenUnPdfDeVerdad()
    {
        // Que los campos salgan bien no sirve de nada si el generador no los admite. Esta es la única
        // prueba que junta las dos piezas, y es la que atraparía un campo que dejara de encajar.
        var pdf = new GeneradorDeDocumentosQuestPdf()
            .SolicitudDeVacaciones(Campos(estado: VacationStatus.Aprobada, respuestaDelLider: "Buen viaje."));

        Assert.True(pdf.Length > 1000, $"El PDF salió de {pdf.Length} bytes: eso no es un documento.");
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }
}
