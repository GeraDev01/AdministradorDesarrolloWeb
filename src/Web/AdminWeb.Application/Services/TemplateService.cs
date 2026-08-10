using System.Text;
using System.Text.RegularExpressions;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>Lo que la pantalla captura de una plantilla. Se pasa por valor y no como entidad: lo que
/// llega del navegador es un DTO, y una entidad rastreada nunca debe cruzar la frontera del API.</summary>
public record TemplateInput(
    TemplateKind Kind,
    string? Title,
    string? Description,
    string? Body,
    string? Tags,
    byte[]? FileBytes = null,
    string? FileName = null);

/// <summary>
/// Una plantilla vista desde una lista: todo menos el cuerpo y el adjunto, que son lo pesado y no
/// se pintan en una rejilla. <c>Actualizada</c> ya viene resuelta (la fecha de edición o, si nunca
/// se editó, la de alta) para que quien la muestre no repita esa decisión.
/// </summary>
public record TemplateResumen(
    int Id,
    TemplateKind Kind,
    string Title,
    string? Description,
    string? Tags,
    int UsageCount,
    DateTime? LastUsedAt,
    DateTime Actualizada,
    bool IsArchived,
    bool TieneArchivo,
    string? FileName);

/// <summary>
/// Biblioteca de plantillas del área: cuerpos de ticket de Freshdesk, respuestas y observaciones
/// para requerimientos o work items de Azure DevOps, documentos de entrega de estimaciones y
/// scripts de utilería (SQL, PowerShell, Bash).
///
/// Quién ve qué: el ADMINISTRADOR lo ve y lo administra todo; el DESARROLLADOR solo LEE los tipos
/// de <see cref="TiposDelEquipo"/> (hoy, los comentarios de DevOps: es lo que pega en sus work
/// items a diario). Los scripts y lo demás siguen cerrados: lo sembrado es inofensivo, pero lo que
/// el administrador pegue después puede llevar nombres de servidores, bases y rutas internas.
/// Escribir —crear, editar, duplicar, archivar, borrar— sigue siendo solo del administrador: la
/// biblioteca es material curado, no un cajón.
///
/// La aplicación **nunca ejecuta** lo guardado aquí: una plantilla se copia al portapapeles o se
/// descarga, y quien la corra decide dónde y con qué credenciales.
/// </summary>
public class TemplateService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
{
    /// <summary>
    /// Tipos que un desarrollador puede leer. Es una lista corta y explícita a propósito: ampliar
    /// lo que ve el equipo debe ser una decisión visible en el código, no un efecto colateral.
    /// </summary>
    public static readonly TemplateKind[] TiposDelEquipo = [TemplateKind.ComentarioDevOps];

    public const int MaxTitulo      = 200;
    public const int MaxDescripcion = 2_000;
    public const int MaxCuerpo      = 100_000;
    public const int MaxEtiquetas   = 300;

    /// <summary>Tope del archivo adjunto (10 MB). Vive en la base del equipo: una plantilla no es
    /// un repositorio de binarios.</summary>
    public const int MaxArchivoBytes = 10 * 1024 * 1024;

    // ── Consultas ────────────────────────────────────────────────────────────────

    /// <summary>
    /// La ÚNICA puerta de lectura: lo que esta consulta no devuelve, ningún método de lectura lo
    /// entrega. Si el filtro se repitiera en Listar, Obtener y ConteoPorTipo, el cuarto método de
    /// lectura que alguien agregue se olvidaría de uno.
    /// </summary>
    private IQueryable<Template> Legibles()
    {
        AuthorizationGuard.RequireAdminOrDesarrollador(currentUser);
        var q = db.Templates.AsNoTracking();
        if (currentUser.IsAdmin) return q;
        // Para el desarrollador, «archivada» significa fuera de circulación, sin excepción.
        return q.Where(t => !t.IsArchived && TiposDelEquipo.Contains(t.Kind));
    }

    /// <summary>
    /// Plantillas que cumplen el filtro, las más usadas primero. <paramref name="texto"/> busca en
    /// título, descripción, etiquetas y cuerpo. <paramref name="incluirArchivadas"/> solo surte
    /// efecto para el administrador: para el desarrollador se ignora sin error, así la pantalla no
    /// necesita saber de permisos para pintar una casilla.
    /// </summary>
    public Task<List<Template>> ListarAsync(TemplateKind? tipo = null, string? texto = null,
        bool incluirArchivadas = false, CancellationToken ct = default)
        => Filtradas(tipo, texto, incluirArchivadas).ToListAsync(ct);

