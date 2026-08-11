using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Seguridad;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Demo;

/// <summary>
/// Los datos base que comparten todos los sembradores: las personas, sus cuentas y los dos equipos.
/// Llegan YA GUARDADOS, así que sus <c>Id</c> están asignados.
/// </summary>
public sealed record DatosBase(
    Developer Ana, Developer Beto, Developer Caro, Developer Dani,
    User Lider, User UsuarioAna, User UsuarioBeto, User UsuarioCaro, User UsuarioDani, User Ops,
    Team Plataforma, Team Producto);

/// <summary>
/// Llena una base VACÍA con datos de demostración, para poder abrir la aplicación y encontrarla con
/// contenido en vez de con todas las pantallas en blanco.
///
/// <para><b>Esto no es una función del producto: es una herramienta para probar.</b> No hay ninguna
/// pantalla que lo dispare, no hay ningún endpoint que lo llame y no se puede encender desde dentro
/// de la aplicación. Solo corre al arrancar, y solo si se cumplen las TRES condiciones de
/// <see cref="SePuede"/>.</para>
///
/// <para><b>Las tres guardas, y por qué son tres.</b> Una sola bastaría si nadie se equivocara nunca.
/// La base de producción es la de verdad —la comparten dos aplicaciones y contiene el trabajo de años
/// del equipo—, y meterle cuatro desarrolladores inventados y catorce requerimientos de mentira no se
/// deshace con un botón. Así que:</para>
/// <list type="number">
///   <item><b>Hay que pedirlo</b> (<c>AdminWeb:DatosDeDemostracion = true</c>). Nunca por omisión.</item>
///   <item><b>No en Production.</b> Aunque alguien copie la configuración por error.</item>
///   <item><b>Solo si la base está VIRGEN.</b> Es la que de verdad protege: producción tiene
///   desarrolladores desde el primer día, así que aunque las otras dos fallaran, aquí se para.</item>
/// </list>
///
/// <para>Los datos se calculan siempre relativos a HOY, nunca con fechas fijas: así siguen pareciendo
/// recientes dentro de seis meses, en vez de convertirse en un archivo histórico raro.</para>
/// </summary>
public static class DatosDeDemostracion
{
    /// <summary>La clave de configuración que hay que encender a propósito.</summary>
    public const string Clave = "AdminWeb:DatosDeDemostracion";

    /// <summary>La contraseña de TODAS las cuentas de demostración. No es un secreto: es el punto.</summary>
    public const string Contrasena = "Demo.2026";

    /// <summary>
    /// Si se puede sembrar. Las tres condiciones van juntas y en este orden porque la última es la
    /// que cuesta una consulta: no vale la pena preguntarle a la base si ya se sabe que no.
    /// </summary>
    public static async Task<bool> SePuedeAsync(
        AppDbContext db, bool pedido, bool esProduccion, CancellationToken ct = default)
    {
        if (!pedido || esProduccion) return false;

        // Una base con desarrolladores NO está virgen. Se mira esta tabla y no Users porque el
        // arranque siembra la cuenta «admin» antes de llegar aquí: mirando usuarios, nunca sembraría.
        return !await db.Developers.AnyAsync(ct);
    }

    /// <summary>
    /// Siembra todo. Devuelve un resumen de lo que creó, para escribirlo en el registro.
    ///
    /// <para>El ORDEN importa: primero las personas —que todo lo demás referencia— y después cada
    /// dominio. Los de trabajo van antes que los de perfiles porque allí hay filtros guardados y
    /// tickets vigilados que apuntan a tickets de DevOps.</para>
    /// </summary>
    public static async Task<string> SembrarAsync(AppDbContext db, CancellationToken ct = default)
    {
        var basicos = await SembrarPersonasAsync(db, ct);

        SembrarTrabajo(db, basicos);
        SembrarPoolYDesempeno(db, basicos);
        SembrarAusenciasYJornada(db, basicos);
        SembrarComunicacion(db, basicos);
        SembrarDespliegues(db, basicos);
        SembrarPerfilesYPlantillas(db, basicos);
        SembrarBitacora(db, basicos);

        return $"{await db.Developers.CountAsync(ct)} desarrolladores, " +
               $"{await db.Requirements.CountAsync(ct)} requerimientos, " +
               $"{await db.PoolActivities.CountAsync(ct)} actividades del pool, " +
               $"{await db.ForumPosts.CountAsync(ct)} publicaciones del foro, " +
               $"{await db.DeploymentJobs.CountAsync(ct)} despliegues";
    }

    // ── Las personas ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Los cuatro desarrolladores, sus cuentas, el líder y operaciones.
    ///
    /// <para><b>Todas las contraseñas son la misma y NO obligan a cambiarla.</b> En producción eso
    /// sería inaceptable; aquí es justo lo que se quiere, porque el punto es poder entrar como
    /// cualquiera de ellos en dos segundos para ver la aplicación con sus ojos. El líder ve cosas que
    /// el desarrollador no, y esa diferencia solo se comprueba cambiando de cuenta.</para>
    /// </summary>
    private static async Task<DatosBase> SembrarPersonasAsync(AppDbContext db, CancellationToken ct)
    {
        var hoy = DateTime.UtcNow;

        var plataforma = new Team
        {
            Name = "Plataforma", ColorHex = "#2563eb",
            Description = "Mantiene los sistemas internos y la infraestructura de despliegue."
        };
        var producto = new Team
        {
            Name = "Producto", ColorHex = "#7c3aed",
            Description = "Atiende lo que piden las áreas de negocio."
        };
        db.Teams.AddRange(plataforma, producto);
        await db.SaveChangesAsync(ct);

        var ana = new Developer
        {
            FullName = "Ana Contreras", Email = "ana.contreras@soltum.mx", Phone = "55 1234 5678",
            Seniority = "Senior", HireDate = hoy.AddYears(-6).AddMonths(-2), VacationDaysLeft = 22,
            EquipmentSerial = "SLT-0142", TeamId = plataforma.Id, TeamRole = TeamRole.Lider,
            Notes = "Se le dan bien los despliegues y la integración con DevOps."
        };
        var beto = new Developer
        {
            FullName = "Beto Nájera", Email = "beto.najera@soltum.mx", Phone = "55 2345 6789",
            Seniority = "Mid", HireDate = hoy.AddYears(-2).AddMonths(-7), VacationDaysLeft = 14,
            EquipmentSerial = "SLT-0187", TeamId = plataforma.Id, TeamRole = TeamRole.Backend
        };
        var caro = new Developer
        {
            FullName = "Carolina Ibarra", Email = "carolina.ibarra@soltum.mx", Phone = "55 3456 7890",
            Seniority = "Senior", HireDate = hoy.AddYears(-4).AddMonths(-1), VacationDaysLeft = 18,
            EquipmentSerial = "SLT-0155", TeamId = producto.Id, TeamRole = TeamRole.Lider
        };
        var dani = new Developer
        {
            FullName = "Daniel Rueda", Email = "daniel.rueda@soltum.mx", Phone = "55 4567 8901",
            Seniority = "Junior", HireDate = hoy.AddMonths(-8), VacationDaysLeft = 8,
            EquipmentSerial = "SLT-0203", TeamId = producto.Id, TeamRole = TeamRole.Frontend,
            Notes = "Entró hace menos de un año: sus vacaciones van por la parte proporcional."
        };
        db.Developers.AddRange(ana, beto, caro, dani);
        await db.SaveChangesAsync(ct);

        plataforma.LeadDeveloperId = ana.Id;
        producto.LeadDeveloperId = caro.Id;

        var hash = PasswordHasher.Hash(Contrasena);

        var lider = new User
        {
            Username = "lider", FullName = "Gerardo Manjarrez", Role = UserRole.Admin,
            PasswordHash = hash, IsActive = true, MustChangePassword = false
        };
        var ops = new User
        {
            Username = "ops", FullName = "Mesa de Operaciones", Role = UserRole.Operaciones,
            PasswordHash = hash, IsActive = true, MustChangePassword = false
        };
        var uAna = Cuenta("ana", ana, hash);
        var uBeto = Cuenta("beto", beto, hash);
        var uCaro = Cuenta("caro", caro, hash);
        var uDani = Cuenta("dani", dani, hash);

        db.Users.AddRange(lider, ops, uAna, uBeto, uCaro, uDani);
        await db.SaveChangesAsync(ct);

        return new DatosBase(ana, beto, caro, dani, lider, uAna, uBeto, uCaro, uDani, ops,
                             plataforma, producto);
    }

    private static User Cuenta(string usuario, Developer dev, string hash) => new()
    {
        Username = usuario,
        FullName = dev.FullName,
        Role = UserRole.Desarrollador,
        DeveloperId = dev.Id,
        PasswordHash = hash,
        IsActive = true,
        MustChangePassword = false
    };

    // ═══ TRABAJO ═══

/// <summary>
/// Siembra el trabajo del equipo: dos sprints (uno corriendo y otro ya cerrado), la cartera de
/// requerimientos repartida por TODOS los estados, quién trae cada uno y los tickets de Azure
/// DevOps de la última sincronización. Es lo que hace que Requerimientos, Sprint y DevOps tengan
/// algo que enseñar al abrir la aplicación en local.
/// </summary>
public static void SembrarTrabajo(AppDbContext db, DatosBase b)
{
    var hoy = DateTime.Today;        // los compromisos son DÍAS locales, sin hora
    var ahora = DateTime.UtcNow;     // los sellos internos (CreatedAt, SyncedAt…) van en UTC

    // ── Sprints ──────────────────────────────────────────────────────────────
    // El de ahora arrancó hace seis días y cierra en siete: con la mitad del calendario consumida,
    // la comparación «avance real contra tiempo transcurrido» de la pantalla de seguimiento tiene
    // de dónde salir. El nombre se calcula con la semana ISO para que nunca se vea viejo.
    var sprintEnCurso = new Sprint
    {
        Name = $"Sprint {hoy:yyyy}-{System.Globalization.ISOWeek.GetWeekOfYear(hoy):00}",
        Goal = "Dejar el timbrado 4.0 del portal de facturación en producción y el catálogo de "
             + "proveedores en pruebas con el área de compras.",
        StartDate = hoy.AddDays(-6),
        EndDate = hoy.AddDays(7),
        CreatedAt = ahora.AddDays(-8)
    };

    // El anterior ya cerró (su última fecha pasó): es el que da la velocidad histórica del equipo.
    var sprintCerrado = new Sprint
    {
        Name = $"Sprint {hoy.AddDays(-20):yyyy}-{System.Globalization.ISOWeek.GetWeekOfYear(hoy.AddDays(-20)):00}",
        Goal = "Migrar la intranet a .NET 8 y sacar los reportes que todavía dependían de Crystal.",
        StartDate = hoy.AddDays(-20),
        EndDate = hoy.AddDays(-7),
        CreatedAt = ahora.AddDays(-24)
    };

    db.Sprints.AddRange(sprintEnCurso, sprintCerrado);
    db.SaveChanges();   // primero los sprints: los requerimientos necesitan su Id ya asignado

    // ── Requerimientos ───────────────────────────────────────────────────────
    // La cartera cubre los siete estados a propósito: una pantalla donde todo está «En desarrollo»
    // no enseña ni el filtro, ni los colores, ni el orden por estado.

    // Lo más grande del sprint y lo que más duele si se cae: por eso va crítico y con la mitad
    // del avance reportado.
    var timbrado = new Requirement
    {
        Title = "Timbrado CFDI 4.0 en el portal de facturación",
        Description = "Actualizar el complemento de pago y el catálogo de regímenes fiscales al "
                    + "esquema 4.0 del SAT. Incluye reemplazar el servicio del PAC y volver a "
                    + "sellar las facturas rechazadas del mes pasado.",
        Status = RequirementStatus.EnDesarrollo,
        Priority = RequirementPriority.Critica,
        EstimateHours = 48m,
        RequestDate = hoy.AddDays(-14),
        CommittedDeliveryDate = hoy.AddDays(3),
        ProgressPercent = 60,
        Source = RequirementSource.Manual,
        SprintId = sprintEnCurso.Id,
        CreatedAt = ahora.AddDays(-14),
        StatusChangedAt = ahora.AddDays(-5)
    };

    var polizas = new Requirement
    {
        Title = "Póliza contable automática al cierre de mes",
        Description = "Generar el layout de pólizas para Contpaqi desde el portal, sin que "
                    + "contabilidad tenga que capturar a mano los movimientos de caja chica.",
        Status = RequirementStatus.EnPruebas,
        Priority = RequirementPriority.Alta,
        EstimateHours = 24m,
        RequestDate = hoy.AddDays(-18),
        CommittedDeliveryDate = hoy.AddDays(2),
        ProgressPercent = 85,
        Source = RequirementSource.Manual,
        SprintId = sprintEnCurso.Id,
        CreatedAt = ahora.AddDays(-18),
        StatusChangedAt = ahora.AddDays(-2)
    };

    // VENCIDO A PROPÓSITO (dos días): está «por entregar» y la fecha comprometida ya pasó, así que
    // la fila sale resaltada en rojo y además dispara el aviso de compromiso.
    var proveedores = new Requirement
    {
        Title = "Alta de proveedores con validación de RFC contra el SAT",
        Description = "Compras captura el RFC y el sistema valida la lista negra del 69-B antes de "
                    + "dejar guardar. Falta el visto bueno de compras para publicar.",
        Status = RequirementStatus.PorEntregar,
        Priority = RequirementPriority.Media,
        EstimateHours = 16m,
        RequestDate = hoy.AddDays(-21),
        CommittedDeliveryDate = hoy.AddDays(-2),
        ProgressPercent = 95,
        Source = RequirementSource.Manual,
        SprintId = sprintEnCurso.Id,
        CreatedAt = ahora.AddDays(-21),
        StatusChangedAt = ahora.AddDays(-3)
    };

    // VENCIDO A PROPÓSITO (cuatro días) y apenas a la mitad: el caso feo, el que el líder tiene que
    // ver de un vistazo el lunes en la mañana.
    var respaldos = new Requirement
    {
        Title = "Respaldo nocturno de la base de nómina al NAS",
        Description = "Tarea programada que saca el .bak, lo comprime y lo copia al NAS de "
                    + "sistemas. Se atrasó porque el NAS se quedó sin espacio y hubo que depurar.",
        Status = RequirementStatus.EnDesarrollo,
        Priority = RequirementPriority.Alta,
        EstimateHours = 12m,
        RequestDate = hoy.AddDays(-16),
        CommittedDeliveryDate = hoy.AddDays(-4),
        ProgressPercent = 45,
        Source = RequirementSource.Manual,
        SprintId = sprintEnCurso.Id,
        CreatedAt = ahora.AddDays(-16),
        StatusChangedAt = ahora.AddDays(-6)
    };

    // VENCIDO A PROPÓSITO (nueve días) y encima crítico: el peor de la lista. Viene de un bug de
    // DevOps —de ahí Source, ExternalId y las horas ya reportadas al work item.
    var kardex = new Requirement
    {
        Title = "El kardex queda en negativo al cancelar una remisión",
        Description = "Al cancelar una remisión ya surtida, el movimiento de salida no se reversa "
                    + "y el almacén de Guadalupe reporta existencias negativas.",
        Status = RequirementStatus.EnPruebas,
        Priority = RequirementPriority.Critica,
        EstimateHours = 20m,
        RequestDate = hoy.AddDays(-25),
        CommittedDeliveryDate = hoy.AddDays(-9),
        ProgressPercent = 70,
        Source = RequirementSource.AzureDevOps,
        ExternalId = "4809",
        ExternalUrl = "https://dev.azure.com/soltum/Interno/_workitems/edit/4809",
        DevOpsReportedSeconds = 3 * 60 * 60,   // ya se le reportaron 3 h al work item
        SprintId = sprintEnCurso.Id,
        CreatedAt = ahora.AddDays(-25),
        StatusChangedAt = ahora.AddDays(-4)
    };

    // Estimado pero sin empezar: dentro del sprint, cuenta como «sin empezar» en el avance.
    var vacaciones = new Requirement
    {
        Title = "Solicitud de vacaciones en la intranet con firma del jefe",
        Description = "Que el empleado capture su solicitud, el jefe la autorice en línea y salga "
                    + "el formato en PDF ya firmado, en vez de pasear la hoja por las oficinas.",
        Status = RequirementStatus.Estimado,
        Priority = RequirementPriority.Media,
        EstimateHours = 18m,
        RequestDate = hoy.AddDays(-9),
        CommittedDeliveryDate = hoy.AddDays(6),
        ProgressPercent = 0,
        Source = RequirementSource.Manual,
        SprintId = sprintEnCurso.Id,
        CreatedAt = ahora.AddDays(-9),
        StatusChangedAt = ahora.AddDays(-7)
    };

    // Del BACKLOG: estimado y con precio, pero todavía no entra a ningún sprint (SprintId nulo).
    var bitacoraFtp = new Requirement
    {
        Title = "Registrar en bitácora cada publicación por FTP",
        Description = "Dejar asentado quién publicó, a qué servidor y con qué versión. Hoy solo "
                    + "queda en el chat del equipo y a la semana ya nadie se acuerda.",
        Status = RequirementStatus.Estimado,
        Priority = RequirementPriority.Baja,
        EstimateHours = 8m,
        RequestDate = hoy.AddDays(-11),
        ProgressPercent = 0,
        Source = RequirementSource.Manual,
        CreatedAt = ahora.AddDays(-11),
        StatusChangedAt = ahora.AddDays(-10)
    };

    // Llegó por correo y nadie lo ha estimado: sin horas, sin compromiso y fuera de sprint. Es el
    // estado en el que nace todo lo que pide otra área.
    var accesoUnico = new Requirement
    {
        Title = "Entrar a la intranet con la misma cuenta de Windows",
        Description = "Petición de Recursos Humanos por correo: la gente de planta olvida la "
                    + "contraseña de la intranet porque es distinta a la de su computadora.",
        Status = RequirementStatus.PorEstimar,
        Priority = RequirementPriority.Media,
        RequestDate = hoy.AddDays(-4),
        ProgressPercent = 0,
        Source = RequirementSource.Email,
        CreatedAt = ahora.AddDays(-4),
        StatusChangedAt = ahora.AddDays(-4)
    };

    var tableroDireccion = new Requirement
    {
        Title = "Tablero de indicadores para dirección",
        Description = "Ventas del mes, cartera vencida y avance de proyectos en una sola pantalla "
                    + "para la junta de los lunes. Falta que dirección diga qué números quiere.",
        Status = RequirementStatus.PorEstimar,
        Priority = RequirementPriority.Baja,
        RequestDate = hoy.AddDays(-2),
        ProgressPercent = 0,
        Source = RequirementSource.AzureDevOps,
        ExternalId = "4826",
        ExternalUrl = "https://dev.azure.com/soltum/Interno/_workitems/edit/4826",
        CreatedAt = ahora.AddDays(-2),
        StatusChangedAt = ahora.AddDays(-2)
    };

    // En desarrollo pero FUERA de sprint: trabajo comprometido con un cliente interno que no cupo
    // en la quincena. Su fecha aún no vence, así que no sale en rojo.
    var cotizador = new Requirement
    {
        Title = "Cotizador de fletes por zona en el portal de ventas",
        Description = "Calcular el flete según la zona del cliente y el peso volumétrico, con la "
                    + "tabla que mantiene logística en Excel.",
        Status = RequirementStatus.EnDesarrollo,
        Priority = RequirementPriority.Media,
        EstimateHours = 30m,
        RequestDate = hoy.AddDays(-7),
        CommittedDeliveryDate = hoy.AddDays(12),
        ProgressPercent = 25,
        Source = RequirementSource.Manual,
        CreatedAt = ahora.AddDays(-7),
        StatusChangedAt = ahora.AddDays(-3)
    };

    // ENTREGADO A TIEMPO: la entrega real es anterior al compromiso, que es como el reporte de
    // cumplimiento distingue «A tiempo» de «Tardía».
    var declaracionIva = new Requirement
    {
        Title = "Reporte de IVA acreditable por proveedor",
        Description = "Concentrado mensual para la declaración, con el desglose de retenciones. "
                    + "Se entregó a contabilidad y ya se usó para el cierre del mes.",
        Status = RequirementStatus.Entregado,
        Priority = RequirementPriority.Alta,
        EstimateHours = 14m,
        RequestDate = hoy.AddDays(-30),
        CommittedDeliveryDate = hoy.AddDays(-9),
        ActualDeliveryDate = hoy.AddDays(-11),
        ProgressPercent = 100,
        Source = RequirementSource.Manual,
        SprintId = sprintCerrado.Id,
        CreatedAt = ahora.AddDays(-30),
        StatusChangedAt = ahora.AddDays(-11)
    };

    // ENTREGADO TARDE: se entregó cuatro días después de lo comprometido. Los dos casos juntos
    // hacen que el porcentaje de cumplimiento del sprint cerrado no salga en 100 %.
    var migracionIntranet = new Requirement
    {
        Title = "Migrar la intranet de .NET Framework 4.8 a .NET 8",
        Description = "Se atoró en el módulo de reportes, que dependía de un componente de terceros "
                    + "sin versión compatible; hubo que rehacerlo con QuestPDF.",
        Status = RequirementStatus.Entregado,
        Priority = RequirementPriority.Media,
        EstimateHours = 60m,
        RequestDate = hoy.AddDays(-45),
        CommittedDeliveryDate = hoy.AddDays(-12),
        ActualDeliveryDate = hoy.AddDays(-8),
        ProgressPercent = 100,
        Source = RequirementSource.Manual,
        SprintId = sprintCerrado.Id,
        CreatedAt = ahora.AddDays(-45),
        StatusChangedAt = ahora.AddDays(-8)
    };

    // CANCELADO: se quedó a medias y ya no se retoma. No cuenta en el avance del sprint ni genera
    // avisos por más vencido que esté, aunque conserva su fecha comprometida y su avance.
    var crystal = new Requirement
    {
        Title = "Rediseñar los formatos de Crystal Reports del almacén",
        Description = "Se canceló: almacén decidió seguir con el formato en Excel que ya tienen y "
                    + "el esfuerzo se movió al kardex.",
        Status = RequirementStatus.Cancelado,
        Priority = RequirementPriority.Baja,
        EstimateHours = 10m,
        RequestDate = hoy.AddDays(-28),
        CommittedDeliveryDate = hoy.AddDays(-10),
        ProgressPercent = 20,
        Source = RequirementSource.Manual,
        SprintId = sprintCerrado.Id,
        CreatedAt = ahora.AddDays(-28),
        StatusChangedAt = ahora.AddDays(-13)
    };

    db.Requirements.AddRange(
        timbrado, polizas, proveedores, respaldos, kardex, vacaciones, bitacoraFtp,
        accesoUnico, tableroDireccion, cotizador, declaracionIva, migracionIntranet, crystal);
    db.SaveChanges();   // ahora sí hay RequirementId para colgarles las asignaciones

    // ── Asignaciones ─────────────────────────────────────────────────────────
    // El timbrado y el kardex van con DOS personas: es lo normal cuando algo urge o cuando quien
    // lo trae necesita que otro le revise antes de subirlo a producción.
    db.Assignments.AddRange(
        new Assignment { RequirementId = timbrado.Id, DeveloperId = b.Ana.Id, Role = "Desarrollo", AssignedAt = ahora.AddDays(-13) },
        new Assignment { RequirementId = timbrado.Id, DeveloperId = b.Beto.Id, Role = "Apoyo en el sellado", AssignedAt = ahora.AddDays(-5) },
        new Assignment { RequirementId = polizas.Id, DeveloperId = b.Beto.Id, Role = "Desarrollo", AssignedAt = ahora.AddDays(-17) },
        new Assignment { RequirementId = proveedores.Id, DeveloperId = b.Caro.Id, Role = "Desarrollo", AssignedAt = ahora.AddDays(-20) },
        new Assignment { RequirementId = respaldos.Id, DeveloperId = b.Ana.Id, Role = null, AssignedAt = ahora.AddDays(-15) },
        new Assignment { RequirementId = kardex.Id, DeveloperId = b.Dani.Id, Role = "Corrección", AssignedAt = ahora.AddDays(-24) },
        new Assignment { RequirementId = kardex.Id, DeveloperId = b.Caro.Id, Role = "Pruebas", AssignedAt = ahora.AddDays(-4) },
        new Assignment { RequirementId = vacaciones.Id, DeveloperId = b.Dani.Id, Role = "Desarrollo", AssignedAt = ahora.AddDays(-7) },
        new Assignment { RequirementId = bitacoraFtp.Id, DeveloperId = b.Beto.Id, Role = null, AssignedAt = ahora.AddDays(-10) },
        new Assignment { RequirementId = cotizador.Id, DeveloperId = b.Caro.Id, Role = "Desarrollo", AssignedAt = ahora.AddDays(-6) },
        new Assignment { RequirementId = declaracionIva.Id, DeveloperId = b.Ana.Id, Role = "Desarrollo", AssignedAt = ahora.AddDays(-29) },
        new Assignment { RequirementId = migracionIntranet.Id, DeveloperId = b.Dani.Id, Role = "Desarrollo", AssignedAt = ahora.AddDays(-44) },
        // Se quedó asignado aunque se canceló: así fue y así debe verse en el histórico de quién
        // trajo qué.
        new Assignment { RequirementId = crystal.Id, DeveloperId = b.Beto.Id, Role = "Desarrollo", AssignedAt = ahora.AddDays(-27) });

    // ── Tickets de Azure DevOps ──────────────────────────────────────────────
    // Todos comparten SyncedAt: la sincronización es MANUAL y trae el lote completo de una pasada,
    // así que en la base real todos los tickets quedan con el mismo sello.
    var sincronizado = ahora.AddMinutes(-40);
    const string org = "https://dev.azure.com/soltum/Interno/_workitems/edit/";

    // AssignedToUniqueName es lo que empata el ticket con la ficha del desarrollador (por correo);
    // el nombre para mostrar no sirve para eso porque va y viene con acentos.
    db.DevOpsTickets.AddRange(
        // Prioridad ya definida por el líder y horas ya estimadas por quien lo trae: el ticket
        // «completo», el que no aparece en ninguna lista de pendientes del líder. Sigue en Active
        // aunque aquí el requerimiento ya esté en pruebas: el equipo no mueve el bug en DevOps
        // hasta que el usuario lo verifica.
        new DevOpsTicket
        {
            ExternalId = 4809,
            Title = "El kardex queda en negativo al cancelar una remisión",
            WorkItemType = "Bug",
            State = "Active",
            Priority = "1",
            AssignedTo = b.Dani.FullName,
            AssignedToUniqueName = b.Dani.Email,
            AreaPath = @"Interno\Producto",
            IterationPath = $@"Interno\{sprintEnCurso.Name}",
            Tags = "Inventarios; Producción",
            Description = "Reportado por el almacén de Guadalupe. Se reproduce cancelando una "
                        + "remisión que ya tenía salida registrada.",
            CreatedAtExternal = ahora.AddDays(-25),
            UpdatedAtExternal = ahora.AddDays(-1),
            SyncedAt = sincronizado,
            Url = org + "4809",
            CommentCount = 7,
            PriorityConfirmedAt = ahora.AddDays(-24),
            PriorityConfirmedByUserId = b.Lider.Id,
            EstimatedHours = 20,
            EstimatedAt = ahora.AddDays(-23),
            EstimatedByDeveloperId = b.Dani.Id
        },
        // SIN PRIORIDAD DEFINIDA y SIN ESTIMAR: el 2 de Priority es el que DevOps le pone a todo
        // por omisión, y como PriorityConfirmedAt está en nulo, nadie la ha pensado todavía.
        new DevOpsTicket
        {
            ExternalId = 4812,
            Title = "Error 500 al descargar el XML del CFDI timbrado",
            WorkItemType = "Bug",
            State = "New",
            Priority = "2",
            AssignedTo = b.Ana.FullName,
            AssignedToUniqueName = b.Ana.Email,
            AreaPath = @"Interno\Plataforma",
            IterationPath = $@"Interno\{sprintEnCurso.Name}",
            Tags = "Facturación",
            Description = "Solo pasa con las facturas que traen complemento de pago.",
            CreatedAtExternal = ahora.AddDays(-2),
            UpdatedAtExternal = ahora.AddDays(-2),
            SyncedAt = sincronizado,
            Url = org + "4812",
            CommentCount = 1
        },
        new DevOpsTicket
        {
            ExternalId = 4815,
            Title = "Publicar por FTP la versión 3.4.1 del portal de facturación",
            WorkItemType = "Task",
            State = "Resolved",
            Priority = "2",
            AssignedTo = b.Beto.FullName,
            AssignedToUniqueName = b.Beto.Email,
            AreaPath = @"Interno\Plataforma",
            IterationPath = $@"Interno\{sprintEnCurso.Name}",
            Tags = "Despliegue; Facturación",
            Description = "Subir el paquete a los dos servidores de producción y verificar el "
                        + "web.config antes de avisarle a facturación.",
            CreatedAtExternal = ahora.AddDays(-6),
            UpdatedAtExternal = ahora.AddHours(-20),
            SyncedAt = sincronizado,
            Url = org + "4815",
            CommentCount = 3,
            PriorityConfirmedAt = ahora.AddDays(-6),
            PriorityConfirmedByUserId = b.Lider.Id,
            EstimatedHours = 2,
            EstimatedAt = ahora.AddDays(-6),
            EstimatedByDeveloperId = b.Beto.Id
        },
        // Historia de usuario con puntos: sirve para que la columna de Story Points no salga vacía
        // en toda la tabla.
        new DevOpsTicket
        {
            ExternalId = 4818,
            Title = "Como cobranza, quiero exportar el estado de cuenta del cliente a Excel",
            WorkItemType = "User Story",
            State = "Active",
            Priority = "3",
            AssignedTo = b.Caro.FullName,
            AssignedToUniqueName = b.Caro.Email,
            AreaPath = @"Interno\Producto",
            IterationPath = $@"Interno\{sprintEnCurso.Name}",
            Tags = "Cobranza",
            Description = "Con los saldos a la fecha de corte y los pagos aplicados del periodo.",
            StoryPoints = 5,
            CreatedAtExternal = ahora.AddDays(-10),
            UpdatedAtExternal = ahora.AddDays(-3),
            SyncedAt = sincronizado,
            Url = org + "4818",
            CommentCount = 4,
            PriorityConfirmedAt = ahora.AddDays(-9),
            PriorityConfirmedByUserId = b.Lider.Id,
            EstimatedHours = 12,
            EstimatedAt = ahora.AddDays(-8),
            EstimatedByDeveloperId = b.Caro.Id
        },
        // Cerrado: no cuenta como abierto ni pesa en los pendientes, pero tiene que existir para
        // que los filtros por estado y el tablero por etiqueta tengan de dónde contar.
        new DevOpsTicket
        {
            ExternalId = 4820,
            Title = "La nómina duplica el sueldo cuando hay dos incidencias el mismo día",
            WorkItemType = "Bug",
            State = "Closed",
            Priority = "1",
            AssignedTo = b.Ana.FullName,
            AssignedToUniqueName = b.Ana.Email,
            AreaPath = @"Interno\Plataforma",
            IterationPath = $@"Interno\{sprintCerrado.Name}",
            Tags = "Nómina; Producción",
            Description = "Se corrigió el agrupado por empleado y día en el cálculo de percepciones.",
            CreatedAtExternal = ahora.AddDays(-19),
            UpdatedAtExternal = ahora.AddDays(-12),
            SyncedAt = sincronizado,
            Url = org + "4820",
            CommentCount = 9,
            PriorityConfirmedAt = ahora.AddDays(-19),
            PriorityConfirmedByUserId = b.Lider.Id,
            EstimatedHours = 8,
            EstimatedAt = ahora.AddDays(-18),
            EstimatedByDeveloperId = b.Ana.Id
        },
        // SIN ASIGNAR y sin prioridad definida: no le cuenta a nadie como «sin estimar» —eso solo
        // aplica a lo que alguien ya trae— pero sí está en la lista de lo que el líder debe repartir.
        new DevOpsTicket
        {
            ExternalId = 4823,
            Title = "Renovar el certificado SSL del portal de facturación",
            WorkItemType = "Task",
            State = "New",
            Priority = "2",
            AssignedTo = "",
            AreaPath = @"Interno\Plataforma",
            IterationPath = @"Interno",
            Tags = "Infraestructura",
            Description = "Vence el mes que entra. Hay que renovarlo y volver a montarlo en los "
                        + "dos servidores de producción.",
            CreatedAtExternal = ahora.AddDays(-3),
            UpdatedAtExternal = ahora.AddDays(-3),
            SyncedAt = sincronizado,
            Url = org + "4823",
            CommentCount = 0
        },
        new DevOpsTicket
        {
            ExternalId = 4826,
            Title = "Tablero de indicadores para dirección",
            WorkItemType = "Feature",
            State = "New",
            Priority = "3",
            AssignedTo = "",
            AreaPath = @"Interno\Producto",
            IterationPath = @"Interno",
            Tags = "Dirección",
            Description = "Pendiente de que dirección defina qué indicadores quiere ver.",
            StoryPoints = 13,
            CreatedAtExternal = ahora.AddDays(-2),
            UpdatedAtExternal = ahora.AddDays(-2),
            SyncedAt = sincronizado,
            Url = org + "4826",
            CommentCount = 2
        },
        // Removed: se descartó por duplicado. Cuenta como cerrado igual que Closed, y es el caso
        // que distingue «terminado» de «terminado por descarte».
        new DevOpsTicket
        {
            ExternalId = 4829,
            Title = "Se pierde la sesión al volver del portal de proveedores",
            WorkItemType = "Bug",
            State = "Removed",
            Priority = "4",
            AssignedTo = b.Beto.FullName,
            AssignedToUniqueName = b.Beto.Email,
            AreaPath = @"Interno\Plataforma",
            IterationPath = $@"Interno\{sprintCerrado.Name}",
            Tags = "Proveedores",
            Description = "Duplicado del 4791; se cerró aquel y este se descartó.",
            CreatedAtExternal = ahora.AddDays(-15),
            UpdatedAtExternal = ahora.AddDays(-14),
            SyncedAt = sincronizado,
            Url = org + "4829",
            CommentCount = 2,
            PriorityConfirmedAt = ahora.AddDays(-15),
            PriorityConfirmedByUserId = b.Lider.Id
        },
        // Asignado pero SIN ESTIMAR: quien lo trae todavía no dijo cuánto le va a llevar, que es
        // justo lo que la pantalla del desarrollador le reclama.
        new DevOpsTicket
        {
            ExternalId = 4831,
            Title = "Dar de alta al auditor externo con permisos de solo lectura",
            WorkItemType = "Task",
            State = "Active",
            Priority = "4",
            AssignedTo = b.Dani.FullName,
            AssignedToUniqueName = b.Dani.Email,
            AreaPath = @"Interno\Producto",
            IterationPath = $@"Interno\{sprintEnCurso.Name}",
            Tags = "Accesos",
            Description = "Solo consulta de facturación y almacén, sin acceso a nómina.",
            CreatedAtExternal = ahora.AddHours(-30),
            UpdatedAtExternal = ahora.AddHours(-6),
            SyncedAt = sincronizado,
            Url = org + "4831",
            CommentCount = 1
        });

    db.SaveChanges();
}


