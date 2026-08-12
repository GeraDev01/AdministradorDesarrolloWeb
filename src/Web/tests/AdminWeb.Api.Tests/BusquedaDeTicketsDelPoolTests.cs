using AdminWeb.Client.Paginas.Pool;
using AdminWeb.Domain.Entities;
using AdminWeb.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdminWeb.Api.Tests;

/// <summary>
/// El buscador de tickets con el que la pantalla del pool escoge a qué work item se liga una
/// actividad.
///
/// <para><b>Por qué esto se prueba con la API levantada y no con una respuesta inventada.</b> Lo que
/// hay que proteger no es el código de la pantalla: es la juntura entre dos cosas que hoy encajan y
/// que nadie obliga a seguir encajando. El buscador global contesta con la línea que se ENSEÑA
/// —«#4821  Corregir el reporte»— y la pantalla saca de ahí el número del work item con el que va a
/// ligar. Escrita la prueba contra un texto de mentira, el día que aquella ruta cambie el formato la
/// prueba seguiría en verde y la pantalla dejaría de encontrar tickets sin que nada avisara.</para>
///
/// <para>Por eso aquí corre la MISMA extensión que usa el navegador
/// (<c>LlamadasDeDevOpsDelPool.BuscarTicketsAsync</c>) sobre la respuesta AUTÉNTICA de
/// <c>/api/busqueda</c>, con tickets sembrados en la base. Si esa juntura se rompe, se rompe aquí.</para>
///
/// <para><b>Lo que más importa de todo esto es lo que NO puede pasar:</b> que se ofrezca un número
/// que no sea el del ticket. Ligar mal escribe el esfuerzo y la prioridad del pool en un work item
/// ajeno, y eso no se nota hasta que alguien lo lee allá. Que el buscador se quede corto es
/// aceptable —se escribe el número a mano—; que acierte de más, no.</para>
/// </summary>
public class BusquedaDeTicketsDelPoolTests(ApiDePrueba api) : IClassFixture<ApiDePrueba>
{
    /// <summary>
    /// Una palabra que no aparece en ninguna otra siembra de estas pruebas: la base es compartida por
    /// toda la clase y un término común haría que una prueba encontrara lo que sembró otra.
    /// </summary>
    private const string Termino = "Escaramujo";

    private const int NumeroDelTicket = 480215;

    [Fact]
    public async Task BuscandoPorTitulo_seOfreceElTicket_conSuNumeroYSuTitulo()
    {
        await SembrarTicketAsync(NumeroDelTicket, $"Corregir el reporte de {Termino}", "Active");
        var cliente = await api.ClienteAdminAsync();

        var (tickets, motivo) = await cliente.BuscarTicketsAsync(Termino);

        var ticket = Assert.Single(tickets);
        Assert.Equal(NumeroDelTicket, ticket.Numero);
        Assert.Equal($"Corregir el reporte de {Termino}", ticket.Titulo);

        // El estado viaja en la línea de detalle porque es con lo que se descarta un ticket ya
        // cerrado sin tener que abrirlo.
        Assert.Contains("Active", ticket.Detalle);
        Assert.Empty(motivo);
    }

    /// <summary>
    /// Quien tiene el número a mano lo escribe en la caja de búsqueda, no en la del número: es la
    /// misma caja para las dos formas de acordarse de un ticket, y si buscar por número no encontrara
    /// nada, la pantalla diría «no existe» de uno que sí existe.
    /// </summary>
    [Fact]
    public async Task BuscandoPorNumero_seEncuentraElMismoTicket()
    {
        await SembrarTicketAsync(NumeroDelTicket + 1, $"Otro asunto de {Termino}", "New");
        var cliente = await api.ClienteAdminAsync();

        var (tickets, _) = await cliente.BuscarTicketsAsync((NumeroDelTicket + 1).ToString());

        Assert.Contains(tickets, t => t.Numero == NumeroDelTicket + 1);
    }

    /// <summary>
    /// El buscador global devuelve de todo —requerimientos, personas, artículos— en la misma lista, y
    /// esta pantalla solo puede ligar work items. Un requerimiento colado aquí se ofrecería con su
    /// identificador LOCAL como si fuera el número de un ticket de Azure DevOps, y ligar por él
    /// escribiría en el work item de cualquier otro.
    /// </summary>
    [Fact]
    public async Task LoQueNoEsUnTicket_noSeOfreceParaLigar()
    {
        int requerimiento = await SembrarRequerimientoAsync($"Requerimiento de {Termino} sin ticket");
        var cliente = await api.ClienteAdminAsync();

        var (tickets, _) = await cliente.BuscarTicketsAsync(Termino);

        Assert.DoesNotContain(tickets, t => t.Numero == requerimiento);
        Assert.All(tickets, t => Assert.DoesNotContain("Requerimiento", t.Titulo));
    }

    /// <summary>
    /// Sin coincidencias no se contesta con una lista vacía a secas: el hueco se lee como «ese ticket
    /// no existe» cuando lo que suele pasar es que todavía no se ha sincronizado aquí, y se puede
    /// ligar igual escribiendo el número. Esa salida hay que nombrarla o nadie la encuentra.
    /// </summary>
    [Fact]
    public async Task SinCoincidencias_seDiceQueElNumeroSePuedeEscribirAMano()
    {
        var cliente = await api.ClienteAdminAsync();

        var (tickets, motivo) = await cliente.BuscarTicketsAsync("Zurriburri-que-no-existe");

        Assert.Empty(tickets);
        Assert.Contains("número", motivo);
    }

    /// <summary>
    /// Un texto demasiado corto no viaja: el servidor tampoco busca con menos de dos caracteres, y un
    /// viaje que vuelve vacío se lee como «no hay ningún ticket así».
    /// </summary>
    [Fact]
    public async Task ConUnaSolaLetra_niSiquieraSePregunta()
    {
        var cliente = await api.ClienteAdminAsync();

        var (tickets, motivo) = await cliente.BuscarTicketsAsync("4");

        Assert.Empty(tickets);
        Assert.Contains("dos caracteres", motivo);
    }

    // ── Siembra ──────────────────────────────────────────────────────────────

    private async Task SembrarTicketAsync(int numero, string titulo, string estado)
    {
        using var ambito = api.Services.CreateScope();
        var db = ambito.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.DevOpsTickets.AnyAsync(t => t.ExternalId == numero)) return;

        db.DevOpsTickets.Add(new DevOpsTicket
        {
            ExternalId = numero,
            Title = titulo,
            State = estado,
            WorkItemType = "Bug",
            Url = $"https://dev.azure.com/pruebas/_workitems/edit/{numero}"
        });

        await db.SaveChangesAsync();
    }

    private async Task<int> SembrarRequerimientoAsync(string titulo)
    {
        using var ambito = api.Services.CreateScope();
        var db = ambito.ServiceProvider.GetRequiredService<AppDbContext>();

        var existente = await db.Requirements.FirstOrDefaultAsync(r => r.Title == titulo);
        if (existente != null) return existente.Id;

        var requerimiento = new Requirement { Title = titulo };
        db.Requirements.Add(requerimiento);
        await db.SaveChangesAsync();

        return requerimiento.Id;
    }
}
