using AdminWeb.Application.Services;
using AdminWeb.Shared.Enums;
using Xunit;

namespace AdminWeb.Application.Tests;

/// <summary>
/// Los dos cálculos puros que se portaron del escritorio: precisión de las estimaciones
/// (<see cref="EstimationStats"/>) y semáforo de capacidad (<see cref="CapacityStats"/>).
///
/// Se prueban los UMBRALES y los bordes, que es lo que decide el color de una fila y, con él, a quién
/// se le asigna el trabajo siguiente. Un ±20 % que se moviera al 25 % no rompería ninguna
/// compilación y cambiaría la lectura de todo el reporte.
/// </summary>
public class EstimacionYCapacidadTests
{
    private static EstimationInput Req(int id, decimal? estimadas, int segundos) =>
        new(id, $"Req {id}", estimadas, segundos, RequirementStatus.EnDesarrollo);

    private static int Horas(double h) => (int)(h * 3600);

    // ── Clasificación de la estimación ───────────────────────────────────────

    [Fact]
    public void Sin_horas_estimadas_no_se_puede_medir()
    {
        Assert.Equal(EstimationClass.SinEstimacion, EstimationStats.Clasificar(0, 10));
        Assert.Equal(EstimationClass.SinEstimacion, EstimationStats.Clasificar(-1, 10));
    }

    [Fact]
    public void Con_estimacion_pero_sin_tiempo_medido_queda_en_espera()
    {
        // No es «preciso» ni «sobreestimado»: todavía no hay nada que comparar, y contarlo como
        // acierto inflaría el porcentaje de precisión con trabajo que no ha empezado.
        Assert.Equal(EstimationClass.SinTiempo, EstimationStats.Clasificar(10, 0));
    }

    [Theory]
    // El ±20 % es INCLUSIVO en los extremos: 1.2 y 0.8 exactos siguen siendo precisos.
    [InlineData(10, 12, EstimationClass.Preciso)]
    [InlineData(10, 8, EstimationClass.Preciso)]
    [InlineData(10, 10, EstimationClass.Preciso)]
    [InlineData(10, 12.1, EstimationClass.Subestimado)]
    [InlineData(10, 7.9, EstimationClass.Sobreestimado)]
    public void El_umbral_de_precision_es_mas_menos_veinte_por_ciento(
        double estimadas, double reales, EstimationClass esperada) =>
        Assert.Equal(esperada, EstimationStats.Clasificar(estimadas, reales));

    [Fact]
    public void La_fila_convierte_segundos_a_horas_y_calcula_delta_y_ratio()
    {
        var fila = EstimationStats.Fila(Req(1, 10m, Horas(15)));

        Assert.Equal(10, fila.EstimateHrs);
        Assert.Equal(15, fila.ActualHrs);
        Assert.Equal(5, fila.DeltaHrs);
        Assert.Equal(1.5, fila.Ratio);
        Assert.Equal(EstimationClass.Subestimado, fila.Clase);
    }

    [Fact]
    public void Sin_tiempo_medido_no_hay_ratio_en_vez_de_un_cero()
    {
        // Un 0.00 en la columna se leería como «tardó cero», que es lo contrario de «aún no empieza».
        Assert.Null(EstimationStats.Fila(Req(1, 10m, 0)).Ratio);
    }

    // ── Resumen agregado ─────────────────────────────────────────────────────

    [Fact]
    public void El_resumen_solo_promedia_lo_que_ya_tiene_tiempo_medido()
    {
        var filas = EstimationStats.Filas(
        [
            Req(1, 10m, Horas(20)),   // ratio 2.0  · subestimado
            Req(2, 10m, Horas(10)),   // ratio 1.0  · preciso
            Req(3, 10m, Horas(5)),    // ratio 0.5  · sobreestimado
            Req(4, 10m, 0)            // sin tiempo: NO entra en el promedio ni en los conteos
        ]);

        var r = EstimationStats.Resumen(filas);

        Assert.Equal(3, r.ConDatos);
        Assert.Equal(1, r.Precisos);
        Assert.Equal(1, r.Subestimados);
        Assert.Equal(1, r.Sobreestimados);
        Assert.Equal(1.17, r.RatioPromedio);          // (2.0 + 1.0 + 0.5) / 3, redondeado
        Assert.Equal(40, r.HorasEstimadas);           // las horas SÍ suman las cuatro filas
        Assert.Equal(35, r.HorasReales);
    }

