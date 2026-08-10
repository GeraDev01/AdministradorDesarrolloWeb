using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Catalogos;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Las consultas de los catálogos del área: desarrolladores, equipos, contactos, programas y
/// recursos de Azure. SOLO LECTURA — el alta, la edición y la baja son de otra fase, y por eso
/// aquí no hay ni un <c>SaveChanges</c>.
///
/// Todo devuelve DTO, nunca entidades: lo que sale de aquí cruza la frontera del API.
///
/// Los filtros se aplican EN LA BASE y no en memoria. Es la diferencia de fondo con el escritorio,
/// donde cada pantalla hacía <c>ToList()</c> de la tabla entera y filtraba con LINQ a objetos: allí
/// eso costaba la memoria de un solo proceso y aquí costaría la de todas las sesiones a la vez.
/// </summary>
public class CatalogosQueryService(AppDbContext db, ICurrentUser currentUser)
{
    /// <summary>Los catálogos son material del líder. La política del endpoint dice lo mismo; ésta
    /// es la segunda barrera, la que viaja pegada al dato.</summary>
    private void SoloAdmin() => AuthorizationGuard.RequireAdmin(currentUser);

    // ── Desarrolladores ──────────────────────────────────────────────────────────

    /// <summary>
    /// La lista de desarrolladores. <paramref name="soloActivos"/> viene marcada por omisión en la
    /// pantalla, igual que en el escritorio: la baja de alguien no borra su ficha, así que sin ese
    /// filtro la lista del día a día se llenaría de gente que ya no está.
    /// </summary>
    public async Task<IReadOnlyList<DesarrolladorDto>> DesarrolladoresAsync(
        string? texto = null, bool soloActivos = true, CancellationToken ct = default)
    {
        SoloAdmin();

        var q = db.Developers.AsNoTracking().AsQueryable();
        if (soloActivos) q = q.Where(d => d.IsActive);

        texto = (texto ?? "").Trim();
        if (texto.Length > 0)
            // Mismos tres campos que buscaba el escritorio: nombre, correo y teléfono.
            q = q.Where(d => d.FullName.Contains(texto)
                          || (d.Email != null && d.Email.Contains(texto))
                          || (d.Phone != null && d.Phone.Contains(texto)));

        // Quiénes tienen cuenta de acceso. Una consulta aparte y no un JOIN por fila: son dos
        // conjuntos pequeños y así la proyección de arriba no arrastra la tabla de usuarios.
        var conCuenta = await db.Users.AsNoTracking()
            .Where(u => u.DeveloperId != null)
            .Select(u => u.DeveloperId!.Value)
            .Distinct()
            .ToListAsync(ct);
        var cuentas = conCuenta.ToHashSet();

        var filas = await q
            .OrderBy(d => d.FullName)
            .Select(d => new
            {
                d.Id, d.FullName, d.Email, d.Phone, d.Seniority, d.HireDate, d.Address,
                d.EquipmentSerial, d.VacationDaysLeft, d.IsActive, d.TeamRole, d.Notes,
                Equipo = d.Team != null ? d.Team.Name : null,
                // Contado en la base: traer las asignaciones para hacerles Count() en memoria era
                // barato con 20 desarrolladores y deja de serlo con años de requerimientos.
                Asignados = d.Assignments.Count()
            })
            .ToListAsync(ct);

        return filas.Select(d => new DesarrolladorDto(
            d.Id, d.FullName, d.Email, d.Phone, d.Seniority, d.HireDate, d.Address,
            d.EquipmentSerial, d.VacationDaysLeft, d.Asignados, cuentas.Contains(d.Id),
            d.IsActive, d.Equipo, d.TeamRole, EtiquetasDeCatalogo.RolDeEquipo(d.TeamRole),
            d.Notes)).ToList();
    }

    // ── Equipos ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// El organigrama: cada equipo con su líder arriba y los integrantes agrupados por rol, más la
    /// columna de «sin equipo».
    ///
    /// En fase 1 es una VISTA: el tablero arrastrable del escritorio (mover gente entre equipos,
    /// asignar roles, registrar rotaciones) es escritura y llega en fase 3. Lo que se conserva es
    /// la información y el orden, que es lo que la gente lee.
    ///
    /// Solo aparecen los desarrolladores ACTIVOS, igual que en el escritorio: un organigrama con
    /// quien ya no está deja de describir al equipo.
    /// </summary>
    public async Task<OrganizacionDto> OrganizacionAsync(CancellationToken ct = default)
    {
        SoloAdmin();

        var equipos = await db.Teams.AsNoTracking()
            .OrderBy(t => t.Name)
            .Select(t => new { t.Id, t.Name, t.Description, t.ColorHex, t.LeadDeveloperId })
            .ToListAsync(ct);

        var devs = await db.Developers.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.FullName)
            .Select(d => new { d.Id, d.FullName, d.TeamId, d.TeamRole })
            .ToListAsync(ct);

