using System.Diagnostics.CodeAnalysis;
using Radial.Core.Execution;
using Radial.Core.Model;

namespace Radial.Windows.Actions;

/// <summary>
/// Lanza una aplicación (<see cref="LaunchApplicationAction"/>). El destino, los argumentos y el directorio de trabajo pasan tal
/// cual al lanzador, separados. Aquí NO se comprueba que el archivo exista: un nombre sin ruta (<c>notepad.exe</c>) lo resuelve el
/// sistema, y si no lo encuentra el lanzador lo informa (<see cref="ActionErrorCodes.TargetNotFound"/>). Sí se aplica la política
/// mínima de <see cref="TargetPolicy"/>: el destino es una ruta absoluta o un nombre de programa (nunca una ruta relativa, de
/// dispositivo ni un protocolo como <c>ms-settings:</c>) y el directorio de trabajo, si se indica, es absoluto; si no,
/// <see cref="ActionErrorCodes.TargetNotAllowed"/> y no se lanza nada.
/// </summary>
public sealed class LaunchApplicationActionExecutor : ProcessActionExecutor<LaunchApplicationAction>
{
    public LaunchApplicationActionExecutor(IProcessLauncher launcher)
        : base(launcher)
    {
    }

    protected override string Subject => "la aplicación";

    protected override bool TryPrepare(
        LaunchApplicationAction action,
        [NotNullWhen(true)] out ProcessLaunchRequest? request,
        [NotNullWhen(false)] out ActionExecutionResult? failure)
    {
        if (!TargetPolicy.IsProgramTarget(action.Path) || (action.WorkingDirectory is not null && !TargetPolicy.IsAbsoluteFilePath(action.WorkingDirectory)))
        {
            request = null;
            failure = ActionExecutionResult.Failure(
                ActionErrorCodes.TargetNotAllowed,
                "El destino de la aplicación no está permitido: debe ser una ruta absoluta o un nombre de programa, y el directorio de trabajo, absoluto.");
            return false;
        }

        request = new ProcessLaunchRequest(action.Path, action.Arguments, action.WorkingDirectory);
        failure = null;
        return true;
    }
}
