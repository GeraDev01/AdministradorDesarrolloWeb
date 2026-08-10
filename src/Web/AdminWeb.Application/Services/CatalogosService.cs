using AdminWeb.Domain.Calculo;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Catalogos;
using AdminWeb.Shared.Dtos.Personas;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// El alta, la edición y la baja de los catálogos del área: desarrolladores, contactos, programas y
/// recursos de Azure. Es la otra mitad de <see cref="CatalogosQueryService"/>, que solo consulta.
///
/// <para>Van juntos los cuatro y no en un servicio por catálogo porque comparten exactamente la
/// misma forma —validar el nombre, crear o buscar, escribir, dejar constancia— y separarlos sería
/// repetir esa forma cuatro veces para que un día divergieran en detalles que nadie decidió.</para>
///
/// <para><b>La baja no borra.</b> En desarrolladores se desactiva, igual que en el escritorio: la
/// ficha está referenciada por asignaciones, evaluaciones, vacaciones y bitácora, y borrarla
/// dejaría huérfano el historial de alguien que sí trabajó aquí. Los otros tres catálogos no tienen
/// baja en el escritorio y tampoco la tienen aquí.</para>
/// </summary>
public class CatalogosService(
    AppDbContext db,
    ICurrentUser currentUser,
    AuditService audit,
    PersonasQueryService personas)
{
    /// <summary>Los catálogos son material del líder. La política del endpoint dice lo mismo; ésta
    /// es la segunda barrera, la que viaja pegada al dato.</summary>
    private void SoloAdmin() => AuthorizationGuard.RequireAdmin(currentUser);

    /// <summary>
    /// El nombre es lo único obligatorio en los cuatro formularios del escritorio, y se recorta
    /// antes de decidir: un nombre de puros espacios pasaba el «no está vacío» y entraba a la base.
    /// </summary>
    private static (bool ok, string error, string limpio) Nombre(string? valor)
    {
        var limpio = valor?.Trim() ?? "";
        return limpio.Length == 0
            ? (false, "El nombre es obligatorio.", "")
            : (true, "", limpio);
    }

    /// <summary>Un texto opcional: en blanco se guarda como nulo, no como cadena vacía. Sin esto la
    /// base acaba con dos formas distintas de decir «no hay dato» y los filtros fallan con una.</summary>
    private static string? Opcional(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    // ── Desarrolladores ──────────────────────────────────────────────────────────

    /// <summary>
    /// Da de alta o edita la ficha de un desarrollador. Con <c>Id</c> nulo es alta.
    ///
    /// <para>NO toca la cuenta de acceso ni la ficha de desarrollo (salario y demás): son otras dos
    /// pantallas con sus propios permisos. Desactivar aquí a alguien deja su cuenta como estaba, que
    /// es lo que hacía el escritorio; quitarle el acceso es una decisión aparte y se toma en
    /// «Usuarios».</para>
    /// </summary>
    public async Task<(bool ok, string mensaje, int id)> GuardarDesarrolladorAsync(
        GuardarDesarrolladorRequest peticion, CancellationToken ct = default)
    {
        SoloAdmin();

        var (valido, error, nombre) = Nombre(peticion.Nombre);
        if (!valido) return (false, error, 0);

        // La fecha de ingreso futura la rechazaba el escritorio al calcular la LFT, pero dejaba
        // guardarla igual. Aquí se rechaza al guardar: una antigüedad negativa no significa nada y
        // envenena el cálculo de vacaciones de esa persona para siempre.
        if (peticion.FechaIngreso is { } ingreso && ingreso.Date > DateTime.Today)
            return (false, "La fecha de ingreso no puede ser futura.", 0);

        if (peticion.DiasVacaciones < 0)
            return (false, "Los días de vacaciones no pueden ser negativos.", 0);

        Developer dev;
        bool esAlta = peticion.Id is null or 0;

        if (esAlta)
        {
            dev = new Developer { CreatedAt = DateTime.UtcNow };
            db.Developers.Add(dev);
        }
        else
        {
            var encontrado = await db.Developers.FirstOrDefaultAsync(d => d.Id == peticion.Id, ct);
            if (encontrado == null) return (false, "El desarrollador no existe.", 0);
            dev = encontrado;
        }

        dev.FullName         = nombre;
        dev.Email            = Opcional(peticion.Correo);
        dev.Phone            = Opcional(peticion.Telefono);
        dev.Seniority        = Opcional(peticion.Seniority);
        dev.HireDate         = peticion.FechaIngreso?.Date;
        dev.Address          = Opcional(peticion.Direccion);
        dev.EquipmentSerial  = Opcional(peticion.SerieEquipo);
        dev.VacationDaysLeft = peticion.DiasVacaciones;
        dev.IsActive         = peticion.Activo;
        dev.Notes            = Opcional(peticion.Notas);

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(esAlta ? AuditAction.Create : AuditAction.Update,
            "Developer", dev.Id.ToString(), dev.FullName, ct);

        return (true, esAlta ? $"«{dev.FullName}» dado de alta." : $"«{dev.FullName}» actualizado.", dev.Id);
    }

    /// <summary>
    /// Desactiva a un desarrollador. Es la «baja» del escritorio, y no borra nada.
    ///
    /// Repetirla sobre alguien ya inactivo se dice en vez de callarse: quien pulsó esperaba cambiar
    /// algo, y un «listo» que no cambió nada es la clase de silencio que hace dudar de la pantalla.
    /// </summary>
    public async Task<(bool ok, string mensaje)> DesactivarDesarrolladorAsync(
        int developerId, CancellationToken ct = default)
    {
        SoloAdmin();

        var dev = await db.Developers.FirstOrDefaultAsync(d => d.Id == developerId, ct);
        if (dev == null) return (false, "El desarrollador no existe.");
        if (!dev.IsActive) return (false, $"«{dev.FullName}» ya estaba inactivo.");

        dev.IsActive = false;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Delete, "Developer", dev.Id.ToString(),
            $"Desactivado: {dev.FullName}", ct);

        return (true, $"«{dev.FullName}» quedó inactivo. Su historial se conserva.");
    }

    /// <summary>
    /// Crea la cuenta de acceso de un desarrollador (rol Desarrollador) con un nombre de usuario
    /// derivado de su correo, y devuelve su contraseña temporal.
    ///
    /// <para>Si ya tiene cuenta NO la recrea ni la restablece por su cuenta: lo dice y ahí se queda.
    /// El escritorio preguntaba «¿restablecer su contraseña?» en un cuadro de sí/no; aquí eso es
    /// otra llamada, la de «Usuarios», porque restablecer la contraseña de alguien es una acción con
    /// consecuencia propia y no el plan B de un botón que se llama «crear acceso».</para>
    ///
    /// <para>La contraseña temporal la pone <see cref="PersonasQueryService.CrearUsuarioAsync"/>,
    /// que a su vez la pide al servicio de autenticación. Aquí solo se resuelve el nombre de usuario
    /// libre: así el alfabeto sin caracteres confundibles y la obligación de cambiarla al entrar
    /// siguen viviendo en un único sitio.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje, CredencialesDto? credenciales)> CrearAccesoAsync(
        int developerId, CancellationToken ct = default)
    {
        SoloAdmin();

        var dev = await db.Developers.FirstOrDefaultAsync(d => d.Id == developerId, ct);
        if (dev == null) return (false, "El desarrollador no existe.", null);

        var existente = await db.Users
            .Where(u => u.DeveloperId == developerId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync(ct);
        if (existente != null)
            return (false, $"«{dev.FullName}» ya tiene la cuenta «{existente}». " +
                           "Para darle una contraseña nueva, usa «Usuarios».", null);

        // Alguien inactivo no debería estrenar acceso: la baja y el alta de cuenta irían en sentidos
        // opuestos el mismo día. Reactivarlo primero es explícito y deja rastro.
        if (!dev.IsActive)
            return (false, $"«{dev.FullName}» está inactivo. Reactívalo antes de darle acceso.", null);

        var usuario = await UsuarioLibreAsync(dev, ct);

        var (ok, mensaje, temporal) = await personas.CrearUsuarioAsync(
            new CrearUsuarioRequest(usuario, dev.FullName, UserRole.Desarrollador, dev.Id), ct);

        if (!ok) return (false, mensaje, null);
        if (temporal == null)
            // La cuenta quedó creada pero sin contraseña utilizable. Se dice tal cual: el arreglo es
            // restablecerla desde «Usuarios», no volver a pulsar aquí (que ahora diría «ya tiene cuenta»).
            return (false, mensaje, null);

        await audit.RecordAsync(AuditAction.Create, "User", dev.Id.ToString(),
            $"Cuenta de acceso creada para {dev.FullName} ({usuario})", ct);

        return (true, mensaje, new CredencialesDto(usuario, temporal,
            "Cópiala ahora: no se vuelve a mostrar. Se le pedirá cambiarla al entrar."));
    }

    /// <summary>
    /// El nombre de usuario libre para una ficha: la parte del correo antes de la arroba, o el
    /// nombre completo si no hay correo, normalizado; si ya está tomado se le añade un número.
    ///
    /// Copiado del escritorio para que una persona que ya tenía cuenta ahí y la pierda no estrene
    /// otra distinta al recrearla.
    /// </summary>
    private async Task<string> UsuarioLibreAsync(Developer dev, CancellationToken ct)
    {
        var baseNombre = !string.IsNullOrWhiteSpace(dev.Email) && dev.Email.Contains('@')
            ? dev.Email[..dev.Email.IndexOf('@')]
            : dev.FullName;

        baseNombre = Normalizar(baseNombre);
        if (baseNombre.Length == 0) baseNombre = "dev";

        var candidato = baseNombre;
        var n = 1;
        while (await db.Users.AnyAsync(u => u.Username == candidato, ct))
            candidato = $"{baseNombre}{++n}";

        return candidato;
    }

    /// <summary>Deja solo letras, dígitos, punto, guion y guion bajo, en minúsculas.</summary>
    private static string Normalizar(string valor) =>
        new([.. valor.Trim().ToLowerInvariant()
                 .Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_')]);

    /// <summary>
    /// Los días que le corresponden por ley a esa fecha de ingreso, para el botón «Calcular (LFT)».
    ///
    /// Es una SUGERENCIA: rellena el campo y el líder puede ajustarlo, igual que en el escritorio.
    /// Se calcula en el servidor —y no en el navegador, que también podría— porque «hoy» tiene que
    /// ser el mismo día para todos y no el del reloj de cada máquina.
    /// </summary>
    public SugerenciaLftDto SugerenciaDeVacaciones(DateTime fechaIngreso)
    {
        SoloAdmin();

        var hoy = DateTime.Today;
        return new SugerenciaLftDto(
            LftVacaciones.DiasCorrespondientes(fechaIngreso, hoy),
            LftVacaciones.Nota(fechaIngreso, hoy));
    }

    // ── Contactos ────────────────────────────────────────────────────────────────

    /// <summary>Da de alta o edita un contacto. Con <c>Id</c> nulo es alta.</summary>
    public async Task<(bool ok, string mensaje, int id)> GuardarContactoAsync(
        GuardarContactoRequest peticion, CancellationToken ct = default)
    {
        SoloAdmin();

        var (valido, error, nombre) = Nombre(peticion.Nombre);
        if (!valido) return (false, error, 0);

        Contact contacto;
        bool esAlta = peticion.Id is null or 0;

        if (esAlta)
        {
            contacto = new Contact { CreatedAt = DateTime.UtcNow };
            db.Contacts.Add(contacto);
        }
        else
        {
            var encontrado = await db.Contacts.FirstOrDefaultAsync(c => c.Id == peticion.Id, ct);
            if (encontrado == null) return (false, "El contacto no existe.", 0);
            contacto = encontrado;
        }

        contacto.Name       = nombre;
        contacto.JobTitle   = Opcional(peticion.Puesto);
        contacto.Company    = Opcional(peticion.Empresa);
        contacto.Email      = Opcional(peticion.Correo);
        contacto.Phone      = Opcional(peticion.Telefono);
        contacto.TeamsLink  = Opcional(peticion.EnlaceTeams);
        contacto.Notes      = Opcional(peticion.Notas);

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(esAlta ? AuditAction.Create : AuditAction.Update,
            "Contact", contacto.Id.ToString(), contacto.Name, ct);

        return (true, esAlta ? $"«{contacto.Name}» dado de alta." : $"«{contacto.Name}» actualizado.", contacto.Id);
    }

    /// <summary>
    /// Borra un contacto. Es el único catálogo donde borrar de verdad es correcto: un contacto no
    /// está referenciado por nada —ni asignaciones, ni evaluaciones, ni bitácora de trabajo— así que
    /// no deja historial huérfano detrás.
    /// </summary>
    public async Task<(bool ok, string mensaje)> BorrarContactoAsync(
        int contactoId, CancellationToken ct = default)
    {
        SoloAdmin();

        var contacto = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactoId, ct);
        if (contacto == null) return (false, "El contacto no existe.");

        var nombre = contacto.Name;
        db.Contacts.Remove(contacto);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Delete, "Contact", contactoId.ToString(), nombre, ct);
        return (true, $"«{nombre}» eliminado.");
    }

    // ── Programas ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Da de alta o edita un programa del inventario. Con <c>Id</c> nulo es alta.
    ///
    /// <para><b>La clave de licencia solo se escribe si viene.</b> Nula significa «déjala como
    /// está», no «bórrala»: la pantalla de edición no la enseña, así que si un nulo borrara, editar
    /// la versión de un programa se llevaría por delante su clave sin que nadie lo pidiera. Para
    /// quitarla se manda una cadena vacía, que sí es una decisión explícita.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje, int id)> GuardarProgramaAsync(
        GuardarProgramaRequest peticion, CancellationToken ct = default)
    {
        SoloAdmin();

        var (valido, error, nombre) = Nombre(peticion.Nombre);
        if (!valido) return (false, error, 0);

        Software programa;
        bool esAlta = peticion.Id is null or 0;

        if (esAlta)
        {
            programa = new Software { CreatedAt = DateTime.UtcNow };
            db.SoftwareItems.Add(programa);
        }
        else
        {
            var encontrado = await db.SoftwareItems.FirstOrDefaultAsync(s => s.Id == peticion.Id, ct);
            if (encontrado == null) return (false, "El programa no existe.", 0);
            programa = encontrado;
            programa.UpdatedAt = DateTime.UtcNow;
        }

        programa.Name          = nombre;
        programa.Category      = peticion.Categoria;
        programa.Status        = peticion.Estado;
        programa.LicenseType   = peticion.Licencia;
        programa.Version       = Opcional(peticion.Version);
        programa.Publisher     = Opcional(peticion.Fabricante);
        programa.LicenseExpiry = peticion.Vence?.Date;
        programa.InstalledOn   = Opcional(peticion.InstaladoEn);
        programa.Url           = Opcional(peticion.Url);
        programa.Notes         = Opcional(peticion.Notas);

        if (peticion.ClaveDeLicencia is not null)
            programa.LicenseKey = Opcional(peticion.ClaveDeLicencia);

        await db.SaveChangesAsync(ct);

        // La clave NO va en el detalle de la bitácora aunque se acabe de cambiar: la bitácora la lee
        // más gente que la que puede ver el inventario, y un secreto copiado ahí ya no se retira.
        await audit.RecordAsync(esAlta ? AuditAction.Create : AuditAction.Update,
            "Software", programa.Id.ToString(),
            peticion.ClaveDeLicencia is not null ? $"{programa.Name} (incl. clave de licencia)" : programa.Name, ct);

        return (true, esAlta ? $"«{programa.Name}» dado de alta." : $"«{programa.Name}» actualizado.", programa.Id);
    }

    /// <summary>
    /// Enseña la clave de licencia de un programa, y deja constancia de quién la miró.
    ///
    /// Es el endpoint propio que anunciaba el contrato de <c>ProgramaDto</c>: la clave no viaja con
    /// la rejilla —ahí quedaría a un «ver código fuente» de cualquiera que abra la pantalla— sino
    /// solo cuando alguien la pide, una a una y con su nombre en la bitácora.
    /// </summary>
    public async Task<(bool ok, string mensaje, string? clave)> VerClaveDeLicenciaAsync(
        int programaId, CancellationToken ct = default)
    {
        SoloAdmin();

        var programa = await db.SoftwareItems
            .Where(s => s.Id == programaId)
            .Select(s => new { s.Name, s.LicenseKey })
            .FirstOrDefaultAsync(ct);

        if (programa == null) return (false, "El programa no existe.", null);
        if (string.IsNullOrWhiteSpace(programa.LicenseKey))
            return (false, $"«{programa.Name}» no tiene clave de licencia guardada.", null);

        // Se registra ANTES de devolverla: si algo fallara entre una cosa y la otra, prefiero una
        // consulta anotada que no ocurrió a una que ocurrió y no quedó anotada.
        await audit.RecordAsync(AuditAction.Read, "Software", programaId.ToString(),
            $"Consultó la clave de licencia de {programa.Name}", ct);

        return (true, "", programa.LicenseKey);
    }

    // ── Recursos de Azure ────────────────────────────────────────────────────────

    /// <summary>Da de alta o edita un recurso de Azure del inventario. Con <c>Id</c> nulo es alta.</summary>
    public async Task<(bool ok, string mensaje, int id)> GuardarRecursoAzureAsync(
        GuardarRecursoAzureRequest peticion, CancellationToken ct = default)
    {
        SoloAdmin();

        var (valido, error, nombre) = Nombre(peticion.Nombre);
        if (!valido) return (false, error, 0);

        if (peticion.CostoMensual is < 0)
            return (false, "El costo mensual no puede ser negativo.", 0);

        AzureResource recurso;
        bool esAlta = peticion.Id is null or 0;

        if (esAlta)
        {
            recurso = new AzureResource { CreatedAt = DateTime.UtcNow };
            db.AzureResources.Add(recurso);
        }
        else
        {
            var encontrado = await db.AzureResources.FirstOrDefaultAsync(r => r.Id == peticion.Id, ct);
            if (encontrado == null) return (false, "El recurso no existe.", 0);
            recurso = encontrado;
            recurso.UpdatedAt = DateTime.UtcNow;
        }

        recurso.Name                = nombre;
        recurso.ResourceType        = peticion.Tipo;
        recurso.Status              = peticion.Estado;
        recurso.Environment         = peticion.Ambiente;
        recurso.ResourceGroup       = Opcional(peticion.GrupoDeRecursos);
        recurso.SubscriptionName    = Opcional(peticion.Suscripcion);
        recurso.Region              = Opcional(peticion.Region);
        recurso.MonthlyCostEstimate = peticion.CostoMensual;
        recurso.Url                 = Opcional(peticion.Url);
        recurso.Notes               = Opcional(peticion.Notas);

        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(esAlta ? AuditAction.Create : AuditAction.Update,
            "AzureResource", recurso.Id.ToString(), recurso.Name, ct);

        return (true, esAlta ? $"«{recurso.Name}» dado de alta." : $"«{recurso.Name}» actualizado.", recurso.Id);
    }
}
