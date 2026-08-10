using System.IO.Compression;
using System.Text;
using AdminWeb.Domain.Documentos;
using AdminWeb.Infrastructure.Documentos;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// La solicitud de vacaciones rellenada sobre la plantilla de Word que el área puede editar.
///
/// <para>Lo que estas pruebas cuidan es que <b>no salga ningún marcador impreso en el documento</b>.
/// Es el fallo que más caro sale de todo el porte: no revienta, no aparece en ningún registro, y se
/// descubre cuando alguien ya tiene en la mano un papel oficial que pone «{{NOMBRE}}».</para>
///
/// <para>La prueba clave es la de los marcadores PARTIDOS. Word divide el texto en «runs» por
/// motivos suyos —una corrección ortográfica, haberlo tecleado en dos veces— y entonces
/// «{{NOMBRE}}» vive como «{{NOM» + «BRE}}». Con la plantilla de fábrica no pasa; empieza a pasar el
/// día en que RH la abre, cambia una palabra y la vuelve a guardar. O sea: justo cuando esta función
/// existe para ser usada.</para>
/// </summary>
public class PlantillaDeVacacionesTests
{
    private static readonly PlantillaDeVacacionesOpenXml Word = new();

    private static DatosDeVacaciones Datos(bool autorizada = true) => new(
        Nombre: "Ana García", FechaSolicitud: "09/08/2026", Departamento: "Desarrollo",
        Puesto: "Senior", JefeDirecto: "Gerardo", FechaIngreso: "01/01/2020",
        TotalDias: "10", Periodo: "2026", FechaInicio: "17/08/2026", FechaFin: "28/08/2026",
        FechaRegreso: "31/08/2026", DiasPendientes: "12",
        Autorizada: autorizada, Rechazada: !autorizada,
        Observaciones: "Cubre Beto.", FirmaDelJefe: null);

    /// <summary>Todo el texto del .docx, ya sin etiquetas XML: lo que se leería impreso.</summary>
    private static string TextoDe(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var sb = new StringBuilder();

        foreach (var entrada in zip.Entries.Where(e => e.FullName.EndsWith(".xml")))
        {
            using var lector = new StreamReader(entrada.Open());
            var xml = lector.ReadToEnd();
            // Se quitan las etiquetas: un token partido entre dos <w:t> solo se ve así, que es
            // exactamente como lo ve quien lee el papel.
            sb.Append(System.Text.RegularExpressions.Regex.Replace(xml, "<[^>]+>", ""));
        }
        return sb.ToString();
    }

    // ── La plantilla de fábrica ──────────────────────────────────────────────────

    [Fact]
    public void La_plantilla_de_fabrica_esta_incrustada_y_es_valida()
    {
        var fabrica = Word.DeFabrica();

        Assert.NotEmpty(fabrica);
        var v = Word.Validar(fabrica);
        Assert.True(v.Ok, v.Mensaje);
        Assert.Empty(v.TokensFaltantes);
    }

    [Fact]
    public void Rellenar_sustituye_TODOS_los_marcadores()
    {
        var docx = Word.Rellenar(Word.DeFabrica(), Datos(), null);
        var texto = TextoDe(docx);

        // Ni uno. Si quedara alguno, saldría impreso en el papel.
        Assert.DoesNotContain("{{", texto);

        Assert.Contains("Ana García", texto);
        Assert.Contains("17/08/2026", texto);
        Assert.Contains("Cubre Beto.", texto);
    }

    [Fact]
    public void La_casilla_de_autorizacion_marca_solo_la_que_toca()
    {
        var si = TextoDe(Word.Rellenar(Word.DeFabrica(), Datos(autorizada: true), null));
        var no = TextoDe(Word.Rellenar(Word.DeFabrica(), Datos(autorizada: false), null));

        Assert.NotEqual(si, no);
        Assert.DoesNotContain("{{AUTORIZA", si);
        Assert.DoesNotContain("{{AUTORIZA", no);
    }

    [Fact]
    public void Rellenar_NO_modifica_la_plantilla_que_recibe()
    {
        // OpenXml escribe sobre el búfer del MemoryStream. Sin trabajar sobre una copia, la primera
        // emisión corrompería la plantilla guardada en la base y las siguientes fallarían.
        var original = Word.DeFabrica();
        var copiaDeControl = original.ToArray();

        Word.Rellenar(original, Datos(), null);

        Assert.Equal(copiaDeControl, original);
    }

    // ── La firma ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Sin_firma_el_ancla_se_borra_en_vez_de_imprimirse()
    {
        var texto = TextoDe(Word.Rellenar(Word.DeFabrica(), Datos(), null));

        Assert.DoesNotContain("FIRMA_GERENTE", texto);
    }