    // ═══ POOL-DESEMPENO ═══

// ── Pool de actividades y desempeño ──────────────────────────────────────────

/// <summary>
/// El pool de actividades con su ciclo entero —lo que está libre, lo tomado, lo entregado, lo
/// devuelto y lo aceptado— y todo lo que cuelga del desempeño: los puntos de varios meses, las
/// evaluaciones y los hitos.
///
/// <para>Los puntos NO se inventan: salen de la matriz que <c>PoolSeed</c> siembra al arrancar,
/// igual que haría la aplicación al publicar cada actividad. Así la demostración enseña números
/// coherentes con lo que el líder tenga configurado, y no una tabla paralela que se contradiga
/// con la pantalla de la matriz.</para>
/// </summary>
public static void SembrarPoolYDesempeno(AppDbContext db, DatosBase b)
{
    var ahora = DateTime.UtcNow;
    int lider = b.Lider.Id;

    // Dependen de PoolSeed y ScoringCriteriaSeed, que corren en el arranque ANTES que esto. Se
    // leen enteros de una vez: son unas cuantas docenas de filas y así no se hace un viaje a la
    // base por cada actividad que se siembre.
    var matriz = db.PoolPointsMatrix.ToList();
    var criterios = db.ScoringCriteria.ToList();

    PoolPointsMatrixEntry Celda(PoolWorkType tipo, PoolComplexity complejidad) =>
        matriz.First(m => m.WorkType == tipo && m.Complexity == complejidad);

    ScoringCriterion Crit(string nombre) => criterios.First(c => c.Name == nombre);

    // ── Lo que está libre en el pool ─────────────────────────────────────────

    var libreIva = new PoolActivity
    {
        Title = "Corregir el IVA desglosado en las notas de crédito del CFDI",
        Description = "Contabilidad reporta que las notas de crédito salen con el IVA sumado al " +
                      "subtotal en lugar de desglosado, y el PAC las está rechazando. Se reproduce " +
                      "con la nota NC-2291 del ambiente de pruebas.",
        WorkType = PoolWorkType.Bug,
        Complexity = PoolComplexity.Media,
        Points = Celda(PoolWorkType.Bug, PoolComplexity.Media).Points,
        Priority = PoolPriority.Alta,
        // En un BUG el plazo lo pone SIEMPRE el líder y la matriz no lo pone por él: por eso ningún
        // bug de la demostración aparece sin horas. Veinticuatro de reloj, que con el PAC rechazando
        // facturas es lo que hay. El ESFUERZO va vacío a propósito: lo estimará quien lo tome, en el
        // momento de tomarlo, que es el único en que ese número es honesto.
        HorasLimite = 24m,
        Status = PoolActivityStatus.Disponible,
        ExternalUrl = "https://dev.azure.com/soltum/Interno/_workitems/edit/4187",
        CreatedByUserId = lider,
        CreatedAt = ahora.AddDays(-2)
    };

    var libreCatalogo = new PoolActivity
    {
        Title = "Actualizar el catálogo de bancos del módulo de pagos",
        Description = "Dar de alta las instituciones nuevas del catálogo del SAT y marcar como " +
                      "inactivas las que ya no operan. Es captura y una migración chica.",
        WorkType = PoolWorkType.Tarea,
        Complexity = PoolComplexity.Baja,
        Points = Celda(PoolWorkType.Tarea, PoolComplexity.Baja).Points,
        Priority = PoolPriority.Baja,
        // Sin plazo propio: en una TAREA el plazo sale de la matriz, y el campo por actividad solo se
        // usa cuando hay un motivo para apretarlo. El ESFUERZO, en cambio, lo estima el líder al
        // publicarla y es obligatorio: es el número contra el que se contrastará el cronómetro.
        HorasEstimadas = 6m,
        HorasEstimadasEnUtc = ahora.AddDays(-17),
        Status = PoolActivityStatus.Disponible,
        CreatedByUserId = lider,
        // Lleva más de dos semanas sin que nadie la tome: paga poco y no urge. El pool no se vacía
        // solo, y que se vea es justo la conversación que el líder necesita tener.
        CreatedAt = ahora.AddDays(-17)
    };

    var libreAntiguedad = new PoolActivity
    {
        Title = "Reporte de antigüedad de saldos para el corporativo",
        Description = "Cortes a 30, 60, 90 y más de 90 días, con filtro por sucursal y exportación " +
                      "a Excel. Lo pidió dirección para el cierre de mes.",
        WorkType = PoolWorkType.Requerimiento,
        Complexity = PoolComplexity.Alta,
        Points = Celda(PoolWorkType.Requerimiento, PoolComplexity.Alta).Points,
        Priority = PoolPriority.Critica,
        // La matriz da 104 horas para un requerimiento alto; aquí se conceden 40 porque el cierre no
        // espera. El plazo es lo ÚNICO de la matriz que se ajusta por actividad: los puntos siguen
        // siendo los de la matriz, porque apretar la fecha no vale puntos.
        //
        // Y son horas de RELOJ: 40 h desde que alguien la tome vence pasado mañana, no dentro de
        // cinco días laborales. Es lo que hace que el plazo se pueda restar de lo que mida el
        // cronómetro, que también cuenta horas.
        HorasLimite = 40m,
        // El esfuerzo lo pone el líder porque es un requerimiento: 72 horas de trabajo dentro de un
        // plazo de 40 de reloj es exactamente la conversación que esta pantalla tiene que provocar.
        HorasEstimadas = 72m,
        HorasEstimadasEnUtc = ahora.AddDays(-1),
        Status = PoolActivityStatus.Disponible,
        ExternalUrl = "https://dev.azure.com/soltum/Interno/_workitems/edit/4203",
        CreatedByUserId = lider,
        CreatedAt = ahora.AddDays(-1)
    };
    // Se anuncian ANTES de que nadie la tome: quien la agarre ya sabe qué se le va a mirar de más
    // y cuánto puede cobrar por encima de la base.
    libreAntiguedad.ExtraCriteria.Add(CriterioExtraDeDemo(Crit("Agregaste pruebas automatizadas")));
    libreAntiguedad.ExtraCriteria.Add(CriterioExtraDeDemo(Crit("Dejaste la documentación al día")));

    var librePolizas = new PoolActivity
    {
        Title = "Migrar la carga masiva de pólizas a la capa nueva de acceso a datos",
        Description = "El proceso todavía arma el SQL a mano y truena con archivos de más de 5 000 " +
                      "renglones. Hay que pasarlo a la capa nueva sin cambiarle el layout al usuario.",
        WorkType = PoolWorkType.Tarea,
        Complexity = PoolComplexity.MuyAlta,
        Points = Celda(PoolWorkType.Tarea, PoolComplexity.MuyAlta).Points,
        Priority = PoolPriority.Media,
        // Plazo el de la matriz (104 h); el esfuerzo lo estimó el líder en 90, que es casi todo el
        // plazo: por eso lleva veinticuatro días sin que nadie se anime.
        HorasEstimadas = 90m,
        HorasEstimadasEnUtc = ahora.AddDays(-24),
        Status = PoolActivityStatus.Disponible,
        CreatedByUserId = lider,
        // La más cara del pool y ahí sigue: enseña que puntos altos no bastan para que alguien
        // tome lo difícil.
        CreatedAt = ahora.AddDays(-24)
    };

    // ── Tomadas y en curso ───────────────────────────────────────────────────

    var tomaAna = ahora.AddDays(-2);
    var enCursoAna = new PoolActivity
    {
        Title = "Tiempo de espera agotado al consultar el estado de cuenta del portal",
        Description = "Los clientes con más de dos años de movimientos ven la pantalla en blanco. " +
                      "Pasa solo en producción y no siempre, así que hay que medir la consulta antes " +
                      "de tocarla.",
        WorkType = PoolWorkType.Bug,
        Complexity = PoolComplexity.Alta,
        Points = Celda(PoolWorkType.Bug, PoolComplexity.Alta).Points,
        Priority = PoolPriority.Alta,
        // Bug: el plazo lo puso el líder, 120 horas de reloj. El plazo empieza a correr cuando se
        // toma y no cuando se publica, así que tomada hace dos días todavía le quedan tres.
        HorasLimite = 120m,
        ClaimDeadlineAt = tomaAna.AddHours(120),
        // La estimación es de ANA, no del líder, y el sello coincide con ClaimedAt porque se capturó
        // al tomarla. Es el caso honesto: se dijo antes de saber lo que costaría. Diez horas contra
        // un cronómetro que ya lleva dos días corriendo es lo que hace útil la comparación.
        HorasEstimadas = 10m,
        HorasEstimadasEnUtc = tomaAna,
        Status = PoolActivityStatus.Tomada,
        ClaimedByDeveloperId = b.Ana.Id,
        ClaimedAt = tomaAna,
        ExternalUrl = "https://dev.azure.com/soltum/Interno/_workitems/edit/4174",
        CreatedByUserId = lider,
        CreatedAt = ahora.AddDays(-5)
    };

    var tomaDani = ahora.AddDays(-9);
    var vencidaDani = new PoolActivity
    {
        Title = "Depurar los movimientos duplicados del corte de caja",
        Description = "Cuando el cajero cierra dos veces seguidas se duplican los movimientos del " +
                      "corte. Hay que limpiar lo que ya está duplicado y evitar el segundo cierre.",
        WorkType = PoolWorkType.Tarea,
        Complexity = PoolComplexity.Media,
        Points = Celda(PoolWorkType.Tarea, PoolComplexity.Media).Points,
        Priority = PoolPriority.Media,
        // Tarea con el plazo apretado a mano: 32 horas en vez de las 40 de la matriz.
        HorasLimite = 32m,
        // El esfuerzo es del LÍDER, porque es una tarea, y por eso el sello es el de la publicación
        // y no el de cuando Dani la tomó.
        HorasEstimadas = 20m,
        HorasEstimadasEnUtc = ahora.AddDays(-22),
        Status = PoolActivityStatus.Tomada,
        ClaimedByDeveloperId = b.Dani.Id,
        ClaimedAt = tomaDani,
        // VENCIDA a propósito: se tomó hace nueve días con 32 horas de plazo, así que se cumplió hace
        // más de una semana y sigue en curso. Es la fila que la pantalla resalta. Vencer no la libera
        // sola —eso le quitaría el trabajo de las manos a quien lo está haciendo ahora mismo—, lo
        // decide el líder.
        ClaimDeadlineAt = tomaDani.AddHours(32),
        // Ya había estado en manos de alguien y volvió al pool. Devolver no se castiga, pero el
        // número queda a la vista: una actividad que rebota varias veces dice algo.
        ReturnedCount = 1,
        ReviewHistory = HistorialDeDemo(
            (ahora.AddDays(-15), $"Liberada por {b.Lider.FullName}: quien la tenía salió de vacaciones. Vuelve al pool."),
            (tomaDani, $"Tomada por {b.Dani.FullName}.")),
        CreatedByUserId = lider,
        CreatedAt = ahora.AddDays(-22)
    };

    // ── Entregada y esperando verificación ───────────────────────────────────

    // Tomada hace dos días y entregada ayer. Con el plazo de la matriz en HORAS (64 para un
    // requerimiento medio) esas dos fechas tienen que estar cerca: con las de antes —tomada hace
    // seis días— la entrega habría caído fuera de plazo y la demostración contaría, sin querer, que
    // aquí se entrega tarde.
    var tomaBeto = ahora.AddDays(-2);
    var entregaBeto = ahora.AddDays(-1);
    var porVerificar = new PoolActivity
    {
        Title = "Alta de proveedores extranjeros sin RFC en el módulo de compras",
        Description = "Capturar proveedores del extranjero con su Tax ID y dejar el RFC genérico " +
                      "XEXX010101000 en la factura, con la validación del país.",
        WorkType = PoolWorkType.Requerimiento,
        Complexity = PoolComplexity.Media,
        Points = Celda(PoolWorkType.Requerimiento, PoolComplexity.Media).Points,
        Priority = PoolPriority.Alta,
        // Plazo el de la matriz: en un requerimiento no lo pone el líder por actividad salvo que haya
        // un motivo. El esfuerzo sí lo puso él, al publicarla.
        HorasEstimadas = 28m,
        HorasEstimadasEnUtc = ahora.AddDays(-8),
        Status = PoolActivityStatus.EnRevision,
        ClaimedByDeveloperId = b.Beto.Id,
        ClaimedAt = tomaBeto,
        ClaimDeadlineAt = tomaBeto.AddHours((double)Celda(PoolWorkType.Requerimiento, PoolComplexity.Media).HorasLimite),
        DeliveredAt = entregaBeto,
        ReviewHistory = HistorialDeDemo(
            (entregaBeto, $"Entregada por {b.Beto.FullName} para verificación.")),
        ExternalUrl = "https://dev.azure.com/soltum/Interno/_workitems/edit/4166",
        CreatedByUserId = lider,
        CreatedAt = ahora.AddDays(-8)
    };
    // Entregada, pero con los criterios extra todavía SIN EVALUAR (IsMet en null): es el caso que
    // bloquea la aceptación hasta que el líder diga sí o no a cada uno.
    porVerificar.ExtraCriteria.Add(CriterioExtraDeDemo(Crit("Agregaste pruebas automatizadas")));
    porVerificar.ExtraCriteria.Add(CriterioExtraDeDemo(Crit("Dejaste evidencias completas")));

    // ── Devuelta para corregir ───────────────────────────────────────────────

    var tomaCaro = ahora.AddDays(-10);
    var entregaCaro = ahora.AddDays(-4);
    var devolucionCaro = ahora.AddDays(-3);
    var devuelta = new PoolActivity
    {
        Title = "El folio de la remisión se repite cuando dos sucursales facturan a la vez",
        Description = "Dos sucursales que timbran en el mismo segundo obtienen el mismo folio y la " +
                      "segunda remisión se pierde. Reportado por Tlalnepantla y por Querétaro.",
        WorkType = PoolWorkType.Bug,
        Complexity = PoolComplexity.Alta,
        Points = Celda(PoolWorkType.Bug, PoolComplexity.Alta).Points,
        Priority = PoolPriority.Critica,
        // Bug crítico: 72 horas de reloj puestas por el líder. Se tomó hace diez días, así que está
        // VENCIDA —también lo estaba con la regla vieja— y además devuelta para corregir: es el caso
        // peor del tablero y conviene que se vea, porque es el que obliga a decidir algo.
        HorasLimite = 72m,
        // La estimación es de CARO, capturada al tomarlo. Sigue puesta porque la actividad NO volvió
        // al pool: devolverla para corregir no le quita el reclamo, sigue siendo suya.
        HorasEstimadas = 12m,
        HorasEstimadasEnUtc = tomaCaro,
        Status = PoolActivityStatus.Devuelta,
        ClaimedByDeveloperId = b.Caro.Id,
        ClaimedAt = tomaCaro,
        ClaimDeadlineAt = tomaCaro.AddHours(72),
        DeliveredAt = entregaCaro,
        ReviewedByUserId = lider,
        ReviewedAt = devolucionCaro,
        ReviewComment = "El candado sobre la tabla de folios resuelve lo de las dos sucursales, pero " +
                        "deja fuera la app móvil, que escribe por otro camino. Falta cubrir ésa y " +
                        "volver a probar con las dos rutas al mismo tiempo.",
        // Sigue siendo de Caro: devolver no se la quita, le dice qué falta.
        ReviewRound = 1,
        ReviewHistory = HistorialDeDemo(
            (entregaCaro, $"Entregada por {b.Caro.FullName} para verificación."),
            (devolucionCaro, $"Devuelta por {b.Lider.FullName}: falta cubrir la ruta de la app móvil.")),
        ExternalUrl = "https://dev.azure.com/soltum/Interno/_workitems/edit/4151",
        CreatedByUserId = lider,
        CreatedAt = ahora.AddDays(-12)
    };
    // Criterios ya evaluados sobre una entrega devuelta: uno cumplido y otro no, con el motivo
    // escrito. Que «no cumplido» se distinga de «sin evaluar» es justo lo que necesita quien va a
    // corregir: le dice qué mirar antes de volver a entregar.
    devuelta.ExtraCriteria.Add(CriterioExtraDeDemo(
        Crit("Dejaste evidencias completas"), true, devolucionCaro,
        "Vinieron el video de la reproducción y las consultas de comprobación."));
    devuelta.ExtraCriteria.Add(CriterioExtraDeDemo(
        Crit("Agregaste pruebas automatizadas"), false, devolucionCaro,
        "La prueba cubre solo el camino del portal; falta la de la app móvil."));

    // ── Aceptadas, con sus puntos ya abonados ────────────────────────────────

    var tomaAnaCerrada = ahora.AddDays(-18);
    var entregaAnaCerrada = ahora.AddDays(-12);
    var aceptacionAna = ahora.AddDays(-11);
    var aceptadaAna = new PoolActivity
    {
        Title = "Automatizar el respaldo nocturno de la base de nómina",
        Description = "Tarea programada que respalda, comprime y sube el archivo al servidor de " +
                      "resguardo, con aviso por correo si falla.",
        WorkType = PoolWorkType.Tarea,
        Complexity = PoolComplexity.Alta,
        Points = Celda(PoolWorkType.Tarea, PoolComplexity.Alta).Points,
        Priority = PoolPriority.Media,
        // Tarea con el plazo aflojado a 160 horas de reloj: se tomó hace 18 días y se entregó a los
        // seis, así que se entregó DENTRO de plazo. Con las 64 h de la matriz habría salido tarde, y
        // una aceptación fuera de plazo no es la historia que esta fila cuenta.
        HorasLimite = 160m,
        // Esfuerzo del líder: 32 horas estimadas para una tarea alta.
        HorasEstimadas = 32m,
        HorasEstimadasEnUtc = ahora.AddDays(-20),
        Status = PoolActivityStatus.Aceptada,
        ClaimedByDeveloperId = b.Ana.Id,
        ClaimedAt = tomaAnaCerrada,
        ClaimDeadlineAt = tomaAnaCerrada.AddHours(160),
        DeliveredAt = entregaAnaCerrada,
        ReviewedByUserId = lider,
        ReviewedAt = aceptacionAna,
        CreatedByUserId = lider,
        CreatedAt = ahora.AddDays(-20)
    };
    // El criterio extra SÍ se cumplió, así que sus puntos se suman a la base al aceptar.
    var extraAna = CriterioExtraDeDemo(
        Crit("Agregaste pruebas automatizadas"), true, aceptacionAna,
        "Trae prueba del restore, que es la parte que de verdad importa de un respaldo.");
    aceptadaAna.ExtraCriteria.Add(extraAna);

    int totalAna = aceptadaAna.Points + extraAna.Points;
    // Mismo desglose que escribe el servicio al aceptar, para que el comentario de los puntos y el
    // historial de la actividad digan exactamente lo mismo.
    string desgloseAna = $" (+{aceptadaAna.Points} base, +{extraAna.Points} por {extraAna.Name})";
    aceptadaAna.ReviewHistory = HistorialDeDemo(
        (entregaAnaCerrada, $"Entregada por {b.Ana.FullName} para verificación."),
        (aceptacionAna, $"Aceptada por {b.Lider.FullName}: +{totalAna} pts{desgloseAna}."));

    var tomaDaniCerrada = ahora.AddDays(-26);
    var entregaDaniCerrada = ahora.AddDays(-23);
    var aceptacionDani = ahora.AddDays(-22);
    var aceptadaDani = new PoolActivity
    {
        Title = "El buscador de clientes ignora los acentos",
        Description = "Buscar «Martinez» no encuentra a «Martínez». Se resuelve con la intercalación " +
                      "correcta en la consulta, sin tocar los datos.",
        WorkType = PoolWorkType.Bug,
        Complexity = PoolComplexity.Baja,
        Points = Celda(PoolWorkType.Bug, PoolComplexity.Baja).Points,
        Priority = PoolPriority.Baja,
        // Bug: el plazo lo puso el líder, 80 horas. Tomada hace 26 días y entregada a los tres:
        // dentro de plazo.
        HorasLimite = 80m,
        // Cuatro horas dichas por DANI al tomarlo, y el sello lo prueba. Es la fila que enseña para
        // qué sirve el número: se puede contrastar con lo que marcó su cronómetro.
        HorasEstimadas = 4m,
        HorasEstimadasEnUtc = tomaDaniCerrada,
        Status = PoolActivityStatus.Aceptada,
        ClaimedByDeveloperId = b.Dani.Id,
        ClaimedAt = tomaDaniCerrada,
        ClaimDeadlineAt = tomaDaniCerrada.AddHours(80),
        DeliveredAt = entregaDaniCerrada,
        ReviewedByUserId = lider,
        ReviewedAt = aceptacionDani,
        // Sin criterios extra: la mayoría de las actividades no los llevan, y conviene que se vea
        // cómo se ve una aceptación normal al lado de la que sí cobró de más.
        ReviewHistory = HistorialDeDemo(
            (entregaDaniCerrada, $"Entregada por {b.Dani.FullName} para verificación."),
            (aceptacionDani, $"Aceptada por {b.Lider.FullName}: +{Celda(PoolWorkType.Bug, PoolComplexity.Baja).Points} pts.")),
        CreatedByUserId = lider,
        CreatedAt = ahora.AddDays(-28)
    };

    // ── Retirada del pool ────────────────────────────────────────────────────

    var retiroLogo = ahora.AddDays(-7);
    var retirada = new PoolActivity
    {
        Title = "Cambiar el logotipo del ticket de venta",
        Description = "Mercadotecnia pidió el logotipo nuevo en el ticket impreso y en el PDF.",
        WorkType = PoolWorkType.Tarea,
        Complexity = PoolComplexity.Baja,
        Points = Celda(PoolWorkType.Tarea, PoolComplexity.Baja).Points,
        Priority = PoolPriority.Baja,
        // Se publicó como tarea, así que llevaba la estimación del líder. Se conserva aunque esté
        // retirada: es lo que se dijo que costaba cuando se publicó.
        HorasEstimadas = 4m,
        HorasEstimadasEnUtc = ahora.AddDays(-15),
        // Se retiró antes de que nadie la tomara, que es la única forma de quitar algo del pool sin
        // dejar a nadie a medias.
        Status = PoolActivityStatus.Retirada,
        ReviewedByUserId = lider,
        ReviewedAt = retiroLogo,
        ReviewHistory = HistorialDeDemo(
            (retiroLogo, $"Retirada por {b.Lider.FullName}: mercadotecnia echó atrás el cambio de imagen.")),
        CreatedByUserId = lider,
        CreatedAt = ahora.AddDays(-15)
    };

    db.PoolActivities.AddRange(
        libreIva, libreCatalogo, libreAntiguedad, librePolizas,
        enCursoAna, vencidaDani, porVerificar, devuelta,
        aceptadaAna, aceptadaDani, retirada);

    // El checklist se copia al TOMARLA, así que solo lo tienen las que alguien tiene o tuvo en las
    // manos. Las que siguen libres no llevan ninguno: el líder todavía puede cambiar la plantilla.
    ChecklistDeDemo(db, enCursoAna, 2, tomaAna, "https://dev.azure.com/soltum/Interno/_git/Portal/pullrequest/588");
    ChecklistDeDemo(db, vencidaDani, 1, tomaDani, "https://dev.azure.com/soltum/Interno/_git/PuntoDeVenta/pullrequest/601");
    ChecklistDeDemo(db, porVerificar, 5, tomaBeto, "https://dev.azure.com/soltum/Interno/_git/Compras/pullrequest/605");
    // Cuatro de cinco: el que falta es justo el que se le devolvió, el de probar las dos rutas.
    ChecklistDeDemo(db, devuelta, 4, tomaCaro, "https://dev.azure.com/soltum/Interno/_git/Ventas/pullrequest/597");
    ChecklistDeDemo(db, aceptadaAna, 4, tomaAnaCerrada, "https://dev.azure.com/soltum/Interno/_git/Nomina/pullrequest/612");
    ChecklistDeDemo(db, aceptadaDani, 5, tomaDaniCerrada, "https://dev.azure.com/soltum/Interno/_git/Portal/pullrequest/574");

    // Hace falta guardar aquí: la entrada de puntos lleva en el comentario el número de la actividad,
    // y ese número no existe hasta que la fila está insertada.
    db.SaveChanges();

    // ── Los puntos que abonaron las dos actividades aceptadas ────────────────
    // Una sola entrada por actividad, con el total ya sumado: es lo que hace que el abono sea
    // atómico y que PoolActivity.PointEntryId pueda ser la guarda contra el doble pago.

    var mesAna = aceptacionAna.ToLocalTime();
    var puntosPoolAna = new PointEntry
    {
        DeveloperId = b.Ana.Id,
        // El criterio «Pool: Tarea» lo siembra PoolSeed; vale 0 por omisión a propósito, porque los
        // puntos los pone la actividad, no el catálogo.
        CriterionId = Crit("Pool: Tarea").Id,
        Points = totalAna,
        Year = mesAna.Year,
        Month = mesAna.Month,
        Date = aceptacionAna,
        Comment = $"Pool #{aceptadaAna.Id}: {aceptadaAna.Title}{desgloseAna}",
        AssignedByUserId = lider,
        ReviewedByUserId = lider,
        ReviewedAt = aceptacionAna,
        ApprovalStatus = PointApprovalStatus.Aprobado,
        EvidenceUrl = "https://dev.azure.com/soltum/Interno/_git/Nomina/pullrequest/612"
    };

    var mesDani = aceptacionDani.ToLocalTime();
    var puntosPoolDani = new PointEntry
    {
        DeveloperId = b.Dani.Id,
        CriterionId = Crit("Pool: Bug").Id,
        Points = aceptadaDani.Points,
        Year = mesDani.Year,
        Month = mesDani.Month,
        Date = aceptacionDani,
        Comment = $"Pool #{aceptadaDani.Id}: {aceptadaDani.Title}",
        AssignedByUserId = lider,
        ReviewedByUserId = lider,
        ReviewedAt = aceptacionDani,
        ApprovalStatus = PointApprovalStatus.Aprobado,
        EvidenceUrl = "https://dev.azure.com/soltum/Interno/_git/Portal/pullrequest/574"
    };

    db.PointEntries.AddRange(puntosPoolAna, puntosPoolDani);
    db.SaveChanges();

    // La traza de vuelta, que es lo que impide que una segunda aceptación duplique los puntos.
    aceptadaAna.PointEntryId = puntosPoolAna.Id;
    aceptadaDani.PointEntryId = puntosPoolDani.Id;

    // ── Puntos de varios meses ───────────────────────────────────────────────

    var hoy = DateTime.Today;
    // Meses hacia atrás SIN salirse del año en curso: si el año acaba de empezar solo hay uno o
    // dos, y el ranking anual no debe llenarse de meses del año pasado.
    var meses = new List<DateTime>();
    for (int n = 0; n < Math.Min(4, hoy.Month); n++) meses.Add(hoy.AddMonths(-n));

    var entradas = new List<PointEntry>();

    PointEntry Nueva(Developer dev, string criterio, int mesAtras, string comentario)
    {
        // Si el año va empezando puede no haber tantos meses hacia atrás: se apila en el más
        // antiguo que sí exista en vez de saltar al año pasado.
        var mes = meses[Math.Min(mesAtras, meses.Count - 1)];
        var c = Crit(criterio);

        // Las 16:00 UTC son media mañana en México: la fecha del calendario coincide con el mes al
        // que se imputa la entrada, que es lo que miran el ranking y las gráficas.
        int dia = Math.Max(1, mes.Day - entradas.Count % 5);
        var fecha = new DateTime(mes.Year, mes.Month, dia, 16, 0, 0, DateTimeKind.Utc);

        return new PointEntry
        {
            DeveloperId = dev.Id,
            CriterionId = c.Id,
            // Copiados del criterio y congelados aquí, igual que hace la aplicación: editar el
            // catálogo no debe revaluar hacia atrás lo que ya se anotó.
            Points = c.DefaultPoints,
            Year = mes.Year,
            Month = mes.Month,
            Date = fecha,
            Comment = comentario
        };
    }

    // La revisión ocurre después de la actividad, pero nunca en el futuro: en el mes en curso la
    // actividad puede ser de anteayer, y sumarle días la mandaría a mañana.
    DateTime YaPasado(DateTime fecha) => fecha < ahora ? fecha : ahora.AddHours(-4);

    // La asigna el líder: nace aprobada y sin autor desarrollador.
    void Asignada(Developer dev, string criterio, int mesAtras, string comentario)
    {
        var e = Nueva(dev, criterio, mesAtras, comentario);
        e.AssignedByUserId = lider;
        e.ApprovalStatus = PointApprovalStatus.Aprobado;
        entradas.Add(e);
    }

    // Autocalificación recién registrada: espera al líder y todavía NO cuenta en el ranking.
    void PorRevisar(Developer dev, string criterio, int mesAtras, string comentario,
                    int minutos, string? evidencia = null)
    {
        var e = Nueva(dev, criterio, mesAtras, comentario);
        e.SubmittedByDeveloperId = dev.Id;
        e.ApprovalStatus = PointApprovalStatus.Pendiente;
        e.MinutesSpent = minutos;
        e.EvidenceUrl = evidencia;
        entradas.Add(e);
    }

    // Autocalificación que el líder ya aprobó.
    void Aprobada(Developer dev, string criterio, int mesAtras, string comentario,
                  int minutos, string? evidencia = null)
    {
        var e = Nueva(dev, criterio, mesAtras, comentario);
        e.SubmittedByDeveloperId = dev.Id;
        e.ApprovalStatus = PointApprovalStatus.Aprobado;
        e.MinutesSpent = minutos;
        e.EvidenceUrl = evidencia;
        e.ReviewedByUserId = lider;
        e.ReviewedAt = YaPasado(e.Date.AddDays(1));
        entradas.Add(e);
    }

    // Rechazada de primera vuelta: el motivo vive en su propio campo y todavía no hay historial,
    // porque no hay conversación que reconstruir.
    void Rechazada(Developer dev, string criterio, int mesAtras, string comentario,
                   int minutos, string motivo)
    {
        var e = Nueva(dev, criterio, mesAtras, comentario);
        e.SubmittedByDeveloperId = dev.Id;
        e.ApprovalStatus = PointApprovalStatus.Rechazado;
        e.MinutesSpent = minutos;
        e.ReviewedByUserId = lider;
        e.ReviewedAt = YaPasado(e.Date.AddDays(1));
        e.ReviewComment = motivo;
        entradas.Add(e);
    }

    // Rechazada y replicada: vuelve a Pendiente con la vuelta contada y las dos mitades de la
    // conversación en el historial. ReviewComment queda en null a propósito —dejarlo pegado haría
    // creer que ya la volvieron a responder—, y por eso el motivo se guarda antes en el historial.
    void Replicada(Developer dev, string criterio, int mesAtras, string comentario,
                   int minutos, string motivo, string replica)
    {
        var e = Nueva(dev, criterio, mesAtras, comentario);
        e.SubmittedByDeveloperId = dev.Id;
        e.ApprovalStatus = PointApprovalStatus.Pendiente;
        e.MinutesSpent = minutos;
        e.ReviewRound = 1;
        var replicaEn = YaPasado(e.Date.AddDays(2));
        var rechazoEn = replicaEn.AddHours(-30);   // el rechazo va siempre ANTES que la réplica
        e.ReviewHistory = HistorialDeDemo(
            (rechazoEn, $"Rechazada: {motivo}"),
            (replicaEn, $"Réplica de {dev.FullName}: {replica}"));
        entradas.Add(e);
    }

    // Ana: senior de Plataforma, la que sostiene los despliegues.
    Asignada(b.Ana, "Lideraste técnicamente (Senior)", 0,
        "Marcó el rumbo del cambio de la capa de despliegue y destrabó al equipo dos veces esta semana.");
    PorRevisar(b.Ana, "Ayudaste a un compañero", 0,
        "Me senté con Daniel a revisar el corte de caja duplicado hasta que lo entendió.", 90);
    Aprobada(b.Ana, "Automatizaste algo repetitivo", 1,
        "El respaldo de nómina ya corre solo cada noche; antes se hacía a mano los viernes.", 240,
        "https://dev.azure.com/soltum/Interno/_git/Nomina/pullrequest/612");
    Asignada(b.Ana, "Entregaste a tiempo", 2,
        "La liberación del portal salió el día comprometido, sin pedir prórroga.");
    Asignada(b.Ana, "Resolviste un incidente crítico", 3,
        "Sacó el timbrado que se cayó un viernes a las siete de la tarde.");

    // Beto: mid de Plataforma, en crecimiento y con un tropiezo a la vista.
    PorRevisar(b.Beto, "Hiciste una buena revisión de código", 0,
        "Revisé el PR de proveedores extranjeros el mismo día y dejé comentarios de fondo.", 45,
        "https://dev.azure.com/soltum/Interno/_git/Compras/pullrequest/605");
    Aprobada(b.Beto, "Corregiste un bug reportado", 1,
        "El reporte de comisiones ya no duplica los renglones de las sucursales foráneas.", 180);
    Rechazada(b.Beto, "Propusiste una mejora por tu cuenta", 1,
        "Propuse pasar los reportes a una vista materializada.", 120,
        "La idea está bien, pero se quedó en la propuesta: cuando la implementes y midas la mejora, la anotamos.");
    Asignada(b.Beto, "Entregaste tarde", 2,
        "El módulo de conciliación salió cuatro días después de lo comprometido.");
    Asignada(b.Beto, "Te hiciste cargo de un módulo (Mid)", 3,
        "Tomó el módulo de proveedores completo y respondió por él de principio a fin.");

    // Caro: senior de Producto, con una discusión abierta con el líder.
    Asignada(b.Caro, "Atendiste bien a un cliente", 0,
        "El corporativo pidió el reporte de antigüedad y quedó conforme con cómo se le explicó el alcance.");
    Replicada(b.Caro, "Diseñaste una buena arquitectura (Senior)", 0,
        "Rediseñé el manejo de folios para que la concurrencia deje de perder remisiones.", 300,
        "Todavía no está entregado: cuando pase la verificación lo revisamos.",
        "El diseño ya está aprobado y documentado, y lo que falta es la ruta de la app móvil, " +
        "que es implementación. Pido que se evalúe el diseño, que es lo que se anotó.");
    Asignada(b.Caro, "Acompañaste a alguien nuevo", 1,
        "Se hizo cargo de la inducción de Daniel en el módulo de ventas.");
    Aprobada(b.Caro, "Cuidaste la seguridad", 2,
        "Saqué las cadenas de conexión del appsettings y las pasé a los secretos del servidor.", 150);
    Asignada(b.Caro, "Se te fue un bug a producción", 3,
        "El cambio de folios dejó fuera la app móvil y se detectó en producción.");

    // Dani: junior de Producto, con lo bueno y lo que le falta.
    PorRevisar(b.Dani, "Terminaste una capacitación", 0,
        "Terminé el curso de SQL Server para desarrolladores; adjunto la constancia.", 480);
    Asignada(b.Dani, "Aprendiste rápido algo nuevo (Junior)", 1,
        "En dos semanas se puso al día con Blazor y ya entrega pantallas completas.");
    Rechazada(b.Dani, "Resolviste una tarea casi solo (Junior)", 1,
        "Saqué el ajuste del catálogo de bancos.", 200,
        "En esa tarea me preguntaste tres veces y el PR lo terminamos juntos: no aplica todavía. " +
        "La siguiente sale sola y la anotamos con gusto.");
    Asignada(b.Dani, "Dejaste tareas arrastrando", 2,
        "Cerró el sprint con dos tareas comprometidas sin terminar.");
    Asignada(b.Dani, "Llegaste al daily", 3,
        "Asistencia completa al daily durante todo el mes.");

    db.PointEntries.AddRange(entradas);

    // ── Evaluaciones e hitos ─────────────────────────────────────────────────
    // Dos por persona y de periodos distintos, para que la ficha enseñe evolución y no una foto.

    var evalReciente = hoy.AddDays(-20);
    var evalAnterior = hoy.AddMonths(-4);

    DeveloperEvaluation Evaluacion(Developer dev, DateTime fecha, int calificacion,
                                   string fortalezas, string debilidades, string comentarios) => new()
    {
        DeveloperId = dev.Id,
        EvaluatorUserId = lider,
        // El nombre se copia tal como estaba al evaluar: el reporte en PDF tiene que seguir
        // diciendo quién firmó aunque esa cuenta cambie o se dé de baja.
        EvaluatorName = b.Lider.FullName,
        EvaluationDate = fecha,
        PeriodLabel = $"{fecha.Year} Q{(fecha.Month - 1) / 3 + 1}",
        OverallRating = calificacion,
        Strengths = fortalezas,
        Weaknesses = debilidades,
        Comments = comentarios,
        CreatedAt = fecha.AddHours(18)
    };

    db.DeveloperEvaluations.AddRange(
        Evaluacion(b.Ana, evalReciente, 5,
            "Se hace cargo de los despliegues de punta a punta y es la única que saca una liberación " +
            "con el FTP dando problemas. Documenta lo que arregla, y eso se nota cuando le toca a alguien más.",
            "Concentra demasiado: cuando ella no está, la liberación se detiene. Le cuesta soltar " +
            "tareas que sabe hacer más rápido que quien las tendría que aprender.",
            "Acordamos que este trimestre acompañe a Beto en dos liberaciones completas en lugar de hacerlas ella."),
        Evaluacion(b.Ana, evalAnterior, 4,
            "Sostuvo sola la migración del portal sin que se cayera una sola liberación.",
            "Trabajó de más varias semanas seguidas y llegó al cierre agotada.",
            "Se le pidió repartir la guardia; quedó de revisarlo con el equipo."),

        Evaluacion(b.Beto, evalReciente, 4,
            "Mejoró mucho en revisiones de código: llega a tiempo y sus comentarios son de fondo, no de estilo. " +
            "Ya se hace cargo del módulo de proveedores sin que nadie esté encima.",
            "Estima corto. Comprometió cuatro días para la conciliación y le llevó ocho, y no avisó " +
            "hasta que ya iba tarde.",
            "Va bien encaminado a senior. Lo que falta es la estimación y avisar a tiempo cuando algo se atora."),
        Evaluacion(b.Beto, evalAnterior, 3,
            "Aprende rápido y no le saca la vuelta a lo que no conoce.",
            "Entregaba sin probar sus propios cambios y QA se lo devolvía dos de cada tres veces.",
            "Se acordó que nada sale a QA sin una pasada propia; en el trimestre siguiente ya se notó."),

        Evaluacion(b.Caro, evalReciente, 5,
            "Es la que mejor traduce lo que pide el negocio a algo que se puede construir. El corporativo " +
            "la busca a ella directamente, y eso nos ahorra dos vueltas en cada requerimiento.",
            "Defiende sus diseños de más: en la discusión de los folios costó que revisara la ruta de " +
            "la app móvil, que era donde estaba el hueco.",
            "Se le propuso llevar la inducción técnica de los que entren el semestre que viene."),
        Evaluacion(b.Caro, evalAnterior, 4,
            "Cerró el trimestre sin arrastrar nada y con la documentación al día.",
            "Se guardó dos bloqueos que hubieran movido la fecha si se avisaban a tiempo.",
            "Quedó en levantar la mano en el daily en cuanto vea el riesgo, no cuando ya no haya salida."),

        Evaluacion(b.Dani, evalReciente, 3,
            "Pregunta en el momento correcto y no repite el mismo error dos veces. Su primer despliegue " +
            "a producción lo hizo solo y sin incidentes.",
            "Todavía arrastra tareas de un sprint a otro y le cuesta decir cuándo va a terminar algo.",
            "Es su primer año. Lo que se le pide para el trimestre que viene es cerrar lo que se compromete, " +
            "aunque comprometa menos."),
        Evaluacion(b.Dani, evalAnterior, 3,
            "Se puso al día con Blazor en dos semanas y ya entrega pantallas completas.",
            "Se atascaba días enteros sin pedir ayuda.",
            "Se acordó la regla de las dos horas: si algo no avanza en ese rato, se pregunta."));

    db.DeveloperMilestones.AddRange(
        new DeveloperMilestone
        {
            DeveloperId = b.Ana.Id,
            Title = "Migración del portal de clientes a .NET 8",
            Description = "Coordinó la migración completa sin interrumpir el servicio: se hizo por " +
                          "módulos, en tres liberaciones nocturnas.",
            Date = hoy.AddMonths(-3),
            Kind = MilestoneKind.Proyecto,
            CreatedByUserId = lider
        },
        new DeveloperMilestone
        {
            DeveloperId = b.Ana.Id,
            Title = "Reconocimiento del área de finanzas por el cierre anual",
            Description = "Finanzas cerró el ejercicio sin un solo incidente de timbrado.",
            Date = hoy.AddMonths(-6),
            Kind = MilestoneKind.Reconocimiento,
            CreatedByUserId = lider
        },
        new DeveloperMilestone
        {
            DeveloperId = b.Beto.Id,
            Title = "Certificación AZ-204 (Desarrollo en Azure)",
            Description = "La presentó por su cuenta y la aprobó en el primer intento.",
            Date = hoy.AddMonths(-2),
            Kind = MilestoneKind.Certificacion,
            CreatedByUserId = lider
        },
        new DeveloperMilestone
        {
            DeveloperId = b.Caro.Id,
            Title = "Un trimestre completo sin incidentes en facturación",
            Description = "El sistema de facturación no tuvo un solo incidente en producción durante el trimestre.",
            Date = hoy.AddMonths(-1),
            Kind = MilestoneKind.Logro,
            CreatedByUserId = lider
        },
        new DeveloperMilestone
        {
            DeveloperId = b.Dani.Id,
            Title = "Primer despliegue a producción por su cuenta",
            Description = "Hizo el despliegue de punta a punta siguiendo la lista de verificación, sin acompañamiento.",
            Date = hoy.AddDays(-40),
            Kind = MilestoneKind.Logro,
            CreatedByUserId = lider
        });

    db.SaveChanges();
}

/// <summary>
/// Un criterio extra de una actividad de demostración. Copia el nombre y los puntos del catálogo
/// —congelados, como hace la aplicación al publicar— y deja <c>IsMet</c> en null mientras nadie lo
/// haya evaluado.
/// </summary>
private static PoolActivityExtraCriterion CriterioExtraDeDemo(
    ScoringCriterion criterio, bool? cumplido = null, DateTime? evaluadoUtc = null,
    string? comentario = null) => new()
{
    // Se guarda el Id del catálogo solo como traza. Va siempre con valor porque la base tiene un
    // índice único por (actividad, criterio) y dos criterios en null dentro de la misma actividad
    // chocarían entre sí en SQL Server.
    ScoringCriterionId = criterio.Id,
    Name = criterio.Name,
    Points = criterio.DefaultPoints,
    IsMet = cumplido,
    EvaluatedAtUtc = evaluadoUtc,
    Comment = comentario
};

/// <summary>
/// Copia el checklist de la plantilla del tipo de la actividad —igual que hace el servicio al
/// tomarla— y marca los primeros <paramref name="marcados"/> puntos.
///
/// <para>Los textos se leen de la base en lugar de escribirlos aquí: así la demostración enseña
/// exactamente la plantilla que el líder tiene configurada, y no una copia que se quede vieja en
/// cuanto alguien la edite.</para>
/// </summary>
private static void ChecklistDeDemo(
    AppDbContext db, PoolActivity actividad, int marcados, DateTime tomadaUtc, string evidenciaUrl)
{
    var plantilla = db.PoolChecklistTemplateItems
        .Where(t => t.WorkType == actividad.WorkType && t.IsActive)
        .OrderBy(t => t.Orden)
        .ToList();

    for (int i = 0; i < plantilla.Count; i++)
    {
        var punto = plantilla[i];
        bool hecho = i < marcados;
        db.PoolActivityChecklistItems.Add(new PoolActivityChecklistItem
        {
            // Por la navegación y no por el Id: la actividad todavía no está guardada y EF rellena
            // la clave foránea al insertar.
            Activity = actividad,
            Text = punto.Text,
            Orden = punto.Orden,
            RequiereEvidencia = punto.RequiereEvidencia,
            IsDone = hecho,
            // Separados en el tiempo: un checklist entero marcado en el mismo minuto se ve sembrado,
            // y la idea es que parezca trabajo de varios días.
            DoneAtUtc = hecho ? tomadaUtc.AddHours(6 * (i + 1)) : null,
            EvidenceUrl = hecho && punto.RequiereEvidencia ? evidenciaUrl : null
        });
    }
}

/// <summary>
/// Arma un historial de revisión con el mismo formato que escriben los servicios: una línea por
/// anotación, fechada en hora local y en orden. Las fechas entran en UTC, como todo lo demás.
/// </summary>
private static string HistorialDeDemo(params (DateTime CuandoUtc, string Texto)[] lineas) =>
    string.Join("\n", lineas.Select(l => $"[{l.CuandoUtc.ToLocalTime():dd/MM/yyyy HH:mm}] {l.Texto}"));