        // Los contadores 🖥/📁 del encabezado de cada columna, agregados en la base.
        var sistemas = await db.AppSystems.AsNoTracking()
            .Where(s => s.TeamId != null)
            .GroupBy(s => s.TeamId!.Value)
            .Select(g => new { Equipo = g.Key, Cuantos = g.Count() })
            .ToDictionaryAsync(x => x.Equipo, x => x.Cuantos, ct);

        var proyectos = await db.Projects.AsNoTracking()
            .Where(p => p.TeamId != null)
            .GroupBy(p => p.TeamId!.Value)
            .Select(g => new { Equipo = g.Key, Cuantos = g.Count() })
            .ToDictionaryAsync(x => x.Equipo, x => x.Cuantos, ct);

        var lista = new List<EquipoDto>(equipos.Count);
        foreach (var t in equipos)
        {
            var miembros = devs.Where(d => d.TeamId == t.Id).ToList();

            // El líder se decide igual que en el escritorio y en ese orden: manda el rol marcado en
            // la ficha y solo si nadie lo tiene se recurre al LeadDeveloperId del equipo. Los dos
            // datos existen y pueden discrepar; quedarse con uno de los dos es reescribir la regla.
            var lider = miembros.FirstOrDefault(m => m.TeamRole == TeamRole.Lider)
                        ?? miembros.FirstOrDefault(m => m.Id == t.LeadDeveloperId);

            var integrantes = new List<IntegranteDeEquipoDto>();
            if (lider != null)
                integrantes.Add(new IntegranteDeEquipoDto(
                    lider.Id, lider.FullName, TeamRole.Lider,
                    EtiquetasDeCatalogo.RolDeEquipo(TeamRole.Lider),
                    EtiquetasDeCatalogo.ColorDeRol(TeamRole.Lider), EsLider: true));

            var resto = miembros.Where(m => lider == null || m.Id != lider.Id).ToList();
            foreach (var rol in EtiquetasDeCatalogo.OrdenDeRoles)
                integrantes.AddRange(resto
                    .Where(m => m.TeamRole == rol)
                    .Select(m => new IntegranteDeEquipoDto(
                        m.Id, m.FullName, rol, EtiquetasDeCatalogo.RolDeEquipo(rol),
                        EtiquetasDeCatalogo.ColorDeRol(rol), EsLider: false)));

            lista.Add(new EquipoDto(
                t.Id, t.Name, t.Description, t.ColorHex, lider?.FullName,
                sistemas.GetValueOrDefault(t.Id), proyectos.GetValueOrDefault(t.Id),
                integrantes));
        }

        var sinEquipo = devs
            .Where(d => d.TeamId == null)
            .Select(d => new IntegranteDeEquipoDto(
                d.Id, d.FullName, d.TeamRole, EtiquetasDeCatalogo.RolDeEquipo(d.TeamRole),
                EtiquetasDeCatalogo.ColorDeRol(d.TeamRole), EsLider: false))
            .ToList();