    /// <summary>
    /// Lo mismo que <see cref="ListarAsync"/> pero SIN el cuerpo ni el adjunto: solo lo que se
    /// pinta en una lista.
    ///
    /// Es lo que usa la web. Traer las entidades completas para llenar una rejilla de cinco
    /// columnas significa leer de la base cada cuerpo de hasta 100 000 caracteres y cada .docx de
    /// hasta 10 MB, para tirarlos al construir el DTO. En el escritorio, con un proceso y una
    /// biblioteca corta, no se notaba; con una sesión por persona sí.
    ///
    /// «¿Tiene adjunto?» se resuelve en el servidor de base de datos con la longitud del binario,
    /// que es justo lo que evita traérselo.
    /// </summary>
    public Task<List<TemplateResumen>> ListarResumenAsync(TemplateKind? tipo = null, string? texto = null,
        bool incluirArchivadas = false, CancellationToken ct = default)
        => Filtradas(tipo, texto, incluirArchivadas)
            .Select(t => new TemplateResumen(
                t.Id, t.Kind, t.Title, t.Description, t.Tags, t.UsageCount, t.LastUsedAt,
                t.UpdatedAt ?? t.CreatedAt, t.IsArchived,
                t.FileBytes != null && t.FileBytes.Length > 0, t.FileName))
            .ToListAsync(ct);

    /// <summary>
    /// El filtro de la lista, en un solo sitio para que las dos formas de listarla (entidades y
    /// resumen) no puedan discrepar. Parte de <see cref="Legibles"/>, que es la puerta de lectura.
    /// </summary>
    private IQueryable<Template> Filtradas(TemplateKind? tipo, string? texto, bool incluirArchivadas)
    {
        var q = Legibles();
        if (!incluirArchivadas) q = q.Where(t => !t.IsArchived);
        if (tipo is { } k) q = q.Where(t => t.Kind == k);

        texto = (texto ?? "").Trim();
        if (texto.Length > 0)
            q = q.Where(t => t.Title.Contains(texto)
                          || (t.Description != null && t.Description.Contains(texto))
                          || (t.Tags != null && t.Tags.Contains(texto))
                          || t.Body.Contains(texto));

        return q.OrderByDescending(t => t.UsageCount)
                .ThenBy(t => t.Title);
    }

    public Task<Template?> ObtenerAsync(int id, CancellationToken ct = default)
    {
        // Sobre Legibles() y no sobre el DbSet: si Listar filtra pero esto no, queda una puerta
        // trasera para leer por Id justo lo que no se quiso mostrar.
        return Legibles().FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    /// <summary>Cuántas plantillas activas hay de cada tipo (para los contadores de la pantalla).</summary>
    public async Task<Dictionary<TemplateKind, int>> ConteoPorTipoAsync(CancellationToken ct = default)
    {
        // Sobre Legibles(): un conteo sin filtrar le diría al desarrollador «hay 4 scripts SQL
        // que no puedes ver».
        return (await Legibles()
            .Where(t => !t.IsArchived)
            .GroupBy(t => t.Kind)
            .Select(g => new { Tipo = g.Key, Cuantas = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.Tipo, x => x.Cuantas);
    }

    // ── Alta, edición y baja ─────────────────────────────────────────────────────

    public async Task<(bool ok, string mensaje, Template? plantilla)> CrearAsync(
        TemplateInput datos, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var (valida, error, limpio) = Validar(datos);
        if (!valida) return (false, error, null);

        var t = new Template
        {
            Kind            = limpio.Kind,
            Title           = limpio.Title!,
            Description     = limpio.Description,
            Body            = limpio.Body!,
            Tags            = limpio.Tags,
            FileBytes       = limpio.FileBytes,
            FileName        = limpio.FileName,
            CreatedByUserId = currentUser.UserId,
            CreatedAt       = DateTime.UtcNow
        };
        db.Templates.Add(t);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Create, "Template", t.Id.ToString(),
            $"Plantilla creada ({EtiquetaTipo(t.Kind)}): {t.Title}", ct);
        return (true, "Plantilla guardada.", t);
    }

    public async Task<(bool ok, string mensaje)> ActualizarAsync(
        int id, TemplateInput datos, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var (valida, error, limpio) = Validar(datos);
        if (!valida) return (false, error);

        var t = await db.Templates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t == null) return (false, "La plantilla ya no existe. Actualiza la lista.");

        t.Kind        = limpio.Kind;
        t.Title       = limpio.Title!;
        t.Description = limpio.Description;
        t.Body        = limpio.Body!;
        t.Tags        = limpio.Tags;
        t.FileBytes   = limpio.FileBytes;
        t.FileName    = limpio.FileName;
        t.UpdatedAt   = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "Template", t.Id.ToString(),
            $"Plantilla actualizada ({EtiquetaTipo(t.Kind)}): {t.Title}", ct);
        return (true, "Plantilla actualizada.");
    }

