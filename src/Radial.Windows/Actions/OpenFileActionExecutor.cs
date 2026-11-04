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
        var verdict = TargetPolicy.Classify(action.Path);
        if (verdict == PathVerdict.Forbidden)
        {
            request = null;
            failure = ActionExecutionResult.Failure(ActionErrorCodes.TargetNotAllowed, "La ruta del archivo no está permitida (ruta de dispositivo o flujo de datos alternativo).");
            return false;
        }

        // Una ruta relativa dependería del directorio de trabajo del proceso: no se resuelve (documento técnico), se trata como inexistente.
        if (verdict == PathVerdict.Relative || !File.Exists(action.Path))
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