    [Fact]
    public void Con_firma_el_documento_lleva_la_imagen_dentro()
    {
        var png = PngDeUnPixel();
        var docx = Word.Rellenar(Word.DeFabrica(), Datos(), new FirmaEnPng(png, 190, 60));

        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        Assert.Contains(zip.Entries, e => e.FullName.Contains("media/") && e.FullName.EndsWith(".png"));
        Assert.DoesNotContain("FIRMA_GERENTE", TextoDe(docx));
    }

    /// <summary>
    /// Medidas en cero: el escritorio caía a 9525 EMU, que es UN píxel, y la firma salía como un
    /// punto invisible en mitad del papel sin que nada avisara.
    /// </summary>
    [Fact]
    public void Una_firma_sin_medidas_no_sale_de_un_pixel()
    {
        var docx = Word.Rellenar(Word.DeFabrica(), Datos(), new FirmaEnPng(PngDeUnPixel(), 0, 0));

        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var lector = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = lector.ReadToEnd();

        // 190 px × 9525 EMU = 1 809 750. Lo que NO puede aparecer es cx="9525".
        Assert.Contains("1809750", xml);
        Assert.DoesNotContain("cx=\"9525\"", xml);
    }

    // ── Validación de lo que suben ───────────────────────────────────────────────

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 })]              // un .exe renombrado
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D })]        // un PDF
    public void Lo_que_no_es_un_docx_se_rechaza_por_sus_BYTES(byte[] basura)
    {
        var v = Word.Validar(basura);

        Assert.False(v.Ok);
        Assert.NotEmpty(v.Mensaje);
    }

    [Fact]
    public void Una_plantilla_a_la_que_le_faltan_marcadores_se_rechaza_nombrandolos()
    {
        var incompleta = DocxConTexto("Hola {{NOMBRE}}, van del {{FECHA_INICIO}} al {{FECHA_FIN}}.");

        var v = Word.Validar(incompleta);

        Assert.False(v.Ok);
        Assert.NotEmpty(v.TokensFaltantes);
        Assert.Contains("{{FECHA_REGRESO}}", v.TokensFaltantes);
        // Se nombran para poder corregirlos de una vez, no de uno en uno.
        Assert.Contains("{{FECHA_REGRESO}}", v.Mensaje);
    }

    // ── El caso que de verdad rompe: marcadores partidos entre runs ──────────────

    /// <summary>
    /// Word parte «{{NOMBRE}}» en varios <c>&lt;w:t&gt;</c> en cuanto alguien edita la plantilla.
    /// Sin normalizar, la sustitución no encuentra nada y el marcador sale impreso.
    /// </summary>
    [Fact]
    public void Un_marcador_PARTIDO_entre_runs_tambien_se_sustituye()
    {
        var partida = DocxConRunsPartidos();

        // Primero: la validación tiene que aceptarla, porque el texto ENTERO sí lleva el marcador.
        var v = Word.Validar(partida);
        Assert.Contains("{{NOMBRE}}", string.Concat(TextoDe(partida)));

        var relleno = Word.Rellenar(partida, Datos(), null);
        var texto = TextoDe(relleno);

        Assert.Contains("Ana García", texto);
        Assert.DoesNotContain("{{NOM", texto);
        Assert.DoesNotContain("{{NOMBRE}}", texto);
    }

    // ── Andamiaje ────────────────────────────────────────────────────────────────

    /// <summary>Un PNG de 1×1 válido, para no depender de ningún archivo.</summary>
    private static byte[] PngDeUnPixel() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>Un .docx mínimo con un párrafo de texto.</summary>
    private static byte[] DocxConTexto(string texto) => DocxCon(
        $"<w:p><w:r><w:t xml:space=\"preserve\">{texto}</w:t></w:r></w:p>");

    /// <summary>Un .docx donde «{{NOMBRE}}» está partido en tres runs, como lo deja Word.</summary>
    private static byte[] DocxConRunsPartidos() => DocxCon(
        "<w:p>" +
        "<w:r><w:t xml:space=\"preserve\">Hola {{NOM</w:t></w:r>" +
        "<w:r><w:t xml:space=\"preserve\">BR</w:t></w:r>" +
        "<w:r><w:t xml:space=\"preserve\">E}}, buenos días.</w:t></w:r>" +
        "</w:p>");

    private static byte[] DocxCon(string cuerpo)
    {
        var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            Escribir(zip, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);

            Escribir(zip, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            Escribir(zip, "word/document.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                $"<w:body>{cuerpo}</w:body></w:document>");
        }
        return memoria.ToArray();
    }

    private static void Escribir(ZipArchive zip, string nombre, string contenido)
    {
        using var flujo = new StreamWriter(zip.CreateEntry(nombre).Open(), new UTF8Encoding(false));
        flujo.Write(contenido);
    }
}
