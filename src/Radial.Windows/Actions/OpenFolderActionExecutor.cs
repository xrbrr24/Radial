using System.Diagnostics.CodeAnalysis;
using Radial.Core.Execution;
using Radial.Core.Model;

namespace Radial.Windows.Actions;

/// <summary>
/// Abre una carpeta (<see cref="OpenFolderAction"/>) con el explorador del sistema. Antes de pedir el lanzamiento comprueba que
/// la ruta es una carpeta existente: un archivo no se trata como carpeta.
/// </summary>
public sealed class OpenFolderActionExecutor : ProcessActionExecutor<OpenFolderAction>
{
    public OpenFolderActionExecutor(IProcessLauncher launcher)
        : base(launcher)
    {
    }

    protected override string Subject => "la carpeta";

    protected override bool TryPrepare(
        OpenFolderAction action,
        [NotNullWhen(true)] out ProcessLaunchRequest? request,
        [NotNullWhen(false)] out ActionExecutionResult? failure)
    {
        if (!Directory.Exists(action.Path))
        {
            request = null;
            failure = ActionExecutionResult.Failure(ActionErrorCodes.TargetNotFound, "La carpeta no existe o la ruta no es una carpeta.");
            return false;
        }

        request = new ProcessLaunchRequest(action.Path);
        failure = null;
        return true;
    }
}
