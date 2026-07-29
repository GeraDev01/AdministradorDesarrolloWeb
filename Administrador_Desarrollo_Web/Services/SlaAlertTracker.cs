using Administrador_Desarrollo_Web.Models;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Decide de qué compromisos hay que avisar en cada revisión, para no repetir el mismo aviso una y
/// otra vez pero tampoco quedarse callado cuando aparece algo nuevo.
///
/// Reglas:
///  · un compromiso se avisa la primera vez que aparece como pendiente;
///  · vuelve a avisarse si pasa a estar FUERA DE PLAZO (es información nueva, más grave);
///  · deja de recordarse en cuanto sale de la lista de pendientes (porque se comentó o se cerró),
///    de modo que su siguiente recordatorio vuelva a avisar.
///
/// Vive fuera de la ventana porque es la parte con estado y con casos límite; ahí dentro no se
/// podría probar.
/// </summary>
public class SlaAlertTracker
{
    private readonly HashSet<int> _avisados = [];
    private readonly HashSet<int> _avisadosVencidos = [];

    /// <summary>
    /// Registra la revisión actual y devuelve solo aquello de lo que toca avisar ahora.
    /// </summary>
    public List<SlaCommitment> Nuevos(IReadOnlyList<SlaCommitment> pendientes, DateTime nowUtc)
    {
        // Lo que ya no está pendiente se olvida: si vuelve a tocar, vuelve a avisar.
        var vigentes = pendientes.Select(p => p.Id).ToHashSet();
        _avisados.IntersectWith(vigentes);
        _avisadosVencidos.IntersectWith(vigentes);

        var nuevos = pendientes
            .Where(p => !_avisados.Contains(p.Id)
                     || (p.EstaVencido(nowUtc) && !_avisadosVencidos.Contains(p.Id)))
            .ToList();

        foreach (var p in nuevos)
        {
            _avisados.Add(p.Id);
            if (p.EstaVencido(nowUtc)) _avisadosVencidos.Add(p.Id);
        }

        return nuevos;
    }

    /// <summary>Olvida todo (por ejemplo al cerrar sesión).</summary>
    public void Reiniciar()
    {
        _avisados.Clear();
        _avisadosVencidos.Clear();
    }
}
