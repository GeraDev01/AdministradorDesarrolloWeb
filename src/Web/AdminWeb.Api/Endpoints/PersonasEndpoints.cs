using AdminWeb.Application.Services;
using AdminWeb.Domain.Documentos;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Personas;

namespace AdminWeb.Api.Endpoints;

/// <summary>
/// Las pantallas de PERSONAS: quién está, perfil y desarrollo, comunicados, la edición del
/// organigrama y las cuentas de acceso.
///
/// <para><b>Todo el grupo es SoloAdmin</b>, sin excepciones, y los servicios vuelven a comprobarlo por
/// su cuenta. Dos barreras y no una: el cliente Blazor corre en la máquina de cada persona y
/// cualquiera puede llamar a estas rutas sin pasar por él.</para>
///
/// <para><b>El salario sale por una sola ruta</b>, <c>GET /api/personas/perfiles/{developerId}</c>, que
/// devuelve la ficha de UNA persona. No viaja en ninguna lista ni en ningún resumen: en cuanto un
/// dato confidencial acompaña a una respuesta que se pide para otra cosa, se pierde la cuenta de
/// quién lo ha visto. Y nunca llega a la bitácora — de eso se encarga
/// <see cref="DeveloperProfileService"/>, que anota el cambio sin el importe.</para>
///
/// <para><b>No existe ninguna ruta para escribir el estado de presencia de otra persona</b>, ni para
/// consultar su histórico. El estado propio se cambia por el hub y no se historiza: un registro
/// minutado de las pausas de alguien es vigilancia, no asistencia. Es la decisión del escritorio y
/// se conserva tal cual.</para>
/// </summary>
public static class PersonasEndpoints
{
    private const string PoliticaDelLider = "SoloAdmin";

    public static void MapPersonasEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/personas")
            .WithTags("Personas")
            .RequireAuthorization(PoliticaDelLider);

