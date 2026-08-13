using AdminWeb.Domain.Documentos;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Equipos;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos;
using AdminWeb.Shared.Dtos.Personas;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Todo lo que las pantallas de PERSONAS necesitan del servidor: quién está, la ficha de desarrollo,
/// los comunicados, la organización de equipos y las cuentas de acceso.
///
/// <para>Es una consulta propia de la web, del mismo corte que <see cref="JornadaQueryService"/>: en
/// el escritorio cada control hablaba directo con la base local y encadenar cinco consultas no
/// costaba nada; aquí cada una es un viaje de red y una pantalla que se pinta en cuatro tandas
/// parpadea. Los servicios portados —presencia, asistencia, fichas, comunicados, acceso— siguen
/// siendo los dueños de su lógica: esto los junta y traduce a DTO.</para>
///
/// <para><b>Sí escribe</b>, a diferencia de <see cref="CatalogosQueryService"/>, y es lo que hace que
/// la fase 3 exista: reorganizar equipos y administrar cuentas son escrituras que en el escritorio
/// vivían dentro de los propios controles (<c>TeamsControl</c>, <c>UserManagementControl</c>) y que
/// aquí no tenían servicio al que ir. La alternativa era tocar <see cref="AuthService"/>, que está
/// portado y probado, para meterle un CRUD que no tenía.</para>
///
/// <para><b>El salario NO pasa por aquí más que en un sitio</b>: <see cref="FichaAsync"/> y
/// <see cref="GuardarFichaAsync"/>, que delegan en <see cref="DeveloperProfileService"/>. Este
/// servicio no escribe ni una línea de bitácora sobre la ficha — la escribe aquel, sin el importe.
/// Ver <see cref="FichaDeDesarrolloDto"/>.</para>
/// </summary>
public class PersonasQueryService(
    AppDbContext db,
    ICurrentUser currentUser,
    AuditService audit,
    AuthService auth,
    PresenceService presencia,
    AttendanceService asistencia,
    DeveloperProfileService fichas,
    AnnouncementService comunicados)
{
    /// <summary>Cuántas rotaciones enseña el historial. Es un vistazo, no un archivo: para eso está la bitácora.</summary>
    public const int RotacionesQueSeEnsenan = 100;

    /// <summary>
    /// Lo que cabe en la función de una persona dentro de su equipo. Es una frase, no una descripción
    /// de puesto: en el organigrama ocupa dos renglones de una tarjeta, y lo que no cabe se corta —así
    /// que dejar escribir mil caracteres sería prometer que se van a leer.
    /// </summary>
    public const int LargoMaximoDeFuncion = 200;

    /// <summary>Las pantallas de personas son del líder. Segunda barrera, la que viaja pegada al dato.</summary>
    private void SoloAdmin() => AuthorizationGuard.RequireAdmin(currentUser);

    // ── Presencia: quién está ────────────────────────────────────────────────────

    /// <summary>
    /// El tablero de «quién está» ahora mismo.
    ///
    /// Incluye a TODAS las cuentas activas y no solo a las conectadas, igual que en el escritorio: si
    /// alguien falta, esa ausencia es justamente el dato que se viene a mirar.
    /// </summary>
    public async Task<TableroDePresenciaDto> TableroAsync(CancellationToken ct = default)
    {
        // La guarda la pone TableroAsync del servicio portado; repetirla aquí sería duplicar la regla.
        var filas = await presencia.TableroAsync(ct);

        var personas = filas.Select(p => new PresenteDto(
            p.UserId,
            p.Nombre,
            p.Conectado,
            p.Estado,
            p.Conectado ? PresenceService.Etiqueta(p.Estado) : "Desconectado",
            p.Conectado ? PresenceService.Icono(p.Estado) : "⚪",
            p.Nota,
            p.DesdeUtc,
            // DateTime.MinValue significa «nunca ha entrado»; se traduce a null para que la pantalla
            // no tenga que conocer ese centinela ni pueda pintar por error un 01/01/0001.
            p.UltimoLatidoUtc == DateTime.MinValue ? null : p.UltimoLatidoUtc,
            // Se COPIA tal cual y no se recalcula aquí: quien decide quién registra jornada es
            // PresenceService, y una segunda opinión en este renglón podría contradecir al registro.
            // Sin esta línea el dato se calcularía y se tiraría, y la pantalla volvería a decir
            // «nunca ha entrado» de quien lleva años entrando.
            p.RegistraJornada))
            .ToList();

        return new TableroDePresenciaDto(
            personas,
            personas.Count(p => p.Conectado),
            personas.Count,
            (int)PresenceService.ToleranciaSinLatido.TotalMinutes);
    }

    /// <summary>
    /// El registro automático de jornadas de un día: lo que la aplicación vio sola.
    ///
    /// Va aparte de la asistencia oficial y no se deduce de ella, igual que en el escritorio: es el
    /// contraste entre las dos lo que delata un olvido.
    /// </summary>
    public async Task<RegistroDeJornadasDto> JornadasDelDiaAsync(DateTime diaLocal, CancellationToken ct = default)
    {
        var jornadas = await presencia.JornadasDelDiaAsync(diaLocal, ct: ct);

        var filas = jornadas.Select(j => new JornadaDelDiaDto(
            j.Id,
            j.DisplayName,
            j.StartedAtUtc,
            j.EndedAtUtc,
            PresenceService.Duracion(j.Duracion),
            j.Abierta,
            j.EndReason == PresenceEnd.SinLatido,
            // ESTE triángulo SE QUEDA, aunque el de la columna «Estado» de la asistencia se haya
            // quitado y por los mismos motivos valdría quitarlo aquí. La razón es que la leyenda de
            // la pantalla de presencia lo CITA entre comillas —«⚠ Sin señales» = la aplicación dejó
            // de responder…—, así que quitarlo solo aquí dejaría la leyenda mandando a buscar en la
            // rejilla algo que ya no está escrito así. Los dos cambios van juntos o no van, y el
            // otro está en Paginas/Personas/Presencia.razor.
            j.EndReason switch
            {
                PresenceEnd.CierreNormal => "Cerró sesión",
                PresenceEnd.SinLatido    => "⚠ Sin señales",
                _                        => "En curso"
            },
            j.Origin))
            .ToList();

        var total = TimeSpan.FromTicks(jornadas.Sum(j => j.Duracion.Ticks));
        return new RegistroDeJornadasDto(
            filas,
            jornadas.Select(j => j.UserId).Distinct().Count(),
            PresenceService.Duracion(total));
    }

    /// <summary>
    /// La asistencia OFICIAL de un día: lo que cada quien marcó a mano, cruzado con la telemetría.
    /// </summary>
    public async Task<AsistenciaDelDiaResumenDto> AsistenciaDelDiaAsync(
        DateTime diaLocal, CancellationToken ct = default)
    {
        var filas = await asistencia.AsistenciaDelDiaAsync(diaLocal, ct);

        var mapeadas = filas.Select(f => new AsistenciaDelDiaDto(
            f.UserId,
            f.Nombre,
            f.RegistroId,
            f.EntradaOficialUtc,
            f.SalidaOficialUtc,
            f.EntradaOficialUtc is DateTime e && f.SalidaOficialUtc is DateTime s
                ? PresenceService.Duracion(s - e)
                : "",
            AMinutos(f.DeltaEntrada),
            AMinutos(f.DeltaSalida),
            f.PrimeraSenalAutoUtc,
            f.UltimaSenalAutoUtc,
            EstadoDeAsistencia(f),
            f.SinMarcar,
            f.Cierre == AttendanceCloseKind.Olvido,
            f.HayDiscrepancia,
            f.CorreccionSolicitada,
            f.NotaCorreccion))
            .ToList();

        return new AsistenciaDelDiaResumenDto(
            mapeadas,
            mapeadas.Count(f => f.RegistroId != null),
            mapeadas.Count(f => f.SinMarcar),
            mapeadas.Count(f => f.Olvido),
            mapeadas.Count(f => f.CorreccionSolicitada),
            (int)AttendanceService.ToleranciaDiscrepancia.TotalMinutes);
    }

    /// <summary>
    /// La asistencia de un día en una hoja de cálculo. Mismas diez columnas que el escritorio.
    ///
    /// <para>Las horas van como TEXTO y no como fecha, igual que allí y por la misma razón: el
    /// escritor de hojas formatea un <c>DateTime</c> como <c>dd/MM/yyyy</c> sin hora, y una columna
    /// «Entrada» sin hora no dice absolutamente nada.</para>
    ///
    /// <para>Se exporta el día ENTERO tal como se ve en pantalla —incluidas las filas sin marcar—:
    /// esa ausencia también es el dato, y de hecho suele ser el motivo por el que alguien exporta.</para>
    /// </summary>
    public async Task<byte[]> ExcelDeAsistenciaAsync(DateTime diaLocal, CancellationToken ct = default)
    {
        var resumen = await AsistenciaDelDiaAsync(diaLocal, ct);

        return HojaDeCalculo.Escribir(
            ["Persona", "Entrada", "Salida", "Horas", "Δ entrada", "Δ salida",
             "1ª señal app", "Últ. señal app", "Estado", "Pidió corrección"],
            [.. resumen.Filas.Select(f => new object?[]
            {
                f.Nombre,
                Hora(f.EntradaUtc),
                Hora(f.SalidaUtc),
                f.Horas,
                Delta(f.DeltaEntradaMinutos),
                Delta(f.DeltaSalidaMinutos),
                Hora(f.PrimeraSenalUtc),
                Hora(f.UltimaSenalUtc),
                f.EstadoTexto,
                f.NotaCorreccion ?? ""
            })],
            "Asistencia");
    }

    /// <summary>La hora en local, porque quien lee la hoja está en su huso, no en UTC.</summary>
    private static string Hora(DateTime? utc) => utc?.ToLocalTime().ToString("HH:mm") ?? "—";

    /// <summary>El desfase con signo: «+7 min» se lee de un vistazo, «7» no dice hacia dónde.</summary>
    private static string Delta(int? minutos) =>
        minutos is null ? "—" : $"{(minutos >= 0 ? "+" : "")}{minutos} min";

    /// <summary>
    /// Cómo se lee la fila: el mismo texto del escritorio, ya sin los pictogramas que llevaba
    /// delante («Sin marcar» tenía un triángulo y «Pide corrección», una mano levantada).
    ///
    /// <para>Se quitaron por lo mismo que los del menú y la barra: los dibuja el SISTEMA OPERATIVO,
    /// así que cambian de forma según el equipo, no heredan el color del texto —en el tema oscuro
    /// siguen brillando con los suyos— y donde no hay fuente de emoji salen como un CUADRO VACÍO.
    /// Sustituirlos por un icono de la fuente no se puede: esto es una CADENA que la rejilla imprime
    /// tal cual, y devolver ahí un nombre de icono pintaría la palabra dentro de la celda.</para>
    ///
    /// <para><b>El aviso no se pierde con la marca.</b> La pantalla resalta en ámbar la fila que
    /// reclama algo usando los booleanos del DTO —olvido, corrección solicitada, sin marcar—, que es
    /// información que viaja aparte y no depende de que nadie sepa leer un triángulo.</para>
    ///
    /// <para>La segunda mitad la escribe <see cref="AttendanceService.EtiquetaCierre"/> y se limpió
    /// en el mismo lote: se CONCATENAN («Pide corrección · Olvido (estimada)»), así que una sola de
    /// las dos sin marca habría dejado la etiqueta a medio decorar.</para>
    /// </summary>
    private static string EstadoDeAsistencia(AsistenciaDelDiaFila f)
    {
        if (f.RegistroId == null) return f.SinMarcar ? "Sin marcar" : "Sin actividad";
        var estado = AttendanceService.EtiquetaCierre(f.Cierre);
        return f.CorreccionSolicitada ? $"Pide corrección · {estado}" : estado;
    }

    /// <summary>El delta con signo, en minutos enteros: «+12» = marcó después de lo que vio la máquina.</summary>
    private static int? AMinutos(TimeSpan? delta) =>
        delta is TimeSpan d ? (int)Math.Round(d.TotalMinutes) : null;

    // ── Perfil y desarrollo ──────────────────────────────────────────────────────

    /// <summary>
    /// La lista de la izquierda: quién tiene ficha y cuándo se actualizó.
    ///
    /// Solo desarrolladores ACTIVOS, igual que el escritorio: la ficha de quien ya no está no se
    /// borra, pero tampoco tiene por qué estorbar en la lista del día a día.
    ///
    /// <b>Sin salario.</b> Ver <see cref="PersonaConFichaDto"/>.
    /// </summary>
    public async Task<IReadOnlyList<PersonaConFichaDto>> PersonasConFichaAsync(CancellationToken ct = default)
    {
        SoloAdmin();

        var personas = await db.Developers.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.FullName)
            .Select(d => new { d.Id, d.FullName })
            .ToListAsync(ct);

        // Solo el identificador y la fecha: esta consulta NO toca las columnas de la ficha, así que
        // el importe no llega ni siquiera a la memoria del servidor mientras se arma esta lista.
        var actualizadas = await db.DeveloperProfiles.AsNoTracking()
            .Select(p => new { p.DeveloperId, p.UpdatedAt })
            .ToDictionaryAsync(x => x.DeveloperId, x => x.UpdatedAt, ct);

        return personas.Select(d => new PersonaConFichaDto(
            d.Id,
            d.FullName,
            actualizadas.ContainsKey(d.Id),
            actualizadas.TryGetValue(d.Id, out var cuando) ? cuando : null)).ToList();
    }

    /// <summary>
    /// La ficha de una persona, con su salario. Es la ÚNICA respuesta del vertical que lo lleva.
    /// La guarda de administrador la impone <see cref="DeveloperProfileService.ObtenerAsync"/>.
    /// </summary>
    public async Task<FichaDeDesarrolloDto?> FichaAsync(int developerId, CancellationToken ct = default)
    {
        var ficha = await fichas.ObtenerAsync(developerId, ct);   // guarda de admin dentro

        var nombre = await db.Developers.AsNoTracking()
            .Where(d => d.Id == developerId)
            .Select(d => d.FullName)
            .FirstOrDefaultAsync(ct);
        if (nombre == null) return null;

        // Sin ficha todavía: se devuelve el esqueleto vacío en vez de un 404, porque la pantalla tiene
        // que dejar capturarla — «no existe» aquí significa «aún no la has escrito».
        return ficha == null
            ? new FichaDeDesarrolloDto(developerId, nombre, null, null, null, null, null, null, null, null)
            : new FichaDeDesarrolloDto(
                developerId, nombre, ficha.Strengths, ficha.Weaknesses, ficha.TechStack,
                ficha.Salary, ficha.Currency, ficha.GrowthExpectations, ficha.Notes, ficha.UpdatedAt);
    }

    /// <summary>
    /// Guarda la ficha.
    ///
    /// Delega TODO en <see cref="DeveloperProfileService.GuardarAsync"/> y no añade ni una línea de
    /// bitácora propia. Es a propósito: aquel deja constancia del cambio SIN el importe, y una
    /// segunda anotación desde aquí sería justo la forma en que el salario acabaría filtrándose a la
    /// bitácora sin que nadie lo hubiera decidido.
    /// </summary>
    public Task<(bool ok, string mensaje)> GuardarFichaAsync(
        GuardarFichaRequest peticion, CancellationToken ct = default) =>
        fichas.GuardarAsync(
            peticion.DeveloperId,
            peticion.Fortalezas,
            peticion.Debilidades,
            peticion.Stack,
            // 0 es «sin registrar», igual que el 0 del control del escritorio: guardarlo como importe
            // haría creer que a alguien se le paga cero.
            peticion.Salario is > 0 ? peticion.Salario : null,
            peticion.Moneda,
            peticion.Crecimiento,
            peticion.Notas,
            ct);

    // ── Comunicados ──────────────────────────────────────────────────────────────

    /// <summary>Los posibles destinatarios, marcando quién tiene cuenta y por tanto puede recibirlo.</summary>
    public async Task<IReadOnlyList<DestinatarioDeComunicadoDto>> DestinatariosAsync(CancellationToken ct = default)
    {
        var destinatarios = await comunicados.DestinatariosPosiblesAsync(ct);   // guarda de admin dentro
        return destinatarios
            .Select(d => new DestinatarioDeComunicadoDto(d.DeveloperId, d.Nombre, d.TieneCuenta))
            .ToList();
    }

    // ── Equipos ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Alta o edición de un equipo, incluido de qué equipo cuelga.
    ///
    /// <para><b>El círculo se impide AQUÍ y no en la pantalla</b>: un equipo no puede ser su propio
    /// ancestro ni directa ni indirectamente. La pantalla puede no ofrecer los descendientes en su
    /// desplegable —y no los ofrece—, pero eso es una comodidad; esta dirección se puede llamar a
    /// mano, y un ciclo no deja un dibujo raro: deja un organigrama que no se puede recorrer, una
    /// rama que no se puede sumar y una pantalla que no carga.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> GuardarEquipoAsync(
        GuardarEquipoRequest peticion, CancellationToken ct = default)
    {
        SoloAdmin();

        var nombre = (peticion.Nombre ?? "").Trim();
        if (nombre.Length == 0) return (false, "El equipo necesita un nombre.");
        if (nombre.Length > 100) return (false, "El nombre del equipo no puede pasar de 100 caracteres.");

        // Dos equipos con el mismo nombre son indistinguibles en el organigrama y en el PDF, que es
        // donde se reparte esta información: quien lo lea no sabría a cuál de los dos pertenece nadie.
        bool repetido = await db.Teams
            .AnyAsync(t => t.Id != peticion.Id && t.Name == nombre, ct);
        if (repetido) return (false, $"Ya hay un equipo llamado «{nombre}».");

        var equipo = peticion.Id > 0
            ? await db.Teams.FirstOrDefaultAsync(t => t.Id == peticion.Id, ct)
            : null;
        if (peticion.Id > 0 && equipo == null) return (false, "Ese equipo ya no existe. Actualiza la pantalla.");

        string? nombreDelPadre = null;
        if (peticion.EquipoPadreId is int padreId)
        {
            // El árbol entero, de una consulta, y SOLO cuando hay padre que validar: guardar un
            // equipo raíz —que es lo que se hace hoy— no paga ninguna lectura de más. Aquí hacen
            // falta dos respuestas —¿existe ese padre? y ¿colgarlo de él cerraría un círculo?— y las
            // dos tienen que mirar la MISMA foto. Son unas decenas de filas; ver JerarquiaDeEquipos
            // para por qué no se hace con SQL recursivo.
            var arbol = await db.Teams.AsNoTracking()
                .Select(t => new { t.Id, t.Name, t.EquipoPadreId })
                .ToListAsync(ct);

            var padre = arbol.FirstOrDefault(t => t.Id == padreId);
            if (padre == null) return (false, "Ese equipo padre ya no existe. Actualiza la pantalla.");
            nombreDelPadre = padre.Name;

            // Un equipo NUEVO todavía no tiene nada debajo (su identificador es 0 y no está en el
            // árbol), así que esta comprobación solo puede saltar editando uno que ya existe.
            var jerarquia = JerarquiaDeEquipos.De(arbol.Select(t => (t.Id, t.EquipoPadreId)));
            if (jerarquia.SeriaCiclo(peticion.Id, padreId))
                return (false, padreId == peticion.Id
                    ? $"«{nombre}» no puede colgar de sí mismo."
                    : $"«{nombre}» no puede colgar de «{padre.Name}»: «{padre.Name}» ya está debajo " +
                      "de él y la jerarquía se cerraría en círculo.");
        }

        bool esNuevo = equipo == null;
        int? padreAnterior = equipo?.EquipoPadreId;
        if (equipo == null)
        {
            equipo = new Team { CreatedAt = DateTime.UtcNow };
            db.Teams.Add(equipo);
        }

        equipo.Name          = nombre;
        equipo.Description   = Limpiar(peticion.Descripcion);
        equipo.ColorHex      = Limpiar(peticion.ColorHex);
        equipo.EquipoPadreId = peticion.EquipoPadreId;
        await db.SaveChangesAsync(ct);

        // SEGUNDA VUELTA, con lo que quedó escrito de verdad. La comprobación de arriba mira el árbol
        // de ANTES de guardar, y dos personas guardando a la vez pasan las dos por ella con cambios
        // que por separado son válidos y juntos cierran el círculo: «A cuelga de B» y «B cuelga de A»
        // llegando a la vez. Como cada quien relee DESPUÉS de haber escrito lo suyo, el último en
        // confirmar ve ya las dos escrituras y deshace la suya; si los dos llegan a verlo, los dos
        // deshacen y la jerarquía se queda como estaba, que es el peor caso aceptable.
        //
        // Un equipo RECIÉN CREADO no necesita esta vuelta: su identificador no existía cuando los
        // demás leyeron el árbol, así que nadie ha podido colgar nada de él mientras tanto.
        if (!esNuevo && peticion.EquipoPadreId != null && await CuelgaDeSiMismoAsync(equipo.Id, ct))
        {
            equipo.EquipoPadreId = padreAnterior;
            await db.SaveChangesAsync(ct);
            return (false, "Otro cambio de la jerarquía entró al mismo tiempo y entre los dos dejaban " +
                           "equipos colgando en círculo. Este se quedó como estaba: vuelve a intentarlo.");
        }

        await audit.RecordAsync(esNuevo ? AuditAction.Create : AuditAction.Update,
            "Team", equipo.Id.ToString(),
            // De quién cuelga va a la bitácora: es la línea que explica por qué el organigrama de
            // ayer no se parece al de hoy, y sin ella la anotación solo diría que alguien lo editó.
            nombreDelPadre == null ? equipo.Name : $"{equipo.Name} (subequipo de «{nombreDelPadre}»)", ct);

        var donde = nombreDelPadre == null ? "" : $" Cuelga de «{nombreDelPadre}».";
        return (true, (esNuevo ? $"Equipo «{equipo.Name}» creado." : $"Equipo «{equipo.Name}» actualizado.") + donde);
    }

    /// <summary>
    /// ¿Este equipo acabó colgando de sí mismo? Se pregunta contra lo que hay ESCRITO en la base, no
    /// contra lo que este servicio creía saber: es la comprobación de después de guardar.
    /// </summary>
    private async Task<bool> CuelgaDeSiMismoAsync(int equipoId, CancellationToken ct)
    {
        var arbol = await db.Teams.AsNoTracking()
            .Select(t => new { t.Id, t.EquipoPadreId })
            .ToListAsync(ct);

        return JerarquiaDeEquipos.De(arbol.Select(t => (t.Id, t.EquipoPadreId))).EsSuPropioAncestro(equipoId);
    }

    /// <summary>
    /// Elimina un equipo. Sus integrantes quedan SIN equipo, no se borran: la advertencia del
    /// escritorio decía justo eso y sigue siendo verdad.
    ///
    /// <para><b>Y sus subequipos SUBEN</b> a colgar de donde colgaba él —o quedan como raíz, si él lo
    /// era—, en vez de irse detrás. Borrar un equipo intermedio no es borrar la rama: quien lo borra
    /// está deshaciendo un nivel de agrupación, no dando de baja a tres equipos con su gente dentro.
    /// Dejarlos apuntando al que ya no está tampoco es opción: la clave foránea de la base lo
    /// rechazaría, y donde la base no mira quedarían equipos fuera del organigrama.</para>
    ///
    /// <para>Con una sola salvedad, que solo aparece si en la base hay un círculo escrito a mano: el
    /// subequipo al que ese abuelo le cerraría el círculo se queda como RAÍZ. Ver el porqué donde se
    /// hace.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> EliminarEquipoAsync(int equipoId, CancellationToken ct = default)
    {
        SoloAdmin();

        var equipo = await db.Teams.FirstOrDefaultAsync(t => t.Id == equipoId, ct);
        if (equipo == null) return (false, "Ese equipo ya no existe. Actualiza la pantalla.");

        // La función se borra junto con el rol y por lo mismo: describía lo que esa persona hacía
        // DENTRO de este equipo, y el equipo deja de existir. Sin esta línea quedaría gente «sin
        // equipo» arrastrando la responsabilidad de un equipo que ya no está en ninguna pantalla.
        var integrantes = await db.Developers.Where(d => d.TeamId == equipo.Id).ToListAsync(ct);
        foreach (var d in integrantes) { d.TeamId = null; d.TeamRole = TeamRole.SinRol; d.TeamFunction = null; }

        // Los hijos DIRECTOS y no la rama entera: al colgarlos del abuelo, los nietos siguen colgando
        // de ellos y se suben solos con su rama puesta.
        var abuelo = equipo.EquipoPadreId;
        var subequipos = await db.Teams.Where(t => t.EquipoPadreId == equipo.Id).ToListAsync(ct);

        // El abuelo pasa por la MISMA regla que cualquier otra escritura de esta columna: si colgar de
        // él cerrara un círculo, el subequipo sube a raíz. Con datos sanos esta comprobación no cambia
        // nunca nada —el abuelo es un ancestro y un ancestro jamás está debajo de su nieto—, y por eso
        // el árbol solo se lee cuando hay abuelo y hay subequipos que recolocar.
        //
        // Existe por el caso torcido: si en la base hubiera un círculo escrito a mano, el abuelo puede
        // ser el PROPIO subequipo, y entonces esta línea lo dejaría colgando de sí mismo. Sería un dato
        // corrupto NUEVO, escrito por una operación normal y sin que nadie se entere —un equipo que se
        // apunta a sí mismo se dibuja como raíz—, y eso es justo lo que ninguna escritura de aquí
        // puede permitirse: los ciclos se aguantan al leer, no se propagan al escribir.
        JerarquiaDeEquipos? jerarquia = null;
        if (abuelo is not null && subequipos.Count > 0)
        {
            var arbol = await db.Teams.AsNoTracking()
                .Select(t => new { t.Id, t.EquipoPadreId })
                .ToListAsync(ct);
            jerarquia = JerarquiaDeEquipos.De(arbol.Select(t => (t.Id, t.EquipoPadreId)));
        }

        foreach (var s in subequipos)
            s.EquipoPadreId = jerarquia is not null && !jerarquia.SeriaCiclo(s.Id, abuelo) ? abuelo : null;

        // El cargo de líder se suelta antes de borrar: la FK apunta a la ficha y quedaría colgando.
        equipo.LeadDeveloperId = null;

        var nombre = equipo.Name;
        db.Teams.Remove(equipo);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Delete, "Team", equipoId.ToString(), nombre, ct);

        var mensaje = $"Equipo «{nombre}» eliminado.";
        if (integrantes.Count > 0) mensaje += $" {integrantes.Count} integrante(s) quedaron sin equipo.";
        // Lo que de verdad pasó y no lo que se pretendía: normalmente coinciden, pero un subequipo al
        // que el abuelo le habría cerrado un círculo se quedó como raíz aunque hubiera abuelo.
        if (subequipos.Count > 0)
            mensaje += subequipos.All(s => s.EquipoPadreId is null)
                ? $" {subequipos.Count} subequipo(s) quedaron como equipos raíz."
                : $" {subequipos.Count} subequipo(s) subieron un nivel.";
        return (true, mensaje);
    }

    /// <summary>
    /// Mueve personas de equipo y deja la rotación registrada.
    ///
    /// Es la traducción del arrastrar y soltar del escritorio, y las reglas son:
    /// <list type="bullet">
    ///   <item>quien era líder del equipo de origen SUELTA el cargo, porque ya no está ahí;</item>
    ///   <item><b>el rol viaja con la persona</b> —quien es backend lo sigue siendo—, salvo «Líder»,
    ///         que se cae a «sin rol»: ese cargo lo da quien recibe, no el gesto de moverla;</item>
    ///   <item>la función se borra, porque describe una responsabilidad dentro del equipo que se
    ///         deja y no algo que la persona sepa hacer;</item>
    ///   <item>queda una fila de rotación con los NOMBRES copiados, para que el historial sobreviva a
    ///         que después se borre el equipo.</item>
    /// </list>
    ///
    /// <para>Lo del rol cambió: el escritorio lo ponía siempre en «sin rol» y aquí se copió tal cual
    /// al principio. Con el organigrama arrastrable la gente cambia de equipo muchísimo más a menudo,
    /// y un rol que hay que volver a poner cada vez acaba sin ponerse. El porqué de cada caso está
    /// escrito donde ocurre.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> MoverIntegrantesAsync(
        MoverIntegrantesRequest peticion, CancellationToken ct = default)
    {
        SoloAdmin();

        if (peticion.DeveloperIds.Count == 0) return (false, "Selecciona al menos a una persona.");

        Team? destino = null;
        if (peticion.EquipoId is int equipoId)
        {
            destino = await db.Teams.FirstOrDefaultAsync(t => t.Id == equipoId, ct);
            if (destino == null) return (false, "Ese equipo ya no existe. Actualiza la pantalla.");
        }

        var nota = Limpiar(peticion.Nota);
        var equipos = await db.Teams.ToDictionaryAsync(t => t.Id, ct);
        int movidos = 0;

        foreach (var devId in peticion.DeveloperIds.Distinct())
        {
            var dev = await db.Developers.FirstOrDefaultAsync(d => d.Id == devId, ct);
            if (dev == null || dev.TeamId == peticion.EquipoId) continue;

            int? origenId = dev.TeamId;
            var origen = origenId is int id && equipos.TryGetValue(id, out var t) ? t : null;
            if (origen != null && origen.LeadDeveloperId == dev.Id) origen.LeadDeveloperId = null;

            dev.TeamId = peticion.EquipoId;

            // EL ROL VIAJA CON LA PERSONA, y antes no. Quien es backend lo sigue siendo al cambiar
            // de equipo: es una cualidad suya, no del sitio donde está. Antes se ponía «sin rol» y
            // había que reasignarlo a mano; con el arrastre en el organigrama la gente se mueve
            // muchísimo más a menudo que cuando hacían falta dos listas y un botón, así que ese
            // trámite pasaba de ocasional a constante — y un rol que hay que volver a poner cada vez
            // acaba sin ponerse, que es como el organigrama se llena de «Sin rol».
            //
            // LÍDER ES LA EXCEPCIÓN, y no es un matiz: «Líder» es uno de los valores de TeamRole,
            // así que dejarlo viajar metería a la persona en su equipo nuevo como líder. Ahí ya hay
            // uno, o no lo hay porque nadie lo ha decidido todavía; en los dos casos el cargo lo da
            // quien recibe, no el gesto de arrastrar. Se cae a «sin rol» igual que antes.
            if (dev.TeamRole == TeamRole.Lider) dev.TeamRole = TeamRole.SinRol;

            // La FUNCIÓN sí se sigue borrando, y no es incoherente con lo de arriba: «mantiene la
            // pasarela de pagos» describe una responsabilidad DENTRO del equipo que se deja, no algo
            // que la persona sepa hacer. Llevársela publicaría en el organigrama nuevo un encargo
            // que nadie le ha dado y que su nuevo líder no sabría que está ahí.
            dev.TeamFunction = null;

            db.TeamRotations.Add(new TeamRotation
            {
                DeveloperId     = dev.Id,
                DeveloperName   = dev.FullName,
                FromTeamId      = origenId,
                FromTeamName    = origen?.Name ?? "Sin equipo",
                ToTeamId        = peticion.EquipoId,
                ToTeamName      = destino?.Name ?? "Sin equipo",
                Note            = nota,
                RotatedByUserId = currentUser.UserId,
                RotatedAt       = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(AuditAction.Update, "Developer", dev.Id.ToString(),
                $"{dev.FullName}: {origen?.Name ?? "Sin equipo"} → {destino?.Name ?? "Sin equipo"}", ct);
            movidos++;
        }

        if (movidos == 0) return (false, "Nadie cambió de equipo: ya estaban donde los mandabas.");
        return (true, $"{movidos} persona(s) movida(s) a {destino?.Name ?? "«sin equipo»"}.");
    }

    /// <summary>
    /// Asigna el rol de alguien dentro de su equipo.
    ///
    /// «Líder» es EXCLUYENTE: al nombrar uno, el anterior se queda sin rol y el equipo apunta al
    /// nuevo. Los dos datos —el rol de la ficha y el <c>LeadDeveloperId</c> del equipo— existen desde
    /// el escritorio y pueden discrepar; escribirlos a la vez es lo que evita que discrepen más.
    /// </summary>
    public async Task<(bool ok, string mensaje)> AsignarRolAsync(
        AsignarRolRequest peticion, CancellationToken ct = default)
    {
        SoloAdmin();

        var dev = await db.Developers.FirstOrDefaultAsync(d => d.Id == peticion.DeveloperId, ct);
        if (dev == null) return (false, "Esa persona ya no está en el catálogo. Actualiza la pantalla.");
        if (dev.TeamId is not int equipoId)
            return (false, $"{dev.FullName} no está en ningún equipo; primero muévela a uno.");

        var equipo = await db.Teams.FirstOrDefaultAsync(t => t.Id == equipoId, ct);

        if (peticion.Rol == TeamRole.Lider)
        {
            var otros = await db.Developers
                .Where(d => d.TeamId == equipoId && d.TeamRole == TeamRole.Lider && d.Id != dev.Id)
                .ToListAsync(ct);
            foreach (var m in otros) m.TeamRole = TeamRole.SinRol;

            dev.TeamRole = TeamRole.Lider;
            if (equipo != null) equipo.LeadDeveloperId = dev.Id;
        }
        else
        {
            dev.TeamRole = peticion.Rol;
            if (equipo != null && equipo.LeadDeveloperId == dev.Id) equipo.LeadDeveloperId = null;
        }

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(AuditAction.Update, "Developer", dev.Id.ToString(),
            $"{dev.FullName}: rol {EtiquetasDeCatalogo.RolDeEquipo(peticion.Rol)}", ct);

        return (true, $"{dev.FullName}: {EtiquetasDeCatalogo.RolDeEquipo(peticion.Rol)}.");
    }

    /// <summary>
    /// Anota qué hace una persona dentro de su equipo. Mandarla vacía la borra.
    ///
    /// <para><b>Solo se le pone función a quien tiene equipo</b>, y no por purismo: el texto describe
    /// una responsabilidad DENTRO de un equipo, se borra al cambiarse de equipo y se borra si el
    /// equipo desaparece. Permitir escribirla a quien no está en ninguno crearía una frase que el
    /// organigrama no puede dibujar en ninguna caja y que la primera rotación borraría sin avisar.</para>
    ///
    /// <para>A la bitácora va el texto entero. No es un dato sensible —es lo que se publica en el
    /// organigrama que se reparte en PDF— y sin él la anotación solo diría «alguien cambió algo».</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> GuardarFuncionAsync(
        GuardarFuncionRequest peticion, CancellationToken ct = default)
    {
        SoloAdmin();

        var funcion = Limpiar(peticion.Funcion);
        if (funcion is { Length: > LargoMaximoDeFuncion })
            return (false, $"La función no puede pasar de {LargoMaximoDeFuncion} caracteres.");

        var dev = await db.Developers.FirstOrDefaultAsync(d => d.Id == peticion.DeveloperId, ct);
        if (dev == null) return (false, "Esa persona ya no está en el catálogo. Actualiza la pantalla.");
        if (dev.TeamId is null)
            return (false, $"{dev.FullName} no está en ningún equipo; la función describe lo que hace " +
                           "dentro de uno. Muévela primero a un equipo.");

        dev.TeamFunction = funcion;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "Developer", dev.Id.ToString(),
            funcion is null
                ? $"{dev.FullName}: se borró su función en el equipo"
                : $"{dev.FullName}: función «{funcion}»", ct);

        return (true, funcion is null
            ? $"{dev.FullName} se queda sin función anotada."
            : $"Función de {dev.FullName} guardada.");
    }

    /// <summary>El historial reciente de rotaciones, lo más nuevo primero.</summary>
    public async Task<IReadOnlyList<RotacionDto>> RotacionesAsync(CancellationToken ct = default)
    {
        SoloAdmin();

        return await db.TeamRotations.AsNoTracking()
            .OrderByDescending(r => r.RotatedAt)
            .Take(RotacionesQueSeEnsenan)
            .Select(r => new RotacionDto(
                r.Id, r.DeveloperName, r.FromTeamName, r.ToTeamName, r.Note, r.RotatedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// El organigrama entero: los equipos con su descripción, su gente ordenada y quien no está en
    /// ninguno.
    ///
    /// <para><b>Es la única consulta que resuelve quién manda en cada equipo</b>, y de ahí salen las
    /// tres cosas que lo enseñan: la pestaña de siempre, el diagrama y el PDF. La regla —manda el rol
    /// marcado en la ficha y solo si nadie lo tiene se recurre al <c>LeadDeveloperId</c> del equipo—
    /// se apoya en dos datos que pueden discrepar, así que cada sitio que la resolviera por su cuenta
    /// sería un sitio donde puede salir otro nombre. Con dos pestañas de la misma pantalla, la
    /// contradicción se vería de un vistazo; con el papel, se repartiría.</para>
    ///
    /// <para><b>Nadie se queda fuera.</b> Se recorren los roles en el orden del escritorio y al final
    /// se añade a quien no haya entrado por ningún rol —un segundo «líder» heredado de datos viejos,
    /// por ejemplo—: en una lista faltar es una fila menos, pero en un organigrama es una persona que
    /// oficialmente no está en ninguna parte.</para>
    ///
    /// <para><b>Los equipos salen en orden de dibujo</b>: cada padre delante de su rama y los hermanos
    /// por nombre, con su nivel ya calculado. El orden y la altura son propiedades del ÁRBOL, no del
    /// equipo, y se resuelven una sola vez aquí para que la pantalla y el PDF no los deduzcan cada uno
    /// a su manera — que es como el papel acaba contradiciendo a la pantalla que lo imprimió.</para>
    /// </summary>
    public async Task<OrganigramaDto> OrganigramaAsync(CancellationToken ct = default)
    {
        SoloAdmin();

        var equipos = await db.Teams.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        var devs = await db.Developers.AsNoTracking()
            .Where(d => d.IsActive).OrderBy(d => d.FullName).ToListAsync(ct);

        var sistemas = await db.AppSystems.AsNoTracking()
            .Where(s => s.TeamId != null).OrderBy(s => s.Name)
            .Select(s => new { s.TeamId, s.Name }).ToListAsync(ct);
        var proyectos = await db.Projects.AsNoTracking()
            .Where(p => p.TeamId != null).OrderBy(p => p.Name)
            .Select(p => new { p.TeamId, p.Name, p.Client }).ToListAsync(ct);

        // El árbol se arma con los equipos ya ordenados por nombre, y ese orden se conserva entre
        // hermanos: dentro de cada rama, las cajas siguen saliendo alfabéticas como siempre.
        var jerarquia = JerarquiaDeEquipos.De(equipos);
        var porId = equipos.ToDictionary(t => t.Id);

        var dibujados = jerarquia.EnOrdenDeDibujo().Select(id => porId[id]).Select(t =>
        {
            var miembros = devs.Where(d => d.TeamId == t.Id).ToList();
            var lider = miembros.FirstOrDefault(m => m.TeamRole == TeamRole.Lider)
                        ?? miembros.FirstOrDefault(m => m.Id == t.LeadDeveloperId);

            var integrantes = new List<PersonaDelOrganigramaDto>(miembros.Count);
            if (lider != null) integrantes.Add(Persona(lider, TeamRole.Lider, esLider: true));

            var resto = miembros.Where(m => lider == null || m.Id != lider.Id).ToList();
            foreach (var rol in EtiquetasDeCatalogo.OrdenDeRoles)
                integrantes.AddRange(resto.Where(m => m.TeamRole == rol)
                                          .Select(m => Persona(m, rol, esLider: false)));

            // El colador: OrdenDeRoles no incluye «Líder», así que un segundo líder marcado en la
            // ficha —que AsignarRolAsync ya no permite, pero que pudo quedar de antes— no entraría
            // por ninguna vuelta del bucle y desaparecería del diagrama sin dejar rastro.
            integrantes.AddRange(resto.Where(m => !EtiquetasDeCatalogo.OrdenDeRoles.Contains(m.TeamRole))
                                      .Select(m => Persona(m, m.TeamRole, esLider: false)));

            return new EquipoDelOrganigramaDto(
                t.Id, t.Name, Limpiar(t.Description), Limpiar(t.ColorHex), lider?.FullName,
                // El padre lo dice el árbol y no la fila: si la columna apuntaba a un equipo que ya
                // no existe, la jerarquía lo trata como raíz y el DTO tiene que decir lo mismo, o
                // quien dibuje buscaría una caja que no está en la lista.
                jerarquia.PadreDe(t.Id),
                jerarquia.Nivel(t.Id),
                integrantes,
                sistemas.Where(s => s.TeamId == t.Id).Select(s => s.Name).ToList(),
                proyectos.Where(p => p.TeamId == t.Id)
                    .Select(p => string.IsNullOrWhiteSpace(p.Client) ? p.Name : $"{p.Name} — {p.Client}")
                    .ToList());
        }).ToList();

        var sinEquipo = devs.Where(d => d.TeamId == null)
            .Select(d => Persona(d, d.TeamRole, esLider: false))
            .ToList();

        return new OrganigramaDto(
            dibujados, sinEquipo,
            dibujados.Sum(e => e.Integrantes.Count) + sinEquipo.Count,
            DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
    }

    /// <summary>
    /// Lo que va impreso en el PDF del organigrama.
    ///
    /// <para>Sale de <see cref="OrganigramaAsync"/> y no de una consulta propia: es LA misma
    /// información dibujada en otro soporte, y tenerla dos veces era garantizar que un día el papel
    /// y la pantalla dijeran cosas distintas del mismo equipo. Aquí solo se traduce al contrato de
    /// documentos, que no conoce identificadores ni enumeraciones — el generador maqueta, no decide.</para>
    /// </summary>
    public async Task<DatosDeEquipos> DatosDeEquiposAsync(CancellationToken ct = default)
    {
        var organigrama = await OrganigramaAsync(ct);   // guarda de admin dentro

        // El papel no conoce identificadores, así que el padre viaja por su NOMBRE. Se traduce con la
        // misma lista que se va a imprimir —no con otra consulta— para que no pueda salir el nombre
        // de un equipo que en la hoja de al lado no está.
        var nombrePorId = organigrama.Equipos.ToDictionary(e => e.Id, e => e.Nombre);

        return new DatosDeEquipos(
            [.. organigrama.Equipos.Select(e => new EquipoImpreso(
                e.Nombre, e.Descripcion, e.Lider, e.ColorHex,
                e.EquipoPadreId is int padre && nombrePorId.TryGetValue(padre, out var suPadre) ? suPadre : null,
                [.. e.Integrantes.Select(Impreso)],
                e.Sistemas, e.Proyectos))],
            [.. organigrama.SinEquipo.Select(Impreso)],
            organigrama.TotalPersonas,
            organigrama.GeneradoEl);
    }

    private static PersonaDelOrganigramaDto Persona(Developer d, TeamRole rol, bool esLider) =>
        new(d.Id, d.FullName, Limpiar(d.Seniority), rol,
            EtiquetasDeCatalogo.RolDeEquipo(rol), EtiquetasDeCatalogo.ColorDeRol(rol),
            Limpiar(d.TeamFunction), esLider);

    private static IntegranteImpreso Impreso(PersonaDelOrganigramaDto p) =>
        new(p.Nombre, p.Nivel, p.RolTexto, p.Funcion, p.EsLider);

    // ── Usuarios ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// La pantalla de cuentas de una vez: las cuentas, las fichas a las que ligarlas y las reglas de
    /// bloqueo que la pantalla explica.
    ///
    /// Ni el hash ni el sello de sesión salen de aquí: ver <see cref="UsuarioDto"/>.
    ///
    /// <para><b>Del segundo factor sale el ESTADO y nada más</b>: si está activo, desde cuándo y
    /// cuántos códigos de rescate quedan. Ni el secreto —que vive cifrado en <c>UserSecrets</c> y no
    /// se lee aquí— ni los códigos de rescate —que en la base solo están como hash— tienen por qué
    /// pasar por una lista que se pide para pintar una rejilla. Lo que el líder necesita para decidir
    /// si reinicia es exactamente esto.</para>
    /// </summary>
    public async Task<PantallaDeUsuariosDto> UsuariosAsync(CancellationToken ct = default)
    {
        SoloAdmin();

        var cuentas = await db.Users.AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => new
            {
                u.Id, u.Username, u.FullName, u.Role, u.DeveloperId, u.IsActive,
                u.MustChangePassword, u.FailedLoginCount, u.LockoutUntil, u.CreatedAt,
                u.SegundoFactorActivo, u.SegundoFactorDesdeUtc,
                Desarrollador = u.Developer != null ? u.Developer.FullName : null
            })
            .ToListAsync(ct);

        // Los códigos de rescate se cuentan de UNA vez para todas las cuentas y no cuenta por cuenta:
        // once cuentas serían once viajes a la base para pintar una columna. Solo se agrupan los que
        // siguen sin gastar; los usados están ahí como constancia, no como saldo.
        var rescatesDisponibles = await db.UserRecoveryCodes.AsNoTracking()
            .Where(c => c.UsadoEnUtc == null)
            .GroupBy(c => c.UserId)
            .Select(g => new { UserId = g.Key, Cuantos = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Cuantos, ct);

        var ahora = DateTime.UtcNow;
        var usuarios = cuentas.Select(u => new UsuarioDto(
            u.Id, u.Username, u.FullName,
            u.Role, EtiquetaDeRol(u.Role),
            u.DeveloperId, u.Desarrollador,
            u.IsActive,
            // El bloqueo se resuelve contra el reloj del SERVIDOR: con el del navegador, un equipo
            // con la hora adelantada enseñaría como libres cuentas que siguen bloqueadas.
            u.LockoutUntil is DateTime hasta && hasta > ahora,
            u.LockoutUntil,
            u.FailedLoginCount,
            u.MustChangePassword,
            u.CreatedAt,
            u.SegundoFactorActivo,
            u.SegundoFactorDesdeUtc,
            rescatesDisponibles.GetValueOrDefault(u.Id))).ToList();

        var desarrolladores = await db.Developers.AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.FullName)
            .Select(d => new OpcionDto(d.Id, d.FullName))
            .ToListAsync(ct);

        return new PantallaDeUsuariosDto(
            usuarios, desarrolladores,
            AuthService.MaxFailedAttempts, AuthService.LockoutMinutes, AuthService.MinPasswordLength,
            CodigosDeRescate.Cuantos, SegundoFactorService.CodigosDeRescateParaAvisar);
    }

    /// <summary>
    /// Da de alta una cuenta y devuelve su contraseña TEMPORAL, que se enseña una sola vez.
    ///
    /// La contraseña no se recibe ni se inventa aquí: la fila se crea con un hash imposible de
    /// adivinar y acto seguido se pide a <see cref="AuthService.ResetPasswordAsync"/> que ponga la
    /// temporal. Así el alfabeto sin caracteres confundibles, el sello de sesión nuevo y la
    /// obligación de cambiarla al entrar viven en UN solo sitio; copiarlos aquí sería tener dos
    /// reglas que un día dejarían de coincidir.
    /// </summary>
    public async Task<(bool ok, string mensaje, string? temporal)> CrearUsuarioAsync(
        CrearUsuarioRequest peticion, CancellationToken ct = default)
    {
        SoloAdmin();

        var (valido, error, usuario, nombre) = await ValidarCuentaAsync(
            peticion.Usuario, peticion.Nombre, peticion.DeveloperId, idExcluido: null, ct);
        if (!valido) return (false, error, null);

        var cuenta = new User
        {
            Username    = usuario,
            FullName    = nombre,
            Role        = peticion.Rol,
            DeveloperId = peticion.DeveloperId,
            IsActive    = true,
            // Un hash de un valor aleatorio que nadie conoce: la cuenta no puede usarse hasta que el
            // restablecimiento de la línea siguiente ponga la temporal. Dejar el hash vacío la habría
            // dejado, por un instante, sin contraseña que verificar.
            PasswordHash       = PasswordHasher.Hash(Guid.NewGuid().ToString("N")),
            MustChangePassword = true,
            CreatedAt          = DateTime.UtcNow
        };
        db.Users.Add(cuenta);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Create, "User", cuenta.Id.ToString(),
            $"Cuenta «{cuenta.Username}» creada con rol {EtiquetaDeRol(cuenta.Role)}", ct);

        var (ok, _, temporal) = await auth.ResetPasswordAsync(cuenta.Id, ct);
        return ok
            ? (true, $"Cuenta «{cuenta.Username}» creada. Su contraseña temporal se muestra una sola vez.", temporal)
            // La cuenta existe pero se quedó sin contraseña utilizable: se dice tal cual, porque el
            // arreglo es pulsar «Restablecer contraseña» y no volver a crearla.
            : (true, $"Cuenta «{cuenta.Username}» creada, pero no se pudo generar su contraseña temporal. " +
                     "Usa «Restablecer contraseña».", null);
    }

    /// <summary>
    /// Edita una cuenta: nombre de usuario, nombre, rol, ficha ligada y si está activa.
    ///
    /// <b>Al cambiar el rol, la ficha ligada o al desactivar se renueva el sello de sesión.</b> No lo
    /// hacía el escritorio porque allí la sesión moría con el proceso; aquí vive en una cookie de
    /// ocho horas que lleva el rol dentro. Sin renovar el sello, a quien se le acaba de quitar el rol
    /// de líder seguiría entrando a las pantallas de líder toda la tarde.
    /// </summary>
    public async Task<(bool ok, string mensaje)> ActualizarUsuarioAsync(
        ActualizarUsuarioRequest peticion, CancellationToken ct = default)
    {
        SoloAdmin();

        var cuenta = await db.Users.FirstOrDefaultAsync(u => u.Id == peticion.Id, ct);
        if (cuenta == null) return (false, "Esa cuenta ya no existe. Actualiza la lista.");

        var (valido, error, usuario, nombre) = await ValidarCuentaAsync(
            peticion.Usuario, peticion.Nombre, peticion.DeveloperId, cuenta.Id, ct);
        if (!valido) return (false, error);

        bool pierdeAdmin = cuenta.Role == UserRole.Admin
            && (peticion.Rol != UserRole.Admin || !peticion.Activo);
        if (pierdeAdmin && await EsElUltimoAdminAsync(cuenta.Id, ct))
            return (false, "Es la única cuenta de líder activa. Nombra a otro líder antes de cambiarle el rol " +
                           "o desactivarla, o nadie podrá administrar la aplicación.");

        // Renovar el sello echa de las sesiones abiertas, así que solo se hace cuando cambia algo que
        // la cookie lleva dentro: rol, ficha ligada o el propio acceso.
        bool cambiaLoQueLlevaLaCookie =
            cuenta.Role != peticion.Rol ||
            cuenta.DeveloperId != peticion.DeveloperId ||
            (cuenta.IsActive && !peticion.Activo);

        cuenta.Username    = usuario;
        cuenta.FullName    = nombre;
        cuenta.Role        = peticion.Rol;
        cuenta.DeveloperId = peticion.DeveloperId;
        cuenta.IsActive    = peticion.Activo;
        if (cambiaLoQueLlevaLaCookie) cuenta.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "User", cuenta.Id.ToString(),
            $"Cuenta «{cuenta.Username}» actualizada: rol {EtiquetaDeRol(cuenta.Role)}, " +
            (cuenta.IsActive ? "activa" : "desactivada"), ct);

        return (true, cambiaLoQueLlevaLaCookie
            ? $"Cuenta «{cuenta.Username}» actualizada. Su sesión abierta, si la tenía, se cerró."
            : $"Cuenta «{cuenta.Username}» actualizada.");
    }

    /// <summary>
    /// Activa o desactiva una cuenta. Es el «Activar/Desact.» del escritorio, con una diferencia: al
    /// desactivar se renueva el sello, que es lo único que echa de verdad a quien ya está dentro.
    /// </summary>
    public async Task<(bool ok, string mensaje)> AlternarActivoAsync(int userId, CancellationToken ct = default)
    {
        SoloAdmin();

        var cuenta = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (cuenta == null) return (false, "Esa cuenta ya no existe. Actualiza la lista.");

        if (cuenta.IsActive && cuenta.Id == currentUser.UserId)
            return (false, "No puedes desactivar tu propia cuenta: te dejaría fuera de la aplicación.");
        if (cuenta.IsActive && cuenta.Role == UserRole.Admin && await EsElUltimoAdminAsync(cuenta.Id, ct))
            return (false, "Es la única cuenta de líder activa. Nombra a otro líder antes de desactivarla.");

        cuenta.IsActive = !cuenta.IsActive;
        if (!cuenta.IsActive) cuenta.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "User", cuenta.Id.ToString(),
            cuenta.IsActive ? $"Cuenta «{cuenta.Username}» reactivada" : $"Cuenta «{cuenta.Username}» desactivada", ct);

        return (true, cuenta.IsActive
            ? $"Cuenta «{cuenta.Username}» activada."
            : $"Cuenta «{cuenta.Username}» desactivada. Si tenía la sesión abierta, se cerró.");
    }

    /// <summary>
    /// Elimina una cuenta.
    ///
    /// Se conservan las dos negativas del escritorio, que no son burocracia: una cuenta de LÍDER no
    /// se borra (hay que bajarle el rol antes, lo que obliga a pensarlo dos veces) y la propia
    /// tampoco. Lo que el histórico pierde es la atribución —quién asignó unos puntos, quién revisó—;
    /// la bitácora conserva el nombre porque nunca tuvo FK a esta tabla.
    /// </summary>
    public async Task<(bool ok, string mensaje)> EliminarUsuarioAsync(int userId, CancellationToken ct = default)
    {
        SoloAdmin();

        var cuenta = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (cuenta == null) return (false, "Esa cuenta ya no existe. Actualiza la lista.");

        if (cuenta.Role == UserRole.Admin)
            return (false, $"«{cuenta.Username}» es líder y no se puede eliminar. " +
                           "Si de verdad quieres darlo de baja, cámbiale antes el rol o desactívalo.");
        if (cuenta.Id == currentUser.UserId)
            return (false, "No puedes eliminar tu propia cuenta.");

        var nombre = cuenta.Username;
        db.Users.Remove(cuenta);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Delete, "User", userId.ToString(),
            $"Cuenta «{nombre}» eliminada", ct);
        return (true, $"Cuenta «{nombre}» eliminada.");
    }

    /// <summary>
    /// Reglas comunes al alta y a la edición de una cuenta. Devuelve ya limpios el usuario y el
    /// nombre, para que quien llame no vuelva a recortarlos y se le olvide en uno de los dos sitios.
    /// </summary>
    private async Task<(bool ok, string error, string usuario, string nombre)> ValidarCuentaAsync(
        string? usuarioCrudo, string? nombreCrudo, int? developerId, int? idExcluido, CancellationToken ct)
    {
        var usuario = (usuarioCrudo ?? "").Trim();
        var nombre  = (nombreCrudo ?? "").Trim();

        if (usuario.Length == 0) return (false, "Escribe el nombre de usuario.", "", "");
        if (usuario.Length > 100) return (false, "El nombre de usuario no puede pasar de 100 caracteres.", "", "");
        if (nombre.Length == 0) return (false, "Escribe el nombre de la persona.", "", "");
        if (nombre.Length > 200) return (false, "El nombre no puede pasar de 200 caracteres.", "", "");

        bool repetido = await db.Users
            .AnyAsync(u => u.Id != idExcluido && u.Username == usuario, ct);
        if (repetido) return (false, $"Ya existe una cuenta con el usuario «{usuario}».", "", "");

        if (developerId is int devId)
        {
            if (!await db.Developers.AnyAsync(d => d.Id == devId, ct))
                return (false, "Esa ficha de desarrollador ya no existe.", "", "");

            // Regla NUEVA, que el escritorio no tenía. Con dos cuentas apuntando a la misma ficha, el
            // envío de comunicados y los avisos de asignación eligen una de las dos de forma
            // arbitraria y la otra persona no se entera nunca de nada — un fallo silencioso, que es
            // la peor clase. Permitirlo no aporta ningún caso de uso real.
            bool yaLigada = await db.Users
                .AnyAsync(u => u.Id != idExcluido && u.DeveloperId == devId, ct);
            if (yaLigada)
                return (false, "Esa ficha de desarrollador ya está ligada a otra cuenta.", "", "");
        }

        return (true, "", usuario, nombre);
    }

    /// <summary>¿Es la última cuenta de líder ACTIVA que queda?</summary>
    private async Task<bool> EsElUltimoAdminAsync(int userId, CancellationToken ct) =>
        !await db.Users.AnyAsync(u => u.Id != userId && u.Role == UserRole.Admin && u.IsActive, ct);

    /// <summary>Los mismos textos de rol que pintaba la rejilla del escritorio.</summary>
    private static string EtiquetaDeRol(UserRole rol) => rol switch
    {
        UserRole.Admin         => "Admin",
        UserRole.Operaciones   => "Operaciones",
        UserRole.Desarrollador => "Desarrollador",
        _                      => rol.ToString()
    };

    private static string? Limpiar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
