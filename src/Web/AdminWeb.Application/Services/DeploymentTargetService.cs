using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Infrastructure.Seguridad;
using AdminWeb.Shared.Dtos.Despliegues;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Alta, corrección y baja de servidores de despliegue.
///
/// Reparto de permisos, portado tal cual del escritorio:
///  · <b>Operaciones puede CREAR</b> servidores (es quien conoce el destino nuevo),
///  · pero <b>no editarlos ni darlos de baja</b>: un servidor mal editado o borrado a media tarde
///    rompe despliegues de todo el equipo, y la contraseña de un servidor existente no debe poder
///    reapuntarse a otro host sin que lo revise un administrador.
///  · Si Operaciones se equivoca al capturar, un Admin lo corrige. Regla sin excepciones: más fácil
///    de explicar y de auditar que una ventana temporal.
///
/// Toda mutación queda en bitácora con el estado antes y después, <b>sin la contraseña</b>.
///
/// <para><b>La diferencia que impone la web.</b> En el escritorio el formulario de edición traía la
/// contraseña cargada porque el valor no salía del proceso. Aquí no puede salir nunca hacia el
/// navegador, así que el formulario llega vacío y una contraseña vacía significa «déjala como
/// está». Sin esa regla, corregir la ruta remota de un servidor lo dejaría sin credenciales.</para>
/// </summary>
public class DeploymentTargetService(AppDbContext db, ICurrentUser quien, AuditService bitacora)
{
    public static bool PuedeCrear(ICurrentUser u) => u.IsAdmin || u.IsOperaciones;
    public static bool PuedeEditar(ICurrentUser u) => u.IsAdmin;
    public static bool PuedeEliminar(ICurrentUser u) => u.IsAdmin;

    /// <summary>Instantánea para la bitácora. Nunca incluye la contraseña, ni cifrada.</summary>
    private static object Instantanea(DeploymentTarget t) => new
    {
        t.Id, t.Nombre, t.Host, t.Puerto, t.Usuario, t.RutaRemota, t.URL, t.IsActive
    };

    public async Task<(bool ok, string mensaje)> CrearAsync(
        GuardarServidorRequest datos, CancellationToken ct = default)
    {
        AuthorizationGuard.RequireAdminOrOperaciones(quien);

        if (string.IsNullOrWhiteSpace(datos.Contrasena))
            return (false, "La contraseña es obligatoria al dar de alta un servidor.");

        var nuevo = new DeploymentTarget
        {
            Nombre = (datos.Nombre ?? "").Trim(),
            Host = (datos.Host ?? "").Trim(),
            Puerto = datos.Puerto,
            Usuario = (datos.Usuario ?? "").Trim(),
            RutaRemota = (datos.RutaRemota ?? "").Trim(),
            URL = string.IsNullOrWhiteSpace(datos.Url) ? null : datos.Url.Trim(),
            IsActive = true
        };

        var error = await ValidarAsync(nuevo, esNuevo: true, ct);
        if (error != null)
        {
            await bitacora.RecordDeniedAsync(AuditAction.Create, "DeploymentTarget", null,
                $"Alta rechazada: {error}", ct: ct);
            return (false, error);
        }

        // Se cifra con el esquema COMPARTIDO con el escritorio, no con Data Protection: hasta el
        // corte las dos aplicaciones despliegan contra los mismos servidores leyendo esta misma fila.
        nuevo.Contrasena = ProtectorPortable.Cifrar(datos.Contrasena!);

        db.DeploymentTargets.Add(nuevo);
        await db.SaveChangesAsync(ct);

        await bitacora.RecordDetailedAsync(AuditAction.Create, "DeploymentTarget", nuevo.Id.ToString(),
            $"Servidor dado de alta: {nuevo.Nombre} ({nuevo.Host}:{nuevo.Puerto}{nuevo.RutaRemota})",
            AuditOutcome.Exito, newValues: Instantanea(nuevo), ct: ct);

        return (true, "Servidor creado. Su contraseña quedó cifrada en la base compartida: el resto " +
                      "del equipo puede desplegar con ella sin volver a capturarla.");
    }

