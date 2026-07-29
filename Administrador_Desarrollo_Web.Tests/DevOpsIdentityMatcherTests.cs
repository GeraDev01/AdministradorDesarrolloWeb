using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Xunit;

namespace Administrador_Desarrollo_Web.Tests;

/// <summary>
/// Empate de un work item de DevOps con un desarrollador. Los casos salen de datos reales:
/// el nombre para mostrar de DevOps viene sin acentos («Daniel Lopez») y a veces recortado
/// («Jesus Canul»), así que el correo tiene que ser la llave que manda.
/// </summary>
public class DevOpsIdentityMatcherTests
{
    private static Developer Dev(string name, string? email) => new() { FullName = name, Email = email };

    [Fact]
    public void ElCorreoManda_AunqueElNombreDifiera()
    {
        var dev = Dev("Jesus Abraham Canul", "jesus.canul@soltum.com.mx");
        // En DevOps el nombre viene recortado, pero el correo es el mismo.
        Assert.True(DevOpsIdentityMatcher.Matches("Jesus Canul", "jesus.canul@soltum.com.mx", dev));
    }

    [Fact]
    public void ElCorreoEsCaseInsensitive()
    {
        var dev = Dev("Angel Palma", "angel.palma@soltum.com.mx");
        Assert.True(DevOpsIdentityMatcher.Matches("Angel Palma", "ANGEL.PALMA@SOLTUM.COM.MX", dev));
    }

    [Fact]
    public void CorreosDistintos_NoEmpatan_AunqueElNombreSeParezca()
    {
        var dev = Dev("Daniel López", "daniel.lopez@soltum.com.mx");
        // Otro Daniel: mismo primer nombre, correo distinto ⇒ NO es él.
        Assert.False(DevOpsIdentityMatcher.Matches("Daniel Lopez", "daniel.mena@soltum.com.mx", dev));
    }

    [Fact]
    public void SinCorreoEnElTicket_CaeAlNombreIgnorandoAcentos()
    {
        var dev = Dev("Daniel López", "daniel.lopez@soltum.com.mx");
        // Ticket viejo (sincronizado antes de guardar el correo): solo trae nombre, sin acento.
        Assert.True(DevOpsIdentityMatcher.Matches("Daniel Lopez", null, dev));
    }

    [Fact]
    public void SinCorreoEnElDesarrollador_CaeAlNombre()
    {
        var dev = Dev("María Alicia Chimal Kamul", null);
        Assert.True(DevOpsIdentityMatcher.Matches("Maria Alicia Chimal Kamul", "maria.chimal@soltum.com.mx", dev));
    }

    [Fact]
    public void NombreDistinto_SinCorreos_NoEmpata()
    {
        var dev = Dev("Angel Palma", null);
        Assert.False(DevOpsIdentityMatcher.Matches("Victor Alexis May Poot", null, dev));
    }

    [Fact]
    public void DesarrolladorDePrueba_ConCorreoBasura_NoRoba_TicketsAjenos()
    {
        // El desarrollador "test" (email "test") no debe empatar con un ticket real: ambos traen
        // correo y son distintos, así que ni siquiera se intenta el respaldo por nombre.
        var test = Dev("test", "test");
        Assert.False(DevOpsIdentityMatcher.Matches("Gerardo Tellez", "gerardo.tellez@soltum.com.mx", test));
    }

    [Fact]
    public void FindDevuelveElPrimeroQueEmpata()
    {
        var devs = new[]
        {
            Dev("Angel Palma", "angel.palma@soltum.com.mx"),
            Dev("Daniel López", "daniel.lopez@soltum.com.mx"),
        };
        var hit = DevOpsIdentityMatcher.Find("Daniel Lopez", "daniel.lopez@soltum.com.mx", devs);
        Assert.NotNull(hit);
        Assert.Equal("Daniel López", hit!.FullName);
    }

    [Fact]
    public void Find_SinCoincidencia_DevuelveNull()
    {
        var devs = new[] { Dev("Angel Palma", "angel.palma@soltum.com.mx") };
        Assert.Null(DevOpsIdentityMatcher.Find("Nadie Conocido", "nadie@x.com", devs));
    }
}
