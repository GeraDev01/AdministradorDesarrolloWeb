using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Alta, corrección y baja de servidores de despliegue.
///
/// Reparto de permisos:
///  · <b>Operaciones puede CREAR</b> servidores (es quien conoce el destino nuevo),
///  · pero <b>no editarlos ni darlos de baja</b>: un servidor mal editado o borrado a media tarde
///    rompe despliegues de todo el equipo, y la contraseña de un servidor existente no debe poder
///    reapuntarse a otro host sin que lo revise un administrador.
///  · Si Operaciones se equivoca al capturar, un Admin lo corrige. Regla sin excepciones: más fácil
///    de explicar y de auditar que una ventana temporal.
///
/// Toda mutación queda en bitácora con el estado antes y después, sin la contraseña.
/// </summary>
public class DeploymentTargetService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly AuditService _audit;

    public DeploymentTargetService(AppDbContext db, ICurrentUser currentUser, AuditService audit)
    {
        _db = db; _currentUser = currentUser; _audit = audit;
    }

    public static bool PuedeCrear(ICurrentUser u) => u.IsAdmin || u.IsOperaciones;
    public static bool PuedeEditar(ICurrentUser u) => u.IsAdmin;
    public static bool PuedeEliminar(ICurrentUser u) => u.IsAdmin;

    /// <summary>Instantánea para la bitácora. Nunca incluye la contraseña, ni cifrada.</summary>
    private static object Instantanea(DeploymentTarget t) => new
    {
        t.Id, t.Nombre, t.Host, t.Puerto, t.Usuario, t.RutaRemota, t.URL, t.IsActive
    };

    /// <summary>
    /// El servidor RASTREADO y con los valores de la base, listo para editarlo y guardarlo.
    ///
    /// El <c>AppDbContext</c> es Singleton y vive lo que dura la sesión, así que lo que quedó
    /// rastreado hace media hora puede estar viejo: el despliegue escribe desde su propio contexto,
    /// y otro equipo pudo corregir el servidor mientras tanto. <c>Find</c> por sí solo devolvería esa
    /// copia vieja sin ir a la base; el <c>Reload</c> es lo que garantiza que se edite lo que hay
    /// hoy y no se pisen cambios ajenos al guardar.
    ///
    /// Devuelve null si ya no existe (alguien lo eliminó desde otro equipo).
    /// </summary>
    public DeploymentTarget? ParaEditar(int targetId) => Rastreado(targetId);

    private DeploymentTarget? Rastreado(int targetId)
    {
        var t = _db.DeploymentTargets.Find(targetId);
        if (t == null) return null;

        var entrada = _db.Entry(t);
        entrada.Reload();
        // Reload deja la entrada en Detached cuando la fila ya no está en la base.
        return entrada.State == EntityState.Detached ? null : t;
    }

    public (bool ok, string mensaje, DeploymentTarget? servidor) Crear(DeploymentTarget nuevo)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(_currentUser);

        var error = Validar(nuevo, esNuevo: true);
        if (error != null)
        {
            _audit.RecordDenied(AuditAction.Create, "DeploymentTarget", null, $"Alta rechazada: {error}");
            return (false, error, null);
        }

        nuevo.IsActive = true;
        _db.DeploymentTargets.Add(nuevo);
        _db.SaveChanges();

        _audit.RecordDetailed(AuditAction.Create, "DeploymentTarget", nuevo.Id.ToString(),
            $"Servidor dado de alta: {nuevo.Nombre} ({nuevo.Host}:{nuevo.Puerto}{nuevo.RutaRemota})",
            AuditOutcome.Exito, newValues: Instantanea(nuevo));

        return (true, "Servidor creado.", nuevo);
    }

    /// <summary>
    /// Aplica los cambios ya escritos sobre la entidad rastreada. Recibe la instantánea previa
    /// porque el formulario edita la MISMA instancia y para cuando llega aquí los valores
    /// originales ya se perdieron.
    /// </summary>
    public (bool ok, string mensaje) Editar(DeploymentTarget servidor, object? estadoPrevio)
    {
        if (!PuedeEditar(_currentUser))
        {
            _audit.RecordDenied(AuditAction.Update, "DeploymentTarget", servidor.Id.ToString(),
                $"Intento de editar el servidor «{servidor.Nombre}» sin ser líder.");
            throw new AuthorizationException(
                "Solo un líder puede modificar un servidor existente. " +
                "Si hay un dato mal capturado, pídele que lo corrija.");
        }

        var error = Validar(servidor, esNuevo: false);
        if (error != null) return (false, error);

        _db.SaveChanges();
        _audit.RecordDetailed(AuditAction.Update, "DeploymentTarget", servidor.Id.ToString(),
            $"Servidor modificado: {servidor.Nombre}",
            AuditOutcome.Exito, oldValues: estadoPrevio, newValues: Instantanea(servidor));
        return (true, "Servidor actualizado.");
    }

    /// <summary>
    /// Baja LÓGICA. El borrado físico dejaba la bitácora apuntando a un id inexistente y rompía la
    /// trazabilidad de los despliegues históricos que lo usaron.
    /// </summary>
    public (bool ok, string mensaje) Desactivar(int targetId, string? motivo)
    {
        if (!PuedeEliminar(_currentUser))
        {
            _audit.RecordDenied(AuditAction.Delete, "DeploymentTarget", targetId.ToString(),
                "Intento de dar de baja un servidor sin ser líder.");
            throw new AuthorizationException("Solo un líder puede dar de baja un servidor.");
        }

        var t = Rastreado(targetId);
        if (t == null) return (false, "El servidor ya no existe.");
        if (!t.IsActive) return (false, "El servidor ya estaba dado de baja.");

        var previo = Instantanea(t);
        t.IsActive = false;
        _db.SaveChanges();

        _audit.RecordDetailed(AuditAction.Delete, "DeploymentTarget", t.Id.ToString(),
            $"Servidor dado de baja: {t.Nombre}" + (string.IsNullOrWhiteSpace(motivo) ? "" : $" — {motivo.Trim()}"),
            AuditOutcome.Exito, oldValues: previo, newValues: Instantanea(t));

        return (true, "Servidor dado de baja. Su historial de despliegues se conserva.");
    }

    public (bool ok, string mensaje) Reactivar(int targetId)
    {
        if (!PuedeEditar(_currentUser))
            throw new AuthorizationException("Solo un líder puede reactivar un servidor.");

        var t = Rastreado(targetId);
        if (t == null) return (false, "El servidor ya no existe.");
        if (t.IsActive) return (false, "El servidor ya estaba activo.");

        var previo = Instantanea(t);
        t.IsActive = true;
        _db.SaveChanges();
        _audit.RecordDetailed(AuditAction.Update, "DeploymentTarget", t.Id.ToString(),
            $"Servidor reactivado: {t.Nombre}", AuditOutcome.Exito, previo, Instantanea(t));
        return (true, "Servidor reactivado.");
    }

    private string? Validar(DeploymentTarget t, bool esNuevo)
    {
        if (string.IsNullOrWhiteSpace(t.Nombre)) return "El nombre es obligatorio.";
        if (string.IsNullOrWhiteSpace(t.Host)) return "El host es obligatorio.";
        if (t.Puerto is < 1 or > 65535) return "El puerto debe estar entre 1 y 65535.";
        if (string.IsNullOrWhiteSpace(t.Usuario)) return "El usuario es obligatorio.";
        if (string.IsNullOrWhiteSpace(t.RutaRemota)) return "La ruta remota es obligatoria.";

        // Dos servidores con el mismo nombre hacen imposible leer la bitácora después.
        bool duplicado = esNuevo
            ? _db.DeploymentTargets.Any(x => x.Nombre == t.Nombre)
            : _db.DeploymentTargets.Any(x => x.Nombre == t.Nombre && x.Id != t.Id);
        if (duplicado) return $"Ya existe un servidor llamado «{t.Nombre}».";

        return null;
    }
}
