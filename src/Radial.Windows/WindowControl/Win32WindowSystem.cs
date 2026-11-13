using System.Runtime.InteropServices;
using Radial.Core.Model;
using Radial.Windows.Native;

namespace Radial.Windows.WindowControl;

/// <summary>
/// <see cref="IWindowSystem"/> real sobre user32. Las operaciones se piden con <c>WM_SYSCOMMAND</c> (<c>SC_MINIMIZE</c>,
/// <c>SC_MAXIMIZE</c>, <c>SC_RESTORE</c>, <c>SC_CLOSE</c>) por <c>PostMessage</c>: lo mismo que hacen los botones de la barra de título
/// (la ventana decide, y una que tenga desactivado el cierre lo ignora) y sin esperar a que responda. <c>SC_CLOSE</c> es un cierre
/// normal: la aplicación puede preguntar si guardar. Si la ventana es de un proceso con más privilegios, Windows (UIPI) rechaza
/// el mensaje con «acceso denegado» y se informa.
/// </summary>
public sealed class Win32WindowSystem : IWindowSystem
{
    // Clases del escritorio y de las barras de tareas: no son ventanas «de una aplicación».
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
    };

    public IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public WindowSnapshot Inspect(IntPtr window)
    {
        if (window == IntPtr.Zero || !WindowNativeMethods.IsWindow(window))
        {
            return new WindowSnapshot(false, false, false, false, false, false, WindowShowState.Normal);
        }

        _ = NativeMethods.GetWindowThreadProcessId(window, out var processId);
        var own = processId == (uint)Environment.ProcessId;
        var shell = ShellClasses.Contains(ClassNameOf(window));
        var style = WindowNativeMethods.GetWindowLongPtr(window, WindowNativeMethods.GWL_STYLE).ToInt64();
        var state = WindowNativeMethods.IsIconic(window)
            ? WindowShowState.Minimized
            : WindowNativeMethods.IsZoomed(window) ? WindowShowState.Maximized : WindowShowState.Normal;

        return new WindowSnapshot(
            Exists: true,
            IsOwnWindow: own,
            IsShell: shell,
            IsVisible: WindowNativeMethods.IsWindowVisible(window),
            CanMinimize: (style & WindowNativeMethods.WS_MINIMIZEBOX) != 0,
            CanMaximize: (style & WindowNativeMethods.WS_MAXIMIZEBOX) != 0,
            State: state);
    }

    public WindowCommandResult Execute(IntPtr window, WindowOperation operation)
    {
        var command = operation switch
        {
            WindowOperation.Minimize => WindowNativeMethods.SC_MINIMIZE,
            WindowOperation.Maximize => WindowNativeMethods.SC_MAXIMIZE,
            WindowOperation.Restore => WindowNativeMethods.SC_RESTORE,
            WindowOperation.Close => WindowNativeMethods.SC_CLOSE,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        if (WindowNativeMethods.PostMessage(window, WindowNativeMethods.WM_SYSCOMMAND, (UIntPtr)(uint)command, IntPtr.Zero))
        {
            return new WindowCommandResult(true);
        }

        var error = Marshal.GetLastWin32Error();
        return new WindowCommandResult(false, error);
    }

    private static string ClassNameOf(IntPtr window)
    {
        var buffer = new char[256];
        var length = WindowNativeMethods.GetClassName(window, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }
}