        return new OrganizacionDto(lista, sinEquipo);
    }

    // ── Contactos ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ContactoDto>> ContactosAsync(
        string? texto = null, CancellationToken ct = default)
    {
        SoloAdmin();

        var q = db.Contacts.AsNoTracking().AsQueryable();

        texto = (texto ?? "").Trim();
        if (texto.Length > 0)
            q = q.Where(c => c.Name.Contains(texto)
                          || (c.JobTitle != null && c.JobTitle.Contains(texto))
                          || (c.Company != null && c.Company.Contains(texto))
                          || (c.Email != null && c.Email.Contains(texto)));

        return await q
            .OrderBy(c => c.Name)
            .Select(c => new ContactoDto(
                c.Id, c.Name, c.JobTitle, c.Company, c.Email, c.Phone, c.TeamsLink, c.Notes))
            .ToListAsync(ct);
    }

    // ── Exportaciones ────────────────────────────────────────────────────────────
    //
    // Las dos bajan LO QUE EL FILTRO ESTÁ ENSEÑANDO y no la tabla entera: es la regla que ya siguen
    // minutas y vacaciones, y bajar el catálogo completo sorprende a quien acaba de acotar.
    // Reusan el método de consulta de arriba en lugar de repetir la consulta, así que si mañana
    // cambia un filtro no puede quedarse una de las dos vistas desalineada con la otra.

    /// <summary>Los desarrolladores del filtro, en una hoja. Mismas columnas que el escritorio.</summary>
    public async Task<byte[]> ExcelDeDesarrolladoresAsync(
        string? texto = null, bool soloActivos = true, CancellationToken ct = default)
    {
        var filas = await DesarrolladoresAsync(texto, soloActivos, ct);

        return HojaDeCalculo.Escribir(
            ["ID", "Nombre", "Correo", "Teléfono", "Seniority", "Equipo", "Rol", "F. Ingreso",
             "Dirección", "N.° Serie equipo", "Vacaciones", "Asignados", "Acceso", "Activo", "Notas"],
            [.. filas.Select(d => new object?[]
            {
                d.Id, d.Nombre, d.Correo, d.Telefono, d.Seniority, d.Equipo ?? "Sin equipo",
                d.RolEquipoTexto, d.FechaIngreso?.ToString("dd/MM/yyyy"), d.Direccion, d.SerieEquipo,
                d.DiasVacaciones, d.Asignados, d.TieneAcceso ? "Sí" : "No", d.Activo ? "Sí" : "No",
                d.Notas
            })],
            "Desarrolladores");
    }

    /// <summary>Los contactos del filtro, en una hoja.</summary>
    public async Task<byte[]> ExcelDeContactosAsync(
        string? texto = null, CancellationToken ct = default)
    {
        var filas = await ContactosAsync(texto, ct);

        return HojaDeCalculo.Escribir(
            ["ID", "Nombre", "Puesto", "Empresa", "Correo", "Teléfono", "Teams", "Notas"],
            [.. filas.Select(c => new object?[]
            {
                c.Id, c.Nombre, c.Puesto, c.Empresa, c.Correo, c.Telefono, c.EnlaceTeams, c.Notas
            })],
            "Contactos");
    }

    // ── Programas ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ProgramaDto>> ProgramasAsync(
        string? texto = null, SoftwareCategory? categoria = null, SoftwareStatus? estado = null,
        CancellationToken ct = default)
    {
        SoloAdmin();

        var q = db.SoftwareItems.AsNoTracking().AsQueryable();

        texto = (texto ?? "").Trim();
        if (texto.Length > 0)
            q = q.Where(s => s.Name.Contains(texto)
                          || (s.Publisher != null && s.Publisher.Contains(texto))
                          || (s.Notes != null && s.Notes.Contains(texto))
                          || (s.InstalledOn != null && s.InstalledOn.Contains(texto)));

        if (categoria is { } c) q = q.Where(s => s.Category == c);
        if (estado is { } e) q = q.Where(s => s.Status == e);

        var filas = await q
            .OrderBy(s => s.Name)
            .Select(s => new
            {
                s.Id, s.Name, s.Category, s.Status, s.LicenseType, s.Version, s.Publisher,
                s.LicenseExpiry, s.InstalledOn, s.Url, s.Notes
                // LicenseKey queda fuera a propósito; ver el comentario del DTO.
            })
            .ToListAsync(ct);

        // El «vence pronto» se resuelve contra el reloj del servidor y no contra el del navegador:
        // un equipo con la fecha mal puesta no debe pintar de rojo licencias que están al día.
        var hoy = DateTime.Today;
        return filas.Select(s => new ProgramaDto(
            s.Id, s.Name,
            s.Category, EtiquetasDeCatalogo.Categoria(s.Category),
            s.Status, EtiquetasDeCatalogo.EstadoDePrograma(s.Status),
            s.LicenseType, EtiquetasDeCatalogo.Licencia(s.LicenseType),
            s.Version, s.Publisher, s.LicenseExpiry,
            Vencida: s.Status == SoftwareStatus.Expirado || s.LicenseExpiry < hoy,
            PorVencer: s.LicenseExpiry != null && s.LicenseExpiry >= hoy && s.LicenseExpiry < hoy.AddDays(30),
            s.InstalledOn, s.Url, s.Notes)).ToList();
    }

    // ── Recursos de Azure ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RecursoAzureDto>> RecursosAzureAsync(
        string? texto = null, AzureResourceType? tipo = null, AzureResourceStatus? estado = null,
        AzureEnvironment? ambiente = null, CancellationToken ct = default)
    {
        SoloAdmin();

        var q = db.AzureResources.AsNoTracking().AsQueryable();

        texto = (texto ?? "").Trim();
        if (texto.Length > 0)
            q = q.Where(r => r.Name.Contains(texto)
                          || (r.ResourceGroup != null && r.ResourceGroup.Contains(texto))
                          || (r.Notes != null && r.Notes.Contains(texto))
                          || (r.Url != null && r.Url.Contains(texto)));

        if (tipo is { } t) q = q.Where(r => r.ResourceType == t);
        if (estado is { } e) q = q.Where(r => r.Status == e);
        if (ambiente is { } a) q = q.Where(r => r.Environment == a);

        var filas = await q
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id, r.Name, r.ResourceType, r.Status, r.Environment, r.ResourceGroup,
                r.SubscriptionName, r.Region, r.MonthlyCostEstimate, r.Url, r.Notes
            })
            .ToListAsync(ct);

        return filas.Select(r => new RecursoAzureDto(
            r.Id, r.Name,
            r.ResourceType, EtiquetasDeCatalogo.TipoDeRecurso(r.ResourceType),
            r.Status, EtiquetasDeCatalogo.EstadoDeRecurso(r.Status),
            r.Environment, EtiquetasDeCatalogo.Ambiente(r.Environment),
            r.ResourceGroup, r.SubscriptionName, r.Region, r.MonthlyCostEstimate,
            r.Url, r.Notes)).ToList();
    }
}
