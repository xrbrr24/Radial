using System.Diagnostics.CodeAnalysis;
using Radial.Core.Execution;
using Radial.Core.Model;

namespace Radial.Windows.Actions;

/// <summary>
/// Abre un archivo (<see cref="OpenFileAction"/>) con la aplicación que Windows tenga asociada. Antes de pedir el lanzamiento
/// comprueba que existe y que es un archivo (una carpeta no lo es). No interpreta la extensión ni ejecuta nada por su cuenta.
/// </summary>
public sealed class OpenFileActionExecutor : ProcessActionExecutor<OpenFileAction>
{
    public OpenFileActionExecutor(IProcessLauncher launcher)
        : base(launcher)
    {
    }

    protected override string Subject => "el archivo";

    protected override bool TryPrepare(
        OpenFileAction action,
        [NotNullWhen(true)] out ProcessLaunchRequest? request,
        [NotNullWhen(false)] out ActionExecutionResult? failure)
    {
        if (!File.Exists(action.Path))
        {
            request = null;
            failure = ActionExecutionResult.Failure(ActionErrorCodes.TargetNotFound, "El archivo no existe.");
            return false;
        }

        request = new ProcessLaunchRequest(action.Path);
        failure = null;
        return true;
    }
}