    // ═══ AUSENCIAS-JORNADA ═══

    // ── Ausencias y jornada ──────────────────────────────────────────────────────

    /// <summary>
    /// Vacaciones, permisos, asistencia oficial, telemetría de presencia, actividades libres y
    /// cronómetro.
    ///
    /// <para>Las cuatro tablas de la jornada se siembran JUNTAS y en el mismo bucle porque la
    /// pantalla «Mi jornada» vive de contrastarlas: lo marcado a mano, lo que la aplicación vio sola
    /// y lo que el cronómetro registró. Sembradas por separado darían tres columnas que no se
    /// parecen en nada y el contraste no enseñaría lo que existe para enseñar.</para>
    /// </summary>
    public static void SembrarAusenciasYJornada(AppDbContext db, DatosBase b)
    {
        var ahora = DateTime.UtcNow;

        // Semilla fija: los horarios varían de un día a otro y de una persona a otra, pero la demo
        // se ve IGUAL en todas las máquinas. Si no, cada quien reportaría una pantalla distinta.
        var rnd = new Random(20260805);

        // Hora local de un día hábil, convertida a UTC. Todo lo de asistencia y presencia se guarda
        // en UTC, pero se piensa en local: «entró a las 8:40» es local, y quien lo lee lo vuelve a
        // convertir. Hacer la cuenta a mano con horas restadas rompería en cuanto cambiara el
        // horario de verano.
        static DateTime UtcDe(DateTime diaLocal, int hora, int minuto) =>
            DateTime.SpecifyKind(diaLocal.Date.AddHours(hora).AddMinutes(minuto), DateTimeKind.Local)
                    .ToUniversalTime();

        static DateTime ProximoLunes(DateTime desde)
        {
            var d = desde.Date;
            while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
            return d;
        }

        static DateTime ProximoHabil(DateTime desde)
        {
            var d = desde.Date;
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
            return d;
        }

        // Los 15 últimos días hábiles: [0] es el más reciente y [14] el más viejo. Los índices se
        // usan más abajo para colocar los casos especiales en días concretos y que todo cuadre
        // entre sí (el permiso de incapacidad con el hueco de asistencia, por ejemplo).
        var dias = Enumerable.Range(1, 15).Select(DiaHabilAtras).ToList();

        // ── Vacaciones: los cuatro estados de VacationStatus ─────────────────────
        var vacaciones = new List<VacationRequest>
        {
            // Pendiente: es la que le sale al líder en su bandeja para resolver.
            new()
            {
                DeveloperId = b.Dani.Id,
                StartDate   = ProximoLunes(DateTime.Today.AddDays(26)),
                EndDate     = ProximoLunes(DateTime.Today.AddDays(26)).AddDays(4),
                Status      = VacationStatus.Pendiente,
                Comment     = "Es la semana de la boda de mi hermana en Guadalajara.",
                CreatedAt   = ahora.AddDays(-2)
            },
            // Otra pendiente, pero corta: dos pendientes hacen que la bandeja se vea como una lista
            // y no como un caso suelto.
            new()
            {
                DeveloperId = b.Beto.Id,
                StartDate   = ProximoHabil(DateTime.Today.AddDays(13)),
                EndDate     = ProximoHabil(DateTime.Today.AddDays(13)).AddDays(1),
                Status      = VacationStatus.Pendiente,
                Comment     = "Dos días para el trámite del pasaporte de los niños.",
                CreatedAt   = ahora.AddHours(-30)
            },
            // Aprobada y YA DISFRUTADA: es el histórico, la fila que se ve en gris.
            new()
            {
                DeveloperId   = b.Beto.Id,
                StartDate     = ProximoLunes(DateTime.Today.AddDays(-48)),
                EndDate       = ProximoLunes(DateTime.Today.AddDays(-48)).AddDays(4),
                Status        = VacationStatus.Aprobada,
                Comment       = "Semana de playa con la familia.",
                ReviewComment = "Va. Ana se queda con la guardia de despliegues esa semana.",
                ReviewedById  = b.Lider.Id,
                ReviewedAt    = ahora.AddDays(-62),
                CreatedAt     = ahora.AddDays(-70)
            },
            // Aprobada FUTURA: la que sirve para saber quién no va a estar la próxima quincena.
            new()
            {
                DeveloperId   = b.Ana.Id,
                StartDate     = ProximoLunes(DateTime.Today.AddDays(19)),
                EndDate       = ProximoLunes(DateTime.Today.AddDays(19)).AddDays(2),
                Status        = VacationStatus.Aprobada,
                Comment       = "Tres días para ir a la graduación de mi hijo.",
                ReviewComment = "Aprobado. Deja documentado el proceso del respaldo nocturno antes de irte.",
                ReviewedById  = b.Lider.Id,
                ReviewedAt    = ahora.AddDays(-5),
                CreatedAt     = ahora.AddDays(-8)
            },
            // Rechazada CON MOTIVO: el motivo es lo único que el solicitante llega a leer, así que
            // no puede ser una línea vacía.
            new()
            {
                DeveloperId   = b.Caro.Id,
                StartDate     = ProximoLunes(DateTime.Today.AddDays(9)),
                EndDate       = ProximoLunes(DateTime.Today.AddDays(9)).AddDays(4),
                Status        = VacationStatus.Rechazada,
                Comment       = "Quiero aprovechar el puente.",
                ReviewComment = "Esa semana cae el cierre de nómina y eres la única que conoce el "
                              + "proceso de facturación. Recórrelas a la siguiente quincena y te las autorizo.",
                ReviewedById  = b.Lider.Id,
                ReviewedAt    = ahora.AddDays(-1),
                CreatedAt     = ahora.AddDays(-4)
            },
            // Cancelada POR EL SOLICITANTE: se anota en ReviewComment y NO en ReviewedById, igual
            // que hace VacationRequestService.CancelarAsync — llenar ese campo haría pasar una
            // cancelación propia por una decisión del líder.
            new()
            {
                DeveloperId   = b.Caro.Id,
                StartDate     = ProximoLunes(DateTime.Today.AddDays(33)),
                EndDate       = ProximoLunes(DateTime.Today.AddDays(33)).AddDays(4),
                Status        = VacationStatus.Cancelada,
                Comment       = "Viaje a Mérida.",
                ReviewComment = $"Cancelada por {b.UsuarioCaro.Username} el "
                              + $"{DateTime.Now.AddDays(-3):dd/MM/yyyy HH:mm}: se pospuso el viaje.",
                CreatedAt     = ahora.AddDays(-11)
            }
        };
        db.VacationRequests.AddRange(vacaciones);

        // ── Permisos: variedad de tipo Y de estado ───────────────────────────────
        var permisos = new List<LeaveRequest>
        {
            // Aprobada y ya ocurrida. La FECHA es la misma en la que más abajo Ana entra a las
            // 11:40 con nota: el permiso y el marcaje tienen que contar la misma historia.
            new()
            {
                DeveloperId             = b.Ana.Id,
                Type                    = LeaveType.CitaMedica,
                Date                    = dias[9],
                DaysCount               = 1,
                Reason                  = "Cita de control en el IMSS a las 9:00; entro después.",
                Status                  = LeaveStatus.Aprobada,
                RequestedByDeveloperId  = b.Ana.Id,
                ApprovedBy              = b.Lider.Username,
                ReviewedById            = b.Lider.Id,
                ReviewedAt              = ahora.AddDays(-15),
                ReviewComment           = "Sin problema, avisa cuando llegues.",
                CreatedAt               = ahora.AddDays(-17)
            },
            // Capturada POR EL ADMINISTRADOR: RequestedByDeveloperId en null y ApprovedBy con el
            // texto libre de siempre. Es el caso heredado de la libreta, y conviene que se vea uno.
            new()
            {
                DeveloperId  = b.Beto.Id,
                Type         = LeaveType.Incapacidad,
                Date         = dias[7],
                // Se calcula para que el permiso termine EXACTAMENTE en dias[5]: si los tres días
                // hábiles cruzan un fin de semana, un DaysCount fijo de 3 dejaría el permiso corto
                // y el hueco de asistencia sin justificar.
                DaysCount    = (dias[5] - dias[7]).Days + 1,
                Reason       = "Incapacidad por gripe; entregó la constancia del IMSS en físico.",
                Status       = LeaveStatus.Aprobada,
                ApprovedBy   = "Recursos Humanos (constancia en el expediente)",
                ReviewedById = b.Lider.Id,
                ReviewedAt   = ahora.AddDays(-13),
                Notes        = "Se avisó por teléfono el mismo lunes a primera hora.",
                CreatedAt    = ahora.AddDays(-13)
            },
            // Pendiente y FUTURA: la que está esperando respuesta ahora mismo.
            new()
            {
                DeveloperId            = b.Caro.Id,
                Type                   = LeaveType.PermisoPersonal,
                Date                   = ProximoHabil(DateTime.Today.AddDays(4)),
                DaysCount              = 1,
                Reason                 = "Trámite en el banco para el crédito de la casa; solo la mañana.",
                Status                 = LeaveStatus.Pendiente,
                RequestedByDeveloperId = b.Caro.Id,
                CreatedAt              = ahora.AddHours(-19)
            },
            new()
            {
                DeveloperId            = b.Dani.Id,
                Type                   = LeaveType.CitaMedica,
                Date                   = ProximoHabil(DateTime.Today.AddDays(2)),
                DaysCount              = 1,
                Reason                 = "Cita con el dentista; salgo a las 16:00.",
                Status                 = LeaveStatus.Pendiente,
                RequestedByDeveloperId = b.Dani.Id,
                CreatedAt              = ahora.AddHours(-5)
            },
            // Rechazada: choca con la salida a producción, y el motivo lo dice.
            new()
            {
                DeveloperId            = b.Dani.Id,
                Type                   = LeaveType.AsuntoFamiliar,
                Date                   = ProximoHabil(DateTime.Today.AddDays(6)),
                DaysCount              = 2,
                Reason                 = "Acompañar a mi mamá a Puebla.",
                Status                 = LeaveStatus.Rechazada,
                RequestedByDeveloperId = b.Dani.Id,
                ReviewedById           = b.Lider.Id,
                ReviewedAt             = ahora.AddHours(-9),
                ReviewComment          = "Esos dos días es la salida a producción del módulo de "
                                       + "inventarios y eres quien hizo las pantallas. Si lo mueves una semana, va.",
                CreatedAt              = ahora.AddDays(-2)
            },
            // Aprobada futura, de capacitación: sirve para que la vista del mes no sea solo faltas.
            new()
            {
                DeveloperId            = b.Ana.Id,
                Type                   = LeaveType.Capacitacion,
                Date                   = ProximoHabil(DateTime.Today.AddDays(11)),
                DaysCount              = 2,
                Reason                 = "Curso de afinación de SQL Server (en línea, pagado por la empresa).",
                Status                 = LeaveStatus.Aprobada,
                RequestedByDeveloperId = b.Ana.Id,
                ApprovedBy             = b.Lider.Username,
                ReviewedById           = b.Lider.Id,
                ReviewedAt             = ahora.AddDays(-6),
                ReviewComment          = "Aprobado. Nos platicas lo que veas en la junta de equipo.",
                Notes                  = "Son 8 horas repartidas en dos mañanas.",
                CreatedAt              = ahora.AddDays(-9)
            },
            // Cancelada por el propio solicitante, con la misma convención que las vacaciones.
            new()
            {
                DeveloperId            = b.Beto.Id,
                Type                   = LeaveType.Otro,
                Date                   = dias[2],
                DaysCount              = 1,
                Reason                 = "Entrega de la mudanza; me tienen que abrir a mí.",
                Status                 = LeaveStatus.Cancelada,
                RequestedByDeveloperId = b.Beto.Id,
                ReviewComment          = $"Cancelada por {b.UsuarioBeto.Username} el "
                                       + $"{DateTime.Now.AddDays(-6):dd/MM/yyyy HH:mm}: la mudanza llegó el sábado.",
                CreatedAt              = ahora.AddDays(-8)
            }
        };
        db.LeaveRequests.AddRange(permisos);

        // ── Actividades libres (lo que no cuelga de ningún requerimiento) ────────
        var anaSoporte = new DevActivity
        {
            DeveloperId = b.Ana.Id,
            Title       = "Soporte a Contabilidad por el cierre de mes",
            Description = "Consultas y ajustes a mano mientras dura el cierre. No es de ningún "
                        + "requerimiento y se cronometra aparte para saber cuánto se va en esto.",
            Status      = DevActivityStatus.Abierta,
            CreatedAt   = ahora.AddDays(-21)
        };
        var anaRespaldo = new DevActivity
        {
            DeveloperId = b.Ana.Id,
            Title       = "Mover el respaldo nocturno al NAS nuevo",
            Description = "Reapuntar la tarea programada y verificar dos noches seguidas que el "
                        + "archivo llegue completo.",
            Status      = DevActivityStatus.Cerrada,
            CreatedAt   = ahora.AddDays(-24),
            ClosedAt    = ahora.AddDays(-7)
        };
        var betoGuardia = new DevActivity
        {
            DeveloperId = b.Beto.Id,
            Title       = "Guardia de despliegues por FTP",
            Description = "Subidas fuera de horario y verificación de que el sitio quedó arriba.",
            Status      = DevActivityStatus.Abierta,
            CreatedAt   = ahora.AddDays(-19)
        };
        var betoCorreos = new DevActivity
        {
            DeveloperId = b.Beto.Id,
            Title       = "Investigar por qué se atora el envío de correos de facturación",
            Description = "Era la cola del servidor de correo, no el sistema. Quedó documentado en el foro.",
            Status      = DevActivityStatus.Cerrada,
            CreatedAt   = ahora.AddDays(-16),
            ClosedAt    = ahora.AddDays(-10)
        };
        var caroJunta = new DevActivity
        {
            DeveloperId = b.Caro.Id,
            Title       = "Seguimiento semanal con Operaciones",
            Description = "Junta de los martes y los pendientes que salen de ahí.",
            Status      = DevActivityStatus.Abierta,
            CreatedAt   = ahora.AddDays(-30)
        };
        var caroCapacitacion = new DevActivity
        {
            DeveloperId = b.Caro.Id,
            Title       = "Capacitar a sucursales en el módulo de inventarios",
            Description = "Tres sesiones por videollamada, más el manual corto en PDF.",
            Status      = DevActivityStatus.Abierta,
            CreatedAt   = ahora.AddDays(-12)
        };
        var daniTickets = new DevActivity
        {
            DeveloperId = b.Dani.Id,
            Title       = "Depurar los tickets viejos de Freshdesk",
            Description = "Cerrar lo que ya se resolvió y no se cerró. Salieron 40 y quedaron 6 vivos.",
            Status      = DevActivityStatus.Cerrada,
            CreatedAt   = ahora.AddDays(-14),
            ClosedAt    = ahora.AddDays(-4)
        };
        var daniCertificado = new DevActivity
        {
            DeveloperId = b.Dani.Id,
            Title       = "Apoyo a Plataforma con el cambio de certificado del portal",
            Description = "Aprender el procedimiento junto con Ana para poder hacerlo la próxima vez.",
            Status      = DevActivityStatus.Abierta,
            CreatedAt   = ahora.AddDays(-6)
        };

        var actividades = new List<DevActivity>
        {
            anaSoporte, anaRespaldo, betoGuardia, betoCorreos,
            caroJunta, caroCapacitacion, daniTickets, daniCertificado
        };
        db.DevActivities.AddRange(actividades);

        // Guardado INTERMEDIO a propósito: WorkInterval guarda ActivityId como un número suelto —no
        // tiene FK ni propiedad de navegación, es bitácora histórica— así que EF no puede resolverlo
        // solo y las actividades necesitan tener su Id antes de que se siembren los tramos.
        db.SaveChanges();

        // ── Asistencia, telemetría y cronómetro de los últimos 15 días hábiles ───
        var gente = new[]
        {
            (Dev: b.Ana,  Cuenta: b.UsuarioAna,  Equipo: "PC-ANA-01",   Hora: 8, Minutos: 515),
            (Dev: b.Beto, Cuenta: b.UsuarioBeto, Equipo: "LAP-BETO-02", Hora: 9, Minutos: 495),
            (Dev: b.Caro, Cuenta: b.UsuarioCaro, Equipo: "PC-CARO-03",  Hora: 8, Minutos: 530),
            (Dev: b.Dani, Cuenta: b.UsuarioDani, Equipo: "LAP-DANI-04", Hora: 9, Minutos: 480)
        };

        var notasDeSesion = new[]
        {
            "Se detuvo al terminar la jornada.",
            "Pausado por la junta diaria.",
            "Interrumpido por una llamada de soporte.",
            null
        };

        var asistencia = new List<AttendanceRecord>();
        var telemetria = new List<WorkPresence>();
        var sesiones   = new List<WorkSession>();
        var tramos     = new List<WorkInterval>();

        for (int p = 0; p < gente.Length; p++)
        {
            var (dev, cuenta, equipo, horaBase, minutosBase) = gente[p];
            var suyas = actividades.Where(a => a.DeveloperId == dev.Id).ToList();

            for (int i = 0; i < dias.Count; i++)
            {
                var dia = dias[i];

                // Beto estuvo incapacitado tres días: sin marca y sin telemetría. El hueco no es un
                // olvido, y por eso arriba existe el permiso de Incapacidad que cae en estos días.
                if (p == 1 && i is 5 or 6 or 7) continue;

                bool sinMarcar      = p == 0 && i == 4;   // Ana trabajó y se le olvidó marcar por completo
                bool olvidoSalida   = p == 1 && i == 2;   // Beto marcó entrada y nunca salida
                bool pideCorreccion = p == 3 && i == 1;   // Dani se equivocó de botón y pide que se lo arreglen
                bool yaCorregido    = p == 2 && i == 6;   // Caro: el líder ya le ajustó la hora
                bool citaMedica     = p == 0 && i == 9;   // Ana entra tarde, con el permiso ya autorizado
                bool jornadaPartida = p == 2 && i == 3;   // Caro cerró la aplicación al mediodía y la reabrió

                // Uno de cada siete días a alguien se le apaga el equipo sin cerrar sesión. Es lo que
                // llena la columna de «sin señal» y lo que justifica que exista PresenceEnd.SinLatido.
                bool sinLatido = (p + i) % 7 == 3;

                var entrada = citaMedica
                    ? UtcDe(dia, 11, 40)
                    : UtcDe(dia, horaBase, 5 + rnd.Next(0, 50));

                var salida = citaMedica
                    ? entrada.AddMinutes(320)
                    : entrada.AddMinutes(minutosBase + rnd.Next(-35, 55));

                // La telemetría NUNCA cuadra con lo marcado: unos abren la aplicación antes de
                // marcar y casi todos la cierran antes de irse. Ese desfase es justo lo que la
                // columna de contraste existe para enseñar, así que se siembra a propósito.
                var iniPresencia = entrada.AddMinutes(rnd.Next(-22, 14));
                var finPresencia = sinLatido
                    ? salida.AddMinutes(-rnd.Next(65, 150))   // se apagó el equipo mucho antes
                    : salida.AddMinutes(-rnd.Next(4, 40));

                if (jornadaPartida)
                {
                    // Dos jornadas el mismo día: cerró la aplicación para comer y la volvió a abrir.
                    // La pantalla las suma; sin un caso así nadie sabría que eso puede pasar.
                    var corteAM = entrada.AddMinutes(215);
                    telemetria.Add(new WorkPresence
                    {
                        UserId = cuenta.Id, DeveloperId = dev.Id, DisplayName = cuenta.FullName,
                        StartedAtUtc = iniPresencia, EndedAtUtc = corteAM, LastSeenUtc = corteAM,
                        State = PresenceState.Comiendo, EndReason = PresenceEnd.CierreNormal,
                        Origin = equipo
                    });
                    telemetria.Add(new WorkPresence
                    {
                        UserId = cuenta.Id, DeveloperId = dev.Id, DisplayName = cuenta.FullName,
                        StartedAtUtc = corteAM.AddMinutes(52), EndedAtUtc = finPresencia,
                        LastSeenUtc = finPresencia, State = PresenceState.Disponible,
                        EndReason = PresenceEnd.CierreNormal, Origin = equipo
                    });
                }
                else
                {
                    telemetria.Add(new WorkPresence
                    {
                        UserId = cuenta.Id, DeveloperId = dev.Id, DisplayName = cuenta.FullName,
                        StartedAtUtc = iniPresencia, EndedAtUtc = finPresencia,
                        LastSeenUtc = finPresencia,
                        // Se quedó en «Ausente» y ahí murió el latido: el estado es del momento y
                        // nadie lo regresa a Disponible cuando se le apaga el equipo.
                        State = sinLatido ? PresenceState.Ausente : PresenceState.Disponible,
                        EndReason = sinLatido ? PresenceEnd.SinLatido : PresenceEnd.CierreNormal,
                        Origin = equipo
                    });
                }

                if (!sinMarcar)
                {
                    var registro = new AttendanceRecord
                    {
                        UserId         = cuenta.Id,
                        DeveloperId    = dev.Id,
                        DisplayName    = cuenta.FullName,
                        CheckInUtc     = entrada,
                        CheckInOrigin  = equipo,
                        CheckOutUtc    = salida,
                        CheckOutOrigin = equipo,
                        CloseKind      = AttendanceCloseKind.Manual
                    };

                    if (citaMedica)
                        registro.CheckInNote = "Entré después de la cita médica; el permiso quedó autorizado.";

                    if (olvidoSalida)
                    {
                        // Se cerró solo al día siguiente, estimando la salida con la última señal de
                        // la telemetría de ese día. Por eso la hora coincide exactamente con el fin
                        // de la presencia y no con una hora redonda.
                        registro.CheckOutUtc    = finPresencia;
                        registro.CheckOutOrigin = null;
                        registro.CloseKind      = AttendanceCloseKind.Olvido;
                    }

                    if (pideCorreccion)
                    {
                        // Le dio a «salida» a la hora de la comida. No puede editarse sus propias
                        // horas —el registro dejaría de probar nada—, así que lo que hace es pedirlo.
                        registro.CheckOutUtc              = entrada.AddMinutes(240);
                        registro.CorrectionRequestNote    = "Le di sin querer a marcar salida cuando "
                            + "salí a comer. Me fui hasta las 18:20; el cronómetro de ese día lo comprueba.";
                        registro.CorrectionRequestedAtUtc = ahora.AddHours(-20);
                    }

                    if (yaCorregido)
                    {
                        registro.CheckOutUtc       = salida.AddMinutes(95);
                        registro.CloseKind         = AttendanceCloseKind.Admin;
                        registro.CorrectedByUserId = b.Lider.Id;
                        registro.CorrectedByName   = b.Lider.FullName;
                        registro.CorrectedAtUtc    = ahora.AddDays(-4);
                        registro.CorrectionReason  = "Se quedó a terminar el despliegue de la noche; "
                            + "lo confirma la bitácora del FTP.";
                    }

                    // Un día trabajado desde casa: se entra en la oficina y se sale desde la laptop.
                    // Guardar un origen por marca —y no uno por registro— es justo para esto.
                    if (p == 0 && i == 8)
                    {
                        registro.CheckOutOrigin = "LAP-ANA-CASA";
                        registro.CheckOutNote   = "Terminé el reporte de Contabilidad desde casa.";
                    }

                    asistencia.Add(registro);
                }

                // ── Cronómetro del día ──────────────────────────────────────────
                // Siempre marca MENOS que la jornada: nadie cronometra el café, la junta ni el
                // teléfono. Ese hueco es información, no un error de captura.
                var jornada = (salida - entrada).TotalSeconds;
                var disponibles = suyas.Where(a => a.ClosedAt == null || a.ClosedAt > salida).ToList();
                var actividad = disponibles.Count > 0 ? disponibles[i % disponibles.Count] : suyas[0];

                var ini1 = entrada.AddMinutes(rnd.Next(10, 35));
                int seg1 = (int)(jornada * (0.28 + rnd.NextDouble() * 0.10));
                var fin1 = ini1.AddSeconds(seg1);
                tramos.Add(new WorkInterval
                {
                    DeveloperId = dev.Id, ActivityId = actividad.Id,
                    StartUtc = ini1, EndUtc = fin1, Seconds = seg1, LocalDate = dia
                });

                var finSesion = fin1;
                int total = seg1;

                // Tres de cada cuatro días hay un segundo tramo por la tarde. Los días de un solo
                // tramo son los que se fueron en juntas y soporte, y también tienen que verse.
                if (rnd.Next(0, 4) > 0)
                {
                    var ini2 = entrada.AddSeconds(jornada * 0.58);
                    int seg2 = (int)(jornada * (0.22 + rnd.NextDouble() * 0.12));
                    var fin2 = ini2.AddSeconds(seg2);
                    tramos.Add(new WorkInterval
                    {
                        DeveloperId = dev.Id, ActivityId = actividad.Id,
                        StartUtc = ini2, EndUtc = fin2, Seconds = seg2, LocalDate = dia
                    });
                    finSesion = fin2;
                    total += seg2;
                }

                sesiones.Add(new WorkSession
                {
                    DeveloperId        = dev.Id,
                    ActivityId         = actividad.Id,
                    StartedAt          = ini1,
                    EndedAt            = finSesion,
                    AccumulatedSeconds = total,
                    Status             = WorkSessionStatus.Detenida,
                    LastHeartbeatUtc   = finSesion,
                    Note               = notasDeSesion[rnd.Next(notasDeSesion.Length)],
                    CreatedAt          = ini1
                });
            }
        }

        // ── Hoy: lo que está pasando AHORA MISMO ────────────────────────────────
        // Todo se cuenta hacia atrás desde este momento y no a una hora fija: la demo se puede
        // sembrar a las diez de la mañana o a las once de la noche, y en los dos casos tiene que
        // verse gente trabajando, nunca alguien que marcó su entrada en el futuro. Si al restar las
        // horas se cae al día anterior (madrugada), ese caso simplemente no se siembra.
        bool esHabilHoy = DateTime.Today.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        bool hayJornadaEnCurso = esHabilHoy && ahora.AddHours(-3).ToLocalTime().Date == DateTime.Today;
        bool cabeUnaJornadaCompleta = esHabilHoy && ahora.AddHours(-7).ToLocalTime().Date == DateTime.Today;

        if (hayJornadaEnCurso)
        {
            var entradaAna = ahora.AddHours(-3).AddMinutes(-14);
            asistencia.Add(new AttendanceRecord
            {
                UserId = b.UsuarioAna.Id, DeveloperId = b.Ana.Id, DisplayName = b.UsuarioAna.FullName,
                CheckInUtc = entradaAna, CheckInOrigin = "PC-ANA-01",
                CheckInNote = "Hoy toca revisar el respaldo del NAS antes que nada."
                // Sin CheckOutUtc ni CloseKind: es el registro abierto que hace que el botón de la
                // barra diga «Marcar salida».
            });
            telemetria.Add(new WorkPresence
            {
                UserId = b.UsuarioAna.Id, DeveloperId = b.Ana.Id, DisplayName = b.UsuarioAna.FullName,
                StartedAtUtc = entradaAna.AddMinutes(-6), LastSeenUtc = ahora.AddMinutes(-1),
                State = PresenceState.Ocupado, StateNote = "Revisando el respaldo del NAS",
                Origin = "PC-ANA-01"
                // Sin EndedAtUtc ni EndReason: sigue conectada. El latido reciente es lo que impide
                // que el barrido la cierre como caída.
            });

            var entradaCaro = ahora.AddHours(-2).AddMinutes(-51);
            asistencia.Add(new AttendanceRecord
            {
                UserId = b.UsuarioCaro.Id, DeveloperId = b.Caro.Id, DisplayName = b.UsuarioCaro.FullName,
                CheckInUtc = entradaCaro, CheckInOrigin = "PC-CARO-03"
            });
            telemetria.Add(new WorkPresence
            {
                UserId = b.UsuarioCaro.Id, DeveloperId = b.Caro.Id, DisplayName = b.UsuarioCaro.FullName,
                StartedAtUtc = entradaCaro.AddMinutes(-3), LastSeenUtc = ahora.AddSeconds(-40),
                State = PresenceState.EnReunion, StateNote = "Junta de seguimiento con Operaciones",
                Origin = "PC-CARO-03"
            });

            // Dani tiene la aplicación abierta pero NO marcó: es el día que aparece en el historial
            // como «Sin marcar (solo telemetría)», que es el caso para el que se hizo esa fila.
            telemetria.Add(new WorkPresence
            {
                UserId = b.UsuarioDani.Id, DeveloperId = b.Dani.Id, DisplayName = b.UsuarioDani.FullName,
                StartedAtUtc = ahora.AddHours(-2).AddMinutes(-8), LastSeenUtc = ahora.AddMinutes(-2),
                State = PresenceState.Comiendo, StateNote = "Comiendo, regreso en 40 min",
                Origin = "LAP-DANI-04"
            });

            // Cronómetro CORRIENDO: es lo que hace que la barra superior enseñe el contador subiendo.
            // El latido reciente hace falta para que el barrido de sesiones caídas no la consolide.
            sesiones.Add(new WorkSession
            {
                DeveloperId        = b.Caro.Id,
                ActivityId         = caroCapacitacion.Id,
                StartedAt          = entradaCaro.AddMinutes(20),
                AccumulatedSeconds = 2760,
                LastResumedAt      = ahora.AddMinutes(-38),
                LastHeartbeatUtc   = ahora.AddMinutes(-1),
                Status             = WorkSessionStatus.Activa,
                Note               = "Preparando la sesión de capacitación de la sucursal Norte.",
                CreatedAt          = entradaCaro.AddMinutes(20)
            });
            tramos.Add(new WorkInterval
            {
                // El tramo YA consolidado de esa misma sesión: los segundos que el cronómetro trae
                // acumulados vienen de aquí, y por eso la suma del día cuadra con lo que se ve.
                DeveloperId = b.Caro.Id, ActivityId = caroCapacitacion.Id,
                StartUtc = entradaCaro.AddMinutes(20), EndUtc = entradaCaro.AddMinutes(66),
                Seconds = 2760, LocalDate = DateTime.Today
            });

            // Cronómetro PAUSADO: sin LastResumedAt, porque un pausado no tiene tramo en curso y
            // enseñarlo corriendo sería mentir sobre el tiempo que se está registrando.
            sesiones.Add(new WorkSession
            {
                DeveloperId        = b.Ana.Id,
                ActivityId         = anaSoporte.Id,
                StartedAt          = entradaAna.AddMinutes(12),
                AccumulatedSeconds = 4980,
                LastHeartbeatUtc   = ahora.AddMinutes(-26),
                Status             = WorkSessionStatus.Pausada,
                Note               = "Pausado: entró un reporte de Contabilidad y hay que atenderlo primero.",
                CreatedAt          = entradaAna.AddMinutes(12)
            });
            tramos.Add(new WorkInterval
            {
                DeveloperId = b.Ana.Id, ActivityId = anaSoporte.Id,
                StartUtc = entradaAna.AddMinutes(12), EndUtc = entradaAna.AddMinutes(95),
                Seconds = 4980, LocalDate = DateTime.Today
            });
        }

        if (cabeUnaJornadaCompleta)
        {
            // Beto ya cerró su día: así la pantalla enseña también el estado «ya marcaste salida»,
            // que es distinto de no haber marcado nunca.
            var entradaBeto = ahora.AddHours(-7);
            asistencia.Add(new AttendanceRecord
            {
                UserId = b.UsuarioBeto.Id, DeveloperId = b.Beto.Id, DisplayName = b.UsuarioBeto.FullName,
                CheckInUtc = entradaBeto, CheckInOrigin = "LAP-BETO-02",
                CheckOutUtc = ahora.AddMinutes(-35), CheckOutOrigin = "LAP-BETO-02",
                CheckOutNote = "Dejé programado el despliegue de las 22:00 y verificado el respaldo.",
                CloseKind = AttendanceCloseKind.Manual
            });
            telemetria.Add(new WorkPresence
            {
                UserId = b.UsuarioBeto.Id, DeveloperId = b.Beto.Id, DisplayName = b.UsuarioBeto.FullName,
                StartedAtUtc = entradaBeto.AddMinutes(-11), EndedAtUtc = ahora.AddMinutes(-33),
                LastSeenUtc = ahora.AddMinutes(-33), State = PresenceState.Disponible,
                EndReason = PresenceEnd.CierreNormal, Origin = "LAP-BETO-02"
            });
            sesiones.Add(new WorkSession
            {
                DeveloperId        = b.Beto.Id,
                ActivityId         = betoGuardia.Id,
                StartedAt          = entradaBeto.AddMinutes(25),
                EndedAt            = ahora.AddMinutes(-48),
                AccumulatedSeconds = 12600,
                Status             = WorkSessionStatus.Detenida,
                LastHeartbeatUtc   = ahora.AddMinutes(-48),
                Note               = "Se detuvo al dejar el despliegue programado.",
                CreatedAt          = entradaBeto.AddMinutes(25)
            });
            tramos.Add(new WorkInterval
            {
                // El tramo dura exactamente los segundos que dice: un tramo es un segmento continuo
                // entre reanudar y pausar, no la sesión entera —el hueco hasta que la detuvo es
                // tiempo pausado, y contarlo sería regalarle horas.
                DeveloperId = b.Beto.Id, ActivityId = betoGuardia.Id,
                StartUtc = entradaBeto.AddMinutes(25), EndUtc = entradaBeto.AddMinutes(235),
                Seconds = 12600, LocalDate = DateTime.Today
            });
        }

        db.AttendanceRecords.AddRange(asistencia);
        db.WorkPresences.AddRange(telemetria);
        db.WorkSessions.AddRange(sesiones);
        db.WorkIntervals.AddRange(tramos);
        db.SaveChanges();
    }


