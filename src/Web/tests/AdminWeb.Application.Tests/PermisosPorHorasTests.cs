using AdminWeb.Application.Services;
using AdminWeb.Domain.Entities;
using AdminWeb.Domain.Security;
using AdminWeb.Infrastructure.Data;
using AdminWeb.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// PERMISOS POR HORAS: un tramo de un solo día —«de 9:00 a 11:00»— en vez del día entero.
///
/// <para>Lo que se protege aquí son tres cosas, y la tercera es la que más importa:</para>
///
/// <para>1. <b>Las cuentas.</b> Cuántas horas es un tramo y cómo se escribe, que es lo que acaba en
/// la pantalla del líder y en la bitácora.</para>
///
/// <para>2. <b>Las validaciones.</b> Un tramo al revés, de cero, más largo que la jornada, a medias
/// —una hora sí y la otra no— o pisando un permiso que ya existe ese día. Todas viven en el
/// SERVICIO y no en la pantalla, porque a la API se la puede llamar sin pasar por el navegador.</para>
///
/// <para>3. <b>Que los permisos de DÍAS COMPLETOS se comporten exactamente igual que antes.</b> Son
/// todo el histórico y son la forma en que se sigue pidiendo casi todo; un cambio que los tocara de
/// refilón no se vería hasta que alguien comparara una cifra de fin de año con la del año pasado. Por
/// eso hay una sección entera dedicada a comprobar que <b>no cambió nada</b>, incluida la frase que
/// se escribe en la bitácora, que es la que se lee dentro de un año.</para>
/// </summary>
public class PermisosPorHorasTests
{
    private const int AdminUserId = 900;
    private const int DevUserId = 10;

    private static readonly DateTime Dia = new(2026, 9, 10);

    private static LeaveRequestService Svc(AppDbContext db, ICurrentUser cu) =>
        new(db, cu, new AuditService(db, cu, new OrigenDePrueba()));

    private static (AppDbContext db, Developer dev, ICurrentUser yo, ICurrentUser jefa) Entorno()
    {
        var db = TestDb.New();
        var dev = new Developer { FullName = "Ana", IsActive = true };
        db.Developers.Add(dev);
        db.SaveChanges();

        return (db, dev,
                UsuarioDePrueba.Como(UserRole.Desarrollador, developerId: dev.Id, userId: DevUserId),
                UsuarioDePrueba.Como(UserRole.Admin, developerId: null, userId: AdminUserId));
    }

    /// <summary>Un borrador por horas. Sin tramo —los dos nulos— sale un permiso de día completo.</summary>
    private static LeaveRequest Borrador(Developer dev, TimeOnly? desde, TimeOnly? hasta,
                                         int dias = 1, DateTime? dia = null) => new()
    {
        DeveloperId = dev.Id,
        Type = LeaveType.CitaMedica,
        Date = dia ?? Dia,
        DaysCount = dias,
        HoraInicio = desde,
        HoraFin = hasta,
        Reason = "Cita con el dentista"
    };

    private static TimeOnly H(int hora, int minuto = 0) => new(hora, minuto);

    // ── Las cuentas ──────────────────────────────────────────────────────────────

    [Fact]
    public void UnTramo_SabeCuantasHorasEs()
    {
        Assert.Equal(2m, LeaveRequestService.Horas(H(9), H(11)));
        Assert.Equal(1.5m, LeaveRequestService.Horas(H(9), H(10, 30)));
        Assert.Equal(0.25m, LeaveRequestService.Horas(H(9), H(9, 15)));
    }

    /// <summary>
    /// Un permiso de día completo son CERO horas, no ocho. Convertir la jornada en horas obligaría a
    /// decidir cuánto dura la de cada persona —dato que la ficha no tiene— y ese número inventado
    /// acabaría sumándose en el indicador del año.
    /// </summary>
    [Fact]
    public void UnPermisoDeDiaCompleto_NoTieneHoras()
    {
        Assert.Equal(0m, LeaveRequestService.Horas(null, null));
        Assert.False(LeaveRequestService.EsPorHoras(null, null));
        Assert.True(LeaveRequestService.EsPorHoras(H(9), H(11)));
    }

