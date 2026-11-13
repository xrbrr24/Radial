using Radial.Core.Execution;
using Radial.Windows.Actions;
using Radial.Windows.Audio;
using Radial.Windows.Shortcuts;
using Radial.Windows.TextInput;
using Radial.Windows.WindowControl;

namespace Radial.App;

/// <summary>
/// Raíz de composición de los ejecutores: qué ejecutor atiende cada tipo de acción. Añadir un tipo de acción es añadir su ejecutor
/// a esta lista; las acciones de un mismo tipo (todos los atajos) comparten el suyo.
/// </summary>
internal static class ActionExecutors
{
    public static IActionExecutor Create()
    {
        // Un único lanzador para las cuatro acciones de apertura: Process.Start con UseShellExecute, nunca cmd ni PowerShell.
        var launcher = new ShellProcessLauncher();
        return new CompositeActionExecutor(
        [
            new LaunchApplicationActionExecutor(launcher),
            new OpenFileActionExecutor(launcher),
            new OpenFolderActionExecutor(launcher),
            new OpenUrlActionExecutor(launcher),
            ShortcutActionExecutor.CreateDefault(),
            TypeTextActionExecutor.CreateDefault(),
            VolumeActionExecutor.CreateDefault(),
            WindowControlActionExecutor.CreateDefault(),
        ]);
    }
}