    // ═══ COMUNICACION ═══

    // ── Comunicación: foro, sugerencias, minutas, notas, avisos y contactos ──────

    /// <summary>
    /// Lo que el equipo se dice entre sí: el muro del foro con sus hilos, las propuestas de mejora
    /// con sus votos, las minutas con sus acuerdos, las notas de cada quien, los avisos que esperan
    /// en la campana y la libreta de contactos.
    ///
    /// <para>Hay varios <c>SaveChanges</c> por el camino y no es descuido: la raíz de un hilo tiene
    /// que existir en la base antes de que se le puedan colgar comentarios —su propio Id es su
    /// <c>RootId</c>— y un voto necesita el Id de la sugerencia que apoya. Sin guardar antes, esas
    /// claves se irían en cero y el foro se vería como una lista de mensajes sueltos.</para>
    /// </summary>
    public static void SembrarComunicacion(AppDbContext db, DatosBase b)
    {
        SembrarForo(db, b);
        SembrarSugerencias(db, b);
        SembrarMinutas(db, b);
        SembrarNotasYAvisos(db, b);
        SembrarContactos(db);
        db.SaveChanges();
    }

    // ── El foro ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cinco hilos que cubren las formas en que se ve una entrada del muro: uno fijado arriba, uno
    /// con subhilo de respuestas a respuestas, uno editado y muy aplaudido, uno con pocas vueltas y
    /// uno cerrado que además tiene un comentario retirado.
    /// </summary>
    private static void SembrarForo(AppDbContext db, DatosBase b)
    {
        var ahora = DateTime.UtcNow;

        // 1. El aviso de Operaciones. Fijado: es lo que tiene que ver todo el mundo al entrar,
        //    aunque haya hilos con actividad más reciente.
        var mantenimiento = Hilo(db, b.Ops, ForumTopic.Anuncio,
            "Ventana de mantenimiento del sábado: se reinician los servidores de aplicación",
            "El sábado de esta semana, de 22:00 a 02:00, vamos a reiniciar los servidores de "
            + "aplicación para aplicar los parches del sistema operativo. Durante la ventana el "
            + "portal interno y el de proveedores van a estar intermitentes. Por favor no programen "
            + "despliegues en ese horario.",
            "mantenimiento, servidores, ventana",
            ahora.AddDays(-4), fijado: true);

        var dudaFtpVentana = Comentario(db, mantenimiento, b.UsuarioAna,
            "¿La ventana también tumba el FTP? Tengo pendiente subir el ajuste de Nómina y prefiero "
            + "adelantarlo al viernes.",
            ahora.AddDays(-4).AddHours(2));

        // Respuesta a la respuesta: Depth 2. Es lo que hace que el hilo se vea sangrado.
        Comentario(db, dudaFtpVentana, b.Ops,
            "El FTP se queda arriba; lo que se recicla al final de la ventana es el sitio. Si subes "
            + "el viernes no hay problema: el reinicio solo recarga lo que ya esté publicado.",
            ahora.AddDays(-4).AddHours(4));

        MeGusta(db, mantenimiento, ahora.AddDays(-3), b.UsuarioAna, b.UsuarioBeto, b.UsuarioCaro);

        // 2. La pregunta de despliegue: dos ramas distintas colgando de la misma raíz, y la
        //    confirmación de quien preguntó colgando de la rama que le sirvió.
        var error550 = Hilo(db, b.UsuarioBeto, ForumTopic.Pregunta,
            "Error 550 al publicar por FTP en el portal de Nómina",
            "Llevo dos intentos y el cliente de FTP me contesta «550 Access is denied» justo en el "
            + "web.config. El resto de los archivos sí suben. ¿A alguien más le pasó ayer?",
            "ftp, despliegue, nomina",
            ahora.AddDays(-2).AddHours(-5));

        var pistaDelPool = Comentario(db, error550, b.UsuarioCaro,
            "A mí me pasó el mes pasado: el archivo se queda tomado por el proceso del sitio. "
            + "Deteniendo el pool de aplicación un momento antes de subirlo se destraba.",
            ahora.AddDays(-2).AddHours(-4));

        Comentario(db, error550, b.UsuarioAna,
            "Revisa también el atributo de solo lectura. Si el paquete salió de una carpeta de red, "
            + "los archivos llegan marcados y el FTP no los puede sobrescribir.",
            ahora.AddDays(-2).AddHours(-3));

        Comentario(db, pistaDelPool, b.UsuarioBeto,
            "Era eso. Detuve el pool, subí el archivo y lo volví a levantar. Gracias, Caro; lo "
            + "apunto en la guía de despliegue para que no se nos vuelva a olvidar.",
            ahora.AddDays(-2).AddHours(-1));

        MeGusta(db, pistaDelPool, ahora.AddDays(-2), b.UsuarioBeto, b.UsuarioDani, b.Lider);

        // 3. El hilo con más aplausos, y el único editado: enseña la marca de «Editada».
        var leccionWebConfig = Hilo(db, b.UsuarioCaro, ForumTopic.Aprendizaje,
            "Lo que aprendimos del web.config que se nos fue en el último publish",
            "Resumen de lo del martes pasado, para que no se pierda. El publish arrastró el "
            + "web.config de desarrollo y el portal de proveedores estuvo apuntando a la base de "
            + "pruebas casi una hora. Ya se corrigieron dos cosas: el perfil de despliegue excluye "
            + "el archivo, y la lista de verificación pide comprobar la cadena de conexión antes de "
            + "mandar. Si van a tocar el perfil, avisen por aquí antes.",
            "despliegue, web.config, lecciones",
            ahora.AddDays(-11));
        // Se editó al día siguiente para agregar el acuerdo de la lista de verificación.
        leccionWebConfig.EditedAtUtc = ahora.AddDays(-10);

        var dudaDeLista = Comentario(db, leccionWebConfig, b.UsuarioDani,
            "Pregunta de nuevo: la lista de verificación, ¿es la que sale en la pantalla de "
            + "despliegues o la del archivo compartido?",
            ahora.AddDays(-10).AddHours(3));

        Comentario(db, dudaDeLista, b.UsuarioCaro,
            "La de la pantalla. La del archivo compartido quedó fuera de uso desde que el despliegue "
            + "exige la otra para dejarte continuar.",
            ahora.AddDays(-10).AddHours(5));

        MeGusta(db, leccionWebConfig, ahora.AddDays(-10),
                b.UsuarioAna, b.UsuarioBeto, b.UsuarioDani, b.Lider, b.Ops);

        // 4. Una idea suelta, de las que después terminan en el tablero de sugerencias.
        var ideaFreshdesk = Hilo(db, b.UsuarioDani, ForumTopic.Idea,
            "¿Y si el tablero avisa cuando un ticket de Freshdesk lleva más de cuatro horas sin respuesta?",
            "Se nos fueron dos tickets la semana pasada porque nadie los vio hasta la tarde. Con un "
            + "foco de color en el tablero —o un aviso en la campana— creo que no volvería a pasar.",
            "freshdesk, tablero, ideas",
            ahora.AddDays(-7));

        Comentario(db, ideaFreshdesk, b.Lider,
            "Me gusta. Levántala como sugerencia para que el equipo la vote y así le puedo dar "
            + "prioridad en el sprint con algo más que mi opinión.",
            ahora.AddDays(-7).AddHours(5));

        MeGusta(db, ideaFreshdesk, ahora.AddDays(-6), b.UsuarioCaro);

        // 5. Hilo cerrado (Locked) y con una entrada retirada. El cierre es POSTERIOR a los
        //    comentarios: el líder lo cerró cuando ya estaba contestado, para que no se llenara de
        //    «ya quedó». Y el comentario retirado deja ver el hueco con «(contenido eliminado…)»
        //    sin romper la respuesta que cuelga debajo.
        var cierreDeMes = Hilo(db, b.Lider, ForumTopic.Otro,
            "Cierre de mes: capturen su bitácora antes del viernes",
            "El recordatorio de siempre: la bitácora de actividades se cierra el viernes a las "
            + "18:00. Lo que no esté capturado no entra en el reporte que se manda a dirección.",
            "bitacora, cierre de mes",
            ahora.AddDays(-18), cerrado: true);

        var dudaDeFechas = Comentario(db, cierreDeMes, b.UsuarioDani,
            "Las horas del jueves pasado, ¿las capturo con la fecha real o con la del día en que las "
            + "registro?",
            ahora.AddDays(-18).AddHours(3));

        Comentario(db, dudaDeFechas, b.Lider,
            "Con la fecha real. Si la pantalla ya no te deja moverla, mándame el detalle y yo lo "
            + "ajusto de mi lado.",
            ahora.AddDays(-18).AddHours(5));

        var equivocado = Comentario(db, cierreDeMes, b.UsuarioBeto,
            "Me equivoqué de hilo, esto era para el de la ventana de mantenimiento.",
            ahora.AddDays(-18).AddHours(7));
        // Su propio autor la retiró: el texto deja de verse, la fila se queda.
        equivocado.DeletedAtUtc = ahora.AddDays(-18).AddHours(8);
        equivocado.DeletedByUserId = b.UsuarioBeto.Id;

        db.SaveChanges();
    }