    /// <summary>Copia una plantilla para partir de ella sin tocar la original.</summary>
    public async Task<(bool ok, string mensaje, Template? plantilla)> DuplicarAsync(
        int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var origen = await db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (origen == null) return (false, "La plantilla ya no existe. Actualiza la lista.", null);

        // El título se recorta para que « (copia)» quepa siempre dentro del límite de la columna.
        var titulo = origen.Title;
        const string sufijo = " (copia)";
        if (titulo.Length + sufijo.Length > MaxTitulo) titulo = titulo[..(MaxTitulo - sufijo.Length)];

        var copia = new Template
        {
            Kind            = origen.Kind,
            Title           = titulo + sufijo,
            Description     = origen.Description,
            Body            = origen.Body,
            Tags            = origen.Tags,
            FileBytes       = origen.FileBytes,
            FileName        = origen.FileName,
            CreatedByUserId = currentUser.UserId,
            CreatedAt       = DateTime.UtcNow
        };
        db.Templates.Add(copia);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Create, "Template", copia.Id.ToString(),
            $"Plantilla duplicada de #{origen.Id}: {copia.Title}", ct);
        return (true, "Plantilla duplicada.", copia);
    }

    /// <summary>Archiva (o restaura) una plantilla: sale de la lista del día a día sin perderla.</summary>
    public async Task<(bool ok, string mensaje)> ArchivarAsync(
        int id, bool archivar, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var t = await db.Templates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t == null) return (false, "La plantilla ya no existe. Actualiza la lista.");
        if (t.IsArchived == archivar)
            return (true, archivar ? "La plantilla ya estaba archivada." : "La plantilla ya estaba activa.");

        t.IsArchived = archivar;
        t.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Update, "Template", t.Id.ToString(),
            $"Plantilla {(archivar ? "archivada" : "restaurada")}: {t.Title}", ct);
        return (true, archivar ? "Plantilla archivada." : "Plantilla restaurada.");
    }

    public async Task<(bool ok, string mensaje)> EliminarAsync(int id, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdmin(currentUser);

        var t = await db.Templates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t == null) return (false, "La plantilla ya no existe. Actualiza la lista.");

        var titulo = t.Title;
        db.Templates.Remove(t);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(AuditAction.Delete, "Template", id.ToString(), $"Plantilla eliminada: {titulo}", ct);
        return (true, "Plantilla eliminada.");
    }

    /// <summary>
    /// Marca que la plantilla se acaba de copiar o descargar. Es estadística de uso, no una operación
    /// crítica: si falla, no debe impedir que la persona se lleve el texto.
    ///
    /// Con la lectura abierta al desarrollador, esta pasa a ser la ÚNICA escritura que su sesión
    /// ejecuta: solo cuenta usos de plantillas que esa sesión puede LEER. Sin la comprobación,
    /// cualquiera subiría el contador de una plantilla que ni siquiera ve — y como aquí se traga
    /// la excepción, nadie se enteraría.
    /// </summary>
    public async Task RegistrarUsoAsync(int id, CancellationToken ct = default)
    {
        try
        {
            if (await Legibles().AllAsync(t => t.Id != id, ct)) return;

            var t = await db.Templates.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t == null) return;
            t.UsageCount++;
            t.LastUsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch { /* el contador de uso nunca vale un error en pantalla */ }
    }

    // ── Marcadores {{...}} ───────────────────────────────────────────────────────

    // Un marcador no cruza saltos de línea ni anida llaves: así un cuerpo con llaves sueltas
    // (JSON de ejemplo, bloques de PowerShell) no se confunde con un hueco por rellenar.
    private static readonly Regex MarcadorRegex =
        new(@"\{\{\s*([^{}\r\n]{1,60}?)\s*\}\}", RegexOptions.Compiled);

    /// <summary>Marcadores que resuelve la propia aplicación; no se le preguntan a nadie.</summary>
    public static readonly string[] MarcadoresIntegrados =
        ["fecha", "hora", "fechahora", "anio", "año", "usuario", "yo"];

    /// <summary>
    /// Los marcadores <c>{{nombre}}</c> del cuerpo que hay que preguntar, en orden de aparición y
    /// sin repetir. Los integrados (fecha, usuario…) se excluyen: se rellenan solos.
    /// </summary>
    public static List<string> Marcadores(string? cuerpo)
    {
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lista = new List<string>();
        foreach (Match m in MarcadorRegex.Matches(cuerpo ?? ""))
        {
            var nombre = m.Groups[1].Value.Trim();
            if (nombre.Length == 0) continue;
            if (EsIntegrado(nombre)) continue;
            if (vistos.Add(nombre)) lista.Add(nombre);
        }
        return lista;
    }

    private static bool EsIntegrado(string nombre) =>
        MarcadoresIntegrados.Contains(nombre, StringComparer.OrdinalIgnoreCase);

    /// <summary>Sustituye los marcadores por sus valores, resolviendo los integrados con la sesión actual.</summary>
    public string Rellenar(string? cuerpo, IReadOnlyDictionary<string, string>? valores = null) =>
        Rellenar(cuerpo, valores, currentUser.Username ?? "", DateTime.Now);

    /// <summary>
    /// Núcleo sustituible (usuario y reloj explícitos) para poder probarlo. Un marcador sin valor se
    /// deja tal cual: es preferible que salte a la vista un <c>{{cliente}}</c> sin llenar a mandar el
    /// texto con un hueco silencioso.
    /// </summary>
    public static string Rellenar(string? cuerpo, IReadOnlyDictionary<string, string>? valores,
        string usuario, DateTime ahora)
    {
        if (string.IsNullOrEmpty(cuerpo)) return "";

        // Búsqueda sin distinguir mayúsculas: quien captura el valor no tiene por qué escribir el
        // marcador exactamente igual que quien redactó la plantilla.
        var mapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (valores != null)
            foreach (var (k, v) in valores) mapa[k.Trim()] = v ?? "";

        return MarcadorRegex.Replace(cuerpo, m =>
        {
            var nombre = m.Groups[1].Value.Trim();
            var integrado = ValorIntegrado(nombre, usuario, ahora);
            if (integrado != null) return integrado;
            return mapa.TryGetValue(nombre, out var v) ? v : m.Value;
        });
    }

    private static string? ValorIntegrado(string nombre, string usuario, DateTime ahora) =>
        nombre.ToLowerInvariant() switch
        {
            "fecha"     => ahora.ToString("dd/MM/yyyy"),
            "hora"      => ahora.ToString("HH:mm"),
            "fechahora" => ahora.ToString("dd/MM/yyyy HH:mm"),
            "anio" or "año" => ahora.ToString("yyyy"),
            "usuario" or "yo" => usuario,
            _ => null
        };

    // ── Etiquetas y formatos ─────────────────────────────────────────────────────

    public static string EtiquetaTipo(TemplateKind k) => k switch
    {
        TemplateKind.TicketFreshdesk         => "Ticket Freshdesk",
        TemplateKind.RespuestaFreshdesk      => "Respuesta Freshdesk",
        TemplateKind.ComentarioRequerimiento => "Comentario de requerimiento",
        TemplateKind.ComentarioDevOps        => "Comentario Azure DevOps",
        TemplateKind.DocumentoEstimacion     => "Documento de estimación",
        TemplateKind.ScriptSql               => "Script SQL",
        TemplateKind.ScriptPowerShell        => "Script PowerShell",
        TemplateKind.ScriptBash              => "Script Bash",
        _                                    => "Otro"
    };

    public static string IconoTipo(TemplateKind k) => k switch
    {
        TemplateKind.TicketFreshdesk         => "🎫",
        TemplateKind.RespuestaFreshdesk      => "💬",
        TemplateKind.ComentarioRequerimiento => "📋",
        TemplateKind.ComentarioDevOps        => "🔷",
        TemplateKind.DocumentoEstimacion     => "📄",
        TemplateKind.ScriptSql               => "🗄",
        TemplateKind.ScriptPowerShell        => "🟦",
        TemplateKind.ScriptBash              => "🐧",
        _                                    => "📌"
    };

    /// <summary>Extensión con la que se ofrece descargar el cuerpo.</summary>
    public static string ExtensionSugerida(TemplateKind k) => k switch
    {
        TemplateKind.ScriptSql           => ".sql",
        TemplateKind.ScriptPowerShell    => ".ps1",
        TemplateKind.ScriptBash          => ".sh",
        TemplateKind.DocumentoEstimacion => ".md",
        _                                => ".txt"
    };

    /// <summary>Estos tipos se guardan para ejecutarse fuera de la aplicación, nunca dentro.</summary>
    public static bool EsScript(TemplateKind k) =>
        k is TemplateKind.ScriptSql or TemplateKind.ScriptPowerShell or TemplateKind.ScriptBash;

    /// <summary>
    /// Codificación con la que se escribe el archivo. Windows PowerShell 5.1 lee un .ps1 sin BOM
    /// como ANSI: sin la marca, cualquier acento del script sale corrupto al ejecutarlo. Lo demás
    /// se guarda en UTF-8 limpio, que es lo que esperan SQL Server Management Studio y bash.
    ///
    /// En la web no se escribe ningún archivo aquí: es la codificación con la que el endpoint de
    /// descarga debe convertir el cuerpo a bytes antes de mandarlo al navegador.
    /// </summary>
    public static Encoding CodificacionArchivo(TemplateKind k) =>
        k == TemplateKind.ScriptPowerShell
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Nombre de archivo propuesto a partir del título, sin caracteres prohibidos.</summary>
    public static string NombreArchivoSugerido(Template t)
    {
        if (!string.IsNullOrWhiteSpace(t.FileName)) return t.FileName!;

        var limpio = new string(t.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
        if (limpio.Length == 0) limpio = $"plantilla-{t.Id}";
        if (limpio.Length > 80) limpio = limpio[..80].TrimEnd();
        return limpio + ExtensionSugerida(t.Kind);
    }

    // ── Validación ───────────────────────────────────────────────────────────────

    private static (bool ok, string error, TemplateInput limpio) Validar(TemplateInput d)
    {
        var titulo      = (d.Title ?? "").Trim();
        var cuerpo      = (d.Body ?? "").Replace("\r\n", "\n").Trim();
        var descripcion = (d.Description ?? "").Trim();
        var etiquetas   = NormalizarEtiquetas(d.Tags);
        var archivo     = string.IsNullOrWhiteSpace(d.FileName) ? null : d.FileName!.Trim();

        if (titulo.Length < 3)          return (false, "Escribe un título (al menos 3 caracteres).", d);
        if (titulo.Length > MaxTitulo)  return (false, $"El título no puede pasar de {MaxTitulo} caracteres.", d);
        if (descripcion.Length > MaxDescripcion)
            return (false, $"La descripción no puede pasar de {MaxDescripcion} caracteres.", d);
        if (etiquetas.Length > MaxEtiquetas)
            return (false, $"Las etiquetas no pueden pasar de {MaxEtiquetas} caracteres.", d);
        if (cuerpo.Length > MaxCuerpo)
            return (false, $"El contenido no puede pasar de {MaxCuerpo:N0} caracteres.", d);

        // Una plantilla sin contenido ni archivo no sirve de nada; con archivo, el cuerpo pasa a ser
        // opcional (es la explicación de cómo llenar el documento).
        if (cuerpo.Length == 0 && d.FileBytes is not { Length: > 0 })
            return (false, "Escribe el contenido de la plantilla o adjunta un archivo.", d);

        if (d.FileBytes is { Length: > MaxArchivoBytes })
            return (false, $"El archivo adjunto no puede pasar de {MaxArchivoBytes / (1024 * 1024)} MB.", d);
        if (d.FileBytes is { Length: > 0 } && archivo == null)
            return (false, "El archivo adjunto necesita un nombre.", d);

        return (true, "", d with
        {
            Title = titulo,
            Body = cuerpo,
            Description = descripcion.Length == 0 ? null : descripcion,
            Tags = etiquetas.Length == 0 ? null : etiquetas,
            FileBytes = d.FileBytes is { Length: > 0 } ? d.FileBytes : null,
            FileName = d.FileBytes is { Length: > 0 } ? archivo : null
        });
    }

    /// <summary>«  cierre , Cliente ,, cierre » → «cierre, Cliente». Sin duplicados ni vacíos.</summary>
    private static string NormalizarEtiquetas(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return "";
        var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var limpias = tags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                          .Where(e => vistas.Add(e));
        return string.Join(", ", limpias);
    }
}
