using Radial.Core.Model;

namespace Radial.Core.Execution;

/// <summary>
/// Reúne varios <see cref="IActionExecutor"/> detrás de uno solo: cada acción la ejecuta el primero que dice poder hacerlo
/// (<see cref="IActionExecutor.CanExecute"/>). Es el «registro» de la sección 8 del documento técnico: añadir un tipo de acción
/// es añadir un ejecutor a la lista de quien compone la aplicación, sin tocar el modelo, el motor ni quien ejecuta. Distintas
/// acciones pueden compartir ejecutor (todos los atajos pasan por el mismo). No añade comportamiento propio.
/// </summary>
public sealed class CompositeActionExecutor : IActionExecutor
{
    private readonly IActionExecutor[] _executors;

    public CompositeActionExecutor(IEnumerable<IActionExecutor> executors)
    {
        ArgumentNullException.ThrowIfNull(executors);
        _executors = executors.ToArray();
        if (_executors.Any(e => e is null))
        {
            throw new ArgumentException("La lista de ejecutores contiene un valor nulo.", nameof(executors));
        }
    }

    public bool CanExecute(RadialAction action) => action is not null && _executors.Any(e => e.CanExecute(action));

    public Task<ActionExecutionResult> ExecuteAsync(RadialAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        foreach (var executor in _executors)
        {
            if (executor.CanExecute(action))
            {
                return executor.ExecuteAsync(action, cancellationToken);
            }
        }

        return Task.FromResult(ActionExecutionResult.Failure(
            ActionErrorCodes.UnsupportedAction,
            $"No hay ningún ejecutor para acciones de tipo {action.GetType().Name}."));
    }
}