    /// <summary>
    /// Publica la raíz de un hilo. Va en dos guardados por lo mismo que en
    /// <c>ForumService.PublicarAsync</c>: <c>RootId</c> apunta a sí misma, y ese Id no existe hasta
    /// que la fila está en la base.
    /// </summary>
    private static ForumPost Hilo(AppDbContext db, User autor, ForumTopic tema, string titulo,
        string cuerpo, string? etiquetas, DateTime cuando, bool fijado = false, bool cerrado = false)
    {
        var raiz = new ForumPost
        {
            ParentId          = null,
            Depth             = 0,
            AuthorUserId      = autor.Id,
            // El nombre se copia, no se consulta: el foro es histórico y tiene que seguir
            // leyéndose aunque mañana se dé de baja la cuenta.
            AuthorName        = autor.FullName,
            AuthorDeveloperId = autor.DeveloperId,
            Title             = titulo,
            Body              = cuerpo,
            Topic             = tema,
            Tags              = etiquetas,
            Pinned            = fijado,
            Locked            = cerrado,
            CreatedAtUtc      = cuando
        };
        db.ForumPosts.Add(raiz);
        db.SaveChanges();

        raiz.RootId = raiz.Id;
        db.SaveChanges();
        return raiz;
    }

    /// <summary>
    /// Cuelga un comentario de una entrada. El padre puede ser la raíz o ya un comentario: de ahí
    /// salen los subhilos, sumando uno a <c>Depth</c> y arrastrando el <c>RootId</c> del padre —que
    /// es el del hilo entero, nunca el del comentario—.
    /// </summary>
    private static ForumPost Comentario(AppDbContext db, ForumPost padre, User autor, string cuerpo,
        DateTime cuando)
    {
        var comentario = new ForumPost
        {
            ParentId          = padre.Id,
            RootId            = padre.RootId,
            Depth             = padre.Depth + 1,
            AuthorUserId      = autor.Id,
            AuthorName        = autor.FullName,
            AuthorDeveloperId = autor.DeveloperId,
            Title             = null,          // los comentarios no llevan título
            Body              = cuerpo,
            Topic             = padre.Topic,   // el comentario hereda el tema del hilo
            CreatedAtUtc      = cuando
        };
        db.ForumPosts.Add(comentario);
        db.SaveChanges();
        return comentario;
    }

    /// <summary>Un «me gusta» por persona: el índice único de ForumLike no admite repetidos.</summary>
    private static void MeGusta(AppDbContext db, ForumPost entrada, DateTime cuando, params User[] quienes)
        => db.ForumLikes.AddRange(quienes.Select((u, i) => new ForumLike
        {
            PostId = entrada.Id, UserId = u.Id, CreatedAtUtc = cuando.AddMinutes(17 * i)
        }));

