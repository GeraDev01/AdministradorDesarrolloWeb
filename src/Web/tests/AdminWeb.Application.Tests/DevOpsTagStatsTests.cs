using AdminWeb.Application.Services;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Tablero por etiqueta de los tickets de DevOps: cuántos bugs, tareas y demás tiene cada cliente o
/// categoría, y el «entrar a la etiqueta» para ver con qué otras coexiste. Cálculo puro, sin base ni
/// red. Portado del escritorio.
/// </summary>
public class DevOpsTagStatsTests
{
    private static TicketParaEtiquetas T(string etiquetas, string tipo, string estado) =>
        new(etiquetas, tipo, estado);

    [Fact]
    public void Partir_separa_limpia_y_quita_repetidas()
    {
        var partes = DevOpsTagStats.Partir("Bug; Bepensa | Bug ,  ");
        Assert.Equal(new[] { "Bug", "Bepensa" }, partes);
    }

    [Fact]
    public void EsCerrado_reconoce_los_estados_finales()
    {
        Assert.True(DevOpsTagStats.EsCerrado("Done"));
        Assert.True(DevOpsTagStats.EsCerrado(" Closed "));
        Assert.True(DevOpsTagStats.EsCerrado("Removed"));
        Assert.False(DevOpsTagStats.EsCerrado("Active"));
        Assert.False(DevOpsTagStats.EsCerrado(null));
    }

    /// <summary>
    /// Aquí «resolved» SÍ cuenta como cerrado y en el resto de la aplicación no. La diferencia viene
    /// del escritorio y es deliberada: este tablero cuenta lo que falta por ATENDER.
    /// </summary>
    [Fact]
    public void Resuelto_cuenta_como_cerrado_solo_en_este_tablero()
    {
        Assert.True(DevOpsTagStats.EsCerrado("Resolved"));
        Assert.False(DevOpsService.EsCerrado("Resolved"));
    }

    [Fact]
    public void Agregar_cuenta_por_etiqueta_y_clasifica_por_tipo()
    {
        var tickets = new[]
        {
            T("Bepensa;Bug",   "Bug",        "Active"),
            T("Bepensa;Tarea", "Task",       "New"),
            T("Bepensa",       "User Story", "Done"),
            T("Otro",          "Bug",        "Active"),
        };

        var filas = DevOpsTagStats.Agregar(tickets);

        var bepensa = Assert.Single(filas, f => f.Etiqueta == "Bepensa");
        Assert.Equal(3, bepensa.Total);
        Assert.Equal(2, bepensa.Abiertos);      // el User Story está Done
        Assert.Equal(1, bepensa.Bugs);
        Assert.Equal(1, bepensa.Tareas);
        Assert.Equal(1, bepensa.UserStories);

        // Las etiquetas «Bug» y «Tarea» también son filas por sí mismas.
        Assert.Contains(filas, f => f.Etiqueta == "Bug");
        Assert.Contains(filas, f => f.Etiqueta == "Otro");
    }

    [Fact]
    public void Agregar_ordena_por_total_descendente()
    {
        var tickets = new[]
        {
            T("A", "Bug", "Active"),
            T("B", "Bug", "Active"),
            T("B", "Bug", "Active"),
        };

        var filas = DevOpsTagStats.Agregar(tickets);

        Assert.Equal("B", filas[0].Etiqueta);   // 2 supera a 1
        Assert.Equal("A", filas[1].Etiqueta);
    }

    [Fact]
    public void Entrar_a_una_etiqueta_muestra_con_cuales_coexiste_y_no_se_lista_a_si_misma()
    {
        var tickets = new[]
        {
            T("Bepensa;Bug",   "Bug",  "Active"),
            T("Bepensa;Tarea", "Task", "New"),
            T("Otro;Bug",      "Bug",  "Active"),   // fuera del subconjunto Bepensa
        };

        var filas = DevOpsTagStats.Agregar(tickets, dentroDe: "Bepensa");

        Assert.DoesNotContain(filas, f => f.Etiqueta == "Bepensa");
        Assert.DoesNotContain(filas, f => f.Etiqueta == "Otro");
        var bug = Assert.Single(filas, f => f.Etiqueta == "Bug");
        Assert.Equal(1, bug.Total);
        Assert.Contains(filas, f => f.Etiqueta == "Tarea");
    }

    [Fact]
    public void Solo_abiertos_excluye_los_cerrados()
    {
        var tickets = new[]
        {
            T("X", "Bug", "Active"),
            T("X", "Bug", "Done"),
        };

        Assert.Equal(2, DevOpsTagStats.Agregar(tickets).Single(f => f.Etiqueta == "X").Total);
        Assert.Equal(1, DevOpsTagStats.Agregar(tickets, soloAbiertos: true).Single(f => f.Etiqueta == "X").Total);
    }

    [Fact]
    public void La_etiqueta_bug_clasifica_como_bug_aunque_el_tipo_no_lo_diga()
    {
        // En la organización se etiqueta «bug» sobre work items de tipo Issue cuando el proceso no
        // ofrece el tipo Bug; el tablero tiene que contarlo igual.
        var tickets = new[] { T("Bepensa;bug", "Issue", "Active") };
        Assert.Equal(1, DevOpsTagStats.Agregar(tickets).Single(f => f.Etiqueta == "Bepensa").Bugs);
    }

    [Fact]
    public void EtiquetasDistintas_las_devuelve_todas_ordenadas_y_sin_repetir()
    {
        var tickets = new[]
        {
            T("Bepensa;Bug", "Bug",  "Active"),
            T("Bug;Tarea",   "Task", "New"),
        };

        Assert.Equal(new[] { "Bepensa", "Bug", "Tarea" }, DevOpsTagStats.EtiquetasDistintas(tickets));
    }

    /// <summary>
    /// Los indicadores cuentan por TICKET y las filas por ETIQUETA. Un ticket con tres etiquetas suma
    /// tres filas pero es un solo bug, y confundirlo haría que «¿cuántos bugs tiene este cliente?»
    /// devolviera un número inflado.
    /// </summary>
    [Fact]
    public void Los_indicadores_cuentan_tickets_no_etiquetas()
    {
        var tickets = new[] { T("Bepensa;Bug;Urgente", "Bug", "Active") };

        var (total, bugs, tareas, historias) = DevOpsTagStats.Indicadores(tickets);

        Assert.Equal(1, total);
        Assert.Equal(1, bugs);
        Assert.Equal(0, tareas);
        Assert.Equal(0, historias);

        // …mientras que como filas aparece en las tres etiquetas.
        Assert.Equal(3, DevOpsTagStats.Agregar(tickets).Count);
    }

    [Fact]
    public void Los_indicadores_respetan_el_subconjunto_en_el_que_se_entro()
    {
        var tickets = new[]
        {
            T("Bepensa", "Bug",  "Active"),
            T("Otro",    "Bug",  "Active"),
        };

        var (total, bugs, _, _) = DevOpsTagStats.Indicadores(tickets, dentroDe: "Bepensa");

        Assert.Equal(1, total);
        Assert.Equal(1, bugs);
    }
}
