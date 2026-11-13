using Radial.Core.Model;

namespace Radial.Windows.WindowControl;

public enum WindowShowState
{
    Normal,
    Minimized,
    Maximized,
}

/// <summary>
/// Lo que el ejecutor necesita saber de una ventana para decidir si puede actuar sobre ella. <see cref="IsOwnWindow"/> es verdadero
/// para cualquier ventana del proceso de Radial (centro de control, overlay, diálogos); <see cref="IsShell"/>, para el escritorio y la
/// barra de tareas. <see cref="Exists"/> falso significa que el identificador ya no es una ventana.
/// </summary>
public sealed record WindowSnapshot(
    bool Exists,
    bool IsOwnWindow,
    bool IsShell,
    bool IsVisible,
    bool CanMinimize,
    bool CanMaximize,
    WindowShowState State);

/// <param name="Success">Windows aceptó la orden (se encoló en la ventana). No dice que la aplicación la haya atendido ya.</param>
/// <param name="Win32Error">Código de error de Windows si falló (5 = acceso denegado: ventana de un proceso con más privilegios).</param>
public sealed record WindowCommandResult(bool Success, int Win32Error = 0);

/// <summary>
/// Lo único que el ejecutor de ventanas necesita del sistema; las pruebas lo sustituyen por un doble y no tocan ninguna ventana real.
/// Ningún método espera a que la ventana responda: una ventana colgada no puede bloquear a Radial.
/// </summary>
public interface IWindowSystem
{
    /// <summary>La ventana con el foco, o <see cref="IntPtr.Zero"/> si no hay ninguna.</summary>
    IntPtr GetForegroundWindow();

    WindowSnapshot Inspect(IntPtr window);

    /// <summary>Encola la operación en la ventana y vuelve. No lanza por fallos previsibles del sistema.</summary>
    WindowCommandResult Execute(IntPtr window, WindowOperation operation);
}
