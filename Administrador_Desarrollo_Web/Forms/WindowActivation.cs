using System.Runtime.InteropServices;

namespace Administrador_Desarrollo_Web.Forms;

/// <summary>
/// Traer al frente la ventana que manda ahora mismo. Lo usan la bandeja del sistema (doble clic o
/// «Abrir») y la guarda de instancia única, que es la que hace que volver a abrir el ejecutable
/// muestre la aplicación que ya estaba corriendo en vez de no hacer nada.
/// </summary>
public static class WindowActivation
{
    /// <summary>
    /// La ventana al mando y la trae al frente.
    ///
    /// «Al mando» es la última ventana VISIBLE: al iniciar sesión es la principal (la de inicio de
    /// sesión se queda escondida detrás, viva), y al cerrar sesión vuelve a serlo la nueva de inicio
    /// de sesión, no la principal de la sesión que se acaba de abandonar. Si no hay ninguna visible,
    /// la aplicación está escondida en la bandeja: se saca la última, que es la principal.
    /// </summary>
    public static void TraerAlFrente()
    {
        var abiertas = Application.OpenForms.Cast<Form>().Where(f => !f.IsDisposed).ToList();
        if (abiertas.Count == 0) return;

        Traer(abiertas.LastOrDefault(f => f.Visible) ?? abiertas[^1]);
    }

    /// <summary>Saca una ventana de la bandeja o de estar minimizada y le da el foco.</summary>
    public static void Traer(Form form)
    {
        if (form.IsDisposed) return;

        if (!form.Visible) form.Show();

        // SW_RESTORE la devuelve a como estaba: si era maximizada, sigue maximizada. Poner
        // WindowState = Normal no es restaurar sino encoger, y le cambiaría el tamaño a quien la
        // tenía a pantalla completa.
        if (form.WindowState == FormWindowState.Minimized)
        {
            try { ShowWindow(form.Handle, SW_RESTORE); }
            catch { form.WindowState = FormWindowState.Normal; }
        }

        form.Activate();
        form.BringToFront();
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