    // ── Sugerencias ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Seis propuestas repartidas por todo el ciclo de vida —de recién enviada a implementada— y por
    /// los tres casos que cambian cómo se ven: la pública y votada, la anónima que solo mira el
    /// administrador, y la que manda alguien sin ficha de desarrollador (Operaciones).
    /// </summary>
    private static void SembrarSugerencias(AppDbContext db, DatosBase b)
    {
        var ahora = DateTime.UtcNow;

        // Aceptada y con el mayor respaldo del equipo: es la que debe salir arriba al ordenar por votos.
        var reintentar = new Suggestion
        {
            DeveloperId      = b.Beto.Id,
            CreatedByUserId  = b.UsuarioBeto.Id,
            Title            = "Botón para reintentar un despliegue fallido sin volver a subir el ZIP",
            Body             = "Cuando un despliegue truena a la mitad hay que armar otra vez el "
                             + "paquete y volverlo a subir, aunque el ZIP ya esté en el servidor. Con "
                             + "un botón de «reintentar» sobre el mismo paquete nos ahorraríamos "
                             + "veinte minutos cada vez.",
            Category         = SuggestionCategory.Producto,
            Status           = SuggestionStatus.Aceptada,
            AdminResponse    = "Va. Entra en el siguiente sprint de Plataforma; Ana ya la desglosó "
                             + "en dos requerimientos.",
            ReviewedByUserId = b.Lider.Id,
            ReviewedAt       = ahora.AddDays(-19),
            CreatedAt        = ahora.AddDays(-24)
        };

        // Recién enviada: sin revisar, sin respuesta y sin nadie que le haya puesto los ojos encima.
        var filtroDelPool = new Suggestion
        {
            DeveloperId     = b.Dani.Id,
            CreatedByUserId = b.UsuarioDani.Id,
            Title           = "Que el tablero del pool recuerde el filtro que dejé puesto",
            Body            = "Cada vez que entro tengo que volver a filtrar por mi nombre y por «en "
                            + "curso». Que se quede como lo dejé la última vez.",
            Category        = SuggestionCategory.Producto,
            Status          = SuggestionStatus.Nueva,
            CreatedAt       = ahora.AddDays(-3)
        };

        // El caso delicado: anónima y visible solo para el administrador. La ficha sigue ligada —su
        // autora le da seguimiento— pero el panel no enseña quién la mandó. Y no se vota: una
        // sugerencia que el equipo no ve no puede ser un concurso de popularidad.
        var horarioDeJuntas = new Suggestion
        {
            DeveloperId      = b.Caro.Id,
            CreatedByUserId  = b.UsuarioCaro.Id,
            Title            = "El seguimiento de los martes se empalma con la ventana de despliegue",
            Body             = "La junta de las 17:00 cae justo cuando hay que subir los cambios, así "
                             + "que siempre se termina saliendo a media junta o desplegando con "
                             + "prisas. ¿Se podría mover a la mañana?",
            Category         = SuggestionCategory.Departamento,
            Status           = SuggestionStatus.EnRevision,
            Anonymous        = true,
            Visibility       = SuggestionVisibility.SoloAdministrador,
            OpenToVoting     = false,
            ReviewedByUserId = b.Lider.Id,   // ya la abrió; todavía no escribe la respuesta
            ReviewedAt       = ahora.AddDays(-6),
            CreatedAt        = ahora.AddDays(-9)
        };

        // Rechazada, pero con motivo escrito: es lo que hace que la siguiente persona se anime a
        // proponer aunque a esta le hayan dicho que no.
        var comparadorDeBases = new Suggestion
        {
            DeveloperId      = b.Ana.Id,
            CreatedByUserId  = b.UsuarioAna.Id,
            Title            = "Comprar licencias de una herramienta para comparar bases de datos",
            Body             = "Comparar esquemas a mano entre desarrollo y producción nos cuesta "
                             + "media mañana en cada liberación, y aun así se nos escapan columnas.",
            Category         = SuggestionCategory.Otro,
            Status           = SuggestionStatus.Rechazada,
            AdminResponse    = "Este año ya no queda presupuesto de herramientas. Lo volvemos a ver "
                             + "en el de enero; mientras, dejé el script de comparación en la carpeta "
                             + "del equipo.",
            ReviewedByUserId = b.Lider.Id,
            ReviewedAt       = ahora.AddDays(-33),
            CreatedAt        = ahora.AddDays(-40)
        };

        // Implementada: la más vieja, para que el tablero tenga historia y no solo pendientes.
        var plantillaDeMinuta = new Suggestion
        {
            DeveloperId      = b.Caro.Id,
            CreatedByUserId  = b.UsuarioCaro.Id,
            Title            = "Una plantilla de minuta para el daily, siempre la misma",
            Body             = "Cada quien escribía la minuta a su manera y después no se encontraba "
                             + "nada. Propongo tres apartados fijos: lo de ayer, lo de hoy y lo que "
                             + "estorba.",
            Category         = SuggestionCategory.Departamento,
            Status           = SuggestionStatus.Implementada,
            AdminResponse    = "Ya está cargada en las plantillas de minuta y es la que usamos desde "
                             + "hace tres semanas.",
            ReviewedByUserId = b.Lider.Id,
            ReviewedAt       = ahora.AddDays(-48),
            CreatedAt        = ahora.AddDays(-55)
        };

        // De Operaciones: su cuenta no tiene ficha de desarrollador, así que DeveloperId va en null.
        // Es el caso que rompe si alguien asume que toda sugerencia tiene ficha detrás.
        var avisoDeDespliegue = new Suggestion
        {
            DeveloperId      = null,
            CreatedByUserId  = b.Ops.Id,
            Title            = "Que el aviso de despliegue programado llegue también a Operaciones",
            Body             = "Nos enteramos de los despliegues cuando ya están corriendo y el "
                             + "monitoreo empieza a marcar. Con que la cuenta de Operaciones reciba "
                             + "el mismo aviso que el equipo, basta.",
            Category         = SuggestionCategory.Producto,
            Status           = SuggestionStatus.EnRevision,
            ReviewedByUserId = b.Lider.Id,
            ReviewedAt       = ahora.AddDays(-4),
            CreatedAt        = ahora.AddDays(-12)
        };

        db.Suggestions.AddRange(reintentar, filtroDelPool, horarioDeJuntas, comparadorDeBases,
                                plantillaDeMinuta, avisoDeDespliegue);
        db.SaveChanges();   // los votos necesitan el Id de la sugerencia que apoyan

        Votan(db, reintentar, b.UsuarioAna, b.UsuarioCaro, b.UsuarioDani, b.Lider, b.Ops);
        Votan(db, filtroDelPool, b.UsuarioBeto, b.UsuarioCaro);
        Votan(db, comparadorDeBases, b.UsuarioBeto, b.UsuarioCaro);
        Votan(db, plantillaDeMinuta, b.UsuarioAna, b.UsuarioBeto, b.UsuarioDani);
        Votan(db, avisoDeDespliegue, b.Lider, b.UsuarioAna);
        // horarioDeJuntas se queda sin votos a propósito: no es votable y no debe aparecer con marcador.

        db.SaveChanges();
    }

    /// <summary>Un voto por persona y sugerencia; el índice único de SuggestionVote no admite dos.</summary>
    private static void Votan(AppDbContext db, Suggestion sugerencia, params User[] quienes)
        => db.SuggestionVotes.AddRange(quienes.Select((u, i) => new SuggestionVote
        {
            SuggestionId = sugerencia.Id, UserId = u.Id, CreatedAt = sugerencia.CreatedAt.AddHours(6 * (i + 1))
        }));

    // ── Minutas ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dos dailies recientes, la sesión de arranque de un proyecto y una retrospectiva ya cerrada.
    /// Los acuerdos se reparten a propósito entre cumplidos, por vencer y vencidos sin cumplir: una
    /// lista donde todo está palomeado no enseña para qué sirve la columna de la fecha.
    /// </summary>
    private static void SembrarMinutas(AppDbContext db, DatosBase b)
    {
        var hoy = DateTime.Today;

        var dailyPlataforma = new Minute
        {
            Type      = MinuteType.Daily,
            Date      = DiaHabilAtras(1),
            Title     = "Daily de Plataforma",
            Content   = "Ana: terminó el ajuste del portal de proveedores; hoy se va a despliegue.\n"
                      + "Beto: destrabó el 550 del FTP en Nómina deteniendo el pool; lo va a documentar.\n"
                      + "Estorbos: seguimos esperando a que Sistemas abra el puerto del servidor nuevo.",
            CreatedById = b.Lider.Id,
            CreatedAt   = DiaHabilAtras(1).AddHours(15),
            ActionItems =
            {
                new MinuteActionItem
                {
                    Description             = "Documentar en la guía de despliegue el paso de detener el pool antes de subir el web.config",
                    ResponsibleDeveloperId  = b.Beto.Id,
                    DueDate                 = hoy.AddDays(2)
                },
                new MinuteActionItem
                {
                    Description             = "Pedir a Sistemas la apertura del puerto en el servidor nuevo",
                    ResponsibleDeveloperId  = b.Ana.Id,
                    DueDate                 = hoy.AddDays(1),
                    IsCompleted             = true
                }
            }
        };

        var dailyProducto = new Minute
        {
            Type      = MinuteType.Daily,
            Date      = DiaHabilAtras(3),
            Title     = "Daily de Producto",
            Content   = "Caro: cerró la pantalla de percepciones y quedó pendiente la validación con el área.\n"
                      + "Dani: sigue con el catálogo de proveedores; le falta la carga inicial.\n"
                      + "Estorbos: Nómina no ha confirmado el catálogo y sin eso no se puede probar.",
            CreatedById = b.Lider.Id,
            CreatedAt   = DiaHabilAtras(3).AddHours(15),
            ActionItems =
            {
                // Vencido y sin cumplir: es el que tiene que salir resaltado en la pantalla.
                new MinuteActionItem
                {
                    Description             = "Confirmar con Nómina el catálogo de percepciones para poder probar",
                    ResponsibleDeveloperId  = b.Caro.Id,
                    DueDate                 = DiaHabilAtras(1)
                },
                new MinuteActionItem
                {
                    Description             = "Subir la carga inicial del catálogo de proveedores al ambiente de pruebas",
                    ResponsibleDeveloperId  = b.Dani.Id,
                    DueDate                 = DiaHabilAtras(2),
                    IsCompleted             = true
                }
            }
        };

        var arranque = new Minute
        {
            Type      = MinuteType.Sesion,
            Date      = DiaHabilAtras(12),
            Title     = "Sesión de arranque: portal de proveedores, fase 2",
            Content   = "Asistieron: el equipo de Producto, Operaciones y Compras.\n\n"
                      + "Alcance acordado: alta de proveedores con documentos, validación de RFC y "
                      + "consulta de facturas. La firma electrónica se queda fuera de esta fase.\n\n"
                      + "Se acordó que los despliegues a producción se hacen en la ventana de los "
                      + "martes y que Compras avisa a los proveedores con dos días de anticipación.",
            CreatedById = b.Lider.Id,
            CreatedAt   = DiaHabilAtras(12).AddHours(12),
            ActionItems =
            {
                new MinuteActionItem
                {
                    Description             = "Levantar los requerimientos de la fase 2 en el tablero",
                    ResponsibleDeveloperId  = b.Caro.Id,
                    DueDate                 = DiaHabilAtras(9),
                    IsCompleted             = true
                },
                new MinuteActionItem
                {
                    Description             = "Preparar el ambiente de pruebas con una copia depurada de proveedores",
                    ResponsibleDeveloperId  = b.Ana.Id,
                    DueDate                 = DiaHabilAtras(7),
                    IsCompleted             = true
                },
                // Lleva más de una semana vencido: el acuerdo que se quedó en el aire.
                new MinuteActionItem
                {
                    Description             = "Documentar el formato del archivo de carga que manda Compras",
                    ResponsibleDeveloperId  = b.Dani.Id,
                    DueDate                 = DiaHabilAtras(6)
                }
            }
        };

        // Una minuta vieja y completamente cerrada: sirve para ver cómo se ve algo terminado.
        var retrospectiva = new Minute
        {
            Type      = MinuteType.Otro,
            Date      = DiaHabilAtras(28),
            Title     = "Retrospectiva del trimestre",
            Content   = "Lo que salió bien: bajaron los tickets reabiertos y la ventana de despliegue "
                      + "ya no se empalma con el cierre de Nómina.\n\n"
                      + "Lo que hay que arreglar: los despliegues siguen dependiendo de que alguien "
                      + "se acuerde de la lista de verificación, y las minutas se escribían cada una "
                      + "distinta.\n\n"
                      + "Acuerdos: plantilla única de minuta y lista de verificación obligatoria "
                      + "antes de desplegar.",
            CreatedById = b.Lider.Id,
            CreatedAt   = DiaHabilAtras(28).AddHours(11),
            ActionItems =
            {
                new MinuteActionItem
                {
                    Description             = "Dejar la plantilla de minuta cargada en el sistema",
                    ResponsibleDeveloperId  = b.Caro.Id,
                    DueDate                 = DiaHabilAtras(22),
                    IsCompleted             = true
                },
                new MinuteActionItem
                {
                    Description             = "Hacer que el despliegue exija la lista de verificación antes de continuar",
                    ResponsibleDeveloperId  = b.Ana.Id,
                    DueDate                 = DiaHabilAtras(18),
                    IsCompleted             = true
                }
            }
        };

        db.Minutes.AddRange(dailyPlataforma, dailyProducto, arranque, retrospectiva);
        db.SaveChanges();
    }

    // ── Notas y avisos ───────────────────────────────────────────────────────────

    /// <summary>
    /// Las notas de cada quien y los avisos que esperan en la campana. Van juntos porque cuentan la
    /// misma historia desde dos lados: lo que uno se apunta y lo que el sistema le recuerda.
    /// </summary>
    private static void SembrarNotasYAvisos(AppDbContext db, DatosBase b)
    {
        var ahora = DateTime.UtcNow;
        var hoy = DateTime.Today;

        db.Notes.AddRange(
            // Vencida y sin cumplir, en prioridad alta: es la que debe verse resaltada.
            new Note
            {
                Title        = "Renovar el certificado del portal de proveedores",
                Content      = "Vence este mes. El trámite con el proveedor tarda tres días hábiles, "
                             + "así que no se puede dejar para el último día.",
                DeveloperId  = b.Ana.Id,
                ReminderDate = hoy.AddDays(-2),
                Priority     = NotePriority.Alta,
                CreatedAt    = ahora.AddDays(-16)
            },
            new Note
            {
                Title        = "Respaldar la base de Nómina antes del cierre de mes",
                Content      = "Respaldo completo y verificar que se pueda restaurar en el servidor de pruebas.",
                DeveloperId  = b.Beto.Id,
                ReminderDate = hoy.AddDays(3),
                Priority     = NotePriority.Alta,
                CreatedAt    = ahora.AddDays(-5)
            },
            new Note
            {
                Title        = "Revisar por qué el respaldo del jueves tardó el doble",
                Content      = "Empezó a las 23:00 y terminó pasadas las 03:00. Ver si coincidió con el reindexado.",
                DeveloperId  = b.Ana.Id,
                ReminderDate = hoy.AddDays(1),
                Priority     = NotePriority.Media,
                CreatedAt    = ahora.AddDays(-3)
            },
            new Note
            {
                Title        = "Actualizar el diagrama de servidores en la documentación",
                Content      = "Quedó desfasado desde que se movió el portal al servidor nuevo.",
                DeveloperId  = b.Caro.Id,
                Priority     = NotePriority.Baja,   // sin recordatorio: es de las que se hacen cuando se pueda
                CreatedAt    = ahora.AddDays(-21)
            },
            new Note
            {
                Title        = "Dar de baja los accesos del practicante que terminó",
                DeveloperId  = b.Dani.Id,
                ReminderDate = hoy.AddDays(-9),
                IsCompleted  = true,                // vencida pero ya cumplida: no debe alarmar
                Priority     = NotePriority.Media,
                CreatedAt    = ahora.AddDays(-14)
            },
            new Note
            {
                Title        = "Cotizar el disco para el servidor de archivos",
                Content      = "Dos cotizaciones mínimo, con tiempo de entrega.",
                DeveloperId  = b.Beto.Id,
                ReminderDate = hoy.AddDays(14),
                Priority     = NotePriority.Baja,
                CreatedAt    = ahora.AddDays(-2)
            },
            // Sin desarrollador: es una nota del área, no de una persona.
            new Note
            {
                Title        = "Preparar el reporte mensual de actividades para dirección",
                Content      = "Se arma con la bitácora ya cerrada; hay que esperar al viernes.",
                ReminderDate = hoy.AddDays(-5),
                IsCompleted  = true,
                Priority     = NotePriority.Alta,
                CreatedAt    = ahora.AddDays(-10)
            });

        db.Notifications.AddRange(
            // El líder es quien tiene que ver el contador con algo: tres sin leer y una ya leída.
            Aviso(b.Lider, NotificationKind.CompromisoPorVencer,
                  "Compromiso: vence mañana",
                  "«Portal de proveedores: carga masiva» está comprometido para mañana y sigue en curso.",
                  ahora.AddHours(-3), dedupe: "compromiso:req-carga-masiva:mañana"),
            Aviso(b.Lider, NotificationKind.General,
                  "Hay sugerencias sin revisar",
                  "Dos propuestas del equipo llevan más de una semana sin respuesta.",
                  ahora.AddHours(-9)),
            Aviso(b.Lider, NotificationKind.FreshDeskAssigned,
                  "Ticket de Freshdesk sin atender",
                  "El ticket #10482 «No puedo entrar al portal de proveedores» lleva cuatro horas sin respuesta.",
                  ahora.AddDays(-1).AddHours(-2)),
            Aviso(b.Lider, NotificationKind.Comunicado,
                  "Ventana de mantenimiento del sábado",
                  "Los servidores de aplicación se reinician el sábado de 22:00 a 02:00.",
                  ahora.AddDays(-4), leido: ahora.AddDays(-4).AddHours(1)),

            // Ana: un ticket de DevOps recién asignado, con su enlace y su clave de deduplicación.
            Aviso(b.UsuarioAna, NotificationKind.DevOpsAssigned,
                  "Te asignaron el ticket #4821",
                  "Bug: el reporte de antigüedad de saldos sale vacío al filtrar por sucursal.",
                  ahora.AddHours(-5),
                  url: "https://dev.azure.com/soltum/Interno/_workitems/edit/4821",
                  dedupe: "devops-assign:4821"),
            Aviso(b.UsuarioAna, NotificationKind.Comunicado,
                  "Ventana de mantenimiento del sábado",
                  "Los servidores de aplicación se reinician el sábado de 22:00 a 02:00.",
                  ahora.AddDays(-4), leido: ahora.AddDays(-4).AddHours(3)),

            Aviso(b.UsuarioBeto, NotificationKind.RequirementAssigned,
                  "Te asignaron un requerimiento",
                  "«Migrar el envío de recibos de Nómina al servidor nuevo» quedó a tu nombre.",
                  ahora.AddDays(-2), leido: ahora.AddDays(-2).AddHours(2)),
            Aviso(b.UsuarioBeto, NotificationKind.FreshDeskAssigned,
                  "Te asignaron el ticket de Freshdesk #10493",
                  "El recibo de nómina llega sin el desglose de deducciones.",
                  ahora.AddHours(-6)),

            Aviso(b.UsuarioCaro, NotificationKind.CompromisoPorVencer,
                  "Compromiso: vence hoy",
                  "«Validación de RFC en el alta de proveedores» está comprometido para hoy.",
                  ahora.AddHours(-8), dedupe: "compromiso:req-validacion-rfc:hoy"),
            Aviso(b.UsuarioCaro, NotificationKind.Comunicado,
                  "Ventana de mantenimiento del sábado",
                  "Los servidores de aplicación se reinician el sábado de 22:00 a 02:00.",
                  ahora.AddDays(-4), leido: ahora.AddDays(-3).AddHours(-6)),

            Aviso(b.UsuarioDani, NotificationKind.General,
                  "Tu acuerdo de la sesión de arranque sigue pendiente",
                  "«Documentar el formato del archivo de carga que manda Compras» venció hace días.",
                  ahora.AddDays(-1)),
            Aviso(b.UsuarioDani, NotificationKind.DevOpsAssigned,
                  "Te asignaron el ticket #4805",
                  "Tarea: cargar el catálogo inicial de proveedores en el ambiente de pruebas.",
                  ahora.AddDays(-6), leido: ahora.AddDays(-6).AddHours(1),
                  url: "https://dev.azure.com/soltum/Interno/_workitems/edit/4805",
                  dedupe: "devops-assign:4805"),

            Aviso(b.Ops, NotificationKind.General,
                  "Despliegue programado para el martes",
                  "«Portal de proveedores 2.4» está agendado para el martes a las 19:00.",
                  ahora.AddHours(-20)));

        db.SaveChanges();
    }

    /// <summary>Un aviso de la campana. Sin <c>leido</c>, se queda sin leer y suma al contador.</summary>
    private static Notification Aviso(User para, NotificationKind tipo, string titulo, string mensaje,
        DateTime cuando, DateTime? leido = null, string? url = null, string? dedupe = null) => new()
    {
        ForUserId = para.Id,
        Kind      = tipo,
        Title     = titulo,
        Message   = mensaje,
        // Solo se abren las URL absolutas http/https; una ruta interna la descarta la pantalla.
        Url       = url,
        DedupeKey = dedupe,
        CreatedAt = cuando,
        ReadAt    = leido
    };

    // ── Contactos ────────────────────────────────────────────────────────────────

    /// <summary>
    /// La libreta del área: a quién se le habla cuando algo se atora. Mezcla gente de la empresa con
    /// el proveedor del centro de datos, que es a quien nadie encuentra el teléfono a las once de la
    /// noche.
    /// </summary>
    private static void SembrarContactos(AppDbContext db)
    {
        var ahora = DateTime.UtcNow;

        db.Contacts.AddRange(
            new Contact
            {
                Name      = "Mónica Alcántara",
                JobTitle  = "Gerente de Recursos Humanos",
                Company   = "Soltum",
                Email     = "monica.alcantara@soltum.mx",
                TeamsLink = "https://teams.microsoft.com/l/chat/0/0?users=monica.alcantara@soltum.mx",
                Phone     = "55 5010 2200 ext. 214",
                Notes     = "Autoriza vacaciones y permisos. Prefiere que se le escriba por Teams.",
                CreatedAt = ahora.AddMonths(-9)
            },
            new Contact
            {
                Name      = "Ricardo Peña",
                JobTitle  = "Coordinador de Nómina",
                Company   = "Soltum",
                Email     = "ricardo.pena@soltum.mx",
                Phone     = "55 5010 2200 ext. 331",
                Notes     = "Dueño del portal de Nómina. Los días de cierre no contesta antes de las 14:00.",
                CreatedAt = ahora.AddMonths(-8)
            },
            new Contact
            {
                Name      = "Lucía Bermúdez",
                JobTitle  = "Jefa de Compras",
                Company   = "Soltum",
                Email     = "lucia.bermudez@soltum.mx",
                TeamsLink = "https://teams.microsoft.com/l/chat/0/0?users=lucia.bermudez@soltum.mx",
                Phone     = "55 5010 2200 ext. 118",
                Notes     = "Área usuaria del portal de proveedores; valida los cambios antes de liberar.",
                CreatedAt = ahora.AddMonths(-6)
            },
            new Contact
            {
                Name      = "Sergio Valdés",
                JobTitle  = "Administrador de infraestructura",
                Company   = "Soltum",
                Email     = "sergio.valdes@soltum.mx",
                Phone     = "55 5010 2200 ext. 402",
                Notes     = "Abre puertos, da de alta cuentas de servicio y maneja el respaldo nocturno.",
                CreatedAt = ahora.AddMonths(-11)
            },
            new Contact
            {
                Name      = "Adriana Cortés",
                JobTitle  = "Contralora",
                Company   = "Soltum",
                Email     = "adriana.cortes@soltum.mx",
                Phone     = "55 5010 2200 ext. 150",
                Notes     = "Autoriza la compra de licencias y de equipo. Presupuesto nuevo hasta enero.",
                CreatedAt = ahora.AddMonths(-4)
            },
            // El único de fuera: no tiene correo capturado a propósito, el trato es por teléfono y
            // por el portal del proveedor.
            new Contact
            {
                Name      = "Mesa de servicio del centro de datos",
                JobTitle  = "Soporte 24/7 (proveedor)",
                Company   = "Centro de datos Bajío",
                Phone     = "800 000 1414",
                Notes     = "Para caídas de enlace y del servidor físico. Piden el número de contrato "
                          + "al abrir el reporte; está en la carpeta de infraestructura.",
                CreatedAt = ahora.AddMonths(-14)
            });

        db.SaveChanges();
    }


    // ═══ DESPLIEGUES ═══

    // ── Despliegues e infraestructura ────────────────────────────────────────────

