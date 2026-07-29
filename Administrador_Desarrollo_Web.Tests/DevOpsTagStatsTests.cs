using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Dashboard por TAG de los tickets de DevOps: cuántos bugs/tareas/etc. tiene cada cliente o etiqueta,
/// y el «entrar al tag» para ver la co-ocurrencia (p. ej. dentro de «Bepensa», cuántos son Bug).
/// Lógica pura, sin base ni red.
/// </summary>
public class DevOpsTagStatsTests
{
    private static TagStatInput T(string tags, string tipo, string estado) => new(tags, tipo, estado);

    [Fact]
    public void SplitTags_separa_limpia_y_deduplica()
    {
        var r = DevOpsTagStats.SplitTags("Bug; Bepensa | Bug ,  ");
        Assert.Equal(new[] { "Bug", "Bepensa" }, r);
    }

    [Fact]
    public void EsCerrado_reconoce_estados_finales()
    {
        Assert.True(DevOpsTagStats.EsCerrado("Done"));
        Assert.True(DevOpsTagStats.EsCerrado(" Closed "));
        Assert.True(DevOpsTagStats.EsCerrado("Removed"));
        Assert.False(DevOpsTagStats.EsCerrado("Active"));
        Assert.False(DevOpsTagStats.EsCerrado(null));
    }

    [Fact]
    public void Aggregate_cuenta_por_tag_y_clasifica_por_tipo()
    {
        var tickets = new[]
        {
            T("Bepensa;Bug",   "Bug",        "Active"),
            T("Bepensa;Tarea", "Task",       "New"),
            T("Bepensa",       "User Story", "Done"),
            T("Otro",          "Bug",        "Active"),
        };

        var rows = DevOpsTagStats.Aggregate(tickets);

        var bepensa = Assert.Single(rows, r => r.Tag == "Bepensa");
        Assert.Equal(3, bepensa.Total);
        Assert.Equal(2, bepensa.Abiertos);      // el User Story está Done
        Assert.Equal(1, bepensa.Bugs);
        Assert.Equal(1, bepensa.Tareas);
        Assert.Equal(1, bepensa.UserStories);

        // Los tags "Bug"/"Tarea" también son filas por sí mismos.
        Assert.Contains(rows, r => r.Tag == "Bug");
        Assert.Contains(rows, r => r.Tag == "Otro");
    }

    [Fact]
    public void Aggregate_ordena_por_total_descendente()
    {
        var tickets = new[]
        {
            T("A", "Bug", "Active"),
            T("B", "Bug", "Active"),
            T("B", "Bug", "Active"),
        };
        var rows = DevOpsTagStats.Aggregate(tickets);
        Assert.Equal("B", rows[0].Tag);   // 2 supera a 1
        Assert.Equal("A", rows[1].Tag);
    }

    [Fact]
    public void Aggregate_entrar_al_tag_muestra_coocurrencia_y_no_se_lista_a_si_mismo()
    {
        var tickets = new[]
        {
            T("Bepensa;Bug",   "Bug",  "Active"),
            T("Bepensa;Tarea", "Task", "New"),
            T("Otro;Bug",      "Bug",  "Active"),   // fuera del subconjunto Bepensa
        };

        var rows = DevOpsTagStats.Aggregate(tickets, dentroDelTag: "Bepensa");

        Assert.DoesNotContain(rows, r => r.Tag == "Bepensa");          // no se lista a sí mismo
        Assert.DoesNotContain(rows, r => r.Tag == "Otro");            // ese ticket no tenía Bepensa
        var bug = Assert.Single(rows, r => r.Tag == "Bug");
        Assert.Equal(1, bug.Total);
        Assert.Contains(rows, r => r.Tag == "Tarea");
    }

    [Fact]
    public void Aggregate_solo_abiertos_excluye_cerrados()
    {
        var tickets = new[]
        {
            T("X", "Bug", "Active"),
            T("X", "Bug", "Done"),
        };
        var cerradosIncluidos = DevOpsTagStats.Aggregate(tickets).Single(r => r.Tag == "X");
        Assert.Equal(2, cerradosIncluidos.Total);

        var soloAbiertos = DevOpsTagStats.Aggregate(tickets, soloAbiertos: true).Single(r => r.Tag == "X");
        Assert.Equal(1, soloAbiertos.Total);
    }

    [Fact]
    public void Aggregate_tag_bug_clasifica_como_bug_aunque_el_tipo_no_lo_diga()
    {
        var tickets = new[] { T("Bepensa;bug", "Issue", "Active") };
        var bepensa = DevOpsTagStats.Aggregate(tickets).Single(r => r.Tag == "Bepensa");
        Assert.Equal(1, bepensa.Bugs);
    }

    [Fact]
    public void TagsDistintos_devuelve_todos_ordenados_sin_repetir()
    {
        var tickets = new[]
        {
            T("Bepensa;Bug", "Bug", "Active"),
            T("Bug;Tarea",   "Task", "New"),
        };
        Assert.Equal(new[] { "Bepensa", "Bug", "Tarea" }, DevOpsTagStats.TagsDistintos(tickets));
    }
}