    public async Task<(bool ok, string mensaje)> EditarAsync(
        int servidorId, GuardarServidorRequest datos, CancellationToken ct = default)
    {
        if (!PuedeEditar(quien))
        {
            await bitacora.RecordDeniedAsync(AuditAction.Update, "DeploymentTarget", servidorId.ToString(),
                "Intento de editar un servidor sin ser líder.", ct: ct);
            throw new AuthorizationException(
                "Solo un líder puede modificar un servidor existente. " +
                "Si hay un dato mal capturado, pídele que lo corrija.");
        }

        var servidor = await db.DeploymentTargets.FirstOrDefaultAsync(t => t.Id == servidorId, ct);
        if (servidor == null) return (false, "Ese servidor ya no existe: alguien lo eliminó.");

        // El estado previo se captura ANTES de tocar la entidad: después ya no existe.
        var previo = Instantanea(servidor);

        servidor.Nombre = (datos.Nombre ?? "").Trim();
        servidor.Host = (datos.Host ?? "").Trim();
        servidor.Puerto = datos.Puerto;
        servidor.Usuario = (datos.Usuario ?? "").Trim();
        servidor.RutaRemota = (datos.RutaRemota ?? "").Trim();
        servidor.URL = string.IsNullOrWhiteSpace(datos.Url) ? null : datos.Url.Trim();

        var error = await ValidarAsync(servidor, esNuevo: false, ct);
        if (error != null)
        {
            // Se descartan los cambios en memoria para que un segundo intento no arrastre lo inválido.
            await db.Entry(servidor).ReloadAsync(ct);
            return (false, error);
        }

        // Vacía = conservar la que ya tiene. Ver la nota de la clase: la contraseña no viaja de vuelta
        // al navegador, así que el formulario no puede reenviarla.
        bool cambioContrasena = !string.IsNullOrWhiteSpace(datos.Contrasena);
        if (cambioContrasena) servidor.Contrasena = ProtectorPortable.Cifrar(datos.Contrasena!);

        await db.SaveChangesAsync(ct);

        await bitacora.RecordDetailedAsync(AuditAction.Update, "DeploymentTarget", servidor.Id.ToString(),
            $"Servidor modificado: {servidor.Nombre}" + (cambioContrasena ? " (contraseña reemplazada)" : ""),
            AuditOutcome.Exito, oldValues: previo, newValues: Instantanea(servidor), ct: ct);

        return (true, cambioContrasena
            ? "Servidor actualizado, con contraseña nueva."
            : "Servidor actualizado. La contraseña se dejó como estaba.");
    }

    /// <summary>
    /// Baja LÓGICA. El borrado físico dejaba la bitácora apuntando a un identificador inexistente y
    /// rompía la trazabilidad de los despliegues históricos que lo usaron.
    /// </summary>
    public async Task<(bool ok, string mensaje)> DesactivarAsync(
        int servidorId, string? motivo, CancellationToken ct = default)
    {
        if (!PuedeEliminar(quien))
        {
            await bitacora.RecordDeniedAsync(AuditAction.Delete, "DeploymentTarget", servidorId.ToString(),
                "Intento de dar de baja un servidor sin ser líder.", ct: ct);
            throw new AuthorizationException("Solo un líder puede dar de baja un servidor.");
        }

        var servidor = await db.DeploymentTargets.FirstOrDefaultAsync(t => t.Id == servidorId, ct);
        if (servidor == null) return (false, "El servidor ya no existe.");
        if (!servidor.IsActive) return (false, "El servidor ya estaba dado de baja.");

        var previo = Instantanea(servidor);
        servidor.IsActive = false;
        await db.SaveChangesAsync(ct);

        await bitacora.RecordDetailedAsync(AuditAction.Delete, "DeploymentTarget", servidor.Id.ToString(),
            $"Servidor dado de baja: {servidor.Nombre}" +
            (string.IsNullOrWhiteSpace(motivo) ? "" : $" — {motivo.Trim()}"),
            AuditOutcome.Exito, oldValues: previo, newValues: Instantanea(servidor), ct: ct);

        return (true, "Servidor dado de baja. Su historial de despliegues se conserva.");
    }

    public async Task<(bool ok, string mensaje)> ReactivarAsync(int servidorId, CancellationToken ct = default)
    {
        if (!PuedeEditar(quien))
            throw new AuthorizationException("Solo un líder puede reactivar un servidor.");

        var servidor = await db.DeploymentTargets.FirstOrDefaultAsync(t => t.Id == servidorId, ct);
        if (servidor == null) return (false, "El servidor ya no existe.");
        if (servidor.IsActive) return (false, "El servidor ya estaba activo.");

        var previo = Instantanea(servidor);
        servidor.IsActive = true;
        await db.SaveChangesAsync(ct);

        await bitacora.RecordDetailedAsync(AuditAction.Update, "DeploymentTarget", servidor.Id.ToString(),
            $"Servidor reactivado: {servidor.Nombre}", AuditOutcome.Exito, previo, Instantanea(servidor), ct: ct);

        return (true, "Servidor reactivado.");
    }

    private async Task<string?> ValidarAsync(DeploymentTarget t, bool esNuevo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(t.Nombre)) return "El nombre es obligatorio.";
        if (string.IsNullOrWhiteSpace(t.Host)) return "El host es obligatorio.";
        if (t.Puerto is < 1 or > 65535) return "El puerto debe estar entre 1 y 65535.";
        if (string.IsNullOrWhiteSpace(t.Usuario)) return "El usuario es obligatorio.";
        if (string.IsNullOrWhiteSpace(t.RutaRemota)) return "La ruta remota es obligatoria.";

        // Dos servidores con el mismo nombre hacen imposible leer la bitácora después.
        bool duplicado = esNuevo
            ? await db.DeploymentTargets.AnyAsync(x => x.Nombre == t.Nombre, ct)
            : await db.DeploymentTargets.AnyAsync(x => x.Nombre == t.Nombre && x.Id != t.Id, ct);

        return duplicado ? $"Ya existe un servidor llamado «{t.Nombre}»." : null;
    }
}
