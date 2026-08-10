using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Empate de un work item de DevOps con un desarrollador.
///
/// Los casos salen de datos reales: el nombre para mostrar de DevOps viene sin acentos («Daniel
/// Lopez») y a veces recortado («Jesus Canul»), así que el correo tiene que ser la llave que manda.
/// Portadas del escritorio sin cambiar un caso: si el empate se degradara, la pantalla «Mis tickets»
/// enseñaría trabajo de otra persona —o dejaría de enseñar el propio— sin que nada lo delatara.
/// </summary>
public class DevOpsIdentityMatcherTests
{
    private static Developer Dev(string nombre, string? correo) => new() { FullName = nombre, Email = correo };

    [Fact]
    public void ElCorreoManda_AunqueElNombreDifiera()
    {
        var dev = Dev("Jesus Abraham Canul", "jesus.canul@soltum.com.mx");

        // En DevOps el nombre viene recortado, pero el correo es el mismo.
        Assert.True(DevOpsIdentityMatcher.Corresponde("Jesus Canul", "jesus.canul@soltum.com.mx", dev));
    }

    [Fact]
    public void ElCorreoIgnoraMayusculas()
    {
        var dev = Dev("Angel Palma", "angel.palma@soltum.com.mx");
        Assert.True(DevOpsIdentityMatcher.Corresponde("Angel Palma", "ANGEL.PALMA@SOLTUM.COM.MX", dev));
    }

    [Fact]
    public void CorreosDistintos_NoEmpatan_AunqueElNombreSeParezca()
    {
        var dev = Dev("Daniel López", "daniel.lopez@soltum.com.mx");

        // Otro Daniel: mismo primer nombre, correo distinto ⇒ NO es él, y no se cae al respaldo.
        Assert.False(DevOpsIdentityMatcher.Corresponde("Daniel Lopez", "daniel.mena@soltum.com.mx", dev));
    }

    [Fact]
    public void SinCorreoEnElTicket_CaeAlNombreIgnorandoAcentos()
    {
        var dev = Dev("Daniel López", "daniel.lopez@soltum.com.mx");

        // Ticket viejo, sincronizado antes de que existiera la columna del correo.
        Assert.True(DevOpsIdentityMatcher.Corresponde("Daniel Lopez", null, dev));
    }

    [Fact]
    public void SinCorreoEnElDesarrollador_CaeAlNombre()
    {
        var dev = Dev("María Alicia Chimal Kamul", null);
        Assert.True(DevOpsIdentityMatcher.Corresponde(
            "Maria Alicia Chimal Kamul", "maria.chimal@soltum.com.mx", dev));
    }

    [Fact]
    public void NombreDistinto_SinCorreos_NoEmpata()
    {
        var dev = Dev("Angel Palma", null);
        Assert.False(DevOpsIdentityMatcher.Corresponde("Victor Alexis May Poot", null, dev));
    }

    [Fact]
    public void DesarrolladorDePrueba_ConCorreoBasura_NoSeQuedaConTicketsAjenos()
    {
        // El desarrollador «test» (correo «test») no debe empatar con un ticket real: ambos traen
        // correo y son distintos, así que ni siquiera se intenta el respaldo por nombre.
        var prueba = Dev("test", "test");
        Assert.False(DevOpsIdentityMatcher.Corresponde(
            "Gerardo Tellez", "gerardo.tellez@soltum.com.mx", prueba));
    }

    [Fact]
    public void Buscar_DevuelveElPrimeroQueEmpata()
    {
        var devs = new[]
        {
            Dev("Angel Palma", "angel.palma@soltum.com.mx"),
            Dev("Daniel López", "daniel.lopez@soltum.com.mx"),
        };

        var encontrado = DevOpsIdentityMatcher.Buscar("Daniel Lopez", "daniel.lopez@soltum.com.mx", devs);

        Assert.NotNull(encontrado);
        Assert.Equal("Daniel López", encontrado!.FullName);
    }

    [Fact]
    public void Buscar_SinCoincidencia_DevuelveNulo()
    {
        var devs = new[] { Dev("Angel Palma", "angel.palma@soltum.com.mx") };
        Assert.Null(DevOpsIdentityMatcher.Buscar("Nadie Conocido", "nadie@x.com", devs));
    }
}