        MapPresencia(grupo);
        MapPerfiles(grupo);
        MapComunicados(grupo);
        MapEquipos(grupo);
        MapUsuarios(grupo);
    }

    // ── Quién está ───────────────────────────────────────────────────────────────

    private static void MapPresencia(IEndpointRouteBuilder grupo)
    {
        grupo.MapGet("/presencia", async (PersonasQueryService personas, CancellationToken ct) =>
            Results.Ok(await personas.TableroAsync(ct)))
        .WithSummary("Quién tiene la aplicación abierta ahora mismo y en qué anda");

        // El día llega como fecha suelta y el servidor lo interpreta en SU zona, igual que el
        // escritorio interpretaba el DateTimePicker en la de la máquina de quien miraba.
        grupo.MapGet("/presencia/jornadas", async (
            DateOnly? dia, PersonasQueryService personas, CancellationToken ct) =>
            Results.Ok(await personas.JornadasDelDiaAsync(Dia(dia), ct)))
        .WithSummary("Registro automático de jornadas de un día");

        grupo.MapGet("/presencia/asistencia", async (
            DateOnly? dia, PersonasQueryService personas, CancellationToken ct) =>
            Results.Ok(await personas.AsistenciaDelDiaAsync(Dia(dia), ct)))
        .WithSummary("Asistencia oficial de un día, cruzada con lo que vio la aplicación");

        grupo.MapGet("/presencia/asistencia/excel", async (
            DateOnly? dia, PersonasQueryService personas, CancellationToken ct) =>
        {
            var elDia = Dia(dia);
            return ResultadosDeArchivo.Excel(
                await personas.ExcelDeAsistenciaAsync(elDia, ct),
                $"Asistencia_{elDia:yyyyMMdd}");
        })
        .WithSummary("La asistencia de ese día en una hoja de cálculo");

        grupo.MapPost("/presencia/asistencia/{registroId:int}/corregir", async (
            int registroId, CorregirAsistenciaRequest cuerpo, AttendanceService asistencia,
            CancellationToken ct) =>
        {
            // El motivo vacío llega hasta el servicio a propósito: es él quien explica por qué hace
            // falta, y ese texto es el que se enseña.
            var (ok, mensaje) = await asistencia.CorregirRegistroAsync(
                registroId, cuerpo.EntradaLocal, cuerpo.SalidaLocal, cuerpo.Motivo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Corrige un registro de asistencia, con motivo y rastro");

        grupo.MapPost("/presencia/asistencia", async (
            AltaDeAsistenciaRequest cuerpo, AttendanceService asistencia, CancellationToken ct) =>
        {
            var (ok, mensaje) = await asistencia.CrearRegistroManualAsync(
                cuerpo.UserId, cuerpo.EntradaLocal, cuerpo.SalidaLocal, cuerpo.Motivo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Da de alta a mano el día que alguien no marcó");
    }

    // ── Perfil y desarrollo ──────────────────────────────────────────────────────

    private static void MapPerfiles(IEndpointRouteBuilder grupo)
    {
        grupo.MapGet("/perfiles", async (PersonasQueryService personas, CancellationToken ct) =>
            Results.Ok(await personas.PersonasConFichaAsync(ct)))
        .WithSummary("Quién tiene ficha de desarrollo (sin ningún dato confidencial)");

        // La ÚNICA ruta por la que sale el salario, y de una persona cada vez.
        grupo.MapGet("/perfiles/{developerId:int}", async (
            int developerId, PersonasQueryService personas, CancellationToken ct) =>
        {
            var ficha = await personas.FichaAsync(developerId, ct);
            return ficha is null
                ? Results.NotFound(new ResultadoDto(false, "Esa persona ya no está en el catálogo."))
                : Results.Ok(ficha);
        })
        .WithSummary("La ficha de desarrollo de una persona (confidencial: solo el líder)");

        grupo.MapPost("/perfiles", async (
            GuardarFichaRequest cuerpo, PersonasQueryService personas, CancellationToken ct) =>
        {
            var (ok, mensaje) = await personas.GuardarFichaAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Guarda la ficha de desarrollo");
    }

    // ── Comunicados ──────────────────────────────────────────────────────────────

    private static void MapComunicados(IEndpointRouteBuilder grupo)
    {
        grupo.MapGet("/comunicados/destinatarios", async (
            PersonasQueryService personas, CancellationToken ct) =>
            Results.Ok(await personas.DestinatariosAsync(ct)))
        .WithSummary("A quién se le puede mandar un comunicado, y quién no lo recibiría");

        grupo.MapPost("/comunicados", async (
            EnviarComunicadoRequest cuerpo, AnnouncementService comunicados, CancellationToken ct) =>
        {
            var (ok, mensaje, _) = await comunicados.EnviarAsync(
                cuerpo.Titulo, cuerpo.Cuerpo, cuerpo.DeveloperIds, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Envía un comunicado como aviso a la bandeja de cada destinatario");
    }

    // ── Equipos ──────────────────────────────────────────────────────────────────

    private static void MapEquipos(IEndpointRouteBuilder grupo)
    {
        // El organigrama se sigue leyendo por /api/catalogos/equipos, que ya existe desde la fase 1.
        // Duplicarlo aquí solo habría creado dos verdades sobre quién es el líder de cada equipo.

        grupo.MapPost("/equipos", async (
            GuardarEquipoRequest cuerpo, PersonasQueryService personas, CancellationToken ct) =>
        {
            var (ok, mensaje) = await personas.GuardarEquipoAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Alta o edición de un equipo");

        grupo.MapPost("/equipos/{equipoId:int}/eliminar", async (
            int equipoId, PersonasQueryService personas, CancellationToken ct) =>
        {
            var (ok, mensaje) = await personas.EliminarEquipoAsync(equipoId, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Elimina un equipo; sus integrantes quedan sin equipo");

        grupo.MapPost("/equipos/integrantes", async (
            MoverIntegrantesRequest cuerpo, PersonasQueryService personas, CancellationToken ct) =>
        {
            var (ok, mensaje) = await personas.MoverIntegrantesAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Mueve personas a un equipo (o las deja sin equipo) y registra la rotación");

        grupo.MapPost("/equipos/rol", async (
            AsignarRolRequest cuerpo, PersonasQueryService personas, CancellationToken ct) =>
        {
            var (ok, mensaje) = await personas.AsignarRolAsync(cuerpo, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Asigna el rol de alguien dentro de su equipo");

        grupo.MapGet("/equipos/rotaciones", async (
            PersonasQueryService personas, CancellationToken ct) =>
            Results.Ok(await personas.RotacionesAsync(ct)))
        .WithSummary("Historial reciente de rotaciones entre equipos");

        grupo.MapGet("/equipos/pdf", async (
            PersonasQueryService personas, IGeneradorDeDocumentos documentos, CancellationToken ct) =>
        {
            var datos = await personas.DatosDeEquiposAsync(ct);

            // El documento se maqueta en memoria y se devuelve como archivo: nada se escribe en disco
            // y es el navegador quien decide dónde guardarlo, en vez del «guardar como» del escritorio.
            var pdf = documentos.OrganizacionDeEquipos(datos);
            return Results.File(pdf, "application/pdf",
                $"Organizacion_Equipos_{DateTime.Now:yyyyMMdd}.pdf");
        })
        .WithSummary("La organización de equipos en PDF, para imprimirla o repartirla");
    }

    // ── Usuarios ─────────────────────────────────────────────────────────────────

    private static void MapUsuarios(IEndpointRouteBuilder grupo)
    {
        grupo.MapGet("/usuarios", async (PersonasQueryService personas, CancellationToken ct) =>
            Results.Ok(await personas.UsuariosAsync(ct)))
        .WithSummary("Las cuentas de acceso, sin hashes ni sellos de sesión");

        grupo.MapPost("/usuarios", async (
            CrearUsuarioRequest cuerpo, PersonasQueryService personas, CancellationToken ct) =>
        {
            var (ok, mensaje, temporal) = await personas.CrearUsuarioAsync(cuerpo, ct);
            return ok
                ? Results.Ok(new ContrasenaTemporalDto(true, mensaje, temporal))
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .WithSummary("Da de alta una cuenta y devuelve su contraseña temporal (una sola vez)");

        grupo.MapPost("/usuarios/{userId:int}/editar", async (
            int userId, ActualizarUsuarioRequest cuerpo, PersonasQueryService personas,
            CancellationToken ct) =>
        {
            // El identificador de la ruta manda sobre el del cuerpo: si no coincidieran, editar «la
            // cuenta de la fila» acabaría escribiendo sobre otra.
            var (ok, mensaje) = await personas.ActualizarUsuarioAsync(cuerpo with { Id = userId }, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Edita una cuenta (usuario, nombre, rol, ficha ligada y acceso)");

        grupo.MapPost("/usuarios/{userId:int}/activo", async (
            int userId, PersonasQueryService personas, CancellationToken ct) =>
        {
            var (ok, mensaje) = await personas.AlternarActivoAsync(userId, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Activa o desactiva una cuenta");

        grupo.MapPost("/usuarios/{userId:int}/contrasena", async (
            int userId, AuthService auth, CancellationToken ct) =>
        {
            var (ok, mensaje, temporal) = await auth.ResetPasswordAsync(userId, ct);
            return ok
                ? Results.Ok(new ContrasenaTemporalDto(true, mensaje, temporal))
                : Results.BadRequest(new ResultadoDto(false, mensaje));
        })
        .WithSummary("Restablece la contraseña y devuelve la temporal (una sola vez)");

        grupo.MapPost("/usuarios/{userId:int}/desbloquear", async (
            int userId, AuthService auth, CancellationToken ct) =>
        {
            var (ok, mensaje) = await auth.DesbloquearCuentaAsync(userId, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Levanta el bloqueo por intentos fallidos (no toca la contraseña)");

        grupo.MapPost("/usuarios/{userId:int}/eliminar", async (
            int userId, PersonasQueryService personas, CancellationToken ct) =>
        {
            var (ok, mensaje) = await personas.EliminarUsuarioAsync(userId, ct);
            return Resultado(ok, mensaje);
        })
        .WithSummary("Elimina una cuenta que no sea de líder");
    }

    // ── Comunes ──────────────────────────────────────────────────────────────────

    /// <summary>El día pedido, o hoy. Se resuelve en el servidor porque es su zona la que manda.</summary>
    private static DateTime Dia(DateOnly? dia) =>
        (dia ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);

    /// <summary>
    /// Un rechazo de negocio sale como 400 con su mensaje, no como excepción. El texto lo escribió el
    /// servicio y explica el motivo en concreto —«es la única cuenta de líder activa»—; eso es lo que
    /// se enseña.
    /// </summary>
    private static IResult Resultado(bool ok, string mensaje) =>
        ok ? Results.Ok(new ResultadoDto(true, mensaje))
           : Results.BadRequest(new ResultadoDto(false, mensaje));
}
