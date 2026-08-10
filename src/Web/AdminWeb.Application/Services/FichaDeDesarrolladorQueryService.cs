using AdminWeb.Domain.Documentos;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Reúne los datos de la ficha de un desarrollador y pide el PDF.
///
/// <para><b>De <c>DeveloperReportService</c> del escritorio solo se porta esta mitad.</b> Aquella
/// clase hacía dos cosas: juntar los datos y ARMAR el documento (un DOCX que se convertía a PDF con
/// LibreOffice instalado en la máquina de cada quien). Lo segundo ya está resuelto y de otra manera:
/// lo maqueta <see cref="IGeneradorDeDocumentos.FichaDeDesarrollador"/> con QuestPDF, sin depender de
/// ningún programa instalado en el servidor. Aquí no queda nada de maquetación: se recogen los datos,
/// se les da forma de <see cref="DatosDeFicha"/> y se pide el archivo.</para>
///
/// <para>Desaparece con ello la comprobación «¿está LibreOffice?» y las pantallas que ofrecían
/// corregir su ruta: ya no hay ninguna ruta que corregir.</para>
///
/// <para><b>Quién puede pedirla:</b> el líder, la de cualquiera; el desarrollador, solo la suya. Lo
/// decide <see cref="AuthorizationGuard.RequireOwnershipOrAdmin"/> aquí dentro, además de la política
/// del endpoint, porque este documento lleva las debilidades que le escribieron a una persona.</para>
/// </summary>
public class FichaDeDesarrolladorQueryService(
    AppDbContext db,
    ICurrentUser actual,
    WorkSessionService cronometros,
    IGeneradorDeDocumentos documentos)
{
    /// <summary>
    /// El PDF de la ficha y el nombre con el que se descarga.
    ///
    /// Devuelve <c>(null, mensaje)</c> cuando el desarrollador no existe: es una situación de datos,
    /// no de permisos, y quien llama la convierte en la respuesta que corresponda.
    /// </summary>
    public async Task<(byte[]? pdf, string nombreOMensaje)> FichaAsync(
        int developerId, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireLoggedIn(actual);
        AuthorizationGuard.RequireOwnershipOrAdmin(actual, developerId);

        var datos = await ReunirAsync(developerId, ct);
        if (datos == null) return (null, "El desarrollador no existe.");

        var nombreDeArchivo = ArchivosSubidos.NombreSeguro(
            $"Ficha_{datos.NombreCompleto}_{DateTime.Now:yyyyMMdd}.pdf");

        return (documentos.FichaDeDesarrollador(datos),
                nombreDeArchivo.Length == 0 ? "ficha.pdf" : nombreDeArchivo);
    }

    /// <summary>
    /// Los datos que van impresos, ya resueltos y formateados. Se expone aparte del PDF porque es lo
    /// que se puede probar sin abrir un archivo binario: el generador maqueta, no calcula.
    /// </summary>
    public async Task<DatosDeFicha?> ReunirAsync(int developerId, CancellationToken ct = default)
    {
        var dev = await db.Developers.AsNoTracking()
            .Where(d => d.Id == developerId)
            .Select(d => new
            {
                d.FullName, d.Email, d.Phone, d.Seniority, d.HireDate, d.TeamId, d.TeamRole,
                Equipo = d.Team != null ? d.Team.Name : null
            })
            .FirstOrDefaultAsync(ct);
        if (dev == null) return null;

        // El año en curso, igual que en el escritorio: la ficha se entrega en la conversación de
        // evaluación y lo que ahí importa es el ejercicio corriente, no el acumulado histórico.
        int anio = DateTime.Today.Year;
        int puntos = await db.PointEntries
            .Where(p => p.DeveloperId == developerId && p.Year == anio
                     && p.ApprovalStatus == PointApprovalStatus.Aprobado)
            .SumAsync(p => (int?)p.Points, ct) ?? 0;

        int activas = await db.Requirements.CountAsync(r =>
            r.Assignments.Any(a => a.DeveloperId == developerId)
            && r.Status != RequirementStatus.Entregado && r.Status != RequirementStatus.Cancelado, ct);

        var evaluaciones = await db.DeveloperEvaluations.AsNoTracking()
            .Where(e => e.DeveloperId == developerId)
            .OrderByDescending(e => e.EvaluationDate).ThenByDescending(e => e.Id)
            .Select(e => new EvaluacionImpresa(
                e.EvaluationDate, e.PeriodLabel, e.OverallRating,
                e.Strengths, e.Weaknesses, e.Comments, e.EvaluatorName))
            .ToListAsync(ct);

        var hitos = await db.DeveloperMilestones.AsNoTracking()
            .Where(m => m.DeveloperId == developerId)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id)
            .Select(m => new HitoImpreso(m.Date, m.Kind, m.Title, m.Description))
            .ToListAsync(ct);

        return new DatosDeFicha(
            NombreCompleto: dev.FullName,
            Correo: dev.Email,
            Telefono: dev.Phone,
            Nivel: dev.Seniority,
            Equipo: dev.Equipo,
            // Sin equipo no hay rol que enseñar: «Sin rol» al lado de «Sin equipo» se lee como si
            // fueran dos huecos distintos cuando en realidad es el mismo.
            RolEnElEquipo: dev.TeamId == null ? null : EtiquetasDeCatalogo.RolDeEquipo(dev.TeamRole),
            FechaDeIngreso: dev.HireDate,
            PuntosAprobadosDelAnio: puntos,
            TiempoTotal: WorkSessionService.Format(
                await cronometros.GetTotalSecondsByDeveloperAsync(developerId, ct)),
            AsignacionesActivas: activas,
            Evaluaciones: evaluaciones,
            Hitos: hitos,
            GeneradoEl: DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
    }
}
