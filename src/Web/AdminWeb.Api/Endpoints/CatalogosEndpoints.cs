using AdminWeb.Application.Services;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Catalogos;
using AdminWeb.Shared.Enums;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Los catálogos del área: desarrolladores, equipos, contactos, programas y recursos de Azure.
///
/// Todo el grupo es SoloAdmin. La consulta la sirve <see cref="CatalogosQueryService"/> y la
/// escritura <see cref="CatalogosService"/>; están separados porque el 90% de las peticiones a estas
/// rutas son de lectura y así el servicio que se resuelve en ellas no arrastra la maquinaria de
/// auditar ni de crear cuentas.
///
/// El borrado de verdad solo existe para contactos. En desarrolladores la baja DESACTIVA —su ficha
/// está referenciada por asignaciones, evaluaciones y bitácora—, y programas y recursos no tienen
/// baja tampoco en el escritorio.
/// </summary>
public static class CatalogosEndpoints
{
    public static void MapCatalogosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/catalogos")
            .WithTags("Catálogos")
            .RequireAuthorization("SoloAdmin");

        grupo.MapGet("/desarrolladores", async (
            string? texto, bool? soloActivos, CatalogosQueryService catalogos, CancellationToken ct) =>
        {
            // soloActivos por omisión en true, igual que la casilla marcada del escritorio.
            var filas = await catalogos.DesarrolladoresAsync(texto, soloActivos ?? true, ct);
            return Results.Ok(filas);
        })
        .WithSummary("Lista de desarrolladores, con búsqueda y filtro de activos");

        grupo.MapGet("/equipos", async (CatalogosQueryService catalogos, CancellationToken ct) =>
            Results.Ok(await catalogos.OrganizacionAsync(ct)))
        .WithSummary("Equipos con su líder e integrantes, más quienes están sin equipo");

        grupo.MapGet("/desarrolladores/excel", async (
            string? texto, bool? soloActivos, CatalogosQueryService catalogos, CancellationToken ct) =>
            ResultadosDeArchivo.Excel(
                await catalogos.ExcelDeDesarrolladoresAsync(texto, soloActivos ?? true, ct),
                "Desarrolladores"))
        .WithSummary("Los desarrolladores del filtro, en una hoja de cálculo");

        grupo.MapGet("/contactos", async (
            string? texto, CatalogosQueryService catalogos, CancellationToken ct) =>
            Results.Ok(await catalogos.ContactosAsync(texto, ct)))
        .WithSummary("Contactos de la empresa");

        grupo.MapGet("/contactos/excel", async (
            string? texto, CatalogosQueryService catalogos, CancellationToken ct) =>
            ResultadosDeArchivo.Excel(await catalogos.ExcelDeContactosAsync(texto, ct), "Contactos"))
        .WithSummary("Los contactos del filtro, en una hoja de cálculo");

        grupo.MapGet("/programas", async (
            string? texto, SoftwareCategory? categoria, SoftwareStatus? estado,
            CatalogosQueryService catalogos, CancellationToken ct) =>
            Results.Ok(await catalogos.ProgramasAsync(texto, categoria, estado, ct)))
        .WithSummary("Inventario de programas y licencias");

        grupo.MapGet("/recursos-azure", async (
            string? texto, AzureResourceType? tipo, AzureResourceStatus? estado,
            AzureEnvironment? ambiente, CatalogosQueryService catalogos, CancellationToken ct) =>
            Results.Ok(await catalogos.RecursosAzureAsync(texto, tipo, estado, ambiente, ct)))
        .WithSummary("Inventario de recursos de Azure");

        // ── Escritura ────────────────────────────────────────────────────────────

        grupo.MapPost("/desarrolladores", async (
            GuardarDesarrolladorRequest cuerpo, CatalogosService catalogos, CancellationToken ct) =>
        {
            var (ok, mensaje, _) = await catalogos.GuardarDesarrolladorAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Alta o edición de un desarrollador");

        grupo.MapPost("/desarrolladores/{developerId:int}/desactivar", async (
            int developerId, CatalogosService catalogos, CancellationToken ct) =>
        {
            var (ok, mensaje) = await catalogos.DesactivarDesarrolladorAsync(developerId, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Desactiva a un desarrollador; no borra su ficha ni su historial");

        grupo.MapPost("/desarrolladores/{developerId:int}/acceso", async (
            int developerId, CatalogosService catalogos, CancellationToken ct) =>
        {
            var (ok, mensaje, credenciales) = await catalogos.CrearAccesoAsync(developerId, ct);
            return ok
                ? Results.Ok(credenciales)
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .WithSummary("Crea la cuenta de acceso del desarrollador y devuelve su contraseña temporal");

        // GET y no POST porque no cambia nada: es una calculadora. Va al servidor —y no al
        // navegador, que también sabría hacerla— para que «hoy» sea el mismo día para todos.
        grupo.MapGet("/desarrolladores/lft", (
            DateTime fechaIngreso, CatalogosService catalogos) =>
            Results.Ok(catalogos.SugerenciaDeVacaciones(fechaIngreso)))
        .WithSummary("Días de vacaciones que corresponden por ley a esa fecha de ingreso");

        grupo.MapPost("/contactos", async (
            GuardarContactoRequest cuerpo, CatalogosService catalogos, CancellationToken ct) =>
        {
            var (ok, mensaje, _) = await catalogos.GuardarContactoAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Alta o edición de un contacto");

        grupo.MapPost("/contactos/{contactoId:int}/eliminar", async (
            int contactoId, CatalogosService catalogos, CancellationToken ct) =>
        {
            var (ok, mensaje) = await catalogos.BorrarContactoAsync(contactoId, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Elimina un contacto");

        grupo.MapPost("/programas", async (
            GuardarProgramaRequest cuerpo, CatalogosService catalogos, CancellationToken ct) =>
        {
            var (ok, mensaje, _) = await catalogos.GuardarProgramaAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Alta o edición de un programa del inventario");

        // POST aunque solo lee: deja una fila en la bitácora, así que no es repetible sin
        // consecuencia. Un GET aquí acabaría cacheado por el navegador o precargado por un
        // acelerador de enlaces, y la bitácora diría que alguien miró una clave que nunca vio.
        grupo.MapPost("/programas/{programaId:int}/clave", async (
            int programaId, CatalogosService catalogos, CancellationToken ct) =>
        {
            var (ok, mensaje, clave) = await catalogos.VerClaveDeLicenciaAsync(programaId, ct);
            return ok
                ? Results.Ok(new ClaveDeLicenciaDto(clave!))
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .WithSummary("Enseña la clave de licencia de un programa y lo anota en la bitácora");

        grupo.MapPost("/recursos-azure", async (
            GuardarRecursoAzureRequest cuerpo, CatalogosService catalogos, CancellationToken ct) =>
        {
            var (ok, mensaje, _) = await catalogos.GuardarRecursoAzureAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Alta o edición de un recurso de Azure");
    }

    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));
}
