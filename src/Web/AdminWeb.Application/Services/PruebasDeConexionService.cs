using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Dtos.Administracion;
using Microsoft.EntityFrameworkCore;

namespace AdminWeb.Application.Services;

/// <summary>
/// Los «probar conexión» de la pantalla de configuración: Azure Blob, SQL y correo.
///
/// <para><b>Se prueba lo que está GUARDADO, nunca valores sueltos del navegador.</b> Es el cambio de
/// fondo respecto al escritorio, donde se podía teclear una cadena y probarla antes de guardarla para
/// no descubrir después que era la equivocada. Aquí no, y no es un olvido: un endpoint que acepte una
/// cadena de conexión por parámetro es un probador de credenciales ajenas —de Azure, de SQL o de un
/// buzón— disponible para cualquiera con una sesión de líder. Se guarda primero y se prueba después;
/// lo que se pierde es una comodidad, lo que se gana es que esta ruta no sirva para nada más que para
/// comprobar la configuración de esta instalación.</para>
///
/// <para>Vive aquí y no repartido entre los tres servicios porque la pantalla pregunta lo mismo tres
/// veces y la regla de arriba es una sola. Cada prueba concreta la sigue haciendo quien sabe hacerla:
/// <see cref="AlmacenamientoService"/> y <see cref="IngestaDeCorreoService"/> ya la tenían, y la de
/// SQL se hace aquí mismo contra el contexto de datos, que es quien sabe a qué base está hablando.</para>
///
/// <para><b>El mensaje que sale no repite ningún secreto.</b> Lo escribe el cliente de cada
/// integración, que traduce el fallo a algo accionable; ninguno reenvía el texto crudo de la
/// excepción ni parte de la cadena con la que se conectó.</para>
/// </summary>
public class PruebasDeConexionService(
    AppDbContext db,
    AlmacenamientoService almacenamiento,
    IngestaDeCorreoService correo,
    ICurrentUser usuarioActual)
{
    /// <summary>
    /// Comprueba una integración contra el servicio real y devuelve el mensaje que se enseña tal cual.
    ///
    /// <para>Un «no conecta» es una RESPUESTA, no un fallo de la petición: quien pulsa el botón
    /// pregunta si la configuración sirve, y «no, la contraseña no se aceptó» es exactamente lo que
    /// vino a saber.</para>
    /// </summary>
    public async Task<(bool ok, string mensaje)> ProbarAsync(
        PruebaDeConexion que, CancellationToken ct = default)
    {
        // La guarda va aquí además de dentro de cada servicio: a esta ruta se llega sin pasar por
        // ningún menú, y la de SQL no tendría ninguna otra —SettingsService.ObtenerAsync no lleva
        // guarda porque es la lectura interna del servidor.
        AuthorizationGuard.RequireAdmin(usuarioActual);

        return que switch
        {
            PruebaDeConexion.Blob => await almacenamiento.ProbarConexionAsync(ct),
            PruebaDeConexion.Correo => await correo.ProbarConexionAsync(ct),
            PruebaDeConexion.Sql => await ProbarSqlAsync(ct),
            _ => (false, "Esa comprobación no existe.")
        };
    }

    /// <summary>
    /// La base que está usando ESTA aplicación ahora mismo.
    ///
    /// <para><b>Antes probaba otra cosa, y ese era el problema.</b> Leía la clave
    /// <c>AzureSqlConnectionString</c> de la tabla de configuración diciendo que era «la que usa el
    /// escritorio». No lo es: en el escritorio esa constante está declarada y <b>no la lee ni la
    /// escribe nadie</b> —su conexión sale de <c>dbprovider.json</c> y del recurso incrustado—, y la
    /// web tampoco la usa: la suya viene de la configuración del servidor. Era un campo que no
    /// gobernaba nada y una prueba que no probaba nada, con el agravante de que alguien podía pasarse
    /// una tarde «arreglando» ahí una conexión rota sin que cambiara absolutamente nada.</para>
    ///
    /// <para>Ahora se prueba la conexión REAL del contexto de datos, que es la única pregunta útil:
    /// ¿puede este servidor hablar con su base? Sin cadena que capturar y sin nada que pueda quedar
    /// desalineado con lo que la aplicación usa de verdad.</para>
    /// </summary>
    private async Task<(bool ok, string mensaje)> ProbarSqlAsync(CancellationToken ct)
    {
        try
        {
            // Abrir y cerrar es la comprobación más barata que de verdad prueba algo. CanConnectAsync
            // habría bastado para el sí/no, pero se traga el motivo, y en esta pantalla el motivo es
            // justo lo que se viene a buscar.
            var conexion = db.Database.GetDbConnection();
            bool laAbriYo = conexion.State != System.Data.ConnectionState.Open;

            if (laAbriYo) await db.Database.OpenConnectionAsync(ct);
            try
            {
                return (true, $"Conectado a «{conexion.Database}» en {conexion.DataSource}.");
            }
            finally
            {
                if (laAbriYo) await db.Database.CloseConnectionAsync();
            }
        }
        catch (Exception ex)
        {
            // Del error se cita el TIPO y el mensaje, que en SqlException ya viene redactado para
            // humanos («Login failed for user…», «network-related or instance-specific error»). Lo
            // que no se cita nunca es la cadena: el analizador de conexiones, cuando se queja del
            // formato, entrecomilla el trozo que no entendió, y ese trozo puede ser la contraseña.
            return (false, $"No se pudo conectar a la base: {ex.Message}");
        }
    }
}
