using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Un aviso por dar sobre un compromiso: a quién, qué decirle y con qué clave de dedupe.</summary>
/// <param name="DiasRestantes">Negativo si la fecha ya pasó.</param>
public sealed record AvisoCompromiso(
    int RequirementId, int DeveloperId, string Titulo, DateTime FechaCompromiso,
    int DiasRestantes, string Etiqueta, string DedupeKey);

/// <summary>
/// Avisa al desarrollador de que su fecha comprometida se acerca — ANTES de que pase, no después.
///
/// La pantalla del sprint pinta en rojo el compromiso ya vencido, pero eso solo lo ve el
/// administrador y solo cuando ya es tarde. Aquí el aviso llega a quien puede hacer algo, mientras
/// todavía puede hacerlo.
///
/// Se avisa en TRES momentos y no más: a tres días, el día anterior, y el día en que vence o
/// después. Cada uno con su propia clave de dedupe, así el aviso no se repite en cada revisión
/// (cada 5 minutos) pero sí vuelve a salir cuando se cruza el siguiente umbral.
/// </summary>
public class CommitmentAlertService
{
    private readonly AppDbContext _db;
    private readonly NotificationService _notif;

    public CommitmentAlertService(AppDbContext db, NotificationService notif)
    {
        _db = db; _notif = notif;
    }

    /// <summary>Umbrales de aviso, en días restantes. De mayor a menor.</summary>
    public static readonly int[] Umbrales = [3, 1, 0];

    /// <summary>
    /// Qué avisos tocan hoy, sin tocar la base ni crear nada: puro y comprobable.
    ///
    /// Un requerimiento entregado o cancelado NO genera aviso por más vencido que esté: ya no hay
    /// nada que apurar. Y solo se avisa al cruzar un umbral, no en cada día intermedio — un aviso
    /// diario durante dos semanas se deja de leer.
    /// </summary>
    public static List<AvisoCompromiso> Calcular(
        IReadOnlyList<(Requirement req, int developerId)> asignados, DateTime hoyLocal)
    {
        var hoy = hoyLocal.Date;
        var avisos = new List<AvisoCompromiso>();

        foreach (var (r, devId) in asignados)
        {
            if (r.CommittedDeliveryDate is not { } compromiso) continue;
            if (r.Status is RequirementStatus.Entregado or RequirementStatus.Cancelado) continue;

            int restantes = (compromiso.Date - hoy).Days;

            // El umbral que le toca hoy es el MÁS CHICO de los ya cruzados: con 5 días no hay
            // aviso; con 3 toca «3 días»; con 2 sigue tocando el de 3 (misma clave → el dedupe lo
            // calla, que es justo lo que se quiere: no avisar todos los días intermedios); con -4
            // toca el de vencido.
            var cruzados = Umbrales.Where(u => restantes <= u).ToList();
            if (cruzados.Count == 0) continue;
            int umbral = cruzados.Min();

            var etiqueta = restantes switch
            {
                < 0 => $"venció hace {-restantes} día(s)",
                0   => "vence HOY",
                1   => "vence mañana",
                _   => $"vence en {restantes} días"
            };

            avisos.Add(new AvisoCompromiso(
                r.Id, devId, r.Title, compromiso.Date, restantes, etiqueta,
                // La clave incluye el umbral y la FECHA comprometida: si el administrador mueve la
                // fecha, el aviso vuelve a salir (es información nueva); si no la mueve, no se
                // repite aunque la revisión corra cada cinco minutos.
                DedupeKey: $"compromiso:{r.Id}:{compromiso:yyyyMMdd}:u{umbral}"));
        }

        return avisos;
    }

    /// <summary>
    /// Revisa los compromisos de TODOS y crea los avisos que falten. Devuelve cuántos creó.
    ///
    /// Sin guarda de rol a propósito: no expone datos a nadie —solo escribe avisos dirigidos a
    /// cada desarrollador— y la dispara el temporizador de la aplicación, que corre con la sesión
    /// que esté abierta, sea del rol que sea.
    /// </summary>
    public int RevisarYAvisar(DateTime? hoyLocal = null)
    {
        // Solo lo que puede vencer: sin fecha comprometida no hay nada que avisar, y entregado o
        // cancelado ya no corre. El filtro va en la BASE para no traer el histórico completo.
        var candidatos = _db.Requirements.AsNoTracking()
            .Where(r => r.CommittedDeliveryDate != null
                     && r.Status != RequirementStatus.Entregado
                     && r.Status != RequirementStatus.Cancelado)
            .Select(r => new { Req = r, DevIds = r.Assignments.Select(a => a.DeveloperId).ToList() })
            .ToList();

        var asignados = candidatos
            .SelectMany(c => c.DevIds.Distinct().Select(d => (c.Req, developerId: d)))
            .ToList();

        int creados = 0;
        foreach (var a in Calcular(asignados, hoyLocal ?? DateTime.Today))
        {
            bool ok = _notif.NotifyDeveloper(
                a.DeveloperId, NotificationKind.CompromisoPorVencer,
                $"Compromiso: {a.Etiqueta}",
                $"«{a.Titulo}» está comprometido para el {a.FechaCompromiso:dd/MM/yyyy} y {a.Etiqueta}.",
                url: "my-assignments", dedupeKey: a.DedupeKey);
            if (ok) creados++;
        }
        return creados;
    }
}