    [Fact]
    public void LaDuracion_DeUnTramo_SeEscribeParaLeerse()
    {
        Assert.Equal("2 h (de 09:00 a 11:00)", LeaveRequestService.Duracion(1, H(9), H(11)));
        Assert.Equal("1 h 30 min (de 09:00 a 10:30)", LeaveRequestService.Duracion(1, H(9), H(10, 30)));
        Assert.Equal("45 min (de 13:15 a 14:00)", LeaveRequestService.Duracion(1, H(13, 15), H(14)));

        // Siempre en 24 h y con dos dígitos: es como se habla de una cita aquí, y así no depende de
        // la cultura del servidor que atienda la petición.
        Assert.Equal("8 h (de 07:00 a 15:00)", LeaveRequestService.Duracion(1, H(7), H(15)));
    }

    // ── Alta ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Solicitar_PorHoras_GuardaElTramo()
    {
        var (db, dev, yo, _) = Entorno();

        var (ok, _, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(9), H(11)));

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(H(9), g.HoraInicio);
        Assert.Equal(H(11), g.HoraFin);
        Assert.Equal(LeaveStatus.Pendiente, g.Status);

        // Y sigue siendo de un día: el tramo cabe en la jornada, así que el último día es el primero.
        Assert.Equal(1, g.DaysCount);
        Assert.Equal(Dia, g.EndDate);
    }

    [Fact]
    public async Task Solicitar_PorHoras_LaBitacoraDiceLasHorasYNoUnDia()
    {
        var (db, dev, yo, _) = Entorno();

        await Svc(db, yo).SolicitarAsync(Borrador(dev, H(9), H(11)));

        var asiento = db.AuditLogs.AsNoTracking().Single(a => a.EntityType == "LeaveRequest");
        Assert.Contains("2 h (de 09:00 a 11:00)", asiento.Details!);
        Assert.DoesNotContain("día(s)", asiento.Details!);
    }

    [Fact]
    public async Task RegistrarPorAdministrador_TambienPuedeSerPorHoras()
    {
        var (db, dev, _, jefa) = Entorno();

        var (ok, _, _) = await Svc(db, jefa).RegistrarPorAdministradorAsync(
            Borrador(dev, H(16), H(18)));

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(LeaveStatus.Aprobada, g.Status);   // registrarlo es concederlo, como siempre
        Assert.Equal(H(16), g.HoraInicio);
        Assert.Equal(H(18), g.HoraFin);
    }

    // ── Validaciones del tramo ───────────────────────────────────────────────────

    [Fact]
    public async Task NoSePuedePedirUnTramoAlReves()
    {
        var (db, dev, yo, _) = Entorno();

        var (ok, mensaje, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(13), H(11)));

        Assert.False(ok);
        Assert.Contains("al revés", mensaje);
        Assert.Empty(db.LeaveRequests);
    }

    [Fact]
    public async Task NoSePuedePedirUnTramoDeCeroHoras()
    {
        var (db, dev, yo, _) = Entorno();

        var (ok, mensaje, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(11), H(11)));

        Assert.False(ok);
        Assert.Contains("no dura nada", mensaje);
        Assert.Empty(db.LeaveRequests);
    }

    [Fact]
    public async Task NoSePuedePedirMasHorasDeLasQueTieneUnaJornada()
    {
        var (db, dev, yo, _) = Entorno();

        // Nueve horas: una más que la jornada.
        var (ok, mensaje, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(8), H(17)));

        Assert.False(ok);
        Assert.Contains("jornada", mensaje);
        Assert.Empty(db.LeaveRequests);
    }

    /// <summary>La jornada COMPLETA sí cabe: es el límite, no algo que ya se pasa.</summary>
    [Fact]
    public async Task UnTramoDeUnaJornadaEnteraSeAcepta()
    {
        var (db, dev, yo, _) = Entorno();

        var (ok, _, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(8), H(16)));

        Assert.True(ok);
        Assert.Equal(LeaveRequestService.HorasDeLaJornada,
                     LeaveRequestService.Horas(H(8), H(16)));
    }

    /// <summary>
    /// Media hora sin la otra media no es un tramo: sin las dos no se sabe cuánto dura, y guardarlo
    /// así dejaría una fila que ninguna pantalla puede contar.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnaHoraSolaNoEsUnTramo(bool faltaElFin)
    {
        var (db, dev, yo, _) = Entorno();

        var borrador = faltaElFin
            ? Borrador(dev, H(9), null)
            : Borrador(dev, null, H(11));

        var (ok, mensaje, _) = await Svc(db, yo).SolicitarAsync(borrador);

        Assert.False(ok);
        Assert.Contains(faltaElFin ? "hora de fin" : "hora de inicio", mensaje);
        Assert.Empty(db.LeaveRequests);
    }

    /// <summary>
    /// «De 9 a 11 durante tres días» no es un tramo, son tres, y guardarlo como uno haría que la
    /// ficha dijera 2 h donde hubo 6.
    /// </summary>
    [Fact]
    public async Task UnTramoNoPuedeAbarcarVariosDias()
    {
        var (db, dev, yo, _) = Entorno();

        var (ok, mensaje, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(9), H(11), dias: 3));

        Assert.False(ok);
        Assert.Contains("un solo día", mensaje);
        Assert.Empty(db.LeaveRequests);
    }

    /// <summary>
    /// Los segundos se recortan al minuto. Solo pueden llegar llamando a la API a mano, y guardarlos
    /// dejaría la ficha diciendo «2 h» mientras el total del año suma 2,0003.
    /// </summary>
    [Fact]
    public async Task UnTramoConSegundos_SeGuardaAlMinuto()
    {
        var (db, dev, yo, _) = Entorno();

        var (ok, _, _) = await Svc(db, yo).SolicitarAsync(
            Borrador(dev, new TimeOnly(9, 0, 30), new TimeOnly(11, 0, 45)));

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(H(9), g.HoraInicio);
        Assert.Equal(H(11), g.HoraFin);
        Assert.Equal(2m, LeaveRequestService.Horas(g.HoraInicio, g.HoraFin));
    }

    // ── No pisar lo que ya hay ───────────────────────────────────────────────────

    [Fact]
    public async Task NoSePuedePedirUnTramoQuePisaOtroDelMismoDia()
    {
        var (db, dev, yo, _) = Entorno();
        await Svc(db, yo).SolicitarAsync(Borrador(dev, H(9), H(11)));

        var (ok, mensaje, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(10), H(12)));

        Assert.False(ok);
        Assert.Contains("se pisa", mensaje);
        Assert.Contains("de 09:00 a 11:00", mensaje);
        Assert.Single(db.LeaveRequests);
    }

    /// <summary>Tocarse por el extremo no es pisarse: son dos ausencias seguidas del mismo día.</summary>
    [Fact]
    public async Task DosTramosQueSoloSeTocanPorElExtremoSonValidos()
    {
        var (db, dev, yo, _) = Entorno();
        await Svc(db, yo).SolicitarAsync(Borrador(dev, H(9), H(11)));

        var (ok, _, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(11), H(13)));

        Assert.True(ok);
        Assert.Equal(2, db.LeaveRequests.Count());
    }

    [Fact]
    public async Task UnTramoDeOtroDiaNoEstorba()
    {
        var (db, dev, yo, _) = Entorno();
        await Svc(db, yo).SolicitarAsync(Borrador(dev, H(9), H(11)));

        var (ok, _, _) = await Svc(db, yo).SolicitarAsync(
            Borrador(dev, H(9), H(11), dia: Dia.AddDays(1)));

        Assert.True(ok);
        Assert.Equal(2, db.LeaveRequests.Count());
    }

    /// <summary>
    /// Un permiso de DÍAS COMPLETOS ocupa el día entero, aunque empezara tres días antes: pedir unas
    /// horas de un día que ya está cubierto no cambiaría nada.
    /// </summary>
    [Fact]
    public async Task NoSePuedePedirUnTramoDeUnDiaQueYaEstaCubiertoPorUnPermisoLargo()
    {
        var (db, dev, yo, _) = Entorno();
        await Svc(db, yo).SolicitarAsync(
            Borrador(dev, null, null, dias: 3, dia: Dia.AddDays(-2)));   // cubre el día 8, 9 y 10

        var (ok, mensaje, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(9), H(11)));

        Assert.False(ok);
        Assert.Contains("día completo", mensaje);
        Assert.Single(db.LeaveRequests);
    }

    /// <summary>
    /// Lo rechazado y lo cancelado no ocupan nada. Tratarlos como si ocuparan impediría volver a
    /// pedir justo lo que a uno le negaron, que es lo que uno hace en cuanto le dicen «pídelo otro
    /// día».
    /// </summary>
    [Theory]
    [InlineData(LeaveStatus.Rechazada)]
    [InlineData(LeaveStatus.Cancelada)]
    public async Task UnPermisoQueYaNoEstaVivoNoEstorba(LeaveStatus estado)
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, previo) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(9), H(11)));

        if (estado == LeaveStatus.Rechazada)
            await Svc(db, jefa).RechazarAsync(previo!.Id, "esa mañana hay entrega");
        else
            await Svc(db, yo).CancelarAsync(previo!.Id);

        var (ok, _, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(10), H(12)));

        Assert.True(ok);
        Assert.Equal(2, db.LeaveRequests.Count());
    }

    [Fact]
    public async Task ElPermisoDeOtraPersonaNoEstorba()
    {
        var (db, dev, yo, jefa) = Entorno();
        var otro = new Developer { FullName = "Beto", IsActive = true };
        db.Developers.Add(otro);
        db.SaveChanges();

        await Svc(db, jefa).RegistrarPorAdministradorAsync(Borrador(otro, H(9), H(11)));

        var (ok, _, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(9), H(11)));

        Assert.True(ok);
    }

    /// <summary>
    /// Al corregir, la propia solicitud no se cuenta como el permiso que ya ocupa ese tramo. Sin
    /// excluirla, guardar una corrección que no toca las horas sería imposible.
    /// </summary>
    [Fact]
    public async Task CorregirUnTramoNoSePisaASiMismo()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(9), H(11)));

        var (ok, _) = await Svc(db, jefa).CorregirPendienteAsync(
            sol!.Id, LeaveType.CitaMedica, Dia, 1, "Se movió la cita", null, H(10), H(12));

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(H(10), g.HoraInicio);
        Assert.Equal(H(12), g.HoraFin);
    }

    [Fact]
    public async Task EditarUnTramoNoSePisaASiMismo()
    {
        var (db, dev, yo, _) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(9), H(11)));

        var (ok, _) = await Svc(db, yo).EditarAsync(sol!.Id, Borrador(dev, H(9), H(12)));

        Assert.True(ok);
        Assert.Equal(H(12), db.LeaveRequests.AsNoTracking().Single().HoraFin);
    }

    /// <summary>
    /// Corregir aplica las MISMAS reglas del alta: no es una puerta de atrás para dejar escrito un
    /// tramo que la solicitud original no habría permitido.
    /// </summary>
    [Fact]
    public async Task CorregirAplicaLosMismosTopesDelTramo()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(9), H(11)));

        var (ok, mensaje) = await Svc(db, jefa).CorregirPendienteAsync(
            sol!.Id, LeaveType.CitaMedica, Dia, 1, "motivo", null, H(8), H(20));

        Assert.False(ok);
        Assert.Contains("jornada", mensaje);
        Assert.Equal(H(11), db.LeaveRequests.AsNoTracking().Single().HoraFin);   // intacto
    }

    /// <summary>
    /// Quitarle el tramo devuelve el permiso a día completo. Es la única forma de enmendar uno que se
    /// capturó por error como horas, y por eso esas dos columnas se escriben aunque lleguen vacías.
    /// </summary>
    [Fact]
    public async Task CorregirSinTramo_DevuelveElPermisoADiaCompleto()
    {
        var (db, dev, yo, jefa) = Entorno();
        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(9), H(11)));

        var (ok, _) = await Svc(db, jefa).CorregirPendienteAsync(
            sol!.Id, LeaveType.CitaMedica, Dia, 2, "Al final es todo el día", null);

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Null(g.HoraInicio);
        Assert.Null(g.HoraFin);
        Assert.Equal(2, g.DaysCount);
    }

    /// <summary>
    /// Colgar el justificante NO puede convertir un permiso de horas en uno de día completo:
    /// <c>EditarAsync</c> reemplaza la solicitud entera, así que el tramo se reenvía como los demás
    /// campos. Es el mismo defecto que ya se cuidaba con el motivo y las fechas.
    /// </summary>
    [Fact]
    public async Task AdjuntarElJustificanteNoLeQuitaLasHoras()
    {
        var (db, dev, yo, _) = Entorno();
        var auditoria = new AuditService(db, yo, new OrigenDePrueba());
        var ausencias = new AusenciasService(db, yo, Svc(db, yo), auditoria);

        var (_, _, sol) = await Svc(db, yo).SolicitarAsync(Borrador(dev, H(9), H(11)));

        var (ok, _) = await ausencias.AdjuntarJustificanteAsync(sol!.Id, [1, 2, 3], "receta.pdf");

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Equal(H(9), g.HoraInicio);
        Assert.Equal(H(11), g.HoraFin);
        Assert.Equal("receta.pdf", g.AttachmentFileName);
    }

    // ── Lo que ve la pantalla ────────────────────────────────────────────────────

    /// <summary>
    /// El indicador del año cuenta los DÍAS de los permisos de día completo y las HORAS de los
    /// tramos, cada cosa en su renglón. Un tramo de dos horas no se convierte en «medio día», que es
    /// la conversión que obligaría a inventar la jornada de cada persona y dejaría el contador de
    /// días sin poderse comparar con el de los años anteriores.
    /// </summary>
    [Fact]
    public async Task ElResumenCuentaLosDiasYLasHorasPorSeparado()
    {
        var (db, dev, yo, jefa) = Entorno();
        int anio = DateTime.Today.Year;

        var permisos = Svc(db, yo);
        var ausencias = new AusenciasService(db, yo, permisos,
                                             new AuditService(db, yo, new OrigenDePrueba()));

        // Dos días completos y dos tramos, todos aprobados y de este año.
        await Svc(db, jefa).RegistrarPorAdministradorAsync(
            Borrador(dev, null, null, dias: 2, dia: new DateTime(anio, 3, 2)));
        await Svc(db, jefa).RegistrarPorAdministradorAsync(
            Borrador(dev, H(9), H(11), dia: new DateTime(anio, 3, 5)));
        await Svc(db, jefa).RegistrarPorAdministradorAsync(
            Borrador(dev, H(13), H(14, 30), dia: new DateTime(anio, 3, 6)));

        var mios = await ausencias.MisPermisosAsync();

        Assert.Equal(3, mios.Resumen.AprobadosEsteAnio);
        Assert.Equal(2, mios.Resumen.DiasAprobadosEsteAnio);     // solo el de días completos
        Assert.Equal(3.5m, mios.Resumen.HorasAprobadasEsteAnio); // 2 h + 1 h 30 min
    }

    [Fact]
    public async Task CadaPermisoViajaConSuTramoYSuFraseDeDuracion()
    {
        var (db, dev, yo, jefa) = Entorno();
        var ausencias = new AusenciasService(db, yo, Svc(db, yo),
                                             new AuditService(db, yo, new OrigenDePrueba()));

        await Svc(db, jefa).RegistrarPorAdministradorAsync(Borrador(dev, H(9), H(11)));
        await Svc(db, jefa).RegistrarPorAdministradorAsync(
            Borrador(dev, null, null, dias: 2, dia: Dia.AddDays(5)));

        var mios = await ausencias.MisPermisosAsync();
        var porHoras = mios.Solicitudes.Single(s => s.PorHoras);
        var porDias = mios.Solicitudes.Single(s => !s.PorHoras);

        Assert.Equal(H(9), porHoras.HoraInicio);
        Assert.Equal(H(11), porHoras.HoraFin);
        Assert.Equal(2m, porHoras.Horas);
        Assert.Equal("2 h (de 09:00 a 11:00)", porHoras.Duracion);
        // Y el último día es el mismo: el tramo no se sale del día en el que cae.
        Assert.Equal(porHoras.Desde, porHoras.Hasta);

        Assert.Null(porDias.HoraInicio);
        Assert.Equal(0m, porDias.Horas);
        Assert.Equal("2 día(s)", porDias.Duracion);

        // El tope de la jornada viaja con la pantalla para que el formulario avise antes de mandar un
        // tramo que la API va a rechazar.
        Assert.Equal(LeaveRequestService.HorasDeLaJornada, mios.HorasDeLaJornada);
    }

    // ── Que a los permisos de DÍAS COMPLETOS no les pasó nada ────────────────────
    //
    // Esta sección no prueba nada nuevo: prueba que lo viejo sigue igual. Son la forma en que se pide
    // casi todo y todo el histórico, y lo que se les rompiera aquí no se vería hasta que alguien
    // comparara una cifra de fin de año con la del año anterior.

    [Fact]
    public async Task UnPermisoDeDiasCompletos_SeGuardaSinTramoYCuentaSusDias()
    {
        var (db, dev, yo, _) = Entorno();

        var (ok, _, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, null, null, dias: 3));

        Assert.True(ok);
        var g = db.LeaveRequests.AsNoTracking().Single();
        Assert.Null(g.HoraInicio);
        Assert.Null(g.HoraFin);
        Assert.Equal(3, g.DaysCount);
        Assert.Equal(Dia.AddDays(2), g.EndDate);
        Assert.Equal("3 día(s)", LeaveRequestService.Duracion(g.DaysCount, g.HoraInicio, g.HoraFin));
    }

    /// <summary>
    /// La frase de la bitácora de un permiso de días es LA MISMA de siempre. Se comprueba entera y a
    /// propósito: ahí está escrito el histórico, y si el texto cambiara, los asientos de antes y los
    /// de después dejarían de leerse —y de buscarse— igual.
    /// </summary>
    [Fact]
    public async Task LaBitacoraDeUnPermisoDeDias_DiceExactamenteLoDeSiempre()
    {
        var (db, dev, yo, _) = Entorno();

        await Svc(db, yo).SolicitarAsync(Borrador(dev, null, null, dias: 2));

        // La fecha se deja fuera de la comparación a propósito: la escribe «dd/MM/yyyy», cuyo
        // separador lo pone la cultura de la máquina, y eso no es lo que se está protegiendo aquí.
        var asiento = db.AuditLogs.AsNoTracking().Single(a => a.EntityType == "LeaveRequest");
        Assert.StartsWith("Permiso solicitado: 🩺 Cita médica ", asiento.Details!);
        Assert.EndsWith(" (2 día(s))", asiento.Details!);
    }

    /// <summary>
    /// A los permisos de días completos NO se les puso el veto de solaparse, y no es un olvido: es lo
    /// que llevan años haciendo. El líder captura una incapacidad que se alarga sobre un permiso ya
    /// concedido y eso tiene que seguir pudiéndose; quien decide es él.
    /// </summary>
    [Fact]
    public async Task DosPermisosDeDiasCompletosQueSeSolapanSiguenSiendoPosibles()
    {
        var (db, dev, _, jefa) = Entorno();

        await Svc(db, jefa).RegistrarPorAdministradorAsync(Borrador(dev, null, null, dias: 3));
        var (ok, _, _) = await Svc(db, jefa).RegistrarPorAdministradorAsync(
            Borrador(dev, null, null, dias: 2, dia: Dia.AddDays(1)));

        Assert.True(ok);
        Assert.Equal(2, db.LeaveRequests.Count());
    }

    /// <summary>Los topes de días no se movieron: siguen siendo de 1 a <c>MaxDias</c>.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(LeaveRequestService.MaxDias + 1)]
    public async Task LosTopesDeDiasSiguenIgual(int dias)
    {
        var (db, dev, yo, _) = Entorno();

        var (ok, _, _) = await Svc(db, yo).SolicitarAsync(Borrador(dev, null, null, dias: dias));

        Assert.False(ok);
        Assert.Empty(db.LeaveRequests);
    }
}