    /// <summary>
    /// Los sistemas que mantiene el área, sus versiones, los servidores a los que se publican y el
    /// historial de despliegues — más el inventario de Azure y de software, y los compromisos de SLA.
    ///
    /// <para>Todo cuenta UNA historia coherente, porque una pantalla de historial donde nada tiene que
    /// ver con nada no se puede leer: el hotfix 2.13.1 salió a medias (un nodo se quedó sin disco), se
    /// arregló con un despliegue de selección directa al nodo que falló, y desde entonces los dos
    /// están iguales. La 4.3.1 de facturación falló por credenciales y se reintentó al día siguiente.</para>
    /// </summary>
    private static void SembrarDespliegues(AppDbContext db, DatosBase b)
    {
        var ahora = DateTime.UtcNow;

        // ── Los sistemas ─────────────────────────────────────────────────────────
        var portal = new AppSystem
        {
            Name = "Portal de Clientes",
            Description = "Consulta de estados de cuenta y descarga de comprobantes para los clientes.",
            DefaultSourceFolder = @"D:\Publicaciones\PortalClientes",
            DefaultBlobFolder = "Productivo",
            TeamId = b.Producto.Id,
            CreatedAt = ahora.AddYears(-3)
        };
        var facturacion = new AppSystem
        {
            Name = "Facturación CFDI 4.0",
            Description = "Servicio de timbrado y reexpedición de comprobantes.",
            DefaultSourceFolder = @"D:\Publicaciones\Facturacion",
            DefaultBlobFolder = "Productivo",
            TeamId = b.Plataforma.Id,
            CreatedAt = ahora.AddYears(-2).AddMonths(-4)
        };
        var intranet = new AppSystem
        {
            Name = "Intranet Soltum",
            Description = "Directorio, avisos internos y solicitudes de las áreas.",
            DefaultSourceFolder = @"D:\Publicaciones\Intranet",
            DefaultBlobFolder = "QA",
            TeamId = b.Plataforma.Id,
            CreatedAt = ahora.AddYears(-5)
        };
        // Dado de baja: se absorbió en el portal. Se conserva para que el historial de versiones no
        // pierda sentido, y de paso muestra cómo se ve un sistema archivado en la lista.
        var integraciones = new AppSystem
        {
            Name = "API de Integraciones",
            Description = "Puente con el ERP del corporativo. En proceso de absorberse dentro del portal.",
            DefaultSourceFolder = @"D:\Publicaciones\ApiIntegraciones",
            DefaultBlobFolder = "QA",
            IsActive = false,
            TeamId = b.Producto.Id,
            CreatedAt = ahora.AddYears(-4)
        };
        db.AppSystems.AddRange(portal, facturacion, intranet, integraciones);
        db.SaveChanges();   // hacen falta sus Id para colgarles versiones

        // ── Las versiones ────────────────────────────────────────────────────────
        // Los números crecen con la fecha; la 2.13.1 es un parche del mismo día del incidente y la
        // 2.15.0-rc.1 es la candidata que todavía no sale a producción (es la que está agendada).
        AppRelease Version(AppSystem sistema, string numero, string bitacora, int diasAtras,
                           long tamano, string carpeta, int autorId)
        {
            var huella = (sistema.Id * 7919 + numero.Sum(ch => (int)ch)).ToString("x6");
            return new AppRelease
            {
                AppSystemId = sistema.Id,
                Version = numero,
                Changelog = bitacora,
                ZipLocalPath = $@"D:\Publicaciones\{sistema.Name.Replace(" ", "")}\{numero}\paquete.zip",
                ZipBlobUrl = $"https://almacen.soltum.mx/paquetes/{carpeta}/{sistema.Id}-{numero}.zip",
                TargetFolder = carpeta,
                ZipChecksum = $"sha256:{huella}{huella}9c41ad7f",
                ZipSizeBytes = tamano,
                CreatedById = autorId,
                CreatedAt = ahora.AddDays(-diasAtras)
            };
        }

        var portal2110 = Version(portal, "2.11.0", "Alta de contactos por lote y filtro por sucursal.", 118, 44_182_016, "Productivo", b.UsuarioCaro.Id);
        var portal2120 = Version(portal, "2.12.0", "Descarga de XML acumulado por mes.", 76, 45_007_552, "Productivo", b.UsuarioCaro.Id);
        var portal2130 = Version(portal, "2.13.0", "Nuevo tablero de saldos y cambio de la barra de navegación.", 41, 46_331_904, "Productivo", b.UsuarioDani.Id);
        var portal2131 = Version(portal, "2.13.1", "Parche: el tablero de saldos no cargaba para clientes con más de 500 movimientos.", 27, 46_338_048, "Productivo", b.UsuarioCaro.Id);
        var portal2140 = Version(portal, "2.14.0", "Aviso de facturas por vencer y recuperación de contraseña por correo.", 13, 47_820_800, "Productivo", b.UsuarioDani.Id);
        // Candidata: aún no toca producción. Es la que aparece agendada más abajo.
        var portal2150 = Version(portal, "2.15.0-rc.1", "Candidata: sesión única por usuario y ajuste del reporte de antigüedad de saldos.", 1, 48_115_712, "QA", b.UsuarioCaro.Id);

        var fact420 = Version(facturacion, "4.2.0", "Complemento de comercio exterior 2.0.", 95, 31_457_280, "Productivo", b.UsuarioAna.Id);
        var fact430 = Version(facturacion, "4.3.0", "Cancelación con acuse y reintento automático ante timeout del PAC.", 34, 32_112_640, "Productivo", b.UsuarioAna.Id);
        var fact431 = Version(facturacion, "4.3.1", "Parche: el acuse de cancelación se guardaba sin el sello del PAC.", 5, 32_118_784, "Productivo", b.UsuarioBeto.Id);

        var intra182 = Version(intranet, "1.8.2", "Buscador del directorio por área y extensión.", 60, 18_874_368, "QA", b.UsuarioBeto.Id);
        var intra190 = Version(intranet, "1.9.0", "Solicitudes de papelería y avisos fijados en la portada.", 8, 19_922_944, "QA", b.UsuarioBeto.Id);

        var api300 = Version(integraciones, "3.0.0", "Migración a autenticación por certificado.", 64, 9_437_184, "QA", b.UsuarioAna.Id);
        var api310 = Version(integraciones, "3.1.0", "Reintento con espera creciente al consultar el catálogo del ERP.", 22, 9_502_720, "QA", b.UsuarioAna.Id);

        db.AppReleases.AddRange(portal2110, portal2120, portal2130, portal2131, portal2140, portal2150,
                                fact420, fact430, fact431, intra182, intra190, api300, api310);

        // ── Los servidores ───────────────────────────────────────────────────────
        // La contraseña SIEMPRE cifrada: aquí son inventadas, pero si se guardaran en claro alguien
        // acabaría copiando el patrón en la siembra real de un ambiente.
        var prod1 = new DeploymentTarget
        {
            Nombre = "Producción — Portal 01",
            Host = "ftps://ftp1.soltum.mx", Puerto = 21,
            Usuario = "deploy_portal",
            Contrasena = ProtectorPortable.Cifrar("Portal.Nodo01.2026"),
            RutaRemota = "/sitios/portal", URL = "https://portal.soltum.mx"
        };
        var prod2 = new DeploymentTarget
        {
            Nombre = "Producción — Portal 02",
            Host = "ftps://ftp2.soltum.mx", Puerto = 21,
            Usuario = "deploy_portal",
            Contrasena = ProtectorPortable.Cifrar("Portal.Nodo02.2026"),
            RutaRemota = "/sitios/portal", URL = "https://portal.soltum.mx"
        };
        var prodFact = new DeploymentTarget
        {
            Nombre = "Producción — Facturación",
            Host = "ftps://timbrado.soltum.mx", Puerto = 990,
            Usuario = "deploy_cfdi",
            Contrasena = ProtectorPortable.Cifrar("Cfdi.Timbrado.2026"),
            RutaRemota = "/sitios/facturacion", URL = "https://facturacion.soltum.mx"
        };
        var qa = new DeploymentTarget
        {
            Nombre = "Pruebas (QA)",
            Host = "ftps://qa.soltum.mx", Puerto = 2121,
            Usuario = "deploy_qa",
            Contrasena = ProtectorPortable.Cifrar("Qa.Pruebas.2026"),
            RutaRemota = "/qa", URL = "https://qa.soltum.mx"
        };
        // Inactivo a propósito: el respaldo se movió a Azure Files y este servidor ya no recibe nada,
        // pero se conserva para poder contestar «¿qué había aquí?». Ilustra el filtro de «solo activos».
        var respaldo = new DeploymentTarget
        {
            Nombre = "Respaldo — Caseta (fuera de servicio)",
            Host = "ftps://respaldo.soltum.mx", Puerto = 21,
            Usuario = "respaldo",
            Contrasena = ProtectorPortable.Cifrar("Respaldo.Historico.2025"),
            RutaRemota = "/respaldos/portal",
            IsActive = false,
            LastDeployedAt = ahora.AddMonths(-9)
            // Sin LastReleaseId ni LastDeploymentJobId: su última copia es anterior a que se guardara
            // el historial de despliegues, y así se ve cómo queda un servidor sin rastro asociado.
        };
        db.DeploymentTargets.AddRange(prod1, prod2, prodFact, qa, respaldo);

        // ── Los perfiles ─────────────────────────────────────────────────────────
        var perfilPortal = new DeploymentProfile
        {
            Name = "Producción — Portal de Clientes",
            Description = "Los dos nodos del portal, en orden: primero el 01 y luego el 02.",
            AllowedForOperaciones = true,
            CreatedAt = ahora.AddYears(-2)
        };
        var perfilFact = new DeploymentProfile
        {
            Name = "Producción — Facturación",
            Description = "Timbrado. Solo el administrador: deja el sitio en mantenimiento mientras copia.",
            AllowedForOperaciones = false,   // el único cerrado a Operaciones, y por eso está aquí
            CreatedAt = ahora.AddYears(-2)
        };
        var perfilQa = new DeploymentProfile
        {
            Name = "Pruebas (QA)",
            Description = "Servidor de pruebas compartido por todos los sistemas.",
            AllowedForOperaciones = true,
            CreatedAt = ahora.AddYears(-2)
        };
        // Perfil oculto: lo crea la aplicación al vuelo cuando alguien elige servidores a mano en vez
        // de usar un perfil. No sale en las listas; existe solo para que su despliegue tenga de qué
        // colgarse. Este respalda el arreglo del nodo 02 que quedó atrás con el hotfix.
        var perfilAdHoc = new DeploymentProfile
        {
            Name = $"Selección directa {ahora.AddDays(-25):dd/MM HH:mm}",
            IsAdHoc = true,
            CreatedAt = ahora.AddDays(-25)
        };
        db.DeploymentProfiles.AddRange(perfilPortal, perfilFact, perfilQa, perfilAdHoc);
        db.SaveChanges();   // versiones, servidores y perfiles ya tienen Id

        db.DeploymentProfileTargets.AddRange(
            new DeploymentProfileTarget { ProfileId = perfilPortal.Id, TargetId = prod1.Id, Order = 1 },
            new DeploymentProfileTarget { ProfileId = perfilPortal.Id, TargetId = prod2.Id, Order = 2 },
            new DeploymentProfileTarget { ProfileId = perfilFact.Id, TargetId = prodFact.Id, Order = 1 },
            new DeploymentProfileTarget { ProfileId = perfilQa.Id, TargetId = qa.Id, Order = 1 },
            new DeploymentProfileTarget { ProfileId = perfilAdHoc.Id, TargetId = prod2.Id, Order = 1 });

        // ── El historial de despliegues ──────────────────────────────────────────
        DeploymentJob Despliegue(AppRelease version, DeploymentProfile perfil, JobStatus estado,
                                 double diasAtras, double duracionMin, int total, int ok, int fallidos,
                                 int lanzadoPor, string notas)
        {
            var inicio = ahora.AddDays(-diasAtras);
            return new DeploymentJob
            {
                AppReleaseId = version.Id,
                DeploymentProfileId = perfil.Id,
                Status = estado,
                CreatedAt = inicio.AddMinutes(-1),
                StartedAt = inicio,
                CompletedAt = inicio.AddMinutes(duracionMin),
                StartedById = lanzadoPor,
                TargetsTotal = total, TargetsOk = ok, TargetsFailed = fallidos,
                Notes = notas
            };
        }

        // Salió a QA primero y a producción tres días después: es la secuencia que el equipo sigue.
        var jQa214 = Despliegue(portal2140, perfilQa, JobStatus.Completado, 12, 3.2, 1, 1, 0, b.Ops.Id,
            "Pase a QA para la revisión de Producto.");
        var jProd214 = Despliegue(portal2140, perfilPortal, JobStatus.Completado, 9, 7.2, 2, 2, 0, b.Lider.Id,
            "Ventana acordada con Operaciones, 22:00.");
        // Parcial: el nodo 02 se quedó sin disco a mitad de la subida. Antes esto se reportaba como
        // «Completado» y por eso los nodos podían quedar con versiones distintas sin que nadie lo viera.
        var jHotfix = Despliegue(portal2131, perfilPortal, JobStatus.Parcial, 26, 6.6, 2, 1, 1, b.Lider.Id,
            "Parche urgente. El nodo 02 se quedó sin espacio; queda pendiente rehacerlo.");
        // El arreglo del anterior, con selección directa al nodo que faltaba.
        var jAdHoc = Despliegue(portal2131, perfilAdHoc, JobStatus.Completado, 25, 3.9, 1, 1, 0, b.Lider.Id,
            "Solo el nodo 02, para dejar los dos en la misma versión.");
        // Fallido: la contraseña guardada dejó de servir. Es el caso que la pantalla debe explicar sin
        // que nadie tenga que abrir el registro del servidor FTP.
        var jFactFalla = Despliegue(fact431, perfilFact, JobStatus.Fallido, 4, 0.3, 1, 0, 1, b.Lider.Id,
            "No conectó. Hay que recapturar la contraseña del servidor de timbrado.");
        var jFactOk = Despliegue(fact431, perfilFact, JobStatus.Completado, 3, 4.8, 1, 1, 0, b.Lider.Id,
            "Reintento tras recapturar la contraseña. Agendado desde el calendario.");
        // Cancelado a mano, ya conectado: se restauró lo anterior y no quedó nada a medias.
        var jIntraCancel = Despliegue(intra190, perfilQa, JobStatus.Cancelado, 6, 1.2, 1, 0, 0, b.Ops.Id,
            "Cancelado: Contabilidad estaba probando el cierre de mes en QA.");

        db.DeploymentJobs.AddRange(jQa214, jProd214, jHotfix, jAdHoc, jFactFalla, jFactOk, jIntraCancel);
        db.SaveChanges();   // los renglones del registro necesitan el Id de su despliegue

        // ── El registro de cada despliegue ───────────────────────────────────────
        // Niveles mezclados a propósito: sin una Advertencia y un Error de verdad, la pantalla del
        // registro se ve igual de gris pase lo que pase.
        DeploymentLogEntry Renglon(DeploymentJob trabajo, DeploymentTarget? servidor, double minuto,
                                   DeployLogLevel nivel, string mensaje) => new()
        {
            JobId = trabajo.Id,
            TargetId = servidor?.Id,
            TargetName = servidor?.Nombre,
            Timestamp = (trabajo.StartedAt ?? trabajo.CreatedAt).AddMinutes(minuto),
            Level = nivel,
            Message = mensaje
        };

        db.DeploymentLogEntries.AddRange(
            // QA de la 2.14.0
            Renglon(jQa214, null, 0.0, DeployLogLevel.Info, "Publicando «Portal de Clientes 2.14.0» con el perfil «Pruebas (QA)»."),
            Renglon(jQa214, qa, 0.3, DeployLogLevel.Info, "Conectado a ftps://qa.soltum.mx:2121 como deploy_qa."),
            Renglon(jQa214, qa, 2.6, DeployLogLevel.Info, "Copiados 418 archivos (45.6 MB)."),
            Renglon(jQa214, qa, 3.1, DeployLogLevel.Exito, "Publicación terminada. La versión anterior quedó en /qa/_respaldos/2.13.1."),
            Renglon(jQa214, null, 3.2, DeployLogLevel.Exito, "1 de 1 servidores actualizados."),

            // Producción de la 2.14.0
            Renglon(jProd214, null, 0.0, DeployLogLevel.Info, "Publicando «Portal de Clientes 2.14.0» con el perfil «Producción — Portal de Clientes»."),
            Renglon(jProd214, prod1, 0.2, DeployLogLevel.Info, "Conectado a ftps://ftp1.soltum.mx como deploy_portal."),
            Renglon(jProd214, prod1, 3.4, DeployLogLevel.Exito, "Publicación terminada en 3 min 12 s."),
            Renglon(jProd214, prod2, 3.6, DeployLogLevel.Info, "Conectado a ftps://ftp2.soltum.mx como deploy_portal."),
            Renglon(jProd214, prod2, 5.9, DeployLogLevel.Advertencia, "El web.config del servidor no coincide con el del paquete; se conservó el del servidor."),
            Renglon(jProd214, prod2, 7.1, DeployLogLevel.Exito, "Publicación terminada en 3 min 30 s."),
            Renglon(jProd214, null, 7.2, DeployLogLevel.Exito, "2 de 2 servidores actualizados."),

            // El hotfix que quedó a medias
            Renglon(jHotfix, null, 0.0, DeployLogLevel.Info, "Publicando «Portal de Clientes 2.13.1» (parche) con el perfil «Producción — Portal de Clientes»."),
            Renglon(jHotfix, prod1, 2.8, DeployLogLevel.Exito, "Publicación terminada."),
            Renglon(jHotfix, prod2, 3.0, DeployLogLevel.Info, "Conectado a ftps://ftp2.soltum.mx como deploy_portal."),
            Renglon(jHotfix, prod2, 4.2, DeployLogLevel.Advertencia, "Reintento 1 de 3: el servidor cortó la conexión al subir /bin/Soltum.Portal.dll."),
            Renglon(jHotfix, prod2, 6.4, DeployLogLevel.Error, "552 Espacio insuficiente en el disco del servidor. Se abortó la copia."),
            Renglon(jHotfix, null, 6.6, DeployLogLevel.Advertencia, "1 de 2 servidores actualizados. Los nodos quedan con versiones distintas."),

            // El arreglo por selección directa
            Renglon(jAdHoc, null, 0.0, DeployLogLevel.Info, "Selección directa: solo «Producción — Portal 02»."),
            Renglon(jAdHoc, prod2, 0.4, DeployLogLevel.Advertencia, "Se liberaron 1.8 GB de respaldos viejos antes de copiar."),
            Renglon(jAdHoc, prod2, 3.7, DeployLogLevel.Exito, "Publicación terminada. Los dos nodos quedan en 2.13.1."),

            // Facturación: el que falló
            Renglon(jFactFalla, null, 0.0, DeployLogLevel.Info, "Publicando «Facturación CFDI 4.0 4.3.1» con el perfil «Producción — Facturación»."),
            Renglon(jFactFalla, prodFact, 0.1, DeployLogLevel.Error, "530 User cannot log in. La contraseña guardada dejó de servir tras el cambio de política del proveedor."),
            Renglon(jFactFalla, null, 0.3, DeployLogLevel.Error, "0 de 1 servidores actualizados. No se copió ningún archivo."),

            // Facturación: el reintento del día siguiente
            Renglon(jFactOk, null, 0.0, DeployLogLevel.Info, "Reintento con la contraseña recapturada."),
            Renglon(jFactOk, prodFact, 0.3, DeployLogLevel.Info, "Conectado a ftps://timbrado.soltum.mx:990 como deploy_cfdi."),
            Renglon(jFactOk, prodFact, 2.2, DeployLogLevel.Advertencia, "Sitio en mantenimiento: el timbrado responde 503 mientras dura la copia."),
            Renglon(jFactOk, prodFact, 4.7, DeployLogLevel.Exito, "Publicación terminada y sitio de vuelta en línea."),
            Renglon(jFactOk, null, 4.8, DeployLogLevel.Exito, "1 de 1 servidores actualizados."),

            // Intranet: cancelado a mitad
            Renglon(jIntraCancel, null, 0.0, DeployLogLevel.Info, "Publicando «Intranet Soltum 1.9.0» con el perfil «Pruebas (QA)»."),
            Renglon(jIntraCancel, qa, 0.4, DeployLogLevel.Info, "Conectado a ftps://qa.soltum.mx:2121 como deploy_qa."),
            Renglon(jIntraCancel, null, 1.1, DeployLogLevel.Advertencia, "Cancelado por el usuario a los 66 segundos."),
            Renglon(jIntraCancel, qa, 1.2, DeployLogLevel.Info, "Restaurada la 1.8.2. No quedaron archivos a medias."));

        // ── En qué quedó cada servidor ───────────────────────────────────────────
        // Es lo que contesta «¿qué versión tiene hoy este servidor y de qué despliegue salió?».
        prod1.LastReleaseId = portal2140.Id;
        prod1.LastDeployedAt = jProd214.CompletedAt;
        prod1.LastDeployedById = b.Lider.Id;
        prod1.LastDeploymentJobId = jProd214.Id;

        prod2.LastReleaseId = portal2140.Id;
        prod2.LastDeployedAt = jProd214.CompletedAt;
        prod2.LastDeployedById = b.Lider.Id;
        prod2.LastDeploymentJobId = jProd214.Id;

        prodFact.LastReleaseId = fact431.Id;
        prodFact.LastDeployedAt = jFactOk.CompletedAt;
        prodFact.LastDeployedById = b.Lider.Id;
        prodFact.LastDeploymentJobId = jFactOk.Id;

        // QA se quedó en la 2.14.0 del portal: el despliegue posterior de la intranet se canceló, y un
        // despliegue cancelado no cambia lo que el servidor tiene.
        qa.LastReleaseId = portal2140.Id;
        qa.LastDeployedAt = jQa214.CompletedAt;
        qa.LastDeployedById = b.Ops.Id;
        qa.LastDeploymentJobId = jQa214.Id;

        // ── El calendario ────────────────────────────────────────────────────────
        db.ScheduledDeployments.AddRange(
            // Futuro: la candidata sale a QA esta noche (04:00 UTC ≈ 22:00 en México).
            new ScheduledDeployment
            {
                AppReleaseId = portal2150.Id,
                DeploymentProfileId = perfilQa.Id,
                ScheduledAtUtc = ahora.Date.AddDays(1).AddHours(4),
                Status = ScheduledDeploymentStatus.Programado,
                ToleranciaMinutos = 60,
                Notes = "Reprogramado del intento de ayer. Producto revisa mañana a primera hora.",
                CreatedByUserId = b.Lider.Id,
                CreatedAt = ahora.AddDays(-1)
            },
            // El mismo, cancelado antes de su hora: por eso existe el de arriba.
            new ScheduledDeployment
            {
                AppReleaseId = portal2150.Id,
                DeploymentProfileId = perfilQa.Id,
                ScheduledAtUtc = ahora.Date.AddHours(4),
                Status = ScheduledDeploymentStatus.Cancelado,
                ToleranciaMinutos = 60,
                Notes = "Ventana de anoche.",
                ResultMessage = "Cancelado por el líder: el paquete no estaba firmado. Se reprogramó para hoy.",
                CreatedByUserId = b.Lider.Id,
                CreatedAt = ahora.AddDays(-2)
            },
            // Ya ejecutado: quedó ligado al despliegue que produjo.
            new ScheduledDeployment
            {
                AppReleaseId = fact431.Id,
                DeploymentProfileId = perfilFact.Id,
                ScheduledAtUtc = jFactOk.StartedAt!.Value,
                Status = ScheduledDeploymentStatus.Completado,
                DeploymentJobId = jFactOk.Id,
                ClaimedBy = "SRV-WEB-01",
                ClaimedAtUtc = jFactOk.StartedAt!.Value.AddSeconds(-8),
                ToleranciaMinutos = 30,
                Notes = "Ventana de mantenimiento del timbrado.",
                ResultMessage = "1 de 1 servidores actualizados.",
                CreatedByUserId = b.Lider.Id,
                CreatedAt = ahora.AddDays(-4)
            },
            // Perdido: nadie tenía la aplicación abierta a esa hora. Es el caso que la pantalla
            // advierte de antemano y que conviene poder enseñar.
            new ScheduledDeployment
            {
                AppReleaseId = api310.Id,
                DeploymentProfileId = perfilQa.Id,
                ScheduledAtUtc = ahora.AddDays(-5),
                Status = ScheduledDeploymentStatus.Perdido,
                ToleranciaMinutos = 60,
                Notes = "Pase de la 3.1.0 a QA.",
                ResultMessage = "Ninguna instancia estaba abierta a esa hora; pasada la tolerancia de 60 minutos se dio por perdido.",
                CreatedByUserId = b.UsuarioAna.Id,
                CreatedAt = ahora.AddDays(-7)
            });

        // ── Inventario de Azure ──────────────────────────────────────────────────
        db.AzureResources.AddRange(
            new AzureResource
            {
                Name = "app-soltum-portal-prod", ResourceType = AzureResourceType.AppService,
                Status = AzureResourceStatus.EnUso, Environment = AzureEnvironment.Produccion,
                ResourceGroup = "rg-soltum-produccion", SubscriptionName = "Soltum — Producción",
                Region = "Mexico Central", Url = "https://portal.soltum.mx",
                MonthlyCostEstimate = 1_850m,
                Notes = "Plan P1v3. Es el que atiende el portal de clientes.",
                CreatedAt = ahora.AddYears(-3), UpdatedAt = ahora.AddDays(-9)
            },
            new AzureResource
            {
                Name = "sql-soltum-facturacion", ResourceType = AzureResourceType.SqlDatabase,
                Status = AzureResourceStatus.EnUso, Environment = AzureEnvironment.Produccion,
                ResourceGroup = "rg-soltum-produccion", SubscriptionName = "Soltum — Producción",
                Region = "Mexico Central", MonthlyCostEstimate = 4_260m,
                Notes = "S3. El respaldo con retención larga corre los domingos.",
                CreatedAt = ahora.AddYears(-2), UpdatedAt = ahora.AddDays(-3)
            },
            new AzureResource
            {
                Name = "stsoltumpaquetes", ResourceType = AzureResourceType.StorageAccount,
                Status = AzureResourceStatus.EnUso, Environment = AzureEnvironment.Compartido,
                ResourceGroup = "rg-soltum-compartido", SubscriptionName = "Soltum — Producción",
                Region = "Mexico Central", MonthlyCostEstimate = 310m,
                Notes = "Guarda los ZIP de cada versión. De aquí los baja el despliegue.",
                CreatedAt = ahora.AddYears(-2).AddMonths(-6), UpdatedAt = ahora.AddDays(-1)
            },
            new AzureResource
            {
                Name = "kv-soltum-secretos", ResourceType = AzureResourceType.KeyVault,
                Status = AzureResourceStatus.EnUso, Environment = AzureEnvironment.Produccion,
                ResourceGroup = "rg-soltum-produccion", SubscriptionName = "Soltum — Producción",
                Region = "Mexico Central", MonthlyCostEstimate = 45m,
                Notes = "Certificado del PAC y cadenas de conexión. Aquí van a vivir las llaves cuando se retire el cifrado compartido.",
                CreatedAt = ahora.AddYears(-1).AddMonths(-2)
            },
            new AzureResource
            {
                Name = "func-timbrado-lote", ResourceType = AzureResourceType.FunctionApp,
                Status = AzureResourceStatus.EnPrueba, Environment = AzureEnvironment.Desarrollo,
                ResourceGroup = "rg-soltum-laboratorio", SubscriptionName = "Soltum — Desarrollo",
                Region = "Mexico Central", MonthlyCostEstimate = 95m,
                Notes = "Prueba de sacar el timbrado por lotes del sitio principal. Sin decidir.",
                CreatedAt = ahora.AddMonths(-2), UpdatedAt = ahora.AddDays(-11)
            },
            // Apagada y costando: es justo lo que el inventario existe para sacar a la luz.
            new AzureResource
            {
                Name = "vm-reportes-legado", ResourceType = AzureResourceType.VirtualMachine,
                Status = AzureResourceStatus.NoUsado, Environment = AzureEnvironment.Produccion,
                ResourceGroup = "rg-soltum-legado", SubscriptionName = "Soltum — Producción",
                Region = "East US 2", MonthlyCostEstimate = 2_180m,
                Notes = "Apagada desde que los reportes se movieron al portal, pero el disco sigue facturando. Pendiente de darla de baja.",
                CreatedAt = ahora.AddYears(-5), UpdatedAt = ahora.AddMonths(-4)
            },
            new AzureResource
            {
                Name = "acr-soltum-imagenes", ResourceType = AzureResourceType.ContainerRegistry,
                Status = AzureResourceStatus.Archivado, Environment = AzureEnvironment.Desarrollo,
                ResourceGroup = "rg-soltum-laboratorio", SubscriptionName = "Soltum — Desarrollo",
                Region = "Mexico Central", MonthlyCostEstimate = 0m,
                Notes = "Del intento de contenerizar la intranet. Vacío; se conserva por el nombre.",
                CreatedAt = ahora.AddYears(-2).AddMonths(-2), UpdatedAt = ahora.AddYears(-1)
            });

        // ── Inventario de software ───────────────────────────────────────────────
        // Las fechas de licencia están puestas para que se vean los tres casos de la pantalla: vencida
        // (rojo), por vencer en menos de 30 días (ámbar) y con holgura.
        db.SoftwareItems.AddRange(
            new Software
            {
                Name = "Visual Studio Professional", Category = SoftwareCategory.IDE,
                LicenseType = SoftwareLicenseType.Suscripcion, Status = SoftwareStatus.EnUso,
                Version = "2026", Publisher = "Microsoft",
                LicenseKey = "Contrato SLT-2026-0114 · 4 puestos",
                LicenseExpiry = ahora.AddMonths(8),
                InstalledOn = "SLT-0142, SLT-0187, SLT-0155, SLT-0203",
                CreatedAt = ahora.AddYears(-3), UpdatedAt = ahora.AddMonths(-4)
            },
            // Ámbar: vence en tres semanas. Renovar es decisión de compras, no del área.
            new Software
            {
                Name = "JetBrains Rider", Category = SoftwareCategory.IDE,
                LicenseType = SoftwareLicenseType.Suscripcion, Status = SoftwareStatus.EnUso,
                Version = "2026.1", Publisher = "JetBrains",
                LicenseKey = "····-····-····-7C21",
                LicenseExpiry = ahora.AddDays(22),
                InstalledOn = "SLT-0142, SLT-0155",
                Notes = "Renovación pendiente de autorizar en compras.",
                CreatedAt = ahora.AddYears(-2), UpdatedAt = ahora.AddDays(-6)
            },
            // Ámbar corto: prueba de 14 días a la que le quedan nueve.
            new Software
            {
                Name = "Redgate SQL Prompt", Category = SoftwareCategory.BaseDeDatos,
                LicenseType = SoftwareLicenseType.Prueba, Status = SoftwareStatus.Instalado,
                Version = "10.14", Publisher = "Redgate",
                LicenseExpiry = ahora.AddDays(9),
                InstalledOn = "SLT-0187",
                Notes = "Prueba de 14 días para decidir si vale la pena pedirlo.",
                CreatedAt = ahora.AddDays(-5)
            },
            // Rojo: venció y nadie renovó. Está en Expirado, que es lo que corresponde.
            new Software
            {
                Name = "Adobe Acrobat Pro", Category = SoftwareCategory.Productividad,
                LicenseType = SoftwareLicenseType.Suscripcion, Status = SoftwareStatus.Expirado,
                Version = "2025", Publisher = "Adobe",
                LicenseExpiry = ahora.AddDays(-18),
                InstalledOn = "SLT-0142",
                Notes = "Se usaba para firmar los oficios de vacaciones; ahora eso lo genera la aplicación.",
                CreatedAt = ahora.AddYears(-2), UpdatedAt = ahora.AddDays(-18)
            },
            // Rojo y peor: vencida pero SIGUE en uso. El caso incómodo que el inventario debe delatar.
            new Software
            {
                Name = "Antivirus corporativo ESET", Category = SoftwareCategory.Seguridad,
                LicenseType = SoftwareLicenseType.Comercial, Status = SoftwareStatus.EnUso,
                Version = "11.3", Publisher = "ESET",
                LicenseKey = "····-····-····-0A93",
                LicenseExpiry = ahora.AddDays(-3),
                InstalledOn = "Todos los equipos del área",
                Notes = "Venció esta semana y sigue instalado: sin renovar deja de actualizar firmas.",
                CreatedAt = ahora.AddYears(-4), UpdatedAt = ahora.AddDays(-3)
            },
            new Software
            {
                Name = "SQL Server Standard", Category = SoftwareCategory.BaseDeDatos,
                LicenseType = SoftwareLicenseType.Comercial, Status = SoftwareStatus.EnUso,
                Version = "2022", Publisher = "Microsoft",
                LicenseKey = "Licencia por núcleo · contrato corporativo",
                InstalledOn = "SRV-SQL-01",
                Notes = "Sin fecha de vencimiento: es licencia perpetua del corporativo.",
                CreatedAt = ahora.AddYears(-3)
            },
            new Software
            {
                Name = "WinSCP", Category = SoftwareCategory.UtileriaRed,
                LicenseType = SoftwareLicenseType.OpenSource, Status = SoftwareStatus.EnUso,
                Version = "6.5", Publisher = "Martin Přikryl",
                InstalledOn = "SLT-0142, SLT-0187, SLT-0155, SLT-0203",
                Notes = "Para revisar a mano lo que quedó en el FTP cuando un despliegue falla.",
                CreatedAt = ahora.AddYears(-5)
            },
            new Software
            {
                Name = "Git para Windows", Category = SoftwareCategory.ControlVersiones,
                LicenseType = SoftwareLicenseType.OpenSource, Status = SoftwareStatus.EnUso,
                Version = "2.49", Publisher = "Git for Windows",
                InstalledOn = "Todos los equipos del área",
                CreatedAt = ahora.AddYears(-5)
            },
            new Software
            {
                Name = "Notepad++", Category = SoftwareCategory.Otro,
                LicenseType = SoftwareLicenseType.Gratuita, Status = SoftwareStatus.EnUso,
                Version = "8.7", Publisher = "Don Ho",
                InstalledOn = "SLT-0187, SLT-0203",
                CreatedAt = ahora.AddYears(-5)
            },
            // Desinstalado: se dejó de usar pero el renglón se conserva para saber qué hubo.
            new Software
            {
                Name = "TeamViewer", Category = SoftwareCategory.Comunicacion,
                LicenseType = SoftwareLicenseType.Comercial, Status = SoftwareStatus.Desinstalado,
                Version = "15", Publisher = "TeamViewer",
                LicenseExpiry = ahora.AddMonths(-5),
                Notes = "Se retiró al pasar el soporte remoto a la herramienta del corporativo.",
                CreatedAt = ahora.AddYears(-4), UpdatedAt = ahora.AddMonths(-5)
            });

        // ── Compromisos de SLA ───────────────────────────────────────────────────
        // Dependen de los REQUERIMIENTOS, que siembra SembrarTrabajo (corre antes que este método).
        // Se toman por posición y se salta el compromiso si no hay requerimiento para él: así, si
        // aquella siembra cambia, aquí salen menos compromisos en vez de tronar.
        var reqs = db.Requirements.OrderBy(r => r.Id).Take(5).ToList();

        // Horas: negativas = ya venció. Cada renglón ilustra un estado distinto de la pantalla.
        var compromisos = new (int Pos, Developer Dev, double Horas, SlaStatus Estado, int Comentarios, int Ticket, string Nota)[]
        {
            (0, b.Ana,  72,   SlaStatus.Activo,    3, 41287, "Compromiso con Cobranza: avance comentado en el ticket cada día."),
            // A propósito ACTIVO y con la hora ya pasada: es el que la pantalla debe resaltar en rojo,
            // y además nadie ha sido avisado todavía (OverdueNotifiedAtUtc en null), así que el trabajo
            // de fondo tiene algo que hacer en cuanto arranque.
            (1, b.Dani, -6,   SlaStatus.Activo,    0, 41310, "Sin un solo comentario en el ticket desde que se abrió."),
            (2, b.Beto, -50,  SlaStatus.Vencido,   1, 41265, "Se pasó de fecha y el administrador ya lo dio por incumplido."),
            (3, b.Caro, -140, SlaStatus.Cumplido,  6, 41198, "Entregado y comentado en tiempo."),
            (4, b.Beto, -20,  SlaStatus.Cancelado, 2, 41244, "Sin efecto: el requerimiento se replanteó y volvió al backlog.")
        };

        foreach (var c in compromisos)
        {
            if (c.Pos >= reqs.Count) continue;

            var vence = ahora.AddHours(c.Horas);
            var sla = new SlaCommitment
            {
                RequirementId = reqs[c.Pos].Id,   // exactamente UNO de los dos objetivos: nunca ActivityId a la vez
                DeveloperId = c.Dev.Id,
                DevOpsTicketExternalId = c.Ticket,
                DevOpsTicketUrl = $"https://devops.soltum.mx/Webpro/_workitems/edit/{c.Ticket}",
                DueAtUtc = vence,
                ReminderEveryHours = 24,
                Status = c.Estado,
                CommentCount = c.Comentarios,
                LastCommentAtUtc = c.Comentarios > 0 ? vence.AddHours(-9) : null,
                Notes = c.Nota,
                CreatedByUserId = b.Lider.Id,
                CreatedAt = vence.AddDays(-4)
            };

            // El recordatorio solo sigue vivo mientras el compromiso está activo; cerrado o cancelado,
            // seguir recordando sería ruido.
            if (c.Estado == SlaStatus.Activo)
                sla.NextReminderAtUtc = vence.AddHours(-12);

            if (c.Estado == SlaStatus.Vencido)
            {
                sla.OverdueNotifiedAtUtc = vence.AddMinutes(20);
                sla.BreachNotifiedAtUtc = vence.AddHours(1);
                sla.ReminderNotifiedAtUtc = vence.AddHours(-12);
            }

            db.SlaCommitments.Add(sla);
        }

        db.SaveChanges();
    }


