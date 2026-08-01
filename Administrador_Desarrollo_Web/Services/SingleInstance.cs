using System.Runtime.InteropServices;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>
/// Una sola aplicación por sesión de Windows.
///
/// No es manía de orden: dos instancias comparten el mismo <c>AppDbContext</c> singleton por proceso
/// pero NO entre procesos, así que cada una vería datos distintos; ambas pondrían su ícono en la
/// bandeja y avisarían dos veces del mismo SLA; y dos cronómetros de trabajo abiertos sobre el mismo
/// desarrollador acabarían contando el tiempo dos veces.
///
/// El alcance es la SESIÓN de Windows (prefijo <c>Local\</c>), no la máquina: en un servidor con
/// varias sesiones, cada usuario tiene su propia configuración en <c>%AppData%</c> y sus secretos
/// cifrados con su DPAPI, así que cada quien merece su instancia.
///
/// Volver a abrir el ejecutable no abre nada: le pide a la que ya está corriendo que se muestre.
/// Eso es lo que evita el peor resultado posible — que la ventana esté escondida en la bandeja,
/// alguien haga doble clic al ejecutable y parezca que la aplicación no arranca.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>Base de los nombres de los objetos del sistema. Estable entre versiones a propósito:
    /// dos versiones distintas del mismo programa siguen siendo el mismo programa.</summary>
    public const string NombreApp = "AdministradorDesarrolloWeb";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _llamada;
    private RegisteredWaitHandle? _registro;
    private bool _dispuesto;

    /// <summary>true si esta es la instancia que manda; false si ya había otra corriendo.</summary>
    public bool EsPrimera { get; }

    /// <summary>
    /// Otra instancia intentó abrirse y pide que esta se muestre. Se dispara EN UN HILO DE FONDO:
    /// quien lo escuche tiene que saltar al hilo de la interfaz antes de tocar una ventana.
    ///
    /// Varias llamadas muy seguidas pueden fundirse en un solo aviso: la señal es de auto-reset y no
    /// lleva contador. Es lo que se quiere — tres dobles clics al ejecutable deben traer la ventana
    /// al frente, no hacerlo tres veces.
    /// </summary>
    public event Action? OtraInstanciaLlamo;

    private SingleInstance(Mutex mutex, EventWaitHandle llamada, bool esPrimera)
    {
        _mutex = mutex;
        _llamada = llamada;
        EsPrimera = esPrimera;
    }

    public static SingleInstance Adquirir(string nombre = NombreApp)
    {
        // Se decide por createdNew, no por WaitOne: un mutex es REENTRANTE para el hilo que ya lo
        // posee, así que WaitOne(0) contestaría «soy la primera» las dos veces dentro de un mismo
        // proceso. createdNew responde lo único que importa aquí: si alguien más ya lo había creado.
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{nombre}.Instancia", out var creado);
        var llamada = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{nombre}.Mostrar", out _);

        var instancia = new SingleInstance(mutex, llamada, creado);
        if (creado) instancia.Escuchar();
        return instancia;
    }

    /// <summary>
    /// Le pide a la instancia viva que se ponga al frente. Solo hace algo en la segunda: llamarse a
    /// sí misma no tendría sentido.
    /// </summary>
    public void PedirQueSeMuestre()
    {
        if (EsPrimera || _dispuesto) return;

        // Windows no deja que un proceso ponga al frente la ventana de otro salvo que quien tiene
        // el foco le ceda el permiso. Esta instancia acaba de ser lanzada por una persona, así que
        // es quien puede cederlo; sin esto, la ventana de la otra solo parpadearía en la barra de
        // tareas en vez de aparecer.
        try { AllowSetForegroundWindow(ASFW_ANY); } catch { /* la ventana igual se muestra */ }
        try { _llamada.Set(); } catch { /* la otra se está cerrando: nada que despertar */ }
    }

    private void Escuchar() =>
        _registro = ThreadPool.RegisterWaitForSingleObject(
            _llamada,
            (_, _) => { try { OtraInstanciaLlamo?.Invoke(); } catch { /* nunca tumbar la app por esto */ } },
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);

    public void Dispose()
    {
        if (_dispuesto) return;
        _dispuesto = true;

        _registro?.Unregister(null);
        _registro = null;

        // Si esta era la primera, soltar el mutex deja el camino libre a la siguiente ejecución.
        // Si el proceso muere de golpe, Windows lo suelta solo al cerrar el último handle: un
        // cuelgue no puede dejar a nadie sin poder volver a abrir la aplicación.
        try { if (EsPrimera) _mutex.ReleaseMutex(); } catch { /* no la poseía este hilo */ }
        _mutex.Dispose();
        _llamada.Dispose();
    }

    private const int ASFW_ANY = -1;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