    [Fact]
    public void Un_resumen_sin_nada_medido_no_inventa_un_promedio()
    {
        var r = EstimationStats.Resumen(EstimationStats.Filas([Req(1, 10m, 0)]));

        Assert.Equal(0, r.ConDatos);
        Assert.Equal(0, r.RatioPromedio);   // quien pinta enseña «—» al ver ConDatos en cero
    }

    // ── Semáforo de capacidad ────────────────────────────────────────────────

    [Fact]
    public void Estar_de_vacaciones_gana_a_todo_lo_demas()
    {
        // La pregunta que responde la pantalla es «¿a quién le asigno esto AHORA?»: da igual cuánto
        // tenga abierto quien hoy no está.
        Assert.Equal(Disponibilidad.DeVacaciones,
            CapacityStats.Clasificar(deVacacionesHoy: true, abiertos: 9, horasPendientes: 200, capacidadHoras: 40));
    }

    [Theory]
    [InlineData(0, 0, Disponibilidad.Libre)]
    [InlineData(1, 10, Disponibilidad.Ocupado)]
    [InlineData(1, 40, Disponibilidad.Ocupado)]      // justo en la capacidad todavía NO es sobrecarga
    [InlineData(1, 40.5, Disponibilidad.Sobrecargado)]
    public void El_semaforo_compara_horas_pendientes_contra_la_capacidad(
        int abiertos, double horasPendientes, Disponibilidad esperada) =>
        Assert.Equal(esperada, CapacityStats.Clasificar(false, abiertos, horasPendientes, 40));

    [Fact]
    public void Sin_requerimientos_abiertos_esta_libre_aunque_arrastre_horas()
    {
        // Sin nada abierto no hay horas pendientes que valgan: lo que pesa es el trabajo vivo.
        Assert.Equal(Disponibilidad.Libre, CapacityStats.Clasificar(false, 0, 999, 40));
    }

    // ── Vacaciones dentro de una ventana ─────────────────────────────────────

    [Fact]
    public void Los_dias_de_vacacion_se_cuentan_inclusive_y_solo_dentro_del_rango()
    {
        var hoy = new DateTime(2026, 8, 10);
        var fin = hoy.AddDays(29);   // ventana de 30 días CON hoy dentro

        // Vacación entera dentro: del 12 al 14 son tres días, no dos.
        Assert.Equal(3, CapacityStats.DiasVacacionEnRango(
            new DateTime(2026, 8, 12), new DateTime(2026, 8, 14), hoy, fin));

        // Vacación que empieza antes de la ventana: solo cuenta lo que cae dentro.
        Assert.Equal(2, CapacityStats.DiasVacacionEnRango(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 11), hoy, fin));

        // Vacación que termina después: se recorta por el final.
        Assert.Equal(1, CapacityStats.DiasVacacionEnRango(
            new DateTime(2026, 9, 8), new DateTime(2026, 9, 30), hoy, fin));
    }

    [Fact]
    public void Una_vacacion_fuera_de_la_ventana_no_cuenta_ningun_dia()
    {
        var hoy = new DateTime(2026, 8, 10);
        var fin = hoy.AddDays(29);

        Assert.Equal(0, CapacityStats.DiasVacacionEnRango(
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 15), hoy, fin));
        Assert.Equal(0, CapacityStats.DiasVacacionEnRango(
            new DateTime(2026, 12, 1), new DateTime(2026, 12, 15), hoy, fin));
    }

    [Fact]
    public void El_primero_y_el_ultimo_dia_de_la_vacacion_cuentan_como_vacacion()
    {
        var inicio = new DateTime(2026, 8, 10);
        var fin = new DateTime(2026, 8, 14);

        Assert.True(CapacityStats.EnVacacion(inicio, fin, inicio));
        Assert.True(CapacityStats.EnVacacion(inicio, fin, fin));
        Assert.False(CapacityStats.EnVacacion(inicio, fin, fin.AddDays(1)));
    }
}
