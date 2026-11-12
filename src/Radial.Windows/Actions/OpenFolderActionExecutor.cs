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
        var verdict = ActionTargets.Classify(action.Path);
        if (verdict == PathVerdict.Forbidden)
        {
            request = null;
            failure = ActionExecutionResult.Failure(ActionErrorCodes.TargetNotAllowed, "La ruta de la carpeta no está permitida (ruta de dispositivo o flujo de datos alternativo).");
            return false;
        }

        // Una ruta relativa («.», «..») dependería del directorio de trabajo del proceso: no se resuelve, se trata como inexistente.
        if (verdict == PathVerdict.Relative || !Directory.Exists(action.Path))
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
