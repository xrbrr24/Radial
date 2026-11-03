using System.Diagnostics.CodeAnalysis;
using Radial.Core.Execution;
using Radial.Core.Model;

namespace Radial.Windows.Actions;

/// <summary>
/// Esqueleto de los ejecutores que abren un destino con el sistema (aplicación, archivo, carpeta, URL). Aplica siempre, y en este
/// orden, el contrato de <see cref="IActionExecutor"/>:
/// 1. Otro tipo de acción: <see cref="ActionErrorCodes.UnsupportedAction"/>.
/// 2. Token ya cancelado: <see cref="ActionExecutionResult.Cancelled"/>, sin tocar nada.
/// 3. <see cref="RadialAction.GetProblems"/> no vacío: <see cref="ActionErrorCodes.InvalidAction"/>, sin pedir ningún lanzamiento
///    (la validación es la de Core; aquí no se repite).
/// 4. Las comprobaciones propias de cada tipo (<see cref="TryPrepare"/>): existencia en disco, esquema de la URL...
/// 5. Se pide el lanzamiento al <see cref="IProcessLauncher"/> y se traduce su resultado.
/// Un fallo previsible se devuelve, no se lanza. Nunca se da por bueno un lanzamiento antes de que el lanzador lo confirme, y no
/// se informa de una cancelación si el lanzamiento ya se inició. Los mensajes no incluyen rutas ni direcciones.
/// </summary>
public abstract class ProcessActionExecutor<TAction> : IActionExecutor
    where TAction : RadialAction
{
    private readonly IProcessLauncher _launcher;

    protected ProcessActionExecutor(IProcessLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        _launcher = launcher;
    }

    /// <summary>Qué se abre, con su artículo, para los mensajes: «la aplicación», «el archivo»...</summary>
    protected abstract string Subject { get; }

    /// <summary>
    /// Comprobaciones propias del tipo y construcción de la solicitud, con una acción que ya superó <c>GetProblems()</c>.
    /// Si falla, <paramref name="failure"/> es el resultado a devolver y no se pide ningún lanzamiento.
    /// </summary>
    protected abstract bool TryPrepare(
        TAction action,
        [NotNullWhen(true)] out ProcessLaunchRequest? request,
        [NotNullWhen(false)] out ActionExecutionResult? failure);

    public bool CanExecute(RadialAction action) => action is TAction;

    public async Task<ActionExecutionResult> ExecuteAsync(RadialAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (action is not TAction typed)
        {
            return ActionExecutionResult.Failure(ActionErrorCodes.UnsupportedAction, $"Este ejecutor no admite acciones de tipo {action.GetType().Name}.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ActionExecutionResult.Cancelled;
        }

        var problems = action.GetProblems().ToList();
        if (problems.Count > 0)
        {
            return ActionExecutionResult.Failure(ActionErrorCodes.InvalidAction, "La acción no es válida: " + string.Join(" ", problems));
        }

        if (!TryPrepare(typed, out var request, out var failure))
        {
            return failure;
        }

        // Las comprobaciones anteriores pueden haber tocado el disco: última oportunidad de cancelar sin haber iniciado nada.
        if (cancellationToken.IsCancellationRequested)
        {
            return ActionExecutionResult.Cancelled;
        }

        ProcessLaunchResult result;
        try
        {
            result = await _launcher.LaunchAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // El lanzador solo lanza esto si se canceló ANTES de iniciar el lanzamiento.
            return ActionExecutionResult.Cancelled;
        }

        // Si el lanzamiento se inició, su resultado manda aunque el token se haya cancelado entretanto.
        return result.Outcome switch
        {
            ProcessLaunchOutcome.Started => ActionExecutionResult.Success,
            ProcessLaunchOutcome.TargetNotFound => ActionExecutionResult.Failure(ActionErrorCodes.TargetNotFound, $"No se encontró {Subject}."),
            _ => ActionExecutionResult.Failure(ActionErrorCodes.LaunchFailed, LaunchFailedMessage(result.NativeErrorCode)),
        };
    }

    private string LaunchFailedMessage(int nativeErrorCode) =>
        nativeErrorCode == 0
            ? $"Windows no pudo abrir {Subject}."
            : $"Windows no pudo abrir {Subject} (error {nativeErrorCode}).";
}
