using System.Text;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Equipos;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Integraciones;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Administracion;
using AdminWeb.Shared.Enums;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>El período y a quién se mira. Vacío en <paramref name="Devs"/> significa «todos».</summary>
/// <param name="Desde">Primer día del período, a medianoche.</param>
/// <param name="Hasta">
/// Último día del período, a medianoche. Los generadores que comparan contra un instante suman un
/// día, igual que en el escritorio: nadie entiende «hasta el 5» como «hasta las 00:00 del 5».
/// </param>
public sealed record ParametrosDeReporte(DateTime Desde, DateTime Hasta, HashSet<int> Devs);

/// <summary>Una tabla de reporte recién calculada, con los valores todavía en su tipo original.</summary>
public sealed record TablaDeReporte(string[] Columnas, List<object?[]> Filas);

/// <summary>
/// El centro de reportes.
///
/// En el escritorio los quince generadores vivían dentro de <c>ReportsControl</c> y el servicio solo
/// sabía escribir un .xlsx. Aquí los generadores son el servicio: la pantalla dejó de ser el sitio
/// donde vive el cálculo porque la pantalla ahora corre en el navegador de cada quien, y una
/// consulta que recorre todos los requerimientos del histórico no puede estar del otro lado de la
/// frontera.
///
/// DOS DECISIONES DEL ESCRITORIO QUE SE CONSERVAN Y CONVIENE NO PERDER:
///
/// · <b>Cada reporte declara qué columna agrupa y cuál suma</b> (<c>ColCategoria</c> y
///   <c>ColValor</c>, con -1 para «cuenta filas»). De ahí salen la gráfica y las cifras grandes sin
///   escribir un tablero por reporte: es lo que hace que agregar el reporte número dieciséis sea una
///   línea y no una pantalla.
///
/// · <b>El período se interpreta como el escritorio</b>: dos fechas «peladas», sin huso. Varios
///   generadores las comparan contra columnas guardadas en UTC, así que el corte puede caer unas
///   horas antes o después del día natural. Es una imprecisión conocida y se conserva a propósito:
///   mientras las dos aplicaciones convivan, el mismo reporte pedido en una y en otra tiene que dar
///   la misma cifra, y «arreglarlo» aquí haría que no cuadraran sin que nadie supiera cuál miente.
///
/// EL ENVÍO POR CORREO SÍ ESTÁ, y con una diferencia que importa: el escritorio escribía el .xlsx en
/// una carpeta temporal —que nunca borraba— y le pasaba la RUTA al formulario de redacción. Aquí la
/// hoja se genera en memoria y viaja como contenido adjunto; no toca el disco del servidor en ningún
/// momento. El reporte se vuelve a generar para el envío por lo mismo que para la descarga: lo que la
/// pantalla tiene pintado es texto, y de ahí solo saldría una hoja de puros textos.
/// </summary>
public class ReportesService(
    AppDbContext db,
    ICurrentUser currentUser,
    SettingsService ajustes,
    IClienteDeCorreo correo,
    AuditService bitacora)
{
    /// <summary>
    /// Un reporte: cómo se calcula y cómo se resume.
    /// </summary>
    /// <param name="ColCategoria">Índice de la columna por la que se agrupa la gráfica.</param>
    /// <param name="ColValor">Índice de la columna numérica que se suma; -1 = contar filas.</param>
    private sealed record Definicion(
        string Clave,
        string Nombre,
        string Descripcion,
        Func<ParametrosDeReporte, CancellationToken, Task<TablaDeReporte>> Generar,
        int ColCategoria,
        int ColValor,
        string TituloDeGrafica);

    // Se construye al pedirla y no en un campo estático porque cada generador es un método de
    // instancia: necesita el contexto de datos de ESTA petición.
    private IReadOnlyList<Definicion>? _definiciones;

    private IReadOnlyList<Definicion> Definiciones => _definiciones ??=
    [
        new("carga-por-desarrollador", "Carga de trabajo por desarrollador",
            "Requerimientos activos, atrasados, horas y edad promedio por desarrollador.",
            CargaDeTrabajoAsync, 0, 2, "Activos por desarrollador"),

        new("pendientes", "Requerimientos pendientes",
            "Todos los requerimientos sin entregar ni cancelar, con atraso y avance.",
            PendientesAsync, 2, -1, "Pendientes por estado"),

        new("avance", "Avance de requerimientos",
            "Porcentaje de avance de cada requerimiento (el indicador es el avance promedio).",
            AvanceAsync, 1, 4, "Avance (%) por requerimiento"),

        new("altas", "Altas por período (tipo de item)",
            "Items dados de alta en el rango de fechas, por origen/tipo.",
            AltasAsync, 2, -1, "Altas por origen/tipo"),

        new("por-desarrollador", "Requerimientos por desarrollador",
            "Requerimientos asignados a las personas seleccionadas (o a todas).",
            PorDesarrolladorAsync, 0, -1, "Requerimientos por desarrollador"),

        new("ciclo-de-vida", "Ciclo de vida de requerimientos",
            "Días vivo, días en el estado actual y lead time de cada requerimiento.",
            CicloDeVidaAsync, 2, -1, "Requerimientos por estado"),

        new("entregas", "Entregas: a tiempo vs tardías",
            "Requerimientos entregados: compromiso vs entrega real y atraso.",
            EntregasAsync, 6, -1, "Entregas por resultado"),

        new("devops-por-responsable", "Tickets de DevOps por responsable",
            "Work items de Azure DevOps agrupados por persona asignada.",
            DevOpsPorResponsableAsync, 0, -1, "Tickets por responsable"),

        new("por-origen", "Requerimientos por origen",
            "Conteo de requerimientos por origen (Manual / DevOps / Correo).",
            PorOrigenAsync, 0, 1, "Requerimientos por origen"),

        new("desempeno", "Desempeño (puntos) por período",
            "Puntos individuales sumados en el rango de fechas.",
            DesempenoAsync, 0, 1, "Puntos por desarrollador"),

        new("estimado-vs-real", "Estimado vs. real (por requerimiento)",
            "Horas estimadas vs. horas realmente dedicadas (cronómetro) EN EL PERÍODO por "
            + "requerimiento, con desviación.",
            EstimadoVsRealAsync, 1, 3, "Horas reales por requerimiento"),

        new("estimado-vs-real-por-desarrollador", "Estimado vs. real (por desarrollador)",
            "Horas estimadas (prorrateadas entre asignados) vs. horas realmente dedicadas EN EL "
            + "PERÍODO por cada desarrollador.",
            EstimadoVsRealPorDesarrolladorAsync, 0, 2, "Horas reales por desarrollador"),

        new("vacaciones", "Vacaciones por período",
            "Solicitudes de vacaciones cuyo inicio cae en el rango.",
            VacacionesAsync, 0, 3, "Días de vacaciones por desarrollador"),

        new("rotaciones", "Rotaciones de equipo por período",
            "Movimientos de desarrolladores entre equipos en el rango.",
            RotacionesAsync, 3, -1, "Rotaciones por equipo destino"),

        new("resumen-por-equipo", "Resumen por equipo",
            "Integrantes, requerimientos activos y puntos propios por equipo.",
            ResumenPorEquipoAsync, 0, 2, "Requerimientos activos por equipo"),
    ];

    // ── Lo que pide la pantalla ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Qué reportes hay y a quién se puede filtrar. Va junto porque la pantalla necesita las dos
    /// cosas antes de poder generar nada, y son dos consultas que no cambian entre reporte y reporte.
    /// </summary>
    public async Task<CatalogoDeReportesDto> CatalogoAsync(CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var devs = await DesarrolladoresAsync(ct);
        return new CatalogoDeReportesDto(
            Definiciones.Select(d => new ReporteDisponibleDto(d.Clave, d.Nombre, d.Descripcion)).ToList(),
            devs.Select(d => new OpcionDto(d.Id, d.FullName)).ToList());
    }

    /// <summary>
    /// Genera un reporte y lo devuelve listo para pintar: tabla en texto, cifras grandes y barras.
    /// Devuelve null si la clave no existe — que es un error de programación del cliente, no del
    /// usuario, y por eso no lleva mensaje.
    /// </summary>
    public async Task<ReporteGeneradoDto?> GenerarAsync(
        string clave, DateTime desde, DateTime hasta, IReadOnlyCollection<int> devs,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        if (Buscar(clave) is not { } def) return null;

        var tabla = await def.Generar(Parametros(desde, hasta, devs), ct);
        var barras = Barras(def, tabla.Filas);

        return new ReporteGeneradoDto(
            def.Clave, def.Nombre, def.Descripcion,
            tabla.Columnas,
            tabla.Filas.Select(f => (IReadOnlyList<string>)f.Select(Texto).ToList()).ToList(),
            Indicadores(def, tabla.Filas, barras),
            def.TituloDeGrafica,
            barras);
    }

    /// <summary>
    /// El mismo reporte, en una hoja de cálculo.
    ///
    /// Se vuelve a generar en vez de recibir del cliente lo que ya tiene pintado, y no es trabajo
    /// duplicado: los valores conservan aquí su tipo (fechas, decimales) y llegan a Excel como
    /// números con los que se puede seguir calculando. Lo que viaja al navegador es texto ya
    /// formateado, y de vuelta solo serviría para producir una hoja de puros textos.
    /// </summary>
    public async Task<(byte[] contenido, string nombre)?> ExcelAsync(
        string clave, DateTime desde, DateTime hasta, IReadOnlyCollection<int> devs,
        CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        if (Buscar(clave) is not { } def) return null;

        var tabla = await def.Generar(Parametros(desde, hasta, devs), ct);
        return (HojaDeCalculo.Escribir(tabla.Columnas, tabla.Filas, "Reporte"), def.Nombre);
    }

    // ── El envío por correo ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Manda el reporte por correo con la hoja de cálculo adjunta.
    ///
    /// <para>Devuelve siempre el motivo en palabras en vez de lanzar: sin correo configurado, sin
    /// destinatarios o con el servidor SMTP rechazando el envío, lo que hay que enseñar es una frase
    /// que diga qué arreglar — no un 500. El único que sí lanza es el de siempre, la guarda de rol,
    /// porque eso no es un rechazo de negocio.</para>
    /// </summary>
    /// <param name="destinatarios">Uno o varios, separados por «;» o «,», como los escribe una persona.</param>
    /// <param name="nota">Lo que quiera decir quien lo manda, o vacío. Va al principio del correo.</param>
    public async Task<(bool ok, string mensaje)> EnviarPorCorreoAsync(
        string clave, DateTime desde, DateTime hasta, IReadOnlyCollection<int> devs,
        string? destinatarios, string? nota, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        if (Buscar(clave) is not { } def) return (false, "Ese reporte no existe.");

        var datosDeCorreo = await ajustes.LeerCorreoAsync(ct);
        if (datosDeCorreo.Diagnostico() is string falta) return (false, falta);

        var destinos = AjustesDeCorreo.Separar(destinatarios);
        if (destinos.Count == 0) return (false, "Indica al menos un destinatario válido.");

        var parametros = Parametros(desde, hasta, devs);
        var tabla = await def.Generar(parametros, ct);

        var adjunto = new AdjuntoDeCorreo(
            NombreDelArchivo(def.Nombre),
            HojaDeCalculo.Escribir(tabla.Columnas, tabla.Filas, "Reporte"),
            AdjuntoDeCorreo.HojaDeCalculo);

        try
        {
            await correo.EnviarAsync(datosDeCorreo, destinos, Asunto(def.Nombre),
                ComponerCorreo(def.Nombre, parametros, tabla.Filas.Count, nota), adjunto, ct);
        }
        catch (ErrorDeCorreo ex)
        {
            // El reporte ya está generado y la pantalla lo sigue teniendo: lo único que falló es el
            // canal. Su mensaje ya viene escrito para leerse y no incluye la contraseña de la cuenta.
            return (false, ex.Message);
        }

        // Se anota QUÉ reporte salió y a cuántos, no a quiénes ni con qué cifras: la bitácora la lee
        // más gente que la que pidió el envío, y las filas del reporte son justamente lo delicado.
        await bitacora.RecordAsync(AuditAction.Update, "Reporte", null,
            $"Reporte «{def.Nombre}» enviado por correo a {destinos.Count} destinatario(s).", ct);

        return (true, $"Reporte enviado a {destinos.Count} destinatario(s).");
    }

    /// <summary>El asunto del correo. Es el mismo que prellenaba el escritorio.</summary>
    public static string Asunto(string nombreDelReporte) => $"Reporte: {nombreDelReporte}";

    /// <summary>
    /// El texto del correo: qué reporte va, de qué período y cuántas filas trae, más la nota de quien
    /// lo manda si escribió una.
    ///
    /// <para>Las cifras del reporte NO se copian al cuerpo. Van en el adjunto, que es donde se pueden
    /// leer y seguir calculando; repetirlas aquí solo daría dos versiones del mismo dato que se
    /// separan en cuanto una se recorte.</para>
    ///
    /// <para>Es una función pura para poder comprobarla sin base de datos y sin red.</para>
    /// </summary>
    public static string ComponerCorreo(
        string nombreDelReporte, ParametrosDeReporte parametros, int filas, string? nota)
    {
        var texto = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(nota))
        {
            texto.AppendLine(nota.Trim());
            texto.AppendLine();
        }

        texto.AppendLine($"Reporte: {nombreDelReporte}");
        texto.AppendLine($"Período: {parametros.Desde:dd/MM/yyyy} al {parametros.Hasta:dd/MM/yyyy}");
        texto.AppendLine(parametros.Devs.Count == 0
            ? "Personas: todas"
            : $"Personas: {parametros.Devs.Count} seleccionada(s)");
        texto.AppendLine($"Filas: {filas}");
        texto.AppendLine();
        texto.AppendLine("Va adjunto en una hoja de cálculo (.xlsx).");
        texto.AppendLine();
        texto.AppendLine("— Generado automáticamente por Administrador de Desarrollo Web.");

        return texto.ToString();
    }

    /// <summary>
    /// Cómo se llama el archivo que llega. Mismo formato que la descarga —nombre y sello de tiempo—
    /// para que el adjunto y el .xlsx bajado a mano no parezcan dos cosas distintas, y saneado porque
    /// varios reportes llevan «:» o «/» en el nombre y eso no puede acabar en el nombre de un archivo.
    /// </summary>
    private static string NombreDelArchivo(string nombreDelReporte) =>
        $"{ArchivosSubidos.NombreSeguro(nombreDelReporte)}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";

    private Definicion? Buscar(string clave) =>
        Definiciones.FirstOrDefault(d => d.Clave == clave);

    private static ParametrosDeReporte Parametros(DateTime desde, DateTime hasta, IReadOnlyCollection<int> devs) =>
        new(desde.Date, hasta.Date, [.. devs]);

    // ── Datos compartidos ────────────────────────────────────────────────────────────────────
    //
    // Se cachean en la instancia porque el servicio es scoped: dura lo que la petición, y varios
    // generadores piden la misma lista de desarrolladores. AsNoTracking en todo: aquí no se escribe
    // nada, y traer el histórico completo de requerimientos rastreado sería pagar por un seguimiento
    // de cambios que nadie va a usar.

    private List<Developer>? _devs;
    private List<Requirement>? _reqs;

    private async Task<List<Developer>> DesarrolladoresAsync(CancellationToken ct) =>
        _devs ??= await db.Developers.AsNoTracking()
            .Where(d => d.IsActive).OrderBy(d => d.FullName).ToListAsync(ct);

    private async Task<List<Requirement>> RequerimientosAsync(CancellationToken ct) =>
        _reqs ??= await db.Requirements.AsNoTracking()
            .Include(r => r.Assignments).ThenInclude(a => a.Developer).ToListAsync(ct);

    /// <summary>Un requerimiento entra en el reporte si lo tiene alguien del filtro (o si no hay filtro).</summary>
    private static bool EsDe(Requirement r, HashSet<int> ids) =>
        ids.Count == 0 || r.Assignments.Any(a => ids.Contains(a.DeveloperId));

    private static bool Cerrado(Requirement r) =>
        r.Status is RequirementStatus.Entregado or RequirementStatus.Cancelado;

    // ── Generadores ──────────────────────────────────────────────────────────────────────────

    private async Task<TablaDeReporte> CargaDeTrabajoAsync(ParametrosDeReporte p, CancellationToken ct)
    {
        var hoy = DateTime.Today;
        var reqs = await RequerimientosAsync(ct);
        var equipos = await db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var devs = await DesarrolladoresAsync(ct);

        var filas = devs
            .Where(d => p.Devs.Count == 0 || p.Devs.Contains(d.Id))
            .Select(d =>
            {
                var mios = reqs.Where(r => r.Assignments.Any(a => a.DeveloperId == d.Id) && !Cerrado(r)).ToList();
                int atrasados = mios.Count(r => r.CommittedDeliveryDate < hoy);
                decimal horas = mios.Sum(r => r.EstimateHours ?? 0);
                int edad = mios.Count == 0 ? 0 : (int)mios.Average(r => (hoy - r.CreatedAt.Date).TotalDays);
                // El equipo EXACTO de la persona, no su rama: la columna contesta «¿dónde está
                // esta persona?», y poner ahí el equipo de arriba la colocaría donde no trabaja.
                string equipo = d.TeamId != null && equipos.TryGetValue(d.TeamId.Value, out var nombre) ? nombre : "—";
                return new object?[] { d.FullName, equipo, mios.Count, atrasados, horas, edad };
            })
            .OrderByDescending(f => (int)f[2]!)
            .ToList();

        return new(["Desarrollador", "Equipo", "Activos", "Atrasados", "Hrs estimadas", "Edad prom (días)"], filas);
    }

    private async Task<TablaDeReporte> PendientesAsync(ParametrosDeReporte p, CancellationToken ct)
    {
        var hoy = DateTime.Today;
        var filas = (await RequerimientosAsync(ct))
            .Where(r => !Cerrado(r) && EsDe(r, p.Devs))
            .OrderBy(r => r.CommittedDeliveryDate ?? DateTime.MaxValue)
            .Select(r => new object?[]
            {
                r.Id, r.Title, Estado(r.Status), Prioridad(r.Priority),
                Asignados(r),
                r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—",
                r.CommittedDeliveryDate < hoy ? (int)(hoy - r.CommittedDeliveryDate!.Value.Date).TotalDays : 0,
                $"{r.ProgressPercent}%", Origen(r.Source)
            })
            .ToList();

        return new(["ID", "Título", "Estado", "Prioridad", "Desarrolladores", "F. Compromiso",
                    "Días atraso", "Avance", "Origen"], filas);
    }

    private async Task<TablaDeReporte> AvanceAsync(ParametrosDeReporte p, CancellationToken ct)
    {
        var filas = (await RequerimientosAsync(ct))
            .Where(r => !Cerrado(r) && EsDe(r, p.Devs))
            .OrderBy(r => r.ProgressPercent)
            .Select(r => new object?[]
            {
                r.Id, r.Title, Estado(r.Status), $"{r.ProgressPercent}%", r.ProgressPercent,
                r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—",
                Asignados(r)
            })
            .ToList();

        return new(["ID", "Título", "Estado", "Avance", "Avance %", "F. Compromiso", "Desarrolladores"], filas);
    }

    private async Task<TablaDeReporte> AltasAsync(ParametrosDeReporte p, CancellationToken ct)
    {
        var hasta = p.Hasta.AddDays(1);
        var filas = (await RequerimientosAsync(ct))
            .Where(r => r.CreatedAt >= p.Desde && r.CreatedAt < hasta && EsDe(r, p.Devs))
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new object?[]
            {
                r.Id, r.Title, Origen(r.Source), Prioridad(r.Priority), Estado(r.Status),
                r.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy"),
                Asignados(r)
            })
            .ToList();

        return new(["ID", "Título", "Origen/Tipo", "Prioridad", "Estado", "Fecha de alta",
                    "Desarrolladores"], filas);
    }

    private async Task<TablaDeReporte> PorDesarrolladorAsync(ParametrosDeReporte p, CancellationToken ct)
    {
        var reqs = await RequerimientosAsync(ct);
        var filas = new List<object?[]>();

        foreach (var d in await DesarrolladoresAsync(ct))
        {
            if (p.Devs.Count > 0 && !p.Devs.Contains(d.Id)) continue;
            foreach (var r in reqs.Where(r => r.Assignments.Any(a => a.DeveloperId == d.Id))
                                  .OrderBy(r => (int)r.Status))
                filas.Add([d.FullName, r.Id, r.Title, Estado(r.Status),
                           r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—", $"{r.ProgressPercent}%"]);
        }

        return new(["Desarrollador", "ID", "Título", "Estado", "F. Compromiso", "Avance"], filas);
    }

    private async Task<TablaDeReporte> CicloDeVidaAsync(ParametrosDeReporte p, CancellationToken ct)
    {
        var hoy = DateTime.Today;
        var filas = (await RequerimientosAsync(ct))
            .Where(r => EsDe(r, p.Devs))
            .OrderByDescending(r => (r.ActualDeliveryDate ?? hoy).Date.Subtract(r.CreatedAt.Date).TotalDays)
            .Select(r =>
            {
                var fin = r.ActualDeliveryDate ?? hoy;
                int vivo = Math.Max(0, (int)(fin.Date - r.CreatedAt.Date).TotalDays);
                int enEstado = Math.Max(0, (int)(hoy - (r.StatusChangedAt ?? r.CreatedAt).Date).TotalDays);
                string aTiempo = r.Status == RequirementStatus.Entregado
                                 && r.ActualDeliveryDate.HasValue && r.CommittedDeliveryDate.HasValue
                    ? (r.ActualDeliveryDate.Value.Date <= r.CommittedDeliveryDate.Value.Date ? "A tiempo" : "Tardía")
                    : "—";
                return new object?[]
                {
                    r.Id, r.Title, Estado(r.Status), vivo, enEstado,
                    r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—",
                    r.ActualDeliveryDate?.ToString("dd/MM/yyyy") ?? "—", aTiempo
                };
            })
            .ToList();

        return new(["ID", "Título", "Estado", "Días vivo", "Días en estado", "F. Compromiso",
                    "F. Entrega", "¿A tiempo?"], filas);
    }

    private async Task<TablaDeReporte> EntregasAsync(ParametrosDeReporte p, CancellationToken ct)
    {
        var filas = (await RequerimientosAsync(ct))
            .Where(r => r.Status == RequirementStatus.Entregado && r.ActualDeliveryDate.HasValue
                        && r.ActualDeliveryDate.Value.Date >= p.Desde && r.ActualDeliveryDate.Value.Date <= p.Hasta
                        && EsDe(r, p.Devs))
            .OrderBy(r => r.ActualDeliveryDate)
            .Select(r =>
            {
                int atraso = r.CommittedDeliveryDate.HasValue
                    ? (int)(r.ActualDeliveryDate!.Value.Date - r.CommittedDeliveryDate.Value.Date).TotalDays
                    : 0;
                return new object?[]
                {
                    r.Id, r.Title, Asignados(r),
                    r.CommittedDeliveryDate?.ToString("dd/MM/yyyy") ?? "—",
                    r.ActualDeliveryDate!.Value.ToString("dd/MM/yyyy"),
                    atraso, atraso <= 0 ? "A tiempo" : "Tardía"
                };
            })
            .ToList();

        return new(["ID", "Título", "Desarrolladores", "F. Compromiso", "F. Entrega",
                    "Atraso (días)", "Resultado"], filas);
    }

    private async Task<TablaDeReporte> DevOpsPorResponsableAsync(ParametrosDeReporte p, CancellationToken ct)
    {
        // El cruce va por NOMBRE y no por id porque un ticket de DevOps guarda el nombre para
        // mostrar de su responsable, no una referencia a la ficha. Es la misma coincidencia parcial
        // del escritorio, con sus limitaciones conocidas (acentos, segundos nombres).
        var nombres = p.Devs.Count == 0
            ? null
            : (await DesarrolladoresAsync(ct)).Where(d => p.Devs.Contains(d.Id)).Select(d => d.FullName).ToList();

        var tickets = await db.DevOpsTickets.AsNoTracking()
            .OrderBy(t => t.AssignedTo).ThenBy(t => t.State).ToListAsync(ct);

        var filas = tickets
            .Where(t => nombres == null
                        || nombres.Any(n => t.AssignedTo.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .Select(t => new object?[]
            {
                string.IsNullOrWhiteSpace(t.AssignedTo) ? "(sin asignar)" : t.AssignedTo,
                t.ExternalId, t.Title, t.WorkItemType, t.State, t.AreaPath
            })
            .ToList();

        return new(["Responsable", "ID DevOps", "Título", "Tipo", "Estado", "Área"], filas);
    }

    private async Task<TablaDeReporte> PorOrigenAsync(ParametrosDeReporte p, CancellationToken ct)
    {
        var reqs = (await RequerimientosAsync(ct)).Where(r => EsDe(r, p.Devs)).ToList();
        var filas = Enum.GetValues<RequirementSource>()
            .Select(src =>
            {
                var grupo = reqs.Where(r => r.Source == src).ToList();
                return new object?[]
                {
                    Origen(src), grupo.Count, grupo.Count(r => !Cerrado(r)),
                    grupo.Count(r => r.Status == RequirementStatus.Entregado)
                };
            })
            .ToList();

        return new(["Origen", "Total", "Activos", "Entregados"], filas);
    }

    private async Task<TablaDeReporte> DesempenoAsync(ParametrosDeReporte p, CancellationToken ct)
    {
        var hasta = p.Hasta.AddDays(1);

        // Solo lo APROBADO, igual que el escritorio: una autocalificación pendiente de revisión no
        // es desempeño todavía, y contarla en un reporte que se comparte adelantaría una decisión
        // que el líder no ha tomado.
        var entradas = await db.PointEntries.AsNoTracking().Include(e => e.Developer)
            .Where(e => e.Date >= p.Desde && e.Date < hasta
                        && e.ApprovalStatus == PointApprovalStatus.Aprobado)
            .ToListAsync(ct);

        var filas = entradas
            .Where(e => p.Devs.Count == 0 || p.Devs.Contains(e.DeveloperId))
            .GroupBy(e => e.Developer.FullName)
            .Select(g => new object?[]
            {
                g.Key, g.Sum(e => e.Points), g.Where(e => e.Points > 0).Sum(e => e.Points),
                g.Where(e => e.Points < 0).Sum(e => e.Points), g.Count()
            })
            .OrderByDescending(f => (int)f[1]!)
            .ToList();

        return new(["Desarrollador", "Total pts", "Premios", "Penalizaciones", "Entradas"], filas);
    }

    private async Task<TablaDeReporte> EstimadoVsRealAsync(ParametrosDeReporte p, CancellationToken ct)
    {
        var ahora = DateTime.UtcNow;
        var hasta = p.Hasta.AddDays(1);

        // Horas reales = sesiones cuyo inicio cae en el rango (respeta el Desde/Hasta de la pantalla).
        var sesiones = await db.WorkSessions.AsNoTracking()
            .Where(w => w.StartedAt >= p.Desde && w.StartedAt < hasta).ToListAsync(ct);
        // Solo las sesiones de un requerimiento: las de actividades libres no tienen contra qué
        // compararse aquí. El escritorio las agrupaba igual bajo una clave nula que luego nadie
        // consultaba.
        var realPorReq = sesiones
            .Where(w => w.RequirementId is int)
            .GroupBy(w => w.RequirementId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(w => w.LiveSeconds(ahora)) / 3600.0);

        var reqs = (await RequerimientosAsync(ct))
            .Where(r => r.Status != RequirementStatus.Cancelado)
            .Where(r => p.Devs.Count == 0 || r.Assignments.Any(a => p.Devs.Contains(a.DeveloperId)))
            .ToList();

        var filas = new List<object?[]>();
        foreach (var r in reqs)
        {
            double estimado = (double)(r.EstimateHours ?? 0);
            double real = realPorReq.GetValueOrDefault(r.Id, 0);
            if (estimado <= 0 && real <= 0) continue;   // sin estimación ni tiempo real: se omite

            double desviacion = real - estimado;
            string porcentaje = estimado > 0 ? $"{desviacion / estimado * 100:+0;-0;0}%" : "—";
            filas.Add([r.Id, r.Title, Math.Round(estimado, 2), Math.Round(real, 2),
                       Math.Round(desviacion, 2), porcentaje]);
        }

        // Mayor desviación primero: es el renglón que hay que mirar.
        filas = filas.OrderByDescending(f => (double)f[4]!).ToList();

        return new(["ID", "Requerimiento", "Estimado (h)", "Real (h)", "Desviación (h)", "Desv %"], filas);
    }

    private async Task<TablaDeReporte> EstimadoVsRealPorDesarrolladorAsync(
        ParametrosDeReporte p, CancellationToken ct)
    {
        var ahora = DateTime.UtcNow;
        var hasta = p.Hasta.AddDays(1);

        var sesiones = await db.WorkSessions.AsNoTracking()
            .Where(w => w.StartedAt >= p.Desde && w.StartedAt < hasta
                        && w.Requirement!.Status != RequirementStatus.Cancelado)
            .ToListAsync(ct);
        var realPorDev = sesiones
            .GroupBy(w => w.DeveloperId)
            .ToDictionary(g => g.Key, g => g.Sum(w => w.LiveSeconds(ahora)) / 3600.0);

        // Estimación PRORRATEADA: el EstimateHours de un requerimiento se reparte entre sus
        // asignados, así la suma por persona equivale al estimado total del requerimiento en vez de
        // multiplicarlo por cuánta gente lo tiene.
        var asignaciones = await db.Assignments.AsNoTracking()
            .Where(a => a.Requirement.Status != RequirementStatus.Cancelado)
            .Select(a => new { a.DeveloperId, a.RequirementId, Estimado = a.Requirement.EstimateHours })
            .Distinct()
            .ToListAsync(ct);

        var devsPorReq = asignaciones.GroupBy(a => a.RequirementId).ToDictionary(g => g.Key, g => g.Count());
        var estimadoPorDev = asignaciones.GroupBy(a => a.DeveloperId)
            .ToDictionary(g => g.Key,
                          g => g.Sum(a => (double)(a.Estimado ?? 0)
                                          / Math.Max(1, devsPorReq.GetValueOrDefault(a.RequirementId, 1))));

        var filas = new List<object?[]>();
        foreach (var d in (await DesarrolladoresAsync(ct))
                 .Where(d => p.Devs.Count == 0 || p.Devs.Contains(d.Id)))
        {
            double estimado = estimadoPorDev.GetValueOrDefault(d.Id, 0);
            double real = realPorDev.GetValueOrDefault(d.Id, 0);
            if (estimado <= 0 && real <= 0) continue;

            double desviacion = real - estimado;
            string porcentaje = estimado > 0 ? $"{desviacion / estimado * 100:+0;-0;0}%" : "—";
            filas.Add([d.FullName, Math.Round(estimado, 2), Math.Round(real, 2),
                       Math.Round(desviacion, 2), porcentaje]);
        }

        filas = filas.OrderByDescending(f => (double)f[2]!).ToList();   // más horas reales primero

        return new(["Desarrollador", "Estimado (h)", "Real (h)", "Desviación (h)", "Desv %"], filas);
    }

    private async Task<TablaDeReporte> VacacionesAsync(ParametrosDeReporte p, CancellationToken ct)
    {
        var solicitudes = await db.VacationRequests.AsNoTracking().Include(v => v.Developer).ToListAsync(ct);

        var filas = solicitudes
            .Where(v => v.StartDate.Date >= p.Desde && v.StartDate.Date <= p.Hasta
                        && (p.Devs.Count == 0 || p.Devs.Contains(v.DeveloperId)))
            .OrderBy(v => v.StartDate)
            .Select(v => new object?[]
            {
                v.Developer.FullName, v.StartDate.ToString("dd/MM/yyyy"), v.EndDate.ToString("dd/MM/yyyy"),
                v.TotalDays, EstadoDeVacaciones(v.Status), v.Comment
            })
            .ToList();

        return new(["Desarrollador", "Inicio", "Fin", "Días", "Estado", "Comentario"], filas);
    }

    private async Task<TablaDeReporte> RotacionesAsync(ParametrosDeReporte p, CancellationToken ct)
    {
        var hasta = p.Hasta.AddDays(1);
        var rotaciones = await db.TeamRotations.AsNoTracking()
            .Where(r => r.RotatedAt >= p.Desde && r.RotatedAt < hasta)
            .OrderByDescending(r => r.RotatedAt)
            .ToListAsync(ct);

        var filas = rotaciones
            .Where(r => p.Devs.Count == 0 || p.Devs.Contains(r.DeveloperId))
            .Select(r => new object?[]
            {
                r.RotatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                r.DeveloperName, r.FromTeamName, r.ToTeamName, r.Note
            })
            .ToList();

        return new(["Fecha", "Desarrollador", "De", "A", "Nota"], filas);
    }

    /// <summary>
    /// Una fila por equipo, con lo SUYO y nada más: sus integrantes, los requerimientos que llevan y
    /// los puntos asignados al equipo en el período.
    ///
    /// <para><b>Con subequipos, las cifras siguen siendo EXACTAS y no de la rama.</b> Es un reporte
    /// que se exporta y que lleva sus propias cifras grandes —total, promedio, máximo de la columna
    /// de requerimientos—, y si un equipo padre sumara lo de sus hijos esas cifras contarían dos
    /// veces a la misma gente: el total de la hoja dejaría de ser el de la empresa. Una fila por
    /// equipo, sumables entre ellas, es lo único que permite leer la hoja sin instrucciones.</para>
    ///
    /// <para>Lo que sí cambia es que la fila DICE de quién cuelga, y que las filas salen en orden de
    /// dibujo —cada padre delante de su rama—: sin eso, quien lea «Desarrollo web: 1 integrante»
    /// junto a un «Front: 6» suelto no tiene forma de saber que el segundo está dentro del primero, y
    /// la cifra exacta se lee como un error. La columna va la ÚLTIMA a propósito: las cuatro de
    /// siempre conservan su sitio, y con ellas lo conservan la gráfica y las cifras grandes, que
    /// apuntan a las columnas por su índice.</para>
    /// </summary>
    private async Task<TablaDeReporte> ResumenPorEquipoAsync(ParametrosDeReporte p, CancellationToken ct)
    {
        var hasta = p.Hasta.AddDays(1);
        var equipos = await db.Teams.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        var devs = await DesarrolladoresAsync(ct);
        var reqs = await RequerimientosAsync(ct);
        var puntos = await db.TeamPointEntries.AsNoTracking()
            .Where(e => e.Date >= p.Desde && e.Date < hasta).ToListAsync(ct);

        // El árbol se arma con los equipos ya ordenados por nombre, así que dentro de cada rama las
        // filas siguen saliendo alfabéticas, y sin jerarquía esto es exactamente el orden de antes.
        var jerarquia = JerarquiaDeEquipos.De(equipos);
        var porId = equipos.ToDictionary(t => t.Id);
        var enOrden = jerarquia.EnOrdenDeDibujo().Select(id => porId[id]).ToList();

        // Filtrar por personas se traduce a filtrar por SUS equipos: el reporte es por equipo, y
        // enseñar un equipo del que solo se pidió a una persona sería enseñar cifras de los demás.
        // Los equipos de encima NO se añaden por colgar de ellos: son otros equipos con otra gente, y
        // meterlos sería colar en la hoja justo las cifras que el filtro quería dejar fuera.
        var soloEstos = p.Devs.Count == 0
            ? null
            : devs.Where(d => p.Devs.Contains(d.Id) && d.TeamId != null).Select(d => d.TeamId!.Value).ToHashSet();

        var filas = enOrden
            .Where(t => soloEstos == null || soloEstos.Contains(t.Id))
            .Select(t =>
            {
                var integrantes = devs.Where(d => d.TeamId == t.Id).Select(d => d.Id).ToHashSet();
                int activos = reqs.Count(r => !Cerrado(r) && r.Assignments.Any(a => integrantes.Contains(a.DeveloperId)));
                int propios = puntos.Where(e => e.TeamId == t.Id).Sum(e => e.Points);
                // El padre por su NOMBRE y el que dice el árbol, no el de la columna: uno que apunte
                // a un equipo que ya no está se lee como raíz, igual que en el organigrama.
                var padre = jerarquia.PadreDe(t.Id) is int padreId ? porId[padreId].Name : "—";
                return new object?[] { t.Name, integrantes.Count, activos, propios, padre };
            })
            .ToList();

        return new(["Equipo", "Integrantes", "Reqs activos", "Pts propios (período)", "Cuelga de"], filas);
    }

    // ── Resumen: barras y cifras grandes ─────────────────────────────────────────────────────

    /// <summary>
    /// Las barras de la gráfica: se agrupa por la columna de categoría y se suma la de valor (o se
    /// cuentan las filas). Catorce como mucho, igual que el escritorio: más barras en el mismo
    /// ancho dejan de leerse.
    /// </summary>
    private static List<BarraDeReporteDto> Barras(Definicion def, List<object?[]> filas) =>
        filas.Where(f => def.ColCategoria < f.Length)
             .GroupBy(f => f[def.ColCategoria]?.ToString() ?? "—")
             .Select(g => new BarraDeReporteDto(
                 g.Key,
                 def.ColValor < 0
                     ? g.Count()
                     : g.Sum(f => ANumero(def.ColValor < f.Length ? f[def.ColValor] : null))))
             .OrderByDescending(b => Math.Abs(b.Valor))
             .Take(14)
             .ToList();

    private static List<IndicadorDto> Indicadores(
        Definicion def, List<object?[]> filas, List<BarraDeReporteDto> barras)
    {
        var kpis = new List<IndicadorDto> { new("Filas", filas.Count.ToString()) };

        if (def.ColValor >= 0)
        {
            var valores = filas.Select(f => ANumero(def.ColValor < f.Length ? f[def.ColValor] : null)).ToList();
            kpis.Add(new("Total", Cifra(valores.Sum())));
            kpis.Add(new("Promedio", Cifra(valores.Count > 0 ? valores.Average() : 0)));
            kpis.Add(new("Máximo", Cifra(valores.Count > 0 ? valores.Max() : 0)));
        }
        else
        {
            kpis.Add(new("Categorías", barras.Count.ToString()));
            if (barras.Count > 0)
                kpis.Add(new("Mayor: " + Recortar(barras[0].Etiqueta, 14), Cifra(barras[0].Valor)));
        }

        return kpis;
    }

    private static string Cifra(double v) => v == Math.Floor(v) ? ((long)v).ToString() : v.ToString("0.#");

    private static string Recortar(string s, int n) => s.Length > n ? s[..n] + "…" : s;

    /// <summary>
    /// Lo que hay en una celda, como número. Los textos se limpian de todo lo que no sea cifra
    /// —«85%», «+12%»— porque varias columnas guardan el valor ya formateado y la gráfica igual
    /// tiene que poder sumarlas.
    /// </summary>
    private static double ANumero(object? v) => v switch
    {
        null => 0,
        int i => i,
        long l => l,
        double d => d,
        decimal m => (double)m,
        string s => double.TryParse(new string([.. s.Where(c => char.IsDigit(c) || c == '.' || c == '-')]),
                                    out var r) ? r : 0,
        _ => double.TryParse(v.ToString(), out var r) ? r : 0
    };

    /// <summary>Cómo se pinta una celda en la pantalla. Es el mismo criterio que la rejilla del escritorio.</summary>
    private static string Texto(object? v) => v switch
    {
        null => "",
        DateTime d => d.ToString("dd/MM/yyyy"),
        bool b => b ? "Sí" : "No",
        _ => v.ToString() ?? ""
    };

    // ── Etiquetas ────────────────────────────────────────────────────────────────────────────
    //
    // Se quedan aquí y no en Shared: estos textos salen del servidor ya escritos dentro de la fila,
    // y el navegador nunca necesita traducir un enum de estos por su cuenta.

    private static string Asignados(Requirement r) =>
        string.Join(", ", r.Assignments.Select(a => a.Developer.FullName));

    private static string Estado(RequirementStatus s) => s switch
    {
        RequirementStatus.PorEstimar => "Por estimar",
        RequirementStatus.Estimado => "Estimado",
        RequirementStatus.EnDesarrollo => "En desarrollo",
        RequirementStatus.EnPruebas => "En pruebas",
        RequirementStatus.PorEntregar => "Por entregar",
        RequirementStatus.Entregado => "Entregado",
        RequirementStatus.Cancelado => "Cancelado",
        _ => s.ToString()
    };

    private static string Prioridad(RequirementPriority p) => p switch
    {
        RequirementPriority.Baja => "Baja",
        RequirementPriority.Media => "Media",
        RequirementPriority.Alta => "Alta",
        RequirementPriority.Critica => "Crítica",
        _ => p.ToString()
    };

    private static string Origen(RequirementSource s) => s switch
    {
        RequirementSource.AzureDevOps => "DevOps",
        RequirementSource.Email => "Correo",
        _ => "Manual"
    };

    private static string EstadoDeVacaciones(VacationStatus s) => s switch
    {
        VacationStatus.Aprobada => "Aprobada",
        VacationStatus.Rechazada => "Rechazada",
        VacationStatus.Cancelada => "Cancelada",
        _ => "Pendiente"
    };
}

/// <summary>
/// Escribe una tabla en un .xlsx.
///
/// Es el <c>ReportService.ExportToExcel</c> del escritorio con un solo cambio: devuelve los bytes en
/// vez de guardarlos en una ruta. Aquí no hay diálogo «guardar como» ni disco donde dejar el
/// archivo — el servidor lo genera en memoria y el navegador decide dónde ponerlo.
///
/// Vive junto a los reportes porque son sus dos únicos usuarios (reportes y minutas) y sacarlo a su
/// propio archivo solo añadiría un sitio donde buscarlo.
/// </summary>
public static class HojaDeCalculo
{
    public static byte[] Escribir(
        IReadOnlyList<string> columnas, IReadOnlyList<object?[]> filas, string nombreDeHoja)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.Worksheets.Add(nombreDeHoja);

        for (int i = 0; i < columnas.Count; i++)
        {
            var celda = hoja.Cell(1, i + 1);
            celda.Value = columnas[i];
            celda.Style.Font.Bold = true;
            celda.Style.Font.FontColor = XLColor.White;
            celda.Style.Fill.BackgroundColor = XLColor.FromArgb(37, 99, 235);
            celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int renglon = 2;
        bool alterna = false;
        foreach (var fila in filas)
        {
            for (int i = 0; i < fila.Length; i++)
            {
                var celda = hoja.Cell(renglon, i + 1);
                var valor = fila[i];

                // Los tipos se conservan para que Excel reciba números con los que se pueda seguir
                // calculando, no textos que parezcan números.
                celda.Value = valor switch
                {
                    null => XLCellValue.FromObject(""),
                    string s => XLCellValue.FromObject(s),
                    int n => XLCellValue.FromObject(n),
                    decimal d => XLCellValue.FromObject(d),
                    double db => XLCellValue.FromObject(db),
                    bool b => XLCellValue.FromObject(b ? "Sí" : "No"),
                    DateTime dt => XLCellValue.FromObject(dt.ToString("dd/MM/yyyy")),
                    _ => XLCellValue.FromObject(valor.ToString() ?? "")
                };

                if (alterna) celda.Style.Fill.BackgroundColor = XLColor.FromArgb(240, 245, 255);
            }
            alterna = !alterna;
            renglon++;
        }

        hoja.ColumnsUsed().AdjustToContents();

        using var memoria = new MemoryStream();
        libro.SaveAs(memoria);
        return memoria.ToArray();
    }
}
