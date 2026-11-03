using System.Diagnostics.CodeAnalysis;
using Radial.Core.Execution;
using Radial.Core.Model;

namespace Radial.Windows.Actions;

/// <summary>
/// Lanza una aplicación (<see cref="LaunchApplicationAction"/>). El destino, los argumentos y el directorio de trabajo pasan tal
/// cual al lanzador, separados. Aquí NO se comprueba que el archivo exista: un nombre sin ruta (<c>notepad.exe</c>) lo resuelve el
/// sistema, y si no lo encuentra el lanzador lo informa (<see cref="ActionErrorCodes.TargetNotFound"/>).
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
        request = new ProcessLaunchRequest(action.Path, action.Arguments, action.WorkingDirectory);
        failure = null;
        return true;
    }
}