    // ═══ PERFILES-PLANTILLAS ═══

    // ── Fichas, historial de equipos y catálogos ─────────────────────────────────

    /// <summary>
    /// Lo que rodea a las personas: la ficha de desarrollo de cada quien (que solo ve el líder), el
    /// historial de rotaciones entre equipos, los proyectos y los catálogos de la pantalla de DevOps.
    ///
    /// <para>Corre AL FINAL a propósito: los tickets vigilados cuelgan de tickets de Azure DevOps que
    /// siembra <c>SembrarTrabajo</c>. Si por lo que sea no hubiera ninguno, esa parte no siembra nada
    /// en vez de reventar el arranque entero.</para>
    ///
    /// <para><b>Aquí NO se siembra ninguna firma</b> (<c>SignatureProfile</c>): esa entidad guarda un
    /// PNG de verdad y una imagen inventada no enseñaría nada. La pantalla ya sabe convivir con que
    /// nadie tenga firma cargada.</para>
    /// </summary>
    public static void SembrarPerfilesYPlantillas(AppDbContext db, DatosBase b)
    {
        var hoy = DateTime.UtcNow;

        // ── Fichas de desarrollo (una por persona: hay índice único en DeveloperId) ──
        //
        // El SALARIO vive en su columna y en NINGUNA otra parte. Los textos libres de esta ficha
        // (fortalezas, expectativas, notas) acaban en más sitios que la columna sensible —resúmenes,
        // exportaciones, la ficha impresa—, así que mencionar ahí una cifra la filtraría por la
        // puerta de atrás. Por eso ninguno de los textos de abajo habla de dinero.
        db.DeveloperProfiles.AddRange(
            new DeveloperProfile
            {
                DeveloperId = b.Ana.Id,
                Strengths = "Se pelea con los despliegues hasta que salen. Documentó el paso a " +
                            "producción por FTP y es a quien le hablan cuando algo se cae un viernes.",
                Weaknesses = "Absorbe demasiado soporte de primer nivel y le cuesta soltarlo en Beto.",
                TechStack = "C# / .NET 8, Blazor, EF Core, SQL Server, IIS, FileZilla y scripts de despliegue",
                Salary = 48_500m,
                Currency = "MXN",
                GrowthExpectations = "Quiere llevar la arquitectura de los sistemas internos y dejar " +
                                     "la operación del día a día en el equipo. En la siguiente revisión " +
                                     "se plantea formalizarla como líder técnica de Plataforma.",
                Notes = "Ficha revisada en la última uno a uno.",
                UpdatedAt = hoy.AddDays(-38)
            },
            new DeveloperProfile
            {
                DeveloperId = b.Beto.Id,
                Strengths = "Reproduce un bug con lo poquito que trae el ticket; rara vez tiene que " +
                            "regresarlo por falta de información.",
                Weaknesses = "Cierra los requerimientos sin escribir la nota de cierre y hay que " +
                             "perseguirlo. Todavía no toca el servidor de producción sin compañía.",
                TechStack = "C#, Blazor, JavaScript, T-SQL; empezando con la API de Azure DevOps",
                Salary = 32_000m,
                Currency = "MXN",
                GrowthExpectations = "Quiere quedarse con el módulo de facturación completo. Acordado " +
                                     "que lleve el próximo pase a producción de punta a punta, con Ana " +
                                     "de copiloto y sin que ella toque el teclado.",
                UpdatedAt = hoy.AddDays(-52)
            },
            new DeveloperProfile
            {
                DeveloperId = b.Caro.Id,
                Strengths = "Traduce lo que pide el área de negocio a algo que se puede construir. Sus " +
                            "minutas son las que evitan el retrabajo dos semanas después.",
                Weaknesses = "Se compromete a fechas en la junta antes de estimar con el equipo.",
                TechStack = "C#, .NET, Blazor, Power BI, T-SQL; integración con Freshdesk",
                Salary = 47_000m,
                Currency = "MXN",
                GrowthExpectations = "Le interesa más el lado de producto que el técnico: se le está " +
                                     "pasando la relación directa con las áreas usuarias.",
                Notes = "Es el contacto de facto con Finanzas y con Dirección.",
                UpdatedAt = hoy.AddDays(-21)
            },
            new DeveloperProfile
            {
                DeveloperId = b.Dani.Id,
                // La ficha del junior es la que enseña para qué sirve la pantalla: dice qué le falta
                // y en qué plazo, no solo qué sabe hacer.
                Strengths = "Pregunta a tiempo en vez de atorarse tres días. Se quedó con la limpieza " +
                            "de catálogos que nadie quería y la sacó completa.",
                Weaknesses = "Sus estimaciones se van al doble. Necesita acompañamiento en cualquier " +
                             "cambio que toque la base de datos.",
                TechStack = "C# y SQL básicos; aprendiendo Blazor y Git",
                Salary = 18_500m,
                Currency = "MXN",
                GrowthExpectations = "Plan a seis meses: dejar de necesitar revisión en cambios chicos " +
                                     "y llevar solo sus propios pases al servidor de calidad.",
                Notes = "Entró hace menos de un año: su primera evaluación formal todavía no toca.",
                UpdatedAt = hoy.AddDays(-9)
            });

        // ── Historial de rotaciones ──────────────────────────────────────────────
        //
        // La tabla guarda los NOMBRES como foto del momento, no solo los Id: el historial tiene que
        // seguir leyéndose aunque después se borre un equipo. Por eso aquí se copian los nombres de
        // hoy en vez de dejar que la pantalla los resuelva.
        db.TeamRotations.AddRange(
            new TeamRotation
            {
                DeveloperId = b.Caro.Id,
                DeveloperName = b.Caro.FullName,
                FromTeamId = b.Plataforma.Id,
                FromTeamName = b.Plataforma.Name,
                ToTeamId = b.Producto.Id,
                ToTeamName = b.Producto.Name,
                Note = "Se mueve para armar el equipo de Producto y quedarse al frente.",
                RotatedByUserId = b.Lider.Id,
                RotatedAt = hoy.AddMonths(-14)
            },
            new TeamRotation
            {
                // Alta: viene de "sin equipo" porque es la rotación que se registró al contratarlo.
                // Deja FromTeamId en null a propósito, para que se vea ese caso en el historial.
                DeveloperId = b.Dani.Id,
                DeveloperName = b.Dani.FullName,
                FromTeamId = null,
                FromTeamName = "Sin equipo",
                ToTeamId = b.Producto.Id,
                ToTeamName = b.Producto.Name,
                Note = "Ingreso. Entra a Producto con Caro como acompañante.",
                RotatedByUserId = b.Lider.Id,
                RotatedAt = hoy.AddMonths(-8)
            },
            new TeamRotation
            {
                DeveloperId = b.Beto.Id,
                DeveloperName = b.Beto.FullName,
                FromTeamId = b.Producto.Id,
                FromTeamName = b.Producto.Name,
                ToTeamId = b.Plataforma.Id,
                ToTeamName = b.Plataforma.Name,
                Note = "Refuerzo para despliegues: Plataforma se quedó corta cuando se juntaron los " +
                       "pases a producción del cierre de trimestre.",
                RotatedByUserId = b.Lider.Id,
                RotatedAt = hoy.AddMonths(-5)
            });

        // ── Proyectos ────────────────────────────────────────────────────────────
        //
        // Uno de cada estado, para que la pantalla no se vea como una lista de cosas activas: el
        // valor del catálogo está justo en distinguir lo pausado de lo cancelado.
        db.Projects.AddRange(
            new Project
            {
                Name = "Portal de Facturación Interna",
                Client = "Administración y Finanzas",
                Description = "Timbrado y consulta de CFDI para cobranza. Es el sistema que más " +
                              "tickets genera al mes y el que no puede estar caído a fin de mes.",
                Status = ProjectStatus.Activo,
                TeamId = b.Producto.Id,
                CreatedAt = hoy.AddMonths(-22)
            },
            new Project
            {
                // Pausado, no cancelado: la diferencia es que este sí se retoma, y el motivo está
                // escrito para que nadie tenga que preguntar por qué lleva meses quieto.
                Name = "Migración a .NET 8 de los sistemas internos",
                Client = "Interno / TI",
                Description = "En pausa desde que se atravesó el cierre fiscal. Se retoma cuando " +
                              "bajen los tickets de facturación; ya están migrados dos de los cinco.",
                Status = ProjectStatus.EnPausa,
                TeamId = b.Plataforma.Id,
                CreatedAt = hoy.AddMonths(-11)
            },
            new Project
            {
                Name = "Tablero de indicadores para Dirección",
                Client = "Dirección General",
                Description = "Entregado y en uso. Lo que queda son ajustes menores que entran como " +
                              "tickets sueltos, no como proyecto.",
                Status = ProjectStatus.Terminado,
                TeamId = b.Producto.Id,
                CreatedAt = hoy.AddMonths(-17)
            },
            new Project
            {
                Name = "App de captura en piso para almacén",
                Client = "Operaciones",
                Description = "Cancelado: el proveedor del lector de código de barras nunca entregó " +
                              "el SDK de Android y rehacer la lectura a mano no se justificaba.",
                Status = ProjectStatus.Cancelado,
                TeamId = b.Plataforma.Id,
                CreatedAt = hoy.AddMonths(-9)
            });

        // ── Reglas de auto-asignación de Azure DevOps ────────────────────────────
        //
        // Se evalúan por Order, de menor a mayor, y gana la primera que empata. El orden que se
        // siembra es el que tiene sentido leer: primero las dos concretas, y la genérica hasta el
        // final para que no tape a las de arriba.
        db.DevOpsAssignmentRules.AddRange(
            new DevOpsAssignmentRule
            {
                Match = DevOpsRuleMatch.TagContiene,
                MatchValue = "facturacion",
                DeveloperId = b.Caro.Id,
                Order = 1,
                IsActive = true,
                CreatedAt = hoy.AddMonths(-6)
            },
            new DevOpsAssignmentRule
            {
                // Lo que huele a pase a producción cae en Ana, que es quien tiene el acceso al FTP.
                Match = DevOpsRuleMatch.TagContiene,
                MatchValue = "despliegue",
                DeveloperId = b.Ana.Id,
                Order = 2,
                IsActive = true,
                CreatedAt = hoy.AddMonths(-4)
            },
            new DevOpsAssignmentRule
            {
                // APAGADA a propósito: mandaba a Beto todos los bugs de todos los sistemas y lo
                // saturó. Se deja en la lista para que se vea cómo luce una regla desactivada,
                // que es un estado que la pantalla distingue y que si no, nunca se vería.
                Match = DevOpsRuleMatch.TipoEsIgual,
                MatchValue = "Bug",
                DeveloperId = b.Beto.Id,
                Order = 3,
                IsActive = false,
                CreatedAt = hoy.AddMonths(-7)
            });

        // ── Vistas guardadas de la rejilla de DevOps ─────────────────────────────
        //
        // ColumnFiltersJson se queda en null a propósito: hoy esa columna guarda el estado COMPLETO
        // de la rejilla (columnas, anchos y orden), no el {"State":[...]} que describe el comentario
        // viejo de la entidad. Inventar ese JSON a mano solo lograría que la pantalla lo rechazara y
        // avisara «esa vista se guardó con una versión anterior». Con la búsqueda basta para que la
        // vista haga algo visible al aplicarla.
        db.DevOpsSavedFilters.AddRange(
            new DevOpsSavedFilter
            {
                Name = "Facturación: todo lo abierto",
                GlobalSearch = "facturacion",
                CreatedAt = hoy.AddMonths(-3)
            },
            new DevOpsSavedFilter
            {
                Name = "Pases a producción de la semana",
                TitleContains = "despliegue",
                CreatedAt = hoy.AddDays(-12)
            });

        // ── Tickets vigilados ────────────────────────────────────────────────────
        //
        // DEPENDE de que SembrarTrabajo haya corrido antes: los tickets de Azure DevOps son suyos.
        // Si no sembró ninguno, la lista sale vacía y aquí no se siembra nada: vale más una pantalla
        // sin vigilados que tumbar el arranque por un orden que cambió.
        var vigilables = db.DevOpsTickets
            .OrderByDescending(t => t.ExternalId)
            .Take(2)
            .ToList();

        for (var i = 0; i < vigilables.Count; i++)
            db.WatchedTickets.Add(new WatchedTicket
            {
                DevOpsTicketId = vigilables[i].Id,
                // Vigilar es PERSONAL y la fila se ata al NOMBRE DE USUARIO de la sesión, no al Id:
                // así se guarda en el producto y así hay que sembrarlo, o el líder no vería su lista.
                WatchedByUser = b.Lider.Username,
                WatchedSince = hoy.AddDays(-3 - i * 4)
            });

        db.SaveChanges();
    }



    // ═══ BITÁCORA ═══

    /// <summary>
    /// Movimientos en la bitácora, para que esa pantalla no se abra vacía.
    ///
    /// <para>Hace falta sembrarla APARTE, y eso dice algo del diseño: los demás sembradores escriben
    /// directo al contexto y no pasan por <c>AuditService</c>, así que ninguno deja rastro. En la
    /// aplicación de verdad eso no ocurre —cada operación va por su servicio, que anota— pero aquí
    /// significa que la pantalla saldría en blanco justo la que existe para enseñar historial.</para>
    ///
    /// <para>Se siembran ENTRADAS VEROSÍMILES, incluidos accesos denegados y un fallo: una bitácora
    /// donde todo salió bien no enseña para qué sirve. Lo que NUNCA se siembra es un valor secreto en
    /// <c>Details</c>, porque es exactamente la regla que la aplicación cumple.</para>
    /// </summary>
    public static void SembrarBitacora(AppDbContext db, DatosBase b)
    {
        var ahora = DateTime.UtcNow;
        var filas = new List<AuditLog>();

        void Anotar(int dias, int horas, User quien, AuditAction accion, string? tipo, string? id,
                    string detalle, AuditOutcome resultado = AuditOutcome.Exito)
            => filas.Add(new AuditLog
            {
                Timestamp = ahora.AddDays(-dias).AddHours(-horas),
                UserId = quien.Id,
                UserName = quien.Username,
                Action = accion,
                EntityType = tipo,
                EntityId = id,
                Details = detalle,
                Outcome = resultado,
                Origin = "192.168.1.40 · Chrome"
            });

        // Accesos de los últimos días, de varias personas.
        for (int d = 1; d <= 6; d++)
        {
            Anotar(d, 9, b.Lider, AuditAction.Login, "User", b.Lider.Id.ToString(), "Inicio de sesión");
            Anotar(d, 8, b.UsuarioAna, AuditAction.Login, "User", b.UsuarioAna.Id.ToString(), "Inicio de sesión");
            if (d % 2 == 0)
                Anotar(d, 8, b.UsuarioBeto, AuditAction.Login, "User", b.UsuarioBeto.Id.ToString(), "Inicio de sesión");
        }

        // Un intento fallido y uno denegado: es de lo que de verdad se busca en una bitácora.
        Anotar(3, 7, b.UsuarioDani, AuditAction.Login, "User", null,
               "Usuario o contraseña incorrectos", AuditOutcome.Fallo);
        Anotar(2, 5, b.Ops, AuditAction.Delete, "Developer", "3",
               "Intento de dar de baja a un desarrollador sin ser líder", AuditOutcome.Denegado);

        // Trabajo del día a día.
        Anotar(5, 6, b.Lider, AuditAction.Create, "Requirement", "5",
               "Requerimiento dado de alta: Timbrado CFDI 4.0 en el portal de facturación");
        Anotar(4, 4, b.Lider, AuditAction.Update, "PoolActivity", "3",
               "Aceptada: +13 pts a Beto Nájera");
        Anotar(3, 3, b.Lider, AuditAction.Update, "VacationRequest", "2",
               "Vacaciones aprobadas: Carolina Ibarra (17/08/2026 — 28/08/2026)");
        Anotar(2, 2, b.Ops, AuditAction.Deploy, "DeploymentJob", "2",
               "Despliegue de Portal de Facturación 3.4.1 a Producción: 2 servidor(es) listos");
        Anotar(2, 1, b.Ops, AuditAction.Backup, "Blob", null,
               "Respaldo previo del despliegue subido a respaldos-despliegue/");
        Anotar(1, 6, b.Lider, AuditAction.ConfigChange, "AppSetting", "FreshDeskEnabled",
               "Configuración actualizada: FreshDeskEnabled");

        // Consulta de un dato sensible: la acción que la web añadió y el escritorio no tenía.
        Anotar(1, 2, b.Lider, AuditAction.Read, "Software", "4",
               "Consultó la clave de licencia de Visual Studio Professional");

        // Un despliegue que salió mal. Sin fallos, la bitácora no enseña para qué sirve.
        Anotar(6, 5, b.Ops, AuditAction.Deploy, "DeploymentJob", "3",
               "Despliegue fallido: el servidor de respaldo rechazó la conexión FTP",
               AuditOutcome.Fallo);

        db.AuditLogs.AddRange(filas);
        db.SaveChanges();
    }

    /// <summary>
    /// El día laborable N días hábiles hacia atrás desde hoy. Sembrar en sábado y domingo llenaría
    /// el registro de jornadas en fin de semana, que es justo lo que nadie espera ver.
    /// </summary>
    internal static DateTime DiaHabilAtras(int cuantos)
    {
        var dia = DateTime.Today;
        while (cuantos > 0)
        {
            dia = dia.AddDays(-1);
            if (dia.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) cuantos--;
        }
        return dia;
    }
}
